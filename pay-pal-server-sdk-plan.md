# PayPal Server SDK integration plan — eShopOnWeb PublicApi

Additive card-payments + saved-cards capability on `src/PublicApi`. PayPal Server SDK
(.NET, root namespace `PayPalServerSdk`, spec 2.29) is the sole PayPal reference. All facts
below come from the SDK map (`sdk-map.md` + `map/operations/*`) and the map-named source
files, read this session.

---

## 1. Scope & sequence

Flow 1 (pay for an order) and Flow 2 (saved cards), driven end-to-end through PublicApi.

1. **Vendor + wire the SDK.** Vendor the SDK source as an in-repo project (`external/PayPalServerSdk`),
   reference it from PublicApi. Bind `PayPal:` settings, fail-fast, register the client via
   `services.AddPayPalServerSdkClient(...)`.
2. **Domain + persistence.** New aggregates: `OrderPayment` (1:1 with the existing `Order` by
   `OrderId`, holds all PayPal-owned state), `OrderRefund` (child of `OrderPayment`), `SavedCard`.
   Reuse the existing `Order`/`OrderItem` model unchanged. EF config + DbSets on `CatalogContext`.
3. **Gateway.** `IPayPalGateway` (ApplicationCore, SDK-free DTOs) → `PayPalGateway` (Infrastructure,
   uses `PayPalServerSdkClient`). One method per SDK operation in scope.
4. **Orchestration services** (`IOrderPaymentService`, `ISavedCardService`) — coordinate
   repositories + gateway, own idempotency/duplicate-claim/error mapping.
5. **Endpoints** on PublicApi under `/api/`.
6. **Tests + self-verify** on the sandbox card.

Operation → SDK mapping:

| eShop action | SDK operations (in order) |
| --- | --- |
| `POST /api/orders` | none (creates local `Order` only; state = AwaitingPayment) |
| `POST /api/orders/{id}/pay` | `Orders.CreateOrder` (intent=AUTHORIZE, card or vault_id) → `Orders.AuthorizeOrder` |
| `POST /api/orders/{id}/fulfil` | `Payments.CaptureAuthorizedPayment`; on stale auth → `Payments.ReauthorizePayment` then capture |
| `POST /api/orders/{id}/cancel` | `Payments.VoidPayment` |
| `POST /api/orders/{id}/refunds` | `Payments.RefundCapturedPayment` |
| `GET /api/my-orders` | none (local read; may `Orders.GetOrder` / `Payments.GetAuthorizedPayment` for unknown-outcome re-reads) |
| `GET /api/reconciliation` | `TransactionSearch.SearchTransactions` (all pages, 31-day windows) |
| `POST /api/payment-methods` | `Vault.CreatePaymentToken` (card) |
| `GET /api/payment-methods` | none (local read) |
| `DELETE /api/payment-methods/{id}` | `Vault.DeletePaymentToken` |

---

## 2. CONTRACT SHEET

> ⚠ **Signatures below are generated code, copied verbatim.** Every parameter name is the literal
> C# identifier — the cancellation-token parameter really is named `ct`, so named arguments write `ct:`.
> ⚠ **Every SDK type is fully-qualified by the namespace its source path implies**, taken from the path
> the map gives for THAT type: `Models/` → `PayPalServerSdk.Models`; `Models/Enums/` →
> `PayPalServerSdk.Models.Enums`; `Errors/` → `PayPalServerSdk.Errors`; `Core/ErrorResponse/` →
> `PayPalServerSdk.Core.ErrorResponse`; `Core/Exceptions/` → `PayPalServerSdk.Core.Exceptions`;
> client/options/servers → `PayPalServerSdk` / `PayPalServerSdk.Servers`.

### Operations

