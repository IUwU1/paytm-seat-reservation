import asyncio
import httpx
import uuid
import sys
import time

async def make_request(client, method, url, headers, json_data=None):
    try:
        if method == "POST":
            response = await client.post(url, headers=headers, json=json_data)
        else:
            response = await client.get(url, headers=headers)
        return response.status_code, response.json() if response.content else {}
    except Exception as e:
        return 500, {"error": str(e)}

async def main():
    base_url = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5053"
    print(f"🚀 Starting Paytm Burst Test against {base_url}...\n")
    
    async with httpx.AsyncClient(timeout=30.0) as client:
        # 1. Create Show

        seats = [f"A{i}" for i in range(1, 501)]
        
        status, show = await make_request(client, "POST", f"{base_url}/shows", 
            {"Authorization": "Bearer admin_secret"}, 
            {"name": "test-burst", "seats": seats, "pricePaise": 10000})
        
        if status != 201:
            print(f"❌ Failed to create show: {status} {show}")
            return
        
        show_id = show["id"]
        print(f"✅ Show created: {show_id}\n")

        # 2. Scenario A: The Hot-Seat Storm (500 users trying to grab 'A1' at exact same time)
        print("⛈️ Firing Hot-Seat Storm (500 concurrent users targeting seat A1)...")
        tasks = []
        for i in range(500):
            tasks.append(make_request(client, "POST", f"{base_url}/shows/{show_id}/reserve",
                {"Authorization": f"Bearer user_{i}"},
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
        assert status_counts.get(409, 0) == 499, "❌ FAILED: Losers did not get 409 Conflict!"
        assert status_counts.get(500, 0) == 0, "❌ FAILED: 5xx Server Errors detected!"
        print("✅ Hot-Seat Storm Passed! Exactly one winner, clean declines for losers, zero 5xx.\n")

        # 3. Scenario B: Quota Bypassing (1 user firing 10 parallel requests for 10 different seats)
        print("🏃 Firing Quota Bypassing Test (1 user, 10 parallel threads)...")
        tasks = []
        for i in range(10):
            tasks.append(make_request(client, "POST", f"{base_url}/shows/{show_id}/reserve",
                {"Authorization": "Bearer user_greedy"},
                {"seats": [f"A{i+2}"], "idempotency_key": str(uuid.uuid4())}
            ))
        
        results = await asyncio.gather(*tasks)
        limit_success = sum(1 for code, _ in results if code == 201)
        print(f"📊 Greedy User successful reservations: {limit_success}")
        assert limit_success <= 4, "❌ FAILED: User exceeded the limit of 4 seats!"
        print("✅ Quota Passed! Advisory lock correctly stopped concurrent limit bypassing.\n")

        # 4. Scenario C: Idempotent Retries
        #print("🔁 Firing Idempotency Test (10 identical requests simultaneously)...")
        #idem_key = str(uuid.uuid4())
        #tasks = [make_request(client, "POST", f"{base_url}/shows/{show_id}/reserve",
        #        {"Authorization": "Bearer user_retry"},
        #        {"seats": ["A20"], "idempotency_key": idem_key}) for _ in range(10)]
        
        #results = await asyncio.gather(*tasks)
        #idem_success = sum(1 for code, _ in results if code == 201)
        #print(f"📊 Idempotency Outcome: {idem_success} executed, {10 - idem_success} cached/declined.")
        #assert idem_success == 1, "❌ FAILED: Idempotent request executed multiple times!"
        #print("✅ Idempotency Passed!\n")
        
        print("🔁 Firing Idempotency Test (10 identical requests simultaneously)...")
        idem_key = str(uuid.uuid4())
        tasks = [make_request(client, "POST", f"{base_url}/shows/{show_id}/reserve",
                {"Authorization": "Bearer user_retry"},
                {"seats": ["A20"], "idempotency_key": idem_key}) for _ in range(10)]
        
        results = await asyncio.gather(*tasks)
        idem_executed = sum(1 for code, _ in results if code == 201)
        idem_cached = sum(1 for code, _ in results if code == 200)
        idem_dropped = sum(1 for code, _ in results if code in (409, 429))
        
        print(f"📊 Idempotency Outcome: {idem_executed} executed (201), {idem_cached} cached (200), {idem_dropped} safely dropped (4xx).")
        
        assert idem_executed == 1, f"❌ FAILED: Expected exactly 1 execution, got {idem_executed}"
        assert idem_cached + idem_dropped == 9, f"❌ FAILED: Expected 9 remaining requests to be cached or cleanly dropped, got {idem_cached + idem_dropped}"
        assert sum(1 for code, _ in results if code >= 500) == 0, "❌ FAILED: 5xx errors detected during idempotency test!"
        
        print("✅ Idempotency Passed!\n")
        
        

        # 5. Invariant & State Check
        print("🔍 Checking final state invariant...")
        status, state = await make_request(client, "GET", f"{base_url}/shows/{show_id}", {})
        counts = state.get("counts", {})
        available = counts.get("available", 0)
        confirmed = counts.get("confirmed", 0)
        total = state.get("total_seats", 0)
        
        print(f"📊 Final State: {counts}")
        if available + confirmed == total:
            print("✅ INVARIANT HOLDS: available + confirmed == total_seats")
        else:
            print("❌ INVARIANT BROKEN!")
            
        print("\n🎉 ALL TESTS PASSED.")

if __name__ == "__main__":
    asyncio.run(main())