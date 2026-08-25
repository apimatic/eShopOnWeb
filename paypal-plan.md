# PayPal Payment Integration Plan — eShopOnWeb

---

## 1. Scope & Sequence

| Step | Description | Operations used |
|---|---|---|
| 1 | Install SDK, DI-register client (`src/PublicApi` / `src/Infrastructure`) | — |
| 2 | Domain: add `PaymentRecord` (EF entity storing PayPal IDs + status) and wire `PaymentMethod` entity / `Buyer` aggregate | — |
| 3 | `POST /api/orders` — create eShop order (no PayPal call; set status `AwaitingPayment`) | — |
| 4 | `POST /api/orders/{orderId}/pay` — authorize: call `CreateOrder` (intent=AUTHORIZE) then `AuthorizeOrder` with card or vault token; persist PayPal order ID + auth ID | `Orders.CreateOrder`, `Orders.AuthorizeOrder` |
| 5 | `POST /api/orders/{orderId}/fulfil` — check auth staleness via `GetAuthorizedPayment`; re-authorize if needed via `ReauthorizePayment`; then `CaptureAuthorizedPayment`; persist capture ID, update status | `Payments.GetAuthorizedPayment`, `Payments.ReauthorizePayment`, `Payments.CaptureAuthorizedPayment` |
| 6 | `POST /api/orders/{orderId}/cancel` — void authorization via `VoidPayment`; update status | `Payments.VoidPayment` |
| 7 | `POST /api/orders/{orderId}/refunds` — refund via `RefundCapturedPayment`; persist refund ID; enforce no-over-refund guard app-side | `Payments.RefundCapturedPayment` |
| 8 | `GET /api/my-orders` — return eShop orders + payment state from `PaymentRecord` (no SDK call) | — |
| 9 | `GET /api/reconciliation?from=&to=` — paginate `SearchTransactions` until `Page == TotalPages`; match `TransactionDetails` against local `PaymentRecord` rows | `TransactionSearch.SearchTransactions` |
| 10 | `POST /api/payment-methods` — vault card: `CreatePaymentToken`; store token ID + safe descriptor in `PaymentMethod` entity | `Vault.CreatePaymentToken` |
| 11 | `GET /api/payment-methods` — `ListCustomerPaymentTokens` (or return from local `PaymentMethod` entity) | `Vault.ListCustomerPaymentTokens` |
| 12 | `DELETE /api/payment-methods/{id}` — `DeletePaymentToken`; remove from local entity | `Vault.DeletePaymentToken` |

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

### 2.1 Client construction & auth (source: `sdk-map.md` — Servers & auth, Getting a client)

| Fact | Value |
|---|---|
| Client class | `PayPalServerSdk.PayPalServerSdkClient` |
| Options class | `PayPalServerSdk.PayPalServerSdkClientOptions` |
| Auth property | `Oauth2: PayPalServerSdk.OAuth2ClientCredentials?` (set `ClientId` + `ClientSecret` on it) |
| Environment | `options.Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox` |
| Optional base-URL override | `options.Server.Default.Sandbox.BaseUrl = "https://..."` — exact chain: `ServerOptions.Default` (`DefaultOptions`) → `DefaultOptions.Sandbox` (`SandboxOptions`) → `SandboxOptions.BaseUrl` (`string`); source: `ServerOptions.cs`, `Servers/DefaultOptions.cs` |
| DI registration | `services.AddPayPalServerSdkClient(o => { ... })` |
| Config keys | `PayPal:ClientId`, `PayPal:ClientSecret` → bind to `OAuth2ClientCredentials`; `PayPal:Environment`, `PayPal:Currency` for app use; `PayPal:BaseUrl` optional |

Namespaces required (add all `using` directives):

```
using PayPalServerSdk;
using PayPalServerSdk.Api;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Servers;       // ServerEnvironment
using PayPalServerSdk.Core.Exceptions; // SdkException<T>
using PayPalServerSdk.Core.ErrorResponse; // RawError, ApiError
```

