"""End-to-end verification of the PayPal payments + saved-cards integration against the running PublicApi.
Stdlib only. Run: python verify_paypal.py"""
import json, ssl, urllib.request, urllib.error, uuid, datetime, sys

BASE = "https://localhost:31403"
CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE

PASS, FAIL = 0, 0

def http(method, path, token=None, body=None):
    url = BASE + path
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Accept", "application/json")
    if data is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, context=CTX) as r:
            txt = r.read().decode()
            return r.status, (json.loads(txt) if txt else None)
    except urllib.error.HTTPError as e:
        txt = e.read().decode()
        try:
            parsed = json.loads(txt)
        except Exception:
            parsed = txt
        return e.code, parsed

def check(name, cond, detail=""):
    global PASS, FAIL
    if cond:
        PASS += 1
        print(f"  PASS  {name}")
    else:
        FAIL += 1
        print(f"  FAIL  {name}  :: {detail}")

def section(t): print("\n=== " + t + " ===")

CARD = {"number": "4111111111111111", "expiry": "12/2030", "securityCode": "123",
        "cardholderName": "Test Buyer",
        "billingAddress": {"addressLine1": "1 Market St", "adminArea2": "San Jose",
                           "adminArea1": "CA", "postalCode": "95131", "countryCode": "US"}}

# --- auth ---
section("Authenticate")
s, d = http("POST", "/api/authenticate", body={"username": "demouser@microsoft.com", "password": "Pass@word1"})
demo = d.get("token") if isinstance(d, dict) else None
check("demouser authenticated", bool(demo), f"{s} {d}")
s, d = http("POST", "/api/authenticate", body={"username": "admin@microsoft.com", "password": "Pass@word1"})
admin = d.get("token") if isinstance(d, dict) else None
check("admin authenticated", bool(admin), f"{s} {d}")

# --- catalog ---
section("Catalog")
s, d = http("GET", "/api/catalog-items?pageSize=5&pageIndex=0")
items = d.get("catalogItems", []) if isinstance(d, dict) else []
check("catalog items fetched", len(items) >= 2, f"{s}")
it1, it2 = items[0], items[1]
print(f"  item1 id={it1['id']} price={it1['price']}  item2 id={it2['id']} price={it2['price']}")

def place_order(token, lines):
    return http("POST", "/api/orders", token=token, body={"items": lines})

def money(x): return round(float(x) + 1e-9, 2)

# --- Flow 1: pay, fulfil, refund ---
section("Flow 1 - place order (demouser)")
lines = [{"catalogItemId": it1["id"], "quantity": 2}, {"catalogItemId": it2["id"], "quantity": 1}]
expected_total = money(it1["price"] * 2 + it2["price"] * 1)
s, d = place_order(demo, lines)
check("order placed (201)", s == 201, f"{s} {d}")
order_id = d["orderId"]
check("orderId is top-level", "orderId" in d, str(d))
check("amount equals catalog total", money(d["amount"]) == expected_total, f"{d.get('amount')} vs {expected_total}")
print(f"  orderId={order_id} total={expected_total} {d.get('currency')}")

section("Pay - authorize with direct card")
s, d = http("POST", f"/api/orders/{order_id}/pay", token=demo, body={"card": CARD})
check("pay returns 200", s == 200, f"{s} {d}")
check("status Authorized", isinstance(d, dict) and d.get("paymentStatus") == "Authorized", str(d))
auth = d.get("authorization") if isinstance(d, dict) else None
check("authorization id present", bool(auth and auth.get("id")), str(d))
check("held amount == order total", isinstance(d, dict) and money(d["amount"]) == expected_total, str(d))
first_auth_id = auth["id"] if auth else None

section("Pay idempotency (double-click)")
s, d = http("POST", f"/api/orders/{order_id}/pay", token=demo, body={"card": CARD})
check("second pay still 200 Authorized", s == 200 and d.get("paymentStatus") == "Authorized", f"{s} {d}")
check("same authorization id (no double authorize)", d.get("authorization", {}).get("id") == first_auth_id,
      f"{d.get('authorization',{}).get('id')} vs {first_auth_id}")

