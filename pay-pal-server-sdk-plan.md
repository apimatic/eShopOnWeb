# PayPal Server SDK (.NET) integration plan — eShopOnWeb PublicApi

Adds card payments (authorize→capture→refund/void), saved cards (vault) and a reconciliation
report to `src/PublicApi`, reusing the existing `Order` aggregate. SDK: `PayPalServerSdk`
(root namespace), OAuth2 client-credentials, Sandbox. Every SDK contract fact below is grounded
from the SDK map/source, cited in the **source** column.

---

## 1. Scope & sequence

| # | Step | PayPal operations used |
| --- | --- | --- |
| 1 | Bind `PayPal:` settings (fail-fast) + register `PayPalServerSdkClient` (Infrastructure DI) | — (client + OAuth2) |
| 2 | Domain: `OrderPayment` aggregate (+ `PaymentRefund` child, `PaymentStatus`), `SavedPaymentMethod` aggregate (ApplicationCore) | — |
| 3 | Persistence: `CatalogContext` DbSets + EF `IEntityTypeConfiguration`s (Infrastructure) | — |
| 4 | `IPaymentProcessor` abstraction + plain result records (ApplicationCore); `PayPalPaymentProcessor` impl (Infrastructure) | CreateOrder, AuthorizeOrder, CaptureAuthorizedPayment, ReauthorizePayment, VoidPayment, RefundCapturedPayment, CreatePaymentToken, DeletePaymentToken, SearchTransactions |
| 5 | Application services: `PaymentService`, `PaymentMethodService`, `ReconciliationService` (ApplicationCore) — orchestrate repo + processor + idempotency/ownership rules | (calls step 4) |
| 6 | PublicApi endpoints (MinimalApi.Endpoint): orders create/pay/fulfil/cancel/refunds, my-orders, reconciliation, payment-methods list/create/delete | — |
| 7 | Wire DI in `PublicApi/Program.cs` | — |
| 8 | Unit tests (domain + processor mapping) + live sandbox verification | — |

**Flow → operation mapping**
- `POST /api/orders` → create eShop `Order` only (no PayPal call); status = awaiting payment.
- `POST /api/orders/{id}/pay` → **CreateOrder** (intent `AUTHORIZE`, payment_source = raw card *or* saved `vault_id`) then **AuthorizeOrder** → hold. Amount = order total to the cent.
- `POST /api/orders/{id}/fulfil` (admin) → **CaptureAuthorizedPayment**; on stale/expired hold → **ReauthorizePayment** then capture; record gross/fee/net.
- `POST /api/orders/{id}/cancel` (admin) → **VoidPayment** (release hold, pre-capture only).
- `POST /api/orders/{id}/refunds` → **RefundCapturedPayment** (full/partial; caller idempotency key).
- `GET /api/my-orders` → caller's orders + payment state (DB).
- `GET /api/reconciliation?from&to` (admin) → **SearchTransactions** across ALL pages, lined up vs eShop orders.
- `POST/GET/DELETE /api/payment-methods` → **CreatePaymentToken** / DB list / **DeletePaymentToken**.

---

## 2. CONTRACT SHEET

⚠ Signatures below are **generated code, verbatim** — every parameter name is the literal C#
identifier (the cancellation-token parameter really is `ct`, so named args write `ct:`; pass
nullable no-default params explicitly, `null` to skip).
⚠ Every SDK type is written **fully-qualified by the namespace its source path implies**, taken
from the path the map gives for THAT type (`Models/` → `PayPalServerSdk.Models`, `Models/Enums/`
→ `PayPalServerSdk.Models.Enums`, `Errors/` → `PayPalServerSdk.Errors`, client/options → root
`PayPalServerSdk`, `Servers/` → `PayPalServerSdk.Servers`, OAuth creds →
`PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`, `RawError`/`SdkException<>` →
`PayPalServerSdk.Core.ErrorResponse` / `.Core.Exceptions`).

### Operations

