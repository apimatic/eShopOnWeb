# PayPal Server SDK integration plan — eShopOnWeb PublicApi

Add PayPal card payments + saved cards to eShopOnWeb, exposed on `src/PublicApi`. Additive; reuses the
existing `Order`/`OrderItem` model. SDK is the APIMatic-generated **PayPal Server SDK .NET** (root
namespace `PayPalServerSdk`, spec 2.29, `netstandard2.0`), consumed from vendored source.

## 1. Scope & sequence

1. **Vendor the SDK** into `src/PayPalServerSdk/` (copy of the SDK source, no `map/`, no `.git`), opt it out
   of central package management, add to solution, reference from `Infrastructure`.
2. **Config + client + fail-fast** — `PayPalOptions` bound from `PayPal:` section (`ClientId`,
   `ClientSecret`, `Environment`, `Currency`, `BaseUrl`); DI-register `PayPalServerSdkClient` (Oauth2 creds;
   BaseUrl override when set); `ValidateOnStart` fail-fast. Client construction facts: §CLIENT.
2b. **Gateway port** — `IPayPalGateway` in `ApplicationCore` returning app DTOs (no SDK types leak out);
   implemented in `Infrastructure/Services/PayPalGateway.cs` over the SDK. One error-translation boundary.
3. **Domain** — `Payment` aggregate (links `Order` by id; holds PayPal ids/statuses for hold/capture/refund)
   + `PaymentRefund` child + `SavedCard` aggregate (owner = buyer identity). DbSets + EF configs on
   `CatalogContext`.
4. **Application services** — `PaymentService` (place/pay/fulfil/cancel/refund/my-orders/reconcile) with
   per-order serialization + local idempotency; `SavedCardService` (save/list/delete).
5. **Endpoints** (`src/PublicApi`, `MinimalApi.Endpoint` `IEndpoint<>` convention, JWT): orders, pay, fulfil,
   cancel, refunds, my-orders, reconciliation, payment-methods CRUD.
6. **Verify live** on sandbox card 4111…; write verification guide.

Operation → capability map (all `client.{Group}.{Op}`):
- place order → **local only** (no PayPal call; order awaits payment).
- pay/authorize → `Orders.CreateOrder` (intent AUTHORIZE, `payment_source.card`, single-step) → read
  `purchase_units[0].payments.authorizations[0]`; if absent & status APPROVED → `Orders.AuthorizeOrder`.
- fulfil/capture → `Payments.CaptureAuthorizedPayment`; stale-auth renew → `Payments.ReauthorizePayment`;
  status probe → `Payments.GetAuthorizedPayment`.
- cancel → `Payments.VoidPayment`.
- refund → `Payments.RefundCapturedPayment`.
- save card → **two-step** `Vault.CreateSetupToken` (raw card) → `Vault.CreatePaymentToken` (referencing the
  setup token via `payment_source.token{ id, type=SETUP_TOKEN }`). *(Implementation note: the single-step
  `CreatePaymentToken` with a raw card, and any request carrying `customer.merchant_customer_id`, both make
  the sandbox return HTTP 500; the two-step flow with the `customer` object omitted works — PayPal mints and
  returns the customer id, which is stored.)*
- delete card → `Vault.DeletePaymentToken`.
- reconciliation → `TransactionSearch.SearchTransactions` (paginated over the whole range).

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim** — every parameter name is the literal C# identifier;
> named arguments must use them exactly (the cancellation-token parameter is literally `ct`, so `ct:`).
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies (records →
> `PayPalServerSdk.Models`; enums → `PayPalServerSdk.Models.Enums`; typed errors → `PayPalServerSdk.Errors`;
> client/options → `PayPalServerSdk`; `ServerEnvironment` → `PayPalServerSdk.Servers`; `RawError` →
> `PayPalServerSdk.Core.ErrorResponse`; `SdkException<>` → `PayPalServerSdk.Core.Exceptions`).

