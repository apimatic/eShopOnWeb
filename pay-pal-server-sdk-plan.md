# PayPal Server SDK (.NET) — integration plan for eShopOnWeb

Adds PayPal card payments + saved cards to eShopOnWeb as an **additive** capability on `src/PublicApi`.
Every PayPal contract fact below comes from the SDK map / map-named source files (SDK spec `2.29`), not memory.

The SDK is **not on NuGet**. It is **vendored** into the repo at `src/PayPalServerSdk/` (generated source copied
from the pinned `main` clone) and referenced by `src/Infrastructure` via `ProjectReference`. The vendored project
sets `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` (repo root turns it on) and keeps its
own generated `PackageReference` versions. Root namespace `PayPalServerSdk`, target `netstandard2.0`, `LangVersion 14`
(builds only under the .NET 10 SDK present on this box).

---

## 1. Scope & sequence

Layering: endpoints in `src/PublicApi` (MinimalApi.Endpoint `IEndpoint<>` style) → depend on `ApplicationCore`
interfaces → implemented in `src/Infrastructure` which owns the SDK client. New persisted aggregates live in
`CatalogContext` (so `EfRepository<T>` works) with EF configs auto-discovered from `src/Infrastructure/Data/Config/`.

1. **Vendor SDK + wire client/DI/config** — copy SDK source to `src/PayPalServerSdk/`, add to solution, reference from
   Infrastructure. Bind `PayPalOptions` from `PayPal:` section; fail-fast validation; register `PayPalServerSdkClient`
   via `AddPayPalServerSdkClient` (uses `IHttpClientFactory`). Base-URL override → `options.Server.Default.Sandbox.BaseUrl`.
2. **Domain** — `Payment` aggregate (1:1 with eShop `Order`, tracks money state + PayPal ids) and `PaymentMethod`
   aggregate (saved card: vault token id + safe display + owner). Both hold `BuyerId` for ownership scoping.
   eShop `Order`/`OrderItem` reused verbatim for `POST /api/orders` (built from catalog prices; `Order.cs` untouched).
3. **Payment processor service** (Infrastructure) — authorize / capture / reauthorize / void / refund, mapping SDK
   models ↔ domain. **Vault service** — save / get / delete card. **Reconciliation service** — paged transaction search.
4. **Endpoints** (`/api/…`) — orders, pay, fulfil, cancel, refunds, my-orders, reconciliation, payment-methods (CRUD).
5. **Tests** — SDK seam faked at `HttpClient` (dotnet-testing); functional endpoint tests via `WebApplicationFactory<Program>`
   + `ApiTokenHelper` (MSTest, in-memory DB). Live sandbox verification via a run script (curl).

### Flow → PayPal operation map

| App action | PayPal call(s) |
| --- | --- |
| `POST /api/orders` | none (eShop `Order` + `Payment{AwaitingPayment}`) |
| `POST /api/orders/{id}/pay` | `Orders.CreateOrder` (intent AUTHORIZE, amount only) → `Orders.AuthorizeOrder` (payment_source.card = raw card **or** `vault_id`) |
| `POST /api/orders/{id}/fulfil` | (renew if stale) `Payments.GetAuthorizedPayment` → `Payments.ReauthorizePayment` → `Payments.CaptureAuthorizedPayment` (final_capture) |
| `POST /api/orders/{id}/cancel` | `Payments.VoidPayment` |
| `POST /api/orders/{id}/refunds` | `Payments.RefundCapturedPayment` (full or partial `amount`) |
| `GET /api/my-orders` | none (reads domain) |
| `GET /api/reconciliation` | `TransactionSearch.SearchTransactions` (loop `page`=1..`total_pages`) |
| `POST /api/payment-methods` | `Vault.CreatePaymentToken` (payment_source.card + customer) |
| `GET /api/payment-methods` | reads domain (owner-scoped); vault id is authoritative for pay |
| `DELETE /api/payment-methods/{id}` | `Vault.DeletePaymentToken` + remove domain row |

---

## 2. CONTRACT SHEET

