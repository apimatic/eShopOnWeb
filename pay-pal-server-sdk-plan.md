# PayPal Server SDK (.NET) integration plan — eShopOnWeb PublicApi

Add card payments + saved cards to eShopOnWeb, additively, on `src/PublicApi`.
PayPal is the processor, accessed **only** through the PayPal Server SDK (.NET), root
namespace `PayPalServerSdk`, vendored from source into `src/PayPalServerSdk`.

## 1. Scope & sequence

Layering: domain + application logic in `ApplicationCore` (depends only on the
`IPaymentGateway` abstraction — no SDK types); SDK adapter + EF config in `Infrastructure`;
HTTP endpoints in `PublicApi`.

1. **Vendor SDK**: copy the SDK source to `src/PayPalServerSdk`, opt it out of central
   package management, add `ProjectReference` from `Infrastructure`. Roll `global.json`
   forward to `latestMajor`.
2. **Domain (ApplicationCore)** — additive aggregates keyed by the caller's identity
   (`BuyerId` = `User.Identity.Name`, same as the existing `Order.BuyerId`):
   - `OrderPayment` (aggregate root) — one per eShop order: holds PayPal order id,
     authorization id, capture id, status, authorized/captured/fee/net amounts,
     authorization expiry, idempotency keys, and a child collection of `PaymentRefund`
     (paypal refund id, idempotency key, amount, status).
   - `SavedPaymentMethod` (aggregate root) — vaulted card: PayPal vault-token id, PayPal
     customer id, brand/last4/expiry/cardholder (safe descriptor only), alias.
   - `IPaymentGateway` (abstraction) + plain request/result records; `IPaymentService`
     (application façade); specifications for by-order / by-buyer / by-id lookups.
3. **Gateway adapter (Infrastructure)** — `PayPalPaymentGateway : IPaymentGateway` over
   `PayPalServerSdkClient`; translates SDK exceptions to domain `PaymentGatewayException`.
   EF `IEntityTypeConfiguration`s + `DbSet`s on `CatalogContext`.
4. **Application service (ApplicationCore)** — `PaymentService` orchestrates repos +
   gateway + idempotency + amount/refund invariants.
5. **Endpoints (PublicApi)** — Flow 1: `POST /api/orders`, `/pay`, `/fulfil`, `/cancel`,
   `/refunds`, `GET /api/my-orders`, `GET /api/reconciliation`. Flow 2:
   `POST/GET /api/payment-methods`, `DELETE /api/payment-methods/{id}`. Operator-only
   endpoints (`fulfil`, `cancel`, `reconciliation`) carry `[Authorize(Roles=Administrators)]`;
   the rest are shopper-scoped to the caller.
6. **Config + fail-fast** — bind `PayPal:` section; validate at startup; register the SDK
   client via `AddPayPalServerSdkClient`; user-secrets hold the values.
7. **Verify** end-to-end on the sandbox card + a saved-card reuse; write a short test suite.

### Capability → operation map (all on the SDK map)

| App action | SDK op(s) |
| --- | --- |
| `/pay` authorize (hold) | `Orders.CreateOrder` (intent=AUTHORIZE, payment_source card **or** card.vault_id, `PayPal-Request-Id`) → inspect returned `Order.purchase_units[].payments.authorizations[]`; if no authorization present and status APPROVED, `Orders.AuthorizeOrder` |
| `/fulfil` capture | `Payments.CaptureAuthorizedPayment` (`PayPal-Request-Id`); staleness → `Payments.GetAuthorizedPayment` then `Payments.ReauthorizePayment` |
| `/cancel` release hold | `Payments.VoidPayment` |
| `/refunds` | `Payments.RefundCapturedPayment` (`PayPal-Request-Id` = caller idempotency key) |
| saved card create | `Vault.CreatePaymentToken` |
| saved card delete | `Vault.DeletePaymentToken` |
| reconciliation | `TransactionSearch.SearchTransactions` (paginated over `total_pages`) |

Saved-card **list** (`GET /api/payment-methods`) is served from our own DB (safe descriptor
we persisted at vault time) — not a PayPal call — so no gap. `Vault.ListCustomerPaymentTokens`
remains available if a PayPal-side listing is ever needed.

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim** — every parameter name is the literal
> C# identifier; named arguments must use them exactly (the cancellation-token parameter is
> literally `ct`). ⚠ Every SDK type is written fully-qualified against the namespace its
> source path implies (`Models/` → `PayPalServerSdk.Models`, `Models/Enums/` →
> `PayPalServerSdk.Models.Enums`, `Errors/` → `PayPalServerSdk.Errors`, client/options/servers
> → `PayPalServerSdk` / `PayPalServerSdk.Servers`), taken from THAT type's own path.

