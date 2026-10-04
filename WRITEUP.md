# Architecture & Engineering Writeup: Paytm Seat Reservation at Scale

## 1. Concurrency Control & The Atomic Decision
To guarantee exactly-once processing and prevent double-booking under extreme contention (e.g., 2,000 users targeting the same front-row seat), this system fundamentally rejects the naive "read-then-write" approach.

* **Bypassing the Change Tracker:** Standard Entity Framework Core `SaveChanges` operations are prone to race conditions under heavy load because the state read into memory can become stale milliseconds later. Instead, I utilized PostgreSQL's row-level conditional updates (`ExecuteUpdateAsync`). The query atomically enforces `WHERE status = 'available'` at the exact moment of the `UPDATE` statement at the database engine level.
* **The "Hot-Seat" Outcome:** If 2,000 threads storm seat A1, PostgreSQL's internal row locks ensure exactly one thread successfully modifies the row, returning an update count of 1. The remaining 1,999 threads return an update count of 0. This allows the API to safely trigger a clean `409 Conflict` (seat_taken) without throwing expensive database exceptions or crashing the connection pool.
* **Deterministic Deadlock Prevention:** When a user requests a multi-seat batch (e.g., `["A5", "A2"]`), the API sorts the incoming array lexicographically (`["A2", "A5"]`) before initiating the transaction. This guarantees that PostgreSQL always acquires physical row locks in a deterministic order across all concurrent requests, mathematically eliminating the possibility of relational deadlocks (circular wait conditions).

## 2. Idempotency & Exactly-Once Network Semantics
In a distributed system, network partitions will happen. Users will click the "Pay" button twice, or a mobile client will retry a request if a timeout occurs, leading to unintentional double-bookings.
* **Payload Hashing:** Idempotency is enforced via a unique composite key `(UserId, IdempotencyKey)`. To ensure the user hasn't changed their mind and reused a key for different seats, the system generates a SHA-256 hash of the sorted seat request payload.
* **The Read-Modify-Write Race Condition:** If a mobile app fires two identical retry requests at the exact same millisecond, a standard `SELECT` check for the idempotency key might return null for both, causing both to execute. To mitigate this, the idempotency check is placed *inside* a transaction scoped by a PostgreSQL advisory lock. This forces simultaneous identical requests to serialize—the first thread populates the cache, and the second thread safely returns the cached `200 OK` JSON response.
* **Payload Mismatches:** If a request arrives with an existing key but a mismatched payload hash, it is cleanly rejected with a `409 Conflict` to prevent accidental state mutation.

## 3. Distributed Quota Enforcement (Preventing Phantom Reads)
Business rules restrict users to a maximum of 4 seats per show. Enforcing this concurrently is notoriously difficult; a naive `SELECT COUNT(*)` allows a "Phantom Read" anomaly where 10 parallel threads all read a count of `0` and simultaneously insert 10 reservations.
* **The Advisory Lock Solution:** I implemented a fast, memory-only PostgreSQL advisory lock (`pg_advisory_xact_lock`). The lock is uniquely scoped to a 64-bit integer combining `hashtext(show_id) + hashtext(user_id)`.
* **Zero-Bottleneck Serialization:** This lock serializes quota-validation strictly per-user. User A's burst of 10 threads will be forced to queue, cleanly granting 4 seats and rejecting 6. Meanwhile, User B is completely unaffected because their lock hash is different.

## 4. Transactional Boundaries & Partial Availability Rollbacks
Multi-seat reservation requests are treated as strict atomic units.
* **The Batch Rollback:** If a user requests a batch of seats (e.g., `["A1", "A2", "A3", "A4"]`) and seat `A3` is already taken by a previous transaction, the entire transaction is rolled back cleanly.
* **Zero Data Drift:** This "all-or-nothing" approach guarantees that no partial bookings occur. Seats A1, A2, and A4 immediately remain available for other users, preventing inventory fragmentation.
* **Safe Cancellations:** Holds are modeled via an explicit cancellation endpoint. The cancellation atomically verifies token ownership and detaches the `reservation_id` from the seats. To prevent resurrection bugs, the SQL update explicitly scopes the reversion *only* to seats currently tied to that specific `reservation_id`.

## 5. CAP Theorem: Consistency over Availability
Under a network partition or heavy contention, this system explicitly favors **Consistency (CP)** over Availability. In a financial/ticketing context, it is vastly preferable to reject a user with a `429 Too Many Requests` or `409 Conflict` than to compromise the `available + held + confirmed == total_seats` invariant or accidentally double-charge a user. All contention timeouts are engineered to fail closed.

## 6. Observability, Security, & Throughput Optimization
To ensure the system is production-ready, observability and load-shedding were prioritized over standard boilerplate logging.
* **I/O Thread Starvation Mitigation:** Framework-level HTTP and EF Core console logging is strictly suppressed (`Warning` level) in production. During a 2,000-request stampede, writing informational logs to standard output creates massive I/O bottlenecks. Suppressing them ensures the CPU dedicates 100% of its cycles to processing transactions.
* **Security & Load Shedding:** A sanitized custom `GlobalExceptionHandler` intercepts Npgsql connection pool exhaustion and translates it into clean `429 Too Many Requests` responses. Unhandled application crashes are returned as generic `500` messages, preventing stack-trace information disclosure to malicious actors.
* **Metrics:** The system exposes an anonymous `/metrics` endpoint for Prometheus scraping. At 2:00 AM, PagerDuty alerts would be triggered by:
  1. Any spike in HTTP 5xx errors (indicating infrastructure logic failure).
  2. Saturation of the PostgreSQL connection pool (>85% active connections).
  3. Invariant drift (if the `paytm_seats_available` gauge + confirmed counters mathematically desync from the total venue capacity).

## 7. AI Disclosure
AI was utilized as an interactive pair-programmer to accelerate boilerplate generation (DTOs, EF Core entity scaffolding, Dockerfile setup) and to quickly author the asynchronous Python `burst.py` load-testing script. However, core architectural decisions—specifically the use of advisory locks for per-user limits, deterministic seat sorting, and raw SQL transaction boundaries to bypass EF Core change-tracker anomalies—were actively directed and designed by me to satisfy the strict correctness constraints required by the prompt.

## 8. What I'd Do Next (Future Architecture)
If I were to scale this service further for production, I would implement:
1. **Time-Boxed Holds via the Outbox Pattern:** Instead of relying solely on explicit cancellation, I would implement the Transactional Outbox pattern. Upon seat hold, an event is atomically saved to the database and published to RabbitMQ/Amazon SQS. A background worker would process this message 10 minutes later, check if the reservation status is still 'pending' (i.e., payment didn't complete), and automatically revert the seats.
2. **Read Replicas & CQRS:** Move the `GET /shows/{id}` queries to a PostgreSQL read-replica. This ensures that heavy read traffic (e.g., thousands of users refreshing the seating chart map) doesn't steal connection pool resources from the primary writer database during an active on-sale burst.
3. **Optimized Batching:** For massive venues (e.g., a stadium with 80,000 seats), the `POST /shows` creation endpoint should bypass EF Core bulk inserts and stream records directly using PostgreSQL `COPY` commands to prevent massive memory allocations on the API server.