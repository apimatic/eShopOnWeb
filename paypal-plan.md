# PayPal Integration Plan — eShopOnWeb (.NET 8)

---

## 1. Scope & Sequence

| Step | eShop endpoint | PayPal SDK operations |
|---|---|---|
| 1 | `POST /api/orders` | (no PayPal call — eShop order creation only) |
| 2 | `POST /api/orders/{orderId}/pay` | `client.Orders.CreateOrder` → `client.Orders.AuthorizeOrder` |
| 3 | `POST /api/orders/{orderId}/fulfil` | `client.Payments.CaptureAuthorizedPayment`; on stale auth → `client.Payments.ReauthorizePayment` then retry |
| 4 | `POST /api/orders/{orderId}/cancel` | `client.Payments.VoidPayment` |
| 5 | `POST /api/orders/{orderId}/refunds` | `client.Payments.RefundCapturedPayment` |
| 6 | `GET /api/my-orders` | no PayPal call — read local payment record |
| 7 | `GET /api/reconciliation` | `client.TransactionSearch.SearchTransactions` (all pages) |
| 8 | `POST /api/payment-methods` | `client.Vault.CreateSetupToken` → `client.Vault.CreatePaymentToken` |
| 9 | `GET /api/payment-methods` | `client.Vault.ListCustomerPaymentTokens` |
| 10 | `DELETE /api/payment-methods/{id}` | `client.Vault.DeletePaymentToken` |
| 11 | `POST /api/orders/{orderId}/pay` (vault) | `client.Orders.CreateOrder` (Token source) → `client.Orders.AuthorizeOrder` |

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

### Namespaces required

```csharp
using PayPalServerSdk;                           // client, options
using PayPalServerSdk.Models;                    // all request/response records
using PayPalServerSdk.Models.Enums;              // all StringEnum<T> types
using PayPalServerSdk.Errors;                    // {Operation}Error types
using PayPalServerSdk.Servers;                   // ServerEnvironment
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials; // OAuth2ClientCredentials
using PayPalServerSdk.Core.ErrorResponse;        // RawError, ApiError (SdkException<T>)
```

---

### Client construction and auth

Source: `PayPalServerSdkClient.cs`, `PayPalServerSdkClientOptions.cs`, `ServiceCollectionExtensions.cs`, `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`

**CRITICAL — no Live `ServerEnvironment` member exists.** The SDK declares only `ServerEnvironment.Sandbox`. To target the live PayPal API, override the sandbox base URL. The environment selector (`PayPal:Environment`) must be translated at options-wiring time into a base URL string, not into a `ServerEnvironment` enum value.

| Item | Detail |
|---|---|
| Constructor | `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` |
| DI helper | `services.AddPayPalServerSdkClient(o => { … })` — registers client as `Singleton`, calls `services.AddHttpClient()` internally |
| `options.Oauth2` | `OAuth2ClientCredentials { ClientId: string (required), ClientSecret: string (required), Scope: string? }` |
| `options.Environment` | `ServerEnvironment` — only valid value is `ServerEnvironment.Sandbox`; do not attempt to set a Live member (it does not exist) |
| Sandbox base URL | `options.Server.Default.Sandbox.BaseUrl` — default `"https://api-m.sandbox.paypal.com"` |
| Live base URL | Set `options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"` while keeping `options.Environment = ServerEnvironment.Sandbox` |
| `PayPal:BaseUrl` override | When set, assign it verbatim to `options.Server.Default.Sandbox.BaseUrl`; this overrides both sandbox and live selection |

**Environment-selection logic** (in options-wiring lambda):

```
if PayPal:BaseUrl is set → options.Server.Default.Sandbox.BaseUrl = PayPal:BaseUrl
else if PayPal:Environment == "live" → options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"
else → leave BaseUrl at its default ("https://api-m.sandbox.paypal.com")
```

`options.Environment` is always `ServerEnvironment.Sandbox` regardless of configured environment.

---

### Step 2A — CreateOrder (AUTHORIZE intent, direct card)

Source: `map/operations/Orders.md`

| Field | Value |
|---|---|
| Controller | `client.Orders` |
| Signature | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion` — all nullable, no default; pass `null` to skip |
| Idempotency key | `payPalRequestId` — supply a stable per-attempt key for double-click safety |
| Returns | `Order` |
| Error | `SdkException<CreateOrderError>` — Case A |
| Error accessors | `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback] |

**`OrderRequest`** (`Models/OrderRequest.cs`, namespace `PayPalServerSdk.Models`):

