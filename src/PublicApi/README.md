# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Pay by bank (Plaid)

The bank endpoints call Plaid's HTTP API directly (no SDK). Configure the `Plaid:` section with
`Plaid:ClientId`, `Plaid:Secret`, `Plaid:Environment` and optionally `Plaid:BaseUrl` (used verbatim when set).
Keep the values out of the repository, e.g. with user-secrets:

    dotnet user-secrets set "Plaid:ClientId" "<client id>" --project src/PublicApi
    dotnet user-secrets set "Plaid:Secret" "<secret>" --project src/PublicApi
    dotnet user-secrets set "Plaid:Environment" "sandbox" --project src/PublicApi

`BankPayment:ExpressFee` (default 5.00) sets the fee for express (same-day) payments.
