# PayPal Server SDK (.NET) integration plan — eShopOnWeb PublicApi

PayPal payments + saved cards, added to `src/PublicApi`. Direct card processing (SAQ-D sandbox
business account), authorize-at-checkout / capture-at-fulfilment / refund-after. SDK is the
APIMatic-generated **PayPal Server SDK** (.NET, root namespace `PayPalServerSdk`, spec `2.29`),
built from source and referenced (not on NuGet).

---

## 1. Scope & sequence

| # | Step | PayPal operations used |
| --- | --- | --- |
| A | SDK ProjectReference into PublicApi; config binding `PayPal:*` + fail-fast; DI-register `PayPalServerSdkClient` via `IHttpClientFactory` | — (client & auth) |
| B | Domain: `OrderPayment` aggregate (+ child `PaymentRefund`), `SavedPaymentMethod` aggregate; EF configs + DbSets on `CatalogContext`; migration | — |
| C | `IPayPalPaymentService` (SDK boundary + error translation) | all below |
| D | `POST /api/orders` — place eShop Order (reuse `Order`/`OrderItem`), create `OrderPayment` in `PendingPayment` | — (local only) |
| E | `POST /api/orders/{orderId}/pay` — authorize the total (hold, not capture); one-off card **or** saved-card vault_id | `Orders.CreateOrder` (intent AUTHORIZE, card in payment_source) → `Orders.AuthorizeOrder` |
| F | `POST /api/orders/{orderId}/fulfil` — capture at fulfilment; renew stale auth first | `Payments.GetAuthorizedPayment` → (if stale) `Payments.ReauthorizePayment` → `Payments.CaptureAuthorizedPayment` |
| G | `POST /api/orders/{orderId}/cancel` — release hold before fulfilment | `Payments.VoidPayment` |
| H | `POST /api/orders/{orderId}/refunds` — full/partial refund after fulfilment, idempotency key | `Payments.RefundCapturedPayment` |
| I | `GET /api/my-orders` — caller's orders + payment state | — (local) |
| J | `GET /api/reconciliation?from&to` — PayPal txns vs eShop, whole range | `TransactionSearch.SearchTransactions` (chunk ≤31d, paginate all pages) |
| K | `POST /api/payment-methods` — vault a card | `Vault.CreatePaymentToken` |
| L | `GET /api/payment-methods` — caller's saved cards | — (local; PayPal `Vault.ListCustomerPaymentTokens` available as cross-check) |
| M | `DELETE /api/payment-methods/{id}` — remove saved card | `Vault.DeletePaymentToken` |

**Not in the map / not built:** any browser-approval round-trip. If a card payment returns
`PAYER_ACTION_REQUIRED` / a 3DS challenge, STOP and report (per task). No such flow is invented.

---

## 2. CONTRACT SHEET

> ⚠ **Signatures are generated code, verbatim.** Every parameter name below is the literal C#
> identifier — named arguments must use them exactly (the cancellation-token param is `ct`, so
> `ct:`; the "return full body" param is `prefer:`).
> ⚠ **Every SDK type is fully-qualified by the namespace its source path implies** — records &
> `Error` → `PayPalServerSdk.Models`; enums → `PayPalServerSdk.Models.Enums`; typed errors →
> `PayPalServerSdk.Errors`; `SdkException<>` → `PayPalServerSdk.Core.Exceptions`; `RawError` →
> `PayPalServerSdk.Core.ErrorResponse`; client/options → `PayPalServerSdk`;
> `ServerEnvironment`/`ServerOptions` → `PayPalServerSdk.Servers` (client-init step).

### Per-operation rows

