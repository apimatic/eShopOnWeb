# PayPal Server SDK integration plan — eShopOnWeb PublicApi

Additive payment capability: PayPal card payments (authorize-at-checkout, capture-at-fulfilment,
void-on-cancel, refund-after-fulfilment), saved cards (vault), and a reconciliation report.
All surface is on `src/PublicApi` (JWT auth). SDK = PayPal Server SDK .NET (`PayPalServerSdk`,
spec 2.29), consumed as a vendored source project (not on NuGet — see §Build integration).

---

## 1. Scope & sequence

| Step | What | PayPal ops used |
| --- | --- | --- |
| S0 | Build integration: vendor SDK source into `src/PayPalServerSdk`, ProjectReference from Infrastructure | — |
| S1 | Config binding `PayPal:` (ClientId, ClientSecret, Environment, Currency, BaseUrl) + fail-fast | — |
| S2 | SDK client DI registration (singleton, IHttpClientFactory, OAuth2, base-URL override) | — |
| S3 | Domain: extend `Order` (status + `OrderPayment` + `OrderRefund`); new `SavedPaymentMethod` aggregate; EF config + DbSets | — |
| S4 | `IPayPalGateway` port (ApplicationCore) + `PayPalGateway` impl (Infrastructure) mapping SDK↔domain | all below |
| S5 | Place order `POST /api/orders` | — |
| S6 | Authorize `POST /api/orders/{id}/pay` (one-off card OR saved card) | `Orders.CreateOrder`, `Orders.AuthorizeOrder` |
| S7 | Fulfil `POST /api/orders/{id}/fulfil` (capture; reauthorize if stale) [admin] | `Payments.CaptureAuthorizedPayment`, `Payments.ReauthorizePayment`, `Payments.GetAuthorizedPayment` |
| S8 | Cancel `POST /api/orders/{id}/cancel` (void) [admin] | `Payments.VoidPayment` |
| S9 | Refund `POST /api/orders/{id}/refunds` (full/partial, idempotency key) | `Payments.RefundCapturedPayment` |
| S10 | `GET /api/my-orders` | — |
| S11 | Reconciliation `GET /api/reconciliation?from&to` [admin], all pages | `TransactionSearch.SearchTransactions` |
| S12 | Save card `POST /api/payment-methods`; list; delete | `Vault.CreateSetupToken`, `Vault.CreatePaymentToken`, `Vault.DeletePaymentToken` |
| S13 | Self-verify end-to-end against sandbox card | — |

---

## 2. CONTRACT SHEET

> ⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C#
> identifier — the cancellation-token parameter really is named `ct`, so named arguments write `ct:`.
> Nullable-no-default params (`payPalMockResponse`, `payPalRequestId`, `body`, …) **must be passed
> explicitly** (pass `null` to skip).
> ⚠ **Every SDK type is written fully-qualified** with the namespace its source path implies
> (`Models/` → `PayPalServerSdk.Models`; `Models/Enums/` → `PayPalServerSdk.Models.Enums`;
> `Errors/` → `PayPalServerSdk.Errors`; client/options → `PayPalServerSdk`;
> `ServerEnvironment` → `PayPalServerSdk.Servers`), taken from the path the map gives for THAT type.

### Operations

