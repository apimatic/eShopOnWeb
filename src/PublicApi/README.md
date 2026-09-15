# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Order SMS configuration

The integration uses the Twilio Lookups v2 and Messaging 2010 contracts in `api-specs/` and does not use a Twilio SDK. Configure these keys in the `Twilio` configuration section:

- `Twilio:AccountSid`
- `Twilio:AuthToken`
- `Twilio:FromNumber`
- `Twilio:MessagingServiceSid`
- `Twilio:BaseUrl` (optional Messaging API override; it never changes the Lookups host)

For local development, keep credentials outside the repository:

```powershell
dotnet user-secrets set "Twilio:AccountSid" $env:TWILIO_ACCOUNT_SID --project src/PublicApi
dotnet user-secrets set "Twilio:AuthToken" $env:TWILIO_AUTH_TOKEN --project src/PublicApi
dotnet user-secrets set "Twilio:FromNumber" $env:TWILIO_FROM_NUMBER --project src/PublicApi
dotnet user-secrets set "Twilio:MessagingServiceSid" $env:TWILIO_MESSAGING_SERVICE_SID --project src/PublicApi
```

Run PublicApi with `UseOnlyInMemoryDatabase=true` where SQL Server is unavailable. Authenticate at `POST /api/authenticate`; the demo shopper is `demouser@microsoft.com` and the operator is `admin@microsoft.com`.
