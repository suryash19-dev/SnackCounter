using SnackCounter.Core;
using SnackCounter.Core.Fakes;

namespace SnackCounter.Tests
{
    public class TestEnv
    {
        public FakeClock Clock;
        public FakeChat Chat;
        public FakeHrGateway Hr;
        public InMemoryRefillRepository Refills;
        public InMemoryEmployeeDirectory Employees;
        public SnackSettings Settings;
        public BudgetService Budget;
        public RefillService Service;
        public ReportService Report;

        public TestEnv()
        {
            Clock = new FakeClock(new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc));
            Chat = new FakeChat();
            Hr = new FakeHrGateway();
            Refills = new InMemoryRefillRepository();
            Employees = new InMemoryEmployeeDirectory();
            Settings = new SnackSettings();

            for (int i = 1; i <= 3; i++)
            {
                Employees.Add(new Employee("o" + i, "Oslo" + i, Office.Oslo, Currency.NOK));
                Employees.Add(new Employee("b" + i, "Blr" + i, Office.Bengaluru, Currency.INR));
            }

            Budget = new BudgetService(Refills, Settings);
            Service = new RefillService(Refills, Employees, Chat, Hr, Clock, Budget, new VolunteerSelector(), Settings);
            Report = new ReportService(Refills, Budget, Settings);
        }

        public static Money Nok(decimal amount)
        {
            return new Money(amount, Currency.NOK);
        }

        public static Money Inr(decimal amount)
        {
            return new Money(amount, Currency.INR);
        }

        // Open a refill and let the given person take it
        public Refill OpenAndAccept(Office office, string employeeId)
        {
            Refill refill = Service.ReportEmpty(office).Refill!;
            RefillResult result = Service.Accept(refill.Id, employeeId);
            Assert.True(result.Success);
            return refill;
        }

        // A complete refill that ends with a claim of the given amount
        public Refill FullRefill(Office office, string employeeId, Money amount)
        {
            Refill refill = OpenAndAccept(office, employeeId);
            RefillResult result = Service.SubmitReceipt(refill.Id, employeeId, amount, "snacks", new byte[] { 1 });
            Assert.True(result.Success);
            return refill;
        }

        // Make everyone in a home office unavailable
        public void MakeEveryoneUnavailable(Office office)
        {
            foreach (Employee e in Employees.All())
            {
                if (e.HomeOffice == office)
                {
                    e.Available = false;
                }
            }
        }
    }
}