| Op | Signature (verbatim) · request→response · error case · **source** |
| --- | --- |
| `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` → `Order`. Case A `SdkException<CreateOrderError>`: `TryGetError(out Error)`[400,401,422], `TryGetRawError`[fallback]. **src** map/operations/Orders.md |
| `client.Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` → `OrderAuthorizeResponse`. Case A `AuthorizeOrderError`: `TryGetError`[400,401,403,404,422,500], `TryGetRawError`. **src** Orders.md |
| `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` → `CapturedPayment`. Case A `CaptureAuthorizedPaymentError`: `TryGetError`[400,401,403,404,409,422], `TryGetNoContent(out RawError)`[500], `TryGetRawError`. **src** Payments.md |
| `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` → `PaymentAuthorization`. Case A `ReauthorizePaymentError`: `TryGetError`[400,401,403,404,422], `TryGetNoContent`[500], `TryGetRawError`. **src** Payments.md |
| `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` → `PaymentAuthorization`. Case A `VoidPaymentError`: `TryGetError`[401,403,404,409,422], `TryGetNoContent`[500], `TryGetRawError`. **src** Payments.md |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` → `Refund`. Case A `RefundCapturedPaymentError`: `TryGetError`[400,401,403,404,409,422], `TryGetNoContent`[500], `TryGetRawError`. **src** Payments.md |
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions=null, CancellationToken ct=default)` → `PaymentTokenResponse`. Case A `CreatePaymentTokenError`: `TryGetError`[400,403,404,422,500], `TryGetRawError`. **src** Vault.md |
| `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions=null, CancellationToken ct=default)` → `void`(Task). Case A `DeletePaymentTokenError`: `TryGetError`[400,403,500], `TryGetRawError`. **src** Vault.md |
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, RequestOptions? requestOptions=null, CancellationToken ct=default)` → `SearchResponse`. **Case B** `SdkException<RawError>` (no typed accessors). `SearchResponse.Page/TotalPages/TotalItems` (int?) drive paging. **src** map/operations/TransactionSearch.md |

### Request models (fields used — C# name : wire : type)

- `OrderRequest` (Models/OrderRequest.cs): `Intent`(intent):`CheckoutPaymentIntent` **req**; `PurchaseUnits`(purchase_units):`IReadOnlyList<PurchaseUnitRequest>` **req** (min1); `PaymentSource`(payment_source):`PaymentSource?`. **src** Models/OrderRequest.cs
- `PurchaseUnitRequest`: `Amount`(amount):`AmountWithBreakdown` **req**; `CustomId`(custom_id):`string?`; `InvoiceId`(invoice_id):`string?`; `ReferenceId`(reference_id):`string?`; `Description`(description):`string?`. **src** Models/PurchaseUnitRequest.cs
- `AmountWithBreakdown`: `CurrencyCode`(currency_code):`string` **req**; `Value`(value):`string` **req** (decimal string, regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`). **src** Models/AmountWithBreakdown.cs
- `PaymentSource`: `Card`(card):`CardRequest?`; (Token variant is `TokenType.BILLING_AGREEMENT` only — NOT for vaulted cards, see below). **src** Models/PaymentSource.cs
- `CardRequest`: `Name`(name):`string?`; `Number`(number):`string?`; `Expiry`(expiry):`string?` (`YYYY-MM`); `SecurityCode`(security_code):`string?`; `BillingAddress`(billing_address):`Address?`; `VaultId`(vault_id):`string?` (**reuse saved card**); `Attributes`(attributes):`CardAttributes?` (**save card during order** via `.Vault.StoreInVault = StoreInVaultInstruction.OnSuccess`). **src** Models/CardRequest.cs
- `Address`: `CountryCode`(country_code):`string` **req**; `AddressLine1/2`, `AdminArea1/2`, `PostalCode`: `string?`. **src** Models/Address.cs
- `CaptureRequest`: `Amount`(amount):`Money?`; `FinalCapture`(final_capture):`bool?`; `InvoiceId`(invoice_id):`string?`. **src** Models/CaptureRequest.cs
- `Money`: `CurrencyCode`(currency_code):`string` **req**; `Value`(value):`string` **req**. **src** Models/Money.cs
- `RefundRequest`: `Amount`(amount):`Money?`; `InvoiceId`(invoice_id):`string?`; `CustomId`(custom_id):`string?`; `NoteToPayer`(note_to_payer):`string?`. **src** Models/RefundRequest.cs
- `ReauthorizeRequest`: `Amount`(amount):`Money?`. (reauth for stale hold; send order total.) **src** Models/ReauthorizeRequest.cs
- `PaymentTokenRequest`: `Customer`(customer):`Customer?`; `PaymentSource`(payment_source):`PaymentTokenRequestPaymentSource` **req**. **src** Models/PaymentTokenRequest.cs
- `PaymentTokenRequestPaymentSource`: `Card`(card):`PaymentTokenRequestCard?`. **src** Models/PaymentTokenRequestPaymentSource.cs
- `PaymentTokenRequestCard`: `Name`,`Number`,`Expiry`,`SecurityCode`:`string?`; `BillingAddress`:`Address?` (raw card to vault; NO vault_id here). **src** Models/PaymentTokenRequestCard.cs
- `Customer`: `Id`(id):`string?` (max 22). **src** Models/Customer.cs

