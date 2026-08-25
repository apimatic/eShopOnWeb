# PayPal Integration Plan — eShopOnWeb PublicApi

> Project: `src/PublicApi` · Package: `AsadAli.Checkout.Sdk` (version-less)

---

## 1. Scope & Sequence

| Step | Description | Operations used |
|---|---|---|
| 1 | Install SDK, register client & credentials in DI | — |
| 2 | Order Authorization (direct card, AUTHORIZE intent) | `Orders.CreateOrder`, `Orders.AuthorizeOrder` |
| 3 | Payment Capture (+ stale-auth handling) | `Payments.CaptureAuthorizedPayment`, `Payments.GetAuthorizedPayment`, `Payments.ReauthorizePayment` |
| 4 | Authorization Void | `Payments.VoidPayment` |
| 5 | Refund (full and partial) | `Payments.RefundCapturedPayment` |
| 6 | Card Vaulting (save, list, delete) | `Vault.CreatePaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.DeletePaymentToken` |
| 7 | Pay with Vaulted Card | `Orders.CreateOrder`, `Orders.AuthorizeOrder` (Token payment source) |
| 8 | Transaction Reconciliation (all pages) | `TransactionSearch.SearchTransactions` |
| 9 | Error boundary (wraps all steps) | — |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one
> from that type's own map row, never from where a neighbouring type sits. A members table names the
> namespace outright; otherwise the row's source path implies it. Enums, unions, auth, server and
> client-config types are spread across different child namespaces, and two types configured side by
> side in the same options object routinely live in different ones. Dropping a type to the root or
> to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Namespaces required

| Contents | `using` directive |
|---|---|
| Client, options | `using PayPalServerSdk;` |
| Server environment | `using PayPalServerSdk.Servers;` |
| All record models | `using PayPalServerSdk.Models;` |
| All enums | `using PayPalServerSdk.Models.Enums;` |
| Typed error classes | `using PayPalServerSdk.Errors;` |

(`Api/` controllers are accessed through `client.Orders`, `client.Payments`, etc. — no separate `using` needed for controller types.)

---

### Step 1 — Client construction & auth

**Source**: `sdk-map.md` (Getting a client, Servers & auth)

| Fact | Value |
|---|---|
| Client class | `PayPalServerSdkClient` |
| Options class | `PayPalServerSdkClientOptions` |
| DI extension | `services.AddPayPalServerSdkClient(o => { … })` |
| Constructor | `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` |
| OAuth2 credentials property | `options.Oauth2` — type `OAuth2ClientCredentials?` (namespace from its map row; load `dotnet-authentication` before wiring) |
| Sandbox environment | `options.Environment = ServerEnvironment.Sandbox` (`PayPalServerSdk.Servers`) |
| Production environment | `ServerEnvironment` has **only one member: `Sandbox`**. There is no `Live`, `Production`, or `Default` constant — the SDK's `Match` method throws `ArgumentOutOfRangeException` on any other value. For production traffic, keep `options.Environment = ServerEnvironment.Sandbox` and override the base URL (see row below). Source: `Servers/ServerEnvironment.cs` |
| Base URL override (exact path) | `options.Server.Default.Sandbox.BaseUrl = "https://custom-url";` — `Server` is `ServerOptions` (namespace `PayPalServerSdk`, file `ServerOptions.cs`); its only child property is `Default` (type `DefaultOptions` from `PayPalServerSdk.Servers`); `DefaultOptions.Sandbox` (type `SandboxOptions`) carries `BaseUrl` (string, default `"https://api-m.sandbox.paypal.com"`). Full chain: `options.Server.Default.Sandbox.BaseUrl`. For production set it to `"https://api-m.paypal.com"`. All calls (including OAuth2 token endpoint) route through this URL. Sources: `ServerOptions.cs`, `Servers/DefaultOptions.cs` |

---

### Step 2 — Order Authorization (direct card, AUTHORIZE intent)

**Source**: `map/operations/Orders.md`, `map/models/records-1-Ac-Pa.md`

