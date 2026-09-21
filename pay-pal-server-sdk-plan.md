# PayPal Server SDK integration plan — eShopOnWeb PublicApi

Adds card payments (authorize → capture at fulfil → void/refund) and vaulted saved cards to
`src/PublicApi`, additively. Domain state lives on the existing `Order` aggregate; PayPal calls
go through an `IPaymentGateway` implemented in Infrastructure over the PayPal Server SDK (.NET).

Non-secret facts from this environment: `PAYPAL_ENVIRONMENT=sandbox`, `PAYPAL_CURRENCY=USD`,
`PayPal:BaseUrl` unset (→ SDK sandbox default). Secrets already loaded into user-secrets under
`PayPal:ClientId/ClientSecret/Environment/Currency` (values never written to the repo).

---

## 1. Scope & sequence

| Step | eShop endpoint | PayPal operation(s) |
| --- | --- | --- |
| 1 | `POST /api/orders` (shopper) | none — create eShop `Order` in `AwaitingPayment` from catalog ids+qty |
| 2 | `POST /api/orders/{id}/pay` (shopper) | `Orders.CreateOrder` (intent=AUTHORIZE) → `Orders.AuthorizeOrder` (payment_source = card **or** card.vault_id) |
| 3 | `POST /api/orders/{id}/fulfil` (admin) | `Payments.CaptureAuthorizedPayment`; if auth stale → `Payments.ReauthorizePayment` then capture |
| 4 | `POST /api/orders/{id}/cancel` (admin) | `Payments.VoidPayment` |
| 5 | `POST /api/orders/{id}/refunds` (shopper own order) | `Payments.RefundCapturedPayment` (empty body = full; `amount` = partial) |
| 6 | `GET /api/my-orders` (shopper) | none — local read of caller's orders + payment state |
| 7 | `GET /api/reconciliation?from&to` (admin) | `TransactionSearch.SearchTransactions` (chunk range ≤31 days, page to `total_pages`) |
| 8 | `POST /api/payment-methods` (shopper) | `Vault.CreatePaymentToken` (payment_source.card) |
| 9 | `GET /api/payment-methods` (shopper) | none — local read of caller's saved cards |
| 10 | `DELETE /api/payment-methods/{id}` (shopper) | `Vault.DeletePaymentToken` |

**Authorize is single-step by decision** (`YOUR CALL`, revised during verification): the card (or the
saved card's `vault_id`) is supplied on `payment_source.card` at `CreateOrder` with `intent=AUTHORIZE`
and `prefer=return=representation`, and `PayPal-Request-Id` set — the "single-step create" the SDK
`<remarks>` flags. Sandbox verification proved the alternative (create bare, then `AuthorizeOrder`
with the card in the body) is rejected `422 UNPROCESSABLE_ENTITY`. The authorization is read from the
create response's `purchase_units[].payments.authorizations[]`; only if it is absent (order merely
`APPROVED`) does the code fall back to an explicit `AuthorizeOrder` (card already attached). Both a
raw card and a saved-card reuse use `CardRequest` (its `VaultId` carries the saved token).
`invoice_id` is **omitted** (the account enforces its uniqueness); `custom_id` carries the eShop
reconciliation reference and surfaces as `custom_field` in transaction reports.

---

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim** — every parameter name is the literal C#
> identifier; the cancellation-token parameter is literally `ct`, so named args write `ct:`. The
> 5/4/etc. leading nullable header params (`payPalMockResponse`…`body`) have no C# default and
> **must be passed explicitly** — pass `null` to skip.
> ⚠ Every SDK type is written fully-qualified from the namespace its source path implies
> (`Models/` → `PayPalServerSdk.Models`, `Models/Enums/` → `PayPalServerSdk.Models.Enums`,
> `Errors/` → `PayPalServerSdk.Errors`, client/options → `PayPalServerSdk`,
> `Servers/` → `PayPalServerSdk.Servers`).

### Client construction / auth / servers

