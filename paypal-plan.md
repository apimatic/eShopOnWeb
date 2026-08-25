# PayPal Integration Plan — eShopOnWeb (`src/PublicApi`)

> All signatures are generated code verbatim. Every parameter name is the literal C# identifier.
> The cancellation-token parameter is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.
>
> Every SDK type below is written fully-qualified with the namespace the map gives it. Records and enums
> are in separate child namespaces — C# does not import them transitively. Add a `using` for each kind:
> `using PayPalServerSdk;` · `using PayPalServerSdk.Models;` · `using PayPalServerSdk.Models.Enums;` ·
> `using PayPalServerSdk.Errors;` · `using PayPalServerSdk.Servers;`

---

## 1. Scope & Sequence

| Step | eShop Endpoint | SDK Operation(s) |
|---|---|---|
| 1 | Install SDK; register DI client | NuGet `AsadAli.Checkout.Sdk`; `services.AddPayPalServerSdkClient(...)` |
| 2 | `POST /api/orders` | No PayPal call — create eShop order in `AwaitingPayment` state |
| 3 | `POST /api/orders/{orderId}/pay` (new card) | `client.Orders.CreateOrder(...)` intent=AUTHORIZE + `PaymentSource.Card` |
| 4 | `POST /api/orders/{orderId}/pay` (saved card) | `client.Orders.CreateOrder(...)` intent=AUTHORIZE + `PaymentSource.Token` |
| 5 | `POST /api/orders/{orderId}/fulfil` — happy path | `client.Payments.CaptureAuthorizedPayment(...)` |
| 6 | `POST /api/orders/{orderId}/fulfil` — stale authorization | `client.Payments.GetAuthorizedPayment(...)` → `client.Payments.ReauthorizePayment(...)` → `client.Payments.CaptureAuthorizedPayment(...)` |
| 7 | `POST /api/orders/{orderId}/cancel` | `client.Payments.VoidPayment(...)` |
| 8 | `POST /api/orders/{orderId}/refunds` | `client.Payments.RefundCapturedPayment(...)` |
| 9 | `GET /api/my-orders` | eShop data only (PayPal state stored from prior steps) |
| 10 | `GET /api/reconciliation?from&to` | `client.TransactionSearch.SearchTransactions(...)` loop over all pages |
| 11 | `POST /api/payment-methods` | `client.Vault.CreateSetupToken(...)` → `client.Vault.CreatePaymentToken(...)` |
| 12 | `GET /api/payment-methods` | `client.Vault.ListCustomerPaymentTokens(...)` |
| 13 | `DELETE /api/payment-methods/{paymentMethodId}` | `client.Vault.DeletePaymentToken(...)` |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The
> cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from
> that type's own map row. Enums, records, and error types live in different child namespaces.

---

### 2A. Operations

#### `client.Orders.CreateOrder` — Steps 3, 4 (authorize-only)

Source: `map/operations/Orders.md`

```
CreateOrder(
  string? payPalMockResponse,         // must pass explicitly — pass null in prod
  string? payPalRequestId,            // IDEMPOTENCY KEY — pass unique string per pay attempt
  string? payPalPartnerAttributionId, // must pass explicitly — pass null
  string? payPalClientMetadataId,     // must pass explicitly — pass null
  string? payPalAuthAssertion,        // must pass explicitly — pass null
  OrderRequest body,                  // required, not nullable
  string? prefer = "return=minimal",  // override: "return=representation" to get authorization ID inline
  RequestOptions? requestOptions = null,
  CancellationToken ct = default
) → Task<OrderAuthorizeResponse>  // WAIT — returns Order, not OrderAuthorizeResponse
```

**Correction:** Returns `PayPalServerSdk.Models.Order`. The authorization details are inside:
`Order.PurchaseUnits[0].Payments.Authorizations[0]` (type `AuthorizationWithAdditionalData`).
Pass `prefer: "return=representation"` to get this populated — with `"return=minimal"` the Payments
collection is absent.

**Error:** `SdkException<PayPalServerSdk.Errors.CreateOrderError>` — Case A (typed)
- `TryGetError(out PayPalServerSdk.Models.Error e)` → [400, 401, 422]
- `TryGetRawError(out RawError r)` → fallback

**Idempotency:** `payPalRequestId` — pass a stable key per "pay" attempt (e.g. `$"pay-{orderId}-{userId}"`). Same key = PayPal returns same result; second call does not re-authorize.

---

#### `client.Orders.AuthorizeOrder` — Step 3/4 (optional separate authorize call)

Source: `map/operations/Orders.md`

Only needed if you split CreateOrder (no payment_source) from the authorize step. For this
integration, authorization is folded into `CreateOrder` with `payment_source` — `AuthorizeOrder` is
documented here for completeness.

```
AuthorizeOrder(
  string id,                          // PayPal order ID from CreateOrder response
  string? payPalMockResponse,         // must pass explicitly — pass null
  string? payPalRequestId,            // IDEMPOTENCY KEY
  string? payPalClientMetadataId,     // must pass explicitly — pass null
  string? payPalAuthAssertion,        // must pass explicitly — pass null
  OrderAuthorizeRequest? body,        // must pass explicitly
  string? prefer = "return=minimal",
  RequestOptions? requestOptions = null,
  CancellationToken ct = default
) → Task<PayPalServerSdk.Models.OrderAuthorizeResponse>
```

`OrderAuthorizeResponse.PurchaseUnits[0].Payments.Authorizations[0]` (type `AuthorizationWithAdditionalData`) holds the authorization ID.

**Error:** `SdkException<PayPalServerSdk.Errors.AuthorizeOrderError>` — Case A
- `TryGetError(out PayPalServerSdk.Models.Error e)` → [400, 401, 403, 404, 422, 500]
- `TryGetRawError(out RawError r)` → fallback

