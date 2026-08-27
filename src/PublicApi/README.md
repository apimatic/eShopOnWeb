# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Order SMS notifications

The order-notification endpoints use the existing JWT name claim for shopper ownership and the
existing `Administrators` role for dispatch, cancellation, resend, content disposal, and
reconciliation. Provider delivery state is refreshed from Twilio when orders or notifications are
read because this host has no public callback URL.

Configuration is bound from the `Twilio` section:

- `Twilio:AccountSid`
- `Twilio:AuthToken`
- `Twilio:FromNumber`
- `Twilio:MessagingServiceSid`
- `Twilio:BaseUrl` (optional messaging API override; Lookup continues to use its own host)

For local development, put credential values in user-secrets for `PublicApi.csproj`. Do not add
them to an appsettings file. The HTTP surface is:

- `POST`, `GET`, and `DELETE /api/contact-numbers`
- `POST /api/orders`, `GET /api/my-orders`, and `GET /api/orders/{orderId}/notifications`
- `POST /api/orders/{orderId}/dispatch` and `/cancel`
- `POST /api/notifications/{notificationId}/resend`
- `DELETE /api/notifications/{notificationId}/content`
- `GET /api/notifications/reconciliation?from=...&to=...`