- Register via DI extension `services.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>)`
  (namespace `PayPalServerSdk`; source `ServiceCollectionExtensions.cs`). It calls
  `services.AddHttpClient()` and registers the client as a **singleton**, building the options object
  **once at registration** (uses `IHttpClientFactory`). Source: `ServiceCollectionExtensions.cs`.
- `PayPalServerSdkClientOptions` (source `PayPalServerSdkClientOptions.cs`): set
  `Oauth2 = new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials { ClientId=…, ClientSecret=… }`
  (both `required string`; `Scope` optional), `Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox`.
  Source: `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`, `sdk-map.md`.
- **BaseUrl override**: `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>` when set.
  Verified: the OAuth token URL is `server.Default("/v1/oauth2/token")` (`AuthSchemes.cs` line 17) and
  `Server.Default` resolves through `Sandbox.BaseUrl` (`Servers/DefaultOptions.cs`), so this single
  override covers **every** call including the token request. Default `https://api-m.sandbox.paypal.com`.
- Auth model: every op in scope has **Auth: `options.Oauth2`**. Error model is throw-only; **no
  no-throw `…Result` variants exist anywhere in this SDK**. Source: `sdk-map.md`.

### Operations

| Op | Signature (verbatim) · request fields used · response fields read · error case · source |
| --- | --- |
| `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)`. Body `OrderRequest`: `Intent (intent): CheckoutPaymentIntent` **required**=AUTHORIZE · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest>` **required** · `PaymentSource (payment_source): PaymentSource` → `.Card (card): CardRequest` (raw card or `VaultId`). Call with `prefer="return=representation"`. Read `Order.Id`, `Order.Status`, `Order.PurchaseUnits[].Payments.Authorizations[]`. Error **Case A** `SdkException<CreateOrderError>`, `TryGetError(out Error)` [400,401,422]. Source: `map/operations/Orders.md`, `Models/OrderRequest.cs`, `Models/Order.cs`. |
| `client.Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", …, CancellationToken ct=default)`. Body `OrderAuthorizeRequest.PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource` → `.Card (card): CardRequest`. Read `OrderAuthorizeResponse.Status`, `.PurchaseUnits[0].Payments.Authorizations[0]` → `.Id`, `.Status (AuthorizationStatus)`, `.Amount`, `.ExpirationTime`. Error **Case A** `SdkException<AuthorizeOrderError>` `TryGetError` [400,401,403,404,422,500]. Source: `map/operations/Orders.md`, `Models/OrderAuthorizeRequest.cs`, `Models/OrderAuthorizeResponse.cs`, `Models/PurchaseUnit.cs`, `Models/PaymentCollection.cs`, `Models/AuthorizationWithAdditionalData.cs`. |
| `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", …, CancellationToken ct=default)`. Body `CaptureRequest.FinalCapture (final_capture): bool?`=true. Read `CapturedPayment.Id`, `.Status (CaptureStatus)`, `.Amount (Money)`, `.SellerReceivableBreakdown` → `.GrossAmount (Money, required)`, `.PaypalFee (Money?)`, `.NetAmount (Money?)`. Error **Case A** `SdkException<CaptureAuthorizedPaymentError>` `TryGetError` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500]. Source: `map/operations/Payments.md`, `Models/CaptureRequest.cs`, `Models/CapturedPayment.cs`, `Models/SellerReceivableBreakdown.cs`. |
| `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", …, CancellationToken ct=default)`. Body `ReauthorizeRequest.Amount (amount): Money?` — **omit → reauthorize original amount** (only `amount` is supported per `<remarks>`; renew days 4–29, once). Read `PaymentAuthorization.Id`, `.Status`, `.ExpirationTime`. Error **Case A** `TryGetError` [400,401,403,404,422] · `TryGetNoContent` [500]. Source: `map/operations/Payments.md`, `Models/ReauthorizeRequest.cs`, `Models/PaymentAuthorization.cs`. |
| `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", …, CancellationToken ct=default)`. No body. Read `PaymentAuthorization.Status`. Error **Case A** `TryGetError` [401,403,404,409,422] · `TryGetNoContent` [500]. Source: `map/operations/Payments.md`. |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", …, CancellationToken ct=default)`. Body **null → full refund**; partial → `RefundRequest.Amount (amount): Money?` (+ optional `CustomId`, `InvoiceId`). Read `Refund.Id`, `.Status (RefundStatus)`, `.Amount`. Error **Case A** `TryGetError` [400,401,403,404,409,422] · `TryGetNoContent` [500]. `payPalRequestId` = caller idempotency key. Source: `map/operations/Payments.md`, `Models/RefundRequest.cs`, `Models/Refund.cs`. |
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions=null, CancellationToken ct=default)`. Body `PaymentTokenRequest.PaymentSource (payment_source): PaymentTokenRequestPaymentSource` **required** → `.Card (card): PaymentTokenRequestCard` (`Number`,`Expiry`,`SecurityCode`,`Name`,`BillingAddress?`). Read `PaymentTokenResponse.Id`, `.PaymentSource.Card (CardPaymentTokenEntity)` → `.LastDigits`, `.Brand (CardBrand)`, `.Expiry`. Error **Case A** `TryGetError` [400,403,404,422,500]. Source: `map/operations/Vault.md`, `Models/PaymentTokenRequest.cs`, `Models/PaymentTokenRequestPaymentSource.cs`, `Models/PaymentTokenRequestCard.cs`, `Models/PaymentTokenResponse.cs`, `Models/CardPaymentTokenEntity.cs`. |
| `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions=null, CancellationToken ct=default)`. Returns `void`. Error **Case A** `TryGetError` [400,403,500]. Source: `map/operations/Vault.md`. |
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, …, CancellationToken ct=default)`. `startDate`/`endDate` RFC-3339, **max range 31 days** (`<remarks>`); `page` is **1-relative**, loop until `page >= total_pages`. Read `SearchResponse.TransactionDetails[].TransactionInfo (TransactionInformation)` → `.TransactionId`, `.TransactionStatus`, `.TransactionAmount (Money)`, `.TransactionInitiationDate`, `.InvoiceId`, `.CustomField`; plus `SearchResponse.TotalPages`, `.Page`. Error **Case B** `SdkException<RawError>`. Source: `map/operations/TransactionSearch.md`, `Models/SearchResponse.cs`, `Models/TransactionDetails.cs`, `Models/TransactionInformation.cs`. |

