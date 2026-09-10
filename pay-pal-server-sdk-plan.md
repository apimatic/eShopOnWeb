# PayPal Server SDK (.NET) integration plan — eShopOnWeb PublicApi

Additive "collect money" capability on `src/PublicApi`: place order → authorize (hold) → fulfil
(capture) → cancel (void) / refund, plus saved cards (vault) and a reconciliation report.
All PayPal facts below come from the SDK map / SDK source (session clone), never memory.

## 1. Scope & sequence

Domain (reuse existing `Order`/`OrderItem`/`Address`/`CatalogItemOrdered` aggregate):
1. New EF aggregate `Payment` (+ child `PaymentRefund`) on `CatalogContext`; new `SavedCard` aggregate.
2. Integration seam `IPayPalGateway` (ApplicationCore/Interfaces) implemented in Infrastructure over the SDK.
3. `IPaymentService` / `IOrderPlacementService` orchestration in ApplicationCore.
4. PublicApi endpoints (MinimalApi.Endpoint pattern).

PayPal call sequence per endpoint (operation → map page):
- `POST /api/orders` — no PayPal call. Create eShop `Order` + `Payment{AwaitingPayment}`.
- `POST /api/orders/{id}/pay` — `Orders.CreateOrder` (intent=AUTHORIZE, purchase_units only, payment_source.card
  = raw card OR `vault_id`) then, only if the response has no authorization yet, `Orders.AuthorizeOrder`.
  Hold = the authorization in `purchase_units[0].payments.authorizations[0]`.
- `POST /api/orders/{id}/fulfil` — `Payments.CaptureAuthorizedPayment` (final_capture=true). If the auth is
  stale: `Payments.GetAuthorizedPayment` → if EXPIRED `Payments.ReauthorizePayment` → capture the new auth.
- `POST /api/orders/{id}/cancel` — `Payments.VoidPayment` (release hold).
- `POST /api/orders/{id}/refunds` — `Payments.RefundCapturedPayment` (full or partial).
- `GET /api/my-orders` — no PayPal call (reads local `Payment`).
- `GET /api/reconciliation` — `TransactionSearch.SearchTransactions`, paged over `total_pages`.
- `POST /api/payment-methods` — `Vault.CreateSetupToken` (payment_source.card) → `Vault.CreatePaymentToken`
  (payment_source.token = setup token). *(Verified: vaulting a raw card directly via CreatePaymentToken
  500s for server-side card processing; the two-step setup-token→payment-token flow is required.)*
- `GET /api/payment-methods` — no PayPal call (reads local `SavedCard`).
- `DELETE /api/payment-methods/{id}` — `Vault.DeletePaymentToken`.

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim: every parameter name is the literal C# identifier
(cancellation-token param is `ct`, so named args write `ct:`). Nullable-no-default params MUST be
passed explicitly (`null` to skip).
⚠ Every SDK type is written fully-qualified with the namespace its **source path** implies
(`Models/` → `PayPalServerSdk.Models`; `Models/Enums/` → `PayPalServerSdk.Models.Enums`;
`Errors/` → `PayPalServerSdk.Errors`; client/options → `PayPalServerSdk`;
`ServerEnvironment` → `PayPalServerSdk.Servers`).

Client construction: `new PayPalServerSdkClient(HttpClient, PayPalServerSdkClientOptions)` OR DI
`services.AddPayPalServerSdkClient(opts => …)`. Options: `Oauth2 = new OAuth2ClientCredentials{ ClientId, ClientSecret }`
(ns `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`), `Environment = ServerEnvironment.Sandbox`,
base-URL override `options.Server.Default.Sandbox.BaseUrl` (used verbatim when `PayPal:BaseUrl` is set).
Auth on every op: `options.Oauth2`. Source: sdk-map.md (Getting a client, Servers & auth).

