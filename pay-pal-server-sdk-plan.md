# PayPal Server SDK integration plan — eShopOnWeb PublicApi

Adds PayPal card payments (authorize → capture at fulfil → void/refund) and vaulted saved
cards to `src/PublicApi`. Additive to the existing catalog/basket/order flow. All SDK facts
below come from the SDK map (`sdk-map.md` + `map/operations/*`) and the map-named source files,
read this session. Contract facts are authoritative; do not re-derive from memory.

---

## 1. Scope & sequence

1. **Vendor the SDK** — not on NuGet, so copy the SDK source into `src/PayPalServerSdk/`
   (netstandard2.0 class lib, `ManagePackageVersionsCentrally=false` to opt out of this repo's
   CPM) and `ProjectReference` it from `PublicApi`.
2. **Config + client + fail-fast** — bind `PayPal:*`, register the singleton `PayPalServerSdkClient`
   via `AddPayPalServerSdkClient`, fail startup on missing credential. Ops: token via `Oauth2`.
3. **Domain** (ApplicationCore) — `OrderPayment` (aggregate root, one per Order), `PaymentRefund`
   (child, idempotency claim), `SavedCard` (aggregate root). Reuse existing `Order`/`OrderItem`.
4. **Gateway** (Infrastructure) — `PayPalGateway` wraps every SDK call + the error boundary.
   Ops: `Orders.CreateOrder`, `Orders.AuthorizeOrder`, `Payments.CaptureAuthorizedPayment`,
   `Payments.ReauthorizePayment`, `Payments.VoidPayment`, `Payments.RefundCapturedPayment`,
   `Payments.GetAuthorizedPayment`, `Payments.GetCapturedPayment`, `Vault.CreateSetupToken` +
   `Vault.CreatePaymentToken` (two-step card vault), `Vault.DeletePaymentToken`,
   `TransactionSearch.SearchTransactions`.
5. **Services** (Infrastructure) — `PaymentService` (place/pay/fulfil/cancel/refund/my-orders/
   reconcile), `SavedCardService` (save/list/delete).
6. **Endpoints** (PublicApi, MinimalApi.Endpoint pattern) — the 10 routes; register services.
7. **Tests + self-verify** — keep existing host-booting test green (placeholder PayPal config),
   add integration tests against a faked gateway, then live sandbox card run.

A capability the map lacks is a Blocker (§6), never an invented path. §6 is currently empty.

---

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim** — every parameter name is the literal C#
> identifier; the cancellation-token parameter is named `ct`, so named args write `ct:`.
> ⚠ Every SDK type is written **fully-qualified with the namespace its source path implies**,
> taken from the path the map gives for THAT type. Records/enums/errors live in
> `PayPalServerSdk.Models` / `PayPalServerSdk.Models.Enums` / `PayPalServerSdk.Errors`; client &
> options in `PayPalServerSdk`; `ServerEnvironment` in `PayPalServerSdk.Servers`; `OAuth2ClientCredentials`
> in `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`; `SdkException<T>` in
> `PayPalServerSdk.Core.Exceptions`; `RawError` in `PayPalServerSdk.Core.ErrorResponse`.

### Operations

