# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Pay by bank (Plaid)

Plaid is called over plain HTTP (no SDK). Settings come from the `Plaid:` section: `Plaid:ClientId`,
`Plaid:Secret`, `Plaid:Environment` (`sandbox`, `development`, `production`) and the optional
`Plaid:BaseUrl`, which when set is used verbatim as the API's base address. Load them with
`dotnet user-secrets set "Plaid:ClientId" "<value>" --project src/PublicApi` (never commit values).

Optional tuning: `BankPayment:ExpressFee` (default 5.00), `BankPayment:ExpressLimit` (default 1,000,000, Same Day ACH
ceiling) and `BankPayment:TransferLimit` (default 10,000,000, Plaid's per-transfer ceiling; larger orders are split
across several transfers).
