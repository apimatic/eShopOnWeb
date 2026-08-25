# PayPal Integration Plan — eShopOnWeb

---

## 1. Scope & Sequence

| Step | What | SDK controller / operations |
|------|------|-----------------------------|
| 1 | Install package & register client in ASP.NET Core DI | `AddPayPalServerSdkClient` / `PayPalServerSdkClient` ctor |
| 2 | Wire OAuth2 credentials and environment | `PayPalServerSdkClientOptions.Oauth2` |
| 3 | **Authorize payment** — create order (AUTHORIZE intent) with card or vault token, then authorize | `client.Orders.CreateOrder` → `client.Orders.AuthorizeOrder` |
| 4 | **Capture payment** — capture an authorization; reauthorize first if stale/expired | `client.Payments.CaptureAuthorizedPayment` (+ `client.Payments.ReauthorizePayment` on stale) |
| 5 | **Void authorization** | `client.Payments.VoidPayment` |
| 6 | **Refund** — partial or full, idempotent per caller-supplied key | `client.Payments.RefundCapturedPayment` |
| 7 | **Transaction reconciliation** — paginate all pages for date range | `client.TransactionSearch.SearchTransactions` (manual page loop) |
| 8 | **Save card** (vault) | `client.Vault.CreatePaymentToken` |
| 9 | **List saved cards** | `client.Vault.ListCustomerPaymentTokens` |
| 10 | **Delete saved card** | `client.Vault.DeletePaymentToken` |

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

### 2.1 Required `using` directives

| Contents | Namespace |
|----------|-----------|
| Client, options, DI extension | `PayPalServerSdk` |
| Server environment enum | `PayPalServerSdk.Servers` |
| All request/response records | `PayPalServerSdk.Models` |
| All enums (`CheckoutPaymentIntent`, etc.) | `PayPalServerSdk.Models.Enums` |
| Typed error classes (`AuthorizeOrderError`, etc.) | `PayPalServerSdk.Errors` |
| `SdkException<T>`, `RawError` | load `dotnet-error-handling` for exact namespace |

Source: `sdk-map.md` (Namespaces section)

---

### 2.2 Client construction & auth

**Client constructor** (source: `PayPalServerSdkClient.cs`, `sdk-map.md`):

```csharp
PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)
```

**DI registration** (source: `ServiceCollectionExtensions.cs`):

```csharp
services.AddPayPalServerSdkClient(o => { /* set credentials/environment on o */ });
```

**`PayPalServerSdkClientOptions` properties used** (source: `PayPalServerSdkClientOptions.cs`):

| Property | Type | Purpose |
|----------|------|---------|
| `Environment` | `ServerEnvironment` (`PayPalServerSdk.Servers`) | Set to `ServerEnvironment.Sandbox` |
| `Oauth2` | `OAuth2ClientCredentials?` | Client ID + secret from env vars |
| `Server` | `ServerOptions` | Base URL override: `options.Server.Default.Sandbox.BaseUrl = "<url>"` — see trap note |

**Environment** (`sdk-map.md` Servers & auth): `ServerEnvironment.Sandbox`

**Credentials**: `OAuth2ClientCredentials` — load `dotnet-authentication` for exact property names and how to build it from `PAYPAL_CLIENT_ID` / `PAYPAL_CLIENT_SECRET`.

---

### 2.3 Step 3 — Authorize payment

#### 2.3a CreateOrder

Controller: `client.Orders` · Source: `map/operations/Orders.md`

```
CreateOrder(
    string? payPalMockResponse,          // null
    string? payPalRequestId,             // REQUIRED: caller-supplied idempotency key (order-scoped)
    string? payPalPartnerAttributionId,  // null
    string? payPalClientMetadataId,      // null
    string? payPalAuthAssertion,         // null
    OrderRequest body,                   // required (not nullable)
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.Order`

Error: `SdkException<AuthorizeOrderError>` — **Case A**
- `TryGetError(out Error)` → [400, 401, 422]
- `TryGetRawError(out RawError)` → fallback

**`OrderRequest` fields** (source: `records-1-Ac-Pa.md`):

| C# name | Wire name | Type | Required? |
|---------|-----------|------|-----------|
| `Intent` | `intent` | `CheckoutPaymentIntent` | yes |
| `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnitRequest>` | yes |
| `PaymentSource` | `payment_source` | `PaymentSource?` | optional (for card; omit for vault-token path if using AuthorizeOrder body) |

