# PayPal Integration Plan — eShopOnWeb `src/PublicApi`

> SDK: `PayPalServerSdk` (NuGet: `AsadAli.Checkout.Sdk`, tag `v1.0.1`, source commit `9653d18`)

---

## 1. Scope & Sequence

| Step | Endpoint | SDK operations |
|------|----------|----------------|
| 1 | Install SDK | `dotnet add package AsadAli.Checkout.Sdk` |
| 2 | Client & DI registration | `services.AddPayPalServerSdkClient(...)` — MUST load `dotnet-client-initialization` |
| 3 | Auth wiring | `Oauth2ClientCredentials` on options — MUST load `dotnet-authentication` |
| 4 | **Pay — Authorize** `POST /api/orders/{orderId}/pay` | `client.Orders.CreateOrder` → `client.Orders.AuthorizeOrder` |
| 5 | **Pay — Capture** `POST /api/orders/{orderId}/fulfil` | `client.Payments.GetAuthorizedPayment` (check status/expiry) → `client.Payments.ReauthorizePayment` (if stale) → `client.Payments.CaptureAuthorizedPayment` |
| 6 | **Pay — Void** `POST /api/orders/{orderId}/cancel` | `client.Payments.VoidPayment` |
| 7 | **Pay — Refund** `POST /api/orders/{orderId}/refunds` | `client.Payments.RefundCapturedPayment` |
| 8 | **Reconciliation** `GET /api/reconciliation?from=&to=` | `client.TransactionSearch.SearchTransactions` — paginate until `page >= total_pages` |
| 9 | **Vault — Save card** `POST /api/payment-methods` | `client.Vault.CreatePaymentToken` |
| 10 | **Vault — List cards** `GET /api/payment-methods` | `client.Vault.ListCustomerPaymentTokens` |
| 11 | **Vault — Delete card** `DELETE /api/payment-methods/{paymentMethodId}` | `client.Vault.DeletePaymentToken` |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one
> from that type's own map row. Enums, unions, auth, server, and client-config types are spread across
> different child namespaces. Dropping a type to the root or to `.Models` makes the implementer guess
> the wrong `using`, and the build breaks.

### Namespaces required

```csharp
using PayPalServerSdk;                        // client, options
using PayPalServerSdk.Servers;                // ServerEnvironment, DefaultOptions (via ServerOptions.Default.Sandbox)
using PayPalServerSdk.Models;                 // all request/response records
using PayPalServerSdk.Models.Enums;           // CheckoutPaymentIntent, AuthorizationStatus, CaptureStatus, etc.
using PayPalServerSdk.Errors;                 // typed error classes
using PayPalServerSdk.Core.Exceptions;        // SdkException<T>
using PayPalServerSdk.Core.ErrorResponse;     // RawError, ApiError
```

---

### Client construction & auth (source: `PayPalServerSdkClientOptions.cs`, `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs`)

| Item | Value |
|------|-------|
| Options class | `PayPalServerSdkClientOptions` (namespace `PayPalServerSdk`) |
| Environment property | `options.Environment = ServerEnvironment.Sandbox` |
| Auth — client-id/secret | `options.Oauth2 = new OAuth2ClientCredentials { OAuthClientId = cfg["PayPal:ClientId"], OAuthClientSecret = cfg["PayPal:ClientSecret"] }` |
| Custom base URL (ALL calls incl. token) | `options.Server.Default.Sandbox.BaseUrl = cfg["PayPal:BaseUrl"]` — only set when the config key is present; `ServerOptions.Default` is type `DefaultOptions` (`PayPalServerSdk.Servers`); its `Sandbox.BaseUrl` defaults to `"https://api-m.sandbox.paypal.com"`. Verified in source (`Servers/DefaultOptions.cs`, `AuthSchemes.cs`): the token endpoint is also derived from this base URL via `server.Default("/v1/oauth2/token")`. |
| DI method | `services.AddPayPalServerSdkClient(o => { … })` |
| Constructor (non-DI) | `new PayPalServerSdkClient(httpClient, options)` |

---

### Step 4 — Authorize payment (Create + Authorize order)

**Phase A — CreateOrder** (set intent = AUTHORIZE and embed payment source)