| Op | Signature (verbatim) | Request fields used | Response fields read | Error case |
| --- | --- | --- | --- | --- |
| `Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `OrderRequest{ Intent(req), PurchaseUnits(req), PaymentSource? }`; pass `payPalRequestId` (idempotency), `prefer:"return=representation"` | `Order{ Id, Status, PurchaseUnits[].Payments.Authorizations[] }` | A `SdkException<CreateOrderError>`; `TryGetError(out Error)` [400,401,422] · `TryGetRawError` |
| `Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `OrderAuthorizeRequest{ PaymentSource? }` (`OrderAuthorizeRequestPaymentSource{ Card? }`); `payPalRequestId`, `prefer:"return=representation"` | `OrderAuthorizeResponse{ Id, Status, PurchaseUnits[].Payments.Authorizations[] }` | A `SdkException<AuthorizeOrderError>`; `TryGetError(out Error)` [400,401,403,404,422,500] |
| `Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `CaptureRequest{ Amount?(Money), FinalCapture?=true }`; `payPalRequestId`, `prefer:"return=representation"` | `CapturedPayment{ Id, Status, Amount(Money), SellerReceivableBreakdown{ GrossAmount, PaypalFee?, NetAmount? } }` | A `SdkException<CaptureAuthorizedPaymentError>`; `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] |
| `Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions?=null, CancellationToken ct=default)` | — | `PaymentAuthorization{ Id, Status, Amount }` | A `SdkException<GetAuthorizedPaymentError>`; `TryGetError` [401,403,404] |
| `Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `ReauthorizeRequest{ Amount?(Money) }`; `payPalRequestId` | `PaymentAuthorization{ Id, Status }` | A `SdkException<ReauthorizePaymentError>`; `TryGetError` [400,401,403,404,422] |
| `Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `payPalRequestId` | `PaymentAuthorization{ Id, Status }` (may be empty body) | A `SdkException<VoidPaymentError>`; `TryGetError` [401,403,404,409,422] |
| `Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` | `RefundRequest{ Amount?(Money) }` (omit body ⇒ full refund); `payPalRequestId` = **caller idempotency key**, `prefer:"return=representation"` | `Refund{ Id, Status, Amount(Money), SellerPayableBreakdown? }` | A `SdkException<RefundCapturedPaymentError>`; `TryGetError` [400,401,403,404,409,422] |
| `Vault.CreateSetupToken` | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions?=null, CancellationToken ct=default)` | `SetupTokenRequest{ Customer?{ Id?, MerchantCustomerId? }, PaymentSource(req)=SetupTokenRequestPaymentSource{ Card?=SetupTokenRequestCard{ Number, Expiry, SecurityCode?, Name? } } }`; `payPalRequestId` | `SetupTokenResponse{ Id, Customer?{ Id } }` | A `SdkException<CreateSetupTokenError>`; `TryGetError` [400,403,422,500] |
| `Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions?=null, CancellationToken ct=default)` | `PaymentTokenRequest{ Customer?{ Id? }, PaymentSource(req)=PaymentTokenRequestPaymentSource{ Token?=VaultTokenRequest{ Id(req), Type(req)=VaultTokenRequestType.SetupToken } } }`; `payPalRequestId` | `PaymentTokenResponse{ Id, Customer?{ Id }, PaymentSource?{ Card?=CardPaymentTokenEntity{ LastDigits, Brand, Expiry, Name } } }` | A `SdkException<CreatePaymentTokenError>`; `TryGetError` [400,403,404,422,500] |
| `Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions?=null, CancellationToken ct=default)` | — | void | A `SdkException<DeletePaymentTokenError>`; `TryGetError` [400,403,500] |
| `TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, RequestOptions?=null, CancellationToken ct=default)` | `startDate`,`endDate` ISO-8601; pass 8 nullable filters `null`; `fields:"transaction_info"`, page loop `1..total_pages` | `SearchResponse{ TransactionDetails[]{ TransactionInfo{ TransactionId, TransactionAmount(Money), TransactionStatus, InvoiceId, CustomField, TransactionInitiationDate } }, TotalPages, Page }` | **B** `SdkException<RawError>` (`StatusCode`, `ReadAsString()`) |

Value/enum facts (source in parens):
- `Money{ CurrencyCode(req), Value(req) }` and `AmountWithBreakdown{ CurrencyCode(req), Value(req), Breakdown? }` —
  `Value` is a **string**, regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`; format order total as invariant `F2`. (`Models/Money.cs`, `Models/AmountWithBreakdown.cs`)
- `PurchaseUnitRequest{ Amount(req)=AmountWithBreakdown, CustomId?, InvoiceId?, Description? }` — set `CustomId`=eShop orderId,
  `InvoiceId`=unique per order (reconciliation join). (`Models/PurchaseUnitRequest.cs`)
