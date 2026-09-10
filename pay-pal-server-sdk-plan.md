# PayPal Server SDK integration plan — eShopOnWeb PublicApi

Additive PayPal card payments + saved cards on `src/PublicApi`. SDK root namespace `PayPalServerSdk`,
client `PayPalServerSdkClient`, OAuth2 client-credentials, only environment `ServerEnvironment.Sandbox`.
Built from source (not on NuGet) — **vendored** into `src/PayPalServerSdk/` and referenced by
`src/Infrastructure`.

## 1. Scope & sequence

Layering mirrors the repo: domain types + service interfaces in `ApplicationCore`; the SDK-touching gateway
+ EF config in `Infrastructure`; HTTP endpoints in `PublicApi` (MinimalApi.Endpoint style, auto-discovered).

1. **Vendor SDK** into `src/PayPalServerSdk/` (csproj with `ManagePackageVersionsCentrally=false` to stay
   out of the repo's central package management), add to `eShopOnWeb.sln`, `ProjectReference` from
   Infrastructure.
2. **Domain (ApplicationCore)**: entities `OrderPayment` (1:1 with `Order`, holds PayPal state), its child
   `PaymentRefund`, and `SavedCard`; `PaymentStatus` enum; DTOs; `PayPalSettings`; interfaces
   `IPayPalGateway`, `IOrderPaymentService`, `ISavedCardService`, `IReconciliationService`; domain exception
   `PaymentGatewayException` (+ `PaymentChallengeException`).
3. **Infrastructure**: `PayPalGateway : IPayPalGateway` (wraps SDK — the only SDK consumer); EF configs +
   DbSets on `CatalogContext`; `AddPayPalServerSdkClient` registration helper.
4. **ApplicationCore services**: `OrderPaymentService`, `SavedCardService`, `ReconciliationService` —
   orchestrate repositories + gateway, own idempotency state + state-machine guards.
5. **PublicApi**: 11 endpoints (below); bind `PayPalSettings` with `ValidateOnStart`; register services +
   SDK client; load credentials into user-secrets (never the repo).
6. Tests + live sandbox verification (direct card `4111 1111 1111 1111`).

Endpoint → operation map (all `client.{Group}.{Op}`, Oauth2):

| Endpoint | Role | PayPal op(s) |
| --- | --- | --- |
| POST /api/orders | shopper | (none — domain only) |
| POST /api/orders/{id}/pay | shopper | `Orders.CreateOrder` (intent AUTHORIZE, card or vault_id) → if APPROVED, `Orders.AuthorizeOrder` |
| POST /api/orders/{id}/fulfil | admin | `Payments.CaptureAuthorizedPayment`; on stale auth `Payments.ReauthorizePayment` then capture |
| POST /api/orders/{id}/cancel | admin | `Payments.VoidPayment` |
| POST /api/orders/{id}/refunds | shopper | `Payments.RefundCapturedPayment` |
| GET /api/my-orders | shopper | (none — domain read) |
| GET /api/reconciliation | admin | `TransactionSearch.SearchTransactions` (all pages) |
| POST /api/payment-methods | shopper | `Orders.CreateOrder` (card + `attributes.vault.store_in_vault=ON_SUCCESS`) → `Payments.VoidPayment` (release the verification hold) — **see §6 note** |
| GET /api/payment-methods | shopper | (none — domain read; vault ids stored locally) |
| DELETE /api/payment-methods/{id} | shopper | `Vault.DeletePaymentToken` |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, **verbatim**. Every parameter name is the literal C# identifier — the
> cancellation-token param is literally `ct`, so named args write `ct:`. Nullable-no-default params
> (`string? payPalMockResponse`, …) **must be passed explicitly** (`null` to skip).
> ⚠ Every SDK type is written fully-qualified by the namespace its source path implies, taken from the path
> the map gives for **that** type: records → `PayPalServerSdk.Models`; enums → `PayPalServerSdk.Models.Enums`;
> typed errors → `PayPalServerSdk.Errors`; client/options → `PayPalServerSdk`; `ServerEnvironment` →
> `PayPalServerSdk.Servers`; `OAuth2ClientCredentials` →
> `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`; `SdkException<T>` →
> `PayPalServerSdk.Core.Exceptions`; `RawError`/`ApiError` → `PayPalServerSdk.Core.ErrorResponse`.

| Op | Signature (verbatim) | Request model → fields used | Response → fields read | Error case | Source |
| --- | --- | --- | --- | --- | --- |
| `CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `OrderRequest`: `Intent` (req, `CheckoutPaymentIntent`), `PurchaseUnits` (req, `IReadOnlyList<PurchaseUnitRequest>`), `PaymentSource?` | `Order`: `Id`, `Status` (`OrderStatus`), `PurchaseUnits[].Payments.Authorizations[]` (`AuthorizationWithAdditionalData`: `Id`, `Status`, `ExpirationTime`, `Amount`) | A `SdkException<CreateOrderError>`; `TryGetError(out Error)` [400,401,422] · `TryGetRawError` | map/operations/Orders.md; Models/OrderRequest.cs |
| `AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `OrderAuthorizeRequest`: `PaymentSource?` (usually pass `body: null`) | `OrderAuthorizeResponse`: `Id`, `Status`, `PurchaseUnits[].Payments.Authorizations[]` | A `CreateOrder`-shaped; `TryGetError` [400,401,403,404,422,500] | Orders.md; Models/OrderAuthorizeResponse.cs |
| `CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `CaptureRequest`: `Amount?` (`Money`), `FinalCapture?` (bool), `InvoiceId?` | `CapturedPayment`: `Id`, `Status` (`CaptureStatus`), `Amount` (`Money`), `SellerReceivableBreakdown` (`GrossAmount`, `PaypalFee`, `NetAmount` — all `Money`) | A; `TryGetError` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | Payments.md; Models/CapturedPayment.cs, SellerReceivableBreakdown.cs |
| `ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `ReauthorizeRequest`: `Amount?` (`Money`) | `PaymentAuthorization`: `Id`, `Status`, `ExpirationTime`, `Amount` | A; `TryGetError` [400,401,403,404,422] · `TryGetNoContent` [500] | Payments.md; Models/PaymentAuthorization.cs |
| `VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | (no body) | `PaymentAuthorization`: `Status` | A; `TryGetError` [401,403,404,409,422] · `TryGetNoContent` [500] | Payments.md |
| `RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `RefundRequest`: `Amount?` (`Money`), `InvoiceId?`, `NoteToPayer?` | `Refund`: `Id`, `Status` (`RefundStatus`), `Amount` (`Money`) | A; `TryGetError` [400,401,403,404,409,422] · `TryGetNoContent` [500] | Payments.md; Models/Refund.cs |
| `CreateSetupToken` | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `SetupTokenRequest`: `PaymentSource` (req, `SetupTokenRequestPaymentSource` → `Card`=`SetupTokenRequestCard`{Number,Expiry,SecurityCode,Name,BillingAddress}), `Customer?` (`Customer`{Id?,MerchantCustomerId?}) | `SetupTokenResponse`: `Id`, `Status` (`PaymentTokenStatus`), `Customer` | A; `TryGetError` [400,403,422,500] | Vault.md; Models/SetupTokenRequest*.cs |
| `CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `PaymentTokenRequest`: `PaymentSource` (req, `PaymentTokenRequestPaymentSource` → `Token`=`VaultTokenRequest`{Id(req),Type(req `VaultTokenRequestType.SetupToken`)}), `Customer?` | `PaymentTokenResponse`: `Id`, `Customer` (`CustomerResponse`.Id), `PaymentSource.Card` (`CardPaymentTokenEntity`: `LastDigits`, `Brand`, `Expiry`, `Name`) | A; `TryGetError` [400,403,404,422,500] | Vault.md; Models/PaymentTokenResponse.cs, CardPaymentTokenEntity.cs |
| `DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions=null, CancellationToken ct=default)` | (none) | `void` (Task) | A; `TryGetError` [400,403,500] | Vault.md |
| `SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, RequestOptions? requestOptions=null, CancellationToken ct=default)` | — (query params; pass nullable ones `null`, **named args**) | `SearchResponse`: `TransactionDetails[]` (`TransactionInformation`: `TransactionId`, `TransactionStatus`, `TransactionAmount` (`Money`), `InvoiceId`, `TransactionInitiationDate`, `FeeAmount`), `Page`, `TotalPages`, `TotalItems` | **B** `SdkException<RawError>` | TransactionSearch.md; Models/SearchResponse.cs, TransactionInformation.cs |

Supporting request model (build, not via SDK list call): `PurchaseUnitRequest`: `Amount` (req, `AmountWithBreakdown`{CurrencyCode,Value — both req strings}), `InvoiceId?`, `CustomId?`, `Description?`. `PaymentSource`: `Card?` (`CardRequest`{Name?,Number?,Expiry?("YYYY-MM"),SecurityCode?,BillingAddress?,VaultId? — vault_id to pay with a saved card}). `Money`{CurrencyCode(req),Value(req "F2" string)}.

Enums (build via static member; read via `.Value`):
- `CheckoutPaymentIntent.Authorize` = "AUTHORIZE" (source Models/Enums/CheckoutPaymentIntent.cs).
- `VaultTokenRequestType.SetupToken` = "SETUP_TOKEN" (Models/Enums/VaultTokenRequestType.cs).
- `OrderStatus`, `CaptureStatus`, `AuthorizationStatus`, `RefundStatus`, `PaymentTokenStatus`, `CardBrand`
  read back by `.Value` (no need to enumerate members); status strings compared by `.Value`.

Client/auth/server (source: sdk-map.md, ServiceCollectionExtensions.cs, Servers/DefaultOptions.cs):
- `services.AddPayPalServerSdkClient(options => {...})` — singleton, options captured **once** at
  registration; fills `Logging.LoggerFactory` from DI `ILoggerFactory`.
- `options.Oauth2 = new OAuth2ClientCredentials { ClientId=…, ClientSecret=… }`.
- `options.Environment = ServerEnvironment.Sandbox` (only member).
- Base-URL override: `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>` — token request resolves
  through the same `server.Default("/v1/oauth2/token")`, so this override covers the credential call too
  (source AuthSchemes.cs line 17). Leave unset when `PayPal:BaseUrl` is absent (default sandbox URL).

## 3. Trap notes

- **Pay is a POST chain that can double-charge on a double-click.** Idempotency = a persisted, stable
  `payPalRequestId` per order reused on retry, **plus** a state guard — not the injected `Idempotency-Key`
  header (fresh GUID per call, deduplicates nothing). `MUST load dotnet-configuration-resilience`.
- **Error boundary mixes Case A (typed `{Op}Error.TryGetError`/`TryGetNoContent`/`TryGetRawError`) and one
  Case B (`SearchTransactions` → `SdkException<RawError>`).** A 2xx body that drifts, or a non-2xx body not
  matching `{Op}Error`, surfaces as `System.Text.Json.JsonException`, **not** `SdkException`.
  `MUST load dotnet-error-handling`.
- **Card number/CVV travel in request bodies (`CardRequest`, `SetupTokenRequestCard`).** Logging posture is
  load-bearing: `LogRequestBody` and `LoggerFactory`. `MUST load dotnet-configuration-resilience`.
- **Reconciliation must cover the whole range, not page 1.** Paging bound + stop condition.
  `MUST load dotnet-configuration-resilience`.
- **Enums interpolated into logs/status carry the debug form, not the wire value — use `.Value`.**
  `MUST load dotnet-models`.
- **Timeout is per-attempt; a whole-call bound needs a CancellationToken deadline.**
  `MUST load dotnet-configuration-resilience`.

## 4. REQUIRED READING (load before implementation — this sheet deliberately omits their contents)

- `paypal-platforms-team:dotnet-client-initialization` — SDK client construction + DI registration (step 3).
- `paypal-platforms-team:dotnet-authentication` — OAuth2 client-credentials + fail-fast (step 3/5).
- `paypal-platforms-team:dotnet-calling-endpoints` — operation call shape, named args for SearchTransactions (step 3/4).
- `paypal-platforms-team:dotnet-models` — enums (`.Value`), Money strings, building nested request models (step 3/4).
- `paypal-platforms-team:dotnet-error-handling` — Case A/B ladder + the two JsonException directions (step 3).
- `paypal-platforms-team:dotnet-configuration-resilience` — retries/verbs, timeout budget, logging redaction, pagination (step 3/4).
- `paypal-platforms-team:dotnet-testing` — seam to fake when writing integration-layer tests (step 6).

⚠ Two mandatory hazard rows (always): (1) a drifted/malformed **2xx** body throws `JsonException` from
deserialization, **not** `SdkException` — an SDK-exception-only catch lets it escape; (2) a **non-2xx** body
not matching its `{Op}Error` shape throws `JsonException` *while the error object is built*, **replacing**
the `SdkException` and destroying the HTTP status. Both handled at the gateway boundary.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalSettings` bound from `PayPal:` section; `[Required]` on `ClientId`, `ClientSecret`, `Environment`, `Currency`; `.ValidateDataAnnotations().ValidateOnStart()` in PublicApi `Program.cs` — host refuses to boot if any is missing/blank (every part checked; `BaseUrl` optional). |
| 2 | Secret sourcing & rotation | From .NET user-secrets (loaded from env `PAYPAL_*` by me; never in repo). `AddPayPalServerSdkClient` captures options **once at registration** → a rotated secret needs a process restart. Acceptable for this app; documented. |
| 3 | Total timeout budget | `options.Retry.Timeout = 30s` (per attempt). Gateway wraps every call through a helper that links `HttpContext.RequestAborted` and `CancelAfter(60s)` — the only whole-call bound. |
| 4 | Write-retry ownership | All mutations are `POST`/`DELETE` → outside default `HttpMethodsToRetry` (`GET,HEAD,PUT,OPTIONS`), so the SDK never resends them. Only reads (`SearchTransactions`) are retryable — idempotent, safe. No `PUT` used. |
| 5 | Idempotency & ambiguous writes | Authorize/capture/void: persisted stable `payPalRequestId` per order (`OrderPayment.AuthorizeRequestId`/`CaptureRequestId`/`VoidRequestId`) + state-machine guard (re-pay of an Authorized order returns current state, no 2nd call). Refund: **caller-supplied** idempotency key → `payPalRequestId`; persisted in `PaymentRefund.IdempotencyKey`; repeat key returns stored `refundId`; distinct keys allowed; invariant `sum(refunds)+new ≤ captured` enforced before the call. Transport failure on a write → outcome unknown → surfaced as such, reconciled via `/api/reconciliation`. |
| 6 | Observability | Gateway logs op + orderId at Information; on `SdkException` logs PayPal `Error.DebugId` (correlation id) + `Error.Name`/issue at Warning. `LogRequestBody` stays **off**. No card fields ever logged by our code. |
| 7 | Sensitive data | `CardRequest`/`SetupTokenRequestCard` carry PAN+CVV. `LogRequestBody=false` (default, left off) **and** `LoggerFactory` assigned via the DI extension (fills from `ILoggerFactory`), which disables the `PAYPALSERVERSDKCLIENT_LOG` env var. PAN/CVV never persisted (only brand+last4+expiry stored) and never written to logs. |
| 8 | Environment selection | SDK declares only `ServerEnvironment.Sandbox`; all traffic → sandbox. `PayPal:Environment` bound + required; value validated to be `sandbox` (guard rejects anything else so test traffic can never target a non-sandbox host). `PayPal:BaseUrl` optional verbatim override for every call incl. token. |

