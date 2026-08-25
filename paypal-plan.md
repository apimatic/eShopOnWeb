# PayPal Integration Plan — eShopOnWeb

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it.** Records
> and enums live in different child namespaces and must each have their own `using`. The
> namespaces are:
> - `PayPalServerSdk` — client, options
> - `PayPalServerSdk.Servers` — `ServerEnvironment`
> - `PayPalServerSdk.Models` — all request/response records
> - `PayPalServerSdk.Models.Enums` — all `StringEnum<T>` enums
> - `PayPalServerSdk.Errors` — typed error classes
> - `PayPalServerSdk.Http` — `SdkException<T>` (verify namespace from `Core/` source if needed)

---

## 1. Scope & Sequence

| Step | What | PayPal SDK operations used |
|---|---|---|
| 1 | Install NuGet, configure DI | — |
| 2 | Auth & client options | OAuth2 client credentials |
| 3 | Entity: `OrderPayment` (new EF entity) and `Order` FK | — |
| 4 | `POST /api/orders` — place order (no PayPal call yet) | — |
| 5 | `POST /api/orders/{orderId}/pay` — authorize payment (inline card or vault ID) | `Orders.CreateOrder`, `Orders.AuthorizeOrder` |
| 6 | `POST /api/orders/{orderId}/fulfil` — capture; reauth if stale | `Payments.ReauthorizePayment`, `Payments.CaptureAuthorizedPayment` |
| 7 | `POST /api/orders/{orderId}/cancel` — void authorization | `Payments.VoidPayment` |
| 8 | `POST /api/orders/{orderId}/refunds` — refund captured payment | `Payments.RefundCapturedPayment` |
| 9 | `GET /api/my-orders` — list caller's orders with payment state | DB read only |
| 10 | `GET /api/reconciliation` — paginated transaction search | `TransactionSearch.SearchTransactions` |
| 11 | `POST /api/payment-methods` — vault a card | `Vault.CreatePaymentToken` |
| 12 | `GET /api/payment-methods` — list saved cards | `Vault.ListCustomerPaymentTokens` |
| 13 | `DELETE /api/payment-methods/{id}` — delete saved card | `Vault.DeletePaymentToken` |

---

## 2. CONTRACT SHEET

### Install

```
dotnet add package AsadAli.Checkout.Sdk
```

Install version-less (floats to latest release that this map documents — tag `v1.0.1`).
Target project: whichever project hosts the PayPal service layer (e.g. `src/Infrastructure`
or `src/PublicApi`).

---

### Servers & auth

| Item | Value | Source |
|---|---|---|
| Environment enum type | `PayPalServerSdk.Servers.ServerEnvironment` | `sdk-map.md` — Servers & auth |
| Sandbox member | `ServerEnvironment.Sandbox` | `sdk-map.md` — Servers & auth |
| Credentials property name | `Oauth2` on `PayPalServerSdkClientOptions` | `sdk-map.md` — Servers & auth |
| Credentials type | `OAuth2ClientCredentials` — namespace `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | source: `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs` |
| Token-strategy property | `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `sdk-map.md` — Servers & auth |
| Base-URL override point | `options.Server` (`ServerOptions`) | `sdk-map.md` — Getting a client |
| DI extension | `services.AddPayPalServerSdkClient(o => { … })` | `sdk-map.md` — Getting a client |

Config keys (from brief): `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`,
`PayPal:Currency`, `PayPal:BaseUrl` (optional override).

---

### Client construction

```csharp
services.AddPayPalServerSdkClient(o =>
{
    o.Environment = ServerEnvironment.Sandbox;   // or Production based on config
    o.Oauth2 = new OAuth2ClientCredentials
    {
        ClientId     = config["PayPal:ClientId"]!,
        ClientSecret = config["PayPal:ClientSecret"]!
    };
    // Optional base-URL override (PayPal:BaseUrl in config):
    // o.Server.Default.Sandbox.BaseUrl = config["PayPal:BaseUrl"];
});
```

`PayPalServerSdkClientOptions` properties used at construction (source: `PayPalServerSdkClientOptions.cs`):