- Pay payment source = `PaymentSource{ Card?=CardRequest{ Number?, Expiry?("YYYY-MM"), SecurityCode?, Name?, VaultId? } }`.
  One-off ⇒ Number+Expiry+SecurityCode. Saved card ⇒ `VaultId` only (no number/cvv). (`Models/PaymentSource.cs`, `Models/CardRequest.cs`)
  ⚠ `TokenType` enum has ONLY `BillingAgreement`("BILLING_AGREEMENT") — it is NOT a vaulted-card token, so a saved
  card is paid via `CardRequest.VaultId`, never `PaymentSource.Token`. (`Models/Enums/TokenType.cs`)
- `CheckoutPaymentIntent.Authorize`("AUTHORIZE") / `.Capture`("CAPTURE"). (`Models/Enums/CheckoutPaymentIntent.cs`)
- `OrderStatus`: Created, Approved, Completed, Voided, **PayerActionRequired**("PAYER_ACTION_REQUIRED" ⇒ 3DS challenge ⇒ STOP/report). (`Models/Enums/OrderStatus.cs`)
- `AuthorizationStatus`: Created, Captured, PartiallyCaptured, Voided, Denied, Pending; expiry via `PaymentAuthorization.Status`
  (there is no EXPIRED member — a stale auth surfaces as a 4xx on capture / re-fetch; treat capture failure whose
  auth status ≠ Created/Captured as reauthorize-then-capture). (`Models/Enums/AuthorizationStatus.cs`)
- `CaptureStatus`, `RefundStatus` — read `.Status.Value` as string for persistence. StringEnum `.Value` = wire string; `==` compares members. (`Core/Enum/TypedEnum.cs`)
- `Error{ Name(req), Message(req), DebugId(req), Details? }` — surface `Name`+`Message`, log `DebugId`. (`Models/Error.cs`)

## 3. Trap notes (hazard + skill; NOT resolved here)

