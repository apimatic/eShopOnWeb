# PayPal Server SDK (.NET) integration plan — eShopOnWeb PublicApi

PayPal payments + saved cards for eShopOnWeb, exposed on `src/PublicApi` (JWT). SDK: `PayPalServerSdk`
(APIMatic-generated, spec 2.29, netstandard2.0). Built from source and referenced as a vendored
project (`src/PayPalServerSdk`), CPM disabled in that project so its own pinned versions apply.

Caller identity = JWT `ClaimTypes.Name` (the username/email). `BuyerId` for all app rows = that name.
PublicApi holds its own isolated in-memory store, so `POST /api/orders` seeds the order there.

---

## 1. Scope & sequence

1. **SDK wiring** — vendor SDK project; DI-register `PayPalServerSdkClient` singleton via
   `AddPayPalServerSdkClient(...)`; bind `PayPal:` options; fail-fast on missing/blank creds; set
   `Server.Default.Sandbox.BaseUrl` when `PayPal:BaseUrl` present (applies to token + every call).
2. **Domain + persistence** — new aggregates `OrderPayment` (payment/fulfilment state for an eShop
   `Order`) and `SavedCard` (vaulted card metadata); `DbSet`s + EF configs on `CatalogContext`;
   `IRepository<>` via existing `EfRepository<>`.
3. **Payment gateway service** (`IPayPalPaymentGateway`) — thin app-facing wrapper over the SDK
   translating domain intents to SDK calls and SDK responses/errors to domain results. All SDK types
   stay behind it.
4. **Flow 2 endpoints** — `POST/GET/DELETE /api/payment-methods` (Vault: CreateSetupToken →
   CreatePaymentToken; DeletePaymentToken).
5. **Flow 1 endpoints** — `POST /api/orders` (app only), `/pay` (CreateOrder AUTHORIZE + AuthorizeOrder),
   `/fulfil` (Get/Reauthorize + CaptureAuthorizedPayment), `/cancel` (VoidPayment),
   `/refunds` (RefundCapturedPayment), `GET /api/my-orders`, `GET /api/reconciliation` (SearchTransactions,
   all pages).
6. Tests (unit for gateway mapping + endpoint auth/ownership) and live sandbox self-verification.

A capability the map lacks is a Blocker (§6), never invented.

---

## 2. CONTRACT SHEET

⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C# identifier;
in named arguments use exactly these (the cancellation-token parameter is literally `ct`, so write `ct:`).
⚠ **Every SDK type is fully-qualified from the namespace its source path implies** (`Models/` →
`PayPalServerSdk.Models`, `Models/Enums/` → `PayPalServerSdk.Models.Enums`, `Errors/` →
`PayPalServerSdk.Errors`, client/options → `PayPalServerSdk`, controllers → `PayPalServerSdk.Api`,
`OAuth2ClientCredentials` → `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`), taken from
that type's own path — never a neighbour's.

### Client construction / auth / servers
- DI: `services.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>)` registers a **singleton**
  client built once (uses `IHttpClientFactory`). Source: `ServiceCollectionExtensions.cs`.
- Options (`PayPalServerSdkClientOptions.cs`): `Oauth2 = new OAuth2ClientCredentials { ClientId, ClientSecret }`
  (both `required`; `Scope?` optional); `Environment = ServerEnvironment.Sandbox`;
  `Server.Default.Sandbox.BaseUrl` (string, default `https://api-m.sandbox.paypal.com`), `Retry`, `Logging`, `Hooks`.
- OAuth2 token URL = `server.Default("/v1/oauth2/token")` — resolved through the **same `Server`**, so
  overriding `Server.Default.Sandbox.BaseUrl` **also** redirects the token request. Source:
  `AuthSchemes.cs`, `Servers/DefaultOptions.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentialsStrategy.cs`.
- Only environment is `ServerEnvironment.Sandbox` (`Servers/ServerEnvironment.cs`) — see §5 row 8.

### Operations

