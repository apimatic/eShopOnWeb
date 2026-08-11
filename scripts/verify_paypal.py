#!/usr/bin/env python
"""End-to-end verification of the PayPal integration against the sandbox.
Run the PublicApi first (see README-PAYPAL.md). Usage: python verify_paypal.py"""
import json, sys, urllib.request, ssl, uuid

BASE = "https://localhost:9083/api"
CTX = ssl.create_default_context(); CTX.check_hostname = False; CTX.verify_mode = ssl.CERT_NONE
CARD = {"number": "4111111111111111", "expiryMonth": "12", "expiryYear": "2030",
        "securityCode": "123", "cardholderName": "Test Shopper",
        "billingLine1": "1 Market St", "billingCity": "San Jose", "billingState": "CA",
        "billingPostalCode": "95131", "billingCountryCode": "US"}

def req(method, path, token=None, body=None):
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(BASE + path, data=data, method=method)
    r.add_header("Content-Type", "application/json")
    if token: r.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(r, context=CTX) as resp:
            txt = resp.read().decode()
            return resp.status, (json.loads(txt) if txt else None)
    except urllib.error.HTTPError as e:
        txt = e.read().decode()
        try: return e.code, json.loads(txt)
        except Exception: return e.code, txt

def tok(u):
    _, d = req("POST", "/authenticate", body={"username": u, "password": "Pass@word1"})
    return d["token"]

def show(label, status, body):
    print(f"\n=== {label}  [HTTP {status}] ===")
    print(json.dumps(body, indent=2, default=str) if isinstance(body, (dict, list)) else body)
    return body

def expect(cond, msg):
    print(("  PASS: " if cond else "  FAIL: ") + msg)
    if not cond: FAIL.append(msg)

FAIL = []
shop = tok("demouser@microsoft.com")
admin = tok("admin@microsoft.com")
print("authenticated shopper + admin")

# ---------- Flow A: card pay -> fulfil -> partial refund ----------
s, o = req("POST", "/orders", shop, {"items": [{"catalogItemId": 3, "quantity": 2}, {"catalogItemId": 5, "quantity": 1}]})
show("A1 place order (2x$12 + 1x$8.5 = $32.50)", s, o)
orderA = o["orderId"]; expect(s == 201 and o["total"] == 32.50, "order total is 32.50 from catalog prices")

s, p = req("POST", f"/orders/{orderA}/pay", shop, {"card": CARD})
show("A2 authorize (hold) with test card", s, p)
expect(s == 200 and p["status"] == "Authorized" and p["authorizationId"], "authorization created (money held, not taken)")
authId = p["authorizationId"]

s, p2 = req("POST", f"/orders/{orderA}/pay", shop, {"card": CARD})
expect(s == 200 and p2.get("authorizationId") == authId, "double-click pay is idempotent (same authorization id)")

s, p = req("POST", f"/orders/{orderA}/fulfil", admin)
show("A3 fulfil (capture money)", s, p)
expect(s == 200 and p["status"] == "Captured" and p["captureId"], "captured at fulfilment")
expect(p.get("capturedAmount") == 32.50, "captured amount == order total 32.50")
expect(p.get("payPalFee") is not None and p.get("netAmount") is not None, "payment shows PayPal fee and net proceeds")

key1 = str(uuid.uuid4())
s, r = req("POST", f"/orders/{orderA}/refunds", shop, {"amount": 10.00, "idempotencyKey": key1})
show("A4 partial refund $10", s, r)
expect(s == 201 and r["refundId"], "refundId returned")
expect(r["payment"]["refundableAmount"] == 22.50, "refundable now 22.50")

s, r2 = req("POST", f"/orders/{orderA}/refunds", shop, {"amount": 10.00, "idempotencyKey": key1})
expect(r2.get("refundId") == r["refundId"], "repeat refund under same key is idempotent (no double refund)")

s, r3 = req("POST", f"/orders/{orderA}/refunds", shop, {"amount": 9999.00, "idempotencyKey": str(uuid.uuid4())})
show("A5 over-refund attempt (should be rejected)", s, r3)
expect(s == 400, "refund beyond captured amount is rejected")

