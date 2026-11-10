# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.



## Pay by bank (Plaid)

Banking endpoints call Plaid's HTTP API directly (no SDK) from `Banking/`. Settings come from the `Plaid:` section:
`Plaid:ClientId`, `Plaid:Secret`, `Plaid:Environment` and the optional `Plaid:BaseUrl` (used verbatim when set).
Load them into user-secrets from the environment, never into a file:

```
dotnet user-secrets set "Plaid:ClientId" "$PLAID_CLIENT_ID" --project src/PublicApi
dotnet user-secrets set "Plaid:Secret" "$PLAID_SECRET" --project src/PublicApi
dotnet user-secrets set "Plaid:Environment" "$PLAID_ENV" --project src/PublicApi
```

Non-secret payment settings live in `BankPayment:` (`ExpressFee`, `ExpressLimit`, `AchLimit`).
The caller is always the JWT's name claim; support endpoints need the Administrators role. Errors the app cannot
recover from answer with a 4xx and `{ statusCode, message, reason }`.
