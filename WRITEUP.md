# Paytm Seat Reservation at Scale

## 1. The Atomic Decision & Deadlock Prevention
To guarantee exactly-once processing and prevent double-booking under extreme contention, this system rejects the naive "read-then-write" approach.
* **The Mechanism:** I utilized PostgreSQL's row-level conditional updates (`ExecuteUpdateAsync`) via Entity Framework Core. The query atomically enforces `WHERE status = 'available'` during the `UPDATE` statement. If 500 threads storm seat A12, exactly one thread successfully modifies the row, returning an update count of 1. The remaining 499 threads return an update count of 0, safely triggering a clean `409 Conflict` (seat_taken) without bubbling up database exceptions.
* **Deadlock Avoidance:** For multi-seat requests, incoming seat arrays are lexicographically sorted (e.g., `["A1", "A2"]`) prior to the transaction. This guarantees that PostgreSQL always acquires physical row locks in a deterministic order, completely eliminating relational deadlocks during simultaneous multi-seat bursts.

## 2. Idempotency & Exactly-Once Semantics
Idempotency is enforced via a unique composite key `(UserId, IdempotencyKey)` and strict request payload hashing.
* **Mechanism:** Upon a successful reservation, a SHA-256 hash of the sorted seat request and the JSON response are saved within the transaction.
* **Replays vs. Conflicts:** If a request arrives with an existing key, the system compares the payload hash. A match safely returns the cached `200 OK` JSON response. A mismatch (same key, different seats) cleanly rejects with a `409 Conflict`.
* **Concurrency Guard:** The idempotency check is placed *inside* a transaction scoped by a PostgreSQL advisory lock, forcing simultaneous identical requests to serialize and wait for the first thread to populate the cache.

## 3. Per-User Limits Under Contention
To prevent a user from bypassing the 4-seat limit by firing concurrent requests, I implemented a fast, memory-only PostgreSQL advisory lock (`pg_advisory_xact_lock`). The lock is uniquely scoped to `hashtext(show_id) + hashtext(user_id)`. This serializes limit-validation strictly per-user, ensuring one user's burst does not bottleneck unrelated buyers targeting different seats.

## 4. Holds & Expiry
Holds are modeled via an explicit cancellation endpoint (`POST /reservations/{id}/cancel`). The cancellation atomically verifies token ownership, marks the reservation as canceled, and detaches the `reservation_id` from the seats, cleanly reverting them to `available`. To prevent resurrection bugs, the SQL update explicitly scopes the reversion only to seats currently tied to that specific `reservation_id`.

## 5. Consistency vs Availability
Under a network partition or heavy contention, this system favors **Consistency (CP)** over Availability. It is better to reject a user with a `429 Too Many Requests` or `409 Conflict` than to compromise the `available + held + confirmed == total_seats` invariant or accidentally double-charge a user. All contention timeouts fail closed.

## 6. Observability
The system exposes a `/metrics` endpoint for Prometheus scraping.
* **2:00 AM Pager Alerts:** I would configure critical alerts for:
    * Any occurrence of HTTP 5xx errors (indicating unhandled infrastructure failure).
    * Saturation of the PostgreSQL connection pool (>85% active connections).
    * Invariant drift (if `paytm_seats_available` gauge + confirmed counters desync from the total seat count).

## 7. AI Disclosure
AI was utilized as an interactive pair-programmer to accelerate boilerplate generation (DTOs, EF Core entity scaffolding, Dockerfile setup) and to quickly write the asynchronous Python `burst.py` load-testing script. Core architectural decisions—specifically the use of advisory locks for per-user limits, deterministic seat sorting, and raw SQL transaction boundaries to bypass EF Core change-tracker anomalies—were actively directed and designed by me to satisfy the strict correctness constraints.

## 8. What I'd Do Next
If I were to extend this service for production, I would implement:
1. **Time-Boxed Holds via Message Broker:** Instead of relying solely on explicit cancellation, I would publish a delayed message to RabbitMQ/Amazon SQS upon seat hold. A background consumer would process this message 10 minutes later, check if the reservation status is still 'pending/held' (i.e., payment didn't complete), and automatically revert the seats.
2. **Read Replicas & CQRS:** Move the `GET /shows/{id}` queries to a PostgreSQL read-replica to ensure that heavy read traffic (users refreshing the seating chart) doesn't steal connection pool resources from the primary writer database during an on-sale burst.
3. **Optimized Batching:** For massive shows (e.g., a stadium with 80,000 seats), the `POST /shows` creation endpoint should batch insert records in chunks of 5,000 using PostgreSQL `COPY` commands rather than standard EF Core bulk inserts to prevent memory spikes.

## Run the Burst Test
The repository includes a one-command burst script that simulates a hot-seat storm, limit bypassing, and idempotent retries.
```bash
./burst.sh <DEPLOYMENT_URL>