Money everywhere = `PayPalServerSdk.Models.Money { CurrencyCode (required, ISO-4217), Value (required, decimal string) }`.
`AmountWithBreakdown` (purchase-unit amount) has the same `CurrencyCode`/`Value` required pair.
`PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown` **required**; set optional
`CustomId (custom_id)` and `InvoiceId (invoice_id)` = eShop reconciliation keys (**purpose: reconcile PayPal
txns ↔ eShop orders in Flow-1 report**); leave `ReferenceId` unset → PayPal defaults it to `default`.

### Enums (wire values needed)

- `CheckoutPaymentIntent`: `AUTHORIZE`, `CAPTURE`. Use `AUTHORIZE`.
- `OrderStatus`: `CREATED SAVED APPROVED VOIDED COMPLETED PAYER_ACTION_REQUIRED`.
- `AuthorizationStatus`: `CREATED CAPTURED DENIED PARTIALLY_CAPTURED VOIDED PENDING`.
- `CaptureStatus`: `COMPLETED DECLINED PARTIALLY_REFUNDED PENDING REFUNDED FAILED`.
- `RefundStatus`: `CANCELLED FAILED PENDING COMPLETED`.
- `CardBrand`: `VISA MASTERCARD AMEX DISCOVER …` (safe-display only). Enums are `StringEnum<T>`,
  read via the C# member or `.ToString()`/value; build with `Type.FromValue("WIRE")`.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| a `paymentMethodId` supplied to `pay` must be a saved card the caller owns (returned by `POST /api/payment-methods`) → resolves to its PayPal `vault_id` | `pay` ← `POST /api/payment-methods` | application: `SavedPaymentMethod` lookup by `(Id, BuyerId)`; 404 otherwise |
