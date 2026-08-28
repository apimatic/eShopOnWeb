# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Order SMS notifications

The order-notification endpoints use the checked-in Twilio Lookups v2 and API v2010 OpenAPI
documents. The integration is a small HTTP client rather than a Twilio SDK.

Configure PublicApi through .NET configuration under these keys:

- `Twilio:AccountSid`
- `Twilio:AuthToken`
- `Twilio:FromNumber`
- `Twilio:MessagingServiceSid`
- `Twilio:BaseUrl` (optional messaging-API override)

For local development, copy environment variables into user-secrets without placing values in
the repository:

```powershell
dotnet user-secrets set 'Twilio:AccountSid' $env:TWILIO_ACCOUNT_SID --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'Twilio:AuthToken' $env:TWILIO_AUTH_TOKEN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'Twilio:FromNumber' $env:TWILIO_FROM_NUMBER --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'Twilio:MessagingServiceSid' $env:TWILIO_MESSAGING_SERVICE_SID --project src/PublicApi/PublicApi.csproj
```

Run PublicApi with `UseOnlyInMemoryDatabase=true` where SQL Server is unavailable. Authenticate at
`POST /api/authenticate`; use the returned bearer token for all notification routes. Shopper routes
use `demouser@microsoft.com` in the seeded development data, and operator routes require the seeded
`admin@microsoft.com` account.