⚠ **Signatures are generated code, verbatim.** Every parameter name is the literal C# identifier; the cancellation
token is `ct` (named arg `ct:`). ⚠ **Every SDK type is written fully-qualified with the namespace its source path
implies** (`Models/` → `PayPalServerSdk.Models`; `Models/Enums/` → `PayPalServerSdk.Models.Enums`; `Errors/` →
`PayPalServerSdk.Errors`; client/options root → `PayPalServerSdk`; `ServerEnvironment` → `PayPalServerSdk.Servers`;
`SdkException<T>` → `PayPalServerSdk.Core.Exceptions`; `RawError` → `PayPalServerSdk.Core.ErrorResponse`).

### Operations (accessor · signature · request fields used · response fields read · error case · source)

**Orders** (`client.Orders`, `Api/Orders.cs`; map `map/operations/Orders.md`)

- `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Order`.
  - body `OrderRequest`: `Intent (intent): CheckoutPaymentIntent` **required** = `Authorize`; `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest>` **required**. (`Models/OrderRequest.cs`)
  - `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown` **required**; `InvoiceId (invoice_id): string?` = `ESHOP-{orderId}`; `CustomId (custom_id): string?` = `{orderId}`; `Description`. (`Models/PurchaseUnitRequest.cs`)
  - `AmountWithBreakdown`: `CurrencyCode (currency_code): string` **required**; `Value (value): string` **required** (regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`, so `"12.34"`). (`Models/AmountWithBreakdown.cs`)
  - read: `Order.Id (id)`, `Order.Status (status): OrderStatus?`. (`Models/Order.cs`)
  - **Case A** `SdkException<CreateOrderError>` — `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)`. (`Errors/CreateOrderError.cs`, `Models/Error.cs`)
- `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `OrderAuthorizeResponse`.
  - body `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`. (`Models/OrderAuthorizeRequest.cs`)
  - `OrderAuthorizeRequestPaymentSource`: `Card (card): CardRequest?`. (`Models/OrderAuthorizeRequestPaymentSource.cs`)
  - `CardRequest` one-off: `Name`, `Number (number)` (13–19 digits), `Expiry (expiry)` `YYYY-MM`, `SecurityCode (security_code)`, `BillingAddress (billing_address): Address?`. saved card: `VaultId (vault_id): string?` (set instead of raw fields). (`Models/CardRequest.cs`)
  - read: `OrderAuthorizeResponse.Status (status): OrderStatus?`; `PurchaseUnits[0].Payments.Authorizations[0].{Id, Status, Amount}`; `.Links`. (`Models/OrderAuthorizeResponse.cs` → `Models/PurchaseUnit.cs` → `Models/PaymentCollection.cs` → `Models/AuthorizationWithAdditionalData.cs`)
  - **Case A** `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError`. (`Errors/AuthorizeOrderError.cs`)
  - **Behavioural (`Api/Orders.cs` `<remarks>`):** "To successfully authorize … the buyer must first approve the order OR a valid payment_source must be provided in the request." → supplying `payment_source.card` at authorize approves + authorizes in one call. `OrderStatus.PayerActionRequired` = a browser-approval challenge → **STOP/report**, do not build approval round-trip (task mandate).

**Payments** (`client.Payments`, `Api/Payments.cs`; map `map/operations/Payments.md`)

- `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `CapturedPayment`.
  - body `CaptureRequest?`: `Amount (amount): Money?`, `InvoiceId`, `FinalCapture (final_capture): bool?` = true. (`Models/CaptureRequest.cs`)
  - read: `CapturedPayment.{Id, Status: CaptureStatus?, Amount: Money?}`, `SellerReceivableBreakdown.{GrossAmount (required), PaypalFee, NetAmount}`. (`Models/CapturedPayment.cs`, `Models/SellerReceivableBreakdown.cs`, `Models/Money.cs`)
  - **Case A** `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. (`Errors/CaptureAuthorizedPaymentError.cs`)