| op | signature (verbatim) | request model + fields used | response fields read | error case + accessors | src |
| --- | --- | --- | --- | --- | --- |
| `Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderRequest{ Intent(intent):CheckoutPaymentIntent **req**; PurchaseUnits(purchase_units):IReadOnlyList<PurchaseUnitRequest> **req**; PaymentSource(payment_source):PaymentSource? }` | `Id(id)`, `Status(status):OrderStatus`, `PurchaseUnits[].Payments.Authorizations[]` | `SdkException<CreateOrderError>` A: `TryGetError(out Error)`[400,401,422] · `TryGetRawError(out RawError)` | Orders.md; OrderRequest.cs |
| `Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `body = null` (card supplied at create; remarks: "a valid payment_source must be provided in the request" — satisfied by create) | `Id(id)`, `Status`, `PurchaseUnits[].Payments.Authorizations[]` → `AuthorizationWithAdditionalData{ Id(id), Status(status):AuthorizationStatus, Amount(amount):Money, ExpirationTime(expiration_time) }` | `SdkException<AuthorizeOrderError>` A: `TryGetError(out Error)`[400,401,403,404,422,500] · `TryGetRawError` | Orders.md; OrderAuthorizeResponse.cs; AuthorizationWithAdditionalData.cs |
| `Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentAuthorization{ Status:AuthorizationStatus, ExpirationTime, Amount:Money }` | `SdkException<GetAuthorizedPaymentError>` A: `TryGetError(out Error)`[401,403,404] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` | Payments.md; PaymentAuthorization.cs |
| `Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ReauthorizeRequest{ Amount(amount):Money? }` — omit → reauth original amount (remarks: supports only `amount`) | `PaymentAuthorization{ Id, Status, ExpirationTime }` (NEW auth id) | `SdkException<ReauthorizePaymentError>` A: `TryGetError(out Error)`[400,401,403,404,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; ReauthorizeRequest.cs |
| `Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CaptureRequest{ Amount(amount):Money?, FinalCapture(final_capture):bool? }` — omit Amount → full capture; set `FinalCapture=true` (purpose: no further captures) | `CapturedPayment{ Id(id), Status(status):CaptureStatus, Amount:Money, SellerReceivableBreakdown(seller_receivable_breakdown){ GrossAmount(gross_amount):Money **req**, PaypalFee(paypal_fee):Money?, NetAmount(net_amount):Money? }, CreateTime(create_time) }` | `SdkException<CaptureAuthorizedPaymentError>` A: `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; CapturedPayment.cs; SellerReceivableBreakdown.cs |
| `Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (empty body) | `PaymentAuthorization{ Status:AuthorizationStatus (VOIDED) }` | `SdkException<VoidPaymentError>` A: `TryGetError(out Error)`[401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md |
| `Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `RefundRequest{ Amount(amount):Money? }` — omit → full refund; set → partial (remarks) | `Refund{ Id(id), Status(status):RefundStatus, Amount:Money }` | `SdkException<RefundCapturedPaymentError>` A: `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; RefundRequest.cs; Refund.cs |
| `Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenRequest{ PaymentSource(payment_source):PaymentTokenRequestPaymentSource **req** { Card(card):PaymentTokenRequestCard{ Name, Number, Expiry, SecurityCode, BillingAddress } }; Customer(customer):Customer?{ Id(id), MerchantCustomerId(merchant_customer_id) } }` | `PaymentTokenResponse{ Id(id), Customer(customer):CustomerResponse{Id}, PaymentSource.Card:CardPaymentTokenEntity{ LastDigits(last_digits), Brand(brand):CardBrand, Expiry } }` | `SdkException<CreatePaymentTokenError>` A: `TryGetError(out Error)`[400,403,404,422,500] · `TryGetRawError` | Vault.md; PaymentTokenRequest.cs; PaymentTokenResponse.cs; CardPaymentTokenEntity.cs |
| `Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `void` (Task) | `SdkException<DeletePaymentTokenError>` A: `TryGetError(out Error)`[400,403,500] · `TryGetRawError` | Vault.md |
| `Vault.ListCustomerPaymentTokens` | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query `customer_id,page_size,page,total_required` | `CustomerVaultPaymentTokensResponse{ PaymentTokens:IReadOnlyList<PaymentTokenResponse>, TotalPages }` | `SdkException<ListCustomerPaymentTokensError>` A: `TryGetError(out Error)`[400,403,500] · `TryGetRawError` | Vault.md |
| `TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query: `startDate`/`endDate` RFC3339 **req**; **max range 31 days** (remarks) → chunk; `balanceAffectingRecordsOnly="N"` to include all; paginate `page` 1..`TotalPages` | `SearchResponse{ TransactionDetails:IReadOnlyList<TransactionDetails>{ TransactionInfo:TransactionInformation{ TransactionId(transaction_id), TransactionAmount(transaction_amount):Money, TransactionStatus(transaction_status), InvoiceId(invoice_id), CustomField(custom_field), TransactionInitiationDate } }, Page, TotalItems, TotalPages }` | `SdkException<RawError>` **Case B**: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | TransactionSearch.md; SearchResponse.cs; TransactionInformation.cs |

### Sub-models used (build order)

- `OrderRequest.PurchaseUnits[]` = `PurchaseUnitRequest{ Amount(amount):AmountWithBreakdown **req**{ CurrencyCode(currency_code) **req**, Value(value) **req** }, InvoiceId(invoice_id)?, CustomId(custom_id)?, Description? }`. Set **InvoiceId = CustomId = eShop payment reference** (purpose: reconciliation linkage; both surface in transaction reports). Value = total formatted `0.00` invariant.
- `OrderRequest.PaymentSource` = `PaymentSource{ Card(card):CardRequest }`. One-off: `CardRequest{ Number, Expiry("YYYY-MM"), SecurityCode, Name, BillingAddress }`. Saved card: `CardRequest{ VaultId(vault_id) = stored token id }` (purpose: pay with vaulted card; omit raw PAN).
- `Money{ CurrencyCode **req**, Value **req** }` for capture/refund/reauthorize amounts.
- Enums (values verbatim): `CheckoutPaymentIntent.Authorize`("AUTHORIZE")/`.Capture`; `OrderStatus`: CREATED/APPROVED/COMPLETED/VOIDED/PAYER_ACTION_REQUIRED/SAVED; `AuthorizationStatus`: CREATED/CAPTURED/DENIED/PARTIALLY_CAPTURED/VOIDED/PENDING; `CaptureStatus`: COMPLETED/DECLINED/PARTIALLY_REFUNDED/PENDING/REFUNDED/FAILED; `RefundStatus`: CANCELLED/FAILED/PENDING/COMPLETED. Build via static members or `Type.FromValue("WIRE")`; read via `.Value`.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| The `authorizationId` captured/reauthorized/voided must be one produced by this order's authorize step | `Payments.Capture/Reauthorize/Void` ← `Orders.AuthorizeOrder` | implementation — persisted `OrderPayment.AuthorizationId`; endpoints act only on the caller's order's stored id |
| The `captureId` refunded must be the capture produced at fulfilment for this order | `Payments.RefundCapturedPayment` ← `Payments.CaptureAuthorizedPayment` | implementation — persisted `OrderPayment.CaptureId` |
| The `vault_id` used to pay must be a token the caller saved (and not deleted) | `Orders.CreateOrder`(card.vault_id) ← `Vault.CreatePaymentToken` | implementation — `SavedPaymentMethod` looked up by `(BuyerId, PaymentMethodId)`; deleted rows unusable |
| The `id` deleted must be a token the caller owns | `Vault.DeletePaymentToken` ← `Vault.CreatePaymentToken` | implementation — ownership check on `SavedPaymentMethod` before SDK call |
| Reconciliation matches PayPal txn to eShop order by the reference eShop set at create | `TransactionSearch` ↔ local `OrderPayment` | implementation — `TransactionInformation.InvoiceId`/`CustomField` ↔ `OrderPayment.PayPalInvoiceReference` |

---

## 3. Trap notes (name the hazard, load the skill — not resolved here)

- **Client/HttpClient lifetime & DI shape.** How `PayPalServerSdkClient` and its `HttpClient` must be owned (long-lived vs per-request) is not on the signature. **MUST load dotnet-client-initialization.**
- **Credential placement & when options are captured.** Where `Oauth2` must be set relative to construction. **MUST load dotnet-authentication.**
- **Named-argument binding for optional params with no C# default.** The `payPalMockResponse…body` "must-pass-explicitly" params and list/search optionals mis-bind positionally. **MUST load dotnet-calling-endpoints.**
- **`prefer` header changes the response body shape.** Whether the breakdown/authorization arrays are even present depends on it — do not read them assuming default. **MUST load dotnet-calling-endpoints.**
- **Building request models: enums are `StringEnum<T>` not C# enums; unknown response fields.** `CheckoutPaymentIntent`/`CardBrand` etc. and how optional-vs-null is expressed. **MUST load dotnet-models.**
- **Error boundary: Case A typed vs Case B raw, and `JsonException` from two directions.** The catch ladder shape and the deserialization traps. **MUST load dotnet-error-handling.**
- **Retry eligibility, per-attempt timeout vs total budget, unredacted body logging.** POST is not resent by default; `Timeout` is per-attempt; `LogRequestBody` prints card JSON. **MUST load dotnet-configuration-resilience.**
- **Test seam is the `HttpClient` constructor arg.** How to fake the transport for endpoint tests. **MUST load dotnet-testing.**

---

## 4. REQUIRED READING (load ALL before implementation; contents deliberately not inlined here)

| skill (plugin `paypal-platforms-team`) | governs |
| --- | --- |
| `dotnet-client-initialization` | Step A — client construction & DI |
| `dotnet-authentication` | Step A — OAuth2 client-credentials wiring |
| `dotnet-calling-endpoints` | Steps E–M — every SDK call, named args, `prefer` |
| `dotnet-models` | Steps E–M — request bodies, enums, unions |
| `dotnet-error-handling` | Step C — error boundary (ALWAYS required) |
| `dotnet-configuration-resilience` | Step A/C — retries, timeout budget, logging posture |
| `dotnet-testing` | Step (tests) — faking the transport seam |

**Mandatory hazard rows (both, verbatim):**
1. A drifted/malformed **2xx** body (missing `required` member) surfaces as `System.Text.Json.JsonException` from deserialization, **not** `SdkException` — an SDK-exception-only catch ladder lets it escape. Boundary must also catch `JsonException`.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is constructed**, so it **replaces** the `SdkException` and the HTTP status is destroyed with it. Boundary must catch `JsonException` around SDK calls and treat as an upstream/transport failure.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` bound from `PayPal:` section; a startup validator (`IValidateOptions` / explicit check in Program.cs) throws if `ClientId`, `ClientSecret`, `Environment`, or `Currency` is null/blank — **each part checked** (blank ≠ missing). Host refuses to start. |
| 2 | Secret sourcing & rotation | Secrets come from .NET user-secrets (loaded from env vars `PAYPAL_CLIENT_ID`/`PAYPAL_CLIENT_SECRET` by an operator step, never committed). DI builds `PayPalServerSdkClientOptions` once at registration and captures it in the client → **a rotated secret needs a process restart**; documented. Restart-free rotation not required for this task. |
| 3 | Total timeout budget | SDK `Timeout` is **per attempt**. Each PayPal call is bounded by a `CancellationTokenSource(TimeSpan)` deadline (config `PayPal:CallTimeoutSeconds`, default 30s) passed as `ct` — that is the whole-call bound. Retries left at SDK default for GETs only (below). |
| 4 | Write-retry ownership | SDK default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS. All our writes are **POST/DELETE → never auto-resent by SDK**. Safe: idempotency keys make them replay-safe anyway (row 5). GET reconciliation/get-auth may retry — safe (read-only). |
| 5 | Idempotency & ambiguous writes | Internal request-ids derive from the per-order **`InvoiceReference`** (`ESHOP-{orderId}-{guid}`) — stable per order (double-click safe) yet unique across in-memory runs, so they never collide with PayPal's 45-day-retained ids from an earlier run on the shared sandbox account (a `"pay-{orderId}"` scheme did collide → PayPal replayed a stale capture; **fixed & verified live**). `CreateOrder`/`AuthorizeOrder`: `payPalRequestId = InvoiceReference` / `+"-auth"` (6h). `CaptureAuthorizedPayment`: `+"-cap"` (45d). `VoidPayment`: `+"-void"`. `ReauthorizePayment`: `+"-reauth"`. `RefundCapturedPayment`: `payPalRequestId = "{InvoiceReference}-refund-{callerKey}"` (unique per capture even if a caller reuses a key) **and** persisted `PaymentRefund.IdempotencyKey` unique per capture → repeat returns the stored refund, two distinct keys = two partial refunds. `CreatePaymentToken`: `payPalRequestId` unset (a new card each save is intended). |
| 6 | Observability | `IAppLogger`-style logging: Info on each transition (order/auth/capture/refund ids), Warn on renew, Error on SDK failures. PayPal `Error.DebugId` (correlation id) is extracted from the error boundary and logged. `LogRequestBody` stays **off**. |
| 7 | Sensitive data | Scope carries **raw PAN/CVV** (`CardRequest.Number/SecurityCode`, `PaymentTokenRequestCard`). Therefore: `options.Logging.LogRequestBody` never enabled; `options.Logging.LoggerFactory` set **explicitly** so `PAYPALSERVERSDKCLIENT_LOG` cannot arm body logging from outside; our own code never logs card fields or request bodies. Only last4+brand+expiry ever stored/returned. |
| 8 | Environment selection | One server group `Default`. SDK declares only `ServerEnvironment.Sandbox` (base `https://api-m.sandbox.paypal.com`). Config `PayPal:Environment` selects it; `PayPal:BaseUrl` (optional) overrides `options.Server.Default.Sandbox.BaseUrl` **and** the token URL verbatim when set. All dev/test → sandbox; a non-sandbox `Environment` value with no live member is rejected at startup (fail-fast) so test traffic can't hit live. |
| 9 | Duplicate prevention under concurrency | Store `CatalogContext`; **`OrderPayment` table, unique index on `OrderId`** rejects a 2nd pay row; **`PaymentRefund`, unique index on `(OrderPaymentId, IdempotencyKey)`** rejects a repeated refund; **`SavedPaymentMethod`, unique index on `PayPalVaultId`**. Code catches `DbUpdateException` on the constraint. ⚠ SQL Server enforces these. **In-memory provider (this machine's mandated mode) does not enforce unique indexes** — there the PayPal-side idempotency key (row 5) is the money-safety backstop; recorded as an environment limitation, not the primary mechanism. |
| 10 | Partial results | Reconciliation returns `TotalPagesScanned`, `TotalTransactions`, and a per-window `Truncated` flag is impossible (we walk every page 1..TotalPages of every ≤31-day chunk) → response carries `Complete=true` and the list of windows scanned. No silent cap. |
| 11 | Startup validation vs test host | `tests/PublicApiIntegrationTests` boots the host via `WebApplicationFactory<Program>` with `appsettings.test.json`. It is given **placeholder** `PayPal:*` values (non-blank) via test config so the fail-fast check passes without real secrets; verified green. Live-secret flows are exercised out-of-band against the running API. |
| 12 | Ordering & no-op side effects | Local `OrderPayment` row is written **before** the PayPal call (status `PendingPayment`/`Authorizing`) and completed after with PayPal ids. Each transition is **gated on actual state change**: pay only when `PendingPayment`; capture only when `Authorized`; void only when `Authorized`; refund only when `Captured`/`PartiallyRefunded` and within remaining capturable amount. Repeated calls in a terminal state return the existing state without a second PayPal effect. |
| 13 | Unknown outcomes | If transport fails after a write may have landed: **pay** → re-read `Orders.GetOrder(payPalOrderId)` by stored PayPal order id; **capture** → re-read via `Payments.GetAuthorizedPayment(authorizationId)` (status CAPTURED/PARTIALLY_CAPTURED) before retrying; **refund** → re-read is by the idempotency key (same `payPalRequestId` replays safely). No definite failure returned without a re-read. |
| 14 | Provider status & reconciliation clocks | Branch on: `OrderStatus`, `AuthorizationStatus` (PENDING→treat as not-yet-usable; DENIED→fail), `CaptureStatus` (PENDING/DECLINED/FAILED→not fulfilled), `RefundStatus` (PENDING/FAILED). No `?? "COMPLETED"`. Reconciliation: both sides filter on the **PayPal transaction timestamp** — PayPal uses `transaction_initiation_date`; eShop rows are matched using the PayPal-reported `create_time` stored on `OrderPayment` (`AuthorizedAt`/`CapturedAt`), **not** the local row-creation column. |

### DUPLICATE CLAIMS

| write | where the claim is stored | what rejects the second one | where that rejection is caught |
| --- | --- | --- | --- |
| pay (create+authorize) | `OrderPayment` (CatalogContext) | unique index on `OrderId` (+ PayPal `PayPal-Request-Id` 6h) | pay endpoint catches `DbUpdateException`; state-gate on `PendingPayment` |
| refund | `PaymentRefund` (CatalogContext) | unique index on `(OrderPaymentId, IdempotencyKey)` (+ PayPal key 45d) | refund endpoint catches `DbUpdateException`; returns stored refund |
| save card | `SavedPaymentMethod` (CatalogContext) | unique index on `PayPalVaultId` | payment-methods endpoint catches `DbUpdateException` |

⚠ SQL Server enforces all three. In-memory mode (mandated locally) does not; the PayPal idempotency key is the backstop there — stated honestly, not as the primary claim.

### PAGED READS

| read | what caps it | how the caller learns the answer was cut short |
| --- | --- | --- |
| reconciliation | 31-day range cap + `page_size` per page | it is **not** cut short — every ≤31d window is walked page 1..`TotalPages`; response returns `Complete=true`, `WindowsScanned`, `TotalTransactions`. If a future cap were hit it would set `Complete=false` (field on the response). |
| list saved cards (local) | none (local table, small) | n/a — full list always returned |

### REPEATED OPERATIONS

| operation | what tells you the state actually changed | the effects gated on that |
| --- | --- | --- |
| pay | `OrderPayment.Status == PendingPayment` before; `Authorized` after | the `CreateOrder`+`AuthorizeOrder` PayPal calls |
| fulfil | `Status == Authorized` before; `Captured` after | the reauth (if stale) + `CaptureAuthorizedPayment` call |
| cancel | `Status == Authorized` before; `Cancelled` after | the `VoidPayment` call |
| refund | idempotency key not already present; remaining capturable > 0 | the `RefundCapturedPayment` call |
| delete card | row exists & owned by caller | the `DeletePaymentToken` call |

### UNKNOWN OUTCOMES

| write | the operation you re-read with | the reference you search by |
| --- | --- | --- |
| pay | `Orders.GetOrder` | stored `OrderPayment.PayPalOrderId` |
| fulfil (capture) | `Payments.GetAuthorizedPayment` | stored `OrderPayment.AuthorizationId` (status shows if captured) |
| refund | replay same `RefundCapturedPayment` with same `payPalRequestId` (idempotent) | caller `IdempotencyKey` |
| save card | list cross-check `Vault.ListCustomerPaymentTokens` | PayPal `customer.id` (stored `SavedPaymentMethod.PayPalCustomerId`) |

---

## 6. Assumptions & Blockers

- **ENVIRONMENT BLOCKER (reported, not a code/plugin defect): the provided sandbox business account
  cannot vault cards.** Every plugin-provided card-vaulting path returns PayPal **HTTP 500
  `INTERNAL_SERVICE_ERROR`** on this account, verified live: `Vault.CreatePaymentToken` (raw card),
  `Vault.CreateSetupToken` (raw card, with and without CVV/verification_method), and
  `Orders.CreateOrder` with `payment_source.card.attributes.vault.store_in_vault=ON_SUCCESS`. The
  *identical* order without the vault instruction authorizes successfully, and pay/fulfil/refund/cancel
  are all verified live — so the account has direct card processing but not card vaulting, despite the
  task stating it is "enabled for vaulting cards." The saved-card code is complete and correct (the
  canonical setup-token → payment-token flow) and is verified end-to-end (save → list → pay-with-saved →
  delete → unusable) by the integration tests against a faked gateway; it will work unchanged on an
  account whose vault is provisioned. It cannot be verified against *this* sandbox account.
- **No plugin/source gaps.** Every required capability maps to an SDK operation above.
- Assumption: `POST /api/orders` needs a shipping address not in the request; a fixed default
  address is used (mirrors the existing Web checkout), since address is out of scope. (minor)
- Assumption: currency is single (`PayPal:Currency`, USD in sandbox); amounts formatted with 2
  decimals invariant. (minor)
- Assumption: the sandbox business account is vault- and card-enabled (per task), so
  `CreatePaymentToken` with raw card + direct card `CreateOrder` succeed without buyer approval.
  If PayPal returns a 3DS/`PAYER_ACTION_REQUIRED` challenge, the pay endpoint STOPS and reports
  it (does not build an approval round-trip) — per task mandate.

## 7. Source labels

Every per-operation row cites its map page and the declaring source file(s) read this session.
Application-design rows (persistence shape, endpoint contracts, state machine) are `YOUR CALL —
not in the map` and decided against the task at implementation time.
