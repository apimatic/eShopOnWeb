# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Order SMS notifications

The order-notification endpoints use JWT authentication. Shopper routes use the JWT name claim;
dispatch, cancellation, resend, content disposal, and reconciliation additionally require the
existing `Administrators` role.

Twilio is bound from the `Twilio` configuration section:

- `Twilio:AccountSid`
- `Twilio:AuthToken`
- `Twilio:FromNumber`
- `Twilio:MessagingServiceSid`
- `Twilio:BaseUrl` (optional messaging REST API override)

Lookup always uses Twilio Lookup v2 on its own host. The base URL override applies to every
Messages-resource request, including create, fetch, update, and paged reconciliation calls.
Do not put credentials in appsettings files; for development, use .NET user-secrets.

The added routes are:

- `POST|GET /api/contact-numbers` and `DELETE /api/contact-numbers/{id}`
- `POST /api/orders`, `GET /api/my-orders`, and `GET /api/orders/{id}/notifications`
- `POST /api/orders/{id}/dispatch` and `POST /api/orders/{id}/cancel`
- `POST /api/notifications/{id}/resend`
- `DELETE /api/notifications/{id}/content`
- `GET /api/notifications/reconciliation?from={ISO-8601}&to={ISO-8601}`

Resend accepts `{ "idempotencyKey": "..." }`. A failed/undelivered message can be retried with
a new key; repeating the same notification and key returns the first retry's identifier without
sending again.
