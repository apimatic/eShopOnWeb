# PayPal .NET SDK — Contract Sheet (eShopOnWeb integration)

SDK: `AsadAli.Checkout.Sdk` · root namespace `PayPalServerSdk` · map release tag `v1.0.1`
(source commit `9653d18`) · target `netstandard2.0`. Every fact below is grounded in the bundled
SDK map (page cited per row) or, where the map fell short (base-URL override + OAuth token URL),
in the named SDK source file. Scope is C#/.NET only.

## 1. Scope & sequence

The integration touches 3 controllers. Suggested build order:

1. **Client + DI + auth + base-URL override** (§ client facts) — `AddPayPalServerSdkClient`.
2. **A. Create Order** (`Orders.CreateOrder`, intent AUTHORIZE, raw card or vaulted card).
3. **B. Authorize** (`Orders.AuthorizeOrder`).
4. **C. Capture authorization** (`Payments.CaptureAuthorizedPayment`).
5. **D. Reauthorize** (`Payments.ReauthorizePayment`).
6. **E. Void** (`Payments.VoidPayment`).
7. **F. Refund** (`Payments.RefundCapturedPayment`).
8. **G. Reads** (`Payments.GetAuthorizedPayment` / `GetCapturedPayment` / `GetRefund`, `Orders.GetOrder`).
9. **H. Vault** (`Vault.CreateSetupToken` → `Vault.CreatePaymentToken`, or direct card;
   `GetPaymentToken` / `ListCustomerPaymentTokens` / `DeletePaymentToken`).
10. **I. Transaction search** (`TransactionSearch.SearchTransactions`, paged).

Package (install version-less, floats to latest): `dotnet add package AsadAli.Checkout.Sdk`.

---

## CONTRACT SHEET

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

### `using` namespaces (add each separately — child namespaces are NOT transitive)