- Client lifetime: the `HttpClient`/handler pipeline must be long-lived & pooled, not per-request; SDK-client wrapper lifetime differs. **MUST load dotnet-client-initialization.**
- Credentials must be set before the client is constructed / in the DI callback and captured once. **MUST load dotnet-authentication.**
- List/search & vault ops have optional params with no C# default that mis-bind positionally; the real idempotency key is a named param (`payPalRequestId`), the injected `Idempotency-Key` header is not one. **MUST load dotnet-calling-endpoints.**
- Building nested request models (enums are `StringEnum<T>` not C# enums; `required` initializers; extension-data bag) and reading response envelopes one level down. **MUST load dotnet-models.**
- `JsonException` reaches the boundary from a drifted 2xx body (NOT an `SdkException`) and from a non-2xx body that
  doesn't match `{Operation}Error` (replaces the `SdkException`, destroying the status); Case-A vs Case-B accessors;
  `TryGetNoContent` [500] on Payments ops. **MUST load dotnet-error-handling.**
- `Timeout` is per-attempt not total; `HttpMethodsToRetry` default excludes POST; `LogRequestBody` logs JSON
  unredacted and `PAYPALSERVERSDKCLIENT_LOG` can arm it unless `LoggerFactory` is set; SearchTransactions paging is manual. **MUST load dotnet-configuration-resilience.**
- Test seam is the `HttpClient` constructor arg; match MSTest style of `tests/PublicApiIntegrationTests`. **MUST load dotnet-testing.**

## 4. REQUIRED READING (load all before implementing; contents deliberately not carried here)

- `paypal-platforms-team:dotnet-client-initialization` — client/DI construction & lifetime.
- `paypal-platforms-team:dotnet-authentication` — OAuth2 client-credentials wiring.
- `paypal-platforms-team:dotnet-calling-endpoints` — named args, idempotency key param.
- `paypal-platforms-team:dotnet-models` — building/reading SDK models & enums.
- `paypal-platforms-team:dotnet-error-handling` — Case A/B, JsonException from both directions (mandatory).
- `paypal-platforms-team:dotnet-configuration-resilience` — timeout budget, retry ownership, logging redaction, paging.
- `paypal-platforms-team:dotnet-testing` — HttpClient seam, MSTest style.

Hazard rows (verbatim, always included):
1. A drifted/malformed **2xx** body (missing `required` member) surfaces as `System.Text.Json.JsonException` from
   deserialization, **not** an `SdkException` — an SDK-exception-only catch ladder lets it escape.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` throws `JsonException`
   **while the error object is being constructed**, so it **replaces** the `SdkException` and the HTTP status is destroyed with it.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind `PayPal:` section to `PayPalOptions`; a startup validator throws if `ClientId`, `ClientSecret`, `Environment`, or `Currency` is missing/blank (every part checked). Host refuses to start rather than 401 on first call. |
| 2 | Secret sourcing & rotation | Secrets from .NET user-secrets (loaded from env vars by me), bound at registration; DI builds `PayPalServerSdkClientOptions` once in the singleton client factory → rotation needs a process restart (documented; acceptable for this app). |
| 3 | Total timeout budget | SDK `Timeout` is per-attempt; the caller bound is a `CancellationToken` deadline (~30s) created per PayPal call in the gateway and passed as `ct:`. Enforced in `PayPalGateway`. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS ⇒ our POSTs (create/authorize/capture/void/refund/vault) are never auto-resent by the SDK. Safe re-drive is our own idempotency (row 5), not SDK retry. |
| 5 | Idempotency & ambiguous writes | Each write carries a deterministic `payPalRequestId`: create=`ord-{orderId}`, authorize=`pay-{orderId}`, capture=`cap-{orderId}`, void=`void-{orderId}`, vault=`vlt-{buyer}-{guid-stored}`. Refund uses the **caller-supplied idempotency key** verbatim as `payPalRequestId`; repeated key ⇒ same PayPal refund + our stored refund row returned, distinct keys ⇒ distinct partial refunds. Local `Payment` state guards double-authorize/double-capture in effect. |
| 6 | Observability | Info: op name + eShop orderId + PayPal ids + status. Error: PayPal `Error.Name`/`Message`/`DebugId` (correlation) at Warning/Error. `LogRequestBody` stays OFF. No card data logged. |
| 7 | Sensitive data | Pay & vault requests carry PAN/CVV (`CardRequest`/`PaymentTokenRequestCard`). ⇒ `options.Logging.LogRequestBody` never enabled **and** `options.Logging.LoggerFactory` set explicitly so `PAYPALSERVERSDKCLIENT_LOG` cannot arm body logging. App never persists PAN/CVV (only PayPal `vault_id`, last4, brand, expiry) and never logs them. |
| 8 | Environment selection | One server group `Default`; only env is `ServerEnvironment.Sandbox` (base `https://api-m.sandbox.paypal.com`). `PayPal:Environment`=sandbox ⇒ Sandbox; `PayPal:BaseUrl` when set overrides `options.Server.Default.Sandbox.BaseUrl` verbatim for every call incl. token. No production env exists in this SDK, so all test traffic stays on sandbox by construction. |

## 6. Assumptions & Blockers

- **Assumption**: `POST /api/orders` reuses the existing `Order` aggregate; buyer identity = JWT `ClaimTypes.Name`
  (email), matching how Web sets `Order.BuyerId`. Order placed directly from catalog item ids+quantities (no basket),
  prices from `CatalogItem.Price`. (Minor — proceed.)
- **Assumption**: `/pay` two-step (CreateOrder then, if needed, AuthorizeOrder) with card in the payment source is the
  direct-card hold flow; AuthorizeOrder `<remarks>` confirms "a valid payment_source must be provided in the request".
  Verified against live sandbox during self-test; fall back to single-step if the response already carries the auth.
- **Assumption**: In-memory DB per env note ⇒ create/pay/fulfil/refund exercised within one process run; no migration
  needed to run (added for SQL completeness only if the toolchain builds).
- **No blockers**: every capability the task needs maps to an SDK operation above. 3DS challenge
  (`OrderStatus.PayerActionRequired`) is surfaced as a STOP/report error, not an approval round-trip.
- **Verified against live sandbox** (all flows drive with test Visa `4111...`): `/pay` — `CreateOrder`
  (intent=AUTHORIZE, payment_source.card) returns the authorization inline (status COMPLETED) so the
  `AuthorizeOrder` fallback is not normally hit; capture returns fee+net; void releases the hold; partial
  refunds + caller idempotency-key dedup + over-refund guard all hold. Saved-card vault is the two-step flow
  above. **Empirical fix:** PayPal's sandbox returns 500 when `merchant_customer_id` contains `@`/`.` despite
  the documented regex allowing them, so it is sanitized to `[0-9A-Za-z_-]`. Vault-token DELETE is made
  idempotent (404-tolerant) and the app removes the card locally regardless, so the delete guarantee holds.
- SDK is not on NuGet ⇒ vendored from the pinned source as a project reference (`ManagePackageVersionsCentrally=false`
  on its csproj to coexist with repo CPM).