- **Controller**: `client.Orders`
- **Method**: `CreateOrder`
- **Signature**: `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - The 5 nullable header params (`payPalMockResponse` … `payPalAuthAssertion`) have **no default** — must be passed explicitly; pass `null` to skip.
  - `payPalRequestId` — use the eShop `orderId` (or a deterministic hash of it) for idempotency; same key = PayPal deduplicates.
- **Returns**: `Order` (namespace `PayPalServerSdk.Models`)
- **Error**: `SdkException<CreateOrderError>` — Case A. `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback]
- **Source**: `operations/Orders.md`

**`OrderRequest` fields** (source: `records-1-Ac-Pa.md`)

| C# name | Wire name | Type | Required? |
|---------|-----------|------|-----------|
| `Intent` | `intent` | `CheckoutPaymentIntent` | **req** |
| `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnitRequest>` | **req** |
| `PaymentSource` | `payment_source` | `PaymentSource?` | optional |
| `Payer` | `payer` | `Payer?` | optional |
| `ApplicationContext` | `application_context` | `OrderApplicationContext?` | optional |

Set `Intent = CheckoutPaymentIntent.Authorize` (wire: `"AUTHORIZE"`) — source: `enums.md`.

**`PurchaseUnitRequest` fields** (source: `records-2-Pa-Ve.md`)

| C# name | Wire name | Type | Required? |
|---------|-----------|------|-----------|
| `Amount` | `amount` | `AmountWithBreakdown` | **req** |
| `CustomId` | `custom_id` | `string?` | optional — store eShop orderId here for reconciliation |
| `InvoiceId` | `invoice_id` | `string?` | optional |

**`AmountWithBreakdown` fields** (source: `records-1-Ac-Pa.md`)

| C# name | Wire name | Type | Required? |
|---------|-----------|------|-----------|
| `CurrencyCode` | `currency_code` | `string` | **req** — use `cfg["PayPal:Currency"]` |
| `Value` | `value` | `string` | **req** — decimal as string, e.g. `"19.99"` |

**`PaymentSource` — card payment** (source: `records-2-Pa-Ve.md`)

Set `PaymentSource.Card` to a `CardRequest` (source: `records-1-Ac-Pa.md`):

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `Number` | `number` | `string?` | raw card number e.g. `"4111111111111111"` |
| `Expiry` | `expiry` | `string?` | `"YYYY-MM"` format |
| `SecurityCode` | `security_code` | `string?` | CVV |
| `Name` | `name` | `string?` | cardholder name |
| `BillingAddress` | `billing_address` | `Address?` | see `Address` below |

`Address` required field: `CountryCode (country_code): string !req`. All other fields (`AddressLine1`, `AdminArea2`, `PostalCode`, etc.) are optional. Source: `records-1-Ac-Pa.md`.

**`PaymentSource` — vault token payment**

Set `PaymentSource.Token` to a `Token` (source: `records-2-Pa-Ve.md`):

| C# name | Wire name | Type | Required? |
|---------|-----------|------|-----------|
| `Id` | `id` | `string` | **req** — the vault payment-token id |
| `Type` | `type` | `TokenType` | **req** — `TokenType.BillingAgreement` (wire: `"BILLING_AGREEMENT"`) — source: `enums.md` |

**Phase B — AuthorizeOrder** (after CreateOrder returns `OrderStatus.Approved` or payment_source embedded)

- **Controller**: `client.Orders`
- **Method**: `AuthorizeOrder`
- **Signature**: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `id` = PayPal order ID (from CreateOrder response `Order.Id`)
  - 5 nullable params — must pass explicitly
  - `payPalRequestId` — same idempotency key as used for CreateOrder; deduplicates double-clicks
- **Returns**: `OrderAuthorizeResponse` (source: `records-1-Ac-Pa.md`)
  - Authorization ID: `response.PurchaseUnits[0].Payments.Authorizations[0].Id`
  - Authorization status: `response.PurchaseUnits[0].Payments.Authorizations[0].Status` — type `AuthorizationStatus?`
  - Expiration time: `response.PurchaseUnits[0].Payments.Authorizations[0].ExpirationTime` — ISO-8601 string
- **Error**: `SdkException<AuthorizeOrderError>` — Case A. `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]
- **Source**: `operations/Orders.md`

`OrderAuthorizeRequest` has one field: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`. For embedded card payment in CreateOrder the body can be `null`. Source: `records-1-Ac-Pa.md`.