---

### 2.2 Step 4 — Authorize payment (`Orders.CreateOrder` + `Orders.AuthorizeOrder`)

Source: `map/operations/Orders.md`

#### `CreateOrder`

| | |
|---|---|
| Controller property | `client.Orders` |
| Method | `CreateOrder` |
| Full signature | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly params | `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion` — all nullable, no default; pass `null` to skip |
| Idempotency | `payPalRequestId` = caller-supplied UUID; same key → same PayPal order, no duplicate |
| Return type | `PayPalServerSdk.Models.Order` |
| Error case | **Case A** — `SdkException<CreateOrderError>` — `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback] |

**Request model: `OrderRequest`** (`PayPalServerSdk.Models.OrderRequest`)

| Field | Wire name | Type | Required? | Value to set |
|---|---|---|---|---|
| `Intent` | `intent` | `CheckoutPaymentIntent` | `!req` | `CheckoutPaymentIntent.Authorize` |
| `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnitRequest>` | `!req` | One element; amount = eShop order total |
| `PaymentSource` | `payment_source` | `PaymentSource?` | optional | `null` here; payment source goes on `AuthorizeOrder` |
| `Payer` | `payer` | `Payer?` | optional | null |

**`PurchaseUnitRequest`** (`PayPalServerSdk.Models.PurchaseUnitRequest`)

| Field | Wire name | Type | Required? |
|---|---|---|---|
| `Amount` | `amount` | `AmountWithBreakdown` | `!req` |
| `CustomId` | `custom_id` | `string?` | optional — use eShop `Order.Id` for correlation |

**`AmountWithBreakdown`** (`PayPalServerSdk.Models.AmountWithBreakdown`)

| Field | Wire name | Type | Required? |
|---|---|---|---|
| `CurrencyCode` | `currency_code` | `string` | `!req` — read from `PayPal:Currency` config |
| `Value` | `value` | `string` | `!req` — decimal as string, e.g. `"49.99"` |

**Response — reading `Order`** (`PayPalServerSdk.Models.Order`)

| Field to read | Path | Type |
|---|---|---|
| PayPal order ID | `.Id` | `string?` |
| Order status | `.Status` | `OrderStatus?` |

Persist `Order.Id` as the PayPal order ID in `PaymentRecord`.

---

#### `AuthorizeOrder`

| | |
|---|---|
| Controller property | `client.Orders` |
| Method | `AuthorizeOrder` |
| Full signature | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `id` | The PayPal order ID from `CreateOrder` |
| Must-pass-explicitly params | `payPalMockResponse`, `payPalRequestId`, `payPalClientMetadataId`, `payPalAuthAssertion` — nullable, no default |
| Idempotency | `payPalRequestId` — same key → same authorization, no double charge |
| `prefer` | Pass `"return=representation"` to ensure the full authorization object (including auth ID) is in the response body (UNVERIFIED: whether `"return=minimal"` omits the auth ID; use representation defensively) |
| Return type | `PayPalServerSdk.Models.OrderAuthorizeResponse` |
| Error case | **Case A** — `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback] |

**Request model: `OrderAuthorizeRequest`** (`PayPalServerSdk.Models.OrderAuthorizeRequest`)

| Field | Wire name | Type | Required? |
|---|---|---|---|
| `PaymentSource` | `payment_source` | `OrderAuthorizeRequestPaymentSource?` | optional |

**`OrderAuthorizeRequestPaymentSource`** (`PayPalServerSdk.Models.OrderAuthorizeRequestPaymentSource`)

| Field | Wire name | Type | Notes |
|---|---|---|---|
| `Card` | `card` | `CardRequest?` | Use for fresh-card payments; set one of the two options below |
| `Token` | `token` | `Token?` | Use for saved-card payments via vault token |

**Path A — fresh card (`CardRequest`)** (`PayPalServerSdk.Models.CardRequest`)

