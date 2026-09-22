# PayPal Server SDK integration plan — eShopOnWeb PublicApi

Additive card-payments + saved-cards capability on `src/PublicApi`, backed by the PayPal
Server SDK (.NET), root namespace `PayPalServerSdk`. All SDK facts below come from the SDK map
(`sdk-map.md` + `map/operations/*`) and the map-named source files, read this session. The SDK
is vendored into `src/PayPalServerSdk/` (not on NuGet) and referenced by `Infrastructure` only.

---

## 1. Scope & sequence

| # | Step | PayPal operations used |
| --- | --- | --- |
| 1 | Vendor SDK source into repo; reference from Infrastructure | — |
| 2 | Domain: `OrderPayment` aggregate (+ `PaymentRefund` owned) and `PaymentMethod` aggregate in ApplicationCore; `IPaymentGateway` abstraction + domain DTOs | — |
| 3 | Infrastructure: `PayPalGateway : IPaymentGateway` using the SDK; `PayPalOptions` binding + fail-fast; DI extension; EF configs; client registration | all below |
| 4 | ApplicationCore: `PaymentService` + `PaymentMethodService` orchestrating repos + gateway, enforcing invariants | — |
| 5 | PublicApi endpoints (Flow 1 + Flow 2), currency/config, user-secrets | — |
| 6 | Pay → **`Orders.CreateOrder`** (intent AUTHORIZE + payment_source card/vault_id). VERIFIED on sandbox: a single-step card create **places the hold inline** — the authorization is on the CreateOrder response, so read it there; only call **`Orders.AuthorizeOrder`** as a fallback when no inline authorization is present (a bare `AuthorizeOrder` after an inline auth returns `ORDER_ALREADY_AUTHORIZED`). | CreateOrder (+ AuthorizeOrder fallback) |
| 7 | Fulfil → **`Payments.CaptureAuthorizedPayment`**; stale auth → **`Payments.ReauthorizePayment`** first | CaptureAuthorizedPayment, ReauthorizePayment |
| 8 | Cancel (pre-fulfil) → **`Payments.VoidPayment`** | VoidPayment |
| 9 | Refund (post-fulfil, full/partial, caller idempotency key) → **`Payments.RefundCapturedPayment`** | RefundCapturedPayment |
| 10 | Saved cards → **`Vault.CreatePaymentToken`** (save), our DB list (caller cards), **`Vault.DeletePaymentToken`** (remove) | CreatePaymentToken, DeletePaymentToken |
| 11 | Reconciliation → **`TransactionSearch.SearchTransactions`** over full page range | SearchTransactions |
| 12 | Unknown-outcome re-reads → GetOrder / GetAuthorizedPayment / GetCapturedPayment / GetRefund | (recovery only) |

No capability in scope is missing from the map. (See §6.)

---

## 2. CONTRACT SHEET

> ⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C#
> identifier; the cancellation-token parameter really is named `ct`, so named arguments write `ct:`.
> ⚠ **Every SDK type is fully-qualified by the namespace its source path implies** (records/unions →
> `PayPalServerSdk.Models`; enums → `PayPalServerSdk.Models.Enums`; errors → `PayPalServerSdk.Errors`;
> client/options → `PayPalServerSdk`; `ServerEnvironment` → `PayPalServerSdk.Servers`;
> `SdkException` → `PayPalServerSdk.Core.Exceptions`; `RawError` → `PayPalServerSdk.Core.ErrorResponse`;
> `OAuth2ClientCredentials` → `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`),
> taken from the path the map gives for THAT type.

### Client construction / auth / server (source: `sdk-map.md`, `AuthSchemes.cs`, `Servers/DefaultOptions.cs`)

- Construct: `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`.
- Auth: `options.Oauth2 = new OAuth2ClientCredentials { ClientId=…, ClientSecret=… }`. Tokens fetched by SDK from `{base}/v1/oauth2/token`.
- Environment: only `ServerEnvironment.Sandbox` exists (default). Base URL override point: `options.Server.Default.Sandbox.BaseUrl` (default `https://api-m.sandbox.paypal.com`).
- **VERIFIED (source `AuthSchemes.cs:17`)**: the token URL is `server.Default("/v1/oauth2/token")` — it resolves through the SAME `DefaultOptions.Resolve` → `Sandbox.BaseUrl`. ⇒ Setting `Server.Default.Sandbox.BaseUrl` retargets **every** call **including the token request**. This satisfies the `PayPal:BaseUrl` "verbatim for every call" mandate.
- DI helper `services.AddPayPalServerSdkClient(...)` exists but we register manually via `IHttpClientFactory` (see trap notes).

