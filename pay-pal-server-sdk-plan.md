# PayPal Server SDK integration plan — eShopOnWeb PublicApi

Additive payments + saved cards on `src/PublicApi`. PayPal Server SDK (.NET, root ns `PayPalServerSdk`,
spec 2.29). Every contract fact below is grounded in the SDK map / map-named source; rows labelled
`YOUR CALL` are application decisions I own.

## 1. Scope & sequence

New project **`src/PayPalServerSdk`** = the SDK source vendored from its repo (built from source; not on NuGet),
`<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` so its inline package versions win over
the repo's `Directory.Packages.props`. Referenced by Infrastructure via `ProjectReference`.

1. **Vendor SDK + build wiring**: copy SDK source into `src/PayPalServerSdk`, add ProjectReference from Infrastructure,
   set `global.json` `rollForward: latestMajor`.
2. **Domain (ApplicationCore)**: `OrderPayment` aggregate (payment+fulfilment state), `PaymentRefund` (owned),
   `PaymentOperation` (idempotency claim, aggregate root), `SavedCard` aggregate; enum `PaymentState`; interfaces
   `IPayPalPaymentService`, `IVaultService`, `IReconciliationService`.
3. **Infrastructure**: EF config for new entities on `CatalogContext`; PayPal client DI (`AddPayPalServerSdkClient`);
   `PayPalOptions` bind + fail-fast validator; service implementations calling the SDK; error-translation.
4. **PublicApi**: endpoints (MinimalApi.Endpoint style like `CreateCatalogItemEndpoint`), request/response DTOs,
   identity/ownership from JWT `ClaimTypes.Name`.
5. **Tests** (`PublicApiIntegrationTests`/`UnitTests`): fake the SDK's `HttpClient` seam.
6. Secrets → `dotnet user-secrets` (PublicApi) from env vars; nothing written to repo.

Operations used (all `client.X`): Orders.**CreateOrder**, Orders.**AuthorizeOrder**, Orders.**GetOrder**;
Payments.**CaptureAuthorizedPayment**, **ReauthorizePayment**, **VoidPayment**, **RefundCapturedPayment**,
**GetAuthorizedPayment**, **GetCapturedPayment**, **GetRefund**; Vault.**CreatePaymentToken**, **DeletePaymentToken**,
**GetPaymentToken**; TransactionSearch.**SearchTransactions**.

Endpoint → PayPal mapping:
- `POST /api/orders` → app only (creates `Order` via existing model + `OrderPayment` in `AwaitingPayment`).
- `POST /api/orders/{id}/pay` → CreateOrder(intent=AUTHORIZE) then AuthorizeOrder(payment_source.card = one-off card **or** `{vault_id}`).
- `POST /api/orders/{id}/fulfil` (admin) → if auth stale: ReauthorizePayment; then CaptureAuthorizedPayment.
- `POST /api/orders/{id}/cancel` (admin) → VoidPayment.
- `POST /api/orders/{id}/refunds` → RefundCapturedPayment.
- `GET /api/my-orders` → app read.
- `GET /api/reconciliation?from&to` (admin) → SearchTransactions (all pages) vs `OrderPayment` rows.
- `POST /api/payment-methods` → CreatePaymentToken(card); `GET` → app read; `DELETE` → DeletePaymentToken.

## 2. CONTRACT SHEET

⚠ **Signatures are generated code, verbatim.** Each operation that takes input takes exactly **ONE request record** as
its first parameter, built with an object initializer using the record's own property names — never flat args. An op
with no inputs takes none.
⚠ **Every SDK type is fully-qualified by the namespace its source path implies** (`Models/`→`PayPalServerSdk.Models`,
`Models/Enums/`→`PayPalServerSdk.Models.Enums`, `Errors/`→`PayPalServerSdk.Errors`,
`Requests/<Ctrl>/`→`PayPalServerSdk.Requests.<Ctrl>`, client/options→`PayPalServerSdk`,
`Servers/`→`PayPalServerSdk.Servers`), taken from THAT type's path, not a neighbour's.

