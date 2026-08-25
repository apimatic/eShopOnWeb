# PayPal Integration Contract Sheet — eShopOnWeb

## 1. Scope & Sequence

| Step | Description | SDK Operations (controller.method) |
|---|---|---|
| 1a | Create PayPal order with AUTHORIZE intent | `client.Orders.CreateOrder(...)` |
| 1b | Authorize the created order (card or vault token) | `client.Orders.AuthorizeOrder(...)` |
| 2 | Capture a previously authorized payment at fulfilment | `client.Payments.CaptureAuthorizedPayment(...)` |
| 3 | Re-authorize stale auth (within days 4–29) | `client.Payments.ReauthorizePayment(...)` |
| 4 | Void authorization (cancel before fulfilment) | `client.Payments.VoidPayment(...)` |
| 5 | Refund a captured payment (full or partial) | `client.Payments.RefundCapturedPayment(...)` |
| 6 | Save a card to vault | `client.Vault.CreatePaymentToken(...)` |
| 7 | List all vaulted cards for a customer | `client.Vault.ListCustomerPaymentTokens(...)` |
| 8 | Delete a vaulted card | `client.Vault.DeletePaymentToken(...)` |
| 9 | Create order + authorize using vault token | Steps 1a + 1b with Token payment source |
| 10 | Transaction search — page through ALL results | `client.TransactionSearch.SearchTransactions(...)` in a manual page loop |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.1 Namespaces — required `using` directives

| Contents | Namespace |
|---|---|
| Client, options, DI extension | `PayPalServerSdk` |
| Environment enum | `PayPalServerSdk.Servers` |
| All record models (requests, responses) | `PayPalServerSdk.Models` |
| All enum types | `PayPalServerSdk.Models.Enums` |
| Typed error classes | `PayPalServerSdk.Errors` |
| `OAuth2ClientCredentials`, `IOAuth2TokenStrategy` | resolved by `dotnet-authentication` — MUST load before wiring |

Child namespaces are NOT imported transitively. Each namespace above needs its own `using`.

### 2.2 Client Construction & Auth