| Op | Signature (verbatim) | Request model → fields I set | Response fields I read | Error case + accessors | Source |
| --- | --- | --- | --- | --- | --- |
| `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderRequest`: `Intent`(req, enum), `PurchaseUnits`(req, 1 item), `PaymentSource`(card) | `Order`: `Id`, `Status`, `PurchaseUnits[].Payments.Authorizations[]` | A `SdkException<CreateOrderError>`: `TryGetError(out Error)`[400,401,422] · `TryGetRawError` | map/operations/Orders.md; Models/OrderRequest.cs |
| `client.Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", ...)` | body = `null` (no fields needed); pass `prefer:"return=representation"` | `OrderAuthorizeResponse`: `Id`,`Status`,`PurchaseUnits[].Payments.Authorizations[].{Id,Status,Amount,ExpirationTime}` | A `SdkException<AuthorizeOrderError>`: `TryGetError(out Error)`[400,401,403,404,422,500] · `TryGetRawError` | Orders.md; Models/OrderAuthorizeResponse.cs |
| `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", ...)` | `CaptureRequest`: `Amount`(Money), `FinalCapture=true`; pass `prefer:"return=representation"` | `CapturedPayment`: `Id`,`Status`,`Amount`,`SellerReceivableBreakdown.{GrossAmount,PaypalFee,NetAmount}` | A `SdkException<CaptureAuthorizedPaymentError>`: `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` | Payments.md; Models/CaptureRequest.cs, Models/CapturedPayment.cs |
| `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", ...)` | `ReauthorizeRequest`: `Amount`(Money) | `PaymentAuthorization`: `Id`,`Status`,`ExpirationTime`,`Amount` | A `SdkException<ReauthorizePaymentError>`: `TryGetError`[400,401,403,404,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; Models/ReauthorizeRequest.cs, Models/PaymentAuthorization.cs |
| `client.Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, ...)` | — | `PaymentAuthorization`: `Status`,`ExpirationTime` | A `SdkException<GetAuthorizedPaymentError>`: `TryGetError`[401,403,404] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; Models/PaymentAuthorization.cs |
| `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", ...)` | — (no body) | `PaymentAuthorization`: `Status` (may be 204/empty) | A `SdkException<VoidPaymentError>`: `TryGetError`[401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", ...)` | `RefundRequest`: `Amount`(Money, omit for full); `NoteToPayer`; pass `prefer:"return=representation"` | `Refund`: `Id`,`Status`,`Amount` | A `SdkException<RefundCapturedPaymentError>`: `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; Models/RefundRequest.cs, Models/Refund.cs |
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, ...)` | `startDate`,`endDate` (ISO-8601 w/ offset), `page`, `pageSize`, others `null` | `SearchResponse`: `TransactionDetails[].TransactionInfo.{TransactionId,TransactionAmount,FeeAmount,InvoiceId,CustomField,TransactionStatus,TransactionInitiationDate}`, `TotalPages`, `Page` | **B** `SdkException<RawError>`: `StatusCode`,`ReadAsString()`,`ReadAsJson<T>()` | TransactionSearch.md; Models/SearchResponse.cs, Models/TransactionInformation.cs |
| `client.Vault.CreateSetupToken` | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, ...)` | `SetupTokenRequest`: `PaymentSource`(req) = `{Card = SetupTokenRequestCard{Name,Number,Expiry,SecurityCode,BillingAddress?}}` | `SetupTokenResponse`: `Id`,`Status` | A `SdkException<CreateSetupTokenError>`: `TryGetError`[400,403,422,500] · `TryGetRawError` | Vault.md; Models/SetupTokenRequest.cs, Models/SetupTokenRequestPaymentSource.cs, Models/SetupTokenRequestCard.cs |
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, ...)` | `PaymentTokenRequest`: `PaymentSource`(req) = `{Token = VaultTokenRequest{Id=setupTokenId, Type=SetupToken}}`; `Customer?{Id}` | `PaymentTokenResponse`: `Id`(vault id), `Customer.Id`, `PaymentSource.Card.{LastDigits,Brand,Expiry,Name}` | A `SdkException<CreatePaymentTokenError>`: `TryGetError`[400,403,404,422,500] · `TryGetRawError` | Vault.md; Models/PaymentTokenRequest.cs, Models/PaymentTokenRequestPaymentSource.cs, Models/VaultTokenRequest.cs, Models/PaymentTokenResponse.cs, Models/CardPaymentTokenEntity.cs |
| `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, ...)` | — | `void` | A `SdkException<DeletePaymentTokenError>`: `TryGetError`[400,403,500] · `TryGetRawError` | Vault.md |

**To pay with a saved card:** `OrderRequest.PaymentSource = new PaymentSource { Card = new CardRequest { VaultId = <token id> } }` (Models/CardRequest.cs — `vault_id`).
**To pay one-off:** `PaymentSource { Card = new CardRequest { Number, Expiry, SecurityCode, Name, BillingAddress } }`.

### Money & nested types
- `Money` (Models/Money.cs): `CurrencyCode`(req,3), `Value`(req, string decimal). Amount to the cent = order total formatted per currency decimals.
- `AmountWithBreakdown` (Models/AmountWithBreakdown.cs): `CurrencyCode`(req), `Value`(req). Used in `PurchaseUnitRequest.Amount`(req).
- `PurchaseUnitRequest` (Models/PurchaseUnitRequest.cs): `Amount`(req); set `CustomId` and `InvoiceId` for reconciliation (`custom_id`, `invoice_id`).
- `SellerReceivableBreakdown`: `GrossAmount`(req Money), `PaypalFee`(Money?), `NetAmount`(Money?).

### Enums (Models/Enums/, `StringEnum<T>` — build via static member, not C# enum)
- `CheckoutPaymentIntent.Authorize` = `"AUTHORIZE"` (also `.Capture`).
- `OrderStatus`: `.Created .Saved .Approved .Voided .Completed .PayerActionRequired` (wire `PAYER_ACTION_REQUIRED` ⇒ STOP/report challenge).
- `AuthorizationStatus`: `.Created .Captured .Denied .PartiallyCaptured .Voided .Pending`.
- `CaptureStatus`: `.Completed .Declined .PartiallyRefunded .Pending .Refunded .Failed`.
- `RefundStatus`: `.Cancelled .Failed .Pending .Completed`.
- `VaultTokenRequestType.SetupToken` = `"SETUP_TOKEN"`.
- `CardBrand`: `.Visa .Mastercard .Discover .Amex …` (read-only, from vault response).

### Client / auth / server
- Client: `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` (only ctor). DI: `services.AddPayPalServerSdkClient(options => …)`.
- Options (PayPalServerSdkClientOptions.cs): `Oauth2 = new OAuth2ClientCredentials { ClientId, ClientSecret }` (namespace `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`); `Environment = ServerEnvironment.Sandbox`; base-URL override `Server.Default.Sandbox.BaseUrl`; `Retry`, `Logging`, `Hooks`.
- 1 server group `Default`; Sandbox base URL `https://api-m.sandbox.paypal.com`; OAuth token from `<base>/v1/oauth2/token`.
- `PayPal:BaseUrl` (optional): when set, use verbatim as base for **every** call incl. token — set `options.Server.Default.Sandbox.BaseUrl` to it.

