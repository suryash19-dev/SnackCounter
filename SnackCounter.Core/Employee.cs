namespace SnackCounter.Core
{
    // A volunteer. CurrentOffice changes when they visit the other office.
    public class Employee
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public Office HomeOffice { get; private set; }
        public Office CurrentOffice { get; set; }
        public Currency PayrollCurrency { get; private set; }
        public bool Available { get; set; }   // false = holiday / not in today

        public Employee(string id, string name, Office homeOffice, Currency payrollCurrency)
        {
            Id = id;
            Name = name;
            HomeOffice = homeOffice;
            CurrentOffice = homeOffice;
            PayrollCurrency = payrollCurrency;
            Available = true;
        }
    }
}