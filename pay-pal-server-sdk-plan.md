# PayPal Server SDK integration plan — eShopOnWeb

Additive PayPal card payments + saved cards on `src/PublicApi`. Reuses the existing
`Order`/`OrderItem` aggregate; adds `OrderPayment`, `PaymentRefund`, `SavedCard` aggregates.
PayPal is reached **only** through the vendored PayPal Server SDK (`PayPalServerSdk`).

## 1. Scope & sequence

| # | Step | PayPal operations used |
| --- | --- | --- |
| 1 | Vendor SDK into `src/PayPalServerSdk`; reference from Infrastructure. CPM off in that project. | — |
| 2 | Domain: `OrderPayment` (+`PaymentRefund`), `SavedCard` aggregates; EF configs + DbContext. | — |
| 3 | `PayPalOptions` bound from `PayPal:` section; fail-fast; SDK client DI; `PayPalGateway` wrapper + error boundary. | client init, auth |
| 4 | Place order (local only, reuse Order aggregate). | — |
| 5 | Pay = authorize hold. | `Orders.CreateOrder` (intent AUTHORIZE, card **or** vault_id) → `Orders.AuthorizeOrder` |
| 6 | Fulfil = capture; renew stale auth. | `Payments.ReauthorizePayment` (only if stale) → `Payments.CaptureAuthorizedPayment` |
| 7 | Cancel before fulfil = release hold. | `Payments.VoidPayment` |
| 8 | Refund after fulfil (full/partial, idempotency key). | `Payments.RefundCapturedPayment` |
| 9 | Save card / list / delete. | `Vault.CreatePaymentToken` (raw card) / local list / `Vault.DeletePaymentToken` |
| 10 | Reconciliation over full date range. | `TransactionSearch.SearchTransactions` (loop all pages) |
| 11 | Unknown-outcome re-reads. | `Orders.GetOrder`, `Payments.GetAuthorizedPayment`, `Payments.GetRefund` |

## 2. CONTRACT SHEET

> ⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C#
> identifier; in named arguments use exactly those names (the cancellation-token parameter is `ct`).
> ⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**, taken
> from the path the map gives for THAT type (records → `PayPalServerSdk.Models`, enums →
> `PayPalServerSdk.Models.Enums`, errors → `PayPalServerSdk.Errors`, client/options →
> `PayPalServerSdk`, server env → `PayPalServerSdk.Servers`, oauth creds →
> `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`, `SdkException<T>` →
> `PayPalServerSdk.Core.Exceptions`, `RawError` → `PayPalServerSdk.Core.ErrorResponse`).

Every op below authenticates with `options.Oauth2`. All are **throw-only** (no `…Result`
sibling), no pagination unless noted, server group `Default`.