| Op | Signature (verbatim) · request fields used · response fields read · error | Source |
| --- | --- | --- |
| `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` → `Order`. Body `OrderRequest`: `Intent (intent): CheckoutPaymentIntent, required` (=AUTHORIZE); `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest>, required` (1 unit). Read `Order.Id (id)`, `Order.Status (status)`. Case A `CreateOrderError` — `TryGetError(out Error)` [400,401,422]. | `map/operations/Orders.md`; `Models/OrderRequest.cs`, `Models/Order.cs`, `Errors/CreateOrderError.cs` |
| `client.Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` → `OrderAuthorizeResponse`. Body `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` → `.Card (card): CardRequest?`. Read `OrderAuthorizeResponse.Status (status): OrderStatus?`, `.PurchaseUnits[0].Payments.Authorizations[0].Id/Status/Amount`. Case A `AuthorizeOrderError` — `TryGetError(out Error)` [400,401,403,404,422,500]. **`<remarks>`: authorize needs buyer approval OR a valid `payment_source` in the request** — we supply the card here. | `map/operations/Orders.md`; `Api/Orders.cs`; `Models/OrderAuthorizeRequest.cs`, `Models/OrderAuthorizeRequestPaymentSource.cs`, `Models/OrderAuthorizeResponse.cs`, `Models/PurchaseUnit.cs`, `Models/PaymentCollection.cs`, `Models/AuthorizationWithAdditionalData.cs` |
| `client.Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions?=null, CancellationToken ct=default)` → `PaymentAuthorization`. Read `.Status (status): AuthorizationStatus?`. Case A `GetAuthorizedPaymentError` — `TryGetError(out Error)` [401,403,404], `TryGetNoContent(out RawError)` [500]. | `map/operations/Payments.md`; `Models/PaymentAuthorization.cs` |
| `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` → `PaymentAuthorization`. Body `ReauthorizeRequest`: `Amount (amount): Money?`. Read new `.Id`, `.Status`. Case A `ReauthorizePaymentError` — `TryGetError(out Error)` [400,401,403,404,422], `TryGetNoContent(out RawError)` [500]. **`<remarks>`: only-once, days 4–29; supports only `amount`.** | `map/operations/Payments.md`; `Models/ReauthorizeRequest.cs`, `Models/PaymentAuthorization.cs` |
| `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` → `CapturedPayment`. Body `CaptureRequest`: `Amount (amount): Money?`, `FinalCapture (final_capture): bool?`(=true), `InvoiceId (invoice_id)?`. Read `.Id (id)`, `.Status: CaptureStatus?`, `.SellerReceivableBreakdown` → `GrossAmount (gross_amount): Money` (required), `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?`. Case A `CaptureAuthorizedPaymentError` — `TryGetError(out Error)` [400,401,403,404,409,422], `TryGetNoContent(out RawError)` [500]. | `map/operations/Payments.md`; `Models/CaptureRequest.cs`, `Models/CapturedPayment.cs`, `Models/SellerReceivableBreakdown.cs`, `Models/Money.cs` |
| `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` → `PaymentAuthorization`. Read `.Status`. Case A `VoidPaymentError` — `TryGetError(out Error)` [401,403,404,409,422], `TryGetNoContent(out RawError)` [500]. **Note param order: `payPalAuthAssertion` before `payPalRequestId`.** | `map/operations/Payments.md`; `Models/PaymentAuthorization.cs` |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", RequestOptions?=null, CancellationToken ct=default)` → `Refund`. Body `RefundRequest`: `Amount (amount): Money?` (omit=full refund), `CustomId?`, `NoteToPayer?`. Read `.Id (id)`, `.Status: RefundStatus?`, `.Amount`. Case A `RefundCapturedPaymentError` — `TryGetError(out Error)` [400,401,403,404,409,422], `TryGetNoContent(out RawError)` [500]. | `map/operations/Payments.md`; `Models/RefundRequest.cs`, `Models/Refund.cs` |
| `client.Vault.CreateSetupToken` | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions?=null, CancellationToken ct=default)` → `SetupTokenResponse`. Body `SetupTokenRequest`: `PaymentSource (payment_source): SetupTokenRequestPaymentSource, required` → `.Card (card): SetupTokenRequestCard?` (`Number`,`Expiry`(YYYY-MM),`SecurityCode`,`Name`,`BillingAddress`); `Customer (customer): Customer?` (`Id?` — pass when reusing an existing PayPal customer). Read `.Id (id)`. Case A `CreateSetupTokenError` — `TryGetError(out Error)` [400,403,422,500]. | `map/operations/Vault.md`; `Models/SetupTokenRequest.cs`, `Models/SetupTokenRequestPaymentSource.cs`, `Models/SetupTokenRequestCard.cs`, `Models/Customer.cs`, `Models/SetupTokenResponse.cs` |
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions?=null, CancellationToken ct=default)` → `PaymentTokenResponse`. Body `PaymentTokenRequest`: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource, required` → `.Token (token): VaultTokenRequest?` (`Id`=setup-token id, `Type`=`VaultTokenRequestType.SetupToken`; both `required`). Read `.Id (id)` (=vault id), `.Customer.Id (customer.id)`, `.PaymentSource.Card: CardPaymentTokenEntity?` → `LastDigits (last_digits)`, `Brand (brand): CardBrand?`, `Expiry (expiry)`. Case A `CreatePaymentTokenError` — `TryGetError(out Error)` [400,403,404,422,500]. | `map/operations/Vault.md`; `Models/PaymentTokenRequest.cs`, `Models/PaymentTokenRequestPaymentSource.cs`, `Models/VaultTokenRequest.cs`, `Models/PaymentTokenResponse.cs`, `Models/PaymentTokenResponsePaymentSource.cs`, `Models/CardPaymentTokenEntity.cs` |
| `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions?=null, CancellationToken ct=default)` → `void`(Task). Case A `DeletePaymentTokenError` — `TryGetError(out Error)` [400,403,500]. | `map/operations/Vault.md`; `Errors/DeletePaymentTokenError.cs` |
| `client.Vault.ListCustomerPaymentTokens` | `ListCustomerPaymentTokens(string customerId, int? pageSize=5, int? page=1, bool? totalRequired=false, RequestOptions?=null, CancellationToken ct=default)` → `CustomerVaultPaymentTokensResponse` (`.PaymentTokens`, `.TotalPages`). Case A `ListCustomerPaymentTokensError` — `TryGetError(out Error)` [400,403,500]. **Optional** (GET is served from local store; may reconcile). | `map/operations/Vault.md`; `Models/CustomerVaultPaymentTokensResponse.cs` |
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, RequestOptions?=null, CancellationToken ct=default)` → `SearchResponse`. Call with **named args** (8 nullable middle params must be passed). Read `.TransactionDetails[].TransactionInfo` (`TransactionId`, `TransactionAmount: Money?`, `FeeAmount: Money?`, `TransactionStatus`, `TransactionInitiationDate`, `InvoiceId`, `CustomField`), `.TotalPages`, `.Page`. **Case B `SdkException<RawError>`** (no typed accessors). | `map/operations/TransactionSearch.md`; `Models/SearchResponse.cs`, `Models/TransactionDetails.cs`, `Models/TransactionInformation.cs` |