### Operations (map: `map/operations/{Orders,Payments,Vault,TransactionSearch}.md`)

| Op | Signature (verbatim) | Request → fields we set | Response → fields we read | Error case | source |
| --- | --- | --- | --- | --- | --- |
| `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `OrderRequest{ Intent=CheckoutPaymentIntent.Authorize, PurchaseUnits=[PurchaseUnitRequest{ Amount=AmountWithBreakdown{CurrencyCode,Value}, InvoiceId, CustomId, Description }], PaymentSource=PaymentSource{ Card=CardRequest{…raw…} OR CardRequest{VaultId=token} } }`; pass `payPalRequestId` = stable key (mandatory for card single-step) | `Order.Id`, `Order.Status` (`OrderStatus`) | A: `TryGetError(out Error)`[400,401,422] | Orders.md; `Models/OrderRequest.cs`, `Models/PurchaseUnitRequest.cs`, `Models/AmountWithBreakdown.cs`, `Models/PaymentSource.cs`, `Models/CardRequest.cs`, `Models/Order.cs` |
| `client.Orders.AuthorizeOrder` (FALLBACK only — see step 6) | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", …)` | `body=null`; `prefer="return=representation"` (need nested auth id); `payPalRequestId`=stable | `OrderAuthorizeResponse.Status`, `.PurchaseUnits[].Payments.Authorizations[].Id` + `.Status` (`AuthorizationStatus`) | A: `TryGetError(out Error)`[400,401,403,404,422,500] | Orders.md; `Models/OrderAuthorizeResponse.cs`, `Models/PurchaseUnit.cs`, `Models/PaymentCollection.cs`, `Models/AuthorizationWithAdditionalData.cs` |
| Note (VERIFIED on sandbox) | `Orders.CreateOrder` with a card + intent=AUTHORIZE returns the authorization inline at `Order.PurchaseUnits[].Payments.Authorizations[].Id`; `PayPalGateway.AuthorizeAsync` reads it there and skips `AuthorizeOrder`. | — | `Order.PurchaseUnits[].Payments.Authorizations[]` | — | `Models/Order.cs`, `Models/PurchaseUnit.cs`, `Models/PaymentCollection.cs` |
| `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", …)` | `body=CaptureRequest{ FinalCapture=true }`; `prefer="return=representation"` (need fee/net); `payPalRequestId`=stable | `CapturedPayment.Id`, `.Status` (`CaptureStatus`), `.Amount`(Money), `.SellerReceivableBreakdown{GrossAmount,PaypalFee,NetAmount}` | A: `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] | Payments.md; `Models/CaptureRequest.cs`, `Models/CapturedPayment.cs`, `Models/SellerReceivableBreakdown.cs`, `Models/Money.cs` |
| `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", …)` | `body=ReauthorizeRequest{ Amount=Money{Currency,Value} }`; `payPalRequestId`=stable | `PaymentAuthorization.Id` (NEW auth id), `.Status` | A: `TryGetError`[400,401,403,404,422] · `TryGetNoContent`[500] | Payments.md; `Models/ReauthorizeRequest.cs`, `Models/PaymentAuthorization.cs` |
| `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", …)` | `payPalRequestId`=stable | `PaymentAuthorization.Status` (VOIDED) | A: `TryGetError`[401,403,404,409,422] · `TryGetNoContent`[500] | Payments.md; `Models/PaymentAuthorization.cs` |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", …)` | `body=RefundRequest{ Amount=Money{…} }` for partial; `body=null`/no-amount for full-remaining; `payPalRequestId`=**caller idempotency key** | `Refund.Id`, `.Status` (`RefundStatus`), `.Amount`(Money) | A: `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent`[500] | Payments.md; `Models/RefundRequest.cs`, `Models/Refund.cs` |
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, …)` | `PaymentTokenRequest{ Customer=Customer{Id=<paypal customer id>}?, PaymentSource=PaymentTokenRequestPaymentSource{ Card=PaymentTokenRequestCard{ Name,Number,Expiry,SecurityCode,BillingAddress } } }`; `payPalRequestId`=stable | `PaymentTokenResponse.Id` (vault token), `.Customer`(CustomerResponse).Id, `.PaymentSource.Card`(CardPaymentTokenEntity){`LastDigits`,`Brand`,`Expiry`,`Name`} | A: `TryGetError`[400,403,404,422,500] | Vault.md; `Models/PaymentTokenRequest.cs`, `Models/PaymentTokenRequestPaymentSource.cs`, `Models/PaymentTokenRequestCard.cs`, `Models/PaymentTokenResponse.cs`, `Models/PaymentTokenResponsePaymentSource.cs`, `Models/CardPaymentTokenEntity.cs`, `Models/Customer.cs` |
| `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions?=null, CancellationToken ct=default)` | — | void | A: `TryGetError`[400,403,500] | Vault.md |
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, …)` | `startDate`,`endDate` (ISO-8601), all 8 middle `null`, `fields="transaction_info"`, iterate `page` 1..`TotalPages` | `SearchResponse.TransactionDetails[].TransactionInfo` (`TransactionInformation`){`TransactionId`,`InvoiceId`,`CustomField`,`TransactionAmount`(Money),`FeeAmount`,`TransactionStatus`,`TransactionInitiationDate`}, `.TotalPages`, `.Page` | **B: `SdkException<RawError>`** (no typed accessors) | TransactionSearch.md; `Models/SearchResponse.cs`, `Models/TransactionDetails.cs`, `Models/TransactionInformation.cs` |

