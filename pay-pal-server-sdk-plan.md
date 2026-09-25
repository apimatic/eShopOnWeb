# PayPal Server SDK integration plan — eShopOnWeb PublicApi

Additive card-payments + saved-cards capability on `src/PublicApi`. PayPal Server SDK (.NET,
root namespace `PayPalServerSdk`, spec `2.29`) is the sole PayPal client. Contract facts below
come from the SDK map + the map-named source files; application design is decided here.

## 1. Scope & sequence

The SDK is **not on NuGet** → vendor its source as a build-referenced project `src/PayPalServerSdk`
(copied from the SDK repo; map/docs excluded). The read-only map clone stays in the system temp dir.

1. **Vendor + reference SDK**: copy SDK project into `src/PayPalServerSdk`, add to solution, `ProjectReference` from `Infrastructure`. Set `ManagePackageVersionsCentrally=false` in that csproj (repo uses central package mgmt; SDK csproj pins its own versions).
2. **Config + client (Infrastructure)**: bind `PayPalOptions` from `PayPal:` section; fail-fast; register `PayPalServerSdkClient` singleton via `IHttpClientFactory`; `BaseUrl` override applies to every call incl. token.  → ops: none (setup).
3. **Domain (ApplicationCore)**: `OrderPayment` (aggregate, 1:1 with `Order`), `OrderRefund` (child), `SavedPaymentMethod` (aggregate), `IdempotencyClaim` (aggregate, PK = claim key), `PaymentStatus` enum, `IPayPalPaymentGateway` + domain result DTOs, `IOrderPaymentService`.
4. **Gateway impl (Infrastructure)**: translate domain calls → SDK ops, map SDK models → domain DTOs, translate SDK exceptions.  → ops: CreateOrder, AuthorizeOrder, CaptureAuthorizedPayment, ReauthorizePayment, VoidPayment, RefundCapturedPayment, GetAuthorizedPayment, CreatePaymentToken, DeletePaymentToken, SearchTransactions.
5. **App service (ApplicationCore)**: place order, authorize(=pay), fulfil(=capture, reauth-on-stale), cancel(=void), refund(partial/full, capped), my-orders, reconciliation, save/list/delete card. Enforces state machine + idempotency claims.
6. **Endpoints (PublicApi)**: the 11 routes; JWT; admin-gate fulfil/cancel/reconciliation; shopper-scope the rest.
7. **Secrets**: load env vars → `dotnet user-secrets` under `PayPal:*` (values never written to repo).
8. **Build, run, self-verify** the five flows on the sandbox card + saved-card reuse.

Card flow chosen: **single-step** `CreateOrder(intent=AUTHORIZE, payment_source.card{...}, Prefer=return=representation, PayPal-Request-Id)`; read the authorization from `purchase_units[0].payments.authorizations[0]`. If absent but order `APPROVED` → `AuthorizeOrder(id)`. If order `PAYER_ACTION_REQUIRED` / an `approve`/`payer-action` link is present → **browser challenge → surface as a blocked outcome to the caller (do not build an approval round-trip)**. Saved-card pay = same CreateOrder with `payment_source.card.vault_id = <token>`.

## 2. CONTRACT SHEET

⚠ **Signatures are generated code, verbatim.** Each operation that takes input takes **one request record**
as its first parameter, built with an **object initializer** using the record's own property names — never flat args.
⚠ **Every SDK type is fully-qualified by the namespace its source path implies** (`Models/` → `PayPalServerSdk.Models`,
`Models/Enums/` → `PayPalServerSdk.Models.Enums`, `Requests/<Ctrl>/` → `PayPalServerSdk.Requests.<Ctrl>`,
`Errors/` → `PayPalServerSdk.Errors`, client/options → `PayPalServerSdk`, `ServerEnvironment` → `PayPalServerSdk.Servers`),
taken from THAT type's own path — never a neighbour's.