**`PurchaseUnitRequest`** (source: `records-1-Ac-Pa.md`):

| C# name | Wire name | Type | Required? |
|---------|-----------|------|-----------|
| `Amount` | `amount` | `AmountWithBreakdown` | yes |

**`AmountWithBreakdown`** (source: `records-1-Ac-Pa.md`):

| C# name | Wire name | Type | Required? |
|---------|-----------|------|-----------|
| `CurrencyCode` | `currency_code` | `string` | yes |
| `Value` | `value` | `string` | yes |

**`PaymentSource`** (source: `records-2-Pa-Ve.md`) — for direct card in CreateOrder body:

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `Card` | `card` | `CardRequest?` | for direct card authorization |
| `Token` | `token` | `Token?` | for vault payment token |

**`CardRequest`** (source: `records-1-Ac-Pa.md`):

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `Number` | `number` | `string?` | card number (PCI SAQ D required) |
| `Expiry` | `expiry` | `string?` | format: YYYY-MM |
| `SecurityCode` | `security_code` | `string?` | CVV |
| `Name` | `name` | `string?` | cardholder name |
| `BillingAddress` | `billing_address` | `Address?` | optional |
| `Attributes` | `attributes` | `CardAttributes?` | optional; contains `Verification.Method` (defaults to `ScaWhenRequired`) |

**`Token`** (source: `records-2-Pa-Ve.md`) — for vault payment token payment source:

| C# name | Wire name | Type | Required? |
|---------|-----------|------|-----------|
| `Id` | `id` | `string` | yes — the `PaymentTokenResponse.Id` value |
| `Type` | `type` | `TokenType` | yes — `TokenType.BillingAgreement` (only available member) |

UNVERIFIED: Whether a v3 Vault payment token ID (`PaymentTokenResponse.Id`) is correctly submitted as `TokenType.BillingAgreement`. The `TokenType` enum exposes only `BillingAgreement (BILLING_AGREEMENT)` — if the live API rejects this for v3 vault tokens, the integration may need a different approach. Defensive coding: check the response `Status` and treat any non-`Completed`/non-`Approved` status as a failure requiring human review.

**Intent enum** (source: `map/models/enums.md`):

`CheckoutPaymentIntent.Authorize` (wire: `"AUTHORIZE"`)

**`Order` response fields** (source: `records-1-Ac-Pa.md`):

| C# name | Wire name | Type | Purpose |
|---------|-----------|------|---------|
| `Id` | `id` | `string?` | PayPal Order ID — pass to `AuthorizeOrder` |
| `Status` | `status` | `OrderStatus?` | check for `PayerActionRequired` — see blocker below |

**BLOCKER CHECK**: If `Order.Status == OrderStatus.PayerActionRequired` after `CreateOrder` or `AuthorizeOrder`, PayPal requires browser-based buyer approval for this card/transaction. Surface this as an actionable error and stop the flow — do NOT proceed to capture. This can happen when SCA/3DS is triggered (the `CardVerification.Method` default is `ScaWhenRequired`).

---

#### 2.3b AuthorizeOrder

Controller: `client.Orders` · Source: `map/operations/Orders.md`

```
AuthorizeOrder(
    string id,                           // PayPal Order ID from CreateOrder
    string? payPalMockResponse,          // null
    string? payPalRequestId,             // REQUIRED: same idempotency key as CreateOrder (makes it idempotent on retry)
    string? payPalClientMetadataId,      // null
    string? payPalAuthAssertion,         // null
    OrderAuthorizeRequest? body,         // payment source (card or vault token) if not already in CreateOrder
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.OrderAuthorizeResponse`

Error: `SdkException<AuthorizeOrderError>` — **Case A**
- `TryGetError(out Error)` → [400, 401, 403, 404, 422, 500]
- `TryGetRawError(out RawError)` → fallback

**`OrderAuthorizeRequest`** (source: `records-1-Ac-Pa.md`):

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `PaymentSource` | `payment_source` | `OrderAuthorizeRequestPaymentSource?` | optional; use if card/token not already embedded in CreateOrder |

**`OrderAuthorizeRequestPaymentSource`** (source: `records-1-Ac-Pa.md`):

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `Card` | `card` | `CardRequest?` | direct card |
| `Token` | `token` | `Token?` | vault payment token |

**`OrderAuthorizeResponse` fields needed** (source: `records-1-Ac-Pa.md`):