| Field | Wire name | Type | Notes |
|---|---|---|---|
| `Number` | `number` | `string?` | Full PAN — never log or persist |
| `Expiry` | `expiry` | `string?` | Format `YYYY-MM` |
| `SecurityCode` | `security_code` | `string?` | CVV — never log or persist |
| `Name` | `name` | `string?` | Cardholder name |
| `BillingAddress` | `billing_address` | `Address?` | `Address.CountryCode` is `!req` |

**Path B — saved card via vault token (`Token`)** (`PayPalServerSdk.Models.Token`)

| Field | Wire name | Type | Required? | Value |
|---|---|---|---|---|
| `Id` | `id` | `string` | `!req` | The `PaymentTokenResponse.Id` from vault step |
| `Type` | `type` | `TokenType` | `!req` | `TokenType.BillingAgreement` (only member) |

**Response — reading `OrderAuthorizeResponse`** (`PayPalServerSdk.Models.OrderAuthorizeResponse`)

| Field to read | Path | Type |
|---|---|---|
| Authorization ID | `.PurchaseUnits[0].Payments.Authorizations[0].Id` | `string?` |
| Authorization status | `.PurchaseUnits[0].Payments.Authorizations[0].Status` | `AuthorizationStatus?` |
| Order status | `.Status` | `OrderStatus?` |

Navigation chain types: `PurchaseUnits` → `IReadOnlyList<PurchaseUnit>` · `PurchaseUnit.Payments` → `PaymentCollection?` · `PaymentCollection.Authorizations` → `IReadOnlyList<AuthorizationWithAdditionalData>?` · `AuthorizationWithAdditionalData.Id` → `string?`

Persist auth ID in `PaymentRecord`. `AuthorizationStatus` values from `map/models/enums.md`: `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending`.

---

### 2.3 Step 5 — Fulfil: check staleness, reauthorize if needed, capture

#### `GetAuthorizedPayment` (staleness check)

Source: `map/operations/Payments.md`

| | |
|---|---|
| Controller property | `client.Payments` |
| Method | `GetAuthorizedPayment` |
| Full signature | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalAuthAssertion` — nullable, no default |
| Return type | `PayPalServerSdk.Models.PaymentAuthorization` |
| Error case | **Case A** — `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |

**Response — reading `PaymentAuthorization`** (`PayPalServerSdk.Models.PaymentAuthorization`)

| Field | Path | Type | Use |
|---|---|---|---|
| Status | `.Status` | `AuthorizationStatus?` | Staleness: `Voided` or `Denied` = not re-authorizable; `Pending` = may be reauthorizable |
| Expiration time | `.ExpirationTime` | `string?` | ISO-8601; compare to `DateTimeOffset.UtcNow` |

**Staleness rules (from `map/operations/Payments.md` notes on `ReauthorizePayment`):**

| Condition | Action |
|---|---|
| Within 3-day honor period AND status `Created`/`PartiallyCaptured` | Capture directly |
| Day 4–29 from original auth, status `Created`/`PartiallyCaptured` | `ReauthorizePayment`, then capture with new auth ID |
| > 29 days from original auth (> 30 days total window) | Cannot re-authorize; must create a new PayPal order + authorization; return operator-actionable error if that path is not wired |
| Status `Voided` or `Denied` | Cannot capture; return operator-actionable error |

---

#### `ReauthorizePayment`

| | |
|---|---|
| Controller property | `client.Payments` |
| Method | `ReauthorizePayment` |
| Full signature | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalRequestId`, `payPalAuthAssertion`, `body` — nullable, no default |
| Return type | `PayPalServerSdk.Models.PaymentAuthorization` |
| Error case | **Case A** — `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |

**Request model: `ReauthorizeRequest`** (`PayPalServerSdk.Models.ReauthorizeRequest`)

| Field | Wire name | Type | Notes |
|---|---|---|---|
| `Amount` | `amount` | `Money?` | Optional; defaults to original amount; set explicitly if needed |

