# PayPal integration plan — eShopOnWeb PublicApi

Additive card-payment + saved-card capability on `src/PublicApi`, using the **PayPal .NET SDK**
(root namespace `PayPal`, client `PayPalClient`) for every PayPal interaction. Sandbox only.

## 0. Architecture decision (YOUR CALL — not in the map)

- **Vendored SDK**: the PayPal SDK is not on NuGet, so its source is copied into `src/PaymentsSdk/PayPal/`
  (build reference, separate from the temp read-only clone) and referenced by `Infrastructure`.
  It uses `LangVersion 14` / `netstandard2.0`; builds under the installed .NET 10 SDK.
  Set `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` in the vendored csproj so its
  inline `PackageReference` versions don't clash with the repo's central package management.
- **Layering** (mirrors eShop): domain + interfaces in `ApplicationCore`; PayPal SDK usage + orchestration
  + persistence in `Infrastructure`; HTTP endpoints in `PublicApi`.
  - `ApplicationCore/Entities/PaymentAggregate/Payment.cs` + `PaymentRefund.cs` — payment/fulfilment state
    for an eShop `Order` (linked by `OrderId`); reuses the existing `Order`/`OrderItem` model, does not
    replace it.
  - `ApplicationCore/Entities/PaymentMethodAggregate/SavedPaymentMethod.cs` — a saved card (safe display
    only; NO PAN/CVV ever).
  - `ApplicationCore/Interfaces/IPayPalPaymentGateway.cs` — abstraction over PayPal calls (keeps the SDK out
    of ApplicationCore). Impl `Infrastructure/Payments/PayPalPaymentGateway.cs` (uses `PayPalClient`).
  - `ApplicationCore/Interfaces/IOrderPaymentService.cs`, `IPaymentMethodService.cs` — orchestration
    (persistence + gateway + idempotency). Impl in `Infrastructure/Payments/`. Caller's `buyerId`
    (= username, `ClaimTypes.Name`) is passed **as a parameter** from the endpoint — services stay web-free
    and testable.
  - Endpoints implement the base `MinimalApi.Endpoint.IEndpoint` (auto-registered by `AddEndpoints()` /
    `MapEndpoints()`), delegate injects `ClaimsPrincipal user` + services; `[Authorize(... JwtBearer)]` on
    the delegate. (VERIFY at runtime the base-interface implementers are mapped; eShop's `AddEndpoints`
    scans the base `IEndpoint`. If not mapped, fall back to `IEndpoint<IResult,TReq,TDep>` + an
    `IHttpContextAccessor`-based current-user read.)
- **Persistence**: new aggregates implement `IAggregateRoot`; `IEntityTypeConfiguration<>` added in
  `Infrastructure/Data/Config` (auto-applied by `ApplyConfigurationsFromAssembly`); consumed via
  `IRepository<>`/`EfRepository<>` + Ardalis `Specification<>`. In-memory mode needs no migration.
  Register `IRepository<>`/`IReadRepository<>` are already registered in Program.cs; register the new
  services there too (services are NOT auto-registered in PublicApi).
- **eShop `Order` creation in PublicApi**: `POST /api/orders` builds an `Order(buyerId, shipToAddress,
  items)` directly from catalog ids+quantities (prices from `CatalogItem.Price`) and a `Payment` row in
  `PendingPayment`. (Basket/`OrderService` are not registered in PublicApi and assume a basket; building the
  `Order` + `OrderItem`/`CatalogItemOrdered` directly is simpler and reuses the same entities.)

## 1. Scope & sequence

| Step | Endpoint(s) | PayPal ops |
| --- | --- | --- |
| S1 | client init, DI, config binding, fail-fast | (client construction, OAuth2) |
| S2 | `POST /api/orders`, `GET /api/my-orders` | — (persistence only) |
| S3 | `POST /api/payment-methods`, `GET`, `DELETE /{id}` | `Vault.CreatePaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.DeletePaymentToken` |
| S4 | `POST /api/orders/{orderId}/pay` (authorize) | `Orders.CreateOrder` (intent=AUTHORIZE); with an inline card/vault source PayPal authorizes at create time, so the authorization is already on the created order — `Orders.AuthorizeOrder` is called **only if** it is not (verified live) |
| S5 | `POST /api/orders/{orderId}/fulfil` (capture, admin) | `Payments.CaptureAuthorizedPayment`, +reauth: `Payments.GetAuthorizedPayment`/`Payments.ReauthorizePayment` |
| S6 | `POST /api/orders/{orderId}/cancel` (admin) | `Payments.VoidPayment` |
| S7 | `POST /api/orders/{orderId}/refunds` | `Payments.RefundCapturedPayment` |
| S8 | `GET /api/reconciliation` (admin) | `TransactionSearch.SearchTransactions` (all pages) |

