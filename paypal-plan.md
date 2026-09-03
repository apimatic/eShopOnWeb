# PayPal payments & saved cards — eShopOnWeb

## 1. Scope & sequence

| Step | What | PayPal operations |
| --- | --- | --- |
| 0 | Runtime pin (`global.json` `rollForward: latestMajor`); vendor unpublished SDK as `src/PayPal` (project reference from Infrastructure); bind `PayPal:` settings; fail-fast; register `PayPalClient` | (client construction) |
| 1 | Extend existing `Order` aggregate with payment/fulfilment state + refunds (no parallel order model). New `SavedPaymentMethod` aggregate. EF configs. | — |
| 2 | Place order `POST /api/orders` from catalog ids/qty; status awaiting payment | — |
| 3 | Save / list / delete cards `POST\|GET\|DELETE /api/payment-methods` | `Vault.CreatePaymentToken`, `Vault.DeletePaymentToken` (list from app DB, shopper-scoped) |
| 4 | Pay `POST /api/orders/{orderId}/pay` — authorize (hold), card **or** saved vault id; amount = order total | `Orders.CreateOrder` (intent `AUTHORIZE` + `payment_source`), `Orders.AuthorizeOrder` if hold not yet present; `Orders.GetOrder` as needed |
| 5 | Fulfil `POST /api/orders/{orderId}/fulfil` (admin) — capture; reauthorize if hold stale | `Payments.GetAuthorizedPayment`, `Payments.ReauthorizePayment`, `Payments.CaptureAuthorizedPayment` (`prefer=return=representation`); `Payments.GetCapturedPayment` if fee/net missing |
| 6 | Cancel `POST /api/orders/{orderId}/cancel` (admin) — void hold | `Payments.VoidPayment` |
| 7 | Refund `POST /api/orders/{orderId}/refunds` — full/partial; caller idempotency key | `Payments.RefundCapturedPayment` (`payPalRequestId` = caller key) |
| 8 | `GET /api/my-orders`; `GET /api/reconciliation` (admin) — page through PayPal reporting and match eShop | `TransactionSearch.SearchTransactions` (loop `page` until `totalPages`) |
| 9 | Unit tests (order state machine, refund cap, ownership); `dotnet test`; live sandbox verify via PublicApi | (test seam: `PayPalClient(HttpClient, options)`) |

If a create/authorize/vault response has `OrderStatus.PayerActionRequired` (or a `rel=payer-action` link), **stop and report** — do not build a browser round-trip.

---

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim. Every parameter name is the literal C# identifier (the cancellation-token parameter is named `ct`, so named arguments write `ct:`).

⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

### Client / auth / servers