| Op | Signature (verbatim) | Request model + fields used | Response fields read | Error case + accessors | source |
| --- | --- | --- | --- | --- | --- |
| `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderRequest{ Intent (intent): CheckoutPaymentIntent **required** = Authorize; PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> **required** [1..10]; PaymentSource (payment_source): PaymentSource? }`. `PurchaseUnitRequest{ Amount (amount): AmountWithBreakdown **required**; ReferenceId?; CustomId (custom_id): string? — purpose: eShop orderId for reconcile; InvoiceId (invoice_id): string? — purpose: unique reconcile key; Description? }`. `AmountWithBreakdown{ CurrencyCode (currency_code) **required**; Value (value) **required** }`. `PaymentSource{ Card (card): CardRequest? }`. `CardRequest{ Number (number); Expiry (expiry) YYYY-MM; SecurityCode (security_code); Name (name); BillingAddress (billing_address): Address?; VaultId (vault_id): string? — purpose: pay with saved card }` | `Order.Id` (paypal order id), `Order.Status` (OrderStatus) | Case A `SdkException<CreateOrderError>`: `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` | Orders.md; Models/OrderRequest.cs, PurchaseUnitRequest.cs, AmountWithBreakdown.cs, PaymentSource.cs, CardRequest.cs, Order.cs |
| `client.Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | body = `null` (card already on the order from CreateOrder) | `OrderAuthorizeResponse.Status` (OrderStatus); `.PurchaseUnits[0].Payments.Authorizations[0]{ Id, Status (AuthorizationStatus), ExpirationTime, CreateTime, Amount }` | Case A `SdkException<AuthorizeOrderError>`: `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` | Orders.md; OrderAuthorizeResponse.cs, PurchaseUnit.cs, PaymentCollection.cs, AuthorizationWithAdditionalData.cs |
| `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | body = `null` (full final capture) | `CapturedPayment{ Id, Status (CaptureStatus), Amount (Money), SellerReceivableBreakdown{ GrossAmount, PaypalFee, NetAmount }, CreateTime }` | Case A `SdkException<CaptureAuthorizedPaymentError>`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | Payments.md; CapturedPayment.cs, SellerReceivableBreakdown.cs, Money.cs |
| `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | body = `null` (reauthorize full remaining) | `PaymentAuthorization{ Id, Status, ExpirationTime, Amount }` | Case A `SdkException<ReauthorizePaymentError>`: `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent` [500] · `TryGetRawError` | Payments.md; ReauthorizeRequest.cs, PaymentAuthorization.cs |
| `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentAuthorization.Status` (VOIDED) | Case A `SdkException<VoidPaymentError>`: `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent` [500] · `TryGetRawError` | Payments.md; PaymentAuthorization.cs |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `RefundRequest{ Amount (amount): Money? — purpose: partial refund; omit → full refund; CustomId?; InvoiceId?; NoteToPayer? }` | `Refund{ Id, Status (RefundStatus), Amount (Money) }` | Case A `SdkException<RefundCapturedPaymentError>`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent` [500] · `TryGetRawError` | Payments.md; RefundRequest.cs, Refund.cs, Money.cs |
| `client.Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentAuthorization.Status` | Case A `SdkException<GetAuthorizedPaymentError>`: `TryGetError` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` | Payments.md |
| `client.Payments.GetRefund` | `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `Refund.Status`, `Refund.Amount` | Case A `SdkException<GetRefundError>` | Payments.md |
| `client.Orders.GetOrder` | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `Order.Status`, purchase-unit payments | Case A `SdkException<GetOrderError>`: `TryGetError` [401,404] · `TryGetRawError` | Orders.md |
| `client.Vault.CreateSetupToken` | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `SetupTokenRequest{ Customer (customer): set MerchantCustomerId (merchant_customer_id)=sanitized buyerId; PaymentSource: SetupTokenRequestPaymentSource **required** }`. `SetupTokenRequestPaymentSource{ Card: SetupTokenRequestCard? }`. `SetupTokenRequestCard{ Number; Expiry; SecurityCode; Name; BillingAddress }` | `SetupTokenResponse{ Id (setup token) }` | Case A `SdkException<CreateSetupTokenError>`: `TryGetError` [400,403,422,500] · `TryGetRawError` | Vault.md; SetupTokenRequest.cs, SetupTokenRequestPaymentSource.cs, SetupTokenRequestCard.cs, SetupTokenResponse.cs |
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenRequest{ PaymentSource (payment_source): PaymentTokenRequestPaymentSource **required** }`. Exchange the setup token: `PaymentTokenRequestPaymentSource{ Token (token): VaultTokenRequest{ Id = setupTokenId, Type = SetupToken } }` | `PaymentTokenResponse{ Id (vault token), Customer{ Id }, PaymentSource.Card: CardPaymentTokenEntity{ LastDigits (last_digits), Brand, Expiry } }` | Case A `SdkException<CreatePaymentTokenError>`: `TryGetError` [400,403,404,422,500] · `TryGetRawError` | Vault.md; PaymentTokenRequest.cs, PaymentTokenRequestPaymentSource.cs, VaultTokenRequest.cs, PaymentTokenResponse.cs, CardPaymentTokenEntity.cs |
| `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | void | Case A `SdkException<DeletePaymentTokenError>`: `TryGetError` [400,403,500] · `TryGetRawError` | Vault.md |
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` | 8 nullable middle params passed `null`; `startDate`/`endDate` ISO-8601; loop `page` 1..`TotalPages` | `SearchResponse{ TransactionDetails[].TransactionInfo{ TransactionId, InvoiceId (invoice_id), CustomField (custom_field), TransactionAmount (Money), FeeAmount, TransactionInitiationDate, TransactionStatus }, Page, TotalPages, TotalItems }` | **Case B** `SdkException<RawError>` (`StatusCode`, `ReadAsString()`) | TransactionSearch.md; SearchResponse.cs, TransactionDetails.cs, TransactionInformation.cs |

Enums (source `Models/Enums/`): `CheckoutPaymentIntent.Authorize`="AUTHORIZE"; `VaultTokenRequestType.SetupToken`="SETUP_TOKEN" (unused — vaulting raw card directly); `AuthorizationStatus` {Created,Captured,Denied,PartiallyCaptured,Voided,Pending}; `CaptureStatus` {Completed,Declined,PartiallyRefunded,Pending,Refunded,Failed}. Read wire value via `.Value` (StringEnum). Build via static member or `.FromValue("…")`.

**Client construction / auth / server:** `services.AddPayPalServerSdkClient(o => { o.Oauth2 = new OAuth2ClientCredentials{ ClientId=…, ClientSecret=… }; o.Environment = ServerEnvironment.Sandbox; if(baseUrl set) o.Server.Default.Sandbox.BaseUrl = baseUrl; })` (source `ServiceCollectionExtensions.cs`, `PayPalServerSdkClientOptions.cs`, `Servers/DefaultOptions.cs`). Only `ServerEnvironment.Sandbox` exists; `PayPal:BaseUrl` override sets `Server.Default.Sandbox.BaseUrl`, which the OAuth token fetch and every op resolve through (map *Servers & auth*).

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A `vault_id` used to pay must be a token this shopper saved and still owns | `Orders.CreateOrder` ← `Vault.CreatePaymentToken` (recorded as `SavedCard` rows scoped by BuyerId) | implementation (SavedCard repo lookup by id **and** BuyerId before building `CardRequest.VaultId`) |
| A `captureId` refunded must be the capture produced by this order's fulfilment | `Payments.RefundCapturedPayment` ← `Payments.CaptureAuthorizedPayment` (stored `OrderPayment.CaptureId`) | implementation (refund reads captureId from the caller's own `OrderPayment`) |
| An `authorizationId` captured/voided/reauthorized must be this order's authorization | `Payments.{Capture,Void,Reauthorize}` ← `Orders.AuthorizeOrder` (stored `OrderPayment.AuthorizationId`) | implementation |
| Sum of refunds must never exceed captured amount | `Payments.RefundCapturedPayment` ← `Payments.CaptureAuthorizedPayment` (stored `CapturedAmount`) | implementation (reject if `RefundedAmount + requested > CapturedAmount`) |

## 3. Trap notes

- Client/DI: SDK `HttpClient` must be long-lived via `IHttpClientFactory`; the built-in DI extension already does `AddHttpClient()` + singleton — do not new-up per request. **MUST load paypal-platforms-team:dotnet-client-initialization**.
- Auth: credentials set in the DI callback are captured once at registration (rotation needs restart); a never-set credential is skipped, surfacing as a later 401 not a construction error. **MUST load paypal-platforms-team:dotnet-authentication**.
- Calling ops: list/search ops have many optional params with no C# default — bind by name; the injected `Idempotency-Key` header is NOT a real key, the `payPalRequestId` param is. **MUST load paypal-platforms-team:dotnet-calling-endpoints**.
- Models: enums are `StringEnum<T>` not C# enums; `required` initializer members; unknown response fields land in `AdditionalProperties`. **MUST load paypal-platforms-team:dotnet-models**.
- Error boundary: Case A typed vs Case B raw differ per op (SearchTransactions is the only Case B); `TryGetRawError` is the fallback, and a drifted 2xx or a non-matching error body throws `JsonException` not `SdkException`. **MUST load paypal-platforms-team:dotnet-error-handling**.
- Config/resilience: `Timeout` is per-attempt not total; `POST`/`PATCH`/`DELETE` are never auto-retried by the SDK while `PUT`/`GET` are; `LogRequestBody` logs JSON unredacted and `PAYPALSERVERSDKCLIENT_LOG` can arm it unless `LoggerFactory` is set. **MUST load paypal-platforms-team:dotnet-configuration-resilience**.
- Testing: the `HttpClient` ctor arg is the fake seam. **MUST load paypal-platforms-team:dotnet-testing**.

## 4. REQUIRED READING (load all before implementing)

- paypal-platforms-team:dotnet-client-initialization · client + DI wiring (step 3)
- paypal-platforms-team:dotnet-authentication · OAuth2 credentials (step 3)
- paypal-platforms-team:dotnet-calling-endpoints · every SDK op call (steps 5–10)
- paypal-platforms-team:dotnet-models · request/response models + enums (steps 5–10)
- paypal-platforms-team:dotnet-error-handling · error boundary (all steps) — **always required**
- paypal-platforms-team:dotnet-configuration-resilience · timeout budget, retry eligibility, logging/redaction (step 3)
- paypal-platforms-team:dotnet-testing · integration seam (tests)

Mandatory hazard rows (both, verbatim): (a) a drifted/malformed **2xx** body (missing `required`
member) surfaces as `System.Text.Json.JsonException` from deserialization, **not** `SdkException` —
an SDK-exception-only catch ladder lets it escape; (b) a **non-2xx** body that does not match its
operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is
being constructed*, so it **replaces** the `SdkException` and the HTTP status is lost with it.

The sheet deliberately does not carry these skills' contents; load them.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions.Validate()` runs at startup in `PublicApi/Program.cs` (via `AddPayPalIntegration`): throws if `ClientId`, `ClientSecret`, or `Currency` is null/whitespace — **each** part checked (blank ≠ missing). `Environment` defaults to `sandbox`; `BaseUrl` optional. Host refuses to start rather than 401 later. |
| 2 | Secret sourcing & rotation | Secrets from .NET user-secrets (loaded from env by the operator, values never in repo). DI builds the options object once at registration and captures it in the SDK singleton → rotation needs a process restart. Documented; acceptable for this app. |
| 3 | Total timeout budget | SDK `Timeout` is per-attempt. Each service call passes a `CancellationToken` from a per-request `CancellationTokenSource` (30s) as `ct:` — that deadline bounds the whole call incl. retries. Enforced in `PayPalGateway`. |
| 4 | Write-retry ownership | All PayPal writes here are `POST` (`CreateOrder`, `AuthorizeOrder`, `Capture`, `Reauthorize`, `Void`, `Refund`, `CreatePaymentToken`) or `DELETE` (`DeletePaymentToken`) → SDK never auto-resends them. Safe to make PayPal-Request-Id idempotent explicitly. |
| 5 | Idempotency & ambiguous writes | Real key = `payPalRequestId` (PayPal-Request-Id). Derived from the **persisted per-attempt invoiceId** (`ESHOP-{orderId}-{guid}`): `{invoiceId}-create/-auth/-capture/-recapture/-reauth/-void`, and `{invoiceId}-refund-{callerKey}` for refunds. This is stable within one payment attempt (invoiceId is stored on the row, so a concurrent double-click reuses the same key → PayPal dedups) yet unique across attempts/process restarts — deliberately **not** `pay:{orderId}`, because the in-memory DB reuses order id 1 every run and a static-per-order key collides at PayPal (observed: `TRANSACTION_REFUSED`). Local claims (row 9) back this; refund also carries the caller's own key. Verified: `OrderPaymentService.PayAsync`/`RefundAsync`/`CaptureWithRenewalAsync`/`CancelAsync`. |
| 6 | Observability | `PayPalGateway` logs op name + PayPal `debug_id` (correlation id from `Error.DebugId`) + status at Warning/Error on failure, Information on success. `LogRequestBody` left off (row 7). |
| 7 | Sensitive data | Request models carry raw PAN/CVV (`CardRequest`, `PaymentTokenRequestCard`). Therefore `options.Logging.LogRequestBody` stays **off** and `options.Logging.LoggerFactory` is set explicitly (DI extension assigns it) so `PAYPALSERVERSDKCLIENT_LOG` cannot arm body logging. App never logs card fields, never persists PAN/CVV — only brand/last4/expiry descriptors. |
| 8 | Environment selection | One server group `Default`, one env `Sandbox`. All dev/test → Sandbox base `https://api-m.sandbox.paypal.com`. `PayPal:BaseUrl` override redirects every call incl. token. No Live env member exists in the SDK, so live traffic would require an explicit `BaseUrl` — test stays on sandbox by default. |
| 9 | Duplicate prevention under concurrency | `OrderPayment.OrderId` has a **unique index** (`IX_OrderPayment_OrderId`); the second concurrent pay insert throws `DbUpdateException`, caught in `OrderPaymentService.PayAsync` → treated as idempotent replay. Refunds: unique index on `PaymentRefund(OrderPaymentId, IdempotencyKey)`; duplicate key → caught → return existing refund. (SQL Server enforces; the in-memory provider used for local runs does not enforce indexes — a documented environment limitation of the test DB, not of the design.) |
| 10 | Partial results | Reconciliation loops `page` 1..`TotalPages` from `SearchResponse.TotalPages` so it covers the whole range. The report DTO carries `pagesFetched`/`totalItems`; if a hard page cap is ever hit it is surfaced as `truncated=true` on the response, not just logged. |
| 11 | Startup validation vs test host | `tests/PublicApi.FunctionalTests` boots the host via `WebApplicationFactory`. It gets placeholder `PayPal:*` config through `appsettings.test.json` (non-blank dummy values) so the fail-fast passes; verified by running that project green. PayPal calls in tests are not exercised (no secrets in CI) — functional tests cover the existing surface + auth-guard on new endpoints. |
| 12 | Ordering & no-op side effects | Local `OrderPayment` row written (claim) **before** the PayPal CreateOrder/Authorize calls; PayPal ids/status written after. Capture/void/refund update the pre-existing row after the provider call. Idempotent transitions gate outbound effects: if `OrderPayment` is already Captured, fulfil returns the stored result **without** re-calling capture; already Voided cancel is a no-op. |
| 13 | Unknown outcomes | On transport failure after the request may have landed: pay re-reads `Orders.GetOrder(payPalOrderId)` (if id known) to see if an authorization exists; fulfil re-reads `Payments.GetAuthorizedPayment(authorizationId)`; refund re-reads `Payments.GetRefund` — but primarily the deterministic `payPalRequestId` makes the retry itself idempotent. Never return a definite failure without the re-read where an id exists. |
| 14 | Provider status & reconciliation clock | Every op's status is branched on: AuthorizeOrder → authorization `Status` must be `Created` (else Denied/Pending → surfaced, not assumed complete); Capture `Status` must be `Completed` (Pending/Declined/Failed surfaced); CreateOrder order `Status` `PAYER_ACTION_REQUIRED`/approval link → **STOP & report a challenge** (task rule), no browser round-trip built. No `?? "COMPLETED"`. Reconciliation: both sides filter on the **PayPal transaction time** — PayPal side via `SearchTransactions` `startDate`/`endDate` on `transaction_initiation_date`; local side on the stored `OrderPayment.PayPalTransactionTime` (PayPal-reported capture/auth `create_time`), **not** the DB row-creation column. |

