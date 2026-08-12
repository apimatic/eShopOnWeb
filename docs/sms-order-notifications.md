# SMS Order Notifications (Twilio)

Additive capability that keeps shoppers informed by text message as their orders progress. It adds
the shopper's mobile contact details, the messages that go out as an order moves, and the operator's
view of what actually reached the customer. It does not replace the existing catalog/basket/order flow.

Everything is exposed as JWT-authenticated HTTP endpoints on **`src/PublicApi`** (routed under `/api/`).

## Twilio integration

Twilio is consumed through a **hand-written client built against the OpenAPI specs in `api-specs/twilio`** —
no Twilio SDK/NuGet package is used. The spec is the contract for every interaction.

| Capability | Spec | Operation |
|---|---|---|
| Validate + canonicalise a number | `twilio_lookups_v2` | `GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}` → `valid`, `phone_number` |
| Send a message | `twilio_api_v2010` | `POST /2010-04-01/Accounts/{Sid}/Messages.json` (`To`,`From`,`Body`) |
| Schedule the follow-up | `twilio_api_v2010` | same, `MessagingServiceSid`+`SendAt`+`ScheduleType=fixed` |
| Cancel the follow-up | `twilio_api_v2010` | `POST /Messages/{Sid}.json` `Status=canceled` |
| Dispose of content | `twilio_api_v2010` | `POST /Messages/{Sid}.json` `Body=` (empty) — record survives |
| Read delivery outcome | `twilio_api_v2010` | `GET /Messages/{Sid}.json` |
| Reconciliation | `twilio_api_v2010` | `GET /Messages.json?From={FromNumber}&DateSent>=…&DateSent<=…` (paged) |

Key implementation files:
- `src/Infrastructure/Twilio/` — `TwilioMessagingClient` (`ISmsGateway`), `TwilioPhoneNumberLookupClient`
  (`IPhoneNumberValidator`), `TwilioSettings`, wire contracts, `TwilioApiException`.
- `src/ApplicationCore/Services/OrderNotificationService.cs` (`INotificationService`) — orchestration.
- `src/ApplicationCore/Entities/ContactNumberAggregate`, `.../NotificationAggregate`, and
  `Order.Status` (additive) — domain model.
- `src/PublicApi/{ContactNumber,Order,Notification}Endpoints/` — the HTTP surface.
- `src/PublicApi/Configuration/TwilioServiceCollectionExtensions.cs` — DI wiring.

## Configuration

Bound from the `Twilio:` configuration section (values loaded via **.NET user-secrets**, never committed):

| Key | Env var | Notes |
|---|---|---|
| `Twilio:AccountSid` | `TWILIO_ACCOUNT_SID` | |
| `Twilio:AuthToken` | `TWILIO_AUTH_TOKEN` | secret; never logged/returned |
| `Twilio:FromNumber` | `TWILIO_FROM_NUMBER` | sender; reconciliation filters on it |
| `Twilio:MessagingServiceSid` | `TWILIO_MESSAGING_SERVICE_SID` | required for scheduling |
| `Twilio:BaseUrl` | — | optional override for the **messaging** API only (not lookups) |

Load them (PowerShell/bash):

```bash
for pair in "Twilio:AccountSid=TWILIO_ACCOUNT_SID" "Twilio:AuthToken=TWILIO_AUTH_TOKEN" \
            "Twilio:FromNumber=TWILIO_FROM_NUMBER" "Twilio:MessagingServiceSid=TWILIO_MESSAGING_SERVICE_SID"; do
  key="${pair%%=*}"; var="${pair##*=}"
  dotnet user-secrets set "$key" "$(printenv "$var")" --project src/PublicApi/PublicApi.csproj
done
```

## Endpoints

Shopper-scoped (any authenticated user, acts only on the caller's own data):
- `POST /api/contact-numbers` → `{ contactNumberId, phoneNumber }` (rejects an unusable number at registration)
- `GET /api/contact-numbers`
- `DELETE /api/contact-numbers/{contactNumberId}`
- `POST /api/orders` → `{ orderId }`
- `GET /api/my-orders`
- `GET /api/orders/{orderId}/notifications` (owner or admin; each entry carries `notificationId`)

Operator-only (administrator role):
- `POST /api/orders/{orderId}/dispatch`
- `POST /api/orders/{orderId}/cancel`
- `POST /api/notifications/{notificationId}/resend` → `{ notificationId }` (idempotency key in body `idempotencyKey` or `Idempotency-Key` header)
- `DELETE /api/notifications/{notificationId}/content`
- `GET /api/notifications/reconciliation?from={iso}&to={iso}`

## Behaviour guarantees

- A message that cannot be sent never fails the order op — it is recorded as a message outcome.
- A shopper with no number on file is simply not messaged.
- Numbers belong to the shopper who registered them; one shopper can't see/use/delete another's.
- A deleted number is never messaged again (resend is gated on the number still existing).
- Content disposal redacts the body at the provider; the message record and its outcome survive.
- Shopper phone numbers are never written to logs (HTTP-client logging is removed; app logs carry only SIDs/statuses/ids).

## Run it

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="https://localhost:9723;http://localhost:9724"
export UseOnlyInMemoryDatabase=true
dotnet run --project src/PublicApi/PublicApi.csproj
# Swagger UI at https://localhost:9723/swagger
```

> In-memory mode: Web and PublicApi hold separate stores and data resets on restart. Drive the whole
> flow through PublicApi in one run. See `docs/verify-sms-notifications.md` for a copy-paste walkthrough.