| Op (client.X) | Method signature (request record + required members) | Body model + fields read/written | Response envelope + inner fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| Orders.CreateOrder | `CreateOrder(CreateOrderRequest{ required Body; PayPalRequestId?; Prefer="return=minimal" })` | `OrderRequest{ required Intent(CheckoutPaymentIntent); required PurchaseUnits: IReadOnlyList<PurchaseUnitRequest>; PaymentSource? }` → write Intent=Authorize, PurchaseUnits=[{Amount, CustomId, InvoiceId}] | `Order{ Id; Status(OrderStatus) }` read Id, Status | A `ApiException<CreateOrderError>`; `TryGetError(out Error)`[400,401,422]·`TryGetRawError(out RawError)` | none | map/operations/Orders.md; Models/OrderRequest.cs |
| Orders.AuthorizeOrder | `AuthorizeOrder(AuthorizeOrderRequest{ required Id; Body?(OrderAuthorizeRequest); PayPalRequestId?; Prefer })` | `OrderAuthorizeRequest{ PaymentSource?(PaymentSource) }` — write PaymentSource.Card (CardRequest one-off or {VaultId}); Prefer="return=representation" | `OrderAuthorizeResponse{ Id; Status; PurchaseUnits[].Payments.Authorizations[]{ Id; Status(AuthorizationStatus); Amount; ExpirationTime } }` | A `ApiException<AuthorizeOrderError>`; `TryGetError`[400,401,403,404,422,500]·`TryGetRawError` | none | Orders.md; Models/OrderAuthorizeResponse.cs; Models/PaymentSource.cs; Models/CardRequest.cs |
| Orders.GetOrder | `GetOrder(GetOrderRequest{ required Id; Fields? })` | — (query `fields`←Fields) | `Order{ Status; PurchaseUnits[].Payments.{Authorizations,Captures,Refunds} }` | A `ApiException<GetOrderError>`; `TryGetError`[401,404]·`TryGetRawError` | none | Orders.md; Models/Order.cs |
| Payments.CaptureAuthorizedPayment | `CaptureAuthorizedPayment(CaptureAuthorizedPaymentRequest{ required AuthorizationId; Body?(CaptureRequest); PayPalRequestId?; Prefer })` | `CaptureRequest{ Amount?(Money); FinalCapture?=false; InvoiceId? }` — write Amount(order total), FinalCapture=true, InvoiceId; Prefer="return=representation" | `CapturedPayment{ Id; Status(CaptureStatus); Amount(Money); SellerReceivableBreakdown{ GrossAmount(Money); PaypalFee(Money?); NetAmount(Money?) } }` | A `ApiException<CaptureAuthorizedPaymentError>`; `TryGetError`[400,401,403,404,409,422]·`TryGetNoContent(out RawError)`[500]·`TryGetRawError` | none | Payments.md; Models/CapturedPayment.cs; Models/SellerReceivableBreakdown.cs |
| Payments.ReauthorizePayment | `ReauthorizePayment(ReauthorizePaymentRequest{ required AuthorizationId; Body?(ReauthorizeRequest); PayPalRequestId?; Prefer })` | `ReauthorizeRequest{ Amount?(Money) }` — write Amount(order total) | `PaymentAuthorization{ Id; Status(AuthorizationStatus); ExpirationTime; Amount }` | A `ApiException<ReauthorizePaymentError>`; `TryGetError`[400,401,403,404,422]·`TryGetNoContent`[500]·`TryGetRawError` | none | Payments.md; Models/ReauthorizeRequest.cs; Models/PaymentAuthorization.cs |
| Payments.VoidPayment | `VoidPayment(VoidPaymentRequest{ required AuthorizationId; PayPalRequestId?; Prefer })` | — | `PaymentAuthorization{ Id; Status(AuthorizationStatus=Voided) }` | A `ApiException<VoidPaymentError>`; `TryGetError`[401,403,404,409,422]·`TryGetNoContent`[500]·`TryGetRawError` | none | Payments.md |
| Payments.RefundCapturedPayment | `RefundCapturedPayment(RefundCapturedPaymentRequest{ required CaptureId; Body?(RefundRequest); PayPalRequestId?; Prefer })` | `RefundRequest{ Amount?(Money); InvoiceId?; NoteToPayer? }` — write Amount for partial (omit for full); Prefer="return=representation" | `Refund{ Id; Status(RefundStatus); Amount(Money) }` read Id | A `ApiException<RefundCapturedPaymentError>`; `TryGetError`[400,401,403,404,409,422]·`TryGetNoContent`[500]·`TryGetRawError` | none | Payments.md; Models/RefundRequest.cs; Models/Refund.cs |
| Payments.GetAuthorizedPayment | `GetAuthorizedPayment(GetAuthorizedPaymentRequest{ required AuthorizationId })` | — | `PaymentAuthorization{ Id; Status }` | A `ApiException<GetAuthorizedPaymentError>`; `TryGetError`[401,403,404]·`TryGetNoContent`[500]·`TryGetRawError` | none | Payments.md |
| Payments.GetCapturedPayment | `GetCapturedPayment(GetCapturedPaymentRequest{ required CaptureId })` | — | `CapturedPayment{ Id; Status; SellerReceivableBreakdown }` | A `ApiException<GetCapturedPaymentError>`; `TryGetError`[401,403,404]·`TryGetNoContent`[500]·`TryGetRawError` | none | Payments.md |
| Payments.GetRefund | `GetRefund(GetRefundRequest{ required RefundId })` | — | `Refund{ Id; Status; Amount }` | A `ApiException<GetRefundError>`; `TryGetError`[401,403,404]·`TryGetNoContent`[500]·`TryGetRawError` | none | Payments.md |
| Vault.CreatePaymentToken | `CreatePaymentToken(CreatePaymentTokenRequest{ required Body(PaymentTokenRequest); PayPalRequestId? })` | `PaymentTokenRequest{ Customer?(Customer{Id?}); required PaymentSource(PaymentTokenRequestPaymentSource{ Card?(PaymentTokenRequestCard{Number,Expiry,SecurityCode,Name,BillingAddress}) }) }` | `PaymentTokenResponse{ Id; Customer(CustomerResponse); PaymentSource.Card(CardPaymentTokenEntity{ LastDigits; Brand(CardBrand); Expiry; Name }) }` | A `ApiException<CreatePaymentTokenError>`; `TryGetError`[400,403,404,422,500]·`TryGetRawError` | none | Vault.md; Models/PaymentTokenRequest.cs; Models/PaymentTokenRequestCard.cs; Models/CardPaymentTokenEntity.cs |
| Vault.DeletePaymentToken | `DeletePaymentToken(DeletePaymentTokenRequest{ required Id })` | — | `void` (Task) | A `ApiException<DeletePaymentTokenError>`; `TryGetError`[400,403,500]·`TryGetRawError` | none | Vault.md |
| Vault.GetPaymentToken | `GetPaymentToken(GetPaymentTokenRequest{ required Id })` | — | `PaymentTokenResponse` | A `ApiException<GetPaymentTokenError>`; `TryGetError`[403,404,422,500]·`TryGetRawError` | none | Vault.md |
| TransactionSearch.SearchTransactions | `SearchTransactions(SearchTransactionsRequest{ required StartDate; required EndDate; Fields="transaction_info"; BalanceAffectingRecordsOnly="Y"; PageSize=100; Page=1 })` | — (query wire names per row) | `SearchResponse{ TransactionDetails[].TransactionInfo{ TransactionId; TransactionAmount(Money); TransactionStatus; InvoiceId; FeeAmount }; Page; TotalPages; TotalItems }` | **B** `ApiException<RawError>` (StatusCode·ReadAsString·ReadAsJson<T>) | **page/page_size** (walk Page 1..TotalPages) | TransactionSearch.md; Models/SearchResponse.cs; Models/TransactionInformation.cs |

