#!/usr/bin/env bash
# End-to-end verification of the PayPal integration on PublicApi.
# Requires the API running on https://localhost:10743 with UseOnlyInMemoryDatabase=true.
set -u
B=https://localhost:10743
pass=0; fail=0
chk(){ if [ "$1" = "$2" ]; then echo "  PASS: $3 ($1)"; pass=$((pass+1)); else echo "  FAIL: $3 (got '$1' want '$2')"; fail=$((fail+1)); fi; }
jget(){ python -c "import sys,json;d=json.load(sys.stdin);print(d$1)" 2>/dev/null; }

SHOP=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jget "['token']")
ADMIN=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jget "['token']")
S=(-H "Authorization: Bearer $SHOP" -H "Content-Type: application/json")
A=(-H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json")
echo "tokens: shopper=${#SHOP} admin=${#ADMIN} chars"

CARD='{"card":{"number":"4111111111111111","expiry":"2027-01","securityCode":"123","name":"Demo User","billingAddressLine1":"123 Main St","billingCity":"San Jose","billingState":"CA","billingPostalCode":"95131","billingCountryCode":"US"}}'

pay_retry(){ # orderId body -> echoes status
  local r st
  for a in 1 2 3 4 5; do
    r=$(curl -sk -X POST $B/api/orders/$1/pay "${S[@]}" -d "$2")
    st=$(echo "$r" | jget "['status']")
    [ "$st" = "Authorized" ] && { echo "$st"; return; }
    sleep 1
  done
  echo "ERR:$r"
}

echo "[Flow 1] place -> pay -> fulfil -> refund"
OID=$(curl -sk -X POST $B/api/orders "${S[@]}" -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}' | jget "['orderId']")
echo "  orderId=$OID"
chk "$(pay_retry $OID "$CARD")" "Authorized" "authorize hold"
chk "$(curl -sk -X POST $B/api/orders/$OID/pay "${S[@]}" -d "$CARD" | jget "['status']")" "Authorized" "double-click idempotent (still one hold)"
F=$(curl -sk -X POST $B/api/orders/$OID/fulfil "${A[@]}")
chk "$(echo "$F"|jget "['status']")" "Fulfilled" "fulfil captures"
echo "  $(echo "$F" | python -c "import sys,json;d=json.load(sys.stdin);print('captured',d['capturedAmount'],'fee',d['payPalFee'],'net',d['netAmount'])")"
R1=$(curl -sk -X POST $B/api/orders/$OID/refunds "${S[@]}" -d '{"amount":10.00,"idempotencyKey":"K1"}' | jget "['refundId']")
R1b=$(curl -sk -X POST $B/api/orders/$OID/refunds "${S[@]}" -d '{"amount":10.00,"idempotencyKey":"K1"}' | jget "['refundId']")
chk "$R1" "$R1b" "refund idempotent under same key"
chk "$(curl -sk -o /dev/null -w '%{http_code}' -X POST $B/api/orders/$OID/refunds "${S[@]}" -d '{"amount":5.00,"idempotencyKey":"K2"}')" "201" "distinct partial refund allowed"
chk "$(curl -sk -o /dev/null -w '%{http_code}' -X POST $B/api/orders/$OID/refunds "${S[@]}" -d '{"amount":1000,"idempotencyKey":"K3"}')" "409" "over-refund rejected"

echo "[Flow 2] save card -> reuse -> fulfil -> delete"
PMID=$(curl -sk -X POST $B/api/payment-methods "${S[@]}" -d '{"card":{"number":"4111111111111111","expiry":"2028-05","securityCode":"321","name":"Demo User"}}' | jget "['paymentMethodId']")
chk "$([ -n "$PMID" ] && echo ok)" "ok" "card saved (id=$PMID)"
OID2=$(curl -sk -X POST $B/api/orders "${S[@]}" -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | jget "['orderId']")
chk "$(pay_retry $OID2 "{\"savedPaymentMethodId\":$PMID}")" "Authorized" "pay order2 with saved card"
chk "$(curl -sk -X POST $B/api/orders/$OID2/fulfil "${A[@]}" | jget "['status']")" "Fulfilled" "fulfil order2"
chk "$(curl -sk -o /dev/null -w '%{http_code}' -X DELETE $B/api/payment-methods/$PMID "${S[@]}")" "200" "delete saved card"
chk "$(curl -sk $B/api/payment-methods "${S[@]}" | jget "['paymentMethods'].__len__()")" "0" "card gone from list"
OIDx=$(curl -sk -X POST $B/api/orders "${S[@]}" -d '{"items":[{"catalogItemId":5,"quantity":1}]}' | jget "['orderId']")
chk "$(curl -sk -o /dev/null -w '%{http_code}' -X POST $B/api/orders/$OIDx/pay "${S[@]}" -d "{\"savedPaymentMethodId\":$PMID}")" "404" "deleted card not usable"

echo "[Cancel] place -> pay -> cancel(void)"
OID3=$(curl -sk -X POST $B/api/orders "${S[@]}" -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | jget "['orderId']")
pay_retry $OID3 "$CARD" >/dev/null
chk "$(curl -sk -X POST $B/api/orders/$OID3/cancel "${A[@]}" | jget "['status']")" "Cancelled" "cancel voids hold"
chk "$(curl -sk -o /dev/null -w '%{http_code}' -X POST $B/api/orders/$OID3/fulfil "${A[@]}")" "409" "cannot fulfil a cancelled order"

echo "[AuthZ] operator-only endpoints reject shoppers (403)"
chk "$(curl -sk -o /dev/null -w '%{http_code}' -X POST $B/api/orders/$OID/fulfil "${S[@]}")" "403" "shopper cannot fulfil"
chk "$(curl -sk -o /dev/null -w '%{http_code}' -X POST $B/api/orders/$OID/cancel "${S[@]}")" "403" "shopper cannot cancel"
chk "$(curl -sk -o /dev/null -w '%{http_code}' "$B/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-10T00:00:00Z" "${S[@]}")" "403" "shopper cannot reconcile"

echo "[Scoping] a different signed-in user sees none of the shopper's data"
chk "$(curl -sk $B/api/my-orders "${A[@]}" | jget "['orders'].__len__()")" "0" "admin sees 0 of demouser's orders"

echo "[Reconciliation] whole-range report (chunked + paginated)"
curl -sk "$B/api/reconciliation?from=2026-07-09T00:00:00Z&to=2026-08-17T23:59:59Z" "${A[@]}" | python -c "import sys,json;d=json.load(sys.stdin);print('  payPalTxns',d['payPalTransactionCount'],'matched',d['matchedCount'],'payPalOnly',len(d['inPayPalNotInEShop']),'eShopOnly',len(d['inEShopNotInPayPal']))"

echo ""
echo "RESULT: $pass passed, $fail failed"