### Response models (fields read)

- `Order`: `Id`(id):`string?`; `Status`(status):`OrderStatus?`; `PurchaseUnits`:`IReadOnlyList<PurchaseUnit>?`. **src** Models/Order.cs
- `OrderAuthorizeResponse`: `Id`; `Status`:`OrderStatus?`; `PurchaseUnits`:`IReadOnlyList<PurchaseUnit>?`; `Links`:`IReadOnlyList<LinkDescription>?`. **src** Models/OrderAuthorizeResponse.cs
- `PurchaseUnit.Payments`(`PaymentCollection?`) → `.Authorizations`:`IReadOnlyList<AuthorizationWithAdditionalData>?`, `.Captures`:`IReadOnlyList<OrdersCapture>?`, `.Refunds`:`IReadOnlyList<Refund>?`. **src** Models/PurchaseUnit.cs, Models/PaymentCollection.cs
- `AuthorizationWithAdditionalData`: `Id`; `Status`:`AuthorizationStatus?`; `ExpirationTime`(expiration_time):`string?`; `Amount`:`Money?`. **src** Models/AuthorizationWithAdditionalData.cs
- `CapturedPayment`: `Id`; `Status`:`CaptureStatus?`; `Amount`:`Money?`; `SellerReceivableBreakdown`(seller_receivable_breakdown):`SellerReceivableBreakdown?`. **src** Models/CapturedPayment.cs
- `SellerReceivableBreakdown`: `GrossAmount`(gross_amount):`Money` **req**; `PaypalFee`(paypal_fee):`Money?`; `NetAmount`(net_amount):`Money?`. **src** Models/SellerReceivableBreakdown.cs
- `PaymentAuthorization`: `Id`; `Status`:`AuthorizationStatus?`; `ExpirationTime`:`string?`. **src** Models/PaymentAuthorization.cs
- `Refund`: `Id`; `Status`:`RefundStatus?`; `Amount`:`Money?`; `SellerPayableBreakdown.TotalRefundedAmount`:`Money?`. **src** Models/Refund.cs
- `PaymentTokenResponse`: `Id`(id):`string?` (**vault token id**); `Customer`:`CustomerResponse?` (`.Id`); `PaymentSource.Card`:`CardPaymentTokenEntity?`. **src** Models/PaymentTokenResponse.cs
- `CardPaymentTokenEntity` (safe descriptor): `LastDigits`(last_digits):`string?`; `Brand`(brand):`CardBrand?`; `Expiry`(expiry):`string?`; `Name`(name):`string?`. **src** Models/CardPaymentTokenEntity.cs
- `SearchResponse`: `TransactionDetails`:`IReadOnlyList<TransactionDetails>?`; `Page/TotalPages/TotalItems`:`int?`; `StartDate/EndDate`:`string?`. **src** Models/SearchResponse.cs
- `TransactionDetails.TransactionInfo`:`TransactionInformation?`. **src** Models/TransactionDetails.cs
- `TransactionInformation`: `TransactionId`(transaction_id):`string?`; `TransactionStatus`(transaction_status):`string?` (single-char code, NOT enum); `TransactionAmount`/`FeeAmount`:`Money?`; `InvoiceId`(invoice_id):`string?`; `CustomField`(custom_field):`string?`; `TransactionInitiationDate`:`string?`; `TransactionEventCode`:`string?`. **src** Models/TransactionInformation.cs

### Enum tables (C# member = wire) — `StringEnum<T>`, read via `.Value`, never `ToString()`