| C# path | Wire path | Type | Purpose |
|---------|-----------|------|---------|
| `.Status` | `status` | `OrderStatus?` | check for `PayerActionRequired` |
| `.PurchaseUnits[0].Payments.Authorizations[0].Id` | nested | `string?` | **Authorization ID** — store this for capture/void |
| `.PurchaseUnits[0].Payments.Authorizations[0].Status` | nested | `AuthorizationStatus?` | confirm `Created` |
| `.PurchaseUnits[0].Payments.Authorizations[0].ExpirationTime` | `expiration_time` | `string?` | authorization expiry (ISO-8601) |

Navigation: `OrderAuthorizeResponse.PurchaseUnits` → `IReadOnlyList<PurchaseUnit>?` → `PurchaseUnit.Payments` → `PaymentCollection?` → `PaymentCollection.Authorizations` → `IReadOnlyList<AuthorizationWithAdditionalData>?` → `AuthorizationWithAdditionalData.Id`

**`AuthorizationStatus` enum** (source: `map/models/enums.md`):

`Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)`

**`OrderStatus` enum** (source: `map/models/enums.md`):

`Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`

---

### 2.4 Step 4 — Capture payment

#### 2.4a CaptureAuthorizedPayment

Controller: `client.Payments` · Source: `map/operations/Payments.md`

```
CaptureAuthorizedPayment(
    string authorizationId,              // PayPal authorization ID from AuthorizeOrder response
    string? payPalMockResponse,          // null
    string? payPalRequestId,             // idempotency key for this capture
    string? payPalAuthAssertion,         // null
    CaptureRequest? body,               // pass null for full capture; or set Amount for partial
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.CapturedPayment`

Error: `SdkException<CaptureAuthorizedPaymentError>` — **Case A**
- `TryGetError(out Error)` → [400, 401, 403, 404, 409, 422]
- `TryGetNoContent(out RawError)` → [500]
- `TryGetRawError(out RawError)` → fallback

**`CaptureRequest`** (source: `records-1-Ac-Pa.md`):

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `Amount` | `amount` | `Money?` | omit for full capture; set for partial |
| `FinalCapture` | `final_capture` | `bool? = false` | set true to prevent further captures |

**`CapturedPayment` fields needed** (source: `records-1-Ac-Pa.md`):

| C# name | Wire name | Type | Purpose |
|---------|-----------|------|---------|
| `Id` | `id` | `string?` | **Capture ID** — store for refund |
| `Status` | `status` | `CaptureStatus?` | confirm `Completed` |
| `Amount` | `amount` | `Money?` | captured amount (`CurrencyCode`, `Value`) |
| `SellerReceivableBreakdown.GrossAmount` | `gross_amount` | `Money` (required) | gross captured |
| `SellerReceivableBreakdown.PaypalFee` | `paypal_fee` | `Money?` | PayPal transaction fee |
| `SellerReceivableBreakdown.NetAmount` | `net_amount` | `Money?` | net proceeds to seller |

**`CaptureStatus` enum** (source: `map/models/enums.md`):

`Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)`

---

#### 2.4b ReauthorizePayment (stale authorization)

Controller: `client.Payments` · Source: `map/operations/Payments.md`

```
ReauthorizePayment(
    string authorizationId,              // original authorization ID
    string? payPalRequestId,             // idempotency key for this reauth
    string? payPalAuthAssertion,         // null
    ReauthorizeRequest? body,            // set Amount if reauthorizing for a different amount
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.PaymentAuthorization` (new authorization with a new ID)

Error: `SdkException<ReauthorizePaymentError>` — **Case A**
- `TryGetError(out Error)` → [400, 401, 403, 404, 422]
- `TryGetNoContent(out RawError)` → [500]
- `TryGetRawError(out RawError)` → fallback

**`ReauthorizeRequest`** (source: `records-2-Pa-Ve.md`):

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `Amount` | `amount` | `Money?` | optional; omit to reauth for original amount |

**`PaymentAuthorization` fields needed** (source: `records-2-Pa-Ve.md`):

| C# name | Wire name | Type | Purpose |
|---------|-----------|------|---------|
| `Id` | `id` | `string?` | **new Authorization ID** — use this for subsequent capture |
| `Status` | `status` | `AuthorizationStatus?` | confirm `Created` |
| `ExpirationTime` | `expiration_time` | `string?` | new expiry |

