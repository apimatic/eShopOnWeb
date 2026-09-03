# PayPal payments + saved cards — eShopOnWeb PublicApi

## 1. Scope & sequence

1. **SDK + host wiring** — vendor the unpublished SDK as a solution project; bind `PayPal:*`; construct `PayPal.PayPalClient`; fail-fast on blank credentials.
2. **Domain + persistence** — reuse `Order`/`OrderItem`; add payment/fulfilment state and refund rows on the existing order aggregate; new `SavedPaymentMethod` aggregate. No parallel order model.
3. **Place order** — `POST /api/orders` from catalog ids/qty; status `AwaitingPayment`. No PayPal call.
4. **Pay (authorize/hold)** — `POST /api/orders/{orderId}/pay` → `client.Orders.CreateOrder` (`intent=AUTHORIZE`, card or `vault_id`). If the create response has no authorization, `client.Orders.AuthorizeOrder`. Persist PayPal order/authorization ids + status. Stop (do not build a browser round-trip) if status is `PAYER_ACTION_REQUIRED`.
5. **Fulfil (capture)** — operator `POST /api/orders/{orderId}/fulfil` → `client.Payments.GetAuthorizedPayment`; if the honor period is stale, `client.Payments.ReauthorizePayment`; then `client.Payments.CaptureAuthorizedPayment`. Persist captured amount, PayPal fee, net. If reauthorize is no longer possible, return an operator-actionable error.
6. **Cancel (void)** — operator `POST /api/orders/{orderId}/cancel` → `client.Payments.VoidPayment`.
7. **Refund** — shopper `POST /api/orders/{orderId}/refunds` → `client.Payments.RefundCapturedPayment` with caller `payPalRequestId`. Cap remaining refundable at captured − already refunded.
8. **Saved cards** — `POST /api/payment-methods` → `client.Vault.CreatePaymentToken`; `GET` from local store; `DELETE /api/payment-methods/{id}` → `client.Vault.DeletePaymentToken`.
9. **My orders** — `GET /api/my-orders` from local orders + payment state.
10. **Reconciliation** — operator `GET /api/reconciliation` → `client.TransactionSearch.SearchTransactions`, walk every page, match to eShop PayPal ids / invoice ids.
11. **Tests + live sandbox verification** on Visa `4111111111111111`.

---

## 2. CONTRACT SHEET

Signatures are generated code, verbatim. Every parameter name is the literal C# identifier (the cancellation-token parameter is named `ct`, so named arguments write `ct:`).

Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

### Operations

