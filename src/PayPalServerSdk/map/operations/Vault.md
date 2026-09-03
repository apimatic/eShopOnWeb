<!-- Generated file — do not edit; regenerated with the SDK. -->

# Vault — operations

Accessor: `client.Vault` · Source: `Api/Vault.cs` · 6 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CreatePaymentToken

- **Auth**: `options.Oauth2`
- **Signature**: `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId` — nullable, no default → **must pass explicitly**
- **Returns**: `PaymentTokenResponse`
- **Error**: `SdkException<CreatePaymentTokenError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `PaymentTokenRequest` | `Models/PaymentTokenRequest.cs` |
| `PaymentTokenResponse` | `Models/PaymentTokenResponse.cs` |
| `CreatePaymentTokenError` | `Errors/CreatePaymentTokenError.cs` |
| `Error` | `Models/Error.cs` |

### CreateSetupToken

- **Auth**: `options.Oauth2`
- **Signature**: `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId` — nullable, no default → **must pass explicitly**
- **Returns**: `SetupTokenResponse`
- **Error**: `SdkException<CreateSetupTokenError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [400, 403, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SetupTokenRequest` | `Models/SetupTokenRequest.cs` |
| `SetupTokenResponse` | `Models/SetupTokenResponse.cs` |
| `CreateSetupTokenError` | `Errors/CreateSetupTokenError.cs` |
| `Error` | `Models/Error.cs` |

### DeletePaymentToken

- **Auth**: `options.Oauth2`
- **Signature**: `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<DeletePaymentTokenError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `DeletePaymentTokenError` | `Errors/DeletePaymentTokenError.cs` |
| `Error` | `Models/Error.cs` |

### GetPaymentToken

- **Auth**: `options.Oauth2`
- **Signature**: `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `PaymentTokenResponse`
- **Error**: `SdkException<GetPaymentTokenError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `PaymentTokenResponse` | `Models/PaymentTokenResponse.cs` |
| `GetPaymentTokenError` | `Errors/GetPaymentTokenError.cs` |
| `Error` | `Models/Error.cs` |

### GetSetupToken

- **Auth**: `options.Oauth2`
- **Signature**: `GetSetupToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `SetupTokenResponse`
- **Error**: `SdkException<GetSetupTokenError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SetupTokenResponse` | `Models/SetupTokenResponse.cs` |
| `GetSetupTokenError` | `Errors/GetSetupTokenError.cs` |
| `Error` | `Models/Error.cs` |

### ListCustomerPaymentTokens

- **Auth**: `options.Oauth2`
- **Signature**: `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - defaults: `pageSize` = `5`, `page` = `1`, `totalRequired` = `false`
- **Query params (wire ← C#)**: `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired`
- **Returns**: `CustomerVaultPaymentTokensResponse`
- **Error**: `SdkException<ListCustomerPaymentTokensError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CustomerVaultPaymentTokensResponse` | `Models/CustomerVaultPaymentTokensResponse.cs` |
| `ListCustomerPaymentTokensError` | `Errors/ListCustomerPaymentTokensError.cs` |
| `Error` | `Models/Error.cs` |