| Field | Wire name | Type | Req? |
|---|---|---|---|
| `Intent` | `intent` | `CheckoutPaymentIntent` | required |
| `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnitRequest>` | required |
| `PaymentSource` | `payment_source` | `PaymentSource?` | optional |
| `Payer` | `payer` | `Payer?` | optional |
| `ApplicationContext` | `application_context` | `OrderApplicationContext?` | optional |

Set `Intent = CheckoutPaymentIntent.Authorize` (wire: `"AUTHORIZE"`).

**`PurchaseUnitRequest`** (`Models/PurchaseUnitRequest.cs`):

| Field | Wire name | Type | Req? |
|---|---|---|---|
| `Amount` | `amount` | `AmountWithBreakdown` | required |
| `ReferenceId` | `reference_id` | `string?` | optional |
| `Description` | `description` | `string?` | optional |
| `CustomId` | `custom_id` | `string?` | optional |
| `InvoiceId` | `invoice_id` | `string?` | optional |

**`AmountWithBreakdown`** (`Models/AmountWithBreakdown.cs`):

| Field | Wire name | Type | Req? |
|---|---|---|---|
| `CurrencyCode` | `currency_code` | `string` | required |
| `Value` | `value` | `string` | required — decimal as string, e.g. `"19.99"` |
| `Breakdown` | `breakdown` | `AmountBreakdown?` | optional |

**`PaymentSource`** for direct card (`Models/PaymentSource.cs`):
Set `PaymentSource.Card = new CardRequest { … }`.

**`CardRequest`** (`Models/CardRequest.cs`):

| Field | Wire name | Type | Notes |
|---|---|---|---|
| `Number` | `number` | `string?` | card number |
| `Expiry` | `expiry` | `string?` | `"YYYY-MM"` format |
| `SecurityCode` | `security_code` | `string?` | CVC |
| `Name` | `name` | `string?` | cardholder name |
| `BillingAddress` | `billing_address` | `Address?` | optional |
| `Attributes` | `attributes` | `CardAttributes?` | optional; can carry vault instruction |
| `VaultId` | `vault_id` | `string?` | not used for raw card flow |

**`PaymentSource`** for vault token:
Set `PaymentSource.Token = new Token { Id = vaultPaymentTokenId, Type = TokenType.BillingAgreement }`.

**`Token`** (`Models/Token.cs`):

| Field | Wire name | Type | Req? |
|---|---|---|---|
| `Id` | `id` | `string` | required — vault payment token ID |
| `Type` | `type` | `TokenType` | required — `TokenType.BillingAgreement` (wire: `"BILLING_AGREEMENT"`) |

**`Order`** response (`Models/Order.cs`):

| Field | Wire name | Type |
|---|---|---|
| `Id` | `id` | `string?` — PayPal Order ID |
| `Status` | `status` | `OrderStatus?` |
| `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnit>?` |
| `Links` | `links` | `IReadOnlyList<LinkDescription>?` |

---

### Step 2B — AuthorizeOrder

Source: `map/operations/Orders.md`

| Field | Value |
|---|---|
| Controller | `client.Orders` |
| Signature | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalClientMetadataId`, `payPalAuthAssertion`, `body` — all nullable, no default |
| Idempotency key | `payPalRequestId` |
| Returns | `OrderAuthorizeResponse` |
| Error | `SdkException<AuthorizeOrderError>` — Case A |
| Error accessors | `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback] |

**`OrderAuthorizeRequest`** (`Models/OrderAuthorizeRequest.cs`):

| Field | Wire name | Type |
|---|---|---|
| `PaymentSource` | `payment_source` | `OrderAuthorizeRequestPaymentSource?` |

**`OrderAuthorizeRequestPaymentSource`** (`Models/OrderAuthorizeRequestPaymentSource.cs`):

| Field | Wire name | Type | Use |
|---|---|---|---|
| `Card` | `card` | `CardRequest?` | direct card payment |
| `Token` | `token` | `Token?` | vault token payment |

For direct card: either provide `CardRequest` in `CreateOrder.PaymentSource.Card` and pass `body: null` to `AuthorizeOrder`, OR provide the card in `AuthorizeOrder.body.PaymentSource.Card` (without card in `CreateOrder`). Do not provide in both.

For vault token: provide `Token` in `AuthorizeOrder.body.PaymentSource.Token` (or in `CreateOrder.PaymentSource.Token`).