Recovery re-reads (unknown outcomes): `Orders.GetOrder(id, fields:null, payPalMockResponse:null, payPalAuthAssertion:null)`, `Payments.GetAuthorizedPayment(authorizationId, payPalMockResponse:null, payPalAuthAssertion:null)`, `Payments.GetCapturedPayment(captureId, payPalMockResponse:null)`, `Payments.GetRefund(refundId, payPalMockResponse:null, payPalAuthAssertion:null)`. All Case A.

### Enums (source: `Models/Enums/*.cs`) — build with static members; JSON wire in parens

- `CheckoutPaymentIntent.Authorize` (`AUTHORIZE`), `.Capture` (`CAPTURE`).
- `OrderStatus`: `Created`,`Saved`,`Approved`,`Voided`,`Completed`,`PayerActionRequired` (`PAYER_ACTION_REQUIRED`).
- `AuthorizationStatus`: `Created`,`Captured`,`Denied`,`PartiallyCaptured`,`Voided`,`Pending`.
- `CaptureStatus`: `Completed`,`Declined`,`PartiallyRefunded`,`Pending`,`Refunded`,`Failed`.
- `RefundStatus`: `Cancelled`,`Failed`,`Pending`,`Completed`.
- Read via `enumValue == OrderStatus.PayerActionRequired` etc. (StringEnum members).

### Optional-field purpose notes (create/update bodies)

| field | purpose | set? |
| --- | --- | --- |
| `OrderRequest.Intent` (required) | AUTHORIZE = hold-then-capture; task wants hold at pay, capture at fulfil | **AUTHORIZE** |
| `PurchaseUnitRequest.InvoiceId` | correlation handle reconciliation joins on (`eshop-{orderId}`) | **set** — cross-op invariant |
| `PurchaseUnitRequest.CustomId` | secondary correlation echo | set (`eshop-{orderId}`) |
| `PurchaseUnitRequest.Description` | human label; omit → provider default (none) | set (order label) |
| `CardRequest.VaultId` | pay with a saved card (mutually exclusive with raw number) | set only for saved-card path |
| `CardRequest.SecurityCode` | CVC for raw card; omit → provider may decline / SCA | set for raw path |
| `CaptureRequest.FinalCapture` | true = release remaining auth; single full capture at fulfil | **true** |
| `ReauthorizeRequest.Amount` | omit → provider default (original auth amount); we set = order total to be explicit | set |
| `RefundRequest.Amount` | **omit → provider default = full remaining**; set = partial | set only for partial |
| `PaymentTokenRequest.Customer.Id` | groups a shopper's vaulted cards under one PayPal customer id; omit → PayPal mints a new customer per card | set to our stored per-shopper customer id when known, else omit on first save and capture the minted id |
| `Token` (payment_source.token) | NOT used — `TokenType` only declares `BILLING_AGREEMENT`, not vault tokens; saved-card pay uses `CardRequest.VaultId` instead | omitted |

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A saved-card id used to pay must be one the caller saved (maps to a real vault token) | `Orders.CreateOrder(card.VaultId)` ← `Vault.CreatePaymentToken` result stored in `PaymentMethod` | `PaymentService.PayAsync` — resolve `PaymentMethod` by (id, buyerId); reject otherwise |
| `authorizationId` captured/voided/reauthorized must be the one AuthorizeOrder returned for THIS order | `Payments.Capture/Void/Reauthorize` ← `Orders.AuthorizeOrder` | stored on `OrderPayment.AuthorizationId`; ops read it, never caller input |
| `captureId` refunded must be the one CaptureAuthorizedPayment returned for THIS order | `Payments.RefundCapturedPayment` ← `Payments.CaptureAuthorizedPayment` | stored on `OrderPayment.CaptureId` |
| reconciliation match key: PayPal `transaction_info.invoice_id` == `eshop-{orderId}` | `TransactionSearch.SearchTransactions` ↔ `Orders.CreateOrder(invoice_id)` | `ReconciliationService` join |
| a partial refund's cumulative total must not exceed captured gross | `Payments.RefundCapturedPayment` ← capture gross | `PaymentService.RefundAsync` pre-check |