**Enums (Models/Enums/, use static members; `.Value` = wire string):**
- `CheckoutPaymentIntent.Authorize`("AUTHORIZE") / `.Capture`.
- `OrderStatus`: Created, Approved, Completed, Voided, PayerActionRequired, Saved.
- `AuthorizationStatus`: Created, Captured, Denied, PartiallyCaptured, Voided, Pending.
- `CaptureStatus`: Completed, Declined, PartiallyRefunded, Pending, Refunded, Failed.
- `RefundStatus`: Cancelled, Failed, Pending, Completed.
- `CardBrand` (read only, persist `.Value`).

**Client construction / auth / servers:**
- DI: `services.AddPayPalServerSdkClient(o => { o.Oauth2 = new OAuth2ClientCredentials{ ClientId, ClientSecret }; o.Environment = ServerEnvironment.Sandbox; if(baseUrl set) o.Server.Default.Sandbox.BaseUrl = baseUrl; o.Logging=…; })` (registers client **singleton**; source ServiceCollectionExtensions.cs).
- `OAuth2ClientCredentials{ required ClientId; required ClientSecret; Scope? }` (Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs).
- Token URL derives from `o.Server.Default.Sandbox.BaseUrl` (AuthSchemes.cs:17 → `server.Default("/v1/oauth2/token")`; DefaultOptions.cs) — so setting BaseUrl covers **token + all calls** (satisfies `PayPal:BaseUrl` verbatim-for-every-call).
- Only environment is `ServerEnvironment.Sandbox` (Servers & auth).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `pay` may charge a saved card only if the named `paymentMethodId` is a `SavedCard` owned by the caller; its `PayPalVaultId` feeds `CardRequest.VaultId` | AuthorizeOrder ← (app SavedCard store, seeded by CreatePaymentToken) | in pay service, before AuthorizeOrder |
| `fulfil`/`cancel`/`refund` act on the `AuthorizationId`/`CaptureId` that this order's own `pay`/`fulfil` produced | Capture/Void/Reauthorize ← AuthorizeOrder; Refund ← CaptureAuthorizedPayment | reads OrderPayment by orderId; ids are app-owned, never caller-supplied |
| Refund `Amount` must be ≤ (CapturedAmount − sum(prior succeeded refunds)) | RefundCapturedPayment ← CaptureAuthorizedPayment | in refund service, before RefundCapturedPayment |
| Delete removes the SavedCard so a later `pay` can no longer name it | DeletePaymentToken + app delete ← CreatePaymentToken | delete service removes row (+ vault) so ownership lookup fails |

