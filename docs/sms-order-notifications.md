# SMS Order Notifications (Twilio)

Additive capability on **`src/PublicApi`** (JWT): shoppers put a mobile number on file, get
texted as their order moves (placed / dispatched / cancelled), and operators can resend,
dispose of message content, and reconcile against the provider. Twilio is reached through a
hand-written client built to the OpenAPI specs in `api-specs/twilio/` (no Twilio SDK).

## Twilio APIs used (authoritative contract = `api-specs/`)

| Need | Spec doc | Operation |
|------|----------|-----------|
| Validate + canonicalise a number | `twilio_lookups_v2` | `GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}` |
| Send / schedule a message | `twilio_api_v2010` | `POST {base}/2010-04-01/Accounts/{Sid}/Messages.json` (`ScheduleType=fixed`+`SendAt` to schedule) |
| Read delivery outcome (no webhooks here) | `twilio_api_v2010` | `GET .../Messages/{Sid}.json` |
| Cancel a scheduled message | `twilio_api_v2010` | `POST .../Messages/{Sid}.json` `Status=canceled` |
| Dispose of message content | `twilio_api_v2010` | `POST .../Messages/{Sid}.json` `Body=''` (redact; record + outcome survive) |
| Reconciliation | `twilio_api_v2010` | `GET .../Messages.json?From={FromNumber}&DateSent>=&DateSent<=` (paginated) |

Auth is HTTP Basic `AccountSid:AuthToken`. The messaging base URL is `Twilio:BaseUrl` when set,
else `https://api.twilio.com`; Lookups always uses its own host.

## Configuration (`Twilio:` section — values via user-secrets/env, never in the repo)

`Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`,
`Twilio:BaseUrl` (optional messaging override).

## Endpoints

Shopper-scoped (any authenticated caller, acts only on own data):
- `POST /api/contact-numbers` → `{ contactNumberId }` · `GET /api/contact-numbers` · `DELETE /api/contact-numbers/{id}`
- `POST /api/orders` → `{ orderId }` · `GET /api/my-orders` · `GET /api/orders/{orderId}/notifications` (each entry has `notificationId`)

Operator-only (`Administrators` role):
- `POST /api/orders/{orderId}/dispatch` · `POST /api/orders/{orderId}/cancel`
- `POST /api/notifications/{notificationId}/resend` (idempotency via `Idempotency-Key` header or `idempotencyKey` query) → `{ notificationId }`
- `DELETE /api/notifications/{notificationId}/content`
- `GET /api/notifications/reconciliation?from={iso}&to={iso}`

## Running locally (this machine)

```
DOTNET_ROLL_FORWARD=Major \
ASPNETCORE_ENVIRONMENT=Development \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:11703;http://localhost:11704" \
dotnet run --project src/PublicApi --no-launch-profile
```

Load the four `Twilio:` secrets into user-secrets first (from the `TWILIO_*` env vars):
```
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"          --project src/PublicApi
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"           --project src/PublicApi
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"          --project src/PublicApi
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID" --project src/PublicApi
```

Get a bearer token from `POST /api/authenticate` (`admin@microsoft.com` / `demouser@microsoft.com`,
password `Pass@word1`) before calling the API.

## Design notes

- New aggregates `ContactNumber` and `Notification` persist through the existing `CatalogContext`;
  orders reuse the existing `Order`/`OrderItem` model with an added `OrderStatus`.
- A failed/undeliverable message never fails the order op — it is recorded as an outcome.
- Phone numbers are PII: never logged (HttpClient loggers are removed so request URIs don't leak
  them) and masked in API responses; the auth token is never logged, returned, or written to a file.
- With the in-memory provider, data lives only for a single run; place, dispatch and cancel orders
  within the same run.