---

## 3. Trap notes (hazard + skill pointer — not resolved here)

- **DI + HttpClient lifetime**: the SDK wraps one long-lived `HttpClient`; rebuilding per request or mis-scoping the client leaks sockets / breaks token cache. **MUST load dotnet-client-initialization.**
- **Credential wiring & where OAuth is set**: getting the credential object onto options at registration vs per-call, and what a 401 actually means (credential never sent vs bad). **MUST load dotnet-authentication.**
- **Named-argument binding on list/search ops**: `SearchTransactions`/`ListCustomerPaymentTokens` have many no-C#-default optional params that mis-bind positionally; and the injected `Idempotency-Key` GUID header is not a real key — the real key is `payPalRequestId`. **MUST load dotnet-calling-endpoints.**
- **Model building**: enums are `StringEnum<T>` not C# enums; `AmountWithBreakdown`/`Money` require exact 2-dp string `Value`; response unknown fields land in `AdditionalProperties`. **MUST load dotnet-models.**
- **Error boundary — two JsonException directions**: a drifted 2xx body throws `JsonException` from deserialization (NOT `SdkException`); a non-2xx body that doesn't match `{Operation}Error` throws `JsonException` while building the error, destroying the status. Case A vs B differs per op (`SearchTransactions` is B). **MUST load dotnet-error-handling.**
- **Retry/timeout/logging/pagination**: `Timeout` is per-attempt not total; `HttpMethodsToRetry` default excludes POST (our writes) so they aren't auto-resent; `LogRequestBody` logs card JSON unredacted; `SearchTransactions`/vault list pagination is manual. **MUST load dotnet-configuration-resilience.**
- **Testing seam**: the `HttpClient` ctor arg is the fake seam; match MSTest + the project's style. **MUST load dotnet-testing.**

---

## 4. REQUIRED READING (load all before implementing; contents deliberately not copied here)

- `paypal-platforms-team:dotnet-client-initialization` — client construction + DI/HttpClient registration (§1, step 3).
- `paypal-platforms-team:dotnet-authentication` — OAuth2 client-credentials wiring (§1, step 3).
- `paypal-platforms-team:dotnet-calling-endpoints` — first calls to each op; named args; real idempotency key (steps 6–11).
- `paypal-platforms-team:dotnet-models` — building `OrderRequest`/`Money`/card bodies; reading enums/breakdown (steps 6–11).
- `paypal-platforms-team:dotnet-error-handling` — try/catch boundary, Case A/B, JsonException traps (all writes).
- `paypal-platforms-team:dotnet-configuration-resilience` — retries/timeouts/logging/pagination (steps 3, 11).
- `paypal-platforms-team:dotnet-testing` — integration-layer tests (step verification).