# ---------- Flow B: cancel before fulfilment ----------
s, o = req("POST", "/orders", shop, {"items": [{"catalogItemId": 4, "quantity": 1}]})
orderB = o["orderId"]; show("B1 place order ($12)", s, o)
s, p = req("POST", f"/orders/{orderB}/pay", shop, {"card": CARD})
expect(s == 200 and p["status"] == "Authorized", "order B authorized")
s, p = req("POST", f"/orders/{orderB}/cancel", admin)
show("B2 cancel (void hold)", s, p)
expect(s == 200 and p["status"] == "Voided", "order B cancelled, funds released (no money moved)")

# ---------- Flow 2: save a card, reuse it to pay a second order ----------
s, m = req("POST", "/payment-methods", shop, {"card": CARD, "label": "my visa"})
show("C1 save card (vault)", s, m)
pmId = m["paymentMethodId"]
expect(s == 201 and pmId and m["last4"] == "1111" and "number" not in m, "saved card returns id + last4, never full PAN")

s, lst = req("GET", "/payment-methods", shop)
expect(any(c["paymentMethodId"] == pmId for c in lst["paymentMethods"]), "saved card appears in the caller's list")

s, o = req("POST", "/orders", shop, {"items": [{"catalogItemId": 5, "quantity": 4}]})
orderC = o["orderId"]; show(f"C2 place 2nd order ($34.00)", s, o)
s, p = req("POST", f"/orders/{orderC}/pay", shop, {"savedPaymentMethodId": pmId})
show("C3 pay 2nd order with SAVED card", s, p)
expect(s == 200 and p["status"] == "Authorized" and p.get("savedPaymentMethodId") == pmId, "saved card reused to authorize a later order")
s, p = req("POST", f"/orders/{orderC}/fulfil", admin)
expect(s == 200 and p["status"] == "Captured", "2nd order captured")

s, _ = req("DELETE", f"/payment-methods/{pmId}", shop)
expect(s == 204, "saved card deleted (204)")
s, lst = req("GET", "/payment-methods", shop)
expect(not any(c["paymentMethodId"] == pmId for c in lst["paymentMethods"]), "deleted card no longer listed")
s, o = req("POST", "/orders", shop, {"items": [{"catalogItemId": 4, "quantity": 1}]})
orderD = o["orderId"]
s, p = req("POST", f"/orders/{orderD}/pay", shop, {"savedPaymentMethodId": pmId})
show("C4 pay a fresh order with the DELETED saved card (must be rejected)", s, p)
expect(s == 404, "deleted card can no longer be used to pay")

# ---------- ownership isolation ----------
other = tok("admin@microsoft.com")  # different identity than the shopper
s, _ = req("POST", f"/orders/{orderA}/refunds", other, {"amount": 1, "idempotencyKey": str(uuid.uuid4())})
expect(s == 404, "another user cannot act on the shopper's order (404)")

# ---------- my-orders ----------
s, mine = req("GET", "/my-orders", shop)
show("D my-orders (shopper view with payment state)", s, {"count": len(mine["orders"]),
     "statuses": [(o["orderId"], o["payment"]["status"]) for o in mine["orders"]]})
expect(s == 200 and len(mine["orders"]) >= 3, "my-orders lists the caller's orders with payment state")

# ---------- reconciliation (operator) ----------
s, rec = req("GET", "/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-31T23:59:59Z", admin)
show("E reconciliation (Aug 2026; sandbox reporting lags so may be empty)", s,
     {k: rec[k] for k in ("payPalTransactionCount","eShopPaymentCount","matchedCount","payPalOnlyCount","eShopOnlyCount")} if isinstance(rec, dict) else rec)
expect(s == 200, "reconciliation report returns 200 over a range")

print("\n================ RESULT ================")
print("ALL CHECKS PASSED" if not FAIL else f"{len(FAIL)} CHECK(S) FAILED:\n  - " + "\n  - ".join(FAIL))
sys.exit(1 if FAIL else 0)
