# PayPal .NET SDK integration plan — eShopOnWeb

SDK: `AsadAli.Checkout.Sdk` (NuGet; install **version-less** — `dotnet add package AsadAli.Checkout.Sdk`), root namespace `PayPalServerSdk`, client `PayPalServerSdkClient`, targets `netstandard2.0` (fine on .NET 8). Map provenance: source commit `9653d18`, tag `v1.0.1`.

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Install package; client construction + DI registration (env from config string, OAuth creds, optional BaseUrl override) | — (`PayPalServerSdkClient` / `AddPayPalServerSdkClient`) |
| 2 | Create order with `intent=AUTHORIZE`, payment_source = direct card **or** vaulted card | `Orders.CreateOrder` |
| 3 | Authorize the order; store authorization id from response | `Orders.AuthorizeOrder` |
| 4 | Read back order/authorization/capture/refund status by stored PayPal ids | `Orders.GetOrder`, `Payments.GetAuthorizedPayment`, `Payments.GetCapturedPayment`, `Payments.GetRefund` |
| 5 | Capture the authorization at fulfilment; persist gross/fee/net | `Payments.CaptureAuthorizedPayment` |
| 6 | Reauthorize a stale authorization (days 4–29 window) | `Payments.ReauthorizePayment` |
| 7 | Void authorization on cancel-before-fulfilment | `Payments.VoidPayment` |
| 8 | Refund capture (full or partial) with caller-supplied idempotency key | `Payments.RefundCapturedPayment` |
| 9 | Vault a card (setup-token → payment-token, or direct vault); list; delete | `Vault.CreateSetupToken`, `Vault.CreatePaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.GetPaymentToken`, `Vault.DeletePaymentToken` |
| 10 | Reconciliation report over a date range, all pages | `TransactionSearch.SearchTransactions` |
| 11 | Error boundary + resilience + tests around all of the above | — |

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

### 2.0 Namespaces (add a `using` per kind of type — C# does not import child namespaces)

| Namespace | Types used from it |
|---|---|
| `PayPalServerSdk` | `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions`, `ServiceCollectionExtensions` |
| `PayPalServerSdk.Servers` | `ServerEnvironment` |
| `PayPalServerSdk.Core.Configuration` | `RetryOptions`, `LoggingOptions` |
| `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | `OAuth2ClientCredentials` |
| `PayPalServerSdk.Core.Authentication.OAuth2` | `IOAuth2TokenStrategy<OAuth2ClientCredentials>` |
| `PayPalServerSdk.Core.Exceptions` | `SdkException<TError>` |
| `PayPalServerSdk.Core.ErrorResponse` | `RawError`, `ApiError` |
| `PayPalServerSdk.Models` | every record below (`OrderRequest`, `Money`, `CardRequest`, …) |
| `PayPalServerSdk.Models.Enums` | every enum below (`CheckoutPaymentIntent`, `AuthorizationStatus`, …) |
| `PayPalServerSdk.Errors` | every `{Operation}Error` below |

(sdk-map.md; `PayPalServerSdkClientOptions.cs`, `ServerOptions.cs`, `Servers/ServerEnvironment.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`)

### 2.1 Client construction, auth, environment, BaseUrl override

Construction (sdk-map.md *Getting a client*; `PayPalServerSdkClient.cs`):

```csharp
var options = new PayPalServerSdkClientOptions
{
    Environment = ServerEnvironment.Sandbox,          // the ONLY member that exists — see below
    Oauth2 = new OAuth2ClientCredentials { ClientId = "…", ClientSecret = "…" }, // both required; Scope optional
    // Server.Default.Sandbox.BaseUrl — the single base-URL override point (see below)
};
var client = new PayPalServerSdkClient(httpClient, options);   // httpClient: System.Net.Http.HttpClient
```

DI alternative (`ServiceCollectionExtensions.cs`, source-verified): `services.AddPayPalServerSdkClient(o => { /* same options */ })` — registers the SDK client as a **singleton** built on `IHttpClientFactory.CreateClient()`; returns `IServiceCollection`.

`PayPalServerSdkClientOptions` members (sdk-map.md): `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Logging: LoggingOptions` · `Server: ServerOptions` · `Oauth2: OAuth2ClientCredentials?` · `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.

Facts verified from source (map gap resolved against tag `v1.0.1`):