**`OrderAuthorizeResponse`** (`Models/OrderAuthorizeResponse.cs`):

| Field | Wire name | Type |
|---|---|---|
| `Id` | `id` | `string?` — PayPal Order ID |
| `Status` | `status` | `OrderStatus?` |
| `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnit>?` |

**Authorization ID extraction:**
```
response.PurchaseUnits[0].Payments.Authorizations[0].Id
```
- `PurchaseUnit.Payments` → `PaymentCollection?`
- `PaymentCollection.Authorizations` → `IReadOnlyList<AuthorizationWithAdditionalData>?`
- `AuthorizationWithAdditionalData.Id` → `string?` — Authorization ID
- `AuthorizationWithAdditionalData.Status` → `AuthorizationStatus?`
- `AuthorizationWithAdditionalData.ExpirationTime` → `string?` — ISO 8601

**STOP condition — challenge:** If `response.Status == OrderStatus.PayerActionRequired`, the merchant account requires a browser-redirect approval flow. Do **not** build the approval redirect. Return an actionable error to the caller: the sandbox test card Visa `4111111111111111` should not trigger this if the merchant account is configured for direct card processing. Treat `PAYER_ACTION_REQUIRED` as a hard error at the service boundary.

---

### Step 3A — CaptureAuthorizedPayment (fulfil)

Source: `map/operations/Payments.md`

| Field | Value |
|---|---|
| Controller | `client.Payments` |
| Signature | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` — all nullable, no default |
| Idempotency key | `payPalRequestId` |
| `prefer` | Pass `"return=representation"` to receive full `SellerReceivableBreakdown` (fee/net amounts) in the response; the default `"return=minimal"` may omit it |
| Returns | `CapturedPayment` |
| Error | `SdkException<CaptureAuthorizedPaymentError>` — Case A |
| Error accessors | `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |

**`CaptureRequest`** (`Models/CaptureRequest.cs`):

| Field | Wire name | Type | Notes |
|---|---|---|---|
| `Amount` | `amount` | `Money?` | omit for full capture |
| `FinalCapture` | `final_capture` | `bool? = false` | set `true` to release remaining auth |
| `InvoiceId` | `invoice_id` | `string?` | optional |
| `NoteToPayer` | `note_to_payer` | `string?` | optional |

**`CapturedPayment`** response (`Models/CapturedPayment.cs`):

| Field | Wire name | Type |
|---|---|---|
| `Id` | `id` | `string?` — Capture ID |
| `Status` | `status` | `CaptureStatus?` |
| `Amount` | `amount` | `Money?` — captured amount |
| `SellerReceivableBreakdown` | `seller_receivable_breakdown` | `SellerReceivableBreakdown?` |

**`SellerReceivableBreakdown`** (`Models/SellerReceivableBreakdown.cs`):

| Field | Wire name | Type |
|---|---|---|
| `GrossAmount` | `gross_amount` | `Money` — required |
| `PaypalFee` | `paypal_fee` | `Money?` — PayPal fee |
| `NetAmount` | `net_amount` | `Money?` — net proceeds |

