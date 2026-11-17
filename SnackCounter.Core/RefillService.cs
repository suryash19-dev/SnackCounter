namespace SnackCounter.Core
{
    // The heart of the system: runs the refill lifecycle.
    //   ReportEmpty -> Accept -> SubmitReceipt -> Tick sees HR approval -> Reimbursed
    //   If someone goes quiet, Tick hands the job to someone else.
    // Every public method takes the same lock, so two people can never
    // change the same refill at the same moment.
    public class RefillService
    {
        private readonly object _lock = new object();
        private readonly IRefillRepository _refills;
        private readonly IEmployeeDirectory _employees;
        private readonly IChat _chat;
        private readonly IHrGateway _hr;
        private readonly IClock _clock;
        private readonly BudgetService _budget;
        private readonly VolunteerSelector _selector;
        private readonly SnackSettings _settings;

        public RefillService(
            IRefillRepository refills,
            IEmployeeDirectory employees,
            IChat chat,
            IHrGateway hr,
            IClock clock,
            BudgetService budget,
            VolunteerSelector selector,
            SnackSettings settings)
        {
            _refills = refills;
            _employees = employees;
            _chat = chat;
            _hr = hr;
            _clock = clock;
            _budget = budget;
            _selector = selector;
            _settings = settings;
        }

        // 1. Someone presses "box is empty"
        public RefillResult ReportEmpty(Office office)
        {
            lock (_lock)
            {
                // Rule: only ONE open refill per office (stops double buying)
                Refill? existing = FindOpenRefill(office);
                if (existing != null)
                {
                    return RefillResult.Ok(existing, "A refill is already in progress.");
                }

                // Rule: if the budget cannot cover a minimum purchase, do not start a refill
                DateTime now = _clock.UtcNow;
                OfficeSettings s = _settings.For(office);
                Money minPurchase = new Money(s.MinPurchase, s.Currency);

                if (_budget.Remaining(office, now.Year, now.Month) < minPurchase)
                {
                    _chat.SendToChannel(office, "Snack box is empty, but this month's budget is used up. Please ask the office admin.");
                    return RefillResult.Fail("Monthly budget is used up.");
                }

                Refill refill = new Refill(office, now);
                _refills.Add(refill);
                TryOffer(refill, true);
                return RefillResult.Ok(refill, "Refill started.");
            }
        }

        // 2. A volunteer says "I'll do it". First one wins.
        public RefillResult Accept(Guid refillId, string employeeId)
        {
            lock (_lock)
            {
                Refill? refill = _refills.Find(refillId);
                Employee? employee = _employees.Find(employeeId);

                if (refill == null || employee == null)
                {
                    return RefillResult.Fail("Unknown refill or employee.");
                }
                if (refill.State != RefillState.Offered)
                {
                    return RefillResult.Fail("Too late, this refill is already taken or closed.");
                }
                if (employee.CurrentOffice != refill.Office || !employee.Available)
                {
                    return RefillResult.Fail("You are not available for this office.");
                }
                if (refill.Skipped.Contains(employeeId))
                {
                    return RefillResult.Fail("You already passed on this refill.");
                }

                DateTime now = _clock.UtcNow;
                refill.Commit(employeeId, now + _settings.PurchaseTimeout);

                Money left = _budget.Remaining(refill.Office, now.Year, now.Month);
                _chat.SendDirect(employeeId,
                    "Thanks " + employee.Name + "! Buy snacks and submit the receipt within "
                    + _settings.PurchaseTimeout.TotalHours.ToString("0") + " hours. Budget left this month: " + left + ".");
                _chat.SendToChannel(refill.Office, employee.Name + " is refilling the snack box.");

                return RefillResult.Ok(refill, "You are committed.");
            }
        }

        // 2b. "I can't do it" (the person we asked, or someone who already committed)
        public RefillResult GiveUp(Guid refillId, string employeeId)
        {
            lock (_lock)
            {
                Refill? refill = _refills.Find(refillId);
                if (refill == null)
                {
                    return RefillResult.Fail("Unknown refill.");
                }

                bool askedPerson = refill.State == RefillState.Offered && refill.NomineeId == employeeId;
                bool committedPerson = refill.State == RefillState.Committed && refill.AssigneeId == employeeId;

                if (!askedPerson && !committedPerson)
                {
                    return RefillResult.Fail("Nothing to give up.");
                }

                refill.Reassign(employeeId);
                TryOffer(refill, true);
                return RefillResult.Ok(refill, "Thanks, we will ask someone else.");
            }
        }

        // 3. The volunteer bought snacks and uploads the receipt -> we create the HR claim
        public RefillResult SubmitReceipt(Guid refillId, string employeeId, Money amount, string description, byte[] receiptImage)
        {
            lock (_lock)
            {
                Refill? refill = _refills.Find(refillId);
                if (refill == null)
                {
                    return RefillResult.Fail("Unknown refill.");
                }

                // The state check is the duplicate protection: a second submit
                // finds the refill already in ClaimSubmitted.
                if (refill.State != RefillState.Committed || refill.AssigneeId != employeeId)
                {
                    return RefillResult.Fail("This refill is not assigned to you (or a receipt was already submitted).");
                }

                DateTime now = _clock.UtcNow;
                if (!_budget.CanSpend(refill.Office, amount, now.Year, now.Month))
                {
                    Money left = _budget.Remaining(refill.Office, now.Year, now.Month);
                    return RefillResult.Fail(
                        "Receipt rejected: amount must be above 0, in "
                        + _settings.For(refill.Office).Currency
                        + ", and within the remaining budget (" + left + ").");
                }

                string claimId;
                try
                {
                    claimId = _hr.SubmitClaim(new ClaimRequest(employeeId, amount, description, receiptImage));
                }
                catch (HrUnavailableException)
                {
                    // Nothing changed: still Committed, nothing counted in the budget, volunteer can retry
                    return RefillResult.Fail("HR system is unavailable right now. Please try again in a few minutes.");
                }

                refill.AttachClaim(claimId, amount, description, now);
                _chat.SendDirect(employeeId, "Claim " + claimId + " for " + amount + " sent to HR. We'll tell you when it is approved.");
                return RefillResult.Ok(refill, "Claim submitted.");
            }
        }

        // 4. Run every few minutes by a timer: handles silence, retries and HR approvals
        public void Tick()
        {
            lock (_lock)
            {
                DateTime now = _clock.UtcNow;

                foreach (Refill refill in _refills.All())
                {
                    if (refill.State == RefillState.Requested)
                    {
                        // maybe somebody became available
                        TryOffer(refill, false);
                    }
                    else if (refill.State == RefillState.Offered && now >= refill.DeadlineAt)
                    {
                        string nominee = refill.NomineeId!;
                        _chat.SendDirect(nominee, "No answer, so we asked someone else. No worries!");
                        refill.Reassign(nominee);
                        TryOffer(refill, true);
                    }
                    else if (refill.State == RefillState.Committed && now >= refill.DeadlineAt)
                    {
                        string assignee = refill.AssigneeId!;
                        _chat.SendDirect(assignee, "The deadline passed, so we gave the refill to someone else.");
                        refill.Reassign(assignee);
                        TryOffer(refill, true);
                    }
                    else if (refill.State == RefillState.ClaimSubmitted)
                    {
                        SyncClaim(refill);
                    }
                }
            }
        }

        // ---- helpers (always called inside the lock) ----

        private Refill? FindOpenRefill(Office office)
        {
            foreach (Refill r in _refills.All())
            {
                if (r.Office == office && r.IsOpen)
                {
                    return r;
                }
            }
            return null;
        }

        private void SyncClaim(Refill refill)
        {
            ClaimStatus status;
            try
            {
                status = _hr.GetStatus(refill.ClaimId!);
            }
            catch (HrUnavailableException)
            {
                return; // try again next tick
            }

            if (status == ClaimStatus.Approved)
            {
                refill.MarkReimbursed();
                _chat.SendDirect(refill.AssigneeId!, "HR approved your claim of " + refill.Amount + ". Thank you!");
            }
            else if (status == ClaimStatus.Rejected)
            {
                refill.MarkRejected();
                _chat.SendDirect(refill.AssigneeId!, "HR rejected your claim. Please contact HR.");
                _chat.SendToChannel(refill.Office, "An HR claim was rejected. Admin please follow up.");
            }
        }

        // Pick the next person to ask. If nobody is left, give everyone a second chance.
        // If still nobody, the refill stays Requested.
        private void TryOffer(Refill refill, bool announce)
        {
            Employee? nominee = PickNominee(refill);

            if (nominee == null && refill.Skipped.Count > 0)
            {
                refill.ClearSkipped();
                nominee = PickNominee(refill);
            }

            if (nominee == null)
            {
                if (announce)
                {
                    _chat.SendToChannel(refill.Office, "Snack box is empty and no volunteer is available right now. We'll keep trying.");
                }
                return;
            }

            refill.Offer(nominee.Id, _clock.UtcNow + _settings.OfferTimeout);

            _chat.SendDirect(nominee.Id,
                "Hi " + nominee.Name + ", the " + refill.Office + " snack box is empty. Can you refill it? Reply 'take' within "
                + _settings.OfferTimeout.TotalHours.ToString("0") + " hours.");

            if (announce)
            {
                _chat.SendToChannel(refill.Office,
                    "Snack box is empty! We asked " + nominee.Name + " first, but anyone available can take it.");
            }
        }

        private Employee? PickNominee(Refill refill)
        {
            List<Employee> candidates = new List<Employee>();

            foreach (Employee e in _employees.All())
            {
                if (e.CurrentOffice == refill.Office
                    && e.Available
                    && !refill.Skipped.Contains(e.Id))
                {
                    candidates.Add(e);
                }
            }

            return _selector.Pick(candidates, _refills.All());
        }
    }
}