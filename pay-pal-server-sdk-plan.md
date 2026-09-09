# PayPal Server SDK (.NET) integration plan — eShopOnWeb PublicApi

Additive PayPal card payments + saved cards on `src/PublicApi`. SDK: `PayPalServerSdk`
(root namespace), OAuth2 client-credentials, single `Sandbox` environment. This file is the
authoritative contract sheet; implementation takes every SDK fact from here or a fresh map lookup.

## 1. Scope & sequence

1. **Vendor + wire the SDK.** Clone the SDK into `src/PayPalServerSdk` (portable, in-repo build
   dependency — distinct from the read-only map clone), disable central package management for it,
   add to `eShopOnWeb.sln`, `ProjectReference` from `Infrastructure`.
2. **Config + fail-fast.** Bind `PayPal:` section (`ClientId`, `ClientSecret`, `Environment`,
   `Currency`, `BaseUrl`) to a `PayPalOptions`. Load secret values into user-secrets (never in repo).
   Register `PayPalServerSdkClient` via `AddPayPalServerSdkClient`; host refuses to start on any
   missing/blank required setting (`ClientId`, `ClientSecret`, `Currency`).
3. **Domain (ApplicationCore, no SDK ref).** New `PaymentAggregate`: `Payment` (aggregate root, 1:1
   with `Order` by `OrderId`) holding PayPal ids/status for hold/capture/refunds, captured amount,
   fee, net; child `PaymentRefund`; `PaymentStatus` enum. New `SavedPaymentMethod` aggregate root
   (BuyerId, PayPal vault id, safe display fields). `IPaymentGateway` abstraction + plain
   request/result DTOs. Reuses existing `Order`/`OrderItem`/`CatalogItemOrdered`.
4. **Infrastructure.** `PayPalPaymentGateway : IPaymentGateway` implementing every PayPal call from
   the SDK; map SDK exceptions → `PaymentGatewayException` (status + message + PayPal `debug_id`).
   Register DbSets + EF configs on `CatalogContext`; add EF migration.
5. **PublicApi endpoints** (MinimalApi `IEndpoint` style like `CatalogItemEndpoints`):
   - `POST /api/orders` (shopper) — place order from catalog ids+qty → CreateOrderAsync + Payment(AwaitingPayment). Uses **no** SDK.
   - `POST /api/orders/{orderId}/pay` (shopper) — CreateOrder(AUTHORIZE)+AuthorizeOrder → hold.
   - `POST /api/orders/{orderId}/fulfil` (**admin**) — CaptureAuthorizedPayment; reauthorize-on-stale.
   - `POST /api/orders/{orderId}/cancel` (**admin**) — VoidPayment.
   - `POST /api/orders/{orderId}/refunds` (shopper owner) — RefundCapturedPayment (caller idem key).
   - `GET /api/my-orders` (shopper) — caller's orders + payment state.
   - `GET /api/reconciliation?from&to` (**admin**) — SearchTransactions over ALL pages vs eShop orders.
   - `POST/GET/DELETE /api/payment-methods` (shopper) — vault save / list / delete.
6. **Self-verify** on sandbox card `4111...`: authorize, capture, refund, saved-card reuse.

> Operator vs shopper: task says fulfil/cancel/reconciliation are admin. Refund is a *return* the
> shopper initiates on their own order → shopper-scoped owner check (still admin may also call).

## 2. CONTRACT SHEET

> ⚠ Signatures below are generated code, VERBATIM. Every parameter name is the literal C#
> identifier — the cancellation-token parameter is `ct`, so named args write `ct:`. Many params are
> `string?` with no default and **must be passed explicitly** (pass `null` to skip).
> ⚠ Every SDK type is written fully-qualified against the namespace its source path implies:
> `Models/` → `PayPalServerSdk.Models`, `Models/Enums/` → `PayPalServerSdk.Models.Enums`,
> `Errors/` → `PayPalServerSdk.Errors`, client/options → `PayPalServerSdk`,
> `ServerEnvironment` → `PayPalServerSdk.Servers`. Take each type's namespace from ITS OWN path.

### Client construction / auth / server (source: sdk-map.md · PayPalServerSdkClientOptions.cs · ServiceCollectionExtensions.cs · AuthSchemes.cs · Servers/DefaultOptions.cs)