- `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization` (`.Status: AuthorizationStatus?`, `.ExpirationTime: string?`). (`Models/PaymentAuthorization.cs`) — Case A, `TryGetError` [401,403,404]·`TryGetNoContent`[500]·`TryGetRawError`.
- `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization` (new `.Id`). body `ReauthorizeRequest?`: `Amount: Money?`. (`Models/ReauthorizeRequest.cs`) — Case A [400,401,403,404,422]·[500]·raw.
- `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization`. — Case A [401,403,404,409,422]·[500]·raw.
- `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Refund`.
  - body `RefundRequest?`: `Amount (amount): Money?` (omit ⇒ full refund; set ⇒ partial), `NoteToPayer`, `InvoiceId`. (`Models/RefundRequest.cs`)
  - read: `Refund.{Id, Status: RefundStatus?, Amount: Money?}`. (`Models/Refund.cs`)
  - **Case A** `SdkException<RefundCapturedPaymentError>` — `TryGetError` [400,401,403,404,409,422]·`TryGetNoContent`[500]·`TryGetRawError`. (`Errors/RefundCapturedPaymentError.cs`)

**Vault** (`client.Vault`, `Api/Vault.cs`; map `map/operations/Vault.md`)

- `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentTokenResponse`.
  - body `PaymentTokenRequest`: `Customer (customer): Customer?` (set `MerchantCustomerId (merchant_customer_id)` = stable per-shopper id); `PaymentSource (payment_source): PaymentTokenRequestPaymentSource` **required**. (`Models/PaymentTokenRequest.cs`)
  - `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?`. (`Models/PaymentTokenRequestPaymentSource.cs`)
  - `PaymentTokenRequestCard`: `Name`, `Number (number)`, `Expiry (expiry)`, `SecurityCode (security_code)`, `BillingAddress`. (`Models/PaymentTokenRequestCard.cs`)
  - read: `PaymentTokenResponse.{Id (vault token id), Customer: CustomerResponse?}`, `PaymentSource.Card: CardPaymentTokenEntity` → `.{LastDigits (last_digits), Brand: CardBrand?, Expiry}` (safe display, never PAN). (`Models/PaymentTokenResponse.cs`, `Models/PaymentTokenResponsePaymentSource.cs`, `Models/CardPaymentTokenEntity.cs`)
  - **Case A** `SdkException<CreatePaymentTokenError>` — `TryGetError(out Error)` [400,403,404,422,500]·`TryGetRawError`. (`Errors/CreatePaymentTokenError.cs`)
- `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `void`. — Case A `TryGetError`[400,403,500]·`TryGetRawError`. (`Errors/DeletePaymentTokenError.cs`)
- (available if needed) `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, …)` → `CustomerVaultPaymentTokensResponse`. Domain store is authoritative for the list; vault list is a reconciliation aid only.

**TransactionSearch** (`client.TransactionSearch`, `Api/TransactionSearch.cs`; map `map/operations/TransactionSearch.md`)

- `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `SearchResponse`.
  - **call with named args** (8 nullable no-default params `transactionId`…`terminalId` must be passed — pass `null`). `startDate`/`endDate` are ISO-8601 date-time strings; `fields:"transaction_info"`.
  - read: `SearchResponse.{TransactionDetails: IReadOnlyList<TransactionDetails>?, Page, TotalPages}`; each `TransactionDetails.TransactionInfo: TransactionInformation?` → `.{TransactionId, InvoiceId (invoice_id), TransactionAmount: Money?, TransactionStatus, TransactionInitiationDate}`. Match eShop by `invoice_id == ESHOP-{orderId}`. (`Models/SearchResponse.cs`, `Models/TransactionDetails.cs`, `Models/TransactionInformation.cs`)
  - **Pagination:** map shows none, but the response carries `Page`/`TotalPages` → loop `page = 1..TotalPages` to cover the whole range (task requirement).
  - **Case B** `SdkException<RawError>` (the only Case-B op) — `RawError.{StatusCode, ReadAsString(), ReadAsJson<T>()}`. **No typed accessors.**

### Enums (source `Models/Enums/<Type>.cs`)

- `CheckoutPaymentIntent`: `Authorize` (`"AUTHORIZE"`), `Capture` (`"CAPTURE"`). Use `.Authorize`.
- `OrderStatus`: `Created`, `Saved`, `Approved`, `Voided`, `Completed`, `PayerActionRequired` (`"PAYER_ACTION_REQUIRED"` ⇒ challenge/stop).
- `AuthorizationStatus`: `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending`.
- `CaptureStatus`: `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed`.
- `RefundStatus`, `CardBrand` (display only). Enums are `StringEnum<T>` — compare via static members or `.Value`; build with `Type.FromValue("WIRE")`.

