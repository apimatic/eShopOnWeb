# PayPal Integration Plan — eShopOnWeb `src/PublicApi`

---

## 1. Scope & Sequence

| Step | eShop endpoint | SDK operations used |
|---|---|---|
| 1 | `POST /api/orders` — place order (awaiting payment) | `client.Orders.CreateOrder` |
| 2 | `POST /api/orders/{orderId}/pay` — authorize (hold, no capture) | `client.Orders.AuthorizeOrder` |
| 3 | `POST /api/orders/{orderId}/fulfil` — capture; renew stale auth if needed | `client.Payments.GetAuthorizedPayment`, `client.Payments.ReauthorizePayment`, `client.Payments.CaptureAuthorizedPayment` |
| 4 | `POST /api/orders/{orderId}/cancel` — void authorization | `client.Payments.VoidPayment` |
| 5 | `POST /api/orders/{orderId}/refunds` — full or partial refund (idempotent) | `client.Payments.RefundCapturedPayment` |
| 6 | `GET /api/my-orders` — caller's orders with payment state | local DB only (no SDK call) |
| 7 | `GET /api/reconciliation?from={}&to={}` — full-range PayPal transactions | `client.TransactionSearch.SearchTransactions` (paginated loop) |
| 8 | `POST /api/payment-methods` — vault a card | `client.Vault.CreatePaymentToken` |
| 9 | `GET /api/payment-methods` — list saved cards | `client.Vault.ListCustomerPaymentTokens` |
| 10 | `DELETE /api/payment-methods/{paymentMethodId}` — remove saved card | `client.Vault.DeletePaymentToken` |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one
> from that type's own map row, never from where a neighbouring type sits. A members table names
> the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒
> `…Core.Configuration`; `Models/Enums/…` ⇒ `PayPalServerSdk.Models.Enums`). Dropping a type to
> the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Namespaces — add ALL of these as `using` directives

| Contents | `using` |
|---|---|
| Client, options, DI extension | `PayPalServerSdk` |
| Controller types (return type of `client.Orders` etc.) | `PayPalServerSdk.Api` |
| Request/response records | `PayPalServerSdk.Models` |
| Enums (`CheckoutPaymentIntent`, `AuthorizationStatus`, etc.) | `PayPalServerSdk.Models.Enums` |
| Typed error classes | `PayPalServerSdk.Errors` |
| Server environment | `PayPalServerSdk.Servers` (for `ServerEnvironment`) |

---

### Step 1 — CreateOrder

**Controller**: `client.Orders` (`PayPalServerSdk.Api`)
**Source page**: `map/operations/Orders.md`

**Signature** (5 must-pass-explicitly nullables first, then required body):
```
CreateOrder(
    string?  payPalMockResponse,        // pass null
    string?  payPalRequestId,           // pass null (or idempotency key for create)
    string?  payPalPartnerAttributionId, // pass null
    string?  payPalClientMetadataId,    // pass null
    string?  payPalAuthAssertion,       // pass null
    OrderRequest body,                  // required, not nullable
    string?  prefer = "return=minimal", // use "return=representation" to get full response
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
) : Task<Order>
```

**Returns**: `Order` (`PayPalServerSdk.Models`)

**Request model** `OrderRequest` (`PayPalServerSdk.Models`):

| C# field (wire name) | Type | Required? |
|---|---|---|
| `Intent (intent)` | `CheckoutPaymentIntent` | **required** — use `CheckoutPaymentIntent.Authorize` |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | **required** |
| `PaymentSource (payment_source)` | `PaymentSource?` | optional |
| `Payer (payer)` | `Payer?` | optional |
| `ApplicationContext (application_context)` | `OrderApplicationContext?` | optional |

**`PurchaseUnitRequest`** (`PayPalServerSdk.Models`):

| C# field (wire name) | Type | Required? |
|---|---|---|
| `Amount (amount)` | `AmountWithBreakdown` | **required** |
| `ReferenceId (reference_id)` | `string?` | optional |
| `CustomId (custom_id)` | `string?` | optional — good place to store eShop order ID |

**`AmountWithBreakdown`** (`PayPalServerSdk.Models`):

| C# field (wire name) | Type | Required? |
|---|---|---|
| `CurrencyCode (currency_code)` | `string` | **required** — from `PayPal:Currency` config |
| `Value (value)` | `string` | **required** — decimal string, e.g. `"99.99"` |
| `Breakdown (breakdown)` | `AmountBreakdown?` | optional |