**`Money`** (`Models/Money.cs`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`

**Stale-auth detection:** HTTP 422 from `CaptureAuthorizedPayment` (accessible via `TryGetError(out Error)`) may indicate the authorization has expired. Inspect `Error.Details[].Issue` for `AUTHORIZATION_PREVIOUSLY_VOIDED` or expiry signals. When detected, proceed to re-authorization (Step 3B).

---

### Step 3B — ReauthorizePayment (stale auth recovery)

Source: `map/operations/Payments.md`

| Field | Value |
|---|---|
| Controller | `client.Payments` |
| Signature | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass explicitly | `payPalRequestId`, `payPalAuthAssertion`, `body` — all nullable, no default |
| Returns | `PaymentAuthorization` |
| Error | `SdkException<ReauthorizePaymentError>` — Case A |
| Error accessors | `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |

**`ReauthorizeRequest`** (`Models/ReauthorizeRequest.cs`): `Amount (amount): Money?` — optional; omit to re-auth for original amount.

**`PaymentAuthorization`** response (`Models/PaymentAuthorization.cs`):

| Field | Wire name | Type |
|---|---|---|
| `Id` | `id` | `string?` — new Authorization ID (store this, replacing old one) |
| `Status` | `status` | `AuthorizationStatus?` |
| `ExpirationTime` | `expiration_time` | `string?` |

**Re-auth rules:**
- Valid window: days 4–29 after original 3-day honor period
- After 30 days total: re-auth is impossible; return actionable error to caller ("authorization too old to re-authorize; a new payment is required")
- On `SdkException<ReauthorizePaymentError>` with 404/422 and no viable re-auth: surface the actionable error, do not retry

---

### Step 4 — VoidPayment (cancel)

Source: `map/operations/Payments.md`

| Field | Value |
|---|---|
| Controller | `client.Payments` |
| Signature | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass explicitly | `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` — all nullable, no default |
| Returns | `PaymentAuthorization` |
| Error | `SdkException<VoidPaymentError>` — Case A |
| Error accessors | `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |

Note: HTTP 409 from `VoidPayment` indicates the authorization was already voided or captured — read `TryGetError(out Error)` and surface a meaningful message rather than treating it as a generic error.

---

### Step 5 — RefundCapturedPayment

Source: `map/operations/Payments.md`

| Field | Value |
|---|---|
| Controller | `client.Payments` |
| Signature | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` — all nullable, no default |
| Idempotency key | `payPalRequestId` — supply the caller-provided idempotency key to prevent double-refund |
| Returns | `Refund` |
| Error | `SdkException<RefundCapturedPaymentError>` — Case A |
| Error accessors | `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |

**`RefundRequest`** (`Models/RefundRequest.cs`):

| Field | Wire name | Type | Notes |
|---|---|---|---|
| `Amount` | `amount` | `Money?` | omit (or pass `null` body) for full refund; set for partial |
| `CustomId` | `custom_id` | `string?` | optional |
| `NoteToPayer` | `note_to_payer` | `string?` | optional |
| `InvoiceId` | `invoice_id` | `string?` | optional |

For full refund: pass `body: null`. For partial: `body: new RefundRequest { Amount = new Money { CurrencyCode = …, Value = "…" } }`.

**`Refund`** response (`Models/Refund.cs`):

| Field | Wire name | Type |
|---|---|---|
| `Id` | `id` | `string?` — Refund ID (persist this) |
| `Status` | `status` | `RefundStatus?` |
| `Amount` | `amount` | `Money?` |
| `SellerPayableBreakdown` | `seller_payable_breakdown` | `SellerPayableBreakdown?` |

HTTP 409 = already refunded (duplicate call). When `payPalRequestId` matches a prior successful refund, PayPal returns the original refund response — treat 409 as idempotent success (read `TryGetError(out Error)` to distinguish from a genuine conflict).

---

### Step 7 — SearchTransactions (reconciliation)

Source: `map/operations/TransactionSearch.md`

| Field | Value |
|---|---|
| Controller | `client.TransactionSearch` |
| Signature | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass explicitly | `transactionId`, `transactionType`, `transactionStatus`, `transactionAmount`, `transactionCurrency`, `paymentInstrumentType`, `storeId`, `terminalId` — all nullable, no default; pass `null` to skip each |
| Date format | ISO 8601 with timezone, e.g. `"2024-01-01T00:00:00-0000"` — both `startDate` and `endDate` required |
| Returns | `SearchResponse` |
| **Error** | **`SdkException<RawError>` — Case B** (this is the single Case B operation in the entire SDK) |
| Error accessors | `ex.Error.StatusCode` · `ex.Error.ReadAsString()` · `ex.Error.ReadAsJson<T>()` |

**`SearchResponse`** (`Models/SearchResponse.cs`):

| Field | Wire name | Type |
|---|---|---|
| `TransactionDetails` | `transaction_details` | `IReadOnlyList<TransactionDetails>?` |
| `Page` | `page` | `int?` — current page number |
| `TotalItems` | `total_items` | `int?` |
| `TotalPages` | `total_pages` | `int?` |

**Pagination loop** — the SDK provides only a `page` cursor (no automatic traversal):

```
currentPage = 1
do
    response = await SearchTransactions(startDate, endDate, ..., page: currentPage)
    collect response.TransactionDetails
    currentPage++
while currentPage <= response.TotalPages
```

Note: `SearchTransactions` uses named arguments to avoid positional mis-binding of the 8 optional nullable params — load `dotnet-calling-endpoints` before writing this call.

**`TransactionDetails`** (`Models/TransactionDetails.cs`):

| Field | Wire name | Type |
|---|---|---|
| `TransactionInfo` | `transaction_info` | `TransactionInformation?` |
| `PayerInfo` | `payer_info` | `PayerInformation?` |

**`TransactionInformation`** key fields (`Models/TransactionInformation.cs`): `TransactionId`, `TransactionAmount`, `FeeAmount`, `TransactionStatus`, `TransactionInitiationDate`, `PaypalReferenceId`.

---

### Step 8A — CreateSetupToken (vault a card)

Source: `map/operations/Vault.md`

| Field | Value |
|---|---|
| Controller | `client.Vault` |
| Signature | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass explicitly | `payPalRequestId` — nullable, no default |
| Returns | `SetupTokenResponse` |
| Error | `SdkException<CreateSetupTokenError>` — Case A |
| Error accessors | `TryGetError1(out Error1)` [400, 403, 422, 500] · `TryGetRawError(out RawError)` [fallback] |

Note: Vault operations use `TryGetError1(out Error1)` — not `TryGetError(out Error)`. These are different types.

**`Error1`** (`Models/Error1.cs`): `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails1>?`

**`SetupTokenRequest`** (`Models/SetupTokenRequest.cs`):

| Field | Wire name | Type | Req? |
|---|---|---|---|
| `Customer` | `customer` | `Customer?` | optional — carry merchant customer ID |
| `PaymentSource` | `payment_source` | `SetupTokenRequestPaymentSource` | required |

**`Customer`** (`Models/Customer.cs`): `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?`

**`SetupTokenRequestPaymentSource`** (`Models/SetupTokenRequestPaymentSource.cs`): set `Card = new SetupTokenRequestCard { … }` for card vaulting.

**`SetupTokenRequestCard`** (`Models/SetupTokenRequestCard.cs`):

| Field | Wire name | Type |
|---|---|---|
| `Number` | `number` | `string?` — card number |
| `Expiry` | `expiry` | `string?` — `"YYYY-MM"` |
| `SecurityCode` | `security_code` | `string?` |
| `Name` | `name` | `string?` |
| `BillingAddress` | `billing_address` | `Address?` |
| `VerificationMethod` | `verification_method` | `VaultCardVerificationMethod?` |

**`SetupTokenResponse`** (`Models/SetupTokenResponse.cs`):

| Field | Wire name | Type |
|---|---|---|
| `Id` | `id` | `string?` — Setup Token ID (pass to CreatePaymentToken) |
| `Status` | `status` | `PaymentTokenStatus?` |
| `Customer` | `customer` | `Customer?` |
| `PaymentSource` | `payment_source` | `SetupTokenResponsePaymentSource?` |

**`SetupTokenResponsePaymentSource.Card`** → `SetupTokenResponseCard`: `LastDigits (last_digits): string?`, `Brand (brand): CardBrand?`, `Expiry (expiry): string?`

---

### Step 8B — CreatePaymentToken (from setup token)

Source: `map/operations/Vault.md`

| Field | Value |
|---|---|
| Controller | `client.Vault` |
| Signature | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass explicitly | `payPalRequestId` — nullable, no default |
| Returns | `PaymentTokenResponse` |
| Error | `SdkException<CreatePaymentTokenError>` — Case A |
| Error accessors | `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback] |