**`Money`** (`PayPalServerSdk.Models.Money`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req` (decimal as string)

**Response:** `PaymentAuthorization.Id` is the **new** authorization ID. Update `PaymentRecord` with the new auth ID before capturing.

---

#### `CaptureAuthorizedPayment`

| | |
|---|---|
| Controller property | `client.Payments` |
| Method | `CaptureAuthorizedPayment` |
| Full signature | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` — nullable, no default |
| Idempotency | `payPalRequestId` — same key → same capture, no double-capture |
| `prefer` | Pass `"return=representation"` to ensure `SellerReceivableBreakdown` is present (UNVERIFIED: whether minimal omits it; use representation defensively) |
| Return type | `PayPalServerSdk.Models.CapturedPayment` |
| Error case | **Case A** — `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |

**Request model: `CaptureRequest`** (`PayPalServerSdk.Models.CaptureRequest`)

| Field | Wire name | Type | Notes |
|---|---|---|---|
| `Amount` | `amount` | `Money?` | Null = capture full authorized amount |
| `FinalCapture` | `final_capture` | `bool? = false` | Set `true` to release remaining authorized amount |

**Response — reading `CapturedPayment`** (`PayPalServerSdk.Models.CapturedPayment`)

| Field | Path | Type | Use |
|---|---|---|---|
| Capture ID | `.Id` | `string?` | Persist; needed for refunds |
| Captured amount | `.SellerReceivableBreakdown.GrossAmount` | `Money` (`!req`) | Show to operator |
| PayPal fee | `.SellerReceivableBreakdown.PaypalFee` | `Money?` | Show to operator |
| Net proceeds | `.SellerReceivableBreakdown.NetAmount` | `Money?` | Show to operator |
| Status | `.Status` | `CaptureStatus?` | `Completed` = success |

`SellerReceivableBreakdown` type: `PayPalServerSdk.Models.SellerReceivableBreakdown`. `CaptureStatus` values: `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed`.

---

### 2.4 Step 6 — Cancel: void authorization

Source: `map/operations/Payments.md`

#### `VoidPayment`

| | |
|---|---|
| Controller property | `client.Payments` |
| Method | `VoidPayment` |
| Full signature | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` — nullable, no default |
| Return type | `PayPalServerSdk.Models.PaymentAuthorization` |
| Error case | **Case A** — `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |

No request body. HTTP 409 = already captured (cannot void); handle as operator-actionable error.

---

### 2.5 Step 7 — Refund after capture

Source: `map/operations/Payments.md`

#### `RefundCapturedPayment`

| | |
|---|---|
| Controller property | `client.Payments` |
| Method | `RefundCapturedPayment` |
| Full signature | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` — nullable, no default |
| Idempotency | `payPalRequestId` — caller supplies stable key; same key → same refund, no double-refund |
| Return type | `PayPalServerSdk.Models.Refund` |
| Error case | **Case A** — `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |

**Request model: `RefundRequest`** (`PayPalServerSdk.Models.RefundRequest`)

| Field | Wire name | Type | Notes |
|---|---|---|---|
| `Amount` | `amount` | `Money?` | Null body (or null Amount) = full refund; set for partial |
| `InvoiceId` | `invoice_id` | `string?` | Optional secondary correlation ID |

**No-over-refund:** Enforce app-side: sum all prior refund amounts for this capture from `PaymentRecord`; reject if `requestedAmount + alreadyRefunded > capturedAmount`.

**Response — reading `Refund`** (`PayPalServerSdk.Models.Refund`)

| Field | Path | Type |
|---|---|---|
| Refund ID | `.Id` | `string?` |
| Status | `.Status` | `RefundStatus?` |
| Total refunded | `.SellerPayableBreakdown.TotalRefundedAmount` | `Money?` |

`RefundStatus` values (`map/models/enums.md`): `Cancelled`, `Failed`, `Pending`, `Completed`. Persist refund ID in `PaymentRecord`.

---

### 2.6 Step 9 — Reconciliation (transaction search, full pagination)

Source: `map/operations/TransactionSearch.md`

#### `SearchTransactions`

| | |
|---|---|
| Controller property | `client.TransactionSearch` |
| Method | `SearchTransactions` |
| Full signature | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Required params | `startDate`, `endDate` — ISO-8601 datetime strings, e.g. `"2025-01-01T00:00:00-0700"` |
| Must-pass-explicitly | `transactionId`, `transactionType`, `transactionStatus`, `transactionAmount`, `transactionCurrency`, `paymentInstrumentType`, `storeId`, `terminalId` — nullable, no default; pass `null` to skip |
| Return type | `PayPalServerSdk.Models.SearchResponse` |
| **Error case** | **Case B** — `SdkException<RawError>` — `ex.Error.StatusCode` · `ex.Error.ReadAsString()` · `ex.Error.ReadAsJson<T>()` |

**Pagination loop** (must cover full range — do NOT stop at page 1):

```
page = 1
do
    response = SearchTransactions(startDate, endDate, null, null, ..., page: page, ...)
    collect response.TransactionDetails
    page++
while page <= response.TotalPages
```

**Response — reading `SearchResponse`** (`PayPalServerSdk.Models.SearchResponse`)

| Field | Path | Type |
|---|---|---|
| Transaction list | `.TransactionDetails` | `IReadOnlyList<TransactionDetails>?` |
| Current page | `.Page` | `int?` |
| Total pages | `.TotalPages` | `int?` |

**`TransactionDetails`** (`PayPalServerSdk.Models.TransactionDetails`):
- `.TransactionInfo` → `TransactionInformation?`
  - `.TransactionId` — PayPal transaction ID (string?)
  - `.TransactionAmount` → `Money?`
  - `.FeeAmount` → `Money?`
  - `.TransactionStatus` — status code string (string?)
  - `.PaypalReferenceId` — related PayPal order/auth/capture ID (string?)
  - `.TransactionInitiationDate` — ISO-8601 string (string?)

Match on `PaypalReferenceId` against stored `PaymentRecord` PayPal IDs to correlate with eShop orders.

---

### 2.7 Steps 10–12 — Card vaulting and payment method management

Source: `map/operations/Vault.md`

#### `CreatePaymentToken` (vault a card)

| | |
|---|---|
| Controller property | `client.Vault` |
| Method | `CreatePaymentToken` |
| Full signature | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `payPalRequestId` | Must pass explicitly; provides idempotency for vault creation |
| Return type | `PayPalServerSdk.Models.PaymentTokenResponse` |
| Error case | **Case A** — `SdkException<CreatePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback] |