**Logic**: if reauth fails (typed error TryGet returns false for all known statuses, or error status is non-recoverable), surface an actionable error: "Authorization is expired and could not be reauthorized; the order cannot be fulfilled."

**Notes on reauth window** (from operation notes): Reauth is valid from day 4 to day 29 after original authorization. After 30 days a new authorization must be created. The US up-to-115% / max +$75 rule applies.

---

### 2.5 Step 5 — Void authorization

Controller: `client.Payments` · Source: `map/operations/Payments.md`

```
VoidPayment(
    string authorizationId,              // PayPal authorization ID
    string? payPalMockResponse,          // null
    string? payPalAuthAssertion,         // null
    string? payPalRequestId,             // idempotency key (optional but recommended)
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.PaymentAuthorization`

Error: `SdkException<VoidPaymentError>` — **Case A**
- `TryGetError(out Error)` → [401, 403, 404, 409, 422]
- `TryGetNoContent(out RawError)` → [500]
- `TryGetRawError(out RawError)` → fallback

**Response note**: With the default `prefer="return=minimal"`, PayPal returns HTTP 204 No Content on success. The SDK return type is `PaymentAuthorization` — the returned object may be null/empty for 204 responses. Do not rely on the returned object's fields for confirmation; treat a non-exception return as success. To get the full authorization object back, pass `prefer: "return=representation"`.

**409 Conflict** (TryGetError): indicates the authorization has already been voided or captured — treat as success (idempotent void) or surface the specific issue string from `Error.Details[0].Issue`.

---

### 2.6 Step 6 — Refund

Controller: `client.Payments` · Source: `map/operations/Payments.md`

```
RefundCapturedPayment(
    string captureId,                    // CapturedPayment.Id
    string? payPalMockResponse,          // null
    string? payPalRequestId,             // REQUIRED: caller-supplied idempotency key (prevents duplicate refunds)
    string? payPalAuthAssertion,         // null
    RefundRequest? body,                 // null for full refund; set Amount for partial
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.Refund`

Error: `SdkException<RefundCapturedPaymentError>` — **Case A**
- `TryGetError(out Error)` → [400, 401, 403, 404, 409, 422]
- `TryGetNoContent(out RawError)` → [500]
- `TryGetRawError(out RawError)` → fallback

**`RefundRequest`** (source: `records-2-Pa-Ve.md`):

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `Amount` | `amount` | `Money?` | set for partial refund; omit/null for full refund |
| `CustomId` | `custom_id` | `string?` | optional merchant-side reference |
| `NoteToPayer` | `note_to_payer` | `string?` | optional message to payer |

**Over-refund prevention** (application logic — not enforced by SDK):
Before calling `RefundCapturedPayment`, the application must verify:
`requestedRefundAmount + sum(previousRefunds) <= capturedAmount`
Use `SellerPayableBreakdown.TotalRefundedAmount` from prior refund responses or maintain a local ledger.

**`Refund` fields needed** (source: `records-2-Pa-Ve.md`):

| C# name | Wire name | Type | Purpose |
|---------|-----------|------|---------|
| `Id` | `id` | `string?` | **Refund ID** |
| `Status` | `status` | `RefundStatus?` | confirm `Completed` |
| `Amount` | `amount` | `Money?` | refunded amount |
| `SellerPayableBreakdown.TotalRefundedAmount` | `total_refunded_amount` | `Money?` | cumulative total refunded on this capture |

**`RefundStatus` enum** (source: `map/models/enums.md`):

`Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)`

**409 Conflict**: a duplicate refund with the same `payPalRequestId` — treat as idempotent success; extract the existing refund ID from the error response body via `TryGetError(out Error)` → `Error.Details[0].Issue` or log and surface safely.

---

### 2.7 Step 7 — Transaction reconciliation (paginated)

Controller: `client.TransactionSearch` · Source: `map/operations/TransactionSearch.md`

```
SearchTransactions(
    string startDate,                    // ISO-8601 e.g. "2024-01-01T00:00:00-0700"
    string endDate,                      // ISO-8601 e.g. "2024-01-31T23:59:59-0700"
    string? transactionId,               // null
    string? transactionType,             // null
    string? transactionStatus,           // null
    string? transactionAmount,           // null
    string? transactionCurrency,         // null
    string? paymentInstrumentType,       // null
    string? storeId,                     // null
    string? terminalId,                  // null
    string? fields = "transaction_info",
    string? balanceAffectingRecordsOnly = "Y",
    int? pageSize = 100,
    int? page = 1,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.SearchResponse`

