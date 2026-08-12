# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Order notifications by SMS (Twilio)

An additive capability that keeps shoppers informed by text message as their orders progress,
using Twilio as the messaging provider. It does not replace the existing catalog/basket/order
flow. All endpoints are JWT-authenticated; the caller's identity comes from the token.

### Endpoints

Shopper-scoped (any authenticated user; acts only on the caller's own data):

| Method & route | Purpose |
| --- | --- |
| `POST /api/contact-numbers` | Register a mobile number (validated with the provider; canonical form stored). Returns `contactNumberId`. |
| `GET /api/contact-numbers` | The caller's registered numbers. |
| `DELETE /api/contact-numbers/{contactNumberId}` | Remove one of the caller's numbers. |
| `POST /api/orders` | Place an order from catalog item ids + quantities. Returns `orderId`. |
| `GET /api/my-orders` | The caller's orders, each with where its notifications got to. |
| `GET /api/orders/{orderId}/notifications` | The notifications for one order (each carries its own `notificationId`). |

Operator actions (restricted to the `Administrators` role):

| Method & route | Purpose |
| --- | --- |
| `POST /api/orders/{orderId}/dispatch` | Mark dispatched; notify shopper; queue a delivery follow-up a few days out. |
| `POST /api/orders/{orderId}/cancel` | Cancel; notify shopper; call off any not-yet-sent follow-up. |
| `POST /api/notifications/{notificationId}/resend` | Re-send a message that did not reach the shopper. Body: `{ "idempotencyKey": "..." }`. Returns the produced `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | Dispose of a message's content at the provider and locally (the send record and outcome survive). |
| `GET /api/notifications/reconciliation?from={iso}&to={iso}` | Reconcile the provider's record of messages sent from `Twilio:FromNumber` against what eShop believes it sent. |

A message that cannot be sent never fails the underlying order operation. A shopper with no
number on file is simply not messaged. Phone numbers are never written to the logs.

### Configuration

Bind the `Twilio:` configuration section (do not hard-code the values — load them from user-secrets
or the environment so the same build can run against a different account):

- `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`
- `Twilio:BaseUrl` — optional override for the **messaging** API base address only (send / read /
  reconcile). It does not govern the lookup API, which is served from a different host.

Load the credentials into user-secrets (values come from the environment, never a repo file):

```bash
dotnet user-secrets --project src/PublicApi set "Twilio:AccountSid" "$TWILIO_ACCOUNT_SID"
dotnet user-secrets --project src/PublicApi set "Twilio:AuthToken" "$TWILIO_AUTH_TOKEN"
dotnet user-secrets --project src/PublicApi set "Twilio:FromNumber" "$TWILIO_FROM_NUMBER"
dotnet user-secrets --project src/PublicApi set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
```

### Running on this machine

```bash
DOTNET_ROLL_FORWARD=Major \
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:9383;http://localhost:9384" \
UseOnlyInMemoryDatabase=true \
dotnet run --project src/PublicApi --no-launch-profile
```

The in-memory store is per-host and is lost on restart, so create, dispatch and cancel orders
within a single run. Get a bearer token from `POST /api/authenticate` first.
