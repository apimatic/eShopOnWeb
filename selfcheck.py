import json, ssl, urllib.request, datetime, sys, random

BASE = "https://localhost:8403"
ctx = ssl.create_default_context(); ctx.check_hostname=False; ctx.verify_mode=ssl.CERT_NONE
PASS = "Pass@word1"
CARD = {"number":"4111111111111111","expiryMonth":"12","expiryYear":"2030","securityCode":"123",
        "cardholderName":"Jane Buyer",
        "billingAddress":{"addressLine1":"1 Market St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}

def call(method, path, token=None, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE+path, data=data, method=method)
    req.add_header("Content-Type","application/json")
    if token: req.add_header("Authorization","Bearer "+token)
    try:
        with urllib.request.urlopen(req, context=ctx) as r:
            txt = r.read().decode()
            return r.status, (json.loads(txt) if txt else None)
    except urllib.error.HTTPError as e:
        txt = e.read().decode()
        try: parsed = json.loads(txt)
        except: parsed = txt
        return e.code, parsed

def show(label, status, body, *keys):
    if keys and isinstance(body, dict):
        body = {k: dig(body,k) for k in keys}
    print(f"[{status}] {label}: {json.dumps(body, default=str)}")
    return body

def dig(d, path):
    cur=d
    for p in path.split('.'):
        if isinstance(cur, dict): cur=cur.get(p)
        else: return None
    return cur

RUN = str(random.randint(100000,999999))
KEY1 = "k1-"+RUN
KEY2 = "k-too-much-"+RUN
results = {"ok":0,"fail":0}
def check(cond, msg):
    print(("  PASS " if cond else "  FAIL ")+msg)
    results["ok" if cond else "fail"] += 1

print("== auth ==")
s, b = call("POST","/api/authenticate", body={"username":"demouser@microsoft.com","password":PASS})
shopper = b["token"]; check(bool(shopper), "shopper token")
s, b = call("POST","/api/authenticate", body={"username":"admin@microsoft.com","password":PASS})
admin = b["token"]; check(bool(admin), "admin token")

print("\n== Flow 2: save card ==")
s, b = call("POST","/api/payment-methods", shopper, {"card":CARD,"label":"My Visa"})
show("save card", s, b)
check(s==201 and isinstance(b.get("paymentMethodId"),int), "paymentMethodId returned as top-level int")
check("4111" not in json.dumps(b), "no full PAN in response")
pm_id = b["paymentMethodId"]; check(b.get("last4")=="1111", "last4 shown")

s, b = call("GET","/api/payment-methods", shopper)
check(any(c["paymentMethodId"]==pm_id for c in b["paymentMethods"]), "saved card listed")

print("\n== Flow 1: place order #1 ==")
s, b = call("POST","/api/orders", shopper, {"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]})
show("place order", s, b, "orderId","total","status")
check(s==201 and isinstance(b.get("orderId"),int), "orderId returned as top-level int")
oid1 = b["orderId"]; total1 = b["total"]
check(b["status"]=="AwaitingPayment", "order starts awaiting payment")
check(abs(total1 - (19.5*2+8.5)) < 0.001, f"total from catalog prices = {total1}")

print("\n== pay order #1 with one-off card (authorize/hold) ==")
s, b = call("POST",f"/api/orders/{oid1}/pay", shopper, {"card":CARD})
show("pay", s, b, "status","payment.authorizationId","payment.paymentStatus","payment.amount")
if s != 200:
    print("  !! PAY FAILED — full body:", json.dumps(b));
check(s==200, "pay returns 200")
if s==200:
    auth1 = dig(b,"payment.authorizationId")
    check(bool(auth1), "authorization id present (hold placed)")
    check(dig(b,"payment.paymentStatus")=="Authorized", "payment status Authorized")
    check(abs(dig(b,"payment.amount") - total1) < 0.001, "hold equals order total to the cent")

print("\n== double-click pay (idempotent) ==")
s, b2 = call("POST",f"/api/orders/{oid1}/pay", shopper, {"card":CARD})
check(s==200 and dig(b2,"payment.authorizationId")==dig(b,"payment.authorizationId"), "same authorization id on repeat")

print("\n== operator fulfils order #1 (capture) ==")
s, b = call("POST",f"/api/orders/{oid1}/fulfil", admin)
show("fulfil", s, b, "status","payment.captureId","payment.capturedAmount","payment.payPalFee","payment.netAmount")
check(s==200 and dig(b,"status")=="Fulfilled", "order fulfilled")
check(bool(dig(b,"payment.captureId")), "capture id present (money taken)")
cap_amt = dig(b,"payment.capturedAmount"); fee = dig(b,"payment.payPalFee"); net = dig(b,"payment.netAmount")
check(cap_amt is not None and fee is not None and net is not None, "captured amount, fee, net all reported")
if cap_amt is not None and fee is not None and net is not None:
    check(abs(cap_amt - fee - net) < 0.001, f"gross({cap_amt}) - fee({fee}) = net({net})")

print("\n== partial refund order #1, idempotency key k1 ==")
s, b = call("POST",f"/api/orders/{oid1}/refunds", shopper, {"amount":10.00,"idempotencyKey":KEY1})
show("refund k1", s, b)
check(s==201 and isinstance(b.get("refundId"),str) and b.get("refundId"), "refundId returned as top-level field")
rid1 = b.get("refundId")
s, b = call("POST",f"/api/orders/{oid1}/refunds", shopper, {"amount":10.00,"idempotencyKey":KEY1})
check(s in (200,201) and b.get("refundId")==rid1, "repeat under k1 does not refund twice (same refundId)")

print("\n== over-refund guard (remaining after 10 of "+str(cap_amt)+") ==")
s, b = call("POST",f"/api/orders/{oid1}/refunds", shopper, {"amount":9999.00,"idempotencyKey":KEY2})
check(s==409, f"over-capture refund rejected ({s})")

print("\n== Flow 2 reuse: order #2 paid with SAVED card ==")
s, b = call("POST","/api/orders", shopper, {"items":[{"catalogItemId":3,"quantity":1}]})
oid2 = b["orderId"]
s, b = call("POST",f"/api/orders/{oid2}/pay", shopper, {"savedCardId":pm_id})
show("pay#2 saved card", s, b, "status","payment.authorizationId")
check(s==200 and dig(b,"payment.paymentStatus")=="Authorized", "order #2 authorized with saved card")
s, b = call("POST",f"/api/orders/{oid2}/fulfil", admin)
check(s==200 and dig(b,"status")=="Fulfilled", "order #2 fulfilled")

print("\n== order #3: pay then operator CANCEL (void hold) ==")
s, b = call("POST","/api/orders", shopper, {"items":[{"catalogItemId":4,"quantity":1}]})
oid3 = b["orderId"]
s, b = call("POST",f"/api/orders/{oid3}/pay", shopper, {"card":CARD})
check(s==200, "order #3 authorized")
s, b = call("POST",f"/api/orders/{oid3}/cancel", admin)
show("cancel #3", s, b, "status")
check(s==200 and dig(b,"status")=="Cancelled", "order #3 cancelled (funds released)")

print("\n== my-orders ==")
s, b = call("GET","/api/my-orders", shopper)
statuses = {o["orderId"]:o["paymentStatus"] for o in b["orders"]}
print("  statuses:", statuses)
check(statuses.get(oid1) in ("PartiallyRefunded",), "order #1 partially refunded")
check(statuses.get(oid2)=="Fulfilled", "order #2 fulfilled")
check(statuses.get(oid3)=="Cancelled", "order #3 cancelled")

print("\n== ownership isolation: second shopper cannot see/act ==")
# register-less: use admin token as 'another identity' to try to pay shopper's order -> should 404 (not admin's order)
s, b = call("POST",f"/api/orders/{oid1}/refunds", admin, {"amount":1.0,"idempotencyKey":"x"})
check(s==404, f"another identity cannot refund shopper's order ({s})")

print("\n== delete saved card, confirm gone & unusable ==")
s, b = call("DELETE",f"/api/payment-methods/{pm_id}", shopper)
check(s==204, f"delete returns 204 ({s})")
s, b = call("GET","/api/payment-methods", shopper)
check(all(c["paymentMethodId"]!=pm_id for c in b["paymentMethods"]), "deleted card no longer listed")
s, b = call("POST","/api/orders", shopper, {"items":[{"catalogItemId":1,"quantity":1}]})
oid4=b["orderId"]
s, b = call("POST",f"/api/orders/{oid4}/pay", shopper, {"savedCardId":pm_id})
check(s in (400,404), f"deleted card no longer usable to pay ({s})")

print("\n== authorization / role checks ==")
s, b = call("POST",f"/api/orders/{oid1}/fulfil", shopper)
check(s==403, f"shopper cannot fulfil (operator only) -> {s}")
s, b = call("POST",f"/api/orders/{oid1}/pay", None, {"card":CARD})
check(s==401, f"anonymous pay rejected -> {s}")
s, b = call("GET","/api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z", shopper)
check(s==403, f"shopper cannot reconcile -> {s}")

print("\n== reconciliation (operator), last ~29 days ==")
to = datetime.datetime.now(datetime.timezone.utc)
frm = to - datetime.timedelta(days=29)
iso = lambda d: d.strftime("%Y-%m-%dT%H:%M:%SZ")
s, b = call("GET",f"/api/reconciliation?from={iso(frm)}&to={iso(to)}", admin)
if s==200:
    show("reconciliation", s, b, "matchedCount","payPalOnlyCount","eShopOnlyCount")
    check(isinstance(b.get("lines"),list), "report has line list")
    check(any(l.get("orderId")==oid1 for l in b["lines"]), "eShop order #1 appears in reconciliation")
else:
    print("  reconciliation body:", json.dumps(b))
    check(False, f"reconciliation returned {s}")

print(f"\n==== RESULT: {results['ok']} passed, {results['fail']} failed ====")
sys.exit(1 if results["fail"] else 0)