**`PaymentTokenRequest`** (`Models/PaymentTokenRequest.cs`):

| Field | Wire name | Type | Req? |
|---|---|---|---|
| `Customer` | `customer` | `Customer?` | optional |
| `PaymentSource` | `payment_source` | `PaymentTokenRequestPaymentSource` | required |

**`PaymentTokenRequestPaymentSource`** (`Models/PaymentTokenRequestPaymentSource.cs`): set `Token = new VaultTokenRequest { Id = setupTokenId, Type = VaultTokenRequestType.SetupToken }`.

**`VaultTokenRequest`** (`Models/VaultTokenRequest.cs`): `Id (id): string !req`, `Type (type): VaultTokenRequestType !req` → `VaultTokenRequestType.SetupToken` (wire: `"SETUP_TOKEN"`)

**`PaymentTokenResponse`** (`Models/PaymentTokenResponse.cs`):

| Field | Wire name | Type |
|---|---|---|
| `Id` | `id` | `string?` — **Payment Token ID** — persist as `paymentMethodId` |
| `Customer` | `customer` | `CustomerResponse?` — `CustomerResponse.Id` is PayPal customer ID |
| `PaymentSource` | `payment_source` | `PaymentTokenResponsePaymentSource?` |

**`PaymentTokenResponsePaymentSource.Card`** → `CardPaymentTokenEntity`:

| Field | Wire name | Type |
|---|---|---|
| `LastDigits` | `last_digits` | `string?` — last 4 digits (safe to store/display) |
| `Brand` | `brand` | `CardBrand?` |
| `Expiry` | `expiry` | `string?` — `"YYYY-MM"` |

---

### Step 9 — ListCustomerPaymentTokens

Source: `map/operations/Vault.md`

| Field | Value |
|---|---|
| Controller | `client.Vault` |
| Signature | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `CustomerVaultPaymentTokensResponse` |
| Error | `SdkException<ListCustomerPaymentTokensError>` — Case A |
| Error accessors | `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback] |

**`CustomerVaultPaymentTokensResponse`** (`Models/CustomerVaultPaymentTokensResponse.cs`):

| Field | Wire name | Type |
|---|---|---|
| `PaymentTokens` | `payment_tokens` | `IReadOnlyList<PaymentTokenResponse>?` |
| `TotalItems` | `total_items` | `int?` |
| `TotalPages` | `total_pages` | `int?` |
| `Customer` | `customer` | `VaultResponseCustomer?` |

The `customerId` parameter maps to query param `customer_id`. The `pageSize` default is `5`; pass a larger value if needed. Map page notes "none (only `page`, no `perPage`)" — manual pagination applies here too if `totalPages > 1`.

---

### Step 10 — DeletePaymentToken

Source: `map/operations/Vault.md`

| Field | Value |
|---|---|
| Controller | `client.Vault` |
| Signature | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `void` (Task) |
| Error | `SdkException<DeletePaymentTokenError>` — Case A |
| Error accessors | `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback] |

---

### Enum values referenced

Namespace: `PayPalServerSdk.Models.Enums` | Source: `map/models/enums.md`

| Enum | C# member | Wire value | Use |
|---|---|---|---|
| `CheckoutPaymentIntent` | `CheckoutPaymentIntent.Authorize` | `"AUTHORIZE"` | CreateOrder intent |
| `OrderStatus` | `OrderStatus.PayerActionRequired` | `"PAYER_ACTION_REQUIRED"` | STOP condition check |
| `OrderStatus` | `OrderStatus.Completed` | `"COMPLETED"` | order fully processed |
| `OrderStatus` | `OrderStatus.Voided` | `"VOIDED"` | cancelled |
| `AuthorizationStatus` | `AuthorizationStatus.Created` | `"CREATED"` | auth ok |
| `AuthorizationStatus` | `AuthorizationStatus.Voided` | `"VOIDED"` | already voided |
| `AuthorizationStatus` | `AuthorizationStatus.Denied` | `"DENIED"` | auth denied |
| `CaptureStatus` | `CaptureStatus.Completed` | `"COMPLETED"` | capture ok |
| `CaptureStatus` | `CaptureStatus.Declined` | `"DECLINED"` | capture declined |
| `RefundStatus` | `RefundStatus.Completed` | `"COMPLETED"` | refund ok |
| `RefundStatus` | `RefundStatus.Pending` | `"PENDING"` | refund in progress |
| `TokenType` | `TokenType.BillingAgreement` | `"BILLING_AGREEMENT"` | vault token in pay flow |
| `VaultTokenRequestType` | `VaultTokenRequestType.SetupToken` | `"SETUP_TOKEN"` | CreatePaymentToken source |
| `PaymentTokenStatus` | `PaymentTokenStatus.Vaulted` | `"VAULTED"` | token ready to use |
| `StoreInVaultInstruction` | `StoreInVaultInstruction.OnSuccess` | `"ON_SUCCESS"` | vault-on-success flow |

---

### Error type summary — all operations in scope

