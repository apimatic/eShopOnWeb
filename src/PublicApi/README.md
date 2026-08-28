# API Endpoints

This folder demonstrates both individual API endpoint classes and controller-based endpoints.

## Order SMS notifications

PublicApi exposes the complete JWT-authenticated notification workflow:

- shopper contact numbers: `POST/GET /api/contact-numbers` and `DELETE /api/contact-numbers/{id}`
- shopper orders: `POST /api/orders`, `GET /api/my-orders`, and `GET /api/orders/{id}/notifications`
- administrator order actions: `POST /api/orders/{id}/dispatch` and `POST /api/orders/{id}/cancel`
- administrator notification actions: resend, content disposal, and reconciliation under `/api/notifications`

The Twilio integration binds `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`,
`Twilio:MessagingServiceSid`, and the optional messaging API override `Twilio:BaseUrl`.
PublicApi also maps `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN`, `TWILIO_FROM_NUMBER`, and
`TWILIO_MESSAGING_SERVICE_SID` into those configuration keys. Use user-secrets or environment
configuration; do not put credential values in an appsettings file.

For local in-memory operation, set `UseOnlyInMemoryDatabase=true`. Authenticate at
`POST /api/authenticate` and pass the returned JWT as a Bearer token. The seeded shopper is
`demouser@microsoft.com`; the seeded administrator is `admin@microsoft.com`.
