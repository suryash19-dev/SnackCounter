namespace SnackCounter.Core
{
    // Decides who we ask FIRST. Fairness rule:
    //   1. fewest claims still waiting for HR money,
    //   2. then whoever refilled longest ago (never refilled = first),
    //   3. then by id, so the result is always the same.
    public class VolunteerSelector
    {
        public Employee? Pick(List<Employee> candidates, List<Refill> history)
        {
            Employee? best = null;
            int bestPending = 0;
            DateTime bestLast = DateTime.MinValue;

            foreach (Employee e in candidates)
            {
                int pending = CountPendingClaims(e.Id, history);
                DateTime last = LastRefillDate(e.Id, history);

                bool better = false;
                if (best == null)
                {
                    better = true;
                }
                else if (pending != bestPending)
                {
                    better = pending < bestPending;
                }
                else if (last != bestLast)
                {
                    better = last < bestLast;
                }
                else
                {
                    better = string.CompareOrdinal(e.Id, best.Id) < 0;
                }

                if (better)
                {
                    best = e;
                    bestPending = pending;
                    bestLast = last;
                }
            }

            return best;
        }

        private int CountPendingClaims(string employeeId, List<Refill> history)
        {
            int count = 0;
            foreach (Refill r in history)
            {
                if (r.AssigneeId == employeeId && r.State == RefillState.ClaimSubmitted)
                {
                    count++;
                }
            }
            return count;
        }

        private DateTime LastRefillDate(string employeeId, List<Refill> history)
        {
            DateTime last = DateTime.MinValue;
            foreach (Refill r in history)
            {
                if (r.AssigneeId == employeeId
                    && r.ClaimSubmittedAt != null
                    && r.ClaimSubmittedAt.Value > last)
                {
                    last = r.ClaimSubmittedAt.Value;
                }
            }
            return last;
        }
    }
}