| Property | Type | Set to |
|---|---|---|
| `Environment` | `ServerEnvironment` | `ServerEnvironment.Sandbox` |
| `Oauth2` | `OAuth2ClientCredentials?` | client-id + secret from config |
| `Oauth2TokenStrategy` | `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | leave null (SDK default) |
| `Server` | `ServerOptions` | optional base-URL override |
| `Retry` | `RetryOptions` | `RetryOptions.Default()` or custom |

---

### Step 5 — Authorize payment: SDK operations

#### 5a. `Orders.CreateOrder`

| | |
|---|---|
| Controller property | `client.Orders` |
| HTTP | `POST /v2/checkout/orders` |
| Source | `Api/Orders.cs` (map: `operations/Orders.md`) |

Full signature (all must-pass-explicitly params shown):

```csharp
Task<PayPalServerSdk.Models.Order> CreateOrder(
    string?  payPalMockResponse,         // null in production
    string?  payPalRequestId,            // idempotency key — set per call
    string?  payPalPartnerAttributionId, // null unless partner
    string?  payPalClientMetadataId,     // null
    string?  payPalAuthAssertion,        // null
    PayPalServerSdk.Models.OrderRequest body,
    string?  prefer = "return=minimal",  // use "return=representation" to get auth IDs
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.Order`

**Request model — `OrderRequest`** (`Models/OrderRequest.cs`, namespace `PayPalServerSdk.Models`):

| Field (wire_name) | Type | Required? |
|---|---|---|
| `Intent (intent)` | `CheckoutPaymentIntent` | **required** |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | **required** |
| `PaymentSource (payment_source)` | `PaymentSource?` | optional |
| `Payer (payer)` | `Payer?` | optional |
| `ApplicationContext (application_context)` | `OrderApplicationContext?` | optional |

`CheckoutPaymentIntent` (enum, `PayPalServerSdk.Models.Enums`): `Authorize (AUTHORIZE)`, `Capture (CAPTURE)`.
For this integration use `CheckoutPaymentIntent.Authorize`.

**Request model — `PurchaseUnitRequest`** (`Models/PurchaseUnitRequest.cs`):

| Field (wire_name) | Type | Required? |
|---|---|---|
| `Amount (amount)` | `AmountWithBreakdown` | **required** |
| `ReferenceId (reference_id)` | `string?` | optional |
| `CustomId (custom_id)` | `string?` | optional (store eShop orderId here) |
| `InvoiceId (invoice_id)` | `string?` | optional |

**Request model — `AmountWithBreakdown`** (`Models/AmountWithBreakdown.cs`):

| Field (wire_name) | Type | Required? |
|---|---|---|
| `CurrencyCode (currency_code)` | `string` | **required** |
| `Value (value)` | `string` | **required** (decimal string e.g. `"29.99"`) |
| `Breakdown (breakdown)` | `AmountBreakdown?` | optional |

**Error**: `SdkException<PayPalServerSdk.Errors.CreateOrderError>` — **Case A**

| Accessor | Statuses |
|---|---|
| `TryGetError(out PayPalServerSdk.Models.Error error)` | 400, 401, 422 |
| `TryGetRawError(out RawError error)` | fallback (all others) |

`Error` fields: `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`

---

#### 5b. `Orders.AuthorizeOrder`

| | |
|---|---|
| Controller property | `client.Orders` |
| HTTP | `POST /v2/checkout/orders/{id}/authorize` |
| Source | `Api/Orders.cs` (map: `operations/Orders.md`) |

Full signature:

```csharp
Task<PayPalServerSdk.Models.OrderAuthorizeResponse> AuthorizeOrder(
    string  id,                          // PayPal order ID from CreateOrder response
    string? payPalMockResponse,          // null
    string? payPalRequestId,             // idempotency key
    string? payPalClientMetadataId,      // null
    string? payPalAuthAssertion,         // null
    PayPalServerSdk.Models.OrderAuthorizeRequest? body,
    string? prefer = "return=minimal",   // use "return=representation" to read auth IDs inline
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.OrderAuthorizeResponse`

**Request model — `OrderAuthorizeRequest`** (`Models/OrderAuthorizeRequest.cs`):

| Field (wire_name) | Type | Required? |
|---|---|---|
| `PaymentSource (payment_source)` | `OrderAuthorizeRequestPaymentSource?` | optional |

**Request model — `OrderAuthorizeRequestPaymentSource`** (`Models/OrderAuthorizeRequestPaymentSource.cs`):

| Field (wire_name) | Type | Required? | Use for |
|---|---|---|---|
| `Card (card)` | `CardRequest?` | optional | inline card |
| `Token (token)` | `Token?` | optional | billing-agreement token |
| `Paypal (paypal)` | `PayPalWallet?` | optional | PayPal wallet |

**For inline card — `CardRequest`** (`Models/CardRequest.cs`):

| Field (wire_name) | Type | Required? |
|---|---|---|
| `Name (name)` | `string?` | optional |
| `Number (number)` | `string?` | PAN — required for inline card |
| `Expiry (expiry)` | `string?` | `"YYYY-MM"` format |
| `SecurityCode (security_code)` | `string?` | CVV |
| `BillingAddress (billing_address)` | `Address?` | optional |
| `VaultId (vault_id)` | `string?` | set this instead of Number/Expiry/SecurityCode for vaulted card |
| `Attributes (attributes)` | `CardAttributes?` | optional (verification/vault instructions) |

**For vaulted card**: set `CardRequest.VaultId` to the payment token ID. Leave `Number`, `Expiry`, `SecurityCode` null.

**`Address`** (`Models/Address.cs`): `AddressLine1?`, `AddressLine2?`, `AdminArea2?`, `AdminArea1?`, `PostalCode?`, `CountryCode` (**required**).

**Response model — `OrderAuthorizeResponse`** (`Models/OrderAuthorizeResponse.cs`):

| Field (wire_name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | PayPal order ID |
| `Status (status)` | `OrderStatus?` | check for `PayerActionRequired` |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnit>?` | navigate for auth ID |

To extract authorization ID:

```csharp
// Requires prefer: "return=representation"
var authId = response.PurchaseUnits?[0]
    .Payments?.Authorizations?[0].Id;
var authStatus = response.PurchaseUnits?[0]
    .Payments?.Authorizations?[0].Status;
var authExpiry = response.PurchaseUnits?[0]
    .Payments?.Authorizations?[0].ExpirationTime; // ISO-8601 string
```

`PurchaseUnit` → `Payments (payments): PaymentCollection?`
`PaymentCollection` → `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`
`AuthorizationWithAdditionalData` fields: `Id (id): string?`, `Status (status): AuthorizationStatus?`, `StatusDetails (status_details): AuthorizationStatusDetails?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`

`OrderStatus` enum (`PayPalServerSdk.Models.Enums`): `Created`, `Saved`, `Approved`, `Voided`, `Completed`, `PayerActionRequired`.

If `response.Status == OrderStatus.PayerActionRequired`: PayPal requires 3DS browser redirect — surface as an actionable error to the caller; do not persist an authorization ID. (See Blockers section.)

**Error**: `SdkException<PayPalServerSdk.Errors.AuthorizeOrderError>` — **Case A**

| Accessor | Statuses |
|---|---|
| `TryGetError(out PayPalServerSdk.Models.Error error)` | 400, 401, 403, 404, 422, 500 |
| `TryGetRawError(out RawError error)` | fallback |

---

### Step 6 — Capture at fulfilment: SDK operations

#### 6a. `Payments.ReauthorizePayment` (stale auth path)

| | |
|---|---|
| Controller property | `client.Payments` |
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/reauthorize` |
| Source | `Api/Payments.cs` (map: `operations/Payments.md`) |

Full signature:

```csharp
Task<PayPalServerSdk.Models.PaymentAuthorization> ReauthorizePayment(
    string  authorizationId,
    string? payPalRequestId,       // idempotency key; must pass explicitly
    string? payPalAuthAssertion,   // null; must pass explicitly
    PayPalServerSdk.Models.ReauthorizeRequest? body,   // must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.PaymentAuthorization`

**Request model — `ReauthorizeRequest`** (`Models/ReauthorizeRequest.cs`):

| Field (wire_name) | Type | Required? |
|---|---|---|
| `Amount (amount)` | `Money?` | optional (same or up to 115% of original in US) |

**Response — `PaymentAuthorization`** (`Models/PaymentAuthorization.cs`):

| Field (wire_name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | new authorization ID — store this, replaces old |
| `Status (status)` | `AuthorizationStatus?` | must be `Created` to proceed |
| `Amount (amount)` | `Money?` | |
| `ExpirationTime (expiration_time)` | `string?` | new expiry (ISO-8601) |

**Stale-auth logic** (determine before calling):
- Auth within 3-day honor period: capture directly, no reauth needed.
- Auth 4–29 days past creation (outside honor period, within 29-day reauth window): call `ReauthorizePayment`.
- Auth older than 29 days after original creation (30+ days total): non-renewable — return an actionable error "Authorization expired and cannot be renewed; a new payment must be authorized." Do not call any PayPal API in this branch.

The expiration time is in `AuthorizationWithAdditionalData.ExpirationTime` (string, ISO-8601) stored in `OrderPayment.AuthorizationExpiresAt`. Parse at fulfil-time.

**Error**: `SdkException<PayPalServerSdk.Errors.ReauthorizePaymentError>` — **Case A**

| Accessor | Statuses |
|---|---|
| `TryGetError(out PayPalServerSdk.Models.Error error)` | 400, 401, 403, 404, 422 |
| `TryGetNoContent(out RawError error)` | 500 |
| `TryGetRawError(out RawError error)` | fallback |

---

#### 6b. `Payments.CaptureAuthorizedPayment`

| | |
|---|---|
| Controller property | `client.Payments` |
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/capture` |
| Source | `Api/Payments.cs` (map: `operations/Payments.md`) |

Full signature:

```csharp
Task<PayPalServerSdk.Models.CapturedPayment> CaptureAuthorizedPayment(
    string  authorizationId,
    string? payPalMockResponse,    // null; must pass explicitly
    string? payPalRequestId,       // idempotency key; must pass explicitly
    string? payPalAuthAssertion,   // null; must pass explicitly
    PayPalServerSdk.Models.CaptureRequest? body,   // must pass explicitly
    string? prefer = "return=minimal",   // use "return=representation" for breakdown
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.CapturedPayment`

**Request model — `CaptureRequest`** (`Models/CaptureRequest.cs`):

| Field (wire_name) | Type | Required? |
|---|---|---|
| `Amount (amount)` | `Money?` | optional (omit to capture full authorization) |
| `InvoiceId (invoice_id)` | `string?` | optional |
| `FinalCapture (final_capture)` | `bool? = false` | set `true` to release remaining auth |
| `PaymentInstruction (payment_instruction)` | `CapturePaymentInstruction?` | optional |
| `NoteToPayer (note_to_payer)` | `string?` | optional |

**Response — `CapturedPayment`** (`Models/CapturedPayment.cs`):

| Field (wire_name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | capture ID — store in `OrderPayment.CaptureId` |
| `Status (status)` | `CaptureStatus?` | must be `Completed` |
| `Amount (amount)` | `Money?` | captured amount |
| `SellerReceivableBreakdown (seller_receivable_breakdown)` | `SellerReceivableBreakdown?` | fee/net breakdown |

**`SellerReceivableBreakdown`** (`Models/SellerReceivableBreakdown.cs`):

| Field (wire_name) | Type | Notes |
|---|---|---|
| `GrossAmount (gross_amount)` | `Money` | **required** — full captured amount |
| `PaypalFee (paypal_fee)` | `Money?` | PayPal processing fee |
| `PaypalFeeInReceivableCurrency (paypal_fee_in_receivable_currency)` | `Money?` | fee in merchant's currency if different |
| `NetAmount (net_amount)` | `Money?` | net to merchant (gross − fee) |
| `ReceivableAmount (receivable_amount)` | `Money?` | if currency conversion applies |

`CaptureStatus` enum (`PayPalServerSdk.Models.Enums`): `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed`.

**Error**: `SdkException<PayPalServerSdk.Errors.CaptureAuthorizedPaymentError>` — **Case A**

| Accessor | Statuses |
|---|---|
| `TryGetError(out PayPalServerSdk.Models.Error error)` | 400, 401, 403, 404, 409, 422 |
| `TryGetNoContent(out RawError error)` | 500 |
| `TryGetRawError(out RawError error)` | fallback |

Note on 409: this is returned for duplicate capture (already captured). Read `Error.Details[].Issue` to surface actionable message.

---

### Step 7 — Void authorization: SDK operation

#### `Payments.VoidPayment`

| | |
|---|---|
| Controller property | `client.Payments` |
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/void` |
| Source | `Api/Payments.cs` (map: `operations/Payments.md`) |

Full signature:

```csharp
Task<PayPalServerSdk.Models.PaymentAuthorization> VoidPayment(
    string  authorizationId,
    string? payPalMockResponse,    // null; must pass explicitly
    string? payPalAuthAssertion,   // null; must pass explicitly
    string? payPalRequestId,       // null (or idempotency key); must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.PaymentAuthorization` (status will be `Voided`)

**Error**: `SdkException<PayPalServerSdk.Errors.VoidPaymentError>` — **Case A**

| Accessor | Statuses |
|---|---|
| `TryGetError(out PayPalServerSdk.Models.Error error)` | 401, 403, 404, 409, 422 |
| `TryGetNoContent(out RawError error)` | 500 |
| `TryGetRawError(out RawError error)` | fallback |

Note: 409 is returned if the authorization has already been fully captured (cannot void). Handle as a business-logic error with actionable message.

---

### Step 8 — Refund: SDK operation

#### `Payments.RefundCapturedPayment`

| | |
|---|---|
| Controller property | `client.Payments` |
| HTTP | `POST /v2/payments/captures/{capture_id}/refund` |
| Source | `Api/Payments.cs` (map: `operations/Payments.md`) |

Full signature:

```csharp
Task<PayPalServerSdk.Models.Refund> RefundCapturedPayment(
    string  captureId,
    string? payPalMockResponse,    // null; must pass explicitly
    string? payPalRequestId,       // caller-supplied idempotency key; must pass explicitly
    string? payPalAuthAssertion,   // null; must pass explicitly
    PayPalServerSdk.Models.RefundRequest? body,   // must pass explicitly
    string? prefer = "return=minimal",   // use "return=representation" for breakdown
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

**Idempotency key**: `payPalRequestId` — pass the caller-supplied idempotency key from the request body here. PayPal deduplicates on this value per capture ID.

Returns: `PayPalServerSdk.Models.Refund`

**Request model — `RefundRequest`** (`Models/RefundRequest.cs`):

| Field (wire_name) | Type | Required? |
|---|---|---|
| `Amount (amount)` | `Money?` | omit for full refund; set for partial |
| `CustomId (custom_id)` | `string?` | optional |
| `InvoiceId (invoice_id)` | `string?` | optional |
| `NoteToPayer (note_to_payer)` | `string?` | optional |

For full refund: pass `body: null` (or `body: new RefundRequest()`).
For partial: `body: new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = decimalString } }`.

**Response — `Refund`** (`Models/Refund.cs`):

| Field (wire_name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | refund ID — store in `OrderPayment.RefundIds` |
| `Status (status)` | `RefundStatus?` | |
| `Amount (amount)` | `Money?` | refunded amount |
| `SellerPayableBreakdown (seller_payable_breakdown)` | `SellerPayableBreakdown?` | fee breakdown |

**`SellerPayableBreakdown`** (`Models/SellerPayableBreakdown.cs`):

| Field (wire_name) | Type | Notes |
|---|---|---|
| `GrossAmount (gross_amount)` | `Money?` | refund gross |
| `PaypalFee (paypal_fee)` | `Money?` | PayPal fee returned |
| `NetAmount (net_amount)` | `Money?` | net refunded to buyer |
| `TotalRefundedAmount (total_refunded_amount)` | `Money?` | cumulative refunds on this capture |

`RefundStatus` enum (`PayPalServerSdk.Models.Enums`): `Cancelled`, `Failed`, `Pending`, `Completed`.

**Cannot-refund-more-than-captured guard**: compare requested refund amount against `OrderPayment.CapturedAmount - OrderPayment.TotalRefundedAmount` before calling PayPal. A 422 from PayPal also signals this; read `Error.Details[].Issue` for "REFUND_AMOUNT_EXCEEDED" or similar.

**Error**: `SdkException<PayPalServerSdk.Errors.RefundCapturedPaymentError>` — **Case A**

| Accessor | Statuses |
|---|---|
| `TryGetError(out PayPalServerSdk.Models.Error error)` | 400, 401, 403, 404, 409, 422 |
| `TryGetNoContent(out RawError error)` | 500 |
| `TryGetRawError(out RawError error)` | fallback |

---

### Step 10 — Reconciliation: SDK operation

#### `TransactionSearch.SearchTransactions`

| | |
|---|---|
| Controller property | `client.TransactionSearch` |
| HTTP | `GET /v1/reporting/transactions` |
| Source | `Api/TransactionSearch.cs` (map: `operations/TransactionSearch.md`) |

Full signature:

```csharp
Task<PayPalServerSdk.Models.SearchResponse> SearchTransactions(
    string  startDate,                    // required — ISO-8601 datetime e.g. "2024-01-01T00:00:00Z"
    string  endDate,                      // required — ISO-8601 datetime
    string? transactionId,               // null; must pass explicitly
    string? transactionType,             // null; must pass explicitly
    string? transactionStatus,           // null; must pass explicitly
    string? transactionAmount,           // null; must pass explicitly
    string? transactionCurrency,         // null; must pass explicitly
    string? paymentInstrumentType,       // null; must pass explicitly
    string? storeId,                     // null; must pass explicitly
    string? terminalId,                  // null; must pass explicitly
    string? fields = "transaction_info",
    string? balanceAffectingRecordsOnly = "Y",
    int?    pageSize = 100,
    int?    page = 1,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Wire query-param mapping: `start_date` ← `startDate`, `end_date` ← `endDate`, `page` ← `page`, `page_size` ← `pageSize`.

Returns: `PayPalServerSdk.Models.SearchResponse`

**Error**: `SdkException<RawError>` — **Case B** (this is the only Case-B operation in scope)

```csharp
catch (SdkException<RawError> ex)
{
    var status = ex.Error.StatusCode;        // HttpStatusCode
    var body   = ex.Error.ReadAsString();    // raw JSON string
    // ReadAsJson<T>() also available
}
```

**Response — `SearchResponse`** (`Models/SearchResponse.cs`):

| Field (wire_name) | Type | Notes |
|---|---|---|
| `TransactionDetails (transaction_details)` | `IReadOnlyList<TransactionDetails>?` | per-transaction records |
| `Page (page)` | `int?` | current page |
| `TotalItems (total_items)` | `int?` | |
| `TotalPages (total_pages)` | `int?` | use for pagination loop |

**Pagination pattern** (no built-in cursor — the SDK map marks "only `page`, no `perPage`"):

```csharp
int page = 1;
int totalPages = 1;
var allTransactions = new List<TransactionDetails>();
do
{
    var result = await client.TransactionSearch.SearchTransactions(
        startDate: from.ToString("o"),
        endDate: to.ToString("o"),
        transactionId: null, transactionType: null, transactionStatus: null,
        transactionAmount: null, transactionCurrency: null,
        paymentInstrumentType: null, storeId: null, terminalId: null,
        page: page,
        ct: ct);
    if (result.TransactionDetails is not null)
        allTransactions.AddRange(result.TransactionDetails);
    if (result.TotalPages.HasValue && result.TotalPages.Value > 0)
        totalPages = result.TotalPages.Value;
    page++;
} while (page <= totalPages);
```

**`TransactionDetails`** (`Models/TransactionDetails.cs`):
- `TransactionInfo (transaction_info): TransactionInformation?`
- `PayerInfo (payer_info): PayerInformation?`

**`TransactionInformation`** (`Models/TransactionInformation.cs`) — fields used for reconciliation:

| Field (wire_name) | Type |
|---|---|
| `TransactionId (transaction_id)` | `string?` |
| `TransactionAmount (transaction_amount)` | `Money?` |
| `FeeAmount (fee_amount)` | `Money?` |
| `TransactionStatus (transaction_status)` | `string?` |
| `TransactionInitiationDate (transaction_initiation_date)` | `string?` |
| `TransactionUpdatedDate (transaction_updated_date)` | `string?` |
| `InvoiceId (invoice_id)` | `string?` |
| `CustomField (custom_field)` | `string?` |
| `PaypalReferenceId (paypal_reference_id)` | `string?` |
| `PaypalReferenceIdType (paypal_reference_id_type)` | `PayPalReferenceIdType?` |
| `EndingBalance (ending_balance)` | `Money?` |

Match against eShop orders by `CustomId` stored in `PurchaseUnitRequest.CustomId` at order creation time (store the eShop integer order ID there).

---

### Step 11 — Vault a card: SDK operation

#### `Vault.CreatePaymentToken`

| | |
|---|---|
| Controller property | `client.Vault` |
| HTTP | `POST /v3/vault/payment-tokens` |
| Source | `Api/Vault.cs` (map: `operations/Vault.md`) |

Full signature:

```csharp
Task<PayPalServerSdk.Models.PaymentTokenResponse> CreatePaymentToken(
    string? payPalRequestId,   // idempotency key; must pass explicitly
    PayPalServerSdk.Models.PaymentTokenRequest body,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.PaymentTokenResponse`

**Request model — `PaymentTokenRequest`** (`Models/PaymentTokenRequest.cs`):

| Field (wire_name) | Type | Required? |
|---|---|---|
| `Customer (customer)` | `Customer?` | optional — set `MerchantCustomerId` to link to eShop user ID |
| `PaymentSource (payment_source)` | `PaymentTokenRequestPaymentSource` | **required** |

**`Customer`** (`Models/Customer.cs`): `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?`

**`PaymentTokenRequestPaymentSource`** (`Models/PaymentTokenRequestPaymentSource.cs`):

| Field (wire_name) | Type | Required? |
|---|---|---|
| `Card (card)` | `PaymentTokenRequestCard?` | set for card vaulting |
| `Token (token)` | `VaultTokenRequest?` | for tokenized source |

**`PaymentTokenRequestCard`** (`Models/PaymentTokenRequestCard.cs`):

| Field (wire_name) | Type | Required? |
|---|---|---|
| `Name (name)` | `string?` | optional |
| `Number (number)` | `string?` | PAN — provide but NEVER store or log |
| `Expiry (expiry)` | `string?` | `"YYYY-MM"` |
| `SecurityCode (security_code)` | `string?` | CVV — never store or log |
| `Brand (brand)` | `CardBrand?` | optional |
| `BillingAddress (billing_address)` | `Address?` | optional |

**Response — `PaymentTokenResponse`** (`Models/PaymentTokenResponse.cs`):

| Field (wire_name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | payment method ID — store as the vault reference |
| `Customer (customer)` | `CustomerResponse?` | PayPal customer ID |
| `PaymentSource (payment_source)` | `PaymentTokenResponsePaymentSource?` | card descriptor |

**`PaymentTokenResponsePaymentSource`** (`Models/PaymentTokenResponsePaymentSource.cs`):
- `Card (card): CardPaymentTokenEntity?`

**`CardPaymentTokenEntity`** (`Models/CardPaymentTokenEntity.cs`) — safe descriptor fields:

| Field (wire_name) | Type | Notes |
|---|---|---|
| `LastDigits (last_digits)` | `string?` | last 4 digits — safe to store/display |
| `Brand (brand)` | `CardBrand?` | e.g. `CardBrand.Visa` |
| `Expiry (expiry)` | `string?` | `"YYYY-MM"` — safe to store/display |
| `Name (name)` | `string?` | cardholder name |

`CardBrand` enum (`PayPalServerSdk.Models.Enums`): `Visa`, `Mastercard`, `Discover`, `Amex`, `Jcb`, `Maestro`, `Diners`, … (full list in `enums.md`).

**Error**: `SdkException<PayPalServerSdk.Errors.CreatePaymentTokenError>` — **Case A**

| Accessor | Statuses |
|---|---|
| `TryGetError1(out PayPalServerSdk.Models.Error1 error)` | 400, 403, 404, 422, 500 |
| `TryGetRawError(out RawError error)` | fallback |

Note: Vault error type uses `Error1` (not `Error`) and `TryGetError1`. `Error1` fields: `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails1>?`, `Links (links): IReadOnlyList<ErrorLinkDescription>?`.

---

### Step 12 — List saved cards: SDK operation

#### `Vault.ListCustomerPaymentTokens`

| | |
|---|---|
| Controller property | `client.Vault` |
| HTTP | `GET /v3/vault/payment-tokens` |
| Source | `Api/Vault.cs` (map: `operations/Vault.md`) |

Full signature:

```csharp
Task<PayPalServerSdk.Models.CustomerVaultPaymentTokensResponse> ListCustomerPaymentTokens(
    string  customerId,                 // merchant_customer_id used at vault time
    int?    pageSize = 5,
    int?    page = 1,
    bool?   totalRequired = false,      // set true to get TotalItems/TotalPages
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Wire query params: `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired`.

Returns: `PayPalServerSdk.Models.CustomerVaultPaymentTokensResponse`

**Response — `CustomerVaultPaymentTokensResponse`** (`Models/CustomerVaultPaymentTokensResponse.cs`):

| Field (wire_name) | Type |
|---|---|
| `TotalItems (total_items)` | `int?` |
| `TotalPages (total_pages)` | `int?` |
| `Customer (customer)` | `VaultResponseCustomer?` |
| `PaymentTokens (payment_tokens)` | `IReadOnlyList<PaymentTokenResponse>?` |

Each `PaymentTokenResponse` has `Id`, `Customer`, `PaymentSource.Card` (see Step 11 model above).

**Error**: `SdkException<PayPalServerSdk.Errors.ListCustomerPaymentTokensError>` — **Case A**

| Accessor | Statuses |
|---|---|
| `TryGetError1(out PayPalServerSdk.Models.Error1 error)` | 400, 403, 500 |
| `TryGetRawError(out RawError error)` | fallback |

---

### Step 13 — Delete saved card: SDK operation

#### `Vault.DeletePaymentToken`

| | |
|---|---|
| Controller property | `client.Vault` |
| HTTP | `DELETE /v3/vault/payment-tokens/{id}` |
| Source | `Api/Vault.cs` (map: `operations/Vault.md`) |

Full signature:

```csharp
Task DeletePaymentToken(
    string id,   // payment token ID (vault reference)
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `void` (Task — no response body on success).

**Ownership guard**: verify the payment token belongs to the authenticated user before calling. The SDK will not enforce ownership — the application must.

**Error**: `SdkException<PayPalServerSdk.Errors.DeletePaymentTokenError>` — **Case A**

| Accessor | Statuses |
|---|---|
| `TryGetError1(out PayPalServerSdk.Models.Error1 error)` | 400, 403, 500 |
| `TryGetRawError(out RawError error)` | fallback |

---

### Idempotency key summary

The `payPalRequestId` parameter maps to the `PayPal-Request-Id` HTTP header. Operations that accept it:

| Operation | param name | Recommended value |
|---|---|---|
| `CreateOrder` | `payPalRequestId` | `$"create-{eShopOrderId}-{timestamp}"` |
| `AuthorizeOrder` | `payPalRequestId` | `$"auth-{eShopOrderId}-{timestamp}"` |
| `CaptureAuthorizedPayment` | `payPalRequestId` | `$"capture-{eShopOrderId}"` (deterministic) |
| `ReauthorizePayment` | `payPalRequestId` | `$"reauth-{eShopOrderId}-{timestamp}"` |
| `RefundCapturedPayment` | `payPalRequestId` | caller-supplied idempotency key from request body |
| `CreatePaymentToken` | `payPalRequestId` | `$"vault-{userId}-{timestamp}"` |

Operations with no `payPalRequestId` param: `VoidPayment` uses `payPalRequestId` too (4th param) — pass the same idempotency pattern.

---

### Enums used — full value lists

**`CheckoutPaymentIntent`** (`PayPalServerSdk.Models.Enums`):
`Capture (CAPTURE)`, `Authorize (AUTHORIZE)`

**`AuthorizationStatus`** (`PayPalServerSdk.Models.Enums`):
`Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)`

**`CaptureStatus`** (`PayPalServerSdk.Models.Enums`):
`Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)`

**`RefundStatus`** (`PayPalServerSdk.Models.Enums`):
`Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)`

**`OrderStatus`** (`PayPalServerSdk.Models.Enums`):
`Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`

**`PaymentTokenStatus`** (`PayPalServerSdk.Models.Enums`):
`Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)`

**`CardBrand`** (selected values, `PayPalServerSdk.Models.Enums`):
`Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, `Jcb (JCB)`, `Maestro (MAESTRO)`, `Diners (DINERS)`, `Unknown (UNKNOWN)` (full list in `map/models/enums.md`)

**`CardType`** (`PayPalServerSdk.Models.Enums`):
`Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)`

**`ServerEnvironment`** (`PayPalServerSdk.Servers`):
`Sandbox` (only member in this SDK)

**`StoreInVaultInstruction`** (`PayPalServerSdk.Models.Enums`):
`OnSuccess (ON_SUCCESS)`

---

### Entity changes — `OrderPayment` (new entity)

Add a new EF entity to `src/ApplicationCore/Entities/OrderAggregate/` (or `src/Infrastructure/`):

```csharp
public class OrderPayment : BaseEntity
{
    // FK to Order
    public int OrderId { get; private set; }

    // PayPal IDs
    public string? PayPalOrderId       { get; private set; }  // from CreateOrder
    public string? AuthorizationId     { get; private set; }  // from AuthorizeOrder
    public string? CaptureId           { get; private set; }  // from CaptureAuthorizedPayment
    public List<string> RefundIds      { get; private set; } = new(); // from RefundCapturedPayment

    // Payment state
    public string PaymentStatus        { get; private set; } = "Pending"; // local status enum string
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }   // parsed from ExpirationTime

    // Financial snapshot (stored for reconciliation display; source of truth is PayPal)
    public decimal? CapturedAmount     { get; private set; }  // GrossAmount from SellerReceivableBreakdown
    public decimal? PayPalFeeAmount    { get; private set; }  // PaypalFee from SellerReceivableBreakdown
    public decimal? NetAmount          { get; private set; }  // NetAmount from SellerReceivableBreakdown
    public decimal? TotalRefundedAmount{ get; private set; }  // running total; updated per refund

    // Currency
    public string Currency             { get; private set; } = "USD";
}
```

Add navigation to `Order`:
```csharp
public OrderPayment? Payment { get; private set; }
```

Add EF configuration: `OrderPayment.RefundIds` stored as JSON column or as a separate `OrderRefund` table (implementer's choice — JSON column is simpler for this use case).

**Local payment status values** (string enum or C# enum):
`Pending`, `Authorized`, `Captured`, `Voided`, `RefundedFull`, `RefundedPartial`, `AuthorizationExpired`

---

### DI registrations needed

```csharp
// In Program.cs / service registration
services.AddPayPalServerSdkClient(o =>
{
    o.Environment = config["PayPal:Environment"] == "Production"
        ? ServerEnvironment.Production   // UNVERIFIED — check if Production member exists; map only shows Sandbox
        : ServerEnvironment.Sandbox;
    o.Oauth2 = new OAuth2ClientCredentials
    {
        ClientId     = config["PayPal:ClientId"]!,
        ClientSecret = config["PayPal:ClientSecret"]!
    };
    // Optional base-URL override (PayPal:BaseUrl in config):
    // o.Server.Default.Sandbox.BaseUrl = config["PayPal:BaseUrl"]!;
});

// Register application service
services.AddScoped<IPayPalPaymentService, PayPalPaymentService>();
```

MUST load `dotnet-client-initialization` before writing this block — the `HttpClient` lifetime and the DI extension's interaction with `IHttpClientFactory` carry non-obvious requirements that the signature does not reveal.

---

## 3. Trap Notes

⚠ **Step 1 (install)** — The NuGet package is `AsadAli.Checkout.Sdk`, not `PayPalServerSdk`. Install version-less. **MUST load `dotnet-client-initialization`** before writing the DI registration — `HttpClient` ownership and `IHttpClientFactory` interaction are the primary trap.

⚠ **Step 2 (auth)** — OAuth2 token acquisition is managed by the SDK, but the exact property names on `OAuth2ClientCredentials` (e.g. `OAuthClientId`, `OAuthClientSecret`) are not confirmed from the map alone — they come from `Core/` source. **MUST load `dotnet-authentication`** before wiring credentials; also load it if you see 401 failures, to check whether a token-strategy hook is required for token caching.

⚠ **Step 2 (base-URL override)** — `PayPal:BaseUrl` is supposed to override ALL PayPal calls including token requests. Whether `options.Server` overrides the token endpoint as well as the API endpoint is not visible in the map. **MUST load `dotnet-configuration-resilience`** to confirm the base-URL override scope before relying on it for token requests.

⚠ **Steps 5–8 (calling operations)** — All these operations have 4–5 nullable, no-default params that must be passed explicitly (positional call will mis-bind). Named arguments are mandatory. **MUST load `dotnet-calling-endpoints`** before the first call — the skill covers named-argument discipline and the `prefer: "return=representation"` vs `"return=minimal"` tradeoff.

⚠ **Step 5 (prefer parameter)** — The default `prefer: "return=minimal"` on `AuthorizeOrder` may return a partial body that omits `PurchaseUnits[0].Payments.Authorizations`. To read the authorization ID inline from the response, pass `prefer: "return=representation"`. Failure to do this forces a separate `GetOrder` call to retrieve the auth ID. **MUST load `dotnet-calling-endpoints`** for context on this header.

⚠ **Step 5 (3DS / PAYER_ACTION_REQUIRED)** — If `AuthorizeOrder` returns a response where `Status == OrderStatus.PayerActionRequired`, PayPal is demanding a 3DS browser redirect. Direct card processing (CNPJ) is not permitted by PayPal in all regions/scenarios. Detecting this status and returning an actionable error (not persisting auth state) is required. See Blockers.

⚠ **Step 6 (stale-auth window)** — `ReauthorizePayment` is only valid 4–29 days after original creation. After 30 days, PayPal will reject the reauth; detect by checking `AuthorizationExpiresAt + 29 days` before calling. Non-renewable authorizations must surface a clear operator error. The `Timeout` on RetryOptions does not cover auth window logic. **MUST load `dotnet-configuration-resilience`** to understand what `Timeout` actually bounds (it is per-attempt, not per-business-flow).

⚠ **Step 8 (refund idempotency)** — `payPalRequestId` in `RefundCapturedPayment` is the idempotency deduplication key scoped per capture ID. Replaying the same key returns the original refund without creating a duplicate. The caller must supply a stable idempotency key per refund attempt. **MUST load `dotnet-calling-endpoints`** for the exact semantics.

⚠ **Step 10 (reconciliation pagination)** — `SearchTransactions` is **Case B** (raw error, no typed accessor). The pagination is manual: read `SearchResponse.TotalPages` and loop on the `page` param. There is no cursor. **MUST load `dotnet-error-handling`** for the Case-B exception pattern before writing the catch ladder.

⚠ **Step 10 (retry on SearchTransactions)** — `SearchTransactions` is a GET (idempotent), but `HttpMethodsToRetry` gates the STATUS trigger only. Transport failures (`HttpRequestException`) are retried on every verb including POST, so non-idempotent writes can execute more than once. **MUST load `dotnet-configuration-resilience`** before configuring retry options.

⚠ **Steps 11–13 (Vault error type)** — Vault operations throw `CreatePaymentTokenError`, `ListCustomerPaymentTokensError`, `DeletePaymentTokenError` whose accessor is `TryGetError1(out Error1)` — NOT `TryGetError(out Error)`. Using the wrong accessor compiles fine but always returns `false`. **MUST load `dotnet-error-handling`** to understand Case-A accessor naming conventions.

⚠ **All steps (enum construction)** — Enums are `StringEnum<T>`, not C# enums. Access via static members (e.g. `CheckoutPaymentIntent.Authorize`, not `CheckoutPaymentIntent.AUTHORIZE`). **MUST load `dotnet-models`** before building any request model containing an enum or union.

---

## 4. REQUIRED READING

Load every skill listed below **before implementation starts** at the step indicated. This sheet deliberately does not carry their contents — each skill resolves usage traps that a one-line note cannot fully convey.

| Skill | Step(s) it governs |
|---|---|
| `dotnet-client-initialization` | Step 1–2 — NuGet install, DI registration, `HttpClient` ownership |
| `dotnet-authentication` | Step 2 — OAuth2 credentials, token strategy, 401 diagnosis |
| `dotnet-calling-endpoints` | Steps 5–13 — named arguments, `prefer` header, all SDK calls |
| `dotnet-models` | Steps 5–13 — `StringEnum<T>`, record initializers, `Money` value format |
| `dotnet-error-handling` | Steps 5–13 — Case A vs B, `TryGetError` vs `TryGetError1`, `TryGetNoContent`, JSON boundary hazards |
| `dotnet-configuration-resilience` | Steps 1–2, 10 — retry/timeout semantics, base-URL override, pagination |
| `dotnet-testing` | All — `HttpClient` seam, mock strategies |

**Mandatory error-boundary hazards** (these belong at the boundary written earliest — before any operation call reaches production code):

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.

- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the error boundary.

---

## 5. Assumptions & Blockers

### Blockers

**BLOCKER (conditional) — Direct card processing without 3DS redirect.**
PayPal's sandbox may require a 3DS browser challenge for direct card payments, returning `OrderStatus.PayerActionRequired` on `AuthorizeOrder`. The SDK surface does not prevent this — the live response decides. Whether the sandbox Visa test card `4111 1111 1111 1111` bypasses 3DS is UNVERIFIED via live traffic. Mitigation (mandatory to code regardless):
1. After `AuthorizeOrder`, check `response.Status`. If `== OrderStatus.PayerActionRequired`, return HTTP 422 with message "This card requires browser-based 3D Secure verification, which is not supported in this flow." Do not persist an authorization.
2. If all sandbox test cards trigger `PayerActionRequired`, direct card authorization is not feasible in sandbox without PayPal's CNP (card-not-present) approval — escalate to PayPal support. This is a design blocker if it occurs.

**BLOCKER (conditional) — `ServerEnvironment.Production` member existence.**
The SDK map lists only `ServerEnvironment.Sandbox` as a known member. If the production environment uses a different member name (e.g. `ServerEnvironment.Live` or `ServerEnvironment.Production`), the build will fail. Verify by reading `Servers/ServerEnvironment.cs` in the SDK source before coding the environment switch. If only `Sandbox` exists, the integration is sandbox-only and a production base-URL must be set via `options.Server` override.

### Assumptions

- Currency is read from `PAYPAL_CURRENCY` env var (config key `PayPal:Currency`); used in every `Money.CurrencyCode` field.
- eShop integer Order ID is stored in `PurchaseUnitRequest.CustomId` for reconciliation matching.
- The `merchantCustomerId` used to link vault tokens to eShop users is the authenticated user's identity claim (e.g. email or subject claim) — must be stable and unique per shopper.
- `prefer: "return=representation"` is passed on `CreateOrder` and `AuthorizeOrder` so authorization IDs are available inline without a follow-up `GetOrder` call.
- Partial refunds accumulate: `OrderPayment.TotalRefundedAmount` is updated on each successful refund and checked against `CapturedAmount` before calling PayPal.
- `RefundIds` on `OrderPayment` is stored as a serialized list; implementer chooses JSON column vs child table.
- The `POST /api/orders` endpoint does not call PayPal — it creates the eShop order in DB only; PayPal interaction starts at `POST /api/orders/{orderId}/pay`.
- Reconciliation date range `from`/`to` are passed as ISO-8601 strings directly to `startDate`/`endDate` parameters.
- The plan covers the `src/PublicApi` project as the host for new endpoints; PayPal service logic lives in `src/Infrastructure`.
