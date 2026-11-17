using SnackCounter.Core;

namespace SnackCounter.Tests
{
    public class RefillRuleTests
    {
        // ---------- Problem: two people buy on the same day ----------

        [Fact]
        public void Reporting_empty_twice_gives_the_same_single_open_refill()
        {
            TestEnv env = new TestEnv();

            Refill first = env.Service.ReportEmpty(Office.Oslo).Refill!;
            Refill second = env.Service.ReportEmpty(Office.Oslo).Refill!;

            Assert.Equal(first.Id, second.Id);
            Assert.Single(env.Refills.All());
        }

        [Fact]
        public void Each_office_has_its_own_open_refill()
        {
            TestEnv env = new TestEnv();

            Refill oslo = env.Service.ReportEmpty(Office.Oslo).Refill!;
            Refill blr = env.Service.ReportEmpty(Office.Bengaluru).Refill!;

            Assert.NotEqual(oslo.Id, blr.Id);
        }

        [Fact]
        public void Two_people_cannot_both_commit_even_when_they_press_at_the_same_time()
        {
            TestEnv env = new TestEnv();
            string[] volunteers = new string[] { "o1", "o2", "o3" };

            // Repeat many times so a timing bug would show up
            for (int round = 0; round < 50; round++)
            {
                Refill refill = env.Service.ReportEmpty(Office.Oslo).Refill!;

                // All three press "take" at the same moment
                List<Task<RefillResult>> tasks = new List<Task<RefillResult>>();
                foreach (string id in volunteers)
                {
                    string employeeId = id;
                    tasks.Add(Task.Run(() => env.Service.Accept(refill.Id, employeeId)));
                }
                Task.WaitAll(tasks.ToArray());

                int winners = 0;
                foreach (Task<RefillResult> t in tasks)
                {
                    if (t.Result.Success)
                    {
                        winners++;
                    }
                }

                Assert.Equal(1, winners);   // exactly one winner
                Assert.Equal(RefillState.Committed, refill.State);

                // Finish this refill so the next round can open a new one
                env.Service.SubmitReceipt(refill.Id, refill.AssigneeId!, TestEnv.Nok(1), "x", new byte[] { 1 });
            }
        }

        [Fact]
        public void A_second_receipt_for_the_same_refill_creates_no_second_HR_claim()
        {
            TestEnv env = new TestEnv();
            Refill refill = env.OpenAndAccept(Office.Oslo, "o1");

            RefillResult first = env.Service.SubmitReceipt(refill.Id, "o1", TestEnv.Nok(500), "snacks", new byte[] { 1 });
            RefillResult second = env.Service.SubmitReceipt(refill.Id, "o1", TestEnv.Nok(500), "snacks", new byte[] { 1 });

            Assert.True(first.Success);
            Assert.False(second.Success);
            Assert.Single(env.Hr.Submitted);
        }

        [Fact]
        public void Only_the_committed_volunteer_can_submit_the_receipt()
        {
            TestEnv env = new TestEnv();
            Refill refill = env.OpenAndAccept(Office.Oslo, "o1");

            RefillResult result = env.Service.SubmitReceipt(refill.Id, "o2", TestEnv.Nok(500), "snacks", new byte[] { 1 });

            Assert.False(result.Success);
            Assert.Empty(env.Hr.Submitted);
        }

        // ---------- Problem: box stays empty because everyone waits for someone else ----------

        [Fact]
        public void Opening_a_refill_asks_one_volunteer_directly()
        {
            TestEnv env = new TestEnv();

            Refill refill = env.Service.ReportEmpty(Office.Oslo).Refill!;

            Assert.Equal(RefillState.Offered, refill.State);
            Assert.Contains(env.Chat.Messages, m => m.To == refill.NomineeId);
        }