section("Ownership - admin cannot pay/see demouser's order")
s, d = http("POST", f"/api/orders/{order_id}/pay", token=admin, body={"card": CARD})
check("admin (different buyer) gets 404 on demouser order", s == 404, f"{s} {d}")

section("my-orders (demouser) shows payment state")
s, d = http("GET", "/api/my-orders", token=demo)
mine = [o for o in d.get("orders", []) if o["orderId"] == order_id] if isinstance(d, dict) else []
check("order visible with Authorized state", len(mine) == 1 and mine[0]["payment"]["paymentStatus"] == "Authorized", str(d)[:300])

section("Fulfil (admin) - capture money")
s, d = http("POST", f"/api/orders/{order_id}/fulfil", token=admin)
check("fulfil returns 200", s == 200, f"{s} {d}")
cap = d.get("capture") if isinstance(d, dict) else None
check("status Captured", isinstance(d, dict) and d.get("paymentStatus") == "Captured", str(d))
check("capture id present", bool(cap and cap.get("id")), str(d))
check("captured amount == total", cap and money(cap["capturedAmount"]) == expected_total, str(cap))
check("paypal fee reported", cap and cap.get("payPalFee") is not None, str(cap))
check("net proceeds reported", cap and cap.get("netAmount") is not None, str(cap))
print(f"  capture={cap['id'] if cap else None} gross={cap['capturedAmount']} fee={cap['payPalFee']} net={cap['netAmount']}")

section("Fulfil idempotency")
s, d = http("POST", f"/api/orders/{order_id}/fulfil", token=admin)
check("re-fulfil still Captured (idempotent)", s == 200 and d.get("paymentStatus") == "Captured", f"{s}")
check("same capture id", d.get("capture", {}).get("id") == (cap["id"] if cap else None), str(d.get("capture")))

section("Non-admin cannot fulfil")
s, d = http("POST", f"/api/orders/{order_id}/fulfil", token=demo)
check("demouser fulfil forbidden (403)", s == 403, f"{s} {d}")

section("Refund - partial, idempotency, second partial, over-cap")
key1 = "rk-" + uuid.uuid4().hex[:8]
part = money(expected_total * 0 + 5.00) if expected_total > 5 else money(expected_total / 2)
s, d = http("POST", f"/api/orders/{order_id}/refunds", token=demo, body={"amount": part, "idempotencyKey": key1})
check("partial refund 201", s == 201, f"{s} {d}")
check("refundId top-level", isinstance(d, dict) and "refundId" in d, str(d))
refund1 = d.get("refundId")
check("total refunded == part", money(d["totalRefunded"]) == part, str(d))
s, d = http("POST", f"/api/orders/{order_id}/refunds", token=demo, body={"amount": part, "idempotencyKey": key1})
check("same key -> same refund, no double refund", d.get("refundId") == refund1 and money(d["totalRefunded"]) == part, str(d))
key2 = "rk-" + uuid.uuid4().hex[:8]
s, d = http("POST", f"/api/orders/{order_id}/refunds", token=demo, body={"amount": part, "idempotencyKey": key2})
check("second distinct partial refund allowed", s == 201 and d.get("refundId") != refund1, f"{s} {d}")
check("total refunded == 2*part", money(d["totalRefunded"]) == money(part * 2), str(d))
key3 = "rk-" + uuid.uuid4().hex[:8]
s, d = http("POST", f"/api/orders/{order_id}/refunds", token=demo, body={"amount": expected_total, "idempotencyKey": key3})
check("refund beyond captured rejected (422)", s == 422, f"{s} {d}")

# --- cancel flow ---
section("Flow 1 - cancel before fulfilment")
s, d = place_order(demo, [{"catalogItemId": it1["id"], "quantity": 1}])
order2 = d["orderId"]
s, d = http("POST", f"/api/orders/{order2}/pay", token=demo, body={"card": CARD})
check("order2 authorized", s == 200 and d.get("paymentStatus") == "Authorized", f"{s} {d}")
s, d = http("POST", f"/api/orders/{order2}/cancel", token=admin)
check("cancel returns 200 Cancelled", s == 200 and d.get("paymentStatus") == "Cancelled", f"{s} {d}")
s, d = http("POST", f"/api/orders/{order2}/pay", token=demo, body={"card": CARD})
check("cannot pay a cancelled order (409)", s == 409, f"{s} {d}")

