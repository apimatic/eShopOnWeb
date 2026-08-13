# Order notifications by SMS (Twilio)

An additive capability on the **`src/PublicApi`** host that keeps shoppers informed by text message as
their orders progress. It adds the shopper's mobile contact details, the messages that go out as an order
moves, and the operator's view of what actually reached the customer. The existing catalog/basket/order flow
is untouched.

## What was added

| Area | Files |
| --- | --- |
| Domain | `ApplicationCore/Entities/NotificationAggregate/*` (`ContactNumber`, `Notification`, `ResendIdempotencyRecord`, `NotificationKind`, `MessageStatus`), `OrderAggregate/OrderStatus.cs` + `Order.Dispatch()/Cancel()` |
| Contracts (provider-agnostic) | `ApplicationCore/Interfaces/IPhoneNumberValidator.cs`, `ISmsGateway.cs`, `IOrderNotificationService.cs` |
| Orchestration | `ApplicationCore/Services/OrderNotificationService.cs` |
| Twilio integration | `Infrastructure/Messaging/*` — `TwilioSmsGateway` (messaging v2010), `TwilioPhoneNumberValidator` (Lookups v2), `TwilioSettings`, DI wiring |
| HTTP endpoints | `PublicApi/ContactNumberEndpoints/*`, `PublicApi/OrderEndpoints/*`, `PublicApi/NotificationEndpoints/*` |
| Persistence | EF configs under `Infrastructure/Data/Config/*` + migration `AddSmsOrderNotifications` |

## The Twilio contract (from `api-specs/`)

Every Twilio interaction is built to the OpenAPI documents in `api-specs/`, hand-written as typed clients
(no third-party Twilio SDK):

- **Number validation / canonicalization** — Lookups V2, `GET /v2/PhoneNumbers/{PhoneNumber}`
  (`twilio_lookups_v2`). `valid` decides usability; `phone_number` is the stored canonical E.164 form.
  Served from `lookups.twilio.com` and **not** governed by `Twilio:BaseUrl`.
- **Send / schedule / read / cancel / redact / list** — Messages resource, v2010
  (`twilio_api_v2010`): `POST .../Messages.json` (with `ScheduleType=fixed` + `SendAt` for the follow-up),
  `GET .../Messages/{Sid}.json`, `POST .../Messages/{Sid}.json` with `Status=canceled` (cancel) or
  `Body=""` (redact), and `GET .../Messages.json?From=&DateSent>=&DateSent<=` (reconciliation).
  This is the **messaging API** that `Twilio:BaseUrl` overrides.

Auth is HTTP Basic (`AccountSid:AuthToken`).

## Endpoints

Shopper-scoped (JWT, acts only on the caller's own data):

- `POST /api/contact-numbers` → `{ contactNumberId, phoneNumber }` — validates + canonicalizes, rejects an
  unusable number with 400.
- `GET /api/contact-numbers`
- `DELETE /api/contact-numbers/{contactNumberId}`
- `POST /api/orders` → `{ orderId, status }` — places an order from catalog item ids/quantities.
- `GET /api/my-orders`
- `GET /api/orders/{orderId}/notifications` — owner or operator; each entry carries its `notificationId`.

Operator-only (administrator role):

- `POST /api/orders/{orderId}/dispatch` — notifies + queues the "how did delivery go?" follow-up ~3 days out.
- `POST /api/orders/{orderId}/cancel` — notifies + calls off any not-yet-sent follow-up.
- `POST /api/notifications/{notificationId}/resend` → `{ notificationId, alreadyProcessed }` — idempotent by the
  caller-supplied `idempotencyKey`.
- `DELETE /api/notifications/{notificationId}/content` — redacts the text at the provider and locally.
- `GET /api/notifications/reconciliation?from={from}&to={to}` — ISO-8601 date-times.

A message that cannot be sent never fails the underlying operation; a shopper with no number on file is
simply not messaged. Numbers are PII and are never logged; the auth token is never logged, returned, or
written to a repo file.

## Configuration

Bind from the `Twilio:` section (no values are hard-coded): `Twilio:AccountSid`, `Twilio:AuthToken`,
`Twilio:FromNumber`, `Twilio:MessagingServiceSid`, and the optional `Twilio:BaseUrl` (messaging API only).
In this environment they are held in .NET user-secrets for `src/PublicApi`.

Load them from the environment variables (values never touch the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
# Twilio:BaseUrl is optional; leave unset to use the provider default (https://api.twilio.com)
```

## Run

```bash
export DOTNET_ROLL_FORWARD=Major        # global.json rolls forward to the .NET 10 SDK
export ASPNETCORE_ENVIRONMENT=Development
export UseOnlyInMemoryDatabase=true     # no LocalDB on this machine
export ASPNETCORE_URLS="https://localhost:10303;http://localhost:10304"
dotnet run --project src/PublicApi/PublicApi.csproj
```

> In-memory mode keeps a per-host store that is lost on restart and ignores migrations, so place/dispatch/
> cancel the orders you create within a single run.

See the top-level task write-up for the full step-by-step verification walkthrough.
