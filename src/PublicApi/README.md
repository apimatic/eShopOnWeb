# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The payment endpoints bind `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`,
`PayPal:Currency`, and optional `PayPal:BaseUrl`. The four supplied flat environment variables
are mapped to that section at startup. For local development they can also be copied to the
.NET user-secrets store without putting values in this repository:

```powershell
dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY --project src/PublicApi/PublicApi.csproj
```

Run PublicApi with `UseOnlyInMemoryDatabase=true` when SQL Server LocalDB is unavailable. Get a
JWT from `POST /api/authenticate`; the payment and order routes are then available in Swagger.

Directly accepting card number, expiry, and security code on the server is a PayPal Advanced
Cards flow and requires PCI DSS SAQ D compliance. The application sends those fields directly
to PayPal and never persists or logs them. Saved cards retain only PayPal's vault token and safe
display metadata.