| Op | Signature (verbatim) · request fields used · response fields read · error case · source |
| --- | --- |
| `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)`. Body `OrderRequest`: `Intent (intent): CheckoutPaymentIntent` **required**, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest>` **required** (min1), `PaymentSource (payment_source): PaymentSource?`. Pass `payPalRequestId` = stable per-order key, others `null`, `prefer:"return=representation"`. Read `Order.Id`, `Order.Status (OrderStatus)`, `Order.PurchaseUnits[].Payments.Authorizations[].{Id,Status,ExpirationTime}`. Case A `SdkException<CreateOrderError>`, `TryGetError(out Error)` [400,401,422]. src: Orders.md, Models/OrderRequest.cs |
| `client.Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` → `OrderAuthorizeResponse`. 5 nullable params must be passed (`null` to skip); `body:null`, `prefer:"return=representation"`. Read `OrderAuthorizeResponse.PurchaseUnits[].Payments.Authorizations[].{Id,Status,ExpirationTime}`, `.Status`. Case A `AuthorizeOrderError`, `TryGetError(out Error)` [400,401,403,404,422,500]. **Remarks**: authorize needs prior buyer approval **or a valid payment_source** — the card/vault supplied at CreateOrder satisfies this; no browser step for the sandbox card. src: Orders.md, Models/OrderAuthorizeResponse.cs |
| `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` → `CapturedPayment`. Pass `payPalRequestId` = stable capture key; `body`=`CaptureRequest{ Amount=Money{CurrencyCode,Value}, FinalCapture=true }`; `prefer:"return=representation"`. Read `CapturedPayment.{Id, Status (CaptureStatus), Amount, SellerReceivableBreakdown.{GrossAmount, PaypalFee, NetAmount}}`. Case A `CaptureAuthorizedPaymentError`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback]. src: Payments.md, Models/CapturedPayment.cs, Models/SellerReceivableBreakdown.cs |
| `client.Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions?=null, CancellationToken ct=default)` → `PaymentAuthorization`. Read `.Status (AuthorizationStatus)`, `.ExpirationTime`. Case A `GetAuthorizedPaymentError` [401,403,404]+NoContent[500]. src: Payments.md, Models/PaymentAuthorization.cs |
| `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` → `PaymentAuthorization`. `body`=`ReauthorizeRequest{ Amount=Money }`; `prefer:"return=representation"`. Read `.Id, .Status, .ExpirationTime`. Case A `ReauthorizePaymentError` [400,401,403,404,422]+NoContent[500]. src: Payments.md, Models/ReauthorizeRequest.cs, Models/PaymentAuthorization.cs |
| `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` → `PaymentAuthorization`. Pass 3 nullable params (`null`). Read `.Status`. Case A `VoidPaymentError` [401,403,404,409,422]+NoContent[500]. src: Payments.md |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` → `Refund`. `payPalRequestId` = **caller idempotency key**; `body`=`RefundRequest{ Amount=Money? }` (null body/amount = full refund; amount = partial). Read `Refund.{Id, Status (RefundStatus), Amount}`. Case A `RefundCapturedPaymentError` [400,401,403,404,409,422]+NoContent[500]. src: Payments.md, Models/RefundRequest.cs, Models/Refund.cs |
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions?=null, CancellationToken ct=default)` → `PaymentTokenResponse`. Body: `Customer (customer): Customer?` (`Id` to attach to existing PayPal customer, else omit), `PaymentSource (payment_source): PaymentTokenRequestPaymentSource` **required** = `{ Card = PaymentTokenRequestCard{ Name, Number, Expiry, SecurityCode, BillingAddress } }`. Read `PaymentTokenResponse.{Id, Customer.Id, PaymentSource.Card (CardPaymentTokenEntity).{LastDigits, Brand, Expiry, Name}}`. Case A `CreatePaymentTokenError` [400,403,404,422,500]. src: Vault.md, Models/PaymentTokenRequest.cs, Models/PaymentTokenRequestCard.cs, Models/CardPaymentTokenEntity.cs |
| `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions?=null, CancellationToken ct=default)` → void. Case A `DeletePaymentTokenError` [400,403,500]. src: Vault.md |
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, RequestOptions?=null, CancellationToken ct=default)` → `SearchResponse`. Call with **named args** (8 nullable filters → `null`); loop `page` 1..`SearchResponse.TotalPages`. Read `SearchResponse.{TotalPages, TransactionDetails[].TransactionInfo (TransactionInformation).{TransactionId, InvoiceId, TransactionStatus, TransactionAmount (Money), FeeAmount, TransactionInitiationDate}}`. **Case B** `SdkException<RawError>` (StatusCode/ReadAsString/ReadAsJson). src: TransactionSearch.md, Models/SearchResponse.cs, Models/TransactionInformation.cs |