| op | controller · signature | request → fields used | response → fields read | error case | source |
| --- | --- | --- | --- | --- | --- |
| Create order | `client.Orders.CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? = null, CancellationToken ct = default)` | `OrderRequest{ Intent (req), PurchaseUnits (req), PaymentSource }`; `PurchaseUnitRequest{ Amount (req)=AmountWithBreakdown{CurrencyCode,Value}, CustomId, InvoiceId }`; `PaymentSource{ Card=CardRequest }`; `CardRequest{ Number, Expiry("YYYY-MM"), SecurityCode, Name, BillingAddress, VaultId }` | `Order{ Id, Status(OrderStatus?), PurchaseUnits[].Payments.Authorizations[] }` | A `SdkException<CreateOrderError>` · `TryGetError(out Error)`[400,401,422] · `TryGetRawError` | map/operations/Orders.md; Models/OrderRequest.cs, PurchaseUnitRequest.cs, AmountWithBreakdown.cs, PaymentSource.cs, CardRequest.cs, Order.cs |
| Authorize order | `client.Orders.AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? = null, CancellationToken ct = default)` | `id`=PayPal order id; `body` may be `null` (payment_source already supplied at create) | `OrderAuthorizeResponse{ Id, Status(OrderStatus?), PurchaseUnits[].Payments.Authorizations[] → AuthorizationWithAdditionalData{ Id, Status(AuthorizationStatus?), ExpirationTime } }` | A `SdkException<AuthorizeOrderError>` · `TryGetError(out Error)`[400,401,403,404,422,500] · `TryGetRawError` | Orders.md; Models/OrderAuthorizeResponse.cs, PurchaseUnit.cs, PaymentCollection.cs |
| Capture (fulfil) | `client.Payments.CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? = null, CancellationToken ct = default)` | `authorizationId`; `CaptureRequest{ Amount?, FinalCapture=true, InvoiceId? }` — **omit Amount → full authorized amount captured (provider default)** | `CapturedPayment{ Id, Status(CaptureStatus?), Amount=Money, SellerReceivableBreakdown{ GrossAmount(req), PaypalFee?, NetAmount? } }` | A `SdkException<CaptureAuthorizedPaymentError>` · `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; Models/CaptureRequest.cs, CapturedPayment.cs, SellerReceivableBreakdown.cs |
| Reauthorize | `client.Payments.ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? = null, CancellationToken ct = default)` | `authorizationId`; `ReauthorizeRequest{ Amount? }` — **omit Amount → provider default (re-auth original amount)** | `PaymentAuthorization{ Id, Status(AuthorizationStatus?), ExpirationTime }` | A `SdkException<ReauthorizePaymentError>` · `TryGetError`[400,401,403,404,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; Models/ReauthorizeRequest.cs, PaymentAuthorization.cs |
| Void (cancel) | `client.Payments.VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? = null, CancellationToken ct = default)` | `authorizationId` | `PaymentAuthorization{ Status }` | A `SdkException<VoidPaymentError>` · `TryGetError`[401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; PaymentAuthorization.cs |
| Refund | `client.Payments.RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? = null, CancellationToken ct = default)` | `captureId`; `RefundRequest{ Amount?, CustomId?, InvoiceId? }` — **omit Amount → full refund (provider default); set Amount → partial**; `payPalRequestId` = caller idempotency key | `Refund{ Id, Status(RefundStatus?), Amount=Money }` | A `SdkException<RefundCapturedPaymentError>` · `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; Models/RefundRequest.cs, Refund.cs |
| Get authorization | `client.Payments.GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? = null, CancellationToken ct = default)` | `authorizationId` | `PaymentAuthorization{ Status, ExpirationTime }` | A `SdkException<GetAuthorizedPaymentError>` · `TryGetError`[401,403,404] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; PaymentAuthorization.cs |
| Get capture | `client.Payments.GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? = null, CancellationToken ct = default)` | `captureId` | `CapturedPayment{ Status, SellerReceivableBreakdown }` | A `SdkException<GetCapturedPaymentError>` · `TryGetError`[401,403,404] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; CapturedPayment.cs |
| Vault card | `client.Vault.CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? = null, CancellationToken ct = default)` | `PaymentTokenRequest{ Customer?=Customer{ MerchantCustomerId }, PaymentSource(req)=PaymentTokenRequestPaymentSource{ Card=PaymentTokenRequestCard{ Number, Expiry, SecurityCode, Name, BillingAddress } } }` | `PaymentTokenResponse{ Id, Customer?=CustomerResponse{Id}, PaymentSource?.Card=CardPaymentTokenEntity{ LastDigits, Brand(CardBrand?), Expiry, Name } }` | A `SdkException<CreatePaymentTokenError>` · `TryGetError`[400,403,404,422,500] · `TryGetRawError` | Vault.md; Models/PaymentTokenRequest.cs, PaymentTokenRequestPaymentSource.cs, PaymentTokenRequestCard.cs, PaymentTokenResponse.cs, PaymentTokenResponsePaymentSource.cs, CardPaymentTokenEntity.cs, Customer.cs |
| Delete vault token | `client.Vault.DeletePaymentToken(string id, RequestOptions? = null, CancellationToken ct = default)` | `id`=vault token id | `void` | A `SdkException<DeletePaymentTokenError>` · `TryGetError`[400,403,500] · `TryGetRawError` | Vault.md |
| Search transactions | `client.TransactionSearch.SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? = null, CancellationToken ct = default)` | `startDate`/`endDate` ISO-8601; pass the 8 nullable filters as `null` (named args); loop `page` 1..`TotalPages` | `SearchResponse{ TransactionDetails[]→TransactionInformation{ TransactionId, InvoiceId, TransactionAmount=Money, TransactionStatus, TransactionInitiationDate }, Page, TotalPages, TotalItems }` | **B** `SdkException<RawError>` (StatusCode, ReadAsString, ReadAsJson<T>) | TransactionSearch.md; Models/SearchResponse.cs, TransactionDetails.cs, TransactionInformation.cs |

