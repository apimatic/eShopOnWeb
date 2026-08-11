"""
End-to-end verification of the eShopOnWeb PayPal payments integration against the PayPal sandbox.

Drives everything through the PublicApi HTTP surface alone:
  Flow 1 (pay an order): create -> pay(authorize) -> fulfil(capture) -> refund; plus cancel.
  Flow 2 (saved cards): save a card -> reuse it to pay a second order -> list -> delete.
  Plus my-orders and the reconciliation report.

Usage:  python verify_paypal_flow.py [base_url]
        base_url defaults to https://localhost:8623
"""
import json
import ssl
import sys
import urllib.request
import urllib.error

BASE = (sys.argv[1] if len(sys.argv) > 1 else "https://localhost:8623").rstrip("/")
API = BASE + "/api"
CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE

TEST_CARD = {
    "number": "4111111111111111",
    "expiry": "2027-05",
    "securityCode": "123",
    "cardholderName": "Demo Shopper",
    "billingAddress": {
        "countryCode": "US",
        "addressLine1": "1 Market St",
        "adminArea2": "San Jose",
        "adminArea1": "CA",
        "postalCode": "95131",
    },
}

PASS = 0
FAIL = 0


def call(method, path, token=None, body=None, expect=None):
    url = API + path
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Accept", "application/json")
    if data is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, context=CTX) as resp:
            raw = resp.read().decode()
            status = resp.status
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        status = e.code
    try:
        parsed = json.loads(raw) if raw else {}
    except json.JSONDecodeError:
        parsed = {"raw": raw}
    if expect is not None:
        ok = status == expect
        mark = "OK " if ok else "!! "
        global PASS, FAIL
        if ok:
            PASS += 1
        else:
            FAIL += 1
        print(f"  {mark}{method} {path} -> {status} (expected {expect})")
        if not ok:
            print("     body:", json.dumps(parsed)[:500])
    return status, parsed


def check(label, condition, detail=""):
    global PASS, FAIL
    if condition:
        PASS += 1
        print(f"  OK  {label} {detail}")
    else:
        FAIL += 1
        print(f"  !!  {label} FAILED {detail}")


def token_for(user):
    _, body = call("POST", "/authenticate", body={"username": user, "password": "Pass@word1"}, expect=200)
    assert body.get("result"), f"auth failed for {user}: {body}"
    return body["token"]


def money(v):
    return None if v is None else round(float(v), 2)