**Value objects.** `Money`/`AmountWithBreakdown`: `CurrencyCode (currency_code): string` **required**, `Value (value): string` **required** (decimal string). `CardRequest`: `Name, Number, Expiry (YYYY-MM), SecurityCode, BillingAddress (Address?), VaultId` — set `VaultId` to charge a saved card, else raw card fields. `Address` (Models/Address.cs) — read at build time.

**Enums** (build with static members / `.FromValue`, read via `.Value`):
- `CheckoutPaymentIntent.Authorize` = `"AUTHORIZE"` (and `.Capture`).
- `OrderStatus`: `Created/Saved/Approved/Voided/Completed/PayerActionRequired`.
- `AuthorizationStatus`: `Created/Captured/Denied/PartiallyCaptured/Voided/Pending`.
- `CaptureStatus`: `Completed/Declined/PartiallyRefunded/Pending/Refunded/Failed`.
- `RefundStatus`: `Cancelled/Failed/Pending/Completed`.

**Client construction / auth / server** (src: sdk-map.md *Getting a client* + *Servers & auth*,
`ServiceCollectionExtensions.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs`):
- `services.AddPayPalServerSdkClient(o => { o.Oauth2 = new OAuth2ClientCredentials{ClientId,ClientSecret}; o.Environment = ServerEnvironment.Sandbox; ... })` — one server env (`Sandbox`).
- `PayPal:BaseUrl` override → set `o.Server.Default.Sandbox.BaseUrl = baseUrl`. Confirmed in
  source that the OAuth **token** URL is `server.Default("/v1/oauth2/token")`, i.e. it resolves
  through the *same* `Sandbox.BaseUrl`, so one override redirects every call incl. the token.

## 3. Trap notes (name the hazard + the skill; do not resolve here)

- **Client/HttpClient lifetime & DI singleton.** The registered client captures its options
  object once; `HttpClient`/handler must be long-lived via `IHttpClientFactory`, not per-call.
  → **MUST load `paypal-platforms-team:dotnet-client-initialization`**.
- **Credential wiring & rotation timing.** When/where `Oauth2` is set and what a rotated
  secret does to an already-built singleton. → **MUST load `paypal-platforms-team:dotnet-authentication`**.
- **Positional mis-binding on `SearchTransactions`.** Many optional params have no C# default
  in a positional call → mis-bind. → **MUST load `paypal-platforms-team:dotnet-calling-endpoints`**.
- **Union/enum/optional model handling.** `PaymentSource` is a bag of optionals; enums are
  `StringEnum<T>` not C# enums; `AdditionalProperties` bag semantics. → **MUST load `paypal-platforms-team:dotnet-models`**.
- **Two-direction `JsonException` + Case A/B ladder.** See REQUIRED READING rows. → **MUST load `paypal-platforms-team:dotnet-error-handling`**.
- **Timeout is per-attempt; retry eligibility by HTTP method; `LogRequestBody` unredacted.**
  → **MUST load `paypal-platforms-team:dotnet-configuration-resilience`**.
- **Test seam = the `HttpClient` ctor arg.** → **MUST load `paypal-platforms-team:dotnet-testing`**.

## 4. REQUIRED READING (load every one BEFORE implementation; contents deliberately not copied here)

| Skill | Governs |
| --- | --- |
| `paypal-platforms-team:dotnet-client-initialization` | SDK client construction + DI registration (step 3/6) |
| `paypal-platforms-team:dotnet-authentication` | OAuth2 credential wiring, rotation (step 6) |
| `paypal-platforms-team:dotnet-calling-endpoints` | every `client.*` call, esp. named args on `SearchTransactions` (step 3/4) |
| `paypal-platforms-team:dotnet-models` | building request bodies / reading enums + unions (step 3/4) |
| `paypal-platforms-team:dotnet-error-handling` | the SDK exception boundary in the gateway adapter (step 3) |
| `paypal-platforms-team:dotnet-configuration-resilience` | retries/timeout/base-URL/pagination/logging (step 3/6) |
| `paypal-platforms-team:dotnet-testing` | gateway/integration tests (step 7) |

