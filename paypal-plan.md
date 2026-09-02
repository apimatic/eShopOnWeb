# PayPal Server SDK (.NET) integration plan — eShopOnWeb

SDK: `AsadAli.Checkout.Sdk` (NuGet, install version-less: `dotnet add package AsadAli.Checkout.Sdk`) · root namespace `PayPalServerSdk` · client `PayPalServerSdkClient` · map provenance: source commit `9653d18`, tag `v1.0.1`.

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Install package; register client + options in DI; bind config (`PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:BaseUrl`, `PayPal:Currency`) | — (client construction, §3.0) |
| 2 | Wire OAuth client-credentials auth; write the integration error boundary; set retry/timeout options | — (§3.0, §3.9, §4) |
| 3 | Checkout — authorize now: create order with intent `AUTHORIZE` paying by raw card **or** vaulted token; 3DS contingency check; authorize the order; persist PayPal order id + authorization id + status + expiry | `Orders.CreateOrder`, `Orders.AuthorizeOrder` (+ `Orders.GetOrder` read-back) |
| 4 | Fulfilment — capture later: read authorization; if stale, reauthorize (operator path when renewal impossible); capture; persist capture id, gross/fee/net, seller protection | `Payments.GetAuthorizedPayment`, `Payments.ReauthorizePayment`, `Payments.CaptureAuthorizedPayment` (+ `Payments.GetCapturedPayment`) |
| 5 | Cancel before fulfilment: void the authorization, releasing the hold | `Payments.VoidPayment` |
| 6 | Refund (full or partial) with caller-supplied idempotency key; read back refund id + status | `Payments.RefundCapturedPayment` (+ `Payments.GetRefund`) |
| 7 | Saved cards: vault a card (customer-scoped), list a shopper's vaulted cards, delete a token | `Vault.CreatePaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.GetPaymentToken`, `Vault.DeletePaymentToken` |
| 8 | Reconciliation: transaction search over an ISO-8601 date range, ALL pages | `TransactionSearch.SearchTransactions` |
| 9 | Tests against the SDK seam | — (`dotnet-testing`) |

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

