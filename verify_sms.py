import json, ssl, time, urllib.request, urllib.error, datetime, os, sys

BASE = "https://localhost:11623"
CTX = ssl._create_unverified_context()

def call(method, path, token=None, body=None, headers=None):
    url = BASE + path
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    try:
        with urllib.request.urlopen(req, context=CTX) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw.strip() else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, raw

def auth(u, p):
    s, d = call("POST", "/api/authenticate", body={"username": u, "password": p})
    assert d and d.get("result"), f"auth failed for {u}: {d}"
    return d["token"]

def check(cond, msg):
    print(("  PASS " if cond else "  FAIL ") + msg)
    if not cond:
        FAILS.append(msg)

FAILS = []
CANADA = os.environ["TWILIO_TEST_TO_NUMBER"]
US = os.environ["TWILIO_UNREACHABLE_TO_NUMBER"]

print("== Authenticate ==")
admin = auth("admin@microsoft.com", "Pass@word1")
shop = auth("demouser@microsoft.com", "Pass@word1")
print("  tokens acquired")

def poll_notifications(order_id, token, kinds_terminal, timeout=90):
    """Poll an order's notifications until the given kinds reach a terminal-ish state."""
    deadline = time.time() + timeout
    last = None
    while time.time() < deadline:
        s, d = call("GET", f"/api/orders/{order_id}/notifications", token=token)
        last = d
        ns = {n["kind"]: n for n in d["notifications"]}
        ok = True
        for k in kinds_terminal:
            st = ns.get(k, {}).get("status")
            if st not in ("delivered", "undelivered", "failed", "canceled", "send_error"):
                ok = False
        if ok:
            return d
        time.sleep(4)
    return last

print("\n== Flow 1: contact number (Canadian) ==")
s, d = call("POST", "/api/contact-numbers", token=shop, body={"phoneNumber": CANADA})
check(s == 201, f"register Canadian -> 201 (got {s})")
ca_id = d["contactNumberId"]; canonical = d["phoneNumber"]
print(f"    contactNumberId={ca_id} canonical={canonical}")
check(canonical.startswith("+"), "stored canonical E.164 form")

s, d = call("GET", "/api/contact-numbers", token=shop)
check(any(c["contactNumberId"] == ca_id for c in d["contactNumbers"]), "GET lists the registered number")

# invalid number rejected at registration
s, d = call("POST", "/api/contact-numbers", token=shop, body={"phoneNumber": "+1555"})
check(s == 400, f"clearly-invalid number rejected at registration -> 400 (got {s})")

print("\n== Flow 2: order placed -> dispatched (schedules follow-up) -> cancelled (calls it off) ==")
s, d = call("POST", "/api/orders", token=shop, body={"items": [{"catalogItemId": 1, "quantity": 1}, {"catalogItemId": 2, "quantity": 2}]})
check(s == 201, f"place order1 -> 201 (got {s})")
order1 = d["orderId"]; print(f"    orderId={order1} status={d['status']} total={d['total']}")

d = poll_notifications(order1, shop, ["OrderPlaced"])
placed = next((n for n in d["notifications"] if n["kind"] == "OrderPlaced"), None)
print(f"    OrderPlaced -> status={placed['status']} sid={placed['providerMessageSid']} err={placed['errorCode']}")
check(placed["status"] == "delivered", f"order-placed SMS DELIVERED to Canadian number (got {placed['status']})")
placed_sid = placed["providerMessageSid"]; placed_nid = placed["notificationId"]

s, d = call("POST", f"/api/orders/{order1}/dispatch", token=admin)
check(s == 200, f"dispatch order1 (admin) -> 200 (got {s})")
time.sleep(3)
s, d = call("GET", f"/api/orders/{order1}/notifications", token=shop)
ns = {n["kind"]: n for n in d["notifications"]}
check("OrderDispatched" in ns, "dispatched notification created")
follow = ns.get("DeliveryFollowUp")
check(follow is not None, "delivery follow-up created")
if follow:
    print(f"    follow-up status={follow['status']} sid={follow['providerMessageSid']} sendAt={follow['scheduledSendAt']}")
    check(follow["status"] == "scheduled", f"follow-up QUEUED with provider (scheduled) (got {follow['status']})")
    follow_sid = follow["providerMessageSid"]

# shopper cannot dispatch (not admin)
s, d = call("POST", f"/api/orders/{order1}/dispatch", token=shop)
check(s in (403, 401), f"shopper cannot dispatch (got {s})")

s, d = call("POST", f"/api/orders/{order1}/cancel", token=admin)
check(s == 200, f"cancel order1 (admin) -> 200 (got {s})")
time.sleep(3)
s, d = call("GET", f"/api/orders/{order1}/notifications", token=shop)
ns = {n["kind"]: n for n in d["notifications"]}
follow2 = ns.get("DeliveryFollowUp")
check(follow2 and follow2["status"] == "canceled", f"follow-up CALLED OFF before it sent (got {follow2['status'] if follow2 else None})")
check("OrderCancelled" in ns, "cancelled notification created")

print("\n== Flow 3: undeliverable number, operator resend + idempotency ==")
s, d = call("POST", "/api/contact-numbers", token=shop, body={"phoneNumber": US})
check(s == 201, f"register US unreachable -> 201 (got {s})")
us_id = d["contactNumberId"]

