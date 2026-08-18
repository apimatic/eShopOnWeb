# SMS Order Notifications (Twilio)

Adds SMS notifications to eShopOnWeb as orders progress, using **Twilio** (via the
APIMatic-generated `AsadAli.TwilioSdk`). It is additive — the existing catalog/basket/order flow
is untouched. All capabilities are exposed on **`src/PublicApi`** (JWT), under `/api/`.

## What was added

| Layer | Additions |
|---|---|
| `ApplicationCore` | `ContactNumber` and `OrderNotification` aggregates; `NotificationKind`; `ISmsProvider` + result records (`PhoneValidationResult`, `SmsDispatchResult`, `ProviderMessageRecord`, `ReconciliationReport`); `SmsProviderException`; `IContactNumberService`/`ContactNumberService` and `IOrderNotificationService`/`OrderNotificationService`; specifications. |
| `Infrastructure` | `TwilioSettings` (bound from `Twilio:` section, startup-validated); `TwilioSmsProvider` (the only Twilio contact point); `AddTwilioSmsProvider` DI wiring; EF configs + `ContactNumbers`/`OrderNotifications` DbSets on `CatalogContext`. |
| `PublicApi` | Endpoints under `ContactNumberEndpoints/` and `OrderNotificationEndpoints/`; DI registration in `Program.cs`. |

## Endpoints

Shopper-scoped (any authenticated caller; acts only on the caller's own data):

- `POST /api/contact-numbers` — validate via provider lookup, store canonical E.164, returns `contactNumberId`.
- `GET /api/contact-numbers` — the caller's numbers.
- `DELETE /api/contact-numbers/{contactNumberId}` — remove one.
- `POST /api/orders` — place an order from catalog `items` (`catalogItemId` + `quantity`); returns `orderId`; shopper told it was placed.
- `GET /api/my-orders` — the caller's orders with where each notification got to.
- `GET /api/orders/{orderId}/notifications` — notifications for the caller's order (each carries `notificationId`).

Operator actions (restricted to the `Administrators` role):

- `POST /api/orders/{orderId}/dispatch` — shopper told it is on its way; a follow-up is queued with the provider for a few days later.
- `POST /api/orders/{orderId}/cancel` — shopper told; any not-yet-sent follow-up is called off at the provider.
- `POST /api/notifications/{notificationId}/resend` — body `{ "idempotencyKey": "..." }` (or `Idempotency-Key` header); returns the `notificationId` the resend produced. Same key ⇒ no second message; fresh key ⇒ new message.
- `DELETE /api/notifications/{notificationId}/content` — dispose of the message content at the provider; the record (sid + status) survives.
- `GET /api/notifications/reconciliation?from={iso}&to={iso}` — the provider's record of messages sent from `Twilio:FromNumber` in the range, lined up against eShop's.

## Guarantees

- A messaging failure **never** fails the order operation — the order is still placed/dispatched/cancelled and the request still succeeds; the attempt is recorded with a failure reason. A shopper with no number on file is simply not messaged.
- A carrier-undeliverable message (e.g. the reserved US number) is handled as an **outcome** (status `undelivered`), not an error.
- A number/order belongs to the shopper who created it; one shopper can never see, use, or delete another's.
- The shopper's number is never written to logs. It is returned only to its owner.

## Configuration & secrets

Bound from the `Twilio:` section (never hard-coded):

| Key | From env var |
|---|---|
| `Twilio:AccountSid` | `TWILIO_ACCOUNT_SID` |
| `Twilio:AuthToken` | `TWILIO_AUTH_TOKEN` |
| `Twilio:FromNumber` | `TWILIO_FROM_NUMBER` |
| `Twilio:MessagingServiceSid` | `TWILIO_MESSAGING_SERVICE_SID` |
| `Twilio:BaseUrl` | *(optional)* messaging-API base-URL override; leave unset for the default host. Does not affect the lookup host. |

Load the secrets into .NET user-secrets (values never enter the repo):

```bash
P=src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"          --project "$P"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"           --project "$P"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"          --project "$P"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID" --project "$P"
```

The app validates these at startup and refuses to boot if any is missing (naming the key, never the value).

## Environment notes (this machine)

- `global.json` uses `rollForward: latestMajor`; build/run with `DOTNET_ROLL_FORWARD=Major` (only the .NET 10 SDK is installed; the ASP.NET Core 8 runtime is present, so the app runs on net8.0).
- Run PublicApi in Development: `appsettings.Development.json` sets `UseOnlyInMemoryDatabase=true` (no LocalDB here). The in-memory store is per-process and lost on restart — place, dispatch and cancel within one run.
- PublicApi listens on `https://localhost:11483` (and `http://localhost:11484`).

## Verify it yourself

```bash
export DOTNET_ROLL_FORWARD=Major
# 1) Build and run
dotnet build eShopOnWeb.sln -c Debug
dotnet run --project src/PublicApi        # https://localhost:11483

# 2) Tokens (shopper + operator; default password Pass@word1)
B=https://localhost:11483
DTOK=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
ATOK=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'    | jq -r .token)

# 3) Register the reachable (Canadian) number  →  returns contactNumberId
curl -sk -X POST $B/api/contact-numbers -H "Authorization: Bearer $DTOK" \
  -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"

# 4) Place an order  →  returns orderId; a real "placed" SMS is delivered to that number
curl -sk -X POST $B/api/orders -H "Authorization: Bearer $DTOK" \
  -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":1,"quantity":1}]}'

# 5) Dispatch (operator)  →  "on its way" + a follow-up is scheduled with the provider
curl -sk -X POST $B/api/orders/1/dispatch -H "Authorization: Bearer $ATOK"

# 6) Cancel (operator)  →  "cancelled" + the scheduled follow-up flips to "canceled" (called off)
curl -sk -X POST $B/api/orders/1/cancel -H "Authorization: Bearer $ATOK"

# 7) See where each notification got to
curl -sk $B/api/my-orders                 -H "Authorization: Bearer $DTOK"
curl -sk $B/api/orders/1/notifications    -H "Authorization: Bearer $DTOK"

# 8) Undeliverable + resend: register the reserved US number, place an order to it (undelivered),
#    then resend the notification. Same key = no second message; fresh key = new message.
curl -sk -X POST $B/api/contact-numbers -H "Authorization: Bearer $DTOK" \
  -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
# ...place an order, note the notificationId, then:
curl -sk -X POST $B/api/notifications/{notificationId}/resend -H "Authorization: Bearer $ATOK" \
  -H "Content-Type: application/json" -d '{"idempotencyKey":"key-1"}'

# 9) Dispose message content (operator) — record survives, content gone at the provider
curl -sk -X DELETE $B/api/notifications/1/content -H "Authorization: Bearer $ATOK"

# 10) Reconciliation over a populated range (operator)
curl -sk "$B/api/notifications/reconciliation?from=2026-08-18T00:00:00Z&to=2026-08-19T23:59:59Z" \
  -H "Authorization: Bearer $ATOK"
```

Only ever register/message `TWILIO_TEST_TO_NUMBER` (Canadian, reachable) and
`TWILIO_UNREACHABLE_TO_NUMBER` (reserved US, undeliverable).
