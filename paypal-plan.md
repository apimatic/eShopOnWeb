# PayPal Integration Plan — eShopOnWeb PublicApi

## 1. Scope & Sequence

| Step | What | Operations used |
|------|------|----------------|
| 1 | Install NuGet package | — |
| 2 | Client registration & auth wiring in DI | `PayPalServerSdkClient`, `PayPalServerSdkClientOptions` |
| 3 | Authorize payment (card or vault token) | `Orders.CreateOrder` → `Orders.AuthorizeOrder` |
| 4 | Capture an authorization | `Payments.CaptureAuthorizedPayment` |
| 5 | Re-authorize a stale authorization | `Payments.GetAuthorizedPayment`, `Payments.ReauthorizePayment` |
| 6 | Void an authorization | `Payments.VoidPayment` |
| 7 | Refund a capture (full or partial) | `Payments.RefundCapturedPayment` |
| 8 | Vault a card | `Vault.CreatePaymentToken` |
| 9 | List vaulted payment methods | `Vault.ListCustomerPaymentTokens` (manual page loop) |
| 10 | Delete a vaulted payment method | `Vault.DeletePaymentToken` |
| 11 | Pay with a vaulted card | `Orders.CreateOrder` → `Orders.AuthorizeOrder` (vault token path) |
| 12 | Transaction search / reconciliation | `TransactionSearch.SearchTransactions` (manual page loop) |
| 13 | Error boundary | `SdkException<TError>`, `JsonException` handling |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

---

### A. NuGet Package

```
dotnet add package AsadAli.Checkout.Sdk
```

Install **version-less** — do not pin a version. Source: `paypal-getting-started` skill (SDK identity table).

---

### B. Client Construction & Auth

**Namespaces required for this section:**

