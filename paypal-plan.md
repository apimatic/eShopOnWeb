# PayPal Integration Plan — eShopOnWeb `src/PublicApi`

---

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it.
> Enums, unions, auth, server and client-config types are spread across different child
> namespaces. Dropping a type to the root or to `.Models` makes the implementer guess
> the wrong `using`, and the build breaks.

---

## 1. Scope & Sequence

| # | Step | SDK operations |
|---|---|---|
| 1 | Install NuGet package, register SDK client in DI, wire OAuth2 credentials, configure environment (Sandbox) and optional base-URL override | — |
| 2 | **Authorize** (`POST /api/orders/{orderId}/pay`) — create PayPal order (AUTHORIZE intent) then authorize it; support inline card and vault token; idempotent via `payPalRequestId` | `client.Orders.CreateOrder`, `client.Orders.AuthorizeOrder` |
| 3 | **Capture** (`POST /api/orders/{orderId}/fulfil`) — attempt capture; detect stale auth, try reauthorize; store captured amount + fee + net; idempotent | `client.Payments.GetAuthorizedPayment`, `client.Payments.ReauthorizePayment`, `client.Payments.CaptureAuthorizedPayment` |
| 4 | **Void** (`POST /api/orders/{orderId}/cancel`) — void the authorization | `client.Payments.VoidPayment` |
| 5 | **Refund** (`POST /api/orders/{orderId}/refunds`) — partial or full refund with caller-supplied idempotency key; enforce no over-refund | `client.Payments.RefundCapturedPayment` |
| 6 | **Reconciliation** (`GET /api/reconciliation`) — fetch ALL pages of transaction search for a date range | `client.TransactionSearch.SearchTransactions` (loop all pages) |
| 7 | **Vault: save card** (`POST /api/payment-methods`) — vault card directly; return payment token ID | `client.Vault.CreatePaymentToken` |
| 8 | **Vault: list cards** (`GET /api/payment-methods`) — list shopper's payment tokens; safe descriptors only | `client.Vault.ListCustomerPaymentTokens` |
| 9 | **Vault: delete card** (`DELETE /api/payment-methods/{paymentMethodId}`) — delete payment token | `client.Vault.DeletePaymentToken` |
| 10 | Write error boundary; validate against all Case A/B error types used | — |

---

## 2. CONTRACT SHEET

### Namespaces

```csharp
using PayPalServerSdk;
using PayPalServerSdk.Api;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Servers;        // ServerEnvironment
```

Source: `sdk-map.md` — Namespaces by content type table.

---

### Client construction

```csharp
// Constructor:
PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)
```

**`PayPalServerSdkClientOptions` properties** (source: `sdk-map.md`):

