"""End-to-end verification of the PayPal payments + saved-cards integration.

Drives every endpoint on the running PublicApi (JWT) against the PayPal sandbox using the
sandbox test Visa. Run against a freshly started instance (in-memory store resets per run).
"""
import json
import ssl
import sys
import time
import urllib.request
import urllib.error

BASE = "https://localhost:30603"
CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE

CARD = {
    "number": "4111111111111111", "expiry": "2030-01", "securityCode": "123",
    "name": "Demo User",
    "billingAddress": {"addressLine1": "1 Main", "adminArea2": "Redmond",
                        "adminArea1": "WA", "postalCode": "98052", "countryCode": "US"},
}

passed, failed = 0, 0

def check(label, cond, extra=""):
    global passed, failed
    mark = "PASS" if cond else "FAIL"
    if cond: passed += 1
    else: failed += 1
    print(f"  [{mark}] {label}{(' -> ' + extra) if extra else ''}")

def req(method, path, token=None, body=None, raw=False):
    url = BASE + path
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(url, data=data, method=method)
    r.add_header("Content-Type", "application/json")
    if token: r.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(r, context=CTX) as resp:
            txt = resp.read().decode()
            return resp.status, (json.loads(txt) if txt and not raw else txt)
    except urllib.error.HTTPError as e:
        txt = e.read().decode()
        try: return e.code, json.loads(txt)
        except Exception: return e.code, txt

def auth(user, pw):
    st, d = req("POST", "/api/authenticate", body={"username": user, "password": pw})
    return d["token"]

def section(t): print("\n=== " + t + " ===")

# ------------------------------------------------------------------ setup
section("AUTH")
shop = auth("demouser@microsoft.com", "Pass@word1")
admin = auth("admin@microsoft.com", "Pass@word1")
check("shopper + admin tokens", bool(shop) and bool(admin))

def place(items):
    st, d = req("POST", "/api/orders", shop, {"items": items})
    return d["orderId"], d

def pay(oid, token=shop, card=True, saved_id=None):
    body = {"card": CARD} if card else {"savedCardId": saved_id}
    return req("POST", f"/api/orders/{oid}/pay", token, body)

# ------------------------------------------------------------------ Flow 1
section("FLOW 1: place -> pay(authorize) -> fulfil(capture) -> refund")
oid, d = place([{"catalogItemId": 1, "quantity": 2}, {"catalogItemId": 2, "quantity": 1}])
total = d["total"]
check("order placed awaiting payment", d["orderStatus"] == "AwaitingPayment", f"orderId={oid} total={total}")

st, d = pay(oid)
p = d.get("payment", {})
check("pay authorized (hold placed, not captured)", st == 200 and p.get("status") == "Authorized", str(d)[:200])
check("authorized amount == order total to the cent", p.get("amount") == total)
check("authorization id present", bool(p.get("authorizationId")))
check("no capture yet", p.get("captureId") is None)

# idempotent pay: second pay must not create a second authorization
st2, d2 = pay(oid)
check("pay is idempotent (same authorization id on repeat)",
      d2.get("payment", {}).get("authorizationId") == p.get("authorizationId"))

st, d = req("GET", "/api/my-orders", shop)
mine = {o["orderId"]: o for o in d}
check("my-orders shows the order + payment state", oid in mine and mine[oid]["payment"]["status"] == "Authorized")

st, d = req("POST", f"/api/orders/{oid}/fulfil", admin)
p = d.get("payment", {})
check("fulfil captured the funds", st == 200 and p.get("status") == "Captured", f"captureId={p.get('captureId')}")
check("captured amount reported", p.get("capturedAmount") == total)
check("PayPal fee reported", p.get("payPalFee") is not None, f"fee={p.get('payPalFee')}")
check("net proceeds reported", p.get("netAmount") is not None, f"net={p.get('netAmount')}")

# fulfil idempotent
st, d = req("POST", f"/api/orders/{oid}/fulfil", admin)
check("fulfil is idempotent (same capture id)", d.get("payment", {}).get("captureId") == p.get("captureId"))

time.sleep(3)  # give the capture a moment to settle before refunding
section("REFUNDS: partial, idempotent, distinct, over-cap")
st, d = req("POST", f"/api/orders/{oid}/refunds", admin, {"amount": 10.00, "idempotencyKey": "refA"})
r1 = d.get("refundId")
check("partial refund #1 (10.00)", st == 200 and d.get("amount") == 10.00, f"refundId={r1}")
check("order now partially refunded", d.get("order", {}).get("orderStatus") == "PartiallyRefunded")
rem = d.get("order", {}).get("payment", {}).get("refundableRemaining")
check("refundable remaining reduced", rem == total - 10.00, f"remaining={rem}")

st, d = req("POST", f"/api/orders/{oid}/refunds", admin, {"amount": 10.00, "idempotencyKey": "refA"})
check("same key does NOT refund twice (same refundId)", d.get("refundId") == r1)
check("total refunded still 10.00", d.get("order", {}).get("payment", {}).get("totalRefunded") == 10.00,
      str(d.get("order", {}).get("payment", {}).get("totalRefunded")))