**Idempotency rule**: Use the same `payPalRequestId` for a given eShop order in both CreateOrder and AuthorizeOrder. If PayPal returns the already-authorized order (HTTP 200 with the same response), do not create a second authorization. Store the PayPal `orderId` and `authorizationId` in eShop's database after first successful authorization.

---

### Step 5 — Capture payment

**5A — Check authorization status**

- **Controller**: `client.Payments`
- **Method**: `GetAuthorizedPayment`
- **Signature**: `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalMockResponse`, `payPalAuthAssertion` — must pass explicitly (pass `null`)
- **Returns**: `PaymentAuthorization` (source: `records-2-Pa-Ve.md`)
  - `Status` — `AuthorizationStatus?`
  - `ExpirationTime` — `string?` (ISO-8601)
- **Error**: `SdkException<GetAuthorizedPaymentError>` — Case A. `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]
- **Source**: `operations/Payments.md`

**Authorization staleness logic**:
- `AuthorizationStatus.Created` (wire `"CREATED"`) — within 3-day honor period, can capture directly.
- `AuthorizationStatus.Pending` (wire `"PENDING"`) — blocked; do not capture; surface error to caller.
- `AuthorizationStatus.Captured` / `PartiallyCaptured` — already captured; idempotent success path.
- `AuthorizationStatus.Voided` / `Denied` — terminal; return actionable error.
- If `ExpirationTime` has passed and status is still `Created`, attempt reauthorize (Step 5B) before capturing.

**5B — Reauthorize (if stale)**

- **Controller**: `client.Payments`
- **Method**: `ReauthorizePayment`
- **Signature**: `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId`, `payPalAuthAssertion`, `body` — must pass explicitly
- **Request body**: `ReauthorizeRequest` — one optional field: `Amount (amount): Money?`. Per SDK notes: "Supports only the `amount` request parameter." Set to original order amount to ensure same hold. Source: `records-2-Pa-Ve.md`.
- **Returns**: `PaymentAuthorization` — updated authorization with new ID and expiration.
- **Error**: `SdkException<ReauthorizePaymentError>` — Case A. `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]
- **Notes**: Reauthorize is only possible from days 4–29 after original auth. If more than 30 days have elapsed, reauthorize will fail — catch this error, return an actionable message ("authorization expired beyond renewal window, re-collect payment").
- **Source**: `operations/Payments.md`

**5C — Capture**

- **Controller**: `client.Payments`
- **Method**: `CaptureAuthorizedPayment`
- **Signature**: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` — must pass explicitly
  - `payPalRequestId` — idempotency key (e.g. `$"capture-{orderId}"`); same key = deduplicates
- **Request body**: `CaptureRequest` (source: `records-1-Ac-Pa.md`)

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `Amount` | `amount` | `Money?` | optional — omit to capture full authorized amount |
| `FinalCapture` | `final_capture` | `bool? = false` | set `true` to prevent further captures |
| `InvoiceId` | `invoice_id` | `string?` | optional — store eShop orderId |
| `NoteToPayer` | `note_to_payer` | `string?` | optional |

- **Returns**: `CapturedPayment` (source: `records-1-Ac-Pa.md`)

**Captured amount, fee, net proceeds** from `CapturedPayment.SellerReceivableBreakdown` (type `SellerReceivableBreakdown`, source: `records-2-Pa-Ve.md`):

| Field | C# | Wire | Notes |
|-------|----|------|-------|
| Captured (gross) amount | `GrossAmount` | `gross_amount` | `Money !req` |
| PayPal fee | `PaypalFee` | `paypal_fee` | `Money?` |
| Net proceeds | `NetAmount` | `net_amount` | `Money?` |

`Money` fields: `CurrencyCode (currency_code): string !req`, `Value (value): string !req`. Source: `records-1-Ac-Pa.md`.

- **Error**: `SdkException<CaptureAuthorizedPaymentError>` — Case A. `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]
- **Source**: `operations/Payments.md`

---

### Step 6 — Void authorization

- **Controller**: `client.Payments`
- **Method**: `VoidPayment`
- **Signature**: `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` — must pass explicitly (pass `null`)
  - No request body parameter.
