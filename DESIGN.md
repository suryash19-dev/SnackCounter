# Snack Counter Automation: Design Document

## 1. What "automate" means here
We automate the **coordination**, not the shopping. The system notices an empty box, asks the right volunteer, makes sure only one person buys, reassigns if someone goes quiet, creates the HR claim, tracks it to "reimbursed", and enforces the budget and reporting. Humans still buy and refill. Reason: robots can't shop, but the *failures in the brief* (nobody acts, double buying, unfair load, no numbers) are all coordination problems.

## 2. From "empty" to "reimbursed"

```
Requested -> Offered -> Committed -> ClaimSubmitted -> Reimbursed
                ^           |              |
                +-----------+              +--> Rejected (HR said no)
          (timeout / "can't do it")
```
1. **Empty.** Anyone presses "box is empty" in chat. If the office already has an open refill, they get that one back (no duplicates).
2. **Budget gate.** If the month's remaining budget is below the minimum typical purchase, no refill starts and the office channel is told.
3. **Offered.** The selector picks one volunteer, who gets a chat DM with a 2-hour window. The channel is told who was asked, **but any available volunteer in that office may take it; first one wins.**
4. **Committed.** The volunteer has 24 hours to buy and submit the receipt. They are told the remaining budget.
5. **Claim submitted.** On receipt upload we check budget and currency, then create the HR claim (amount, currency, description, receipt image, employee ID).
6. **Reimbursed.** A timer (`Tick`, every few minutes) asks HR for claim status and notifies the volunteer on approval.
- **Silence.** In `Offered` or `Committed`, when the deadline passes `Tick` skips that person for this refill and offers it to the next one. "I can't" does the same, immediately.

## 3. Parts and the data each keeps
| Part | Responsibility | Data it keeps |
|---|---|---|
| `Refill` (state machine) | Guarded transitions; illegal moves throw | office, state, nominee, assignee, deadline, skipped people, amount, HR claim id, timestamps |
| `RefillService` | The lifecycle; one lock around every change | (uses the others, no data of its own) |
| `VolunteerSelector` | Who is asked first | none (reads refill history) |
| `BudgetService` | Spent / remaining per office per month | none (derived from refills, so it can't drift) |
| `ReportService` | Monthly report for management | none (derived) |
| Employee directory | Who volunteers | id, name, home office, current office, payroll currency, available |
| Ports (simulated here) | `IChat`, `IHrGateway`, `IClock`, repositories | held by the chat tool, HR, and a database in real life |

Design choice: **spend is never stored as a counter; it is always computed from the refills.** One source of truth.

## 4. How each problem is prevented
| Problem | Mechanism |
|---|---|
| Box empty for days, "someone else will do it" | A named person is asked directly with a deadline; silence moves it on automatically; "nobody available" is announced and retried every tick |
| Two people buy the same day | Max one open refill per office; accepting is atomic (single lock + state check), so exactly one winner; second receipt is refused, so one HR claim |
| A few people do most refills, wait for money | Selector asks first those with **no claims waiting on HR**, then whoever refilled **longest ago**; unavailable people and people who already timed out on this refill are skipped |
| No idea of spend or what is eaten | Monthly report: per office in its own currency, plus one combined NOK total. (Snack-level data: see 6) |

## 5. Assumptions (and why)
1. **Anyone can report "empty"**: simplest; no sensors or inventory system exist.
2. **Volunteer pool = the employee directory.** Visitors count as volunteers in the office they are *currently* in (`CurrentOffice`); someone on holiday is `Available = false`. Reason: the brief says visitors and holidays happen; a flag is the simplest model.
3. **Offer timeout 2 h, purchase deadline 24 h.** A short offer window keeps the box from staying empty; 24 h tolerates hybrid days. Both are settings.
4. **Anyone available may take an offered refill**, not only the person asked. Reason: if the asked person is away, others can still help immediately.
5. **Money is `decimal` with a currency; mixing currencies throws.** A receipt must be in the *office's* currency, so a visitor buying in Bengaluru claims in INR. Reason: HR reimburses in payroll currency by itself ("in the currency of their payroll"), so we never convert employee money.
6. **Budget is per calendar month (UTC) in the office's own currency, counted when the claim is sent to HR** (pending counts, rejected does not). Reason: waiting for HR approval (1-10 days) would otherwise let us overspend.
7. **An over-budget receipt is refused, not partially paid.** The volunteer is told the remaining budget when they commit, so it should be rare. A real system would add an admin override.
8. **Combined report uses a fixed rate (1 NOK = 8 INR), in NOK.** Reason: monthly budgets are fixed numbers; a fixed, configurable rate is simple and reproducible. Conversion happens only in the report.
9. **HR has no duplicate-protection** (not mentioned in the brief), so we protect ourselves with the state machine. If HR is unreachable, the refill stays `Committed` and the volunteer can retry.
10. **Chat tool can send DMs/channel messages and send us "take"/"empty"/receipt events**; HR has the claim create + status check described in the brief.

## 6. Trade-offs and what I deliberately did NOT build
- **In-memory storage + one lock** instead of a database. It makes the rules easy to read and test; in production the lock becomes a DB transaction/row version, behind the same interfaces.
- **No real chat/HR integration, UI, or authentication.** Company login already identifies people; the ports mark exactly where integrations plug in.
- **No receipt OCR or snack inventory.** The brief wants "which snacks get eaten", but that needs item-level data; the claim has a free-text description today. Next step would be structured items on the receipt.
- **No forecasting or auto-detecting an empty box** (e.g. a sensor).
- **No admin override** for over-budget receipts, **no cancel**, **no speed-up of HR** (it is another company's system; we only make waiting visible).
- **Simple fairness only** (HR-pending first, then least recent). No weighting by distance, calendars, or preferences.