- **Environment selection.** `ServerEnvironment` has exactly one member: `ServerEnvironment.Sandbox` (`Servers/ServerEnvironment.cs`); `ServerEnvironment.Default()` returns it. **There is no Production member.** "Production" is reached only by overriding the base URL (next row). Map the config string yourself: `"sandbox"` → defaults; `"production"` → `BaseUrl = "https://api-m.paypal.com"`; anything else → treat as a verbatim base URL.
- **BaseUrl override covers EVERY call including OAuth.** `options.Server.Default.Sandbox.BaseUrl` (string, default `"https://api-m.sandbox.paypal.com"`; `ServerOptions.cs` → `Servers/DefaultOptions.cs`). Every controller URL *and* the OAuth token URL are resolved through the same path: `AuthSchemes` builds the token URL as `server.Default("/v1/oauth2/token")`, and `Server.Default(path)` → `DefaultOptions.Resolve(environment, path)` → `new UrlTemplate(Sandbox.BaseUrl, path, [])` (`AuthSchemes.cs`, `Server.cs`, `Servers/DefaultOptions.cs`). One override, used verbatim, no exceptions. Set it before constructing the client.
- **OAuth request shape (default strategy).** POST `{BaseUrl}/v1/oauth2/token`, `Authorization: Basic base64(clientId:clientSecret)`, form body `grant_type=client_credentials` (+ `scope` when `Scope` set) (`OAuth2ClientCredentialsStrategy.cs`).
- **Token caching.** The default scheme caches the token until it expires and refreshes under a lock (`OAuth2Scheme.cs`). **If `Oauth2` is null the client silently uses a no-auth scheme** — no construction-time error; calls simply go out unauthenticated and fail with 401. Always set `Oauth2`.
- **RetryOptions**: all members `required` — build a full instance or start from `RetryOptions.Default()` (sdk-map.md). Members: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout (TimeSpan?)`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`.

### 2.2 Orders — create + authorize (intent=AUTHORIZE)

`client.Orders` — map page `operations/Orders.md`.

| | CreateOrder | AuthorizeOrder | GetOrder |
|---|---|---|---|
| Signature | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must pass explicitly | `payPalMockResponse, payPalRequestId, payPalPartnerAttributionId, payPalClientMetadataId, payPalAuthAssertion` (pass `null`) | `payPalMockResponse, payPalRequestId, payPalClientMetadataId, payPalAuthAssertion, body` (pass `null`) | `fields, payPalMockResponse, payPalAuthAssertion` (pass `null`) |
| Returns | `Order` | `OrderAuthorizeResponse` | `Order` |
| Error | `SdkException<CreateOrderError>` — Case A: `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` | `SdkException<AuthorizeOrderError>` — Case A: `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` | `SdkException<GetOrderError>` — Case A: `TryGetError(out Error)` [401, 404] · `TryGetRawError(out RawError)` |

Request/response models (records pages `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`; all in `PayPalServerSdk.Models`):