| Contents | Namespace |
|---|---|
| Client, options, `ServerOptions` | `PayPalServerSdk` |
| `ServerEnvironment`, `DefaultOptions` (+ nested `SandboxOptions`) | `PayPalServerSdk.Servers` |
| Controllers (`client.Orders` etc.) | `PayPalServerSdk.Api` |
| Request/response records (`OrderRequest`, `Money`, `CardRequest`, …) | `PayPalServerSdk.Models` |
| Enums (`CheckoutPaymentIntent`, `OrderStatus`, …) | `PayPalServerSdk.Models.Enums` |
| Typed error payloads `{Op}Error` (thrown as `SdkException<…>`) | `PayPalServerSdk.Errors` |
| `SdkException<TError>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` |
| `RequestOptions` | `PayPalServerSdk.Core` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` |

### Client construction, auth, environment, base-URL override

- **Client class**: `PayPalServerSdkClient` · single ctor
  `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`.
  API groups are properties: `client.Orders`, `client.Payments`, `client.Vault`,
  `client.TransactionSearch`. (`sdk-map.md`)
- **DI**: `services.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)`
  (namespace `PayPalServerSdk`, source `ServiceCollectionExtensions.cs`). It calls
  `services.AddHttpClient()` internally, resolves an `IHttpClientFactory`, creates one
  `HttpClient`, and registers the **`PayPalServerSdkClient` as a singleton**. So the SDK client is
  a singleton over a factory-created `HttpClient` — do not also `new` one per request. (source
  `ServiceCollectionExtensions.cs`)
- **Environment (sandbox)**: `options.Environment = ServerEnvironment.Sandbox;`
  `ServerEnvironment` is a `StringEnum`, only member is `Sandbox`; `ServerEnvironment.Default()`
  is `Sandbox`. (source `Servers/ServerEnvironment.cs`)
- **OAuth2 client-credentials**: set `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …,
  ClientSecret = … }`. Shape (source
  `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`):
  `ClientId: string` **required**, `ClientSecret: string` **required**, `Scope: string?` optional.
  Load id/secret from configuration, not literals.
- **Base-URL verbatim override (the `PayPal:BaseUrl` requirement) — resolved from source:**
  set `options.Server.Default.Sandbox.BaseUrl = payPalBaseUrl;`
  - `options.Server` is `ServerOptions` (ns `PayPalServerSdk`) → `.Default` is `DefaultOptions`
    (ns `PayPalServerSdk.Servers`) → `.Sandbox` is nested `DefaultOptions.SandboxOptions` →
    `.BaseUrl` is a `string` (default `"https://api-m.sandbox.paypal.com"`).
    (source `ServerOptions.cs`, `Servers/DefaultOptions.cs`)
  - Every request URL is built as `new UrlTemplate(Sandbox.BaseUrl, path, [])` — the `BaseUrl` is
    used **verbatim as the origin/root** and the operation path (e.g. `/v2/checkout/orders`) is
    appended. So `BaseUrl` must be the scheme+host root **without** a trailing operation path.
  - **CRITICAL, confirmed in source**: the OAuth2 token request URL is
    `server.Default("/v1/oauth2/token")` — i.e. the token/credential request is resolved through
    the **same** `Sandbox.BaseUrl` as every other call. Setting `BaseUrl` therefore applies
    verbatim to the token request too. (source `AuthSchemes.cs` line building
    `OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), …)`)
  - Because `ServerEnvironment` has only `Sandbox`, `DefaultOptions.Resolve` always resolves to
    `Sandbox.BaseUrl` regardless — there is no separate "production" node to set. When
    `PayPal:BaseUrl` is unset, leave the default; when set, assign it to `Sandbox.BaseUrl`.
- **Idempotency header `PayPal-Request-Id`**: it is NOT a header you add via `RequestOptions`; it
  is a **named string parameter** on each write operation (`payPalRequestId`). Pass your
  idempotency key there; pass `null` to omit. Which operations support it is noted per row below.

### Error model (applies to every call)

- All ops are **throw-based**; there are **no `…Result` no-throw variants** anywhere. On an error
  status the call throws `SdkException<TError>` (ns `PayPalServerSdk.Core.Exceptions`), which
  exposes **only** `.Error` (type `TError`). There is **no status-code property on the exception
  itself** (source `Core/Exceptions/SdkException.cs`).
- **Case A (typed)** — 39 of 40 ops. `TError` is a `{Op}Error : ApiError` (ns
  `PayPalServerSdk.Errors`) with status-specific `TryGet…(out payload)` accessors plus inherited
  `TryGetRawError(out RawError)`. Typed payloads are ordinary records in `PayPalServerSdk.Models`:
  - `Error` = `Name (name): string !req`, `Message (message): string !req`,
    `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`,
    `Links: IReadOnlyList<LinkDescription>?`.
  - `Error1` = same shape but `Details: IReadOnlyList<ErrorDetails1>?`,
    `Links: IReadOnlyList<ErrorLinkDescription>?` (used by all Vault ops).
  - `ErrorDetails` = `Field?`, `Value?`, `Location (= "body")?`, `Issue (issue): string !req`,
    `Description?`. (`records-1-Ac-Pa.md`)
- **Case B (raw)** — ONLY `TransactionSearch.SearchTransactions`. `TError` is `RawError` (ns
  `PayPalServerSdk.Core.ErrorResponse`): `StatusCode: HttpStatusCode`, `ReadAsString(): string`,
  `ReadAsJson<T>(): T?`, `ReadAsBytes()`.
- **Reading the numeric HTTP status**: the typed `Error`/`Error1` payloads carry NO status field —
  the numeric status is available via `ex.Error.TryGetRawError(out RawError raw)` →
  `raw.StatusCode`, or (Case B) directly `ex.Error.StatusCode`. Which typed accessor fires for
  which status, and the `TryGetRawError`-is-not-a-catch-all trap, are covered by
  `dotnet-error-handling` (see trap notes). (`sdk-map.md` error section)
- **Reading HTTP status on SUCCESS**: success returns the **bare payload model** (e.g. `Order`,
  `CapturedPayment`) — there is **no `ApiResponse<T>` envelope and no HTTP-status property on a
  successful return**. Judge success by the resource-level `Status` enum on the returned model,
  not an HTTP code.

---

### A. Create Order — `client.Orders.CreateOrder` (`operations/Orders.md`)

- **Signature**: `CreateOrder(string? payPalMockResponse, string? payPalRequestId,
  string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion,
  OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null,
  CancellationToken ct = default)`
  - First 5 params are nullable-no-default → **must pass explicitly** (`null` to skip).
  - **Idempotency**: pass your key as `payPalRequestId` (2nd arg).
  - Consider passing `prefer: "return=representation"` so the response body is fully populated
    (default `"return=minimal"` returns a sparse body).
- **Request `body`: `OrderRequest`** (`records-1-Ac-Pa.md`):
  `Intent (intent): CheckoutPaymentIntent !req` → `CheckoutPaymentIntent.Authorize`;
  `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`;
  `PaymentSource (payment_source): PaymentSource?`; `Payer?`; `ApplicationContext?`.
- **`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`): `Amount (amount): AmountWithBreakdown !req`;
  optional `ReferenceId?`, `CustomId?`, `InvoiceId?`, `Description?`, `Items?`, `Payee?`.
- **`AmountWithBreakdown`** (`records-1-Ac-Pa.md`): `CurrencyCode (currency_code): string !req`,
  `Value (value): string !req` (decimal-string to the cent, e.g. `"49.99"`),
  `Breakdown (breakdown): AmountBreakdown?`.
- **`PaymentSource`** (`records-2-Pa-Ve.md`): `Card (card): CardRequest?`, `Token (token): Token?`,
  `Paypal?`, + wallet variants. For direct card use `Card`.
  - **Raw CARD → `CardRequest`** (`records-1-Ac-Pa.md`): `Name (name): string?`,
    `Number (number): string?` (`"4111111111111111"` sandbox), `Expiry (expiry): string?`
    (plain string; SDK does not model a format — pass the API's `YYYY-MM`),
    `SecurityCode (security_code): string?`, `BillingAddress (billing_address): Address?`,
    `VaultId (vault_id): string?`, `SingleUseToken?`, `Attributes?`, `StoredCredential?`.
  - **`Address`** (`records-1-Ac-Pa.md`): `AddressLine1?`, `AddressLine2?`,
    `AdminArea2 (admin_area_2): string?` (city), `AdminArea1 (admin_area_1): string?` (state),
    `PostalCode?`, `CountryCode (country_code): string !req`.
  - **VAULTED card variant**: reuse `PaymentSource.Card = new CardRequest { VaultId = "<vault-id>" }`
    — set `VaultId` (the payment-token id from capability H) instead of raw
    `Number`/`Expiry`/`SecurityCode`. (`CardRequest.VaultId`, `records-1-Ac-Pa.md`) The `Token`
    field (`Token { Id, Type }`) is for BILLING_AGREEMENT tokens only (`TokenType` has just
    `BillingAgreement`), NOT for a vaulted card — use `CardRequest.VaultId` for vaulted cards.
- **Returns `Order`** (`records-1-Ac-Pa.md`): read `Id (id): string?` (created order id),
  `Status (status): OrderStatus?`, `Links: IReadOnlyList<LinkDescription>?`,
  `PurchaseUnits: IReadOnlyList<PurchaseUnit>?`.
- **Error**: Case A `SdkException<CreateOrderError>` (ns `PayPalServerSdk.Errors`) —
  `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` [fallback].

### B. Authorize order — `client.Orders.AuthorizeOrder` (`operations/Orders.md`)

- **Signature**: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId,
  string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body,
  string? prefer = "return=minimal", RequestOptions? requestOptions = null,
  CancellationToken ct = default)`
  - `id` = order id from A. Params 2–6 must pass explicitly. **Idempotency**: `payPalRequestId`.
  - `body` (`OrderAuthorizeRequest`) is optional — pass `null` when the order was created with the
    card already in `payment_source` (typical for direct-card AUTHORIZE). `OrderAuthorizeRequest`
    only carries `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`.
    (`records-1-Ac-Pa.md`)