s, d = call("POST", "/api/orders", token=shop, body={"items": [{"catalogItemId": 3, "quantity": 1}]})
order2 = d["orderId"]; print(f"    orderId={order2}")
d = poll_notifications(order2, shop, ["OrderPlaced"], timeout=120)
placed2 = next((n for n in d["notifications"] if n["kind"] == "OrderPlaced"), None)
print(f"    order2 OrderPlaced -> status={placed2['status']} err={placed2['errorCode']}")
check(placed2["status"] in ("undelivered", "failed"), f"US destination is undelivered (expected live-account outcome) (got {placed2['status']})")
p2_nid = placed2["notificationId"]

s, d = call("POST", f"/api/notifications/{p2_nid}/resend", token=admin, body={"idempotencyKey": "resend-key-1"})
check(s == 201, f"resend (key1) -> 201 sent (got {s})")
r1_nid = d["notificationId"]; print(f"    resend#1 notificationId={r1_nid} outcome={d['outcome']}")

s, d = call("POST", f"/api/notifications/{p2_nid}/resend", token=admin, body={"idempotencyKey": "resend-key-1"})
check(s == 200 and d["notificationId"] == r1_nid and d["outcome"] == "duplicate", f"repeat with SAME key -> no new message, same id (got {s}/{d.get('outcome')}/{d.get('notificationId')})")

s, d = call("POST", f"/api/notifications/{p2_nid}/resend", token=admin, body={"idempotencyKey": "resend-key-2"})
check(s == 201 and d["notificationId"] != r1_nid, f"fresh key -> genuine new message (got {s}, id {d.get('notificationId')})")

# shopper cannot resend
s, d = call("POST", f"/api/notifications/{p2_nid}/resend", token=shop, body={"idempotencyKey": "shopper-should-fail"})
check(s in (401, 403), f"shopper cannot resend (got {s})")

print("\n== Flow 3b: content disposal (redact at provider) ==")
s, d = call("DELETE", f"/api/notifications/{placed_nid}/content", token=admin)
check(s == 204, f"dispose content (admin) -> 204 (got {s})")
s, d = call("GET", f"/api/orders/{order1}/notifications", token=shop)
disposed = next((n for n in d["notifications"] if n["notificationId"] == placed_nid), None)
check(disposed and disposed["contentRedacted"], "notification marked content-redacted")
print(f"    disposed sid for provider check: {placed_sid}")
with open("/tmp/disposed_sid.txt", "w") as f:
    f.write(placed_sid or "")

# shopper cannot dispose
s, d = call("DELETE", f"/api/notifications/{p2_nid}/content", token=shop)
check(s in (401, 403), f"shopper cannot dispose content (got {s})")

print("\n== Flow: my-orders ==")
s, d = call("GET", "/api/my-orders", token=shop)
check(s == 200, "GET my-orders 200")
oids = {o["orderId"] for o in d["orders"]}
check(order1 in oids and order2 in oids, "my-orders lists the caller's orders with their notifications")

print("\n== Ownership isolation ==")
# admin registers the (allowed) Canadian number under its own identity
s, d = call("POST", "/api/contact-numbers", token=admin, body={"phoneNumber": CANADA})
admin_cn = d["contactNumberId"]
s, d = call("GET", "/api/contact-numbers", token=shop)
check(all(c["contactNumberId"] != admin_cn for c in d["contactNumbers"]), "shopper does not see admin's contact number")
s, d = call("DELETE", f"/api/contact-numbers/{admin_cn}", token=shop)
check(s == 404, f"shopper cannot delete admin's number (got {s})")
# delete own number, then confirm gone
s, d = call("DELETE", f"/api/contact-numbers/{us_id}", token=shop)
check(s == 204, f"shopper deletes own number -> 204 (got {s})")
s, d = call("GET", "/api/contact-numbers", token=shop)
check(all(c["contactNumberId"] != us_id for c in d["contactNumbers"]), "deleted number no longer appears")

print("\n== Flow: reconciliation ==")
now = datetime.datetime.now(datetime.timezone.utc)
frm = (now - datetime.timedelta(hours=6)).strftime("%Y-%m-%dT%H:%M:%SZ")
to = (now + datetime.timedelta(minutes=5)).strftime("%Y-%m-%dT%H:%M:%SZ")
s, d = call("GET", f"/api/notifications/reconciliation?from={frm}&to={to}", token=admin)
check(s == 200, f"reconciliation (admin) -> 200 (got {s})")
if s == 200:
    print(f"    providerCount={d['providerCount']} eShopCount={d['eShopCount']} matchedCount={d['matchedCount']} providerOnly={len(d['providerOnly'])} eShopOnly={len(d['eShopOnly'])}")
    check(d["providerCount"] > 0, "provider returned messages for the configured sender in range")
    check(d["matchedCount"] > 0, "at least one message matches between provider and eShop")
# shopper cannot reconcile
s, d = call("GET", f"/api/notifications/reconciliation?from={frm}&to={to}", token=shop)
check(s in (401, 403), f"shopper cannot reconcile (got {s})")

print("\n===== SUMMARY =====")
print("FAILURES:", len(FAILS))
for f in FAILS:
    print("  -", f)
sys.exit(1 if FAILS else 0)