| Op | Signature (verbatim) | Request model + fields used | Response fields read | Error case + accessors | Source |
| --- | --- | --- | --- | --- | --- |
| `Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderRequest{ Intent(intent):CheckoutPaymentIntent req; PurchaseUnits(purchase_units):IReadOnlyList<PurchaseUnitRequest> req; PaymentSource(payment_source):PaymentSource? }`; `PurchaseUnitRequest{ Amount(amount):AmountWithBreakdown req; CustomId(custom_id):string?; InvoiceId(invoice_id):string?; Description(description):string? }`; `AmountWithBreakdown{ CurrencyCode(currency_code):string req; Value(value):string req }`; `PaymentSource{ Card(card):CardRequest? }`; `CardRequest{ Number(number); Expiry(expiry); SecurityCode(security_code); Name(name); BillingAddress(billing_address):Address?; VaultId(vault_id):string? }`; `Address{ CountryCode(country_code):string req; AddressLine1; AdminArea1/2; PostalCode }` | `Order{ Id(id):string?; Status(status):OrderStatus?; PurchaseUnits(purchase_units):IReadOnlyList<PurchaseUnit>?; Links(links):IReadOnlyList<LinkDescription>? }`; `PurchaseUnit.Payments(payments):PaymentCollection?`; `PaymentCollection.Authorizations:IReadOnlyList<AuthorizationWithAdditionalData>?`; `AuthorizationWithAdditionalData{ Id; Status:AuthorizationStatus?; Amount:Money?; ExpirationTime(expiration_time):string? }`; `LinkDescription{ Rel(rel):string req; Href }` | A: `SdkException<CreateOrderError>` — `TryGetError(out Error)`[400,401,422] · `TryGetRawError(out RawError)` | Orders.md; Models/OrderRequest.cs, PurchaseUnitRequest.cs, AmountWithBreakdown.cs, PaymentSource.cs, CardRequest.cs, Address.cs, Order.cs, PurchaseUnit.cs, PaymentCollection.cs, AuthorizationWithAdditionalData.cs, LinkDescription.cs |
| `Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | body may be `null` (card already on the order); fallback path only | `OrderAuthorizeResponse{ Id; Status:OrderStatus?; PurchaseUnits:IReadOnlyList<PurchaseUnit>? }` → same `payments.authorizations` walk | A: `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)`[400,401,403,404,422,500] · `TryGetRawError` | Orders.md; Models/OrderAuthorizeResponse.cs |
| `Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentAuthorization{ Id; Status:AuthorizationStatus?; Amount:Money?; ExpirationTime:string? }` | A: `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)`[401,403,404] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` | Payments.md; Models/PaymentAuthorization.cs |
| `Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CaptureRequest{ Amount(amount):Money?; FinalCapture(final_capture):bool?; InvoiceId(invoice_id):string? }`; `Money{ CurrencyCode req; Value req }` | `CapturedPayment{ Id; Status:CaptureStatus?; Amount:Money?; SellerReceivableBreakdown(seller_receivable_breakdown):SellerReceivableBreakdown? }`; `SellerReceivableBreakdown{ GrossAmount(gross_amount):Money req; PaypalFee(paypal_fee):Money?; NetAmount(net_amount):Money? }` | A: `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` | Payments.md; Models/CaptureRequest.cs, Money.cs, CapturedPayment.cs, SellerReceivableBreakdown.cs |
| `Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ReauthorizeRequest{ Amount(amount):Money? }` | `PaymentAuthorization{ Id; Status; ExpirationTime }` (new auth id) | A: `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)`[400,401,403,404,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; Models/ReauthorizeRequest.cs |
| `Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentAuthorization{ Status }` | A: `SdkException<VoidPaymentError>` — `TryGetError(out Error)`[401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md |
| `Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `RefundRequest{ Amount(amount):Money?; InvoiceId; CustomId; NoteToPayer }` (Amount null ⇒ full remaining) | `Refund{ Id; Status:RefundStatus?; Amount:Money? }` | A: `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; Models/RefundRequest.cs, Refund.cs |
| `Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenRequest{ Customer(customer):Customer?; PaymentSource(payment_source):PaymentTokenRequestPaymentSource req }`; `PaymentTokenRequestPaymentSource{ Card(card):PaymentTokenRequestCard? }`; `PaymentTokenRequestCard{ Number; Expiry; SecurityCode; Name; BillingAddress:Address? }`; `Customer{ MerchantCustomerId(merchant_customer_id):string? }` | `PaymentTokenResponse{ Id; Customer(customer):CustomerResponse?; PaymentSource:PaymentTokenResponsePaymentSource? }`; `PaymentTokenResponsePaymentSource.Card:CardPaymentTokenEntity?`; `CardPaymentTokenEntity{ LastDigits(last_digits); Brand:CardBrand?; Expiry; Name }` | A: `SdkException<CreatePaymentTokenError>` — `TryGetError(out Error)`[400,403,404,422,500] · `TryGetRawError` | Vault.md; Models/PaymentTokenRequest.cs, PaymentTokenRequestPaymentSource.cs, PaymentTokenRequestCard.cs, Customer.cs, PaymentTokenResponse.cs, PaymentTokenResponsePaymentSource.cs, CardPaymentTokenEntity.cs |
| `Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `void` | A: `SdkException<DeletePaymentTokenError>` — `TryGetError(out Error)`[400,403,500] · `TryGetRawError` | Vault.md |
| `TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` | pass the 8 nullable params **explicitly null**; call with named args | `SearchResponse{ TransactionDetails(transaction_details):IReadOnlyList<TransactionDetails>?; Page:int?; TotalItems:int?; TotalPages:int? }`; `TransactionDetails.TransactionInfo:TransactionInformation?`; `TransactionInformation{ TransactionId; InvoiceId; TransactionAmount:Money?; FeeAmount:Money?; TransactionStatus:string?; TransactionInitiationDate:string? }` | **Case B**: `SdkException<RawError>` (`StatusCode`, `ReadAsString()`, `ReadAsJson<DefaultError>()`) | TransactionSearch.md; Models/SearchResponse.cs, TransactionDetails.cs, TransactionInformation.cs |

Typed error payload — `Models/Error.cs`: `Name(name) req`, `Message(message) req`, `DebugId(debug_id) req`,
`Details(details):IReadOnlyList<ErrorDetails>?`. `ErrorDetails{ Issue(issue) req; Description; Field }`.
`debug_id` is PayPal's correlation id → log it.

**Enums** (`Models/Enums/`, `StringEnum<T>` — build with `.FromValue("WIRE")` or the static members; compare by value):
- `CheckoutPaymentIntent`: `Authorize`="AUTHORIZE", `Capture`="CAPTURE".
- `OrderStatus`: Created, Saved, Approved, Voided, Completed, PayerActionRequired.
- `AuthorizationStatus`: Created, Captured, Denied, PartiallyCaptured, Voided, Pending. **No `Expired` member** — a stale auth surfaces as the wire value `"EXPIRED"` (open enum) or via `ExpirationTime` in the past; detect by `ExpirationTime`/capture-error, not by a member.
- `CaptureStatus`: Completed, Declined, PartiallyRefunded, Pending, Refunded, Failed.
- `RefundStatus`: Cancelled, Failed, Pending, Completed.
- `TokenType`: only `BillingAgreement`="BILLING_AGREEMENT" → **not** for vaulted cards; a saved card is reused via `CardRequest.VaultId`, not `PaymentSource.Token`.
- `CardBrand` (Models/Enums/CardBrand.cs) — read `.Value` for display.

### §CLIENT — construction / auth / server
- Options: `PayPalServerSdk.PayPalServerSdkClientOptions { Oauth2 = new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials { ClientId, ClientSecret }, Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox }`. (Source: sdk-map.md *Getting a client*, ServiceCollectionExtensions.cs.)
- BaseUrl override: `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>` **when set**. Confirmed to also cover the token request: `AuthSchemes.cs` builds the token URL via `server.Default("/v1/oauth2/token")`, and `Server.Default` resolves through `options.Server.Default.Sandbox.BaseUrl` (DefaultOptions.cs). So one override covers token + every API call. (Source: AuthSchemes.cs, Server.cs, Servers/DefaultOptions.cs.)
- Only `ServerEnvironment.Sandbox` exists (Servers/ServerEnvironment.cs) — see PRODUCTION READINESS §8.
- DI: register over a **named** `HttpClient` (own timeout + `PooledConnectionLifetime`), construct the client in a singleton factory, set `options.Logging.LoggerFactory` **explicitly** (see trap notes). Not `AddPayPalServerSdkClient` — we need the named-client control and explicit logging posture.

## 3. Trap notes (hazard + skill pointer — not resolved here)

- **Client lifetime / token-cache / DNS**: one long-lived client vs per-request; singleton + `IHttpClientFactory` handler-rotation interaction. → **MUST load `paypal-platforms-team:dotnet-client-initialization`**.
- **Credential absence is silent**: an unset/blank Oauth2 credential sends an unauthenticated request that 401s a round-trip later, not at startup. → **MUST load `paypal-platforms-team:dotnet-authentication`**.
- **Timeout is per-attempt, not a call budget; POST/PATCH/DELETE are never SDK-resent while PUT is; a hung GET costs a multiple of Timeout**: bounding pay/capture/refund and the reconciliation page loop. → **MUST load `paypal-platforms-team:dotnet-configuration-resilience`**.
- **Pagination has no built-in stop guarantee**: the reconciliation loop over `TotalPages` needs a provider-independent bound. → **MUST load `paypal-platforms-team:dotnet-configuration-resilience`**.
- **Building request models**: `required` init members, `StringEnum` (not C# enum) via `.FromValue`, `Money.Value` is a **string**, wire names ≠ C# names. → **MUST load `paypal-platforms-team:dotnet-models`**.
- **Error boundary mechanics**: Case A `TryGet…` vs Case B `RawError`; JSON drift on a 2xx and a non-matching non-2xx body both surface as `JsonException`. → **MUST load `paypal-platforms-team:dotnet-error-handling`**.

## 4. REQUIRED READING (load all before implementing; contents deliberately not copied here)

- `paypal-platforms-team:dotnet-client-initialization` — client construction & DI lifetime (step 2/2b).
- `paypal-platforms-team:dotnet-authentication` — Oauth2 credentials + startup fail-fast (step 2).
- `paypal-platforms-team:dotnet-models` — building every request payload (steps 2b, 4).
- `paypal-platforms-team:dotnet-calling-endpoints` — first calls; named args for `SearchTransactions` (step 2b).
- `paypal-platforms-team:dotnet-error-handling` — the one error-translation boundary in `PayPalGateway` (step 2b).
- `paypal-platforms-team:dotnet-configuration-resilience` — timeouts/retries/base-URL, reconciliation paging (steps 2, 2b, 4).

⚠ Two hazard rows that always apply (both throw `System.Text.Json.JsonException`, opposite handling): a
drifted/malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization,
**not** `SdkException`, so an SDK-exception-only catch ladder lets it escape; a **non-2xx** body that does
not match the operation's generated `{Operation}Error` shape throws `JsonException` **while the error object
is constructed**, replacing the `SdkException` and destroying the HTTP status. The gateway boundary catches
`JsonException` alongside `SdkException<>`.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` bound from `PayPal:` with `[Required]` on `ClientId`,`ClientSecret`,`Environment`,`Currency`; `AddOptions().Bind().ValidateDataAnnotations().ValidateOnStart()`. Both Oauth2 halves checked (blank ≠ missing). Host refuses to boot on any blank. |
| 2 | Secret sourcing & rotation | Values from **.NET user-secrets** (loaded by me from env vars `PAYPAL_CLIENT_*`), never in repo files. Client built once in a singleton factory → a rotated secret needs a process restart; documented, acceptable for this app. |
| 3 | Total timeout budget | Named `HttpClient.Timeout = 30s` (per-attempt hard bound on a hang, any verb). `options.Retry.Timeout = 20s`. Every gateway call wrapped by a per-request `CancellationTokenSource` deadline (`45s`) linked to `HttpContext.RequestAborted` — the only whole-call bound. |
| 4 | Write-retry ownership | All PayPal writes here are `POST`/`DELETE` → SDK never resends them (default `HttpMethodsToRetry` = GET,HEAD,PUT,OPTIONS). No PUT in scope. So SDK-side duplicate resend is a non-issue; duplicates are guarded application-side (§5.5). |
| 5 | Idempotency & ambiguous writes | Authorize/capture/void carry a **deterministic** `PayPal-Request-Id` derived from the payment's **GUID invoice id** (`{InvoiceId}-{authorize\|capture\|void}`) — deterministic per order (double-click safe) yet unique across runs, so the in-memory DB resetting order numbering never collides with a prior run's request-ids in PayPal's ~6h window. *(Corrected during implementation: an `eshop-order-{id}` scheme collided across runs and PayPal rejected/replayed it.)* Refund carries the **caller-supplied** idempotency key as `PayPal-Request-Id`. Primary guard is application-side: a per-order `SemaphoreSlim` serializes pay/fulfil/cancel/refund; local state (Payment status, stored refund-key→id map) short-circuits repeats **before** any PayPal call. Transport-failure ambiguity on a write → reconcile via provider state, not blind retry. A successful `VoidPayment` returns HTTP 204 (empty body) that the SDK's `PaymentAuthorization` return type cannot deserialize; a per-call `SdkHook.OnResponse` captures the status so a 2xx-empty is treated as success, not a parse failure. |
| 6 | Observability | Built-in SDK logger via explicit `LoggerFactory` at host level: request/response lines at Information, retries at Warning, failures at Error. Gateway logs PayPal `debug_id` from `Error`/`RawError` on every failure for correlation. `LogRequestBody` stays **off**. |
| 7 | Sensitive data | Card PAN/CVC flow through `CardRequest`/`PaymentTokenRequestCard` (fields `number`,`security_code`). Therefore: `LogRequestBody=false`, `LoggerFactory` **assigned explicitly** (disarms the `PAYPALSERVERSDKCLIENT_LOG` env var), and our own code never logs request DTOs or card fields. PAN/CVC never persisted — only PayPal ids, brand, last4, expiry are stored. |
| 8 | Environment selection | SDK ships **only** `ServerEnvironment.Sandbox`. We always select Sandbox and route the real host via `PayPal:BaseUrl`. Fail-fast rule: if `PayPal:Environment` is not `sandbox`/blank **and** `PayPal:BaseUrl` is blank → throw at startup (refuse to silently send non-sandbox traffic to the sandbox host). When `BaseUrl` is set it is used verbatim for token + all calls. Dev/test sets `PayPal:Environment=sandbox`, `BaseUrl` unset → sandbox default. |

## 6. Assumptions & Blockers

- **Authorize model**: for a direct card the plan uses **single-step** `CreateOrder(intent=AUTHORIZE, payment_source.card, prefer="return=representation", PayPal-Request-Id)` and reads the authorization from `purchase_units[].payments.authorizations[]`; `AuthorizeOrder` is a fallback only if that array is empty and status is `APPROVED`. Grounded in the `AuthorizeOrder`/`CreateOrder` `<remarks>` ("a valid payment_source must be provided in the request"; PayPal-Request-Id "mandatory for all single-step create order calls … with payment source information like Card"). If PayPal instead returns a `payer-action`/approval link with no authorization (3DS challenge), the gateway **STOPs and reports** an operator-actionable error — no browser round-trip is built (per task).
- **Refund authorization scope**: task's binding rule — "Fulfil, cancel and reconciliation are operator actions … Every other endpoint is shopper-scoped and acts only on the caller's own data" — puts **refunds on the shopper endpoint**, acting on the caller's own order. Implemented that way.
- **Saved-card owner id**: the buyer identity = JWT `ClaimTypes.Name` (the only stable subject the token carries; the existing app already uses the user identity string as `Order.BuyerId`). `Customer.merchant_customer_id` on vault requests carries it for traceability.
- No blockers: every capability the task needs maps to an SDK operation above.

## 7. Source labels — every §2 row cites its map page / declaring file. `YOUR CALL — not in the map`
(application design, decided against the task): Payment/SavedCard persistence & state machine, the per-order
`SemaphoreSlim` concurrency guard, refund-key dedup store, reconciliation matching (by `invoice_id`),
authorize single-step-vs-two-step choice, and the JWT-identity → buyer mapping.