---

#### `client.Payments.GetAuthorizedPayment` — Step 6 (staleness check)

Source: `map/operations/Payments.md`

```
GetAuthorizedPayment(
  string authorizationId,
  string? payPalMockResponse,   // must pass explicitly — pass null
  string? payPalAuthAssertion,  // must pass explicitly — pass null
  RequestOptions? requestOptions = null,
  CancellationToken ct = default
) → Task<PayPalServerSdk.Models.PaymentAuthorization>
```

Read `PaymentAuthorization.Status` and `PaymentAuthorization.ExpirationTime` to decide whether to reauthorize before capturing. `ExpirationTime` is ISO-8601 string — parse to `DateTimeOffset` for comparison.

**Error:** `SdkException<PayPalServerSdk.Errors.GetAuthorizedPaymentError>` — Case A
- `TryGetError(out PayPalServerSdk.Models.Error e)` → [401, 403, 404]
- `TryGetNoContent(out RawError r)` → [500]
- `TryGetRawError(out RawError r)` → fallback

---

#### `client.Payments.ReauthorizePayment` — Step 6 (stale renewal)

Source: `map/operations/Payments.md`

```
ReauthorizePayment(
  string authorizationId,
  string? payPalRequestId,      // must pass explicitly — idempotency key
  string? payPalAuthAssertion,  // must pass explicitly — pass null
  ReauthorizeRequest? body,     // must pass explicitly; body.Amount = same or adjusted amount
  string? prefer = "return=minimal",
  RequestOptions? requestOptions = null,
  CancellationToken ct = default
) → Task<PayPalServerSdk.Models.PaymentAuthorization>
```

The returned `PaymentAuthorization.Id` is the **new** authorization ID — store it and use it for
capture. Valid window: days 4–29 from original authorization. If >30 days: `ReauthorizePayment` will
throw — catch and surface as an actionable operator error (cannot renew; must create a new order).

**Error:** `SdkException<PayPalServerSdk.Errors.ReauthorizePaymentError>` — Case A
- `TryGetError(out PayPalServerSdk.Models.Error e)` → [400, 401, 403, 404, 422]
- `TryGetNoContent(out RawError r)` → [500]
- `TryGetRawError(out RawError r)` → fallback

---

#### `client.Payments.CaptureAuthorizedPayment` — Steps 5, 6

Source: `map/operations/Payments.md`

```
CaptureAuthorizedPayment(
  string authorizationId,
  string? payPalMockResponse,   // must pass explicitly — pass null
  string? payPalRequestId,      // IDEMPOTENCY KEY — pass unique key per fulfil attempt
  string? payPalAuthAssertion,  // must pass explicitly — pass null
  CaptureRequest? body,         // must pass explicitly; may be null for full capture
  string? prefer = "return=minimal",  // override: "return=representation" to get fee breakdown
  RequestOptions? requestOptions = null,
  CancellationToken ct = default
) → Task<PayPalServerSdk.Models.CapturedPayment>
```

Use `prefer: "return=representation"` to get `SellerReceivableBreakdown` populated.

**Store from `CapturedPayment`:**
- `CapturedPayment.Id` — capture ID
- `CapturedPayment.Amount` — gross captured (Money)
- `CapturedPayment.SellerReceivableBreakdown.GrossAmount` — gross (Money, required)
- `CapturedPayment.SellerReceivableBreakdown.PaypalFee` — PayPal fee (Money, nullable)
- `CapturedPayment.SellerReceivableBreakdown.NetAmount` — net proceeds (Money, nullable)

**Error:** `SdkException<PayPalServerSdk.Errors.CaptureAuthorizedPaymentError>` — Case A
- `TryGetError(out PayPalServerSdk.Models.Error e)` → [400, 401, 403, 404, 409, 422]
- `TryGetNoContent(out RawError r)` → [500]
- `TryGetRawError(out RawError r)` → fallback

**Idempotency:** `payPalRequestId` — same key = same capture result, no double-capture.

---

#### `client.Payments.VoidPayment` — Step 7

Source: `map/operations/Payments.md`

```
VoidPayment(
  string authorizationId,
  string? payPalMockResponse,   // must pass explicitly — pass null
  string? payPalAuthAssertion,  // must pass explicitly — pass null
  string? payPalRequestId,      // must pass explicitly — param 4, NOT param 2; idempotency key
  string? prefer = "return=minimal",
  RequestOptions? requestOptions = null,
  CancellationToken ct = default
) → Task<PayPalServerSdk.Models.PaymentAuthorization>
```

CRITICAL: The parameter order for `VoidPayment` is different from other operations —
`payPalRequestId` is the **4th** nullable parameter, after `payPalAuthAssertion`, not the 2nd.
Always use named arguments to avoid mis-binding.

**Error:** `SdkException<PayPalServerSdk.Errors.VoidPaymentError>` — Case A
- `TryGetError(out PayPalServerSdk.Models.Error e)` → [401, 403, 404, 409, 422]
- `TryGetNoContent(out RawError r)` → [500]
- `TryGetRawError(out RawError r)` → fallback

---

#### `client.Payments.RefundCapturedPayment` — Step 8

Source: `map/operations/Payments.md`

```
RefundCapturedPayment(
  string captureId,
  string? payPalMockResponse,   // must pass explicitly — pass null
  string? payPalRequestId,      // IDEMPOTENCY KEY — caller-supplied key
  string? payPalAuthAssertion,  // must pass explicitly — pass null
  RefundRequest? body,          // must pass explicitly; null body = full refund; set Amount for partial
  string? prefer = "return=minimal",
  RequestOptions? requestOptions = null,
  CancellationToken ct = default
) → Task<PayPalServerSdk.Models.Refund>
```