| # | Controller | Method signature | Request + fields used | Response envelope + fields read | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `Orders` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 5 params `payPalMockResponse`…`payPalAuthAssertion` must be passed explicitly | **body `PayPal.Models.OrderRequest`**: `Intent` (`intent`) **required** `PayPal.Models.Enums.CheckoutPaymentIntent`; `PurchaseUnits` (`purchase_units`) **required** `IReadOnlyList<PayPal.Models.PurchaseUnitRequest>`; `PaymentSource` (`payment_source`) optional `PayPal.Models.PaymentSource`. **`PurchaseUnitRequest`**: `Amount` (`amount`) **required** `PayPal.Models.AmountWithBreakdown`; `InvoiceId` (`invoice_id`) optional; `CustomId` (`custom_id`) optional. **`AmountWithBreakdown`**: `CurrencyCode` (`currency_code`) **required** string; `Value` (`value`) **required** string. **`PaymentSource`**: `Card` (`card`) `PayPal.Models.CardRequest`. **`CardRequest`**: one-off: `Number` (`number`), `Expiry` (`expiry` YYYY-MM), `SecurityCode` (`security_code`), `Name` (`name`), `BillingAddress` (`billing_address`) `PayPal.Models.Address`; saved-card: `VaultId` (`vault_id`); saved-card also sets `StoredCredential` (`stored_credential`) `PayPal.Models.CardStoredCredential` with `PaymentInitiator` (`payment_initiator`) **required**, `PaymentType` (`payment_type`) **required**, `Usage` (`usage`). **`Address`**: `CountryCode` (`country_code`) **required**; `AddressLine1`, `AdminArea1`, `AdminArea2`, `PostalCode` optional. Left out of OrderRequest: `ProcessingInstruction`, `Payer`, `ApplicationContext`. **prefer**: `"return=representation"`. **payPalRequestId**: stable per eShop pay attempt (mandatory for single-step create with payment_source). Pass null for unused header params. | `PayPal.Models.Order`: `Id` (`id`); `Status` (`status`) `PayPal.Models.Enums.OrderStatus`; `PurchaseUnits[].Payments.Authorizations[]` → `Id`, `Status`, `Amount`, `ExpirationTime`; `Links` (`rel` = `payer-action`). | Case A `PayPal.Core.Exceptions.SdkException<PayPal.Errors.CreateOrderError>`: `TryGetError(out PayPal.Models.Error)` [400, 401, 422]; `TryGetRawError(out PayPal.Core.ErrorResponse.RawError)` fallback. `Error`: `Name`, `Message`, `DebugId` required; `Details[].Issue`, `Description`. | none (default) | `map/operations/Orders.md`; `Api/Orders.cs`; `Models/OrderRequest.cs`; `Models/Order.cs`; `Models/PurchaseUnitRequest.cs`; `Models/AmountWithBreakdown.cs`; `Models/PaymentSource.cs`; `Models/CardRequest.cs`; `Models/Address.cs`; `Models/CardStoredCredential.cs`; `Models/PurchaseUnit.cs`; `Models/PaymentCollection.cs`; `Models/AuthorizationWithAdditionalData.cs`; `Models/Error.cs`; `Errors/CreateOrderError.cs` |
| 2 | `Orders` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `id` = PayPal order id. `body` null when payment_source already on the order. `prefer` `"return=representation"`. `payPalRequestId` same key family as create. | `PayPal.Models.OrderAuthorizeResponse`: same payment fields as Order (`Id`, `Status`, `PurchaseUnits[].Payments.Authorizations[]`, `Links`). | Case A `SdkException<PayPal.Errors.AuthorizeOrderError>`: `TryGetError(out Error)` [400, 401, 403, 404, 422, 500]; `TryGetRawError`. | none | `map/operations/Orders.md`; `Api/Orders.cs`; `Models/OrderAuthorizeRequest.cs`; `Models/OrderAuthorizeResponse.cs` |
| 3 | `Orders` | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `fields` pass `"payment_source"` when needed; others null. | `PayPal.Models.Order` as row 1. | Case A `SdkException<PayPal.Errors.GetOrderError>`: `TryGetError` [401, 404]; `TryGetRawError`. | none | `map/operations/Orders.md` |
| 4 | `Payments` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `authorizationId` stored on eShop order. | `PayPal.Models.PaymentAuthorization`: `Id`, `Status` (`PayPal.Models.Enums.AuthorizationStatus`), `ExpirationTime`, `Amount`. | Case A `SdkException<PayPal.Errors.GetAuthorizedPaymentError>`: `TryGetError` [401, 403, 404]; `TryGetNoContent(out RawError)` [500]; `TryGetRawError`. | none | `map/operations/Payments.md`; `Models/PaymentAuthorization.cs` |
| 5 | `Payments` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | **body `PayPal.Models.ReauthorizeRequest`**: `Amount` (`amount`) optional `PayPal.Models.Money` (currency_code + value required on Money). `prefer` `"return=representation"`. | `PayPal.Models.PaymentAuthorization`: new `Id`, `Status`, `ExpirationTime` — **replace** stored authorization id. | Case A `SdkException<PayPal.Errors.ReauthorizePaymentError>`: `TryGetError` [400, 401, 403, 404, 422]; `TryGetNoContent` [500]; `TryGetRawError`. | none | `map/operations/Payments.md`; `Api/Payments.cs`; `Models/ReauthorizeRequest.cs`; `Models/Money.cs` |
| 6 | `Payments` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | **body `PayPal.Models.CaptureRequest`**: `Amount` optional (omit = full remaining); `FinalCapture` (`final_capture`) `true`; `InvoiceId` optional. `prefer` `"return=representation"`. `payPalRequestId` stable per eShop fulfil. | `PayPal.Models.CapturedPayment`: `Id`, `Status` (`PayPal.Models.Enums.CaptureStatus`), `Amount`; `SellerReceivableBreakdown.GrossAmount`, `PaypalFee`, `NetAmount`. | Case A `SdkException<PayPal.Errors.CaptureAuthorizedPaymentError>`: `TryGetError` [400, 401, 403, 404, 409, 422]; `TryGetNoContent` [500]; `TryGetRawError`. | none | `map/operations/Payments.md`; `Models/CaptureRequest.cs`; `Models/CapturedPayment.cs`; `Models/SellerReceivableBreakdown.cs` |
| 7 | `Payments` | `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` | used if capture response omits breakdown | `PayPal.Models.CapturedPayment` as row 6. | Case A `SdkException<PayPal.Errors.GetCapturedPaymentError>`: `TryGetError` [401, 403, 404]; `TryGetNoContent` [500]; `TryGetRawError`. | none | `map/operations/Payments.md` |
| 8 | `Payments` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `payPalRequestId` stable per eShop cancel. `prefer` `"return=representation"`. | `PayPal.Models.PaymentAuthorization`: `Id`, `Status` (expect `VOIDED`). | Case A `SdkException<PayPal.Errors.VoidPaymentError>`: `TryGetError` [401, 403, 404, 409, 422]; `TryGetNoContent` [500]; `TryGetRawError`. | none | `map/operations/Payments.md`; `Api/Payments.cs` |
| 9 | `Payments` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | **body `PayPal.Models.RefundRequest`**: omit `Amount` for full refund; set `Amount` (`Money`) for partial. Left out: `CustomId`, `InvoiceId`, `NoteToPayer`, `PaymentInstruction`. **`payPalRequestId`**: caller-supplied refund idempotency key (server stores 45 days). `prefer` `"return=representation"`. | `PayPal.Models.Refund`: `Id` (returned as `refundId`), `Status` (`PayPal.Models.Enums.RefundStatus`), `Amount`. | Case A `SdkException<PayPal.Errors.RefundCapturedPaymentError>`: `TryGetError` [400, 401, 403, 404, 409, 422]; `TryGetNoContent` [500]; `TryGetRawError`. | none | `map/operations/Payments.md`; `Api/Payments.cs`; `Models/RefundRequest.cs`; `Models/Refund.cs` |
| 10 | `Vault` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | **body `PayPal.Models.PaymentTokenRequest`**: `PaymentSource` **required** `PayPal.Models.PaymentTokenRequestPaymentSource`; `Customer` optional `PayPal.Models.Customer`. **PaymentSource.Card** `PayPal.Models.PaymentTokenRequestCard`: `Number`, `Expiry`, `SecurityCode`, `Name`, `BillingAddress`. **Customer**: `MerchantCustomerId` (`merchant_customer_id`) — shopper username (fits regex, max 64). Left out: `Token` on payment source. `payPalRequestId` optional but set (3-hour store). | `PayPal.Models.PaymentTokenResponse`: `Id` (vault id, returned as `paymentMethodId`); `Customer.Id`; `PaymentSource.Card` `PayPal.Models.CardPaymentTokenEntity`: `LastDigits`, `Brand`, `Expiry`, `Name`. Never persist PAN/CVV. | Case A `SdkException<PayPal.Errors.CreatePaymentTokenError>`: `TryGetError` [400, 403, 404, 422, 500]; `TryGetRawError`. | none | `map/operations/Vault.md`; `Api/Vault.cs`; `Models/PaymentTokenRequest.cs`; `Models/PaymentTokenRequestPaymentSource.cs`; `Models/PaymentTokenRequestCard.cs`; `Models/Customer.cs`; `Models/PaymentTokenResponse.cs`; `Models/PaymentTokenResponsePaymentSource.cs`; `Models/CardPaymentTokenEntity.cs` |
| 11 | `Vault` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `id` = vault token id. | `void` | Case A `SdkException<PayPal.Errors.DeletePaymentTokenError>`: `TryGetError` [400, 403, 500]; `TryGetRawError`. | none | `map/operations/Vault.md` |
| 12 | `TransactionSearch` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 params `transactionId`…`terminalId` must be passed explicitly | `startDate`/`endDate` RFC3339 (seconds required). Pass `fields: "transaction_info"`, `pageSize: 100`, `page` starting at 1; all unused filters `null`. `endDate` max 31 days after `startDate` — split longer ranges. | `PayPal.Models.SearchResponse`: `TransactionDetails[]` → `TransactionInfo.TransactionId`, `PaypalReferenceId`, `InvoiceId`, `CustomField`, `TransactionAmount`, `FeeAmount`, `TransactionStatus`, `TransactionInitiationDate`; `TotalPages`, `Page`, `TotalItems`. | **Case B** `SdkException<PayPal.Core.ErrorResponse.RawError>`: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. | not a Pageable type — **manual page loop** on `page`/`total_pages` until the range is exhausted | `map/operations/TransactionSearch.md`; `Api/TransactionSearch.cs`; `Models/SearchResponse.cs`; `Models/TransactionDetails.cs`; `Models/TransactionInformation.cs` |