**Response envelope** (`Order`): read `Order.Id` to get PayPal order ID. Store locally.

**Error**: `SdkException<CreateOrderError>` — Case A
- `ex.Error.TryGetError(out Error error)` → 400, 401, 422 — `error.Name`, `error.Message`, `error.Details`
- `ex.Error.TryGetRawError(out RawError raw)` → fallback — `raw.StatusCode`, `raw.ReadAsString()`

---

### Step 2 — AuthorizeOrder

**Controller**: `client.Orders`
**Source page**: `map/operations/Orders.md`

**Signature** (5 must-pass-explicitly nullable params before body):
```
AuthorizeOrder(
    string  id,                          // PayPal order ID from step 1
    string? payPalMockResponse,          // pass null
    string? payPalRequestId,             // IDEMPOTENCY KEY — derive from eShop orderId, e.g. "auth-{orderId}"
    string? payPalClientMetadataId,      // pass null
    string? payPalAuthAssertion,         // pass null
    OrderAuthorizeRequest? body,         // pass body (not null)
    string? prefer = "return=minimal",   // use "return=representation"
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
) : Task<OrderAuthorizeResponse>
```

**Returns**: `OrderAuthorizeResponse` (`PayPalServerSdk.Models`)

**Request model** `OrderAuthorizeRequest` (`PayPalServerSdk.Models`):

| C# field (wire name) | Type |
|---|---|
| `PaymentSource (payment_source)` | `OrderAuthorizeRequestPaymentSource?` |

**`OrderAuthorizeRequestPaymentSource`** (`PayPalServerSdk.Models`):

| C# field (wire name) | Type | Use case |
|---|---|---|
| `Card (card)` | `CardRequest?` | raw card details OR vaulted card via `VaultId` |
| `Token (token)` | `Token?` | billing agreement token |

**`CardRequest`** (`PayPalServerSdk.Models`) — for raw card:

| C# field (wire name) | Type | Notes |
|---|---|---|
| `Number (number)` | `string?` | raw PAN |
| `Expiry (expiry)` | `string?` | ISO format `"YYYY-MM"` |
| `SecurityCode (security_code)` | `string?` | CVC |
| `Name (name)` | `string?` | cardholder name |
| `BillingAddress (billing_address)` | `Address?` | optional |
| `VaultId (vault_id)` | `string?` | **use this instead of Number/Expiry/SecurityCode when paying with a saved card** — set to `PaymentTokenResponse.Id` from vault |

**To pay with a saved card**: populate only `CardRequest.VaultId = paymentMethodId`; leave `Number`, `Expiry`, `SecurityCode` null.

**Idempotency**: `payPalRequestId` prevents double-authorization on double-click. Derive deterministically from eShop order ID.

**Response envelope** — reading the authorization ID (needed for steps 3, 4):
```
OrderAuthorizeResponse
  .PurchaseUnits[0]          // IReadOnlyList<PurchaseUnit>
    .Payments                // PaymentCollection
      .Authorizations[0]     // IReadOnlyList<AuthorizationWithAdditionalData>
        .Id                  // string? — store as authorizationId
```

`PaymentCollection` (`PayPalServerSdk.Models`) fields:
- `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`
- `Captures (captures): IReadOnlyList<OrdersCapture>?`
- `Refunds (refunds): IReadOnlyList<Refund>?`

`AuthorizationWithAdditionalData` (`PayPalServerSdk.Models`) key fields:
- `Id (id): string?` — authorization ID
- `Status (status): AuthorizationStatus?`
- `ExpirationTime (expiration_time): string?` — ISO-8601

**Error**: `SdkException<AuthorizeOrderError>` — Case A
- `ex.Error.TryGetError(out Error error)` → 400, 401, 403, 404, 422, 500
- `ex.Error.TryGetRawError(out RawError raw)` → fallback

---

### Step 3a — GetAuthorizedPayment (staleness check before capture)

**Controller**: `client.Payments`
**Source page**: `map/operations/Payments.md`

**Signature**:
```
GetAuthorizedPayment(
    string  authorizationId,
    string? payPalMockResponse,     // pass null
    string? payPalAuthAssertion,    // pass null
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
) : Task<PaymentAuthorization>
```

**Returns**: `PaymentAuthorization` (`PayPalServerSdk.Models`)

Key fields:
- `Status (status): AuthorizationStatus?`
- `ExpirationTime (expiration_time): string?` — ISO-8601, parse to `DateTimeOffset` to compare

