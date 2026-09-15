# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Order SMS notifications

The JWT-authenticated order notification API is exposed under `/api/`:

- shoppers manage `/contact-numbers`, place `/orders`, and read `/my-orders` and an owned order's `/notifications`;
- administrators dispatch or cancel orders, resend failed notifications, dispose of provider content, and run reconciliation reports.

The integration reads `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`,
`Twilio:MessagingServiceSid`, and the optional messaging-API override `Twilio:BaseUrl` from
configuration. For local development, copy environment values into user-secrets without putting
their values in this repository:

```powershell
dotnet user-secrets set "Twilio:AccountSid" $env:TWILIO_ACCOUNT_SID --project src/PublicApi
dotnet user-secrets set "Twilio:AuthToken" $env:TWILIO_AUTH_TOKEN --project src/PublicApi
dotnet user-secrets set "Twilio:FromNumber" $env:TWILIO_FROM_NUMBER --project src/PublicApi
dotnet user-secrets set "Twilio:MessagingServiceSid" $env:TWILIO_MESSAGING_SERVICE_SID --project src/PublicApi
```

Delivery status is refreshed from the provider when notifications are read because this host has
no public callback URL. The reconciliation endpoint follows all provider pages and requests only
messages sent from `Twilio:FromNumber`.