#### 2a. CreateOrder

Controller accessor: `client.Orders`

Signature:
```
CreateOrder(
    string? payPalMockResponse,
    string? payPalRequestId,
    string? payPalPartnerAttributionId,
    string? payPalClientMetadataId,
    string? payPalAuthAssertion,
    OrderRequest body,
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
All five header params are nullable, no default → **must pass explicitly** (pass `null` to skip).

**CRITICAL: pass `prefer: "return=representation"` to receive a response body with the order ID.**

Request model: `OrderRequest` (`PayPalServerSdk.Models`)

| Field (wire_name) | Type | Required |
|---|---|---|
| `Intent (intent)` | `CheckoutPaymentIntent` | YES |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | YES |

`PurchaseUnitRequest` fields needed:

| Field (wire_name) | Type | Required |
|---|---|---|
| `Amount (amount)` | `AmountWithBreakdown` | YES |

`AmountWithBreakdown` fields:

| Field (wire_name) | Type | Required |
|---|---|---|
| `CurrencyCode (currency_code)` | `string` | YES |
| `Value (value)` | `string` (decimal as string, e.g. `"19.99"`) | YES |

For direct card payment at create-order time, optionally set `PaymentSource (payment_source): PaymentSource?` on `OrderRequest`:
- `PaymentSource.Card (card): CardRequest?`

(Card can alternatively be supplied on the AuthorizeOrder call below — see Step 2b.)

Returns: `Order` (`PayPalServerSdk.Models`)
- `Id (id): string?` — **order ID to store and pass to AuthorizeOrder**
- `Status (status): OrderStatus?`

Error: **Case A** `SdkException<CreateOrderError>` (`PayPalServerSdk.Errors`)
- `TryGetError(out Error typed)` → HTTP 400, 401, 422
- `TryGetRawError(out RawError raw)` → fallback

#### 2b. AuthorizeOrder

Signature:
```
AuthorizeOrder(
    string id,
    string? payPalMockResponse,
    string? payPalRequestId,
    string? payPalClientMetadataId,
    string? payPalAuthAssertion,
    OrderAuthorizeRequest? body,
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
All five header params nullable, no default → **must pass explicitly**.

**CRITICAL: pass `prefer: "return=representation"` to receive the authorization ID in the response body.**

Request model: `OrderAuthorizeRequest` (`PayPalServerSdk.Models`)

| Field (wire_name) | Type | Required |
|---|---|---|
| `PaymentSource (payment_source)` | `OrderAuthorizeRequestPaymentSource?` | optional |

`OrderAuthorizeRequestPaymentSource` fields:

| Field (wire_name) | Type | Notes |
|---|---|---|
| `Card (card)` | `CardRequest?` | for direct raw-card payment |
| `Token (token)` | `Token?` | for vaulted-token payment (Step 7) |

`CardRequest` fields for direct card payment (`PayPalServerSdk.Models`):

| Field (wire_name) | Type |
|---|---|
| `Name (name)` | `string?` |
| `Number (number)` | `string?` |
| `Expiry (expiry)` | `string?` (format: `YYYY-MM`) |
| `SecurityCode (security_code)` | `string?` |
| `BillingAddress (billing_address)` | `Address?` |

`Address` fields (`PayPalServerSdk.Models`):

| Field (wire_name) | Type | Required |
|---|---|---|
| `CountryCode (country_code)` | `string` | YES |
| `AddressLine1 (address_line_1)` | `string?` | optional |
| `AdminArea2 (admin_area_2)` | `string?` | optional (city) |
| `AdminArea1 (admin_area_1)` | `string?` | optional (state) |
| `PostalCode (postal_code)` | `string?` | optional |

Returns: `OrderAuthorizeResponse` (`PayPalServerSdk.Models`)

Reading the authorization ID from the response:
```
response.PurchaseUnits          // IReadOnlyList<PurchaseUnit>?
  [0].Payments                  // PaymentCollection?
  .Authorizations               // IReadOnlyList<AuthorizationWithAdditionalData>?
  [0].Id                        // string?  ← AUTHORIZATION ID
```
Also on the response: `response.Id` = order ID.

Error: **Case A** `SdkException<AuthorizeOrderError>` (`PayPalServerSdk.Errors`)
- `TryGetError(out Error typed)` → 400, 401, 403, 404, 422, 500
- `TryGetRawError(out RawError raw)` → fallback

---

### Step 3 — Payment Capture (+ stale-auth handling)

**Source**: `map/operations/Payments.md`, `map/models/records-1-Ac-Pa.md`, `map/models/records-2-Pa-Ve.md`

Controller accessor: `client.Payments`

#### 3a. CaptureAuthorizedPayment

Signature:
```
CaptureAuthorizedPayment(
    string authorizationId,
    string? payPalMockResponse,
    string? payPalRequestId,
    string? payPalAuthAssertion,
    CaptureRequest? body,
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
Four header params nullable, no default → **must pass explicitly**.

**CRITICAL: pass `prefer: "return=representation"` to receive the capture ID and breakdown.**

Request model: `CaptureRequest` (`PayPalServerSdk.Models`) — all fields optional:

| Field (wire_name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | for partial capture only |
| `FinalCapture (final_capture)` | `bool? = false` | set `true` to prevent further captures |

Returns: `CapturedPayment` (`PayPalServerSdk.Models`)

| Field to store | Path | Type |
|---|---|---|
| Capture ID | `.Id` | `string?` |
| Captured amount | `.Amount` | `Money?` |
| PayPal fee | `.SellerReceivableBreakdown?.PaypalFee` | `Money?` |
| Net amount (seller receives) | `.SellerReceivableBreakdown?.NetAmount` | `Money?` |
| Gross amount | `.SellerReceivableBreakdown.GrossAmount` | `Money` (required field) |

`SellerReceivableBreakdown` is `PayPalServerSdk.Models.SellerReceivableBreakdown`. `Money` has `CurrencyCode (currency_code): string !req` and `Value (value): string !req`.

Error: **Case A** `SdkException<CaptureAuthorizedPaymentError>` (`PayPalServerSdk.Errors`)
- `TryGetError(out Error typed)` → 400, 401, 403, 404, 409, 422
- `TryGetNoContent(out RawError raw)` → 500
- `TryGetRawError(out RawError raw)` → fallback

#### 3b. Stale-auth handling — GetAuthorizedPayment

Signature:
```
GetAuthorizedPayment(
    string authorizationId,
    string? payPalMockResponse,
    string? payPalAuthAssertion,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
Both header params nullable, no default → **must pass explicitly**.

Returns: `PaymentAuthorization` (`PayPalServerSdk.Models`)
- `Status (status): AuthorizationStatus?` — check for `AuthorizationStatus.Voided`, `AuthorizationStatus.Denied`
- `ExpirationTime (expiration_time): string?` — ISO 8601 datetime; compare to `DateTimeOffset.UtcNow`

Error: **Case A** `SdkException<GetAuthorizedPaymentError>`
- `TryGetError(out Error typed)` → 401, 403, 404
- `TryGetNoContent(out RawError raw)` → 500

Detection logic: on `CaptureAuthorizedPayment` failure (422 or after checking status), fetch the auth with `GetAuthorizedPayment`. If `Status == Voided/Denied` OR `ExpirationTime` is in the past, the auth is stale.

#### 3c. Stale-auth handling — ReauthorizePayment

Signature:
```
ReauthorizePayment(
    string authorizationId,
    string? payPalRequestId,
    string? payPalAuthAssertion,
    ReauthorizeRequest? body,
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
`payPalRequestId` and `payPalAuthAssertion` nullable, no default → **must pass explicitly**.

**CRITICAL: pass `prefer: "return=representation"` to receive the new authorization ID.**

Request model: `ReauthorizeRequest` (`PayPalServerSdk.Models`):
- `Amount (amount): Money?` — optional; omit to re-authorize for the original amount

Returns: `PaymentAuthorization` (`PayPalServerSdk.Models`)
- `Id (id): string?` — **new authorization ID**

PayPal constraint (from operation notes): reauthorize only works from day 4 to day 29 after the original authorization. If 30 days have passed, re-authorization is impossible — report as such.

Error: **Case A** `SdkException<ReauthorizePaymentError>`
- `TryGetError(out Error typed)` → 400, 401, 403, 404, 422
- `TryGetNoContent(out RawError raw)` → 500

---

### Step 4 — Authorization Void

**Source**: `map/operations/Payments.md`

Controller accessor: `client.Payments`

Signature:
```
VoidPayment(
    string authorizationId,
    string? payPalMockResponse,
    string? payPalAuthAssertion,
    string? payPalRequestId,
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
`payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` nullable, no default → **must pass explicitly**.

Note: If the void result is not needed, the default `prefer="return=minimal"` is acceptable (returns 204 with no body). Pass `prefer: "return=representation"` only if the `PaymentAuthorization` response is needed.

Returns: `PaymentAuthorization` (only when `prefer="return=representation"`)

Error: **Case A** `SdkException<VoidPaymentError>` (`PayPalServerSdk.Errors`)
- `TryGetError(out Error typed)` → 401, 403, 404, 409, 422
- `TryGetNoContent(out RawError raw)` → 500
- `TryGetRawError(out RawError raw)` → fallback

---

### Step 5 — Refund (full and partial)

**Source**: `map/operations/Payments.md`, `map/models/records-2-Pa-Ve.md`

Controller accessor: `client.Payments`

Signature:
```
RefundCapturedPayment(
    string captureId,
    string? payPalMockResponse,
    string? payPalRequestId,
    string? payPalAuthAssertion,
    RefundRequest? body,
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
`payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` nullable, no default → **must pass explicitly**.

**Idempotency key**: pass the caller-supplied key as `payPalRequestId` (wire header: `PayPal-Request-Id`). Using the same key on a retry returns the original result without executing a second refund.

**CRITICAL: pass `prefer: "return=representation"` to receive the refund ID in the body.**

Request model: `RefundRequest` (`PayPalServerSdk.Models`):
- Full refund: pass `body: null` (or `new RefundRequest {}` with no fields set)
- Partial refund: `body: new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = "10.00" } }`

Returns: `Refund` (`PayPalServerSdk.Models`)

| Field to store | Path | Type |
|---|---|---|
| Refund ID | `.Id` | `string?` |
| Refunded amount | `.Amount` | `Money?` |
| Gross amount paid back | `.SellerPayableBreakdown?.GrossAmount` | `Money?` |
| Net amount | `.SellerPayableBreakdown?.NetAmount` | `Money?` |
| PayPal fee portion | `.SellerPayableBreakdown?.PaypalFee` | `Money?` |

Note: Refund uses `SellerPayableBreakdown` (`PayPalServerSdk.Models.SellerPayableBreakdown`), NOT `SellerReceivableBreakdown` — different model, different field name.

Error: **Case A** `SdkException<RefundCapturedPaymentError>` (`PayPalServerSdk.Errors`)
- `TryGetError(out Error typed)` → 400, 401, 403, 404, 409, 422
- `TryGetNoContent(out RawError raw)` → 500
- `TryGetRawError(out RawError raw)` → fallback

---

### Step 6 — Card Vaulting

**Source**: `map/operations/Vault.md`, `map/models/records-2-Pa-Ve.md`

Controller accessor: `client.Vault`

#### 6a. CreatePaymentToken (save a card)

Signature:
```
CreatePaymentToken(
    string? payPalRequestId,
    PaymentTokenRequest body,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
`payPalRequestId` nullable, no default → **must pass explicitly**.

Request model: `PaymentTokenRequest` (`PayPalServerSdk.Models`)

| Field (wire_name) | Type | Required |
|---|---|---|
| `Customer (customer)` | `Customer?` | optional |
| `PaymentSource (payment_source)` | `PaymentTokenRequestPaymentSource` | YES |

`Customer` (`PayPalServerSdk.Models`):
- `Id (id): string?` — pass the existing PayPal customer ID to associate the card, or `null` to let PayPal create a new customer

`PaymentTokenRequestPaymentSource`:
- `Card (card): PaymentTokenRequestCard?`

`PaymentTokenRequestCard` (`PayPalServerSdk.Models`):

| Field (wire_name) | Type |
|---|---|
| `Name (name)` | `string?` |
| `Number (number)` | `string?` |
| `Expiry (expiry)` | `string?` (format: `YYYY-MM`) |
| `SecurityCode (security_code)` | `string?` |
| `BillingAddress (billing_address)` | `Address?` |

Returns: `PaymentTokenResponse` (`PayPalServerSdk.Models`)

| Field | Path | Notes |
|---|---|---|
| Vault token ID | `.Id` | `string?` — store as "vaulted card ID" |
| Customer ID | `.Customer?.Id` | `string?` — PayPal-assigned customer ID |
| Last 4 digits | `.PaymentSource?.Card?.LastDigits` | `string?` |
| Brand | `.PaymentSource?.Card?.Brand` | `CardBrand?` |
| Expiry | `.PaymentSource?.Card?.Expiry` | `string?` |

`PaymentTokenResponse.PaymentSource` is `PaymentTokenResponsePaymentSource?` → `.Card` is `CardPaymentTokenEntity?`.

Error: **Case A** `SdkException<CreatePaymentTokenError>` (`PayPalServerSdk.Errors`)
- `TryGetError1(out Error1 typed)` → 400, 403, 404, 422, 500 (accessor is `TryGetError1`, NOT `TryGetError`)
- `TryGetRawError(out RawError raw)` → fallback

#### 6b. ListCustomerPaymentTokens

Signature:
```
ListCustomerPaymentTokens(
    string customerId,
    int? pageSize = 5,
    int? page = 1,
    bool? totalRequired = false,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `CustomerVaultPaymentTokensResponse` (`PayPalServerSdk.Models`)
- `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`
- `TotalItems (total_items): int?`
- `TotalPages (total_pages): int?`

Pagination: `page` is manual — the SDK has no auto-paginator; loop from page 1 to `TotalPages` if needed.

Error: **Case A** `SdkException<ListCustomerPaymentTokensError>`
- `TryGetError1(out Error1 typed)` → 400, 403, 500

#### 6c. DeletePaymentToken

Signature:
```
DeletePaymentToken(
    string id,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `void` (Task)

Error: **Case A** `SdkException<DeletePaymentTokenError>`
- `TryGetError1(out Error1 typed)` → 400, 403, 500

---

### Step 7 — Pay with Vaulted Card

**Source**: `map/operations/Orders.md`, `map/models/records-1-Ac-Pa.md`, `map/models/enums.md`

Reuse `Orders.CreateOrder` (same as Step 2a: `intent = AUTHORIZE`), then call `AuthorizeOrder` with a `Token` payment source instead of `Card`.

`OrderAuthorizeRequest.PaymentSource.Token = new Token { Id = vaultTokenId, Type = ??? }`

`Token` model (`PayPalServerSdk.Models`):
- `Id (id): string !req` — the payment token ID from `PaymentTokenResponse.Id`
- `Type (type): TokenType !req` — see note below

`TokenType` enum (`PayPalServerSdk.Models.Enums`): only defines `BillingAgreement (BILLING_AGREEMENT)`.

UNVERIFIED: PayPal's vault API (v3) payment tokens require `type = PAYMENT_METHOD_TOKEN` on the wire. The SDK's `TokenType` enum documents only `BillingAgreement`. Since `StringEnum<T>` supports `FromValue`, the defensive-coding directive is:
- Try `Token.Type = StringEnum<TokenType>.FromValue("PAYMENT_METHOD_TOKEN")`; if the live API rejects it, fall back to using `CardRequest.VaultId = vaultTokenId` on `OrderAuthorizeRequestPaymentSource.Card` (which also accepts a pre-vaulted card reference). Only live traffic can confirm which form the server accepts.

---

### Step 8 — Transaction Reconciliation (all pages)

**Source**: `map/operations/TransactionSearch.md`, `map/models/records-2-Pa-Ve.md`

Controller accessor: `client.TransactionSearch`

Signature:
```
SearchTransactions(
    string startDate,
    string endDate,
    string? transactionId,
    string? transactionType,
    string? transactionStatus,
    string? transactionAmount,
    string? transactionCurrency,
    string? paymentInstrumentType,
    string? storeId,
    string? terminalId,
    string? fields = "transaction_info",
    string? balanceAffectingRecordsOnly = "Y",
    int? pageSize = 100,
    int? page = 1,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
Eight filter params (`transactionId` … `terminalId`) nullable, no default → **must pass explicitly** (pass `null` to skip each).

- `startDate` / `endDate`: ISO-8601 datetime strings (e.g. `"2024-01-01T00:00:00Z"`)
- To include payer info and reference IDs pass `fields: "all"` (overrides default `"transaction_info"`)
- `balanceAffectingRecordsOnly`: default `"Y"` limits to balance-affecting transactions only; pass `"N"` for all

Returns: `SearchResponse` (`PayPalServerSdk.Models`)

| Field | Path | Type |
|---|---|---|
| Transaction list | `.TransactionDetails` | `IReadOnlyList<TransactionDetails>?` |
| Transaction ID | `[i].TransactionInfo?.TransactionId` | `string?` |
| Amount | `[i].TransactionInfo?.TransactionAmount` | `Money?` |
| Status | `[i].TransactionInfo?.TransactionStatus` | `string?` |
| External order reference | `[i].TransactionInfo?.PaypalReferenceId` | `string?` |
| Reference type | `[i].TransactionInfo?.PaypalReferenceIdType` | `PayPalReferenceIdType?` |
| Current page | `.Page` | `int?` |
| Total pages | `.TotalPages` | `int?` |
| Total items | `.TotalItems` | `int?` |

**All-pages loop**: call `SearchTransactions(…, page: 1)`, read `TotalPages`, then loop calling `SearchTransactions(…, page: p)` for each p in 2..TotalPages. Aggregate `TransactionDetails` across all pages.

Error: **Case B** (raw, NOT typed) `SdkException<RawError>` — THIS OPERATION IS CASE B
```csharp
catch (SdkException<RawError> ex)
{
    var status = ex.Error.StatusCode;
    var body   = ex.Error.ReadAsString();
}
```
There is NO `TryGetError` / `TryGetError1` accessor — the catch block type must be `SdkException<RawError>`.

---

### Enum values required

**Source**: `map/models/enums.md` — namespace `PayPalServerSdk.Models.Enums` for all

| Enum | Members needed |
|---|---|
| `CheckoutPaymentIntent` | `Authorize (AUTHORIZE)`, `Capture (CAPTURE)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` — only documented member; see UNVERIFIED note in Step 7 |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)` (and others) |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |

All are `StringEnum<T>`, NOT C# enums. Construct with static members (`CheckoutPaymentIntent.Authorize`) or `StringEnum<T>.FromValue("wire_value")`.

---

### Error types by operation

| Operation | Error class | Error accessor (typed) | Also |
|---|---|---|---|
| `CreateOrder` | `CreateOrderError` | `TryGetError(out Error)` [400,401,422] | `TryGetRawError` fallback |
| `AuthorizeOrder` | `AuthorizeOrderError` | `TryGetError(out Error)` [400,401,403,404,422,500] | `TryGetRawError` fallback |
| `CaptureAuthorizedPayment` | `CaptureAuthorizedPaymentError` | `TryGetError(out Error)` [400,401,403,404,409,422] | `TryGetNoContent` [500] + `TryGetRawError` |
| `GetAuthorizedPayment` | `GetAuthorizedPaymentError` | `TryGetError(out Error)` [401,403,404] | `TryGetNoContent` [500] + `TryGetRawError` |
| `ReauthorizePayment` | `ReauthorizePaymentError` | `TryGetError(out Error)` [400,401,403,404,422] | `TryGetNoContent` [500] + `TryGetRawError` |
| `VoidPayment` | `VoidPaymentError` | `TryGetError(out Error)` [401,403,404,409,422] | `TryGetNoContent` [500] + `TryGetRawError` |
| `RefundCapturedPayment` | `RefundCapturedPaymentError` | `TryGetError(out Error)` [400,401,403,404,409,422] | `TryGetNoContent` [500] + `TryGetRawError` |
| `CreatePaymentToken` | `CreatePaymentTokenError` | `TryGetError1(out Error1)` [400,403,404,422,500] | `TryGetRawError` fallback |
| `ListCustomerPaymentTokens` | `ListCustomerPaymentTokensError` | `TryGetError1(out Error1)` [400,403,500] | `TryGetRawError` fallback |
| `DeletePaymentToken` | `DeletePaymentTokenError` | `TryGetError1(out Error1)` [400,403,500] | `TryGetRawError` fallback |
| `SearchTransactions` | — | **Case B: `SdkException<RawError>` only** | `ex.Error.StatusCode`, `ex.Error.ReadAsString()` |

All error classes (`CreateOrderError`, etc.) are in namespace `PayPalServerSdk.Errors`. `Error`, `Error1`, `RawError` are in `PayPalServerSdk.Models` and `PayPalServerSdk` respectively (see map). The `TryGetError1` vs `TryGetError` distinction is real — Vault operations use `TryGetError1` (not `TryGetError`).

`Error` fields: `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`
`Error1` fields: `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails1>?`

---

## 3. Trap Notes

Attached to the step where each hazard bites:

- ⚠ **Steps 2b, 3a, 3c, 5** (`prefer` default) — `prefer` defaults to `"return=minimal"`. PayPal returns 204 with no body under `return=minimal` for authorize/capture/void/refund operations, so authorization IDs, capture IDs, refund IDs, and seller breakdowns will NOT be in the response unless `prefer: "return=representation"` is passed explicitly. **MUST load `dotnet-calling-endpoints`** before coding any operation that reads from the response body.

- ⚠ **Step 1** (client registration / `IHttpClientFactory`) — the `HttpClient` passed to the SDK must be long-lived and reused via `IHttpClientFactory`; the SDK client wrapper may be transient. Reconstructing `HttpClient` per request causes socket exhaustion. **MUST load `dotnet-client-initialization`** before wiring the client into the service container.

- ⚠ **Step 1** (credentials / OAuth2 token strategy) — credentials must be set before construction; do not hard-code secrets; the `Oauth2TokenStrategy` property controls token caching and refresh. **MUST load `dotnet-authentication`** before setting `options.Oauth2` or `options.Oauth2TokenStrategy`.

- ⚠ **Step 1** (production environment / base-URL override) — only `ServerEnvironment.Sandbox` is documented in the SDK map; production environment and the exact shape of `options.Server` for base-URL override are unresolved at map level. **MUST load `dotnet-configuration-resilience`** before configuring the environment or `PayPal:BaseUrl`.

- ⚠ **Steps 2, 3, 5** (retry on non-idempotent POST) — `CaptureAuthorizedPayment`, `AuthorizeOrder`, and `RefundCapturedPayment` are all `POST`. `HttpMethodsToRetry` gates only the status-trigger retry path; transport failures (`HttpRequestException`) retry on ALL verbs including `POST`, so a network blip can execute a payment or refund twice. Use `payPalRequestId` idempotency keys on writes that support it (all five payment operations accept `payPalRequestId`). **MUST load `dotnet-configuration-resilience`** before configuring `RetryOptions`.

- ⚠ **Step 6** (Vault error accessor is `TryGetError1`, NOT `TryGetError`) — `CreatePaymentToken`, `ListCustomerPaymentTokens`, and `DeletePaymentToken` all use `TryGetError1(out Error1)`, not the `TryGetError(out Error)` that Orders/Payments operations use. Writing `TryGetError` on a Vault error object is a compile error. **MUST load `dotnet-error-handling`** before writing any `try/catch` block.

- ⚠ **Step 8** (`SearchTransactions` is Case B) — `SearchTransactions` is the one Case B operation in this SDK (raw error, no typed accessor). Its catch block must be `catch (SdkException<RawError> ex)`, NOT `catch (SdkException<SearchTransactionsError> ex)` (no such type exists). Mixing Case A and Case B patterns is a build error. **MUST load `dotnet-error-handling`** before writing the boundary.

- ⚠ **Step 6a** (enum models vs record models) — all enums are `StringEnum<T>` records, not C# enums. `CheckoutPaymentIntent.Authorize` is correct; `CheckoutPaymentIntent.AUTHORIZE` does not compile. Use `.FromValue("AUTHORIZE")` only when a static member is not listed. **MUST load `dotnet-models`** before constructing any model with enum fields.

---

## 4. REQUIRED READING

The following companion skills govern specific steps. Load them **before implementation starts**. This sheet deliberately does not carry their contents — defaults, worked examples, and the parts a one-line note cannot cover live inside the skill.

| Skill | Step(s) governed |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, DI registration, `IHttpClientFactory` lifetime |
| `dotnet-authentication` | Step 1 — OAuth2 credentials, token strategy, secret loading |
| `dotnet-calling-endpoints` | Steps 2–8 — calling operations, `prefer` parameter, named arguments, response envelopes |
| `dotnet-models` | Steps 2–7 — `StringEnum<T>` construction, `init`-only records, enum field assignment |
| `dotnet-error-handling` | Step 9 (error boundary) + Steps 2–8 — Case A vs Case B, `TryGet…` accessors, `JsonException` boundary |
| `dotnet-configuration-resilience` | Step 1 — retry semantics, `Timeout` scope, base-URL / environment override, `RetryOptions` shape |
| `dotnet-testing` | All steps — SDK test seam (`HttpClient` constructor), mock response shape |

**`JsonException` boundary — two mandatory rows** (load `dotnet-error-handling` before writing the boundary):

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## 5. Assumptions & Blockers

| Item | Detail |
|---|---|
| **Assumption** | `PayPal:Currency` config value is a valid ISO 4217 code (e.g. `"USD"`) and is used as `AmountWithBreakdown.CurrencyCode`. |
| **Assumption** | Direct card payment (raw PAN on the wire) is PCI-compliant for this integration; no hosted-fields or client-side tokenization is in scope. |
| **Assumption** | `PaymentTokenRequest.Customer.Id` accepts the application's own customer/shopper identifier. If PayPal requires a PayPal-issued customer ID, the first vault call per customer must pass `Id = null` and the returned `PaymentTokenResponse.Customer?.Id` must be stored for subsequent calls. |
| ~~UNVERIFIED~~ **RESOLVED** | Production `ServerEnvironment`: `ServerEnvironment` has only one member (`Sandbox`). There is no `Live`/`Production` constant — the SDK throws on any other value. For `PayPal:Environment = "production"`, keep `options.Environment = ServerEnvironment.Sandbox` and set `options.Server.Sandbox.BaseUrl = "https://api-m.paypal.com"`. Source: `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs`. |
| **UNVERIFIED** | Vaulted-card payment token type — `TokenType` enum only documents `BillingAgreement`; PayPal's v3 vault API may require `PAYMENT_METHOD_TOKEN` on the wire. Use `StringEnum<TokenType>.FromValue("PAYMENT_METHOD_TOKEN")` as first attempt; fall back to `CardRequest.VaultId = tokenId` if rejected. Only live traffic can confirm. |
| **Blocker — none** | No blocking unknowns prevent planning; all UNVERIFIED items have defensive-coding directives above. |
