# PayPal Server SDK (.NET) integration plan — eShopOnWeb payments & saved cards

Scope: add PayPal-backed payments (authorize at checkout, capture at fulfilment, void on cancel,
refund after fulfilment), saved cards (vault), and a reconciliation report to `src/PublicApi`.

---

## 1. Scope & sequence

| # | Step | SDK operations used |
| --- | --- | --- |
| 1 | Reference the SDK (built from source; not on a package feed) from `src/Infrastructure`. | — |
| 2 | `PayPalSettings` bound from configuration section `PayPal` (`ClientId`, `ClientSecret`, `Environment`, `Currency`, `BaseUrl`), validated on start. | — |
| 3 | DI registration of `PayPalClient` (singleton over an `IHttpClientFactory` client). | — |
| 4 | Domain: `Order` gains a lifecycle state; new `Payment` aggregate (+ `PaymentRefund`), new `SavedCard` entity. Persistence via existing `IRepository<T>`/`CatalogContext`. | — |
| 5 | `IPaymentGateway` (ApplicationCore, SDK-free) + `PayPalPaymentGateway` (Infrastructure). | all below |
| 6 | Place order — no PayPal call. | — |
| 7 | Pay = **authorize**: create a PayPal order with intent `AUTHORIZE`, then authorize it with a card (raw or `vault_id`). | `Orders.CreateOrder`, `Orders.AuthorizeOrder` |
| 8 | Fulfil = **capture**, with re-authorization when the hold went stale. | `Payments.GetAuthorizedPayment`, `Payments.ReauthorizePayment`, `Payments.CaptureAuthorizedPayment` |
| 9 | Cancel = **void** the hold. | `Payments.VoidPayment` |
| 10 | Refund (full / partial, caller-supplied idempotency key). | `Payments.RefundCapturedPayment` |
| 11 | Saved cards: create / list / delete. | `Vault.CreatePaymentToken`, `Vault.DeletePaymentToken` (fallback: `Vault.CreateSetupToken` → `Vault.CreatePaymentToken` with `token`) |
| 12 | Reconciliation over an arbitrary range: chunk into ≤31-day windows, page each window to exhaustion. | `TransactionSearch.SearchTransactions` |
| 13 | PublicApi endpoints, JWT-authenticated; operator actions restricted to the `Administrators` role. | — |
| 14 | Unit tests + live sandbox verification. | — |

---

## 2. CONTRACT SHEET

> ⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C#
> identifier — the cancellation-token parameter really is named `ct`, so a named argument writes
> `ct:`. Parameters listed as "must pass explicitly" are nullable with no default: pass `null` to skip.
>
> ⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**, taken
> from the path the map gives for THAT type, never from where a neighbouring type sits.
> `Api/` → `PayPal.Api` · `Models/` → `PayPal.Models` · `Models/Enums/` → `PayPal.Models.Enums` ·
> `Errors/` → `PayPal.Errors` · client/options/`Server*` → `PayPal` · `Servers/` → `PayPal.Servers`.

### 2.1 Client, auth, servers

| Fact | Value | Source |
| --- | --- | --- |
| Root namespace / client / options | `PayPal` · `PayPal.PayPalClient` · `PayPal.PayPalClientOptions` | `sdk-map.md` |
| Only constructor | `PayPalClient(HttpClient httpClient, PayPalClientOptions options)` | `sdk-map.md`, `PayPalClient.cs` |
| Auth | `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = … }` (namespace `PayPal.Core.Authentication.OAuth2.ClientCredentials`) | `sdk-map.md` §Servers & auth, `PayPalClientOptions.cs` |
| Token endpoint | `/v1/oauth2/token`, resolved through `server.Default(...)` — i.e. **through the same base-URL override as every other call** | `AuthSchemes.cs` |
| Environments | **`ServerEnvironment.Production` is the only member** (wire value `production`); its default base URL is `https://api-m.sandbox.paypal.com`. `ServerEnvironment.Match` throws `ArgumentOutOfRangeException` for anything else. | `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs` |
| Base-URL override point | `options.Server.Default.Production.BaseUrl` (default `https://api-m.sandbox.paypal.com`) | `Servers/DefaultOptions.cs` |
| Options surface | `Environment`, `Retry`, `Logging`, `Server`, `Hooks`, `Oauth2`, `Oauth2TokenStrategy` | `PayPalClientOptions.cs` |
| DI helper | `services.AddPayPalClient(...)` exists but builds options **once at registration** and is declared inside a C# 14 `extension(IServiceCollection)` block | `ServiceCollectionExtensions.cs` |
| SDK csproj | `netstandard2.0`, `LangVersion 14` | `PayPal.csproj` |