- **Returns**: `PaymentAuthorization`
- **Error**: `SdkException<VoidPaymentError>` — Case A. `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]
- **Source**: `operations/Payments.md`

---

### Step 7 — Refund

- **Controller**: `client.Payments`
- **Method**: `RefundCapturedPayment`
- **Signature**: `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `captureId` — the PayPal capture ID from Step 5C
  - `payPalRequestId` — **caller-supplied idempotency key**: same key = deduplicates refund; different key = new partial refund leg
  - `payPalMockResponse`, `payPalAuthAssertion` — must pass explicitly (pass `null`)
- **Request body**: `RefundRequest` (source: `records-2-Pa-Ve.md`)

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `Amount` | `amount` | `Money?` | omit for full refund; set for partial refund |
| `InvoiceId` | `invoice_id` | `string?` | optional |
| `NoteToPayer` | `note_to_payer` | `string?` | optional |
| `CustomId` | `custom_id` | `string?` | optional |

**Partial-refund guard**: The integration must track cumulative refunded amount in eShop's database (sum of all successful refunds for a captureId). Reject any refund request where `requestedAmount + alreadyRefunded > capturedAmount` before calling PayPal. PayPal itself returns HTTP 422 on over-refund, but enforce this locally first to give a clear error message.

- **Returns**: `Refund` (source: `records-2-Pa-Ve.md`)
  - `Id` — refund ID for future reference
  - `Status` — `RefundStatus?`: `Completed`, `Pending`, `Failed`, `Cancelled` (source: `enums.md`)
- **Error**: `SdkException<RefundCapturedPaymentError>` — Case A. `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]
- **Source**: `operations/Payments.md`

---

### Step 8 — Reconciliation report (full-range pagination)

- **Controller**: `client.TransactionSearch`
- **Method**: `SearchTransactions`
- **Signature**: `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - All 8 nullable optional params (`transactionId` … `terminalId`) — **must pass explicitly** (pass `null` to skip)
  - `startDate` / `endDate` — ISO-8601 datetime strings from query params (`from` / `to`)
- **Returns**: `SearchResponse` (source: `records-2-Pa-Ve.md`)
  - `TransactionDetails` — `IReadOnlyList<TransactionDetails>?`
  - `Page` — `int?` — current page
  - `TotalPages` — `int?` — total pages
  - `TotalItems` — `int?`
- **Error**: `SdkException<RawError>` — **Case B** (the only Case B operation in scope). Accessors: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`. Source: `operations/TransactionSearch.md`.

**Pagination pattern** — `SearchTransactions` has no built-in pagination helper; implement a loop:
```
page = 1
do:
    response = await SearchTransactions(startDate, endDate, null, null, null, null, null, null, null, null, page: page, ct: ct)
    accumulate response.TransactionDetails
    page++
