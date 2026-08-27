# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Order SMS notifications

The order-notification endpoints are JWT authenticated and live under `/api/`:

- shopper: `POST|GET|DELETE /contact-numbers`, `POST /orders`, `GET /my-orders`, and `GET /orders/{orderId}/notifications`;
- administrator: `POST /orders/{orderId}/dispatch`, `POST /orders/{orderId}/cancel`, `POST /notifications/{notificationId}/resend`, `DELETE /notifications/{notificationId}/content`, and `GET /notifications/reconciliation`.

`POST /orders` accepts catalog item ids/quantities plus the existing order model's shipping address. The dispatch follow-up is scheduled with the provider for three days later. Reconciliation uses the provider's native open interval `(from,to)` and always supplies this application's configured sending number in the provider request.

Configuration binds from `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, and optional `Twilio:BaseUrl`. Keep values in user-secrets or another configuration secret source. `Twilio:BaseUrl` overrides messaging calls only; phone-number lookup remains on its independent provider host.
