# PayPal Server SDK integration plan — eShopOnWeb PublicApi

Additive card-payments capability on `src/PublicApi`: pay for an order via PayPal (authorize →
capture at fulfil → void/refund), plus saved (vaulted) cards. SDK = APIMatic-generated
**PayPal Server SDK (.NET)**, root namespace `PayPalServerSdk`, referenced by building it from
source (vendored as a project; not on NuGet).

## 1. Scope & sequence

1. **Client & config** — bind `PayPalOptions` from the `PayPal:` section; register
   `PayPalServerSdkClient` as a singleton over `IHttpClientFactory`; fail-fast on missing creds;
   apply optional `PayPal:BaseUrl` override. Ops: none.
2. **Domain + persistence** — `OrderPayment` aggregate (+ owned `RefundRecord`s) linked to the
   existing `Order` by `OrderId`; `SavedPaymentMethod` aggregate. EF configs, unique indexes.
   Ops: none.
3. **Gateway** — `IPayPalGateway` wraps the SDK and translates errors. Ops:
   `Orders.CreateOrder`, `Orders.AuthorizeOrder`, `Payments.CaptureAuthorizedPayment`,
   `Payments.ReauthorizePayment`, `Payments.VoidPayment`, `Payments.RefundCapturedPayment`,
   `Vault.CreateSetupToken`, `Vault.CreatePaymentToken`, `Vault.DeletePaymentToken`,
   `TransactionSearch.SearchTransactions`.
4. **Order-payment flow** — `POST /api/orders` (create Order + OrderPayment=AwaitingPayment),
   `POST /api/orders/{id}/pay` (create PayPal order → authorize = the hold),
   `POST /api/orders/{id}/fulfil` (capture; reauthorize if stale),
   `POST /api/orders/{id}/cancel` (void), `POST /api/orders/{id}/refunds` (refund, keyed),
   `GET /api/my-orders`, `GET /api/reconciliation`.
5. **Saved cards** — `POST /api/payment-methods` (setup-token → payment-token vault),
   `GET /api/payment-methods`, `DELETE /api/payment-methods/{id}`.

Chosen PayPal flow for `pay`: **two-step** — `CreateOrder(intent=AUTHORIZE, purchase_units=[amount],
no payment_source)` then `AuthorizeOrder(id, body.payment_source.card=…)`. `AuthorizeOrder` remarks
(`Api/Orders.cs`): *"To successfully authorize payment for an order, the buyer must first approve the
order or a valid payment_source must be provided in the request."* — so passing the card (or vaulted
`card.vault_id`) at authorize does unbranded direct-card processing with no browser step. A stable
`PayPal-Request-Id` per local order makes both calls idempotent.

## 2. CONTRACT SHEET

⚠ **Signatures below are generated code, verbatim. Every parameter name is the literal C#
identifier — named arguments must use them exactly (the cancellation-token parameter is `ct`, so
write `ct:`). Nullable-no-default params (`payPalMockResponse`, `payPalAuthAssertion`, `body`, …)
MUST be passed explicitly (pass `null` to skip).**
⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**
(`Models/*` → `PayPalServerSdk.Models`; `Models/Enums/*` → `PayPalServerSdk.Models.Enums`;
`Errors/*` → `PayPalServerSdk.Errors`; client/options root → `PayPalServerSdk`;
`Servers/*` → `PayPalServerSdk.Servers`; `Core/ErrorResponse/RawError` →
`PayPalServerSdk.Core.ErrorResponse`; `Core/Exceptions/SdkException` →
`PayPalServerSdk.Core.Exceptions`).

