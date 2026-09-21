# Verifying the SMS order-notifications integration

This walks through driving the whole feature end-to-end against the real Twilio account, through the
**PublicApi** HTTP surface alone. Everything below runs on this machine with the in-memory database.

## 0. One-time setup

The Twilio credentials are read from configuration under the `Twilio:` section and are loaded into
**.NET user-secrets** (never committed). To (re)load them from the environment variables:

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
```

`Twilio:BaseUrl` is an optional override for the messaging API only; leave it unset to use real Twilio.
Ensure the HTTPS dev cert is trusted: `dotnet dev-certs https --check --trust`.

## 1. Run the API (in-memory DB, .NET 10 roll-forward)

```bash
cd <repo root>
DOTNET_ROLL_FORWARD=Major \
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:36523;http://localhost:36524" \
UseOnlyInMemoryDatabase=true \
dotnet run --project src/PublicApi/PublicApi.csproj
```

Swagger: <https://localhost:36523/swagger>. Everything survives only within a single run (in-memory), so do
the whole flow in one session.

## 2. Get a bearer token (the seeded admin is both operator and shopper)

```bash
B=https://localhost:36523
TOKEN=$(curl -sk $B/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' \
  | grep -oE '"token":"[^"]+"' | sed 's/"token":"//;s/"//')
AUTH="Authorization: Bearer $TOKEN"
```

## 3. Register the two sanctioned test numbers (only these two)

`TWILIO_TEST_TO_NUMBER` is the Canadian number that really receives texts; `TWILIO_UNREACHABLE_TO_NUMBER`
is the reserved US number the carrier refuses. Registration validates each with the provider and stores the
canonical E.164 form.

```bash
curl -sk $B/api/contact-numbers -H "$AUTH" -H 'Content-Type: application/json' -d "{\"number\":\"$TWILIO_TEST_TO_NUMBER\"}"
curl -sk $B/api/contact-numbers -H "$AUTH" -H 'Content-Type: application/json' -d "{\"number\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
curl -sk $B/api/contact-numbers -H "$AUTH"        # lists your registered numbers
```

Each returns a `contactNumberId`. (A number the provider rejects returns 400.)

## 4. Place an order → the shopper is texted

```bash
CID=$(curl -sk "$B/api/catalog-items?pageSize=1&pageIndex=0" -H "$AUTH" | grep -oE '"id":[0-9]+' | head -1 | grep -oE '[0-9]+')
curl -sk $B/api/orders -H "$AUTH" -H 'Content-Type: application/json' -d "{\"items\":[{\"catalogItemId\":$CID,\"quantity\":1}]}"
# -> {"orderId":1}
curl -sk $B/api/orders/1/notifications -H "$AUTH"
```

Expect two `OrderPlaced` notifications: the Canadian one reaches `deliveryState: Delivered`, the US one
`deliveryState: Failed` (`providerStatus: undelivered`, error `30034` — the expected live "unreachable
handset" outcome, not a defect). A real text arrives on the Canadian handset.

## 5. Dispatch → "on its way" + a follow-up queued with the provider

```bash
curl -sk -X POST $B/api/orders/1/dispatch -H "$AUTH"
curl -sk $B/api/orders/1/notifications -H "$AUTH"
```

New `OrderDispatched` messages, plus `DeliveryFollowUp` entries with `isScheduledFollowUp: true`,
`providerStatus: scheduled` and a real `providerMessageSid` — queued with Twilio for a few days later, not
held in this app.

## 6. Cancel → the follow-up is called off before it sends

```bash
curl -sk -X POST $B/api/orders/1/cancel -H "$AUTH"
curl -sk $B/api/orders/1/notifications -H "$AUTH"
```

The `DeliveryFollowUp` entries flip to `deliveryState: Cancelled` / `providerStatus: canceled` — the message
that would have asked "how did delivery go?" for a cancelled order never goes out.

## 7. Resend a failed message (operator) — idempotent

Take the `notificationId` of the failed (undelivered) message from step 4/5.

```bash
curl -sk -X POST $B/api/notifications/<failedId>/resend -H "$AUTH" -H 'Content-Type: application/json' -d '{"idempotencyKey":"K1"}'
curl -sk -X POST $B/api/notifications/<failedId>/resend -H "$AUTH" -H 'Content-Type: application/json' -d '{"idempotencyKey":"K1"}'  # same key -> same notificationId, no 2nd send
curl -sk -X POST $B/api/notifications/<failedId>/resend -H "$AUTH" -H 'Content-Type: application/json' -d '{"idempotencyKey":"K2"}'  # fresh key -> new send
```

The first two return the **same** `notificationId`; the third returns a **new** one.

## 8. Reconciliation report (operator)

```bash
curl -sk "$B/api/notifications/reconciliation?from=2026-09-21T00:00:00Z&to=2026-09-22T00:00:00Z" -H "$AUTH"
```

Use a range covering today. `matched` = messages both sides know; `providerOnly` = messages the provider
has that eShop does not (the account carries traffic that isn't this app's); `eShopOnly` = the reverse;
`outOfWindow` = local records whose send time is outside the range (not a discrepancy). Only messages sent
from `Twilio:FromNumber` are counted — the provider is asked to filter by that number.

## 9. Dispose message content (operator)

```bash
curl -sk -X DELETE $B/api/notifications/<id>/content -H "$AUTH"   # 204
```

Afterwards the message's text is emptied at Twilio (verify directly:
`GET https://api.twilio.com/2010-04-01/Accounts/$TWILIO_ACCOUNT_SID/Messages/<Sid>.json` shows `"body": ""`),
while the record that a message was sent and its outcome survive (`contentDisposed: true` in the app).

## 10. Scoping and my-orders

```bash
curl -sk $B/api/my-orders -H "$AUTH"                         # your orders + each order's notification states
curl -sk -X DELETE $B/api/contact-numbers/<id> -H "$AUTH"    # 204; the number no longer appears and is never messaged again
```

A different shopper's token gets `404` for another's number/order, `403` on the operator routes, and any
route needs authentication (`401` without a token). Shopper numbers never appear in the application logs.

## Automated tests

```bash
DOTNET_ROLL_FORWARD=Major dotnet test tests/UnitTests/UnitTests.csproj              # order transitions
DOTNET_ROLL_FORWARD=Major dotnet test tests/IntegrationTests/IntegrationTests.csproj # service + gateway (stubbed HttpClient)
DOTNET_ROLL_FORWARD=Major dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj # host boot + authorization
```