Namespace legend (add one `using` per line you touch — C# does not import child namespaces transitively):

| Namespace | Contents |
|---|---|
| `PayPalServerSdk` | `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` |
| `PayPalServerSdk.Servers` | `ServerEnvironment`, `DefaultOptions` |
| `PayPalServerSdk.Api` | controllers (`Orders`, `Payments`, `Vault`, `TransactionSearch`) |
| `PayPalServerSdk.Models` | all record models incl. error payloads `Error`, `Error1`, `DefaultError` |
| `PayPalServerSdk.Models.Enums` | all enums (`StringEnum<T>`, **not** C# enums) |
| `PayPalServerSdk.Errors` | typed `{Operation}Error` classes |
| `PayPalServerSdk.Core` | `RequestOptions` |
| `PayPalServerSdk.Core.Configuration` | `RetryOptions`, `LoggingOptions` |
| `PayPalServerSdk.Core.Exceptions` | `SdkException<TError>` |
| `PayPalServerSdk.Core.ErrorResponse` | `ApiError`, `RawError` |
| `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | `OAuth2ClientCredentials` |
| `PayPalServerSdk.Core.Authentication.OAuth2` | `IOAuth2TokenStrategy<T>` |

Field listings below are `CSharpName (wire_name): Type` — `!req` = C# `required` (must be set in the object initializer); `?` = optional/nullable. Records are immutable with `init`-only setters. This SDK has **no union types** (`unions.md`: 0 OneOf/AnyOf) — payment-source selection is "set exactly one property on the container record".

### 3.0 Client construction, auth, environment, base-URL override

Construction (map: `sdk-map.md` *Getting a client*; source: `PayPalServerSdkClient.cs`, `ServiceCollectionExtensions.cs`):

```csharp
var options = new PayPalServerSdk.PayPalServerSdkClientOptions { /* below */ };
var client = new PayPalServerSdk.PayPalServerSdkClient(httpClient, options); // httpClient: System.Net.Http.HttpClient
// DI alternative: services.AddPayPalServerSdkClient(o => { /* same options */ });
```

`PayPalServerSdkClientOptions` members (map: `sdk-map.md`; source: `PayPalServerSdkClientOptions.cs`):

| Property | Type (fully-qualified) | Set to |
|---|---|---|
| `Environment` | `PayPalServerSdk.Servers.ServerEnvironment` | `ServerEnvironment.Sandbox` — the **only** member that exists (source: `Servers/ServerEnvironment.cs`); there is no `Production` member — production is reached via the base-URL override below |
| `Oauth2` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?` | `new() { ClientId = cfg["PayPal:ClientId"], ClientSecret = cfg["PayPal:ClientSecret"] }` — both `required string`; `Scope (string?)` optional (source: `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`) |
| `Oauth2TokenStrategy` | `PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | leave `null` — default strategy POSTs form `grant_type=client_credentials` with HTTP Basic `base64(clientId:clientSecret)` to `/v1/oauth2/token` (source: `OAuth2ClientCredentialsStrategy.cs`) |
| `Server` | `PayPalServerSdk.ServerOptions` | base-URL override — see below |
| `Retry` | `PayPalServerSdk.Core.Configuration.RetryOptions` | all members `required` — start from `RetryOptions.Default()`; tune per §4 trap note |
| `Logging` | `PayPalServerSdk.Core.Configuration.LoggingOptions` | optional |

**Base-URL override (`PayPal:BaseUrl`) — covers EVERY call including the OAuth token request** (source: `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs`):

```csharp
options.Server.Default.Sandbox.BaseUrl = cfg["PayPal:BaseUrl"]; // e.g. sandbox default "https://api-m.sandbox.paypal.com"
```

- Path: `ServerOptions.Default` (`PayPalServerSdk.Servers.DefaultOptions`) `.Sandbox` (`DefaultOptions.SandboxOptions`) `.BaseUrl` (`string`, default `"https://api-m.sandbox.paypal.com"`).
- Every API call resolves its URL through this value, and the default OAuth token strategy builds its token URL from the **same** resolution (`server.Default("/v1/oauth2/token")` in `AuthSchemes.cs`) — one override covers both.
- The production host value is **not** carried anywhere in the SDK source (only the sandbox default exists) — the app must supply it through `PayPal:BaseUrl` config.

Controller properties on the client (source: `PayPalServerSdkClient.cs`): `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch` (+ `client.Subscriptions`, unused here).

**Per-call headers / idempotency**: idempotency keys and the `Prefer` header are first-class generated parameters (`payPalRequestId`, `prefer`) on each write operation — pass them as arguments, not headers. `PayPalServerSdk.Core.RequestOptions` carries **only** `LogLevel` (source: `Core/RequestOptions.cs`) — there is no per-call arbitrary-header hook; anything beyond the generated header params needs a `DelegatingHandler` on the `HttpClient` (§4, client-registration trap).

### 3.1 Create order (intent AUTHORIZE, raw card or vaulted token) — step 3

`client.Orders.CreateOrder(...)` (map: `operations/Orders.md`):

```csharp
CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId,
    string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body,
    string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- First 5 params are nullable with **no default → must be passed explicitly** (pass `null`), except set `payPalRequestId` to the app's idempotency key for the checkout attempt.
- `body` is `PayPalServerSdk.Models.OrderRequest` (required, non-nullable):

| Model | Fields |
|---|---|
| `OrderRequest` | `Intent (intent): CheckoutPaymentIntent !req` → `CheckoutPaymentIntent.Authorize` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` · `PaymentSource (payment_source): PaymentSource?` · `Payer (payer): Payer?` · `ApplicationContext (application_context): OrderApplicationContext?` |
| `PurchaseUnitRequest` | `Amount (amount): AmountWithBreakdown !req` · `ReferenceId (reference_id): string?` · `InvoiceId (invoice_id): string?` · `CustomId (custom_id): string?` · `Description (description): string?` · `Items (items): IReadOnlyList<ItemRequest>?` |
| `AmountWithBreakdown` | `CurrencyCode (currency_code): string !req` (from config) · `Value (value): string !req` (server-side catalog total, e.g. `"129.99"`) · `Breakdown (breakdown): AmountBreakdown?` |
| `PaymentSource` (set exactly one) | `Card (card): CardRequest?` (+ `Token`, `Paypal`, wallets, APMs — unused) |
| `CardRequest` — raw card | `Number (number): string?` · `Expiry (expiry): string?` (`"YYYY-MM"`) · `SecurityCode (security_code): string?` · `Name (name): string?` · `BillingAddress (billing_address): Address?` · `Attributes (attributes): CardAttributes?` |
| `CardRequest` — vaulted token | `VaultId (vault_id): string?` ← the vault/payment-token id; set **only** this |
| `Address` | `CountryCode (country_code): string !req` · `AddressLine1 (address_line_1): string?` · `AddressLine2 (address_line_2): string?` · `AdminArea2 (admin_area_2): string?` (city) · `AdminArea1 (admin_area_1): string?` (state) · `PostalCode (postal_code): string?` |
| `CardAttributes` | `Verification (verification): CardVerification?` → `Method (method): OrdersCardVerificationMethod?` **defaults to `ScaWhenRequired`** — see 3DS BLOCKER, §5 |

- Returns `PayPalServerSdk.Models.Order`: `Id (id): string?` · `Status (status): OrderStatus?` · `Intent (intent): CheckoutPaymentIntent?` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` · `Links (links): IReadOnlyList<LinkDescription>?` (`Href (href) !req`, `Rel (rel) !req`, `Method (method): LinkHttpMethod?`).
- Error: `SdkException<PayPalServerSdk.Errors.CreateOrderError>` — Case A · `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback.

### 3.2 Authorize order (hold funds) — step 3

`client.Orders.AuthorizeOrder(...)` (map: `operations/Orders.md`):

```csharp
AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId,
    string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body,
    string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- `id` = PayPal order id from 3.1. The 5 middle params are must-pass-explicitly (pass `null`); `body` may be `null` because the payment source was supplied at create — pass `new PayPalServerSdk.Models.OrderAuthorizeRequest()` only if you must re-supply a source (`PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` → `Card (card): CardRequest?`).
- Returns `PayPalServerSdk.Models.OrderAuthorizeResponse`: `Id`, `Status (status): OrderStatus?`, `PurchaseUnits` → `PurchaseUnit.Payments (payments): PaymentCollection?` → `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → per authorization: `Id (id): string?` · `Status (status): AuthorizationStatus?` · `Amount (amount): Money?` · `SellerProtection (seller_protection): SellerProtection?` · `ExpirationTime (expiration_time): string?` · `ProcessorResponse (processor_response): ProcessorResponse?`. **Read path: `resp.PurchaseUnits[0].Payments.Authorizations[0].Id`** — persist order id + authorization id + status + expiry.
- Error: `SdkException<PayPalServerSdk.Errors.AuthorizeOrderError>` — Case A · `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)`.
- `prefer`: the SDK default is `"return=minimal"`; this integration reads nested fields (`purchase_units.payments.authorizations`), so pass `prefer: "return=representation"` on this call. Which fields a minimal response omits is `UNVERIFIED` (only live traffic confirms) — the directive stands as defensive coding.

Read-back: `client.Orders.GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, ...)` (3 must-pass-explicitly; pass `null`) → `Order`. Error `GetOrderError`: `TryGetError(out Error)` [401, 404].

### 3.3 Capture the authorization at fulfilment — step 4

`client.Payments.CaptureAuthorizedPayment(...)` (map: `operations/Payments.md`):

```csharp
CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId,
    string? payPalAuthAssertion, CaptureRequest? body,
    string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- 4 middle params must-pass-explicitly; set `payPalRequestId` to a fulfilment-scoped idempotency key. Pass `prefer: "return=representation"` (same `UNVERIFIED` rationale as 3.2 — the breakdown fields below are the whole point of the call).