## 3. Trap notes (name the hazard + skill; do not resolve here)

- **Client/HttpClient lifetime & DI**: whether the SDK client is singleton vs the HttpClient pipeline reuse — getting this wrong leaks sockets or captures stale config. → **MUST load dotnet-client-initialization**.
- **Credential application timing / where secrets come from**: a mis-timed credential set surfaces only as a runtime 401. → **MUST load dotnet-authentication**.
- **Building request records / which optional fields gate acceptance**: `required?` selects nothing on some records; the injected `Idempotency-Key` header is NOT a key. → **MUST load dotnet-calling-endpoints**.
- **Enums are `OpenStringEnum` not C# enums; unions via TryGet; unknown-field bag**: constructing/reading `CheckoutPaymentIntent`/`CardBrand`/statuses wrong won't serialize. → **MUST load dotnet-models**.
- **Error surface**: Case A typed vs Case B raw mixing; `TryGetNoContent` on Payments ops for 500; `ResponseDeserializationException` escapes a `ApiException<TError>`-only ladder. → **MUST load dotnet-error-handling**.
- **Retries/timeouts/pagination/logging**: `Timeout` is per-attempt not total; `POST` not resent by default; `LogRequestBody` logs JSON unredacted; only a `CancellationToken` bounds a whole call. → **MUST load dotnet-configuration-resilience**.
- **Test seam**: the `HttpClient` ctor arg is the fake point; don't stub SDK internals. → **MUST load dotnet-testing**.