These are API-agnostic usage skills; contract facts still come only from this sheet or a map lookup.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` bound from `PayPal:` section; `IValidateOptions<PayPalOptions>` + `.ValidateOnStart()` in the PayPal DI extension refuses host start if `ClientId`, `ClientSecret`, `Environment`, or `Currency` is null/blank (each part checked separately — a blank part ≠ missing). |
| 2 | Secret sourcing & rotation | Secrets from **.NET user-secrets** (`PayPal:ClientId/ClientSecret/...`), loaded into config by `WebApplication.CreateBuilder` in Development (PublicApi has a `UserSecretsId`). Options snapshot captured once at registration into the singleton gateway (via `IHttpClientFactory` typed client) → **rotation requires process restart**; acceptable for this app (documented). |
| 3 | Total timeout budget | SDK `Timeout` is per-attempt. Each gateway call is bounded by a `CancellationTokenSource` linked to the request `ct` with a total deadline (`PayPal:TimeoutSeconds`, default 100s) → caller-visible whole-call bound regardless of retries. |
| 4 | Write-retry ownership | All PayPal writes in scope are **POST** ⇒ SDK default `HttpMethodsToRetry` never resends them. Safe resend is instead achieved by our deterministic `payPalRequestId` per (order,op) — a replay returns the original result. No PUT in scope. |
| 5 | Idempotency & ambiguous writes | CreateOrder/AuthorizeOrder/Capture/Void/Reauthorize: deterministic `payPalRequestId` = `{OrderPayment.IdempotencyKey}-{op}` (`IdempotencyKey` = per-order GUID minted at placement, unique across runs). Refund: **caller-supplied idempotency key** passed as `payPalRequestId`; PayPal dedups replays, distinct keys = distinct partial refunds. Vault save: `{PaymentMethod.IdempotencyKey}-vault`. The generator `Idempotency-Key` GUID header is ignored (not a key). |
| 6 | Observability | Info logs on each transition with `orderId`, PayPal ids (order/auth/capture/refund) and status; PayPal error `debug_id`/`name` (from `Error`/`RawError` body) logged at Warning/Error. `LogRequestBody` stays **off**. |
| 7 | Sensitive data | Card number/CVC flow through `CardRequest`/`PaymentTokenRequestCard` (raw). ⇒ `LogRequestBody` off **and** `options.Logging.LoggerFactory` set explicitly (blocks `PAYPALSERVERSDKCLIENT_LOG` from arming body logging). Our code never logs card fields; only PayPal-returned `last_digits`/`brand`/`expiry` are stored/returned. Full PAN/CVC never persisted, never logged. |
| 8 | Environment selection | SDK declares only `ServerEnvironment.Sandbox` (1 group `Default`). We always set `Environment=Sandbox` and, when `PayPal:BaseUrl` is non-blank, set `Server.Default.Sandbox.BaseUrl` to it verbatim (governs token + all calls — verified). Non-sandbox targeting is therefore expressed by `PayPal:BaseUrl`, not an env member; documented. Test traffic stays on sandbox because that is the only environment and the base URL default is the sandbox host. |
| 9 | Duplicate prevention under concurrency | The money-moving writes are outbound to PayPal; the claim store is **PayPal's idempotency store**, keyed by the `payPalRequestId` column (deterministic per (order,op) / caller key for refunds). PayPal rejects a replay by returning the original result — no second hold/capture/refund. This holds across hosts and restarts (survives the in-memory-DB reset caveat). Local `PaymentRefund` additionally carries `IdempotencyKey` with a unique index `(OrderPaymentId, IdempotencyKey)`; the repeat is caught by looking up that row and returning it. (In-process locks / existence-only checks are NOT relied on for money safety — PayPal is the authority.) |
| 10 | Partial results | `SearchTransactions` and `ListCustomerPaymentTokens` are paged. Reconciliation loops `page` 1..`TotalPages` (from the response) → whole range covered. The reconciliation response DTO carries `pagesRead`/`totalPages` and a `truncated` bool so a caller learns from the **return value** (not a log) if a hard page cap was ever hit. |
| 11 | Startup validation vs test host | `PublicApiIntegrationTests` boots the real `Program` via `WebApplicationFactory<Program>`. Its `appsettings.test.json` gets **placeholder** `PayPal:` values (dummy non-secret strings) so `.ValidateOnStart()` passes and the host boots; those tests never call PayPal. Ran and green (see verification). |
| 12 | Ordering & no-op side effects | `OrderPayment` row (with `InvoiceId`, `IdempotencyKey`, state `AwaitingPayment`) is written at `POST /api/orders` **before** any PayPal call; PayPal ids/status persisted **after** each call returns. Idempotent transitions are gated on current `PaymentState` — a second `pay`/`fulfil`/`cancel` on an already-transitioned order performs no new outbound effect and returns the existing state. |
| 13 | Unknown outcomes | On transport failure after send, the deterministic `payPalRequestId` makes a same-key resend return the original result; additionally the gateway exposes re-read helpers (`GetOrder`/`GetAuthorizedPayment`/`GetCapturedPayment`/`GetRefund`) keyed by the stored PayPal id (the same reference the duplicate-claim row uses). The catch block re-reads rather than declaring definite failure. |
| 14 | Provider status | Every write returns a status field (`OrderStatus`/`AuthorizationStatus`/`CaptureStatus`/`RefundStatus`). Each is branched: `PayerActionRequired` on order/`Pending` needing approval ⇒ STOP + operator-actionable error (browser challenge — task says report, not build round-trip); `Denied`/`Declined`/`Failed` ⇒ payment failure surfaced; `Voided` ⇒ cancelled; `Completed`/`Captured` ⇒ success. No `status ?? "COMPLETED"` defaulting; an absent/unreadable status is treated as pending, not success. Reconciliation clock: both sides filter on the PayPal **transaction initiation date** within the `[from,to]` range (not a local row-creation column). |

### DUPLICATE CLAIMS

| write | where the claim is stored | what rejects the second one | where that rejection is caught | where in the code |
| --- | --- | --- | --- | --- |
| pay/authorize | PayPal idempotency store, key = `payPalRequestId` `{IdempotencyKey}-create`/`-authorize` | PayPal returns original order/auth on replay | gateway returns same ids; `PaymentService` also gates on `PaymentState != AwaitingPayment` | `PayPalGateway.AuthorizeAsync`, `PaymentService.PayAsync` |
| fulfil/capture | PayPal store, key = `{IdempotencyKey}-capture` | PayPal returns original capture on replay | `PaymentService.FulfilAsync` gates on `PaymentState != Authorized` | `PayPalGateway.CaptureAsync`, `PaymentService.FulfilAsync` |
| cancel/void | PayPal store, key = `{IdempotencyKey}-void` | PayPal returns/repeats void | `PaymentService.CancelAsync` gates on state | `PayPalGateway.VoidAsync`, `PaymentService.CancelAsync` |
| refund | PayPal store, key = caller idempotency key; local unique index `(OrderPaymentId, IdempotencyKey)` on `PaymentRefund` | PayPal replay returns original refund; local lookup returns stored refund | `PaymentService.RefundAsync` | `PaymentService.RefundAsync`, `PaymentRefundConfiguration` |
| save card | PayPal store, key = `{IdempotencyKey}-vault` | PayPal replay returns original token | `PaymentMethodService.SaveAsync` | `PayPalGateway.VaultCardAsync` |

### PAGED READS

| read | what caps it | how the caller learns it was cut short | where in the code |
| --- | --- | --- | --- |
| reconciliation `SearchTransactions` | loop to `TotalPages`; hard safety cap `PayPal:MaxReconciliationPages` (default 200) | `ReconciliationResponse.Truncated` bool + `PagesRead`/`TotalPages` fields | `ReconciliationService.BuildAsync`, `ReconciliationEndpoint` |
| saved cards list | our own DB (`PaymentMethod` by BuyerId) — not PayPal-paged | N/A (returns all rows) | `PaymentMethodService.ListAsync` |

### REPEATED OPERATIONS

| operation | what tells you the state actually changed | the effects gated on that | where in the code |
| --- | --- | --- | --- |
| pay | `PaymentState` was `AwaitingPayment` before | the PayPal Create+Authorize calls + state→`Authorized` | `PaymentService.PayAsync` |
| fulfil | `PaymentState` was `Authorized` | the Capture call + state→`Fulfilled` | `PaymentService.FulfilAsync` |
| cancel | `PaymentState` was `Authorized`/`AwaitingPayment` | the Void call + state→`Cancelled` | `PaymentService.CancelAsync` |
| delete saved card | row existed for (id,buyerId) | the `DeletePaymentToken` call + row removal | `PaymentMethodService.DeleteAsync` |

### UNKNOWN OUTCOMES

| write | the operation you re-read with | the reference you search by | where in the code |
| --- | --- | --- | --- |
| create/authorize | `Orders.GetOrder` / `Payments.GetAuthorizedPayment` | `OrderPayment.PayPalOrderId` / `.AuthorizationId` | `PaymentService.PayAsync` catch |
| capture | `Payments.GetCapturedPayment` | `OrderPayment.CaptureId` (else re-read auth) | `PaymentService.FulfilAsync` catch |
| refund | `Payments.GetRefund` | last `PaymentRefund.PayPalRefundId` for the idempotency key | `PaymentService.RefundAsync` catch |
| void | `Payments.GetAuthorizedPayment` | `OrderPayment.AuthorizationId` | `PaymentService.CancelAsync` catch |

### OPERATION OUTCOMES

| write | the status field | every value it can hold, and what the app does with each | where in the code |
| --- | --- | --- | --- |
| CreateOrder | `Order.Status` | `Created`/`Saved`/`Approved`→proceed to authorize; `PayerActionRequired`→STOP+report browser challenge; `Voided`/other→payment failure | `PayPalGateway.AuthorizeAsync` |
| AuthorizeOrder | `AuthorizationStatus` | `Created`/`Pending`→hold placed (Pending surfaced as processing); `Denied`→declined; `Captured`/`PartiallyCaptured`/`Voided`→unexpected, surfaced | `PayPalGateway.AuthorizeAsync` |
| Capture | `CaptureStatus` | `Completed`→fulfilled+record fee/net; `Pending`→awaiting settlement (surfaced, not success); `Declined`/`Failed`→capture failure; `Refunded`/`PartiallyRefunded`→unexpected here | `PayPalGateway.CaptureAsync`, `PaymentService.FulfilAsync` |
| Reauthorize | `AuthorizationStatus` | `Created`→new hold usable; `Denied`→cannot renew, operator-actionable error; else surfaced | `PayPalGateway.ReauthorizeAsync` |
| Void | `AuthorizationStatus` | `Voided`→cancelled; else surfaced as failure | `PayPalGateway.VoidAsync` |
| Refund | `RefundStatus` | `Completed`→refund recorded; `Pending`→recorded as pending; `Cancelled`/`Failed`→refund failure | `PayPalGateway.RefundAsync`, `PaymentService.RefundAsync` |

### WRITE ORDER

| write | what exists locally BEFORE the call | what is written after it returns | where in the code |
| --- | --- | --- | --- |
| pay | `OrderPayment{ OrderId, BuyerId, InvoiceId=eshop-{id}, IdempotencyKey, State=AwaitingPayment }` (created at POST /api/orders) | `PayPalOrderId`, `AuthorizationId`, `AuthorizationStatus`, `State=Authorized` | `OrderPlacementService.PlaceAsync` then `PaymentService.PayAsync` |
| fulfil | existing `OrderPayment` with `AuthorizationId`, `State=Authorized` | `CaptureId`, `CaptureStatus`, `GrossAmount`, `PaypalFee`, `NetAmount`, `State=Fulfilled` | `PaymentService.FulfilAsync` |
| cancel | existing `OrderPayment` (`Authorized`) | `AuthorizationStatus=Voided`, `State=Cancelled` | `PaymentService.CancelAsync` |
| refund | existing `OrderPayment` (`Fulfilled`) + new `PaymentRefund{ IdempotencyKey, Amount, State=Pending }` written before call | `PaymentRefund.PayPalRefundId`, `.Status`; roll up to `State=Refunded/PartiallyRefunded` | `PaymentService.RefundAsync` |
| save card | `PaymentMethod{ BuyerId, IdempotencyKey }` (created before call) | `PayPalVaultTokenId`, `Brand`, `LastDigits`, `Expiry` | `PaymentMethodService.SaveAsync` |

---

## 6. Assumptions & Blockers

- **Assumption**: caller identity for scoping = JWT `ClaimTypes.Name` (username), used as `Order.BuyerId` / `PaymentMethod.BuyerId` — matches how the app models buyers. Minor.
- **Assumption**: shipping address for the reused `Order` is taken from the request (or a default) since the API places an order directly from catalog ids; the existing `Order` requires a `ShipToAddress`. Minor.
- **Assumption**: fulfil/cancel/reconciliation are admin-role endpoints (task says operator = the app's administrator role). Minor.
- **Blockers**: none — every required capability maps to an operation above. A 3DS/browser challenge (`PAYER_ACTION_REQUIRED`) is handled per the task by STOP-and-report, not by building an approval round-trip.

## 7. Source labels
Every row's source cell cites its map page or the map-named declaring file, read this session.
`YOUR CALL — not in the map` items (persistence shape, DI lifetime specifics, endpoint contracts,
buyer-id scoping) are decided against the eShop conventions at implementation time.