### Enums needed (Models/Enums/*, `PayPalServerSdk.Models.Enums`)

- `CheckoutPaymentIntent`: `.Authorize`("AUTHORIZE"), `.Capture`("CAPTURE"). Use `.Authorize`.
- `OrderStatus`: Created, Saved, **Approved**, Voided, **Completed**, **PayerActionRequired**("PAYER_ACTION_REQUIRED"). PayerActionRequired = 3DS/browser challenge → **STOP & report** (task mandate), never build approval round-trip.
- `AuthorizationStatus`: Created, Captured, Denied, PartiallyCaptured, Voided, Pending. Fulfil-ready = `Created`; `Denied`/`Voided`/`Pending` = not ready.
- `CaptureStatus`: **Completed** (done), Declined (failed), Pending (not-yet), Refunded/PartiallyRefunded, Failed (failed).
- `RefundStatus`: **Completed** (done), Pending (not-yet), Cancelled/Failed (failed).
- `CardBrand` (response only — display). Read `.Value` for the string.

### Client / auth / server (source: sdk-map.md *Getting a client* + *Servers & auth*; ServiceCollectionExtensions.cs; DefaultOptions.cs; AuthSchemes.cs)

- Register: `services.AddPayPalServerSdkClient(o => { o.Oauth2 = new OAuth2ClientCredentials{ ClientId=…, ClientSecret=… }; o.Environment = ServerEnvironment.Sandbox; /* base-url override below */ });`
  Builds options **once at registration**, captured in the singleton (rotation ⇒ restart — PROD row 2).
- **Base-URL override**: `o.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>`. Verified: the OAuth token URL is `server.Default("/v1/oauth2/token")` (AuthSchemes.cs:17) which resolves through `DefaultOptions.Sandbox.BaseUrl` — so overriding it covers **every** call *including the token request*, exactly as the task requires. Only `ServerEnvironment.Sandbox` exists.
- Only Case-B op is `SearchTransactions`; all others Case-A typed `TryGetError(out Error)`.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| `catalogItemId`s in a place-order request must be catalog items that exist | `CreateOrder(local Order)` ← catalog `IReadRepository<CatalogItem>` | implementation — load each id, 400 if any missing |
| pay names one of the caller's saved cards → must be a `SavedCard` the caller owns (`vault_id` accepted by CreateOrder must be one CreatePaymentToken returned & stored) | `Orders.CreateOrder(card.VaultId)` ← `Vault.CreatePaymentToken` (SavedCards store) | implementation — resolve SavedCard by (id, buyerId); 404 otherwise |
| refund/fulfil/cancel act on an order → must be the caller's own order (refund) / any order (admin fulfil-cancel) | payment ops ← `OrderPayment` store | implementation — owner check (shopper) / admin role (operator) |

---

## 3. Trap notes (hazard + skill; not resolved here)