- `body`: `PayPalServerSdk.Models.CaptureRequest?` — `Amount (amount): Money?` (`CurrencyCode (currency_code): string !req`, `Value (value): string !req`; omit for full authorized amount) · `InvoiceId (invoice_id): string?` · `FinalCapture (final_capture): bool? = false` (set `true` when no later captures) · `NoteToPayer (note_to_payer): string?` · `SoftDescriptor (soft_descriptor): string?`.
- Returns `PayPalServerSdk.Models.CapturedPayment` **directly (no envelope)**: `Id (id)` · `Status (status): CaptureStatus?` · `StatusDetails (status_details): CaptureStatusDetails?` (`Reason (reason): CaptureIncompleteReason?`) · `Amount (amount): Money?` · `FinalCapture (final_capture): bool?` · `SellerProtection (seller_protection): SellerProtection?` (`Status (status): SellerProtectionStatus?`, `DisputeCategories (dispute_categories)`) · **`SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`** → `GrossAmount (gross_amount): Money !req` · `PaypalFee (paypal_fee): Money?` · `NetAmount (net_amount): Money?` · `PaypalFeeInReceivableCurrency (paypal_fee_in_receivable_currency): Money?` · `ReceivableAmount (receivable_amount): Money?` · `ExchangeRate (exchange_rate): ExchangeRate?` · `PlatformFees (platform_fees)`. Persist: capture id, status, gross, PayPal fee, net.
- Error: `SdkException<PayPalServerSdk.Errors.CaptureAuthorizedPaymentError>` — Case A · `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

Read-backs: `Payments.GetCapturedPayment(string captureId, string? payPalMockResponse, ...)` → `CapturedPayment`; `Payments.GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, ...)` → `PayPalServerSdk.Models.PaymentAuthorization` (`Id`, `Status: AuthorizationStatus?`, `Amount`, `ExpirationTime (expiration_time)`, `SellerProtection`, `SupplementaryData (supplementary_data): PaymentSupplementaryData?` → `RelatedIds (related_ids): RelatedIdentifiers?` → `OrderId (order_id)`, `AuthorizationId (authorization_id)`, `CaptureId (capture_id)` — the SDK's linkage fields). Both Case A, `TryGetError(out Error)` [401, 403, 404] + `TryGetNoContent(out RawError)` [500].

### 3.4 Reauthorize a stale authorization — step 4

`client.Payments.ReauthorizePayment(...)` (map: `operations/Payments.md`):

```csharp
ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion,
    ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- `body`: `PayPalServerSdk.Models.ReauthorizeRequest?` — `Amount (amount): Money?` only (the operation supports only `amount`).