| Operation | Error exception | Typed accessor | Fallback |
|---|---|---|---|
| `CreateOrder` | `SdkException<CreateOrderError>` | `TryGetError(out Error)` [400, 401, 422] | `TryGetRawError` |
| `AuthorizeOrder` | `SdkException<AuthorizeOrderError>` | `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] | `TryGetRawError` |
| `CaptureAuthorizedPayment` | `SdkException<CaptureAuthorizedPaymentError>` | `TryGetError(out Error)` [400–422] · `TryGetNoContent(out RawError)` [500] | `TryGetRawError` |
| `ReauthorizePayment` | `SdkException<ReauthorizePaymentError>` | `TryGetError(out Error)` [400–422] · `TryGetNoContent(out RawError)` [500] | `TryGetRawError` |
| `VoidPayment` | `SdkException<VoidPaymentError>` | `TryGetError(out Error)` [401–422] · `TryGetNoContent(out RawError)` [500] | `TryGetRawError` |
| `RefundCapturedPayment` | `SdkException<RefundCapturedPaymentError>` | `TryGetError(out Error)` [400–422] · `TryGetNoContent(out RawError)` [500] | `TryGetRawError` |
| `SearchTransactions` | **`SdkException<RawError>`** (**Case B**) | n/a — use `ex.Error.StatusCode`, `ex.Error.ReadAsString()` | n/a |
| `CreateSetupToken` | `SdkException<CreateSetupTokenError>` | **`TryGetError1(out Error1)`** [400, 403, 422, 500] | `TryGetRawError` |
| `CreatePaymentToken` | `SdkException<CreatePaymentTokenError>` | **`TryGetError1(out Error1)`** [400, 403, 404, 422, 500] | `TryGetRawError` |
| `ListCustomerPaymentTokens` | `SdkException<ListCustomerPaymentTokensError>` | **`TryGetError1(out Error1)`** [400, 403, 500] | `TryGetRawError` |
| `DeletePaymentToken` | `SdkException<DeletePaymentTokenError>` | **`TryGetError1(out Error1)`** [400, 403, 500] | `TryGetRawError` |

All error classes are in namespace `PayPalServerSdk.Errors`.

**`Error`** (`Models/Error.cs`): `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`

**`ErrorDetails`** (`Models/ErrorDetails.cs`): `Issue (issue): string !req`, `Field (field): string?`, `Description (description): string?`, `Location (location): string? = "body"`

**`Error1`** (`Models/Error1.cs`): same shape as `Error` but `Details` is `IReadOnlyList<ErrorDetails1>` and `Links` is `IReadOnlyList<ErrorLinkDescription>`.

---

### State to persist per payment record

| Field | Source |
|---|---|
| PayPal Order ID | `OrderAuthorizeResponse.Id` (or `Order.Id` from CreateOrder) |
| Authorization ID | `response.PurchaseUnits[0].Payments.Authorizations[0].Id` |
| Authorization Status | `AuthorizationWithAdditionalData.Status` |
| Capture ID | `CapturedPayment.Id` |
| Capture Status | `CapturedPayment.Status` |
| Refund IDs | `Refund.Id` (append per refund) |
| Refund Statuses | `Refund.Status` per entry |

---

## 3. Trap Notes

> ⚠ Step 2 (client setup) — The SDK only has `ServerEnvironment.Sandbox`; the `PayPal:Environment` config value must be translated to a base-URL string at options-wiring time, not to a `ServerEnvironment` member. There is no `Live` member; attempting to use one causes a compile error. **MUST load `dotnet-client-initialization`** before writing the DI registration.

> ⚠ Step 2 (auth) — `OAuth2ClientCredentials` has `required` properties (`ClientId`, `ClientSecret`); object initializer must set both. The credential object type is in `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` — a different namespace from the client itself. **MUST load `dotnet-authentication`** before wiring credentials.

> ⚠ Step 2 / Step 11 (calling endpoints) — `CreateOrder`, `AuthorizeOrder`, and `SearchTransactions` all have many nullable-no-default parameters that must be passed explicitly (including `null`) — positional calls will mis-bind silently. **MUST load `dotnet-calling-endpoints`** before writing any operation call.

> ⚠ Step 3 (capture) — To receive `SellerReceivableBreakdown` (PayPal fee and net proceeds) in the response, pass `prefer: "return=representation"` to `CaptureAuthorizedPayment`. The SDK default is `"return=minimal"` which may omit it. **MUST load `dotnet-calling-endpoints`** for named-argument guidance.

> ⚠ Step 3B (reauth) — whether a stale authorization can be re-sent depends on timing and PayPal's re-auth window semantics. **MUST load `dotnet-configuration-resilience`** before wiring retry logic — the SDK's `HttpMethodsToRetry` gate and transport-failure retry semantics affect whether a failed `POST /reauthorize` is retried automatically.

> ⚠ Step 5 (refund idempotency) — `payPalRequestId` is the idempotency key for `RefundCapturedPayment`. A 409 response means PayPal already processed a refund for that key — this is success, not an error. **MUST load `dotnet-error-handling`** before writing the catch ladder.

> ⚠ Step 7 (transaction search) — `SearchTransactions` is the **only Case B operation** in this SDK (`SdkException<RawError>`, no typed accessor). Its error-handling path is entirely different from every other operation. **MUST load `dotnet-error-handling`** — catching `SdkException<{Operation}Error>` around this call will not compile.

> ⚠ Step 8 (vault) — Vault operations use `TryGetError1(out Error1)`, not `TryGetError(out Error)`. These are distinct types. Using the wrong accessor will always return `false`. **MUST load `dotnet-error-handling`** before writing vault error boundaries.

> ⚠ Step 8 (models) — `StringEnum<T>` values (e.g. `TokenType`, `VaultTokenRequestType`, `CheckoutPaymentIntent`) are not C# enums — they are records. Do not use `new TokenType(…)` or cast from `string`. Use the static member: `TokenType.BillingAgreement`. **MUST load `dotnet-models`** before constructing any enum-typed field.

> ⚠ All steps (resilience) — The SDK's `Timeout` option is per-attempt, not a total-call budget. `HttpMethodsToRetry` controls the status-trigger gate but `POST` transport failures are retried on every verb. `CreateOrder`, `AuthorizeOrder`, and `RefundCapturedPayment` are not safe to retry without idempotency keys. **MUST load `dotnet-configuration-resilience`** before tuning retries.

---

## 4. REQUIRED READING

Load each skill **before implementation starts** for the step it governs. This sheet deliberately does not carry their contents — each resolves a trap that a one-line note cannot safely summarize.

| Skill | Steps governed |
|---|---|
| `dotnet-client-initialization` | Step 2 (DI registration, HttpClient lifetime, singleton registration) |
| `dotnet-authentication` | Step 2 (OAuth2 credential shape, setting credentials before construction) |
| `dotnet-calling-endpoints` | Steps 2, 3, 5, 7, 8, 9, 10, 11 (named arguments, nullable-no-default params, async/ct) |
| `dotnet-models` | Steps 2, 8 (StringEnum construction, record init, nullable handling) |
| `dotnet-error-handling` | All steps — error boundary design; see mandatory rows below |
| `dotnet-configuration-resilience` | Steps 3B, 7 (retry semantics, Timeout scope, pagination) |
| `dotnet-testing` | All — stub seam identification, test framework alignment |

**Mandatory `dotnet-error-handling` rows for the error boundary:**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.

- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the error boundary.

---

## 5. Assumptions & Blockers

| # | Item | Impact |
|---|---|---|
| 1 | The SDK has no `Live` `ServerEnvironment` member. The plan translates `PayPal:Environment` to a base-URL string override at options-wiring time. This is a factual SDK constraint, not an assumption. | Client setup code must set `options.Server.Default.Sandbox.BaseUrl` for live; implementer should verify the production base URL `"https://api-m.paypal.com"` is correct at sandbox test time. |
| 2 | The direct-card authorization flow requires the merchant account to be enabled for direct card processing. If PayPal returns `OrderStatus.PayerActionRequired`, the plan specifies this as a STOP condition — no approval redirect is built. The implementation must surface this as a `400 Bad Request` (or equivalent) with message "Card requires browser approval; contact merchant account support." | If the sandbox merchant account is not configured for direct card processing, end-to-end testing will fail at Step 2B. |
| 3 | `CustomerId` used as the vault `customer_id` parameter in `ListCustomerPaymentTokens` is assumed to be the merchant-assigned customer identifier stored in the app DB (or the PayPal customer ID returned from `CreatePaymentToken.Customer.Id`). The plan assumes the app persists either the PayPal customer ID or a merchant customer ID that was supplied during `CreateSetupToken`. | If no customer identifier mapping exists, `ListCustomerPaymentTokens` cannot be called. |
| 4 | `PayPal:Currency` config value is assumed to be a valid ISO 4217 code (e.g. `"USD"`). It is used verbatim as `AmountWithBreakdown.CurrencyCode` and `Money.CurrencyCode` throughout. No currency-validation logic is scoped. | If misconfigured, PayPal returns a 422. |
| 5 | The reconciliation endpoint (`GET /api/reconciliation?from={from}&to={to}`) receives dates; their format (ISO string, offset, etc.) is assumed to match what `SearchTransactions` requires (`"YYYY-MM-DDTHH:MM:SSZ"` or equivalent RFC3339). The plan does not include date-parsing logic. | Implementer must validate and format the `from`/`to` query params before passing to the SDK. |
| 6 | UNVERIFIED — Whether the live wire response for `CapturedPayment` always populates `SellerReceivableBreakdown.PaypalFee` when `prefer="return=representation"` is set. The SDK model declares it `Money?` (optional). The boundary should extract best-effort: if `PaypalFee` is `null`, report it as unavailable rather than failing. |  |
