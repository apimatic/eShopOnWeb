# PayPal Server SDK — integration plan (eShopOnWeb PublicApi)

Grounded from the PayPal Server SDK .NET SDK map (`sdk-map.md` + `map/operations/*`) and the
map-named source files, plus the `dotnet-*` companion skills. Every SDK fact below cites its map
page or declaring source file. `PayPalServerSdk` is the root namespace; the SDK is **not on NuGet**
and is vendored into the repo as `src/PayPalServerSdk/` (a `netstandard2.0` project, central package
management disabled for it) and referenced by `src/PublicApi`.

The application design (entities, persistence, endpoint routes, shopper scoping) is decided here as
`YOUR CALL` and is **not** an SDK fact.

---

## 1. Scope & sequence

Two additive flows exposed on `src/PublicApi` (JWT, MinimalApi.Endpoint `IEndpoint<>` convention).
The eShop `Order`/`OrderItem` aggregate is reused unchanged; a new `OrderPayment` aggregate carries
all PayPal-owned state + lifecycle, and a new `SavedPaymentMethod` aggregate carries vaulted cards.

PayPal money model = **authorization intent**: create order (intent=AUTHORIZE) → authorize (hold) →
capture (take) → refund (give back); void releases a hold before capture.

| Step | Endpoint | PayPal operations used |
| --- | --- | --- |
| Place order (awaiting payment) | `POST /api/orders` | *(none — eShop `Order` + `OrderPayment` row only)* |
| Authorize (hold) | `POST /api/orders/{orderId}/pay` | `Orders.CreateOrder` (intent=AUTHORIZE, card **or** saved-card vault_id) → `Orders.AuthorizeOrder` |
| Fulfil (capture) | `POST /api/orders/{orderId}/fulfil` | `Payments.CaptureAuthorizedPayment`; on stale hold `Payments.ReauthorizePayment` then re-capture |
| Cancel (release) | `POST /api/orders/{orderId}/cancel` | `Payments.VoidPayment` |
| Refund (full/partial) | `POST /api/orders/{orderId}/refunds` | `Payments.RefundCapturedPayment` |
| My orders | `GET /api/my-orders` | *(none — local read)* |
| Reconciliation | `GET /api/reconciliation?from&to` | `TransactionSearch.SearchTransactions` (all pages) |
| Save card | `POST /api/payment-methods` | `Vault.CreateSetupToken` (card) → `Vault.CreatePaymentToken` (token=SETUP_TOKEN) |
| List saved cards | `GET /api/payment-methods` | *(none — local read; ownership enforced in DB)* |
| Delete saved card | `DELETE /api/payment-methods/{id}` | `Vault.DeletePaymentToken` |

Operator-only (role `Administrators`): fulfil, cancel, reconciliation. All others shopper-scoped to
`ClaimTypes.Name` (= eShop `BuyerId`).

---

## 2. CONTRACT SHEET

⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C#
identifier — the cancellation-token parameter really is named `ct`, so named arguments write `ct:`.
The 5-nullable-no-default params on `CreateOrder`/`AuthorizeOrder`/`CaptureAuthorizedPayment`/etc.
**must be passed explicitly** (pass `null` to skip).

⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**, taken
from the path the map gives for THAT type: records `Models/` → `PayPalServerSdk.Models`; enums
`Models/Enums/` → `PayPalServerSdk.Models.Enums`; typed errors `Errors/` → `PayPalServerSdk.Errors`;
client/options root → `PayPalServerSdk`; `ServerEnvironment` → `PayPalServerSdk.Servers`;
`SdkException<T>` → `PayPalServerSdk.Core.Exceptions`; `RawError`/`ApiError` →
`PayPalServerSdk.Core.ErrorResponse`; `RetryOptions`/`LoggingOptions` →
`PayPalServerSdk.Core.Configuration`.

### Operations (controller · signature · request→response · error case · source)

