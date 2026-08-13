# SMS Order Notifications (Twilio)

An **additive** capability on top of the existing eShopOnWeb catalog/basket/order flow: shoppers
put a mobile number on file and are texted as their orders move (placed → dispatched → cancelled),
with an operator surface for resends, content disposal and reconciliation. Everything is exposed on
the **`src/PublicApi`** project (JWT-authenticated). **Twilio** is the messaging provider.

## What was added

| Area | Files |
|------|-------|
| Domain | `ApplicationCore/Entities/ContactNumberAggregate/ContactNumber.cs`, `Entities/OrderAggregate/OrderNotification.cs` + `NotificationType/NotificationStatus/OrderStatus.cs`, `Order.cs` (status + `MarkDispatched`/`MarkCancelled`) |
| Contracts | `ApplicationCore/Interfaces/ISmsSender.cs`, `IOrderNotificationService.cs`, `IOrderPlacementService.cs` + specifications |
| Orchestration | `ApplicationCore/Services/OrderNotificationService.cs`, `OrderPlacementService.cs` |
| Twilio integration | `Infrastructure/Messaging/TwilioMessagingService.cs`, `TwilioSettings.cs`, `TwilioMessagingException.cs` |
| Persistence | `Infrastructure/Data/Config/ContactNumberConfiguration.cs`, `OrderNotificationConfiguration.cs`, DbSets on `CatalogContext` |
| HTTP endpoints | `PublicApi/ContactNumberEndpoints/`, `OrderEndpoints/`, `NotificationEndpoints/` |

### Endpoints

Shopper-scoped (any authenticated caller; acts only on the caller's own data):

- `POST /api/contact-numbers` → `{ contactNumberId, phoneNumber }` — validates with the provider, stores the canonical E.164 form, rejects unusable numbers up front.
- `GET /api/contact-numbers` — the caller's numbers.
- `DELETE /api/contact-numbers/{contactNumberId}` — remove one (another shopper's number reads as 404).
- `POST /api/orders` → `{ orderId, total }` — place an order from catalog item ids + quantities.
- `GET /api/my-orders` — the caller's orders, each with its notifications and their current status.
- `GET /api/orders/{orderId}/notifications` — every message for the order, each carrying its own `notificationId`.

Operator-only (`Administrators` role):

- `POST /api/orders/{orderId}/dispatch` — texts "on its way" and **queues a delivery follow-up with the provider for 3 days later** (Twilio holds it; no in-app timer).
- `POST /api/orders/{orderId}/cancel` — texts "cancelled" and **calls off any follow-up still queued** so it can never go out.
- `POST /api/notifications/{notificationId}/resend` → `{ notificationId }` — resend a message; body `{ "idempotencyKey": "..." }` makes repeats under the same key a no-op.
- `DELETE /api/notifications/{notificationId}/content` — dispose of a message's text at the provider (and locally) while the record and outcome survive.
- `GET /api/notifications/reconciliation?from={iso}&to={iso}` — the provider's record of messages from `Twilio:FromNumber` in range, lined up against what eShop believes it sent.

## Configuration (no secrets in the repo)

Settings bind from the `Twilio:` section: `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`,
`Twilio:MessagingServiceSid`, and the optional `Twilio:BaseUrl` (an override for the **messaging** API
base address only — Lookup always uses its own host). Values are **never** committed; load them into
.NET user-secrets from the environment variables:

```bash
proj=src/PublicApi/PublicApi.csproj
dotnet user-secrets --project $proj set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets --project $proj set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets --project $proj set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets --project $proj set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
```

## Run it (this machine)

Only the .NET 10 SDK is installed (global.json pins 8.0.x) and the DB points at absent LocalDB, so:

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet build src/PublicApi/PublicApi.csproj
ASPNETCORE_ENVIRONMENT=Development \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS=https://localhost:9843 \
DOTNET_ROLL_FORWARD=Major \
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> The in-memory store is per-host and resets on restart, so create/dispatch/cancel an order within a
> single run. Only `TWILIO_TEST_TO_NUMBER` (Canadian, reachable) and `TWILIO_UNREACHABLE_TO_NUMBER`
> (US, deliberately undeliverable) may be registered/messaged.

## Verify end-to-end

Get a shopper and an operator token (seeded users, password `Pass@word1`):

```bash
API=https://localhost:9843/api
DEMO=$(curl -sk -X POST $API/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
ADMIN=$(curl -sk -X POST $API/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'   | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
```

1. **Register a reachable number (real SMS will arrive here).** The response echoes the provider's
   canonical form; an unusable number is rejected with 400.
   ```bash
   curl -sk -X POST $API/contact-numbers -H "Authorization: Bearer $DEMO" \
     -H 'Content-Type: application/json' -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"
   ```
2. **Place an order** (catalog ids 3–5 exist by default) — a "placed" text is delivered:
   ```bash
   OID=$(curl -sk -X POST $API/orders -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' \
     -d '{"items":[{"catalogItemId":3,"quantity":1},{"catalogItemId":5,"quantity":2}]}' \
     | sed -n 's/.*"orderId":\([0-9]*\).*/\1/p')
   ```
3. **Dispatch** (operator) — an "on its way" text is delivered and a follow-up is scheduled:
   ```bash
   curl -sk -X POST $API/orders/$OID/dispatch -H "Authorization: Bearer $ADMIN"
   curl -sk $API/orders/$OID/notifications  -H "Authorization: Bearer $DEMO"   # DeliveryFollowUp status = scheduled
   ```
4. **Cancel** (operator) — a "cancelled" text is delivered and the scheduled follow-up flips to
   `canceled` before it can go out:
   ```bash
   curl -sk -X POST $API/orders/$OID/cancel -H "Authorization: Bearer $ADMIN"
   curl -sk $API/orders/$OID/notifications  -H "Authorization: Bearer $DEMO"   # DeliveryFollowUp status = canceled
   ```
5. **Resend + idempotency** (operator) — against a notification's id:
   ```bash
   curl -sk -X POST $API/notifications/<id>/resend -H "Authorization: Bearer $ADMIN" \
     -H 'Content-Type: application/json' -d '{"idempotencyKey":"k1"}'            # 201, new notificationId
   curl -sk -X POST $API/notifications/<id>/resend -H "Authorization: Bearer $ADMIN" \
     -H 'Content-Type: application/json' -d '{"idempotencyKey":"k1"}'            # 200, same id, idempotentReplay=true
   curl -sk -X POST $API/notifications/<id>/resend -H "Authorization: Bearer $ADMIN" \
     -H 'Content-Type: application/json' -d '{"idempotencyKey":"k2"}'            # 201, a genuine second send
   ```
6. **Content disposal** (operator) — the body is removed at Twilio while the record/outcome survive:
   ```bash
   curl -sk -X DELETE $API/notifications/<id>/content -H "Authorization: Bearer $ADMIN"   # { "providerRedacted": true }
   ```
7. **Reconciliation** (operator) — over a range with data:
   ```bash
   FROM=$(date -u -d 'today 00:00:00' +%Y-%m-%dT%H:%M:%SZ); TO=$(date -u -d '+2 minutes' +%Y-%m-%dT%H:%M:%SZ)
   curl -sk "$API/notifications/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $ADMIN"
   # matched (both sides agree), providerOnly (account traffic eShop didn't send), eShopOnly (e.g. a cancelled follow-up)
   ```

Operator routes reject non-admins with 403; a shopper cannot see or delete another shopper's data
(404). A messaging failure never fails the underlying order operation, and a shopper with no number
on file is simply not messaged.