Error: `SdkException<RawError>` — **Case B** (this is the one raw-error operation in the SDK)
- `ex.Error.StatusCode` → `HttpStatusCode`
- `ex.Error.ReadAsString()` → raw error body string
- `ex.Error.ReadAsJson<T>()` → deserialize to known error shape if needed

**`SearchResponse` fields** (source: `records-2-Pa-Ve.md`):

| C# name | Wire name | Type | Purpose |
|---------|-----------|------|---------|
| `TransactionDetails` | `transaction_details` | `IReadOnlyList<TransactionDetails>?` | this page's records |
| `TotalPages` | `total_pages` | `int?` | total number of pages |
| `TotalItems` | `total_items` | `int?` | total number of records |
| `Page` | `page` | `int?` | current page number |

**`TransactionDetails`** (source: `records-2-Pa-Ve.md`):

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `TransactionInfo` | `transaction_info` | `TransactionInformation?` | amounts, status, IDs |

**`TransactionInformation` fields** (source: `records-2-Pa-Ve.md`):

| C# name | Wire name | Type | Purpose |
|---------|-----------|------|---------|
| `TransactionId` | `transaction_id` | `string?` | unique transaction ID |
| `TransactionAmount` | `transaction_amount` | `Money?` | transaction amount |
| `FeeAmount` | `fee_amount` | `Money?` | PayPal fee |
| `TransactionStatus` | `transaction_status` | `string?` | status string |
| `TransactionInitiationDate` | `transaction_initiation_date` | `string?` | initiation timestamp |

**Pagination — manual loop** (source: `map/operations/TransactionSearch.md` — no built-in cursor/page):

```
page = 1
do {
    response = SearchTransactions(startDate, endDate, ..., page: page, ...)
    collect response.TransactionDetails
    page++
} while (page <= (response.TotalPages ?? 1))
```

`TransactionSearch.SearchTransactions` has no automatic next-page token. The caller must increment `page` from 1 to `TotalPages`. Both `TotalPages` and `page` may be null — defensive code: treat null `TotalPages` as 1 (single page), treat null `TransactionDetails` as empty list.

**Query wire names** (source: `map/operations/TransactionSearch.md`): `start_date` ← `startDate`, `end_date` ← `endDate`, `page` ← `page`, `page_size` ← `pageSize`.

---

### 2.8 Step 8 — Save card (vault)

Controller: `client.Vault` · Source: `map/operations/Vault.md`

```
CreatePaymentToken(
    string? payPalRequestId,             // idempotency key for this vault request
    PaymentTokenRequest body,            // required (not nullable)
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.PaymentTokenResponse`

Error: `SdkException<CreatePaymentTokenError>` — **Case A**
- `TryGetError1(out Error1)` → [400, 403, 404, 422, 500]
- `TryGetRawError(out RawError)` → fallback

Note: error accessor is `TryGetError1` (not `TryGetError`) — the out type is `Error1` (not `Error`). Both `Error` and `Error1` live in `PayPalServerSdk.Models`. See `map/models/records-1-Ac-Pa.md` for field shapes.

**`PaymentTokenRequest`** (source: `records-2-Pa-Ve.md`):

| C# name | Wire name | Type | Required? |
|---------|-----------|------|-----------|
| `Customer` | `customer` | `Customer?` | set to link token to shopper |
| `PaymentSource` | `payment_source` | `PaymentTokenRequestPaymentSource` | yes |

**`Customer`** (source: `records-1-Ac-Pa.md`):

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `Id` | `id` | `string?` | vault customer ID mapped to shopper identity |

**`PaymentTokenRequestPaymentSource`** (source: `records-2-Pa-Ve.md`):

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `Card` | `card` | `PaymentTokenRequestCard?` | for card vaulting |

**`PaymentTokenRequestCard`** (source: `records-2-Pa-Ve.md`):

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `Name` | `name` | `string?` | cardholder name |
| `Number` | `number` | `string?` | card number |
| `Expiry` | `expiry` | `string?` | expiry in YYYY-MM format |
| `SecurityCode` | `security_code` | `string?` | CVV |
| `BillingAddress` | `billing_address` | `Address?` | optional |

**`PaymentTokenResponse` fields** (source: `records-2-Pa-Ve.md`):

