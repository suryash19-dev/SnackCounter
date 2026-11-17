namespace SnackCounter.Core
{
    // Budget rule: each office has a monthly budget in ITS OWN currency.
    // Spent = all claims sent to HR this month (approved or still pending).
    // Rejected claims do not count.
    public class BudgetService
    {
        private readonly IRefillRepository _refills;
        private readonly SnackSettings _settings;

        public BudgetService(IRefillRepository refills, SnackSettings settings)
        {
            _refills = refills;
            _settings = settings;
        }

        public Money Budget(Office office)
        {
            OfficeSettings s = _settings.For(office);
            return new Money(s.MonthlyBudget, s.Currency);
        }

        public Money Spent(Office office, int year, int month)
        {
            Money total = Money.Zero(_settings.For(office).Currency);

            foreach (Refill r in _refills.All())
            {
                bool sentToHr = r.State == RefillState.ClaimSubmitted
                             || r.State == RefillState.Reimbursed;

                if (r.Office == office
                    && sentToHr
                    && r.ClaimSubmittedAt != null
                    && r.ClaimSubmittedAt.Value.Year == year
                    && r.ClaimSubmittedAt.Value.Month == month
                    && r.Amount != null)
                {
                    total = total + r.Amount;
                }
            }

            return total;
        }

        public Money Remaining(Office office, int year, int month)
        {
            return Budget(office) - Spent(office, year, month);
        }

        public bool CanSpend(Office office, Money amount, int year, int month)
        {
            if (amount.Currency != _settings.For(office).Currency)
            {
                return false;
            }
            if (amount.Amount <= 0)
            {
                return false;
            }
            return amount <= Remaining(office, year, month);
        }
    }
}