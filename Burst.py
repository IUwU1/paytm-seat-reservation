import asyncio
import httpx
import uuid
import sys
import time

async def make_request(client, method, url, headers=None, json_data=None):
    headers = headers or {}
    try:
        if method == "POST":
            response = await client.post(url, headers=headers, json=json_data)
        else:
            response = await client.get(url, headers=headers)
        return response.status_code, response.json() if response.content else {}
    except Exception as e:
        return 500, {"error": str(e)}

async def bounded_request(sem, client, method, url, headers=None, json_data=None):
    async with sem:
        return await make_request(client, method, url, headers, json_data)

async def main():
    base_url = sys.argv[1] if len(sys.argv) > 1 else "https://paytm-seat-reservation-production-84ff.up.railway.app/"
    print(f"🚀 Starting Paytm Burst Test against {base_url}...\n")
    
    sem = asyncio.Semaphore(1000)
    
    run_id = str(uuid.uuid4())[:8]
    
    # Increased timeout to 120 seconds to allow cloud queues to drain properly
    async with httpx.AsyncClient(timeout=120.0) as client:
        
        # ==========================================
        # PHASE 1: BASIC ENDPOINT FUNCTIONAL TESTS
        # ==========================================
        print("🛠️ PHASE 1: Basic Endpoint Functional Tests")
        
        # 1. Test POST /shows
        print("   -> Testing POST /shows (Admin)...", end=" ")
        status, func_show = await make_request(
            client, "POST", f"{base_url}/shows", 
            {"Authorization": "Bearer admin_secret"}, 
            {"name": f"functional-test-{run_id}", "seats": ["T1", "T2"], "price_paise": 15000}
        )
        assert status == 201, f"❌ Failed: {status} {func_show}"
        func_show_id = func_show["id"]
        print("✅ Passed")

        # 2. Test GET /shows/{id}
        print("   -> Testing GET /shows/{id} (Public)...", end=" ")
        status, get_show = await make_request(client, "GET", f"{base_url}/shows/{func_show_id}")
        assert status == 200, f"❌ Failed: {status}"
        assert get_show["name"] == f"functional-test-{run_id}", "❌ Failed: Name mismatch"
        print("✅ Passed")

        func_user = f"User_FuncTest_{run_id}"
        func_idem = str(uuid.uuid4())

        # 3. Test POST /shows/{id}/reserve (Testing Mixed-Case Auth Header)
        print("   -> Testing POST /shows/{id}/reserve (User)...", end=" ")
        status, reserve_resp = await make_request(
            client, "POST", f"{base_url}/shows/{func_show_id}/reserve",
            {"Authorization": f"Bearer {func_user}"}, 
            {"seats": ["T1"], "idempotency_key": func_idem}
        )
        assert status == 201, f"❌ Failed: {status} {reserve_resp}"
        func_res_id = reserve_resp["reservation_id"]
        print("✅ Passed")

        # 4. Test POST /reservations/{id}/cancel (Testing Lower-Case Auth Header)
        print("   -> Testing POST /reservations/{id}/cancel (User)...", end=" ")
        status, cancel_resp = await make_request(
            client, "POST", f"{base_url}/reservations/{func_res_id}/cancel",
            {"Authorization": f"Bearer {func_user.lower()}"} 
        )
        assert status == 200, f"❌ Failed: {status} {cancel_resp}"
        print("✅ Passed\n")


        # ==========================================
        # PHASE 2: HIGH-CONCURRENCY & BUSINESS LOGIC
        # ==========================================
        print("🔥 PHASE 2: High-Concurrency & Business Logic Tests\n")

        # 1. Create Show
        seats = [f"A{i}" for i in range(1, 501)]
        
        status, show = await make_request(
            client, "POST", f"{base_url}/shows", 
            {"Authorization": "Bearer admin_secret"}, 
            {"name": f"test-burst-{run_id}", "seats": seats, "price_paise": 10000}
        )
        
        if status != 201:
            print(f"❌ Failed to create show: {status} {show}")
            return
        
        show_id = show["id"]
        print(f"✅ Show created: {show_id}\n")

        # 2. Scenario A: The Hot-Seat Storm (2,000 users trying to grab 'A1' at exact same time)
        TOTAL_HOT_SEAT_REQUESTS = 2000
        print(f"⛈ Firing Hot-Seat Storm ({TOTAL_HOT_SEAT_REQUESTS} concurrent users targeting seat A1)...")
        tasks = []
        for i in range(TOTAL_HOT_SEAT_REQUESTS):
            tasks.append(bounded_request(
                sem, client, "POST", f"{base_url}/shows/{show_id}/reserve",
                {"Authorization": f"Bearer user_{run_id}_{i}"},
                {"seats": ["A1"], "idempotency_key": str(uuid.uuid4())}
            ))
        
        start_time = time.time()
        results = await asyncio.gather(*tasks)
        print(f"⏱️ Burst completed in {time.time() - start_time:.2f} seconds.")

        status_counts = {}
        for code, _ in results:
            status_counts[code] = status_counts.get(code, 0) + 1
        
        print(f"📊 Hot-Seat Outcome: {status_counts}")
        assert status_counts.get(201, 0) == 1, "❌ FAILED: Seat A1 was double-booked!"
        
        declines = status_counts.get(409, 0) + status_counts.get(429, 0)
        assert declines == (TOTAL_HOT_SEAT_REQUESTS - 1), f"❌ FAILED: Expected {TOTAL_HOT_SEAT_REQUESTS - 1} declines, got {declines}!"
        assert status_counts.get(500, 0) == 0, "❌ FAILED: 5xx Server Errors detected!"
        print("✅ Hot-Seat Storm Passed! Exactly one winner, clean declines for losers, zero 5xx.\n")

        # 3. Scenario B: Quota Bypassing (1 user firing 10 parallel requests for 10 different seats)
        print("🏃 Firing Quota Bypassing Test (1 user, 10 parallel threads)...")
        tasks = []
        for i in range(10):
            tasks.append(bounded_request(
                sem, client, "POST", f"{base_url}/shows/{show_id}/reserve",
                {"Authorization": f"Bearer user_greedy_{run_id}"},
                {"seats": [f"A{i+2}"], "idempotency_key": str(uuid.uuid4())}
            ))
        
        results = await asyncio.gather(*tasks)
        limit_success = sum(1 for code, _ in results if code == 201)
        print(f"📊 Greedy User successful reservations: {limit_success}")
        assert limit_success <= 4, "❌ FAILED: User exceeded the limit of 4 seats!"
        print("✅ Quota Passed! Advisory lock correctly stopped concurrent limit bypassing.\n")

        # 4. Scenario C: Idempotent Retries
        print("🔁 Firing Idempotency Test (10 identical requests simultaneously)...")
        idem_key = str(uuid.uuid4())
        retry_user = f"Bearer user_retry_{run_id}"
        tasks = [
            bounded_request(
                sem, client, "POST", f"{base_url}/shows/{show_id}/reserve",
                {"Authorization": retry_user},
                {"seats": ["A20"], "idempotency_key": idem_key}
            ) for _ in range(10)
        ]
        
        results = await asyncio.gather(*tasks)
        idem_executed = sum(1 for code, _ in results if code == 201)
        idem_cached = sum(1 for code, _ in results if code == 200)
        idem_dropped = sum(1 for code, _ in results if code in (409, 429))
        
        print(f"📊 Idempotency Outcome: {idem_executed} executed (201), {idem_cached} cached (200), {idem_dropped} safely dropped (4xx).")
        
        assert idem_executed == 1, f"❌ FAILED: Expected exactly 1 execution, got {idem_executed}"
        assert idem_cached + idem_dropped == 9, f"❌ FAILED: Expected 9 remaining requests to be cached or cleanly dropped, got {idem_cached + idem_dropped}"
        assert sum(1 for code, _ in results if code >= 500) == 0, "❌ FAILED: 5xx errors detected during idempotency test!"
        
        print("✅ Idempotency Passed!\n")
        
        # 5. Idempotency Payload Mismatch
        print("🕵️ Firing Idempotency Mismatch Test (Same Key, Different Seats)...")
        status, response = await make_request(
            client, "POST", f"{base_url}/shows/{show_id}/reserve",
            {"Authorization": retry_user},
            {"seats": ["A21"], "idempotency_key": idem_key} 
        )
                
        print(f"📊 Mismatch Outcome: {status} {response}")
        assert status == 409, f"❌ FAILED: Expected 409 Conflict for mismatched body, got {status}"
        assert response.get("reason") == "idempotent_replay_mismatch", "❌ FAILED: Incorrect decline reason"
        print("✅ Idempotency Mismatch Passed!\n")

        # 6. Scenario G: Transactional Rollback (Partial Availability)
        print("🛑 Firing Transactional Rollback Test (Requesting batch A50-A53, but A52 is taken)...")
        # Pre-book seat A52
        status, _ = await make_request(
            client, "POST", f"{base_url}/shows/{show_id}/reserve",
            {"Authorization": f"Bearer user_prebook_{run_id}"},
            {"seats": ["A52"], "idempotency_key": str(uuid.uuid4())}
        )
        assert status == 201, f"❌ FAILED: Could not pre-book seat A52. Got {status}"

        # Attempt to book batch including the taken seat
        status, response = await make_request(
            client, "POST", f"{base_url}/shows/{show_id}/reserve",
            {"Authorization": f"Bearer user_batchfail_{run_id}"},
            {"seats": ["A50", "A51", "A52", "A53"], "idempotency_key": str(uuid.uuid4())}
        )
        assert status == 409, f"❌ FAILED: Expected 409 Conflict, got {status}"
        
        # Verify no partial bookings occurred
        _, show_state = await make_request(client, "GET", f"{base_url}/shows/{show_id}", {})
        seat_status = {s["seatNumber"]: s["status"].lower() for s in show_state.get("seats", [])}
        
        assert seat_status.get("A50") == "available", "❌ FAILED: Seat A50 was partially booked!"
        assert seat_status.get("A51") == "available", "❌ FAILED: Seat A51 was partially booked!"
        assert seat_status.get("A53") == "available", "❌ FAILED: Seat A53 was partially booked!"
        print("✅ Transactional Rollback Passed! No partial bookings occurred.\n")

        # 7. Invariant & State Check
        print("🔍 Checking current state invariant...")
        status, state = await make_request(client, "GET", f"{base_url}/shows/{show_id}", {})
        counts = state.get("counts", {})
        available = counts.get("available", 0)
        confirmed = counts.get("confirmed", 0)
        total = state.get("total_seats", 0)
        
        print(f"📊 Current State: {counts}")
        if available + confirmed == total:
            print("✅ INVARIANT HOLDS: available + confirmed == total_seats")
        else:
            print("❌ INVARIANT BROKEN!")
        
        # 8. Cancellation & Rebooking
        print("🗑️ Firing Cancellation & Re-booking Test...")
        
        _, show_state = await make_request(client, "GET", f"{base_url}/shows/{show_id}", {})
        available_seats = [s["seatNumber"] for s in show_state.get("seats", []) if s["status"].lower() == "available"]
        assert len(available_seats) > 0, "No available seats left for the cancel test!"
        
        # Pick from the end of the array to avoid seats the other tests might have touched
        cancel_seat = available_seats[-1] 
        
        # User A books a seat
        cancel_idem = str(uuid.uuid4())
        user_cancela = f"Bearer user_cancela_{run_id}"
        user_cancelb = f"Bearer user_cancelb_{run_id}"
        
        status, book_resp = await make_request(
            client, "POST", f"{base_url}/shows/{show_id}/reserve",
            {"Authorization": user_cancela},
            {"seats": [cancel_seat], "idempotency_key": cancel_idem}
        )
        
        assert status == 201, f"Failed to book seat {cancel_seat} for cancel test. Got {status}"
        reservation_id = book_resp["reservation_id"]

        # User B tries to cancel User A's reservation (Spoofing Identity Test)
        status, _ = await make_request(
            client, "POST", f"{base_url}/reservations/{reservation_id}/cancel",
            {"Authorization": user_cancelb},
            {}
        )
        
        assert status in (401, 403, 404, 409), f"❌ FAILED: User B was able to cancel User A's reservation! Got {status}"

        # User A successfully cancels their own reservation
        status, cancel_resp = await make_request(
            client, "POST", f"{base_url}/reservations/{reservation_id}/cancel",
            {"Authorization": user_cancela}, 
            {}
        )
        
        assert status == 200, f"❌ FAILED: User A could not cancel their own reservation! Got {status}"

        # User B successfully re-books the now-released seat
        status, _ = await make_request(
            client, "POST", f"{base_url}/shows/{show_id}/reserve",
            {"Authorization": user_cancelb}, 
            {"seats": [cancel_seat], "idempotency_key": str(uuid.uuid4())}
        )
        
        assert status == 201, f"❌ FAILED: Released seat was not cleanly re-bookable! Got {status}"
        print("✅ Cancellation & Re-booking Passed!\n")
        
        # 9. Reconciliation
        print("⚖️ Checking Final Reconciliation Invariant...")
        
        status, show_state = await make_request(client, "GET", f"{base_url}/shows/{show_id}",{})
        assert status == 200, "❌ FAILED: Could not fetch show state"

        # Calculate counts from the returned per-seat status array
        seats = show_state.get("seats", [])
        total_seats = show_state.get("total_seats", len(seats))
        
        available = sum(1 for s in seats if s["status"].lower() == "available")
        confirmed = sum(1 for s in seats if s["status"].lower() == "confirmed")
        held = sum(1 for s in seats if s["status"].lower() == "held")
        
        print(f"📊 Final State: {available} available + {held} held + {confirmed} confirmed = {available + held + confirmed} (Total: {total_seats})")
        
        assert (available + held + confirmed) == total_seats, "❌ FAILED: INVARIANT DRIFT DETECTED! Seats were lost or duplicated."
        print("✅ Reconciliation Invariant Holds!\n")        
            
    print("\n🎉 ALL TESTS PASSED.")

if __name__ == "__main__":
    asyncio.run(main())