### 2.2 Operations

Legend — error case: **A** = `SdkException<{Operation}Error>` (typed), **B** = `SdkException<RawError>`.
All operations are throw-only, non-paginated, server group `Default` (map defaults).

| Op | Signature (verbatim) | Request model → fields used | Response → fields read | Error | Source |
| --- | --- | --- | --- | --- | --- |
| `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderRequest`: `Intent (intent)`: `CheckoutPaymentIntent` **required** · `PurchaseUnits (purchase_units)`: `IReadOnlyList<PurchaseUnitRequest>` **required** · `PaymentSource (payment_source)`: `PaymentSource?` | `Order`: `Id (id)`, `Status (status)`, `PurchaseUnits (purchase_units)` | A · `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` | `map/operations/Orders.md`; `Models/OrderRequest.cs`, `Models/Order.cs` |
| `client.Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderAuthorizeRequest`: `PaymentSource (payment_source)`: `OrderAuthorizeRequestPaymentSource?` → `Card (card)`: `CardRequest?` | `OrderAuthorizeResponse`: `Id (id)`, `Status (status)`: `OrderStatus?`, `PurchaseUnits (purchase_units)`: `IReadOnlyList<PurchaseUnit>?` | A · `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError(out RawError)` | `map/operations/Orders.md`; `Models/OrderAuthorizeRequest.cs`, `Models/OrderAuthorizeResponse.cs` |
| `client.Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentAuthorization`: `Id (id)`, `Status (status)`: `AuthorizationStatus?`, `Amount (amount)`: `Money?`, `ExpirationTime (expiration_time)`: `string?` | A · `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` | `map/operations/Payments.md`; `Models/PaymentAuthorization.cs` |
| `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ReauthorizeRequest`: `Amount (amount)`: `Money?` | `PaymentAuthorization` (as above) — **the new authorization id** | A · `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` | `map/operations/Payments.md`; `Models/ReauthorizeRequest.cs` |
| `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CaptureRequest`: `Amount (amount)`: `Money?` · `FinalCapture (final_capture)`: `bool?` (default `false`) · `InvoiceId (invoice_id)`: `string?` | `CapturedPayment`: `Id (id)`, `Status (status)`: `CaptureStatus?`, `Amount (amount)`: `Money?`, `SellerReceivableBreakdown (seller_receivable_breakdown)` | A · `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` | `map/operations/Payments.md`; `Models/CaptureRequest.cs`, `Models/CapturedPayment.cs` |
| `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (no body) | `PaymentAuthorization`: `Status (status)` expected `VOIDED` | A · `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` | `map/operations/Payments.md` |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `RefundRequest`: `Amount (amount)`: `Money?` · `InvoiceId (invoice_id)`: `string?` · `NoteToPayer (note_to_payer)`: `string?`. **Full refund = empty/`null` body; partial = `amount`.** | `Refund`: `Id (id)`, `Status (status)`: `RefundStatus?`, `Amount (amount)`: `Money?` | A · `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` | `map/operations/Payments.md`; `Models/RefundRequest.cs`, `Models/Refund.cs` |
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenRequest`: `Customer (customer)`: `Customer?` (`Id (id)`, `MerchantCustomerId (merchant_customer_id)`) · `PaymentSource (payment_source)`: `PaymentTokenRequestPaymentSource` **required** → `Card (card)`: `PaymentTokenRequestCard?` (`Name`, `Number`, `Expiry`, `SecurityCode`, `BillingAddress`) **or** `Token (token)`: `VaultTokenRequest?` (`Id` **required**, `Type` **required** = `VaultTokenRequestType.SetupToken`) | `PaymentTokenResponse`: `Id (id)`, `Customer (customer)`: `CustomerResponse?`, `PaymentSource (payment_source)` → `Card (card)`: `CardPaymentTokenEntity?` (`LastDigits (last_digits)`, `Brand (brand)`, `Expiry (expiry)`, `Name (name)`) | A · `TryGetError(out Error)` [400,403,404,422,500] · `TryGetRawError(out RawError)` | `map/operations/Vault.md`; `Models/PaymentTokenRequest.cs`, `Models/PaymentTokenResponse.cs`, `Models/PaymentTokenRequestCard.cs`, `Models/CardPaymentTokenEntity.cs` |
| `client.Vault.CreateSetupToken` *(fallback path)* | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `SetupTokenRequest`: `Customer (customer)`: `Customer?` · `PaymentSource (payment_source)`: `SetupTokenRequestPaymentSource` **required** → `Card (card)`: `SetupTokenRequestCard?` | `SetupTokenResponse`: `Id (id)`, `Status (status)`: `PaymentTokenStatus?`, `Customer`, `Links (links)` | A · `TryGetError(out Error)` [400,403,422,500] · `TryGetRawError(out RawError)` | `map/operations/Vault.md`; `Models/SetupTokenRequest.cs`, `Models/SetupTokenResponse.cs` |
| `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `void` (Task) | A · `TryGetError(out Error)` [400,403,500] · `TryGetRawError(out RawError)` | `map/operations/Vault.md` |
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query only; `start_date`/`end_date` are RFC-3339 with seconds; **max supported range 31 days**; `page` is the 1-relative page index | `SearchResponse`: `TransactionDetails (transaction_details)`: `IReadOnlyList<TransactionDetails>?` → `TransactionInfo (transaction_info)`: `TransactionInformation?` (`TransactionId`, `TransactionAmount`, `FeeAmount`, `TransactionStatus`, `TransactionInitiationDate`, `InvoiceId`, `CustomField`, `TransactionEventCode`) · `Page (page)`, `TotalItems (total_items)`, `TotalPages (total_pages)`, `LastRefreshedDatetime (last_refreshed_datetime)` | **B** — `SdkException<RawError>` only (`StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`) | `map/operations/TransactionSearch.md`; `Models/SearchResponse.cs`, `Models/TransactionDetails.cs`, `Models/TransactionInformation.cs`, `Api/TransactionSearch.cs` |

### 2.3 Shared models

| Model | Fields used (`C# (wire): type, required?`) | Source |
| --- | --- | --- |
| `PurchaseUnitRequest` | `Amount (amount): AmountWithBreakdown` **required** · `ReferenceId (reference_id): string?` · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `Description (description): string?` | `Models/PurchaseUnitRequest.cs` |
| `AmountWithBreakdown` | `CurrencyCode (currency_code): string` **required** · `Value (value): string` **required** · `Breakdown (breakdown): AmountBreakdown?` | `Models/AmountWithBreakdown.cs` |
| `Money` | `CurrencyCode (currency_code): string` **required** · `Value (value): string` **required** | `Models/Money.cs` |
| `CardRequest` (order payment source) | `Name (name): string?` · `Number (number): string?` · `Expiry (expiry): string?` · `SecurityCode (security_code): string?` · `BillingAddress (billing_address): Address?` · `VaultId (vault_id): string?` · `Attributes (attributes): CardAttributes?` — **all optional**, so `required?` selects nothing: for a raw card send `Number` + `Expiry`; to pay with a saved card send **only** `VaultId`. | `Models/CardRequest.cs` |
| `Address` | `CountryCode (country_code): string` **required** · `AddressLine1 (address_line_1)` · `AddressLine2` · `AdminArea1 (admin_area_1)` (state) · `AdminArea2 (admin_area_2)` (city) · `PostalCode (postal_code)` — all `string?` | `Models/Address.cs` |
| `PurchaseUnit` (response) | `Payments (payments): PaymentCollection?` · `Amount (amount): AmountWithBreakdown?` · `CustomId`, `InvoiceId`, `ReferenceId` | `Models/PurchaseUnit.cs` |
| `PaymentCollection` | `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` · `Captures (captures): IReadOnlyList<OrdersCapture>?` · `Refunds (refunds): IReadOnlyList<Refund>?` | `Models/PaymentCollection.cs` |
| `AuthorizationWithAdditionalData` | `Id (id): string?` · `Status (status): AuthorizationStatus?` · `Amount (amount): Money?` · `ExpirationTime (expiration_time): string?` · `ProcessorResponse (processor_response): ProcessorResponse?` | `Models/AuthorizationWithAdditionalData.cs` |
| `SellerReceivableBreakdown` | `GrossAmount (gross_amount): Money` **required** · `PaypalFee (paypal_fee): Money?` · `NetAmount (net_amount): Money?` · `ReceivableAmount`, `ExchangeRate`, `PlatformFees` | `Models/SellerReceivableBreakdown.cs` |
| `CardAttributes` | `Vault (vault): VaultInstructionBase?` → `StoreInVault (store_in_vault): StoreInVaultInstruction?` · `Customer (customer): CardCustomerInformation?` | `Models/CardAttributes.cs`, `Models/VaultInstructionBase.cs` |
| `Error` (typed-error payload) | `Name (name): string` **required** · `Message (message): string` **required** · `DebugId (debug_id): string` **required** · `Details (details): IReadOnlyList<ErrorDetails>?` | `Models/Error.cs` |