| Fact | Value | source |
| --- | --- | --- |
| Client | `PayPal.PayPalClient(HttpClient, PayPal.PayPalClientOptions)` only ctor; groups `Orders`, `Payments`, `Vault`, `TransactionSearch` | sdk-map.md; `PayPalClient.cs` |
| Options | `Environment`, `Retry`, `Logging`, `Server`, `Hooks`, `Oauth2`, `Oauth2TokenStrategy` | sdk-map.md; `PayPalClientOptions.cs` |
| Auth | `options.Oauth2 = new PayPal.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials { ClientId, ClientSecret }` | sdk-map.md; `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs` |
| Token URL | `server.Default("/v1/oauth2/token")` — same Default group as every op; BaseUrl override therefore covers token + API | `AuthSchemes.cs`; sdk-map.md Servers & auth |
| Environments | **Only** `PayPal.Servers.ServerEnvironment.Production` (`"production"`) → host `https://api-m.sandbox.paypal.com` | sdk-map.md; `Servers/ServerEnvironment.cs` |
| BaseUrl override | `options.Server.Default.Production.BaseUrl` (type `PayPal.ServerOptions` / `PayPal.Servers.DefaultOptions.ProductionOptions`) | sdk-map.md; `ServerOptions.cs`; `Servers/DefaultOptions.cs` |
| DI | `services.AddPayPalClient(Action<PayPalClientOptions>?)` — options object built **once** in the callback, captured in singleton | `ServiceCollectionExtensions.cs` |
| Default retry | `HttpMethodsToRetry` = GET, HEAD, PUT, OPTIONS; `MaxRetries` = 3; `Timeout` = 100s | `Core/Configuration/RetryOptions.cs` |
| Logging | `PayPal.Core.Configuration.LoggingOptions`; `LogRequestBody`; `LoggerFactory`; `PAYPALCLIENT_LOG` | sdk-map.md; `Core/Configuration/LoggingOptions.cs` |
| Errors | Throw-only. Case A: `PayPal.Core.Exceptions.SdkException<{Op}Error>`; Case B: `SdkException<PayPal.Core.ErrorResponse.RawError>`. No `…Result` variants. | sdk-map.md |
| Typed error payload | `PayPal.Models.Error`: `Name`, `Message`, `DebugId` (all required); optional `Details` / `Links` | `Models/Error.cs` |
| App config keys | `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, `PayPal:BaseUrl` | YOUR CALL — not in the map (task binding keys) |

### Operations

| Controller · method | Signature (verbatim) | Request | Response fields read | Error | Pagination | source |
| --- | --- | --- | --- | --- | --- | --- |
| `Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 5 header params must be passed explicitly (`null` to skip). **`payPalRequestId` is mandatory for single-step create with payment_source (Card / vault_id).** | `PayPal.Models.OrderRequest`: **required** `Intent` (wire `intent`), `PurchaseUnits` (wire `purchase_units`, 1–10). Optional: `PaymentSource` (`payment_source`). Left out: `ProcessingInstruction`, `Payer` (deprecated), `ApplicationContext`. Prefer `"return=representation"`. | `PayPal.Models.Order`: `Id`, `Status`, `PurchaseUnits[].Payments.Authorizations[]` (`Id`,`Status`,`Amount`,`ExpirationTime`), `Links` (`Rel`,`Href`). Stop if `Status == PayerActionRequired`. | Case A `SdkException<PayPal.Errors.CreateOrderError>`: `TryGetError(out PayPal.Models.Error)` [400,401,422] · `TryGetRawError` | none (default) | map/operations/Orders.md; `Api/Orders.cs` remarks; `Models/OrderRequest.cs`; `Models/Order.cs` |
| `Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 5 params must be passed explicitly. Buyer must have approved **or** `payment_source` provided. | `PayPal.Models.OrderAuthorizeRequest`: optional `PaymentSource` (`OrderAuthorizeRequestPaymentSource`: `Card` and/or `Token`). Pass `null` body when source already on the order. Prefer `"return=representation"`. | `PayPal.Models.OrderAuthorizeResponse`: `Id`, `Status`, `PurchaseUnits[].Payments.Authorizations[]` (`Id`,`Status`,`Amount`,`ExpirationTime`) | Case A `SdkException<PayPal.Errors.AuthorizeOrderError>`: `TryGetError` [400,401,403,404,422,500] · `TryGetRawError` | none | map/operations/Orders.md; `Api/Orders.cs`; `Models/OrderAuthorizeRequest.cs`; `Models/OrderAuthorizeResponse.cs`; `Models/PaymentCollection.cs`; `Models/AuthorizationWithAdditionalData.cs` |
| `Orders.GetOrder` | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `fields`, `payPalMockResponse`, `payPalAuthAssertion` must be passed explicitly | none | same `Order` shape as create | Case A `SdkException<PayPal.Errors.GetOrderError>`: `TryGetError` [401,404] · `TryGetRawError` | none | map/operations/Orders.md |
| `Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | none | `PayPal.Models.PaymentAuthorization`: `Id`, `Status`, `Amount`, `ExpirationTime`, `CreateTime` | Case A `SdkException<PayPal.Errors.GetAuthorizedPaymentError>`: `TryGetError` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | none | map/operations/Payments.md; `Models/PaymentAuthorization.cs` |
| `Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 params must be passed explicitly. Honor period 3 days; reauth days 4–29; after 30 days must **create a new authorized payment**, not reauth. Supports only `amount`. Server stores `PayPal-Request-Id` 45 days. | `PayPal.Models.ReauthorizeRequest`: optional `Amount` (`PayPal.Models.Money`: required `CurrencyCode`/`Value`) | `PaymentAuthorization` (new hold id/status/expiration) | Case A `SdkException<PayPal.Errors.ReauthorizePaymentError>`: `TryGetError` [400,401,403,404,422] · `TryGetNoContent` [500] · `TryGetRawError` | none | map/operations/Payments.md; `Api/Payments.cs` remarks; `Models/ReauthorizeRequest.cs` |
| `Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 params must be passed explicitly. `payPalRequestId` stored 45 days. Prefer `"return=representation"` (minimal omits amounts/fees). | `PayPal.Models.CaptureRequest`: optional `Amount`; `FinalCapture` (default false) — set `true`. Left out: `InvoiceId`, `PaymentInstruction`, `NoteToPayer`, `SoftDescriptor`. | `PayPal.Models.CapturedPayment`: `Id`, `Status`, `Amount`, `SellerReceivableBreakdown.GrossAmount` / `PaypalFee` / `NetAmount` | Case A `SdkException<PayPal.Errors.CaptureAuthorizedPaymentError>`: `TryGetError` [400,401,403,404,409,422] · `TryGetNoContent` [500] · `TryGetRawError` | none | map/operations/Payments.md; `Models/CaptureRequest.cs`; `Models/CapturedPayment.cs`; `Models/SellerReceivableBreakdown.cs` |
| `Payments.GetCapturedPayment` | `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` | none | same `CapturedPayment` (fees may appear after pending) | Case A `SdkException<PayPal.Errors.GetCapturedPaymentError>`: `TryGetError` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` | none | map/operations/Payments.md |
| `Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 params must be passed explicitly. Cannot void a fully captured auth. `payPalRequestId` stored 45 days. | none (empty body) | `PaymentAuthorization` (`Status` → Voided) | Case A `SdkException<PayPal.Errors.VoidPaymentError>`: `TryGetError` [401,403,404,409,422] · `TryGetNoContent` [500] · `TryGetRawError` | none | map/operations/Payments.md; `Api/Payments.cs` |
| `Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 params must be passed explicitly. **`payPalRequestId` is the real caller-supplied key (stored 45 days).** Full refund: empty/null body; partial: `Amount`. Prefer `"return=representation"`. | `PayPal.Models.RefundRequest`: optional `Amount`. Left out: `CustomId`, `InvoiceId`, `NoteToPayer`, `PaymentInstruction`. | `PayPal.Models.Refund`: `Id`, `Status`, `Amount`, `SellerPayableBreakdown.TotalRefundedAmount` | Case A `SdkException<PayPal.Errors.RefundCapturedPaymentError>`: `TryGetError` [400,401,403,404,409,422] · `TryGetNoContent` [500] · `TryGetRawError` | none | map/operations/Payments.md; `Api/Payments.cs`; `Models/RefundRequest.cs`; `Models/Refund.cs`; `Models/SellerPayableBreakdown.cs` |
| `Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must be passed explicitly; stored 3 hours. | `PayPal.Models.PaymentTokenRequest`: **required** `PaymentSource` (`PaymentTokenRequestPaymentSource.Card` = `PaymentTokenRequestCard`: optional `Name`,`Number`,`Expiry`,`SecurityCode`,`BillingAddress`). Optional `Customer` (`MerchantCustomerId`). Direct card vault (no setup-token / browser). | `PayPal.Models.PaymentTokenResponse`: `Id` (vault id); `PaymentSource.Card` (`LastDigits`,`Brand`,`Expiry`,`Name`) — never PAN | Case A `SdkException<PayPal.Errors.CreatePaymentTokenError>`: `TryGetError` [400,403,404,422,500] · `TryGetRawError` | none | map/operations/Vault.md; `Api/Vault.cs`; `Models/PaymentTokenRequest.cs`; `Models/PaymentTokenRequestCard.cs`; `Models/PaymentTokenResponse.cs`; `Models/CardPaymentTokenEntity.cs` |
| `Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | none | `void` | Case A `SdkException<PayPal.Errors.DeletePaymentTokenError>`: `TryGetError` [400,403,500] · `TryGetRawError` | none | map/operations/Vault.md |
| `TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 filter params must be passed explicitly (`null` to skip). `endDate` max span **31 days**. Reporting can lag ~3 hours. | query only | `PayPal.Models.SearchResponse`: `TransactionDetails[]` → `TransactionInfo` (`TransactionId`,`PaypalReferenceId`,`InvoiceId`,`CustomField`,`TransactionAmount`,`FeeAmount`,`TransactionStatus`,`TransactionInitiationDate`); `Page`,`TotalItems`,`TotalPages` | **Case B** `SdkException<RawError>` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`) | **not** a Pageable; caller loops `page` using `total_pages` | map/operations/TransactionSearch.md; `Api/TransactionSearch.cs` remarks; `Models/SearchResponse.cs`; `Models/TransactionDetails.cs`; `Models/TransactionInformation.cs` |

Not used (out of scope): `ConfirmOrder`, `CaptureOrder`, `PatchOrder`, tracking, `CreateSetupToken` / `GetSetupToken` / `GetPaymentToken` / `ListCustomerPaymentTokens` (app DB is the shopper list), `SearchBalances`, Subscriptions.

### Enums actually used

| Type | Members (C# = wire) | source |
| --- | --- | --- |
| `PayPal.Models.Enums.CheckoutPaymentIntent` | `Capture`=CAPTURE, **`Authorize`=AUTHORIZE** | `Models/Enums/CheckoutPaymentIntent.cs` |
| `PayPal.Models.Enums.OrderStatus` | `Created`, `Saved`, `Approved`, `Voided`, `Completed`, **`PayerActionRequired`=PAYER_ACTION_REQUIRED** | `Models/Enums/OrderStatus.cs` |
| `PayPal.Models.Enums.AuthorizationStatus` | `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending` | `Models/Enums/AuthorizationStatus.cs` |
| `PayPal.Models.Enums.CaptureStatus` | `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed` | `Models/Enums/CaptureStatus.cs` |
| `PayPal.Models.Enums.RefundStatus` | `Cancelled`, `Failed`, `Pending`, `Completed` | `Models/Enums/RefundStatus.cs` |
| `PayPal.Models.Enums.CardBrand` | `Visa`=VISA, … `Unknown` (read `.Value` / `.ToString()` for display) | `Models/Enums/CardBrand.cs` |
| StringEnum | **not** a C# enum — compare via `==` static members or `.Value` | `Core/Enum/StringEnum.cs`; `Core/Enum/TypedEnum.cs` |

### Request field details (pay / vault)

| Model | Fields used | source |
| --- | --- | --- |
| `PayPal.Models.PurchaseUnitRequest` | required `Amount` (`AmountWithBreakdown`: required `CurrencyCode`,`Value`); optional `CustomId` (eShop order id), `InvoiceId` (unique per merchant, e.g. `eshop-{orderId}`) | `Models/PurchaseUnitRequest.cs`; `Models/AmountWithBreakdown.cs` |
| `PayPal.Models.PaymentSource` / `OrderAuthorizeRequestPaymentSource` | `Card` = `PayPal.Models.CardRequest`: one-off `Number`,`Expiry` (YYYY-MM),`SecurityCode`,`Name`,`BillingAddress`; saved `VaultId`. Do not send PAN + VaultId together. | `Models/PaymentSource.cs`; `Models/CardRequest.cs` |
| `PayPal.Models.Address` | required `CountryCode` (ISO-2); optional `AddressLine1`,`AdminArea2` (city),`AdminArea1` (state),`PostalCode` | `Models/Address.cs` |
| `PayPal.Models.Money` | required `CurrencyCode` (3), `Value` (decimal string) | `Models/Money.cs` |
| `PayPal.Models.Customer` | optional `MerchantCustomerId` (shopper username, max 64, charset `^[0-9a-zA-Z-_.^*$@#]+$`) | `Models/Customer.cs` |
| Items / breakdown | **omitted** so amount need not equal item_total+tax+… | YOUR CALL — not in the map |

### Application decisions (not SDK)

| Decision | Choice | source |
| --- | --- | --- |
| Layering | Gateway interface in ApplicationCore; `PayPalClient` lives in Infrastructure; HTTP endpoints on PublicApi (`IEndpoint<…>` like `CreateCatalogItemEndpoint`) | YOUR CALL — not in the map |
| Order model | Extend existing `Order` / `OrderItem`; new owned refunds; `SavedPaymentMethod` is a separate aggregate | YOUR CALL — not in the map |
| Shopper identity | `ClaimTypes.Name` from JWT (username) as `BuyerId` | YOUR CALL — not in the map (exemplar: `IdentityTokenClaimService.cs`) |
| Admin | `BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS` on fulfil/cancel/reconciliation | YOUR CALL — not in the map (exemplar: `CreateCatalogItemEndpoint.cs`) |
| Idempotent pay/fulfil/cancel | Local state short-circuit (already authorized/captured/voided → return stored result). Also pass stable `payPalRequestId` (order-scoped) on create/authorize/capture/void. | YOUR CALL — not in the map |
| Refund idempotency | Caller key required; persist on `OrderRefund`; same key returns existing; pass as `payPalRequestId` | map row above + YOUR CALL |
| Stale hold | If `expiration_time` elapsed or capture fails as expired: `ReauthorizePayment` then capture new id. If reauth fails (30-day window / already reauthed): operator error “authorization cannot be renewed — shopper must pay again”. | `ReauthorizeRequest` / `CheckoutPaymentIntent.Authorize` remarks; remainder YOUR CALL |
| Reconciliation match | Join PayPal `invoice_id`/`custom_field`/`transaction_id` to eShop `invoice_id` / capture / auth / refund ids; emit paypal-only, eshop-only, matched. Split `from`/`to` into ≤31-day windows; page until `TotalPages`. Empty range is valid. | YOUR CALL — not in the map |
| 3DS | Treat `PAYER_ACTION_REQUIRED` as a hard failure to the caller; do not redirect | task + `OrderStatus` remarks |
| PAN | Never persist; never log; `LogRequestBody` off | task |

---

## 3. Trap notes

- Step 0 — `Timeout` on `RetryOptions` does not bound a whole call the way a caller budget implies; a hung retryable request can overshoot. **MUST load dotnet-configuration-resilience**
- Step 0 — Default `HttpMethodsToRetry` does not include POST/DELETE; pay/capture/void/refund/vault writes are not SDK-resent, while GET reporting is. Mis-owning write retries duplicates money movement. **MUST load dotnet-configuration-resilience**
- Step 0 — `LogRequestBody` logs JSON bodies unredacted (PAN/CVV on pay & vault). Leaving `LoggerFactory` unset arms `PAYPALCLIENT_LOG`. **MUST load dotnet-configuration-resilience**
- Step 0 — `HttpClient` / handler pipeline lifetime vs wrapping `PayPalClient` lifetime: a per-request client burns sockets; a singleton handler is the opposite trap. **MUST load dotnet-client-initialization**
- Step 0 — `AddPayPalClient` captures options at registration; a rotated secret is invisible until restart. **MUST load dotnet-client-initialization**
- Step 0 — Credentials must be on options before the client is constructed; a late set is a 401 that never looks like “forgot Oauth2”. **MUST load dotnet-authentication**
- Steps 3–8 — Optional params without C# defaults (`payPalRequestId` … `body`) mis-bind if passed positionally; the generator `Idempotency-Key` header is not a key. **MUST load dotnet-calling-endpoints**
- Steps 3–8 — Request records are init-only; `required` members; unions/enums are not `new`/C# enums; `AdditionalProperties` keeps unknown fields. **MUST load dotnet-models**
- Steps 3–8 — Case A vs Case B is per operation (`SearchTransactions` is the only Case B in scope); `TryGetRawError` is not a catch-all on typed errors; `SdkException` has no HTTP status of its own. **MUST load dotnet-error-handling**
- Steps 3–8 — A drifted/malformed **2xx** body (missing `required`) surfaces as `System.Text.Json.JsonException`, not `SdkException`. **MUST load dotnet-error-handling**
- Steps 3–8 — A **non-2xx** body that does not match `{Operation}Error` throws `JsonException` while the error object is being constructed, replacing `SdkException` and destroying the HTTP status. **MUST load dotnet-error-handling**
- Step 9 — The test seam is the `HttpClient` constructor argument, not internals of controllers or generated models. **MUST load dotnet-testing**

---

## 4. REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
| --- | --- |
| paypal-platforms-team / `dotnet-client-initialization` | Step 0 — client, DI, HttpClient lifetime, options capture |
| paypal-platforms-team / `dotnet-authentication` | Step 0 — Oauth2 credentials, token strategy |
| paypal-platforms-team / `dotnet-calling-endpoints` | Steps 3–8 — named args, `ct:`, real vs injected idempotency |
| paypal-platforms-team / `dotnet-models` | Steps 3–8 — records, required, StringEnum, Money/Address |
| paypal-platforms-team / `dotnet-error-handling` | Steps 3–8 — Case A/B, JsonException from 2xx **and** from non-2xx error construction |
| paypal-platforms-team / `dotnet-configuration-resilience` | Step 0 & 8 — retries, Timeout budget, BaseUrl, logging, SearchTransactions paging |
| paypal-platforms-team / `dotnet-testing` | Step 9 — HttpClient seam |

Always: `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling: (1) a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException`, so an SDK-exception-only catch ladder lets it escape; (2) a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so it **replaces** the `SdkException` and the HTTP status is destroyed with it.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | **Credential fail-fast** | Bind `PayPalOptions` from section `PayPal`. On PublicApi startup, require non-whitespace `ClientId`, `ClientSecret`, and `Currency`. `Environment` may be blank (SDK default is the only environment). `BaseUrl` optional. Host throws at boot if a required part is missing or blank — not on first 401. Tests supply non-blank placeholders via `appsettings.test.json` (not live secrets). |
| 2 | **Secret sourcing & rotation** | Values come from env (`PAYPAL_CLIENT_ID` → `PayPal:ClientId`, etc.) overlaid at startup, and from user-secrets (same keys) for local. `AddPayPalClient` builds options **once**; rotation requires process restart. No secret values in repo files. |
| 3 | **Total timeout budget** | SDK `Retry.Timeout` is per-attempt. Writes are not SDK-retried (POST). Bound each gateway call with `CancellationTokenSource(TimeSpan.FromSeconds(100))` plus the request `ct`, so a hung write cannot exceed ~100s. Reads (GET auth/capture/search) may retry up to 3 times — wrap those with a 100s **linked** deadline so total wall time is capped, not `Timeout × (1+MaxRetries)`. |
| 4 | **Write-retry ownership** | SDK will **not** resend POST/DELETE (create, authorize, capture, void, refund, vault create/delete). App does not add HTTP retries on those. GET (`GetAuthorizedPayment`, `GetCapturedPayment`, `SearchTransactions`, `GetOrder`) may be SDK-retried. PUT is not in scope. |
| 5 | **Idempotency & ambiguous writes** | CreateOrder / AuthorizeOrder / Capture / Void: app-generated stable `payPalRequestId` (`eshop-pay-{orderId}`, `eshop-capture-{orderId}`, `eshop-void-{orderId}`) plus local state short-circuit. Refund: **caller** key → `payPalRequestId` (real key, 45-day store) **and** unique index on (OrderId, IdempotencyKey). Vault create: new Guid `payPalRequestId` per user click; effect-idempotency is “new token”. Generator `Idempotency-Key` is **not** cited as a key. Ambiguous timeout after a write: reconcile via `GetOrder` / `GetAuthorizedPayment` / `GetCapturedPayment` / stored ids — do not blindly retry POST. |
| 6 | **Observability** | Information: operation name, eShop order id, PayPal order/auth/capture/refund/vault ids, `Error.DebugId`. Warning: mapped provider errors. Never log request bodies or PAN/CVV. `LogRequestBody` = false; `LoggerFactory` assigned from DI so `PAYPALCLIENT_LOG` cannot enable bodies. |
| 7 | **Sensitive data** | In-scope request fields include `CardRequest.Number`, `SecurityCode`, `PaymentTokenRequestCard.Number`/`SecurityCode`. `LogRequestBody` stays off **and** `LoggerFactory` is set explicitly. App DB stores vault id + last digits + brand + expiry only. |
| 8 | **Environment selection** | One server group `Default`. Only environment member: `ServerEnvironment.Production` → `https://api-m.sandbox.paypal.com`. Task `PayPal:Environment` of `sandbox`/`production` both select that member (SDK has no live/prod host). If `PayPal:BaseUrl` is set, assign it to `options.Server.Default.Production.BaseUrl` for **every** call including `/v1/oauth2/token`. |

---

## 6. Assumptions & Blockers

**Assumptions**

- Direct card `CreateOrder` + `AuthorizeOrder` with sandbox Visa `4111111111111111` completes without `PAYER_ACTION_REQUIRED`. If live traffic returns that status, that is a **runtime stop**, not a planning blocker.
- Direct `CreatePaymentToken` with card details is accepted (account enabled for vaulting). Setup-token + browser is out of scope.
- Refunds are shopper-scoped (not in the admin-only list). Fulfil/cancel/reconciliation are admin.
- `POST /api/orders` includes a shipping address because existing `Order` requires `ShipToAddress`.
- Existing Web basket checkout remains unpaid (additive). Payment is driven through PublicApi.
- Dummy non-secret PayPal placeholders in test `appsettings.test.json` are allowed so fail-fast does not break existing PublicApi tests.

**Blockers**

- None. Map covers authorize, capture, reauthorize, void, refund, vault create/delete, and transaction search.

---

## 7. Repo conventions to imitate (pattern + one file)

| Convention | Exemplar |
| --- | --- |
| PublicApi minimal endpoint + JWT admin role | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` |
| Request/response records + `orderId` top-level on create | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.CreateCatalogItemResponse.cs` |
| JWT authenticate | `src/PublicApi/AuthEndpoints/AuthenticateEndpoint.cs` |
| Exception middleware | `src/PublicApi/Middleware/ExceptionMiddleware.cs` |
| Order aggregate / total | `src/ApplicationCore/Entities/OrderAggregate/Order.cs` |
| Place-order from catalog snapshots | `src/ApplicationCore/Services/OrderService.cs` |
| EF owned address + order items | `src/Infrastructure/Data/Config/OrderConfiguration.cs` |
| Spec include items | `src/ApplicationCore/Specifications/OrderWithItemsByIdSpec.cs` |
| DI + in-memory flag | `src/PublicApi/Program.cs`; `src/Infrastructure/Dependencies.cs` |
| Unit test style | `tests/UnitTests/ApplicationCore/Entities/OrderTests/OrderTotal.cs` |
