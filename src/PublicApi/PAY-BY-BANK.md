# Pay by bank (Plaid)

Endpoints live in `PayByBankEndpoints/`, the logic in `PayByBank/`. Plaid is only ever called through the
SDK in `sdk/plaid` (see `PayByBank/PlaidGateway.cs`, the one place that builds the client and turns Plaid
failures into 4xx/5xx answers with a reason).

## Configuration

| Key | Meaning |
| --- | --- |
| `Plaid:ClientId`, `Plaid:Secret` | API credentials |
| `Plaid:Environment` | `sandbox` or `production` |
| `Plaid:BaseUrl` | optional; when set it is used verbatim as the API base address |
| `PayByBank:ExpressFee` | optional flat fee for express payment (default `5.00`) |

Never put credential values in the repository. Load them into user-secrets from the environment
(`PLAID_CLIENT_ID`, `PLAID_SECRET`, `PLAID_ENV`):

```
dotnet user-secrets set "Plaid:ClientId"    "$PLAID_CLIENT_ID"  --project src/PublicApi
dotnet user-secrets set "Plaid:Secret"      "$PLAID_SECRET"     --project src/PublicApi
dotnet user-secrets set "Plaid:Environment" "$PLAID_ENV"        --project src/PublicApi
```

If a `Plaid:` key is unset the matching `PLAID_*` environment variable is used.

## Behaviour worth knowing

* **Identity** comes from the JWT (`ClaimTypes.Name`); support endpoints need the `Administrators` role.
* **Connections** are stored in `BankConnections`; Plaid access tokens are protected with ASP.NET data
  protection. Disconnecting calls `/item/remove` first (that is what stops Plaid billing) and keeps the
  row if Plaid fails, so it can be retried.
* **Speeds.** `standard` is ACH. `express` is Same Day ACH and is only offered when order + fee is at most
  $1,000,000 and the request arrives on a US bank business day before 14:45 ET (Plaid's 15:00 cutoff, with
  its recommended 15-minute margin). Otherwise Plaid would route it over standard rails and the shopper would
  pay the fee for nothing. Orders above the $10,000,000 single-ACH limit are split across several transfers,
  all authorised before any is created.
* **Shipping changes** re-price the open checkout; confirming charges the new total and re-checks the speed.
* **Confirm** is idempotent per checkout. `arrivesOn` is Plaid's `expected_funds_available_date`.
* **Cancel** cancels still-cancellable transfers and refunds the rest, in full (the express fee is part of the
  debit), and is safe to retry. Shipped orders answer 409.
* **Bank activity** keeps a local copy of `/transactions/sync` per connection (added, modified and removed,
  pending included; restarts on `TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION`). **Date look-ups** page
  `/transactions/get` straight from the bank and wait out `PRODUCT_NOT_READY` for banks connected without
  the transactions product.
* **Portfolio**: Plaid reports an option's `quantity` as units (contracts × 100); the API reports `contracts`.
* The SDK serialises date-typed request fields as timestamps; `PlaidGateway` trims `start_date`/`end_date` to
  `YYYY-MM-DD` with an SDK hook, and the SDK needs the `Plaid-Version` header configured.
* The SDK needs the .NET 10 SDK to build (`global.json`); the apps still target net8.0.