### Client / auth / server (source: `sdk-map.md`, `PayPalServerSdkClientOptions.cs`, `ServiceCollectionExtensions.cs`, `AuthSchemes.cs`, `Servers/DefaultOptions.cs`)

- Construct: `services.AddPayPalServerSdkClient(o => { o.Oauth2 = new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials { ClientId=…, ClientSecret=… }; o.Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox; /* if PayPal:BaseUrl set */ o.Server.Default.Sandbox.BaseUrl = baseUrl; })`. Registers a **singleton** client over `IHttpClientFactory`.
- Auth: OAuth2 client-credentials; token fetched via `server.Default("/v1/oauth2/token")` → **honours `Sandbox.BaseUrl`**, so `PayPal:BaseUrl` redirects the token request too (requirement satisfied). `ServerEnvironment` declares **only `Sandbox`** — any other host is a `BaseUrl` override, not an environment member.

---

## 3. Trap notes (name the hazard + the skill; do NOT resolve inline)

- **DI registration captures the options object once at registration → a rotated secret needs a process restart; HttpClient must be long-lived via factory, client lifetime.** `MUST load dotnet-client-initialization`.
- **Credentials must be set before/at client construction and sourced from config not literals.** `MUST load dotnet-authentication`.
- **List/search ops (`SearchTransactions`, `ListCustomerPaymentTokens`) have optional params with no C# default → a positional call mis-binds; whether a write takes a real idempotency key vs the injected `Idempotency-Key` header.** `MUST load dotnet-calling-endpoints`.
- **`StringEnum<T>` are not C# enums; unions via `TryGet…`; response models carry an extension-data bag only where declared; `required` init members.** `MUST load dotnet-models`.
- **Case A vs Case B per operation; `System.Text.Json.JsonException` reaches the boundary from a drifted 2xx (missing `required`) AND from a non-2xx body that fails to match `{Operation}Error` (destroying the status); `TryGetRawError` fallback; `TryGetNoContent` on the Payments ops.** `MUST load dotnet-error-handling`.
- **`Timeout` is per-attempt not total; `HttpMethodsToRetry` default excludes POST/PATCH/DELETE (so PayPal writes are not auto-resent) but includes PUT/GET; `LogRequestBody` logs JSON unredacted; `PAYPALSERVERSDKCLIENT_LOG` can force body logging unless `LoggerFactory` is set.** `MUST load dotnet-configuration-resilience`.
- **The `HttpClient` ctor arg is the test seam; match the project's existing test framework/assertions.** `MUST load dotnet-testing`.

---

## 4. REQUIRED READING (load all before implementation; sheet does not carry their contents)

Load the **paypal-platforms-team** copies (plugin-qualified):

- `paypal-platforms-team:dotnet-client-initialization` — SDK client construction + DI singleton over IHttpClientFactory (step 1).
- `paypal-platforms-team:dotnet-authentication` — OAuth2 client-credentials wiring, secret sourcing (step 1).
- `paypal-platforms-team:dotnet-calling-endpoints` — named-arg calls, must-pass-null params, real idempotency key (steps pay/fulfil/refund/reconcile/vault).
- `paypal-platforms-team:dotnet-models` — StringEnum, required init members, unions, extension bag (all model mapping).
- `paypal-platforms-team:dotnet-error-handling` — Case A/B, TryGet ladders, JsonException from 2xx and non-2xx (error boundary — always required).
- `paypal-platforms-team:dotnet-configuration-resilience` — retries/timeout budget, base-URL selection, logging redaction (step 1 + refund idempotency reconciliation).
- `paypal-platforms-team:dotnet-testing` — HttpClient seam for faking the SDK (tests).

