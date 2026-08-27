# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Order SMS notifications

The JWT-authenticated notification API adds these routes without changing the existing
catalog endpoints:

- Shopper: `POST|GET /api/contact-numbers`, `DELETE /api/contact-numbers/{id}`,
  `POST /api/orders`, `GET /api/my-orders`, and
  `GET /api/orders/{id}/notifications`.
- Administrator: `POST /api/orders/{id}/dispatch`, `POST /api/orders/{id}/cancel`,
  `POST /api/notifications/{id}/resend`,
  `DELETE /api/notifications/{id}/content`, and
  `GET /api/notifications/reconciliation?from=...&to=...`.

Configuration is bound from `Twilio:AccountSid`, `Twilio:AuthToken`,
`Twilio:FromNumber`, `Twilio:MessagingServiceSid`, and optional `Twilio:BaseUrl`.
Keep credentials in .NET user-secrets or another external configuration provider. The
base URL override applies only to the classic messaging API; phone validation always uses
Twilio Lookup. In environments without SQL Server, set `UseOnlyInMemoryDatabase=true`.