**Refund idempotency rules:**
- Same `payPalRequestId` + same `captureId` = PayPal returns same `Refund` (no double-refund)
- Different `payPalRequestId` + same `captureId` = new refund allowed (partial refunds accumulate)
- Partial refund guard: validate `body.Amount.Value` does not exceed stored captured amount **before** calling the API

**Error:** `SdkException<PayPalServerSdk.Errors.RefundCapturedPaymentError>` — Case A
- `TryGetError(out PayPalServerSdk.Models.Error e)` → [400, 401, 403, 404, 409, 422]
- `TryGetNoContent(out RawError r)` → [500]
- `TryGetRawError(out RawError r)` → fallback

---

#### `client.TransactionSearch.SearchTransactions` — Step 10

Source: `map/operations/TransactionSearch.md`

```
SearchTransactions(
  string startDate,                         // ISO-8601 datetime, e.g. "2025-01-01T00:00:00-0700"
  string endDate,                           // ISO-8601 datetime
  string? transactionId,                    // must pass explicitly — pass null
  string? transactionType,                  // must pass explicitly — pass null
  string? transactionStatus,               // must pass explicitly — pass null
  string? transactionAmount,               // must pass explicitly — pass null
  string? transactionCurrency,             // must pass explicitly — pass null
  string? paymentInstrumentType,           // must pass explicitly — pass null
  string? storeId,                         // must pass explicitly — pass null
  string? terminalId,                      // must pass explicitly — pass null
  string? fields = "transaction_info",
  string? balanceAffectingRecordsOnly = "Y",
  int? pageSize = 100,
  int? page = 1,
  RequestOptions? requestOptions = null,
  CancellationToken ct = default
) → Task<PayPalServerSdk.Models.SearchResponse>
```

**Pagination loop (full range, not just first page):**
1. Call with `page: 1`, `pageSize: 100`
2. Read `SearchResponse.TotalPages` (int?) from first response
3. Collect `SearchResponse.TransactionDetails` (IReadOnlyList<TransactionDetails>?)
4. Loop `page: 2` through `TotalPages`, collecting all `TransactionDetails`
5. Merge collected `TransactionDetails` with eShop orders for reconciliation output

**Error: Case B (raw error)** — `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>`
- `ex.Error.StatusCode` (HttpStatusCode)
- `ex.Error.ReadAsString()` (string body)
- `ex.Error.ReadAsJson<T>()` (T?)
No typed error class exists for this operation.

---

#### `client.Vault.CreateSetupToken` — Step 11

Source: `map/operations/Vault.md`

```
CreateSetupToken(
  string? payPalRequestId,          // must pass explicitly — idempotency key
  SetupTokenRequest body,           // required
  RequestOptions? requestOptions = null,
  CancellationToken ct = default
) → Task<PayPalServerSdk.Models.SetupTokenResponse>
```

**Error:** `SdkException<PayPalServerSdk.Errors.CreateSetupTokenError>` — Case A
- `TryGetError1(out PayPalServerSdk.Models.Error1 e)` → [400, 403, 422, 500]
- `TryGetRawError(out RawError r)` → fallback

Note: Vault operations use `TryGetError1` / `Error1`, not `TryGetError` / `Error`.

---

#### `client.Vault.CreatePaymentToken` — Step 11

Source: `map/operations/Vault.md`

```
CreatePaymentToken(
  string? payPalRequestId,          // must pass explicitly — idempotency key
  PaymentTokenRequest body,         // required
  RequestOptions? requestOptions = null,
  CancellationToken ct = default
) → Task<PayPalServerSdk.Models.PaymentTokenResponse>
```

**Error:** `SdkException<PayPalServerSdk.Errors.CreatePaymentTokenError>` — Case A
- `TryGetError1(out PayPalServerSdk.Models.Error1 e)` → [400, 403, 404, 422, 500]
- `TryGetRawError(out RawError r)` → fallback

---

#### `client.Vault.ListCustomerPaymentTokens` — Step 12

Source: `map/operations/Vault.md`

```
ListCustomerPaymentTokens(
  string customerId,                // merchant-side customer ID (wire name: customer_id)
  int? pageSize = 5,
  int? page = 1,
  bool? totalRequired = false,
  RequestOptions? requestOptions = null,
  CancellationToken ct = default
) → Task<PayPalServerSdk.Models.CustomerVaultPaymentTokensResponse>
```

**Error:** `SdkException<PayPalServerSdk.Errors.ListCustomerPaymentTokensError>` — Case A
- `TryGetError1(out PayPalServerSdk.Models.Error1 e)` → [400, 403, 500]
- `TryGetRawError(out RawError r)` → fallback

---

#### `client.Vault.DeletePaymentToken` — Step 13

Source: `map/operations/Vault.md`

```
DeletePaymentToken(
  string id,                        // payment token ID
  RequestOptions? requestOptions = null,
  CancellationToken ct = default
) → Task (void)
```

**Error:** `SdkException<PayPalServerSdk.Errors.DeletePaymentTokenError>` — Case A
- `TryGetError1(out PayPalServerSdk.Models.Error1 e)` → [400, 403, 500]
- `TryGetRawError(out RawError r)` → fallback

---

### 2B. Request Models

Source for all: `map/models/records-1-Ac-Pa.md`, `map/models/records-2-Pa-Ve.md` — namespace `PayPalServerSdk.Models`.

#### `OrderRequest` — body for `CreateOrder`

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `Intent (intent)` | `CheckoutPaymentIntent` | **required** |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | **required** |
| `PaymentSource (payment_source)` | `PaymentSource?` | optional — include for direct authorize |
| `Payer (payer)` | `Payer?` | optional |
| `ApplicationContext (application_context)` | `OrderApplicationContext?` | optional |

