# PayPal Server SDK integration plan — eShopOnWeb PublicApi

Adds card payments (PayPal as processor) and saved cards to `src/PublicApi`, additively, on top of the
existing `Order`/`OrderItem` model. SDK facts below come from the SDK map + the map-named source files
(read this session); application-design rows are labelled `YOUR CALL`.

## 1. Scope & sequence

1. **Vendor the SDK** into `src/PayPalServerSdk/` (no NuGet feed), opt it out of central package
   management, reference it from `Infrastructure`, add to both solutions.
2. **Config + client** — bind `PayPal:` options, fail-fast, register `AddPayPalServerSdkClient` (base-URL
   override applies to the token call too — see §2 client notes).
3. **Domain** — `OrderPayment` (agg root) + `PaymentRefund` (child) + `SavedPaymentMethod` (agg root) +
   `PaymentStatus`; EF configs with unique indexes; `DbSet`s; specs.
4. **Gateway** `IPayPalGateway` (ApplicationCore interface, plain DTOs) implemented in
   `Infrastructure/Payments` using the SDK. One boundary that translates every SDK/transport failure into
   `PayPalGatewayException` (carries provider issue code + debug id + status).
5. **Orchestration** `PaymentService` (ApplicationCore) — persist-local-first, PayPal call, settle;
   idempotency, stale-auth renewal, refunds, reconciliation.
6. **Endpoints** on PublicApi (`IEndpoint` minimal-API style): `orders`, `orders/{id}/pay`,
   `orders/{id}/fulfil`, `orders/{id}/cancel`, `orders/{id}/refunds`, `my-orders`, `reconciliation`,
   `payment-methods` (POST/GET/DELETE).
7. **Secrets** into user-secrets from env; build; test; verify flows on sandbox; write verify guide.

Operations used (all `client.{Group}.{Op}`, throw-based):
`Orders.CreateOrder`, `Orders.AuthorizeOrder`; `Payments.CaptureAuthorizedPayment`,
`Payments.ReauthorizePayment`, `Payments.GetAuthorizedPayment`, `Payments.VoidPayment`,
`Payments.RefundCapturedPayment`; `Vault.CreatePaymentToken`, `Vault.DeletePaymentToken`;
`TransactionSearch.SearchTransactions`.

**Card auth flow (YOUR CALL, defensive against live status):** `pay` = `CreateOrder(intent=AUTHORIZE,
purchase_units=[amount], payment_source.card{ number | vault_id }, PayPal-Request-Id=payKey)`. Read the
returned `Order.Status` + `purchase_units[0].payments.authorizations[0]`:
- authorization already present (status `COMPLETED`) → use it (single-step card create authorizes inline);
- status `APPROVED`, no authorization → call `AuthorizeOrder(id, …, body:null)` and read its
  `OrderAuthorizeResponse.purchase_units[0].payments.authorizations[0]`;
- status `PAYER_ACTION_REQUIRED` (or a link rel `payer-action`) → **STOP path**: return an operator/shopper
  error "PayPal requires browser approval (3DS challenge)"; this is the task's *stop-and-report* case, not
  an approval round-trip. (Sandbox test card `4111…` does not challenge, so this path is defensive.)

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim; every parameter name is the literal C# identifier — named
arguments must use them exactly (the cancellation-token param really is `ct`, so write `ct:`). The 5
"must-pass-explicitly" header params on create/authorize/capture/refund are nullable-without-default → pass
`null` to skip.
⚠ Every SDK type is written fully-qualified by the namespace its source path implies (`Models/` →
`PayPalServerSdk.Models`; `Models/Enums/` → `PayPalServerSdk.Models.Enums`; `Errors/` →
`PayPalServerSdk.Errors`; `Core/ErrorResponse/` → `PayPalServerSdk.Core.ErrorResponse`;
`Core/Exceptions/` → `PayPalServerSdk.Core.Exceptions`).