while (page <= response.TotalPages)
```
`pageSize` defaults to 100 (maximum per the SDK default). Use named arguments because the operation has many optionals that would mis-bind positionally.

`TransactionDetails` fields (source: `records-2-Pa-Ve.md`): `TransactionInfo (transaction_info): TransactionInformation?`, `PayerInfo (payer_info): PayerInformation?`, etc.

Key `TransactionInformation` fields (source: `records-2-Pa-Ve.md`): `TransactionId`, `TransactionAmount`, `FeeAmount`, `TransactionStatus`, `TransactionInitiationDate`.

---

### Step 9 — Vault a card (save payment method)

The vault flow uses two steps: **CreateSetupToken** (to tokenize the card without requiring browser approval for direct card processing), then **CreatePaymentToken** (to convert the setup token to a permanent vault token).

**Step 9A — CreateSetupToken**

- **Controller**: `client.Vault`
- **Method**: `CreateSetupToken`
- **Signature**: `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId` — must pass explicitly (pass `null` or a shopper-scoped idempotency key)
- **Request body**: `SetupTokenRequest` (source: `records-2-Pa-Ve.md`)

| C# name | Wire name | Type | Required? |
|---------|-----------|------|-----------|
| `PaymentSource` | `payment_source` | `SetupTokenRequestPaymentSource` | **req** |
| `Customer` | `customer` | `Customer?` | optional — set `Id` to eShop shopper's PayPal customer ID if known |

`SetupTokenRequestPaymentSource.Card` — type `SetupTokenRequestCard?` (source: `records-2-Pa-Ve.md`):

| C# name | Wire name | Type | Notes |
|---------|-----------|------|-------|
| `Number` | `number` | `string?` | raw card number |
| `Expiry` | `expiry` | `string?` | `"YYYY-MM"` |
| `SecurityCode` | `security_code` | `string?` | CVV |
| `Name` | `name` | `string?` | cardholder name |
| `BillingAddress` | `billing_address` | `Address?` | |

- **Returns**: `SetupTokenResponse` (source: `records-2-Pa-Ve.md`)
  - `Id` — setup token ID (temporary)
  - `Status` — `PaymentTokenStatus?` (source: `enums.md`): `Created`, `PayerActionRequired`, `Approved`, `Vaulted`, `Tokenized`
- **Error**: `SdkException<CreateSetupTokenError>` — Case A. `TryGetError1(out Error1)` [400, 403, 422, 500] · `TryGetRawError(out RawError)` [fallback]
- **Source**: `operations/Vault.md`

**BLOCKER — Setup token browser approval**: If `SetupTokenResponse.Status == PaymentTokenStatus.PayerActionRequired`, PayPal requires the cardholder to complete an out-of-band approval (redirect/3DS). For a direct server-side card vault this is a **blocker** — the test card `4111 1111 1111 1111` may or may not trigger this. Detect the status, inspect `Links` for a `rel: approve` URL, and surface it as an error to the API caller. Do NOT silently proceed to Step 9B with an unapproved setup token.

**Step 9B — CreatePaymentToken** (after setup token is approved/vaulted)

- **Controller**: `client.Vault`
- **Method**: `CreatePaymentToken`
- **Signature**: `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId` — must pass explicitly
- **Request body**: `PaymentTokenRequest` (source: `records-2-Pa-Ve.md`)

| C# name | Wire name | Type | Required? |
|---------|-----------|------|-----------|
| `PaymentSource` | `payment_source` | `PaymentTokenRequestPaymentSource` | **req** |
| `Customer` | `customer` | `Customer?` | optional |

`PaymentTokenRequestPaymentSource` options (source: `records-2-Pa-Ve.md`):
- `.Token` — type `VaultTokenRequest?`: `Id (id): string !req` = setup token ID from 9A; `Type (type): VaultTokenRequestType !req` = `VaultTokenRequestType.SetupToken` (wire: `"SETUP_TOKEN"`) — source: `enums.md`.

- **Returns**: `PaymentTokenResponse` (source: `records-2-Pa-Ve.md`)
  - `Id` — permanent vault token ID (store this in eShop database as the saved card reference)
  - `Customer.Id` — PayPal customer ID (store per shopper)
  - `PaymentSource.Card` — type `CardPaymentTokenEntity?` (source: `records-1-Ac-Pa.md`)
    - `LastDigits (last_digits): string?` — safe display, last 4
    - `Brand (brand): CardBrand?` — e.g. `CardBrand.Visa` (source: `enums.md`)
    - `Expiry (expiry): string?`
    - **Never** return `Number` — the vault response does not include the full card number; only `LastDigits` and `Brand` are safe to store/display.
- **Error**: `SdkException<CreatePaymentTokenError>` — Case A. `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]
- **Source**: `operations/Vault.md`

---

### Step 10 — List saved cards

- **Controller**: `client.Vault`
- **Method**: `ListCustomerPaymentTokens`
- **Signature**: `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `customerId` — PayPal customer ID stored during Step 9B
- **Returns**: `CustomerVaultPaymentTokensResponse` (source: `records-1-Ac-Pa.md`)
  - `PaymentTokens` — `IReadOnlyList<PaymentTokenResponse>?`
  - `TotalItems`, `TotalPages` — for pagination
  - Each `PaymentTokenResponse.PaymentSource.Card` — `CardPaymentTokenEntity?` with `LastDigits`, `Brand`, `Expiry`
- **Error**: `SdkException<ListCustomerPaymentTokensError>` — Case A. `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback]
- **Source**: `operations/Vault.md`

---

### Step 11 — Delete saved card

- **Controller**: `client.Vault`
- **Method**: `DeletePaymentToken`
- **Signature**: `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `id` — vault payment-token ID
- **Returns**: `void` (Task) — HTTP 204 on success
- **Error**: `SdkException<DeletePaymentTokenError>` — Case A. `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback]
- **Source**: `operations/Vault.md`

---

### Enum value tables (in-scope only)