Response id fields (top-level): `orderId` (POST /orders), `paymentMethodId` (POST /payment-methods),
`refundId` (POST /orders/{id}/refunds).

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim** — every parameter name is the literal C# identifier;
> named arguments use those exact names (cancellation token is `ct:`). Pass every nullable-no-default param
> explicitly (`null` to skip).
> ⚠ Every SDK type is written **fully-qualified from the path the map gives for THAT type**. Namespaces:
> client/options root `PayPal`; controllers `PayPal.Api`; records `PayPal.Models`; enums
> `PayPal.Models.Enums`; errors `PayPal.Errors`; env `PayPal.Servers`; OAuth creds
> `PayPal.Core.Authentication.OAuth2.ClientCredentials`; `SdkException<T>` `PayPal.Core.Exceptions`;
> `RawError`/`ApiError` `PayPal.Core.ErrorResponse`; `RetryOptions` `PayPal.Core.Configuration`.

### Client construction / auth / server (source: sdk-map.md §Getting a client, §Servers & auth; `PayPalClientOptions.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs`)
- `new PayPalClient(HttpClient httpClient, PayPalClientOptions options)` — only ctor.
- `options.Oauth2 = new PayPal.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials { ClientId=…, ClientSecret=… }`.
- `options.Environment = PayPal.Servers.ServerEnvironment.Production` — **only** environment; value
  `"production"` → hosting **PayPal Sandbox** (`https://api-m.sandbox.paypal.com`). `FromValue` for
  arbitrary strings is not exposed for `ServerEnvironment`; map the config value ourselves (always
  Production).
- BaseUrl override: `options.Server.Default.Production.BaseUrl = <PayPal:BaseUrl>` (verbatim). Token endpoint
  is `server.Default("/v1/oauth2/token")` → **also uses this BaseUrl** (satisfies "every call incl. token").
- Groups: 1 (`Default`). Retry via `options.Retry` (`RetryOptions.Default() with { … }`; members `required`).

### Orders (accessor `client.Orders`; source: map/operations/Orders.md, `Api/Orders.cs`)
| Op | Signature (verbatim) | Returns | Error case | Reads |
| --- | --- | --- | --- | --- |
| CreateOrder | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `Order` | A `SdkException<CreateOrderError>`; `TryGetError(out Error)`[400,401,422] · `TryGetRawError` | `Order.Id`, `Order.Status` |
| AuthorizeOrder | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `OrderAuthorizeResponse` | A `SdkException<AuthorizeOrderError>`; `TryGetError(out Error)`[400,401,403,404,422,500] · `TryGetRawError` | `.Status`, `.PurchaseUnits[0].Payments.Authorizations[0].{Id,Status,ExpirationTime,Amount}` |

