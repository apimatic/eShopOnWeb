# PayPal Integration Plan — eShopOnWeb

---

## 1. Scope & Sequence

| Step | What | Operations used |
|---|---|---|
| 1 | Install SDK; wire client + credentials in DI | `AddPayPalServerSdkClient` |
| 2 | Write error boundary (wraps ALL SDK calls) | — |
| 3 | Create order with AUTHORIZE intent (direct card OR vaulted card) | `Orders.CreateOrder` |
| 4 | Detect browser-redirect requirement; surface error if `PAYER_ACTION_REQUIRED` | — |
| 5 | Authorize the order (no card re-supply needed if already in CreateOrder) | `Orders.AuthorizeOrder` |
| 6 | Capture authorization at fulfilment | `Payments.CaptureAuthorizedPayment` |
| 7 | Void authorization on cancel (before capture) | `Payments.VoidPayment` |
| 8 | Refund a captured payment (full or partial) | `Payments.RefundCapturedPayment` |
| 9 | Handle stale authorization: inspect expiry, re-authorize or surface error | `Payments.GetAuthorizedPayment`, `Payments.ReauthorizePayment` |
| 10 | Vault a card (store token + display info; never raw PAN) | `Vault.CreatePaymentToken` |
| 11 | List vault tokens for a customer | `Vault.ListCustomerPaymentTokens` |
| 12 | Delete a vault token | `Vault.DeletePaymentToken` |
| 13 | Transaction reconciliation — full paginated listing for a date range | `TransactionSearch.SearchTransactions` |

Steps 3–5 handle all three payment modes: new direct card, vaulted card, and future card types — controlled by which fields are set on `CardRequest`. Steps 6–9 are payment-source-agnostic (they act on authorization/capture IDs, not cards).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2a. Required `using` directives

