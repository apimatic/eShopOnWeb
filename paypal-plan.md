# PayPal .NET SDK — Integration Plan
## Target: `src/PublicApi/PublicApi.csproj` (net8.0)

---

## 1. Scope & Sequence

| Step | Description | Operations used |
|---|---|---|
| 1 | Install NuGet package; register `PayPalServerSdkClient` in DI with credentials, environment, BaseUrl override | — |
| 2 | **Authorize payment** — create order frame (AUTHORIZE intent), then authorize with card or vault token | `client.Orders.CreateOrder` → `client.Orders.AuthorizeOrder` |
| 3 | **Capture payment** — take held funds; report amounts | `client.Payments.CaptureAuthorizedPayment` |
| 4 | **Void authorization** — release hold on cancel | `client.Payments.VoidPayment` |
| 5 | **Refund captured payment** — full or partial; idempotent via caller key | `client.Payments.RefundCapturedPayment` |
| 6 | **Re-authorize stale authorization** — renew before fulfilment | `client.Payments.ReauthorizePayment` |
| 7 | **Save a card** — vault via setup-token flow or direct card vault | `client.Vault.CreateSetupToken` → `client.Vault.CreatePaymentToken` |
| 8 | **List saved cards** — all vault payment tokens for a customer (all pages) | `client.Vault.ListCustomerPaymentTokens` |
| 9 | **Delete saved card** — remove a vault payment token | `client.Vault.DeletePaymentToken` |
| 10 | **Transaction report** — query all pages of a date-range search; match to eShop order IDs | `client.TransactionSearch.SearchTransactions` |

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

### 2a. Required `using` directives

| Namespace | Contents used |
|---|---|
| `PayPalServerSdk` | `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` |
| `PayPalServerSdk.Servers` | `ServerEnvironment`, `DefaultOptions` |
| `PayPalServerSdk.Models` | All request/response records |
| `PayPalServerSdk.Models.Enums` | `CheckoutPaymentIntent`, `CardBrand`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus`, `VaultTokenRequestType` |
| `PayPalServerSdk.Errors` | `CreateOrderError`, `AuthorizeOrderError`, `CaptureAuthorizedPaymentError`, `VoidPaymentError`, `RefundCapturedPaymentError`, `ReauthorizePaymentError`, `CreatePaymentTokenError`, `CreateSetupTokenError`, `ListCustomerPaymentTokensError`, `DeletePaymentTokenError` |
| `PayPalServerSdk.Core.Exceptions` | `SdkException<T>` |
| `PayPalServerSdk.Core.ErrorResponse` | `RawError` |
| `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | `OAuth2ClientCredentials` |

C# does **not** import child namespaces transitively — each namespace above needs its own `using`. Omitting any one produces `CS0103`/`CS0246` on the types it covers.

### 2b. Client construction & auth

Source: `PayPalServerSdkClientOptions.cs`, `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`

```csharp
// OAuth2ClientCredentials (namespace PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials)
// Fields: ClientId (required string), ClientSecret (required string), Scope (string?)

var options = new PayPalServerSdkClientOptions
{
    Environment = ServerEnvironment.Sandbox,   // only documented value
    Oauth2 = new OAuth2ClientCredentials
    {
        ClientId     = config["PayPal:ClientId"]!,
        ClientSecret = config["PayPal:ClientSecret"]!,
    },
};

// BaseUrl override — applies to ALL calls including the token endpoint.
// ServerOptions (namespace PayPalServerSdk) has: Default (DefaultOptions from PayPalServerSdk.Servers)
// DefaultOptions.Sandbox.BaseUrl (string) defaults to "https://api-m.sandbox.paypal.com"
var baseUrl = config["PayPal:BaseUrl"];
if (!string.IsNullOrEmpty(baseUrl))
    options.Server.Default.Sandbox.BaseUrl = baseUrl;

// DI registration:
services.AddPayPalServerSdkClient(o =>
{
    o.Environment = ServerEnvironment.Sandbox;
    o.Oauth2 = new OAuth2ClientCredentials { ClientId = ..., ClientSecret = ... };
    if (!string.IsNullOrEmpty(baseUrl)) o.Server.Default.Sandbox.BaseUrl = baseUrl;
});
```