st, d = req("POST", f"/api/orders/{oid}/refunds", admin, {"amount": 5.00, "idempotencyKey": "refB"})
check("distinct key = legitimate second partial refund (5.00)", st == 200 and d.get("refundId") != r1)
check("total refunded now 15.00", d.get("order", {}).get("payment", {}).get("totalRefunded") == 15.00)

st, d = req("POST", f"/api/orders/{oid}/refunds", admin, {"amount": 100.00, "idempotencyKey": "refC"})
check("over-cap refund rejected (not > captured)", st >= 400 and st < 500, f"http={st}")

# ------------------------------------------------------------------ Cancel / void
section("CANCEL (void before fulfilment)")
oid2, _ = place([{"catalogItemId": 3, "quantity": 1}])
st, d = pay(oid2)
check("second order authorized", d.get("payment", {}).get("status") == "Authorized")
st, d = req("POST", f"/api/orders/{oid2}/cancel", admin)
p = d.get("payment", {})
check("cancel voided the hold", st == 200 and p.get("status") == "Voided" and d.get("orderStatus") == "Cancelled")
check("no money captured on cancel", p.get("captureId") is None)
st, d = req("POST", f"/api/orders/{oid2}/fulfil", admin)
check("cannot fulfil a cancelled order", st >= 400, f"http={st}")

# ------------------------------------------------------------------ Flow 2 saved cards
section("FLOW 2: save card -> reuse to pay a second order -> delete")
st, d = req("POST", "/api/payment-methods", shop, {**CARD})
pmid = d.get("paymentMethodId")
check("card saved (paymentMethodId returned)", st in (200, 201) and pmid is not None, f"pmid={pmid}")
check("safe description only (brand + last4, no PAN)", d.get("brand") and d.get("lastDigits") == "1111",
      f"brand={d.get('brand')} last4={d.get('lastDigits')}")

st, d = req("GET", "/api/payment-methods", shop)
check("saved card appears in caller's list", any(c["paymentMethodId"] == pmid for c in d))

oid3, _ = place([{"catalogItemId": 4, "quantity": 1}])
st, d = pay(oid3, card=False, saved_id=pmid)
check("paid a NEW order using the SAVED card", st == 200 and d.get("payment", {}).get("status") == "Authorized",
      str(d)[:200])
st, d = req("POST", f"/api/orders/{oid3}/fulfil", admin)
check("captured the saved-card order", d.get("payment", {}).get("status") == "Captured")

st, d = req("DELETE", f"/api/payment-methods/{pmid}", shop, raw=True)
check("delete saved card returns 204", st == 204, f"http={st}")
st, d = req("GET", "/api/payment-methods", shop)
check("deleted card no longer listed", all(c["paymentMethodId"] != pmid for c in d))
oid4, _ = place([{"catalogItemId": 5, "quantity": 1}])
st, d = pay(oid4, card=False, saved_id=pmid)
check("deleted card can no longer pay", st >= 400, f"http={st}")

# ------------------------------------------------------------------ authz / isolation
section("AUTHZ + SHOPPER ISOLATION")
st, d = req("GET", "/api/my-orders")
check("unauthenticated request rejected (401)", st == 401, f"http={st}")
st, d = req("POST", f"/api/orders/{oid}/fulfil", shop)
check("shopper cannot fulfil (admin-only) -> 403", st == 403, f"http={st}")
st, d = pay(oid, token=admin)  # admin acting as a different shopper on demouser's order
check("one shopper cannot pay another's order -> 404", st == 404, f"http={st}")
st, d = req("GET", "/api/my-orders", admin)
check("admin (as a shopper) does not see demouser's orders", all(o["orderId"] != oid for o in d))

# ------------------------------------------------------------------ reconciliation
section("RECONCILIATION (operator)")
import datetime
now = datetime.datetime.now(datetime.timezone.utc)
frm = (now - datetime.timedelta(days=3)).strftime("%Y-%m-%dT%H:%M:%SZ")
to = (now + datetime.timedelta(minutes=5)).strftime("%Y-%m-%dT%H:%M:%SZ")
st, d = req("GET", f"/api/reconciliation?from={frm}&to={to}", admin)
ok = st == 200 and "lines" in d and all(k in d for k in
     ("payPalTransactionCount", "eShopPaymentCount", "matchedCount", "payPalOnlyCount", "eShopOnlyCount"))
check("reconciliation report returns valid structure (200)", ok, f"http={st}")
if ok:
    print(f"       paypalTx={d['payPalTransactionCount']} eShopPayments={d['eShopPaymentCount']} "
          f"matched={d['matchedCount']} paypalOnly={d['payPalOnlyCount']} eShopOnly={d['eShopOnlyCount']}")
    print("       (an empty PayPal side over a just-created range is an expected sandbox reporting lag)")
st, d = req("GET", f"/api/reconciliation?from={frm}&to={to}", shop)
check("reconciliation is admin-only (shopper -> 403)", st == 403, f"http={st}")

print(f"\n================  {passed} passed, {failed} failed  ================")
sys.exit(1 if failed else 0)