| Op | Signature (params) | Request model → fields used | Response envelope → fields read | Error case + accessors | Source |
| --- | --- | --- | --- | --- | --- |
| `Orders.CreateOrder` | `(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions?, ct)` | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent, required`; `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest>, required` — each `Amount (amount): AmountWithBreakdown required` {`CurrencyCode` req, `Value` req}, `CustomId` (reconcile→local orderId), `InvoiceId` (reconcile→unique), `Description` (purpose: statement/report label). Omit `PaymentSource` (two-step). | `Order`: `Id (id)`, `Status (status): OrderStatus` | A `SdkException<CreateOrderError>` · `TryGetError(out Error)`[400,401,422] · `TryGetRawError` | Orders.md; `Models/OrderRequest.cs`, `Models/PurchaseUnitRequest.cs`, `Models/AmountWithBreakdown.cs`, `Models/Order.cs`, `Errors/CreateOrderError.cs`, `Models/Error.cs` |
| `Orders.AuthorizeOrder` | `(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", RequestOptions?, ct)` | `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` → `Card (card): CardRequest?` {`Name`,`Number`,`Expiry`(YYYY-MM),`SecurityCode`,`BillingAddress`,`VaultId` (saved-card path — set `VaultId` INSTEAD of raw PAN)} | `OrderAuthorizeResponse`: `Id (id)`, `Status (status)`, `PurchaseUnits[].Payments (payments): PaymentCollection` → `Authorizations[]: AuthorizationWithAdditionalData` {`Id`, `Status: AuthorizationStatus`, `Amount: Money`} | A `SdkException<AuthorizeOrderError>` · `TryGetError`[400,401,403,404,422,500] · `TryGetRawError` | Orders.md; `Models/OrderAuthorizeRequest.cs`, `Models/OrderAuthorizeRequestPaymentSource.cs`, `Models/CardRequest.cs`, `Models/OrderAuthorizeResponse.cs`, `Models/PaymentCollection.cs`, `Models/AuthorizationWithAdditionalData.cs` |
| `Payments.CaptureAuthorizedPayment` | `(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", RequestOptions?, ct)` | `CaptureRequest`: `Amount (amount): Money?` (omit → full authorized amount captured; set for exact-to-cent), `FinalCapture (final_capture): bool?`=true (purpose: close the auth) | `CapturedPayment`: `Id (id)`, `Status (status): CaptureStatus`, `Amount: Money`, `SellerReceivableBreakdown (seller_receivable_breakdown)` → `GrossAmount` (captured), `PaypalFee`, `NetAmount` | A `SdkException<CaptureAuthorizedPaymentError>` · `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` | Payments.md; `Models/CaptureRequest.cs`, `Models/Money.cs`, `Models/CapturedPayment.cs`, `Models/SellerReceivableBreakdown.cs` |
| `Payments.ReauthorizePayment` | `(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", RequestOptions?, ct)` | `ReauthorizeRequest`: `Amount (amount): Money?` (omit → original amount) | `PaymentAuthorization`: `Id (id)`, `Status (status): AuthorizationStatus`, `ExpirationTime` | A `SdkException<ReauthorizePaymentError>` · `TryGetError`[400,401,403,404,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; `Models/ReauthorizeRequest.cs`, `Models/PaymentAuthorization.cs` |
| `Payments.VoidPayment` | `(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", RequestOptions?, ct)` | (no body) | `PaymentAuthorization` (may be empty/204 on void) — read `Status` if present | A `SdkException<VoidPaymentError>` · `TryGetError`[401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; `Models/PaymentAuthorization.cs` |
| `Payments.RefundCapturedPayment` | `(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", RequestOptions?, ct)` | `RefundRequest`: `Amount (amount): Money?` (omit → full refund; set for partial), `NoteToPayer`, `CustomId`(→local orderId) | `Refund`: `Id (id)`, `Status (status): RefundStatus`, `Amount: Money`, `SellerPayableBreakdown` → `TotalRefundedAmount` | A `SdkException<RefundCapturedPaymentError>` · `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; `Models/RefundRequest.cs`, `Models/Refund.cs`, `Models/SellerPayableBreakdown.cs` |
| `Vault.CreateSetupToken` | `(string? payPalRequestId, SetupTokenRequest body, RequestOptions?, ct)` | `SetupTokenRequest`: `PaymentSource (payment_source): SetupTokenRequestPaymentSource, required` → `Card (card): SetupTokenRequestCard` {`Name`,`Number`,`Expiry`,`SecurityCode`,`BillingAddress`}; `Customer (customer): Customer?` → `MerchantCustomerId` (purpose: associate to shopper) | `SetupTokenResponse`: `Id (id)`, `Status: PaymentTokenStatus` | A `SdkException<CreateSetupTokenError>` · `TryGetError`[400,403,422,500] · `TryGetRawError` | Vault.md; `Models/SetupTokenRequest.cs`, `Models/SetupTokenRequestPaymentSource.cs`, `Models/SetupTokenRequestCard.cs`, `Models/Customer.cs`, `Models/SetupTokenResponse.cs` |
| `Vault.CreatePaymentToken` | `(string? payPalRequestId, PaymentTokenRequest body, RequestOptions?, ct)` | `PaymentTokenRequest`: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource, required` → `Token (token): VaultTokenRequest` {`Id (id) req` = setup-token id, `Type (type): VaultTokenRequestType req` = `SetupToken`}; `Customer` optional | `PaymentTokenResponse`: `Id (id)` = **vault id (store)**, `PaymentSource → Card: CardPaymentTokenEntity` {`LastDigits`, `Brand`, `Expiry`, `Name`} (safe display) | A `SdkException<CreatePaymentTokenError>` · `TryGetError`[400,403,404,422,500] · `TryGetRawError` | Vault.md; `Models/PaymentTokenRequest.cs`, `Models/PaymentTokenRequestPaymentSource.cs`, `Models/VaultTokenRequest.cs`, `Models/PaymentTokenResponse.cs`, `Models/PaymentTokenResponsePaymentSource.cs`, `Models/CardPaymentTokenEntity.cs` |
| `Vault.DeletePaymentToken` | `(string id, RequestOptions?, ct)` | — | `void` | A `SdkException<DeletePaymentTokenError>` · `TryGetError`[400,403,500] · `TryGetRawError` | Vault.md; `Errors/DeletePaymentTokenError.cs` |
| `TransactionSearch.SearchTransactions` | `(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, RequestOptions?, ct)` | query params only; `startDate`/`endDate` = ISO-8601 (**required**); iterate `page`=1..`total_pages` | `SearchResponse`: `TransactionDetails[]` → `TransactionInfo: TransactionInformation` {`TransactionId`, `TransactionStatus`(D/P/S/V), `TransactionAmount: Money`, `TransactionInitiationDate`, `InvoiceId`, `CustomField`}; `TotalPages`, `Page`, `TotalItems` | **B** `SdkException<RawError>` (no typed accessors — `StatusCode`, `ReadAsString`, `ReadAsJson<T>`) | TransactionSearch.md; `Models/SearchResponse.cs`, `Models/TransactionDetails.cs`, `Models/TransactionInformation.cs` |

Enums (values verbatim; build via static members or `Type.FromValue("WIRE")`):
- `CheckoutPaymentIntent` (`Models/Enums/CheckoutPaymentIntent.cs`): `Authorize`="AUTHORIZE", `Capture`="CAPTURE".
- `OrderStatus`: Created, Saved, Approved, Voided, Completed, PayerActionRequired.
- `AuthorizationStatus`: Created, Captured, Denied, PartiallyCaptured, Voided, Pending.
- `CaptureStatus`: Completed, Declined, PartiallyRefunded, Pending, Refunded, Failed.
- `RefundStatus`: Cancelled, Failed, Pending, Completed.
- `VaultTokenRequestType` (`Models/Enums/VaultTokenRequestType.cs`): `SetupToken`="SETUP_TOKEN".

Client construction / auth / server (source: `sdk-map.md` Getting-a-client + Servers & auth,
`PayPalServerSdkClientOptions.cs`, `ServiceCollectionExtensions.cs`, `AuthSchemes.cs`,
`Servers/DefaultOptions.cs`):
- Only ctor: `new PayPalServerSdkClient(HttpClient, PayPalServerSdkClientOptions)`. Options:
  `Oauth2 = new OAuth2ClientCredentials { ClientId=…, ClientSecret=… }`
  (`PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`),
  `Environment = ServerEnvironment.Sandbox` (`PayPalServerSdk.Servers`; only member; from config).
- **BaseUrl override**: set `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>`. Verified
  `AuthSchemes.cs` builds the token URL as `server.Default("/v1/oauth2/token")`, which resolves
  through `DefaultOptions.Sandbox.BaseUrl` — so the override applies to the token request too.
- Token endpoint (default): `https://api-m.sandbox.paypal.com/v1/oauth2/token`, managed by the SDK.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| the saved-card `paymentMethodId` used to `pay` must be one previously created & owned by the caller | `AuthorizeOrder` (card.vault_id) ← `POST /api/payment-methods` (`Vault.CreatePaymentToken`) | implementation: look up `SavedPaymentMethod` by id + BuyerId, use its `VaultId`; deleted card 404s |
| the `authorizationId` captured/voided/reauthorized must be the one AuthorizeOrder returned for that order | `CaptureAuthorizedPayment`/`VoidPayment`/`ReauthorizePayment` ← `AuthorizeOrder` | implementation: persisted on `OrderPayment`, never caller-supplied |
| the `captureId` refunded must be the one CaptureAuthorizedPayment returned for that order | `RefundCapturedPayment` ← `CaptureAuthorizedPayment` | implementation: persisted on `OrderPayment` |
| reconciliation matches PayPal transactions to eShop orders on a shared external id | `SearchTransactions` ↔ orders | implementation: set `invoice_id`+`custom_id` on CreateOrder = per-order external id; match on `TransactionInformation.InvoiceId`/`CustomField` |

## 3. Trap notes (name the hazard; do not resolve here)

- **Client lifetime / HttpClient**: the SDK client must be long-lived over a pooled `HttpClient`
  (`IHttpClientFactory`), not rebuilt per request. → `MUST load dotnet-client-initialization`.
- **Credentials placement + rotation capture**: where credentials are set relative to construction,
  and that options are captured once. → `MUST load dotnet-authentication`.
- **Named-argument binding for optional params with no C# default** on Orders/Payments/Vault ops
  (the `payPalMockResponse … body` chain, `SearchTransactions`' 8-nullable chain). → `MUST load
  dotnet-calling-endpoints`.
- **Models**: `StringEnum<T>` are not C# enums; `AdditionalProperties` extension bag; `required`
  initializer members; wire names ≠ C# names. → `MUST load dotnet-models`.
- **Error boundary**: which exception type reaches each catch; Case A typed vs Case B raw;
  `TryGetNoContent` on Payments ops; and the two `System.Text.Json.JsonException` directions. →
  `MUST load dotnet-error-handling`.
- **Timeouts/retries/base-URL/pagination/logging**: `Timeout` is per-attempt not total; default
  `HttpMethodsToRetry` never resends POST; `LogRequestBody` logs JSON unredacted; pagination is
  manual. → `MUST load dotnet-configuration-resilience`.
- **Testing seam**: the `HttpClient` ctor arg is the fake seam; match the project's frameworks. →
  `MUST load dotnet-testing`.

## 4. REQUIRED READING (load all before implementation; contents deliberately not inlined here)

- `paypal-platforms-team:dotnet-client-initialization` — step 1 (client & DI).
- `paypal-platforms-team:dotnet-authentication` — step 1 (OAuth2 client-credentials).
- `paypal-platforms-team:dotnet-calling-endpoints` — step 3–5 (every SDK call; named args).
- `paypal-platforms-team:dotnet-models` — step 3–5 (request/response models, enums, unions).
- `paypal-platforms-team:dotnet-error-handling` — gateway error boundary (always required).
- `paypal-platforms-team:dotnet-configuration-resilience` — client tuning, base-URL, pagination, logging.
- `paypal-platforms-team:dotnet-testing` — integration-layer tests.

Both mandatory `JsonException` hazards apply: a drifted/malformed **2xx** body surfaces as
`System.Text.Json.JsonException` from deserialization (NOT `SdkException`) and escapes an
SDK-exception-only ladder; a **non-2xx** body that doesn't match its `{Operation}Error` shape throws
`JsonException` while constructing the error object, replacing the `SdkException` and destroying the
HTTP status. The gateway's catch ladder catches `SdkException<…>` AND `System.Text.Json.JsonException`
AND a general `Exception` fallback, translating each to a domain `PayPalGatewayException`.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` bound from `PayPal:` in `Infrastructure/Dependencies` (or a `PaymentDependencies`); an `IValidateOptions<PayPalOptions>`/startup check throws if `ClientId`, `ClientSecret`, `Environment`, or `Currency` is missing/blank (each part checked separately — blank ≠ missing). `BaseUrl` optional. Host refuses to start; no discovery via first-call 401. |
| 2 | Secret sourcing & rotation | Secrets from **.NET user-secrets** (`PayPal:ClientId/ClientSecret/Environment/Currency`), loaded from env vars by me; never in repo. Options object built once at registration and captured in the singleton SDK client → a rotated secret takes effect on process restart. Restartless rotation not required; documented as restart-required. |
| 3 | Total timeout budget | SDK `Timeout` is per-attempt. Retries left at SDK default but POST/PATCH/DELETE are not resent (row 4), so the multiplied-timeout risk applies only to GET-shaped calls (`GetOrder` re-reads). A per-call `CancellationToken` from the ASP.NET request bounds the whole call; the gateway passes `ct`. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET,HEAD,PUT,OPTIONS → all our writes are POST/DELETE, never auto-resent by the SDK. Idempotency (row 5) covers deliberate re-sends. |
| 5 | Idempotency & ambiguous writes | Real caller-supplied key exists as `payPalRequestId` (PayPal-Request-Id) on CreateOrder/AuthorizeOrder/Capture/Reauthorize/Void/Vault-create. **As shipped**, the per-order reference is the run-unique `OrderPayment.InvoiceId` (`ESHOP-{orderId}-{guid}`) plus a per-op suffix (`-create`/`-auth`/`-capture`/`-capture-renewed`/`-reauth`/`-void`) — required because the in-memory store restarts order ids at 1 each run while PayPal caches responses per PayPal-Request-Id and enforces `invoice_id` uniqueness across runs; still stable across retries within a run so a double-click never authorizes/captures twice. **Refund** uses the **caller-supplied idempotency key** as PayPal-Request-Id AND as a locally-unique `RefundRecord.IdempotencyKey` (row 9) → repeat key = same refund; distinct keys = distinct partial refunds. `DeletePaymentToken` has no key: idempotent by nature (delete-if-exists) + local removal. |
| 6 | Observability | Info: state transitions (authorized/captured/voided/refunded) with local order id + PayPal id. Warning/Error: gateway failures with PayPal `debug_id` (from `Error.DebugId`/`RawError`) — the correlation id reaching our logs. `LogRequestBody` stays OFF (row 7). No card fields ever logged. |
| 7 | Sensitive data | CardRequest/SetupTokenRequestCard carry PAN/CVV/expiry. Posture: `options.Logging.LogRequestBody=false` (SDK default) AND `LoggerFactory` set explicitly (via `AddPayPalServerSdkClient` DI which assigns `sp.GetService<ILoggerFactory>()`) so `PAYPALSERVERSDKCLIENT_LOG` cannot switch body logging on from outside code. Our own code never logs request DTOs; card DTOs excluded from any log. Full PAN never persisted (only vault id + last4/brand from response). |
| 8 | Environment selection | One server group `Default`; sole environment `ServerEnvironment.Sandbox`. `options.Environment` from `PayPal:Environment` (must be `Sandbox`); test traffic kept off live by there being no non-sandbox host in the SDK, and `PayPal:BaseUrl` override (when set) points every call — including token — at the given base. |
| 9 | Duplicate prevention under concurrency | Store `OrderPayments`, column `OrderId` — **unique index** (`OrderConfiguration`-style EF config) rejects a second payment row for the same order; the insert is wrapped and `DbUpdateException` is caught → treated as "already exists", reloaded. Store `Refunds` (owned collection / table `OrderPaymentRefunds`), column `IdempotencyKey` — **unique index**; duplicate refund insert caught → returns existing. ⚠ Caveat (NOT the mechanism): the mandated dev harness uses the EF **InMemory** provider which does not enforce indexes; the unique-index+catch is the production (SQL Server) mechanism, and PayPal-Request-Id idempotency is the second line so even a dev-only double-insert cannot double-charge. |
| 10 | Partial results | `SearchTransactions` is paged (`pageSize`=100). Reconciliation loops `page`=1..`SearchResponse.TotalPages` and aggregates, so the whole range is covered. The report DTO carries `PagesScanned` and `TotalItems` so a caller sees the scope; no silent truncation. |
| 11 | Startup validation vs test host | The host-booting test project is **`tests/PublicApiIntegrationTests`** (`WebApplicationFactory<Program>`), configured by its `appsettings.test.json`. I add **placeholder** `PayPal:*` values there (non-secret) so the fail-fast passes at boot; no PayPal call is made unless a payment endpoint is exercised. This project is run and must be green. |
| 12 | Ordering & no-op side effects | `OrderPayment` row is written (AwaitingPayment) at order creation, BEFORE any provider call; PayPal ids are persisted as each call returns (PayPalOrderId after CreateOrder, AuthorizationId after AuthorizeOrder, CaptureId after capture). Each transition is gated on current status: `pay` on an already-Authorized order is a no-op returning current state; `fulfil`/`cancel`/`refund` reject illegal source states. No unconditional side effects. |
| 13 | Unknown outcomes | On transport failure after a write may have been received: `pay` resumes idempotently — if `PayPalOrderId` is stored but no `AuthorizationId`, it re-issues `AuthorizeOrder` (same PayPal-Request-Id) or re-reads via `Orders.GetOrder(payPalOrderId)`; capture/void/refund re-runs under the same PayPal-Request-Id which PayPal de-dups. Re-read operation = `Orders.GetOrder`, searched by stored `PayPalOrderId`. |
| 14 | Provider status & reconciliation clock | Status fields branched on: `OrderAuthorizeResponse`/`AuthorizationWithAdditionalData.Status` (must be Created/Pending→treat Pending as not-yet-held and surface), `CapturedPayment.Status` (Completed vs Declined/Pending/Failed → only Completed marks Fulfilled; others surface an operator error), `RefundStatus`, reauth `AuthorizationStatus`. No `?? "COMPLETED"`. Reconciliation clock: both sides filter on the **transaction initiation date** — PayPal via `SearchTransactions` `start_date`/`end_date`; the report compares against orders by the shared `invoice_id`/`custom_id`, not a local row-creation column. |

## 6. Assumptions & Blockers

- **Shipping address**: the task's `POST /api/orders` describes only item ids + quantities. `Order`
  requires a `ShipToAddress`. Assumption: accept an optional address in the request; default to a
  placeholder ("N/A") when omitted. Minor — proceed.
- **Currency decimals**: amounts formatted to 2 decimals (`0.00`, InvariantCulture), correct for the
  configured USD-class currency; matched to the cent against `Order.Total()`. Minor — proceed.
- **Amount to the cent**: capture passes the exact authorized `Money` amount (not omit) so the held,
  captured, and order-total amounts are equal to the cent.
- **Vaulting raw card without browser**: sandbox business account is enabled for direct card
  processing and vaulting (per task), so setup-token→payment-token with a raw card needs no approval
  round-trip. If PayPal returns a 3DS/`PAYER_ACTION_REQUIRED` challenge on a card authorize, that is
  the task's STOP-and-report case — the gateway surfaces it as a distinct error, not an approval flow.
- No Blockers: every capability the flows need maps to an SDK operation above.

### Implementation notes (verified against the live sandbox)

- **`VoidPayment` returns 204 No Content**, and the SDK throws `System.Text.Json.JsonException` deserializing
  the empty body into `PaymentAuthorization` (the 2xx-empty-body trap from `dotnet-error-handling`). The
  gateway's `VoidAsync` treats a `JsonException` on void as success — a genuine void failure comes back as
  `SdkException<VoidPaymentError>` (its `RawError` fallback never fails to construct), so the two are
  distinguishable. Verified: cancel → status `Cancelled`, `authorizationStatus VOIDED`.
- **`invoice_id` uniqueness / request-id caching** (see row 5) drove the run-unique `InvoiceId`.
- **No EF migrations are generated**: this machine mandates the EF InMemory provider (`UseOnlyInMemoryDatabase=true`),
  which ignores migrations and builds the model (including the unique indexes) from the configuration at
  startup. The unique indexes are the production (SQL Server) enforcement mechanism for row 9; a migration
  would be generated from the same `IEntityTypeConfiguration` classes for a relational deployment.
- **Live end-to-end verified** on the sandbox with Visa `4111 1111 1111 1111`: authorize (hold, amount to
  the cent), capture at fulfil (PayPal fee 1.24, net 27.76 recorded), partial refund + idempotent repeat +
  second partial + over-refund rejected, saved-card vault → reuse to pay a second order → capture → delete →
  no longer usable (404), cancel/void, and reconciliation (returned 40 provider transactions; freshly-created
  captures show under `onlyInEShop` because PayPal's transaction reporting lags — an expected sandbox result).