| Op | Signature (params in order) | Request model + fields used | Response envelope → fields read | Error case | Source |
| --- | --- | --- | --- | --- | --- |
| `Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `OrderRequest{ Intent(intent):CheckoutPaymentIntent **req**; PurchaseUnits(purchase_units):IReadOnlyList<PurchaseUnitRequest> **req** (min1); PaymentSource(payment_source):PaymentSource? }` | `Order{ Id(id):string?; Status(status):OrderStatus?; PurchaseUnits(purchase_units):IReadOnlyList<PurchaseUnit>? }` → `PurchaseUnit.Payments(payments):PaymentCollection?` → `Authorizations:IReadOnlyList<AuthorizationWithAdditionalData>?` → `.Id, .Status, .ExpirationTime` | A `SdkException<CreateOrderError>`; `TryGetError(out Error)`[400,401,422] · `TryGetRawError`[fallback] | map Orders.md; Models/OrderRequest.cs, Order.cs, PurchaseUnit.cs, PaymentCollection.cs, AuthorizationWithAdditionalData.cs |
| `Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `OrderAuthorizeRequest{ PaymentSource(payment_source):OrderAuthorizeRequestPaymentSource? }` — pass `body:null` (card already attached at create) | `OrderAuthorizeResponse{ Id; Status:OrderStatus?; PurchaseUnits:IReadOnlyList<PurchaseUnit>? }` → same authorizations path | A `SdkException<AuthorizeOrderError>`; `TryGetError`[400,401,403,404,422,500]·`TryGetRawError` | map Orders.md; Models/OrderAuthorizeRequest.cs, OrderAuthorizeResponse.cs |
| `Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `body:null` (full capture). **Pass `prefer:"return=representation"`** to get the fee breakdown. `payPalRequestId=captureKey` (server stores 45d → real idempotency key) | `CapturedPayment{ Id(id):string?; Status(status):CaptureStatus?; Amount(amount):Money?; SellerReceivableBreakdown(seller_receivable_breakdown):SellerReceivableBreakdown? }` → `GrossAmount(gross_amount):Money **req**`, `PaypalFee(paypal_fee):Money?`, `NetAmount(net_amount):Money?`; `Money{ CurrencyCode, Value }` | A `SdkException<CaptureAuthorizedPaymentError>`; `TryGetError`[400,401,403,404,409,422]·`TryGetNoContent(out RawError)`[500]·`TryGetRawError` | map Payments.md; Models/CapturedPayment.cs, SellerReceivableBreakdown.cs, Money.cs |
| `Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `ReauthorizeRequest{ Amount(amount):Money? }` = order total | `PaymentAuthorization{ Id:string?; Status:AuthorizationStatus?; ExpirationTime:string? }` — **new** authorization id | A `SdkException<ReauthorizePaymentError>`; `TryGetError`[400,401,403,404,422]·`TryGetNoContent`[500]·`TryGetRawError` | map Payments.md; Models/ReauthorizeRequest.cs, PaymentAuthorization.cs |
| `Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions?=null, CancellationToken ct=default)` | — | `PaymentAuthorization{ Status, ExpirationTime }` (re-read after ambiguous write) | A; `TryGetError`[401,403,404]·`TryGetNoContent`[500]·`TryGetRawError` | map Payments.md |
| `Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | empty body; `payPalRequestId=voidKey` | `PaymentAuthorization{ Status }` (→ VOIDED) | A `SdkException<VoidPaymentError>`; `TryGetError`[401,403,404,409,422]·`TryGetNoContent`[500]·`TryGetRawError` | map Payments.md |
| `Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `RefundRequest{ Amount(amount):Money? }` — **omit Amount for full refund**, set for partial. `payPalRequestId=caller idempotency key` (real key, 45d). `prefer:"return=representation"` | `Refund{ Id(id):string?; Status(status):RefundStatus?; Amount(amount):Money? }` | A `SdkException<RefundCapturedPaymentError>`; `TryGetError`[400,401,403,404,409,422]·`TryGetNoContent`[500]·`TryGetRawError` | map Payments.md; Models/RefundRequest.cs, Refund.cs |
| `Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions?=null, CancellationToken ct=default)` | `PaymentTokenRequest{ Customer(customer):Customer?{ MerchantCustomerId(merchant_customer_id) }; PaymentSource(payment_source):PaymentTokenRequestPaymentSource **req**{ Card:PaymentTokenRequestCard{ Name,Number,Expiry,SecurityCode,BillingAddress } } }` | `PaymentTokenResponse{ Id(id):string?; Customer(customer):CustomerResponse?→.Id; PaymentSource(payment_source):PaymentTokenResponsePaymentSource?→ Card:CardPaymentTokenEntity{ LastDigits(last_digits), Brand(brand):CardBrand?, Expiry(expiry), Name } }` | A `SdkException<CreatePaymentTokenError>`; `TryGetError`[400,403,404,422,500]·`TryGetRawError` | map Vault.md; Models/PaymentTokenRequest.cs, PaymentTokenRequestCard.cs, PaymentTokenResponse.cs, CardPaymentTokenEntity.cs, Customer.cs |
| `Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions?=null, CancellationToken ct=default)` | — | `void` | A `SdkException<DeletePaymentTokenError>`; `TryGetError`[400,403,500]·`TryGetRawError` | map Vault.md |
| `TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, RequestOptions?=null, CancellationToken ct=default)` | query only; pass 8 middle nullables as `null` (use **named args**). `startDate`/`endDate` RFC-3339; **max 31-day window** (remarks) | `SearchResponse{ TransactionDetails(transaction_details):IReadOnlyList<TransactionDetails>?; Page(page):int?; TotalPages(total_pages):int? }` → `TransactionDetails.TransactionInfo:TransactionInformation{ TransactionId(transaction_id), PaypalReferenceId(paypal_reference_id), TransactionAmount:Money?, TransactionInitiationDate }` | **B** `SdkException<RawError>` (`StatusCode`, `ReadAsString`) | map TransactionSearch.md; Models/SearchResponse.cs, TransactionDetails.cs, TransactionInformation.cs |