- Building the client/options + `IHttpClientFactory` lifetime vs. the transient wrapper — **MUST load dotnet-client-initialization**.
- Supplying `Oauth2` credentials & where secrets are set — **MUST load dotnet-authentication**.
- Optional params on `CreateOrder`/`Search…` have no C# default and mis-bind positionally; the injected `Idempotency-Key` header is **not** a real key (the real one is `payPalRequestId`) — **MUST load dotnet-calling-endpoints**.
- `CardRequest`/enums: `StringEnum<T>` not C# enums; unions via `TryGet…`; extension-data bag presence — **MUST load dotnet-models**.
- Case A vs B per op, `TryGetError` vs `TryGetRawError`, and `JsonException` escaping the SDK-exception ladder from two directions — **MUST load dotnet-error-handling**.
- `Timeout` is per-attempt not total; `HttpMethodsToRetry` excludes POST so writes are not auto-resent; `LogRequestBody` logs card JSON unredacted; `SearchTransactions` pagination is hand-rolled — **MUST load dotnet-configuration-resilience**.
- The `HttpClient` ctor arg is the test seam — **MUST load dotnet-testing**.

---

## 4. REQUIRED READING (load ALL before implementation; sheet omits their contents)

- `paypal-platforms-team:dotnet-client-initialization` · client + DI (step 2)
- `paypal-platforms-team:dotnet-authentication` · Oauth2 credentials (step 2)
- `paypal-platforms-team:dotnet-calling-endpoints` · every SDK call (steps 4–5)
- `paypal-platforms-team:dotnet-models` · request/response models (steps 4–5)
- `paypal-platforms-team:dotnet-error-handling` · gateway error boundary (step 4) — **always required**
- `paypal-platforms-team:dotnet-configuration-resilience` · timeout budget, retry, logging, pagination (step 2,4)
- `paypal-platforms-team:dotnet-testing` · faking the SDK seam (step 7)