### Enums actually used

| Type | Members used (C# = wire) | Source |
| --- | --- | --- |
| `PayPal.Models.Enums.CheckoutPaymentIntent` | `Authorize` = `AUTHORIZE` (not `Capture`) | `Models/Enums/CheckoutPaymentIntent.cs` |
| `PayPal.Models.Enums.OrderStatus` | `Created`=`CREATED`, `Approved`=`APPROVED`, `Completed`=`COMPLETED`, `Voided`=`VOIDED`, `PayerActionRequired`=`PAYER_ACTION_REQUIRED` | `Models/Enums/OrderStatus.cs` |
| `PayPal.Models.Enums.AuthorizationStatus` | `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending` | `Models/Enums/AuthorizationStatus.cs` |
| `PayPal.Models.Enums.CaptureStatus` | `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed` | `Models/Enums/CaptureStatus.cs` |
| `PayPal.Models.Enums.RefundStatus` | `Cancelled`, `Failed`, `Pending`, `Completed` | `Models/Enums/RefundStatus.cs` |
| `PayPal.Models.Enums.PaymentInitiator` | `Customer`=`CUSTOMER` | `Models/Enums/PaymentInitiator.cs` |
| `PayPal.Models.Enums.StoredPaymentSourcePaymentType` | `OneTime`=`ONE_TIME` | `Models/Enums/StoredPaymentSourcePaymentType.cs` |
| `PayPal.Models.Enums.StoredPaymentSourceUsageType` | `Subsequent`=`SUBSEQUENT` on vault_id pay | `Models/Enums/StoredPaymentSourceUsageType.cs` |
| `PayPal.Models.Enums.CardBrand` | read `Value` / `ToString()` for display (Visa=`VISA`, …) | `Models/Enums/CardBrand.cs` |
| `PayPal.Servers.ServerEnvironment` | **only** `Production` = `"production"` (hosting text: PayPal Sandbox; default base `https://api-m.sandbox.paypal.com`) | `Servers/ServerEnvironment.cs`; `sdk-map.md` Servers & auth |
| `PayPal.Core.Enum.TypedEnum<string,T>.Value` | wire string on every StringEnum | `Core/Enum/TypedEnum.cs` |

### Client construction / auth / server

| Fact | Detail | Source |
| --- | --- | --- |
| Client | `PayPal.PayPalClient(HttpClient httpClient, PayPal.PayPalClientOptions options)` only constructor | `sdk-map.md`; `PayPalClient.cs` |
| DI | `PayPal.ServiceCollectionExtensions.AddPayPalClient(Action<PayPalClientOptions>?)` | `ServiceCollectionExtensions.cs` |
| Auth | `options.Oauth2 = new PayPal.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials { ClientId, ClientSecret }` (both `required`) | `sdk-map.md`; `OAuth2ClientCredentials.cs` |
| Token URL | `server.Default("/v1/oauth2/token")` — **same Default group as API calls**, so `options.Server.Default.Production.BaseUrl` applies to the token request | `AuthSchemes.cs`; `Servers/DefaultOptions.cs` |
| Base URL override | `options.Server.Default.Production.BaseUrl` (default `"https://api-m.sandbox.paypal.com"`) | `ServerOptions.cs`; `Servers/DefaultOptions.cs`; `sdk-map.md` |
| Groups in scope | **Default only** (Orders, Payments, Vault, TransactionSearch all use `_server.Default(...)`) | `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`, `Api/TransactionSearch.cs` |
| Retry defaults | `HttpMethodsToRetry` = GET, HEAD, PUT, OPTIONS; `MaxRetries`=3; `Timeout`=100s per attempt | `Core/Configuration/RetryOptions.cs` |
| Logging | `PayPal.Core.Configuration.LoggingOptions`; `LogRequestBody` default false; `LoggerFactory`; form deny-list `RedactedKeys` does **not** include `number`/`security_code` | `Core/Configuration/LoggingOptions.cs` |
| Root namespace | `PayPal` (not `Paypal`) | `sdk-map.md` |
| No-throw variants | absent | `sdk-map.md` |
| Injected `Idempotency-Key` | `Guid.NewGuid()` on every non-GET — **not** a real key | `Api/Orders.cs` et al. |
| Real keys | `payPalRequestId` → header `PayPal-Request-Id` on CreateOrder (6h, **mandatory** with payment_source), AuthorizeOrder, CaptureAuthorizedPayment (45d), ReauthorizePayment (45d), VoidPayment (45d), RefundCapturedPayment (45d), CreatePaymentToken (3h) | XML `<param>` on those methods |
| Prefer | `"return=minimal"` default — we pass `"return=representation"` on writes that must return ids/amounts | `Api/Orders.cs`, `Api/Payments.cs` |
| 3DS / browser | `OrderStatus.PayerActionRequired` + `links.rel=payer-action` → **STOP and report**; do not implement approval | `Models/Enums/OrderStatus.cs`; task mandate |
| TokenType | only `BILLING_AGREEMENT` — vaulted card pay uses `CardRequest.VaultId`, **not** `PaymentSource.Token` | `Models/Enums/TokenType.cs`; `Models/CardRequest.cs` |

### Application contracts (not SDK)

| Decision | Label |
| --- | --- |
| Bind `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, `PayPal:BaseUrl` from config/user-secrets/env; never hard-code values | YOUR CALL — not in the map |
| Map `PayPal:Environment` to the only SDK member `ServerEnvironment.Production`; when `PayPal:BaseUrl` is non-blank, set it verbatim on `options.Server.Default.Production.BaseUrl` | YOUR CALL — not in the map |
| Amount string: two-decimal invariant (`0.00`) for the configured ISO currency (sandbox card is USD-class) | YOUR CALL — not in the map |
| `custom_id` = eShop `Order.Id` string; `invoice_id` = `eShop-{orderId}-{guid}` so PayPal's merchant-level unique-invoice setting cannot reject in-memory order ids that restart at 1. Reconciliation matches PayPal ids first, then invoice / custom_field. | YOUR CALL — not in the map |
| PayPal-Request-Id for pay/fulfil/cancel/reauth includes `OrderDate.UtcTicks` so in-memory id reuse cannot replay a 6h/45d cached sandbox result from a previous process | YOUR CALL — not in the map |
| Honor-period stale = `ExpirationTime` in the past **or** capture/reauth error indicating expired auth → reauthorize; if original auth age ≥ 30 days or reauth fails as unrenewable, operator error (do not fail silently) | `CheckoutPaymentIntent` / `ReauthorizeRequest` remarks + YOUR CALL for the 30-day cutoff message |
| eShop payment states: `AwaitingPayment` → `Authorized` → `Fulfilled` / `Cancelled`; after capture `PartiallyRefunded` / `Refunded` | YOUR CALL — not in the map |
| Refund remaining = captured amount − sum of successful refunds; reject over-refund | YOUR CALL — not in the map |
| Shopper identity = JWT `ClaimTypes.Name`; admin role `BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS` | YOUR CALL — not in the map (app code) |
| Idempotent pay/fulfil/cancel: if already in terminal state for that action, return stored result without a second PayPal money-movement | YOUR CALL — not in the map |
| GET payment-methods from local rows (PayPal-safe descriptors only); ownership by `BuyerId` | YOUR CALL — not in the map |
| SearchTransactions `page` treated as 1-based (default `page=1` + remarks example); walk `page = 1..TotalPages` | `Api/TransactionSearch.cs` remarks; UNVERIFIED if `TotalPages` is 0 when empty |
| Empty reconciliation range is a valid sandbox result, not a gap | task |

---

## 3. Trap notes

- Step 1 client/DI: `HttpClient` lifetime vs wrapper lifetime — a per-request `new HttpClient()` exhausts sockets; the SDK constructor takes one client and `AddPayPalClient` captures options at registration. **MUST load paypal-platforms-team/dotnet-client-initialization**
- Step 1 auth: when credentials are applied relative to construction, a late set is ignored; rotating ClientId/Secret after the singleton is built does not take effect. **MUST load paypal-platforms-team/dotnet-authentication**
- Steps 4–10 calls: optional params without C# defaults (`payPalMockResponse` … `body` / filter params) mis-bind if passed positionally; named arguments required. **MUST load paypal-platforms-team/dotnet-calling-endpoints**
- Steps 4–10 models: unions/enums are not `new`'d C# enums; `required` vs optional/`JsonIgnore` and wire names vs C# names. **MUST load paypal-platforms-team/dotnet-models**
- All PayPal catch blocks: Case A vs Case B mix (SearchTransactions is B; rest A); `TryGetRawError` is not a catch-all on typed errors; status on success is not on the return value. **MUST load paypal-platforms-team/dotnet-error-handling**
- Step 1 resilience: which writes the SDK may resend (`HttpMethodsToRetry`); what `Timeout` actually bounds vs a caller `CancellationToken`; `LogRequestBody` logs JSON unredacted; `PAYPALCLIENT_LOG` can force body logging if `LoggerFactory` is unset. Card PAN/CVV are on CreateOrder/CreatePaymentToken request models. **MUST load paypal-platforms-team/dotnet-configuration-resilience**
- Step 11 tests: which seam to fake so tests do not depend on SDK internals. **MUST load paypal-platforms-team/dotnet-testing**

---

## 4. REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
| --- | --- |
| `paypal-platforms-team/dotnet-client-initialization` | Step 1 client & DI |
| `paypal-platforms-team/dotnet-authentication` | Step 1 credentials |
| `paypal-platforms-team/dotnet-calling-endpoints` | Steps 4–10 operation calls |
| `paypal-platforms-team/dotnet-models` | Request/response construction |
| `paypal-platforms-team/dotnet-error-handling` | Every SDK try/catch / exception-translation |
| `paypal-platforms-team/dotnet-configuration-resilience` | Retries, timeout budget, BaseUrl, logging, pagination loop |
| `paypal-platforms-team/dotnet-testing` | Step 11 tests |

**Always — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException`, so an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so it **replaces** the `SdkException` and the HTTP status is destroyed with it.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind `IOptions<PayPalOptions>` from section `PayPal`. Host startup (`Program.cs`) refuses to start if `ClientId`, `ClientSecret`, or `Currency` is missing/whitespace. All three parts checked independently. `BaseUrl` optional. `Environment` required non-blank (mapped to the only SDK environment). |
| 2 | Secret sourcing & rotation | Secrets from env `PAYPAL_CLIENT_ID` / `PAYPAL_CLIENT_SECRET` loaded into PublicApi **user-secrets** (names only in repo). DI `AddPayPalClient` builds `PayPalClientOptions` **once at registration** and captures them in the singleton — rotation requires process restart. No mid-process rotation. |
| 3 | Total timeout budget | SDK `Retry.Timeout` is per-attempt (default 100s) with up to 3 retries on **retryable methods only**. PublicApi passes `HttpContext.RequestAborted` as `ct`. Write POSTs are not retried by the SDK, so one attempt × 100s unless the caller token fires first. Caller deadline = the request CT. Do not raise `Timeout` without also bounding CT. |
| 4 | Write-retry ownership | SDK may resend GET (GetOrder, GetAuthorizedPayment, GetCapturedPayment, SearchTransactions). SDK will **not** resend POST CreateOrder / AuthorizeOrder / CaptureAuthorizedPayment / ReauthorizePayment / VoidPayment / RefundCapturedPayment / CreatePaymentToken / DeletePaymentToken. Application must not add its own automatic POST retries. |
| 5 | Idempotency & ambiguous writes | CreateOrder/AuthorizeOrder: `payPalRequestId` = `pay-{orderId}` (and local short-circuit if already Authorized). Capture: `payPalRequestId` = `fulfil-{orderId}`. Void: `cancel-{orderId}`. Refund: **caller-supplied** key as `payPalRequestId` (required on our API); also persist key→refundId locally so a replay after the 45-day PayPal window still does not double-refund. CreatePaymentToken: `vault-{buyerId}-{hash of last-4+expiry}` optional; local unique on vault id. DeletePaymentToken: no key — reconcile by GET list / local deleted flag. Ambiguous timeout on a POST: record the PayPal request id and on retry reuse it; if local state is unknown, GetOrder/GetAuthorizedPayment/GetCapturedPayment before issuing a new write. |
| 6 | Observability | Information: operation name, eShop orderId, PayPal order/authorization/capture/refund ids, `Error.DebugId`. Warning/Error: mapped failures. **Never log request JSON** (`LogRequestBody` off). Correlation: PayPal `debug_id` into our logs. |
| 7 | Sensitive data | Scope carries PAN (`number`) and CVC (`security_code`) on CreateOrder and CreatePaymentToken. **`LogRequestBody` stays off** and `Logging.LoggerFactory` is assigned explicitly so `PAYPALCLIENT_LOG` cannot enable bodies. App DB stores only vault id, last digits, brand, expiry, PayPal customer id. App logs never echo card fields. Form bodies: deny-list does not cover PAN — another reason JSON body logging stays off. |
| 8 | Environment selection | One server group `Default`. Only environment member: `ServerEnvironment.Production` → `https://api-m.sandbox.paypal.com`. All in-scope operations use Default. Deployment sets `PayPal:Environment` (sandbox) and optionally `PayPal:BaseUrl` (verbatim override for **every** call including `/v1/oauth2/token`). There is no live/production host in this SDK — test traffic cannot be pointed at live through the enum; a wrong `BaseUrl` would be the only way, so leave it unset unless an explicit override is supplied. |

---

## 6. Assumptions & Blockers

**Assumptions**

- Direct card + vault on this sandbox merchant will complete without `PAYER_ACTION_REQUIRED`. If live traffic returns that status, stop and report (task mandate) — not a planning blocker.
- CreatePaymentToken with raw card (no setup-token/3DS) is accepted on this account. If PayPal requires a setup token + browser, that is the same 3DS stop condition.
- `SearchTransactions` empty for a just-created window is expected lag (task), not a missing capability.
- Refunds are shopper-scoped per the task (only fulfil/cancel/reconciliation are operator).
- PublicApi JWT `ClaimTypes.Name` is the buyer id, matching existing `Order.BuyerId`.
- Shipping address is required by the existing `Order` constructor; the place-order request will include it (sensible US defaults allowed only if the caller omits it — prefer requiring it).

**Blockers**

- None. Vault, authorize, capture, reauthorize, void, refund, and transaction search are all on the map.

---

## 7. Repo conventions (read-only survey)

| Convention | Exemplar to imitate at edit time |
| --- | --- |
| Minimal API endpoint class `IEndpoint<IResult, TRequest, TDep>` + nested request/response | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` |
| Admin JWT | same file, `[Authorize(Roles = …ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` |
| Shopper JWT | `src/PublicApi/CatalogItemEndpoints/CatalogItemGetByIdEndpoint.cs` (add `[Authorize]` + JWT scheme) |
| DDD aggregate, private setters, EF owned types | `src/ApplicationCore/Entities/OrderAggregate/Order.cs`; `src/Infrastructure/Data/Config/OrderConfiguration.cs` |
| `IRepository<T> where T : IAggregateRoot` | `src/Infrastructure/Data/EfRepository.cs` |
| PublicApi DI + JWT | `src/PublicApi/Program.cs` |
| Exception → HTTP | `src/PublicApi/Middleware/ExceptionMiddleware.cs` |
| In-memory vs SQL | `src/Infrastructure/Dependencies.cs` |
| Demo users | `src/Infrastructure/Identity/AppIdentityDbContextSeed.cs` (`demouser@microsoft.com`, `admin@microsoft.com`) |
| Catalog prices | `src/Infrastructure/Data/CatalogContextSeed.cs` |
| Unit test style | `tests/UnitTests/ApplicationCore/Entities/OrderTests/OrderTotal.cs` |
| PublicApi UserSecretsId | `src/PublicApi/PublicApi.csproj` (`d224f77a-49b4-46f1-9f7a-2042c57d915c`) |
| PublicApi HTTPS ports | `src/PublicApi/Properties/launchSettings.json` (`https://localhost:21723`) |
| SDK pin / roll-forward | `global.json` currently `latestFeature` — set `latestMajor` per machine gotcha |
