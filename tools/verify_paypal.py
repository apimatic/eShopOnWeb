import json, urllib.request, urllib.error, sys, datetime

BASE = "http://localhost:30484"

def call(method, path, token=None, body=None, expect=None):
    url = BASE + path
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req) as r:
            txt = r.read().decode()
            code = r.status
    except urllib.error.HTTPError as e:
        txt = e.read().decode()
        code = e.code
    try:
        parsed = json.loads(txt) if txt else {}
    except Exception:
        parsed = {"raw": txt}
    tag = "OK" if (expect is None or code == expect) else "!!"
    print(f"[{tag}] {method} {path} -> {code}")
    if tag == "!!" or expect is None:
        print("     ", json.dumps(parsed)[:600])
    return code, parsed

def auth(user):
    _, p = call("POST", "/api/authenticate", body={"username": user, "password": "Pass@word1"})
    return p["token"]

CARD = {"number": "4111111111111111", "expiryMonth": 12, "expiryYear": 2030,
        "securityCode": "123", "name": "John Doe",
        "addressLine1": "1 Market St", "city": "San Jose", "state": "CA",
        "postalCode": "95131", "countryCode": "US"}

def main():
    demo = auth("demouser@microsoft.com")
    admin = auth("admin@microsoft.com")

    print("\n== FLOW 1: pay an order ==")
    _, o = call("POST", "/api/orders", demo, {"items": [{"catalogItemId": 5, "quantity": 2}]}, expect=201)
    oid = o["orderId"]; print("   orderId:", oid, "total:", o["order"]["total"], "status:", o["order"]["status"])

    code, pay = call("POST", f"/api/orders/{oid}/pay", demo, {"card": CARD})
    if code != 200:
        print("   >> Card 4111 failed; retrying with PayPal-documented sandbox card 4032030000000000")
        CARD2 = dict(CARD); CARD2["number"] = "4032030000000000"
        code, pay = call("POST", f"/api/orders/{oid}/pay", demo, {"card": CARD2}, expect=200)
        globals()["CARD"] = CARD2
    p = pay["order"]["payment"]
    print("   auth status:", pay["order"]["status"], "authId:", p.get("authorizationId"), "amount:", p.get("amount"))

    # idempotent double-pay
    call("POST", f"/api/orders/{oid}/pay", demo, {"card": CARD}, expect=200)

    call("GET", "/api/my-orders", demo, expect=200)

    print("\n== fulfil (admin) → capture ==")
    _, ff = call("POST", f"/api/orders/{oid}/fulfil", admin, expect=200)
    p = ff["order"]["payment"]
    print("   status:", ff["order"]["status"], "captureId:", p.get("captureId"),
          "gross:", p.get("capturedGross"), "fee:", p.get("payPalFee"), "net:", p.get("netProceeds"))

    print("\n== partial refund (shopper) ==")
    _, r1 = call("POST", f"/api/orders/{oid}/refunds", demo, {"amount": 5.00, "idempotencyKey": "refund-1"}, expect=201)
    print("   refundId:", r1.get("refundId"), "amount:", r1.get("refund", {}).get("amount"), "status:", r1.get("refund", {}).get("status"))
    # idempotent replay same key
    _, r1b = call("POST", f"/api/orders/{oid}/refunds", demo, {"amount": 5.00, "idempotencyKey": "refund-1"}, expect=201)
    print("   replay same key refundId:", r1b.get("refundId"), "(should equal", r1.get("refundId"), ")")
    # second distinct partial refund
    _, r2 = call("POST", f"/api/orders/{oid}/refunds", demo, {"amount": 3.00, "idempotencyKey": "refund-2"}, expect=201)
    print("   refundId2:", r2.get("refundId"), "orderStatus:", r2.get("order", {}).get("status"),
          "remaining:", r2.get("order", {}).get("payment", {}).get("remainingRefundable"))
    # over-refund guard
    call("POST", f"/api/orders/{oid}/refunds", demo, {"amount": 100.00, "idempotencyKey": "refund-3"}, expect=409)

    print("\n== ownership checks ==")
    call("POST", f"/api/orders/{oid}/fulfil", demo, expect=403)   # shopper can't fulfil
    _, ao = call("POST", "/api/orders", admin, {"items": [{"catalogItemId": 3, "quantity": 1}]}, expect=201)
    call("POST", f"/api/orders/{ao['orderId']}/pay", demo, {"card": CARD}, expect=404)  # not demo's order

    print("\n== FLOW 2: saved cards ==")
    _, sc = call("POST", "/api/payment-methods", demo, {"card": CARD, "alias": "My Visa"}, expect=201)
    pmid = sc["paymentMethodId"]
    print("   paymentMethodId:", pmid, "brand:", sc["paymentMethod"]["cardBrand"],
          "last4:", sc["paymentMethod"]["last4"], "expiry:", sc["paymentMethod"]["expiry"])
    call("GET", "/api/payment-methods", demo, expect=200)

    print("\n== pay a 2nd order with the saved card ==")
    _, o2 = call("POST", "/api/orders", demo, {"items": [{"catalogItemId": 4, "quantity": 1}]}, expect=201)
    oid2 = o2["orderId"]
    _, pay2 = call("POST", f"/api/orders/{oid2}/pay", demo, {"savedPaymentMethodId": pmid}, expect=200)
    print("   order2 status:", pay2["order"]["status"], "authId:", pay2["order"]["payment"].get("authorizationId"))
    call("POST", f"/api/orders/{oid2}/fulfil", admin, expect=200)

    print("\n== delete saved card, then it can't be used ==")
    call("DELETE", f"/api/payment-methods/{pmid}", demo, expect=200)
    call("GET", "/api/payment-methods", demo, expect=200)
    _, o3 = call("POST", "/api/orders", demo, {"items": [{"catalogItemId": 5, "quantity": 1}]}, expect=201)
    call("POST", f"/api/orders/{o3['orderId']}/pay", demo, {"savedPaymentMethodId": pmid}, expect=404)

    print("\n== cancel-before-fulfil (void) ==")
    _, o4 = call("POST", "/api/orders", demo, {"items": [{"catalogItemId": 5, "quantity": 1}]}, expect=201)
    call("POST", f"/api/orders/{o4['orderId']}/pay", demo, {"card": CARD}, expect=200)
    _, cc = call("POST", f"/api/orders/{o4['orderId']}/cancel", admin, expect=200)
    print("   cancelled status:", cc["order"]["status"], "authStatus:", cc["order"]["payment"].get("authorizationStatus"))

    print("\n== reconciliation (admin) ==")
    frm = (datetime.datetime.utcnow() - datetime.timedelta(days=20)).strftime("%Y-%m-%dT%H:%M:%SZ")
    to = datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")
    _, rec = call("GET", f"/api/reconciliation?from={frm}&to={to}", admin, expect=200)
    print("   matched:", rec["matchedCount"], "inPayPalNotEShop:", rec["inPayPalNotInEShopCount"],
          "inEShopNotPayPal:", rec["inEShopNotInPayPalCount"])
    print("   (empty/partial is expected in sandbox due to ~3h reporting lag)")

    print("\nDONE.")

if __name__ == "__main__":
    main()
