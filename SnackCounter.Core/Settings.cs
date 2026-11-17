namespace SnackCounter.Core
{
    public class OfficeSettings
    {
        public Office Office { get; private set; }
        public Currency Currency { get; private set; }
        public decimal MonthlyBudget { get; private set; }
        public decimal MinPurchase { get; private set; }

        public OfficeSettings(Office office, Currency currency, decimal monthlyBudget, decimal minPurchase)
        {
            Office = office;
            Currency = currency;
            MonthlyBudget = monthlyBudget;
            MinPurchase = minPurchase;
        }
    }

    // All the numbers we assumed, in one place
    public class SnackSettings
    {
        public Dictionary<Office, OfficeSettings> Offices { get; private set; }

        // Fixed rate used ONLY for the management report: 1 NOK = 8 INR
        public decimal InrPerNok { get; set; }

        // How long the asked person has to say "I'll do it"
        public TimeSpan OfferTimeout { get; set; }

        // How long a volunteer has to buy and submit the receipt
        public TimeSpan PurchaseTimeout { get; set; }

        public SnackSettings()
        {
            Offices = new Dictionary<Office, OfficeSettings>();
            Offices[Office.Oslo] = new OfficeSettings(Office.Oslo, Currency.NOK, 6000m, 300m);
            Offices[Office.Bengaluru] = new OfficeSettings(Office.Bengaluru, Currency.INR, 15000m, 500m);

            InrPerNok = 8m;
            OfferTimeout = TimeSpan.FromHours(2);
            PurchaseTimeout = TimeSpan.FromHours(24);
        }

        public OfficeSettings For(Office office)
        {
            return Offices[office];
        }

        public Money ToNok(Money money)
        {
            if (money.Currency == Currency.NOK)
            {
                return money;
            }
            return new Money(Math.Round(money.Amount / InrPerNok, 2), Currency.NOK);
        }
    }
}