- Returns `PayPalServerSdk.Models.PaymentAuthorization` (fields as 3.3).
- Server-side window (from the operation's own doc text in the map): reauthorize from day 4 to day 29 after the 3-day honor period; at 30+ days reauthorization is impossible and a new authorization must be created instead; allowed amount is context/geography-capped (e.g. up to 115% / +$75 in the US). Enforcement is server-side.
- Error: `SdkException<PayPalServerSdk.Errors.ReauthorizePaymentError>` — Case A · `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500]. **A 4xx here = the hold cannot be renewed → surface to an operator** (do not silently retry or capture); read `Error.Details (details): IReadOnlyList<ErrorDetails>?` → `Issue (issue): string !req`, `Description (description): string?` for the operator message.

### 3.5 Void an authorization — step 5

`client.Payments.VoidPayment(...)` (map: `operations/Payments.md`):

```csharp
VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion,
    string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- ⚠ Parameter order here is `payPalAuthAssertion` **before** `payPalRequestId` — different from every other write op. Use named arguments.
- Returns `PayPalServerSdk.Models.PaymentAuthorization` (expect `Status` = `AuthorizationStatus.Voided`). Cannot void a fully-captured authorization (server rule).
- Error: `SdkException<PayPalServerSdk.Errors.VoidPaymentError>` — Case A · `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500].

### 3.6 Refund a capture (full/partial, idempotent) — step 6

`client.Payments.RefundCapturedPayment(...)` (map: `operations/Payments.md`):

```csharp
RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId,
    string? payPalAuthAssertion, RefundRequest? body,
    string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- **Idempotency**: pass the caller-supplied key as `payPalRequestId` (wire: `PayPal-Request-Id` header). Same key repeated = no double refund; a **distinct** key per distinct partial refund of the same capture. The app must generate and persist one key per logical refund.
- `body`: `PayPalServerSdk.Models.RefundRequest?` — full refund: pass `new RefundRequest()` (empty payload = full refund per the operation doc; whether `null` behaves identically is `UNVERIFIED` — always pass the empty object, never `null`). Partial refund: set `Amount (amount): Money?`. Also `InvoiceId (invoice_id): string?`, `CustomId (custom_id): string?`, `NoteToPayer (note_to_payer): string?`.
- Returns `PayPalServerSdk.Models.Refund`: **`Id (id): string?`** · **`Status (status): RefundStatus?`** · `StatusDetails (status_details): RefundStatusDetails?` (`Reason (reason): RefundIncompleteReason?`) · `Amount (amount): Money?` · `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount (total_refunded_amount)`) · `Links`.
- Error: `SdkException<PayPalServerSdk.Errors.RefundCapturedPaymentError>` — Case A · `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500].

Read-back: `Payments.GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, ...)` → `Refund`.

### 3.7 Saved cards (vault) — step 7

All under `client.Vault` (map: `operations/Vault.md`). Error payload type for all four: `PayPalServerSdk.Models.Error1` via `TryGetError1(out Error1)` (`Name`, `Message`, `DebugId` all `string !req`; `Details (details): IReadOnlyList<ErrorDetails1>?`; `Links: IReadOnlyList<ErrorLinkDescription>?` — note `ErrorLinkDescription.Rel (rel)` is **nullable**).

**Create (vault) a card** — `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` (`payPalRequestId` must-pass-explicitly; use it as the vault-attempt idempotency key):

| Model | Fields |
|---|---|
| `PaymentTokenRequest` | `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` · `Customer (customer): Customer?` ← **customer scoping** |
| `Customer` | `Id (id): string?` (PayPal customer id — see below) · `MerchantCustomerId (merchant_customer_id): string?` (the app's own shopper id) |
| `PaymentTokenRequestPaymentSource` | `Card (card): PaymentTokenRequestCard?` |
| `PaymentTokenRequestCard` | `Number (number): string?` · `Expiry (expiry): string?` · `SecurityCode (security_code): string?` · `Name (name): string?` · `Brand (brand): CardBrand?` · `BillingAddress (billing_address): Address?` |

- Returns `PayPalServerSdk.Models.PaymentTokenResponse`: **`Id (id): string?`** ← the vault/payment-token id to store · `Customer (customer): CustomerResponse?` → **`Id (id): string?`** ← the PayPal customer id · `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `Card (card): CardPaymentTokenEntity?` → safe display fields only: `Brand (brand): CardBrand?`, `LastDigits (last_digits): string?`, `Expiry (expiry): string?`, `Name (name): string?`, `Type (type): CardType?` — **never full PAN** (the response model has no full-number field).
- **Scoping recommendation**: persist the PayPal customer id (`PaymentTokenResponse.Customer.Id`) on the shopper at first vault; pass it in `PaymentTokenRequest.Customer.Id` on later vaults and as `customerId` on list. `MerchantCustomerId` can carry the app's shopper id.
- Error: `CreatePaymentTokenError` — `TryGetError1(out Error1)` [400, 403, 404, 422, 500].

