# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Order SMS notifications

The order-notification endpoints use JWT identity for shopper ownership and the existing
`Administrators` role for dispatch, cancellation, resend, content disposal, and reconciliation.
Twilio settings are bound from the `Twilio` section:

- `Twilio:AccountSid`
- `Twilio:AuthToken`
- `Twilio:FromNumber`
- `Twilio:MessagingServiceSid`
- `Twilio:BaseUrl` (optional messaging API override; Lookup always uses its own provider host)

For local development, copy credentials from the environment into user-secrets without putting
their values in the repository:

```powershell
dotnet user-secrets set "Twilio:AccountSid" $env:TWILIO_ACCOUNT_SID --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:AuthToken" $env:TWILIO_AUTH_TOKEN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:FromNumber" $env:TWILIO_FROM_NUMBER --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:MessagingServiceSid" $env:TWILIO_MESSAGING_SERVICE_SID --project src/PublicApi/PublicApi.csproj
```

Run locally with the in-memory database:

```powershell
$env:DOTNET_ROLL_FORWARD="Major"
$env:UseOnlyInMemoryDatabase="true"
dotnet run --project src/PublicApi/PublicApi.csproj --launch-profile PublicApi
```

Authenticate at `POST /api/authenticate`, then drive the flow through the routes under
`/api/contact-numbers`, `/api/orders`, and `/api/notifications`. Notification query endpoints poll
Twilio because this application has no public callback URL. Delivery follow-ups are scheduled with
Twilio three days ahead; the local hosted worker only retries cancellation requests and never sends
scheduled messages itself.