def main():
    print("== Authenticate ==")
    shopper = token_for("demouser@microsoft.com")
    admin = token_for("admin@microsoft.com")

    # ---------------- Flow 1: pay an order ----------------
    print("\n== Flow 1: create -> pay -> fulfil -> refund ==")
    _, o1 = call("POST", "/orders", shopper,
                 {"items": [{"catalogItemId": 1, "quantity": 1}, {"catalogItemId": 2, "quantity": 2}]}, expect=201)
    order_id = o1["orderId"]
    total = money(o1["total"])
    print(f"     order {order_id}, total {total} {o1['currency']}, status {o1['status']}")
    check("order starts awaiting payment", o1["status"] == "AwaitingPayment")

    # pay (authorize) with the sandbox card
    _, p1 = call("POST", f"/orders/{order_id}/pay", shopper, {"card": TEST_CARD}, expect=200)
    pay = p1["order"]["payment"]
    print(f"     authorization {pay['authorizationId']} status {pay['authorizationStatus']} amount held {money(pay['amount'])}")
    check("order authorized", p1["status"] == "Authorized")
    check("hold equals order total to the cent", money(pay["amount"]) == total)
    check("authorization id present", bool(pay["authorizationId"]))
    check("not captured yet", pay["captureId"] is None)

    # idempotency: paying again must not create a second authorization
    _, p1b = call("POST", f"/orders/{order_id}/pay", shopper, {"card": TEST_CARD}, expect=200)
    check("re-pay is idempotent (same authorization id)",
          p1b["order"]["payment"]["authorizationId"] == pay["authorizationId"])

    # fulfil (capture) as operator
    _, f1 = call("POST", f"/orders/{order_id}/fulfil", admin, expect=200)
    cap = f1["order"]["payment"]
    print(f"     capture {cap['captureId']} status {cap['captureStatus']} gross {money(cap['capturedGross'])} "
          f"fee {money(cap['payPalFee'])} net {money(cap['netAmount'])}")
    check("order fulfilled", f1["status"] == "Fulfilled")
    check("captured gross equals total", money(cap["capturedGross"]) == total)
    check("capture id present", bool(cap["captureId"]))
    check("fee reported", cap["payPalFee"] is not None)
    check("net proceeds reported", cap["netAmount"] is not None)
    check("net = gross - fee",
          cap["netAmount"] is None or cap["payPalFee"] is None or
          money(cap["netAmount"]) == round(money(cap["capturedGross"]) - money(cap["payPalFee"]), 2))

    # shopper cannot fulfil (operator only)
    call("POST", f"/orders/{order_id}/fulfil", shopper, expect=403)

    # partial refund with an idempotency key
    refund_amt = round(total / 2, 2)
    _, r1 = call("POST", f"/orders/{order_id}/refunds", shopper,
                 {"amount": refund_amt, "idempotencyKey": "refund-key-A"}, expect=200)
    print(f"     refund {r1['refundId']} amount {money(r1['amount'])} status {r1['status']} "
          f"totalRefunded {money(r1['totalRefunded'])}")
    check("refund id present", bool(r1["refundId"]))
    check("order partially refunded", r1["orderStatus"] == "PartiallyRefunded")

    # replay same key -> same refund, no double refund
    _, r1b = call("POST", f"/orders/{order_id}/refunds", shopper,
                  {"amount": refund_amt, "idempotencyKey": "refund-key-A"}, expect=200)
    check("repeat refund key does not refund twice", r1b["refundId"] == r1["refundId"])
    check("total refunded unchanged on replay", money(r1b["totalRefunded"]) == money(r1["totalRefunded"]))

    # refund beyond remaining must be rejected
    _, rover = call("POST", f"/orders/{order_id}/refunds", shopper,
                    {"amount": total, "idempotencyKey": "refund-key-over"}, expect=422)
    check("over-refund rejected (never refundable beyond captured)", True)

    # a second, distinct partial refund of the remainder is legitimate
    _, r2 = call("POST", f"/orders/{order_id}/refunds", shopper,
                 {"idempotencyKey": "refund-key-B"}, expect=200)
    print(f"     refund {r2['refundId']} amount {money(r2['amount'])} totalRefunded {money(r2['totalRefunded'])}")
    check("second distinct refund allowed", r2["refundId"] != r1["refundId"])
    check("order now fully refunded", r2["orderStatus"] == "Refunded")
    check("total refunded equals captured", money(r2["totalRefunded"]) == total)

    # ---------------- cancel flow (before fulfilment) ----------------
    print("\n== Cancel before fulfilment (funds released) ==")
    _, oc = call("POST", "/orders", shopper, {"items": [{"catalogItemId": 3, "quantity": 1}]}, expect=201)
    cancel_order = oc["orderId"]
    call("POST", f"/orders/{cancel_order}/pay", shopper, {"card": TEST_CARD}, expect=200)
    _, cc = call("POST", f"/orders/{cancel_order}/cancel", admin, expect=200)
    check("order cancelled", cc["status"] == "Cancelled")
    check("authorization voided", cc["order"]["payment"]["authorizationStatus"] == "VOIDED")
    # cannot refund a cancelled (never captured) order — state conflict
    call("POST", f"/orders/{cancel_order}/refunds", shopper,
         {"idempotencyKey": "x"}, expect=409)

    # ---------------- Flow 2: saved cards ----------------
    print("\n== Flow 2: save a card, reuse it to pay a second order ==")
    _, sc = call("POST", "/payment-methods", shopper, {"card": TEST_CARD, "label": "My Visa"}, expect=201)
    pm_id = sc["paymentMethodId"]
    print(f"     saved card {pm_id}: {sc['cardBrand']} ****{sc['cardLast4']} exp {sc['expiry']}")
    check("saved card id present", bool(pm_id))
    check("safe descriptor only (last4, no PAN)", sc.get("cardLast4") == "1111" and "number" not in sc)

    _, lst = call("GET", "/payment-methods", shopper, expect=200)
    check("saved card appears in list", any(m["paymentMethodId"] == pm_id for m in lst["paymentMethods"]))

    _, o2 = call("POST", "/orders", shopper, {"items": [{"catalogItemId": 4, "quantity": 1}]}, expect=201)
    order2 = o2["orderId"]
    _, p2 = call("POST", f"/orders/{order2}/pay", shopper, {"savedPaymentMethodId": pm_id}, expect=200)
    check("second order authorized with saved card", p2["status"] == "Authorized")
    print(f"     order {order2} paid with saved card -> {p2['order']['payment']['savedCardDescriptor']}")
    _, f2 = call("POST", f"/orders/{order2}/fulfil", admin, expect=200)
    check("second order fulfilled", f2["status"] == "Fulfilled")

    # another shopper's saved card is invisible/unusable
    other = token_for("admin@microsoft.com")  # different identity than demouser
    call("DELETE", f"/payment-methods/{pm_id}", other, expect=403)

    # delete the saved card; it must no longer appear or be usable
    call("DELETE", f"/payment-methods/{pm_id}", shopper, expect=204)
    _, lst2 = call("GET", "/payment-methods", shopper, expect=200)
    check("deleted card no longer listed", all(m["paymentMethodId"] != pm_id for m in lst2["paymentMethods"]))
    _, o3 = call("POST", "/orders", shopper, {"items": [{"catalogItemId": 5, "quantity": 1}]}, expect=201)
    call("POST", f"/orders/{o3['orderId']}/pay", shopper, {"savedPaymentMethodId": pm_id}, expect=403)

    # ---------------- my-orders (own data only) ----------------
    print("\n== my-orders ==")
    _, mine = call("GET", "/my-orders", shopper, expect=200)
    ids = [o["orderId"] for o in mine["orders"]]
    check("my-orders returns the shopper's orders", order_id in ids and order2 in ids)
    check("my-orders carries payment state", all("payment" in o for o in mine["orders"]))
    # cross-shopper isolation: admin (different identity) cannot pay demouser's order
    call("POST", f"/orders/{order_id}/pay", admin, {"card": TEST_CARD}, expect=403)

    # ---------------- reconciliation (operator) ----------------
    print("\n== reconciliation (operator) ==")
    import datetime
    now = datetime.datetime.now(datetime.timezone.utc)
    frm = (now - datetime.timedelta(days=30)).strftime("%Y-%m-%dT%H:%M:%SZ")
    to = now.strftime("%Y-%m-%dT%H:%M:%SZ")
    st, rec = call("GET", f"/reconciliation?from={frm}&to={to}", admin, expect=200)
    check("reconciliation report returned", "matched" in rec and "inPayPalNotInEShop" in rec and "inEShopNotInPayPal" in rec)
    print(f"     range {frm}..{to}: payPalTransactionCount={rec['payPalTransactionCount']}, "
          f"matched={len(rec['matched'])}, inEShopNotInPayPal={len(rec['inEShopNotInPayPal'])}")
    print("     (PayPal reporting lags, so just-created captures may show under inEShopNotInPayPal — expected.)")
    check("captured orders appear on the eShop side of the report",
          any(u["eShopOrderId"] in (order_id, order2) for u in rec["inEShopNotInPayPal"]) or len(rec["matched"]) > 0)
    # shopper cannot run reconciliation
    call("GET", f"/reconciliation?from={frm}&to={to}", shopper, expect=403)

    # a wider (>31 day) range must also be accepted: the client date-chunks internally
    frm2 = (now - datetime.timedelta(days=90)).strftime("%Y-%m-%dT%H:%M:%SZ")
    call("GET", f"/reconciliation?from={frm2}&to={to}", admin, expect=200)

    print(f"\n==== RESULT: {PASS} checks passed, {FAIL} failed ====")
    sys.exit(1 if FAIL else 0)


if __name__ == "__main__":
    main()