- **Returns `OrderAuthorizeResponse`** (`records-1-Ac-Pa.md`): the authorization sits at
  `resp.PurchaseUnits[i].Payments.Authorizations[j]`. Path types:
  `OrderAuthorizeResponse.PurchaseUnits: IReadOnlyList<PurchaseUnit>?` →
  `PurchaseUnit.Payments: PaymentCollection?` →
  `PaymentCollection.Authorizations: IReadOnlyList<AuthorizationWithAdditionalData>?`
  (`records-2-Pa-Ve.md`). Each `AuthorizationWithAdditionalData`: `Id (id): string?`
  (**authorization id**), `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`
  (**authorized amount**; `Money.CurrencyCode`, `Money.Value`). Read defensively — the collections
  are nullable and only populated when `prefer=return=representation`.
- **Error**: Case A `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)`
  [400,401,403,404,422,500] · `TryGetRawError` [fallback].

### C. Capture an authorization — `client.Payments.CaptureAuthorizedPayment` (`operations/Payments.md`)

- **Signature**: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse,
  string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body,
  string? prefer = "return=minimal", RequestOptions? requestOptions = null,
  CancellationToken ct = default)`
  - `authorizationId` from B. Params 2–5 must pass explicitly. **Idempotency**: `payPalRequestId`.
  - `body` (`CaptureRequest`, optional — `null` = capture full authorized amount)
    (`records-1-Ac-Pa.md`): `Amount (amount): Money?` (partial capture),
    `FinalCapture (final_capture): bool? = false`, `InvoiceId?`, `NoteToPayer?`, `SoftDescriptor?`.
- **Returns `CapturedPayment`** (`records-1-Ac-Pa.md`): `Id (id): string?` (**capture id**),
  `Status (status): CaptureStatus?`, `Amount (amount): Money?` (**captured amount**),
  `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`.
  - **`SellerReceivableBreakdown`** (`records-2-Pa-Ve.md`):
    `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?`,
    `NetAmount (net_amount): Money?` → gross / fee / net (each `Money` = `CurrencyCode`, `Value`).
- **Error**: Case A `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)`
  [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback].

### D. Reauthorize — `client.Payments.ReauthorizePayment` (`operations/Payments.md`)

- **Signature**: `ReauthorizePayment(string authorizationId, string? payPalRequestId,
  string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal",
  RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - Params 2–4 must pass explicitly. **Idempotency**: `payPalRequestId`.
  - `body` (`ReauthorizeRequest`, optional) (`records-2-Pa-Ve.md`): only `Amount (amount): Money?`.
- **Returns `PaymentAuthorization`** (`records-2-Pa-Ve.md`): `Id (id): string?`,
  `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime?`.
- **Error**: Case A `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)`
  [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback].

### E. Void an authorization — `client.Payments.VoidPayment` (`operations/Payments.md`)

- **Signature**: `VoidPayment(string authorizationId, string? payPalMockResponse,
  string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal",
  RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - **No request-body param.** Params 2–4 must pass explicitly. **Idempotency**: `payPalRequestId`
    (note: it is the **4th** param here, after `payPalAuthAssertion`).
- **Returns `PaymentAuthorization`** (`records-2-Pa-Ve.md`): expected `Status` =
  `AuthorizationStatus.Voided`.
  - `UNVERIFIED` (only live traffic can confirm): the live void endpoint commonly returns
    `204 No Content`, so the returned `PaymentAuthorization` fields may all be null. Code
    defensively — do not assume `Id`/`Status` are populated; if you need to confirm the voided
    state, re-read via `GetAuthorizedPayment` (G).
- **Error**: Case A `SdkException<VoidPaymentError>` — `TryGetError(out Error)`
  [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback].

### F. Refund a captured payment — `client.Payments.RefundCapturedPayment` (`operations/Payments.md`)

- **Signature**: `RefundCapturedPayment(string captureId, string? payPalMockResponse,
  string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body,
  string? prefer = "return=minimal", RequestOptions? requestOptions = null,
  CancellationToken ct = default)`
  - `captureId` from C. Params 2–5 must pass explicitly. **Idempotency**: `payPalRequestId`.
  - **Full refund**: `body: null` (empty body). **Partial refund**: `body = new RefundRequest {
    Amount = new Money { CurrencyCode = "USD", Value = "10.00" } }`.
  - `RefundRequest` (`records-2-Pa-Ve.md`): `Amount (amount): Money?`, `CustomId?`, `InvoiceId?`,
    `NoteToPayer?`, `PaymentInstruction?`.
- **Returns `Refund`** (`records-2-Pa-Ve.md`): `Id (id): string?` (**refund id**),
  `Status (status): RefundStatus?`, `Amount (amount): Money?`,
  `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?`
  (`GrossAmount?`, `PaypalFee?`, `NetAmount?`, `TotalRefundedAmount?`).
- **Error**: Case A `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)`
  [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback].

### G. Reads by id (`operations/Payments.md`, `operations/Orders.md`)

- **Get authorization**: `client.Payments.GetAuthorizedPayment(string authorizationId,
  string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null,
  CancellationToken ct = default)` → `PaymentAuthorization` (read `Status`, `Amount`, `Id`).
  Error Case A `GetAuthorizedPaymentError`.
- **Get capture**: `client.Payments.GetCapturedPayment(string captureId,
  string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  → `CapturedPayment`. Error Case A `GetCapturedPaymentError`.
- **Get refund**: `client.Payments.GetRefund(string refundId, string? payPalMockResponse,
  string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  → `Refund`. Error Case A `GetRefundError`.
- **Get order**: `client.Orders.GetOrder(string id, string? fields, string? payPalMockResponse,
  string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  → `Order`. Error Case A `GetOrderError` [401,404].
  (All the `payPal*`/`fields` params are nullable-no-default → pass explicitly.)

### H. Vault / payment method tokens — `client.Vault.*` (`operations/Vault.md`)

Two supported flows. **Direct** (one call) or **setup→payment** (two calls). All Vault errors are
Case A with accessor **`TryGetError1(out Error1)`** (note `Error1`, not `Error`) + `TryGetRawError`.

- **Direct payment token from a raw card**:
  `client.Vault.CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body,
  RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentTokenResponse`.
  **Idempotency**: `payPalRequestId`.
  - `PaymentTokenRequest` (`records-2-Pa-Ve.md`): `Customer (customer): Customer?`,
    `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.
  - `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?`,
    `Token (token): VaultTokenRequest?`.
  - `PaymentTokenRequestCard`: `Name?`, `Number?`, `Expiry?`, `SecurityCode?`,
    `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`.
- **Setup token first** (deferred/approval flows):
  `client.Vault.CreateSetupToken(string? payPalRequestId, SetupTokenRequest body,
  RequestOptions? requestOptions = null, CancellationToken ct = default)` → `SetupTokenResponse`
  (read `Id`, `Status: PaymentTokenStatus?`). Then call `CreatePaymentToken` with
  `PaymentTokenRequestPaymentSource.Token = new VaultTokenRequest { Id = setupToken.Id,
  Type = VaultTokenRequestType.SetupToken }` to exchange the setup token for the durable payment
  token. (`VaultTokenRequest` `records-2-Pa-Ve.md`: `Id !req`, `Type !req`.)
  - `SetupTokenRequest` (`records-2-Pa-Ve.md`): `Customer?`,
    `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req`;
    `SetupTokenRequestCard`: `Name?`, `Number?`, `Expiry?`, `SecurityCode?`, `Brand?`,
    `BillingAddress?`, `VerificationMethod?`, `ExperienceContext?`.
- **`PaymentTokenResponse`** (`records-2-Pa-Ve.md`): `Id (id): string?` (**vault/token id** — this
  is the value to put in `CardRequest.VaultId` in capability A), `Customer (customer):
  CustomerResponse?`, `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?`, `Links`.
  - **Safe card description**: `PaymentTokenResponsePaymentSource.Card` is a `CardPaymentTokenEntity`
    (`records-1-Ac-Pa.md`): `LastDigits (last_digits): string?` (**last 4**),
    `Brand (brand): CardBrand?` (**network**), `Expiry (expiry): string?`, `Type (type): CardType?`,
    `Name?`, `BillingAddress?`.
- **Customer association**: `Customer` (`records-1-Ac-Pa.md`): `Id (id): string?` (PayPal customer
  id — reuse across tokens to group them), `MerchantCustomerId (merchant_customer_id): string?`
  (your own id). On first vault omit `Id` and PayPal mints one (returned in
  `PaymentTokenResponse.Customer.Id`); reuse it on subsequent vaults for the same shopper.
- **Get token**: `client.Vault.GetPaymentToken(string id, RequestOptions? requestOptions = null,
  CancellationToken ct = default)` → `PaymentTokenResponse`.
- **List a customer's tokens**: `client.Vault.ListCustomerPaymentTokens(string customerId,
  int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null,
  CancellationToken ct = default)` → `CustomerVaultPaymentTokensResponse` (`records-1-Ac-Pa.md`:
  `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`,
  `Customer (customer): VaultResponseCustomer?`,
  `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `Links`). Wire query:
  `customer_id`←`customerId`, `page_size`←`pageSize`, `page`←`page`, `total_required`←`totalRequired`.
  Pass `totalRequired: true` to get `TotalItems`/`TotalPages` populated. Call with **named
  arguments** (optional params have defaults).
- **Delete token**: `client.Vault.DeletePaymentToken(string id, RequestOptions? requestOptions = null,
  CancellationToken ct = default)` → `void` (Task).

### I. Transaction search — `client.TransactionSearch.SearchTransactions` (`operations/TransactionSearch.md`)

- **Signature**: `SearchTransactions(string startDate, string endDate, string? transactionId,
  string? transactionType, string? transactionStatus, string? transactionAmount,
  string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId,
  string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y",
  int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null,
  CancellationToken ct = default)`
  - **Required**: `startDate`, `endDate` — ISO-8601 strings (wire `start_date` / `end_date`).
  - The **8** params `transactionId … terminalId` are nullable-no-default → **must pass
    explicitly** (`null` to skip). **Call with named arguments** — positional binding of the
    optional tail (`fields`, `pageSize`, `page`) mis-binds easily.
  - Range limits (from op notes): lists up to the previous three years; executed transactions can
    take up to 3 hours to appear.
- **Returns `SearchResponse`** (`records-2-Pa-Ve.md`):
  `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`,
  `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`,
  `Links (links): IReadOnlyList<LinkDescription>?`.
  - Per-item: `TransactionDetails.TransactionInfo` is `TransactionInformation?`
    (`records-2-Pa-Ve.md`): `TransactionId (transaction_id): string?`,
    `TransactionAmount (transaction_amount): Money?`,
    `TransactionStatus (transaction_status): string?` (**plain string, not an SDK enum** — PayPal
    sends codes like `S`/`P`/`V`/`D`), `FeeAmount?`, `TransactionInitiationDate?`.
- **Pagination (page WHOLE range)**: use `page` (1-based) + `pageSize` (SDK default 100). Read
  `SearchResponse.TotalPages` and loop `page = 1 … TotalPages`, or follow the HATEOAS `next` link
  in `SearchResponse.Links` (`LinkDescription.Rel == "next"`). There is no `perPage`/cursor beyond
  `page`/`page_size`. (`operations/TransactionSearch.md` marks pagination "only `page`".)
- **Error — THE ONLY Case B op**: `SdkException<RawError>` (ns
  `PayPalServerSdk.Core.ErrorResponse`). **No typed accessors** — read `ex.Error.StatusCode`
  (`HttpStatusCode`) and `ex.Error.ReadAsString()` / `ex.Error.ReadAsJson<T>()`. A catch ladder
  written for `SdkException<{Op}Error>` will NOT catch this — catch `SdkException<RawError>`
  specifically for this call.

---

### Enum value tables (needed by scope) — ns `PayPalServerSdk.Models.Enums` (`models/enums.md`)

Write the C# member (left); the wire value (right) is what crosses the wire.

| Enum | Members (`CSharp` = `WIRE`) |
|---|---|
| `CheckoutPaymentIntent` | `Capture`=CAPTURE, `Authorize`=AUTHORIZE |
| `OrderStatus` | `Created`, `Saved`, `Approved`, `Voided`, `Completed`, `PayerActionRequired`=PAYER_ACTION_REQUIRED |
| `AuthorizationStatus` | `Created`, `Captured`, `Denied`, `PartiallyCaptured`=PARTIALLY_CAPTURED, `Voided`, `Pending` |
| `CaptureStatus` | `Completed`, `Declined`, `PartiallyRefunded`=PARTIALLY_REFUNDED, `Pending`, `Refunded`, `Failed` |
| `RefundStatus` | `Cancelled`, `Failed`, `Pending`, `Completed` |
| `CardType` | `Credit`, `Debit`, `Prepaid`, `Store`, `Unknown` |
| `CardBrand` | `Visa`, `Mastercard`, `Amex`, `Discover`, `Jcb`, `Diners`, `Maestro`, `Elo`, `Rupay`, `Unknown`, … (30 members) |
| `TokenType` | `BillingAgreement`=BILLING_AGREEMENT (only member) |
| `VaultTokenRequestType` | `SetupToken`=SETUP_TOKEN (only member) |
| `PaymentTokenStatus` | `Created`, `PayerActionRequired`=PAYER_ACTION_REQUIRED, `Approved`, `Vaulted`, `Tokenized` |
| `VaultStatus` | `Vaulted`, `Created`, `Approved` |

Enums are `StringEnum<T>`, **not** C# enums — write `CheckoutPaymentIntent.Authorize` or
`CardBrand.FromValue("VISA")`. `Money` (`records-1-Ac-Pa.md`) = `CurrencyCode (currency_code):
string !req`, `Value (value): string !req` — amounts are decimal **strings**.

---

## Trap notes (load the named skill at that step — do not resolve inline)

> ⚠ Step 1 (client + DI) — whether the `HttpClient`/handler is long-lived and how the SDK-client
> lifetime relates to it is not shown by the ctor; the DI helper's singleton-over-factory choice
> has consequences you must not fight. **MUST load `dotnet-client-initialization`** before wiring
> the client or writing a custom factory.

> ⚠ Step 1 (auth) — when/where credentials must be set relative to client construction, and how to
> source the secret, are not shown by the property. **MUST load `dotnet-authentication`** before
> setting `Oauth2`.

> ⚠ Step 1 (base URL / retries / timeouts / pagination) — the SDK retry/timeout options do **not**
> bound a whole call and are **not** the `HttpClient` timeout; whether a failed write can be
> re-sent depends on transport-failure vs status-trigger retry behaviour that the option names hide
> (critical for the non-idempotent POSTs in A–F — pair with `PayPal-Request-Id`). **MUST load
> `dotnet-configuration-resilience`** before tuning the client or relying on paging.

> ⚠ Steps 2–9 (any op call) — optional params with no C# default mis-bind in positional calls;
> use named arguments (esp. `SearchTransactions`, `ListCustomerPaymentTokens`). **MUST load
> `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 2–9 (building request bodies) — `PaymentSource`/`OrderAuthorizeRequestPaymentSource` etc.
> are option objects, enums are `StringEnum<T>` (not C# enums), and unmodeled JSON is dropped on
> deserialize. **MUST load `dotnet-models`** before constructing payloads or mapping responses.

> ⚠ All steps (error boundary) — which typed accessor fires for which status, that
> `TryGetRawError` is not a catch-all on typed errors, and that `SearchTransactions` alone throws
> `SdkException<RawError>` — these decide whether your catch ladder is correct. **MUST load
> `dotnet-error-handling`** before writing any try/catch.

> ⚠ Step (tests) — the `HttpClient` ctor argument is the test seam. **MUST load `dotnet-testing`**
> before stubbing the SDK.

---

## REQUIRED READING — load BEFORE implementation starts

The sheet deliberately does NOT carry these skills' contents (defaults, worked examples, wiring you
must still do yourself). Load each before writing the code for its step:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — setting OAuth2 client-credentials, auth-manager shape |
| `dotnet-configuration-resilience` | Step 1 — base-URL override, retries/timeouts, pagination |
| `dotnet-calling-endpoints` | Steps 2–10 — named-argument calling, async, cancellation |
| `dotnet-models` | Steps 2–10 — request/response models, enums, option objects |
| `dotnet-error-handling` | ALL — the try/catch boundary (always required) |
| `dotnet-testing` | Tests — the SDK test seam |

**Mandatory boundary caveats — `System.Text.Json.JsonException` reaches the boundary from two
directions and they need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException`
  from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets
  it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

- **Path**: the brief did not dictate a plan path, so this file was written to the default
  `<repo root>/paypal-plan.md` (`C:\claude-runs\t3ali-task3-plugin-opus48high-013\repo\paypal-plan.md`).
- **Assumption**: "sandbox" maps to `ServerEnvironment.Sandbox` — the SDK exposes **only** a
  Sandbox environment (no `Production` member). A production/live PayPal host must be reached by
  setting `options.Server.Default.Sandbox.BaseUrl` to the live URL (the same override used for the
  `PayPal:BaseUrl` requirement); there is no separate live environment node. Confirm the intended
  live host with the team.
- **Assumption**: the AUTHORIZE-intent order carries the card in `payment_source` at CreateOrder
  time, so `AuthorizeOrder` is called with `body: null`. If instead the card is supplied at
  authorize time, populate `OrderAuthorizeRequest.PaymentSource`.
- **`UNVERIFIED` (live-traffic only)**: `VoidPayment` may return `204 No Content`, leaving the
  returned `PaymentAuthorization` fields null — read defensively and, if needed, confirm via
  `GetAuthorizedPayment`. Likewise, `prefer` defaults to `"return=minimal"`, so success bodies
  (A–F) can be sparse; request `"return=representation"` where you need populated fields, and still
  code the response reads defensively (nullable throughout).
- No blockers: every operation A–I is covered by the SDK map; there are **no genuine gaps**.