- Body: `OrderRequest { Intent (intent): CheckoutPaymentIntent [req], PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> [req, 1..10], PaymentSource (payment_source): PaymentSource? }` (`Models/OrderRequest.cs`).
- `PurchaseUnitRequest { Amount (amount): AmountWithBreakdown [req], ReferenceId?, CustomId (custom_id): string?, InvoiceId (invoice_id): string?, Description? }` (`Models/PurchaseUnitRequest.cs`). Set `CustomId`/`InvoiceId` = eShop order id (reconciliation join).
- `AmountWithBreakdown { CurrencyCode (currency_code): string [req, len3], Value (value): string [req, decimal-as-string] }` (`Models/AmountWithBreakdown.cs`).
- `PaymentSource { Card (card): CardRequest?, Token (token): Token? }` (`Models/PaymentSource.cs`).
- `CardRequest { Name?, Number (number): string? [PAN], Expiry (expiry): string? "YYYY-MM", SecurityCode (security_code): string? [CVV], BillingAddress (billing_address): Address?, VaultId (vault_id): string? }` (`Models/CardRequest.cs`). One-off card → Number/Expiry/SecurityCode/Name/BillingAddress. Saved card → **VaultId only**.
- `CheckoutPaymentIntent.Authorize` = `"AUTHORIZE"` (`Models/Enums/CheckoutPaymentIntent.cs`).
- `OrderStatus` (`Models/Enums/OrderStatus.cs`): `Completed`="COMPLETED", `Approved`="APPROVED", `Created`="CREATED", `Voided`, `Saved`, **`PayerActionRequired`="PAYER_ACTION_REQUIRED"** ⇒ 3DS/browser challenge → STOP+report (§6).
- `AuthorizationWithAdditionalData { Id (id): string?, Status (status): AuthorizationStatus?, Amount (amount): Money?, ExpirationTime (expiration_time): string?, ... }` (`Models/AuthorizationWithAdditionalData.cs`). `PaymentCollection.Authorizations` (`Models/PaymentCollection.cs`).
- `AuthorizationStatus` (`Models/Enums/AuthorizationStatus.cs`): `Created`="CREATED", `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending`. (No "EXPIRED" constant — a real EXPIRED deserializes as unknown `.Value=="EXPIRED"`; tolerated.)

