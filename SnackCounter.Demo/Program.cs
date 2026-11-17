using SnackCounter.Core;
using SnackCounter.Core.Fakes;

namespace SnackCounter.Demo
{
    public class Program
    {
        private static FakeChat _chat = new FakeChat();

        public static void Main()
        {
            FakeClock clock = new FakeClock(new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc));
            FakeHrGateway hr = new FakeHrGateway();
            InMemoryRefillRepository refills = new InMemoryRefillRepository();
            InMemoryEmployeeDirectory people = new InMemoryEmployeeDirectory();
            SnackSettings settings = new SnackSettings();

            people.Add(new Employee("o1", "Anna", Office.Oslo, Currency.NOK));
            people.Add(new Employee("o2", "Bjorn", Office.Oslo, Currency.NOK));
            people.Add(new Employee("o3", "Cecilie", Office.Oslo, Currency.NOK));
            people.Add(new Employee("b1", "Divya", Office.Bengaluru, Currency.INR));

            BudgetService budget = new BudgetService(refills, settings);
            RefillService service = new RefillService(refills, people, _chat, hr, clock, budget, new VolunteerSelector(), settings);
            ReportService report = new ReportService(refills, budget, settings);

            Step("1. Someone reports the Oslo box is empty (twice!)");
            Refill refill = service.ReportEmpty(Office.Oslo).Refill!;
            service.ReportEmpty(Office.Oslo);
            Console.WriteLine("   state=" + refill.State + ", asked=" + refill.NomineeId + ", total refills=" + refills.All().Count);
            ShowChat();

            Step("2. The person we asked stays silent for 2 hours -> we ask someone else");
            clock.Advance(TimeSpan.FromHours(2));
            service.Tick();
            Console.WriteLine("   state=" + refill.State + ", asked=" + refill.NomineeId);
            ShowChat();

            Step("3. Cecilie takes it; Anna tries a moment later and is told it's taken");
            Console.WriteLine("   Cecilie: " + service.Accept(refill.Id, "o3").Message);
            Console.WriteLine("   Anna:    " + service.Accept(refill.Id, "o1").Message);
            ShowChat();

            Step("4. Cecilie buys snacks for 800 NOK and uploads the receipt");
            RefillResult submit = service.SubmitReceipt(refill.Id, "o3", new Money(800, Currency.NOK), "Chips, fruit, nuts", new byte[] { 1 });
            Console.WriteLine("   " + submit.Message);
            ShowChat();

            Step("5. HR approves 3 days later");
            clock.Advance(TimeSpan.FromDays(3));
            hr.Approve(refill.ClaimId!);
            service.Tick();
            Console.WriteLine("   state=" + refill.State);
            ShowChat();

            Step("6. A Bengaluru refill too, then the monthly report");
            Refill blr = service.ReportEmpty(Office.Bengaluru).Refill!;
            service.Accept(blr.Id, "b1");
            service.SubmitReceipt(blr.Id, "b1", new Money(2400, Currency.INR), "Biscuits, juice", new byte[] { 1 });
            Console.WriteLine(report.Monthly(2026, 11).ToText());
        }

        private static void Step(string title)
        {
            Console.WriteLine();
            Console.WriteLine("== " + title);
        }

        private static void ShowChat()
        {
            foreach (ChatMessage m in _chat.Messages)
            {
                Console.WriteLine("   [chat -> " + m.To + "] " + m.Message);
            }
            _chat.Messages.Clear();
        }
    }
}