- `CheckoutPaymentIntent`: `Capture`=CAPTURE, `Authorize`=AUTHORIZE. **src** Models/Enums/CheckoutPaymentIntent.cs
- `OrderStatus`: Created, Saved, Approved, Voided, Completed, PayerActionRequired=PAYER_ACTION_REQUIRED. **src** Models/Enums/OrderStatus.cs
- `AuthorizationStatus`: Created, Captured, Denied, PartiallyCaptured, Voided, Pending. **src** Models/Enums/AuthorizationStatus.cs
- `CaptureStatus`: Completed, Declined, PartiallyRefunded, Pending, Refunded, Failed. **src** Models/Enums/CaptureStatus.cs
- `RefundStatus`: Cancelled, Failed, Pending, Completed. **src** Models/Enums/RefundStatus.cs
- `StoreInVaultInstruction`: OnSuccess=ON_SUCCESS. **src** Models/Enums/StoreInVaultInstruction.cs
- `CardBrand`: open enum, read `.Value` for wire (e.g. "VISA"). **src** Models/Enums/CardBrand.cs

### Client construction / auth / server

- Construct `new PayPalServerSdkClient(HttpClient, PayPalServerSdkClientOptions)`; only ctor. **src** sdk-map.md.
- Auth: `options.Oauth2 = new OAuth2ClientCredentials { ClientId, ClientSecret }` (namespace `.Core.Authentication.OAuth2.ClientCredentials`). SDK fetches+caches token. **src** sdk-map.md Servers&auth.
- Environment: `options.Environment = ServerEnvironment.Sandbox` (`PayPalServerSdk.Servers`); only environment declared. **src** Servers/ServerEnvironment.cs (map).
- **BaseUrl override**: `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>` when set. Token endpoint is `server.Default("/v1/oauth2/token")` (**AuthSchemes.cs**), i.e. resolved against the Default group base URL — so this override reaches the token request too, as required. **src** AuthSchemes.cs.

---

## 3. Trap notes (name the hazard + skill; do not resolve here)

- **Step 1 (client/DI):** singleton client vs `IHttpClientFactory` handler rotation, and where the built-in logger's env-var switch is armed — `MUST load dotnet-client-initialization`.
- **Step 1 (auth):** what "credential configured" actually guarantees at construction vs first call, and multi-part credential blank-vs-missing — `MUST load dotnet-authentication`.
- **Step 4 (calls):** `SearchTransactions` has 8 leading no-default nullable params before the defaulted paging ones; positional mis-binding — `MUST load dotnet-calling-endpoints`.
- **Step 4 (models):** `StringEnum` `.Value` vs `ToString()` when a status/brand leaves the process (log/DB/JSON); building `Money`/`AmountWithBreakdown` value strings — `MUST load dotnet-models`.
- **Step 4 (errors):** the two-directions `JsonException` at the boundary (drifted 2xx body vs non-2xx body not matching `{Operation}Error`); Case A accessor-ladder ordering with `TryGetNoContent` before `TryGetRawError`; `SearchTransactions` is Case B — `MUST load dotnet-error-handling`.
- **Step 4 (resilience):** `POST`/`DELETE` are never SDK-resent but a transport failure leaves outcome *unknown*; per-attempt vs total timeout; unbounded page loop in reconciliation; `LogRequestBody` logs card JSON unredacted — `MUST load dotnet-configuration-resilience`.
- **Step 8 (tests):** the `HttpClient` ctor arg is the fake seam — `MUST load dotnet-testing`.

---

## 4. REQUIRED READING (load BEFORE implementation; contents deliberately not restated here)

| Skill (plugin `paypal-platforms-team`) | Governs |
| --- | --- |
| `dotnet-client-initialization` | Step 1 client construction + DI |
| `dotnet-authentication` | Step 1 OAuth2 creds + fail-fast |
| `dotnet-calling-endpoints` | Step 4 all operation calls |
| `dotnet-models` | Step 4 request/response model + enum handling |
| `dotnet-error-handling` | Step 4 error boundary (always required) |
| `dotnet-configuration-resilience` | Step 4 timeouts/retry/pagination/logging |
| `dotnet-testing` | Step 8 SDK-stub tests |

