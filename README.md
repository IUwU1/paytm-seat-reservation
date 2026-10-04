# Paytm Seat Reservation System

A high-concurrency, thread-safe JSON API for managing event seat reservations. Built with **.NET 8** and **PostgreSQL**, this system is designed to handle massive ticket-sale stampedes (e.g., thousands of users attempting to book the exact same seat simultaneously) without race conditions, double-bookings, or deadlocks.

## Architectural Execution & Concurrency Strategy

To satisfy the strict zero-race-condition and zero-5xx requirements, the following mechanisms were implemented:

* **Strict Concurrency Control:** Enforced via PostgreSQL transaction-level advisory locks (`pg_advisory_xact_lock`) combined with atomic `ExecuteUpdateAsync` operations. This ensures absolute data consistency when multiple threads attempt to mutate the same row.
* **Deterministic Deadlock Prevention:** Implemented a pre-transaction sorting algorithm that orders requested seats deterministically, preventing circular wait conditions (deadlocks) during multi-seat bookings.
* **Atomic Batch Rollbacks:** Multi-seat reservation requests are treated as strict atomic units. If a user requests a batch of seats and even a single seat is taken, the entire transaction is rolled back cleanly to prevent partial availability drift.
* **Network Resilience & Idempotency:** Built an idempotency guard utilizing SHA-256 request hashing. Network retries are safely cached or rejected, preventing duplicate reservations and unintentional state mutations.
* **Distributed Quota Enforcement:** Safely restricted users to a maximum of 4 seats per show, backed by distributed locks to prevent parallel-request quota bypassing.
* **Production Observability & Security:** Integrated Prometheus (`/metrics`) and Serilog. A custom Global Exception Handler sanitizes internal stack traces (preventing info-disclosure) and gracefully sheds load by returning `429 Too Many Requests` during connection pool exhaustion.

## API Endpoints

| Method | Endpoint | Description | Auth Required |
|--------|----------|-------------|---------------|
| `POST` | `/shows` | Create a new show with a defined seat map | Admin (`Bearer admin_secret`) |
| `GET`  | `/shows/{id}` | Fetch show state, availability counts, and seat map | None |
| `POST` | `/shows/{id}/reserve` | Book up to 4 seats. Requires `idempotency_key` | User (`Bearer <token>`) |
| `POST` | `/reservations/{id}/cancel` | Cancel an active reservation | User (`Bearer <token>`) |
| `GET`  | `/metrics` | Prometheus metrics scrape target | None |
| `GET`  | `/health/readiness` | Container liveness and DB connection probe | None |

*Note: All monetary values are handled as `long` integers representing paise to avoid floating-point inaccuracies.*

## Load Testing & Validation Results

The architecture was heavily validated against a custom asynchronous Python load generator (`burst.py`). The system successfully passed all high-stress concurrency scenarios:

1. **The Hot-Seat Storm (2,000 Concurrent Requests):**
   * *Scenario:* 2,000 simultaneous connections attempted to reserve seat `A1` at the exact same millisecond.
   * *Result:* The database lock queue processed the requests perfectly. Exactly 1 request won the seat (`201 Created`), 1,999 requests were cleanly declined (`409 Conflict`), and 0 server errors (`500`) occurred.
2. **Quota Bypassing Defense:**
   * *Scenario:* A single user fired 10 parallel threads attempting to reserve 10 different seats to bypass the 4-seat limit.
   * *Result:* Advisory locks successfully serialized the user's requests, granting exactly 4 seats and cleanly declining the rest.
3. **Transactional Rollback (Partial Availability):**
   * *Scenario:* A user attempted to book a batch of 4 seats, but one of the requested seats was already taken by a previous transaction.
   * *Result:* The system cleanly rejected the entire batch request (`409 Conflict`), preventing partial bookings and ensuring the remaining 3 seats stayed available for other users.
4. **Idempotency & Replay Protection:**
   * *Scenario:* 10 identical reservation payloads were fired simultaneously using the same `idempotency_key`.
   * *Result:* 1 request executed, while the remaining 9 were successfully intercepted and safely dropped by the idempotency middleware.
5. **Final State Reconciliation:**
   * *Result:* After all chaos tests, the total counts of `Available + Held + Confirmed` seats mathematically matched the total capacity of the venue. The invariant held perfectly.

---

## 🛠 For Evaluators: Running the Project

The system adheres to 12-Factor App methodology. The `Dockerfile` is completely environment-agnostic, allowing the system to run seamlessly locally or in the cloud.

### Option A: Instant Local Setup (Docker Compose)
You can spin up the entire architecture locally with a single command. The API will automatically wait for PostgreSQL to boot and will self-apply Entity Framework migrations on startup.



# Testing the Live Cloud Deployment
# The system is currently deployed on Railway.app, utilizing public TCP proxy routing to the database and strict Npgsql connection pooling limits to shed load gracefully during extreme spikes.
```bash
# Execute the concurrency test against the live cloud deployment
python Burst.py https://paytm-seat-reservation-production-84ff.up.railway.app
```
```bash
# Clone the repository
git clone https://github.com/IUwU1/paytm-seat-reservation.git
cd paytm-seat-reservation
# Build and start the API and Database containers
docker-compose up --build

# In a new terminal, run the concurrency test against the local deployment
python Burst.py http://localhost:8080
```