| Type | Namespace |
|------|-----------|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` | `PayPalServerSdk` |
| `ServerEnvironment` | `PayPalServerSdk.Servers` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |

**`PayPalServerSdkClientOptions` relevant properties** (source: `PayPalServerSdkClientOptions.cs`):

| Property | Type | Notes |
|----------|------|-------|
| `Environment` | `ServerEnvironment` | Always set to `ServerEnvironment.Sandbox` — the only member in v1.0.1 (see blocker B1) |
| `Oauth2` | `OAuth2ClientCredentials?` | Set `ClientId` and `ClientSecret` |
| `Server` | `ServerOptions` | Override base URL via `options.Server.Default.Sandbox.BaseUrl` |

**`OAuth2ClientCredentials` fields** (source: `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`):

| Field | Type | Notes |
|-------|------|-------|
| `ClientId` | `string` (required) | Maps to `PayPal:ClientId` config |
| `ClientSecret` | `string` (required) | Maps to `PayPal:ClientSecret` config |
| `Scope` | `string?` | Leave null |

**Base URL configuration** (source: `ServerOptions.cs`, `Servers/DefaultOptions.cs`):

`ServerOptions.Default` → `DefaultOptions`; `DefaultOptions.Sandbox` → `SandboxOptions`; `SandboxOptions.BaseUrl: string` (default `"https://api-m.sandbox.paypal.com"`).

```
options.Server.Default.Sandbox.BaseUrl = baseUrl;
```

**Environment mapping** (BLOCKER B1 — see §5):

| `PayPal:Environment` config value | `options.Environment` | `BaseUrl` to set |
|-----------------------------------|-----------------------|-----------------|
| `"sandbox"` | `ServerEnvironment.Sandbox` | `"https://api-m.sandbox.paypal.com"` (default, no override needed unless `PayPal:BaseUrl` set) |
| `"production"` | `ServerEnvironment.Sandbox` (only available value) | `"https://api-m.paypal.com"` |
| `PayPal:BaseUrl` set | `ServerEnvironment.Sandbox` | Use `PayPal:BaseUrl` value; overrides environment mapping |

**Token endpoint** (source: `AuthSchemes.cs`): `/v1/oauth2/token` relative to the configured base URL. The SDK fetches tokens automatically — no manual token call needed.

**DI registration:**

```csharp
services.AddPayPalServerSdkClient(o =>
{
    o.Environment = ServerEnvironment.Sandbox;
    o.Oauth2 = new OAuth2ClientCredentials { ClientId = ..., ClientSecret = ... };
    o.Server.Default.Sandbox.BaseUrl = resolvedBaseUrl; // computed from PayPal:Environment + PayPal:BaseUrl
});
```

Source: `sdk-map.md` (Getting a client), `ServerOptions.cs`, `DefaultOptions.cs`, `OAuth2ClientCredentials.cs`.

---

### C. Operations — Authorize Payment (Op 1 / Op 9)

**Step 1 — CreateOrder** (controller: `client.Orders`, source: `map/operations/Orders.md`)

```
CreateOrder(
    string? payPalMockResponse,        // pass null
    string? payPalRequestId,           // idempotency key — same key = same order returned
    string? payPalPartnerAttributionId,// pass null
    string? payPalClientMetadataId,    // pass null
    string? payPalAuthAssertion,       // pass null
    OrderRequest body,                 // required, NOT nullable
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

Returns: `PayPalServerSdk.Models.Order`

**`OrderRequest`** (source: `map/models/records-1-Ac-Pa.md`):

| Field (wire name) | Type | Required? |
|-------------------|------|-----------|
| `Intent (intent)` | `CheckoutPaymentIntent` | required |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | required |
| `PaymentSource (payment_source)` | `PaymentSource?` | optional — leave null for two-call flow |
| `Payer (payer)` | `Payer?` | optional |

**`CheckoutPaymentIntent`** (namespace: `PayPalServerSdk.Models.Enums`): `CheckoutPaymentIntent.Authorize` (wire: `"AUTHORIZE"`)

**`PurchaseUnitRequest`** (source: `map/models/records-1-Ac-Pa.md`):

| Field (wire name) | Type | Required? |
|-------------------|------|-----------|
| `Amount (amount)` | `AmountWithBreakdown` | required |
| `InvoiceId (invoice_id)` | `string?` | optional — set to your order ID for reconciliation |

**`AmountWithBreakdown`** (source: `map/models/records-1-Ac-Pa.md`):

| Field (wire name) | Type | Required? |
|-------------------|------|-----------|
| `CurrencyCode (currency_code)` | `string` | required — e.g. `"USD"` from `PayPal:Currency` config |
| `Value (value)` | `string` | required — decimal as string, e.g. `"12.50"` |

Extract from response: `order.Id` = PayPal order ID.

Error: `SdkException<PayPalServerSdk.Errors.CreateOrderError>` — Case A. Accessor: `TryGetError(out PayPalServerSdk.Models.Error error)` [400, 401, 422]. Fallback: `TryGetRawError(out RawError)`.

**Step 2 — AuthorizeOrder** (controller: `client.Orders`, source: `map/operations/Orders.md`)

```
AuthorizeOrder(
    string id,                         // PayPal order ID from CreateOrder response
    string? payPalMockResponse,        // pass null
    string? payPalRequestId,           // idempotency key — use same key per authorization attempt
    string? payPalClientMetadataId,    // pass null
    string? payPalAuthAssertion,       // pass null
    OrderAuthorizeRequest? body,       // card or vault token details
    string? prefer = "return=minimal", // MUST pass "return=representation" to get auth ID in body
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

Returns: `PayPalServerSdk.Models.OrderAuthorizeResponse`

**`OrderAuthorizeRequest`** (source: `map/models/records-1-Ac-Pa.md`):

| Field (wire name) | Type | Required? |
|-------------------|------|-----------|
| `PaymentSource (payment_source)` | `OrderAuthorizeRequestPaymentSource?` | optional |

**`OrderAuthorizeRequestPaymentSource`** (source: `map/models/records-1-Ac-Pa.md`):

| Field (wire name) | Type | Notes |
|-------------------|------|-------|
| `Card (card)` | `CardRequest?` | for direct card payment (Op 1) |
| `Token (token)` | `Token?` | for billing-agreement tokens only (not vault payment tokens) |

**Card payment (Op 1) — `CardRequest`** (source: `map/models/records-1-Ac-Pa.md`):

| Field (wire name) | Type | Notes |
|-------------------|------|-------|
| `Name (name)` | `string?` | cardholder name |
| `Number (number)` | `string?` | card number, e.g. `"4111111111111111"` |
| `Expiry (expiry)` | `string?` | format `"YYYY-MM"`, e.g. `"2026-12"` — combine expiry year + month |
| `SecurityCode (security_code)` | `string?` | CVV |
| `BillingAddress (billing_address)` | `Address?` | see Address fields below |
| `VaultId (vault_id)` | `string?` | for vaulted card (Op 9) — set this, leave Number/Expiry/SecurityCode null |
| `StoredCredential (stored_credential)` | `CardStoredCredential?` | set for MIT (merchant-initiated) when using vault |

**PCI compliance note**: Passing card number, CVV, and expiry directly requires PCI SAQ D compliance (per `CardRequest` SDK doc comment). Source: `map/models/records-1-Ac-Pa.md`.

**`Address`** (source: `map/models/records-1-Ac-Pa.md`):

| Field (wire name) | Type | Required? |
|-------------------|------|-----------|
| `CountryCode (country_code)` | `string` | required, e.g. `"US"` |
| `AddressLine1 (address_line_1)` | `string?` | street |
| `AdminArea2 (admin_area_2)` | `string?` | city |
| `AdminArea1 (admin_area_1)` | `string?` | state |
| `PostalCode (postal_code)` | `string?` | postal code |

**Vaulted card (Op 9) — `CardRequest`**: Set only `VaultId = vaultTokenId`; leave `Number`, `Expiry`, `SecurityCode` null.

**Response extraction** (prefer must be `"return=representation"`):

| Data point | Property path |
|------------|---------------|
| PayPal order ID | `response.Id` |
| Authorization ID | `response.PurchaseUnits[0].Payments.Authorizations[0].Id` |
| Authorization status | `response.PurchaseUnits[0].Payments.Authorizations[0].Status` (`AuthorizationStatus?`) |
| Expiration time | `response.PurchaseUnits[0].Payments.Authorizations[0].ExpirationTime` (ISO 8601 string) |

Types: `OrderAuthorizeResponse` → `PurchaseUnit.Payments` (`PaymentCollection`) → `Authorizations` (`IReadOnlyList<AuthorizationWithAdditionalData>?`). Source: `map/models/records-1-Ac-Pa.md`, `map/models/records-2-Pa-Ve.md`.

Error: `SdkException<PayPalServerSdk.Errors.AuthorizeOrderError>` — Case A. Accessor: `TryGetError(out PayPalServerSdk.Models.Error error)` [400, 401, 403, 404, 422, 500]. Fallback: `TryGetRawError(out RawError)`.

**Idempotency**: Pass a stable `payPalRequestId` (e.g. a UUID tied to the checkout session) to both `CreateOrder` and `AuthorizeOrder`. The same `payPalRequestId` on a retry returns the same result rather than creating a new resource.

**`AuthorizationStatus` enum** (namespace: `PayPalServerSdk.Models.Enums`, source: `map/models/enums.md`):

| C# member | Wire value | Meaning |
|-----------|------------|---------|
| `Created` | `CREATED` | Successfully authorized, within 3-day honor period |
| `Captured` | `CAPTURED` | Already captured |
| `Denied` | `DENIED` | Declined |
| `PartiallyCaptured` | `PARTIALLY_CAPTURED` | Partial capture done |
| `Voided` | `VOIDED` | Voided |
| `Pending` | `PENDING` | Pending — see `StatusDetails.Reason` |

**Sandbox test card**: Visa `4111 1111 1111 1111` — any future expiry, any CVV, any billing address.

---

### D. Capture an Authorization (Op 2)

**Controller**: `client.Payments` · Source: `map/operations/Payments.md`

```
CaptureAuthorizedPayment(
    string authorizationId,            // authorization ID from Op 1
    string? payPalMockResponse,        // pass null
    string? payPalRequestId,           // idempotency key
    string? payPalAuthAssertion,       // pass null
    CaptureRequest? body,              // pass null for full capture, or set Amount for partial
    string? prefer = "return=minimal", // use "return=representation" to get fee breakdown
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

Returns: `PayPalServerSdk.Models.CapturedPayment`

**`CaptureRequest`** (source: `map/models/records-1-Ac-Pa.md`):

| Field (wire name) | Type | Notes |
|-------------------|------|-------|
| `Amount (amount)` | `Money?` | omit (null body or null Amount) for full capture |
| `FinalCapture (final_capture)` | `bool? = false` | set true to close the authorization |

**Response extraction** (with `prefer = "return=representation"`):

| Data point | Property path | Type |
|------------|---------------|------|
| Capture ID | `result.Id` | `string?` |
| Captured amount | `result.Amount` | `Money?` |
| PayPal fee | `result.SellerReceivableBreakdown.PaypalFee` | `Money?` |
| Net amount | `result.SellerReceivableBreakdown.NetAmount` | `Money?` |
| Status | `result.Status` | `CaptureStatus?` |

**`CaptureStatus` enum** (namespace: `PayPalServerSdk.Models.Enums`):

| C# member | Wire value |
|-----------|------------|
| `Completed` | `COMPLETED` |
| `Declined` | `DECLINED` |
| `PartiallyRefunded` | `PARTIALLY_REFUNDED` |
| `Pending` | `PENDING` |
| `Refunded` | `REFUNDED` |
| `Failed` | `FAILED` |

**Stale/expired authorization detection**:

- Before capture: call `GetAuthorizedPayment(authorizationId, null, null)` → returns `PaymentAuthorization`. Parse `PaymentAuthorization.ExpirationTime` (ISO 8601 string) and compare with `DateTimeOffset.UtcNow`. If expired, do not attempt capture — proceed to Op 5 (re-authorize).
- On capture: a 422 response (`TryGetError` returns true) whose `Error.Details[].Issue` contains `"AUTHORIZATION_EXPIRED"` or `"AUTHORIZATION_ALREADY_VOIDED"` indicates a stale/expired authorization. A 409 indicates a duplicate capture (`DUPLICATE_INVOICE_ID` or `AUTHORIZATION_ALREADY_CAPTURED`).

Error: `SdkException<PayPalServerSdk.Errors.CaptureAuthorizedPaymentError>` — Case A. Accessors: `TryGetError(out PayPalServerSdk.Models.Error error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].

---

### E. Re-authorize a Stale Authorization (Op 3)

**Controller**: `client.Payments` · Source: `map/operations/Payments.md`

```
ReauthorizePayment(
    string authorizationId,            // original (expired) authorization ID
    string? payPalRequestId,           // idempotency key — must pass explicitly
    string? payPalAuthAssertion,       // pass null — must pass explicitly
    ReauthorizeRequest? body,          // must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

Returns: `PayPalServerSdk.Models.PaymentAuthorization`

**`ReauthorizeRequest`** (source: `map/models/records-2-Pa-Ve.md`):

| Field (wire name) | Type | Notes |
|-------------------|------|-------|
| `Amount (amount)` | `Money?` | optional — set to re-authorize a specific amount (US: up to 115% of original, max +$75) |

**Response extraction**:

| Data point | Property path |
|------------|---------------|
| New authorization ID | `result.Id` |
| New status | `result.Status` (`AuthorizationStatus?`) |
| New expiration time | `result.ExpirationTime` |

**Reauthorization window note** (from SDK operation doc): valid from day 4 through day 29 after the original authorization. If 30 days have elapsed since the original authorization, you must start a new CreateOrder + AuthorizeOrder flow instead.

Error: `SdkException<PayPalServerSdk.Errors.ReauthorizePaymentError>` — Case A. Accessors: `TryGetError(out PayPalServerSdk.Models.Error error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].

---

### F. Void an Authorization (Op 4)

**Controller**: `client.Payments` · Source: `map/operations/Payments.md`

```
VoidPayment(
    string authorizationId,
    string? payPalMockResponse,        // pass null — must pass explicitly
    string? payPalAuthAssertion,       // pass null — must pass explicitly
    string? payPalRequestId,           // idempotency key — must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

Returns: `PayPalServerSdk.Models.PaymentAuthorization`

**Response extraction**: `result.Status` should equal `AuthorizationStatus.Voided`.

Error: `SdkException<PayPalServerSdk.Errors.VoidPaymentError>` — Case A. Accessors: `TryGetError(out PayPalServerSdk.Models.Error error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].

---

### G. Refund a Capture (Op 5)

**Controller**: `client.Payments` · Source: `map/operations/Payments.md`

```
RefundCapturedPayment(
    string captureId,
    string? payPalMockResponse,        // pass null — must pass explicitly
    string? payPalRequestId,           // idempotency key (caller-supplied) — must pass explicitly
    string? payPalAuthAssertion,       // pass null — must pass explicitly
    RefundRequest? body,               // null = full refund; set Amount for partial — must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

Returns: `PayPalServerSdk.Models.Refund`

**Idempotency**: `payPalRequestId` is the caller-supplied idempotency key. Same key → same refund result returned. Different keys → distinct refund transactions created. Pass it as the third positional argument (or `payPalRequestId:` named argument).

**`RefundRequest`** (source: `map/models/records-2-Pa-Ve.md`):

| Field (wire name) | Type | Notes |
|-------------------|------|-------|
| `Amount (amount)` | `Money?` | null/omit for full refund; set for partial |
| `CustomId (custom_id)` | `string?` | optional reference |
| `NoteToPayer (note_to_payer)` | `string?` | optional |

**Preventing over-refund**: The SDK does not enforce the constraint at request time. Before calling `RefundCapturedPayment` with a partial amount, call `GetCapturedPayment(captureId, null)` and compare the requested refund amount against `CapturedPayment.SellerReceivableBreakdown.GrossAmount.Value` (required field). Reject in application code if requested amount exceeds captured amount. PayPal will also reject with a 422 (`TryGetError` fires, check `Error.Details[].Issue`).

**Response extraction**:

| Data point | Property path | Type |
|------------|---------------|------|
| Refund ID | `result.Id` | `string?` |
| Refunded amount | `result.Amount` | `Money?` |
| Status | `result.Status` | `RefundStatus?` |

**`RefundStatus` enum** (namespace: `PayPalServerSdk.Models.Enums`):

| C# member | Wire value |
|-----------|------------|
| `Completed` | `COMPLETED` |
| `Failed` | `FAILED` |
| `Pending` | `PENDING` |
| `Cancelled` | `CANCELLED` |

Error: `SdkException<PayPalServerSdk.Errors.RefundCapturedPaymentError>` — Case A. Accessors: `TryGetError(out PayPalServerSdk.Models.Error error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].

---

### H. Vault a Card (Op 6)

**Controller**: `client.Vault` · Source: `map/operations/Vault.md`

```
CreatePaymentToken(
    string? payPalRequestId,           // idempotency key — must pass explicitly
    PaymentTokenRequest body,          // required, NOT nullable
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

Returns: `PayPalServerSdk.Models.PaymentTokenResponse`

**`PaymentTokenRequest`** (source: `map/models/records-2-Pa-Ve.md`):

| Field (wire name) | Type | Required? |
|-------------------|------|-----------|
| `Customer (customer)` | `Customer?` | optional — set `Customer.Id = customerId` to link to merchant's customer record |
| `PaymentSource (payment_source)` | `PaymentTokenRequestPaymentSource` | required |

**`Customer`** (source: `map/models/records-1-Ac-Pa.md`):

| Field (wire name) | Type | Notes |
|-------------------|------|-------|
| `Id (id)` | `string?` | your system's user/customer ID — links the vault token to the customer |

**`PaymentTokenRequestPaymentSource`** (source: `map/models/records-2-Pa-Ve.md`):

| Field (wire name) | Type | Notes |
|-------------------|------|-------|
| `Card (card)` | `PaymentTokenRequestCard?` | set for card vaulting |

**`PaymentTokenRequestCard`** (source: `map/models/records-2-Pa-Ve.md`):

| Field (wire name) | Type | Notes |
|-------------------|------|-------|
| `Name (name)` | `string?` | cardholder name |
| `Number (number)` | `string?` | card number |
| `Expiry (expiry)` | `string?` | `"YYYY-MM"` format |
| `SecurityCode (security_code)` | `string?` | CVV |
| `BillingAddress (billing_address)` | `Address?` | see Address fields in §C |

**Response extraction**:

| Data point | Property path | Type |
|------------|---------------|------|
| Vault token / payment method ID | `result.Id` | `string?` |
| Last 4 digits | `result.PaymentSource.Card.LastDigits` | `string?` |
| Card brand | `result.PaymentSource.Card.Brand` | `CardBrand?` |
| Expiry | `result.PaymentSource.Card.Expiry` | `string?` |

Types: `PaymentTokenResponse.PaymentSource` → `PaymentTokenResponsePaymentSource` → `.Card` → `CardPaymentTokenEntity`. All in `PayPalServerSdk.Models`. Source: `map/models/records-2-Pa-Ve.md`.

Error: `SdkException<PayPalServerSdk.Errors.CreatePaymentTokenError>` — Case A. Accessor: `TryGetError1(out PayPalServerSdk.Models.Error1 error)` [400, 403, 404, 422, 500]. Fallback: `TryGetRawError(out RawError)`.

Note: `Error1` (not `Error`) — different type used by Vault operations. Fields: `Name`, `Message`, `DebugId` (all `string !req`), `Details` (`IReadOnlyList<ErrorDetails1>?`). Source: `map/models/records-1-Ac-Pa.md`.

---

### I. List Vaulted Payment Methods (Op 7)

**Controller**: `client.Vault` · Source: `map/operations/Vault.md`

```
ListCustomerPaymentTokens(
    string customerId,                 // your merchant customer ID
    int? pageSize = 5,
    int? page = 1,
    bool? totalRequired = false,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

Returns: `PayPalServerSdk.Models.CustomerVaultPaymentTokensResponse`

**Pagination**: The SDK provides no auto-pagination helper. `CustomerVaultPaymentTokensResponse.TotalPages` gives the page count. Set `totalRequired: true` to populate `TotalPages`. Loop from `page = 1` to `TotalPages`, collecting `PaymentTokens` from each response.

**Response extraction**:

| Data point | Property path |
|------------|---------------|
| Token list | `result.PaymentTokens` (`IReadOnlyList<PaymentTokenResponse>?`) |
| Total page count | `result.TotalPages` (`int?`) — non-null when `totalRequired: true` |
| Total item count | `result.TotalItems` (`int?`) |

Per token: same extraction as §H response extraction.

Error: `SdkException<PayPalServerSdk.Errors.ListCustomerPaymentTokensError>` — Case A. Accessor: `TryGetError1(out PayPalServerSdk.Models.Error1 error)` [400, 403, 500]. Fallback: `TryGetRawError(out RawError)`.

---

### J. Delete a Vaulted Payment Method (Op 8)

**Controller**: `client.Vault` · Source: `map/operations/Vault.md`

```
DeletePaymentToken(
    string id,                         // vault token / payment method ID
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

Returns: `void` (Task — no return value on success)

Error: `SdkException<PayPalServerSdk.Errors.DeletePaymentTokenError>` — Case A. Accessor: `TryGetError1(out PayPalServerSdk.Models.Error1 error)` [400, 403, 500]. Fallback: `TryGetRawError(out RawError)`.

Success indicator: no exception thrown. There is no return value to inspect.

---

### K. Transaction Search / Reconciliation (Op 10)

**Controller**: `client.TransactionSearch` · Source: `map/operations/TransactionSearch.md`

```
SearchTransactions(
    string startDate,
    string endDate,
    string? transactionId,             // pass null — must pass explicitly
    string? transactionType,           // pass null
    string? transactionStatus,         // pass null
    string? transactionAmount,         // pass null
    string? transactionCurrency,       // pass null
    string? paymentInstrumentType,     // pass null
    string? storeId,                   // pass null
    string? terminalId,                // pass null
    string? fields = "transaction_info",
    string? balanceAffectingRecordsOnly = "Y",
    int? pageSize = 100,
    int? page = 1,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

Returns: `PayPalServerSdk.Models.SearchResponse`

**Date format**: `startDate` and `endDate` are `string`. Format `DateTimeOffset` inputs as RFC 3339: `offset.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")`. Example: `"2024-01-01T00:00:00Z"`.

**Pagination — all results**: `SearchResponse.TotalPages` (`int?`) gives the page count. Loop from page 1 to `TotalPages`, calling `SearchTransactions` with incrementing `page` parameter, collecting all `TransactionDetails` items. Set `pageSize: 100` (maximum per call).

**Response extraction** (per `TransactionDetails` item):

| Data point | Property path | Type |
|------------|---------------|------|
| PayPal transaction ID | `td.TransactionInfo.TransactionId` | `string?` |
| Amount | `td.TransactionInfo.TransactionAmount` | `Money?` |
| Fee | `td.TransactionInfo.FeeAmount` | `Money?` |
| Status | `td.TransactionInfo.TransactionStatus` | `string?` |
| Invoice/order ref | `td.TransactionInfo.InvoiceId` | `string?` |
| Custom field / order ref | `td.TransactionInfo.CustomField` | `string?` |
| PayPal reference ID | `td.TransactionInfo.PaypalReferenceId` | `string?` |

Types: `SearchResponse.TransactionDetails` → `IReadOnlyList<TransactionDetails>?` → each `TransactionDetails.TransactionInfo` → `TransactionInformation`. Source: `map/models/records-2-Pa-Ve.md`.

Error: **Case B** — `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>`. No typed accessors. Read via `ex.Error.StatusCode` (`HttpStatusCode`), `ex.Error.ReadAsString()`, or `ex.Error.ReadAsJson<T>()`. Source: `map/operations/TransactionSearch.md`.

---

### L. Key Types Referenced — Namespaces Summary

| Type | Namespace |
|------|-----------|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` | `PayPalServerSdk` |
| `ServerEnvironment` | `PayPalServerSdk.Servers` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| All `record` models (`OrderRequest`, `CapturedPayment`, `Refund`, `PaymentTokenResponse`, `SearchResponse`, etc.) | `PayPalServerSdk.Models` |
| All enums (`CheckoutPaymentIntent`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus`, `CardBrand`) | `PayPalServerSdk.Models.Enums` |
| All error classes (`CreateOrderError`, `AuthorizeOrderError`, `CaptureAuthorizedPaymentError`, etc.) | `PayPalServerSdk.Errors` |
| `SdkException<TError>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError` | `PayPalServerSdk.Core.ErrorResponse` |

---

## 3. Trap Notes

> Each trap names its consequence. The MUST load pointer is what resolves it — the plan deliberately does not carry the skill's contents.

- ⚠ Step 2 (client registration) — the SDK client wraps an `HttpClient`; the `HttpClient` itself must be long-lived and routed through `IHttpClientFactory`. Constructing a new `HttpClient` per request causes socket exhaustion. **MUST load `dotnet-client-initialization`** before wiring the DI registration.

- ⚠ Step 2 (auth) — `OAuth2ClientCredentials` holds the raw client secret in memory. Whether the token is cached across requests and under what conditions a new token is fetched is controlled by the `Oauth2TokenStrategy` property — loading credentials from config rather than hardcoding is required. **MUST load `dotnet-authentication`** before wiring credentials.

- ⚠ Step 3 / Step 11 (AuthorizeOrder) — the `prefer` parameter defaults to `"return=minimal"`, which causes the response body to omit purchase-unit and authorization details. The authorization ID is NOT accessible at `PurchaseUnits[0].Payments.Authorizations[0].Id` unless `prefer = "return=representation"` is passed. Forgetting this leaves the caller with no authorization ID in the response. **MUST load `dotnet-calling-endpoints`** for the full shape of the response envelope and how `prefer` interacts with it.

- ⚠ Step 4 / Step 5 (CaptureAuthorizedPayment / ReauthorizePayment) — idempotent writes go over `POST`. The SDK's `HttpMethodsToRetry` setting gates only the **status-code** retry trigger; a transport failure (`HttpRequestException`) is retried on **every** verb including `POST`, so a failed capture or re-authorization can execute more than once if transport retries are enabled. **MUST load `dotnet-configuration-resilience`** before wiring retry options for any write operation.

- ⚠ Step 7 / Step 12 (list / search pagination) — neither `ListCustomerPaymentTokens` nor `SearchTransactions` has an SDK-level auto-pagination helper. The implementation must loop manually using `page` and `TotalPages`. The interaction of `pageSize`, `totalRequired`, and `TotalPages` has subtleties. **MUST load `dotnet-calling-endpoints`** for the exact pagination mechanics.

- ⚠ Step 13 (error boundary) — two distinct `JsonException` paths cross the boundary from opposite directions (see REQUIRED READING below). An error boundary that catches only `SdkException` misses the deserialization failure on a drifted 2xx body; one that maps every `JsonException` to a 5xx masks deterministic rejections as outages. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

- ⚠ All vault operations (Steps 8–10) — the error type is `Error1`, NOT `Error`. The accessor is `TryGetError1(out Error1)`, not `TryGetError(out Error)`. Writing `TryGetError` on a Vault error gives a compile error (`CS1061`). Mixing these up in a shared error-handling helper is a runtime gap. **MUST load `dotnet-error-handling`** for Case A vs Case B mechanics and accessor naming.

- ⚠ All steps — enums are `StringEnum<T>` records, not C# enums. Construct with the static member (`CheckoutPaymentIntent.Authorize`), not with `new`. Comparing enum values requires the `==` operator on the record, not `switch` on an underlying int. **MUST load `dotnet-models`** before writing any code that reads or compares enum values.

---

## 4. REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry the contents of these skills.

| Skill | Step(s) governed |
|-------|-----------------|
| `dotnet-client-initialization` | Step 2 — DI registration, `IHttpClientFactory`, `HttpClient` lifetime |
| `dotnet-authentication` | Step 2 — OAuth2 credential wiring, token caching, secret loading from config |
| `dotnet-calling-endpoints` | Steps 3–12 — `prefer` header, named vs positional params, pagination, response envelope shape |
| `dotnet-models` | Steps 3–12 — `StringEnum<T>` construction and comparison, `Money.Value` as string not decimal |
| `dotnet-error-handling` | Step 13 and all call sites — Case A (`SdkException<{Op}Error>`) vs Case B (`SdkException<RawError>`), `TryGet…` accessors, `JsonException` boundary handling |
| `dotnet-configuration-resilience` | Step 2 — retry policy, per-attempt `Timeout`, transport-failure retry on POST, base-URL wiring |
| `dotnet-testing` | All steps — `HttpClient` as the test seam, stub shape, assertion style |

**Mandatory `JsonException` boundary rows** — both must be handled; they need opposite treatment:

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**B1 — BLOCKER: `ServerEnvironment` has no `Production` member.**
The SDK at v1.0.1 defines exactly one `ServerEnvironment` value: `ServerEnvironment.Sandbox`. There is no `Production` or `Live` member. Source: `Servers/ServerEnvironment.cs` (SDK source). Consequence: the `PayPal:Environment = "production"` config value cannot select a different SDK environment enum — the implementation must always use `ServerEnvironment.Sandbox` and route to production by overriding `options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"`. The plan documents this mapping. No implementation change is needed — but the code must not attempt `ServerEnvironment.FromValue("production")` or similar; that will throw at runtime.

**A1 — ASSUMPTION: Vault token ID is the `PaymentTokenResponse.Id`.**
For Op 9 (pay with saved card), the plan uses `CardRequest.VaultId` set to the `PaymentTokenResponse.Id` value returned from `CreatePaymentToken`. This is the direct vault payment token path. If the live API requires a different token type (e.g. a setup token ID must be exchanged for a payment token before use), the `CreateSetupToken` → `CreatePaymentToken` two-step flow would be needed instead. This is UNVERIFIED via live traffic. Defensive coding: if a 422 is returned on `AuthorizeOrder` with `VaultId` set, inspect `Error.Details[].Issue` to determine if a token exchange is required.

**A2 — ASSUMPTION: `CardRequest.VaultId` accepts a `PaymentTokenResponse.Id` directly.**
The `CardRequest.VaultId` field description says "the id from a previously saved payment token or a vaulted payment method". The `PaymentTokenResponse.Id` is the payment token ID from `CreatePaymentToken`. This should be the correct value. UNVERIFIED via live traffic — treat any 422 on authorize-with-vault-id as a signal to check token type.

**A3 — ASSUMPTION: `TransactionInformation.InvoiceId` carries the order/invoice reference.**
The `InvoiceId` field is described as an invoice ID. Whether the `InvoiceId` set in `PurchaseUnitRequest.InvoiceId` during `CreateOrder` maps back to `TransactionInformation.InvoiceId` in the search result is UNVERIFIED via live traffic. Also check `CustomField` (maps to `PurchaseUnitRequest.CustomId`). The plan documents both fields.

**A4 — ASSUMPTION: `Money.Value` is a decimal string formatted to 2 decimal places.**
The `Money.Value` field is `string`. The implementation must format `decimal` order totals as e.g. `total.ToString("F2", CultureInfo.InvariantCulture)` before setting this field. PayPal rejects values with more than 2 decimal places for most currencies.

**D1 — DESIGN DECISION DEFERRED: Vault customer ID storage.**
How the application persists the mapping of `Customer.Id` (passed to `CreatePaymentToken`) to internal user accounts, and how `PaymentTokenResponse.Id` (the vault token) is stored and retrieved, is a design decision left to the implementer.

**D2 — DESIGN DECISION DEFERRED: Authorization ID + Order ID persistence.**
The implementation must store the authorization ID (from `AuthorizeOrder` response) to enable later capture, re-authorization, and void. How and where this is stored is a design decision left to the implementer.
