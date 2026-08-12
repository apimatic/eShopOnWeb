# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Order SMS notifications (Twilio)

An additive capability that keeps shoppers informed by SMS as their orders progress, using
Twilio as the messaging provider. All endpoints are JWT-authenticated and routed under `/api/`.

### Endpoints

Shopper-scoped (any authenticated caller; acts only on the caller's own data):

- `POST /api/contact-numbers` — register a mobile number (provider-validated & canonicalised; returns `contactNumberId`)
- `GET /api/contact-numbers` — the caller's registered numbers
- `DELETE /api/contact-numbers/{contactNumberId}` — remove one
- `POST /api/orders` — place an order from catalog items (returns `orderId`); shopper is told it was placed
- `GET /api/my-orders` — the caller's orders, each with its notification outcomes
- `GET /api/orders/{orderId}/notifications` — notifications for the order (each carries `notificationId`)

Operator-only (`Administrators` role):

- `POST /api/orders/{orderId}/dispatch` — mark dispatched; tell the shopper; queue a "how did delivery go?" follow-up with the provider a few days out
- `POST /api/orders/{orderId}/cancel` — mark cancelled; tell the shopper; call off any pending follow-up
- `POST /api/notifications/{notificationId}/resend` — re-send (idempotency key in body; returns the new `notificationId`)
- `DELETE /api/notifications/{notificationId}/content` — redact the message text at the provider
- `GET /api/notifications/reconciliation?from={iso}&to={iso}` — provider-vs-eShop reconciliation over a range, scoped to `Twilio:FromNumber`

### Contract

The [Twilio OpenAPI specs](../../api-specs/twilio) are the authoritative contract. Two hand-written
clients (no SDK) live in [`../Infrastructure/Twilio`](../Infrastructure/Twilio):

- Messaging (`twilio_api_v2010`): send, schedule (`ScheduleType=fixed` + `SendAt`), fetch, cancel, redact (empty `Body`), and list for reconciliation.
- Lookups (`twilio_lookups_v2`): validate & canonicalise a number at registration.

### Configuration (`Twilio:` section)

Bind via user-secrets / environment — never hard-coded:

| Key | Source env var |
| --- | --- |
| `Twilio:AccountSid` | `TWILIO_ACCOUNT_SID` |
| `Twilio:AuthToken` | `TWILIO_AUTH_TOKEN` (secret) |
| `Twilio:FromNumber` | `TWILIO_FROM_NUMBER` |
| `Twilio:MessagingServiceSid` | `TWILIO_MESSAGING_SERVICE_SID` |
| `Twilio:BaseUrl` | optional override for the **messaging** API only |

Load them into user-secrets, e.g. `dotnet user-secrets set "Twilio:AccountSid" "$TWILIO_ACCOUNT_SID"`.

### Design notes

- A message that cannot be sent never fails the underlying order operation; it is recorded as a
  failed outcome. A shopper with no number on file is simply not messaged.
- The follow-up is scheduled **with the provider** (`SendAt`), not by an in-app timer, and is
  cancelled at the provider on order cancel so it can never reach a cancelled order's shopper.
- Reconciliation asks the provider for `From={Twilio:FromNumber}` messages only, so other
  account traffic is excluded at the source.
- Shopper numbers are stored in canonical E.164 and never written to logs; the auth token is
  never logged, returned, or committed.