### Enums (needed)
- `CheckoutPaymentIntent` (`Models/Enums/CheckoutPaymentIntent.cs`): `Capture`="CAPTURE", `Authorize`="AUTHORIZE".
- `VaultTokenRequestType` (`Models/Enums/VaultTokenRequestType.cs`): `SetupToken`="SETUP_TOKEN".
- `AuthorizationStatus` (`Models/Enums/AuthorizationStatus.cs`): `Created`,`Captured`,`Denied`,`PartiallyCaptured`,`Voided`,`Pending`. **No `Expired` member** — but `StringEnum<T>` is open, so a wire `"EXPIRED"` deserializes and is read via `.Value`. Compare `.Value` to detect staleness.
- `CaptureStatus`: `Completed`,`Declined`,`PartiallyRefunded`,`Pending`,`Refunded`,`Failed`.
- `RefundStatus`, `CardBrand`, `OrderStatus` — read member names from `Models/Enums/*.cs` at implementation (values surfaced as `.Value` strings in responses; not constructed by us).

Amounts: `Money`/`AmountWithBreakdown` `CurrencyCode` + `Value` are **strings** (`required`). Format decimal
order total to the currency's minor units (2dp for USD) with invariant culture; `CurrencyCode` from
`PayPal:Currency`. Reconciliation compares to the cent by parsing `Value` back to decimal (invariant).