Two mandatory `JsonException` hazards (both bypass an SDK-exception-only catch ladder):
1. A drifted/malformed **2xx** body (a missing `required` member) surfaces as a
   `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — an
   SDK-exception-only catch lets it escape.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
   throws `JsonException` **while the error object is being constructed**, so it *replaces*
   the `SdkException` and the HTTP status is destroyed with it.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalSettings` bound from `PayPal:`; a startup validator throws if `ClientId`, `ClientSecret`, `Environment`, or `Currency` is missing/blank (each checked independently — a blank part ≠ a missing one). `BaseUrl` optional. Host refuses to start rather than 401 on first call. |
| 2 | Secret sourcing & rotation | Values come from env vars → loaded into **.NET user-secrets** (`PayPal:ClientId/ClientSecret/Environment/Currency`); never written to any repo file. The DI singleton captures options once at registration, so a rotated secret takes effect only on process restart — acceptable for this app; documented. |
| 3 | Total timeout budget | SDK `Timeout` is **per attempt**. Endpoints pass the ASP.NET request-aborted `CancellationToken` through to every SDK call as `ct:`, which is the only thing that bounds a whole (possibly retried) call. Retries left at SDK default; a per-call `ct` deadline caps wall-clock. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS, so our POST writes (create/authorize/capture/void/refund/vault) are **never auto-resent** by the SDK — each carries its own idempotency key regardless (row 5). No PUT/PATCH writes in scope. |
| 5 | Idempotency & ambiguous writes | authorize → stable `PayPal-Request-Id` `pay:{orderId}` + app guard (already-authorized returns existing); capture → stable `cap:{orderId}` + guard; void → `void:{orderId}` + guard; **refund → caller-supplied idempotency key** passed as `PayPal-Request-Id`, plus a DB uniqueness guard on that key so a repeat returns the same refund while two *distinct* keys make two legitimate partial refunds. Refund total is capped at captured amount. `CreateOrder`+authorize share the `pay:{orderId}` key. |
| 6 | Observability | Info: state transitions (order/payment ids, PayPal ids, status). Warning: reauthorize path. Error: gateway failures with PayPal `debug_id`/message from the error body. `LogRequestBody` stays **off** (row 7). No card data ever logged. |
| 7 | Sensitive data | In scope: `CardRequest`/`PaymentTokenRequestCard` carry PAN + CVV + expiry. Therefore `Logging.LogRequestBody` is left OFF **and** `Logging.LoggerFactory` is set explicitly at registration so the `PAYPALSERVERSDKCLIENT_LOG` env var cannot force body logging on. Our own code logs only vault-token id + last4/brand (safe descriptor); raw PAN/CVV are never persisted or logged. |
| 8 | Environment selection | One server group (`Default`), one env member (`ServerEnvironment.Sandbox`). All deployments here target Sandbox; `PayPal:Environment` is validated to be `sandbox`. `PayPal:BaseUrl`, when set, overrides `Server.Default.Sandbox.BaseUrl` verbatim (and thereby the token URL). No live host is reachable, so test traffic cannot leak to production. |

## 6. Assumptions & Blockers

- **Assumption**: identity/ownership key = `User.Identity.Name` (the JWT `name` claim), matching
  the existing `Order.BuyerId` convention in the Web app. Minor.
- **Assumption**: saved-card **list** may be served from our own persisted safe descriptor
  rather than a live `Vault.ListCustomerPaymentTokens` call — not a PayPal interaction, so no
  mandate conflict. Minor.
- **Assumption**: create-order-with-payment_source may already yield the authorization in the
  `CreateOrder` response for the sandbox card; the adapter inspects the response and only calls
  `AuthorizeOrder` when no authorization is present (defensive, correct either way).
- **No blockers.** Every capability in scope maps to an SDK operation on the map. If PayPal
  answers the card with a browser challenge (`PAYER_ACTION_REQUIRED` / a `payer-action` link),
  the adapter STOPS and reports it as an unsupported-challenge error rather than building an
  approval round-trip (per task).

## 7. Source labels — every contract row above cites its map page or declaring file. Application
design rows (persistence, ownership, idempotency policy, endpoint shape) are **YOUR CALL — not
in the map** and decided against the task.