#### `PurchaseUnitRequest` — inside `OrderRequest.PurchaseUnits`

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `Amount (amount)` | `AmountWithBreakdown` | **required** |
| `ReferenceId (reference_id)` | `string?` | optional (use eShop orderId for traceability) |
| `InvoiceId (invoice_id)` | `string?` | optional |
| `CustomId (custom_id)` | `string?` | optional |

#### `AmountWithBreakdown` — inside `PurchaseUnitRequest.Amount`

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `CurrencyCode (currency_code)` | `string` | **required** — e.g. `"USD"` from `PayPal:Currency` config |
| `Value (value)` | `string` | **required** — decimal string, e.g. `"29.99"` |
| `Breakdown (breakdown)` | `AmountBreakdown?` | optional |

#### `PaymentSource` — inside `OrderRequest.PaymentSource` (direct card path)

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `Card (card)` | `CardRequest?` | Set for new card; leave other fields null |
| `Token (token)` | `Token?` | Set for saved card (vault token); leave Card null |

#### `CardRequest` — inside `PaymentSource.Card`

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `Name (name)` | `string?` | optional — cardholder name |
| `Number (number)` | `string?` | optional — card number (PAN) |
| `Expiry (expiry)` | `string?` | optional — `"YYYY-MM"` format |
| `SecurityCode (security_code)` | `string?` | optional — CVV |
| `BillingAddress (billing_address)` | `Address?` | optional |
| `Attributes (attributes)` | `CardAttributes?` | optional — set for vault-on-success |
| `VaultId (vault_id)` | `string?` | optional — alternative to Token for vaulted card |
| `StoredCredential (stored_credential)` | `CardStoredCredential?` | optional — for MIT/CIT flows |

Sandbox test card: Number `"4111111111111111"`, future Expiry e.g. `"2027-02"`, any SecurityCode.

#### `Token` — inside `PaymentSource.Token` (saved card path)

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `Id (id)` | `string` | **required** — payment token ID from vault |
| `Type (type)` | `TokenType` | **required** — `TokenType.BillingAgreement` |

UNVERIFIED: The SDK exposes only `TokenType.BillingAgreement ("BILLING_AGREEMENT")` — this is the
wire value used to reference a Vault payment token in an order's payment source. Confirm against live
traffic if the sandbox roundtrip rejects this value; if so, the integration must fall back to
`CardRequest.VaultId`.

#### `CaptureRequest` — body for `CaptureAuthorizedPayment`

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `Amount (amount)` | `Money?` | optional — omit for full capture |
| `FinalCapture (final_capture)` | `bool? = false` | optional |
| `InvoiceId (invoice_id)` | `string?` | optional |
| `NoteToPayer (note_to_payer)` | `string?` | optional |

#### `ReauthorizeRequest` — body for `ReauthorizePayment`

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `Amount (amount)` | `Money?` | optional — pass same amount as original |

#### `RefundRequest` — body for `RefundCapturedPayment`

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `Amount (amount)` | `Money?` | optional — omit for full refund; set for partial |
| `CustomId (custom_id)` | `string?` | optional |
| `InvoiceId (invoice_id)` | `string?` | optional |
| `NoteToPayer (note_to_payer)` | `string?` | optional |

#### `SetupTokenRequest` — body for `CreateSetupToken`

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `PaymentSource (payment_source)` | `SetupTokenRequestPaymentSource` | **required** |
| `Customer (customer)` | `Customer?` | optional — set `Id = merchantCustomerId` for association |

#### `SetupTokenRequestPaymentSource`

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `Card (card)` | `SetupTokenRequestCard?` | Set for card vaulting |

#### `SetupTokenRequestCard`

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `Name (name)` | `string?` | optional |
| `Number (number)` | `string?` | optional — PAN |
| `Expiry (expiry)` | `string?` | optional — `"YYYY-MM"` |
| `SecurityCode (security_code)` | `string?` | optional — CVV |
| `BillingAddress (billing_address)` | `Address?` | optional |
| `VerificationMethod (verification_method)` | `VaultCardVerificationMethod?` | optional |

#### `PaymentTokenRequest` — body for `CreatePaymentToken`

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `PaymentSource (payment_source)` | `PaymentTokenRequestPaymentSource` | **required** |
| `Customer (customer)` | `Customer?` | optional — set same customer ID |

#### `PaymentTokenRequestPaymentSource`

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `Token (token)` | `VaultTokenRequest?` | Set to convert setup token → payment token |
| `Card (card)` | `PaymentTokenRequestCard?` | Alternative: vault card directly |

#### `VaultTokenRequest`

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `Id (id)` | `string` | **required** — setup token ID from `SetupTokenResponse.Id` |
| `Type (type)` | `VaultTokenRequestType` | **required** — `VaultTokenRequestType.SetupToken` |

#### `Money`

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `CurrencyCode (currency_code)` | `string` | **required** — e.g. `"USD"` |
| `Value (value)` | `string` | **required** — decimal string |

#### `Address`

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `CountryCode (country_code)` | `string` | **required** |
| `AddressLine1 (address_line_1)` | `string?` | optional |
| `AdminArea2 (admin_area_2)` | `string?` | optional — city |
| `AdminArea1 (admin_area_1)` | `string?` | optional — state/region |
| `PostalCode (postal_code)` | `string?` | optional |

#### `Customer` — inside `SetupTokenRequest` and `PaymentTokenRequest`

| C# Property (wire name) | Type | Required? |
|---|---|---|
| `Id (id)` | `string?` | optional — merchant-side customer ID for vault association |

---