| an `orderId` supplied to pay/fulfil/cancel/refund/my-orders must be an order created by `POST /api/orders`; shopper ops additionally require caller ownership | all order ops ← `POST /api/orders` | application: repository load + `BuyerId == caller` (or admin role) |
| refund amount: `Σ refunds ≤ captured amount`; a partly-refunded order stays refundable only up to the remainder | `refunds` ← `fulfil` | application: `Order`/`Payment` domain guard before calling PayPal |
| a deleted saved card is no longer usable to pay | `pay` ← `DELETE /api/payment-methods/{id}` | application: local delete + `Vault.DeletePaymentToken`; lookup fails afterward |

---

## 3. Trap notes (hazard + skill pointer — not resolved here)

- **Client lifetime / HttpClient pipeline**: the DI extension registers a singleton over
  `IHttpClientFactory` — do not rebuild per request, and understand what the singleton captures.
  `MUST load dotnet-client-initialization`.
- **Credentials shape & where to set them**: OAuth2 client-credentials must be set before/at
  registration; a never-set credential is *skipped*, surfacing as a later 401 not a throw.
  `MUST load dotnet-authentication`.
- **Must-pass-explicitly params & named args**: the leading nullable header params have no default
  and list/search ops mis-bind positionally. `MUST load dotnet-calling-endpoints`.
- **Models**: enums are `StringEnum<T>` not C# enums; `required` initializer members; unions via
  `TryGet…`; unknown response fields kept on `AdditionalProperties`. `MUST load dotnet-models`.
- **Error boundary — two JsonException directions & Case A/B accessors**: see REQUIRED READING
  hazard rows. `MUST load dotnet-error-handling`.
- **Timeout is per-attempt, retries gate on HTTP method, pagination is hand-rolled, LogRequestBody
  is unredacted**: capture/void/refund are `POST` (never auto-resent); the reconciliation page loop
  is mine to bound. `MUST load dotnet-configuration-resilience`.
- **Test seam**: the `HttpClient` ctor arg / a fake `IPaymentGateway` is the seam.
  `MUST load dotnet-testing`.

---

## 4. REQUIRED READING (load all before implementing; contents deliberately not copied here)

| skill (plugin `paypal-platforms-team`) | governs |
| --- | --- |
| `paypal-platforms-team:dotnet-client-initialization` | SDK client construction + DI singleton (step: Infra DI) |
| `paypal-platforms-team:dotnet-authentication` | OAuth2 client-credentials wiring (step: Infra DI) |
| `paypal-platforms-team:dotnet-calling-endpoints` | every `client.{group}.{op}` call (steps 2–10) |
| `paypal-platforms-team:dotnet-models` | building request bodies / reading enums+unions (steps 2–10) |
| `paypal-platforms-team:dotnet-error-handling` | the gateway error-translation boundary (all PayPal calls) |
| `paypal-platforms-team:dotnet-configuration-resilience` | timeout budget, retry eligibility, reconciliation pagination, logging posture |
| `paypal-platforms-team:dotnet-testing` | integration-layer tests |