### 2c. Operations contract table

---

#### Step 2 — Authorize Payment

**Sub-step A: CreateOrder**

| Item | Value |
|---|---|
| Controller | `client.Orders` (source: `Api/Orders.cs`) |
| Method | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly (no default, nullable) | `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion` — pass `null` for each to skip |
| `prefer` | Pass `"return=representation"` to receive full Order response including authorization details |
| Returns | `PayPalServerSdk.Models.Order` |
| Error case | Case A — `SdkException<CreateOrderError>` |
| Error accessors | `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback] |
| No-throw variant | absent |
| Map source | `operations/Orders.md` |

**`OrderRequest` fields** (namespace `PayPalServerSdk.Models`):

| Field (wire name) | Type | Required? |
|---|---|---|
| `Intent (intent)` | `CheckoutPaymentIntent` | required |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | required |
| `PaymentSource (payment_source)` | `PaymentSource?` | optional (supply card/token here for inline processing) |

**`PurchaseUnitRequest` required fields** (source: `records-1-Ac-Pa.md`):

| Field (wire name) | Type | Required? |
|---|---|---|
| `Amount (amount)` | `AmountWithBreakdown` | required |

**`AmountWithBreakdown` fields**:

| Field (wire name) | Type | Required? |
|---|---|---|
| `CurrencyCode (currency_code)` | `string` | required |
| `Value (value)` | `string` | required |

**Idempotency for CreateOrder**: supply `payPalRequestId` as a stable per-order key. PayPal will return the same `Order` on duplicates.

---

**Sub-step B: AuthorizeOrder**

| Item | Value |
|---|---|
| Controller | `client.Orders` |
| Method | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `id` | Order ID returned from `CreateOrder` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalClientMetadataId`, `payPalAuthAssertion`, `body` |
| `prefer` | Pass `"return=representation"` to receive the authorization ID inline |
| Returns | `PayPalServerSdk.Models.OrderAuthorizeResponse` |
| Error case | Case A — `SdkException<AuthorizeOrderError>` |
| Error accessors | `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback] |
| Map source | `operations/Orders.md` |

**`OrderAuthorizeRequest` fields** (source: `records-1-Ac-Pa.md`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `PaymentSource (payment_source)` | `OrderAuthorizeRequestPaymentSource?` | optional; supply card/token here if not in CreateOrder |

**`OrderAuthorizeRequestPaymentSource` fields**:

| Field (wire name) | Type | Notes |
|---|---|---|
| `Card (card)` | `CardRequest?` | for one-off card or vault card token |
| `Token (token)` | `Token?` | for billing-agreement token |

**`CardRequest` fields used** (source: `records-1-Ac-Pa.md`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Number (number)` | `string?` | PAN for one-off card (e.g. `"4111111111111111"`) |
| `Expiry (expiry)` | `string?` | Format `YYYY-MM` |
| `SecurityCode (security_code)` | `string?` | CVV |
| `VaultId (vault_id)` | `string?` | Vault payment token ID for saved card (set this instead of Number/Expiry/SecurityCode) |

**Reading authorization ID** from `OrderAuthorizeResponse` (with `prefer:"return=representation"`):
```
response.PurchaseUnits?[0].Payments?.Authorizations?[0].Id
```
Path: `OrderAuthorizeResponse.PurchaseUnits: IReadOnlyList<PurchaseUnit>?` → `PurchaseUnit.Payments: PaymentCollection?` → `PaymentCollection.Authorizations: IReadOnlyList<AuthorizationWithAdditionalData>?` → `AuthorizationWithAdditionalData.Id: string?`