Pay-with-saved-card: on `CreateOrder`, set `PaymentSource.Card = new PayPalServerSdk.Models.CardRequest {
VaultId = <saved token id> }` (CardRequest has `VaultId(vault_id)`). `TokenType` enum has only
`BILLING_AGREEMENT`, so `PaymentSource.Token` is **not** the vault path — `card.vault_id` is.

Enums (`Models/Enums/`, `StringEnum<T>`, read via `.Value`, build via static member / `FromValue`):
- `CheckoutPaymentIntent.Authorize`("AUTHORIZE") / `.Capture`
- `OrderStatus`: `Created,Saved,Approved,Voided,Completed,PayerActionRequired`
- `AuthorizationStatus`: `Created,Captured,Denied,PartiallyCaptured,Voided,Pending`
- `CaptureStatus`: `Completed,Declined,PartiallyRefunded,Pending,Refunded,Failed`
- `RefundStatus`: `Cancelled,Failed,Pending,Completed`
- `Error{ Name(name) **req**, Message **req**, DebugId(debug_id) **req**, Details:IReadOnlyList<ErrorDetails>? }`; `ErrorDetails{ Issue(issue) **req**, Description }` — used to detect `AUTHORIZATION_EXPIRED` etc.

Client construction / auth / server node:
- `services.AddPayPalServerSdkClient(o => { o.Oauth2 = new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials{ ClientId, ClientSecret }; o.Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox; if(baseUrl set) o.Server.Default.Sandbox.BaseUrl = baseUrl; o.Logging = o.Logging with { LoggerFactory = NullLoggerFactory.Instance, LogRequestBody = false }; o.Retry = RetryOptions.Default() with { Timeout = 30s }; })`. (`ServiceCollectionExtensions.cs`, `PayPalServerSdkClientOptions.cs`.)
- Token URL = `server.Default("/v1/oauth2/token")` → resolves through `Server.Default.Sandbox.BaseUrl`, so a `PayPal:BaseUrl` override reaches the token request too (verified: `AuthSchemes.cs`, `Server.cs`, `DefaultOptions.cs`).
- `ServerEnvironment` exposes **only** `Sandbox` and its `FromValue` is `private` → cannot map an arbitrary `PayPal:Environment` string through it. Decision: always select `Sandbox`; `PayPal:Environment` is validated non-blank and, when not `sandbox`, the deployment must set `PayPal:BaseUrl` (the only real host knob this SDK has). Recorded in §5 row 8.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A capture only ever targets an authorization this app created for that order (its stored `AuthorizationId`, refreshed if reauthorized) | `CaptureAuthorizedPayment` ← `CreateOrder`/`AuthorizeOrder`/`ReauthorizePayment` | implementation (`OrderPayment.AuthorizationId`) |
| A refund only ever targets the capture this app recorded for that order | `RefundCapturedPayment` ← `CaptureAuthorizedPayment` | implementation (`OrderPayment.CaptureId`) |
| A `vault_id` used to pay must be a saved card **owned by the caller** and still present | `CreateOrder(card.vault_id)` ← `CreatePaymentToken` / not `DeletePaymentToken`d | implementation (owner-scoped `SavedPaymentMethod` lookup) |
| Sum of refunds must never exceed the captured amount | `RefundCapturedPayment` ← `CaptureAuthorizedPayment` | implementation (`OrderPayment` refund ledger check before call) |