---

## 3. Trap notes (hazard + skill pointer; not resolved here)

- **Client lifetime / HttpClient ownership** — a singleton client over a factory `HttpClient`; getting the
  lifetime/handler pipeline wrong leaks sockets or breaks DNS. MUST load `paypal-platforms-team:dotnet-client-initialization`.
- **Setting credentials & rotation timing** — where creds attach and when a rotated secret takes effect.
  MUST load `paypal-platforms-team:dotnet-authentication`.
- **Named-argument binding on `SearchTransactions`/`AuthorizeOrder`** — optional params with no C# default
  mis-bind positionally; and which write takes a REAL idempotency key vs the injected header. MUST load
  `paypal-platforms-team:dotnet-calling-endpoints`.
- **Building request models / reading `StringEnum`/`Money` strings / `Optional`-vs-null / extension bags** —
  MUST load `paypal-platforms-team:dotnet-models`.
- **Error boundary — Case A typed vs Case B raw, `TryGet…` ladder, `TryGetNoContent` for 500, reading
  `Error.DebugId`** — MUST load `paypal-platforms-team:dotnet-error-handling`.
- **Retries/timeouts/base-URL/pagination/logging semantics** (esp. total vs per-attempt timeout, which verbs
  the SDK resends, and that `LogRequestBody` prints JSON unredacted) — MUST load
  `paypal-platforms-team:dotnet-configuration-resilience`.
- **Test seam (the `HttpClient` ctor arg) / asserting real behaviour** — MUST load `paypal-platforms-team:dotnet-testing`.

---

## 4. REQUIRED READING (load all before implementation; contents deliberately not carried here)

- `paypal-platforms-team:dotnet-client-initialization` — step 1 (client + DI singleton).
- `paypal-platforms-team:dotnet-authentication` — step 1 (OAuth2 client-credentials, secret sourcing).
- `paypal-platforms-team:dotnet-calling-endpoints` — steps 4–5 (every SDK call; named args; idempotency key).
- `paypal-platforms-team:dotnet-models` — steps 4–5 (build request bodies; read enums/Money/unions).
- `paypal-platforms-team:dotnet-error-handling` — every SDK call boundary (always required).
- `paypal-platforms-team:dotnet-configuration-resilience` — step 1 + reconciliation pagination + logging.
- `paypal-platforms-team:dotnet-testing` — tests.

