namespace SnackCounter.Core
{
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public interface IChat
    {
        void SendDirect(string employeeId, string message);
        void SendToChannel(Office office, string message);
    }

    public class ClaimRequest
    {
        public string EmployeeId { get; private set; }
        public Money Amount { get; private set; }
        public string Description { get; private set; }
        public byte[] ReceiptImage { get; private set; }

        public ClaimRequest(string employeeId, Money amount, string description, byte[] receiptImage)
        {
            EmployeeId = employeeId;
            Amount = amount;
            Description = description;
            ReceiptImage = receiptImage;
        }
    }

    public enum ClaimStatus
    {
        Pending,
        Approved,
        Rejected
    }

    public class HrUnavailableException : Exception
    {
        public HrUnavailableException(string message) : base(message)
        {
        }
    }

    public interface IHrGateway
    {
        // Creates a claim and returns the HR claim id. May throw (HR is another company's system).
        string SubmitClaim(ClaimRequest request);
        ClaimStatus GetStatus(string claimId);
    }

    public interface IRefillRepository
    {
        void Add(Refill refill);
        Refill? Find(Guid id);
        List<Refill> All();
    }

    public interface IEmployeeDirectory
    {
        List<Employee> All();
        Employee? Find(string id);
    }
}