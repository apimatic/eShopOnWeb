# Order notifications by SMS (Twilio)

An additive capability that keeps eShopOnWeb shoppers informed by text message as their orders
progress. It adds a shopper's mobile contact details, the messages that go out as an order moves,
and the operator's view of what actually reached the customer. It does not replace the existing
catalog/basket/order flow.

All capabilities are exposed as JWT-authenticated HTTP endpoints on the **PublicApi** project under
`/api/`. The caller's identity comes from the token (`ClaimTypes.Name`).

## Twilio integration

Every Twilio interaction is built **by hand against the OpenAPI specifications in `api-specs/twilio`**
— no pre-built Twilio SDK is used. Auth is HTTP Basic (`AccountSid:AuthToken`), the spec's
`accountSid_authToken` scheme.

| Capability | Spec | Operation |
|---|---|---|
| Validate & canonicalize a number | `twilio_lookups_v2` | `GET https://lookups.twilio.com/v2/PhoneNumbers/{number}` → `valid`, `phone_number` |
| Send / schedule a message | `twilio_api_v2010` | `POST /2010-04-01/Accounts/{sid}/Messages.json` (`To`,`Body`,`From`; scheduling adds `MessagingServiceSid`,`ScheduleType=fixed`,`SendAt`) |
| Cancel a scheduled message | `twilio_api_v2010` | `POST .../Messages/{sid}.json` `Status=canceled` |
| Dispose (redact) content | `twilio_api_v2010` | `POST .../Messages/{sid}.json` `Body=""` (keeps the record) |
| Fetch current delivery state | `twilio_api_v2010` | `GET .../Messages/{sid}.json` |
| Reconcile | `twilio_api_v2010` | `GET .../Messages.json?From={FromNumber}&DateSent>=…&DateSent<=…` (paged) |

The messaging API base URL is `https://api.twilio.com` by default and is overridden verbatim by
`Twilio:BaseUrl` when set. Lookups is served from its own host and is **not** governed by that setting.
There is no public callback URL for this app, so delivery outcomes are obtained by **asking the
provider** (fetch/list), never by receiving a webhook.

Code: gateway `Infrastructure/Services/Twilio/TwilioMessagingGateway.cs` (implements the
provider-agnostic `ApplicationCore/Interfaces/ISmsGateway`); orchestration in
`ApplicationCore/Services/{ContactNumberService,OrderNotificationService,NotificationOperationsService}.cs`;
entities in `ApplicationCore/Entities/NotificationAggregate`.

## Configuration

Bind from the `Twilio:` section — **values are never committed**; load them into .NET user-secrets or
environment:

| Key | Env var |
|---|---|
| `Twilio:AccountSid` | `TWILIO_ACCOUNT_SID` |
| `Twilio:AuthToken` | `TWILIO_AUTH_TOKEN` (secret — never logged/returned) |
| `Twilio:FromNumber` | `TWILIO_FROM_NUMBER` |
| `Twilio:MessagingServiceSid` | `TWILIO_MESSAGING_SERVICE_SID` |
| `Twilio:BaseUrl` | *(optional messaging-API override)* |

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
```

## Endpoints

Shopper-scoped (any authenticated caller; acts only on the caller's own data):

| Method & route | Purpose |
|---|---|
| `POST /api/contact-numbers` | Register a mobile number (validated + canonicalized). Returns `contactNumberId`. |
| `GET /api/contact-numbers` | The caller's registered numbers. |
| `DELETE /api/contact-numbers/{contactNumberId}` | Remove one (nothing is sent to it again). |
| `POST /api/orders` | Place an order from catalog item ids + quantities. Returns `orderId`. |
| `GET /api/my-orders` | The caller's orders, each with its notifications' delivery state. |
| `GET /api/orders/{orderId}/notifications` | Notifications for one order (each with `notificationId`). Owner or operator. |

Operator-only (administrator role):

| Method & route | Purpose |
|---|---|
| `POST /api/orders/{orderId}/dispatch` | Mark dispatched → "on its way" + schedule a delivery-feedback follow-up a few days out. |
| `POST /api/orders/{orderId}/cancel` | Cancel → notify + call off the not-yet-sent follow-up. |
| `POST /api/notifications/{notificationId}/resend` | Re-send a message that did not reach the shopper. Idempotent on `idempotencyKey` (body field or `Idempotency-Key` header). Returns the new `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | Dispose of a message's content at the provider (record survives). |
| `GET /api/notifications/reconciliation?from={iso}&to={iso}` | Provider's messages from `Twilio:FromNumber` over the range vs eShop's records. |

## Guarantees

- A messaging failure never fails the underlying order operation; a shopper with no number on file
  is simply not messaged (recorded as `NotSent`).
- A cancelled order's queued follow-up is called off before it can go out.
- Numbers are stored in canonical E.164 form and are **never written to logs**; the auth token is
  never logged, returned, or committed.
- One shopper can never see, use, or delete another's numbers or orders.

See `VERIFY.md` in this folder for a runnable end-to-end verification walkthrough.