- `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req` → `CheckoutPaymentIntent.Authorize` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` · `PaymentSource (payment_source): PaymentSource?` · `Payer (payer): Payer?` · `ApplicationContext (application_context): OrderApplicationContext?`
- `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req` · `ReferenceId (reference_id): string?` ← set to local order id · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `Description`, `SoftDescriptor`, `Payee`, `Items`, `Shipping`, … (optional)
- `AmountWithBreakdown`: `CurrencyCode (currency_code): string !req` (from config) · `Value (value): string !req` — **string, not decimal**: format the order total invariant-culture to the cent (e.g. `"109.00"`) · `Breakdown (breakdown): AmountBreakdown?`
- `PaymentSource` (plain record, NOT a union — set exactly one): `Card (card): CardRequest?` · `Token (token): Token?` · `Paypal`, `Bancontact`, `Blik`, `Eps`, `Giropay`, `Ideal`, `Mybank`, `P24`, `Sofort`, `Trustly`, `ApplePay`, `GooglePay`, `Venmo` (all optional)
- **Payment source (a) — one-off direct card:** `CardRequest`: `Number (number): string?` · `Expiry (expiry): string?` (`"YYYY-MM"`) · `SecurityCode (security_code): string?` · `Name (name): string?` · `BillingAddress (billing_address): Address?` · `Attributes (attributes): CardAttributes?` · `VaultId (vault_id): string?` · `StoredCredential (stored_credential): CardStoredCredential?`. `Address`: `CountryCode (country_code): string !req` · `AddressLine1/2 (address_line_1/2): string?` · `AdminArea2 (admin_area_2, city): string?` · `AdminArea1 (admin_area_1, state): string?` · `PostalCode (postal_code): string?`. CardRequest doc: passing PAN/CVV directly requires PCI SAQ D — sandbox test card with direct card processing enabled per the brief.
- **Payment source (b) — vaulted card:** `new PaymentSource { Card = new CardRequest { VaultId = vaultPaymentTokenId } }`. `CardRequest.VaultId` doc (source-verified, `Models/CardRequest.cs`): "The PayPal-generated ID for the vaulted payment source… stored on the merchant's server so the saved payment source can be used for future transactions." This is the fully-modeled path — prefer it over `PaymentSource.Token` (see Assumptions).
- `Order` / `OrderAuthorizeResponse` (same envelope shape): `Id (id): string?` · `Status (status): OrderStatus?` · `Intent (intent): CheckoutPaymentIntent?` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` · `Links (links): IReadOnlyList<LinkDescription>?` · `CreateTime/UpdateTime`. `Order` has `PaymentSource: PaymentSourceResponse?`; `OrderAuthorizeResponse` has `PaymentSource: OrderAuthorizeResponsePaymentSource?`.
- **Authorization id lives one level down:** `PurchaseUnit.Payments (payments): PaymentCollection?` → `PaymentCollection.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → `.Id`, `.Status (AuthorizationStatus?)`, `.Amount (Money?)`, `.ExpirationTime (expiration_time): string?`. Also `Captures (captures): IReadOnlyList<OrdersCapture>?`, `Refunds (refunds): IReadOnlyList<Refund>?`.
- `Money`: `CurrencyCode (currency_code): string !req` · `Value (value): string !req`.
- `LinkDescription`: `Href (href): string !req` · `Rel (rel): string !req` · `Method (method): LinkHttpMethod?`.
- `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` — when the payment source was already supplied on `CreateOrder`, pass `body: null`.

Flow: `CreateOrder(..., body: orderRequest)` → `AuthorizeOrder(id: order.Id, null, null, null, null, body: null)` → read authorization id + status from `PurchaseUnits[0].Payments.Authorizations[0]`. Persist: order id, authorization id, amount, currency, expiration_time.

### 2.3 Payments — capture / reauthorize / void / refund / gets

`client.Payments` — map page `operations/Payments.md`. All Case A typed errors; every one also has `TryGetNoContent(out RawError)` mapped to **[500]** plus the `TryGetRawError(out RawError)` fallback.

| Operation | Signature (must-pass-explicitly params in **bold**-ish — all the `string?` ones before `prefer`; pass `null`) | Returns | Typed accessors |
|---|---|---|---|
| CaptureAuthorizedPayment | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CapturedPayment` | `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] |
| ReauthorizePayment | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentAuthorization` | `TryGetError(out Error)` [400, 401, 403, 404, 422] |
| VoidPayment | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — note param order: `payPalAuthAssertion` BEFORE `payPalRequestId` | `PaymentAuthorization` | `TryGetError(out Error)` [401, 403, 404, 409, 422] |
| RefundCapturedPayment | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `Refund` | `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] |
| GetAuthorizedPayment | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentAuthorization` | `TryGetError(out Error)` [401, 403, 404] |
| GetCapturedPayment | `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CapturedPayment` | `TryGetError(out Error)` [401, 403, 404] |
| GetRefund | `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `Refund` | `TryGetError(out Error)` [401, 403, 404] |

Models (records pages):

- `CaptureRequest`: `Amount (amount): Money?` · `InvoiceId (invoice_id): string?` · `FinalCapture (final_capture): bool? = false` · `NoteToPayer (note_to_payer): string?` · `SoftDescriptor`, `PaymentInstruction`. Omit `Amount` (null body or amountless) for full capture; set `FinalCapture = true` when nothing further will be captured.
- `CapturedPayment` — the fulfilment payload: `Id (id): string?` · `Status (status): CaptureStatus?` · `StatusDetails (status_details): CaptureStatusDetails?` (`Reason: CaptureIncompleteReason?`) · `Amount (amount): Money?` · `FinalCapture (final_capture): bool?` · **`SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`** · `InvoiceId`, `CustomId`, `Links`, `CreateTime`, `UpdateTime`.
- `SellerReceivableBreakdown`: `GrossAmount (gross_amount): Money !req` · `PaypalFee (paypal_fee): Money?` · `NetAmount (net_amount): Money?` · `PaypalFeeInReceivableCurrency`, `ReceivableAmount`, `ExchangeRate`, `PlatformFees`. Doc: **"not available for transactions that are in pending state"** — read it only when `Status == CaptureStatus.Completed`; pass `prefer: "return=representation"` on the capture call so the full body (not the minimal default) comes back.
- `PaymentAuthorization`: `Id`, `Status (status): AuthorizationStatus?`, `StatusDetails.Reason (AuthorizationIncompleteReason?)`, `Amount: Money?`, `InvoiceId`, `CustomId`, `ExpirationTime (expiration_time): string?`, `Links`, `CreateTime`, `UpdateTime`.
- `ReauthorizeRequest`: `Amount (amount): Money?` — **only** `amount` is supported.
- `RefundRequest`: `Amount (amount): Money?` — **null/omitted amount = full refund; amount set = partial refund** · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `NoteToPayer (note_to_payer): string?`.
- `Refund`: `Id`, `Status (status): RefundStatus?`, `StatusDetails.Reason (RefundIncompleteReason?)`, `Amount: Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (`TotalRefundedAmount (total_refunded_amount): Money?`, `NetAmount`, `PaypalFee`…), `InvoiceId`, `CustomId`, `Links`, `CreateTime`, `UpdateTime`.
- **Idempotency (source-verified, `Api/Payments.cs`):** the `payPalRequestId` parameter serializes to the **`PayPal-Request-Id`** header; doc: "The server stores keys for 45 days." Present on `RefundCapturedPayment`, `CaptureAuthorizedPayment`, `ReauthorizePayment`, `VoidPayment`, `CreateOrder`, `AuthorizeOrder`. Reuse the same key for a logical refund to make retries safe; use a **distinct** key per distinct partial refund. Also set it on create/capture/void for the same reason.