### 2C. Response Models (fields the integration reads)

Source: `map/models/records-1-Ac-Pa.md`, `map/models/records-2-Pa-Ve.md` — namespace `PayPalServerSdk.Models`.

#### `Order` — returned by `CreateOrder` (with `prefer="return=representation"`)

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | PayPal order ID — store this |
| `Status (status)` | `OrderStatus?` | Expected `Completed` for direct card authorize |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnit>?` | Envelope for authorization |

Authorization is at: `Order.PurchaseUnits[0].Payments.Authorizations[0]` (type `AuthorizationWithAdditionalData`)

#### `AuthorizationWithAdditionalData` — inside `PurchaseUnit.Payments.Authorizations`

`PurchaseUnit.Payments` is type `PaymentCollection`; `PaymentCollection.Authorizations` is `IReadOnlyList<AuthorizationWithAdditionalData>?`.

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | Authorization ID — store this |
| `Status (status)` | `AuthorizationStatus?` | `Created` = valid hold |
| `ExpirationTime (expiration_time)` | `string?` | ISO-8601; parse to check staleness |
| `Amount (amount)` | `Money?` | Authorized amount |

#### `PaymentAuthorization` — returned by `GetAuthorizedPayment`, `ReauthorizePayment`, `VoidPayment`

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | Authorization ID (new ID after reauthorize — store updated) |
| `Status (status)` | `AuthorizationStatus?` | |
| `ExpirationTime (expiration_time)` | `string?` | ISO-8601 |
| `Amount (amount)` | `Money?` | |

#### `CapturedPayment` — returned by `CaptureAuthorizedPayment`

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | Capture ID — store this |
| `Status (status)` | `CaptureStatus?` | Expected `Completed` |
| `Amount (amount)` | `Money?` | Gross captured amount |
| `SellerReceivableBreakdown (seller_receivable_breakdown)` | `SellerReceivableBreakdown?` | Fee breakdown — null when `prefer=minimal` |

#### `SellerReceivableBreakdown` — inside `CapturedPayment`

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `GrossAmount (gross_amount)` | `Money` | **required** — gross captured |
| `PaypalFee (paypal_fee)` | `Money?` | PayPal fee — may be null |
| `NetAmount (net_amount)` | `Money?` | Net proceeds — may be null |

#### `Refund` — returned by `RefundCapturedPayment`

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | Refund ID — store this |
| `Status (status)` | `RefundStatus?` | |
| `Amount (amount)` | `Money?` | Refunded amount |
| `SellerPayableBreakdown (seller_payable_breakdown)` | `SellerPayableBreakdown?` | Breakdown of refund |

#### `SearchResponse` — returned by `SearchTransactions`

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `TransactionDetails (transaction_details)` | `IReadOnlyList<TransactionDetails>?` | This page's results |
| `TotalPages (total_pages)` | `int?` | Total number of pages |
| `Page (page)` | `int?` | Current page number |
| `TotalItems (total_items)` | `int?` | Total transaction count |

#### `TransactionDetails` — inside `SearchResponse.TransactionDetails`

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `TransactionInfo (transaction_info)` | `TransactionInformation?` | Core transaction data |

#### `TransactionInformation` (key fields for reconciliation)

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `TransactionId (transaction_id)` | `string?` | PayPal transaction ID |
| `PaypalReferenceId (paypal_reference_id)` | `string?` | Related order/capture ID |
| `TransactionAmount (transaction_amount)` | `Money?` | |
| `FeeAmount (fee_amount)` | `Money?` | |
| `TransactionStatus (transaction_status)` | `string?` | Wire string, not an enum |
| `TransactionInitiationDate (transaction_initiation_date)` | `string?` | ISO-8601 |
| `InvoiceId (invoice_id)` | `string?` | Your invoice ID if set |

#### `PaymentTokenResponse` — returned by `CreatePaymentToken`, `GetPaymentToken`; inside `CustomerVaultPaymentTokensResponse.PaymentTokens`

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | Payment token ID — this is the `paymentMethodId` to expose |
| `Customer (customer)` | `CustomerResponse?` | Associated customer |
| `PaymentSource (payment_source)` | `PaymentTokenResponsePaymentSource?` | Safe card descriptor |

#### `PaymentTokenResponsePaymentSource`

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `Card (card)` | `CardPaymentTokenEntity?` | Safe card details |

#### `CardPaymentTokenEntity` — safe card descriptor (never expose full PAN)

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `LastDigits (last_digits)` | `string?` | Last 4 digits |
| `Brand (brand)` | `CardBrand?` | e.g. `CardBrand.Visa` |
| `Expiry (expiry)` | `string?` | `"YYYY-MM"` |
| `Type (type)` | `CardType?` | e.g. `CardType.Credit` |

#### `CustomerVaultPaymentTokensResponse` — returned by `ListCustomerPaymentTokens`

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `PaymentTokens (payment_tokens)` | `IReadOnlyList<PaymentTokenResponse>?` | All saved tokens |
| `TotalItems (total_items)` | `int?` | |
| `TotalPages (total_pages)` | `int?` | |

#### `SetupTokenResponse` — returned by `CreateSetupToken`

| C# Property (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | Setup token ID — pass to `CreatePaymentToken` |
| `Status (status)` | `PaymentTokenStatus?` | `Created` = proceed to `CreatePaymentToken` |

#### Error models (used with TryGetError / TryGetError1)

| Record | Fields | Used by |
|---|---|---|
| `Error` (namespace `PayPalServerSdk.Models`) | `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?` | Orders, Payments controllers |
| `Error1` (namespace `PayPalServerSdk.Models`) | `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails1>?` | Vault controller |
| `ErrorDetails` | `Issue (issue): string !req`, `Field (field): string?`, `Description (description): string?` | Inspect `Issue` for machine-actionable codes |

---

### 2D. Enums

Source: `map/models/enums.md` — namespace `PayPalServerSdk.Models.Enums`.

These are `StringEnum<T>` records — NOT C# enums. Build with the static member name.

| Enum | Members needed | Wire values |
|---|---|---|
| `CheckoutPaymentIntent` | `CheckoutPaymentIntent.Authorize` | `"AUTHORIZE"` |
| `AuthorizationStatus` | `Created`, `Captured`, `Denied`, `Voided`, `Pending`, `PartiallyCaptured` | `"CREATED"` etc. |
| `CaptureStatus` | `Completed`, `Declined`, `Pending`, `Failed`, `Refunded`, `PartiallyRefunded` | `"COMPLETED"` etc. |
| `RefundStatus` | `Completed`, `Pending`, `Failed`, `Cancelled` | `"COMPLETED"` etc. |
| `OrderStatus` | `Created`, `Approved`, `Completed`, `Voided`, `Saved`, `PayerActionRequired` | `"CREATED"` etc. |
| `PaymentTokenStatus` | `Created`, `Approved`, `Vaulted`, `Tokenized`, `PayerActionRequired` | `"CREATED"` etc. |
| `TokenType` | `TokenType.BillingAgreement` | `"BILLING_AGREEMENT"` |
| `VaultTokenRequestType` | `VaultTokenRequestType.SetupToken` | `"SETUP_TOKEN"` |
| `VaultCardVerificationMethod` | `ScaWhenRequired`, `ScaAlways` | `"SCA_WHEN_REQUIRED"` etc. |
| `StoreInVaultInstruction` | `StoreInVaultInstruction.OnSuccess` | `"ON_SUCCESS"` |
| `CardBrand` | `Visa`, `Mastercard`, `Amex`, etc. | `"VISA"` etc. |
| `CardType` | `Credit`, `Debit`, `Prepaid` | `"CREDIT"` etc. |

---

### 2E. Client Construction & Auth

Source: `sdk-map.md` (Servers & auth section); companion: `dotnet-client-initialization`, `dotnet-authentication`.

**DI registration** (`src/PublicApi/Program.cs` or `Startup.cs`):

```csharp
// using PayPalServerSdk;
// using PayPalServerSdk.Servers;

