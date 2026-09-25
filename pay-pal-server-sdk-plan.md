# PayPal Server SDK integration plan — eShopOnWeb payments & saved cards

Adds PayPal-processed payments (authorize → capture → refund/void) and saved cards (vault) to
eShopOnWeb, exposed as JWT endpoints on `src/PublicApi`, reusing the existing `Order` aggregate.
All contract facts below come from the SDK map / map-named source files (cited in the **source**
column), never memory.

---

## 1. Scope & sequence

| # | Step | PayPal operations used |
| --- | --- | --- |
| 1 | Vendor the SDK **source** into the repo as project `src/PayPalServerSdk` (it is not on NuGet, and the temp reference clone must not be a build dependency); `ProjectReference` it from `PublicApi`, `Infrastructure`. Set `ManagePackageVersionsCentrally=false` in the vendored csproj so its pinned versions don't clash with root central-package-management. | — |
| 2 | Domain: extend `Order` aggregate with payment/fulfilment state — an owned `OrderPaymentInfo` (PayPal ids + statuses for hold/capture) and an `OrderStatus`; add child `PaymentRefund` list; add `SavedPaymentMethod` + `PayPalCustomerRef` entities. EF config + concurrency token. | — |
| 3 | Infrastructure `PayPal/`: `PayPalSettings` options bound from `PayPal:*` with fail-fast; DI registration of one long-lived `PayPalServerSdkClient` (singleton over `IHttpClientFactory`); `IPayPalGateway` + `PayPalGateway` translating SDK calls/errors to domain. | all below |
| 3a | authorize (money hold) | `Orders.CreateOrder` (intent=AUTHORIZE) then `Orders.AuthorizeOrder` (payment_source in body) |
| 3b | capture at fulfil | `Payments.CaptureAuthorizedPayment`; stale renew: `Payments.ReauthorizePayment` |
| 3c | cancel before fulfil | `Payments.VoidPayment` |
| 3d | refund after fulfil | `Payments.RefundCapturedPayment` |
| 3e | reconciliation | `TransactionSearch.SearchTransactions` |
| 3f | save card | `Vault.CreatePaymentToken` |
| 3g | list saved cards | `Vault.ListCustomerPaymentTokens` |
| 3h | delete saved card | `Vault.DeletePaymentToken` |
| 4 | Application services (ApplicationCore): `IOrderPaymentService` (place/pay/fulfil/cancel/refund/my-orders), `IPaymentMethodService` (save/list/delete), `IReconciliationService`. | — |
| 5 | `PublicApi` endpoints (IEndpoint style) under `/api/` per task; shopper-scoped by token identity, operator actions gated to `Administrators`. | — |
| 6 | Tests (gateway error-translation, service state machine) + live sandbox end-to-end verification. | — |

A capability the map lacks would be a Blocker (§6) — none found; every required flow maps to an
operation above.

---

## 2. CONTRACT SHEET

> ⚠ **Signatures are generated code, verbatim.** Each operation that takes input takes **ONE request
> record** as its first parameter, built with an object initializer using the record's **own** property
> names — never flat positional arguments. An operation with no inputs takes none.
> ⚠ **Every SDK type is written fully-qualified with the namespace its source path implies** (records/unions →
> `PayPalServerSdk.Models`; enums → `PayPalServerSdk.Models.Enums`; typed errors → `PayPalServerSdk.Errors`;
> request records → `PayPalServerSdk.Requests.<Controller>`; client/options → `PayPalServerSdk`;
> `ServerEnvironment` → `PayPalServerSdk.Servers`). Take each type's namespace from **its own** source path.

