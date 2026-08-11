#!/usr/bin/env bash
# End-to-end verification of the PayPal payments + saved-cards integration against the running
# PublicApi (in-memory DB) and the PayPal sandbox. Drives the whole surface through the API alone.
#
#   BASE=https://localhost:8883 bash tests/PaymentFlows.verify.sh
#
# Requires: curl, python3. Uses the seeded users demouser@ (shopper) and admin@ (operator).
set -u
B="${BASE:-https://localhost:8883}"
PASS=0; FAIL=0
j() { python -c "import json,sys;d=json.load(sys.stdin);print(eval(sys.argv[1]))" "$1"; }
check() { if [ "$1" = "$2" ]; then echo "  PASS: $3 ($1)"; PASS=$((PASS+1)); else echo "  FAIL: $3 (got '$1' want '$2')"; FAIL=$((FAIL+1)); fi; }
tok() { curl -sk -X POST "$B/api/authenticate" -H 'Content-Type: application/json' -d "{\"username\":\"$1\",\"password\":\"Pass@word1\"}" | j "d['token']"; }

SHOP=$(tok demouser@microsoft.com); ADMIN=$(tok admin@microsoft.com)
CARD='{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","cardholderName":"Demo User","billingAddress":{"addressLine1":"1 Main St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}}'

echo "== Flow 1: pay, fulfil, refund =="
OID=$(curl -sk -X POST "$B/api/orders" -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}' | j "d['orderId']")
PAY=$(curl -sk -X POST "$B/api/orders/$OID/pay" -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d "$CARD")
check "$(echo "$PAY" | j "d['payment']['state']")" "Authorized" "pay authorizes a hold"
A1=$(echo "$PAY" | j "d['payment']['authorizationId']")
A2=$(curl -sk -X POST "$B/api/orders/$OID/pay" -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d "$CARD" | j "d['payment']['authorizationId']")
check "$A1" "$A2" "double pay is idempotent (same authorization)"
FUL=$(curl -sk -X POST "$B/api/orders/$OID/fulfil" -H "Authorization: Bearer $ADMIN")
check "$(echo "$FUL" | j "d['payment']['state']")" "Captured" "fulfil captures the funds"
check "$(echo "$FUL" | j "'yes' if d['payment']['payPalFee'] is not None and d['payment']['netAmount'] is not None else 'no'")" "yes" "capture reports fee and net"
REF=$(curl -sk -X POST "$B/api/orders/$OID/refunds" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' -d '{"amount":10.00,"idempotencyKey":"refA"}')
R1=$(echo "$REF" | j "d['refundId']")
check "$(echo "$REF" | j "d['paymentState']")" "PartiallyRefunded" "partial refund succeeds"
R2=$(curl -sk -X POST "$B/api/orders/$OID/refunds" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' -d '{"amount":10.00,"idempotencyKey":"refA"}' | j "d['refundId']")
check "$R1" "$R2" "same refund key is idempotent (no double refund)"
BIG=$(curl -sk -o /dev/null -w '%{http_code}' -X POST "$B/api/orders/$OID/refunds" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' -d '{"amount":1000,"idempotencyKey":"refBig"}')
check "$BIG" "400" "refund beyond captured is rejected"
R3=$(curl -sk -o /dev/null -w '%{http_code}' -X POST "$B/api/orders/$OID/refunds" -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' -d '{"amount":5.00,"idempotencyKey":"refB"}')
check "$R3" "201" "distinct partial refund (new key) succeeds"
MY=$(curl -sk "$B/api/my-orders" -H "Authorization: Bearer $SHOP" | j "str(len(d['orders']))+':'+d['orders'][0]['payment']['state']")
check "$MY" "1:PartiallyRefunded" "my-orders shows payment state"
check "$(curl -sk "$B/api/my-orders" -H "Authorization: Bearer $ADMIN" | j "len(d['orders'])")" "0" "one shopper cannot see another's orders"

echo "== Cancel before fulfilment (funds released) =="
OB=$(curl -sk -X POST "$B/api/orders" -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | j "d['orderId']")
curl -sk -X POST "$B/api/orders/$OB/pay" -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d "$CARD" > /dev/null
CAN=$(curl -sk -X POST "$B/api/orders/$OB/cancel" -H "Authorization: Bearer $ADMIN")
check "$(echo "$CAN" | j "d['payment']['state']")" "Voided" "cancel voids the hold"
check "$(curl -sk -o /dev/null -w '%{http_code}' -X POST "$B/api/orders/$OB/fulfil" -H "Authorization: Bearer $ADMIN")" "400" "cannot fulfil a cancelled order"

echo "== Flow 2: saved card reused for a second order =="
PMID=$(curl -sk -X POST "$B/api/payment-methods" -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d "$CARD" | j "d['paymentMethodId']")
check "$(curl -sk "$B/api/payment-methods" -H "Authorization: Bearer $SHOP" | j "len(d['paymentMethods'])")" "1" "saved card is listed"
OC=$(curl -sk -X POST "$B/api/orders" -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | j "d['orderId']")
PC=$(curl -sk -X POST "$B/api/orders/$OC/pay" -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d "{\"savedPaymentMethodId\":$PMID}")
check "$(echo "$PC" | j "d['payment']['state']")" "Authorized" "pay a 2nd order with the saved card"
check "$(curl -sk -X POST "$B/api/orders/$OC/fulfil" -H "Authorization: Bearer $ADMIN" | j "d['payment']['state']")" "Captured" "capture the saved-card order"
check "$(curl -sk -o /dev/null -w '%{http_code}' -X DELETE "$B/api/payment-methods/$PMID" -H "Authorization: Bearer $SHOP")" "200" "delete saved card"
check "$(curl -sk "$B/api/payment-methods" -H "Authorization: Bearer $SHOP" | j "len(d['paymentMethods'])")" "0" "deleted card no longer listed"
OD=$(curl -sk -X POST "$B/api/orders" -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d '{"items":[{"catalogItemId":5,"quantity":1}]}' | j "d['orderId']")
check "$(curl -sk -o /dev/null -w '%{http_code}' -X POST "$B/api/orders/$OD/pay" -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d "{\"savedPaymentMethodId\":$PMID}")" "404" "deleted card can no longer pay"

echo "== Reconciliation & authorization =="
check "$(curl -sk -o /dev/null -w '%{http_code}' "$B/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-12T00:00:00Z" -H "Authorization: Bearer $ADMIN")" "200" "reconciliation returns a report (operator)"
check "$(curl -sk -o /dev/null -w '%{http_code}' "$B/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-12T00:00:00Z" -H "Authorization: Bearer $SHOP")" "403" "reconciliation is operator-only"
check "$(curl -sk -o /dev/null -w '%{http_code}' -X POST "$B/api/orders/$OID/fulfil" -H "Authorization: Bearer $SHOP")" "403" "fulfil is operator-only"
check "$(curl -sk -o /dev/null -w '%{http_code}' "$B/api/my-orders")" "401" "unauthenticated request is rejected"

echo ""
echo "RESULT: $PASS passed, $FAIL failed"
[ "$FAIL" = "0" ] && echo "ALL GREEN" || echo "SOME FAILURES"