Mandatory hazard rows (verbatim): a drifted/malformed **2xx** body (missing `required` member) surfaces as
`System.Text.Json.JsonException` from deserialization, **not** `SdkException` — an SDK-exception-only catch
lets it escape; a **non-2xx** body not matching its `{Operation}Error` shape throws `JsonException` *while the
error object is constructed*, **replacing** the `SdkException` and destroying the HTTP status. The gateway
boundary catches `SdkException<T>` **and** `JsonException` (and `OperationCanceledException`).

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalStartupValidator` (hosted/`IValidateOptions`) checks `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Currency`, `PayPal:Environment` all non-blank at startup; if `Environment` ≠ `sandbox` then `PayPal:BaseUrl` required. Missing/blank → `InvalidOperationException` before first request (not a lazy 401). Both credential parts checked separately. |
| 2 | Secret sourcing & rotation | Secrets from **.NET user-secrets** (`PayPal:ClientId/ClientSecret`), never in repo files. `AddPayPalServerSdkClient` builds options once at registration → captured in singleton → **rotation needs a process restart**; documented, acceptable for this app. |
| 3 | Total timeout budget | SDK `Timeout` is **per-attempt**; the whole-call bound is a `CancellationToken` deadline. Each service call passes a linked CTS (default 100s wall-clock budget) as `ct:` so a hung retried call cannot exceed it. Enforced in `PayPalGateway`. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS → the SDK never auto-resends our POST writes (create/authorize/capture/void/refund/vault). Safe: each money POST carries a caller `payPalRequestId`, so even a manual replay is idempotent at PayPal. Retries left at SDK default. |
| 5 | Idempotency & ambiguous writes | authorize → `payPalRequestId` = `OrderPayment.AuthorizeRequestId` (stable Guid per order); capture → `CaptureRequestId` (stable Guid per order); refund → local claim keyed on the **caller-supplied** `idempotencyKey` (unique `PaymentRefund` index), with the PayPal `payPalRequestId` **namespaced by capture id** so a reused literal key cannot false-collide across orders/runs. PayPal stores keys ~6h. The generator's per-call `Idempotency-Key: Guid.NewGuid()` header is **not** relied on. A replayed money POST is **not** issued (PayPal answers a repeat request-id with `DUPLICATE_REQUEST_ID`, not the original result). |
| 6 | Observability | Info: state transitions with orderId + PayPal ids. Warning/Error: gateway failures with the provider `debug_id`/correlation from the error body (`Error.DebugId` when present, else raw). `LogRequestBody` stays **off**. No card fields ever logged. |
| 7 | Sensitive data | Card PAN/CVV/expiry flow through `CardRequest`/`PaymentTokenRequestCard` → **never** persisted (only brand + last4 + expiry kept) and **never** logged. `LogRequestBody` off **and** `LoggerFactory` set explicitly at registration so `PAYPALSERVERSDKCLIENT_LOG` cannot force body logging on. |
| 8 | Environment selection | One server group `Default`, one env member `Sandbox`. Dev/test → Sandbox base. `PayPal:Environment=sandbox` maps to `ServerEnvironment.Sandbox`; any other value **requires** `PayPal:BaseUrl` (the live base) which overrides the base URL — keeps sandbox traffic off live. |
| 9 | Duplicate prevention under concurrency | **refund**: `PaymentRefunds` table, unique index on `(OrderPaymentId, IdempotencyKey)`; insert-before-call; second row rejected by the unique constraint → `DbUpdateException` caught → return existing refund. **authorize/capture**: `OrderPayments.RowVersion` concurrency token rejects the second `SaveChanges` (`DbUpdateConcurrencyException` caught → reload → idempotent return); the money itself is deduped by `payPalRequestId` at PayPal. (SQL Server enforces both; the mandated in-memory provider is a test substitution — the design carries the claim, and PayPal's key is the cross-host authority.) |
| 10 | Partial results | `SearchTransactions` reconciliation loops `page` 1..`TotalPages`; the report DTO carries `PagesFetched`/`TotalPages` and `Truncated` so a caller sees if coverage was cut. No silent cap. |
| 11 | Startup validation vs test host | `tests/PublicApiIntegrationTests` (+ FunctionalTests) boot the host via `WebApplicationFactory`. They will be given **placeholder** `PayPal:*` config (non-blank dummy values) via the factory so fail-fast passes; run and confirm green. |
| 12 | Ordering & no-op side effects | `OrderPayment` row is written at **placement** (before any PayPal call) carrying invoiceId + request-id claims; provider ids written only after each call returns. Idempotent transitions gate their side effects on an actual state change (already-Authorized/Captured/Cancelled → return current, no second PayPal call). |
| 13 | Unknown outcomes | Transport/timeout failures surface as `PaymentGatewayException` and **never** persist a false "failed" outcome: authorize leaves Status=AwaitingPayment (next `/pay` re-drives with the same `AuthorizeRequestId`); capture leaves Status=Authorized (next `/fulfil` re-drives with the same `CaptureRequestId`); refund leaves its claim row unsettled (record-and-sweep). `GET /api/reconciliation` (`SearchTransactions`, filtered on the PayPal event time) is the sweep that re-reads PayPal and surfaces holds/captures/refunds eShop is unsure of. A replayed money POST is deliberately not issued (see row 5). |
| 14 | Provider status & reconciliation clocks | Branch on status enums (§ OPERATION OUTCOMES). Absent status is treated **not-yet**, never coalesced to success. Reconciliation filters both sides on the **PayPal transaction time**: PayPal `transaction_initiation_date` vs the stored `OrderPayment.PayPalCreatedAt` (the authorization/capture `create_time` from PayPal) — **not** a local row-creation column. |

---

## 6. Assumptions & Blockers

- **Assumption (minor):** the buyer identity = the JWT `ClaimTypes.Name` (username), used as `Order.BuyerId`
  and `SavedCard.BuyerId`, matching how eShop scopes orders/baskets by buyer id.
- **Assumption (minor):** shipping address is not the focus; place-order accepts an optional address and
  falls back to a fixed placeholder so the existing `Order(buyerId, address, items)` ctor is satisfied.
- **Assumption (minor):** refund is shopper-scoped per the task's explicit rule ("every other endpoint is
  shopper-scoped"); owner check applies. Fulfil/cancel/reconciliation are admin.
- **Design decision (YOUR CALL):** `GET /api/payment-methods` lists from the local `SavedCards` store
  (authoritative for ownership), not `Vault.ListCustomerPaymentTokens` — so a shopper can never see another's
  card and there is no paging cap to leak. Vault list op therefore out of scope.
- **No Blockers.** Every capability maps to an SDK op. The direct-card, no-browser path is the sandbox
  business account's documented capability; if a live card response is `PAYER_ACTION_REQUIRED` (3DS
  challenge) the code returns an actionable error and I STOP & report per the task — I do not build an
  approval round-trip.

---

## 7. Verification tables (filled from the shipped code)

### DUPLICATE CLAIMS
| write | where the claim is stored | what rejects the second one | where that rejection is caught | where in the code |
| --- | --- | --- | --- | --- |
| refund | `PaymentRefunds` unique index `(OrderPaymentId, IdempotencyKey)` (`PaymentRefundConfiguration`) | DB unique constraint (SQL Server) + PayPal `payPalRequestId` (namespaced by capture) | `catch (DbUpdateException)` → reload → return existing refund | `PaymentService.RefundAsync` |
| authorize | `OrderPayments.RowVersion` concurrency token (`OrderPaymentConfiguration`); PayPal `AuthorizeRequestId` (stable per order) | `DbUpdateConcurrencyException` on 2nd SaveChanges; PayPal dedupes the money by request id | `catch (DbUpdateConcurrencyException)` → reload → return settled | `PaymentService.SaveTransitionAsync` (fast-path guard in `AuthorizeAsync`) |
| capture (fulfil) | `OrderPayments.RowVersion`; PayPal `CaptureRequestId` (stable per order) | `DbUpdateConcurrencyException`; PayPal dedupes | `catch (DbUpdateConcurrencyException)` → reload → return settled | `PaymentService.SaveTransitionAsync` (fast-path guard in `FulfilAsync`) |

### PAGED READS
| read | what caps it | how the caller learns it was cut short | where in the code |
| --- | --- | --- | --- |
| reconciliation `SearchTransactions` | loops `page` 1..`TotalPages`; `MaxReconciliationPages` (500) backstop | `ReconciliationReport.Truncated` (+ `PayPalPagesFetched`/`PayPalTotalPages`) | `PayPalGateway.SearchTransactionsAsync` → `PaymentService.ReconcileAsync` |

### REPEATED OPERATIONS
| operation | what tells you state actually changed | the effects gated on that | where in the code |
| --- | --- | --- | --- |
| pay/authorize | `OrderPayment.Status` == AwaitingPayment (else return existing) | the PayPal authorize call + `MarkAuthorized` | `PaymentService.AuthorizeAsync` |
| fulfil/capture | Status == Authorized (else return existing) | the PayPal capture call + `MarkCaptured` | `PaymentService.FulfilAsync` |
| cancel/void | Status == Authorized (else return existing; AwaitingPayment skips the void) | the PayPal void call + `MarkCancelled` | `PaymentService.CancelAsync` |
| refund | `FindRefundByKey` returns null (else return recorded) | the PayPal refund call + row insert | `PaymentService.RefundAsync` |

### UNKNOWN OUTCOMES
| write | how the outcome is settled | the reference it keys on | where in the code |
| --- | --- | --- | --- |
| authorize | gateway failure leaves Status=AwaitingPayment (no false failure persisted); next `/pay` re-drives with the same `AuthorizeRequestId`; orphan holds surface in reconciliation | `AuthorizeRequestId` / `InvoiceId` | `AuthorizeAsync` (no state write on `PaymentGatewayException`) + `ReconcileAsync` |
| capture | on provider rejection: renew authorization once then retry capture; gateway failure leaves Status=Authorized so next `/fulfil` re-drives with the same `CaptureRequestId` | `CaptureRequestId` / `authorizationId` | `FulfilAsync` (renew-and-retry) + `RenewAuthorizationAsync` |
| refund | definite rejection → refund row recorded `FAILED` (same key returns FAILED); gateway/unknown failure → claim row left unsettled (record-and-sweep), settled by reconciliation | caller `IdempotencyKey` / `captureId` | `RefundAsync` catch + `ReconcileAsync` |

### OPERATION OUTCOMES
| write | the status field | every value → app action | where in the code |
| --- | --- | --- | --- |
| authorize | `OrderStatus` + `AuthorizationStatus` | order PayerActionRequired→**PaymentChallengeRequiredException (STOP)**; Created/Captured/PartiallyCaptured→Authorized; Pending→Pending; Denied/Voided/absent→Failed | `PayPalGateway.AuthorizeAsync` (outcome switch) → `PaymentService.AuthorizeAsync` |
| capture | `CaptureStatus` | Completed/Refunded/PartiallyRefunded→Completed(record gross/fee/net); Pending→Pending(not-yet); Declined/Failed/absent→Failed→PaymentValidationException | `PayPalGateway.MapCapture` → `FulfilAsync` (rejects Failed) |
| refund | `RefundStatus` | Completed→recorded+applied; Pending→recorded+applied(reserves remaining); Failed/Cancelled/absent→Failed (not applied) | `PayPalGateway.RefundAsync` (outcome switch) → `RefundAsync` |

### WRITE ORDER
| write | what exists locally BEFORE the call | what is written after it returns | where in the code |
| --- | --- | --- | --- |
| authorize | `OrderPayment` (AwaitingPayment, InvoiceId, AuthorizeRequestId) created at placement | PayPalOrderId, AuthorizationId, AuthorizationStatus, ExpiresAt, PayPalCreatedAt, Status=Authorized | `PlaceOrderAsync` (before) → `AuthorizeAsync`+`MarkAuthorized` (after) |
| capture | `OrderPayment` (Authorized) | CaptureId, CaptureStatus, CapturedGross/PaypalFee/NetAmount, PayPalCreatedAt, Status=Captured | `FulfilAsync` → `MarkCaptured` |
| refund | `PaymentRefund` (IdempotencyKey, amount, status null) inserted & saved before call | PayPalRefundId, Status, IsEffective; payment RefundedAmount/Status | `RefundAsync` (`StartRefund`+save, then `RecordResult`) |

---

## 8. Implementation notes — runtime discoveries (contract facts held; these are application decisions)

- **Card vaulting is a two-step flow.** Creating a payment token directly from a raw card
  (`Vault.CreatePaymentToken` with `payment_source.card`) returns HTTP 500 on this account; the supported
  no-browser path is `Vault.CreateSetupToken` (raw card) → `Vault.CreatePaymentToken` (token=setup-token).
  `PayPalGateway.VaultCardAsync` does the two-step.
- **`customer.merchant_customer_id` must be sanitised.** PayPal's vault 500s on characters the schema regex
  nominally allows (e.g. `@` in an email), so the buyer id is reduced to `[0-9A-Za-z_-]` before it is sent.
- **Refund `PayPal-Request-Id` is namespaced by the capture id.** A bare caller key can collide with the
  same literal used earlier (PayPal retains request-ids ~6h and the sandbox account is reused), which
  returns `DUPLICATE_REQUEST_ID`. Local idempotency is still enforced by the unique `PaymentRefund` claim.
- **No blind replay of money POSTs.** PayPal answers a replayed request-id with a `DUPLICATE_REQUEST_ID`
  error, not the original result, so double-submit protection is the local state machine + unique claim,
  and unknown transport outcomes are settled by reconciliation, never by re-POSTing.
- **Void returns 204 (no body);** the SDK throws `JsonException` deserialising it — `VoidAsync` treats that
  as success.
- **Reconciliation matches invoice ids case-insensitively** — PayPal's transaction reporting lower-cases
  `invoice_id`.