| Option property | Type | Notes |
|---|---|---|
| `Environment` | `PayPalServerSdk.Servers.ServerEnvironment` | Set to `ServerEnvironment.Sandbox` |
| `Oauth2` | `OAuth2ClientCredentials?` | Exact property names on `OAuth2ClientCredentials` resolved by `dotnet-authentication` |
| `Oauth2TokenStrategy` | `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | Custom token strategy for base-URL override; see trap note §3 |
| `Server` | `ServerOptions` (source: `Servers/ServerOptions.cs`) | Override base URL when `PayPal:BaseUrl` is configured; exact `ServerOptions` property resolved by `dotnet-configuration-resilience` |
| `Retry` | `RetryOptions` | Build with `RetryOptions.Default()` as base; see trap note §3 |

Client constructor (source: `PayPalServerSdkClient.cs`):
```
PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)
```
DI extension (source: `ServiceCollectionExtensions.cs`):
```
services.AddPayPalServerSdkClient(o => { /* set credentials / environment */ });
```

**Source: `sdk-map.md` → Getting a client + Servers & auth sections**

---

### 2.3 Operations — Signatures, Request Models, Response Shapes, Error Cases

#### Step 1a — CreateOrder (`client.Orders`, source: `map/operations/Orders.md`)

**Signature:**
```
Task<Order> CreateOrder(
    string? payPalMockResponse,          // must pass explicitly — null to skip
    string? payPalRequestId,             // must pass explicitly
    string? payPalPartnerAttributionId,  // must pass explicitly
    string? payPalClientMetadataId,      // must pass explicitly
    string? payPalAuthAssertion,         // must pass explicitly
    OrderRequest body,                   // required, non-nullable
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
Pass `prefer: "return=representation"` to receive the full response body (not just minimal).

**Request model — `OrderRequest`** (source: `Models/OrderRequest.cs`, namespace `PayPalServerSdk.Models`):

| C# name (wire_name) | Type | Req? | Value for this integration |
|---|---|---|---|
| `Intent (intent)` | `CheckoutPaymentIntent` | required | `CheckoutPaymentIntent.Authorize` |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | required | One entry per charge |
| `PaymentSource (payment_source)` | `PaymentSource?` | optional | Set for inline card or vault token path |

**`PurchaseUnitRequest`** (source: `Models/PurchaseUnitRequest.cs`):

| C# name (wire_name) | Type | Req? |
|---|---|---|
| `Amount (amount)` | `AmountWithBreakdown` | required |
| `ReferenceId (reference_id)` | `string?` | optional |
| `CustomId (custom_id)` | `string?` | optional — carry order reference |

**`AmountWithBreakdown`** (source: `Models/AmountWithBreakdown.cs`):

| C# name (wire_name) | Type | Req? |
|---|---|---|
| `CurrencyCode (currency_code)` | `string` | required — from config `PayPal:Currency` |
| `Value (value)` | `string` | required — decimal string e.g. `"12.50"` |

**`PaymentSource`** (source: `Models/PaymentSource.cs`) — set ONE of:
- `Card (card): CardRequest?` for direct sandbox card (Visa 4111 1111 1111 1111)
- `Token (token): Token?` for vault token payment (Step 9)

**`CardRequest`** (source: `Models/CardRequest.cs`) for direct card:

| C# name (wire_name) | Type |
|---|---|
| `Number (number)` | `string?` |
| `Expiry (expiry)` | `string?` — format `YYYY-MM` |
| `SecurityCode (security_code)` | `string?` |
| `Name (name)` | `string?` |
| `BillingAddress (billing_address)` | `Address?` |

**`Token`** (source: `Models/Token.cs`) for vault payment:

| C# name (wire_name) | Type | Req? |
|---|---|---|
| `Id (id)` | `string` | required — `PaymentTokenResponse.Id` from vault save |
| `Type (type)` | `TokenType` | required — `TokenType.BillingAgreement` (only value in enum) |

**Response — `Order`** (source: `Models/Order.cs`):

| C# name (wire_name) | Type | Read for |
|---|---|---|
| `Id (id)` | `string?` | order ID — pass to `AuthorizeOrder` |
| `Status (status)` | `OrderStatus?` | confirm `OrderStatus.Created` |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnit>?` | |

**Error:** Case A — `SdkException<CreateOrderError>` (namespace `PayPalServerSdk.Errors`)
- `TryGetError(out Error error)` — hits 400, 401, 422
- `TryGetRawError(out RawError raw)` — fallback

**Enum values used:**

| Enum (namespace `PayPalServerSdk.Models.Enums`) | Member | Wire value |
|---|---|---|
| `CheckoutPaymentIntent` | `Authorize` | `AUTHORIZE` |
| `TokenType` | `BillingAgreement` | `BILLING_AGREEMENT` |
| `OrderStatus` | `Created` | `CREATED` |

---

#### Step 1b — AuthorizeOrder (`client.Orders`, source: `map/operations/Orders.md`)

**Signature:**
```
Task<OrderAuthorizeResponse> AuthorizeOrder(
    string id,                           // order ID from CreateOrder response
    string? payPalMockResponse,          // must pass explicitly — null to skip
    string? payPalRequestId,             // must pass explicitly
    string? payPalClientMetadataId,      // must pass explicitly
    string? payPalAuthAssertion,         // must pass explicitly
    OrderAuthorizeRequest? body,         // must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
Pass `prefer: "return=representation"` to receive the full response with authorization details.

**Request model — `OrderAuthorizeRequest`** (source: `Models/OrderAuthorizeRequest.cs`):

| C# name (wire_name) | Type |
|---|---|
| `PaymentSource (payment_source)` | `OrderAuthorizeRequestPaymentSource?` |

**`OrderAuthorizeRequestPaymentSource`** (source: `Models/OrderAuthorizeRequestPaymentSource.cs`) — set ONE of:
- `Card (card): CardRequest?` for inline card
- `Token (token): Token?` for vault token

**Response — `OrderAuthorizeResponse`** (source: `Models/OrderAuthorizeResponse.cs`):

| C# name (wire_name) | Type | Read for |
|---|---|---|
| `Id (id)` | `string?` | order ID |
| `Status (status)` | `OrderStatus?` | |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnit>?` | contains authorizations |

To extract the **authorization_id** (needed for capture/void/reauth):
```
response.PurchaseUnits?[0]
    .Payments?.Authorizations?[0]
    .Id  // AuthorizationWithAdditionalData.Id — string?
```

**`PurchaseUnit`** (source: `Models/PurchaseUnit.cs`):
- `Payments (payments): PaymentCollection?`

**`PaymentCollection`** (source: `Models/PaymentCollection.cs`):
- `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`

**`AuthorizationWithAdditionalData`** (source: `Models/AuthorizationWithAdditionalData.cs`):

| C# name (wire_name) | Type | Read for |
|---|---|---|
| `Id (id)` | `string?` | **authorization_id — store this** |
| `Status (status)` | `AuthorizationStatus?` | must be `Created` for a live auth |
| `ExpirationTime (expiration_time)` | `string?` | ISO-8601 — check before capture |
| `Amount (amount)` | `Money?` | authorized amount |
| `CreateTime (create_time)` | `string?` | original auth timestamp — use for 29-day window check |

**`AuthorizationStatus` enum** (namespace `PayPalServerSdk.Models.Enums`):

| Member | Wire value | Meaning |
|---|---|---|
| `Created` | `CREATED` | live — capturable |
| `Captured` | `CAPTURED` | already captured |
| `Denied` | `DENIED` | declined |
| `PartiallyCaptured` | `PARTIALLY_CAPTURED` | |
| `Voided` | `VOIDED` | cancelled |
| `Pending` | `PENDING` | in review |

**Error:** Case A — `SdkException<AuthorizeOrderError>`
- `TryGetError(out Error error)` — 400, 401, 403, 404, 422, 500
- `TryGetRawError(out RawError raw)` — fallback

---

#### Step 2 — CaptureAuthorizedPayment (`client.Payments`, source: `map/operations/Payments.md`)

**Signature:**
```
Task<CapturedPayment> CaptureAuthorizedPayment(
    string authorizationId,              // from AuthorizationWithAdditionalData.Id
    string? payPalMockResponse,          // must pass explicitly — null to skip
    string? payPalRequestId,             // must pass explicitly
    string? payPalAuthAssertion,         // must pass explicitly
    CaptureRequest? body,                // must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
Pass `prefer: "return=representation"` to receive `SellerReceivableBreakdown`.

**Request model — `CaptureRequest`** (source: `Models/CaptureRequest.cs`):

| C# name (wire_name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | omit for full capture; set for partial |
| `FinalCapture (final_capture)` | `bool? = false` | `true` to prevent further captures |
| `NoteToPayer (note_to_payer)` | `string?` | optional |

**Response — `CapturedPayment`** (source: `Models/CapturedPayment.cs`):

| C# name (wire_name) | Type | Read for |
|---|---|---|
| `Id (id)` | `string?` | capture ID — store for refunds |
| `Status (status)` | `CaptureStatus?` | expect `CaptureStatus.Completed` |
| `Amount (amount)` | `Money?` | captured amount (gross) |
| `SellerReceivableBreakdown (seller_receivable_breakdown)` | `SellerReceivableBreakdown?` | fee + net |

**`SellerReceivableBreakdown`** (source: `Models/SellerReceivableBreakdown.cs`):

| C# name (wire_name) | Type |
|---|---|
| `GrossAmount (gross_amount)` | `Money` (required) |
| `PaypalFee (paypal_fee)` | `Money?` |
| `NetAmount (net_amount)` | `Money?` |

**`Money`** (source: `Models/Money.cs`): `CurrencyCode (currency_code): string`, `Value (value): string`

**Error:** Case A — `SdkException<CaptureAuthorizedPaymentError>`
- `TryGetError(out Error error)` — 400, 401, 403, 404, 409, 422
- `TryGetNoContent(out RawError raw)` — 500
- `TryGetRawError(out RawError raw)` — fallback

**Auth expiry / re-auth logic (Step 3 integration):**
- Before calling capture: read `authorization.ExpirationTime` (stored from Step 1b)
- If expired and `(DateTime.UtcNow - authorization.CreateTime).TotalDays < 30`: call `ReauthorizePayment` (Step 3), then retry capture with new authorization_id
- If `>= 30 days` from original auth creation: re-auth not possible — return structured error to caller ("authorization expired; new order required") UNVERIFIED: exact PayPal error `Name` field for expired-auth capture — detect from `Error.Name` at the catch boundary and compare to re-auth eligibility
- If capture throws 422 or 404 with the auth already expired: the same escalation path applies

**`CaptureStatus` enum** (namespace `PayPalServerSdk.Models.Enums`): `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed`

---

#### Step 3 — ReauthorizePayment (`client.Payments`, source: `map/operations/Payments.md`)

**Signature:**
```
Task<PaymentAuthorization> ReauthorizePayment(
    string authorizationId,
    string? payPalRequestId,    // must pass explicitly — null to skip
    string? payPalAuthAssertion, // must pass explicitly
    ReauthorizeRequest? body,   // must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

**Request model — `ReauthorizeRequest`** (source: `Models/ReauthorizeRequest.cs`):

| C# name (wire_name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | new authorized amount; must match capture amount |

Notes from operation doc: re-auth is valid from days 4–29 after original auth. Each re-auth has a new 3-day honor period. US: up to 115% of original amount, max +$75.

**Response — `PaymentAuthorization`** (source: `Models/PaymentAuthorization.cs`):

| C# name (wire_name) | Type | Read for |
|---|---|---|
| `Id (id)` | `string?` | **new authorization_id — use this for the subsequent capture** |
| `Status (status)` | `AuthorizationStatus?` | |
| `ExpirationTime (expiration_time)` | `string?` | new expiry |

**Error:** Case A — `SdkException<ReauthorizePaymentError>`
- `TryGetError(out Error error)` — 400, 401, 403, 404, 422
- `TryGetNoContent(out RawError raw)` — 500
- `TryGetRawError(out RawError raw)` — fallback

---

#### Step 4 — VoidPayment (`client.Payments`, source: `map/operations/Payments.md`)

**Signature:**
```
Task<PaymentAuthorization> VoidPayment(
    string authorizationId,
    string? payPalMockResponse,    // must pass explicitly — null to skip
    string? payPalAuthAssertion,   // must pass explicitly
    string? payPalRequestId,       // must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

**Response — `PaymentAuthorization`**: check `Status` = `AuthorizationStatus.Voided`

**Error:** Case A — `SdkException<VoidPaymentError>`
- `TryGetError(out Error error)` — 401, 403, 404, 409, 422
- `TryGetNoContent(out RawError raw)` — 500
- `TryGetRawError(out RawError raw)` — fallback

---

#### Step 5 — RefundCapturedPayment (`client.Payments`, source: `map/operations/Payments.md`)

**Signature:**
```
Task<Refund> RefundCapturedPayment(
    string captureId,                    // from CapturedPayment.Id
    string? payPalMockResponse,          // must pass explicitly — null to skip
    string? payPalRequestId,             // IDEMPOTENCY KEY — pass caller-supplied key here
    string? payPalAuthAssertion,         // must pass explicitly — null to skip
    RefundRequest? body,                 // must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

**Idempotency key:** `payPalRequestId` parameter. Repeating the same string returns the same `Refund` result without creating a duplicate refund. Callers MUST supply a stable, unique key per refund intent (e.g., a UUID stored with the refund record).

**Request model — `RefundRequest`** (source: `Models/RefundRequest.cs`):

| C# name (wire_name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | omit for full refund; set for partial |
| `NoteToPayer (note_to_payer)` | `string?` | optional |
| `InvoiceId (invoice_id)` | `string?` | optional |

**Response — `Refund`** (source: `Models/Refund.cs`):

| C# name (wire_name) | Type | Read for |
|---|---|---|
| `Id (id)` | `string?` | **refundId — return to caller** |
| `Status (status)` | `RefundStatus?` | |
| `Amount (amount)` | `Money?` | |
| `SellerPayableBreakdown (seller_payable_breakdown)` | `SellerPayableBreakdown?` | |

**`RefundStatus` enum** (namespace `PayPalServerSdk.Models.Enums`): `Cancelled`, `Failed`, `Pending`, `Completed`

**Error:** Case A — `SdkException<RefundCapturedPaymentError>`
- `TryGetError(out Error error)` — 400, 401, 403, 404, 409, 422
- `TryGetNoContent(out RawError raw)` — 500
- `TryGetRawError(out RawError raw)` — fallback

409 Conflict typically indicates a duplicate refund when a different `payPalRequestId` is used on the same capture. The 409 `Error.Name` and `Error.Details` carry PayPal's conflict description.

---

#### Step 6 — CreatePaymentToken (`client.Vault`, source: `map/operations/Vault.md`)

**Signature:**
```
Task<PaymentTokenResponse> CreatePaymentToken(
    string? payPalRequestId,    // must pass explicitly — null or idempotency key
    PaymentTokenRequest body,   // required
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

**Request model — `PaymentTokenRequest`** (source: `Models/PaymentTokenRequest.cs`):

| C# name (wire_name) | Type | Req? |
|---|---|---|
| `Customer (customer)` | `Customer?` | optional — set to link to our user |
| `PaymentSource (payment_source)` | `PaymentTokenRequestPaymentSource` | required |

**`Customer`** (source: `Models/Customer.cs`):
- `Id (id): string?` — set to our system's user identifier (the `customer_id`)

**`PaymentTokenRequestPaymentSource`** (source: `Models/PaymentTokenRequestPaymentSource.cs`):
- `Card (card): PaymentTokenRequestCard?`

**`PaymentTokenRequestCard`** (source: `Models/PaymentTokenRequestCard.cs`):

| C# name (wire_name) | Type |
|---|---|
| `Number (number)` | `string?` |
| `Expiry (expiry)` | `string?` — format `YYYY-MM` |
| `SecurityCode (security_code)` | `string?` |
| `Name (name)` | `string?` |
| `BillingAddress (billing_address)` | `Address?` |

**Response — `PaymentTokenResponse`** (source: `Models/PaymentTokenResponse.cs`):

| C# name (wire_name) | Type | Read for |
|---|---|---|
| `Id (id)` | `string?` | **vaultId / payment_token — store and return** |
| `Customer (customer)` | `CustomerResponse?` | confirmed customer record |
| `PaymentSource (payment_source)` | `PaymentTokenResponsePaymentSource?` | card descriptor |

**`PaymentTokenResponsePaymentSource`** (source: `Models/PaymentTokenResponsePaymentSource.cs`):
- `Card (card): CardPaymentTokenEntity?`

**`CardPaymentTokenEntity`** (source: `Models/CardPaymentTokenEntity.cs`) — safe card descriptor:

| C# name (wire_name) | Type | Read for |
|---|---|---|
| `LastDigits (last_digits)` | `string?` | last4 |
| `Brand (brand)` | `CardBrand?` | card network |
| `Expiry (expiry)` | `string?` | `YYYY-MM` |
| `Name (name)` | `string?` | cardholder name |
| `Type (type)` | `CardType?` | credit/debit |

Never expose `CardPaymentTokenEntity.Number` — that field is not present on this response type (no full PAN is returned).

**`CardBrand` enum** (namespace `PayPalServerSdk.Models.Enums`): `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, … (see enums.md for full list)

**Error:** Case A — `SdkException<CreatePaymentTokenError>`
- `TryGetError1(out Error1 error)` — 400, 403, 404, 422, 500
- `TryGetRawError(out RawError raw)` — fallback

Note: Vault operations use `TryGetError1(out Error1)`, NOT `TryGetError(out Error)`. `Error1` (source: `Models/Error1.cs`) has the same shape (`Name`, `Message`, `DebugId`, `Details`).

---

#### Step 7 — ListCustomerPaymentTokens (`client.Vault`, source: `map/operations/Vault.md`)

**Signature:**
```
Task<CustomerVaultPaymentTokensResponse> ListCustomerPaymentTokens(
    string customerId,           // our user identifier
    int? pageSize = 5,
    int? page = 1,
    bool? totalRequired = false,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
Wire query params: `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired`.

**Response — `CustomerVaultPaymentTokensResponse`** (source: `Models/CustomerVaultPaymentTokensResponse.cs`):

| C# name (wire_name) | Type | Read for |
|---|---|---|
| `PaymentTokens (payment_tokens)` | `IReadOnlyList<PaymentTokenResponse>?` | list of tokens |
| `TotalItems (total_items)` | `int?` | total count |
| `TotalPages (total_pages)` | `int?` | for pagination |

This operation has no SDK-level auto-pagination. Caller must loop on `page` manually if `totalPages > 1`. Note the default `pageSize` is 5 — increase to e.g. 20 for practical listing.

**Error:** Case A — `SdkException<ListCustomerPaymentTokensError>`
- `TryGetError1(out Error1 error)` — 400, 403, 500
- `TryGetRawError(out RawError raw)` — fallback

---

#### Step 8 — DeletePaymentToken (`client.Vault`, source: `map/operations/Vault.md`)

**Signature:**
```
Task DeletePaymentToken(
    string id,                   // PaymentTokenResponse.Id (the vaultId)
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
Returns `void`. A successful delete produces no response body.

**Error:** Case A — `SdkException<DeletePaymentTokenError>`
- `TryGetError1(out Error1 error)` — 400, 403, 500
- `TryGetRawError(out RawError raw)` — fallback

---

#### Step 9 — Pay with vault token

Use the same `CreateOrder` + `AuthorizeOrder` flow as Steps 1a + 1b.

In `OrderRequest.PaymentSource`:
```
PaymentSource = new PaymentSource
{
    Token = new Token
    {
        Id = vaultId,                         // PaymentTokenResponse.Id from Step 6
        Type = TokenType.BillingAgreement     // only enum value available
    }
}
```
In `OrderAuthorizeRequest.PaymentSource`:
```
PaymentSource = new OrderAuthorizeRequestPaymentSource
{
    Token = new Token
    {
        Id = vaultId,
        Type = TokenType.BillingAgreement
    }
}
```

UNVERIFIED: whether the live PayPal API accepts a `CreatePaymentToken`-generated token ID via `Token.Type = BillingAgreement` in the authorize flow. The SDK has only one `TokenType` member (`BillingAgreement`). If the live wire rejects this, fall back to placing the token ID in `CardRequest.VaultId` instead, and handle defensively.

---

#### Step 10 — SearchTransactions (`client.TransactionSearch`, source: `map/operations/TransactionSearch.md`)

**Signature:**
```
Task<SearchResponse> SearchTransactions(
    string startDate,                     // ISO-8601 required
    string endDate,                       // ISO-8601 required
    string? transactionId,               // must pass explicitly — null to skip
    string? transactionType,             // must pass explicitly
    string? transactionStatus,           // must pass explicitly
    string? transactionAmount,           // must pass explicitly
    string? transactionCurrency,         // must pass explicitly
    string? paymentInstrumentType,       // must pass explicitly
    string? storeId,                     // must pass explicitly
    string? terminalId,                  // must pass explicitly
    string? fields = "transaction_info",
    string? balanceAffectingRecordsOnly = "Y",
    int? pageSize = 100,
    int? page = 1,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
Wire query params: `start_date`, `end_date`, `transaction_id`, `transaction_type`, `transaction_status`, `transaction_amount`, `transaction_currency`, `payment_instrument_type`, `store_id`, `terminal_id`, `fields`, `balance_affecting_records_only`, `page_size`, `page`.

**Pagination loop to fetch ALL pages:**
```csharp
int currentPage = 1;
int totalPages;
do
{
    var response = await client.TransactionSearch.SearchTransactions(
        startDate: startDate, endDate: endDate,
        transactionId: null, transactionType: null, transactionStatus: null,
        transactionAmount: null, transactionCurrency: null,
        paymentInstrumentType: null, storeId: null, terminalId: null,
        page: currentPage, ct: ct);
    totalPages = response.TotalPages ?? 1;
    // accumulate response.TransactionDetails
    currentPage++;
} while (currentPage <= totalPages);
```
The SDK provides no auto-pagination for this operation. `pageSize` max is 100 (default).

**Response — `SearchResponse`** (source: `Models/SearchResponse.cs`):

| C# name (wire_name) | Type |
|---|---|
| `TransactionDetails (transaction_details)` | `IReadOnlyList<TransactionDetails>?` |
| `Page (page)` | `int?` |
| `TotalPages (total_pages)` | `int?` |
| `TotalItems (total_items)` | `int?` |

**`TransactionDetails`** (source: `Models/TransactionDetails.cs`):
- `TransactionInfo (transaction_info): TransactionInformation?`

**`TransactionInformation`** (source: `Models/TransactionInformation.cs`) — fields for reconciliation:

| C# name (wire_name) | Type | Maps to |
|---|---|---|
| `TransactionId (transaction_id)` | `string?` | transaction_id |
| `TransactionAmount (transaction_amount)` | `Money?` | amount |
| `FeeAmount (fee_amount)` | `Money?` | fee |
| `TransactionStatus (transaction_status)` | `string?` | status (raw string, not enum) |
| `TransactionInitiationDate (transaction_initiation_date)` | `string?` | create_time |
| `PaypalReferenceId (paypal_reference_id)` | `string?` | associated order reference |

**Error: Case B** — `SdkException<RawError>` (this is the ONE Case B operation in this SDK)
```csharp
catch (SdkException<RawError> ex)
{
    var status = ex.Error.StatusCode;    // HttpStatusCode
    var body   = ex.Error.ReadAsString();
}
```
No `TryGet…` accessors — use raw bytes/string.

---

### 2.4 Error model — `Error` vs `Error1`

| Type | Source | Used by |
|---|---|---|
| `Error` (source: `Models/Error.cs`) | `TryGetError(out Error)` | Orders and Payments operations |
| `Error1` (source: `Models/Error1.cs`) | `TryGetError1(out Error1)` | Vault operations (all 6) |

Both have: `Name (name): string`, `Message (message): string`, `DebugId (debug_id): string`, `Details (details): IReadOnlyList<…>?`

**Shared `TryGetNoContent`:** Several Payments operations (CaptureAuthorizedPayment, VoidPayment, ReauthorizePayment, RefundCapturedPayment) expose `TryGetNoContent(out RawError)` for HTTP 500. Check this in addition to `TryGetError`.

---

### 2.5 Environment & base URL configuration

| Config key | SDK option | Notes |
|---|---|---|
| `PayPal:ClientId` | `options.Oauth2.OAuthClientId` (exact property name per `dotnet-authentication`) | |
| `PayPal:ClientSecret` | `options.Oauth2.OAuthClientSecret` | |
| `PayPal:BaseUrl` | `options.Server` (type `ServerOptions`, exact property per `dotnet-configuration-resilience`) | override ALL calls including token endpoint |
| `PayPal:Currency` | application code | pass as `CurrencyCode` in `AmountWithBreakdown` |

When `PayPal:BaseUrl` is set and overrides the token endpoint URL, the OAuth2 token fetch must also target the custom base. Whether `options.Server` covers the token-fetch endpoint or whether a custom `Oauth2TokenStrategy` is required is UNVERIFIED at the map level — **MUST load `dotnet-authentication`** and **`dotnet-configuration-resilience`** before wiring.

---

## 3. Trap Notes

⚠ Steps 1a, 1b, 2, 5 — the `prefer` parameter defaults to `"return=minimal"`. The response body may omit nested objects (e.g., `SellerReceivableBreakdown`, `Authorizations` list) when minimal. Pass `prefer: "return=representation"` on any call where the response body is read. **MUST load `dotnet-calling-endpoints`** for parameter-passing mechanics.

⚠ Steps 1a, 1b, 2, 4, 5 — all nullable params before `body` have no C# default and MUST be passed explicitly (as `null` to skip). Positional calls silently mis-bind. Use named arguments. **MUST load `dotnet-calling-endpoints`** before writing the first call.

⚠ Step 2 (auth expiry) — whether the live PayPal error for an expired authorization is reliably detectable from `Error.Name` (vs. `Error.Details[].Issue`) is UNVERIFIED. Implement defensive check: inspect both `Error.Name` and `Error.Details` for known expiry indicators; fall back to checking `AuthorizationWithAdditionalData.ExpirationTime` before attempting capture.

⚠ Steps 6, 7, 8 — Vault error accessor is `TryGetError1(out Error1)`, NOT `TryGetError(out Error)`. Using the wrong accessor at the catch boundary compiles but returns `false` at runtime, leaking the error. **MUST load `dotnet-error-handling`** before writing the vault error boundary.

⚠ Step 9 — the token type `TokenType.BillingAgreement` is the only available value and is labeled UNVERIFIED for use with `CreatePaymentToken` vault tokens. Code defensively: catch the error from `AuthorizeOrder` when using a vault token and surface a clear "vault token type incompatible" message if PayPal rejects it.

⚠ Step 10 — `SearchTransactions` is **Case B** (`SdkException<RawError>`) — there is no `TryGetError(out Error)` accessor. A catch ladder that only catches `SdkException<SearchTransactionsError>` will miss the error. **MUST load `dotnet-error-handling`** to write this boundary correctly.

⚠ Step 3 (client registration) — `Timeout` on `RetryOptions` is NOT a whole-call timeout and `HttpMethodsToRetry` has separate semantics from transport-failure retries. **MUST load `dotnet-configuration-resilience`** before wiring retry/timeout options.

⚠ All steps — Auth credentials and base URL: the exact property names on `OAuth2ClientCredentials`, the `ServerOptions` field for base URL override, and whether a custom `Oauth2TokenStrategy` is needed for a non-default token endpoint are NOT fully resolvable from the map. **MUST load `dotnet-authentication`** and **`dotnet-configuration-resilience`** before configuring credentials or the custom base URL.

---

## 4. REQUIRED READING

Load ALL of these skills before implementation starts. This sheet deliberately does not carry their contents — defaults, worked examples, and the parts that a one-line note cannot convey are inside each skill.

| Skill | Step(s) it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 (client construction, `IHttpClientFactory`, DI registration) |
| `dotnet-authentication` | Step 1 (OAuth2 wiring, `OAuth2ClientCredentials` properties, token strategy) |
| `dotnet-calling-endpoints` | Steps 1a–10 (named args, `prefer` param, request body construction) |
| `dotnet-models` | Steps 1a–9 (record init syntax, `StringEnum<T>` construction, null handling) |
| `dotnet-error-handling` | Steps 1a–10 (Case A / Case B ladder, `TryGet…` accessors, JsonException boundary) |
| `dotnet-configuration-resilience` | Step 1 (base URL override, `RetryOptions`, `Timeout` semantics) |
| `dotnet-testing` | All steps (HTTP seam for unit tests, mock responses) |

**JsonException hazard — two directions, opposite handling (MUST load `dotnet-error-handling` before writing the error boundary):**

- A drifted or malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, NOT as `SdkException` — an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match the operation's generated `{Operation}Error` shape throws `JsonException` while the error object is being constructed, so the `JsonException` replaces the `SdkException` and the HTTP status is destroyed — a boundary that maps every `JsonException` to 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## 5. Assumptions & Blockers

| # | Item |
|---|---|
| A1 | `Token.Type = TokenType.BillingAgreement` is assumed to be the correct token type for paying with a `CreatePaymentToken`-vaulted card. The SDK exposes only this one `TokenType` value. UNVERIFIED against live wire — treat as a risk item; test early in sandbox. |
| A2 | The `prefer: "return=representation"` value is assumed sufficient to receive full response fields (SellerReceivableBreakdown, Authorizations list). UNVERIFIED which specific fields are omitted under `"return=minimal"` — defaulting to representation for all operations that read the response body is the safe choice. |
| A3 | Detecting an expired authorization from the capture error response relies on `Error.Name` / `Error.Details[].Issue` fields. The exact PayPal error code for an expired auth is UNVERIFIED — implement defensive multi-field inspection and log the raw error for observability. |
| A4 | Base URL override (`PayPal:BaseUrl`) covering the OAuth2 token endpoint: whether `options.Server` alone covers the token URL or a custom `Oauth2TokenStrategy` is also needed is UNVERIFIED at the map level. Resolves via `dotnet-authentication` + `dotnet-configuration-resilience`. |
| A5 | The integration scopes only Orders, Payments, Vault, and TransactionSearch controllers. Subscriptions controller is out of scope. |
| A6 | Direct card submission (sandbox Visa 4111...) requires the merchant account to be PCI SAQ D compliant or to use PayPal-hosted fields. The `CardRequest` model supports raw number input; the SDK does not enforce PCI compliance at compile time. |
