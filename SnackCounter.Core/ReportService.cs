namespace SnackCounter.Core
{
    public class OfficeLine
    {
        public Office Office { get; private set; }
        public Money Budget { get; private set; }
        public Money Spent { get; private set; }
        public Money Remaining { get; private set; }
        public int Claims { get; private set; }
        public int StillWaitingForHr { get; private set; }

        public OfficeLine(Office office, Money budget, Money spent, Money remaining, int claims, int stillWaitingForHr)
        {
            Office = office;
            Budget = budget;
            Spent = spent;
            Remaining = remaining;
            Claims = claims;
            StillWaitingForHr = stillWaitingForHr;
        }
    }

    public class MonthlyReport
    {
        public int Year { get; private set; }
        public int Month { get; private set; }
        public List<OfficeLine> Offices { get; private set; }
        public Money TotalInNok { get; private set; }

        public MonthlyReport(int year, int month, List<OfficeLine> offices, Money totalInNok)
        {
            Year = year;
            Month = month;
            Offices = offices;
            TotalInNok = totalInNok;
        }

        public string ToText()
        {
            List<string> lines = new List<string>();
            lines.Add("Snack report " + Year + "-" + Month.ToString("00"));

            foreach (OfficeLine o in Offices)
            {
                lines.Add("  " + o.Office + ": spent " + o.Spent + " of " + o.Budget
                    + " (left " + o.Remaining + "), " + o.Claims + " refills, "
                    + o.StillWaitingForHr + " waiting for HR");
            }

            lines.Add("  TOTAL (both offices, in NOK at the fixed rate): " + TotalInNok);
            return string.Join(Environment.NewLine, lines);
        }
    }

    // The one monthly report management asked for
    public class ReportService
    {
        private readonly IRefillRepository _refills;
        private readonly BudgetService _budget;
        private readonly SnackSettings _settings;

        public ReportService(IRefillRepository refills, BudgetService budget, SnackSettings settings)
        {
            _refills = refills;
            _budget = budget;
            _settings = settings;
        }

        public MonthlyReport Monthly(int year, int month)
        {
            List<OfficeLine> lines = new List<OfficeLine>();
            Money totalNok = Money.Zero(Currency.NOK);

            foreach (Office office in _settings.Offices.Keys)
            {
                Money spent = _budget.Spent(office, year, month);

                int claims = 0;
                int waitingForHr = 0;

                foreach (Refill r in _refills.All())
                {
                    if (r.Office == office
                        && r.ClaimSubmittedAt != null
                        && r.ClaimSubmittedAt.Value.Year == year
                        && r.ClaimSubmittedAt.Value.Month == month)
                    {
                        if (r.State == RefillState.ClaimSubmitted)
                        {
                            claims++;
                            waitingForHr++;
                        }
                        else if (r.State == RefillState.Reimbursed)
                        {
                            claims++;
                        }
                    }
                }

                lines.Add(new OfficeLine(
                    office,
                    _budget.Budget(office),
                    spent,
                    _budget.Remaining(office, year, month),
                    claims,
                    waitingForHr));

                // Convert ONLY here, only for the combined total
                totalNok = totalNok + _settings.ToNok(spent);
            }

            return new MonthlyReport(year, month, lines, totalNok);
        }
    }
}