Mandatory `JsonException` hazards (verbatim): a drifted/malformed **2xx** body (missing `required` member) surfaces as
`System.Text.Json.JsonException` from deserialization, **not** `SdkException` — an SDK-exception-only catch ladder lets
it escape. A **non-2xx** body that does not match its `{Operation}Error` shape throws `JsonException` **while the error
object is constructed**, replacing the `SdkException` and destroying the HTTP status.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` bound from `PayPal:`; a startup validator (`IValidateOptions`/explicit check in `Program.cs`) throws if `ClientId`, `ClientSecret`, `Environment`, or `Currency` is missing **or blank** (each part checked separately — a blank part ≠ missing). Host refuses to start rather than 401 on first call. |
| 2 | Secret sourcing & rotation | Secrets in **.NET user-secrets** (loaded from env vars `PAYPAL_*` by me, values never in repo). `AddPayPalServerSdkClient` builds options **once at registration** into the singleton → a rotated secret takes effect only on process restart. Documented as such; no hot-reload required for this task. |
| 3 | Total timeout budget | SDK `Timeout` is **per-attempt**. Each service method accepts/So passes a `CancellationToken`; endpoints flow `HttpContext.RequestAborted`. A whole-call budget is bounded by that token (a `CancellationTokenSource` deadline in the service), not by `Timeout`. Retries kept conservative (see #4). |
| 4 | Write-retry ownership | All PayPal money writes are **POST** → SDK default `HttpMethodsToRetry` (GET/HEAD/PUT/OPTIONS) never resends them; safe. We do not add POST to the retry set. Idempotency (#5) is what makes a manual retry safe. |
| 5 | Idempotency & ambiguous writes | `CreateOrder`/`AuthorizeOrder`/`Capture`/`Void`/`Reauthorize`/`RefundCapturedPayment`/`CreatePaymentToken` take a **real** `payPalRequestId` (→ `PayPal-Request-Id`). Pay: stable key per (order, step) + domain state guard (authorize only from `AwaitingPayment`; PayPal order id persisted so retry skips re-create). Capture: guarded by state + stable key. **Refund: caller-supplied idempotency key** stored on `PaymentRefund`; a repeat key returns the stored `refundId` (no second refund) and is passed as `payPalRequestId`; two distinct keys ⇒ two legitimate partial refunds; enforce `refundedTotal + amount ≤ capturedAmount`. The injected `Idempotency-Key: Guid.NewGuid()` header is **not** used as a key. |
| 6 | Observability | Structured logs at Info (state transitions: authorized/captured/voided/refunded with order id + PayPal ids) / Warning (challenge, stale-auth reauth) / Error (SDK failures). PayPal `Error.DebugId` (correlation id) is logged on every failure. `LogRequestBody` stays **off**. |
| 7 | Sensitive data | Scope carries **PAN/CVV/expiry** (`CardRequest`, `PaymentTokenRequestCard`). Full card details are **never** persisted (domain stores only vault token id + last4/brand/expiry) and **never** logged: `LogRequestBody` off **and** `options.Logging.LoggerFactory` set explicitly so `PAYPALSERVERSDKCLIENT_LOG` cannot force body logging; our own code never echoes a request body or card field. |
| 8 | Environment selection | One server group `Default`; SDK declares only `ServerEnvironment.Sandbox` (`https://api-m.sandbox.paypal.com`). All dev/test targets **sandbox**. `PayPal:Environment` bound + validated (sandbox expected); `PayPal:BaseUrl` optional override applied verbatim to `options.Server.Default.Sandbox.BaseUrl` (governs API **and** token calls). No live host reachable without an explicit BaseUrl override, keeping test traffic off production. |

---

## 6. Assumptions & Blockers

**Blockers:** none — the map covers every capability the task needs (authorize, capture, reauthorize, void, refund,
vault create/delete, transaction search).

**Assumptions (minor, decided and proceeding):**
- eShop `Order.cs` left unmodified; payment/fulfilment state lives on a new `Payment` aggregate (1:1 with order). "Fulfilled" = captured.
- `POST /api/orders` builds the `Order` from catalog item ids+qty reading `CatalogItem.Price`; `BuyerId` = JWT `ClaimTypes.Name`; `ShipToAddress` from optional request fields else a placeholder (shipping is not the focus).
- Reconciliation matches on `invoice_id = ESHOP-{orderId}` set on the purchase unit (and echoed on capture). Empty sandbox range = expected, not a gap.
- `PayerActionRequired` / any `payer-action` challenge on the card ⇒ actionable error surfaced to the caller; no browser round-trip built (task mandate).
- Per-shopper PayPal customer id = a stable derived value (`MerchantCustomerId`) so saved cards group under the shopper.
