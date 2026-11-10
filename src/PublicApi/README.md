# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Pay by bank (Plaid)

The pay-by-bank endpoints call Plaid through the SDK in `sdk/plaid`. Settings come from the `Plaid:` section:
`Plaid:ClientId`, `Plaid:Secret`, `Plaid:Environment` (`sandbox` or `production`) and the optional
`Plaid:BaseUrl`, which when set is used verbatim as the API's base address. Keep the credentials in .NET
user-secrets (`dotnet user-secrets set Plaid:ClientId ... --project src/PublicApi`), never in the repository.

`BankPayment:ExpressFee` and `BankPayment:SameDayLimit` tune the express fee and the same-day ACH ceiling.
The `PublicApiIntegrationTests` pay-by-bank tests run against the Plaid sandbox and need `PLAID_CLIENT_ID` and `PLAID_SECRET` set.