## 6. Assumptions & Blockers

- **Assumption (minor):** BuyerId = the JWT `Name` claim (username/email), matching how the Web
  checkout sets `Order.BuyerId`. The token carries identity; no separate id needed.
- **RESOLVED at runtime:** direct raw-card vaulting via `Vault.CreatePaymentToken` returned `500`
  from the sandbox, so the integration uses the canonical **`CreateSetupToken` → `CreatePaymentToken`
  (SETUP_TOKEN)** flow (the fallback anticipated here). Both are SDK operations, so this was a runtime
  pivot, not a gap. Verified working (Visa test card vaulted, last4 `1111`).
- **RESOLVED at runtime:** `merchant_customer_id`'s documented regex permits `@`/`.`, but the
  sandbox `500`s when they are present; `PayPalGateway.SanitizeCustomerId` restricts it to
  `[0-9a-zA-Z_-]` (stable per shopper). `VoidPayment` returns `204 No Content`, so `VoidAsync`
  treats the SDK's empty-body `JsonException` as success (a real failure is an `SdkException`).
- **Assumption (minor):** Amounts formatted to 2 decimals (`F2`, InvariantCulture) — correct for the
  sandbox currency (USD/EUR-class). Zero-decimal currencies (JPY) not in scope for this task.
- **No blockers.** Every capability the task requires maps to an SDK operation above.

## 7. Source labels — every contract row cites a map page or map-named file (see the `source` column of §2). Application decisions (persistence, claim columns, buyerId, reconcile key) are `YOUR CALL — not in the map` and decided here against the task.