### Payments (accessor `client.Payments`; source: map/operations/Payments.md, `Api/Payments.cs`)
| Op | Signature | Returns | Error accessors |
| --- | --- | --- | --- |
| CaptureAuthorizedPayment | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `CapturedPayment` | A `CaptureAuthorizedPaymentError`: `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` |
| GetAuthorizedPayment | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions?=null, CancellationToken ct=default)` | `PaymentAuthorization` | A `GetAuthorizedPaymentError`: `TryGetError`[401,403,404] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` |
| ReauthorizePayment | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `PaymentAuthorization` | A `ReauthorizePaymentError`: `TryGetError`[400,401,403,404,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` |
| RefundCapturedPayment | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `Refund` | A `RefundCapturedPaymentError`: `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` |
| VoidPayment | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `PaymentAuthorization` | A `VoidPaymentError`: `TryGetError`[401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` |

- `CaptureRequest { Amount (amount): Money?, FinalCapture (final_capture): bool?=false, InvoiceId?, NoteToPayer? }` (`Models/CaptureRequest.cs`). Capture full → `FinalCapture=true`, amount omitted (defaults to full auth).
- `CapturedPayment { Id (id): string?, Status (status): CaptureStatus?, Amount (amount): Money?, SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown? }` (`Models/CapturedPayment.cs`).
- `SellerReceivableBreakdown { GrossAmount (gross_amount): Money [req], PaypalFee (paypal_fee): Money?, NetAmount (net_amount): Money? }` (`Models/SellerReceivableBreakdown.cs`) → captured amount / fee / net proceeds.
- `Money { CurrencyCode (currency_code): string [req], Value (value): string [req] }` (`Models/Money.cs`).
- `PaymentAuthorization { Id?, Status (status): AuthorizationStatus?, Amount?, ExpirationTime? }` (`Models/PaymentAuthorization.cs`).
- `RefundRequest { Amount (amount): Money?, CustomId?, InvoiceId?, NoteToPayer? }` — empty body = full refund; `Amount` set = partial (`Models/RefundRequest.cs`).
- `Refund { Id (id): string?, Status (status): RefundStatus?, Amount (amount): Money? }` (`Models/Refund.cs`).
- `ReauthorizeRequest { Amount (amount): Money? }` (`Models/ReauthorizeRequest.cs`).
- `Error` (`Models/Error.cs`) `{ Name [req], Message [req], DebugId (debug_id) [req], Details (details): IReadOnlyList<ErrorDetails>? }`; `ErrorDetails { Issue (issue): string [req], Description?, Field?, Location? }` (`Models/ErrorDetails.cs`). Stale-auth detection: `Issue` contains/equals an expired-authorization code (e.g. `AUTHORIZATION_EXPIRED`/`AUTH_EXPIRED`) on the 4xx from capture.

### Vault (accessor `client.Vault`; source: map/operations/Vault.md, `Api/Vault.cs`)
| Op | Signature | Returns | Error accessors |
| --- | --- | --- | --- |
| CreatePaymentToken | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions?=null, CancellationToken ct=default)` | `PaymentTokenResponse` | A `CreatePaymentTokenError`: `TryGetError`[400,403,404,422,500] · `TryGetRawError` |
| ListCustomerPaymentTokens | `ListCustomerPaymentTokens(string customerId, int? pageSize=5, int? page=1, bool? totalRequired=false, RequestOptions?=null, CancellationToken ct=default)` | `CustomerVaultPaymentTokensResponse` | A `ListCustomerPaymentTokensError`: `TryGetError`[400,403,500] · `TryGetRawError` |
| DeletePaymentToken | `DeletePaymentToken(string id, RequestOptions?=null, CancellationToken ct=default)` | `void` (Task) | A `DeletePaymentTokenError`: `TryGetError`[400,403,500] · `TryGetRawError` |

- Vaulting a card directly: `PaymentTokenRequest { Customer (customer): Customer?, PaymentSource (payment_source): PaymentTokenRequestPaymentSource [req] }` (`Models/PaymentTokenRequest.cs`).
- `PaymentTokenRequestPaymentSource { Card (card): PaymentTokenRequestCard? }` (`Models/PaymentTokenRequestPaymentSource.cs`).
- `PaymentTokenRequestCard { Name?, Number (number): string? [PAN], Expiry (expiry): string? "YYYY-MM", SecurityCode?, BillingAddress? }` (`Models/PaymentTokenRequestCard.cs`).
- `Customer { Id (id): string?, MerchantCustomerId (merchant_customer_id): string? }` (`Models/Customer.cs`). We assign a stable per-buyer PayPal `customer.id` (persist it) so `ListCustomerPaymentTokens(customerId)` returns that buyer's cards. (id regex `^[0-9a-zA-Z_-]+$`, len ≤22 → derive a compliant id per buyer, persist mapping.)
- `PaymentTokenResponse { Id (id): string?, Customer (customer): CustomerResponse?, PaymentSource (payment_source): PaymentTokenResponsePaymentSource? }` (`Models/PaymentTokenResponse.cs`). `Id` = vault token id (persist; use as `CardRequest.VaultId`).
- `PaymentTokenResponsePaymentSource.Card` = `CardPaymentTokenEntity { Name?, LastDigits (last_digits): string?, Brand (brand): CardBrand?, Expiry (expiry): string? }` (`Models/CardPaymentTokenEntity.cs`) — the **safe** display (brand + last 4 + expiry), never PAN.
- `CustomerVaultPaymentTokensResponse { TotalItems?, TotalPages?, PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>? }` (`Models/CustomerVaultPaymentTokensResponse.cs`).

### TransactionSearch (accessor `client.TransactionSearch`; source: map/operations/TransactionSearch.md, `Api/TransactionSearch.cs`)
- `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, RequestOptions?=null, CancellationToken ct=default)` → `SearchResponse`. **Error Case B** `SdkException<RawError>` (no typed accessors — read `ex.Error.StatusCode`/`ReadAsString()`).
- `SearchResponse { TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?, Page?, TotalItems?, TotalPages (total_pages): int? }` (`Models/SearchResponse.cs`). **Pagination**: not auto-`Pageable`; drive `page` 1..`TotalPages` myself (bounded loop, page cap).
- `TransactionDetails.TransactionInfo` = `TransactionInformation { TransactionId (transaction_id): string?, TransactionStatus (transaction_status): string?, TransactionAmount (transaction_amount): Money?, FeeAmount?, InvoiceId (invoice_id): string?, CustomField (custom_field): string?, TransactionInitiationDate? }` (`Models/TransactionInformation.cs`). Join to eShop by `InvoiceId`/`CustomField` (= eShop order id) and/or `TransactionId` (= capture/refund id).
- `startDate`/`endDate` are ISO-8601 date-times (pass request `from`/`to` verbatim, RFC3339).

### Enums needed
- `CheckoutPaymentIntent`: `Authorize`="AUTHORIZE" (also `Capture`).
- `OrderStatus`: `Completed`,`Approved`,`Created`,`Voided`,`PayerActionRequired`.
- `AuthorizationStatus`: `Created`,`Captured`,`Voided`,`Denied`,`PartiallyCaptured`,`Pending`.
- Read enum wire value via `.Value` (NOT `ToString()`); `==` compares by value.

## 3. Trap notes (hazard + skill pointer — not resolved here)
- S1 client lifetime & the singleton/stale-DNS + per-attempt-`Timeout` traps, and that an unset
  `LoggerFactory` arms an env var → **MUST load dotnet-client-initialization**, **dotnet-configuration-resilience**.
- S1 OAuth2 client-credentials caching, and that a **missing/blank credential fails only as a later 401**
  (fail-fast needed) → **MUST load dotnet-authentication**.
- S1/all: `Timeout` is per-attempt not a call budget; which verbs the SDK resends; what `LogRequestBody`
  logs unredacted → **MUST load dotnet-configuration-resilience**.
- S3/S4 building card/amount/vault models: `required` init members, enums are `StringEnum` not C# enums,
  `.Value` vs `ToString()`, wire-name vs C# name → **MUST load dotnet-models**.
- S4–S8 first calls & named-arg / must-pass-null params, response-envelope unwrap
  (`purchase_units[].payments.authorizations[]`) → **MUST load dotnet-calling-endpoints**.
- S4–S8 error boundary: Case A vs B per op, `TryGetNoContent(out RawError)` is a distinct 500 accessor to
  cover before `TryGetRawError`; **2xx `JsonException` vs error-path `JsonException` mean opposite things**;
  `HttpRequestException`/`TaskCanceledException` bypass `SdkException` catches → **MUST load dotnet-error-handling**.
- S8 reconciliation paging must be **bounded** (page cap) and cover the whole `TotalPages`, and Case-B
  `RawError` read → **MUST load dotnet-configuration-resilience**, **dotnet-error-handling**.
- Tests: which seam to fake → **MUST load dotnet-testing**.

## 4. REQUIRED READING (load before implementation; sheet does NOT carry their contents)
- **dotnet-client-initialization** — S1 client construction & DI/HttpClient lifetime.
- **dotnet-authentication** — S1 OAuth2 credentials + startup fail-fast.
- **dotnet-configuration-resilience** — S1 retries/timeouts/logging + S8 pagination.
- **dotnet-models** — S3/S4 request-model construction, enums.
- **dotnet-calling-endpoints** — S4–S8 first calls, named args, response unwrap.
- **dotnet-error-handling** — every op's try/catch boundary (always required).
- **dotnet-testing** — integration tests.
- Mandatory `JsonException` hazard rows: (a) a drifted/malformed **2xx** body surfaces as
  `System.Text.Json.JsonException` from deserialization, **not** `SdkException` — an SDK-exception-only
  ladder lets it escape; (b) a **non-2xx** body not matching its `{Operation}Error` throws `JsonException`
  **while the error object is constructed**, replacing the `SdkException` and destroying the HTTP status.
  Catch `JsonException` at the gateway boundary and map to a safe provider error.

## 5. PRODUCTION READINESS
| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalSettings` bound from `PayPal:` section; `[Required]` on `ClientId`,`ClientSecret`,`Environment`,`Currency`; `.ValidateDataAnnotations().ValidateOnStart()` in Program.cs + explicit blank-check guard (each part; blank ≠ missing) that throws naming the key, never the value. Host refuses to boot if any is missing/blank. |
| 2 | Secret sourcing & rotation | Secrets from env vars → loaded into **.NET user-secrets** (values never in repo). SDK options built **once at registration** and captured in the singleton `PayPalClient`, so a rotated secret needs a process restart — acceptable for this reference app; documented in the verify guide. |
| 3 | Total timeout budget | `options.Retry.Timeout` set explicitly (per-attempt, e.g. 30s), and the gateway wraps every SDK call in a `CancellationTokenSource` linked to `HttpContext.RequestAborted` with a total budget (e.g. 60s) — the only thing that bounds a whole call. `HttpClient.Timeout` also set as backstop. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS → our writes (POST create/authorize/capture/refund/void, DELETE token) are **never resent by the SDK**. Reads (SearchTransactions GET, GetAuthorizedPayment GET, ListCustomerPaymentTokens GET) are retryable — fine. Keep defaults; do NOT add POST to the retry list. |
| 5 | Idempotency & ambiguous writes | Authorize/capture: app-level guard on `Payment.Status` + persisted PayPal ids (double-click returns the existing result, no second auth/capture) **and** a stable `payPalRequestId` (`PayPal-Request-Id`, a real key per `Api/*.cs`) derived from the eShop order id for network-level dedup. Refund: **caller-supplied idempotency key** → stored per payment; a repeat key returns the stored `refundId` (no second refund); the key is also passed as `payPalRequestId`. Two distinct partial refunds use distinct keys and both proceed, guarded by `sum(refunds)+amount ≤ capturedGross`. Transport failure on a write → reconcile via `GET /api/reconciliation` / GetAuthorizedPayment (outcome unknown, not "failed"). |
| 6 | Observability | Gateway logs op name + PayPal ids + PayPal error `Name`/`Issue`/**`debug_id`** (correlation id) at Info/Warning; SDK built-in logger on (Info: request line, allow-list redacted). No card data in any log. `LoggerFactory` set explicitly from `ILoggerFactory`. |
| 7 | Sensitive data | Card PAN/CVV flow through `CreateOrder` & `CreatePaymentToken` request bodies. `options.Logging.LogRequestBody` stays **false** and `options.Logging.LoggerFactory` is assigned explicitly (so `PAYPALCLIENT_LOG=trace` cannot force body logging on). Our own diagnostics never echo the card fields. PAN/CVV are never persisted (DB stores only brand/last4/expiry) and never returned in responses. |
| 8 | Environment selection | One SDK env (`ServerEnvironment.Production` → sandbox host). `PayPal:Environment` bound & validated but the SDK exposes only Production; we always select Production (which IS sandbox) and additionally honor `PayPal:BaseUrl` override. All dev/test traffic is sandbox by construction; no live host reachable. |

## 6. Assumptions & Blockers
- **Assumption**: `AddEndpoints()` registers base-`IEndpoint` implementers (verify at runtime; fallback noted §0). Minor.
- **Assumption**: direct-card authorize on sandbox test card `4111 1111 1111 1111` completes without a 3DS
  challenge (task says the account is enabled for direct card & no browser needed). If a response comes back
  `PAYER_ACTION_REQUIRED` / carries a `payer-action` approve link, the endpoint returns a clear
  "browser approval required — not supported" error and we STOP+report (task mandate), rather than building
  an approval round-trip.
- **UNVERIFIED (live-only)**: the exact PayPal `Issue` code for an expired authorization on capture, and the
  reauthorize path — sandbox auths don't expire within a test run. Implemented defensively: on a capture 4xx
  whose `Issue` indicates an expired/again-authorization-required authorization, call `ReauthorizePayment`
  then retry capture once; if reauthorize also fails (e.g. past the 30-day window), surface an
  operator-actionable error ("authorization can no longer be renewed — re-collect payment"). No fabricated
  data path; both branches use real SDK ops.
- **VERIFIED live (SDK quirk, corrected in code)**: `Payments.VoidPayment` succeeds with HTTP **204 No
  Content**, but its generated return type is `PaymentAuthorization`, so the SDK throws
  `System.Text.Json.JsonException` deserializing the empty body on success. The gateway attaches a per-call
  `SdkHook.OnResponse` to read the real status and treats a 2xx-with-empty-body as success (not a transport
  failure). This is the "2xx JsonException = outcome-known-success" branch of the error-handling hazard.
- **No Blockers**: every required capability (authorize hold, capture with fee/net, void, partial/full
  refund, vault card + list + delete, transaction search) is covered by an SDK op above. All flows verified
  live on the sandbox: authorize (hold), capture (with PayPal fee/net), void, partial + full refund with
  idempotency & cap enforcement, vault a card + reuse it to pay + delete, and reconciliation.

## 7. Source labels — every contract row cites its map page or declaring file (above). Rows without a
  citation are `YOUR CALL — not in the map` (§0 architecture, idempotency guard design, persistence,
  request contract our own callers must satisfy) and are decided against the task at implementation time.