## 6. Assumptions & Blockers

- **No Blockers.** Every required capability maps to an SDK operation above.
- **Vault approach (verified against the live sandbox account).** The v3 standalone vault endpoints
  (`Vault.CreateSetupToken` / `Vault.CreatePaymentToken`) return PayPal `INTERNAL_SERVER_ERROR (500)` on this
  account — vaulting-without-purchase is not provisioned. Vaulting **on a transaction** is, so save-card runs
  a nominal `1.00` authorization with `payment_source.card.attributes.vault.store_in_vault = ON_SUCCESS`
  (the proven `/v2/checkout/orders` path), reads the vault id from
  `order.payment_source.card.attributes.vault.id`, then **voids** the authorization so no money is taken.
  NB: sending `attributes.customer.merchant_customer_id` with an email-shaped value also triggers a 500, so
  the customer object is sent only as `{ id }` when a PayPal customer id already exists; the first save lets
  PayPal create the customer. `Vault.DeletePaymentToken` removes the token normally.
- **Invoice-id uniqueness.** PayPal remembers invoice ids forever while the in-memory order id resets per
  run, so each `OrderPayment` generates a unique `eshop-{orderId}-{guid}` invoice id (stable for the order);
  reconciliation matches PayPal transactions back to eShop orders by that stored invoice id (not by parsing
  the order id out of a fixed prefix).
- **`VoidPayment` returns HTTP 204** (empty body) on success; the SDK cannot deserialize that into
  `PaymentAuthorization` and throws `JsonException`, which the gateway treats as success for void.
- Assumption: refund is **shopper-scoped** — the task names only fulfil/cancel/reconciliation as operator
  actions and says "every other endpoint is shopper-scoped," so refund acts on the caller's own order.
- Assumption: `POST /api/orders` needs a shipping address for the existing `Order` ctor; request carries an
  optional address, else a placeholder is used (payment flow does not depend on it).
- Assumption: saved-card listing is served from our own DB (we store vault ids), which is also the ownership
  record; `Vault.ListCustomerPaymentTokens` is available but not needed.
- Sandbox reconciliation may return empty for just-created payments (reporting lag) — **expected**, not a
  gap; report is correct over a populated range.
- If a card payment returns a browser-approval challenge (`PAYER_ACTION_REQUIRED`/approve link) →
  `PaymentChallengeException` → endpoint 422 "browser approval required; unsupported". Not building an
  approval round-trip (per task). Sandbox Visa test card does not challenge.