**Mandatory hazard rows (verbatim):** `System.Text.Json.JsonException` reaches the boundary from two
directions needing opposite handling: (1) a drifted/malformed **2xx** body (a missing `required`
member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException`, so an
SDK-exception-only catch ladder lets it escape; (2) a **non-2xx** body that does not match its
operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is
being constructed*, so it **replaces** the `SdkException` and the HTTP status is destroyed with it.
→ The gateway catches `SdkException<T>`, then `JsonException`, then `Exception`, and never assumes an
`SdkException` always arrives.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` validated at startup (`ValidateOnStart`): host refuses to start if `ClientId`, `ClientSecret`, `Currency`, or `Environment` is missing/blank. `ClientId`+`ClientSecret` are **both** checked (a blank part ≠ missing). BaseUrl optional. |
| 2 | Secret sourcing & rotation | Secrets from **.NET user-secrets** (dev) / env (`PayPal__*`) → `PayPal:` config section. The DI extension captures options in a **singleton at registration**, so a rotated secret takes effect **only on process restart**; documented, no hot-reload required by task. |
| 3 | Total timeout budget | SDK `Timeout` is **per-attempt**. Every gateway call is issued with a `CancellationToken` from a per-call `CancellationTokenSource` (default 100s) that bounds the **whole** call incl. retries; the ASP.NET request-abort token is linked in. Enforced in the gateway. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS. All money-moving writes here are **POST** (create/authorize/capture/void/refund/vault) → **never auto-resent** by the SDK. So idempotency is enforced by me (row 5), not defeated by retries. Left at SDK default. |
| 5 | Idempotency & ambiguous writes | Every gateway-generated `PayPal-Request-Id` (create/authorize/capture/void/reauth) is `{phase}-{orderId}-{processNonce}`: stable within a process (idempotent for retries) and salted by a per-process nonce so the in-memory-DB reset (order ids restart at 1) never replays a prior run's cached response — a real collision found and fixed during verification. Plus a domain short-circuit: a second `pay` on an `Authorized` order returns the existing hold (never authorizes twice); capture/void are guarded by order state. **Refund** uses the **caller-supplied idempotency key verbatim** (not salted) as `PayPal-Request-Id` **and** persists it per refund; a repeat key returns the stored `refundId` (no second refund); two distinct partial keys both proceed. |
| 6 | Observability | Gateway logs at Information: op name, eShop orderId, PayPal order/auth/capture/refund ids, status. On error logs status + PayPal `debug_id`/error name from the typed `Error`/`RawError` body. **`LogRequestBody` stays OFF.** No card fields ever logged. |
| 7 | Sensitive data | Card PAN/CVV flow through `CardRequest`/`PaymentTokenRequestCard` (request models). Therefore SDK `LogRequestBody` is **off** and `options.Logging.LoggerFactory` is **set explicitly** in registration so `PAYPALSERVERSDKCLIENT_LOG` cannot force body logging on from outside. Card details are **never persisted** (only vault token id + brand + last4 + expiry) and **never logged**. |
| 8 | Environment selection | One server group `Default`; only environment member is `ServerEnvironment.Sandbox`. All test traffic → sandbox. `PayPal:Environment` bound from config; non-`sandbox`/non-empty values map to Sandbox with a startup warning (SDK declares no Production member) — keeps prod/live traffic impossible from this build unless `PayPal:BaseUrl` is explicitly overridden. |

---

## 6. Assumptions & Blockers

- **Assumption (minor):** vaulting a card needs no prior PayPal `customer` — `card.vault_id` alone is
  accepted by `AuthorizeOrder`'s payment source, so saved-card metadata (token id, brand, last4) is
  stored locally and ownership enforced locally, rather than via `Vault.ListCustomerPaymentTokens`.
- **Assumption (minor):** in-memory DB per host (task-mandated `UseOnlyInMemoryDatabase=true`) means
  the whole Flow-1 lifecycle is exercised within one PublicApi run; an EF migration is still authored
  for the SQL path (production-grade) but is inert under the in-memory provider.
- **No blockers.** Every capability (authorize, capture, reauthorize, void, refund, vault, delete,
  transaction search) is present in the SDK map. 3DS/browser challenge is **not** built — if a card
  payment returns a challenge/`PAYER_ACTION_REQUIRED`/approve-link, the gateway throws a
  `PaymentChallengeRequiredException` surfaced as an actionable error (per task "STOP and report").

---

## 7. Runtime notes

- Run PublicApi with `UseOnlyInMemoryDatabase=true` and `DOTNET_ROLL_FORWARD=Major` (SDK 8→10);
  bind only to the assigned port block (`APP_PORT_BLOCK_BASE`=36320 … +19).
- SDK is not on NuGet → vendored as a source project referenced by Infrastructure (built from the
  `context-plugins/paypal-csharp-sdk` `main` source), so the solution builds portably.