**Idempotency for AuthorizeOrder**: supply `payPalRequestId`. Same key on retry returns the same authorization.

---

#### Step 3 — Capture Payment

| Item | Value |
|---|---|
| Controller | `client.Payments` (source: `Api/Payments.cs`) |
| Method | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` |
| Idempotency | `payPalRequestId` — supply stable per-capture key; duplicate returns same `CapturedPayment` |
| Returns | `PayPalServerSdk.Models.CapturedPayment` |
| Error case | Case A — `SdkException<CaptureAuthorizedPaymentError>` |
| Error accessors | `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |
| Map source | `operations/Payments.md` |

**`CaptureRequest` fields** (source: `records-1-Ac-Pa.md`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | optional; omit to capture full authorized amount |
| `FinalCapture (final_capture)` | `bool? = false` | set `true` if no further captures expected |
| `InvoiceId (invoice_id)` | `string?` | optional merchant invoice ID |

**Reading amounts from `CapturedPayment`** (source: `records-1-Ac-Pa.md`):

| What | Path |
|---|---|
| Capture ID | `response.Id` |
| Captured gross amount | `response.SellerReceivableBreakdown?.GrossAmount` (`Money`: `.CurrencyCode`, `.Value`) |
| PayPal fee | `response.SellerReceivableBreakdown?.PaypalFee` (`Money`) |
| Net proceeds | `response.SellerReceivableBreakdown?.NetAmount` (`Money`) |
| Status | `response.Status` (`CaptureStatus?`) |

`SellerReceivableBreakdown` (source: `records-2-Pa-Ve.md`): `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?`

---

#### Step 4 — Void Authorization

| Item | Value |
|---|---|
| Controller | `client.Payments` |
| Method | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` |
| No request body | — |
| Returns | `PayPalServerSdk.Models.PaymentAuthorization` — check `response.Status == AuthorizationStatus.Voided` |
| Error case | Case A — `SdkException<VoidPaymentError>` |
| Error accessors | `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |
| Map source | `operations/Payments.md` |

Note: 409 signals the authorization is already voided or captured; treat as idempotent success in the void path.

---

#### Step 5 — Refund Captured Payment

| Item | Value |
|---|---|
| Controller | `client.Payments` |
| Method | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` |
| **Idempotency** | `payPalRequestId` — **must be the caller-supplied key** for deduplication; same key on retry returns same `Refund` |
| Full refund | Pass `body: null` |
| Partial refund | Pass `body: new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = amount } }` |
| Returns | `PayPalServerSdk.Models.Refund` |
| Error case | Case A — `SdkException<RefundCapturedPaymentError>` |
| Error accessors | `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |
| Map source | `operations/Payments.md` |

