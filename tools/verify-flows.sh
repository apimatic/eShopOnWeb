#!/usr/bin/env bash
# End-to-end verification of the PayPal integration against the sandbox, through PublicApi alone.
# Requires: the PublicApi running on https://localhost:9843 (in-memory DB), python on PATH.
set -uo pipefail
B=${BASE_URL:-https://localhost:9843}
CURL="curl -sk"
jq_get() { python -c "import sys,json;d=json.load(sys.stdin);print(d$1)"; }
line() { echo "-----------------------------------------------------------------"; }

echo "## Authenticate"
ST=$($CURL -X POST "$B/api/authenticate" -H "Content-Type: application/json" -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq_get "['token']")
AT=$($CURL -X POST "$B/api/authenticate" -H "Content-Type: application/json" -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq_get "['token']")
SH="Authorization: Bearer $ST"; AD="Authorization: Bearer $AT"
echo "shopper token len ${#ST}, admin token len ${#AT}"

CARD='{"number":"4111111111111111","expiryMonth":12,"expiryYear":2030,"securityCode":"123","cardholderName":"Demo Shopper","billingLine1":"1 Market St","billingCity":"San Francisco","billingState":"CA","billingCountryCode":"US","billingPostalCode":"94105"}'

line; echo "## Flow 1: place -> pay -> fulfil -> refund"
O1=$($CURL -X POST "$B/api/orders" -H "$SH" -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}')
OID=$(echo "$O1" | jq_get "['orderId']"); echo "placed order $OID total=$(echo "$O1" | jq_get "['order']['total']")"

echo "== pay (authorize/hold) =="
$CURL -X POST "$B/api/orders/$OID/pay" -H "$SH" -H "Content-Type: application/json" -d "{\"card\":$CARD}" | python -m json.tool

echo "== pay again (idempotency: must NOT double-authorize) =="
$CURL -X POST "$B/api/orders/$OID/pay" -H "$SH" -H "Content-Type: application/json" -d "{\"card\":$CARD}" | jq_get "['order']['payment']['status']"

echo "== fulfil (capture) [admin] =="
$CURL -X POST "$B/api/orders/$OID/fulfil" -H "$AD" | python -m json.tool

echo "== partial refund [shopper, idempotency key] =="
RK="refund-$OID-$(date +%s)"
$CURL -X POST "$B/api/orders/$OID/refunds" -H "$SH" -H "Content-Type: application/json" -d "{\"amount\":10.00,\"idempotencyKey\":\"$RK\"}" | python -m json.tool
echo "== repeat same refund key (must return same refund, not a second) =="
$CURL -X POST "$B/api/orders/$OID/refunds" -H "$SH" -H "Content-Type: application/json" -d "{\"amount\":10.00,\"idempotencyKey\":\"$RK\"}" | jq_get "['refundId']"

line; echo "## Flow 1b: place -> pay -> cancel (void, no money moves)"
O2=$($CURL -X POST "$B/api/orders" -H "$SH" -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":3,"quantity":1}]}')
OID2=$(echo "$O2" | jq_get "['orderId']"); echo "placed order $OID2"
$CURL -X POST "$B/api/orders/$OID2/pay" -H "$SH" -H "Content-Type: application/json" -d "{\"card\":$CARD}" | jq_get "['order']['payment']['status']"
echo "== cancel [admin] =="
$CURL -X POST "$B/api/orders/$OID2/cancel" -H "$AD" | jq_get "['order']['payment']['status']"

line; echo "## Flow 2: save card -> reuse to pay a second order -> list -> delete"
PM=$($CURL -X POST "$B/api/payment-methods" -H "$SH" -H "Content-Type: application/json" -d "{\"card\":$CARD,\"alias\":\"my visa\"}")
echo "$PM" | python -m json.tool
PMID=$(echo "$PM" | jq_get "['paymentMethodId']")
echo "== list saved cards =="
$CURL "$B/api/payment-methods" -H "$SH" | python -m json.tool
echo "== place + pay a second order using saved card $PMID =="
O3=$($CURL -X POST "$B/api/orders" -H "$SH" -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":4,"quantity":1}]}')
OID3=$(echo "$O3" | jq_get "['orderId']"); echo "placed order $OID3"
$CURL -X POST "$B/api/orders/$OID3/pay" -H "$SH" -H "Content-Type: application/json" -d "{\"savedPaymentMethodId\":$PMID}" | jq_get "['order']['payment']['status']"
echo "== delete saved card =="
$CURL -o /dev/null -w "delete status: %{http_code}\n" -X DELETE "$B/api/payment-methods/$PMID" -H "$SH"
$CURL "$B/api/payment-methods" -H "$SH" | python -m json.tool

line; echo "## my-orders (shopper)"
$CURL "$B/api/my-orders" -H "$SH" | python -c "import sys,json;d=json.load(sys.stdin);[print(o['orderId'],o['status'],(o.get('payment') or {}).get('status')) for o in d['orders']]"

line; echo "## reconciliation (admin) over a wide range"
FROM="2026-08-01T00:00:00Z"; TO="2026-08-31T23:59:59Z"
$CURL "$B/api/reconciliation?from=$FROM&to=$TO" -H "$AD" | python -c "import sys,json;d=json.load(sys.stdin);print('paypalTxns',d['payPalTransactionCount'],'matched',d['matchedCount'],'paypalOnly',d['payPalOnlyCount'],'eshopOnly',d['eShopOnlyCount'])"
echo "DONE"