| Contents | Namespace |
|---|---|
| Client, options, DI | `PayPalServerSdk` |
| Operation controllers | `PayPalServerSdk.Api` |
| Request/response records | `PayPalServerSdk.Models` |
| Enums (`CheckoutPaymentIntent`, `OrderStatus`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus`, `StoreInVaultInstruction`, etc.) | `PayPalServerSdk.Models.Enums` |
| Error classes (`AuthorizeOrderError`, `CaptureAuthorizedPaymentError`, `Error`, `Error1`, etc.) | `PayPalServerSdk.Errors` |
| `ServerEnvironment` | `PayPalServerSdk.Servers` |
| `SdkException<T>`, `RawError`, `ApiError` | See `dotnet-error-handling` for the exact `using` — these live under `Core/` |

Source: `sdk-map.md` (namespaces table) + `map/operations/*.md`.

---

### 2b. Client construction & auth

| Property | Type | Notes |
|---|---|---|
| `Environment` | `ServerEnvironment` | `ServerEnvironment.Sandbox` for sandbox. **UNVERIFIED**: only `Sandbox` is listed in the map (`Servers/ServerEnvironment.cs`). Whether a `Production` member exists or whether production is selected via `ServerEnvironment.FromValue(...)` requires confirming against the SDK source or `dotnet-configuration-resilience`. |
| `Oauth2` | `OAuth2ClientCredentials?` | Set `ClientId` / `ClientSecret` from `PayPal:ClientId` / `PayPal:ClientSecret` config. |
| `Oauth2TokenStrategy` | `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | Leave null to use the SDK's default token fetch. |
| `Server` | `ServerOptions` | Override base URL here when `PayPal:BaseUrl` is set. Exact `ServerOptions` shape: MUST load `dotnet-configuration-resilience`. |
| `Retry` | `RetryOptions` | Tune retries and timeout here. MUST load `dotnet-configuration-resilience`. |

DI registration: `services.AddPayPalServerSdkClient(o => { ... });` (`ServiceCollectionExtensions.cs`).

Source: `sdk-map.md` (*Getting a client*, *Servers & auth*).

---

### 2c. Operations

#### OP-1: CreateOrder — create order with AUTHORIZE intent + card payment source

Controller: `client.Orders` · Source: `map/operations/Orders.md`

**Signature:**
```
CreateOrder(
    string? payPalMockResponse,       // null (sandbox mock header — skip in prod)
    string? payPalRequestId,          // IDEMPOTENCY KEY — caller-supplied UUID per logical create
    string? payPalPartnerAttributionId, // null
    string? payPalClientMetadataId,   // null
    string? payPalAuthAssertion,      // null
    OrderRequest body,                // !req — see request model below
    string? prefer = "return=minimal",// pass "return=representation" to get full response
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
All five nullable params before `body` have no C# default — **must pass explicitly** (use `null` to skip).

**Request model** `OrderRequest` (`Models/OrderRequest.cs`, namespace `PayPalServerSdk.Models`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Intent (intent)` | `CheckoutPaymentIntent !req` | `CheckoutPaymentIntent.Authorize` (wire: `AUTHORIZE`) |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest> !req` | At least one element |
| `PaymentSource (payment_source)` | `PaymentSource?` | Set `Card` for direct/vaulted card processing |
| `Payer (payer)` | `Payer?` | Optional |
| `ApplicationContext (application_context)` | `OrderApplicationContext?` | Optional |

**`PurchaseUnitRequest`** fields used (`Models/PurchaseUnitRequest.cs`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `AmountWithBreakdown !req` | |
| `CustomId (custom_id)` | `string?` | Put eShop order ID here for reconciliation |
| `InvoiceId (invoice_id)` | `string?` | Optional alternate reference |
| `ReferenceId (reference_id)` | `string?` | Multi-purchase-unit discriminator; PayPal uses `"default"` if omitted and there is one unit |

**`AmountWithBreakdown`** (`Models/AmountWithBreakdown.cs`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `CurrencyCode (currency_code)` | `string !req` | e.g. `"USD"` from `PayPal:Currency` config |
| `Value (value)` | `string !req` | Decimal string, e.g. `"99.99"` |

**`PaymentSource`** (`Models/PaymentSource.cs`) — set only the `Card` property:

| Field (wire name) | Type |
|---|---|
| `Card (card)` | `CardRequest?` |

**`CardRequest`** (`Models/CardRequest.cs`) — for **new direct card**:

| Field (wire name) | Type | Notes |
|---|---|---|
| `Name (name)` | `string?` | Cardholder name |
| `Number (number)` | `string?` | PAN — e.g. `"4111111111111111"` (sandbox Visa) |
| `Expiry (expiry)` | `string?` | `YYYY-MM` format |
| `SecurityCode (security_code)` | `string?` | CVC |
| `BillingAddress (billing_address)` | `Address?` | |

For **vaulted card** — set only `VaultId`; do NOT set `Number`/`SecurityCode`:

| Field (wire name) | Type | Notes |
|---|---|---|
| `VaultId (vault_id)` | `string?` | Payment token ID from vault (returned by OP-10) |

**`Address`** (`Models/Address.cs`):

| Field (wire name) | Type |
|---|---|
| `CountryCode (country_code)` | `string !req` |
| `AddressLine1 (address_line_1)` | `string?` |
| `AddressLine2 (address_line_2)` | `string?` |
| `AdminArea1 (admin_area_1)` | `string?` — state/province |
| `AdminArea2 (admin_area_2)` | `string?` — city |
| `PostalCode (postal_code)` | `string?` |

**Returns:** `Order` (`Models/Order.cs`)

| Field (wire name) | Type | Read for |
|---|---|---|
| `Id (id)` | `string?` | PayPal order ID — store in DB |
| `Status (status)` | `OrderStatus?` | **Check immediately** — see OP-1 status logic below |

**No-redirect detection (CRITICAL):** After `CreateOrder`, check `order.Status`:
- `OrderStatus.PayerActionRequired` (wire: `PAYER_ACTION_REQUIRED`) → **surface error**: "This card requires browser approval and cannot be used for direct payment. Ask the payer to use a different card or payment method."
- Any status other than `PayerActionRequired` that prevents proceeding → treat as error.

**Error:** `SdkException<CreateOrderError>` — Case A
- `TryGetError(out Error)` for HTTP 400, 401, 422
- `TryGetRawError(out RawError)` fallback

---

#### OP-2: AuthorizeOrder — authorize a created order (put hold on funds)

Controller: `client.Orders` · Source: `map/operations/Orders.md`

**Signature:**
```
AuthorizeOrder(
    string id,                        // PayPal order ID from OP-1 response
    string? payPalMockResponse,       // null
    string? payPalRequestId,          // IDEMPOTENCY KEY — same key as OP-1 for double-click safety,
                                      // or a new key scoped to this authorize call
    string? payPalClientMetadataId,   // null
    string? payPalAuthAssertion,      // null
    OrderAuthorizeRequest? body,      // null when card was supplied in CreateOrder
    string? prefer = "return=minimal",// **pass "return=representation"** to get auth ID in response
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

**Request model** `OrderAuthorizeRequest` (`Models/OrderAuthorizeRequest.cs`):
- `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` — pass `null` when card was already provided in `CreateOrder`. Only supply here if card was NOT in `CreateOrder`.

**Returns:** `OrderAuthorizeResponse` (`Models/OrderAuthorizeResponse.cs`)

| Field (wire name) | Type | Read for |
|---|---|---|
| `Id (id)` | `string?` | PayPal order ID (confirm) |
| `Status (status)` | `OrderStatus?` | Check for `PayerActionRequired` — if set, surface error |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnit>?` | Navigate to auth ID (requires `prefer="return=representation"`) |

**Authorization ID extraction path** (with `prefer="return=representation"`):
```
response.PurchaseUnits[0]
    .Payments                                       // PaymentCollection
    .Authorizations[0]                              // AuthorizationWithAdditionalData
    .Id                                             // string? — authorization ID — STORE IN DB
```

**Expiry extraction path** (same nav):
```
response.PurchaseUnits[0].Payments.Authorizations[0].ExpirationTime   // string? ISO-8601 — STORE IN DB
```

**Auth status**:
```
response.PurchaseUnits[0].Payments.Authorizations[0].Status   // AuthorizationStatus?
```
Expect `AuthorizationStatus.Created` (wire: `CREATED`) on success.

> If `prefer = "return=minimal"` (default), `PurchaseUnits` is absent — you will not be able to extract the authorization ID from the response. **Always pass `prefer: "return=representation"`** for this call.

**Error:** `SdkException<AuthorizeOrderError>` — Case A
- `TryGetError(out Error)` for HTTP 400, 401, 403, 404, 422, 500
- `TryGetRawError(out RawError)` fallback

---

#### OP-3: CaptureAuthorizedPayment — take the money at fulfilment

Controller: `client.Payments` · Source: `map/operations/Payments.md`

**Signature:**
```
CaptureAuthorizedPayment(
    string authorizationId,           // authorization ID from OP-2 — stored in DB
    string? payPalMockResponse,       // null
    string? payPalRequestId,          // IDEMPOTENCY KEY — per capture attempt
    string? payPalAuthAssertion,      // null
    CaptureRequest? body,             // null for full capture, or supply Amount for partial
    string? prefer = "return=minimal",// **pass "return=representation"** to get fee breakdown
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

**Request model** `CaptureRequest` (`Models/CaptureRequest.cs`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | Null = full capture. Set for partial captures. |
| `FinalCapture (final_capture)` | `bool? = false` | Set `true` to prevent further partial captures |
| `InvoiceId (invoice_id)` | `string?` | Optional reference |
| `NoteToPayer (note_to_payer)` | `string?` | Optional |

**Returns:** `CapturedPayment` (`Models/CapturedPayment.cs`)

| Field (wire name) | Type | Read for |
|---|---|---|
| `Id (id)` | `string?` | Capture ID — STORE IN DB for refund |
| `Status (status)` | `CaptureStatus?` | Expect `CaptureStatus.Completed` |
| `Amount (amount)` | `Money?` | Captured amount |
| `SellerReceivableBreakdown (seller_receivable_breakdown)` | `SellerReceivableBreakdown?` | Fee and net — requires `prefer="return=representation"` |

**`SellerReceivableBreakdown`** fields (`Models/SellerReceivableBreakdown.cs`):

| Field (wire name) | Type | Read for |
|---|---|---|
| `GrossAmount (gross_amount)` | `Money !req` | Gross captured amount |
| `PaypalFee (paypal_fee)` | `Money?` | PayPal transaction fee |
| `NetAmount (net_amount)` | `Money?` | Net proceeds (gross minus fee) |

**`Money`** (`Models/Money.cs`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`

**HTTP 409 on capture = authorization expired or already captured.** Handle `TryGetError(out Error)` for 409 — when this is an expiry case, proceed to OP-6 (re-authorize).

**Error:** `SdkException<CaptureAuthorizedPaymentError>` — Case A
- `TryGetError(out Error)` for HTTP 400, 401, 403, 404, 409, 422
- `TryGetNoContent(out RawError)` for HTTP 500
- `TryGetRawError(out RawError)` fallback

---

#### OP-4: VoidPayment — release held funds on cancel

Controller: `client.Payments` · Source: `map/operations/Payments.md`

**Signature:**
```
VoidPayment(
    string authorizationId,           // authorization ID — stored in DB
    string? payPalMockResponse,       // null
    string? payPalAuthAssertion,      // null
    string? payPalRequestId,          // null (void is idempotent by design; no key needed)
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
Note parameter ORDER: `payPalMockResponse`, then `payPalAuthAssertion`, then `payPalRequestId`. This differs from other operations — do NOT swap them.

**Returns:** `PaymentAuthorization` (`Models/PaymentAuthorization.cs`) — with `prefer="return=minimal"` the body may be minimal. Check status if needed.

**Error:** `SdkException<VoidPaymentError>` — Case A
- `TryGetError(out Error)` for HTTP 401, 403, 404, 409, 422
- `TryGetNoContent(out RawError)` for HTTP 500
- `TryGetRawError(out RawError)` fallback

**409 on void** = already voided, already captured, or auth already completed. Treat as idempotent success or surface specific message from `Error.Message`.

---

#### OP-5: RefundCapturedPayment — partial or full refund

Controller: `client.Payments` · Source: `map/operations/Payments.md`

**Signature:**
```
RefundCapturedPayment(
    string captureId,                 // capture ID from OP-3 — stored in DB
    string? payPalMockResponse,       // null
    string? payPalRequestId,          // PER-REFUND IDEMPOTENCY KEY — caller-supplied, unique per refund
    string? payPalAuthAssertion,      // null
    RefundRequest? body,              // null = full refund; supply Amount for partial
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

**Idempotency:** `payPalRequestId` is the per-refund key. The same key can never produce a second refund (PayPal enforces deduplication). For two sequential partial refunds, use two different keys. A 422 with the same key after a successful first call is a duplicate — treat as idempotent.

**Request model** `RefundRequest` (`Models/RefundRequest.cs`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | Null = full refund; partial refund = set `Value` |
| `CustomId (custom_id)` | `string?` | Optional internal reference |
| `NoteToPayer (note_to_payer)` | `string?` | Optional |

**Returns:** `Refund` (`Models/Refund.cs`)

| Field (wire name) | Type | Read for |
|---|---|---|
| `Id (id)` | `string?` | Refund ID |
| `Status (status)` | `RefundStatus?` | `RefundStatus.Completed` = success |
| `Amount (amount)` | `Money?` | Amount refunded |
| `SellerPayableBreakdown (seller_payable_breakdown)` | `SellerPayableBreakdown?` | Fee detail |

**`SellerPayableBreakdown`** (`Models/SellerPayableBreakdown.cs`):
`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount` — all `Money?`.

**409 on refund** = refund for this key already exists (duplicate call with same `payPalRequestId`). Treat as idempotent; do NOT retry with a different key for the same intended refund.

**Error:** `SdkException<RefundCapturedPaymentError>` — Case A
- `TryGetError(out Error)` for HTTP 400, 401, 403, 404, 409, 422
- `TryGetNoContent(out RawError)` for HTTP 500
- `TryGetRawError(out RawError)` fallback

---

#### OP-6: GetAuthorizedPayment — inspect authorization before re-authorize attempt

Controller: `client.Payments` · Source: `map/operations/Payments.md`

**Signature:**
```
GetAuthorizedPayment(
    string authorizationId,
    string? payPalMockResponse,       // null
    string? payPalAuthAssertion,      // null
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

**Returns:** `PaymentAuthorization` (`Models/PaymentAuthorization.cs`)

| Field (wire name) | Type | Read for |
|---|---|---|
| `Status (status)` | `AuthorizationStatus?` | Current auth state |
| `ExpirationTime (expiration_time)` | `string?` | ISO-8601 UTC — compare to `DateTime.UtcNow` |
| `Id (id)` | `string?` | Confirm ID |

**`AuthorizationStatus` enum** (`Models/Enums/AuthorizationStatus.cs`):
- `Created (CREATED)` — active, capturable
- `PartiallyCaptured (PARTIALLY_CAPTURED)` — partial capture taken
- `Captured (CAPTURED)` — fully captured
- `Voided (VOIDED)` — voided or expired
- `Pending (PENDING)` — pending review
- `Denied (DENIED)` — denied

**There is NO `Expired` member** in `AuthorizationStatus`. Expiry is detected by comparing `ExpirationTime` (string → `DateTime.Parse`) to `DateTime.UtcNow`. A voided or pending+expired authorization will show `Voided` or `Pending` status and an `ExpirationTime` in the past.

**Stale auth handling logic:**
1. If `ExpirationTime` is in the past (or `Status == Voided`): attempt `ReauthorizePayment` (OP-7).
2. If re-auth also fails (ReauthorizePayment throws): catch the error, read `Error.Message`, surface a clear, non-silent error ("Authorization expired and re-authorization failed — order cannot proceed").

**Error:** `SdkException<GetAuthorizedPaymentError>` — Case A
- `TryGetError(out Error)` for HTTP 401, 403, 404
- `TryGetNoContent(out RawError)` for HTTP 500
- `TryGetRawError(out RawError)` fallback

---

#### OP-7: ReauthorizePayment — re-authorize an expired authorization

Controller: `client.Payments` · Source: `map/operations/Payments.md`

**Signature:**
```
ReauthorizePayment(
    string authorizationId,           // original (stale) auth ID
    string? payPalRequestId,          // IDEMPOTENCY KEY — new key for this re-auth attempt
    string? payPalAuthAssertion,      // null
    ReauthorizeRequest? body,         // supply Amount to keep same amount
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

**Request model** `ReauthorizeRequest` (`Models/ReauthorizeRequest.cs`):
- `Amount (amount): Money?` — the amount to re-authorize; supply same amount as original.

**Window:** PayPal allows re-authorization only between day 4 and day 29 after original authorization. After 30 days, re-authorization is impossible and a new CreateOrder/AuthorizeOrder flow is required.

**Returns:** `PaymentAuthorization` — contains the NEW authorization ID in `Id`. Store the new auth ID in DB, replacing the old one.

**Error:** `SdkException<ReauthorizePaymentError>` — Case A
- `TryGetError(out Error)` for HTTP 400, 401, 403, 404, 422 — these indicate re-auth is impossible
- `TryGetNoContent(out RawError)` for HTTP 500
- `TryGetRawError(out RawError)` fallback
- On 422: re-auth is not possible (authorization too old or wrong state) → surface clear error, do NOT silent-fail.

---

#### OP-8: CreatePaymentToken — vault a card

Controller: `client.Vault` · Source: `map/operations/Vault.md`

**Signature:**
```
CreatePaymentToken(
    string? payPalRequestId,          // IDEMPOTENCY KEY — per vault request
    PaymentTokenRequest body,         // !req
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

**Request model** `PaymentTokenRequest` (`Models/PaymentTokenRequest.cs`):

| Field (wire name) | Type |
|---|---|
| `PaymentSource (payment_source)` | `PaymentTokenRequestPaymentSource !req` |
| `Customer (customer)` | `Customer?` — supply to associate token with a PayPal customer ID |

**`PaymentTokenRequestPaymentSource`** (`Models/PaymentTokenRequestPaymentSource.cs`):
- `Card (card): PaymentTokenRequestCard?`

**`PaymentTokenRequestCard`** (`Models/PaymentTokenRequestCard.cs`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Name (name)` | `string?` | Cardholder name |
| `Number (number)` | `string?` | Full PAN — app does NOT store this after the call |
| `Expiry (expiry)` | `string?` | `YYYY-MM` |
| `SecurityCode (security_code)` | `string?` | CVC — app does NOT store this |
| `BillingAddress (billing_address)` | `Address?` | |
| `Brand (brand)` | `CardBrand?` | Optional hint |

**`Customer`** (`Models/Customer.cs`):
- `Id (id): string?` — PayPal customer ID (if you have one from a previous vaulting)
- `MerchantCustomerId (merchant_customer_id): string?` — your internal customer ID (eShop user ID)

**Returns:** `PaymentTokenResponse` (`Models/PaymentTokenResponse.cs`)

| Field (wire name) | Type | Store in app DB |
|---|---|---|
| `Id (id)` | `string?` | Vault token ID — store this |
| `Customer (customer)` | `CustomerResponse?` | `.Id` = PayPal customer ID — store for listing |
| `PaymentSource (payment_source)` | `PaymentTokenResponsePaymentSource?` | Navigate to card display info |

**Display info extraction path:**
```
response.PaymentSource.Card.LastDigits   // string? — store for display ("ending in 1111")
response.PaymentSource.Card.Brand        // CardBrand? — e.g. CardBrand.Visa
response.PaymentSource.Card.Expiry       // string? — "YYYY-MM"
```

`PaymentTokenResponsePaymentSource` → `Card` → `CardPaymentTokenEntity` (`Models/CardPaymentTokenEntity.cs`).

**App stores:** vault token `Id`, `LastDigits`, `Brand`, `Expiry`, and PayPal customer `Id`. Never stores `Number` or `SecurityCode`.

**Error:** `SdkException<CreatePaymentTokenError>` — Case A
- `TryGetError1(out Error1)` for HTTP 400, 403, 404, 422, 500
- `TryGetRawError(out RawError)` fallback

Note: Vault controller uses `Error1` (not `Error`) in its error accessors. `Error1` has same shape as `Error` but `Details` is `IReadOnlyList<ErrorDetails1>` and `Links` is `IReadOnlyList<ErrorLinkDescription>`.

---

#### OP-9: ListCustomerPaymentTokens — retrieve saved cards for a customer

Controller: `client.Vault` · Source: `map/operations/Vault.md`

**Signature:**
```
ListCustomerPaymentTokens(
    string customerId,                // PayPal customer ID (from OP-8 response.Customer.Id)
    int? pageSize = 5,
    int? page = 1,
    bool? totalRequired = false,      // **pass true** to get TotalPages for multi-page iteration
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Wire names: `customer_id`, `page_size`, `page`, `total_required`.

**Returns:** `CustomerVaultPaymentTokensResponse` (`Models/CustomerVaultPaymentTokensResponse.cs`)

| Field (wire name) | Type | Notes |
|---|---|---|
| `PaymentTokens (payment_tokens)` | `IReadOnlyList<PaymentTokenResponse>?` | Tokens for this page |
| `TotalPages (total_pages)` | `int?` | Only populated when `totalRequired = true` |
| `TotalItems (total_items)` | `int?` | Only populated when `totalRequired = true` |
| `Customer (customer)` | `VaultResponseCustomer?` | Customer info |

**Pagination pattern:**
```csharp
var allTokens = new List<PaymentTokenResponse>();
int page = 1;
int totalPages;
do {
    var resp = await client.Vault.ListCustomerPaymentTokens(
        customerId: customerId, pageSize: 20, page: page, totalRequired: true, ct: ct);
    if (resp.PaymentTokens is not null) allTokens.AddRange(resp.PaymentTokens);
    totalPages = resp.TotalPages ?? 1;
    page++;
} while (page <= totalPages);
```
`totalRequired` **must be `true`** to get `TotalPages`; default `false` leaves it null.

**Error:** `SdkException<ListCustomerPaymentTokensError>` — Case A
- `TryGetError1(out Error1)` for HTTP 400, 403, 500
- `TryGetRawError(out RawError)` fallback

---

#### OP-10: DeletePaymentToken — remove a saved card

Controller: `client.Vault` · Source: `map/operations/Vault.md`

**Signature:**
```
DeletePaymentToken(
    string id,                        // vault token ID
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

**Returns:** `void` (Task).

**Error:** `SdkException<DeletePaymentTokenError>` — Case A
- `TryGetError1(out Error1)` for HTTP 400, 403, 500
- `TryGetRawError(out RawError)` fallback

---

#### OP-11: SearchTransactions — reconciliation / paginated listing

Controller: `client.TransactionSearch` · Source: `map/operations/TransactionSearch.md`

**Signature:**
```
SearchTransactions(
    string startDate,                 // ISO-8601, e.g. "2024-01-01T00:00:00-0700"
    string endDate,                   // ISO-8601, e.g. "2024-01-31T23:59:59-0700"
    string? transactionId,            // null — filter by specific transaction
    string? transactionType,          // null
    string? transactionStatus,        // null
    string? transactionAmount,        // null
    string? transactionCurrency,      // null
    string? paymentInstrumentType,    // null
    string? storeId,                  // null
    string? terminalId,               // null
    string? fields = "transaction_info",  // pass "all" for full detail
    string? balanceAffectingRecordsOnly = "Y",
    int? pageSize = 100,
    int? page = 1,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
The 8 params `transactionId` through `terminalId` have no C# default — **must pass explicitly** (use `null` to skip). Wire names follow `snake_case` (see map: `start_date`, `end_date`, `transaction_id`, etc.).

**Returns:** `SearchResponse` (`Models/SearchResponse.cs`)

| Field (wire name) | Type | Notes |
|---|---|---|
| `TransactionDetails (transaction_details)` | `IReadOnlyList<TransactionDetails>?` | Items on this page |
| `Page (page)` | `int?` | Current page number |
| `TotalPages (total_pages)` | `int?` | Total pages — use for loop termination |
| `TotalItems (total_items)` | `int?` | |

**`TransactionDetails`** → `TransactionInfo (transaction_info): TransactionInformation?`

**`TransactionInformation`** key fields (`Models/TransactionInformation.cs`):

| Field (wire name) | Type | Meaning |
|---|---|---|
| `TransactionId (transaction_id)` | `string?` | PayPal transaction ID |
| `TransactionAmount (transaction_amount)` | `Money?` | Transaction amount |
| `FeeAmount (fee_amount)` | `Money?` | PayPal fee |
| `TransactionStatus (transaction_status)` | `string?` | Status string (e.g. `"S"` = success) |
| `TransactionInitiationDate (transaction_initiation_date)` | `string?` | ISO-8601 timestamp |
| `TransactionUpdatedDate (transaction_updated_date)` | `string?` | ISO-8601 |
| `InvoiceId (invoice_id)` | `string?` | Matches eShop order ID if set in `PurchaseUnitRequest.InvoiceId` |
| `CustomField (custom_field)` | `string?` | Also check if eShop order ID was put in `custom_id` |
| `PaypalReferenceId (paypal_reference_id)` | `string?` | Related order or authorization ID |

**Pagination pattern:**
```csharp
var allTxns = new List<TransactionDetails>();
int page = 1;
int totalPages;
do {
    var resp = await client.TransactionSearch.SearchTransactions(
        startDate: startDate, endDate: endDate,
        transactionId: null, transactionType: null, transactionStatus: null,
        transactionAmount: null, transactionCurrency: null,
        paymentInstrumentType: null, storeId: null, terminalId: null,
        fields: "all", balanceAffectingRecordsOnly: "Y",
        pageSize: 100, page: page, ct: ct);
    if (resp.TransactionDetails is not null) allTxns.AddRange(resp.TransactionDetails);
    totalPages = resp.TotalPages ?? 1;
    page++;
} while (page <= totalPages);
```

**Error:** `SdkException<RawError>` — **Case B** (this is the one Case-B operation in-scope)
```csharp
catch (SdkException<RawError> ex)
{
    var status = ex.Error.StatusCode;
    var body   = ex.Error.ReadAsString();  // or ReadAsJson<SearchError>() if shape is known
}
```
There are no `TryGet…` accessors — read status and body directly from `ex.Error`.

---

### 2d. Key Enum Values

Namespace: `PayPalServerSdk.Models.Enums` for all below.

| Enum | Members used | Wire values |
|---|---|---|
| `CheckoutPaymentIntent` | `Authorize` | `AUTHORIZE` |
| `OrderStatus` | `PayerActionRequired`, `Completed`, `Approved`, `Created` | `PAYER_ACTION_REQUIRED`, `COMPLETED`, `APPROVED`, `CREATED` |
| `AuthorizationStatus` | `Created`, `Voided`, `Captured`, `Denied`, `Pending`, `PartiallyCaptured` | `CREATED`, `VOIDED`, `CAPTURED`, `DENIED`, `PENDING`, `PARTIALLY_CAPTURED` |
| `CaptureStatus` | `Completed`, `Declined`, `Pending`, `Failed` | `COMPLETED`, `DECLINED`, `PENDING`, `FAILED` |
| `RefundStatus` | `Completed`, `Pending`, `Failed`, `Cancelled` | `COMPLETED`, `PENDING`, `FAILED`, `CANCELLED` |
| `CardBrand` | `Visa`, `Mastercard`, `Amex`, `Discover` | `VISA`, `MASTERCARD`, `AMEX`, `DISCOVER` |
| `StoreInVaultInstruction` | `OnSuccess` | `ON_SUCCESS` — used when vaulting inline during a payment |
| `ServerEnvironment` | `Sandbox` | see UNVERIFIED note in §2b |

Enums are `StringEnum<T>` — NOT C# enums. Use the static member (e.g. `CheckoutPaymentIntent.Authorize`) or `CheckoutPaymentIntent.FromValue("AUTHORIZE")`. Source: `map/models/enums.md`.

---

### 2e. Error payload types

Both `Error` and `Error1` share the same logical shape but differ in their `Details` and `Links` list types:

| Field (wire name) | Type |
|---|---|
| `Name (name)` | `string !req` — machine-readable error name |
| `Message (message)` | `string !req` — human-readable description |
| `DebugId (debug_id)` | `string !req` — PayPal correlation ID for support |
| `Details (details)` | `IReadOnlyList<ErrorDetails>?` (Error) / `IReadOnlyList<ErrorDetails1>?` (Error1) |

`ErrorDetails` field of interest: `Issue (issue): string !req`, `Description (description): string?`, `Field (field): string?`.

Source: `map/models/records-1-Ac-Pa.md`.

---

## 3. Trap Notes

⚠ **Steps 3 & 5 (CreateOrder, AuthorizeOrder): `prefer = "return=minimal"` is the default.** With the default, `PurchaseUnits` is absent from the response and the authorization ID cannot be extracted. Always pass `prefer: "return=representation"` for both calls, and for `CaptureAuthorizedPayment` (to get `SellerReceivableBreakdown`). **MUST load `dotnet-calling-endpoints`** before writing these calls — it covers how optional parameters bind and where named-argument order matters.

⚠ **Step 1 (client construction): `HttpClient` lifetime and `IHttpClientFactory` ownership** — the SDK client wraps an `HttpClient`; creating one per-request causes socket exhaustion. **MUST load `dotnet-client-initialization`** before wiring the client into the service container.

⚠ **Step 1 (credentials): set `Oauth2` on `PayPalServerSdkClientOptions` BEFORE constructing the client.** The token endpoint and token refresh strategy are not the same as the `HttpClient` timeout. **MUST load `dotnet-authentication`** before writing credential wiring — it covers when and how the SDK fetches and caches access tokens.

⚠ **Step 1 (resilience): `RetryOptions.Timeout` is per-attempt, not total.** `HttpMethodsToRetry` gates only the status-code retry trigger; a transport failure (`HttpRequestException`) retries on every verb, including POST — so `CreateOrder`, `AuthorizeOrder`, `CaptureAuthorizedPayment`, and `RefundCapturedPayment` can execute more than once if retries are enabled. For non-idempotent writes without `payPalRequestId`, this is dangerous. **MUST load `dotnet-configuration-resilience`** before tuning retries, timeouts, or the base-URL override.

⚠ **Step 1 (base URL override): `PayPal:BaseUrl` maps to `options.Server` (a `ServerOptions` object).** The exact shape of `ServerOptions` and how to set a custom base URL are not in the map. **MUST load `dotnet-configuration-resilience`** before wiring the base URL override.

⚠ **Steps 6 & 8 (error accessors): Vault controller uses `TryGetError1(out Error1)`, NOT `TryGetError(out Error)`.** The accessor and payload type differ from Orders/Payments. Do not mix them. Source: `map/operations/Vault.md`.

⚠ **Step 13 (SearchTransactions) is Case B** — the error type is `SdkException<RawError>`, not a typed error. There are no `TryGet…` accessors; read `.Error.StatusCode` and `.Error.ReadAsString()` directly. **MUST load `dotnet-error-handling`** to understand Case A vs Case B error boundaries before writing the catch ladder.

⚠ **Step 12 (error boundary): `JsonException` reaches the boundary from two directions with opposite meanings.** See the REQUIRED READING block below. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

⚠ **Step 10 (vault listing): `TotalPages` is null unless `totalRequired: true` is passed** to `ListCustomerPaymentTokens`. The default is `false`. A loop that reads `TotalPages` without setting this will loop zero times or throw a null-dereference. Pass `totalRequired: true` explicitly.

⚠ **Step 9 (stale auth / re-authorize): PayPal's window for re-authorization is day 4 through day 29 after original authorization.** Outside this window (either too soon or after 30 days), `ReauthorizePayment` fails. After 30 days, a full new CreateOrder+AuthorizeOrder is required. The 409/422 from `ReauthorizePayment` is a deterministic rejection, not a transient error — do NOT retry it. Source: `map/operations/Payments.md` (ReauthorizePayment notes).

⚠ **Steps 4–7 (VoidPayment parameter order): unlike other Payments operations, the third positional param is `payPalAuthAssertion` and the fourth is `payPalRequestId`** (reversed relative to `CaptureAuthorizedPayment` / `RefundCapturedPayment`). Always use named arguments to avoid silent mis-binding. **MUST load `dotnet-calling-endpoints`** before writing any call with optional parameters.

⚠ **Model instantiation: all record fields are `init`-only.** Use object initializer syntax; there are no setters. `required` fields (`!req`) must appear in the initializer or the build fails. **MUST load `dotnet-models`** when assembling any multi-field request model.

---

## 4. REQUIRED READING

Load ALL of the following skills before implementation starts. The contract sheet above deliberately does not carry their contents — each skill covers essential usage patterns, defaults, and hazards that a one-line summary cannot replace. Loading a skill after the relevant code is written means the hazard arrives too late to shape the implementation.

| Skill | Steps it governs |
|---|---|
| `dotnet-client-initialization` | Step 1: `AddPayPalServerSdkClient`, `HttpClient` lifetime, factory, DI registration |
| `dotnet-authentication` | Step 1: `OAuth2ClientCredentials`, token caching, credential rotation |
| `dotnet-calling-endpoints` | Steps 3–13: every SDK call — param order, named arguments, `ct:` token name, response envelopes |
| `dotnet-models` | Steps 3–13: `init`-only records, `StringEnum<T>` construction, `required` fields, nullable optionals |
| `dotnet-error-handling` | Step 2: error boundary; every operation's catch ladder; Case A vs Case B; `JsonException` directions |
| `dotnet-configuration-resilience` | Step 1: retries, per-attempt vs total timeout, base-URL override via `ServerOptions`, POST retry hazard |
| `dotnet-testing` | Throughout: how to stub the SDK's `HttpClient` seam; test doubles for each controller |

**`dotnet-error-handling` mandatory hazard rows (copy verbatim into the boundary design):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

| # | Item | Status |
|---|---|---|
| A1 | `PayPal:Environment = "production"` maps to a `ServerEnvironment` member. The map lists only `ServerEnvironment.Sandbox`. Whether a `Production` member exists or whether production requires `ServerEnvironment.FromValue("production")` is **UNVERIFIED** — must confirm from `Servers/ServerEnvironment.cs` in the SDK source before production deployment. | UNVERIFIED |
| A2 | "Direct card processing" means the card is supplied in `CreateOrder.PaymentSource.Card` or `AuthorizeOrder.body.PaymentSource.Card` rather than via a PayPal-hosted JS form. The app accepts PCI responsibility for transmitting raw card numbers to PayPal's API. The SDK does not add a hosted-fields layer; the integration must ensure TLS and comply with its PCI SAQ-D obligations. | Assumed |
| A3 | eShop order ID is carried through PayPal as `PurchaseUnitRequest.CustomId` (recommended) and/or `InvoiceId`. Both surface in `TransactionInformation` for reconciliation matching. | Assumed |
| A4 | The "customer ID" used for vault listing (`ListCustomerPaymentTokens.customerId`) is the PayPal customer `Id` returned in `PaymentTokenResponse.Customer.Id` after the first vault call, not the eShop user ID. The eShop must store this PayPal customer ID per user. | Assumed |
| A5 | `SearchTransactions` date range parameters are ISO-8601 strings including timezone offset (e.g. `"2024-01-01T00:00:00-0700"`). Exact format constraints are not in the map; the app should normalize to UTC (`+0000`) to avoid ambiguity. | Assumed |
| A6 | For the stale-auth re-authorize path, the app stores `ExpirationTime` from `AuthorizeOrder` response alongside the authorization ID. If `ExpirationTime` is null in the response (minimal prefer), the app cannot detect expiry without a `GetAuthorizedPayment` call. Pass `prefer="return=representation"` at authorize time to always get `ExpirationTime`. | Assumed |