## 4. REQUIRED READING (load ALL before implementing; sheet omits their contents)

| Skill (plugin `sdk-skills-test-dev`) | Step it governs |
| --- | --- |
| `sdk-skills-test-dev:dotnet-client-initialization` | PayPal client construction + DI (Infrastructure) |
| `sdk-skills-test-dev:dotnet-authentication` | OAuth2 client-credentials wiring + fail-fast |
| `sdk-skills-test-dev:dotnet-calling-endpoints` | every `client.{Ctrl}.{Op}` call + request records |
| `sdk-skills-test-dev:dotnet-models` | request/response models, enums, unions |
| `sdk-skills-test-dev:dotnet-error-handling` | error boundary / translation (always required) |
| `sdk-skills-test-dev:dotnet-configuration-resilience` | retries, per-attempt timeout, total budget, pagination, logging |
| `sdk-skills-test-dev:dotnet-testing` | integration-layer tests (HttpClient seam) |

**Mandatory hazard row**: a drifted/malformed **2xx** (missing `required` member) or a **non-2xx** body that doesn't match
the operation's generated `{Operation}Error` shape surfaces as **`ResponseDeserializationException`** — an `ApiException`
that keeps HTTP status + target type but is **not** `ApiException<TError>`. A ladder catching only `ApiException<TError>`
lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` bound from `PayPal:` section; a hosted `IStartupFilter`/validated-options check throws at startup if `ClientId`, `ClientSecret`, or `Currency` is null/blank (each part checked separately — blank ≠ missing). Host refuses to start rather than 401 on first call. |
| 2 | Secret sourcing & rotation | Secrets from **user-secrets** (loaded from env by me; never in repo). DI builds the options object **once at registration** and captures it in the singleton client → a rotated secret needs a process restart; documented, acceptable for this app (no hot-rotation requirement). |
| 3 | Total timeout budget | SDK `Timeout` is **per-attempt**. Each service call passes a `CancellationToken` from `CancellationTokenSource(TimeSpan)` (default 30s) as the whole-call deadline; retries bounded via `RetryOptions.MaxRetries`. The CT is the only whole-call bound. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS → our POST writes (CreateOrder/Authorize/Capture/Void/Refund/CreatePaymentToken) and DELETE are **never auto-resent** by the SDK. Reads (GET) may retry. We keep default (do not add POST to retry set); duplicate-safety comes from PayPalRequestId + app claim, not retry suppression. |
| 5 | Idempotency & ambiguous writes | Real caller/derived key = **`PayPalRequestId`** on each write, based on the **unique `InvoiceId`** (`ESHOP-{orderId}-{guid}`): create=`create-{InvoiceId}`, authorize=`auth-{InvoiceId}`, capture=`cap-{InvoiceId}`, void=`void-{InvoiceId}`, reauthorize=`reauth-{InvoiceId}`; refund=**caller-supplied key**. Beside it, an app `PaymentOperation`/`PaymentRefund` claim row (see DUPLICATE CLAIMS). Injected `Idempotency-Key` header is ignored. ⚠ **A per-run-unique base is required** (not `payment.Id`): the in-memory provider restarts ids at 1, so `create-1` would replay a prior run's already-captured PayPal order — **observed 422 `UNPROCESSABLE_ENTITY`**; `InvoiceId` carries a fresh guid so it is stable per order within a run yet unique across restarts. |
| 6 | Observability | Structured logs at Information (op start/result: orderId, paypalOrderId, authId, captureId, refundId, status) and Error (translated failure incl. PayPal `Error.DebugId` correlation id from `TryGetError`). `LogRequestBody` stays **off**. |
| 7 | Sensitive data | Pay/save-card requests carry PAN/CVV (`CardRequest`/`PaymentTokenRequestCard`). Therefore `options.Logging.LogRequestBody` stays **off** AND `options.Logging.LoggerFactory` is **set explicitly** in DI so `PAYPALSERVERSDKCLIENT_LOG` cannot switch body logging on externally. App never logs card fields; only stores brand+last4+expiry (never PAN/CVV) — no full card in DB or logs. |
| 8 | Environment selection | One server group `Default`, one env `Sandbox`. All deployments target Sandbox here (`PAYPAL_ENVIRONMENT=sandbox`). `PayPal:BaseUrl` optional override → sets `o.Server.Default.Sandbox.BaseUrl` (covers token + calls). No live env exists in this SDK, so no risk of test traffic hitting live. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. App-owned claim rows with unique index refuse the 2nd write (SQL Server prod); in the forced in-memory provider the deterministic PayPalRequestId is the money-layer backstop (documented env caveat, not the design). |
| 10 | Partial results | Reconciliation walks all pages; report DTO carries `pagesFetched`, `totalPages`, and `complete` (bool) so a truncated result is visible in the **return value**, not just logs. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| pay (authorize) | `PaymentOperations` table, unique index (OrderPaymentId, OperationType="AUTHORIZE") | unique-index insert violation (`DbUpdateException`) on 2nd insert, done **before** the SDK call | catch `DbUpdateException` → return current OrderPayment state | `OrderPaymentService.TryClaimAsync(id, Authorize)` called in `PayAsync` before `_gateway.AuthorizeOrderAsync`; returns null → `PayAsync` returns current summary |
| fulfil (capture) | `PaymentOperations`, unique index (OrderPaymentId, "CAPTURE") | unique-index insert violation before capture call | catch `DbUpdateException` → return current state | `OrderPaymentService.TryClaimAsync(id, Capture)` in `FulfilAsync` before `_gateway.CaptureAsync` |
| cancel (void) | `PaymentOperations`, unique index (OrderPaymentId, "VOID") | unique-index insert violation before void call | catch `DbUpdateException` → return current state | `OrderPaymentService.TryClaimAsync(id, Void)` in `CancelAsync` before `_gateway.VoidAsync` |
| refund | `PaymentRefunds` table, unique index (OrderPaymentId, IdempotencyKey=caller key) | unique-index insert violation before refund call | catch `DbUpdateException` → return the existing refund for that key | `OrderPaymentService.RefundAsync`: `payment.AddRefund` + `_paymentRepository.UpdateAsync` (inserts the claim) before `_gateway.RefundAsync`; also a prior in-memory read of `payment.Refunds` by key short-circuits |
| save card | `SavedCards` table, unique index (BuyerId, PayPalVaultId) | unique-index insert violation after vault call | catch `DbUpdateException` → return existing SavedCard | `SavedCardService.SaveCardAsync`: `_repository.AddAsync(saved)` |

(⚠ In-memory EF provider does not enforce unique indexes; SQL Server — the production connection string this repo ships —
does. The `PayPalRequestId` sent to PayPal is the money-layer dedup that holds regardless of provider.)

**PAGED READS**

| Read | What caps it | How the caller learns it was cut short | Where in the code |
| --- | --- | --- | --- |
| reconciliation SearchTransactions | walk Page=1..`SearchResponse.TotalPages`; safety cap `MaxPages=1000` | report DTO field `Complete: bool` (+ `PagesFetched`,`TotalPages`) in the returned body | `PayPalGateway.SearchTransactionsAsync` sets `TransactionSearchResult.Complete = fetched >= totalPages`; `ReconciliationService.ReconcileAsync` surfaces it as `ReconciliationReport.Complete` (verified: 12/12 pages, complete=true) |

**UNKNOWN OUTCOMES**

Settle mechanism = **idempotent replay under the deterministic `PayPalRequestId`** (keyed on the unique `InvoiceId`, above). On a transport failure the app-owned claim is released so a retry is possible, and because the same request id replays the same PayPal call (6h for create/authorize, 45d for capture/void/refund) the outcome is settled on retry rather than duplicated. `GetAuthorizationAsync` (GET, `AuthorizationId`) is available for an operator to read the live state.

| Write | Settle path | Reference | Where in the code | Test that fails the connection |
| --- | --- | --- | --- | --- |
| CreateOrder / AuthorizeOrder | release claim, leave `AwaitingPayment`; retry pay replays `create-{InvoiceId}`/`auth-{InvoiceId}` idempotently | `InvoiceId` (deterministic key) | `OrderPaymentService.PayAsync` `catch { ReleaseClaimAsync(claim) }` before rethrow | `PayPalGatewayTests` stubs a connection fault on authorize → `PaymentException` (provider-unavailable) surfaces, no partial state persisted |
| CaptureAuthorizedPayment | release CAPTURE claim; retry fulfil replays `cap-{InvoiceId}` idempotently | `InvoiceId` | `OrderPaymentService.FulfilAsync` `catch { ReleaseClaimAsync(claim) }` | gateway test: capture connection fault → `PaymentException.ProviderUnavailable` |
| VoidPayment | release VOID claim; retry cancel replays `void-{InvoiceId}`; 204 empty body treated as success | `InvoiceId` | `OrderPaymentService.CancelAsync` `catch`; `PayPalGateway.VoidAsync` swallows 2xx `ResponseDeserializationException` | gateway test asserts 2xx-empty void = success |
| RefundCapturedPayment | keep the `PaymentRefund` row (marked FAILED on error, so it neither double-refunds under the same key nor holds refundable amount); retry with a new key, or same key replays idempotently (45d) | caller idempotency key | `OrderPaymentService.RefundAsync` `catch { refund.MarkFailed(); UpdateAsync }` | gateway test: refund connection fault → `PaymentException` |

## 6. Assumptions & Blockers

- **Assumption (minor):** "operator" = existing `Administrators` role (`BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS`),
  used by this project's privileged endpoints (`CreateCatalogItemEndpoint`). Fulfil/cancel/reconciliation restricted to it.
- **Assumption (minor):** caller identity = JWT `ClaimTypes.Name` (email), used as `Order.BuyerId` (matches eShop's buyer id convention).
- **Assumption (minor):** currency for all amounts = `PayPal:Currency` (`USD` here); amounts from catalog `Price × Units`, formatted to 2 dp.
- **Assumption:** direct card payment on the sandbox business account returns no 3DS challenge (task states account is enabled for
  direct card processing). If AuthorizeOrder returns `PAYER_ACTION_REQUIRED`/a `payer-action` link, the pay service throws a
  clear "challenge required" error and I STOP-and-report (no browser round-trip built).
- **No Blockers**: every required capability (authorize/hold, capture-with-fee-breakdown, void, partial/full refund with caller
  idempotency key, vault card + list + delete, transaction search for reconciliation) is covered by the map.

### Contract facts confirmed at runtime (sandbox)

- **`VoidPayment` returns `204 No Content` (empty body) on success**, but its generated return type is `PaymentAuthorization`,
  so the SDK raises `ResponseDeserializationException` on the *success* path. `PayPalGateway.VoidAsync` treats a `2xx`
  `ResponseDeserializationException` as success. (Verified: void → 204, order becomes `Cancelled`/`VOIDED`.)
- A refund issued **immediately after capture** can return `400` (settlement still in flight); retried after a few seconds it
  succeeds. The failed `PaymentRefund` row is kept (status FAILED, excluded from refundable) so a same-key repeat never
  double-refunds; a new key retries.
- Direct card authorization on the sandbox business account returned **no 3-D Secure challenge** (as the task states), so the
  browserless create→authorize path holds funds without an approval round-trip.
- Verified end to end on the sandbox with Visa `4111 1111 1111 1111`: authorize → capture (fee/net reported) → partial refund
  (idempotent repeat + second distinct partial + over-refund rejected); saved card reused to pay & fulfil a second order;
  cancel/void; reconciliation over a full multi-page range; and shopper-ownership / operator-role enforcement (404/403).