**`RefundRequest` fields** (source: `records-2-Pa-Ve.md`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | for partial refund; omit for full |
| `CustomId (custom_id)` | `string?` | optional |
| `InvoiceId (invoice_id)` | `string?` | optional |
| `NoteToPayer (note_to_payer)` | `string?` | optional |

**Reading from `Refund`** (source: `records-2-Pa-Ve.md`):

| What | Path |
|---|---|
| Refund ID | `response.Id` |
| Status | `response.Status` (`RefundStatus?`) |
| Gross refunded | `response.SellerPayableBreakdown?.GrossAmount` |
| PayPal fee refunded | `response.SellerPayableBreakdown?.PaypalFee` |
| Net refunded | `response.SellerPayableBreakdown?.NetAmount` |

Note: 409 from `RefundCapturedPayment` means a refund with that `payPalRequestId` already exists — extract the existing `Refund` from the error payload or re-fetch; do **not** treat as failure.

---

#### Step 6 — Re-authorize Stale Authorization

| Item | Value |
|---|---|
| Controller | `client.Payments` |
| Method | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalRequestId`, `payPalAuthAssertion`, `body` |
| Returns | `PayPalServerSdk.Models.PaymentAuthorization` — new authorization ID: `response.Id` |
| Error case | Case A — `SdkException<ReauthorizePaymentError>` |
| Error accessors | `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |
| Map source | `operations/Payments.md` |

**`ReauthorizeRequest` fields** (source: `records-2-Pa-Ve.md`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | new amount; PayPal allows up to 115% of original, max +$75 USD in US |

Constraints: reauthorize is only valid from day 4 to day 29 after original authorization. After 30 days, create a new order instead.

---

#### Step 7 — Save a Card (Vault)

Two-step setup-token flow (recommended for card vaulting to support verification):

**Sub-step A: CreateSetupToken**

| Item | Value |
|---|---|
| Controller | `client.Vault` (source: `Api/Vault.cs`) |
| Method | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalRequestId` |
| Returns | `PayPalServerSdk.Models.SetupTokenResponse` |
| Setup token ID | `response.Id` |
| Error case | Case A — `SdkException<CreateSetupTokenError>` |
| Error accessors | `TryGetError1(out Error1)` [400, 403, 422, 500] · `TryGetRawError(out RawError)` [fallback] |
| Map source | `operations/Vault.md` |

**`SetupTokenRequest` fields** (source: `records-2-Pa-Ve.md`):

| Field (wire name) | Type | Required? |
|---|---|---|
| `Customer (customer)` | `Customer?` | optional; set `Id` to associate with customer |
| `PaymentSource (payment_source)` | `SetupTokenRequestPaymentSource` | required |

**`SetupTokenRequestPaymentSource.Card`** → `SetupTokenRequestCard`:

| Field (wire name) | Type | Notes |
|---|---|---|
| `Number (number)` | `string?` | PAN |
| `Expiry (expiry)` | `string?` | `YYYY-MM` |
| `SecurityCode (security_code)` | `string?` | CVV |
| `Name (name)` | `string?` | cardholder name |

---

**Sub-step B: CreatePaymentToken** (converts setup token to permanent vault token)

| Item | Value |
|---|---|
| Controller | `client.Vault` |
| Method | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalRequestId` |
| Returns | `PayPalServerSdk.Models.PaymentTokenResponse` |
| Error case | Case A — `SdkException<CreatePaymentTokenError>` |
| Error accessors | `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback] |
| Map source | `operations/Vault.md` |

**`PaymentTokenRequest` fields** (source: `records-2-Pa-Ve.md`):

| Field (wire name) | Type | Required? |
|---|---|---|
| `Customer (customer)` | `Customer?` | optional; set `Id` to link to shopper |
| `PaymentSource (payment_source)` | `PaymentTokenRequestPaymentSource` | required |

`PaymentTokenRequestPaymentSource` — when converting from setup token:
```csharp
new PaymentTokenRequestPaymentSource
{
    Token = new VaultTokenRequest
    {
        Id   = setupTokenId,  // from CreateSetupToken response
        Type = VaultTokenRequestType.SetupToken   // wire: "SETUP_TOKEN"
    }
}
```

`PaymentTokenRequestPaymentSource` — direct card vault (no setup token):
```csharp
new PaymentTokenRequestPaymentSource
{
    Card = new PaymentTokenRequestCard
    {
        Number        = "4111111111111111",
        Expiry        = "YYYY-MM",
        SecurityCode  = "...",
        Name          = "...",
    }
}
```

**Reading from `PaymentTokenResponse`** (source: `records-2-Pa-Ve.md`):

| What | Path |
|---|---|
| Vault token ID | `response.Id` |
| Last 4 digits | `response.PaymentSource?.Card?.LastDigits` |
| Card brand | `response.PaymentSource?.Card?.Brand` (`CardBrand?` enum) |

`PaymentTokenResponse.PaymentSource: PaymentTokenResponsePaymentSource?` → `Card: CardPaymentTokenEntity?` → `LastDigits: string?`, `Brand: CardBrand?`

**`Customer` model** (source: `records-1-Ac-Pa.md`): `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?`

---

#### Step 8 — List Saved Cards

| Item | Value |
|---|---|
| Controller | `client.Vault` |
| Method | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Query params (wire ← C#) | `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired` |
| Returns | `PayPalServerSdk.Models.CustomerVaultPaymentTokensResponse` |
| Error case | Case A — `SdkException<ListCustomerPaymentTokensError>` |
| Error accessors | `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback] |
| Map source | `operations/Vault.md` |

**Pagination** — no SDK auto-pagination; caller must loop manually:
```
pass totalRequired: true on first call
loop page = 1 to response.TotalPages (int?)
collect response.PaymentTokens (IReadOnlyList<PaymentTokenResponse>?)
```

**`CustomerVaultPaymentTokensResponse` fields** (source: `records-1-Ac-Pa.md`):

| Field | Type |
|---|---|
| `TotalItems (total_items)` | `int?` |
| `TotalPages (total_pages)` | `int?` |
| `PaymentTokens (payment_tokens)` | `IReadOnlyList<PaymentTokenResponse>?` |

Each `PaymentTokenResponse` in the list: vault token ID=`t.Id`, last4=`t.PaymentSource?.Card?.LastDigits`, brand=`t.PaymentSource?.Card?.Brand`

---

#### Step 9 — Delete Saved Card

| Item | Value |
|---|---|
| Controller | `client.Vault` |
| Method | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `id` | Vault payment token ID |
| Returns | `void` (Task) |
| Error case | Case A — `SdkException<DeletePaymentTokenError>` |
| Error accessors | `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback] |
| Map source | `operations/Vault.md` |

Note: 404 from delete is NOT in the documented accessor list; falls through to `TryGetRawError`. Treat a missing token as idempotent success in the delete path.

---

#### Step 10 — Transaction Report (all pages)

| Item | Value |
|---|---|
| Controller | `client.TransactionSearch` (source: `Api/TransactionSearch.cs`) |
| Method | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `startDate`, `endDate` | Required strings; ISO-8601 format (e.g. `"2026-01-01T00:00:00-0700"`) |
| Must-pass-explicitly (8 nullable) | `transactionId`, `transactionType`, `transactionStatus`, `transactionAmount`, `transactionCurrency`, `paymentInstrumentType`, `storeId`, `terminalId` — pass `null` for each |
| Query params (wire ← C#) | `start_date` ← `startDate`, `end_date` ← `endDate`, `page_size` ← `pageSize`, `page` ← `page` (and 10 more — see map) |
| Returns | `PayPalServerSdk.Models.SearchResponse` |
| **Error case** | **Case B** — `SdkException<RawError>` (this is the only Case B operation in scope) |
| Error accessors | `ex.Error.StatusCode: HttpStatusCode`, `ex.Error.ReadAsString(): string`, `ex.Error.ReadAsJson<T>(): T?` |
| Map source | `operations/TransactionSearch.md` |

**Pagination** — no SDK auto-pagination; loop manually:
```
page 1: call → read response.TotalPages
loop page 2 to TotalPages: call with page: n, collect TransactionDetails
```

**`SearchResponse` fields** (source: `records-2-Pa-Ve.md`):

| Field (wire name) | Type |
|---|---|
| `TransactionDetails (transaction_details)` | `IReadOnlyList<TransactionDetails>?` |
| `Page (page)` | `int?` |
| `TotalItems (total_items)` | `int?` |
| `TotalPages (total_pages)` | `int?` |

**Matching to eShop order IDs** — via `TransactionDetails.TransactionInfo.CustomField`:

| Path | Type | Notes |
|---|---|---|
| `TransactionDetails.TransactionInfo` | `TransactionInformation?` | |
| `TransactionInformation.CustomField (custom_field)` | `string?` | populate at CreateOrder time via `PurchaseUnitRequest.CustomId` |
| `TransactionInformation.InvoiceId (invoice_id)` | `string?` | alternative: populate via `PurchaseUnitRequest.InvoiceId` |
| `TransactionInformation.TransactionId (transaction_id)` | `string?` | PayPal transaction ID |
| `TransactionInformation.TransactionAmount (transaction_amount)` | `Money?` | |

---

### 2d. Enum values used

All enums are `StringEnum<T>` — NOT C# enums. Use static members (e.g. `CheckoutPaymentIntent.Authorize`), never string literals. All are in namespace `PayPalServerSdk.Models.Enums`.

| Enum | Member | Wire value | Where used |
|---|---|---|---|
| `CheckoutPaymentIntent` | `.Authorize` | `AUTHORIZE` | `OrderRequest.Intent` |
| `CheckoutPaymentIntent` | `.Capture` | `CAPTURE` | (not in scope, for reference) |
| `AuthorizationStatus` | `.Voided` | `VOIDED` | checking void response |
| `AuthorizationStatus` | `.Created` | `CREATED` | checking initial auth status |
| `CaptureStatus` | `.Completed` | `COMPLETED` | checking capture response |
| `RefundStatus` | `.Completed` | `COMPLETED` | checking refund response |
| `RefundStatus` | `.Pending` | `PENDING` | refund in-progress |
| `CardBrand` | `.Visa` | `VISA` | test card brand |
| `VaultTokenRequestType` | `.SetupToken` | `SETUP_TOKEN` | `VaultTokenRequest.Type` when converting setup → payment token |
| `PaymentTokenStatus` | `.Vaulted` | `VAULTED` | checking vault token status |

`Money` model (source: `records-1-Ac-Pa.md`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`. Both are required.

---

## 3. Trap Notes

⚠ **Step 1 (client registration) — `HttpClient` lifetime and factory.** The SDK wraps an `HttpClient`; if it is created per-request the app leaks sockets. **MUST load `dotnet-client-initialization`** before writing the DI registration.

⚠ **Step 1 (auth) — OAuth2 token lifecycle and credential injection.** `Oauth2TokenStrategy` governs token caching and refresh; the default strategy may or may not fit production refresh needs. **MUST load `dotnet-authentication`** before finalizing credential wiring.

⚠ **Step 1 (resilience) — `Timeout` bounds a single attempt, not the whole call; `HttpMethodsToRetry` does NOT prevent POST retries on transport failures.** `CaptureAuthorizedPayment`, `RefundCapturedPayment`, and `AuthorizeOrder` are non-idempotent writes that can execute more than once on a retry. **MUST load `dotnet-configuration-resilience`** before configuring `RetryOptions`.

⚠ **Step 2 (prefer parameter) — `"return=minimal"` may omit authorization ID and purchase unit details from `AuthorizeOrder` response.** Pass `prefer: "return=representation"` on `AuthorizeOrder` and `CreateOrder` calls where the response fields are needed.

⚠ **Step 3/5 (idempotency) — PayPal's 409 on duplicate capture/refund is not a failure.** 409 from `CaptureAuthorizedPayment` or `RefundCapturedPayment` means a prior request with the same `payPalRequestId` already succeeded. The error body in that case contains the existing resource. Extract it via `TryGetError(out Error)` and parse the details rather than treating it as a hard failure.

⚠ **Step 5 (refund — full vs partial) — for a full refund pass `body: null`; for partial supply `RefundRequest.Amount`.** Supplying an `Amount` that exactly equals the capture amount may be treated as a full refund by PayPal but always use `null` body for explicit full refunds.

⚠ **Step 6 (re-auth window) — only valid days 4–29 from original authorization.** After 30 days the operation will fail; a new order must be created instead. Check `PaymentAuthorization.ExpirationTime` before deciding to reauthorize vs. create new.

⚠ **Step 7 (vault — error accessor difference) — Vault operations use `TryGetError1(out Error1)` not `TryGetError(out Error)`.** `Error1` has a different `Details` list type (`ErrorDetails1` with `ErrorLinkDescription` links). Do NOT mix with the Orders/Payments error pattern.

⚠ **Step 8 (list pagination) — `ListCustomerPaymentTokens` has no SDK auto-pagination.** Pass `totalRequired: true` to receive `TotalPages`, then loop manually from page 1 to TotalPages.

⚠ **Step 10 (SearchTransactions error — Case B only) — `SearchTransactions` throws `SdkException<RawError>`, not a typed error.** The catch must handle `SdkException<RawError>` (with `.Error.StatusCode` and `.Error.ReadAsString()`), not `SdkException<SearchTransactionsError>` (which does not exist). A catch ladder that only handles typed `SdkException<{Op}Error>` will miss it.

⚠ **Step 10 (date format) — `startDate`/`endDate` must be ISO-8601 with timezone offset**, e.g. `"2026-01-01T00:00:00-0700"`. Plain date strings will be rejected.

⚠ **Error boundary — `JsonException` from two directions.** See REQUIRED READING below.

---

## 4. REQUIRED READING

Load each skill BEFORE implementing the step it governs. The contract sheet above intentionally does not carry their content.

| Skill | Step(s) governed |
|---|---|
| `dotnet-client-initialization` | Step 1 — DI registration, HttpClient lifetime, factory wiring |
| `dotnet-authentication` | Step 1 — OAuth2 credential injection, token strategy, 401 handling |
| `dotnet-calling-endpoints` | Steps 2–10 — named-argument discipline, required vs optional params, async usage |
| `dotnet-models` | Steps 2–10 — `StringEnum<T>` construction, immutable record initializers |
| `dotnet-error-handling` | Steps 2–10 — Case A vs Case B exception catch ladder, accessor usage |
| `dotnet-configuration-resilience` | Step 1 — retry/timeout configuration, base URL override wiring |
| `dotnet-testing` | All — test seam, HttpClient stub, SDK mock approach |

**`JsonException` hazards — both must shape the error boundary (MUST load `dotnet-error-handling`):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## 5. Assumptions & Blockers

| # | Item |
|---|---|
| A1 | **Currency from config** — `PayPal:Currency` config key is assumed to provide the ISO-4217 currency code (e.g. `"USD"`) used in all `Money` objects. The code is passed as-is to `CurrencyCode` fields. |
| A2 | **Customer ID for vault** — `ListCustomerPaymentTokens` requires a `customerId` string. It is assumed the caller (eShop service layer) tracks a stable per-shopper PayPal customer ID (e.g. from the first vault call's `PaymentTokenResponse.Customer.Id`). The plan does not prescribe where this ID is stored. |
| A3 | **eShop order ID → PayPal custom field** — to match transactions in the report, eShop order IDs must be written to `PurchaseUnitRequest.CustomId` at `CreateOrder` time. This must be coordinated with the eShop order service. |
| A4 | **PCI scope** — passing raw card numbers via `CardRequest.Number` and `SecurityCode` requires PCI SAQ D compliance. The brief calls for direct card processing; PCI implications are the implementer's responsibility. |
| A5 | **`PayPal:BaseUrl` override** — when set, it replaces the entire base URL including the token-fetch endpoint. The override applies to `options.Server.Default.Sandbox.BaseUrl` (source: `Servers/DefaultOptions.cs`, `ServerOptions.cs`). If the override URL uses a different path prefix than `api-m.sandbox.paypal.com`, the token endpoint may differ; this is UNVERIFIED against a live custom proxy. |
| A6 | **Re-auth vs. new order** — the plan recommends checking `PaymentAuthorization.ExpirationTime` before calling `ReauthorizePayment`. The boundary at exactly 30 days is documented in the SDK notes but is UNVERIFIED as to exact enforcement behavior on the live wire. |
| B1 | **No blockers** — all contract facts are resolved from the map or SDK source. |