## 3. Trap notes

- **CaptureAuthorizedPayment `prefer` defaults to `return=minimal`** → without `return=representation` the
  `seller_receivable_breakdown` (fee/net) may be absent; task requires captured/fee/net. Consequence: fee &
  net silently null. MUST load `paypal-platforms-team:dotnet-calling-endpoints` (param/prefer handling).
- **`POST`/`DELETE` are never resent by the SDK; `PayPal-Request-Id` is the real idempotency key on
  capture/void/refund (45d) and create/authorize (6h).** The generator-injected `Idempotency-Key` header is
  `Guid.NewGuid()` per call — inert. Consequence if confused: double capture/refund on caller retry. MUST
  load `paypal-platforms-team:dotnet-configuration-resilience` (retry/idempotency).
- **`JsonException` reaches the boundary from two directions** (drifted 2xx body vs non-2xx body not
  matching `{Operation}Error`) — opposite meanings. Consequence: status destroyed / pending-treated-as-fail.
  MUST load `paypal-platforms-team:dotnet-error-handling`.
- **A 2xx with a non-terminal `status` is not success** (`CaptureStatus.Pending`, `RefundStatus.Pending`,
  `AuthorizationStatus.Denied`). Consequence: recording money moved when it did not. MUST load
  `paypal-platforms-team:dotnet-error-handling`.
- **Card fields are sensitive; `LogRequestBody`/env-var can dump the raw PAN.** Consequence: PAN in logs.
  MUST load `paypal-platforms-team:dotnet-configuration-resilience` (logging/redaction).
- **SearchTransactions is Case B, is capped at a 31-day window, and pages via `total_pages`.** Consequence:
  silent truncation / range rejected. MUST load `paypal-platforms-team:dotnet-configuration-resilience`
  (pagination) + `paypal-platforms-team:dotnet-error-handling` (Case B).
- **`StringEnum.ToString()` is the debug form, not the wire value; use `.Value`.** Consequence: wrong strings
  in DB/logs. MUST load `paypal-platforms-team:dotnet-models`.

## 4. REQUIRED READING (load before implementation)

- `paypal-platforms-team:dotnet-client-initialization` — client/DI + HttpClient lifetime (step 2).
- `paypal-platforms-team:dotnet-authentication` — OAuth2 client-credentials + startup fail-fast (step 2).
- `paypal-platforms-team:dotnet-calling-endpoints` — signatures, named args, `prefer`, bodies (steps 4-5).
- `paypal-platforms-team:dotnet-models` — StringEnum `.Value`, records, Money/`AdditionalProperties` (steps 4-5).
- `paypal-platforms-team:dotnet-error-handling` — Case A/B ladders; **both** `JsonException` directions; 2xx-status-not-success (step 4). Always required.
- `paypal-platforms-team:dotnet-configuration-resilience` — retries/idempotency, timeouts, pagination, logging/redaction (steps 2,4,5).
- `paypal-platforms-team:dotnet-testing` — HttpClient stub seam matched to the repo's test stack (step 7).