---

## 3. Trap notes (name the hazard; resolve by loading the skill)

- **S2 client/DI:** the `HttpClient`/handler must be long-lived and reused (factory), not rebuilt per request; getting lifetime wrong leaks sockets. → **MUST load dotnet-client-initialization**.
- **S2 auth:** OAuth2 credentials must be set at registration/before client construction, and the options object is captured once — consequence for secret rotation. → **MUST load dotnet-authentication**.
- **S2/S11 config & base-URL/pagination/logging:** what `Timeout` actually bounds (per-attempt, not total) and which HTTP methods the SDK will resend on retry; how base-URL override composes; that list ops are not auto-paged; and that `LogRequestBody` writes JSON bodies unredacted. → **MUST load dotnet-configuration-resilience**.
- **S4 calls:** list/search ops must be called with named arguments (optional params have no C# default and mis-bind positionally); the injected `Idempotency-Key` header is **not** a real key — the real key is `payPalRequestId`. → **MUST load dotnet-calling-endpoints**.
- **S4 models:** enums are `StringEnum<T>` (static members / `FromValue`, not C# enums); `required` members must be set in the initializer; unknown response fields land in `AdditionalProperties`. → **MUST load dotnet-models**.
- **S4/S6–S12 errors:** Case A typed vs Case B raw differ; `TryGetNoContent` exists on Payments ops for 500; and the two JSON-exception directions below. → **MUST load dotnet-error-handling**.
- **S13 tests:** the `HttpClient` ctor arg is the test seam. → **MUST load dotnet-testing**.

---

## 4. REQUIRED READING (load all before implementation; sheet does NOT carry their contents)

| Skill (plugin-qualified) | Governs |
| --- | --- |
| `paypal-platforms-team:dotnet-client-initialization` | S2 client construction + DI |
| `paypal-platforms-team:dotnet-authentication` | S2 OAuth2 credentials |
| `paypal-platforms-team:dotnet-configuration-resilience` | S2 base-URL/retry/timeout/logging; S11 pagination |
| `paypal-platforms-team:dotnet-calling-endpoints` | S4 all operation calls, named args, idempotency key |
| `paypal-platforms-team:dotnet-models` | S4 building request models, enums, AdditionalProperties |
| `paypal-platforms-team:dotnet-error-handling` | S4/S6–S12 try/catch, Case A/B, JsonException traps |
| `paypal-platforms-team:dotnet-testing` | S13 integration tests |

**Two hazard rows that always apply (JsonException reaches the boundary from two directions, needing opposite handling):**
1. A drifted/malformed **2xx** body (a missing `required` member) surfaces as `System.Text.Json.JsonException` from deserialization — **not** an `SdkException` — so an SDK-exception-only catch ladder lets it escape.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so it **replaces** the `SdkException` and the HTTP status is destroyed with it.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` validated at startup (`ValidateOnStart`): `ClientId`, `ClientSecret`, `Environment`, `Currency` each non-blank (blank ≠ missing — every part checked); host refuses to start otherwise, so a missing secret is a boot failure, not a first-call 401. |
| 2 | Secret sourcing & rotation | Values from env vars → loaded into **.NET user-secrets** (never in repo). DI builds the options once at registration and captures it in the singleton client; a rotated secret takes effect only on process restart — acceptable for this reference app; documented. |
| 3 | Total timeout budget | SDK `Timeout` is per-attempt; retries multiply it. Each gateway call is bounded by a `CancellationTokenSource` deadline (configurable, default 30s) passed as `ct:` — that is the caller-visible budget. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS; our writes are all POST/DELETE → SDK never auto-resends them. So duplicate side-effects can only come from our own retries — none; each write is one call guarded by our idempotency (§5.5). |
| 5 | Idempotency & ambiguous writes | Real key = `payPalRequestId` (PayPal-Request-Id). Authorize: deterministic `create-{runId}-{orderId}` / `authorize-{runId}-{orderId}` + our order-status guard + per-order in-process lock. Capture: `capture-{runId}-{orderId}` + status guard. Void: `void-{runId}-{orderId}`. **Refund: caller-supplied idempotency key** → used verbatim as `payPalRequestId` AND stored on `OrderRefund`; repeat key returns the stored `refundId`; distinct keys allow distinct partial refunds, capped at captured amount. Reconciliation (search) is a read — no key. |
| 6 | Observability | Info: op + orderId + PayPal ids (order/auth/capture/refund) + status. Warning/Error: PayPal `Error.DebugId` (correlation id) + name + message into our logs. SDK `LogRequestBody` stays **off**. |
| 7 | Sensitive data | One-off card PAN/CVV flow through `CardRequest`/`SetupTokenRequestCard` — never persisted (only vault id + last4/brand/expiry stored) and never logged: SDK `Logging.LogRequestBody` off, `Logging.LoggerFactory` **set explicitly** to the app's `ILoggerFactory` at client construction (which disables the `PAYPALSERVERSDKCLIENT_LOG` env-var override that could otherwise force body logging on), and our own code never echoes request bodies. |
| 8 | Environment selection | One server group `Default`; SDK declares only `ServerEnvironment.Sandbox`. `PayPal:Environment` selects Sandbox; any non-sandbox deployment must set `PayPal:BaseUrl` explicitly (used verbatim for every call incl. token). All dev/test traffic → sandbox. |

---

## 6. Assumptions & Blockers

- **Build integration (assumption):** SDK is not on NuGet, so its source is vendored into
  `src/PayPalServerSdk/` and referenced as a project (with `ManagePackageVersionsCentrally=false`
  in that csproj, since the repo uses central package management and the SDK carries versioned
  `PackageReference`s). This is the "build reference," distinct from the temp read-only map clone.
- **Order shipping address (assumption):** `Order` requires a `ShipToAddress`; `POST /api/orders`
  accepts an optional address and defaults a placeholder when omitted (payment flow is the focus).
- **Amount decimal formatting (`YOUR CALL — not in the map`):** format the order total to the
  currency's minor-unit precision (2 for most incl. USD; 0 for zero-decimal currencies like JPY)
  so the held amount equals the order total to the cent.
- **Reconciliation matching (`YOUR CALL — not in the map`):** set purchase-unit `custom_id` =
  `{runId}:{orderId}` and `invoice_id` = `{runId}-{orderId}` (runId = process GUID → invoice
  uniqueness across runs); match PayPal `TransactionInfo.CustomField`/`InvoiceId` back to orders.
- **Reconciliation reporting lag (verified against sandbox):** PayPal's transaction search returns
  **404 `INVALID_REQUEST` "Data for the given start date is not available"** when the window's start
  date is too recent (reporting lags live activity). Per the task, that is an expected empty range, not
  a gap — the gateway treats that specific 404 as an empty window rather than an error, so a recent range
  returns `rangeEmpty: true` while an older, settled range returns data. The search is windowed into
  ≤30-day windows and fully paged within each.
- **Refund idempotency & PayPal-Request-Id uniqueness (verified):** the caller's idempotency key dedups
  in our own store (a repeat returns the stored refund without calling PayPal). The value sent to PayPal
  as `PayPal-Request-Id` is run-scoped (`refund-{runId}-{orderId}-{callerKey}`) because that header is
  unique *merchant-wide* — a bare caller key can collide with historical usage on a shared sandbox account
  and be rejected as `DUPLICATE_REQUEST_ID`. Authorize/capture/void keys are likewise run-scoped.
- **Stale-auth path (`UNVERIFIED` — only live 30-day expiry could exercise it):** capture catches
  the expired-authorization error, calls `ReauthorizePayment`, then re-captures; if reauthorize
  fails (past the 29-day window) it returns an operator-actionable error. Defensive coding; not
  reproducible within a single run.
- **No blockers.** Every required capability is covered by the ops in §2. Direct card auth/capture/
  refund/void, vault via setup→payment token, and transaction search are all present. If a card
  payment returns a 3DS/`PAYER_ACTION_REQUIRED` challenge, the code STOPS and surfaces it (no
  browser round-trip is built) — per task mandate.
