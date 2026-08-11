#!/usr/bin/env bash
# End-to-end verification of the PayPal integration against the PublicApi host.
# Drives every flow through the API alone with the sandbox test card 4111 1111 1111 1111.
# Requires: a running PublicApi (see run steps), curl, jq. Uses -k to accept the dev cert.
set -euo pipefail

BASE="${BASE:-https://localhost:8403}"
CURL="curl -sk"
PASS="Pass@word1"

jqr() { jq -r "$1"; }

echo "== Authenticate demouser (shopper) and admin (operator) =="
SHOPPER_TOKEN=$($CURL -X POST "$BASE/api/authenticate" -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"'"$PASS"'"}' | jqr '.token')
ADMIN_TOKEN=$($CURL -X POST "$BASE/api/authenticate" -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"'"$PASS"'"}' | jqr '.token')
echo "shopper token: ${SHOPPER_TOKEN:0:16}...  admin token: ${ADMIN_TOKEN:0:16}..."

SH_AUTH=(-H "Authorization: Bearer $SHOPPER_TOKEN")
AD_AUTH=(-H "Authorization: Bearer $ADMIN_TOKEN")
JSON=(-H 'Content-Type: application/json')

CARD='{"number":"4111111111111111","expiryMonth":"12","expiryYear":"2030","securityCode":"123","cardholderName":"Jane Buyer","billingAddress":{"addressLine1":"1 Market St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}'

echo; echo "== Flow 2: save a card =="
SAVE=$($CURL -X POST "$BASE/api/payment-methods" "${SH_AUTH[@]}" "${JSON[@]}" -d '{"card":'"$CARD"',"label":"My Visa"}')
echo "$SAVE" | jq .
PM_ID=$(echo "$SAVE" | jqr '.paymentMethodId')
echo "saved paymentMethodId=$PM_ID"

echo; echo "== List saved cards =="
$CURL "$BASE/api/payment-methods" "${SH_AUTH[@]}" | jq .

echo; echo "== Flow 1: place order #1 =="
ORD1=$($CURL -X POST "$BASE/api/orders" "${SH_AUTH[@]}" "${JSON[@]}" \
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}')
echo "$ORD1" | jq .
OID1=$(echo "$ORD1" | jqr '.orderId')
echo "orderId #1 = $OID1"

echo; echo "== Pay order #1 with one-off card (authorize / hold) =="
$CURL -X POST "$BASE/api/orders/$OID1/pay" "${SH_AUTH[@]}" "${JSON[@]}" -d '{"card":'"$CARD"'}' | jq .

echo; echo "== Double-click pay again (must be idempotent, same authorization) =="
$CURL -X POST "$BASE/api/orders/$OID1/pay" "${SH_AUTH[@]}" "${JSON[@]}" -d '{"card":'"$CARD"'}' | jq '.payment.authorizationId, .status'

echo; echo "== Operator fulfils order #1 (capture — money taken; fee/net shown) =="
$CURL -X POST "$BASE/api/orders/$OID1/fulfil" "${AD_AUTH[@]}" | jq '.status, .payment.captureId, .payment.capturedAmount, .payment.payPalFee, .payment.netAmount'

echo; echo "== Partial refund on order #1 (idempotency key k1) =="
$CURL -X POST "$BASE/api/orders/$OID1/refunds" "${SH_AUTH[@]}" "${JSON[@]}" -d '{"amount":10.00,"idempotencyKey":"k1"}' | jq .
echo "-- repeat same key k1 (must NOT refund twice) --"
$CURL -X POST "$BASE/api/orders/$OID1/refunds" "${SH_AUTH[@]}" "${JSON[@]}" -d '{"amount":10.00,"idempotencyKey":"k1"}' | jq .

echo; echo "== Flow 2 reuse: place order #2, pay with SAVED card =="
ORD2=$($CURL -X POST "$BASE/api/orders" "${SH_AUTH[@]}" "${JSON[@]}" -d '{"items":[{"catalogItemId":3,"quantity":1}]}')
OID2=$(echo "$ORD2" | jqr '.orderId')
echo "orderId #2 = $OID2"
$CURL -X POST "$BASE/api/orders/$OID2/pay" "${SH_AUTH[@]}" "${JSON[@]}" -d '{"savedCardId":'"$PM_ID"'}' | jq '.status, .payment.authorizationId'
echo "-- operator fulfils order #2 --"
$CURL -X POST "$BASE/api/orders/$OID2/fulfil" "${AD_AUTH[@]}" | jq '.status, .payment.captureId'

echo; echo "== Place order #3, pay, then operator CANCEL (void hold, no money moves) =="
ORD3=$($CURL -X POST "$BASE/api/orders" "${SH_AUTH[@]}" "${JSON[@]}" -d '{"items":[{"catalogItemId":4,"quantity":1}]}')
OID3=$(echo "$ORD3" | jqr '.orderId')
echo "orderId #3 = $OID3"
$CURL -X POST "$BASE/api/orders/$OID3/pay" "${SH_AUTH[@]}" "${JSON[@]}" -d '{"card":'"$CARD"'}' | jq '.status'
$CURL -X POST "$BASE/api/orders/$OID3/cancel" "${AD_AUTH[@]}" | jq '.status'

echo; echo "== My orders (payment state) =="
$CURL "$BASE/api/my-orders" "${SH_AUTH[@]}" | jq '.orders[] | {orderId, paymentStatus, total}'

echo; echo "== Delete the saved card, then confirm it is gone and unusable =="
$CURL -X DELETE "$BASE/api/payment-methods/$PM_ID" "${SH_AUTH[@]}" -o /dev/null -w "delete status: %{http_code}\n"
$CURL "$BASE/api/payment-methods" "${SH_AUTH[@]}" | jq '.paymentMethods | length'

echo; echo "== Reconciliation (operator), last ~29 days (PayPal caps the range at 31 days) =="
FROM=$(date -u -d '29 days ago' +%Y-%m-%dT%H:%M:%SZ); TO=$(date -u +%Y-%m-%dT%H:%M:%SZ)
$CURL "$BASE/api/reconciliation?from=$FROM&to=$TO" "${AD_AUTH[@]}" | jq '{matched: .matchedCount, payPalOnly: .payPalOnlyCount, eShopOnly: .eShopOnlyCount}'

echo; echo "== Negative checks =="
echo "-- shopper cannot fulfil (operator only) --"
$CURL -X POST "$BASE/api/orders/$OID1/fulfil" "${SH_AUTH[@]}" -o /dev/null -w "shopper fulfil status: %{http_code}\n"
echo "-- unauthenticated pay is rejected --"
$CURL -X POST "$BASE/api/orders/$OID1/pay" "${JSON[@]}" -d '{"card":'"$CARD"'}' -o /dev/null -w "anon pay status: %{http_code}\n"

echo; echo "DONE."