**Request model: `PaymentTokenRequest`** (`PayPalServerSdk.Models.PaymentTokenRequest`)

| Field | Wire name | Type | Required? |
|---|---|---|---|
| `Customer` | `customer` | `Customer?` | optional — set `Customer.Id` to stable per-user merchant customer ID |
| `PaymentSource` | `payment_source` | `PaymentTokenRequestPaymentSource` | `!req` |

**`Customer`** (`PayPalServerSdk.Models.Customer`): `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?`. Use `Id` = stable user identity (e.g. SHA-256 of username) to group tokens per user.

**`PaymentTokenRequestPaymentSource`** (`PayPalServerSdk.Models.PaymentTokenRequestPaymentSource`):

| Field | Wire name | Type | Notes |
|---|---|---|---|
| `Card` | `card` | `PaymentTokenRequestCard?` | Use for direct card vault |

**`PaymentTokenRequestCard`** (`PayPalServerSdk.Models.PaymentTokenRequestCard`)

| Field | Wire name | Type | Notes |
|---|---|---|---|
| `Name` | `name` | `string?` | Cardholder name |
| `Number` | `number` | `string?` | Full PAN — never persist, never log |
| `Expiry` | `expiry` | `string?` | Format `YYYY-MM` |
| `SecurityCode` | `security_code` | `string?` | CVV — never persist, never log |
| `BillingAddress` | `billing_address` | `Address?` | `Address.CountryCode` is `!req` |