| Op (client.X) | Signature (req record → required members) | Body model + fields read/written | Response envelope + inner fields read | Error case + accessors | Pag. | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `Orders.CreateOrder` | `CreateOrder(CreateOrderRequest{Body*; PayPalRequestId?; Prefer="return=minimal"})` | `OrderRequest{Intent*(CheckoutPaymentIntent), PurchaseUnits*(IReadOnlyList<PurchaseUnitRequest>), PaymentSource?}`; `PurchaseUnitRequest{Amount*(AmountWithBreakdown), CustomId?, InvoiceId?, Description?}`; `AmountWithBreakdown{CurrencyCode*, Value*}`; `PaymentSource{Card?(CardRequest)}`; `CardRequest{Name?,Number?,Expiry?"YYYY-MM",SecurityCode?,BillingAddress?(Address),VaultId?}` | `Order{Id, Status(OrderStatus?), PurchaseUnits(IReadOnlyList<PurchaseUnit>?), Links}`; `PurchaseUnit.Payments(PaymentCollection?)`; `PaymentCollection.Authorizations(IReadOnlyList<AuthorizationWithAdditionalData>?)`; `AuthorizationWithAdditionalData{Id, Status(AuthorizationStatus?), Amount(Money?), ExpirationTime}` | A: `TryGetError(out Error)`[400,401,422] · `TryGetRawError` | none | Orders.md; Models/OrderRequest.cs, PurchaseUnitRequest.cs, AmountWithBreakdown.cs, PaymentSource.cs, CardRequest.cs, Order.cs, PurchaseUnit.cs, PaymentCollection.cs, AuthorizationWithAdditionalData.cs |
| `Orders.AuthorizeOrder` | `AuthorizeOrder(AuthorizeOrderRequest{Id*; Body?(OrderAuthorizeRequest); PayPalRequestId?; Prefer})` | Body optional (payment source already attached at create → pass null) | `OrderAuthorizeResponse{Id, Status, PurchaseUnits(IReadOnlyList<PurchaseUnit>?)}` (same auth path as above) | A: `TryGetError`[400,401,403,404,422,500] · `TryGetRawError` | none | Orders.md; Models/OrderAuthorizeResponse.cs |
| `Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(CaptureAuthorizedPaymentRequest{AuthorizationId*; Body?(CaptureRequest); PayPalRequestId?; Prefer})` | `CaptureRequest{Amount?(Money), FinalCapture?}` — omit Amount for full capture; set `FinalCapture=true` | `CapturedPayment{Id, Status(CaptureStatus?), Amount(Money?), SellerReceivableBreakdown{GrossAmount*, PaypalFee?, NetAmount?}}` | A: `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` | none | Payments.md; Models/CaptureRequest.cs, CapturedPayment.cs, SellerReceivableBreakdown.cs, Money.cs |
| `Payments.ReauthorizePayment` | `ReauthorizePayment(ReauthorizePaymentRequest{AuthorizationId*; Body?(ReauthorizeRequest); PayPalRequestId?; Prefer})` | `ReauthorizeRequest{Amount?(Money)}` — set Amount = order total | `PaymentAuthorization{Id, Status(AuthorizationStatus?), Amount, ExpirationTime}` | A: `TryGetError`[400,401,403,404,422] · `TryGetNoContent`[500] · `TryGetRawError` | none | Payments.md; Models/ReauthorizeRequest.cs, PaymentAuthorization.cs |
| `Payments.VoidPayment` | `VoidPayment(VoidPaymentRequest{AuthorizationId*})` | none | `PaymentAuthorization{Id, Status(AuthorizationStatus?)}` (expect VOIDED) | A: `TryGetError`[401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | none | Payments.md |
| `Payments.RefundCapturedPayment` | `RefundCapturedPayment(RefundCapturedPaymentRequest{CaptureId*; Body?(RefundRequest); PayPalRequestId?; Prefer})` | `RefundRequest{Amount?(Money)}` — omit for full, set for partial | `Refund{Id, Status(RefundStatus?), Amount(Money?), SellerPayableBreakdown{TotalRefundedAmount?}}` | A: `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | none | Payments.md; Models/RefundRequest.cs, Refund.cs, SellerPayableBreakdown.cs |
| `Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(GetAuthorizedPaymentRequest{AuthorizationId*})` | none | `PaymentAuthorization{Id, Status, ExpirationTime}` (re-read for unknown-outcome settle) | A: `TryGetError`[401,403,404] · `TryGetNoContent`[500] · `TryGetRawError` | none | Payments.md |
| `Vault.CreatePaymentToken` | `CreatePaymentToken(CreatePaymentTokenRequest{Body*(PaymentTokenRequest); PayPalRequestId?})` | `PaymentTokenRequest{Customer?(Customer{Id?,MerchantCustomerId?}), PaymentSource*(PaymentTokenRequestPaymentSource{Card?(PaymentTokenRequestCard{Name?,Number?,Expiry?,SecurityCode?,BillingAddress?})})}` | `PaymentTokenResponse{Id, Customer(CustomerResponse?), PaymentSource.Card(CardPaymentTokenEntity{LastDigits?,Brand?(CardBrand?),Expiry?,Name?})}` | A: `TryGetError`[400,403,404,422,500] · `TryGetRawError` | none | Vault.md; Models/PaymentTokenRequest.cs, PaymentTokenRequestPaymentSource.cs, PaymentTokenRequestCard.cs, PaymentTokenResponse.cs, PaymentTokenResponsePaymentSource.cs, CardPaymentTokenEntity.cs, Customer.cs |
| `Vault.DeletePaymentToken` | `DeletePaymentToken(DeletePaymentTokenRequest{Id*})` | none | `void` (Task) | A: `TryGetError`[400,403,500] · `TryGetRawError` | none | Vault.md |
| `TransactionSearch.SearchTransactions` | `SearchTransactions(SearchTransactionsRequest{StartDate*, EndDate*, Fields="transaction_info", BalanceAffectingRecordsOnly="Y", PageSize=100, Page=1})` | query params (see wire names in TransactionSearch.md) | `SearchResponse{TransactionDetails(IReadOnlyList<TransactionDetails>?), Page, TotalPages, TotalItems}`; `TransactionDetails.TransactionInfo(TransactionInformation{TransactionId, InvoiceId, CustomField, TransactionAmount(Money?), TransactionStatus, TransactionEventCode, TransactionInitiationDate})` | **B: `ApiException<RawError>`** — `StatusCode`/`ReadAsString()`/`ReadAsJson<T>()` | **page-based** (`page`+`total_pages`) | TransactionSearch.md; Models/SearchResponse.cs, TransactionDetails.cs, TransactionInformation.cs |

Enums (`Models/Enums/`, ns `PayPalServerSdk.Models.Enums`; static PascalCase members, wire value in parens):
- `CheckoutPaymentIntent`: `Authorize`("AUTHORIZE"), `Capture`("CAPTURE") — **use `Authorize`**. src CheckoutPaymentIntent.cs
- `OrderStatus`: Created, Saved, Approved, Voided, Completed, PayerActionRequired. src OrderStatus.cs
- `AuthorizationStatus`: Created, Captured, Denied, PartiallyCaptured, Voided, Pending. src AuthorizationStatus.cs
- `CaptureStatus`: Completed, Declined, PartiallyRefunded, Pending, Refunded, Failed. src CaptureStatus.cs
- `RefundStatus`: Cancelled, Failed, Pending, Completed. src RefundStatus.cs
- `CardBrand` (display only, store via safe accessor). src CardBrand.cs

Client/auth/server (sdk-map.md *Getting a client* / *Servers & auth*):
- `new PayPalServerSdkClient(HttpClient, PayPalServerSdkClientOptions)` — only ctor.
- Options: `Oauth2 = new OAuth2ClientCredentials{ClientId, ClientSecret}` (ns `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`); `Environment = ServerEnvironment.Sandbox`.
- One server group `Default`, sandbox base `https://api-m.sandbox.paypal.com`; override `options.Server.Default.Sandbox.BaseUrl` for `PayPal:BaseUrl`.
- Token endpoint `/v1/oauth2/token` on the same base — BaseUrl override therefore covers the token call too.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The `vault_id` passed to `CardRequest.VaultId` at pay must be a token this shopper saved (i.e. a `SavedPaymentMethod.PayPalVaultId` owned by the caller) — never an arbitrary/other shopper's token | `CreateOrder` ← `CreatePaymentToken`/local `SavedPaymentMethod` store | in `OrderPaymentService.Pay`, before CreateOrder: look up the saved card by (caller, paymentMethodId); 404 if not owned |
| The `AuthorizationId` captured/voided/reauthorized must be the one this order's authorize stored | Capture/Void/Reauthorize ← CreateOrder/AuthorizeOrder | read from `OrderPayment.AuthorizationId` for the caller's order only |
| The `CaptureId` refunded must be the one this order's fulfil stored | RefundCapturedPayment ← CaptureAuthorizedPayment | read from `OrderPayment.CaptureId` |
| Deleting a saved card removes local ownership so it can no longer be used to pay | `DeletePaymentToken` + local delete | pay-time lookup fails after delete |

## 3. Trap notes (name the hazard; load the skill — do NOT resolve inline)

- Client/DI lifetime & `HttpClient` ownership for the SDK client — get it wrong and you leak sockets or rebuild per call. **MUST load dotnet-client-initialization**.
- Credential placement (set before/at construction) and sourcing from config not code. **MUST load dotnet-authentication**.
- Request records: which members are required vs optional-but-semantically-needed; the injected `Idempotency-Key` header is NOT the real key. **MUST load dotnet-calling-endpoints**.
- `OpenStringEnum` are not C# enums (no cast, `Match`/`TryGetKnownValue`, PascalCase members); union/optional read via `TryGet…`; extension-data bag presence per model. **MUST load dotnet-models**.
- Case A vs B per operation; `TryGetNoContent` is a real accessor on the Payments ops (500) distinct from `TryGetRawError`; a drifted 2xx/non-2xx body surfaces as `ResponseDeserializationException`, not `ApiException<TError>` — the ladder must also catch that (see REQUIRED READING). **MUST load dotnet-error-handling**.
- `Timeout` is per-attempt not total; `HttpMethodsToRetry` default excludes POST so PayPal writes are not auto-resent; `LogRequestBody` logs JSON **unredacted** and `PAYPALSERVERSDKCLIENT_LOG` can arm it from outside code. **MUST load dotnet-configuration-resilience**.
- The `HttpClient` ctor arg is the test seam. **MUST load dotnet-testing**.

## 4. REQUIRED READING (load all before implementing; sheet omits their contents)

- `sdk-skills-test-dev:dotnet-client-initialization` — SDK client construction & DI (step 2/4).
- `sdk-skills-test-dev:dotnet-authentication` — OAuth2 client-credentials wiring (step 2).
- `sdk-skills-test-dev:dotnet-calling-endpoints` — request records & call shape (step 4).
- `sdk-skills-test-dev:dotnet-models` — enums/unions/models mapping (step 4).
- `sdk-skills-test-dev:dotnet-error-handling` — try/catch boundary in the gateway (step 4/5).
- `sdk-skills-test-dev:dotnet-configuration-resilience` — retries/timeouts/base-url/logging (step 2/4).
- `sdk-skills-test-dev:dotnet-testing` — SDK seam for tests (if tests are added).

⚠ Hazard row (verbatim): a body that does not match its declared type — a drifted/malformed **2xx**
(missing `required` member) or a **non-2xx** body not matching the operation's generated `{Operation}Error`
shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and
names the target type but is **not** `ApiException<TError>`. A ladder catching only `ApiException<TError>`
lets it escape → the gateway must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `AddPayPalIntegration` validates `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Currency`, `PayPal:Environment` non-blank at registration; throws → host refuses to start. Every part checked (blank ≠ missing). `BaseUrl` optional. |
| 2 | Secret sourcing & rotation | Secrets from env → `dotnet user-secrets` under `PayPal:*` (dev). `PayPalOptions` bound once and the `PayPalServerSdkClient` is a singleton built at registration → **rotation needs a process restart** (documented; acceptable for this app). |
| 3 | Total timeout budget | SDK `Timeout` is per-attempt. Gateway passes a `CancellationToken` from an overall budget (`PayPal:CallTimeoutSeconds`, default 30s) via `RequestOptions`/token deadline — that token, not `Timeout`, bounds the whole call incl. retries. |
| 4 | Write-retry ownership | All PayPal writes here are POST → SDK default `HttpMethodsToRetry` (GET/HEAD/PUT/OPTIONS) does **not** resend them. `SearchTransactions` GET may retry safely. No POST is auto-resent → duplicates prevented by claims + `PayPal-Request-Id` (row 5). |
| 5 | Idempotency & ambiguous writes | CreateOrder: real key `PayPalRequestId` (mandatory single-step card; 6h) = deterministic per order. Capture: `PayPalRequestId` (45d) per order. Refund: `PayPalRequestId` (45d) = caller-supplied refund idempotency key. Void/Reauthorize: no create-key needed (void is naturally idempotent by auth state; reauth guarded by claim). Local claim precedes each (DUPLICATE CLAIMS). |
| 6 | Observability | Gateway logs op name + PayPal ids (order/auth/capture/refund) at Info; on `ApiException` logs `Error.DebugId` (PayPal correlation) + `Error.Name`/first `Issue` at Warning/Error. **No request bodies logged.** |
| 7 | Sensitive data | Card PAN/CVV flow through `CardRequest`/`PaymentTokenRequestCard`. Therefore: `LogRequestBody` stays **off**, `options.Logging.LoggerFactory` set **explicitly** (so `PAYPALSERVERSDKCLIENT_LOG` cannot arm body logging), and no app log line echoes card fields. PAN/CVV never persisted — only vault token id + brand + last 4. |
| 8 | Environment selection | SDK declares only `ServerEnvironment.Sandbox`. `PayPal:Environment` maps `sandbox`→Sandbox; any other value → fail-fast (no live env exists in this SDK, so test traffic cannot reach live). `PayPal:BaseUrl` override, when set, points every call (incl. token) at that URL via `options.Server.Default.Sandbox.BaseUrl`. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS — `IdempotencyClaim` table, PK = claim key, inserted+saved **before** each SDK write; duplicate PK insert throws → 2nd caller rejected before reaching PayPal. |
| 10 | Partial results | `SearchTransactions` is paged. Reconciliation loops `page=1..TotalPages` (per ≤31-day window) accumulating all `transaction_details`; the report DTO carries `Pages`/`TotalItems`/`Truncated=false` so the caller sees full coverage, not a silent first page. |
| 11 | Unknown outcomes | CreateOrder/Capture/Refund connection failure after PayPal may have acted → settle via re-read: CreateOrder resend with same `PayPalRequestId` returns the same order; Capture → `GetAuthorizedPayment(authId)` shows CAPTURED; Refund resend with same `PayPalRequestId` returns same refund. Gateway catches `SdkConnectionException`/`SdkTimeoutException` and calls the settle path before reporting failure. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Authorize (pay) | `IdempotencyClaim` table (app DB), PK `"auth:{orderId}"` | duplicate-PK insert on `SaveChanges` (enforced even by EF InMemory) | `TryClaimAsync` returns false → reload; if already Authorized return it, else 409, in `OrderPaymentService.PayAsync` | `OrderPaymentService.PayAsync`: `TryClaimAsync($"auth:{orderId}")` then `_gateway.AuthorizeAsync(request, …)` |
| Fulfil (capture) | `IdempotencyClaim` PK `"capture:{orderId}"` | duplicate-PK insert | `TryClaimAsync` false → reload; if Fulfilled return, else 409, in `OrderPaymentService.FulfilAsync` | `OrderPaymentService.FulfilAsync`: `TryClaimAsync($"capture:{orderId}")` then `_gateway.CaptureAsync(payment.AuthorizationId!, …)` |
| Refund | `IdempotencyClaim` PK `"refund:{orderId}:{callerKey}"` | duplicate-PK insert | `TryClaimAsync` false → reload; existing/raced refund with same key returned, else 409, in `OrderPaymentService.RefundAsync`; a completed same-key refund is returned earlier by the pre-lookup `TryGetRefundByIdempotencyKey` | `OrderPaymentService.RefundAsync`: `TryClaimAsync($"refund:{orderId}:{idempotencyKey}")` then `_gateway.RefundAsync(payment.CaptureId!, …)` |
| Save card | none — vaulting the same card twice yields two independent tokens (harmless); no caller key in scope | n/a | n/a — a duplicate save just creates another distinct saved card | `OrderPaymentService.SaveCardAsync` (no claim, by design) |
| Cancel (void) | `IdempotencyClaim` PK `"cancel:{orderId}"` | duplicate-PK insert | `TryClaimAsync` false → reload; if Canceled return, else 409, in `OrderPaymentService.CancelAsync` | `OrderPaymentService.CancelAsync`: `TryClaimAsync($"cancel:{orderId}")` then `_gateway.VoidAsync(payment.AuthorizationId!, …)` |

⚠ Void/Reauthorize share the capture path guard (an order already Fulfilled/Canceled is rejected by state check + the capture/auth claim), so they add no separate claim row.

**PAGED READS**

| Read | What caps it | How the caller learns it was cut short | Where in the code |
| --- | --- | --- | --- |
| `SearchTransactions` (reconciliation) | `page_size` (100) × `total_pages`; range split into ≤31-day windows; per-window backstop `MaxPagesPerWindow`=1000 | `PayPalTransactionSearchResult.Truncated` (always false — every page/window is walked) + `PagesRetrieved`/`WindowsQueried`, surfaced on `ReconciliationReport` | `PayPalPaymentGateway.SearchTransactionsAsync` (window+page loop sets `PagesRetrieved`/`WindowsQueried`/`Truncated=false`); consumed by `OrderPaymentService.ReconcileAsync` → `ReconciliationReport` |

**UNKNOWN OUTCOMES**

| Write | Re-read op | Reference searched by | Where in the code | Test that fails the connection |
| --- | --- | --- | --- | --- |
| CreateOrder (authorize) | CreateOrder resend (same `PayPalRequestId`) → PayPal returns the same order | `PayPalRequestId` (`auth-{orderId}-{guid}`, stable within the call) | `PayPalPaymentGateway.InvokeAsync` idempotent-resend arm: `catch (SdkException) when (… SdkConnection/Timeout && idempotentResend && attempt == 1)` → loops and re-issues the same `CreateOrderRequest`; used by `AuthorizeAsync(idempotentResend: true)` | `UnitTests/Infrastructure/PayPal/PayPalPaymentGatewayTests.cs`: `Authorize_resends_once_on_transport_failure_and_settles` (throws on attempt 1, 200 on attempt 2, asserts 2 create attempts + a settled result) and `Authorize_reports_unknown_outcome_when_transport_keeps_failing` (asserts `OutcomeUnknown`). |
| CaptureAuthorizedPayment | Capture resend (same `PayPalRequestId`, 45-day retention) → same capture | `PayPalRequestId` (`capture-{orderId}-{guid}`) | `PayPalPaymentGateway.InvokeAsync` resend arm; used by `CaptureAsync(idempotentResend: true)` | Same `InvokeAsync` resend arm as authorize — covered by the two tests above (shared code path). |
| RefundCapturedPayment | Refund resend (same `PayPalRequestId`, 45-day retention) → same refund | `PayPalRequestId` (`{invoiceId}:{callerKey}`) | `PayPalPaymentGateway.InvokeAsync` resend arm; used by `RefundAsync(idempotentResend: true)`; second-caller duplicates are additionally stopped by the `IdempotencyClaim` + pre-lookup in `OrderPaymentService.RefundAsync` | Shared `InvokeAsync` resend arm (tests above); refund replay under the same caller key verified live (returns the same refundId, no double refund). |

⚠ Settle semantics note: after one resend that still fails on transport, `InvokeAsync` throws `PayPalGatewayException(outcomeUnknown: true)`; `PayAsync`/`FulfilAsync`/`RefundAsync` then release their claim and, for authorize, do **not** mark the payment failed (leaving it retryable). A subsequent caller retry re-issues with a fresh per-run request id; PayPal's own idempotency window covers the in-flight window. eShop's `IdempotencyClaim` prevents a concurrent duplicate reaching PayPal at all.

## 6. Assumptions & Blockers

- **No Blockers.** Every capability the task needs maps to a shipped operation.
- Assumption: caller identity for shopper-scoping = JWT `ClaimTypes.Name` (username), used as `Order.BuyerId` — matches the app's token (`IdentityTokenClaimService`).
- Assumption: PayPal `customer` for vaulting is generated on first save; its id is stored per-shopper on `SavedPaymentMethod` and reused. Listing shopper cards is served from the app DB (ownership authority), not from PayPal.
- Assumption (minor): reconciliation is an operator (admin) report spanning all shoppers.
- Assumption: sandbox test card 4111… authorizes without a 3DS/browser challenge; if a challenge is returned, the pay endpoint returns a clear "browser approval required — unsupported" error rather than an approval round-trip (per task).