        [Fact]
        public void If_the_asked_person_does_not_answer_someone_else_is_asked_and_the_first_is_not_picked_again()
        {
            TestEnv env = new TestEnv();
            Refill refill = env.Service.ReportEmpty(Office.Oslo).Refill!;
            string? firstNominee = refill.NomineeId;

            env.Clock.Advance(env.Settings.OfferTimeout + TimeSpan.FromMinutes(1));
            env.Service.Tick();

            Assert.Equal(RefillState.Offered, refill.State);
            Assert.NotEqual(firstNominee, refill.NomineeId);
            Assert.Contains(firstNominee!, refill.Skipped);
        }

        [Fact]
        public void A_refill_nobody_completes_is_handed_to_someone_else()
        {
            TestEnv env = new TestEnv();
            Refill refill = env.OpenAndAccept(Office.Oslo, "o1");

            env.Clock.Advance(env.Settings.PurchaseTimeout + TimeSpan.FromMinutes(1));
            env.Service.Tick();

            Assert.Equal(RefillState.Offered, refill.State);
            Assert.Null(refill.AssigneeId);
            Assert.NotEqual("o1", refill.NomineeId);

            // The person who dropped out can no longer submit
            RefillResult late = env.Service.SubmitReceipt(refill.Id, "o1", TestEnv.Nok(500), "snacks", new byte[] { 1 });
            Assert.False(late.Success);
        }

        [Fact]
        public void Nothing_happens_before_the_deadline()
        {
            TestEnv env = new TestEnv();
            Refill refill = env.OpenAndAccept(Office.Oslo, "o1");

            env.Clock.Advance(env.Settings.PurchaseTimeout - TimeSpan.FromMinutes(1));
            env.Service.Tick();

            Assert.Equal(RefillState.Committed, refill.State);
            Assert.Equal("o1", refill.AssigneeId);
        }

        [Fact]
        public void Giving_up_passes_the_refill_on()
        {
            TestEnv env = new TestEnv();
            Refill refill = env.OpenAndAccept(Office.Oslo, "o1");

            RefillResult result = env.Service.GiveUp(refill.Id, "o1");

            Assert.True(result.Success);
            Assert.Equal(RefillState.Offered, refill.State);
            Assert.NotEqual("o1", refill.NomineeId);
        }

        [Fact]
        public void If_nobody_is_available_the_refill_waits_and_is_offered_when_someone_returns()
        {
            TestEnv env = new TestEnv();
            env.MakeEveryoneUnavailable(Office.Oslo);

            Refill refill = env.Service.ReportEmpty(Office.Oslo).Refill!;
            Assert.Equal(RefillState.Requested, refill.State);

            env.Employees.Find("o2")!.Available = true;
            env.Service.Tick();

            Assert.Equal(RefillState.Offered, refill.State);
            Assert.Equal("o2", refill.NomineeId);
        }

        [Fact]
        public void An_unavailable_volunteer_cannot_take_the_refill()
        {
            TestEnv env = new TestEnv();
            Refill refill = env.Service.ReportEmpty(Office.Oslo).Refill!;
            env.Employees.Find("o3")!.Available = false;

            RefillResult result = env.Service.Accept(refill.Id, "o3");

            Assert.False(result.Success);
        }

        // ---------- Problem: unfair load, slow reimbursement ----------

        [Fact]
        public void Selector_skips_unavailable_people_and_prefers_those_not_waiting_for_HR_money()
        {
            TestEnv env = new TestEnv();

            // o1 refills and is now waiting for HR
            env.FullRefill(Office.Oslo, "o1", TestEnv.Nok(500));
            env.Employees.Find("o2")!.Available = false;

            Refill next = env.Service.ReportEmpty(Office.Oslo).Refill!;

            // o2 unavailable, o1 still waiting for money -> o3
            Assert.Equal("o3", next.NomineeId);
        }

        [Fact]
        public void Selector_prefers_whoever_refilled_longest_ago()
        {
            TestEnv env = new TestEnv();

            Refill r1 = env.FullRefill(Office.Oslo, "o1", TestEnv.Nok(500));
            env.Hr.Approve(r1.ClaimId!);
            env.Service.Tick();

            env.Clock.Advance(TimeSpan.FromDays(1));

            Refill r2 = env.FullRefill(Office.Oslo, "o2", TestEnv.Nok(500));
            env.Hr.Approve(r2.ClaimId!);
            env.Service.Tick();

            Refill next = env.Service.ReportEmpty(Office.Oslo).Refill!;
            Assert.Equal("o3", next.NomineeId);   // never refilled

            env.Service.GiveUp(next.Id, "o3");
            Assert.Equal("o1", next.NomineeId);   // refilled longest ago (o2 is more recent)
        }