| C# path | Wire name | Type | Purpose |
|---------|-----------|------|---------|
| `.Id` | `id` | `string?` | **Payment Token ID** — store this as the vault identifier; pass as `Token.Id` in future payments |
| `.Customer.Id` | `customer.id` | `string?` | vault customer ID confirming association |
| `.PaymentSource.Card.LastDigits` | `last_digits` | `string?` | last 4 digits of card (safe to display) |
| `.PaymentSource.Card.Brand` | `brand` | `CardBrand?` | card brand enum (e.g. `CardBrand.Visa`) |
| `.PaymentSource.Card.Expiry` | `expiry` | `string?` | card expiry (safe to display) |

Navigation: `.PaymentSource` is `PaymentTokenResponsePaymentSource?`, `.PaymentSource.Card` is `CardPaymentTokenEntity?`.

Full card number is never present in the response — only `LastDigits`, `Brand`, and `Expiry` are safe to surface to the UI.

---

### 2.9 Step 9 — List saved cards

Controller: `client.Vault` · Source: `map/operations/Vault.md`

```
ListCustomerPaymentTokens(
    string customerId,                   // vault customer ID mapped to shopper
    int? pageSize = 5,
    int? page = 1,
    bool? totalRequired = false,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.CustomerVaultPaymentTokensResponse`

Error: `SdkException<ListCustomerPaymentTokensError>` — **Case A**
- `TryGetError1(out Error1)` → [400, 403, 500]
- `TryGetRawError(out RawError)` → fallback

**`CustomerVaultPaymentTokensResponse` fields** (source: `records-1-Ac-Pa.md`):

| C# name | Wire name | Type | Purpose |
|---------|-----------|------|---------|
| `PaymentTokens` | `payment_tokens` | `IReadOnlyList<PaymentTokenResponse>?` | list of saved tokens; each has same safe card fields as Step 8 |
| `TotalItems` | `total_items` | `int?` | total tokens for this customer |
| `TotalPages` | `total_pages` | `int?` | for multi-page listing |

Query wire names: `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired`.

For each `PaymentTokenResponse` in the list, surface: `.Id`, `.PaymentSource.Card.LastDigits`, `.PaymentSource.Card.Brand`, `.PaymentSource.Card.Expiry`.

---

### 2.10 Step 10 — Delete saved card

Controller: `client.Vault` · Source: `map/operations/Vault.md`