**Response — reading `PaymentTokenResponse`** (`PayPalServerSdk.Models.PaymentTokenResponse`)

| Field | Path | Type | Persist |
|---|---|---|---|
| Vault token ID | `.Id` | `string?` | YES — store in `PaymentMethod.VaultTokenId` |
| Last digits | `.PaymentSource.Card.LastDigits` | `string?` | YES — safe descriptor |
| Brand | `.PaymentSource.Card.Brand` | `CardBrand?` | YES — safe descriptor |
| Expiry | `.PaymentSource.Card.Expiry` | `string?` | YES — safe descriptor |
| Card type | `.PaymentSource.Card.Type` | `CardType?` | YES — safe descriptor |

Navigation: `PaymentSource` → `PaymentTokenResponsePaymentSource?` → `Card` → `CardPaymentTokenEntity?`

**Full card details (`Number`, `SecurityCode`) must never be stored in the app database or logged.**

---

#### `ListCustomerPaymentTokens`

| | |
|---|---|
| Controller property | `client.Vault` |
| Method | `ListCustomerPaymentTokens` |
| Full signature | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `customerId` | Merchant-side customer ID (same value used as `Customer.Id` in vault step) |
| Return type | `PayPalServerSdk.Models.CustomerVaultPaymentTokensResponse` |
| Error case | **Case A** — `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback] |

**Response fields:** `.PaymentTokens: IReadOnlyList<PaymentTokenResponse>?`, `.TotalItems: int?`, `.TotalPages: int?`

Note: The map records "only `page`, no `perPage`" for this operation's pagination — there is no cursor-based next-page link in the SDK signature; increment `page` and re-call if `TotalPages > 1`.

Alternatively, serve the list entirely from the local `PaymentMethod` entity (populated at vault time) and call `ListCustomerPaymentTokens` only to reconcile or validate. This avoids a PayPal roundtrip on every page load.

---

#### `DeletePaymentToken`

| | |
|---|---|
| Controller property | `client.Vault` |
| Method | `DeletePaymentToken` |
| Full signature | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `id` | The `PaymentTokenResponse.Id` (vault token ID) |
| Returns | `void` (Task) |
| Error case | **Case A** — `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback] |

After a successful delete, mark the local `PaymentMethod` entity as deleted/inactive so it cannot be passed to subsequent authorize calls.

---

### 2.8 Enum value tables (in-scope only)

Source: `map/models/enums.md`. All in namespace `PayPalServerSdk.Models.Enums`.