**Authorization staleness logic** (application layer):
- Honor period: 3 days from creation. Reauth window: day 4–29 from original creation.
- If `ExpirationTime` (in PayPal's response, this is the 3-day honor period expiry) is past AND `CreateTime` is within 29 days → reauthorize (Step 3b).
- If original creation is 30+ days ago → cannot reauthorize; return actionable error to caller — "authorization expired beyond renewal window, order must be restarted."
- If `Status == AuthorizationStatus.Voided` or `AuthorizationStatus.Denied` → return actionable error.

**`AuthorizationStatus`** enum (`PayPalServerSdk.Models.Enums`):
| C# member | wire value |
|---|---|
| `Created` | `CREATED` |
| `Captured` | `CAPTURED` |
| `Denied` | `DENIED` |
| `PartiallyCaptured` | `PARTIALLY_CAPTURED` |
| `Voided` | `VOIDED` |
| `Pending` | `PENDING` |

**Error**: `SdkException<GetAuthorizedPaymentError>` — Case A
- `ex.Error.TryGetError(out Error error)` → 401, 403, 404
- `ex.Error.TryGetNoContent(out RawError raw)` → 500
- `ex.Error.TryGetRawError(out RawError raw)` → fallback

---

### Step 3b — ReauthorizePayment (stale authorization renewal)

**Controller**: `client.Payments`
**Source page**: `map/operations/Payments.md`

**Signature** (3 must-pass-explicitly nullable params):
```
ReauthorizePayment(
    string  authorizationId,
    string? payPalRequestId,        // pass null (or idempotency key)
    string? payPalAuthAssertion,    // pass null
    ReauthorizeRequest? body,       // pass body
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
) : Task<PaymentAuthorization>
```

**Returns**: `PaymentAuthorization` — new `Id` replaces old authorization ID. Store the new `Id` as the current `authorizationId` for capture.

**Request model** `ReauthorizeRequest` (`PayPalServerSdk.Models`):

| C# field (wire name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | optional — if omitted, PayPal uses original amount |

`Money` (`PayPalServerSdk.Models`):
- `CurrencyCode (currency_code): string !req`
- `Value (value): string !req`

**Renewal constraints** (PayPal-enforced, confirmed from map Notes):
- Only valid 4–29 days after original authorization.
- Allowed reauth amount: up to 115% of original (not to exceed +$75 USD in US). The implementer should pass the exact original order total as amount to stay safe.
- If PayPal rejects (422), surface the error as an actionable failure rather than retrying.

**Error**: `SdkException<ReauthorizePaymentError>` — Case A
- `ex.Error.TryGetError(out Error error)` → 400, 401, 403, 404, 422
- `ex.Error.TryGetNoContent(out RawError raw)` → 500
- `ex.Error.TryGetRawError(out RawError raw)` → fallback

---

### Step 3c — CaptureAuthorizedPayment

**Controller**: `client.Payments`
**Source page**: `map/operations/Payments.md`

**Signature** (4 must-pass-explicitly nullable params):
```
CaptureAuthorizedPayment(
    string  authorizationId,
    string? payPalMockResponse,     // pass null
    string? payPalRequestId,        // idempotency key, e.g. "capture-{orderId}"
    string? payPalAuthAssertion,    // pass null
    CaptureRequest? body,           // pass body
    string? prefer = "return=minimal", // use "return=representation"
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
) : Task<CapturedPayment>
```

**Returns**: `CapturedPayment` (`PayPalServerSdk.Models`)

**Request model** `CaptureRequest` (`PayPalServerSdk.Models`):

| C# field (wire name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | optional — if omitted captures full authorized amount |
| `FinalCapture (final_capture)` | `bool? = false` | set `true` to release any remaining hold |
| `InvoiceId (invoice_id)` | `string?` | optional — can store eShop order ID |
| `NoteToPayer (note_to_payer)` | `string?` | optional |

**Reading capture results** from `CapturedPayment`:
- `CapturedPayment.Id` → capture ID (store locally)
- `CapturedPayment.Status` → `CaptureStatus` enum (see below)
- `CapturedPayment.Amount` → `Money` — captured amount
- `CapturedPayment.SellerReceivableBreakdown.PaypalFee` → `Money?` — PayPal fee
- `CapturedPayment.SellerReceivableBreakdown.NetAmount` → `Money?` — net proceeds

`SellerReceivableBreakdown` (`PayPalServerSdk.Models`):
- `GrossAmount (gross_amount): Money !req`
- `PaypalFee (paypal_fee): Money?`
- `NetAmount (net_amount): Money?`

**`CaptureStatus`** enum (`PayPalServerSdk.Models.Enums`):
| C# member | wire value |
|---|---|
| `Completed` | `COMPLETED` |
| `Declined` | `DECLINED` |
| `PartiallyRefunded` | `PARTIALLY_REFUNDED` |
| `Pending` | `PENDING` |
| `Refunded` | `REFUNDED` |
| `Failed` | `FAILED` |

**Error**: `SdkException<CaptureAuthorizedPaymentError>` — Case A
- `ex.Error.TryGetError(out Error error)` → 400, 401, 403, 404, 409, 422
- `ex.Error.TryGetNoContent(out RawError raw)` → 500
- `ex.Error.TryGetRawError(out RawError raw)` → fallback

A 409 from capture typically means the authorization was already captured; check status before retrying.

---

### Step 4 — VoidPayment

**Controller**: `client.Payments`
**Source page**: `map/operations/Payments.md`

**Signature** (3 must-pass-explicitly nullable params — note order differs from other Payments ops):
```
VoidPayment(
    string  authorizationId,
    string? payPalMockResponse,     // pass null
    string? payPalAuthAssertion,    // pass null
    string? payPalRequestId,        // pass null (or idempotency key)
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
) : Task<PaymentAuthorization>
```

**Returns**: `PaymentAuthorization` (status will be `Voided`)

**Error**: `SdkException<VoidPaymentError>` — Case A
- `ex.Error.TryGetError(out Error error)` → 401, 403, 404, 409, 422
- `ex.Error.TryGetNoContent(out RawError raw)` → 500
- `ex.Error.TryGetRawError(out RawError raw)` → fallback

A 409 means the authorization has already been voided or fully captured; treat as an idempotent success or surface the current state.

---

### Step 5 — RefundCapturedPayment

**Controller**: `client.Payments`
**Source page**: `map/operations/Payments.md`

**Signature** (4 must-pass-explicitly nullable params):
```
RefundCapturedPayment(
    string  captureId,
    string? payPalMockResponse,     // pass null
    string? payPalRequestId,        // IDEMPOTENCY KEY — use caller's idempotency key verbatim
    string? payPalAuthAssertion,    // pass null
    RefundRequest? body,            // pass body (null body = full refund; partial = set Amount)
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
) : Task<Refund>
```

**Returns**: `Refund` (`PayPalServerSdk.Models`)

**Request model** `RefundRequest` (`PayPalServerSdk.Models`):

| C# field (wire name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | omit for full refund; set for partial |
| `CustomId (custom_id)` | `string?` | optional — eShop refund reference |
| `InvoiceId (invoice_id)` | `string?` | optional |
| `NoteToPayer (note_to_payer)` | `string?` | optional |

**Idempotency**: `payPalRequestId` is the caller's idempotency key. PayPal rejects a second refund with the same key on the same capture (409). This is how "same key must not refund twice" is enforced at the PayPal layer. The application layer must still guard "total refunded must not exceed captured" by summing stored refund amounts before calling.

**Key fields from `Refund`**:
- `Refund.Id` → refund ID (store locally)
- `Refund.Status` → `RefundStatus` enum

**`RefundStatus`** enum (`PayPalServerSdk.Models.Enums`):
| C# member | wire value |
|---|---|
| `Completed` | `COMPLETED` |
| `Pending` | `PENDING` |
| `Failed` | `FAILED` |
| `Cancelled` | `CANCELLED` |

**Error**: `SdkException<RefundCapturedPaymentError>` — Case A
- `ex.Error.TryGetError(out Error error)` → 400, 401, 403, 404, 409, 422
- `ex.Error.TryGetNoContent(out RawError raw)` → 500
- `ex.Error.TryGetRawError(out RawError raw)` → fallback

A 409 with same idempotency key means PayPal already processed this refund — look up the existing refund ID rather than treating this as an error.

---

### Step 7 — SearchTransactions (reconciliation, paginated)

**Controller**: `client.TransactionSearch`
**Source page**: `map/operations/TransactionSearch.md`

**ERROR CASE IS B — NOT A.** This is the one operation in the SDK that uses `SdkException<RawError>`.

**Signature** (8 must-pass-explicitly nullable filter params):
```
SearchTransactions(
    string  startDate,                              // ISO-8601, e.g. "2024-01-01T00:00:00-0700"
    string  endDate,                                // ISO-8601
    string? transactionId,                          // pass null for reconciliation
    string? transactionType,                        // pass null
    string? transactionStatus,                      // pass null
    string? transactionAmount,                      // pass null
    string? transactionCurrency,                    // pass null (or set to PayPal:Currency)
    string? paymentInstrumentType,                  // pass null
    string? storeId,                                // pass null
    string? terminalId,                             // pass null
    string? fields = "transaction_info",            // use default
    string? balanceAffectingRecordsOnly = "Y",      // use default
    int?    pageSize = 100,                         // use default (max=100)
    int?    page = 1,                               // increment per iteration
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
) : Task<SearchResponse>
```

**Returns**: `SearchResponse` (`PayPalServerSdk.Models`)

**Pagination strategy** — the map says "none (only `page`, no `perPage`)". Full range requires a manual loop:
1. Call with `page: 1` — read `SearchResponse.TotalPages`.
2. Loop `page = 2` through `TotalPages`, accumulating `SearchResponse.TransactionDetails` across all pages.
3. `pageSize = 100` is the API default and maximum — do not exceed it.

**`SearchResponse`** key fields:
- `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`
- `Page (page): int?` — current page
- `TotalItems (total_items): int?`
- `TotalPages (total_pages): int?`

**`TransactionDetails`** (`PayPalServerSdk.Models`):
- `TransactionInfo (transaction_info): TransactionInformation?`

**`TransactionInformation`** (`PayPalServerSdk.Models`) key fields for reconciliation:
- `TransactionId (transaction_id): string?`
- `TransactionAmount (transaction_amount): Money?`
- `FeeAmount (fee_amount): Money?`
- `TransactionStatus (transaction_status): string?` — raw string (not an enum in this SDK)
- `TransactionInitiationDate (transaction_initiation_date): string?`
- `InvoiceId (invoice_id): string?` — correlates to eShop order ID if set at capture
- `PaypalReferenceId (paypal_reference_id): string?` — the original capture/auth ID

**Error**: `SdkException<RawError>` — **Case B** (no typed accessors)
```csharp
catch (SdkException<RawError> ex)
{
    var status = ex.Error.StatusCode;   // HttpStatusCode
    var body   = ex.Error.ReadAsString();
}
```

---

### Step 8 — CreatePaymentToken (vault a card)

**Controller**: `client.Vault`
**Source page**: `map/operations/Vault.md`

**Signature**:
```
CreatePaymentToken(
    string? payPalRequestId,        // idempotency key — e.g. "vault-{userId}-{fingerprint}"
    PaymentTokenRequest body,       // required, not nullable
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
) : Task<PaymentTokenResponse>
```

**Returns**: `PaymentTokenResponse` (`PayPalServerSdk.Models`)

**Request model** `PaymentTokenRequest` (`PayPalServerSdk.Models`):

| C# field (wire name) | Type | Required? |
|---|---|---|
| `Customer (customer)` | `Customer?` | optional — set `Id` to eShop/PayPal customer ID for retrieval |
| `PaymentSource (payment_source)` | `PaymentTokenRequestPaymentSource` | **required** |

**`Customer`** (`PayPalServerSdk.Models`):
- `Id (id): string?` — stable per-customer ID you manage; used for `ListCustomerPaymentTokens`
- `MerchantCustomerId (merchant_customer_id): string?`

**`PaymentTokenRequestPaymentSource`** (`PayPalServerSdk.Models`):
- `Card (card): PaymentTokenRequestCard?`

**`PaymentTokenRequestCard`** (`PayPalServerSdk.Models`):

| C# field (wire name) | Type |
|---|---|
| `Number (number)` | `string?` — full PAN |
| `Expiry (expiry)` | `string?` — `"YYYY-MM"` |
| `SecurityCode (security_code)` | `string?` — CVC |
| `Name (name)` | `string?` — cardholder name |
| `Brand (brand)` | `CardBrand?` — optional |
| `BillingAddress (billing_address)` | `Address?` — optional |

**Response** `PaymentTokenResponse` — safe descriptor to return to caller:
- `Id (id): string?` → **paymentMethodId** — expose this in the API response; store for future use
- `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?`
  - `.Card` → `CardPaymentTokenEntity?` — safe descriptor (no PAN):
    - `LastDigits (last_digits): string?`
    - `Brand (brand): CardBrand?`
    - `Expiry (expiry): string?`
    - `VerificationStatus (verification_status): CardVerificationStatus?`

**NEVER return `Number` or `SecurityCode` — they are not present in the response by design.**

**Error**: `SdkException<CreatePaymentTokenError>` — Case A
- `ex.Error.TryGetError1(out Error1 error)` → 400, 403, 404, 422, 500
  - Note: accessor is `TryGetError1` (not `TryGetError`) — `Error1` type, not `Error`
- `ex.Error.TryGetRawError(out RawError raw)` → fallback

---

### Step 9 — ListCustomerPaymentTokens

**Controller**: `client.Vault`
**Source page**: `map/operations/Vault.md`

**Signature**:
```
ListCustomerPaymentTokens(
    string customerId,              // stable customer ID from your system
    int?   pageSize = 5,
    int?   page = 1,
    bool?  totalRequired = false,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
) : Task<CustomerVaultPaymentTokensResponse>
```

**Returns**: `CustomerVaultPaymentTokensResponse` (`PayPalServerSdk.Models`)

Key fields:
- `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`
- `TotalItems (total_items): int?`
- `TotalPages (total_pages): int?`

The map reports "Pagination: none (only `page`, no `perPage`)". Pagination works the same as `SearchTransactions` — loop if `TotalPages > 1`. For typical use (card management UI), one page with `pageSize = 20` is usually sufficient.

**Error**: `SdkException<ListCustomerPaymentTokensError>` — Case A
- `ex.Error.TryGetError1(out Error1 error)` → 400, 403, 500
- `ex.Error.TryGetRawError(out RawError raw)` → fallback

---

### Step 10 — DeletePaymentToken

**Controller**: `client.Vault`
**Source page**: `map/operations/Vault.md`

**Signature**:
```
DeletePaymentToken(
    string  id,                     // paymentMethodId
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
) : Task
```

**Returns**: `void` (Task)

**Error**: `SdkException<DeletePaymentTokenError>` — Case A
- `ex.Error.TryGetError1(out Error1 error)` → 400, 403, 500
- `ex.Error.TryGetRawError(out RawError raw)` → fallback

---

### Client construction & auth facts

**Source**: `sdk-map.md` (Getting a client, Servers & auth sections)

| Fact | Value |
|---|---|
| NuGet package | `AsadAli.Checkout.Sdk` (install version-less) |
| Client class | `PayPalServerSdkClient` in `PayPalServerSdk` |
| Options class | `PayPalServerSdkClientOptions` in `PayPalServerSdk` |
| DI extension | `services.AddPayPalServerSdkClient(o => { ... })` |
| Auth credentials property | `options.Oauth2` of type `OAuth2ClientCredentials?` |
| Token strategy property | `options.Oauth2TokenStrategy` of type `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` |
| Sandbox environment | `options.Environment = ServerEnvironment.Sandbox` — `ServerEnvironment` is in `PayPalServerSdk.Servers` |
| Production environment | `ServerEnvironment` has **only `Sandbox`** (source-confirmed: `Match` throws on any other value — no production enum member exists in this SDK version) |
| Base URL override | `options.Server.Default.Sandbox.BaseUrl` — set to `"https://api-m.paypal.com"` for production while keeping `Environment = ServerEnvironment.Sandbox` |
| Constructor | `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` |

**Configuration binding** — secrets come from env vars mapped to:
- `PayPal:ClientId` → `OAuth2ClientCredentials.ClientId` (`required string`)
- `PayPal:ClientSecret` → `OAuth2ClientCredentials.ClientSecret` (`required string`)
- `PayPal:Environment` → set `ServerEnvironment.Sandbox` or production base URL
- `PayPal:Currency` → use in every `Money.CurrencyCode` and `AmountWithBreakdown.CurrencyCode`
- `PayPal:BaseUrl` → if present, override all calls via `options.Server`

---

### Enums used — full value tables

**`CheckoutPaymentIntent`** (`PayPalServerSdk.Models.Enums`):
| C# member | wire |
|---|---|
| `Authorize` | `AUTHORIZE` |
| `Capture` | `CAPTURE` |

**`AuthorizationStatus`** (`PayPalServerSdk.Models.Enums`):
| C# member | wire |
|---|---|
| `Created` | `CREATED` |
| `Captured` | `CAPTURED` |
| `Denied` | `DENIED` |
| `PartiallyCaptured` | `PARTIALLY_CAPTURED` |
| `Voided` | `VOIDED` |
| `Pending` | `PENDING` |

**`CaptureStatus`** (`PayPalServerSdk.Models.Enums`):
| C# member | wire |
|---|---|
| `Completed` | `COMPLETED` |
| `Declined` | `DECLINED` |
| `PartiallyRefunded` | `PARTIALLY_REFUNDED` |
| `Pending` | `PENDING` |
| `Refunded` | `REFUNDED` |
| `Failed` | `FAILED` |

**`RefundStatus`** (`PayPalServerSdk.Models.Enums`):
| C# member | wire |
|---|---|
| `Completed` | `COMPLETED` |
| `Pending` | `PENDING` |
| `Failed` | `FAILED` |
| `Cancelled` | `CANCELLED` |

**`OrderStatus`** (`PayPalServerSdk.Models.Enums`):
| C# member | wire |
|---|---|
| `Created` | `CREATED` |
| `Saved` | `SAVED` |
| `Approved` | `APPROVED` |
| `Voided` | `VOIDED` |
| `Completed` | `COMPLETED` |
| `PayerActionRequired` | `PAYER_ACTION_REQUIRED` |

**`PaymentTokenStatus`** (`PayPalServerSdk.Models.Enums`):
| C# member | wire |
|---|---|
| `Created` | `CREATED` |
| `PayerActionRequired` | `PAYER_ACTION_REQUIRED` |
| `Approved` | `APPROVED` |
| `Vaulted` | `VAULTED` |
| `Tokenized` | `TOKENIZED` |

**`TokenType`** (`PayPalServerSdk.Models.Enums`):
| C# member | wire |
|---|---|
| `BillingAgreement` | `BILLING_AGREEMENT` |

(Only one member — `Token.Type` must always be `TokenType.BillingAgreement`.)

---

## 3. Trap Notes

⚠ Step 2 (client registration) — the SDK's retry/timeout options are **not** a whole-call timeout and `POST` operations (including authorize, capture, refund) can be retried by the SDK on transport failures, making non-idempotent writes execute more than once unless idempotency keys are set. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ Step 2 (auth) — `OAuth2ClientCredentials` properties and the exact way to load them from `IConfiguration`/secrets are not on the map; wiring them wrong produces a silent `null` credential and every call fails 401. **MUST load `dotnet-authentication`** before setting credentials.

⚠ Step 2 (client DI) — `IHttpClientFactory` lifetime and the `AddPayPalServerSdkClient` DI pattern have requirements the client constructor signature does not show. **MUST load `dotnet-client-initialization`** before registering the client in ASP.NET Core DI.

⚠ Steps 2, 3, 4, 5, 7 (calling endpoints) — many operations have 4–5 nullable parameters with no default that must be passed explicitly (positional call will mis-bind); the `prefer` default is `"return=minimal"` which omits fields (including `PurchaseUnits.Payments`) needed to extract authorization/capture IDs — use `"return=representation"` wherever the response must be read. **MUST load `dotnet-calling-endpoints`** before writing any operation call.

⚠ Steps 3a, 5 (staleness / over-refund guards) — the guard logic ("total refunded ≤ captured", "reauth window 4–29 days") lives in the application layer; the SDK will return 422 if violated, but guarding before the call avoids wasting a round trip and makes the error message actionable. The `ExpirationTime` in `PaymentAuthorization` is the 3-day honor period expiry, NOT the 29-day absolute expiry — compute the original creation date from `CreateTime` for the 29-day check.

⚠ Step 7 (SearchTransactions — Case B error) — this is the **only Case B operation** in this SDK. Do not catch `SdkException<SearchTransactionsError>` (that type does not exist); catch `SdkException<RawError>` and read `ex.Error.StatusCode` + `ex.Error.ReadAsString()`. A Case-A-only catch ladder will let this error escape the boundary. **MUST load `dotnet-error-handling`**.

⚠ Steps 8, 9, 10 (Vault error accessors) — Vault operations throw `SdkException<CreatePaymentTokenError>` / `SdkException<ListCustomerPaymentTokensError>` / `SdkException<DeletePaymentTokenError>` with accessor **`TryGetError1(out Error1 error)`** — not `TryGetError(out Error)`. The payload type is `Error1` (namespace `PayPalServerSdk.Models`), not `Error`. Mixing these up compiles but always returns false, silently dropping error details.

⚠ Steps 8, 9 (models) — enums (`CardBrand`, `CaptureStatus`, etc.) are `StringEnum<T>` records, NOT C# enums. Compare with `== CardBrand.Visa`, not `switch`/`case`. Build with the static member directly: `CheckoutPaymentIntent.Authorize`. Do NOT use `new CheckoutPaymentIntent(...)`. **MUST load `dotnet-models`**.

⚠ All steps (testing) — the test seam is the `HttpClient` constructor parameter; the SDK has no built-in mock interface. **MUST load `dotnet-testing`** before writing any SDK-touching test.

---

## 4. REQUIRED READING

Load every skill in this list **before implementation starts**. The contract sheet above deliberately does not carry their contents — each skill covers defaults, worked examples, and wiring details that a one-line note cannot replace.

| Skill | Step(s) governed |
|---|---|
| `dotnet-client-initialization` | Step 2 — client construction, `IHttpClientFactory`, DI registration with `AddPayPalServerSdkClient` |
| `dotnet-authentication` | Step 2 — `OAuth2ClientCredentials` wiring, credential loading from `IConfiguration`, token refresh |
| `dotnet-calling-endpoints` | Steps 1–5, 7–10 — named-argument discipline for nullable params, `prefer` header, async usage, response unwrapping |
| `dotnet-models` | Steps 1–5, 7–10 — `StringEnum<T>` construction and comparison, `required` init-only record fields, nullable handling |
| `dotnet-error-handling` | All steps — Case A vs Case B boundaries, `TryGet…` accessor mechanics, `JsonException` escape paths (two directions — see below) |
| `dotnet-configuration-resilience` | Step 2 — `Timeout` per-attempt semantics, `HttpMethodsToRetry`, POST retry danger, base URL override for production |
| `dotnet-testing` | All steps — `HttpClient` seam, SDK test patterns, matching existing project test framework |

**`JsonException` at the error boundary — two directions requiring opposite handling (MUST load `dotnet-error-handling` before writing the boundary):**

- A drifted or malformed **2xx** body (e.g., a required field absent in a sandbox vs. production response) surfaces as `JsonException` from deserialization, **not** as `SdkException` — an SDK-exception-only catch ladder lets it escape the integration boundary silently.
- A **non-2xx** body that does not match the operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so the `JsonException` replaces the `SdkException` and the HTTP status is destroyed — a boundary that maps every `JsonException` to 5xx then reports a deterministic 422 rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## 5. Assumptions & Blockers

| # | Item | Impact |
|---|---|---|
| 1 | **Production environment**: `ServerEnvironment` has **only `Sandbox`** (source-confirmed — `Match()` throws `ArgumentOutOfRangeException` on any unknown value; no production member exists). For production, keep `Environment = ServerEnvironment.Sandbox` and override the URL: `options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"`. If `PayPal:Environment != "sandbox"`, apply this override. If `PayPal:BaseUrl` is set, use that value instead. | Step 2 — environment wiring for production |
| 2 | **Customer ID for vault**: `ListCustomerPaymentTokens` requires a `customerId` string. The integration must decide how to generate and persist this ID (e.g., a stable hash of the eShop user identity, or PayPal's own customer ID from a prior `CreatePaymentToken` response `PaymentTokenResponse.Customer.Id`). This mapping is not in the SDK — it is an application design decision. | Steps 8, 9 |
| 3 | **Card vault sandbox account enablement**: the brief states the sandbox business account is enabled for direct card processing and vaulting. If `CreatePaymentToken` returns 403 in practice, the sandbox account needs the capability enabled in the PayPal Developer Dashboard. UNVERIFIED (confirmed only by brief statement, not by SDK source). | Step 8 |
| 4 | **Refund idempotency key storage**: the brief says "same key must not refund twice." The application must persist the caller's idempotency key alongside each refund record. If the same key arrives again and a 409 is returned by PayPal, the application must look up the existing refund by key and return it — not treat the 409 as an error. This is application-layer state design, not an SDK question. | Step 5 |
| 5 | **`ExpirationTime` semantics for staleness**: `PaymentAuthorization.ExpirationTime` reflects the 3-day honor period expiry. To compute the 29-day absolute window, use `PaymentAuthorization.CreateTime` (not `ExpirationTime`). If `CreateTime` is absent from the live response (it is nullable), the fallback is to attempt reauth and handle a 422 as "renewal impossible." UNVERIFIED whether `CreateTime` is always populated in live responses. | Step 3a/3b |