services.AddPayPalServerSdkClient(options =>
{
    options.Environment = ServerEnvironment.Sandbox; // map lists Sandbox as the only member
    options.Oauth2 = new OAuth2ClientCredentials
    {
        // exact constructor / property names: MUST load dotnet-authentication before wiring
    };
    // options.Server.BaseUri = ... // when PayPal:BaseUrl override is set — load dotnet-configuration-resilience
});
```

**Configuration keys** (from `appsettings.json` / secrets):

| Key | Usage |
|---|---|
| `PayPal:ClientId` | `OAuth2ClientCredentials` client ID |
| `PayPal:ClientSecret` | `OAuth2ClientCredentials` client secret |
| `PayPal:Environment` | Map to `ServerEnvironment.Sandbox` (only member the map documents); for production, use `PayPal:BaseUrl` override |
| `PayPal:Currency` | Currency code string for `AmountWithBreakdown.CurrencyCode` and `Money.CurrencyCode` |
| `PayPal:BaseUrl` | Optional — base-URL override for all calls including token endpoint; applied via `options.Server` — exact property: MUST load `dotnet-configuration-resilience` |

**Namespaces required (add all `using` directives):**
```csharp
using PayPalServerSdk;
using PayPalServerSdk.Api;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Servers;
using PayPalServerSdk.Core.ErrorResponse; // for RawError in Case B
```

---

### 2F. How-To Reference (each named operation)

**Authorize-only (no capture):**
- `CreateOrder` with `body.Intent = CheckoutPaymentIntent.Authorize` + `body.PaymentSource.Card` (or `.Token`)
- Pass `prefer: "return=representation"` to get `PurchaseUnits[0].Payments.Authorizations` populated
- Extract and store: `order.PurchaseUnits[0].Payments.Authorizations[0].Id` (authorization ID)

**Capture an authorization:**
- `CaptureAuthorizedPayment(authorizationId: storedAuthId, ..., prefer: "return=representation")`
- Extract: `CapturedPayment.Id` (capture ID), `CapturedPayment.SellerReceivableBreakdown.*`

**Void an authorization:**
- `VoidPayment(authorizationId: storedAuthId, ...)` — MUST use named arguments (param order differs)

**Refund a capture (full):**
- `RefundCapturedPayment(captureId: storedCaptureId, ..., body: null, payPalRequestId: callerKey)`

**Refund a capture (partial):**
- `RefundCapturedPayment(captureId: storedCaptureId, ..., body: new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = partialAmount } }, payPalRequestId: callerKey)`
- Guard: validate `partialAmount <= storedCapturedAmount` before calling SDK

**List transactions for a date range (full range):**
```csharp
var allDetails = new List<TransactionDetails>();
int page = 1, totalPages = 1;
do {
    var resp = await client.TransactionSearch.SearchTransactions(
        startDate: from.ToString("o"),
        endDate: to.ToString("o"),
        transactionId: null, transactionType: null, transactionStatus: null,
        transactionAmount: null, transactionCurrency: null,
        paymentInstrumentType: null, storeId: null, terminalId: null,
        page: page, ct: ct);
    totalPages = resp.TotalPages ?? 1;
    if (resp.TransactionDetails != null) allDetails.AddRange(resp.TransactionDetails);
    page++;
} while (page <= totalPages);
```

**Vault/save a card:**
1. `CreateSetupToken(payPalRequestId: idempotencyKey, body: SetupTokenRequest { Customer = new Customer { Id = merchantCustomerId }, PaymentSource = new SetupTokenRequestPaymentSource { Card = new SetupTokenRequestCard { Number = pan, Expiry = "YYYY-MM", SecurityCode = cvv, Name = name, BillingAddress = addr } } })`
2. Extract `setupTokenId = SetupTokenResponse.Id`
3. `CreatePaymentToken(payPalRequestId: idempotencyKey2, body: PaymentTokenRequest { Customer = new Customer { Id = merchantCustomerId }, PaymentSource = new PaymentTokenRequestPaymentSource { Token = new VaultTokenRequest { Id = setupTokenId, Type = VaultTokenRequestType.SetupToken } } })`
4. Store `paymentMethodId = PaymentTokenResponse.Id`
5. Return safe descriptor from `PaymentTokenResponse.PaymentSource.Card` (`LastDigits`, `Brand`, `Expiry`)

**Charge a vaulted card:**
- `CreateOrder` with `body.PaymentSource = new PaymentSource { Token = new Token { Id = paymentMethodId, Type = TokenType.BillingAgreement } }` (UNVERIFIED wire value — see Section 2C)

**Renew a stale authorization:**
1. Before capture, call `GetAuthorizedPayment(authorizationId: storedAuthId, ...)` — parse `PaymentAuthorization.ExpirationTime` as `DateTimeOffset`
2. If expired (i.e. `ExpirationTime < DateTimeOffset.UtcNow`), call `ReauthorizePayment(authorizationId: storedAuthId, payPalRequestId: newKey, payPalAuthAssertion: null, body: new ReauthorizeRequest { Amount = new Money { CurrencyCode = currency, Value = amount } })`
3. If `ReauthorizePayment` throws `SdkException<ReauthorizePaymentError>` → surface as actionable operator error: "Authorization cannot be renewed; create a new order"
4. If success: update stored authorization ID to `PaymentAuthorization.Id` from the response
5. Proceed to `CaptureAuthorizedPayment` with the new authorization ID

---

### 2G. Idempotency Summary

| Operation | Idempotency Mechanism | Header Name (wire) |
|---|---|---|
| `CreateOrder` | `payPalRequestId` parameter | `PayPal-Request-Id` |
| `AuthorizeOrder` | `payPalRequestId` parameter | `PayPal-Request-Id` |
| `CaptureAuthorizedPayment` | `payPalRequestId` parameter | `PayPal-Request-Id` |
| `VoidPayment` | `payPalRequestId` parameter (4th nullable, must use named arg) | `PayPal-Request-Id` |
| `RefundCapturedPayment` | `payPalRequestId` parameter = caller-supplied key | `PayPal-Request-Id` |
| `ReauthorizePayment` | `payPalRequestId` parameter | `PayPal-Request-Id` |
| `CreateSetupToken` | `payPalRequestId` parameter | `PayPal-Request-Id` |
| `CreatePaymentToken` | `payPalRequestId` parameter | `PayPal-Request-Id` |

**Refund idempotency rules:**
- Same `payPalRequestId` on `RefundCapturedPayment` → same `Refund` returned (no double-refund)
- Different `payPalRequestId` on same `captureId` → new refund (partial refunds accumulate)
- The caller must supply the key; eShop stores the mapping `(callerKey → refundId)` to detect replay before hitting the SDK

---

## 3. Trap Notes

All traps below point to companion skills that must be loaded before implementing the named step.
Do **not** resolve these inline — load the skill; it carries defaults, examples, and the parts a
one-line note cannot.

> Step 1 (client registration) — the SDK's `HttpClient` lifetime must be managed via `IHttpClientFactory`
> and not rebuilt per request; a naively registered `new PayPalServerSdkClient(new HttpClient(), ...)` per
> request creates socket exhaustion under load. **MUST load `dotnet-client-initialization`** before wiring
> the client into the DI container.

> Step 1 (credentials) — `OAuth2ClientCredentials` construction: property names, whether secrets are
> read from `IConfiguration` at startup or lazily, and how the SDK refreshes tokens automatically are
> not visible in the map. **MUST load `dotnet-authentication`** before writing the credentials block.

> Step 1 (`PayPal:BaseUrl` override) — `options.Server` is of type `ServerOptions`; the exact property
> name for the base-URI override is not in the map. `PayPal:Environment` maps only to `ServerEnvironment.Sandbox`
> (the only environment the map documents); production requires `PayPal:BaseUrl` via `options.Server`.
> **MUST load `dotnet-configuration-resilience`** before wiring the base URL or tuning retry/timeout.

> Steps 3–8 (retry/timeout) — `RetryOptions.HttpMethodsToRetry` gates only the status-code trigger;
> transport failures (`HttpRequestException`) are retried on **every** verb including `POST`, so a
> non-idempotent write (e.g., a authorize without an idempotency key) can execute more than once.
> `RetryOptions.Timeout` is per-attempt, not a total-call budget. **MUST load
> `dotnet-configuration-resilience`** before configuring retry.

> Steps 3, 4, 11 (named arguments) — `CreateOrder`, `AuthorizeOrder`, `CaptureAuthorizedPayment`, and
> `RefundCapturedPayment` each have 4–5 must-pass-explicitly nullable parameters between the required
> params; positional calls bind to the wrong slots silently. `VoidPayment` has `payPalRequestId` as
> the 4th nullable (not the 2nd). **MUST load `dotnet-calling-endpoints`** before writing any SDK call.

> Steps 3–13 (error boundary) — 39 of 40 operations are Case A (typed `SdkException<{Op}Error>`) but
> `SearchTransactions` is Case B (`SdkException<RawError>`). The Vault operations use `TryGetError1` /
> `Error1` not `TryGetError` / `Error`. A single catch ladder does not cover both. **MUST load
> `dotnet-error-handling`** before writing the error boundary.

> Steps 3–13 (enum construction) — SDK enums are `StringEnum<T>` records, not C# enums; `(CheckoutPaymentIntent)"AUTHORIZE"` will not compile. Use `CheckoutPaymentIntent.Authorize`. **MUST load `dotnet-models`** before constructing any enum or union.

> Steps 5, 6 (stale authorization) — `PaymentAuthorization.ExpirationTime` is an ISO-8601 string; parse
> with `DateTimeOffset.Parse` for comparison. `ReauthorizePayment` only works days 4–29; a 30+-day-old
> authorization throws and the error must be surfaced as an actionable operator message, not a 5xx.
> Check `TryGetError(out Error e)` → `e.Details[].Issue` to distinguish expired-window vs other 422 causes.

> Step 7 (void param order) — `VoidPayment`'s nullable parameters are ordered: `payPalMockResponse`,
> `payPalAuthAssertion`, `payPalRequestId` — the idempotency key is the **3rd** nullable (4th total
> param), not the 2nd. Positional call puts it in `payPalAuthAssertion`. Always use named arguments.

> Step 10 (SearchTransactions pagination) — the operation signature shows no `perPage` shorthand; use
> `page:` / `pageSize:` named arguments. `TotalPages` in `SearchResponse` is nullable — null-guard before
> the loop (`totalPages = resp.TotalPages ?? 1`). `SearchTransactions` is Case B; its catch block uses
> `SdkException<RawError>`, not `SdkException<{Op}Error>`.

> Step 11 (vault two-step) — `CreateSetupToken` for a direct card does not require a browser redirect;
> `SetupTokenResponse.Status` should be `Created` immediately. If `Status` is `PayerActionRequired`,
> the card requires 3DS — the integration must handle or reject this case. The `CreatePaymentToken` step
> must use a **different** `payPalRequestId` from `CreateSetupToken`.

> All steps (testing) — the SDK's test seam is the `HttpClient` constructor argument; mock at the
> `HttpMessageHandler` level. **MUST load `dotnet-testing`** before writing SDK-touching unit or
> integration tests.

---

## 4. REQUIRED READING

Load every skill below **before implementation starts** on the step it governs. This contract sheet
deliberately does not carry their contents — each skill holds defaults, worked examples, gotchas, and
mechanics a one-liner cannot convey. Loading after code is written defeats the purpose.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — DI registration, HttpClient lifetime, `AddPayPalServerSdkClient` options shape |
| `dotnet-authentication` | Step 1 — `OAuth2ClientCredentials` construction, token-refresh mechanics, reading secrets from `IConfiguration` |
| `dotnet-calling-endpoints` | Steps 3–13 — named-argument discipline, must-pass-explicitly pattern, `prefer` header, async/cancellation |
| `dotnet-models` | Steps 3–13 — `StringEnum<T>` construction, `init`-only record initializer, nullable handling, `IReadOnlyList<T>` |
| `dotnet-error-handling` | Steps 3–13 — Case A vs B mechanics, `TryGet…` accessor pattern, `TryGetError1` for Vault, `JsonException` boundary rules (see below) |
| `dotnet-configuration-resilience` | Step 1 — base-URL override via `ServerOptions`, retry/timeout semantics, per-attempt vs total budget |
| `dotnet-testing` | All steps — `HttpMessageHandler` mock seam, record construction in tests |

**`JsonException` boundary hazards — both rows are mandatory:**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException`
  from deserialization, **not** as an `SdkException` — an SDK-exception-only catch ladder lets it
  escape the integration boundary.

- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the error boundary.

---

## 5. Assumptions & Blockers

| # | Item |
|---|---|
| A1 | The eShop assigns each shopper a stable `merchantCustomerId` (string) usable as the vault `Customer.Id`. If no such ID exists, vault association must be deferred or the customer entity created at vault time. |
| A2 | `PayPal:Currency` configuration key holds the ISO-4217 currency code (e.g. `"USD"`) used in all `Money` and `AmountWithBreakdown` instances. Currency mismatches between the eShop order total and the PayPal amount cause 422 errors. |
| A3 | `ServerEnvironment` exposes only `Sandbox` in the SDK map. Production requires a base-URL override via `options.Server` (exact property name: load `dotnet-configuration-resilience`). `PayPal:Environment` config should be mapped to environment selection logic at startup, not at call-time. |
| A4 | `TokenType.BillingAgreement` is the SDK's only `TokenType` value; its use for vault payment tokens in `CreateOrder.PaymentSource.Token` is marked UNVERIFIED — confirm via sandbox roundtrip. If PayPal rejects it, fall back to `CardRequest.VaultId = paymentTokenId` instead of `PaymentSource.Token`. |
| A5 | The reconciliation endpoint's `from`/`to` query params are ISO-8601 date-times; pass them directly as `startDate`/`endDate` to `SearchTransactions`. PayPal requires these in a specific format; if the API returns 400, apply `DateTimeOffset.Parse(from).ToString("yyyy-MM-ddTHH:mm:sszzz")` formatting. |
| A6 | Partial refund guard (partial amount ≤ captured amount) is enforced in eShop code before the SDK call. The eShop must store the gross captured amount (from `CapturedPayment.SellerReceivableBreakdown.GrossAmount.Value`) at fulfil time for this comparison. |
| A7 | Authorization renewal window (days 4–29) and the 30-day hard limit are PayPal's contract, not the SDK's. The integration cannot determine the window from the SDK response alone — check `PaymentAuthorization.ExpirationTime` and handle `ReauthorizePayment` errors as operator-actionable. |
