namespace SnackCounter.Core
{
    // Return value for actions that can be refused (a normal "no" is not an exception)
    public class RefillResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }
        public Refill? Refill { get; private set; }

        private RefillResult(bool success, string message, Refill? refill)
        {
            Success = success;
            Message = message;
            Refill = refill;
        }

        public static RefillResult Ok(Refill refill, string message)
        {
            return new RefillResult(true, message, refill);
        }

        public static RefillResult Fail(string message)
        {
            return new RefillResult(false, message, null);
        }
    }
}