Two mandatory `JsonException` hazard rows (both reach the boundary, opposite handling):
1. A drifted/malformed **2xx** body (a missing `required` member) surfaces as `System.Text.Json.JsonException` from deserialization — **not** an `SdkException` — so an SDK-exception-only catch ladder lets it escape.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so it **replaces** the `SdkException` and the HTTP status is destroyed with it.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalSettings` bound from `PayPal:` with `[Required]` on `ClientId`,`ClientSecret`,`Environment`,`Currency`; `AddOptions().Bind().ValidateDataAnnotations().ValidateOnStart()` in Program.cs. Each part checked (blank ≠ missing). Host refuses to boot if any is missing/blank — never a first-call 401. |
| 2 | Secret sourcing & rotation | Secrets from **.NET user-secrets** (`PayPal:ClientId/ClientSecret`) loaded from env vars by me; never in repo files. Options object built once at DI registration and captured in the singleton client → rotation needs a process restart (acceptable for this app; documented). |
| 3 | Total timeout budget | SDK `Retry.Timeout` is per-attempt (default 100s). Set `Retry.Timeout=15s` and enforce a **whole-call** budget with a `CancellationToken` (`CancelAfter`, linked to `HttpContext.RequestAborted`) in the processor's single `Bounded()` helper. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry`=GET,HEAD,PUT,OPTIONS → our POST/DELETE PayPal writes are **never** SDK-resent. We keep defaults (no verb added). PUT unused. |
| 5 | Idempotency & ambiguous writes | CreateOrder/Capture/Void: deterministic `payPalRequestId` (`eshop-{op}-{orderId}`) — the real PayPal idempotency key (operation param, not the injected header) — **plus** a DB state guard so a double-click returns the existing state. Refund: **caller-supplied** idempotency key → `payPalRequestId`; DB stores (paymentId,key)→refundId so a repeat returns the same `refundId`, while two distinct keys make two legit partial refunds. Cumulative-refund guard: Σrefunds ≤ captured. Transport failure on a write → outcome unknown → reconciliation report + `GetOrder`/status re-read path. |
| 6 | Observability | Request line/status/retry logged via DI `ILoggerFactory` at Information/Warning. `LogRequestBody` stays **off**. On PayPal error we log the correlation/debug id from the error body + our order id, never card data. |
| 7 | Sensitive data | Card PAN/CVC flow through `CardRequest`/`PaymentTokenRequestCard`. `LogRequestBody=false` **and** `options.Logging.LoggerFactory` assigned explicitly (DI factory) so `PAYPALSERVERSDKCLIENT_LOG` cannot switch body logging on externally. PAN/CVC never persisted (DB stores only brand/last4/expiry/vault-token-id) and never logged. |
| 8 | Environment selection | One server group `Default`, one environment `Sandbox`. Every deployment sets `PayPal:Environment=sandbox`; `options.Environment=ServerEnvironment.Sandbox`. Optional `PayPal:BaseUrl` overrides `options.Server.Default.Sandbox.BaseUrl` (covers token endpoint too, §2). All dev/test traffic → sandbox host; no live host reachable. |

---

## 6. Assumptions & Blockers

- **No Blockers.** Every capability the task needs maps to a map-documented operation.
- Assumption: saving a card (`POST /api/payment-methods`) uses **CreatePaymentToken with a raw card** (business account is vault-enabled, direct card processing, no browser) rather than the setup-token two-step — the sandbox account supports direct vaulting per the task.
- Assumption: reuse of a saved card at pay time = `PaymentSource.Card.VaultId` (the `Token` payment source is `BILLING_AGREEMENT`-only, not cards — confirmed Models/Enums/TokenType.cs).
- Assumption: `buyerId`/owner scope = JWT `ClaimTypes.Name` (the Identity username/email), matching how `Order.BuyerId` is used elsewhere.
- Assumption: currency formatting uses 2 decimal places (invariant) — correct for the configured `USD` and other minor-unit-2 currencies; the "to the cent" requirement is met by formatting `Order.Total()` with 2 decimals for both authorize and capture.
- If a card payment returns a browser-approval challenge (`OrderStatus.PayerActionRequired` / an `approve` HATEOAS link) the pay endpoint returns a clear error and does **not** build an approval round-trip (per task).
