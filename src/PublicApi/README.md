# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Pay by bank (Plaid)

The pay-by-bank endpoints (`/api/banks`, `/api/bank/*`, `/api/checkout/bank-payment*`, `/api/orders/{id}/cancel`,
`/api/support/shoppers/{email}/*`, `/api/portfolio`) call Plaid through the SDK in `sdk/plaid`.
Settings are read from the `Plaid:` configuration section: `Plaid:ClientId`, `Plaid:Secret`,
`Plaid:Environment` (`sandbox` or `production`) and the optional `Plaid:BaseUrl` (used verbatim when set).
Keep the values out of the repository, e.g. with user-secrets:

```
dotnet user-secrets set "Plaid:ClientId" "<client id>"
dotnet user-secrets set "Plaid:Secret" "<secret>"
dotnet user-secrets set "Plaid:Environment" "sandbox"
```

`BankPayment:ExpressFee` (default 5.00) is the fee for express (same-day) payments. Building needs the .NET 10 SDK
(the Plaid SDK uses C# 14); the apps still target net8.0.
