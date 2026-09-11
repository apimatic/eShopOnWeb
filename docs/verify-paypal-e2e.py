import json, ssl, urllib.request, urllib.error, sys, time

BASE = "https://localhost:30803"
CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE

def call(method, path, token=None, body=None):
    url = BASE + path
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, context=CTX) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, raw

def auth(user, pw):
    s, d = call("POST", "/api/authenticate", body={"username": user, "password": pw})
    return d["token"]

results = []
def check(name, cond, detail=""):
    results.append((name, cond, detail))
    print(("PASS" if cond else "FAIL"), "-", name, ("" if cond else ":: " + str(detail)))

CARD = {"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"Demo Shopper",
        "billingAddress":{"addressLine1":"123 Main St","adminArea2":"Kent","adminArea1":"OH","postalCode":"44240","countryCode":"US"}}

demo = auth("demouser@microsoft.com", "Pass@word1")
admin = auth("admin@microsoft.com", "Pass@word1")
print("== tokens acquired ==")

# ---- Flow 1: place, pay, fulfil, refund ----
s, order = call("POST", "/api/orders", demo, {"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]})
check("place order 201", s == 201, (s, order))
oid = order["orderId"]
expected_total = 19.5*2 + 8.5*1
check("order total correct", abs(order["total"] - expected_total) < 0.001, (order.get("total"), expected_total))
check("orderId top-level field", isinstance(oid, int), oid)

# pay (authorize)
s, pay = call("POST", f"/api/orders/{oid}/pay", demo, {"card": CARD})
check("pay 200 authorized", s == 200 and pay["status"] == "Authorized", (s, pay))
auth_id = pay.get("authorizationId")
check("authorization id present", bool(auth_id), pay)
check("hold equals order total to the cent", abs(pay["amount"] - round(expected_total,2)) < 0.001, pay.get("amount"))
check("card last4 shown, no full number", pay.get("cardLast4") == "1111", pay.get("cardLast4"))

# idempotent double-click pay
s, pay2 = call("POST", f"/api/orders/{oid}/pay", demo, {"card": CARD})
check("double pay idempotent (same auth id)", s == 200 and pay2.get("authorizationId") == auth_id, (s, pay2.get("authorizationId"), auth_id))

# shopper cannot fulfil (admin only)
s, _ = call("POST", f"/api/orders/{oid}/fulfil", demo)
check("shopper fulfil forbidden 403", s == 403, s)

# fulfil as admin -> capture
s, ful = call("POST", f"/api/orders/{oid}/fulfil", admin)
check("fulfil 200 captured", s == 200 and ful["status"] == "Captured", (s, ful))
check("captured amount == order total", ful.get("capturedAmount") is not None and abs(ful["capturedAmount"] - round(expected_total,2)) < 0.001, ful.get("capturedAmount"))
check("paypal fee reported", ful.get("payPalFee") is not None, ful.get("payPalFee"))
check("net proceeds reported", ful.get("netAmount") is not None, ful.get("netAmount"))
check("net = captured - fee", ful.get("netAmount") is not None and abs(ful["capturedAmount"] - ful["payPalFee"] - ful["netAmount"]) < 0.001, (ful.get("capturedAmount"),ful.get("payPalFee"),ful.get("netAmount")))
cap_id = ful.get("captureId")

# fulfil idempotent
s, ful2 = call("POST", f"/api/orders/{oid}/fulfil", admin)
check("fulfil idempotent (same capture id)", s == 200 and ful2.get("captureId") == cap_id, (s, ful2.get("captureId"), cap_id))

# partial refund
s, ref = call("POST", f"/api/orders/{oid}/refunds", demo, {"amount": 8.50, "idempotencyKey": "ref-key-A"})
check("partial refund 201", s == 201, (s, ref))
check("refundId top-level field", isinstance(ref.get("refundId"), int), ref)
check("payment now PartiallyRefunded", ref["payment"]["status"] == "PartiallyRefunded", ref["payment"]["status"])
rid = ref.get("refundId")

# idempotent refund (same key) -> same refund, no double
s, ref_same = call("POST", f"/api/orders/{oid}/refunds", demo, {"amount": 8.50, "idempotencyKey": "ref-key-A"})
check("refund idempotent same key (same refundId)", s == 201 and ref_same.get("refundId") == rid, (s, ref_same.get("refundId"), rid))

# over-refund beyond captured must be rejected
remaining = ref["payment"]["refundableRemaining"]
s, over = call("POST", f"/api/orders/{oid}/refunds", demo, {"amount": remaining + 100, "idempotencyKey": "ref-key-over"})
check("over-refund rejected (>captured)", s in (409,400), (s, over))

# second distinct partial refund (different key) legitimate
s, ref2 = call("POST", f"/api/orders/{oid}/refunds", demo, {"amount": 1.00, "idempotencyKey": "ref-key-B"})
check("second distinct partial refund allowed", s == 201 and ref2.get("refundId") != rid, (s, ref2.get("refundId"), rid))

# ---- Flow 2: saved cards ----
s, saved = call("POST", "/api/payment-methods", demo, {"card": CARD})
check("save card 201", s == 201, (s, saved))
pmid = saved.get("paymentMethodId")
check("paymentMethodId top-level field", isinstance(pmid, int), saved)
check("saved card safe description (no full number)", saved.get("cardLast4") == "1111" and "number" not in json.dumps(saved).lower().replace('cardnumber',''), saved)

s, lst = call("GET", "/api/payment-methods", demo)
check("list saved cards includes new one", any(m["paymentMethodId"] == pmid for m in lst["paymentMethods"]), lst)

# place a 2nd order and pay with saved card
s, order2 = call("POST", "/api/orders", demo, {"items":[{"catalogItemId":3,"quantity":1}]})
oid2 = order2["orderId"]
s, pay_saved = call("POST", f"/api/orders/{oid2}/pay", demo, {"paymentMethodId": pmid})
check("pay 2nd order with saved card authorized", s == 200 and pay_saved["status"] == "Authorized", (s, pay_saved))

# delete saved card
s, _ = call("DELETE", f"/api/payment-methods/{pmid}", demo)
check("delete saved card 204", s == 204, s)
s, lst2 = call("GET", "/api/payment-methods", demo)
check("deleted card no longer listed", all(m["paymentMethodId"] != pmid for m in lst2["paymentMethods"]), lst2)
# deleted card no longer usable
s, order3 = call("POST", "/api/orders", demo, {"items":[{"catalogItemId":4,"quantity":1}]})
oid3 = order3["orderId"]
s, pay_del = call("POST", f"/api/orders/{oid3}/pay", demo, {"paymentMethodId": pmid})
check("deleted card not usable to pay", s == 404, (s, pay_del))

# ---- Cancel flow (before fulfilment) ----
s, order4 = call("POST", "/api/orders", demo, {"items":[{"catalogItemId":5,"quantity":1}]})
oid4 = order4["orderId"]
s, pay4 = call("POST", f"/api/orders/{oid4}/pay", demo, {"card": CARD})
check("order4 authorized", s == 200 and pay4["status"] == "Authorized", (s, pay4))
s, canc = call("POST", f"/api/orders/{oid4}/cancel", admin)
check("cancel 200 -> Cancelled (funds released)", s == 200 and canc["status"] == "Cancelled", (s, canc))
# cannot fulfil a cancelled order
s, ff = call("POST", f"/api/orders/{oid4}/fulfil", admin)
check("cannot fulfil cancelled order", s == 409, (s, ff))

# ---- my-orders ----
s, mine = call("GET", "/api/my-orders", demo)
check("my-orders returns caller orders with payment state", s == 200 and any(o["orderId"] == oid and o["payment"] for o in mine["orders"]), s)

# ---- ownership: admin (different buyer) cannot refund demo's order ----
s, own = call("POST", f"/api/orders/{oid}/refunds", admin, {"amount": 1.0, "idempotencyKey": "ownership-key"})
check("cross-shopper refund blocked (404 not found)", s == 404, (s, own))
# admin's my-orders must not include demo's order
s, adminOrders = call("GET", "/api/my-orders", admin)
check("admin my-orders excludes demo order", all(o["orderId"] != oid for o in adminOrders["orders"]), adminOrders)

# ---- reconciliation (admin) over a wide range ----
s, rec = call("GET", "/api/reconciliation?from=2026-09-01T00:00:00Z&to=2026-09-30T23:59:59Z", admin)
check("reconciliation 200 (admin)", s == 200, (s, rec if s!=200 else "ok"))
if s == 200:
    print("   reconciliation: paypalTxns=%s eshopPayments=%s matched=%s missingInEShop=%s missingInPayPal=%s" % (
        rec["payPalTransactionCount"], rec["eShopPaymentCount"], len(rec["matched"]), len(rec["missingInEShop"]), len(rec["missingInPayPal"])))
# shopper cannot access reconciliation
s, _ = call("GET", "/api/reconciliation?from=2026-09-01T00:00:00Z&to=2026-09-30T23:59:59Z", demo)
check("shopper reconciliation forbidden 403", s == 403, s)

print("\n==== SUMMARY ====")
passed = sum(1 for _,c,_ in results if c)
print(f"{passed}/{len(results)} checks passed")
sys.exit(0 if passed == len(results) else 1)