```
DeletePaymentToken(
    string id,                           // PaymentTokenResponse.Id (the payment token ID to delete)
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `void` (Task)

Error: `SdkException<DeletePaymentTokenError>` — **Case A**
- `TryGetError1(out Error1)` → [400, 403, 500]
- `TryGetRawError(out RawError)` → fallback

A non-exception return means the token was deleted. No response body is present on success.

---

### 2.11 Enum values used

All enums are in `PayPalServerSdk.Models.Enums`. These are `StringEnum<T>` records — NOT C# enums. Use the static member syntax: `CheckoutPaymentIntent.Authorize`, not `"AUTHORIZE"`.

| Enum | Members used | Wire value |
|------|-------------|------------|
| `CheckoutPaymentIntent` | `Authorize` | `AUTHORIZE` |
| `AuthorizationStatus` | `Created`, `Captured`, `Denied`, `Voided`, `Pending` | `CREATED`, `CAPTURED`, etc. |
| `OrderStatus` | `PayerActionRequired`, `Completed`, `Approved` | `PAYER_ACTION_REQUIRED`, etc. |
| `CaptureStatus` | `Completed`, `Declined`, `Pending`, `Failed` | `COMPLETED`, etc. |
| `RefundStatus` | `Completed`, `Pending`, `Failed`, `Cancelled` | `COMPLETED`, etc. |
| `TokenType` | `BillingAgreement` | `BILLING_AGREEMENT` |
| `CardBrand` | `Visa`, `Mastercard`, `Amex`, `Discover` (for display only) | `VISA`, etc. |

---

### 2.12 Error types quick-reference

| Operation | Error type | Case | Accessors |
|-----------|-----------|------|-----------|
| `CreateOrder` | `CreateOrderError` | A | `TryGetError(out Error)`, `TryGetRawError(out RawError)` |
| `AuthorizeOrder` | `AuthorizeOrderError` | A | `TryGetError(out Error)`, `TryGetRawError(out RawError)` |
| `CaptureAuthorizedPayment` | `CaptureAuthorizedPaymentError` | A | `TryGetError(out Error)`, `TryGetNoContent(out RawError)`, `TryGetRawError(out RawError)` |
| `ReauthorizePayment` | `ReauthorizePaymentError` | A | `TryGetError(out Error)`, `TryGetNoContent(out RawError)`, `TryGetRawError(out RawError)` |
| `VoidPayment` | `VoidPaymentError` | A | `TryGetError(out Error)`, `TryGetNoContent(out RawError)`, `TryGetRawError(out RawError)` |
| `RefundCapturedPayment` | `RefundCapturedPaymentError` | A | `TryGetError(out Error)`, `TryGetNoContent(out RawError)`, `TryGetRawError(out RawError)` |
| `SearchTransactions` | `RawError` | **B** | `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()` |
| `CreatePaymentToken` | `CreatePaymentTokenError` | A | `TryGetError1(out Error1)`, `TryGetRawError(out RawError)` |
| `ListCustomerPaymentTokens` | `ListCustomerPaymentTokensError` | A | `TryGetError1(out Error1)`, `TryGetRawError(out RawError)` |
| `DeletePaymentToken` | `DeletePaymentTokenError` | A | `TryGetError1(out Error1)`, `TryGetRawError(out RawError)` |

`Error` fields: `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`
`Error1` fields: same shape but `Details (details): IReadOnlyList<ErrorDetails1>?`, `Links (links): IReadOnlyList<ErrorLinkDescription>?`

All typed error classes are in `PayPalServerSdk.Errors`. Source: `map/models/records-1-Ac-Pa.md`.

---

## 3. Trap Notes

> Trap notes name the hazard and its consequence; they do NOT resolve it. Load the named companion skill for the answer.

**Step 1 (client registration)** — the `HttpClient` underlying the SDK client must be long-lived and managed via `IHttpClientFactory`, not created per-request. The SDK client wrapper may be transient. Getting this wrong causes socket exhaustion under load. **MUST load `dotnet-client-initialization`** before writing the factory or DI registration.

**Step 2 (authentication)** — `OAuth2ClientCredentials` has specific required property names; setting them wrong or in the wrong order silently sends null credentials and causes 401s. Credentials must be loaded from configuration (env vars), not hardcoded. **MUST load `dotnet-authentication`** before wiring credentials.

**Step 2 (base URL override)** — `PayPal:BaseUrl` must override both API calls AND the OAuth2 token endpoint. Setting only the API base URL and leaving the token URL pointing at production causes 401s in sandbox. The `ServerOptions` property on `PayPalServerSdkClientOptions` is the override point, but what it bounds and how to set it is not obvious from the signature. **MUST load `dotnet-configuration-resilience`** before wiring the base URL override.

**Step 3 (calling AuthorizeOrder with named args)** — `AuthorizeOrder` has 5 nullable, no-default parameters that must be passed explicitly (including as `null`). A positional call with fewer args mis-binds silently. **MUST load `dotnet-calling-endpoints`** before writing any operation call.

**Step 3 (PayerActionRequired status)** — if `OrderAuthorizeResponse.Status == OrderStatus.PayerActionRequired`, the card triggered SCA and PayPal requires browser-based buyer approval. This IS NOT a PayPal API error (no exception is thrown); it is a valid success response with an unusable authorization. The application MUST check the status and surface this as a hard failure to the caller — do not proceed to capture. The sandbox test card (4111...) may or may not trigger this depending on sandbox configuration.

**Step 3 (vault token type)** — UNVERIFIED: using `TokenType.BillingAgreement` with a v3 Vault payment token ID. `TokenType` exposes only `BillingAgreement (BILLING_AGREEMENT)`. If live traffic rejects this for vault tokens, the integration may need an alternative payment source pattern. Code defensively: check `OrderAuthorizeResponse.Status` and surface any non-`Approved`/non-`Completed` status as an actionable error.

**Step 4 (retry semantics)** — the SDK's retry configuration gates `POST` retries on HTTP status codes, but transport failures (`HttpRequestException`) are retried on every verb including `POST`. This means `CaptureAuthorizedPayment` and `ReauthorizePayment` can execute more than once on network failure. The `payPalRequestId` idempotency key MUST be set on these calls to prevent double-capture. **MUST load `dotnet-configuration-resilience`** before configuring retries.

**Step 5 (void with default prefer)** — `VoidPayment` with `prefer="return=minimal"` (the default) returns HTTP 204 No Content on success. The SDK declared return type is `PaymentAuthorization` but the object will be empty/null. Do not read fields from the returned object; treat a non-exception return as success. **MUST load `dotnet-calling-endpoints`** for guidance on 204 handling.

**Step 7 (SearchTransactions is Case B)** — this is the only Case B operation in the SDK. There is no `TryGetError` accessor; the error object is `RawError` directly. An error boundary that only catches `SdkException<SomeTypedError>` will let the `SdkException<RawError>` escape. **MUST load `dotnet-error-handling`** before writing the reconciliation error boundary.

**Step 6 (refund idempotency and 409)** — a 409 on `RefundCapturedPayment` may mean the same idempotency key was already used successfully. Extract the existing refund ID from the error body (`TryGetError(out Error)` → `Error.Details[0].Issue` / `Error.DebugId`) rather than treating 409 as a hard failure. **MUST load `dotnet-error-handling`** for the full Case A / Case B catch ladder shape.

**Error boundary (JsonException leakage — two directions)** — described in REQUIRED READING below. **MUST load `dotnet-error-handling`** before writing the integration error boundary.

---

## 4. REQUIRED READING

Load each skill **before implementation of the indicated step starts**. This plan deliberately does not carry their contents.

| Skill | Governs |
|-------|---------|
| `dotnet-client-initialization` | Step 1 — `HttpClient` lifetime, DI registration, builder/options shape |
| `dotnet-authentication` | Step 2 — `OAuth2ClientCredentials` property names, wiring credentials from config |
| `dotnet-configuration-resilience` | Steps 2, 4 — base URL override (including token endpoint), retry semantics per-attempt vs total, `Timeout` scoping |
| `dotnet-calling-endpoints` | Steps 3–10 — named-argument discipline, 204 handling, required-nullable parameter pattern |
| `dotnet-models` | Steps 3–10 — `StringEnum<T>` construction (`EnumType.Member`, not string literal), enum comparison, nullable field access patterns |
| `dotnet-error-handling` | Steps 3–10 — Case A vs Case B catch ladder shape, `TryGet…` accessor mechanics, `JsonException` boundary hazards |
| `dotnet-testing` | All steps — `HttpClient` test seam, stub patterns for SDK operations |

**JsonException hazard rows — include verbatim in every error boundary review:**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the error boundary. These rows apply to all 10 operations.

---

## 5. Assumptions & Blockers

| # | Type | Detail |
|---|------|--------|
| 1 | Assumption | Currency is read from `PayPal:Currency` env var at service startup and passed in `AmountWithBreakdown.CurrencyCode` for every purchase unit. |
| 2 | Assumption | The vault customer ID is determined by the calling service (e.g. a stable hash or ID of the eShopOnWeb shopper) and passed to the SDK on each vault operation. The SDK does not maintain a customer identity mapping. |
| 3 | Assumption | Over-refund prevention is enforced by application logic before calling `RefundCapturedPayment`. The SDK does not enforce this — PayPal will reject an over-refund at the API level with a 422, but the application must check proactively to return a clear caller-facing error. |
| 4 | Assumption | Idempotency keys (for authorize, capture, refund) are generated by the calling service and are stable per logical operation (e.g. `$"authorize-{orderId}"`, `$"capture-{orderId}"`, `$"refund-{orderId}-{callerRefundKey}"`). |
| 5 | Assumption | `PAYPAL_ENVIRONMENT` env var is used to select `ServerEnvironment.Sandbox` vs production — but the `ServerEnvironment` enum currently exposes only `Sandbox` per the SDK map. A production member may exist in the live SDK version; verify at build time. |
| 6 | Potential blocker | `OrderStatus.PayerActionRequired` — if the sandbox test card (Visa 4111...) consistently triggers SCA for this merchant account, direct card authorization is not possible without a browser round-trip. This can only be confirmed by live sandbox traffic. Mitigation: disable 3DS verification by setting `CardAttributes.Verification.Method = OrdersCardVerificationMethod.AvsCvv` in `CardRequest.Attributes` (UNVERIFIED — whether this suppresses SCA in sandbox). |
| 7 | Unverified | `TokenType.BillingAgreement` is the only `TokenType` member. Whether the live PayPal v3 Vault payment token flow accepts this value when the token is used as a payment source has not been confirmed from the SDK source or map alone. If it is rejected, the vaulted-token payment path will fail at runtime. |
| 8 | Assumption | The application project already targets a compatible .NET framework (netstandard2.0 or later) and can reference the `AsadAli.Checkout.Sdk` NuGet package. |