- DI: `services.AddPayPalServerSdkClient(options => { options.Oauth2 = new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials { ClientId=…, ClientSecret=… }; options.Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox; /* BaseUrl override ↓ */ })`. Registers a **singleton** `PayPalServerSdkClient`; options built once at registration.
- `OAuth2ClientCredentials { required string ClientId; required string ClientSecret; string? Scope }`.
- **BaseUrl override:** set `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>` when non-empty. Confirmed in source: the OAuth token request resolves through `server.Default("/v1/oauth2/token")` (AuthSchemes.cs) which uses the SAME `Default.Sandbox.BaseUrl` (Servers/DefaultOptions.cs) → override applies to token AND every API call, as the task requires. Default when unset: `https://api-m.sandbox.paypal.com`.
- Only environment member is `ServerEnvironment.Sandbox` (Servers/ServerEnvironment.cs). YOUR CALL below §5-row8 for non-sandbox `Environment` values.
- Every operation below authenticates with `options.Oauth2` (single scheme, no per-op override).

### Operations

| Op | Signature (verbatim) · returns · error case · source |
| --- | --- |
| Vault.CreateSetupToken | `CreateSetupToken(string? payPalRequestId, PayPalServerSdk.Models.SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `SetupTokenResponse` · Case A `SdkException<CreateSetupTokenError>`, `TryGetError(out Error)`[400,403,422,500] · map/operations/Vault.md |
| Vault.CreatePaymentToken | `CreatePaymentToken(string? payPalRequestId, PayPalServerSdk.Models.PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentTokenResponse` · Case A `SdkException<CreatePaymentTokenError>`, `TryGetError(out Error)`[400,403,404,422,500] · Vault.md |
| Vault.DeletePaymentToken | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `void`(Task) · Case A `SdkException<DeletePaymentTokenError>`, `TryGetError(out Error)`[400,403,500] · Vault.md |
| Orders.CreateOrder | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Order` · Case A `SdkException<CreateOrderError>`, `TryGetError(out Error)`[400,401,422] · map/operations/Orders.md |
| Orders.AuthorizeOrder | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `OrderAuthorizeResponse` · Case A `SdkException<AuthorizeOrderError>`, `TryGetError(out Error)`[400,401,403,404,422,500] · Orders.md |
| Payments.CaptureAuthorizedPayment | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `CapturedPayment` · Case A `SdkException<CaptureAuthorizedPaymentError>`, `TryGetError(out Error)`[400,401,403,404,409,422], `TryGetNoContent(out RawError)`[500] · map/operations/Payments.md |
| Payments.ReauthorizePayment | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization` · Case A `SdkException<ReauthorizePaymentError>`, `TryGetError(out Error)`[400,401,403,404,422], `TryGetNoContent`[500] · Payments.md |
| Payments.GetAuthorizedPayment | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization` · Case A `SdkException<GetAuthorizedPaymentError>` · Payments.md |
| Payments.VoidPayment | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization` · Case A `SdkException<VoidPaymentError>`, `TryGetError(out Error)`[401,403,404,409,422], `TryGetNoContent`[500] · Payments.md |
| Payments.RefundCapturedPayment | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Refund` · Case A `SdkException<RefundCapturedPaymentError>`, `TryGetError(out Error)`[400,401,403,404,409,422], `TryGetNoContent`[500] · Payments.md |
| TransactionSearch.SearchTransactions | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `SearchResponse` · **Case B** `SdkException<RawError>` (no typed accessors) · map/operations/TransactionSearch.md |

### Request model fields used (source = each `Models/<Type>.cs`)

- `OrderRequest` { `required CheckoutPaymentIntent Intent` (intent); `required IReadOnlyList<PurchaseUnitRequest> PurchaseUnits` (purchase_units), 1..10; `PaymentSource? PaymentSource` (payment_source) }.
- `PurchaseUnitRequest` { `required AmountWithBreakdown Amount` (amount); `string? CustomId` (custom_id); `string? InvoiceId` (invoice_id); `string? Description` }.
- `AmountWithBreakdown` { `required string CurrencyCode` (currency_code, len 3); `required string Value` (value, regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`) }. Same shape `Money` { required CurrencyCode, required Value }.
- `PaymentSource` { `CardRequest? Card` (card); `Token? Token` (token) }. **Vaulted card ⇒ use `Card.VaultId`** — `Token.Type` is `TokenType` whose only value is `BILLING_AGREEMENT`, NOT a vaulted card token.
- `CardRequest` { `string? Name`; `string? Number` (13-19 digits); `string? Expiry` (`YYYY-MM`); `string? SecurityCode` (3-4); `Address? BillingAddress`; `string? VaultId` (vault_id, `^[0-9a-zA-Z_-]+$`) }.
- `SetupTokenRequest` { `Customer? Customer`; `required SetupTokenRequestPaymentSource PaymentSource` }. `SetupTokenRequestPaymentSource` { `SetupTokenRequestCard? Card` }. `SetupTokenRequestCard` { Name, Number, Expiry, SecurityCode, `CardBrand? Brand`, `Address? BillingAddress`, `VaultCardVerificationMethod? VerificationMethod`, ExperienceContext }.
- `Customer` { `string? Id`; `string? MerchantCustomerId` (merchant_customer_id, len ≤64, `^[0-9a-zA-Z-_.^*$@#]+$`) }.
- `PaymentTokenRequest` { `Customer? Customer`; `required PaymentTokenRequestPaymentSource PaymentSource` }. `PaymentTokenRequestPaymentSource` { `PaymentTokenRequestCard? Card`; `VaultTokenRequest? Token` }. `VaultTokenRequest` { `required string Id`; `required VaultTokenRequestType Type` (=`SetupToken`, wire SETUP_TOKEN) }.
- `CaptureRequest` { `Money? Amount`; `bool? FinalCapture` (default false) }.
- `ReauthorizeRequest` { `Money? Amount` (only supported param) }.
- `RefundRequest` { `Money? Amount` (omit for full refund); `string? CustomId`; `string? InvoiceId`; `string? NoteToPayer` }.
- `Address` (Models/Address.cs — billing) — all fields optional; used only to pass caller billing address.

### Response model fields read (source = each `Models/<Type>.cs`)

- `Order` { `string? Id` (id); `OrderStatus? Status` (status) }. (CreateOrder return)
- `OrderAuthorizeResponse` { `string? Id`; `OrderStatus? Status`; `IReadOnlyList<PurchaseUnit>? PurchaseUnits` }. `PurchaseUnit.Payments` → `PaymentCollection { IReadOnlyList<AuthorizationWithAdditionalData>? Authorizations; IReadOnlyList<OrdersCapture>? Captures; IReadOnlyList<Refund>? Refunds }`. `AuthorizationWithAdditionalData { AuthorizationStatus? Status; string? Id; Money? Amount; string? ExpirationTime }`.
- `CapturedPayment` { `CaptureStatus? Status`; `string? Id`; `Money? Amount`; `SellerReceivableBreakdown? SellerReceivableBreakdown` }. `SellerReceivableBreakdown { required Money GrossAmount; Money? PaypalFee; Money? NetAmount }` — the captured amount, PayPal's fee, net proceeds.
- `PaymentAuthorization` { `AuthorizationStatus? Status`; `string? Id`; `Money? Amount`; `string? ExpirationTime } (Reauthorize/Void/Get return; reauthorize yields a fresh authorization Id).
- `Refund` { `RefundStatus? Status`; `string? Id`; `Money? Amount` }.
- `SetupTokenResponse` { `string? Id`; `PaymentTokenStatus? Status` (default Created); `SetupTokenResponsePaymentSource? PaymentSource`; `IReadOnlyList<LinkDescription>? Links` }. If status/links imply payer approval required → **challenge, STOP & report** (task rule), don't build an approval round-trip.
- `PaymentTokenResponse` { `string? Id`; `PaymentTokenResponsePaymentSource? PaymentSource` } → `.Card` = `CardPaymentTokenEntity { string? Name; string? LastDigits (last_digits); CardBrand? Brand; string? Expiry }` — the SAFE display info to return/store (never PAN).
- `SearchResponse` { `IReadOnlyList<TransactionDetails>? TransactionDetails; int? Page; int? TotalPages; int? TotalItems }`. `TransactionDetails.TransactionInfo` = `TransactionInformation { string? TransactionId; Money? TransactionAmount; Money? FeeAmount; string? TransactionStatus; string? InvoiceId (invoice_id); string? CustomField (custom_field); string? TransactionInitiationDate }`. **Reconciliation link:** eShop stamps `custom_id`=orderId onto the PurchaseUnit → surfaces as `custom_field`; match transactions↔orders by that (fallback `invoice_id`).
- Case A typed payload `Error` (Models/Error.cs) { `required string Name; required string Message; required string DebugId (debug_id); IReadOnlyList<ErrorDetails>? Details }`.

### Enums (source = `Models/Enums/<Type>.cs`, values verbatim)

- `CheckoutPaymentIntent`: `.Capture`(CAPTURE), `.Authorize`(AUTHORIZE). Use **Authorize**.
- `OrderStatus`: Created, Saved, Approved, Voided, Completed, **PayerActionRequired**(PAYER_ACTION_REQUIRED).
- `AuthorizationStatus`: Created, Captured, Denied, PartiallyCaptured, Voided, Pending.
- `CaptureStatus`: Completed, Declined, PartiallyRefunded, Pending, Refunded, Failed.
- `RefundStatus`: Cancelled, Failed, Pending, Completed.
- `VaultTokenRequestType`: `.SetupToken`(SETUP_TOKEN). `TokenType`: only `.BillingAgreement`(BILLING_AGREEMENT).
- Enums are `StringEnum<T>` (not C# enums): use static members above or `T.FromValue("WIRE")`.

## 3. Trap notes (hazard + consequence + skill; NOT resolved here)

- **Order↔Authorize↔Capture body shapes & optional-field acceptance** — which optional fields the endpoint actually requires for a direct-card AUTHORIZE to be accepted (vs rejected) is semantics on the method `<remarks>`, not the field list. `MUST load paypal-platforms-team:dotnet-calling-endpoints` + reread `Api/Orders.cs`/`Api/Payments.cs` remarks if a call is rejected.
- **StringEnum + union/optional model building** — `StringEnum<T>` is not a C# enum and optional-vs-nullable init differs from plain records; building payloads wrong compiles but mis-serializes. `MUST load paypal-platforms-team:dotnet-models`.
- **Error boundary: two JsonException directions + Case A/B mix** — see REQUIRED READING; a Case-A-only or SdkException-only ladder is silently wrong. `MUST load paypal-platforms-team:dotnet-error-handling`.
- **Retry eligibility / per-attempt timeout / base-URL / pagination / body logging** — the knob names don't reveal what retries, what a timeout bounds, or that JSON bodies log unredacted. `MUST load paypal-platforms-team:dotnet-configuration-resilience`.
- **HttpClient/singleton lifetime & DI** — `AddPayPalServerSdkClient` uses `IHttpClientFactory`; lifetime rules aren't in the signature. `MUST load paypal-platforms-team:dotnet-client-initialization`.
- **Credential wiring / 401 meaning** — an unset credential is skipped, not thrown; a 401 can mean "nothing sent". `MUST load paypal-platforms-team:dotnet-authentication`.
- **Testing seam** — the `HttpClient` ctor arg is the fake seam. `MUST load paypal-platforms-team:dotnet-testing`.

## 4. REQUIRED READING (load ALL before implementing; sheet does NOT carry their contents)

- `paypal-platforms-team:dotnet-client-initialization` — SDK client construction + DI (step 1/2).
- `paypal-platforms-team:dotnet-authentication` — OAuth2 credential wiring (step 2).
- `paypal-platforms-team:dotnet-calling-endpoints` — every Orders/Payments/Vault/TransactionSearch call (step 4/5).
- `paypal-platforms-team:dotnet-models` — building request payloads / StringEnum / unions (step 4/5).
- `paypal-platforms-team:dotnet-error-handling` — exception boundary (step 4). **Always required.**
  - ⚠ A drifted/malformed **2xx** body (missing `required` member) surfaces as `System.Text.Json.JsonException` from deserialization, **not** `SdkException` — an SDK-exception-only catch ladder lets it escape.
  - ⚠ A **non-2xx** body that doesn't match its operation's generated `{Operation}Error` throws `JsonException` **while the error object is being constructed**, so it **replaces** the `SdkException` and the HTTP status is destroyed with it.
- `paypal-platforms-team:dotnet-configuration-resilience` — retries/timeout/base-URL/pagination/logging (step 2/4).
- `paypal-platforms-team:dotnet-testing` — test seam (tests).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` validated at startup (`ValidateOnStart`): host refuses to start if `ClientId`, `ClientSecret`, or `Currency` is null/whitespace. Both credential parts checked separately (blank ≠ missing). `Environment` defaults to `sandbox`; `BaseUrl` optional. |
| 2 | Secret sourcing & rotation | Secrets from **.NET user-secrets** (loaded from env vars by the operator/me, values never in repo). `AddPayPalServerSdkClient` builds the options object once at registration and captures it in the singleton ⇒ a rotated secret needs a **process restart**; documented, acceptable for this app. |
| 3 | Total timeout budget | SDK `Timeout` is **per-attempt**; the whole-call bound is a `CancellationToken`. Gateway derives a `CancellationTokenSource` (default 100s) per operation from the request's token and passes it as `ct:` so a hung retryable call can't stack per-attempt timeouts unbounded. (Confirm knob semantics via dotnet-configuration-resilience before tuning.) |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS ⇒ our POSTs (CreateOrder, AuthorizeOrder, Capture, Void, Refund, vault create) are **never auto-resent** by the SDK. Safe: each carries an idempotency key (row 5) so even a manual retry can't double-charge. Retries left at SDK default. |
| 5 | Idempotency & ambiguous writes | **CreateOrder/AuthorizeOrder/Capture/Void:** stable `PayPal-Request-Id` (`payPalRequestId`) derived from the eShop order id (e.g. `auth-{orderId}`, `cap-{orderId}`, `void-{orderId}`) + app-side state guard on `Payment.Status` (already-authorized/fulfilled/cancelled ⇒ return existing, no second call). **Refund:** caller-supplied idempotency key → used BOTH as `payPalRequestId` AND persisted per capture; a repeat key returns the stored `refundId` without calling PayPal; two DISTINCT keys = two legitimate partial refunds. App enforces Σrefunds ≤ captured amount. **Vault create:** stable per-request id. |
| 6 | Observability | `ILogger` logs op name + PayPal `debug_id` from `Error`/`RawError` at Warning/Error; success at Information (ids only, no bodies). `LogRequestBody` stays **off**. Provider `debug_id` is the correlation id carried into our logs and surfaced in `PaymentGatewayException`. |
| 7 | Sensitive data | Requests carry **PAN/CVV/expiry** (`CardRequest`, `SetupTokenRequestCard`). Therefore: `LogRequestBody` off; `options.Logging.LoggerFactory` set explicitly (via DI `ILoggerFactory`) so `PAYPALSERVERSDKCLIENT_LOG` env var can't force body logging on; our own code NEVER logs the card DTO, and card fields are read from the request and passed straight to the SDK — never persisted (only PayPal vault id + last4/brand/expiry stored) and never echoed in responses. |
| 8 | Environment selection | SDK declares only `ServerEnvironment.Sandbox`. Mapping (YOUR CALL — not in the map): `PayPal:Environment` in {sandbox} ⇒ `Sandbox`. If `PayPal:BaseUrl` set ⇒ used verbatim for all calls incl. token (overrides base URL). If `Environment` is a non-sandbox value AND no `BaseUrl` ⇒ **fail fast** at startup (SDK has no live host; refuse rather than silently hit sandbox). All dev/test → sandbox. |

## Verified behaviours (from live sandbox self-verification)

- **Create-with-card + `intent=AUTHORIZE` auto-authorizes.** The hold is on the `CreateOrder`
  response's `purchase_units[].payments.authorizations[]`; a separate `AuthorizeOrder` then returns
  `ORDER_ALREADY_AUTHORIZED`. So the gateway reads the authorization off the create response and only
  calls `AuthorizeOrder` as a fallback when it is absent.
- **`VoidPayment` returns HTTP 204 with no body**, which the SDK then fails to deserialize into
  `PaymentAuthorization` — surfacing as a `JsonException` that means SUCCESS here. `VoidAsync` treats an
  empty-body `JsonException` as success and only an `SdkException<VoidPaymentError>` as failure.
- **`PayPal-Request-Id` idempotency is account-wide and durable** (a reused value replays or is rejected
  `DUPLICATE_REQUEST_ID`). Keys are therefore derived from a per-payment `IdempotencySeed` (a GUID), not
  from the resettable order id, so they never collide across in-memory restarts; the refund key is
  namespaced per payment so the same caller key stays globally unique across captures.

## 6. Assumptions & Blockers

- **No blockers.** Every capability the task needs maps to an SDK operation above. Verified live on the
  sandbox: authorize, capture (with fee/net), full+partial refunds, saved-card reuse, void, reconciliation.
- Assumption (minor): direct-card AUTHORIZE and card vaulting on this sandbox complete **without** a
  3DS/browser challenge (task states the account is enabled for both). If PayPal returns
  `PAYER_ACTION_REQUIRED`/an approval link, the gateway raises a clear "challenge required" error and
  the flow STOPS — per task rule, no approval round-trip is built.
- Assumption (minor): amounts formatted to 2 decimals (config currency = USD). Helper formats the
  order total with invariant culture, 2 fraction digits, so PayPal's held/captured amount equals the
  order total to the cent.
- Assumption (minor): with the in-memory provider each host has its own store; the whole flow is
  driven through PublicApi's `POST /api/orders`, so orders/payments/cards live in one PublicApi run.

## 7. Source labels — every §2 row cites its map page or declaring file (above). Rows without an SDK
source are application decisions: §1 layout, §5 rows 1-7, and the `Environment` mapping in §5-row8 are
**YOUR CALL — not in the map**; the sandbox-only environment fact and the BaseUrl/token resolution are
map/source facts (cited). No row is left open for a later lookup.