All enums: namespace `PayPalServerSdk.Models.Enums`. Construct via static member (e.g. `CheckoutPaymentIntent.Authorize`) or `Type.FromValue("wire")`. Source: `enums.md`.

| Enum | Members (C# name → wire value) |
|------|--------------------------------|
| `CheckoutPaymentIntent` | `Capture ("CAPTURE")`, `Authorize ("AUTHORIZE")` |
| `OrderStatus` | `Created ("CREATED")`, `Saved ("SAVED")`, `Approved ("APPROVED")`, `Voided ("VOIDED")`, `Completed ("COMPLETED")`, `PayerActionRequired ("PAYER_ACTION_REQUIRED")` |
| `AuthorizationStatus` | `Created ("CREATED")`, `Captured ("CAPTURED")`, `Denied ("DENIED")`, `PartiallyCaptured ("PARTIALLY_CAPTURED")`, `Voided ("VOIDED")`, `Pending ("PENDING")` |
| `CaptureStatus` | `Completed ("COMPLETED")`, `Declined ("DECLINED")`, `PartiallyRefunded ("PARTIALLY_REFUNDED")`, `Pending ("PENDING")`, `Refunded ("REFUNDED")`, `Failed ("FAILED")` |
| `RefundStatus` | `Cancelled ("CANCELLED")`, `Failed ("FAILED")`, `Pending ("PENDING")`, `Completed ("COMPLETED")` |
| `PaymentTokenStatus` | `Created ("CREATED")`, `PayerActionRequired ("PAYER_ACTION_REQUIRED")`, `Approved ("APPROVED")`, `Vaulted ("VAULTED")`, `Tokenized ("TOKENIZED")` |
| `TokenType` | `BillingAgreement ("BILLING_AGREEMENT")` |
| `VaultTokenRequestType` | `SetupToken ("SETUP_TOKEN")` |
| `StoreInVaultInstruction` | `OnSuccess ("ON_SUCCESS")` |
| `CardBrand` | `Visa ("VISA")`, `Mastercard ("MASTERCARD")`, `Amex ("AMEX")`, `Discover ("DISCOVER")` (plus many more in `enums.md`) |

---

## 3. Trap Notes

> These name the hazard and its consequence. The companion skill carries the resolution, defaults, and
> worked examples — do not assume the one-liner below settles the implementation.

⚠ **Step 2 (client registration)** — The SDK client wraps an `HttpClient`; creating a new `HttpClient` per request exhausts socket handles under load. The DI registration must use `IHttpClientFactory` with a long-lived handler. **MUST load `dotnet-client-initialization`** before wiring the service container.

⚠ **Step 3 (auth)** — `OAuth2ClientCredentials` must be set on `options` before the client is constructed. Secrets must come from config (`IConfiguration`), not hardcoded. Token caching and refresh behavior are governed by `Oauth2TokenStrategy`. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ **Step 3 (custom base URL)** — `options.Server.Default.Sandbox.BaseUrl` overrides the base URL for ALL calls, including the token endpoint (`/v1/oauth2/token` uses the same base URL per `AuthSchemes.cs`). Only apply the override when `PayPal:BaseUrl` is present in config. **MUST load `dotnet-configuration-resilience`** for correct wiring.

⚠ **Step 4 / 7 (POST idempotency + retry)** — `HttpMethodsToRetry` gates only the status-code retry trigger, but transport failures (`HttpRequestException`) are retried on every verb including `POST`. A `CreateOrder`, `AuthorizeOrder`, or `RefundCapturedPayment` can therefore execute more than once on transport retry. Always pass `payPalRequestId` (idempotency key) for every mutating operation and store the PayPal-side result before acknowledging success to the caller. **MUST load `dotnet-configuration-resilience`** before tuning retry options.

⚠ **Steps 4–11 (calling operations with many optionals)** — Operations like `SearchTransactions` have 14 parameters; a positional call will mis-bind optional params. Always use named arguments. **MUST load `dotnet-calling-endpoints`** before writing the first call.

⚠ **Step 4 (CardRequest — PCI scope)** — Passing `Number`, `SecurityCode`, `Expiry` directly via `CardRequest` requires **PCI SAQ D compliance** (noted in the SDK's `CardRequest` summary). Confirm PCI scope with the eShopOnWeb project owner before going live. The test card `4111 1111 1111 1111` works in sandbox without this concern.

⚠ **Steps 5–7 (authorization state machine)** — Only `AuthorizationStatus.Created` can be captured or voided. Attempt on `Voided`, `Denied`, or fully `Captured` returns 422. Check status from `GetAuthorizedPayment` before proceeding. **MUST load `dotnet-error-handling`** for the 422 path.

⚠ **Steps 4, 9 (browser-approval blocker)** — Direct card processing (no browser redirect) works only when the card does not trigger 3DS/SCA. If `OrderStatus.PayerActionRequired` or `PaymentTokenStatus.PayerActionRequired` is returned, a browser approval URL is required. This integration does NOT implement a browser redirect flow. Surface the `rel: approve` link from the `Links` array as an error to the caller. **This is a stated blocker for the test card in non-sandbox environments.**

⚠ **Step 8 (SearchTransactions — Case B error)** — `SearchTransactions` is the **only Case B operation** in this integration (`SdkException<RawError>`, no typed accessor). The catch ladder for all other operations must not absorb `SdkException<RawError>` from `SearchTransactions` as though it were a typed error. **MUST load `dotnet-error-handling`** for correct ladder structure.

⚠ **Steps 4–11 (enums are not C# enums)** — All enums are `StringEnum<T>` records. Construct via static member (`CheckoutPaymentIntent.Authorize`) or `Type.FromValue("AUTHORIZE")`. Do not use `new`, cast, or comparison with raw strings without `==` overload. **MUST load `dotnet-models`** before building any request with enum fields.

---

## 4. REQUIRED READING

Load every skill listed below **before implementation starts**. The contract sheet above deliberately
does not carry their contents — the skills carry defaults, worked examples, and hazard resolutions
that a one-line trap note cannot fully substitute.

| Skill | Steps governed |
|-------|----------------|
| `dotnet-client-initialization` | Step 2 — client DI registration, HttpClient lifetime |
| `dotnet-authentication` | Step 3 — OAuth2 credentials wiring, token strategy |
| `dotnet-calling-endpoints` | Steps 4–11 — calling operations, named args, request/response shape |
| `dotnet-models` | Steps 4–11 — StringEnum construction, record initializers, required fields |
| `dotnet-error-handling` | Steps 4–11 — Case A vs Case B ladder, SdkException, TryGet accessors |
| `dotnet-configuration-resilience` | Steps 2–3, 8 — retry semantics, Timeout scope, base URL override, pagination |
| `dotnet-testing` | All — SDK test seam (HttpClient constructor argument), mocking strategy |

**JsonException boundary — mandatory awareness (load `dotnet-error-handling` before writing ANY catch ladder):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException`
  from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets
  it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

---

## 5. Assumptions & Blockers

### Assumptions

| # | Assumption |
|---|-----------|
| A1 | The `PayPal:Currency` config key holds an ISO-4217 code (e.g. `"USD"`) and is always populated. |
| A2 | eShopOnWeb's `src/PublicApi` project is ASP.NET Core 8 with a DI container (`IServiceCollection`) already wired. |
| A3 | The eShop database schema will be extended to store: PayPal `orderId`, `authorizationId`, `captureId`, `customerId` per shopper, vault token IDs, and cumulative refunded amounts. Schema migration is outside this plan. |
| A4 | The shopper identity (for `customerId` in vault operations) is derived from the authenticated user's claim in `src/PublicApi`. The claim key is an assumption — verify with the project team. |
| A5 | Reconciliation `from`/`to` query params arrive as ISO-8601 datetime strings and are passed verbatim to `startDate`/`endDate`. |

### Blockers

| # | Blocker |
|---|---------|
| B1 | **Browser-approval flow not implemented.** If PayPal returns `PayerActionRequired` for direct card processing (Visa `4111 1111 1111 1111`) in any flow, the integration cannot complete the operation without a browser redirect. This must be tested against sandbox before go-live. PayPal may require 3DS/SCA for card vaulting (`CreateSetupToken`) even in sandbox. |
| B2 | **PCI SAQ D compliance.** Passing raw card numbers (`CardRequest.Number`) through the server requires PCI SAQ D. Confirm with the project owner whether hosted fields or a PayPal JS SDK tokenization flow is required for production. The sandbox test card is unaffected. |
