# SMS order notifications (Twilio)

Additive capability that keeps shoppers informed by text message as their orders progress, using
**Twilio** as the provider. It adds the shopper's mobile contact details, the messages that go out as
an order moves, and the operator's view of what actually reached the customer. The existing
catalog / basket / order flow is untouched.

All capabilities are HTTP endpoints on **`src/PublicApi`** (JWT-authenticated). Operator actions are
restricted to the `Administrators` role; every other endpoint is shopper-scoped and acts only on the
caller's own data.

## Twilio contract

The Twilio OpenAPI specs under `api-specs/twilio/` are the authoritative contract. Two documents are
used, and the clients are hand-written to them (no third-party SDK):

| Capability | Spec | Host |
|---|---|---|
| Send / read / cancel / redact / list messages | `twilio_api_v2010` (Messages resource) | `https://api.twilio.com` (overridable via `Twilio:BaseUrl`) |
| Validate a number & get its canonical E.164 form | `twilio_lookups_v2` (PhoneNumber lookup) | `https://lookups.twilio.com` (**not** governed by `Twilio:BaseUrl`) |

Key spec-driven behaviours:

- **Send** — `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (form-encoded; `To`, `From` or
  `MessagingServiceSid`, `Body`).
- **Schedule the follow-up** — the same create call with `MessagingServiceSid` + `ScheduleType=fixed`
  + `SendAt` (ISO-8601). The follow-up is queued **with the provider**, not held by a local timer.
- **Call off a scheduled message** — `POST …/Messages/{Sid}.json` with `Status=canceled`.
- **Dispose of content** — `POST …/Messages/{Sid}.json` with an empty `Body` (redaction at the
  provider; the record of the send and its outcome survive).
- **Delivery outcome** — `GET …/Messages/{Sid}.json`.
- **Reconciliation** — `GET …/Messages.json?From={FromNumber}&DateSent>={from}&DateSent<={to}`, paged
  to cover the whole range. The `From` filter scopes the query to this application's sending number,
  because the account also carries other traffic.
- **Validation** — `GET /v2/PhoneNumbers/{PhoneNumber}`; `valid` gates registration and
  `phone_number` is the stored canonical form.

## Configuration

Bound from the `Twilio:` configuration section (loaded via .NET user-secrets; **no values live in the
repo**):

| Key | Source env var | Purpose |
|---|---|---|
| `Twilio:AccountSid` | `TWILIO_ACCOUNT_SID` | Basic-auth username |
| `Twilio:AuthToken` | `TWILIO_AUTH_TOKEN` | Basic-auth password (secret; never logged/returned) |
| `Twilio:FromNumber` | `TWILIO_FROM_NUMBER` | Sending number; reconciliation is scoped to it |
| `Twilio:MessagingServiceSid` | `TWILIO_MESSAGING_SERVICE_SID` | Required to schedule the follow-up |
| `Twilio:BaseUrl` | *(optional)* | Overrides the messaging-API base address only |

Load the secrets (values come from the environment; nothing is written into the repo):

```bash
proj=src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"          --project $proj
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"           --project $proj
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"          --project $proj
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID" --project $proj
```

## Endpoints

Shopper-scoped (any authenticated caller, own data only):

- `POST /api/contact-numbers` — register a mobile number → `{ contactNumberId, phoneNumber }`
- `GET /api/contact-numbers` — the caller's numbers
- `DELETE /api/contact-numbers/{contactNumberId}` — remove one
- `POST /api/orders` — place an order from catalog items → `{ orderId, status, total }`
- `GET /api/my-orders` — the caller's orders with notification outcomes
- `GET /api/orders/{orderId}/notifications` — what was sent for this order (each entry has its own `notificationId`)

Operator (Administrators role):

- `POST /api/orders/{orderId}/dispatch` — mark dispatched, notify, queue the follow-up
- `POST /api/orders/{orderId}/cancel` — cancel, notify, call off the pending follow-up
- `POST /api/notifications/{notificationId}/resend` — resend (requires an `Idempotency-Key` header) → `{ notificationId }`
- `DELETE /api/notifications/{notificationId}/content` — dispose of the message content at the provider
- `GET /api/notifications/reconciliation?from={ISO}&to={ISO}` — provider vs. eShop reconciliation

## Design notes

- **Failure isolation** — a message that cannot be sent never fails the order operation; the attempt
  is recorded (`sendFailed`) and the request still succeeds. No number on file ⇒ no message.
- **Owner scoping** — contact numbers and order notifications are filtered by the caller's buyer id
  (the token's name). Reading another shopper's order notifications returns `403`.
- **Idempotent resend** — the caller-supplied `Idempotency-Key` is stored on the produced
  notification; a repeat under the same key returns the same notification without sending again.
- **Privacy** — destination numbers are persisted (so re-sends can reach them) but never written to
  logs, and are not returned in API responses.
- **Persistence** — `ContactNumber` and `OrderNotification` are new aggregates on `CatalogContext`;
  `Order` gained an additive `Status` (`Placed` / `Dispatched` / `Cancelled`).

## Verifying end-to-end

Run in-memory on the assigned port block (see the run command in the PR/summary). Get a bearer token
from `POST /api/authenticate`, then exercise the endpoints above. Register only the two provided test
destinations (`TWILIO_TEST_TO_NUMBER`, a reachable Canadian number, and `TWILIO_UNREACHABLE_TO_NUMBER`,
a reserved US number the carrier refuses — an expected `undelivered` outcome, not a defect).