Both mandatory `System.Text.Json.JsonException` hazards apply and are handled at the boundary:
1. A drifted/malformed **2xx** body (missing `required` member) surfaces as `JsonException` from
   deserialization — **not** an `SdkException` — so the catch ladder must also catch `JsonException`, or it escapes.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
   `JsonException` **while the error object is being constructed**, replacing the `SdkException` and destroying
   the HTTP status — handle it as an upstream failure, not a success.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` bound from `PayPal:` section and validated at startup (`ValidateOnStart`): host refuses to start if `ClientId`, `ClientSecret`, `Environment`, or `Currency` is missing **or blank/whitespace** (each part checked separately — a blank part ≠ a missing one). `BaseUrl` optional; if present must be a valid absolute URI. |
| 2 | Secret sourcing & rotation | Values from env vars `PAYPAL_*` loaded into **.NET user-secrets** (never written to repo files). DI builds the options object **once at registration** and captures it in the singleton client → a rotated secret takes effect only on process restart. Documented in the verification guide; no hot-rotation requirement for this task. |
| 3 | Total timeout budget | SDK `Timeout` is **per-attempt**; a whole call is bounded only by a `CancellationToken` deadline. Each gateway method links the request-aborted token with a per-call `CancellationTokenSource` timeout (config `PayPal:RequestTimeoutSeconds`, default 100s) so a hung retryable call cannot exceed the budget. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS, so our `POST`s (create/authorize/capture/void/refund/vault) are **never resent by the SDK** — safe. `GET`s (GetAuthorizedPayment, SearchTransactions) are retryable and idempotent. We do not enable POST retries. |
| 5 | Idempotency & ambiguous writes | **Create/Authorize (`/pay`)**: app-level state machine — an order already Authorized returns its stored authorization; deterministic `PayPal-Request-Id` (`payPalRequestId`) on CreateOrder + AuthorizeOrder derived from the eShop order id. **Capture (`/fulfil`)**: already-Fulfilled returns stored capture; deterministic `payPalRequestId`. **Void (`/cancel`)**: already-Cancelled returns; deterministic `payPalRequestId`. **Refund**: caller-supplied idempotency key → passed as `payPalRequestId`; the key is stored per order, a repeat returns the stored `refundId`, distinct keys allow distinct partial refunds; total refunded is capped at captured gross (never over-refund). The generator's injected `Idempotency-Key: Guid.NewGuid()` header is per-call and **not** relied upon. Reconciliation is the cross-check path (`custom_id`/`invoice_id` = eShop order id stamped on every purchase unit / capture / refund). |
| 6 | Observability | App logs at Info: order/payment lifecycle transitions with eShop order id + PayPal ids (order/auth/capture/refund) — **never** card data. On SDK error the boundary logs the PayPal `Error.DebugId` (correlation id) + status. SDK `LogRequestBody` stays **off** (see row 7). |
| 7 | Sensitive data | Card PAN/CVV/expiry flow through `CardRequest`/`SetupTokenRequestCard` request bodies → **sensitive**. `options.Logging.LogRequestBody` stays off **and** `options.Logging.LoggerFactory` is set explicitly in DI (the `AddPayPalServerSdkClient` helper already assigns `ILoggerFactory`) so the `PAYPALSERVERSDKCLIENT_LOG` env var cannot switch body logging on from outside code. App never logs request DTOs on card paths; PAN/CVV never persisted (only brand + last4 + expiry kept). |
| 8 | Environment selection | One server group `Default`; only member `ServerEnvironment.Sandbox` (base `https://api-m.sandbox.paypal.com`). All dev/test targets **sandbox** via `PayPal:Environment=sandbox`. `PayPal:BaseUrl`, when set, overrides `Server.Default.Sandbox.BaseUrl` and (confirmed via `AuthSchemes.cs`) also the `/v1/oauth2/token` request — used verbatim for every call. Production would set live creds + BaseUrl; the SDK exposes no live `ServerEnvironment` member, so live is reached only via the BaseUrl override — documented. |

---

## 6. Assumptions & Blockers

- **Assumption (minor):** direct-card authorize on the sandbox test card `4111 1111 1111 1111` returns without
  a 3DS browser challenge (account is enabled for direct card processing per the task). If PayPal returns a
  challenge requiring browser approval, per the task we **STOP and report** rather than build an approval round-trip.
- **Assumption (minor):** `/pay` uses CreateOrder(intent=AUTHORIZE, no payment source) then AuthorizeOrder with
  the card/vault in the request body — matching the `AuthorizeOrder` `<remarks>` ("a valid payment_source must be
  provided in the request") and the map's own example. Saved-card pay sets `CardRequest.VaultId`; one-off pay sets
  `Number/Expiry/SecurityCode`.
- **Assumption (minor):** staleness/renewal — at `/fulfil` we GetAuthorizedPayment; if not capturable
  (`.Value` = `EXPIRED`, or capture throws an expiry issue) we ReauthorizePayment then capture the (possibly new)
  authorization id; if reauthorization fails we return an operator-actionable 409 ("authorization expired and
  could not be renewed; collect a new payment"). Reauthorization cannot be forced in sandbox (needs a 3-day-old
  auth), so this path is verified by unit test, not live — not a gap.
- **No Blockers.** Every required capability (authorize hold, capture with fee/net breakdown, void, full/partial
  refund, vault a card + list + delete, transaction search for reconciliation) maps to an operation above.
