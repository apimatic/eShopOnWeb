# Order SMS Notifications (Twilio)

Additive capability on **`src/PublicApi`**: shoppers put a mobile number on file, receive SMS as
their orders move, and operators can recover from and account for what was sent. It reuses the
existing `Order` / `OrderItem` model and does not change the catalog/basket/order flow.

## Architecture

| Layer | What was added |
| --- | --- |
| `ApplicationCore` | `ContactNumber` and `SmsNotification` aggregates (`Entities/NotificationAggregate`); `ISmsProvider` (the provider boundary + result records) and `IOrderNotificationService`; `OrderNotificationService` orchestration; supporting specifications. |
| `Infrastructure` | `TwilioSmsProvider` + `TwilioSettings` (`Services/Sms`) — every Twilio call is a documented REST call issued here; EF configs and two `DbSet`s on `CatalogContext`. |
| `PublicApi` | 11 endpoints (contact numbers, orders, notifications) following the project's `IEndpoint` + `[Authorize]` conventions; DI + `Twilio:` settings binding in `Program.cs`. |

Design guarantees:

- **A messaging failure never fails the order operation.** Every provider call is guarded; a
  failure is recorded as an outcome on the notification, never raised.
- **A shopper's number is never logged** (verified: zero occurrences in the app log). It is stored
  so messages can be reconciled/re-sent, but never written to logs and never returned across shoppers.
- **Numbers and orders are owner-scoped** — one shopper can never see, use, or delete another's.
- **Delivery outcome is polled on read** (there is no public callback URL), so `GET` endpoints
  report the live provider status.

### Twilio calls used (all via the twilio-docs MCP reference)

- Validate a number (registration): Lookup v2 `GET /v2/PhoneNumbers/{E164}` — served from its own
  host, **not** governed by `Twilio:BaseUrl`. Rejects a number the provider considers invalid;
  stores the returned canonical `phone_number`.
- Send now: `POST /2010-04-01/Accounts/{Sid}/Messages.json` with `From = Twilio:FromNumber`.
- Schedule the follow-up: same endpoint with `ScheduleType=fixed`, `SendAt` (3 days out),
  `MessagingServiceSid`.
- Call off the follow-up: `POST .../Messages/{Sid}.json` with `Status=canceled`.
- Dispose of content: `POST .../Messages/{Sid}.json` with an empty `Body` (redaction — record and
  outcome survive).
- Read outcome: `GET .../Messages/{Sid}.json`.
- Reconcile: `GET .../Messages.json?From={Twilio:FromNumber}&DateSent...` (server-side `From` filter,
  every page followed).

Messaging calls use `Twilio:BaseUrl` verbatim when set, otherwise `https://api.twilio.com`.

## Configuration

Bound from the `Twilio:` configuration section (loaded from **.NET user-secrets** on this machine —
never committed):

| Key | Source env var |
| --- | --- |
| `Twilio:AccountSid` | `TWILIO_ACCOUNT_SID` |
| `Twilio:AuthToken` | `TWILIO_AUTH_TOKEN` (secret — never logged/returned/committed) |
| `Twilio:FromNumber` | `TWILIO_FROM_NUMBER` |
| `Twilio:MessagingServiceSid` | `TWILIO_MESSAGING_SERVICE_SID` |
| `Twilio:BaseUrl` | optional messaging-API override |

```powershell
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$env:TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$env:TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$env:TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$env:TWILIO_MESSAGING_SERVICE_SID"
```

## Endpoints

Shopper (any authenticated caller; acts only on the caller's own data):

- `POST /api/contact-numbers` → `{ contactNumberId, phoneNumber }`
- `GET /api/contact-numbers`
- `DELETE /api/contact-numbers/{contactNumberId}`
- `POST /api/orders` (catalog item ids + quantities) → `{ orderId, total }`
- `GET /api/my-orders`
- `GET /api/orders/{orderId}/notifications` (each entry has its own `notificationId`)

Operator (administrator role only):

- `POST /api/orders/{orderId}/dispatch`
- `POST /api/orders/{orderId}/cancel`
- `POST /api/notifications/{notificationId}/resend` (idempotency key in body or `Idempotency-Key`
  header) → `{ notificationId }` of the message the resend produced
- `DELETE /api/notifications/{notificationId}/content`
- `GET /api/notifications/reconciliation?from={iso}&to={iso}`

## Running the API locally

```powershell
$env:DOTNET_ROLL_FORWARD = 'Major'          # SDK 8.0.x pinned, only .NET 10 SDK installed
$env:ASPNETCORE_ENVIRONMENT = 'Development'  # so user-secrets load
$env:ASPNETCORE_URLS = 'https://localhost:9863;http://localhost:9864'
$env:UseOnlyInMemoryDatabase = 'true'        # no LocalDB on this machine
dotnet run --project src/PublicApi
```

Swagger: `https://localhost:9863/swagger`. Seeded users (password `Pass@word1`):
`demouser@microsoft.com` (shopper), `admin@microsoft.com` (operator).

> In-memory data is per-process and per-host, so place/dispatch/cancel within one run, and drive
> everything through PublicApi (an order placed in the Web storefront is invisible here).

## Self-verification (what was exercised live against the real account)

1. Register `TWILIO_TEST_TO_NUMBER` (Canadian, reachable) → Lookup validated, canonical stored.
2. `POST /api/orders` → the *placed* SMS reached the handset (`delivered`).
3. `POST /api/orders/{id}/dispatch` → *dispatched* SMS `delivered`; a *follow-up* queued with the
   provider (`scheduled`, real SID, `SendAt` 3 days out).
4. `POST /api/orders/{id}/cancel` → *cancelled* SMS `delivered`; the scheduled follow-up went
   `scheduled → canceled` (called off before it could send).
5. Register `TWILIO_UNREACHABLE_TO_NUMBER` (US) and place an order → SMS accepted then refused by
   the carrier (`undelivered`, error `30034`) — handled as an outcome, order still placed.
6. Resend the failed message: same idempotency key returns the **same** notification (no second
   send); a fresh key produces a new one.
7. `DELETE /api/notifications/{id}/content` → body redacted at the provider and locally; the record
   and its `undelivered` outcome survive.
8. `GET /api/notifications/reconciliation` → provider vs eShop lined up by SID (matched entries, plus
   the canceled follow-up correctly flagged as eShop-only because it was not sent from `FromNumber`).
9. Authorization: shopper → operator endpoints `403`; no token `401`; another/unknown order `404`.
   Removed number → resend refuses to send (`recipient_removed`).