| Op | Signature (verbatim) | Reads on response | Error case | Source |
| --- | --- | --- | --- | --- |
| `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? = null, CancellationToken ct = default)` | `Order.Id`, `Order.Status` | A `CreateOrderError` · `TryGetError(out Error)`[400,401,422] · `TryGetRawError`[fallback] | map/operations/Orders.md |
| `client.Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? = null, CancellationToken ct = default)` | `OrderAuthorizeResponse.Status`, `.PurchaseUnits[0].Payments.Authorizations[0].{Id,Status,ExpirationTime,Amount}` | A `AuthorizeOrderError` · `TryGetError`[400,401,403,404,422,500] · `TryGetRawError` | Orders.md |
| `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? = null, CancellationToken ct = default)` | `CapturedPayment.{Id,Status,Amount,SellerReceivableBreakdown.{GrossAmount,PaypalFee,NetAmount}}` | A `CaptureAuthorizedPaymentError` · `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` | map/operations/Payments.md |
| `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? = null, CancellationToken ct = default)` | `PaymentAuthorization.{Id,Status,ExpirationTime}` | A `ReauthorizePaymentError` · `TryGetError`[400,401,403,404,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md |
| `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? = null, CancellationToken ct = default)` | `PaymentAuthorization.Status` (VOIDED) | A `VoidPaymentError` · `TryGetError`[401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? = null, CancellationToken ct = default)` | `Refund.{Id,Status,Amount}` | A `RefundCapturedPaymentError` · `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md |
| `client.Vault.CreateSetupToken` | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? = null, CancellationToken ct = default)` | `SetupTokenResponse.{Id,Customer.Id}` | A `CreateSetupTokenError` · `TryGetError`[400,403,422,500] · `TryGetRawError` | map/operations/Vault.md |
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? = null, CancellationToken ct = default)` | `PaymentTokenResponse.{Id,Customer.Id,PaymentSource.Card.{LastDigits,Brand,Expiry,Name}}` | A `CreatePaymentTokenError` · `TryGetError`[400,403,404,422,500] · `TryGetRawError` | Vault.md |
| `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? = null, CancellationToken ct = default)` | *(void)* | A `DeletePaymentTokenError` · `TryGetError`[400,403,500] · `TryGetRawError` | Vault.md |
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? = null, CancellationToken ct = default)` | `SearchResponse.{TotalPages,Page,TransactionDetails[].TransactionInfo.{TransactionId,InvoiceId,CustomField,TransactionAmount,FeeAmount,TransactionStatus,TransactionInitiationDate,TransactionEventCode}}` | **B** `SdkException<RawError>` | map/operations/TransactionSearch.md |

Wire dates for search are ISO-8601 `startDate`/`endDate` (`start_date`/`end_date`). `SearchResponse`
carries `total_pages`/`page` → **paginate manually** by incrementing `page` until `page >= total_pages`
(covers the whole range, not one page). Source: `Models/SearchResponse.cs`.

### Request models to build (fields I set · wire name · source)

- `OrderRequest` (`Models/OrderRequest.cs`): `Intent` (**required**, `CheckoutPaymentIntent.Authorize`),
  `PurchaseUnits` (**required**, 1 item), `PaymentSource` (card or saved-card).
- `PurchaseUnitRequest` (`Models/PurchaseUnitRequest.cs`): `Amount` (**required** `AmountWithBreakdown`),
  `CustomId` = eShop order reference (surfaces in transaction search `custom_field` for reconciliation),
  `Description`. **`InvoiceId` = `eshop-{orderRef}-{GUID}` (globally unique)** — VERIFIED: the merchant
  account enforces invoice_id uniqueness and duplicate-transaction detection, so a small integer order
  id (which also resets with the in-memory store) triggers `DUPLICATE_INVOICE_ID`. A unique invoice_id
  avoids it; reconciliation matches on `custom_id`, not `invoice_id`.
- `AmountWithBreakdown` (`Models/AmountWithBreakdown.cs`): `CurrencyCode` (**required**, from
  `PayPal:Currency`), `Value` (**required**, `Order.Total()` as `F2` invariant string — equals order
  total to the cent).
- `PaymentSource` (`Models/PaymentSource.cs`): `Card` = `CardRequest`.
- `CardRequest` (`Models/CardRequest.cs`): one-off → `Number,Expiry(YYYY-MM),SecurityCode,Name,BillingAddress`;
  saved-card → `VaultId` (only). `BillingAddress` = `Address` (**required** `CountryCode`; `Models/Address.cs`).
- `CaptureRequest` (`Models/CaptureRequest.cs`): `Amount` (omit → full capture), `FinalCapture=true`.
- `RefundRequest` (`Models/RefundRequest.cs`): `Amount` (omit → full), `CustomId`, `NoteToPayer`.
- `ReauthorizeRequest` (`Models/ReauthorizeRequest.cs`): `Amount` (order total).
- `SetupTokenRequest` (`Models/SetupTokenRequest.cs`): `Customer` (`Customer.Id` if returning shopper),
  `PaymentSource` (**required** `SetupTokenRequestPaymentSource`).
- `SetupTokenRequestPaymentSource` (`Models/SetupTokenRequestPaymentSource.cs`): `Card` =
  `SetupTokenRequestCard` (`Models/SetupTokenRequestCard.cs`: `Number,Expiry,SecurityCode,Name,BillingAddress`).
- `PaymentTokenRequest` (`Models/PaymentTokenRequest.cs`): `Customer`, `PaymentSource` (**required**).
- `PaymentTokenRequestPaymentSource` (`Models/PaymentTokenRequestPaymentSource.cs`): `Token` =
  `VaultTokenRequest` (`Models/VaultTokenRequest.cs`: **required** `Id`=setup-token id, **required**
  `Type`=`VaultTokenRequestType.SetupToken`).

### Enums (member → wire; source)

- `CheckoutPaymentIntent.Authorize`="AUTHORIZE" (`Models/Enums/CheckoutPaymentIntent.cs`).
- `OrderStatus`: Created/Completed/Voided/**PayerActionRequired**="PAYER_ACTION_REQUIRED" (`Models/Enums/OrderStatus.cs`).
- `AuthorizationStatus`: Created/Captured/Denied/PartiallyCaptured/Voided/Pending (`Models/Enums/AuthorizationStatus.cs`).
- `CaptureStatus`: Completed/Declined/PartiallyRefunded/Pending/Refunded/Failed (`Models/Enums/CaptureStatus.cs`).
- `RefundStatus`: Cancelled/Failed/Pending/Completed (`Models/Enums/RefundStatus.cs`).
- `VaultTokenRequestType.SetupToken`="SETUP_TOKEN" (`Models/Enums/VaultTokenRequestType.cs`).
- Read wire value with `.Value` (NOT `ToString()`); enums are `StringEnum<T>`, compared by value.

### Typed error body (`Models/Error.cs`)

`Error`: **required** `Name`, `Message`, `DebugId` (PayPal correlation id), optional `Details[]`. This
is the `out` type of `TryGetError` on every Case-A `{Operation}Error` in scope.

### Client construction / auth / server (source: `sdk-map.md`, `AuthSchemes.cs`, `ServiceCollectionExtensions.cs`)

- Client: `new PayPalServerSdkClient(HttpClient, PayPalServerSdkClientOptions)`; DI via
  `services.AddPayPalServerSdkClient(options => …)` (registers **singleton**, fills
  `options.Logging.LoggerFactory` from the container).
- Auth: `options.Oauth2 = new OAuth2ClientCredentials { ClientId=…, ClientSecret=… }`
  (`Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`). Single scheme → no
  `AuthSchemeException` composition to worry about; an **unset** credential sends no auth and surfaces
  as a provider 401 (→ fail-fast, §5.1).
- Environment: only `ServerEnvironment.Sandbox` exists (`sdk-map.md` Servers & auth). Base URL +
  token URL both resolve from `server.Default("/v1/oauth2/token")` (verified in `AuthSchemes.cs`), so
  **setting `options.Server.Default.Sandbox.BaseUrl` redirects every call including the token
  request** — this is how `PayPal:BaseUrl` is honored verbatim.

---

## 3. Trap notes (hazard + consequence; skill named, not resolved)

- **Client/HttpClient lifetime & DI singleton + stale DNS.** A per-request client re-pays a token
  fetch and a singleton pins DNS; getting the lifetime/handler wrong is a real cost, not style.
  `MUST load dotnet-client-initialization`.
- **Secrets in logs via built-in logger.** `LogRequestBody` and the `PAYPALSERVERSDKCLIENT_LOG` env
  var can emit unredacted JSON bodies carrying card PAN/CVV; leaving `LoggerFactory` unset arms the
  env var. `MUST load dotnet-configuration-resilience`.
- **`Timeout` is per-attempt, not a call budget; retries multiply it.** A hung/failing call can cost
  a multiple of the knob; only a `CancellationToken` deadline bounds the whole call.
  `MUST load dotnet-configuration-resilience`.
- **Manual pagination of `SearchTransactions` can loop unbounded / silently truncate.** Provider
  stop-conditions are not a bound; a page cap and a no-progress guard are mine to add.
  `MUST load dotnet-configuration-resilience`.
- **Named arguments for `SearchTransactions`.** 8 leading nullable-no-default params mis-bind in a
  positional call. `MUST load dotnet-calling-endpoints`.
- **Enum `.Value` vs `ToString()`.** Interpolating a `StringEnum<T>` into a log/response yields the
  debug form, not the wire value. `MUST load dotnet-models`.
- **`TryGetRawError` is not a catch-all and must be last; Case-A `TryGetNoContent(out RawError)`
  [500] is a separate arm.** Skipping an accessor silently drops that response.
  `MUST load dotnet-error-handling`.
- **Union/model read-back & unknown fields.** Response nesting (`PurchaseUnits→Payments→Authorizations`)
  and future fields need the right access pattern. `MUST load dotnet-models`.

---

## 4. REQUIRED READING (load before implementation; contents deliberately not restated here)

| Skill (plugin-qualified) | Governs |
| --- | --- |
| `paypal-platforms-team:dotnet-client-initialization` | Client construction, HttpClient/DI lifetime |
| `paypal-platforms-team:dotnet-authentication` | OAuth2 client-credentials wiring + fail-fast |
| `paypal-platforms-team:dotnet-calling-endpoints` | Named args, building request bodies, reading responses |
| `paypal-platforms-team:dotnet-models` | Enums (`.Value`), nested models, unknown fields, dates as strings |
| `paypal-platforms-team:dotnet-error-handling` | Case A/B catch ladders, boundary translation |
| `paypal-platforms-team:dotnet-configuration-resilience` | Retries, per-attempt timeout, call budget, pagination, logging/redaction |

Both mandatory `JsonException` hazard rows:
- A drifted/malformed **2xx** body (missing `required` member) surfaces as
  `System.Text.Json.JsonException` from deserialization, **not** as `SdkException` — an
  SDK-exception-only catch ladder lets it escape. Catch it at the boundary and sanitize.
- A **non-2xx** body that doesn't match its operation's generated `{Operation}Error` shape throws
  `JsonException` **while the error object is being constructed**, so it **replaces** the
  `SdkException` and the HTTP status is destroyed with it.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | **Credential fail-fast** | Bind `PayPalSettings` from section `PayPal:` with `[Required]` on `ClientId`, `ClientSecret`, `Environment`, `Currency`; `AddOptions<>().Bind(...).ValidateDataAnnotations().ValidateOnStart()` **plus** an explicit guard that rejects blank/whitespace on **each** part (a blank part ≠ missing). Host refuses to boot; message names the key, never echoes a value. |
| 2 | **Secret sourcing & rotation** | Secrets come from **.NET user-secrets** (loaded from env `PAYPAL_*` by the operator) via `IConfiguration`; never in repo files. `AddPayPalServerSdkClient` builds the options + `OAuth2ClientCredentials` **once at registration** and captures them in the singleton, so a rotated secret takes effect only on **process restart** — acceptable here; documented, no hot-reload. |
| 3 | **Total timeout budget** | `options.Retry.Timeout` set to 30s (per-attempt). Every SDK call goes through one `Bounded(...)` helper at the gateway boundary that links `HttpContext.RequestAborted` and `CancelAfter(60s)` — the only true whole-call bound. |
| 4 | **Write-retry ownership** | All PayPal writes in scope are **POST**; default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS, so the SDK **never resends** them. No `PUT` in scope. Default kept unchanged (POST stays non-retried) — protects against double authorize/capture at the transport layer. |
| 5 | **Idempotency & ambiguous writes** | PayPal's `PayPal-Request-Id` (the `payPalRequestId` param, a **real** caller-controlled key) is set to a **deterministic** value per logical write: authorize=`pay-{orderId}`, capture=`cap-{orderPaymentId}`, void=`void-{orderPaymentId}`, reauth=`reauth-{orderPaymentId}`. Refund uses the **caller-supplied** idempotency key verbatim as `payPalRequestId`. Belt-and-braces DB guards: pay short-circuits if already authorized; fulfil if already captured; refund records processed keys (same key → return prior refund; distinct keys → new partial refund allowed). Ambiguous-outcome writes are reconcilable via `GET /api/reconciliation` (custom_id/invoice_id = eShop order ref). |
| 6 | **Observability** | Info: operation + eShop order id + PayPal ids (order/auth/capture/refund) + `Error.DebugId`/`Error.Name` on failure (the PayPal correlation id) — **never** card fields or request bodies. `LogRequestBody` stays off. |
| 7 | **Sensitive data** | Card PAN/CVV live in `CardRequest`/`SetupTokenRequestCard` request models. → `LogRequestBody` stays **false** and `options.Logging.LoggerFactory` is **assigned explicitly** in DI so `PAYPALSERVERSDKCLIENT_LOG` cannot switch bodies on from outside. Our own DTOs/logs never echo PAN/CVV; card number is never persisted (only PayPal vault id + last4/brand/expiry stored). |
| 8 | **Environment selection** | Only `ServerEnvironment.Sandbox` is declared; always selected. `PayPal:BaseUrl` (optional) → `options.Server.Default.Sandbox.BaseUrl` (verbatim, covers token + all calls, verified in `AuthSchemes.cs`). Dev/test target sandbox only; there is no live environment member, so live traffic is only reachable by an explicit `BaseUrl` override — never by default. |

---

## 6. Assumptions & Blockers

- **No Blockers.** Every capability the two flows need is covered by an in-scope operation.
- **Assumption (design, YOUR CALL):** payment/fulfilment lifecycle lives on a new `OrderPayment`
  aggregate keyed to the reused eShop `Order`; the `Order`/`OrderItem` model itself is untouched
  (additive, per task).
- **Assumption:** `POST /api/orders` request carries `{ items:[{catalogItemId, quantity}], (optional
  shipping address) }`; prices are read from `CatalogItem.Price` (server-side), never trusted from the
  client; currency from `PayPal:Currency`.
- **UNVERIFIED (live-traffic only) — defensive directives:**
  - Whether the sandbox card auth returns `PAYER_ACTION_REQUIRED` (3DS challenge). Directive: if
    `AuthorizeOrder`/order status is `PAYER_ACTION_REQUIRED` or a payer-action link is present, **do
    not** build a browser round-trip — return a clear "approval required, not supported" error and
    STOP/report (per task). 
  - Whether `ReauthorizePayment` succeeds for a direct-card authorization and PayPal's honor-period
    limits. Directive: on capture, if the stored authorization is past `ExpirationTime` or capture
    fails with an expired/`422` auth, attempt `ReauthorizePayment`; if that fails, surface an
    **operator-actionable** error ("authorization can no longer be renewed — a new authorization is
    required") rather than a raw failure. Do not assume reauth always works.
  - Whether sandbox enforces `PayPal-Request-Id` retention. Directive: keep the DB idempotency guards
    as the primary defense; treat the header as best-effort.

**Resolved during self-verification (live sandbox):**
- Direct-card authorize→capture→refund, void, saved-card vault + reuse, and multi-page reconciliation
  all confirmed against the sandbox test card. Capture returns real `paypal_fee`/`net_amount`.
- `VoidPayment` returns **204 No Content**; `prefer=return=representation` yields a parseable body, and
  a `JsonException` on a 2xx/empty void is treated as success (an error would be `SdkException`).
- No `PAYER_ACTION_REQUIRED` (3DS) challenge was seen with the sandbox card; the guard remains in place.
- The shared sandbox account intermittently returns `TRANSACTION_REFUSED`/`DUPLICATE_INVOICE_ID` under
  concurrent load from other runs; a refused authorize leaves the order retryable (no hold placed).