| Enum | Members (C# name — wire value) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` — only member |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` — only member |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |

Enums are `StringEnum<T>` records — **not** C# enums. Construct via the static member (e.g. `CheckoutPaymentIntent.Authorize`) or `CheckoutPaymentIntent.FromValue("AUTHORIZE")`. Do not use `new`.

---

### 2.9 Error model shapes

Source: `map/models/records-1-Ac-Pa.md`

**`Error`** (used by Orders and Payments operations with `TryGetError`) — `PayPalServerSdk.Models.Error`:

| Field | Wire name | Type |
|---|---|---|
| `Name` | `name` | `string !req` |
| `Message` | `message` | `string !req` |
| `DebugId` | `debug_id` | `string !req` |
| `Details` | `details` | `IReadOnlyList<ErrorDetails>?` |

**`Error1`** (used by Vault operations with `TryGetError1`) — `PayPalServerSdk.Models.Error1`:

| Field | Wire name | Type |
|---|---|---|
| `Name` | `name` | `string !req` |
| `Message` | `message` | `string !req` |
| `DebugId` | `debug_id` | `string !req` |
| `Details` | `details` | `IReadOnlyList<ErrorDetails1>?` |

**`RawError`** (Case B — `SearchTransactions`, and fallback on all Case A): `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`

**No-throw variants are absent across the entire SDK** — every operation throws on error.

---

### 2.10 Idempotency key summary

| Operation | Parameter that carries idempotency key | Scope |
|---|---|---|
| `CreateOrder` | `payPalRequestId` | Prevents duplicate PayPal orders |
| `AuthorizeOrder` | `payPalRequestId` | Prevents double-authorization |
| `CaptureAuthorizedPayment` | `payPalRequestId` | Prevents double-capture |
| `RefundCapturedPayment` | `payPalRequestId` | Prevents double-refund |
| `CreatePaymentToken` | `payPalRequestId` | Prevents duplicate vault entries |

The caller of `POST /api/orders/{orderId}/pay` should supply or derive a stable idempotency key (e.g. `$"pay-{orderId}"`). The caller of `POST /api/orders/{orderId}/refunds` must supply the idempotency key in the request body; the service passes it as `payPalRequestId`. The app must also guard against over-refund at the application layer (sum of prior refunds ≤ captured amount).

---

## 3. Trap Notes

> ⚠ **Step 1 (client registration)** — the SDK's retry `Timeout` property does not bound the total call duration; it is per-attempt. The `HttpClient` lifetime and handler pipeline must be managed through `IHttpClientFactory`, not rebuilt per request. **MUST load `dotnet-client-initialization`** before wiring the client into DI.

> ⚠ **Step 2 (authentication)** — credentials go on `PayPalServerSdkClientOptions.Oauth2` (`OAuth2ClientCredentials`), not on a separate builder call. The namespace for `OAuth2ClientCredentials` is not `PayPalServerSdk.Models` — take it from the map's *Servers & auth* section. **MUST load `dotnet-authentication`** before setting credentials.

> ⚠ **Steps 4–7, 10–12 (calling operations)** — the 4–5 nullable header params on `CreateOrder`, `AuthorizeOrder`, `CaptureAuthorizedPayment`, `RefundCapturedPayment`, and `CreatePaymentToken` have no C# default and **must be passed explicitly** (pass `null` to skip each). A positional call that omits them mis-binds every argument that follows. **MUST load `dotnet-calling-endpoints`** before writing the first SDK call.

> ⚠ **Steps 4–12 (models)** — `TokenType`, `CheckoutPaymentIntent`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus`, `VaultStatus`, and `CardBrand` are `StringEnum<T>` records, not C# enums. Using `new` or casting will not compile. `CardRequest`, `PurchaseUnitRequest`, and `AmountWithBreakdown` have `required` properties that must be set in the object initializer. **MUST load `dotnet-models`** before building any request model.

> ⚠ **Step 5 (re-authorization)** — `ReauthorizePayment` returns a **new** `PaymentAuthorization` with a **new** authorization ID. The old auth ID is now stale. `PaymentRecord` must be updated with the new auth ID before `CaptureAuthorizedPayment` is called; using the old ID will fail with 404 or 422. **MUST load `dotnet-calling-endpoints`** for response-reading patterns.

> ⚠ **Step 9 (reconciliation)** — `SearchTransactions` is the **only Case B operation** in this integration (`SdkException<RawError>`). Its error handler shape differs from all other operations. A catch ladder that only catches `SdkException<{Operation}Error>` will let this exception escape the boundary. **MUST load `dotnet-error-handling`** and handle Case B explicitly.

> ⚠ **Step 9 (reconciliation pagination)** — the `page` parameter defaults to `1` and `pageSize` to `100`. The operation has no server-side cursor — you must loop, incrementing `page`, until `response.Page == response.TotalPages`. Stopping after the first response silently truncates the date range. **MUST load `dotnet-configuration-resilience`** for pagination patterns.

> ⚠ **Steps 10–12 (vault error type)** — Vault operations (`CreatePaymentToken`, `ListCustomerPaymentTokens`, `DeletePaymentToken`) use `TryGetError1(out Error1)`, not `TryGetError(out Error)`. `Error1` is a different type from `Error` (different `Details` element type; different `Links` type). A catch block that destructures `Error` on a vault exception will fail to compile. **MUST load `dotnet-error-handling`** before writing vault error handlers.

> ⚠ **Steps 4, 10 (card data hygiene)** — `CardRequest.Number`, `CardRequest.SecurityCode`, `PaymentTokenRequestCard.Number`, and `PaymentTokenRequestCard.SecurityCode` must never be logged, persisted, or included in error detail records. Wire a logging filter before enabling SDK-level logging. **MUST load `dotnet-configuration-resilience`** for logging configuration.

---

## 4. REQUIRED READING

Load these skills **before implementation starts**. This sheet deliberately does not carry their contents — they govern the runtime behavior, defaults, and wiring details that a one-line note cannot safely summarize.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, `IHttpClientFactory`, DI registration |
| `dotnet-authentication` | Step 2 — `OAuth2ClientCredentials`, credential binding, rotation |
| `dotnet-calling-endpoints` | Steps 4–12 — named arguments, must-pass params, response reading, async patterns |
| `dotnet-models` | Steps 4–12 — `StringEnum<T>`, `required` initializers, nullable fields |
| `dotnet-error-handling` | ALL steps — Case A vs Case B error boundary, `TryGet…` accessor mechanics |
| `dotnet-configuration-resilience` | Steps 1, 9 — retry semantics, `Timeout` scope, pagination, logging |
| `dotnet-testing` | All steps — `HttpClient` test seam, mock response setup |

**Error-boundary hazards (mandatory rows — do not omit):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the error boundary.

---

## 5. Assumptions & Blockers

| # | Item |
|---|---|
| A1 | The eShop `PaymentMethod` entity shell (currently on `Buyer`) will be **replaced** with a new EF-mapped entity carrying: `VaultTokenId (string)`, `LastFourDigits (string)`, `CardBrand (string)`, `Expiry (string)`, `CardType (string)`, `CustomerId (string)` (maps to PayPal `Customer.Id`), `IsDeleted (bool)`. The existing shell fields (if any) should be mapped or removed. |
| A2 | A new `PaymentRecord` EF entity is added to `ApplicationCore`/`Infrastructure` with: `OrderId (int)`, `PayPalOrderId (string?)`, `AuthorizationId (string?)`, `CaptureId (string?)`, `RefundIds (List<string>)`, `TotalRefundedAmount (decimal)`, `Status (enum)`, `UpdatedAt (DateTimeOffset)`. |
| A3 | The stable `Customer.Id` passed to PayPal vault is derived from the JWT `BuyerId` (username/email) — a deterministic mapping (e.g. `BuyerId` directly, or a hash) agreed upon before Step 10 implementation. |
| A4 | `POST /api/orders/{orderId}/pay` makes two sequential SDK calls (`CreateOrder` + `AuthorizeOrder`). If `CreateOrder` succeeds but `AuthorizeOrder` fails, the orphaned PayPal order should be voided or left to expire. The brief does not specify — assume: log the orphaned PayPal order ID and return the auth failure to the caller; do not auto-void (the PayPal order expires automatically). |
| A5 | Re-authorization after 30 days (where `ReauthorizePayment` is no longer allowed) requires creating a new `OrderRequest`. The brief says "return an operator-actionable error if renewal is impossible" — assumed to mean a `422` or `409` response with a descriptive message, not an automatic new-order creation. |
| A6 | `prefer = "return=representation"` is used for `AuthorizeOrder` and `CaptureAuthorizedPayment` to ensure the full response payload is returned. UNVERIFIED: whether `"return=minimal"` actually omits the auth ID or `SellerReceivableBreakdown` on the live wire — this is the documented defensive-coding directive. |
| B1 | **Blocker (potential):** The map records `TokenType` with only one member: `BillingAgreement`. If the live vault API requires a different `TokenType` value for a card payment token, the `Token`-based payment path will fail. UNVERIFIED from the SDK source — only live traffic can confirm. Defensive fallback: prefer `CardRequest.VaultId` (set to the `PaymentTokenResponse.Id`) instead of `Token` for the saved-card payment path; both approaches are in this sheet. |
