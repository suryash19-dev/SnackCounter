using SnackCounter.Core;

namespace SnackCounter.Tests
{
    public class BudgetAndReportTests
    {
        [Fact]
        public void Money_in_different_currencies_cannot_be_mixed()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                Money sum = TestEnv.Nok(1) + TestEnv.Inr(1);
            });

            Assert.Throws<InvalidOperationException>(() =>
            {
                bool bigger = TestEnv.Nok(1) > TestEnv.Inr(1);
            });
        }

        [Fact]
        public void Budgets_are_checked_separately_in_each_offices_own_currency()
        {
            TestEnv env = new TestEnv();
            env.FullRefill(Office.Oslo, "o1", TestEnv.Nok(5000));          // Oslo: 1000 NOK left
            env.FullRefill(Office.Bengaluru, "b1", TestEnv.Inr(3000));     // Bengaluru: 12000 INR left

            Assert.Equal(TestEnv.Nok(1000), env.Budget.Remaining(Office.Oslo, 2026, 11));
            Assert.Equal(TestEnv.Inr(12000), env.Budget.Remaining(Office.Bengaluru, 2026, 11));
        }

        [Fact]
        public void A_receipt_that_would_exceed_the_monthly_budget_is_rejected_and_not_sent_to_HR()
        {
            TestEnv env = new TestEnv();
            env.FullRefill(Office.Oslo, "o1", TestEnv.Nok(5000));          // 1000 NOK left
            Refill refill = env.OpenAndAccept(Office.Oslo, "o2");

            RefillResult tooMuch = env.Service.SubmitReceipt(refill.Id, "o2", TestEnv.Nok(1001), "snacks", new byte[] { 1 });
            Assert.False(tooMuch.Success);
            Assert.Single(env.Hr.Submitted);                               // only the first claim

            RefillResult exact = env.Service.SubmitReceipt(refill.Id, "o2", TestEnv.Nok(1000), "snacks", new byte[] { 1 });
            Assert.True(exact.Success);                                    // exactly the remaining budget is fine
            Assert.Equal(TestEnv.Nok(0), env.Budget.Remaining(Office.Oslo, 2026, 11));
        }

        [Fact]
        public void A_receipt_in_the_wrong_currency_is_rejected()
        {
            TestEnv env = new TestEnv();
            Refill refill = env.OpenAndAccept(Office.Oslo, "o1");

            RefillResult result = env.Service.SubmitReceipt(refill.Id, "o1", TestEnv.Inr(500), "snacks", new byte[] { 1 });

            Assert.False(result.Success);
            Assert.Empty(env.Hr.Submitted);
        }

        [Fact]
        public void A_zero_or_negative_receipt_is_rejected()
        {
            TestEnv env = new TestEnv();
            Refill refill = env.OpenAndAccept(Office.Oslo, "o1");

            Assert.False(env.Service.SubmitReceipt(refill.Id, "o1", TestEnv.Nok(0), "x", new byte[] { 1 }).Success);
            Assert.False(env.Service.SubmitReceipt(refill.Id, "o1", TestEnv.Nok(-5), "x", new byte[] { 1 }).Success);
        }

        [Fact]
        public void When_the_budget_is_used_up_no_new_refill_is_opened_and_the_channel_is_told()
        {
            TestEnv env = new TestEnv();
            env.FullRefill(Office.Oslo, "o1", TestEnv.Nok(5800));          // 200 NOK left, below the 300 NOK minimum

            RefillResult result = env.Service.ReportEmpty(Office.Oslo);

            Assert.False(result.Success);
            Assert.Single(env.Refills.All());
            Assert.Contains(env.Chat.Messages, m => m.To == "#Oslo" && m.Message.Contains("budget"));
        }

        [Fact]
        public void The_budget_starts_fresh_each_month()
        {
            TestEnv env = new TestEnv();
            env.FullRefill(Office.Oslo, "o1", TestEnv.Nok(5800));

            env.Clock.Advance(TimeSpan.FromDays(20));                      // now December

            Assert.Equal(TestEnv.Nok(6000), env.Budget.Remaining(Office.Oslo, 2026, 12));
            Assert.True(env.Service.ReportEmpty(Office.Oslo).Success);
        }

        [Fact]
        public void Monthly_report_shows_each_currency_and_a_combined_total_in_NOK()
        {
            TestEnv env = new TestEnv();                                   // fixed rate: 8 INR = 1 NOK
            env.FullRefill(Office.Oslo, "o1", TestEnv.Nok(1000));
            env.FullRefill(Office.Bengaluru, "b1", TestEnv.Inr(4000));     // = 500 NOK

            MonthlyReport report = env.Report.Monthly(2026, 11);

            OfficeLine oslo = FindLine(report, Office.Oslo);
            OfficeLine blr = FindLine(report, Office.Bengaluru);

            Assert.Equal(TestEnv.Nok(1000), oslo.Spent);
            Assert.Equal(TestEnv.Inr(4000), blr.Spent);
            Assert.Equal(TestEnv.Nok(5000), oslo.Remaining);
            Assert.Equal(TestEnv.Inr(11000), blr.Remaining);
            Assert.Equal(TestEnv.Nok(1500), report.TotalInNok);
            Assert.Equal(1, oslo.StillWaitingForHr);
        }

        [Fact]
        public void Monthly_report_only_includes_that_month()
        {
            TestEnv env = new TestEnv();
            env.FullRefill(Office.Oslo, "o1", TestEnv.Nok(1000));

            env.Clock.Advance(TimeSpan.FromDays(20));                      // December
            env.FullRefill(Office.Oslo, "o2", TestEnv.Nok(700));

            Assert.Equal(TestEnv.Nok(1000), env.Report.Monthly(2026, 11).TotalInNok);
            Assert.Equal(TestEnv.Nok(700), env.Report.Monthly(2026, 12).TotalInNok);
        }

        // Small helper: find one office's line in the report
        private static OfficeLine FindLine(MonthlyReport report, Office office)
        {
            foreach (OfficeLine line in report.Offices)
            {
                if (line.Office == office)
                {
                    return line;
                }
            }
            throw new InvalidOperationException("No report line for " + office);
        }
    }
}