# SMS Order Notifications (Twilio)

Additive capability on **`src/PublicApi`**: shoppers put a mobile number on file, get an SMS as
their order is placed / dispatched / cancelled, and operators can see and act on what actually
reached the customer. The messaging provider is **Twilio**, consumed **only** through a
hand-written client built against the Twilio **OpenAPI documents in `api-specs/`** — no Twilio
SDK/NuGet package is used.

## Which spec documents back this

| Capability | api-specs document | Operation |
|---|---|---|
| Send / schedule / fetch / cancel / redact / list messages | `api-specs/twilio/twilio_api_v2010` | `CreateMessage`, `FetchMessage`, `UpdateMessage`, `ListMessage`, `DeleteMessage` (2010-04-01 Messages resource) |
| Validate a number & get its canonical E.164 form | `api-specs/twilio/twilio_lookups_v2` | `GET /v2/PhoneNumbers/{PhoneNumber}` |

- Scheduling uses `ScheduleType=fixed` + `SendAt` + the messaging service.
- Content disposal uses `UpdateMessage` with `Body=""` (provider-side redaction; the record survives).
- Calling off a follow-up uses `UpdateMessage` with `Status=canceled`.
- Reconciliation asks the provider (`ListMessage`) with the `From=` sending-number filter applied
  provider-side, over a `DateSent` range.
- Lookups is served from `lookups.twilio.com` and is **not** governed by `Twilio:BaseUrl`
  (that overrides only the messaging API).

## Configuration (`Twilio:` section — values via env / user-secrets, never in the repo)

| Key | From env var |
|---|---|
| `Twilio:AccountSid` | `TWILIO_ACCOUNT_SID` |
| `Twilio:AuthToken` | `TWILIO_AUTH_TOKEN` |
| `Twilio:FromNumber` | `TWILIO_FROM_NUMBER` |
| `Twilio:MessagingServiceSid` | `TWILIO_MESSAGING_SERVICE_SID` |
| `Twilio:BaseUrl` | *(optional messaging-API override; unset = provider default)* |

Load them into user-secrets (stored outside the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
```

## Endpoints

Shopper-scoped (any authenticated caller, acts only on own data):
- `POST /api/contact-numbers` → `{ contactNumberId, phoneNumber }`
- `GET /api/contact-numbers`
- `DELETE /api/contact-numbers/{contactNumberId}`
- `POST /api/orders` → `{ orderId }`
- `GET /api/my-orders`
- `GET /api/orders/{orderId}/notifications` (each entry carries `notificationId`)

Operator-only (`Administrators` role):
- `POST /api/orders/{orderId}/dispatch`
- `POST /api/orders/{orderId}/cancel`
- `POST /api/notifications/{notificationId}/resend` → `{ notificationId }` (idempotency key in body
  `idempotencyKey` or `Idempotency-Key` header)
- `DELETE /api/notifications/{notificationId}/content`
- `GET /api/notifications/reconciliation?from={iso}&to={iso}`

## Running on this machine

The SDK is .NET 10 while the app targets net8.0, and there is no LocalDB, so:

```bash
cd src/PublicApi
export DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true
export ASPNETCORE_URLS="https://localhost:11523;http://localhost:11524"
dotnet run    # or: dotnet bin/Debug/net8.0/PublicApi.dll
```

With the in-memory provider each host keeps its own store and data does not survive a restart, so
create, dispatch and cancel an order within the same run. Swagger UI: `https://localhost:11523/swagger`.

## Database

The two tables (`ContactNumbers`, `OrderNotifications`) ship as EF migration
`AddOrderNotifications` for the SQL Server path. The in-memory provider ignores migrations.