| Property | Type | Notes |
|---|---|---|
| `Environment` | `ServerEnvironment` | `ServerEnvironment.Sandbox` for sandbox |
| `Oauth2` | `OAuth2ClientCredentials?` | Set client ID + secret |
| `Oauth2TokenStrategy` | `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | Optional custom token strategy |
| `Server` | `ServerOptions` (namespace `PayPalServerSdk`, source: `ServerOptions.cs`) | Set `options.Server.Default.Sandbox.BaseUrl = "<verbatim url>"` — `Default` is `DefaultOptions` (`Servers/DefaultOptions.cs`), `Sandbox` is `SandboxOptions` with `BaseUrl: string` |
| `Retry` | `RetryOptions` | Build from `RetryOptions.Default()` or set all required members |
| `Logging` | `LoggingOptions` | Optional |

DI extension: `services.AddPayPalServerSdkClient(o => { ... })` (source: `ServiceCollectionExtensions.cs`)

---

### Step 2 — Authorize order

#### `client.Orders.CreateOrder` (source: `operations/Orders.md`)

```
CreateOrder(
    string? payPalMockResponse,           // must pass explicitly — null to skip
    string? payPalRequestId,              // IDEMPOTENCY KEY — must pass explicitly
    string? payPalPartnerAttributionId,   // must pass explicitly — null to skip
    string? payPalClientMetadataId,       // must pass explicitly — null to skip
    string? payPalAuthAssertion,          // must pass explicitly — null to skip
    OrderRequest body,                    // required (non-nullable)
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `Order` — namespace `PayPalServerSdk.Models`
Error: **Case A** — `SdkException<CreateOrderError>` (namespace `PayPalServerSdk.Errors`)
- `TryGetError(out Error error)` — statuses 400, 401, 422
- `TryGetRawError(out RawError error)` — fallback

**`OrderRequest` fields** (source: `records-1-Ac-Pa.md`):

| C# name (wire name) | Type | Req? |
|---|---|---|
| `Intent (intent)` | `CheckoutPaymentIntent` | **required** — `CheckoutPaymentIntent.Authorize` |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | **required** |
| `PaymentSource (payment_source)` | `PaymentSource?` | optional — pass `null` at CreateOrder; supply at AuthorizeOrder instead |
| `Payer (payer)` | `Payer?` | optional |
| `ApplicationContext (application_context)` | `OrderApplicationContext?` | optional |

**`PurchaseUnitRequest` fields** (source: `records-2-Pa-Ve.md`):

| C# name (wire name) | Type | Req? |
|---|---|---|
| `Amount (amount)` | `AmountWithBreakdown` | **required** |
| `ReferenceId (reference_id)` | `string?` | optional — store eShop order ID here for correlation |
| `CustomId (custom_id)` | `string?` | optional |
| `InvoiceId (invoice_id)` | `string?` | optional |

**`AmountWithBreakdown` fields** (source: `records-1-Ac-Pa.md`):

| C# name (wire name) | Type | Req? |
|---|---|---|
| `CurrencyCode (currency_code)` | `string` | **required** — from `PayPal:Currency` config |
| `Value (value)` | `string` | **required** — decimal as string, e.g. `"12.50"` |
| `Breakdown (breakdown)` | `AmountBreakdown?` | optional |

**`CheckoutPaymentIntent` enum** (namespace `PayPalServerSdk.Models.Enums`, source: `enums.md`):

| Member | Wire value |
|---|---|
| `CheckoutPaymentIntent.Capture` | `CAPTURE` |
| `CheckoutPaymentIntent.Authorize` | `AUTHORIZE` — **use this** |

**`Order` response** (source: `records-1-Ac-Pa.md`):

| C# name (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | PayPal Order ID — store in DB |
| `Status (status)` | `OrderStatus?` | |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnit>?` | |

---

#### `client.Orders.AuthorizeOrder` (source: `operations/Orders.md`)

```
AuthorizeOrder(
    string id,                          // PayPal Order ID from CreateOrder
    string? payPalMockResponse,         // must pass explicitly — null to skip
    string? payPalRequestId,            // IDEMPOTENCY KEY — must pass explicitly
    string? payPalClientMetadataId,     // must pass explicitly — null to skip
    string? payPalAuthAssertion,        // must pass explicitly — null to skip
    OrderAuthorizeRequest? body,        // must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `OrderAuthorizeResponse` — namespace `PayPalServerSdk.Models`
Error: **Case A** — `SdkException<AuthorizeOrderError>`
- `TryGetError(out Error error)` — statuses 400, 401, 403, 404, 422, 500
- `TryGetRawError(out RawError error)` — fallback

**`OrderAuthorizeResponse` fields** (source: `records-1-Ac-Pa.md`):

| C# name (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | PayPal Order ID |
| `Status (status)` | `OrderStatus?` | Check for `PayerActionRequired` — see trap note |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnit>?` | Authorization ID buried here |

**Authorization ID extraction path:**
`response.PurchaseUnits[0].Payments.Authorizations[0].Id`
- `PurchaseUnit.Payments` is `PaymentCollection?`
- `PaymentCollection.Authorizations` is `IReadOnlyList<AuthorizationWithAdditionalData>?`
- `AuthorizationWithAdditionalData.Id` is `string?`
Store this authorization ID in the DB.

**`OrderAuthorizeRequest` fields** (source: `records-1-Ac-Pa.md`):

| C# name (wire name) | Type |
|---|---|
| `PaymentSource (payment_source)` | `OrderAuthorizeRequestPaymentSource?` |

**`OrderAuthorizeRequestPaymentSource` fields** (source: `records-1-Ac-Pa.md`):

| C# name (wire name) | Type | When to use |
|---|---|---|
| `Card (card)` | `CardRequest?` | Inline card OR vault token via `VaultId` |
| `Token (token)` | `Token?` | Billing agreement tokens only — NOT for card vault tokens |

**`CardRequest` fields** (source: `records-1-Ac-Pa.md`):

| C# name (wire name) | Type | Notes |
|---|---|---|
| `Name (name)` | `string?` | Cardholder name |
| `Number (number)` | `string?` | Card number (inline) |
| `Expiry (expiry)` | `string?` | Format: `"YYYY-MM"` |
| `SecurityCode (security_code)` | `string?` | CVV |
| `BillingAddress (billing_address)` | `Address?` | |
| `VaultId (vault_id)` | `string?` | **Use this for vault token payment** — set to the `PaymentTokenResponse.Id` |
| `Attributes (attributes)` | `CardAttributes?` | optional |

**For inline card:** populate `Number`, `Expiry`, `SecurityCode`, `Name`, `BillingAddress` in `CardRequest`.
**For vault token:** populate only `VaultId` in `CardRequest` (set all other card fields to null).

**`Address` fields** (source: `records-1-Ac-Pa.md`):

| C# name (wire name) | Type | Req? |
|---|---|---|
| `CountryCode (country_code)` | `string` | **required** |
| `AddressLine1 (address_line_1)` | `string?` | optional |
| `AddressLine2 (address_line_2)` | `string?` | optional |
| `AdminArea2 (admin_area_2)` | `string?` | City |
| `AdminArea1 (admin_area_1)` | `string?` | State/province |
| `PostalCode (postal_code)` | `string?` | optional |

**`OrderStatus` enum** (namespace `PayPalServerSdk.Models.Enums`, source: `enums.md`):

| Member | Wire value | Notes |
|---|---|---|
| `OrderStatus.Created` | `CREATED` | |
| `OrderStatus.Approved` | `APPROVED` | |
| `OrderStatus.Completed` | `COMPLETED` | |
| `OrderStatus.PayerActionRequired` | `PAYER_ACTION_REQUIRED` | **3DS required — FAIL with clear error; do not redirect** |
| `OrderStatus.Voided` | `VOIDED` | |
| `OrderStatus.Saved` | `SAVED` | |

---

### Step 3 — Capture (and handle stale authorization)

#### `client.Payments.GetAuthorizedPayment` (source: `operations/Payments.md`)

```
GetAuthorizedPayment(
    string authorizationId,
    string? payPalMockResponse,     // must pass explicitly
    string? payPalAuthAssertion,    // must pass explicitly
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PaymentAuthorization` — namespace `PayPalServerSdk.Models`
Error: **Case A** — `SdkException<GetAuthorizedPaymentError>`
- `TryGetError(out Error error)` — statuses 401, 403, 404
- `TryGetNoContent(out RawError error)` — status 500
- `TryGetRawError(out RawError error)` — fallback

**`PaymentAuthorization` fields** (source: `records-2-Pa-Ve.md`):

| C# name (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | Authorization ID |
| `Status (status)` | `AuthorizationStatus?` | Check before capture |
| `ExpirationTime (expiration_time)` | `string?` | ISO-8601 datetime string |
| `Amount (amount)` | `Money?` | |
| `StatusDetails (status_details)` | `AuthorizationStatusDetails?` | |

**`AuthorizationStatus` enum** (source: `enums.md`):

| Member | Wire value | Notes |
|---|---|---|
| `AuthorizationStatus.Created` | `CREATED` | Within honor period — capture directly |
| `AuthorizationStatus.Captured` | `CAPTURED` | Already captured |
| `AuthorizationStatus.Voided` | `VOIDED` | Already voided |
| `AuthorizationStatus.Denied` | `DENIED` | Cannot capture |
| `AuthorizationStatus.Pending` | `PENDING` | |
| `AuthorizationStatus.PartiallyCaptured` | `PARTIALLY_CAPTURED` | |

**Stale auth logic:** If `Status != Created` or `ExpirationTime` is in the past → attempt reauthorize. If beyond 29 days from original creation (PayPal rule: reauthorize only days 4-29 after 3-day honor period) → fail with "renewal impossible, create new order".

---

#### `client.Payments.ReauthorizePayment` (source: `operations/Payments.md`)

```
ReauthorizePayment(
    string authorizationId,
    string? payPalRequestId,        // must pass explicitly — idempotency key
    string? payPalAuthAssertion,    // must pass explicitly
    ReauthorizeRequest? body,       // must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PaymentAuthorization`
Error: **Case A** — `SdkException<ReauthorizePaymentError>`
- `TryGetError(out Error error)` — statuses 400, 401, 403, 404, 422
- `TryGetNoContent(out RawError error)` — status 500
- `TryGetRawError(out RawError error)` — fallback

**`ReauthorizeRequest` fields** (source: `records-2-Pa-Ve.md`):

| C# name (wire name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | Optional — if null, reauthorizes for same amount |

**Note:** Reauthorize is only valid 4-29 days after the 3-day honor period. After 29 days from original authorization date, PayPal rejects reauthorization — catch 422 from `TryGetError` and report "authorization expired, cannot renew".

---

#### `client.Payments.CaptureAuthorizedPayment` (source: `operations/Payments.md`)

```
CaptureAuthorizedPayment(
    string authorizationId,
    string? payPalMockResponse,     // must pass explicitly
    string? payPalRequestId,        // IDEMPOTENCY KEY — must pass explicitly
    string? payPalAuthAssertion,    // must pass explicitly
    CaptureRequest? body,           // must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `CapturedPayment` — namespace `PayPalServerSdk.Models`
Error: **Case A** — `SdkException<CaptureAuthorizedPaymentError>`
- `TryGetError(out Error error)` — statuses 400, 401, 403, 404, **409** (conflict/already captured), 422
- `TryGetNoContent(out RawError error)` — status 500
- `TryGetRawError(out RawError error)` — fallback

**`CaptureRequest` fields** (source: `records-1-Ac-Pa.md`):

| C# name (wire name) | Type | Default |
|---|---|---|
| `Amount (amount)` | `Money?` | optional — if null, captures full authorization |
| `InvoiceId (invoice_id)` | `string?` | optional |
| `FinalCapture (final_capture)` | `bool?` | `= false` |
| `NoteToPayer (note_to_payer)` | `string?` | optional |
| `SoftDescriptor (soft_descriptor)` | `string?` | optional |

**`CapturedPayment` fields** (source: `records-1-Ac-Pa.md`):

| C# name (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | Capture ID — store in DB |
| `Status (status)` | `CaptureStatus?` | |
| `Amount (amount)` | `Money?` | Captured amount (gross) |
| `SellerReceivableBreakdown (seller_receivable_breakdown)` | `SellerReceivableBreakdown?` | Fee + net breakdown |

**`SellerReceivableBreakdown` fields** (source: `records-2-Pa-Ve.md`):

| C# name (wire name) | Type | Notes |
|---|---|---|
| `GrossAmount (gross_amount)` | `Money` | **required** — captured amount |
| `PaypalFee (paypal_fee)` | `Money?` | PayPal fee — store in DB |
| `NetAmount (net_amount)` | `Money?` | Net proceeds — store in DB |

**`Money` fields** (source: `records-1-Ac-Pa.md`):

| C# name (wire name) | Type | Req? |
|---|---|---|
| `CurrencyCode (currency_code)` | `string` | **required** |
| `Value (value)` | `string` | **required** — decimal as string |

**`CaptureStatus` enum** (source: `enums.md`):

| Member | Wire value |
|---|---|
| `CaptureStatus.Completed` | `COMPLETED` |
| `CaptureStatus.Declined` | `DECLINED` |
| `CaptureStatus.Pending` | `PENDING` |
| `CaptureStatus.Refunded` | `REFUNDED` |
| `CaptureStatus.PartiallyRefunded` | `PARTIALLY_REFUNDED` |
| `CaptureStatus.Failed` | `FAILED` |

---

### Step 4 — Void authorization

#### `client.Payments.VoidPayment` (source: `operations/Payments.md`)

```
VoidPayment(
    string authorizationId,
    string? payPalMockResponse,     // must pass explicitly
    string? payPalAuthAssertion,    // must pass explicitly
    string? payPalRequestId,        // must pass explicitly — idempotency key
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PaymentAuthorization`
Error: **Case A** — `SdkException<VoidPaymentError>`
- `TryGetError(out Error error)` — statuses 401, 403, 404, **409** (already captured/voided), 422
- `TryGetNoContent(out RawError error)` — status 500
- `TryGetRawError(out RawError error)` — fallback

**Note:** A 409 from `TryGetError` means the authorization was already captured or already voided — report appropriate error and do not retry.

---

### Step 5 — Refund

#### `client.Payments.RefundCapturedPayment` (source: `operations/Payments.md`)

```
RefundCapturedPayment(
    string captureId,
    string? payPalMockResponse,     // must pass explicitly
    string? payPalRequestId,        // IDEMPOTENCY KEY (caller-supplied) — must pass explicitly
    string? payPalAuthAssertion,    // must pass explicitly
    RefundRequest? body,            // must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `Refund` — namespace `PayPalServerSdk.Models`
Error: **Case A** — `SdkException<RefundCapturedPaymentError>`
- `TryGetError(out Error error)` — statuses 400, 401, 403, 404, **409** (duplicate / over-refund), 422
- `TryGetNoContent(out RawError error)` — status 500
- `TryGetRawError(out RawError error)` — fallback

**`RefundRequest` fields** (source: `records-2-Pa-Ve.md`):

| C# name (wire name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | null = full refund; set for partial refund |
| `CustomId (custom_id)` | `string?` | optional |
| `InvoiceId (invoice_id)` | `string?` | optional |
| `NoteToPayer (note_to_payer)` | `string?` | optional |

**`Refund` fields** (source: `records-2-Pa-Ve.md`):

| C# name (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | Refund ID — store in DB |
| `Status (status)` | `RefundStatus?` | |
| `Amount (amount)` | `Money?` | Amount refunded |
| `SellerPayableBreakdown (seller_payable_breakdown)` | `SellerPayableBreakdown?` | Includes `TotalRefundedAmount` |

**`SellerPayableBreakdown` fields** (source: `records-2-Pa-Ve.md`) — needed for over-refund guard:

| C# name (wire name) | Type | Notes |
|---|---|---|
| `GrossAmount (gross_amount)` | `Money?` | Refund gross |
| `PaypalFee (paypal_fee)` | `Money?` | |
| `NetAmount (net_amount)` | `Money?` | |
| `TotalRefundedAmount (total_refunded_amount)` | `Money?` | Cumulative refunds on capture (UNVERIFIED: whether this reflects ALL prior refunds or just this one — use our own DB tally as the authoritative guard; treat this field as best-effort confirmation only) |

**Over-refund guard:** Before calling `RefundCapturedPayment`, compute the sum of all prior refund amounts stored in our DB. If `requestedAmount + priorRefunds > capturedAmount`, reject with a clear error before making the API call. Do not rely solely on a 409 from PayPal; enforce this locally.

**`RefundStatus` enum** (source: `enums.md`):

| Member | Wire value |
|---|---|
| `RefundStatus.Completed` | `COMPLETED` |
| `RefundStatus.Pending` | `PENDING` |
| `RefundStatus.Failed` | `FAILED` |
| `RefundStatus.Cancelled` | `CANCELLED` |

---

### Step 6 — Reconciliation (all pages)

#### `client.TransactionSearch.SearchTransactions` (source: `operations/TransactionSearch.md`)

```
SearchTransactions(
    string startDate,                        // required — ISO-8601 datetime e.g. "2024-01-01T00:00:00-0700"
    string endDate,                          // required — ISO-8601 datetime
    string? transactionId,                   // must pass explicitly — null to skip
    string? transactionType,                 // must pass explicitly — null to skip
    string? transactionStatus,               // must pass explicitly — null to skip
    string? transactionAmount,               // must pass explicitly — null to skip
    string? transactionCurrency,             // must pass explicitly — null to skip
    string? paymentInstrumentType,           // must pass explicitly — null to skip
    string? storeId,                         // must pass explicitly — null to skip
    string? terminalId,                      // must pass explicitly — null to skip
    string? fields = "transaction_info",
    string? balanceAffectingRecordsOnly = "Y",
    int? pageSize = 100,
    int? page = 1,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `SearchResponse` — namespace `PayPalServerSdk.Models`
Error: **Case B** — `SdkException<RawError>` (NOT typed)
- `ex.Error.StatusCode: HttpStatusCode`
- `ex.Error.ReadAsString(): string`
- `ex.Error.ReadAsJson<T>(): T?`

**`SearchResponse` fields** (source: `records-2-Pa-Ve.md`):

| C# name (wire name) | Type | Notes |
|---|---|---|
| `TransactionDetails (transaction_details)` | `IReadOnlyList<TransactionDetails>?` | List of transactions |
| `Page (page)` | `int?` | Current page number |
| `TotalPages (total_pages)` | `int?` | Total pages — loop until page == total_pages |
| `TotalItems (total_items)` | `int?` | Total record count |

**Pagination:** `SearchTransactions` has no built-in pagination helper. Loop manually:
```
page = 1
do:
    resp = SearchTransactions(startDate, endDate, ..., page: page)
    collect resp.TransactionDetails
    page++
while page <= resp.TotalPages
```

**`TransactionDetails` → `TransactionInformation` fields** (source: `records-2-Pa-Ve.md`):

| C# name (wire name) | Type | Notes |
|---|---|---|
| `TransactionId (transaction_id)` | `string?` | Match against our DB PayPal IDs |
| `TransactionAmount (transaction_amount)` | `Money?` | Gross transaction amount |
| `FeeAmount (fee_amount)` | `Money?` | PayPal fee |
| `TransactionStatus (transaction_status)` | `string?` | Raw string status |
| `PaypalReferenceId (paypal_reference_id)` | `string?` | Correlated reference |
| `TransactionInitiationDate (transaction_initiation_date)` | `string?` | |

Access: `transactionDetails.TransactionInfo.TransactionId` etc.

---

### Step 7 — Vault card

#### `client.Vault.CreatePaymentToken` (source: `operations/Vault.md`)

```
CreatePaymentToken(
    string? payPalRequestId,                // IDEMPOTENCY KEY — must pass explicitly
    PaymentTokenRequest body,               // required (non-nullable)
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PaymentTokenResponse` — namespace `PayPalServerSdk.Models`
Error: **Case A** — `SdkException<CreatePaymentTokenError>`
- `TryGetError1(out Error1 error)` — statuses 400, 403, 404, 422, 500
- `TryGetRawError(out RawError error)` — fallback

**`PaymentTokenRequest` fields** (source: `records-2-Pa-Ve.md`):

| C# name (wire name) | Type | Req? |
|---|---|---|
| `PaymentSource (payment_source)` | `PaymentTokenRequestPaymentSource` | **required** |
| `Customer (customer)` | `Customer?` | optional — pass eShop customer ID as `Customer.Id` to associate vault token with customer |

**`PaymentTokenRequestPaymentSource` fields** (source: `records-2-Pa-Ve.md`):

| C# name (wire name) | Type | Notes |
|---|---|---|
| `Card (card)` | `PaymentTokenRequestCard?` | Use for direct card vaulting |
| `Token (token)` | `VaultTokenRequest?` | Use for setup-token-to-payment-token conversion |

**`PaymentTokenRequestCard` fields** (source: `records-2-Pa-Ve.md`):

| C# name (wire name) | Type | Notes |
|---|---|---|
| `Name (name)` | `string?` | Cardholder name |
| `Number (number)` | `string?` | Card number |
| `Expiry (expiry)` | `string?` | Format: `"YYYY-MM"` |
| `SecurityCode (security_code)` | `string?` | CVV |
| `Brand (brand)` | `CardBrand?` | optional |
| `BillingAddress (billing_address)` | `Address?` | optional |

**`Customer` fields** (source: `records-1-Ac-Pa.md`):

| C# name (wire name) | Type |
|---|---|
| `Id (id)` | `string?` — PayPal customer ID |
| `MerchantCustomerId (merchant_customer_id)` | `string?` — our internal customer ID |

**`PaymentTokenResponse` fields** (source: `records-2-Pa-Ve.md`):

| C# name (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | **Vault token ID** — store in our DB; use as `CardRequest.VaultId` for future payments |
| `Customer (customer)` | `CustomerResponse?` | |
| `PaymentSource (payment_source)` | `PaymentTokenResponsePaymentSource?` | |

**`PaymentTokenResponsePaymentSource` fields** (source: `records-2-Pa-Ve.md`):

| C# name (wire name) | Type |
|---|---|
| `Card (card)` | `CardPaymentTokenEntity?` |

**`CardPaymentTokenEntity` fields** (source: `records-1-Ac-Pa.md`) — safe descriptors to store/return:

| C# name (wire name) | Type |
|---|---|
| `LastDigits (last_digits)` | `string?` |
| `Brand (brand)` | `CardBrand?` |
| `Expiry (expiry)` | `string?` |
| `Type (type)` | `CardType?` |
| `BillingAddress (billing_address)` | `CardResponseAddress?` |

**Never store or return `Number` or `SecurityCode` from the original request.**

---

### Step 8 — List saved cards

#### `client.Vault.ListCustomerPaymentTokens` (source: `operations/Vault.md`)

```
ListCustomerPaymentTokens(
    string customerId,                  // PayPal customer ID (stored from CreatePaymentToken)
    int? pageSize = 5,
    int? page = 1,
    bool? totalRequired = false,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `CustomerVaultPaymentTokensResponse` — namespace `PayPalServerSdk.Models`
Error: **Case A** — `SdkException<ListCustomerPaymentTokensError>`
- `TryGetError1(out Error1 error)` — statuses 400, 403, 500
- `TryGetRawError(out RawError error)` — fallback

**`CustomerVaultPaymentTokensResponse` fields** (source: `records-1-Ac-Pa.md`):

| C# name (wire name) | Type |
|---|---|
| `PaymentTokens (payment_tokens)` | `IReadOnlyList<PaymentTokenResponse>?` |
| `TotalItems (total_items)` | `int?` |
| `TotalPages (total_pages)` | `int?` |
| `Customer (customer)` | `VaultResponseCustomer?` |
| `Links (links)` | `IReadOnlyList<LinkDescription>?` |

Response items are `PaymentTokenResponse` — read `Id`, `PaymentSource.Card.LastDigits`, `PaymentSource.Card.Brand`, `PaymentSource.Card.Expiry` for safe descriptors. Never expose full card numbers (they are not returned by this API).

---

### Step 9 — Delete vault token

#### `client.Vault.DeletePaymentToken` (source: `operations/Vault.md`)

```
DeletePaymentToken(
    string id,                          // Vault token ID (PaymentTokenResponse.Id)
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `void` (Task)
Error: **Case A** — `SdkException<DeletePaymentTokenError>`
- `TryGetError1(out Error1 error)` — statuses 400, 403, 500
- `TryGetRawError(out RawError error)` — fallback

After successful deletion, remove the token from our DB records.

---

### Error payload types

**`Error`** (used by Orders and Payments errors; source: `records-1-Ac-Pa.md`) — namespace `PayPalServerSdk.Models`:

| C# name (wire name) | Type | Req? |
|---|---|---|
| `Name (name)` | `string` | required |
| `Message (message)` | `string` | required |
| `DebugId (debug_id)` | `string` | required |
| `Details (details)` | `IReadOnlyList<ErrorDetails>?` | optional |
| `Links (links)` | `IReadOnlyList<LinkDescription>?` | optional |

**`Error1`** (used by Vault errors; source: `records-1-Ac-Pa.md`) — namespace `PayPalServerSdk.Models`:

| C# name (wire name) | Type | Req? |
|---|---|---|
| `Name (name)` | `string` | required |
| `Message (message)` | `string` | required |
| `DebugId (debug_id)` | `string` | required |
| `Details (details)` | `IReadOnlyList<ErrorDetails1>?` | optional |
| `Links (links)` | `IReadOnlyList<ErrorLinkDescription>?` | optional |

**`DefaultError`** (used by SearchBalances; source: `records-1-Ac-Pa.md`):

| C# name (wire name) | Type | Req? |
|---|---|---|
| `Name (name)` | `string` | required |
| `Message (message)` | `string` | required |
| `DebugId (debug_id)` | `string` | required |
| `Details (details)` | `IReadOnlyList<TransactionSearchErrorDetails>?` | optional |

---

### Idempotency key summary

| Operation | Idempotency parameter | Notes |
|---|---|---|
| `CreateOrder` | `payPalRequestId` | Use eShop orderId + attempt hash |
| `AuthorizeOrder` | `payPalRequestId` | Same key per authorization attempt |
| `CaptureAuthorizedPayment` | `payPalRequestId` | Generate once per capture attempt; store and reuse on retry |
| `ReauthorizePayment` | `payPalRequestId` | Generate once per reauth attempt |
| `VoidPayment` | `payPalRequestId` | Use with void operations |
| `RefundCapturedPayment` | `payPalRequestId` | **Caller-supplied** — accept from the API request body |
| `CreatePaymentToken` | `payPalRequestId` | Generate once per vault attempt |

---

### Servers & Auth

| Property | Type | Note |
|---|---|---|
| `options.Environment` | `ServerEnvironment.Sandbox` | Sandbox environment |
| `options.Oauth2` | `OAuth2ClientCredentials?` | Set `ClientId` + `ClientSecret` from config |
| `options.Server` | `DefaultOptions` (namespace `PayPalServerSdk.Servers`, source: `Servers/DefaultOptions.cs`) | Custom base-URL override when `PayPal:BaseUrl` is set |

**Custom base URL — verified shape (sources: `ServerOptions.cs` + `Servers/DefaultOptions.cs`):**
```csharp
// ServerOptions (namespace PayPalServerSdk) has one public property:
//   public DefaultOptions Default { get; set; } = new();
// DefaultOptions (namespace PayPalServerSdk.Servers) has:
//   public SandboxOptions Sandbox { get; set; } = new();
// SandboxOptions.BaseUrl defaults to "https://api-m.sandbox.paypal.com"
options.Server.Default.Sandbox.BaseUrl = configuration["PayPal:BaseUrl"];
```
The full access path is `options.Server.Default.Sandbox.BaseUrl`. This compiles and builds clean (verified). Whether this also overrides the OAuth2 token endpoint — **MUST load `dotnet-configuration-resilience`** to confirm before implementing.

---

## 3. Trap Notes

> ⚠ **Step 2 (AuthorizeOrder response)** — If `OrderAuthorizeResponse.Status == OrderStatus.PayerActionRequired`, the card requires browser-based 3DS/SCA. The integration must return a clear error to the caller — do not build a redirect flow. This status is a deterministic rejection for the direct-card requirement. **MUST load `dotnet-calling-endpoints`** before writing the response-handling logic.

> ⚠ **Step 2 (vault token in payment)** — The `OrderAuthorizeRequestPaymentSource.Token` field (type `Token`) accepts only `TokenType.BillingAgreement` wire values. Card vault tokens from `CreatePaymentToken` are NOT billing agreements. Use `CardRequest.VaultId = paymentTokenId` instead of `OrderAuthorizeRequestPaymentSource.Token`. **MUST load `dotnet-models`** before constructing either payment source variant.

> ⚠ **Step 3 (stale authorization reauthorize limits)** — `ReauthorizePayment` is valid only from day 4 to day 29 after the 3-day honor period (i.e., days 4-29 from creation). Beyond 29 days from original authorization, PayPal rejects with 422; the integration must report "authorization expired, cannot renew — create new order" without retrying. **MUST load `dotnet-error-handling`** to handle the 422 case correctly (it comes through `TryGetError`, not `TryGetNoContent`).

> ⚠ **Step 5 (refund idempotency vs over-refund)** — PayPal returns HTTP 409 for both duplicate refund requests (same `payPalRequestId`) and over-refund attempts. Distinguish them using our local DB tally: enforce the "total refunds ≤ captured amount" check before calling the API. Do not treat all 409s as "already refunded". **MUST load `dotnet-error-handling`** before writing the 409 handling branch.

> ⚠ **Step 6 (SearchTransactions — Case B error)** — `SearchTransactions` is the single Case B operation in this SDK (one of 40 total). Its error is `SdkException<RawError>`, NOT `SdkException<SomeNamedError>`. A catch ladder that only catches typed errors will miss this. **MUST load `dotnet-error-handling`** before writing the reconciliation error boundary.

> ⚠ **Step 6 (pagination — manual loop required)** — `SearchTransactions` has no built-in paging helper (map marks it "none — only `page`"). Implement a manual loop incrementing `page` from 1 to `response.TotalPages`. Pass all 8 nullable params explicitly (pass `null` to skip); a positional call will mis-bind them. **MUST load `dotnet-calling-endpoints`** before writing the loop.

> ⚠ **Step 7 (direct card vaulting — PCI scope)** — `CreatePaymentToken` with a raw `PaymentTokenRequestCard` (number + CVV) requires PCI SAQ D compliance at the merchant, identical to direct card authorization. This is in scope because the overall integration already requires PCI SAQ D for `CardRequest` use in authorization. No redirect or hosted-fields flow is needed, but the PCI obligation must be acknowledged.

> ⚠ **Step 1 (client registration / HttpClient lifetime)** — the SDK wraps an `HttpClient`; the handler pipeline must be long-lived and managed by `IHttpClientFactory`. **MUST load `dotnet-client-initialization`** before writing the DI registration.

> ⚠ **Step 1 (credentials — load from configuration, not hardcoded)** — `OAuth2ClientCredentials` properties must be wired from configuration (e.g. `IConfiguration["PayPal:ClientId"]`), never hardcoded. **MUST load `dotnet-authentication`** before setting credentials.

> ⚠ **Step 1 (retry — POST idempotency risk)** — `HttpMethodsToRetry` gates only the *status* trigger; a transport failure (`HttpRequestException`) is retried on every verb, `POST` included. A non-idempotent `CreateOrder` or `AuthorizeOrder` call can execute more than once on transport retry. The `payPalRequestId` idempotency key (used on every write operation) is the mitigation — confirm it is set before any retry fires. **MUST load `dotnet-configuration-resilience`** before configuring retry options.

> ⚠ **Step 1 (custom BaseUrl — token endpoint)** — when `PayPal:BaseUrl` is configured, verify that it also overrides the OAuth2 token request endpoint, not just data-plane calls. If the token endpoint is separately configurable, failure to override it will cause 401 errors in production with a custom base URL while sandbox works. **MUST load `dotnet-configuration-resilience`** for the exact override mechanism.

---

## 4. REQUIRED READING

Load **all** of the following before writing any implementation code. The contract sheet deliberately does not carry the contents of these skills — they define defaults, worked examples, and mechanics that a one-line trap note cannot replace.

| Skill | Steps it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — DI registration, `IHttpClientFactory`, `AddPayPalServerSdkClient` |
| `dotnet-authentication` | Step 1 — `OAuth2ClientCredentials`, credential wiring, reading secrets from config |
| `dotnet-calling-endpoints` | Steps 2–9 — named-argument discipline for nullable must-pass params, response envelope reading |
| `dotnet-models` | Steps 2–9 — `StringEnum<T>` construction (`CheckoutPaymentIntent.Authorize` not `"AUTHORIZE"`), `CardRequest.VaultId` vs `Token` variant shape |
| `dotnet-error-handling` | Steps 2–10 — Case A typed accessors per operation, Case B raw error for `SearchTransactions`, `TryGetNoContent` for 500s, `JsonException` boundary rules (see below) |
| `dotnet-configuration-resilience` | Step 1 — retry semantics, `Timeout` scope, `ServerOptions` shape for custom base URL |
| `dotnet-testing` | All — SDK test seam via `HttpClient` constructor argument |

**Mandatory `JsonException` boundary rules** (`dotnet-error-handling` governs both; load before writing the error boundary):

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the boundary.

---

## 5. Assumptions & Blockers

| Item | Type | Detail |
|---|---|---|
| PayPal sandbox supports direct card authorization without browser redirect | ASSUMPTION | The sandbox accepts inline `CardRequest` (number/expiry/CVV) in `AuthorizeOrder` and returns `APPROVED` (not `PAYER_ACTION_REQUIRED`) for test card numbers. If sandbox cards always trigger 3DS, direct-card authorization is a **BLOCKER** for this integration and an alternative (SetupToken + hosted fields) would be required. UNVERIFIED: sandbox behavior depends on specific test card numbers used. |
| Card vault tokens usable as `CardRequest.VaultId` in AuthorizeOrder | ASSUMPTION | The `CardRequest.VaultId` field is used to reference a card vault token from `CreatePaymentToken`. The map describes this field as "the vaulted id of the wallet PayPal account" — it is UNVERIFIED whether PayPal sandbox accepts a card payment-token ID (from Vault v3 API) as `vault_id` in an Orders v2 `AuthorizeOrder` call. If not accepted, a different payment source type would be needed. Defensive code: if the authorization returns `PAYER_ACTION_REQUIRED` or 422 when using `VaultId`, report a clear error. |
| `DefaultOptions` shape for custom base URL | RESOLVED (source: `Servers/DefaultOptions.cs`) | `options.Server.Sandbox.BaseUrl = verbatimUrl`. `DefaultOptions` class (namespace `PayPalServerSdk.Servers`) has one public property: `Sandbox: SandboxOptions` with `BaseUrl: string`. Whether this also overrides the OAuth2 token endpoint is UNVERIFIED — confirm via `dotnet-configuration-resilience`. |
| eShop customer ID to PayPal customer ID mapping | ASSUMPTION | The integration stores a mapping from eShop user ID to PayPal customer ID (used as `customerId` in `ListCustomerPaymentTokens`). This mapping must be persisted in the eShop DB. The plan assumes this table/column exists or will be added as part of this implementation. |
| Over-refund check uses local DB as authoritative source | DESIGN DECISION | `SellerPayableBreakdown.TotalRefundedAmount` is marked UNVERIFIED (it may reflect only the current refund, not cumulative totals). The integration uses the sum of all refund amounts stored in our DB as the authoritative over-refund guard, treating any PayPal-returned total as confirmation only. |
| Reauthorization window | DOCUMENTED CONSTRAINT | PayPal allows reauthorization only from day 4 to day 29 after original authorization. The integration must persist the original authorization creation timestamp to enforce this check. |
| `balanceAffectingRecordsOnly` default for reconciliation | ASSUMPTION | The reconciliation step uses the default `"Y"` (balance-affecting records only). If the reconciler needs all transaction types, override to `"N"`. |
