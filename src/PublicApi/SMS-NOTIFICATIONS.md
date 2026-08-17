# Order notifications by SMS (Twilio)

An additive capability on `src/PublicApi`: shoppers put a mobile number on file, get texted as their
orders move (placed / dispatched / cancelled, plus a scheduled "how did delivery go?" follow-up), and
operators can re-send, dispose of message content, and reconcile against the provider.

Twilio is reached through a **hand-written client built to the OpenAPI documents in `api-specs/twilio`**
(`src/Infrastructure/Twilio/TwilioMessagingClient.cs`) — no Twilio SDK is used. Contracts used:

| Capability | Spec document | Operation |
|---|---|---|
| Validate & canonicalise a number | `twilio_lookups_v2` | `GET lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}` |
| Send / schedule a message | `twilio_api_v2010` | `POST /2010-04-01/Accounts/{Sid}/Messages.json` |
| Read a message's outcome | `twilio_api_v2010` | `GET  …/Messages/{Sid}.json` |
| Cancel a scheduled message | `twilio_api_v2010` | `POST …/Messages/{Sid}.json` (`Status=canceled`) |
| Redact a message's body | `twilio_api_v2010` | `POST …/Messages/{Sid}.json` (`Body=`) |
| Reconcile sent messages | `twilio_api_v2010` | `GET  …/Messages.json?From=…&DateSent>=…&DateSent<=…` |

Auth is HTTP Basic (`AccountSid:AuthToken`) per the spec's `accountSid_authToken` scheme.

## Endpoints

Shopper-scoped (authenticated; acts only on the caller's own data):
- `POST /api/contact-numbers` → `{ contactNumberId, phoneNumber }` (invalid numbers rejected 400)
- `GET /api/contact-numbers`
- `DELETE /api/contact-numbers/{contactNumberId}`
- `POST /api/orders` → `{ orderId, status, total }`
- `GET /api/my-orders`
- `GET /api/orders/{orderId}/notifications` (owner or operator; each entry carries `notificationId`)

Operator-scoped (`Administrators` role):
- `POST /api/orders/{orderId}/dispatch`
- `POST /api/orders/{orderId}/cancel`
- `POST /api/notifications/{notificationId}/resend` (body `{ idempotencyKey }` or `Idempotency-Key` header) → `{ notificationId }`
- `DELETE /api/notifications/{notificationId}/content`
- `GET /api/notifications/reconciliation?from={iso}&to={iso}`

Guarantees: a message that cannot be sent never fails the underlying order operation; a shopper with
no number on file is simply not messaged; a shopper's number is never written to logs.

## Configuration (`Twilio:` section — never commit secret values)

`Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid` are loaded
from user-secrets / environment. `Twilio:BaseUrl` is an optional override for the **messaging** API base
address only (the Lookups host is not affected); when set it is used verbatim for every messaging call.

Load the secrets (values come from the environment; nothing is written into the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
```

## Run (this machine)

```bash
export DOTNET_ROLL_FORWARD=Major          # SDK pinned to 8.0.x; only .NET 10 SDK is installed
export UseOnlyInMemoryDatabase=true        # no LocalDB present; data lives for one run only
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="https://localhost:10623;http://localhost:10624"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

Swagger: `https://localhost:10623/swagger`. Because the in-memory store is per-run, create, dispatch and
cancel orders within the same run.

See the top of this repo's task write-up / PR description for a full curl walkthrough. Seeded logins:
`demouser@microsoft.com` (shopper) and `admin@microsoft.com` (operator), password `Pass@word1`.