        // ---------- Visitors ----------

        [Fact]
        public void A_visiting_employee_can_refill_the_office_they_are_in_and_the_claim_is_in_that_offices_currency()
        {
            TestEnv env = new TestEnv();
            env.MakeEveryoneUnavailable(Office.Bengaluru);
            env.Employees.Find("o1")!.CurrentOffice = Office.Bengaluru;   // Oslo person visiting Bengaluru

            Refill refill = env.Service.ReportEmpty(Office.Bengaluru).Refill!;
            Assert.Equal("o1", refill.NomineeId);

            Assert.True(env.Service.Accept(refill.Id, "o1").Success);
            Assert.True(env.Service.SubmitReceipt(refill.Id, "o1", TestEnv.Inr(1500), "snacks", new byte[] { 1 }).Success);

            ClaimRequest claim = Assert.Single(env.Hr.Submitted);
            Assert.Equal("o1", claim.EmployeeId);                       // HR pays o1 in o1's payroll currency
            Assert.Equal(Currency.INR, claim.Amount.Currency);          // claim is in the office's currency
            Assert.Equal(TestEnv.Inr(1500), env.Budget.Spent(Office.Bengaluru, 2026, 11));   // counts for Bengaluru
            Assert.Equal(TestEnv.Nok(0), env.Budget.Spent(Office.Oslo, 2026, 11));           // not for Oslo
        }

        // ---------- HR system ----------

        [Fact]
        public void If_HR_is_down_the_volunteer_can_retry_and_nothing_is_counted()
        {
            TestEnv env = new TestEnv();
            Refill refill = env.OpenAndAccept(Office.Oslo, "o1");
            env.Hr.FailNextSubmit = true;

            RefillResult failed = env.Service.SubmitReceipt(refill.Id, "o1", TestEnv.Nok(500), "snacks", new byte[] { 1 });
            Assert.False(failed.Success);
            Assert.Equal(RefillState.Committed, refill.State);
            Assert.Equal(TestEnv.Nok(0), env.Budget.Spent(Office.Oslo, 2026, 11));

            RefillResult retry = env.Service.SubmitReceipt(refill.Id, "o1", TestEnv.Nok(500), "snacks", new byte[] { 1 });
            Assert.True(retry.Success);
            Assert.Single(env.Hr.Submitted);
        }

        [Fact]
        public void When_HR_approves_the_refill_becomes_Reimbursed_and_the_volunteer_is_told()
        {
            TestEnv env = new TestEnv();
            Refill refill = env.FullRefill(Office.Oslo, "o1", TestEnv.Nok(500));

            env.Service.Tick();
            Assert.Equal(RefillState.ClaimSubmitted, refill.State);   // still pending

            env.Hr.Approve(refill.ClaimId!);
            env.Service.Tick();

            Assert.Equal(RefillState.Reimbursed, refill.State);
            Assert.Contains(env.Chat.Messages, m => m.To == "o1" && m.Message.Contains("approved"));
        }

        [Fact]
        public void A_rejected_claim_does_not_count_in_the_budget()
        {
            TestEnv env = new TestEnv();
            Refill refill = env.FullRefill(Office.Oslo, "o1", TestEnv.Nok(500));
            Assert.Equal(TestEnv.Nok(500), env.Budget.Spent(Office.Oslo, 2026, 11));

            env.Hr.Reject(refill.ClaimId!);
            env.Service.Tick();

            Assert.Equal(RefillState.Rejected, refill.State);
            Assert.Equal(TestEnv.Nok(0), env.Budget.Spent(Office.Oslo, 2026, 11));
        }
    }
}