| # | Op | Signature (verbatim) | Request model + fields used | Response fields read | Error case | Pagination | Source |
|---|----|----------------------|------------------------------|----------------------|-----------|------------|--------|
| O1 | `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent, required` (=AUTHORIZE); `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest>, required` (one unit; **purpose:** carries amount + invoice_id/custom_id for reconciliation); `PaymentSource (payment_source): PaymentSource?` (**purpose:** card for one-off OR card.vault_id for saved card; omit → no source). `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown, required`; `InvoiceId (invoice_id): string?` (**purpose:** unique per-order recon key); `CustomId (custom_id): string?` (**purpose:** eShop order id echo). `AmountWithBreakdown`: `CurrencyCode (currency_code): string, required`; `Value (value): string, required`. `PaymentSource.Card (card): CardRequest?`; `CardRequest`: `Number/Expiry/SecurityCode/Name/BillingAddress` for one-off, OR `VaultId (vault_id): string?` for saved. Pass `payPalRequestId` = deterministic `create-{invoiceId}` (idempotency). | `Order.Id (id)`, `Order.Status (status): OrderStatus?`, `Order.Links (links)`, `Order.PurchaseUnits[].Payments.Authorizations[]` | A: `SdkException<CreateOrderError>`; `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` | none | Orders.md; Models/OrderRequest.cs, Models/PurchaseUnitRequest.cs, Models/AmountWithBreakdown.cs, Models/PaymentSource.cs, Models/CardRequest.cs |
| O2 | `client.Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` (**purpose:** omit when card already supplied at CreateOrder → pass `null` body). Pass `prefer="return=representation"` to get the authorization id. `payPalRequestId` = `authorize-{invoiceId}`. | `OrderAuthorizeResponse.Status (status): OrderStatus?`; `.PurchaseUnits[].Payments.Authorizations[].Id`, `.Status (AuthorizationStatus)`, `.ExpirationTime`, `.CreateTime`, `.Amount` | A: `SdkException<AuthorizeOrderError>`; `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` | none | Orders.md; Models/OrderAuthorizeRequest.cs, Models/OrderAuthorizeResponse.cs, Models/PurchaseUnit.cs, Models/PaymentCollection.cs, Models/AuthorizationWithAdditionalData.cs |
| O3 | `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CaptureRequest`: `Amount (amount): Money?` (**purpose:** omit → capture full authorized amount = provider default); `FinalCapture (final_capture): bool?` (**purpose:** set true — single full capture). Pass `payPalRequestId="capture-{invoiceId}"`, `prefer="return=representation"`. | `CapturedPayment.Id (id)`, `.Status (status): CaptureStatus?`, `.Amount (Money)`, `.SellerReceivableBreakdown.GrossAmount/PaypalFee/NetAmount`, `.CreateTime` | A: `SdkException<CaptureAuthorizedPaymentError>`; `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | none | Payments.md; Models/CaptureRequest.cs, Models/CapturedPayment.cs, Models/SellerReceivableBreakdown.cs, Models/Money.cs |
| O4 | `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `RefundRequest`: `Amount (amount): Money?` (**purpose:** omit → full refund = provider default; set for partial); `CustomId (custom_id): string?` (**purpose:** store idempotency key for recon re-read); `InvoiceId (invoice_id): string?`. Pass `payPalRequestId` = caller idempotency key, `prefer="return=representation"`. | `Refund.Id (id)`, `.Status (status): RefundStatus?`, `.Amount (Money)`, `.SellerPayableBreakdown.TotalRefundedAmount` | A: `SdkException<RefundCapturedPaymentError>`; `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | none | Payments.md; Models/RefundRequest.cs, Models/Refund.cs, Models/SellerPayableBreakdown.cs |
| O5 | `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | no body | `PaymentAuthorization.Status (status): AuthorizationStatus?` (expect VOIDED) | A: `SdkException<VoidPaymentError>`; `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent` [500] · `TryGetRawError` | none | Payments.md; Models/PaymentAuthorization.cs |
| O6 | `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ReauthorizeRequest`: `Amount (amount): Money?` (**purpose:** omit → reauthorize original amount = provider default) | `PaymentAuthorization.Id (id)`, `.Status`, `.ExpirationTime` | A: `SdkException<ReauthorizePaymentError>`; `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent` [500] · `TryGetRawError` | none | Payments.md; Models/ReauthorizeRequest.cs, Models/PaymentAuthorization.cs |
| O7 | `client.Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentAuthorization.Status`, `.Id`, `.ExpirationTime` | A: `SdkException<GetAuthorizedPaymentError>`; `TryGetError(out Error)` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` | none | Payments.md; Models/PaymentAuthorization.cs |
| O8 | `client.Orders.GetOrder` | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query `fields` (pass null) | `Order.Status`, `.PurchaseUnits[].Payments.Authorizations[].Id/Status` | A: `SdkException<GetOrderError>`; `TryGetError(out Error)` [401,404] · `TryGetRawError` | none | Orders.md; Models/Order.cs |
| V1 | `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenRequest`: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource, required`; `Customer (customer): Customer?` (**purpose:** set `MerchantCustomerId` = eShop buyer id to group tokens). `PaymentTokenRequestPaymentSource.Card (card): PaymentTokenRequestCard?`; `PaymentTokenRequestCard`: `Number/Expiry/SecurityCode/Name/BillingAddress`. Pass `payPalRequestId` = deterministic per save request. | `PaymentTokenResponse.Id (id)` (= vault id), `.PaymentSource.Card (CardPaymentTokenEntity): LastDigits/Brand/Expiry/Name` | A: `SdkException<CreatePaymentTokenError>`; `TryGetError(out Error)` [400,403,404,422,500] · `TryGetRawError` | none | Vault.md; Models/PaymentTokenRequest.cs, Models/PaymentTokenRequestPaymentSource.cs, Models/PaymentTokenRequestCard.cs, Models/Customer.cs, Models/PaymentTokenResponse.cs, Models/PaymentTokenResponsePaymentSource.cs, Models/CardPaymentTokenEntity.cs |
| V2 | `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | void | A: `SdkException<DeletePaymentTokenError>`; `TryGetError(out Error)` [400,403,500] · `TryGetRawError` | none | Vault.md |
| T1 | `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` | 8 nullable params (`transactionId`…`terminalId`) → pass `null`. Pass `startDate`/`endDate` (RFC3339). Page via `page`, read `pageSize`. | `SearchResponse.TransactionDetails[].TransactionInfo`: `TransactionId`, `InvoiceId`, `TransactionAmount (Money)`, `FeeAmount`, `TransactionStatus (string)`, `TransactionInitiationDate`; `SearchResponse.TotalPages`, `.Page`, `.TotalItems` | **B: `SdkException<RawError>`** (StatusCode/ReadAsString/ReadAsJson) | **page-based**: loop `page=1..TotalPages` | TransactionSearch.md; Models/SearchResponse.cs, Models/TransactionDetails.cs, Models/TransactionInformation.cs |

### Enums needed (source: Models/Enums/<Type>.cs)

- `CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`), `.Capture`.
- `OrderStatus`: Created, Saved, Approved, Voided, Completed, **PayerActionRequired** (challenge marker).
- `AuthorizationStatus`: Created, Captured, Denied, PartiallyCaptured, Voided, Pending.
- `CaptureStatus`: Completed, Declined, PartiallyRefunded, Pending, Refunded, Failed.
- `RefundStatus`: Cancelled, Failed, Pending, Completed.
- `CardBrand` (response only, used to describe saved card).

### Client construction / auth / server node

- Client: `new PayPalServerSdkClient(HttpClient, PayPalServerSdkClientOptions)` — or DI
  `services.AddPayPalServerSdkClient(options => {...})` (source: ServiceCollectionExtensions.cs — builds
  options **once at registration**, captured in the singleton).
- Auth: `options.Oauth2 = new OAuth2ClientCredentials { ClientId=…, ClientSecret=… }`
  (`PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`). Every op in scope: **Auth `options.Oauth2`**.
- Environment: only `ServerEnvironment.Sandbox` exists (spec declares no others). Token URL =
  `server.Default("/v1/oauth2/token")` — resolves through `options.Server.Default.Sandbox.BaseUrl`
  (confirmed AuthSchemes.cs), so a `PayPal:BaseUrl` override set on
  `options.Server.Default.Sandbox.BaseUrl` retargets **every** call including the token request.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| An order id supplied to pay/fulfil/cancel/refund/my-orders must be an `Order` owned by the caller | write ops ← `Order` rows filtered by `BuyerId` | implementation (services, spec by BuyerId) |
| A `paymentMethodId` used to pay must be a `SavedCard` the caller saved (and still present) | `pay` ← `POST /api/payment-methods` | implementation (SavedCardService scoped by BuyerId; deleted card unusable) |
| Refund amount + already-refunded must never exceed captured amount | `refunds` ← `fulfil` (capture) | implementation (OrderPaymentService cap check) |
| The `captureId` refunded must be the one produced by this order's fulfilment | `refunds` ← `fulfil` | implementation (captureId read from OrderPayment) |
| The `authorizationId` captured/voided/reauthorized must be the one this order's pay produced | fulfil/cancel ← pay | implementation (authorizationId read from OrderPayment) |

---

## 3. Trap notes (hazard + consequence + skill; not resolved here)

- **T1 (client + HttpClient lifetime).** Getting `HttpClient`/handler lifetime wrong (rebuilt per request) exhausts sockets or breaks DNS refresh; the wrapper's lifetime is a separate question. `MUST load dotnet-client-initialization`.
- **T2 (auth wiring).** Where credentials are set relative to client construction, and secret sourcing from config vs hardcode, is not shown by the options shape; a credential never set is silently skipped, so a 401 can mean "nothing sent". `MUST load dotnet-authentication`.
- **T3 (calling ops / named args).** Many optional params have no C# default and mis-bind positionally; whether a write takes a REAL idempotency key is per-op, and the generator-injected `Idempotency-Key` header is not one. `MUST load dotnet-calling-endpoints`.
- **T4 (models).** Enums are `StringEnum<T>` not C# enums; unions build via factory / read via `TryGet…`; unknown response fields land on `AdditionalProperties` only where present. `MUST load dotnet-models`.
- **T5 (errors).** Case A vs B per op; `System.Text.Json.JsonException` reaches the boundary from a drifted 2xx body AND while constructing a non-2xx error object (destroying the status) — an SDK-exception-only ladder misses both. `MUST load dotnet-error-handling`.
- **T6 (resilience/config).** `HttpMethodsToRetry` gates every retry trigger (POST never resent by default; PUT is); `Timeout` is per-attempt not total; `LogRequestBody` logs JSON unredacted; the log env var arms body logging unless `LoggerFactory` is set. `MUST load dotnet-configuration-resilience`.
- **T7 (testing).** The `HttpClient` ctor arg is the fake seam; match the project's framework. `MUST load dotnet-testing`.

---

## 4. REQUIRED READING (load ALL before implementation starts; contents intentionally not carried here)

| skill (plugin-qualified) | governs step |
| --- | --- |
| `paypal-platforms-team:dotnet-client-initialization` | vendored client + DI registration (step 1/3) |
| `paypal-platforms-team:dotnet-authentication` | OAuth2 credentials binding (step 1) |
| `paypal-platforms-team:dotnet-calling-endpoints` | every gateway call, named args, idempotency key (step 3) |
| `paypal-platforms-team:dotnet-models` | building request bodies / reading enums+unions (step 3) |
| `paypal-platforms-team:dotnet-error-handling` | gateway error boundary — Case A/B + JsonException traps (step 3) — **always required** |
| `paypal-platforms-team:dotnet-configuration-resilience` | retries/timeouts/base-URL/pagination/logging (step 1/3) |
| `paypal-platforms-team:dotnet-testing` | integration-layer tests (step 6) |

Hazard rows that always apply (from getting-started): a drifted/malformed **2xx** body (missing `required`
member) surfaces as `System.Text.Json.JsonException` from deserialization, NOT an `SdkException` — an
SDK-only catch ladder lets it escape; and a **non-2xx** body that does not match its op's generated
`{Operation}Error` throws `JsonException` **while the error object is constructed**, replacing the
`SdkException` and destroying the HTTP status. The gateway boundary catches `JsonException` too.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalSettings` bound from `PayPal:` in Program.cs via `AddOptions<PayPalSettings>().Bind(section).Validate(...).ValidateOnStart()`; validator rejects blank `ClientId`, `ClientSecret`, `Environment`, `Currency` (each part checked separately — a blank part ≠ missing). Host refuses to start otherwise. |
| 2 | Secret sourcing & rotation | Secrets from .NET user-secrets (dev) / env vars `PayPal__*` (prod), never repo. `AddPayPalServerSdkClient` builds options once at registration → captured in the singleton, so a rotated secret takes effect only after process restart. Documented; no hot-rotation required for this task. |
| 3 | Total timeout budget | SDK `Timeout` is per-attempt. Each endpoint enforces a whole-call deadline via a `CancellationToken` (`CancellationTokenSource` linked to request-aborted, default 100s cap) passed as `ct:`. That token is the only bound on the full call. |
| 4 | Write-retry ownership | Every PayPal write in scope is `POST` (Create/Authorize/Capture/Refund/Void/Reauthorize/CreatePaymentToken) or `DELETE` (DeletePaymentToken) → **never auto-resent** by the SDK (default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS). No PUT in scope. Safe: our idempotency is explicit (row 5). |
| 5 | Idempotency & ambiguous writes | CreateOrder/AuthorizeOrder/Capture use a **deterministic** `payPalRequestId` (`create-`/`authorize-`/`capture-{invoiceId}`, invoiceId persisted before the calls) so a double-click dedupes at PayPal. Refund uses the **caller-supplied** idempotency key as `payPalRequestId` AND a unique DB column (row 9). Void/Reauthorize are naturally idempotent by resulting state. |
| 6 | Observability | Info: state transitions with orderId + PayPal ids. Warning/Error: mapped PayPal errors incl. `debug_id`/`name`/`message` from `Error`/`RawError`. `LogRequestBody` stays **off** (row 7). Correlation: PayPal `debug_id` logged. |
| 7 | Sensitive data | Card PAN/CVV flow through `CardRequest`/`PaymentTokenRequestCard` (request models). Therefore `options.Logging.LogRequestBody` stays off **and** `options.Logging.LoggerFactory` is assigned explicitly at registration (defeats `PAYPALSERVERSDKCLIENT_LOG`). App never logs card fields; only last-4/brand from responses persisted. Card number never stored in our DB. |
| 8 | Environment selection | One server group `Default`; only `ServerEnvironment.Sandbox` declared. Config `PayPal:Environment` bound + validated; `PayPal:BaseUrl` optional override applied to `options.Server.Default.Sandbox.BaseUrl` (also retargets token). All dev/test → sandbox. If a non-sandbox environment name is configured without a BaseUrl override, fail-fast (can't reach a live host through a sandbox-only enum). |
| 9 | Duplicate prevention under concurrency | Store **`OrderPayment`**, column **`OrderId`**, **unique index** (`HasIndex(o=>o.OrderId).IsUnique()`). First `pay` inserts the row; a concurrent second insert violates the unique index → `DbUpdateException` caught in `OrderPaymentService` → treated as idempotent (return existing). Refund: store **`OrderRefund`**, column **`IdempotencyKey`**, **unique index**; repeat key → `DbUpdateException` caught → return existing refund. (SQL Server enforces this in production; note: the dev-machine in-memory provider does not enforce indexes — that is an environment limitation of this box, not of the design.) |
| 10 | Partial results | Reconciliation walks all `SearchResponse.TotalPages` across 31-day windows. The response DTO carries `Truncated: bool` + `PagesScanned`/`WindowsScanned`; a safety cap (windows/pages) sets `Truncated=true` so the caller learns of truncation via the body, not a log. Expected path: `Truncated=false`. |
| 11 | Startup validation vs test host | Host-booting test projects: **`tests/PublicApiIntegrationTests`** (`WebApplicationFactory<Program>`, env Development, loads `appsettings.test.json`) and **`tests/FunctionalTests`** (`WebApplicationFactory<AuthenticateEndpoint>`, env `Testing`). Both get **placeholder** PayPal config (`appsettings.test.json` gets `PayPal:*` placeholders; `src/PublicApi/appsettings.Testing.json` created with in-memory + `PayPal:*` placeholders, loaded only under env Testing). Both projects run and green (verified in step 6). Placeholders are non-secret literals, allowed. |
| 12 | Ordering & no-op side effects | `pay`: `OrderPayment` row (OrderId + invoiceId + status Authorizing) is written **before** the PayPal call; authorization/capture details written after it returns. Idempotent transitions gate outbound effects: `pay` on an already-Authorized payment returns existing state and makes **no** PayPal call; `fulfil` on already-Captured returns existing, no capture; `cancel` on already-Cancelled no-ops. |
| 13 | Unknown outcomes | On transport failure after send: `pay` catch re-reads `Orders.GetOrder(payPalOrderId)` (search by stored PayPalOrderId) to see whether the authorization landed; `fulfil` catch re-reads `Payments.GetAuthorizedPayment(authorizationId)` for `CAPTURED`; deterministic `payPalRequestId` makes a safe re-issue return the same resource. Never report definite failure without re-reading. |
| 14 | Provider status & reconciliation clocks | Status fields read & branched: `OrderStatus` (PayerActionRequired → challenge STOP/report; Approved/Created → proceed), `AuthorizationStatus` (Created→ok, Denied/Voided→fail, Pending→not-done), `CaptureStatus` (Completed→ok, Pending→not-done, Declined/Failed→fail), `RefundStatus` (Completed→ok, Pending→not-done, Failed/Cancelled→fail). No absent status coalesced to success. Reconciliation single timestamp both sides filter on = **PayPal transaction/event time**: PayPal side via `start_date`/`end_date`; eShop side via `OrderPayment.ProcessedAt` (stored from PayPal capture/auth `create_time`), **not** a local row-creation column. |

### DUPLICATE CLAIMS

| write | where the claim is stored | what rejects the second one | where that rejection is caught | where in the code |
| --- | --- | --- | --- | --- |
| pay (authorize) | `OrderPayment.OrderId` | unique index on `OrderPayment.OrderId` | `DbUpdateException` catch | TBD |
| refund | `OrderRefund.IdempotencyKey` | unique index on `OrderRefund.IdempotencyKey` | `DbUpdateException` catch | TBD |
| save card | `SavedCard.(BuyerId,PayPalVaultId)` | unique index on `(BuyerId,PayPalVaultId)` | `DbUpdateException` catch | TBD |

### PAGED READS

| read | what caps it | how the caller learns the answer was cut short | where in the code |
| --- | --- | --- | --- |
| reconciliation `SearchTransactions` | loop over `TotalPages` per 31-day window; safety cap on windows*pages | `ReconciliationReport.Truncated` bool + counts in response body | TBD |

### REPEATED OPERATIONS

| operation | what tells you the state actually changed | the effects gated on that | where in the code |
| --- | --- | --- | --- |
| pay | `OrderPayment` absent / status==Authorizing before authorize | CreateOrder+AuthorizeOrder PayPal calls | TBD |
| fulfil | `OrderPayment.Status != Fulfilled` (no CaptureId) | CaptureAuthorizedPayment call | TBD |
| cancel | `OrderPayment.Status == Authorized` (not already Voided/Captured) | VoidPayment call | TBD |
| refund | `OrderRefund` with this idempotency key absent | RefundCapturedPayment call | TBD |

### UNKNOWN OUTCOMES

| write | the operation you re-read with | the reference you search by | where in the code |
| --- | --- | --- | --- |
| pay (create+authorize) | `Orders.GetOrder` | stored `OrderPayment.PayPalOrderId` | TBD |
| fulfil (capture) | `Payments.GetAuthorizedPayment` | stored `OrderPayment.AuthorizationId` | TBD |

### OPERATION OUTCOMES

| write | the status field | every value it can hold, and what the app does with each | where in the code |
| --- | --- | --- | --- |
| pay/CreateOrder | `Order.Status` (OrderStatus) | Created/Approved → proceed to authorize (done-enough to authorize); Saved → proceed; **PayerActionRequired → STOP/report challenge (not-yet, unsupported)**; Voided/Completed(unexpected here) → fail | TBD |
| pay/AuthorizeOrder | authorization `Status` (AuthorizationStatus) | Created → success (hold placed); Pending → not-done (surface pending); Denied/Voided → fail; Captured/PartiallyCaptured (unexpected) → fail | TBD |
| fulfil/Capture | `CapturedPayment.Status` (CaptureStatus) | Completed → success (record fee/net); Pending → not-done; Declined/Failed → fail; Partially/Refunded (unexpected) → fail | TBD |
| refund/Refund | `Refund.Status` (RefundStatus) | Completed → success; Pending → not-done (record pending); Failed/Cancelled → fail | TBD |
| cancel/Void | `PaymentAuthorization.Status` | Voided → success; anything else → fail | TBD |

### WRITE ORDER

| write | what exists locally BEFORE the call | what is written after it returns | where in the code |
| --- | --- | --- | --- |
| pay | `OrderPayment{OrderId,BuyerId,InvoiceId,Amount,Status=Authorizing}` | PayPalOrderId, AuthorizationId, AuthorizationStatus, ExpiresAt, ProcessedAt, Status=Authorized | TBD |
| fulfil | existing `OrderPayment` (Authorized) | CaptureId, CaptureStatus, CapturedAmount, PaypalFee, NetAmount, ProcessedAt, Status=Fulfilled | TBD |
| refund | `OrderRefund{IdempotencyKey,Amount,Status=Pending}` appended to `OrderPayment` | PayPalRefundId, Status, on OrderPayment: Status=Partially/Refunded | TBD |
| save card | (call first — vault has no pre-claim; then) `SavedCard{BuyerId,PayPalVaultId,Brand,LastDigits,Expiry}` | — (single insert after vault returns the id) | TBD |

> Note on "save card" WRITE ORDER: the vault token is created at PayPal first because the vault id
> IS the claim we store; there is no eShop-side reference to pre-persist (nothing to reconcile a
> half-made vault token against beyond the token id itself). On transport failure the token may exist
> orphaned in the vault — acceptable (no money moved; a later save re-vaults; orphan is inert). This is
> the one write where the local row is built from the provider response by necessity.

---

## 6. Assumptions & Blockers

- **A1.** "Save a card" without a browser is done via `Vault.CreatePaymentToken` with
  `payment_source.card` (direct card vaulting), since the sandbox business account is enabled for
  vaulting + direct card. `CreateSetupToken`→`CreatePaymentToken` is the browser/approval path and is
  **not** used. If direct `CreatePaymentToken` returns an approval-required link, that is the 3DS/
  challenge STOP-and-report case, not a new flow.
- **A2.** Paying with a saved card = `payment_source.card.vault_id = SavedCard.PayPalVaultId` on
  CreateOrder (per `CardRequest.VaultId`).
- **A3.** Order currency comes from `PayPal:Currency` config; amount = `Order.Total()` formatted to the
  currency's minor units (2 dp for USD; zero-decimal currency list handled).
- **A4.** invoice_id is made unique per order (`ESHOP-{orderId}-{shortGuid}`) to satisfy PayPal's
  per-merchant uniqueness across in-memory-reset runs; stored on `OrderPayment.InvoiceId` and used as
  the reconciliation match key against `TransactionInformation.InvoiceId`.
- **No Blockers.** Every capability the task needs maps to an in-scope SDK operation. Reconciliation
  reporting lag on freshly-created sandbox transactions is an expected empty-range result, not a gap.