# --- Flow 2: saved cards ---
section("Flow 2 - save card")
s, d = http("POST", "/api/payment-methods", token=demo, body=CARD)
check("save card 201", s == 201, f"{s} {d}")
check("paymentMethodId top-level", isinstance(d, dict) and "paymentMethodId" in d, str(d))
pm_id = d.get("paymentMethodId")
check("last4 = 1111 (safe description)", d.get("last4") == "1111", str(d))
check("no full number in response", "number" not in json.dumps(d), str(d))
print(f"  saved paymentMethodId={pm_id} brand={d.get('brand')} last4={d.get('last4')} expiry={d.get('expiry')}")

section("List saved cards (demouser)")
s, d = http("GET", "/api/payment-methods", token=demo)
check("card appears in list", any(m["paymentMethodId"] == pm_id for m in d.get("paymentMethods", [])), str(d))

section("Ownership - admin cannot see/delete demouser's card")
s, d = http("GET", "/api/payment-methods", token=admin)
check("admin does not see demouser card", all(m["paymentMethodId"] != pm_id for m in d.get("paymentMethods", [])), str(d))
s, d = http("DELETE", f"/api/payment-methods/{pm_id}", token=admin)
check("admin delete of demouser card -> 404", s == 404, f"{s} {d}")

section("Reuse saved card to pay a second order")
s, d = place_order(demo, [{"catalogItemId": it2["id"], "quantity": 3}])
order3 = d["orderId"]
exp3 = money(it2["price"] * 3)
s, d = http("POST", f"/api/orders/{order3}/pay", token=demo, body={"savedPaymentMethodId": pm_id})
check("pay with saved card authorized", s == 200 and d.get("paymentStatus") == "Authorized", f"{s} {d}")
check("held amount == order3 total", isinstance(d, dict) and money(d["amount"]) == exp3, str(d))
s, d = http("POST", f"/api/orders/{order3}/fulfil", token=admin)
check("saved-card order captured", s == 200 and d.get("paymentStatus") == "Captured", f"{s} {d}")

section("Delete saved card, then it is unusable")
s, d = http("DELETE", f"/api/payment-methods/{pm_id}", token=demo)
check("delete own card -> 204", s == 204, f"{s} {d}")
s, d = http("GET", "/api/payment-methods", token=demo)
check("card no longer listed", all(m["paymentMethodId"] != pm_id for m in d.get("paymentMethods", [])), str(d))
s, d = place_order(demo, [{"catalogItemId": it1["id"], "quantity": 1}])
order4 = d["orderId"]
s, d = http("POST", f"/api/orders/{order4}/pay", token=demo, body={"savedPaymentMethodId": pm_id})
check("deleted card cannot be used to pay -> 404", s == 404, f"{s} {d}")

# --- reconciliation ---
section("Reconciliation (admin, whole range)")
now = datetime.datetime.now(datetime.timezone.utc)
frm = (now - datetime.timedelta(days=40)).strftime("%Y-%m-%dT%H:%M:%SZ")
to = now.strftime("%Y-%m-%dT%H:%M:%SZ")
s, d = http("GET", f"/api/reconciliation?from={frm}&to={to}", token=admin)
check("reconciliation 200", s == 200, f"{s} {str(d)[:200]}")
if isinstance(d, dict):
    print(f"  paypalTxns={d.get('payPalTransactionCount')} matched={d.get('matchedCount')} "
          f"inPayPalOnly={d.get('inPayPalOnlyCount')} inEShopOnly={d.get('inEShopOnlyCount')}")
s, d = http("GET", "/api/reconciliation?from=" + frm + "&to=" + to, token=demo)
check("reconciliation forbidden for non-admin (403)", s == 403, f"{s}")

section("Auth required")
s, d = http("POST", f"/api/orders/{order_id}/pay", body={"card": CARD})
check("pay without token -> 401", s == 401, f"{s}")

print(f"\n==== RESULT: {PASS} passed, {FAIL} failed ====")
sys.exit(1 if FAIL else 0)
