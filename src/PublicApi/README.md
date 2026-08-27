# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Order SMS notifications

The order-notification API uses the hand-written Twilio client in `Infrastructure/Twilio`. Its contracts come from these repository specifications:

- `api-specs/twilio/twilio_lookups_v2/twilio_lookups_v2.yaml` for destination validation and canonicalization.
- `api-specs/twilio/twilio_api_v2010/twilio_api_v2010.yaml` for send, schedule, fetch, cancel, redact, and sender-filtered reconciliation operations.

Do not add a Twilio SDK package. Configure `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, and `Twilio:MessagingServiceSid` through user-secrets. `Twilio:BaseUrl` is an optional messaging-API-only override; Lookups always uses the host in its own OpenAPI document.

For local verification, set `UseOnlyInMemoryDatabase=true`, run PublicApi, authenticate separately as `demouser@microsoft.com` and `admin@microsoft.com`, and use the returned JWTs. The flow is intentionally split across independently invocable routes:

1. Register and list shopper destinations with `POST/GET /api/contact-numbers`.
2. Place a catalog-backed order with `POST /api/orders`.
3. Inspect it with `GET /api/my-orders` or `GET /api/orders/{orderId}/notifications`.
4. As an administrator, dispatch or cancel with `POST /api/orders/{orderId}/dispatch|cancel`.
5. As an administrator, resend with a JSON `idempotencyKey`, dispose content, or reconcile a complete ISO-8601 range through the `/api/notifications` routes.

The in-memory database is per host and per process. Keep all steps in one PublicApi run.
