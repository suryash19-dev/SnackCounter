namespace SnackCounter.Core.Fakes
{
    public class FakeClock : IClock
    {
        public DateTime UtcNow { get; private set; }

        public FakeClock(DateTime start)
        {
            UtcNow = start;
        }

        public void Advance(TimeSpan by)
        {
            UtcNow = UtcNow + by;
        }
    }

    public class ChatMessage
    {
        public string To { get; private set; }
        public string Message { get; private set; }

        public ChatMessage(string to, string message)
        {
            To = to;
            Message = message;
        }
    }

    public class FakeChat : IChat
    {
        public List<ChatMessage> Messages { get; private set; }

        public FakeChat()
        {
            Messages = new List<ChatMessage>();
        }

        public void SendDirect(string employeeId, string message)
        {
            lock (Messages)
            {
                Messages.Add(new ChatMessage(employeeId, message));
            }
        }

        public void SendToChannel(Office office, string message)
        {
            lock (Messages)
            {
                Messages.Add(new ChatMessage("#" + office, message));
            }
        }
    }

    public class FakeHrGateway : IHrGateway
    {
        private readonly Dictionary<string, ClaimStatus> _claims = new Dictionary<string, ClaimStatus>();

        public List<ClaimRequest> Submitted { get; private set; }

        // Set to true to simulate "HR is down" for the next submit
        public bool FailNextSubmit { get; set; }

        public FakeHrGateway()
        {
            Submitted = new List<ClaimRequest>();
        }

        public string SubmitClaim(ClaimRequest request)
        {
            if (FailNextSubmit)
            {
                FailNextSubmit = false;
                throw new HrUnavailableException("HR is down");
            }

            Submitted.Add(request);
            string claimId = "CLM-" + Submitted.Count.ToString("000");
            _claims[claimId] = ClaimStatus.Pending;
            return claimId;
        }

        public ClaimStatus GetStatus(string claimId)
        {
            return _claims[claimId];
        }

        // Helpers for tests/demo: "HR staff" decide
        public void Approve(string claimId)
        {
            _claims[claimId] = ClaimStatus.Approved;
        }

        public void Reject(string claimId)
        {
            _claims[claimId] = ClaimStatus.Rejected;
        }
    }

    public class InMemoryRefillRepository : IRefillRepository
    {
        private readonly List<Refill> _items = new List<Refill>();

        public void Add(Refill refill)
        {
            _items.Add(refill);
        }

        public Refill? Find(Guid id)
        {
            foreach (Refill r in _items)
            {
                if (r.Id == id)
                {
                    return r;
                }
            }
            return null;
        }

        public List<Refill> All()
        {
            return new List<Refill>(_items);
        }
    }

    public class InMemoryEmployeeDirectory : IEmployeeDirectory
    {
        private readonly List<Employee> _items = new List<Employee>();

        public void Add(Employee employee)
        {
            _items.Add(employee);
        }

        public List<Employee> All()
        {
            return new List<Employee>(_items);
        }

        public Employee? Find(string id)
        {
            foreach (Employee e in _items)
            {
                if (e.Id == id)
                {
                    return e;
                }
            }
            return null;
        }
    }
}