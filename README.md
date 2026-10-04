
# Paytm Seat Reservation System

A high-concurrency, thread-safe JSON API for managing event seat reservations. Built with **.NET 8** and **PostgreSQL**, this system is designed to handle massive ticket-sale stampedes (e.g., thousands of users attempting to book the exact same seat simultaneously) without race conditions, double-bookings, or deadlocks.

## Architectural Execution & Concurrency Strategy

To satisfy the strict zero-race-condition and zero-5xx requirements, the following mechanisms were implemented:

* **Strict Concurrency Control:** Enforced via PostgreSQL transaction-level advisory locks (`pg_advisory_xact_lock`) combined with atomic `ExecuteUpdateAsync` operations. This ensures absolute data consistency when multiple threads attempt to mutate the same row.
* **Deterministic Deadlock Prevention:** Implemented a pre-transaction sorting algorithm that orders requested seats deterministically, preventing circular wait conditions (deadlocks) during multi-seat bookings.
* **Network Resilience & Idempotency:** Built an idempotency guard utilizing SHA-256 request hashing. Network retries are safely cached or rejected, preventing duplicate reservations and unintentional state mutations.
* **Distributed Quota Enforcement:** Safely restricted users to a maximum of 4 seats per show, backed by distributed locks to prevent parallel-request quota bypassing.
* **Production Observability:** Integrated Prometheus (`/metrics`) and Serilog to monitor connection pool exhaustion, request histograms, and real-time state invariant drift.

## API Endpoints

| Method | Endpoint | Description | Auth Required |
|--------|----------|-------------|---------------|
| `POST` | `/shows` | Create a new show with a defined seat map | Admin (`Bearer admin_secret`) |
| `GET`  | `/shows/{id}` | Fetch show state, availability counts, and seat map | None |
| `POST` | `/shows/{id}/reserve` | Book up to 4 seats. Requires `idempotency_key` | User (`Bearer <token>`) |
| `POST` | `/reservations/{id}/cancel` | Cancel an active reservation | User (`Bearer <token>`) |

*Note: All monetary values are handled as `long` integers representing paise to avoid floating-point inaccuracies.*

## Load Testing & Validation Results

The architecture was heavily validated against a custom asynchronous Python load generator (`burst.py`). The system successfully passed all high-stress concurrency scenarios deployed against the live cloud environment:

1. **The Hot-Seat Storm (2,000 Concurrent Requests):** 
   * *Scenario:* 2,000 simultaneous connections attempted to reserve seat `A1` at the exact same millisecond.
   * *Result:* The database lock queue processed the requests perfectly. Exactly 1 request won the seat (`201 Created`), 1,999 requests were cleanly declined (`409 Conflict`), and 0 server errors (`500`) occurred.
2. **Quota Bypassing Defense:** 
   * *Scenario:* A single user fired 10 parallel threads attempting to reserve 10 different seats to bypass the 4-seat limit.
   * *Result:* Advisory locks successfully serialized the user's requests, granting exactly 4 seats and cleanly declining the rest.
3. **Idempotency & Replay Protection:** 
   * *Scenario:* 10 identical reservation payloads were fired simultaneously using the same `idempotency_key`. 
   * *Result:* 1 request executed, while the remaining 9 were successfully intercepted and safely dropped by the idempotency middleware.
4. **Final State Reconciliation:**
   * *Result:* After all chaos tests, the total counts of `Available + Held + Confirmed` seats mathematically matched the total capacity of the venue. The invariant held perfectly.

## Cloud Infrastructure

The system is currently containerized and deployed on **Railway.app**:
* **Database Routing:** Utilizes standard public TCP proxy routing (`proxy.rlwy.net`) to bypass internal Docker DNS limitations on Alpine Linux.
* **Connection Pooling:** Npgsql is explicitly configured with a strict connection pooling limit (`Minimum Pool Size=10; Maximum Pool Size=80`) and global exception handling to gracefully queue or shed load (returning `429 Too Many Requests`) rather than crashing the database during extreme traffic spikes.

---

## 🛠 For Evaluators: Running the Project

### Testing the Live Deployment
You can run the full load-testing suite against the live Railway environment directly from your local machine.

```bash
# Clone the repository
git clone [https://github.com/your-username/paytm-reservation.git](https://github.com/your-username/paytm-reservation.git)
cd paytm-reservation

# Execute the concurrency test against the cloud deployment
./burst.sh [https://your-railway-app.up.railway.app](https://your-railway-app.up.railway.app)