Client namespaces confirmed: models `PayPalServerSdk.Models`; enums `PayPalServerSdk.Models.Enums`
(`OpenStringEnum<T>`, not C# enums — use static members / `TryGetKnownValue` / `Match`); errors
`PayPalServerSdk.Errors`; requests `PayPalServerSdk.Requests.{Orders|Payments|Vault|TransactionSearch}`.

### Per-operation rows

| Operation (controller.method) | Request record + members (required?) | Body model + fields read/written (wire) | Response envelope → fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `Orders.CreateOrder(CreateOrderRequest)` | `Body: OrderRequest` (req); optional headers `PayPalRequestId` (idempotency, 6h), `Prefer` | `OrderRequest`: `Intent: CheckoutPaymentIntent` (req, `intent`), `PurchaseUnits: IReadOnlyList<PurchaseUnitRequest>` (req, `purchase_units`, 1..10), `PaymentSource?` (`payment_source`). `PurchaseUnitRequest`: `Amount: AmountWithBreakdown` (req, `amount`), `CustomId?` (`custom_id`), `InvoiceId?` (`invoice_id`), `Description?`. `AmountWithBreakdown`: `CurrencyCode` (req), `Value` (req, string). | `Order` → `Id`, `Status: OrderStatus?` | A `ApiException<CreateOrderError>`; `TryGetError(out Error)` [400,401,422] · `TryGetRawError` | none | map/operations/Orders.md; Models/OrderRequest.cs, PurchaseUnitRequest.cs, AmountWithBreakdown.cs; Requests/Orders/CreateOrderRequest.cs |
| `Orders.AuthorizeOrder(AuthorizeOrderRequest)` | `Id: string` (req, `^[A-Z0-9]+$`); `Body: OrderAuthorizeRequest?`; optional `PayPalRequestId`, `Prefer` | `OrderAuthorizeRequest`: `PaymentSource: OrderAuthorizeRequestPaymentSource?` (`payment_source`) → `Card: CardRequest?`. `CardRequest` (direct): `Number`,`Expiry` (`YYYY-MM`),`SecurityCode`,`Name`,`BillingAddress: Address?`; (saved) `VaultId` only. | `OrderAuthorizeResponse` → `Id`, `Status: OrderStatus?`, `PurchaseUnits[].Payments.Authorizations[]: AuthorizationWithAdditionalData` → `Id`, `Status: AuthorizationStatus?`, `ExpirationTime`, `Amount` | A `ApiException<AuthorizeOrderError>`; `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` | none | Orders.md; Models/OrderAuthorizeRequest.cs, OrderAuthorizeRequestPaymentSource.cs, CardRequest.cs, OrderAuthorizeResponse.cs, PurchaseUnit.cs, PaymentCollection.cs, AuthorizationWithAdditionalData.cs |
| `Payments.CaptureAuthorizedPayment(CaptureAuthorizedPaymentRequest)` | `AuthorizationId: string` (req); `Body: CaptureRequest?`; **`PayPalRequestId: string?` (REAL idempotency key, 45-day)**; `Prefer` | `CaptureRequest`: `Amount: Money?`, `FinalCapture: bool?`(`final_capture`), `InvoiceId?` | `CapturedPayment` → `Id`, `Status: CaptureStatus?`, `Amount: Money?`, `SellerReceivableBreakdown` → `GrossAmount`(req), `PaypalFee: Money?`, `NetAmount: Money?` | A `ApiException<CaptureAuthorizedPaymentError>`; `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | none | Payments.md; Requests/Payments/CaptureAuthorizedPaymentRequest.cs; Models/CaptureRequest.cs, CapturedPayment.cs, SellerReceivableBreakdown.cs, Money.cs |
| `Payments.ReauthorizePayment(ReauthorizePaymentRequest)` | `AuthorizationId: string` (req); `Body: ReauthorizeRequest?` (only `Amount: Money?`) | `ReauthorizeRequest`: `Amount` only | `PaymentAuthorization` → `Id`, `Status: AuthorizationStatus?`, `ExpirationTime`, `Amount` | A `ApiException<ReauthorizePaymentError>`; `TryGetError` [400,401,403,404,422] · `TryGetNoContent` [500] · `TryGetRawError` | none | Payments.md; Models/ReauthorizeRequest.cs, PaymentAuthorization.cs |
| `Payments.VoidPayment(VoidPaymentRequest)` | `AuthorizationId: string` (req) | — | `PaymentAuthorization` → `Id`, `Status` | A `ApiException<VoidPaymentError>`; `TryGetError` [401,403,404,409,422] · `TryGetNoContent` [500] · `TryGetRawError` | none | Payments.md; Requests/Payments/VoidPaymentRequest.cs |
| `Payments.RefundCapturedPayment(RefundCapturedPaymentRequest)` | `CaptureId: string` (req); `Body: RefundRequest?`; **`PayPalRequestId: string?` (REAL idempotency key, 45-day)** | `RefundRequest`: `Amount: Money?` (omit ⇒ full refund; present ⇒ partial), `CustomId?`, `InvoiceId?`, `NoteToPayer?` | `Refund` → `Id`, `Status: RefundStatus?`, `Amount: Money?` | A `ApiException<RefundCapturedPaymentError>`; `TryGetError` [400,401,403,404,409,422] · `TryGetNoContent` [500] · `TryGetRawError` | none | Payments.md; Requests/Payments/RefundCapturedPaymentRequest.cs; Models/RefundRequest.cs, Refund.cs |
| `Vault.CreatePaymentToken(CreatePaymentTokenRequest)` | `Body: PaymentTokenRequest` (req) | `PaymentTokenRequest`: `Customer: Customer?` (`Id?` PayPal cust id ≤22 `^[0-9a-zA-Z_-]+$`, `MerchantCustomerId?`), `PaymentSource: PaymentTokenRequestPaymentSource` (req) → `Card: PaymentTokenRequestCard?` (`Number`,`Expiry`,`SecurityCode`,`Name`,`BillingAddress`) | `PaymentTokenResponse` → `Id`, `Customer: CustomerResponse?`(`Id`), `PaymentSource.Card: CardPaymentTokenEntity?` → `LastDigits`, `Brand: CardBrand?`, `Expiry` | A `ApiException<CreatePaymentTokenError>`; `TryGetError` [400,403,404,422,500] · `TryGetRawError` | none | Vault.md; Models/PaymentTokenRequest.cs, PaymentTokenRequestPaymentSource.cs, PaymentTokenRequestCard.cs, PaymentTokenResponse.cs, PaymentTokenResponsePaymentSource.cs, CardPaymentTokenEntity.cs, Customer.cs |
| `Vault.ListCustomerPaymentTokens(ListCustomerPaymentTokensRequest)` | `CustomerId: string` (req); query `PageSize`,`Page`,`TotalRequired` | — | `CustomerVaultPaymentTokensResponse` → `PaymentTokens: IReadOnlyList<PaymentTokenResponse>?`, `TotalPages: int?`, `TotalItems: int?` | A `ApiException<ListCustomerPaymentTokensError>`; `TryGetError` [400,403,500] · `TryGetRawError` | **page-based** (`page`,`page_size`; `total_pages`) | Vault.md; Models/CustomerVaultPaymentTokensResponse.cs |
| `Vault.DeletePaymentToken(DeletePaymentTokenRequest)` | `Id: string` (req) | — | `void` (Task) | A `ApiException<DeletePaymentTokenError>`; `TryGetError` [400,403,500] · `TryGetRawError` | none | Vault.md; Requests/Vault/DeletePaymentTokenRequest.cs |
| `TransactionSearch.SearchTransactions(SearchTransactionsRequest)` | `StartDate: string` (req, ISO-8601), `EndDate: string` (req); query `Fields`(def `transaction_info`), `BalanceAffectingRecordsOnly`(def `Y`), `PageSize`(def 100,max 500), `Page`(def 1) | — | `SearchResponse` → `TransactionDetails[].TransactionInfo: TransactionInformation` → `TransactionId`,`InvoiceId`,`CustomField`,`TransactionAmount: Money?`,`FeeAmount`,`TransactionStatus`,`TransactionInitiationDate`,`TransactionEventCode`; `TotalPages: int?`,`TotalItems: int?`,`Page: int?` | **B** `ApiException<RawError>` (`StatusCode`,`ReadAsString()`,`ReadAsJson<T>()`) | **page-based** (`page`,`page_size`; `total_pages`); **max 31-day range per call** | TransactionSearch.md; Requests/TransactionSearch/SearchTransactionsRequest.cs; Models/SearchResponse.cs, TransactionDetails.cs, TransactionInformation.cs |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The `vault_id` used in `AuthorizeOrder` payment_source.card MUST be a token `Id` returned by `CreatePaymentToken` **and owned by the calling shopper** | `AuthorizeOrder` ← `CreatePaymentToken`/`ListCustomerPaymentTokens` | `OrderPaymentService.PayAsync`: resolve `paymentMethodId`→`SavedPaymentMethod` scoped to caller's `BuyerId`; reject if not found/not owner, before any PayPal call |
| `AuthorizationId` passed to Capture/Void/Reauthorize MUST be the one persisted from this order's `AuthorizeOrder` | Capture/Void/Reauthorize ← AuthorizeOrder | read `OrderPaymentInfo.AuthorizationId` off the persisted order; never trust a caller-supplied id |
| `CaptureId` passed to Refund MUST be the one persisted from this order's capture | RefundCapturedPayment ← CaptureAuthorizedPayment | read `OrderPaymentInfo.CaptureId` off the persisted order |
| `CustomerId` for `ListCustomerPaymentTokens` MUST be the PayPal customer id created for this shopper by the first `CreatePaymentToken` | List ← CreatePaymentToken | read `PayPalCustomerRef.PayPalCustomerId` for caller's `BuyerId`; if none, list is empty (no PayPal call) |
| Cumulative refunded amount MUST never exceed captured gross | RefundCapturedPayment (repeated) ← CapturedPayment | `OrderPaymentInfo.RefundAsync` checks `sum(refunds)+requested ≤ CapturedGross` before the PayPal call; reject `422`-style otherwise |

⚠ Derived from the task, not the map.

### Enums used (source: Models/Enums/*)

- `CheckoutPaymentIntent`: `Capture`(CAPTURE), `Authorize`(AUTHORIZE). Use `.Authorize`.
- `OrderStatus`: Created, Saved, Approved, Voided, Completed, PayerActionRequired. After AuthorizeOrder expect `Completed`; `PayerActionRequired` ⇒ browser challenge → **STOP & report** (task rule).
- `AuthorizationStatus`: Created, Captured, Denied, PartiallyCaptured, Voided, Pending. Active hold = `Created`.
- `CaptureStatus`: Completed, Declined, PartiallyRefunded, Pending, Refunded, Failed.
- `RefundStatus`: Cancelled, Failed, Pending, Completed.
- `CardBrand` (response only; not set on requests) — read via `Match`/`Value`.
- `TokenType`: only `BillingAgreement` — so **saved-card reuse uses `card.vault_id`, not `PaymentSource.Token`** (Token is billing-agreements).

### Client construction / auth / server node (source: sdk-map.md; Servers/ServerEnvironment.cs)

- Ctor: `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`.
- Auth: `options.Oauth2 = new OAuth2ClientCredentials { ClientId=…, ClientSecret=… }` (namespace `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`).
- `options.Environment = ServerEnvironment.Sandbox` (only member; token endpoint `…/v1/oauth2/token`).
- Base-URL override (`PayPal:BaseUrl`, when set): `options.Server.Default.Sandbox.BaseUrl = <value>` — one server group `Default`, sandbox node.
- Accessors: `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch`.

---

## 3. Trap notes (name the hazard + skill; do not resolve inline)

- **Client/HttpClient lifetime** (step 3): the `HttpClient`/handler pipeline must be long-lived & reused; getting singleton-vs-transient wrong leaks sockets or captures a stale handler. `MUST load dotnet-client-initialization`.
- **Credential application & 401 semantics** (step 3): an unset credential is *skipped*, so a blank secret surfaces as a silent 401 not an exception. `MUST load dotnet-authentication`.
- **Request-record vs body & the injected `Idempotency-Key`** (steps 3a–3h): the generator injects a per-call `Idempotency-Key: Guid.NewGuid()` that dedups nothing; the real key is `PayPalRequestId` on capture/refund records only. `MUST load dotnet-calling-endpoints`.
- **Enums & unions are not what they look like** (steps 3a–3f): `OpenStringEnum<T>` (no C# enum, no public ctor), unknown values reach `otherwise`; `AdditionalProperties` presence varies by model. `MUST load dotnet-models`.
- **Error taxonomy & the drift trap** (all SDK calls): Case A typed vs Case B raw differ; `ResponseDeserializationException` is an `ApiException` but **not** `ApiException<TError>`; `TryGetNoContent`/`TryGetRawError` mechanics. `MUST load dotnet-error-handling`.
- **Retry eligibility, per-attempt timeout, base-URL, pagination, logging redaction** (steps 3,3e,3f–3h): what `Timeout` bounds, which verbs the SDK resents, that `LogRequestBody` prints JSON unredacted. `MUST load dotnet-configuration-resilience`.
- **Test seam** (step 6): which constructor arg to fake so tests assert behaviour not SDK internals. `MUST load dotnet-testing`.

---

## 4. REQUIRED READING — load ALL before implementing (facts still come from map, not these)

| Skill (plugin `sdk-skills-test-dev`) | Governs step |
| --- | --- |
| `sdk-skills-test-dev:dotnet-client-initialization` | client + DI (step 3) |
| `sdk-skills-test-dev:dotnet-authentication` | credentials/fail-fast (step 3) |
| `sdk-skills-test-dev:dotnet-calling-endpoints` | every SDK call (3a–3h) |
| `sdk-skills-test-dev:dotnet-models` | request/response models (3a–3f) |
| `sdk-skills-test-dev:dotnet-error-handling` | error boundary (all) — always required |
| `sdk-skills-test-dev:dotnet-configuration-resilience` | retry/timeout/base-url/pagination/logging (3,3e) |
| `sdk-skills-test-dev:dotnet-testing` | tests (step 6) |

The sheet deliberately does not carry these skills' contents.

Mandatory hazard row: a drifted/malformed 2xx (missing `required`) or a non-2xx body not matching the
operation's `{Operation}Error` shape surfaces as `ResponseDeserializationException` — an `ApiException`
that is **not** `ApiException<TError>`; the catch ladder must also catch `ResponseDeserializationException`
(or `ApiException`), else it escapes.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalSettings` bound from `PayPal:` in `Infrastructure` DI; a startup validator (`ValidateOnStart`) throws if `ClientId`/`ClientSecret`/`Environment`/`Currency` are missing **or blank** (each part checked separately) — host refuses to start rather than 401 on first call. |
| 2 | Secret sourcing & rotation | Secrets from .NET user-secrets (dev) / env-provided config (prod); values never in repo. Options object is built **once at registration** and captured in the singleton client → a rotated secret needs a process restart (documented; no hot-reload required by task). |
| 3 | Total timeout budget | SDK `Timeout` is **per-attempt**; the caller-facing bound is a `CancellationToken` deadline (per request, ~30s) passed into every gateway call from the endpoint; recorded in `PayPalGateway`. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS → our POST writes (CreateOrder, AuthorizeOrder, Capture, Void, Refund, CreatePaymentToken) are **never auto-resent** by the SDK; DELETE (DeletePaymentToken) likewise. Idempotency for retried-by-us cases handled by `PayPalRequestId` + store claim (§9). Leave retry defaults; only GET (SearchTransactions) is retryable. |
| 5 | Idempotency & ambiguous writes | Capture & Refund carry the **real** `PayPalRequestId` key. Refund key = caller-supplied idempotency key (task requirement). Capture key = deterministic `order-{id}-capture`. CreateOrder/AuthorizeOrder carry deterministic `PayPalRequestId` (`order-{id}-create` / `-authorize`, 6h window) so a double-click reuses the same PayPal order/hold. Void has no key → guarded by store claim + status read-back. |
| 6 | Observability | Structured logs at Info (operation + eShop orderId + PayPal id + status), Warning (renewed auth), Error (Case A/B translated). `LogRequestBody` **off**. The PayPal `debug_id`/correlation id from error bodies is read via error accessors and logged. |
| 7 | Sensitive data | Card number/CVV/expiry flow through `CardRequest`/`PaymentTokenRequestCard` request models → **`LogRequestBody` stays off AND `options.Logging.LoggerFactory` is set explicitly** so `PAYPALSERVERSDKCLIENT_LOG` cannot switch body logging on externally. Our own code never logs card fields; only last-4/brand from responses is persisted/returned. Full PAN never stored in our DB. |
| 8 | Environment selection | One server group `Default`; SDK declares only `ServerEnvironment.Sandbox`. `PayPal:Environment=sandbox` → `Sandbox`. For any non-sandbox deployment the SDK exposes no live environment member, so `PayPal:BaseUrl` **must** be supplied and is applied verbatim to `options.Server.Default.Sandbox.BaseUrl` (also honored in sandbox when set) — this is how test vs live traffic is separated. All dev/test targets sandbox. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. Store claim = `Order` status transition guarded by an EF **concurrency token** (`RowVersion`); on SQL Server the second concurrent write throws `DbUpdateConcurrencyException`. ⚠ The task-mandated **in-memory provider does not enforce concurrency tokens**, so in that dev configuration the effective guard degrades to the deterministic `PayPalRequestId` (PayPal dedups the duplicate authorize/create/capture); this is documented, not silent. |
| 10 | Partial results | Reconciliation (`SearchTransactions`) and saved-card list (`ListCustomerPaymentTokens`) are paged; both loop **all** pages via `total_pages` and the report/response exposes a `Complete`/coverage indicator + windows covered (see PAGED READS) so a truncated result is visible in the payload, not just logs. |
| 11 | Unknown outcomes | Each write's `catch` for `SdkConnectionException`/`SdkTimeoutException` re-reads provider state (GetOrder / GetAuthorizedPayment / GetCapturedPayment / GetRefund / ListCustomerPaymentTokens) by the reference we already persisted, and settles the order/payment state from that rather than reporting a blind failure. See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Authorize (pay) | `Order` row (Status + `ConcurrencyToken` Guid, `.IsConcurrencyToken()`) in Catalog DB (the store this app owns) | store: `DbUpdateConcurrencyException` on the second SaveChanges after both read `AwaitingPayment` (SQL); + `PayPalRequestId` derived from `Order.PaymentReference` beside it | `OrderPaymentService.PayAsync` catch of `DbUpdateConcurrencyException` | `OrderPaymentService.PayAsync` — `order.RecordAuthorization(...)` (bumps token) then `_orderRepository.UpdateAsync`, before which the status guard runs |
| Capture (fulfil) | `Order` row (Status `Authorized`→`Fulfilled` + `ConcurrencyToken`) | store: `DbUpdateConcurrencyException` (SQL); + `PayPalRequestId` = `{PaymentReference:N}-capture` | `OrderPaymentService.FulfilAsync` / `SaveWithConcurrencyRetryAsync` catch of `DbUpdateConcurrencyException` | `OrderPaymentService.FulfilAsync` — `order.RecordCapture(...)` then `SaveWithConcurrencyRetryAsync(order, orderId, Fulfilled)` |
| Cancel (void) | `Order` row (Status `Authorized`→`Cancelled` + `ConcurrencyToken`) | store: `DbUpdateConcurrencyException` (SQL) | `OrderPaymentService.CancelAsync` / `SaveWithConcurrencyRetryAsync` | `OrderPaymentService.CancelAsync` — `order.RecordCancellation()` then `SaveWithConcurrencyRetryAsync(order, orderId, Cancelled)` |
| Refund | `OrderRefund` owned row, unique index on (`OrderId`,`IdempotencyKey`) | store: unique-index violation `DbUpdateException` on the second insert with the same caller key (SQL); + `PayPalRequestId` = `{PaymentReference:N}-{key}` | `OrderPaymentService.RefundAsync` catch of `DbUpdateException` (`IsUniqueViolation`) | `OrderPaymentService.RefundAsync` — `FindRefundByKey` short-circuit + `order.RecordRefund(key,...)` then `UpdateAsync`; catch loads the earlier refund |
| Save card | `PayPalCustomerRef`/`SavedPaymentMethod` insert under caller `BuyerId` | dedup is not required by task (each save = distinct vault token); no duplicate-suppression claim — n/a | n/a | n/a |

⚠ In-memory provider caveat (row 9) applies to the concurrency-token rows; the SQL target enforces them.

**PAGED READS**

| Read | What caps it | How the caller learns it was cut short | Where in the code |
| --- | --- | --- | --- |
| `SearchTransactions` (reconciliation) | 31-day range/call + `page_size` (max 500) | `PayPalReconciliationResult.Complete` → `ReconciliationReport.PayPalDataComplete` → `ReconciliationResponse.PayPalDataComplete` in the response body (false if any window/page failed) | `PayPalGateway.SearchTransactionsAsync` sets `Complete`; surfaced by `ReconciliationService.ReconcileAsync` and `ReconciliationEndpoint` |
| `ListCustomerPaymentTokens` (saved cards) | `page_size` (max 5) + `total_pages` (max 10) | `PayPalGateway.ListVaultedCardsAsync` loops all pages before returning; a page fetch failure throws (surfaces as an error, not a silent short list) | `PayPalGateway.ListVaultedCardsAsync` page loop |

**UNKNOWN OUTCOMES**

| Write | Re-read with | Reference searched by | Where in the code | Test that fails the connection |
| --- | --- | --- | --- | --- |
| CreateOrder/AuthorizeOrder | `Orders.GetOrder` | PayPal order id (known in-gateway after CreateOrder) | `PayPalGateway.AuthorizeAsync` catch → `TrySettleAuthorizationAsync` (re-reads via `GetOrderAsync`); if still unknown, throws `OutcomeUnknown` | `PayPalGateway.AuthorizeAsync` / `TrySettleAuthorizationAsync` |
| CaptureAuthorizedPayment | `Orders.GetOrder` (captures) | persisted PayPal order id | `OrderPaymentService.FulfilAsync` catch of `OutcomeUnknown` → `_gateway.GetOrderAsync(...).Capture` settles it, else rethrow | `OrderPaymentService.FulfilAsync` `catch (PaymentGatewayException ex) when (ex.OutcomeUnknown)` |
| RefundCapturedPayment | re-issue with the same `PayPalRequestId` (PayPal dedups 45d) | composed `{PaymentReference:N}-{key}` | `OrderPaymentService.RefundAsync` catch of `OutcomeUnknown` re-issues once with the same key to settle | `OrderPaymentService.RefundAsync` `catch (PaymentGatewayException ex) when (ex.OutcomeUnknown)` |
| VoidPayment | `Payments.GetAuthorizedPayment` | persisted authorization id | `OrderPaymentService.CancelAsync` catch of `OutcomeUnknown` → `GetAuthorizationAsync`; proceeds if `VOIDED`, else rethrow | `OrderPaymentService.CancelAsync` `catch (PaymentGatewayException ex) when (ex.OutcomeUnknown)` |
| CreatePaymentToken | (vault token id echoed on success) | — | `PaymentMethodService.SaveCardAsync` — a transport failure surfaces `OutcomeUnknown`; a re-save creates a new token (no duplicate charge), reconcilable via `ListVaultedCardsAsync` | `PaymentMethodService.SaveCardAsync` (no charge occurs on vault; safe to retry) |

All `TBD` cells replaced with the concrete members after the code compiled (Step 3.5 of the workflow). Verified live against the PayPal sandbox: authorize→capture→refund, saved-card reuse, void, and reconciliation.

---

## 6. Assumptions & Blockers

- **No Blockers.** Every required capability maps to an operation.
- **Assumptions (minor — proceed):**
  - Order total & currency: amount = `Order.Total()` (sum of `OrderItem.UnitPrice*Units`, snapshotted from basket), formatted to the currency's minor units (USD ⇒ 2 dp) as PayPal `value` string; currency from `PayPal:Currency`. Task: amount = catalog prices; currency from config.
  - `POST /api/orders` builds the order directly from posted `{catalogItemId, quantity}` items (identity from token), snapshotting live catalog price into `OrderItem`/`CatalogItemOrdered` — reusing `Order`/`OrderItem`, not a parallel model. A placeholder `ShipToAddress` is set (aggregate requires one; no address endpoint in scope).
  - Reconciliation matches PayPal transactions to eShop orders by `invoice_id`/`custom_field` = eShop order reference (set as `PurchaseUnitRequest.CustomId`/`InvoiceId` at create) and by persisted capture id; unmatched-either-way rows are reported both directions.
  - Non-sandbox environments require `PayPal:BaseUrl` (SDK has no live env member) — YOUR-CALL config decision, recorded in §5.8.
  - SDK vendored as source project (not NuGet, not the temp clone) — YOUR-CALL build decision.