**List a shopper's cards** — `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` (query wire names: `customer_id`, `page_size`, `page`, `total_required`):

- Returns `PayPalServerSdk.Models.CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Customer (customer): VaultResponseCustomer?` · `Links`.
- **Pagination**: no SDK auto-paginator (map: "Pagination: none") — loop `page` from 1 to `TotalPages` (pass `totalRequired: true` so totals are populated), aggregating `PaymentTokens`.
- Error: `ListCustomerPaymentTokensError` — `TryGetError1(out Error1)` [400, 403, 500].

**Delete a token** — `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `void` (Task). Error: `DeletePaymentTokenError` — `TryGetError1(out Error1)` [400, 403, 500].

**Read one token** — `GetPaymentToken(string id, ...)` → `PaymentTokenResponse`. Error: `GetPaymentTokenError` — `TryGetError1(out Error1)` [403, 404, 422, 500].

**Pay with a vaulted token** — on `CreateOrder` (3.1): `PaymentSource.Card = new CardRequest { VaultId = "<payment-token-id>" }`, intent still `CheckoutPaymentIntent.Authorize`; then `AuthorizeOrder` as 3.2.

### 3.8 Reconciliation — transaction search — step 8

`client.TransactionSearch.SearchTransactions(...)` (map: `operations/TransactionSearch.md`):

```csharp
SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType,
    string? transactionStatus, string? transactionAmount, string? transactionCurrency,
    string? paymentInstrumentType, string? storeId, string? terminalId,
    string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y",
    int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- `startDate`/`endDate` required ISO-8601 strings (wire `start_date`/`end_date`). The 8 middle params are must-pass-explicitly — pass `null` for unused filters.
