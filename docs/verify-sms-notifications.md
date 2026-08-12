# Verify the SMS order-notification integration

A copy-paste walkthrough that drives every flow end to end through PublicApi. It sends real SMS to
the two designated test numbers only (`TWILIO_TEST_TO_NUMBER`, a deliverable Canadian number, and
`TWILIO_UNREACHABLE_TO_NUMBER`, a reserved US number that no handset receives).

## 0. Load secrets and start PublicApi

```bash
cd <repo>
for pair in "Twilio:AccountSid=TWILIO_ACCOUNT_SID" "Twilio:AuthToken=TWILIO_AUTH_TOKEN" \
            "Twilio:FromNumber=TWILIO_FROM_NUMBER" "Twilio:MessagingServiceSid=TWILIO_MESSAGING_SERVICE_SID"; do
  key="${pair%%=*}"; var="${pair##*=}"
  dotnet user-secrets set "$key" "$(printenv "$var")" --project src/PublicApi/PublicApi.csproj >/dev/null
done

export DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development \
       ASPNETCORE_URLS="https://localhost:9723;http://localhost:9724" UseOnlyInMemoryDatabase=true
dotnet run --project src/PublicApi/PublicApi.csproj      # leave running; use another shell below
```

## 1. Tokens (shopper + operator)

```bash
B=https://localhost:9723
ST=$(curl -sk -X POST $B/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
AT=$(curl -sk -X POST $B/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'   | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
```

## 2. Flow 1 — contact number (validate, store canonical, isolate, delete)

```bash
# Register the deliverable number → 201 { contactNumberId, phoneNumber (provider-canonical E.164) }
curl -sk -X POST $B/api/contact-numbers -H "Authorization: Bearer $ST" -H 'Content-Type: application/json' \
  -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"
# An unusable number is rejected here → 400
curl -sk -X POST $B/api/contact-numbers -H "Authorization: Bearer $ST" -H 'Content-Type: application/json' \
  -d '{"phoneNumber":"not-a-number"}'
curl -sk $B/api/contact-numbers -H "Authorization: Bearer $ST"       # lists only the caller's numbers
```

## 3. Flow 2 — messages as the order moves (deliverable number)

```bash
# Place → real "placed" SMS. Response carries orderId.
O1=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $ST" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")

curl -sk -X POST $B/api/orders/$O1/dispatch -H "Authorization: Bearer $AT"   # dispatched SMS + schedules follow-up
curl -sk $B/api/orders/$O1/notifications -H "Authorization: Bearer $AT"      # follow-up shows status "scheduled"
curl -sk -X POST $B/api/orders/$O1/cancel   -H "Authorization: Bearer $AT"   # cancels follow-up + cancelled SMS
curl -sk $B/api/orders/$O1/notifications -H "Authorization: Bearer $AT"      # follow-up now "canceled" — never sent
```

Expect: `OrderPlaced=delivered`, `OrderDispatched=delivered`, `DeliveryFollowUp=canceled`,
`OrderCancelled=delivered`. A shopper token on `/dispatch` or `/cancel` returns **403**.

## 4. Undeliverable outcome (reserved US number)

```bash
curl -sk -X POST $B/api/contact-numbers -H "Authorization: Bearer $ST" -H 'Content-Type: application/json' \
  -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"                    # now the latest number
O2=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $ST" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
sleep 12
curl -sk $B/api/orders/$O2/notifications -H "Authorization: Bearer $AT"      # status "undelivered" (carrier refused)
```

`undelivered` (e.g. error 30034) is the expected live-account result, not a defect.

## 5. Flow 3 — operator actions

```bash
# Resend (idempotency). Same key → not re-sent; fresh key → new attempt.
curl -sk -X POST $B/api/notifications/5/resend -H "Authorization: Bearer $AT" -H 'Content-Type: application/json' -d '{"idempotencyKey":"k1"}'   # 201 {notificationId:6}
curl -sk -X POST $B/api/notifications/5/resend -H "Authorization: Bearer $AT" -H 'Content-Type: application/json' -d '{"idempotencyKey":"k1"}'   # 200 {notificationId:6, replayed:true}
curl -sk -X POST $B/api/notifications/5/resend -H "Authorization: Bearer $AT" -H 'Content-Type: application/json' -d '{"idempotencyKey":"k2"}'   # 201 {notificationId:7}

# Content disposal — body removed at the provider, record survives.
curl -sk -X DELETE $B/api/notifications/1/content -H "Authorization: Bearer $AT"
# Optional provider check: GET .../Messages/{sid}.json shows body:"" but status unchanged.

# A deleted number is never messaged again.
curl -sk -X DELETE $B/api/contact-numbers/1 -H "Authorization: Bearer $ST"                                     # 204
curl -sk -X POST $B/api/notifications/2/resend -H "Authorization: Bearer $AT" -H 'Content-Type: application/json' -d '{"idempotencyKey":"k3"}'  # 409 ContactRemoved

# Reconciliation (operator's own number only), over a range with data.
curl -sk "$B/api/notifications/reconciliation?from=2026-08-12T00:00:00Z&to=2026-08-12T23:59:59Z" -H "Authorization: Bearer $AT"

# Shopper's own view.
curl -sk $B/api/my-orders -H "Authorization: Bearer $ST"
```

Reconciliation returns `matched` (in both), `providerOnly` (the provider knows, eShop doesn't) and
`eShopOnly` (the reverse), counting only messages sent from `Twilio:FromNumber`.

## 6. No PII in logs

Grep the PublicApi console/log output for the national digits of either test number — there are zero
matches. The app logs message SIDs, statuses and ids only; HTTP-client request logging is removed.