**Reauthorize limits (from the operation's map row, `operations/Payments.md`):** initial 3-day honor period; reauthorize from day 4 through day 29 after the original authorization; at 30 days you must create a NEW authorization (reauthorization is rejected); a reauthorized payment gets a fresh 3-day honor period; allowed amount is context/geography-dependent — e.g. US: up to 115% of the original amount, max +$75 USD. **"Can no longer be renewed" signals:** `AuthorizationStatus` has no `Expired` member (values below) — a non-renewable authorization surfaces as a 4xx from `ReauthorizePayment`/`CaptureAuthorizedPayment` with `Error.Details[].Issue` (`ErrorDetails.Issue (issue): string !req`, plus `Field`, `Value`, `Description`); treat 422/404 there as terminal-for-renewal → create a new authorization. See Assumptions for a doc inconsistency on reauthorization count.

### 2.4 Vault v3 — save / list / get / delete

`client.Vault` — map page `operations/Vault.md`. All Case A with **`TryGetError1(out Error1)`** (note the `1` — different payload type from Orders/Payments) + `TryGetRawError(out RawError)` fallback.

| Operation | Signature | Returns | `TryGetError1` statuses |
|---|---|---|---|
| CreateSetupToken | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` (`payPalRequestId` must be passed explicitly) | `SetupTokenResponse` | [400, 403, 422, 500] |
| CreatePaymentToken | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` (`payPalRequestId` must be passed explicitly) | `PaymentTokenResponse` | [400, 403, 404, 422, 500] |
| ListCustomerPaymentTokens | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` — query wires: `customer_id`, `page_size`, `page`, `total_required` | `CustomerVaultPaymentTokensResponse` | [400, 403, 500] |
| GetPaymentToken | `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenResponse` | [403, 404, 422, 500] |
| DeletePaymentToken | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `void` (Task) | [400, 403, 500] |
| GetSetupToken (optional, status check) | `GetSetupToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `SetupTokenResponse` | [403, 404, 422, 500] |

Models (records pages):

- `SetupTokenRequest`: `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req` · `Customer (customer): Customer?`. `SetupTokenRequestPaymentSource`: set one — `Card (card): SetupTokenRequestCard?` (also `Paypal`, `Venmo`, `ApplePay`, `Token`, `Bank`).
- `SetupTokenRequestCard`: `Number (number): string?` · `Expiry (expiry): string?` · `SecurityCode (security_code): string?` · `Name (name): string?` · `Brand (brand): CardBrand?` · `BillingAddress (billing_address): Address?` · `VerificationMethod (verification_method): VaultCardVerificationMethod?` · `ExperienceContext (experience_context): VaultCardExperienceContext?`.
- `Customer`: `Id (id): string?` (PayPal customer id) · `MerchantCustomerId (merchant_customer_id): string?` (your shopper id).
- `SetupTokenResponse`: `Id (id): string?` · `Status (status): PaymentTokenStatus? = Created` · `Customer`, `PaymentSource`, `Links`.
- **Flow A (setup token → payment token):** `CreateSetupToken` → take `SetupTokenResponse.Id` → `CreatePaymentToken` with `PaymentTokenRequest { PaymentSource = new PaymentTokenRequestPaymentSource { Token = new VaultTokenRequest { Id = setupTokenId, Type = VaultTokenRequestType.SetupToken } }, Customer = … }`. `VaultTokenRequest`: `Id (id): string !req` · `Type (type): VaultTokenRequestType !req` (only value: `SetupToken (SETUP_TOKEN)`).
- **Flow B (direct vault):** `CreatePaymentToken` with `PaymentSource.Card = new PaymentTokenRequestCard { Number, Expiry, SecurityCode, Name, Brand, BillingAddress }` (all optional on the record; supply the real card).
- `PaymentTokenResponse`: `Id (id): string?` ← **the vault payment token id to store and later use as `CardRequest.VaultId`** · `Customer (customer): CustomerResponse?` (`Id`, `MerchantCustomerId`) · `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` · `Links`.
- **Safe card description (never PAN):** `PaymentTokenResponsePaymentSource.Card` → `CardPaymentTokenEntity`: `Brand (brand): CardBrand?` · `LastDigits (last_digits): string?` · `Expiry (expiry): string?` · `Name (name): string?` · `BillingAddress (billing_address): CardResponseAddress?` · `Type (type): CardType?` — the record has **no `Number` field at all**; safe by construction.
- `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Customer`, `Links`. **Pagination is manual**: loop `page` 1..`TotalPages` (default `pageSize` is only 5 — pass a larger one). For `customerId`, use the PayPal customer id (`CustomerResponse.Id`) returned at vault time — store it alongside your shopper id.
- `Error1` payload: `Name (name): string !req` · `Message (message): string !req` · `DebugId (debug_id): string !req` · `Details (details): IReadOnlyList<ErrorDetails1>?` (`Issue (issue): string !req`, `Field`, `Value`, `Description`) · `Links: IReadOnlyList<ErrorLinkDescription>?` — note `ErrorLinkDescription.Rel` is **nullable** (live API omits it on some errors; `records-1-Ac-Pa.md`).

### 2.5 Transaction Search v1 — reconciliation

`client.TransactionSearch` — map page `operations/TransactionSearch.md`.

`SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`

- Must pass explicitly (pass `null`): `transactionId, transactionType, transactionStatus, transactionAmount, transactionCurrency, paymentInstrumentType, storeId, terminalId`.
- Query wires: `start_date` ← `startDate`, `end_date` ← `endDate` (both ISO-8601 strings, required), `page_size` ← `pageSize` (default 100), `page` ← `page` (default 1), `fields` (default `"transaction_info"` — sufficient: everything below lives in `transaction_info`).
- **Returns** `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` · `Page (page): int?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `StartDate`, `EndDate`, `LastRefreshedDatetime`, `AccountNumber`, `Links`. **Pagination is manual — loop `page = 1..TotalPages`** and concatenate `TransactionDetails`, or the report silently truncates at page 1.
- `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?` — reconciliation fields: `TransactionId (transaction_id): string?` · `TransactionAmount (transaction_amount): Money?` · `FeeAmount (fee_amount): Money?` · `TransactionInitiationDate (transaction_initiation_date): string?` · `TransactionUpdatedDate (transaction_updated_date): string?` · `TransactionStatus (transaction_status): string?` (plain string, not an enum) · `InvoiceId (invoice_id): string?` · `CustomField (custom_field): string?` · `PaypalReferenceId (paypal_reference_id): string?` + `PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?` · `TransactionEventCode (transaction_event_code): string?`. Join keys against local orders: `InvoiceId` / `CustomField` (set them at order creation — §2.2) and the stored capture/authorization ids.
- **Error: Case B — `SdkException<RawError>`** (the SDK's only Case-B operation). No typed accessors: read `ex.Error.StatusCode` (`HttpStatusCode`) and `ex.Error.ReadAsString()` / `ReadAsJson<T>()`. (`SearchBalances`, out of scope, is the other op on this controller.)

### 2.6 Error payloads & core error types

- `SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`) exposes `.Error: TError`. Case A: `TError` = `{Operation}Error : ApiError` with the `TryGet…` accessors above + inherited `TryGetRawError(out RawError)` fallback. Case B: `TError` = `RawError`: `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes()`. (sdk-map.md *Error-handling model*)
- `Error` (Orders/Payments 4xx/5xx payload): `Name (name): string !req` · `Message (message): string !req` · `DebugId (debug_id): string !req` · `Details (details): IReadOnlyList<ErrorDetails>?` · `Links`. `ErrorDetails`: `Issue (issue): string !req` · `Field`, `Value`, `Location = "body"`, `Description`.
- **No-throw `…Result` variants: absent across the entire SDK** — every call is throw-only. (sdk-map.md)

### 2.7 Enum values actually needed (`PayPalServerSdk.Models.Enums`; `enums.md`)

Enums are `StringEnum<T>` records, **not** C# enums — use the static members below (or `Type.FromValue("WIRE")`); never `new`, never lowercase wire strings as member names.

| Enum | Members (C# name (wire)) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created`, `Saved`, `Approved`, `Voided`, `Completed`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` — **no Expired member** |
| `CaptureStatus` | `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed` |
| `RefundStatus` | `Cancelled`, `Failed`, `Pending`, `Completed` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover`, `Amex (AMEX)`, `Maestro`, … (30 values; `enums.md`) |
| `CardType` | `Credit`, `Debit`, `Prepaid`, `Store`, `Unknown` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` **only** — see Assumptions |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` only |
| `PaymentTokenStatus` | `Created`, `PayerActionRequired`, `Approved`, `Vaulted`, `Tokenized` |
| `VaultStatus` | `Vaulted`, `Created`, `Approved` |
| `VaultCardVerificationMethod` | `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `ScaAlways (SCA_ALWAYS)` |
| `OrdersCardVerificationMethod` | `ScaAlways`, `ScaWhenRequired` (default on `CardVerification.Method`), `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` (for `VaultInstruction.StoreInVault` if vault-on-success is later wanted at order time) |
| `PaymentInitiator` / `StoredPaymentSourcePaymentType` / `StoredPaymentSourceUsageType` | `Customer`/`Merchant` · `OneTime`/`Recurring`/`Unscheduled` · `First`/`Subsequent`/`Derived` — for `CardStoredCredential` on merchant-initiated repeat charges |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `LinkHttpMethod` | `Get`, `Post`, `Put`, `Delete`, `Head`, `Connect`, `Options`, `Patch` |

## 3. Trap notes (hazard named, resolution deliberately NOT inline — load the skill)

> ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind the SDK client has a lifetime contract (factory-managed, reused) that the constructor signature does not convey, and the DI helper's singleton registration interacts with it. **MUST load `dotnet-client-initialization`** before wiring the client.
>
> ⚠ Step 1 (auth) — credentials must be set before construction / in the DI callback and loaded from configuration (never hardcoded); the token-strategy extension point (`Oauth2TokenStrategy`) has a contract the options row doesn't show. Note the source-verified silent failure: null `Oauth2` ⇒ unauthenticated calls, no exception at build. **MUST load `dotnet-authentication`**.
>
> ⚠ Steps 2–10 (every call) — most optional parameters are nullable **with no C# default**: they mis-bind or fail to compile in positional calls; call with named arguments and pass explicit `null`s. **MUST load `dotnet-calling-endpoints`** before the first call.
>
> ⚠ Steps 2–10 (models) — enums are `StringEnum<T>` (statics/`FromValue`, not C# enum members), records are immutable with `required` init members, and unmodeled JSON fields are silently dropped on deserialize. **MUST load `dotnet-models`** the moment a field isn't a plain string/number.
>
> ⚠ Step 11 (error boundary) — which operations are Case A vs Case B, what `TryGetRawError` does and does not catch on a typed error, and the two `JsonException` directions in §4 all decide whether your boundary classifies failures correctly. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
>
> ⚠ Steps 1, 5–8 (resilience) — whether a failed write (create-order, capture, refund) can be re-sent by the SDK under the covers, and what `RetryOptions.Timeout` actually bounds, decides your idempotency-key discipline (`PayPal-Request-Id` on every write) and your outer timeout budget; there is also no built-in logging hook. **MUST load `dotnet-configuration-resilience`** before tuning `Retry`/`Timeout`.
>
> ⚠ Step 11 (tests) — the SDK's test seam is the `HttpClient` constructor argument; match eShopOnWeb's existing test framework/assertion style. **MUST load `dotnet-testing`** before stubbing the SDK.

## 4. REQUIRED READING (load ALL before implementation starts)

This sheet deliberately does not carry these skills' contents; loading them is part of the work.

- `dotnet-client-initialization` — Step 1: client construction, HttpClient ownership, DI registration.
- `dotnet-authentication` — Step 1: credentials wiring, token strategy, secret handling.
- `dotnet-calling-endpoints` — Steps 2–10: parameter passing, named arguments, envelopes.
- `dotnet-models` — Steps 2–10: records, `required` members, `StringEnum<T>`, wire names.
- `dotnet-error-handling` — Step 11: Case A/B mechanics, accessor use, the exception boundary.
- `dotnet-configuration-resilience` — Steps 1, 5–8: retries, timeouts, base URL, pagination, logging.
- `dotnet-testing` — Step 11: faking the SDK seam.

Two hazards belong in this first sheet because the boundary is written early — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

Assumptions (about intent — correct me via a revision if wrong):

1. **Server-side direct-card flow assumed**: payment_source supplied at order creation, then `AuthorizeOrder` — no buyer redirect/approval step (the `rel:approve` HATEOAS flow) is in scope. Direct PAN handling carries the PCI SAQ D burden noted on `CardRequest`; brief states sandbox test card with direct card processing enabled.
2. **One purchase unit per order**; `PurchaseUnitRequest.ReferenceId` = local order id, and `CustomId`/`InvoiceId` set at creation so Transaction Search rows join back to local orders.
3. **Environment config string** maps as: `"sandbox"` → SDK defaults; `"production"` → `Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"`; any other non-empty value → used verbatim as the base URL (this is the task's required override; source-verified that it also rewrites the OAuth token URL). The SDK models **no** `ServerEnvironment.Production` — production is only ever a base-URL override.
4. **Vaulted card as order payment_source uses `CardRequest.VaultId`** (fully modeled, doc-verified). The alternative `PaymentSource.Token` path requires a `TokenType` whose only modeled value is `BillingAgreement (BILLING_AGREEMENT)` (`Models/Enums/TokenType.cs`); the wire value a Vault v3 payment token would need is **UNVERIFIED** — do not guess it via `FromValue`; use the `vault_id` path.
5. **Reauthorization count doc inconsistency (map-visible):** the `ReauthorizePayment` operation note says "you can issue multiple re-authorizations after the honor period expires," while the `ReauthorizeRequest` model doc says "You can reauthorize a payment only once from days four to 29." Which is accurate is **UNVERIFIED** (only live traffic could settle it). Defensive directive: after any successful reauthorize, treat a further reauthorize attempt's 4xx as terminal and fall back to a new authorization; never retry a 422.
6. **Expired-authorization status is UNVERIFIED:** `AuthorizationStatus` models no `Expired` value, so what `GetAuthorizedPayment` returns for a lapsed authorization (status string vs 404) can only be confirmed against the live API. Defensive directive: decide renewability from the `ReauthorizePayment`/`CaptureAuthorizedPayment` error (`Error.Details[].Issue` + status code), not from a status-string match; extract `Issue`/`Message` best-effort and fall back to the generic `Error.Message`.
7. **Capture/refund full payloads:** pass `prefer: "return=representation"` on `CaptureAuthorizedPayment` and `RefundCapturedPayment` — the SDK default is `"return=minimal"`, and `SellerReceivableBreakdown` is documented as absent for pending transactions; whether minimal responses omit it is **UNVERIFIED**, so ask for the representation.
8. eShopOnWeb integration points (which project hosts the client, where config lives, existing DI/test conventions) are the implementer's repo knowledge — not blocking.

Blockers: none.
