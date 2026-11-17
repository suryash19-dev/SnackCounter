namespace SnackCounter.Core
{
    public enum RefillState
    {
        Requested,       // box is empty, nobody asked yet (or nobody was available)
        Offered,         // we asked a volunteer; anyone eligible may take it, first wins
        Committed,       // one volunteer said "I'll do it" and has a deadline
        ClaimSubmitted,  // receipt sent to HR, waiting for approval
        Reimbursed,      // HR approved. Done.
        Rejected         // HR rejected the claim. Done (does not count in the budget).
    }

    // One "box is empty" -> "volunteer reimbursed" journey.
    // All state changes go through methods that check the rules.
    public class Refill
    {
        private readonly List<string> _skipped = new List<string>();

        public Guid Id { get; private set; }
        public Office Office { get; private set; }
        public DateTime OpenedAt { get; private set; }
        public RefillState State { get; private set; }

        public string? NomineeId { get; private set; }     // who we asked
        public string? AssigneeId { get; private set; }    // who committed
        public DateTime? DeadlineAt { get; private set; }  // offer expiry (Offered) or purchase deadline (Committed)

        public Money? Amount { get; private set; }
        public string? Description { get; private set; }
        public string? ClaimId { get; private set; }
        public DateTime? ClaimSubmittedAt { get; private set; }

        // People who timed out or declined for THIS refill
        public IReadOnlyCollection<string> Skipped
        {
            get { return _skipped; }
        }

        // "Open" = the box still needs filling. At most ONE open refill per office.
        public bool IsOpen
        {
            get
            {
                return State == RefillState.Requested
                    || State == RefillState.Offered
                    || State == RefillState.Committed;
            }
        }

        public Refill(Office office, DateTime openedAt)
        {
            Id = Guid.NewGuid();
            Office = office;
            OpenedAt = openedAt;
            State = RefillState.Requested;
        }

        public void Offer(string nomineeId, DateTime expiresAt)
        {
            Require(State == RefillState.Requested, "Offer");
            NomineeId = nomineeId;
            DeadlineAt = expiresAt;
            State = RefillState.Offered;
        }

        public void Commit(string employeeId, DateTime deadline)
        {
            Require(State == RefillState.Offered, "Commit");
            AssigneeId = employeeId;
            DeadlineAt = deadline;
            State = RefillState.Committed;
        }

        // Remove the person from the job (timeout or "can't do it")
        // and go back to Requested so we can pick someone else.
        public void Reassign(string personToSkip)
        {
            Require(State == RefillState.Offered || State == RefillState.Committed, "Reassign");

            if (!_skipped.Contains(personToSkip))
            {
                _skipped.Add(personToSkip);
            }

            NomineeId = null;
            AssigneeId = null;
            DeadlineAt = null;
            State = RefillState.Requested;
        }

        public void ClearSkipped()
        {
            _skipped.Clear();
        }

        public void AttachClaim(string claimId, Money amount, string description, DateTime at)
        {
            Require(State == RefillState.Committed, "AttachClaim");
            ClaimId = claimId;
            Amount = amount;
            Description = description;
            ClaimSubmittedAt = at;
            DeadlineAt = null;
            State = RefillState.ClaimSubmitted;
        }

        public void MarkReimbursed()
        {
            Require(State == RefillState.ClaimSubmitted, "MarkReimbursed");
            State = RefillState.Reimbursed;
        }

        public void MarkRejected()
        {
            Require(State == RefillState.ClaimSubmitted, "MarkRejected");
            State = RefillState.Rejected;
        }

        private void Require(bool condition, string action)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Cannot " + action + " when refill is " + State + ".");
            }
        }
    }
}