The sheet deliberately does not carry these skills' contents.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` bound from `PayPal:` with `[Required]` on `ClientId,ClientSecret,Environment,Currency`; `AddOptions<PayPalOptions>().Bind().ValidateDataAnnotations().ValidateOnStart()` in `Program.cs`. Every part checked (blank ≠ missing). |
| 2 | Secret sourcing & rotation | From env → .NET user-secrets (`PayPal:*`). DI builds the options object **once at registration** and the SDK client is a singleton → a rotated secret needs a process restart (documented; acceptable for this app). |
| 3 | Total timeout budget | `RetryOptions.Timeout=30s` is **per attempt**. Each PayPal call is wrapped with a linked `CancellationTokenSource` off `HttpContext.RequestAborted` with a 60s call budget in the gateway; that token is the only whole-call bound. |
| 4 | Write-retry ownership | Writes are all `POST`/`DELETE` → SDK never resents them (default `HttpMethodsToRetry`). No `PUT` in scope. Reads (`GetAuthorizedPayment`, `SearchTransactions` GET) may retry — harmless. |
| 5 | Idempotency & ambiguous writes | authorize→`PayPal-Request-Id = "pay-{orderPaymentId}"`; capture→`"cap-{orderPaymentId}"`; void→`"void-{orderPaymentId}"`; vault→`"vault-{guid-per-attempt-persisted}"`; **refund→caller-supplied `Idempotency-Key` header value** carried as `PayPal-Request-Id` (real 45-day key). Repeated key ⇒ PayPal dedupes; locally the refund ledger + unique index dedupe. |
| 6 | Observability | `Information` request/response lines via host `ILoggerFactory` (URL path only, query allow-list); `LogRequestBody=false`. On gateway failure we log PayPal `Error.Name`+`debug_id` (correlation) at `Warning`/`Error`; never the card body. |
| 7 | Sensitive data | Scope carries PAN/CVV/expiry (`CardRequest`, `PaymentTokenRequestCard`). `LogRequestBody` stays **off** and `Logging.LoggerFactory` is **assigned explicitly** (`NullLoggerFactory` for the SDK's own logger; the SDK env-var cannot switch body logging on). App never logs a request body on these paths. PAN never persisted (only `last_digits`+`brand`+`expiry` from the response). |
| 8 | Environment selection | One server group `Default`, one environment `ServerEnvironment.Sandbox` (`https://api-m.sandbox.paypal.com`). All deployments select Sandbox; a different PayPal host is expressed via `PayPal:BaseUrl` (→ `Server.Default.Sandbox.BaseUrl`, token call included). No live env member exists in this SDK; test traffic is kept off live by construction (only Sandbox), and by requiring `BaseUrl` when `PayPal:Environment != sandbox`. |
| 9 | Duplicate prevention under concurrency | Store `OrderPayments`, column `OrderId`, **unique index** → the 2nd concurrent `pay` INSERT is rejected (`DbUpdateException` caught → return existing state). Refunds: store `PaymentRefunds`, columns `(OrderPaymentId, IdempotencyKey)`, **unique index**, rejection caught. (SQL Server enforces these; the in-memory provider used for local verification does not — that is an environment caveat, not the mechanism. Defence-in-depth: deterministic `PayPal-Request-Id` makes PayPal itself dedupe the money movement.) |
| 10 | Partial results | `reconciliation` pages every 31-day window to `total_pages`; the response DTO carries `Truncated:bool` + `WindowsCovered` so a capped result is visible to the caller, never only logged. A per-window page cap guards a non-advancing provider and sets `Truncated=true`. |
| 11 | Startup validation vs test host | `PublicApiIntegrationTests` boots `WebApplicationFactory<Program>` and loads `appsettings.test.json` → add non-secret **placeholder** `PayPal:*` there. `FunctionalTests` boots `WebApplicationFactory<AuthenticateEndpoint>` in env `Testing` → add placeholder `src/PublicApi/appsettings.Testing.json`. Both host-booting projects are run and must be green. |
| 12 | Ordering & no-op side effects | `OrderPayment` row is written (status `PendingPayment`/claim) **before** the PayPal call and completed after; provider response is never the first local write. Each transition (`pay`,`fulfil`,`cancel`,`refund`) is gated on the current `PaymentStatus` so a repeat is a no-op that fires no second PayPal call. |
| 13 | Unknown outcomes | On transport failure after a write may have been received: `pay` re-reads via `GetOrder`/authorization state before deciding; `fulfil` re-reads via `GetAuthorizedPayment`; refund re-reads via searching the refund ledger + `PayPal-Request-Id` replay. Gateway returns an "unknown/pending — reconcile" outcome, never a bare failure. |
| 14 | Provider status & reconciliation clock | Branch on `OrderStatus`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus` explicitly (no `?? COMPLETED`); `Pending`/`Denied`/`Declined` get a distinct path. Reconciliation filters **both** sides on the PayPal **transaction/event time**: PayPal via `start_date`/`end_date` (transaction_initiation_date), local via stored `AuthorizedAtUtc`/`CapturedAtUtc` (PayPal-reported `create_time`), never a local row-creation column. |

## 6. Assumptions & Blockers

- **Assumption (minor):** `POST /api/orders` needs a ship-to address (existing `Order` requires non-null
  `ShipToAddress`); the request accepts optional address fields and defaults a placeholder when omitted, so
  the flow is drivable with only item ids+quantities. Order total is computed from **catalog prices**, not
  client-supplied amounts.
- **Assumption (minor):** caller identity = JWT `ClaimTypes.Name` (username); `Order.BuyerId` and
  `SavedPaymentMethod.BuyerId`/`OrderPayment.BuyerId` are scoped to it. Operator role = existing
  `Administrators`.
- **No Blockers.** Every capability required (authorize hold, capture, void, refund incl. partial,
  reauthorize, vault card, delete vault card, transaction search) is present in the SDK map.

## 6b. Post-implementation notes (verified against the sandbox)

Findings from live self-verification, recorded here so the sheet stays the record of what was verified:

- **Saved-card vault is a two-step flow, not one call.** `CreatePaymentToken` with a raw `payment_source.card`
  returns HTTP 500 on the sandbox; the working path is `CreateSetupToken` (card) → `CreatePaymentToken`
  (`payment_source.token{ id, type=SETUP_TOKEN }`). The setup-token card additionally requires
  `verification_method` (`VaultCardVerificationMethod.ScaWhenRequired`) **and** an `experience_context`
  (`return_url`/`cancel_url`) — PayPal rejects the exchange with `MISSING_REQUIRED_PARAMETER @
  /payment_source/card/experience_context` otherwise. `ScaWhenRequired` does not challenge the sandbox test
  card (no browser round-trip); if a real card ever required SCA the flow surfaces it as an actionable error.
- **Corrected generated-SDK/spec drift — `LinkDescription.rel`/`href`.** The generated `Models/LinkDescription.cs`
  marked `rel` and `href` `required`, but PayPal's **Vault** payment-token success (200) response returns a
  HATEOAS link that omits `rel`, so the SDK threw `System.Text.Json.JsonException` while deserializing an
  otherwise-successful response and blocked the saved-card capability. Orders/Payments responses always
  include `rel` (used successfully), so only Vault was affected. Since the SDK is vendored, both were relaxed
  to nullable in `src/PayPalServerSdk/Models/LinkDescription.cs` (documented in-file) to match the observed
  API contract; the integration does not consume these links. This is a corrected generation defect, not a
  capability gap.
- **Void returns 204 No Content.** `VoidPayment` (prefer=minimal) succeeds with an empty body; the SDK's JSON
  reader throws on the empty body. A 2xx void is success by definition (it returns void), so the gateway
  treats that `JsonException` as success.
- **Idempotency key = persisted per-payment seed**, not the DB id. `OrderPayment.IdempotencySeed` (a GUID set
  at row creation, persisted) namespaces every `PayPal-Request-Id` (`pay-`/`cap-`/`void-`/`reauth-`, and
  `refund-{seed}-{callerKey}`). This is globally unique per payment across hosts/restarts in production, and
  avoids cross-run collisions on the shared sandbox that a monotonic-but-reused in-memory id would cause.
- **Fees require `prefer: return=representation`** on capture (confirmed: gross 36.50 / fee 1.44 / net 35.06).
- Row 3 refinement: the whole-call budget (a linked `CancellationToken` off `HttpContext.RequestAborted`,
  90 s) is enforced at the API endpoint boundary that fronts the gateway, not inside the gateway.

## 7. Source labels

Every §2 row cites its map page + declaring file. Live-only facts labelled inline (the card-create inline
status behaviour in §1 is `YOUR CALL`/defensive; confirmed at self-verify against the sandbox card).
