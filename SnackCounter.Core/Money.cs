namespace SnackCounter.Core
{
    public enum Currency
    {
        NOK,
        INR
    }

    public enum Office
    {
        Oslo,
        Bengaluru
    }

    public class Money
    {
        public decimal Amount { get; private set; }
        public Currency Currency { get; private set; }

        public Money(decimal amount, Currency currency)
        {
            Amount = amount;
            Currency = currency;
        }

        public static Money Zero(Currency currency)
        {
            return new Money(0, currency);
        }

        public static Money operator +(Money a, Money b)
        {
            CheckCurrency(a, b);
            return new Money(a.Amount + b.Amount, a.Currency);
        }

        public static Money operator -(Money a, Money b)
        {
            CheckCurrency(a, b);
            return new Money(a.Amount - b.Amount, a.Currency);
        }

        public static bool operator >(Money a, Money b)
        {
            CheckCurrency(a, b);
            return a.Amount > b.Amount;
        }

        public static bool operator <(Money a, Money b)
        {
            CheckCurrency(a, b);
            return a.Amount < b.Amount;
        }

        public static bool operator >=(Money a, Money b)
        {
            CheckCurrency(a, b);
            return a.Amount >= b.Amount;
        }

        public static bool operator <=(Money a, Money b)
        {
            CheckCurrency(a, b);
            return a.Amount <= b.Amount;
        }

        private static void CheckCurrency(Money a, Money b)
        {
            if (a.Currency != b.Currency)
            {
                throw new InvalidOperationException(
                    "Cannot mix " + a.Currency + " and " + b.Currency + ".");
            }
        }

        // Two Money objects are equal when amount AND currency are the same
        public override bool Equals(object? obj)
        {
            Money? other = obj as Money;
            if (other == null)
            {
                return false;
            }
            return Amount == other.Amount && Currency == other.Currency;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Amount, Currency);
        }

        public override string ToString()
        {
            return Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " " + Currency;
        }
    }
}