### 2.4 Enums needed (`StringEnum<T>` — not C# enums; build with the static member or `T.FromValue("WIRE")`, read with `.Value`)

| Enum | Members (C# → wire) | Source |
| --- | --- | --- |
| `CheckoutPaymentIntent` | `Capture` → `CAPTURE` · `Authorize` → `AUTHORIZE` | `Models/Enums/CheckoutPaymentIntent.cs` |
| `OrderStatus` | `Created`→`CREATED` · `Saved`→`SAVED` · `Approved`→`APPROVED` · `Voided`→`VOIDED` · `Completed`→`COMPLETED` · `PayerActionRequired`→`PAYER_ACTION_REQUIRED` | `Models/Enums/OrderStatus.cs` |
| `AuthorizationStatus` | `Created`→`CREATED` · `Captured`→`CAPTURED` · `Denied`→`DENIED` · `PartiallyCaptured`→`PARTIALLY_CAPTURED` · `Voided`→`VOIDED` · `Pending`→`PENDING`. **No `EXPIRED` member exists.** | `Models/Enums/AuthorizationStatus.cs` |
| `CaptureStatus` | `Completed`→`COMPLETED` · `Declined`→`DECLINED` · `PartiallyRefunded`→`PARTIALLY_REFUNDED` · `Pending`→`PENDING` · `Refunded`→`REFUNDED` · `Failed`→`FAILED` | `Models/Enums/CaptureStatus.cs` |
| `RefundStatus` | `Cancelled`→`CANCELLED` · `Failed`→`FAILED` · `Pending`→`PENDING` · `Completed`→`COMPLETED` | `Models/Enums/RefundStatus.cs` |
| `VaultTokenRequestType` | `SetupToken` → `SETUP_TOKEN` (only member) | `Models/Enums/VaultTokenRequestType.cs` |
| `TokenType` | `BillingAgreement` → `BILLING_AGREEMENT` (**only** member) — so `payment_source.token` is *not* the way to pay with a vaulted card; `card.vault_id` is. | `Models/Enums/TokenType.cs` |
| `PaymentTokenStatus` | `Created` · `PayerActionRequired` · `Approved` · `Vaulted` · `Tokenized` | `Models/Enums/PaymentTokenStatus.cs` |
| `StoreInVaultInstruction` | `OnSuccess` → `ON_SUCCESS` (only member) | `Models/Enums/StoreInVaultInstruction.cs` |

### 2.5 Endpoint semantics that decide what we must pass (from the operations' XML `<remarks>`/`<param>`)

| Fact | Consequence | Source |
| --- | --- | --- |
| `AuthorizeOrder`/`CaptureOrder`: "the buyer must first approve the order **or a valid `payment_source` must be provided in the request**". | Direct-card flow: create the order without a payment source, then send the card on `AuthorizeOrder` — no browser approval round-trip. | `Api/Orders.cs` `<remarks>` |
| `prefer`: `return=minimal` returns only id/status/links; `return=representation` returns the complete resource. | Every call whose result we persist (authorization id, capture fee/net breakdown, refund id, void status) passes `prefer: "return=representation"`. | `Api/Orders.cs`, `Api/Payments.cs` `<param name="prefer">` |
| `payPalRequestId` **is** a real caller-supplied idempotency key. Retention: **Orders 6 h** ("mandatory for all single-step create order calls" that carry payment-source information), **Payments 45 days**, **Vault 3 h**. | Every write passes a deterministic, operation-scoped key. See PRODUCTION READINESS §5. | `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs` `<param name="payPalRequestId">` |
| `ReauthorizePayment`: reauthorize after the 3-day honor period expires; possible from day 4 to day 29; after 30 days you must create a **new** authorized payment; supports only the `amount` parameter. | Fulfilment renews a stale hold; past 29 days it cannot, and the operator is told to have the shopper pay again. | `Api/Payments.cs` `<remarks>` |
| `RefundCapturedPayment`: "For a full refund, include an empty payload… For a partial refund, include an `amount` object". | Full refund passes `body: null`; partial passes `new RefundRequest { Amount = … }`. | `Api/Payments.cs` `<remarks>` |
| `VoidPayment`: "You cannot void an authorized payment that has been fully captured." | Cancel is rejected locally once the order is fulfilled. | `Api/Payments.cs` `<remarks>` |
| `SearchTransactions`: `end_date` — "maximum supported range is 31 days"; "It takes a maximum of three hours for executed transactions to appear"; `page`/`page_size` paginate. | Reconciliation splits any requested range into ≤31-day windows and pages each to `total_pages`. Recent activity legitimately absent. | `Api/TransactionSearch.cs` `<param>`/`<remarks>` |
| `CreatePaymentToken`: "Creates a Payment Token from the given payment source and adds it to the Vault of the associated customer." | One call vaults a raw card (`payment_source.card`). | `Api/Vault.cs` `<remarks>` |

---

## 3. Trap notes

Each names a hazard and hands over the skill; none is resolved here.

| # | Step | Hazard | Pointer |
| --- | --- | --- | --- |
| T1 | Step 3 — client & DI | Who owns the `HttpClient`/handler pipeline and what lifetime the client may have; getting it wrong costs socket exhaustion or stale DNS, silently. | **`MUST load paypal-api:dotnet-client-initialization`** |
| T2 | Step 2/3 — credentials | When credentials must be set relative to client construction, and where they may be read from; a wrong order yields 401s that look like bad secrets. | **`MUST load paypal-api:dotnet-authentication`** |
| T3 | Steps 7–12 — every call | Optional parameters with no C# default mis-bind in a positional call, and the return envelope may not be the payload; a wrong read compiles and returns null. | **`MUST load paypal-api:dotnet-calling-endpoints`** |
| T4 | Steps 7–12 — payload building | `StringEnum<T>` is not a C# enum, unions are not built with `new`, and unknown response fields do not simply vanish; each has a specific construction/read idiom. | **`MUST load paypal-api:dotnet-models`** |
| T5 | All SDK calls — error boundary | Which exception types actually reach a `catch`, how to read the status and error body safely, and why an SDK-exception-only ladder is silently wrong. | **`MUST load paypal-api:dotnet-error-handling`** |
| T6 | Step 3 — resilience & logging | What `Timeout` actually bounds, which HTTP methods the SDK may resend, how list pagination is meant to be driven, and what the built-in logger does with a JSON request body — this integration posts card numbers. | **`MUST load paypal-api:dotnet-configuration-resilience`** |
| T7 | Step 14 — tests | Which seam to fake so tests assert behaviour instead of execution and stay independent of SDK internals. | **`MUST load paypal-api:dotnet-testing`** |

---

## 4. REQUIRED READING

Load **all** of these **before implementation starts**. They are the `dotnet-*` skills shipped by the
**`paypal-api`** plugin (every APIMatic .NET plugin ships these same names — load the `paypal-api:`
copies, not another plugin's). This sheet deliberately does **not** carry their contents.

| Skill (plugin-qualified) | Step it governs |
| --- | --- |
| `paypal-api:dotnet-client-initialization` | Step 3 — constructing and DI-registering `PayPalClient` |
| `paypal-api:dotnet-authentication` | Steps 2–3 — OAuth2 client-credentials wiring |
| `paypal-api:dotnet-calling-endpoints` | Steps 7–12 — every operation call |
| `paypal-api:dotnet-models` | Steps 7–12 — request payloads and response mapping |
| `paypal-api:dotnet-error-handling` | All SDK calls — the error boundary |
| `paypal-api:dotnet-configuration-resilience` | Step 3 — retries, timeouts, base URL, pagination, logging |
| `paypal-api:dotnet-testing` | Step 14 — tests around the gateway |

**Two hazard rows that always apply** — `System.Text.Json.JsonException` reaches the boundary from two
directions and they need opposite handling:

1. A drifted or malformed **2xx** body (e.g. a missing `required` member) surfaces as a `JsonException`
   thrown by *deserialization*, **not** as an `SdkException` — so a catch ladder that only catches
   `SdkException<…>` lets it escape.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
   `JsonException` *while the error object is being constructed*, so it **replaces** the `SdkException`
   and the HTTP status is destroyed with it.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | **Credential fail-fast** | Bound in `src/Infrastructure/PayPal/PayPalSettings.cs` from configuration section **`PayPal`** via `AddOptions<PayPalSettings>().Bind(config.GetSection("PayPal")).ValidateOnStart()`, with an `IValidateOptions<PayPalSettings>` that rejects **each** part independently when missing *or* whitespace: `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Currency` (must be 3 letters), `PayPal:Environment`. `PayPal:BaseUrl`, when present, must parse as an absolute http(s) URI. The host therefore refuses to start rather than discovering a blank secret as a 401 on the first payment. |
| 2 | **Secret sourcing & rotation** | Values come from .NET user-secrets (loaded from the `PAYPAL_*` environment variables by the operator; never written into any repo file). We do **not** use `services.AddPayPalClient(...)`: it builds the options object once at registration and captures it in the singleton, so a rotated secret would not take effect until process restart *and* the C# 14 `extension` block is an unnecessary consumption risk. Instead `PayPalClient` is registered as a singleton built from `IOptionsMonitor<PayPalSettings>.CurrentValue` at construction — still process-lifetime, so **rotation requires a restart, which is accepted here**; a restart is the deployment's rotation step. (Rotation without restart would need a custom `Oauth2TokenStrategy`; out of scope and recorded as such.) |
| 3 | **Total timeout budget** | `RetryOptions.Timeout` is per attempt, so a retried call can cost a multiple of it. The caller-visible budget is enforced by the application: every gateway method takes a `CancellationToken` and `PayPalPaymentGateway` links it with a **30-second** deadline (`CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter`) passed as `ct:` to every operation. Per-attempt `Timeout` is set to 10 s and `MaxRetries` to 2, keeping the worst case inside the 30 s wall. |
| 4 | **Write-retry ownership** | The SDK's default `HttpMethodsToRetry` is `GET, HEAD, PUT, OPTIONS`, so **none** of this scope's writes (all `POST`/`DELETE`) is ever resent by the SDK — which is what we want for authorize/capture/refund. We keep the default list unchanged and do not add `POST`. Reads (`GetAuthorizedPayment`, `SearchTransactions`) are `GET` and therefore retried. |
| 5 | **Idempotency & ambiguous writes** | Every write passes a **real** caller-supplied key in `payPalRequestId` (the generator-injected `Idempotency-Key: Guid.NewGuid()` header is *not* a key and is not relied on). **PayPal enforces that header's uniqueness per merchant account, not per resource** — and retains Payments keys for 45 days — so keys are seeded with the payment's own unique invoice reference `eshop-{orderId}-{utcTimestamp}` rather than with an id that restarts (with the in-memory provider, order ids restart at 1 every run) or with a caller string on its own. Verified the hard way: a first-ever refund keyed `refund-a` was rejected with *"The value of PayPal-Request-Id header has already been used"*. Keys are therefore `{invoiceId}-create-{attempt}`, `{invoiceId}-auth-{attempt}`, `{invoiceId}-cap-{attempt}`, `{invoiceId}-void`, `{invoiceId}-reauth-{attempt}`, and for refunds `{invoiceId}-rf-{callerKey}` — the caller's key scoped to the payment, so replaying it deduplicates while two shoppers using the same obvious key on different orders do not collide; an over-long key is folded into a stable SHA-256 digest rather than truncated. Vaulting uses a fresh per-request key. Local guards close the rest: a unique `(PaymentId, IdempotencyKey)` index on `PaymentRefund` returns the stored refund on a repeat, and an in-process per-order mutex plus a state re-check after acquiring it makes a double-click a no-op. Because Orders keys are retained only 6 h and Vault keys 3 h, the durable guard is always the local state machine, never PayPal's window alone. Reconciliation (`GET /api/reconciliation`) is the standing path for any write whose outcome was ambiguous, and a payment whose outcome could not be established is frozen (`AwaitingReconciliation`) rather than retried blind. |
| 6 | **Observability** | `Information`: order placed, authorized, captured, voided, refunded — with our order/payment ids, the PayPal order/authorization/capture/refund ids, amount and currency. `Warning`: stale authorization re-authorized; refund rejected for exceeding the captured amount. `Error`: any `SdkException`, logged with PayPal's `Error.DebugId` (the provider's correlation id) plus `Error.Name`/`Message` and, for Case B, `RawError.StatusCode` — `DebugId` is what PayPal support correlates on, so it always reaches our logs. **`LogRequestBody` is never enabled** (see row 7). No card field is ever logged, at any level. |
| 7 | **Sensitive data** | Yes — `CardRequest.Number`/`SecurityCode` and `PaymentTokenRequestCard.Number`/`SecurityCode` carry a PAN and CVV (`Models/CardRequest.cs`, `Models/PaymentTokenRequestCard.cs`). Therefore: `options.Logging.LogRequestBody` stays **off**, and `options.Logging.LoggerFactory` is **assigned explicitly** so the `PAYPALSERVERSDKCLIENT_LOG` environment variable cannot switch body logging on from outside the code. Card fields exist only as method parameters — never persisted (the app's DB stores only PayPal's vault id, brand, last 4 and expiry), never put in an exception message, never echoed in an API response. Request DTOs carrying card data are excluded from any diagnostic serialization. |
| 8 | **Environment selection** | The map declares **one** server group (`Default`) with **one** environment member, `ServerEnvironment.Production` (wire `production`), whose base URL is the **sandbox** host `https://api-m.sandbox.paypal.com`; `ServerEnvironment.Match` throws for any other value. So: `options.Environment` is always `ServerEnvironment.Production`, and the deployment is selected by base URL, not by the enum. `PayPal:Environment` = `sandbox` (any case) → leave the SDK's default base URL, i.e. the sandbox host. `PayPal:BaseUrl`, when set, overwrites `options.Server.Default.Production.BaseUrl` and is used verbatim for **every** call **including the OAuth token request** (`AuthSchemes.cs` resolves `/v1/oauth2/token` through the same `server.Default(...)`). Because the SDK declares **no** live base URL, `PayPal:Environment` = `live`/`production` **without** `PayPal:BaseUrl` fails validation at startup with an explicit message — test traffic can never silently reach a live system, and live traffic can never silently land on the sandbox. This machine's deployment sets `PayPal:Environment` = `sandbox` and leaves `PayPal:BaseUrl` unset. |

---

## 6. Assumptions & Blockers

**Blockers:** none. Every capability this integration needs is on the map, and all of it was
exercised against the sandbox — a real authorization, a real capture with PayPal's fee and net, real
partial refunds, a real void, a card vaulted and reused to pay a second order, and a reconciliation
report over a 45-day range. No PayPal call returned a browser-approval challenge.

**Assumptions (minor — proceeding):**

- **A1 — Currency decimals.** Amounts are formatted with 2 decimal places (`0.00`, invariant culture)
  because the configured currency here is a 2-decimal currency. The map documents `Money.Value` as a
  string whose precision depends on the currency but does not enumerate per-currency precision, so a
  zero- or three-decimal currency would need a table this SDK does not carry. Guarded, not assumed
  blindly: after authorizing we compare PayPal's returned authorization amount with the order total and
  fail the payment if they differ by a cent.
- **A2 — Vaulting a raw card in one call. CONFIRMED against the sandbox.**
  `Vault.CreatePaymentToken` with `payment_source.card` carrying a raw number
  (`Models/PaymentTokenRequestCard.cs`) vaults the card in one call and returns a token usable as
  `card.vault_id`; the `CreateSetupToken` fallback was not needed and is not implemented.
- **A3 — Expired authorizations.** Still `UNVERIFIED` — a sandbox hold lasts 29 days, so this
  session could not observe one expiring; the defensive handling below is what ships, and its
  branches are covered by unit tests rather than by live traffic.
  `AuthorizationStatus` declares no `EXPIRED` member
  (`Models/Enums/AuthorizationStatus.cs`), yet `ReauthorizePayment`'s own prose describes an
  authorization expiring after its 3-day honor period. `UNVERIFIED` — only live traffic settles what
  the wire actually sends. Defensive directive: never compare authorization status by object identity
  against a static member for the stale case; read `.Value` as a string, treat any value outside the
  declared set as "not capturable", and drive the decision primarily from the stored
  `expiration_time` plus a capture failure, falling back to `ReauthorizePayment` and surfacing an
  operator-actionable 409 when reauthorization itself fails.
- **A4 — Caller identity.** The JWT issued by `POST /api/authenticate` carries `ClaimTypes.Name`
  (the username/email) and role claims (`src/Infrastructure/Identity/IdentityTokenClaimService.cs`),
  and the existing `Order.BuyerId` is that username. New shopper-scoped rows key off the same value.
- **A5 — Order lifecycle on the storefront.** Adding a lifecycle state to `Order` means orders created
  by the existing Web checkout start as "awaiting payment". That is additive and accurate — the
  storefront never took money — and no existing behaviour changes.

---

## 7. Repo conventions to imitate (exemplar per pattern — read the file at edit time)

| Pattern | Exemplar |
| --- | --- |
| PublicApi endpoint (route + auth + Swagger tags) | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` |
| Request/response DTO pair with correlation id | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.CreateCatalogItemRequest.cs`, `…Response.cs`, `src/PublicApi/BaseResponse.cs` |
| Admin-only authorization attribute | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` (`BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS`) |
| Aggregate root + owned value object | `src/ApplicationCore/Entities/OrderAggregate/Order.cs` |
| EF mapping for an aggregate | `src/Infrastructure/Data/Config/OrderConfiguration.cs` |
| Specification for scoped queries | `src/ApplicationCore/Specifications/CustomerOrdersWithItemsSpecification.cs` |
| Application service over repositories | `src/ApplicationCore/Services/OrderService.cs` |
| Infrastructure DI composition | `src/Infrastructure/Dependencies.cs`, `src/PublicApi/Program.cs` |
| Unit test style (xunit + NSubstitute) | `tests/UnitTests/ApplicationCore/Services/` |
