<!-- Generated file — do not edit; regenerated with the SDK. -->

# TransactionSearch — operations

Accessor: `client.TransactionSearch` · Source: `Api/TransactionSearch.cs` · 2 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### SearchBalances

- **Auth**: `options.Oauth2`
- **Signature**: `SearchBalances(string? asOfTime, string? currencyCode, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `asOfTime` — nullable, no default → **must pass explicitly**
  - `currencyCode` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `as_of_time` ← `asOfTime`, `currency_code` ← `currencyCode`
- **Returns**: `BalancesResponse`
- **Error**: `SdkException<SearchBalancesError>` — **Case A (typed)**
- **Error accessors**: `TryGetDefaultError(out DefaultError)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `BalancesResponse` | `Models/BalancesResponse.cs` |
| `SearchBalancesError` | `Errors/SearchBalancesError.cs` |
| `DefaultError` | `Models/DefaultError.cs` |

### SearchTransactions

- **Auth**: `options.Oauth2`
- **Signature**: `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 8 params (`transactionId` … `terminalId`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `fields` = `"transaction_info"`, `balanceAffectingRecordsOnly` = `"Y"`, `pageSize` = `100`, `page` = `1`
- **Query params (wire ← C#)**: `start_date` ← `startDate`, `end_date` ← `endDate`, `transaction_id` ← `transactionId`, `transaction_type` ← `transactionType`, `transaction_status` ← `transactionStatus`, `transaction_amount` ← `transactionAmount`, `transaction_currency` ← `transactionCurrency`, `payment_instrument_type` ← `paymentInstrumentType`, `store_id` ← `storeId`, `terminal_id` ← `terminalId`, `fields` ← `fields`, `balance_affecting_records_only` ← `balanceAffectingRecordsOnly`, `page_size` ← `pageSize`, `page` ← `page`
- **Returns**: `SearchResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SearchResponse` | `Models/SearchResponse.cs` |