- Service caveats from the operation doc: executed transactions take up to **3 hours** to appear; range limited to the previous **3 years**; specifying optional filters empties `ending_balance`.
- Returns `PayPalServerSdk.Models.SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` · `Page (page): int?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `LastRefreshedDatetime (last_refreshed_datetime): string?` · `Links`.
- **Pagination**: no auto-paginator — loop `page` 1…`TotalPages` at `pageSize: 100`, aggregate all `TransactionDetails` before matching.
- Per transaction: `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?` → `TransactionId (transaction_id): string?` · **`PaypalReferenceId (paypal_reference_id): string?` + `PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?`** — the order/authorization/capture linkage fields · `TransactionEventCode (transaction_event_code): string?` · `TransactionAmount (transaction_amount): Money?` · `FeeAmount (fee_amount): Money?` · **`TransactionStatus (transaction_status): string?` — a plain string, NOT an enum** (compare defensively) · `TransactionInitiationDate (transaction_initiation_date)`, `TransactionUpdatedDate (transaction_updated_date): string?` · `InvoiceId (invoice_id): string?` · `CustomField (custom_field): string?` · `ProtectionEligibility (protection_eligibility): string?`. Match app orders on `InvoiceId`/`CustomField` (set them at create/capture time) and `PaypalReferenceId`.
- Error: **Case B — the SDK's only one**: `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>`; read `ex.Error.StatusCode` (`HttpStatusCode`) + `ex.Error.ReadAsString()` / `ReadAsJson<T>()`. No typed accessors exist for this operation.

### 3.9 Error model (applies to every call)

- Every operation is **throw-only** (no `…Result` variants exist anywhere in this SDK). On an error status the SDK throws `PayPalServerSdk.Core.Exceptions.SdkException<TError>` whose only member is `Error: TError` (source: `Core/Exceptions/SdkException.cs`) — the exception itself carries **no** StatusCode property.
- **Case A** (39 of 40 ops — everything in this plan except SearchTransactions): `TError` = `PayPalServerSdk.Errors.{Operation}Error : PayPalServerSdk.Core.ErrorResponse.ApiError`. Status-specific `TryGet…(out …)` accessors per the rows above; inherited `TryGetRawError(out RawError)` is the fallback for undocumented statuses — it is **not** a catch-all on typed errors (§4).
- **Case B** (SearchTransactions only): `TError` = `RawError`: `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>`.
- Typed payloads: `PayPalServerSdk.Models.Error` (Orders/Payments ops) and `Error1` (Vault ops) — `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details`, `Links`. HTTP status in Case A is known from **which** `TryGet…` returned true (each maps to the statuses listed in its row).

### 3.10 Enum values actually needed (all `PayPalServerSdk.Models.Enums`, `StringEnum<T>` — construct via static members, e.g. `CheckoutPaymentIntent.Authorize`; map: `models/enums.md`)

| Enum | Members (wire) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** ← 3DS contingency |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `SellerProtectionStatus` | `Eligible (ELIGIBLE)`, `PartiallyEligible (PARTIALLY_ELIGIBLE)`, `NotEligible (NOT_ELIGIBLE)` |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `CardBrand` (display) | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, … (29 members — full list in `models/enums.md`) |
| `CardType` (display) | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)` (default), `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| 3DS read-back on `CardResponse.AuthenticationResult (authentication_result): AuthenticationResponse?` | `LiabilityShift (liability_shift): LiabilityShiftIndicator?` (`No`/`Possible`/`Unknown`) · `ThreeDSecure (three_d_secure): ThreeDSecureAuthenticationResponse?` → `AuthenticationStatus: ParesStatus?` (`Y`/`N`/`U`/`A`/`C`/`R`/`D`/`I`), `EnrollmentStatus: EnrollmentStatus?` (`Y`/`N`/`U`/`B`) |
| Incomplete reasons (status details) | `AuthorizationIncompleteReason` (`PendingReview`, `DeclinedByRiskFraudFilters`) · `CaptureIncompleteReason` (12 members incl. `PendingReview`, `DeclinedByRiskFraudFilters`) · `RefundIncompleteReason` (`Echeck`) |

## 4. Trap notes (hazard + consequence — the named skill carries the resolution)

> ⚠ Step 1 (client registration) — the SDK client takes an `HttpClient` constructor argument; how that client/handler pipeline must be owned and lived (and where a `DelegatingHandler` for extra headers would attach) is not visible from the signature, and getting lifetime wrong exhausts sockets. **MUST load `dotnet-client-initialization`** before wiring DI.

> ⚠ Step 2 (auth) — the credentials shape is known (§3.0), but when credentials must be set relative to client construction, and how token acquisition/caching/refresh behaves, is not. **MUST load `dotnet-authentication`** before wiring credentials.

> ⚠ Steps 3–8 (every call) — many parameters are nullable with no C# default and **must be passed explicitly**; positional calls mis-bind (worst case: `VoidPayment`, whose `payPalAuthAssertion`/`payPalRequestId` order differs from the other writes). **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 3–8 (model building/reading) — enums are `StringEnum<T>` records, not C# enums; records are immutable with `required` members; unmodeled JSON fields are silently dropped on deserialize (matters when reading linkage/breakdown fields). **MUST load `dotnet-models`**.

> ⚠ Step 2 (error boundary) — Case A vs Case B differs per operation (§3.9), `TryGetRawError` is not a catch-all, and `JsonException` reaches the boundary from two directions needing opposite handling (verbatim rows in §5). **MUST load `dotnet-error-handling`** before writing any `try/catch`.

> ⚠ Step 2 (retry/timeout) — what the SDK's retry options actually re-execute (in particular whether a failed non-idempotent write — capture, refund — can be sent more than once) and what `Timeout` actually bounds are not derivable from the option names; the consequence is double-charge risk that only disciplined `payPalRequestId` use mitigates. **MUST load `dotnet-configuration-resilience`** before setting `Retry` and before relying on retries anywhere near money movement.

> ⚠ Step 8 (pagination loops) — neither `SearchTransactions` nor `ListCustomerPaymentTokens` has an SDK paginator; the manual page loop's interaction with per-attempt timeouts and retries is a resilience-config question. **MUST load `dotnet-configuration-resilience`**.

> ⚠ Step 9 (tests) — the test seam is the `HttpClient` constructor argument, not the controllers. **MUST load `dotnet-testing`** before stubbing the SDK.

## 5. REQUIRED READING (load ALL before implementation starts — this sheet deliberately does not carry their contents)

- `dotnet-client-initialization` — step 1: client construction, `HttpClient` ownership/lifetime, DI registration.
- `dotnet-authentication` — step 2: supplying credentials, token behaviour.
- `dotnet-calling-endpoints` — steps 3–8: explicit-parameter and named-argument discipline, async/cancellation.
- `dotnet-models` — steps 3–8: `StringEnum<T>`, `required` members, immutability, deserialization drop behaviour.
- `dotnet-error-handling` — step 2: the Case A/B boundary. Mandatory verbatim hazards:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
    `JsonException` from deserialization, **not** as an `SdkException` — so an
    SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
    throws `JsonException` *while the error object is being constructed*, so the `JsonException`
    **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
    maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
    and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary.
- `dotnet-configuration-resilience` — step 2 + step 8: retries, timeouts, base-URL, pagination loops, logging.
- `dotnet-testing` — step 9: faking the `HttpClient` seam.

## 6. Assumptions & Blockers

**Blockers**

1. **3DS / browser-challenge contingency (hard blocker, by design).** Direct card processing can trigger a 3DS/browser challenge: the SDK models this as `OrderStatus.PayerActionRequired (PAYER_ACTION_REQUIRED)` on the create/authorize response (`models/enums.md`), with the challenge URL surfaced through `Order.Links` (`LinkDescription.Href/Rel`), and card verification defaults to `OrdersCardVerificationMethod.ScaWhenRequired` (`records-1-Ac-Pa.md`, `CardVerification`). This app must **not** build an approval round-trip. Directive: after `CreateOrder` and after `AuthorizeOrder`, if `Status == OrderStatus.PayerActionRequired` (or a payer-action link is present), treat the payment as **blocked** — void/ abandon the order server-side and surface an operator-visible failure ("card requires 3DS authentication; not supported by this integration"). `CardResponse.AuthenticationResult` (liability shift, PARes status) may be logged as evidence. Whether a given sandbox card actually triggers the contingency is `UNVERIFIED` — only live traffic confirms.

**Assumptions**

2. `prefer: "return=representation"` is passed on `AuthorizeOrder`, `CaptureAuthorizedPayment`, and `RefundCapturedPayment` because the integration reads nested fields; exactly which fields the SDK-default `"return=minimal"` omits is `UNVERIFIED` (live-traffic-only fact).
3. Full refund = `new RefundRequest()` (empty object) per the operation doc; whether a `null` body behaves identically is `UNVERIFIED` — always pass the empty object.
4. The production API host value is not present in the SDK source (only the sandbox default `https://api-m.sandbox.paypal.com`); production is reached by setting `PayPal:BaseUrl` (§3.0). `ServerEnvironment` has only the `Sandbox` member — this is source-confirmed, not a map gap.
5. Reauthorization windows/caps in §3.4 come from the SDK's own operation doc text; enforcement is server-side, so any 4xx from `ReauthorizePayment` is treated as "renewal impossible → operator".
6. `TransactionInformation.TransactionStatus` is a plain `string?` with no enum in the SDK — reconciliation compares it defensively (no value list exists in the map to enumerate).
7. `balanceAffectingRecordsOnly` defaults to `"Y"`; the map carries no value list for it (plain `string?`) — left at default unless reconciliation needs non-balance-affecting records.
8. Amounts are decimal **strings** (`Money.Value`, `AmountWithBreakdown.Value`) — format from server-side catalog prices with invariant culture; currency comes from config.
9. Per-call custom headers beyond the generated parameters (`payPalRequestId`, `prefer`, `payPalClientMetadataId`, `payPalAuthAssertion`, `payPalMockResponse`) are not supported by `RequestOptions` (it carries only `LogLevel` — source-confirmed); none are needed for this scope.
