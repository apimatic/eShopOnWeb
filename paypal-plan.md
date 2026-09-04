# PayPal integration plan + contract sheet — eShopOnWeb (PayPalServerSdk .NET)

Scope: server-to-server card payments (direct + vaulted), authorize → capture → refund lifecycle, card vaulting, transaction reconciliation, all through the APIMatic-generated `PayPalServerSdk` SDK. Target: sandbox. Read this sheet top-to-bottom once before writing code — every signature, wire name, enum member and accessor below is final and was taken from the bundled SDK map (pages cited in each row) or, where the map names a source file, that file.

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|---|---|
| 0 | Add package `AsadAli.Checkout.Sdk` **version `1.0.1`** to the project that calls PayPal (see §2.1). | — |
| 1 | `PayPal:` settings section → `PayPalOptions` (ClientId, ClientSecret, Environment, Currency, BaseUrl) + registration (§2.2). | — |
| 2 | Client construction & DI registration with OAuth2 credentials, sandbox environment, optional base-URL override (§2.3). | — (token request is internal; §3.1) |
| 3 | Write the PayPal error boundary first — every operation throws `SdkException<T>` (§3.10); map to app exceptions. | — |
| 4 | Vault a card: `client.Vault.CreatePaymentToken` (full card) → persist token id + display data (`CardPaymentTokenEntity`). List/delete tokens: `ListCustomerPaymentTokens`, `DeletePaymentToken`, `GetPaymentToken`. | Vault |
| 5 | Create order with `intent: AUTHORIZE` and `payment_source.card` — full card (`CardRequest.Number/Expiry/SecurityCode/BillingAddress`) or vaulted (`CardRequest.VaultId`). Detect and STOP on `PAYER_ACTION_REQUIRED`. | Orders.CreateOrder |
| 6 | Authorize: `client.Orders.AuthorizeOrder` — read authorization id/status from `PurchaseUnits[0].Payments.Authorizations[0]`. Detect card challenge → STOP. | Orders.AuthorizeOrder |
| 7 | Capture: `client.Payments.CaptureAuthorizedPayment` — read `SellerReceivableBreakdown` (gross / paypal fee / net). | Payments.CaptureAuthorizedPayment |
| 8 | Void (`Payments.VoidPayment`) and reauthorize (`Payments.ReauthorizePayment`) an authorization. | Payments |
| 9 | Refund a capture: `client.Payments.RefundCapturedPayment` with caller-supplied idempotency key (`payPalRequestId`). | Payments.RefundCapturedPayment |
| 10 | Reconciliation: `client.TransactionSearch.SearchTransactions` — page through the whole range via `page`/`TotalPages`. | TransactionSearch.SearchTransactions |
| 11 | Tests over the integration layer (HttpClient seam). | — |

## 2. Package, settings, client

### 2.1 NuGet package (exact id + pinned version)

| Fact | Value | Source |
|---|---|---|
| Package id | `AsadAli.Checkout.Sdk` | sdk-map.md (SDK identity) |
| Pinned version | `1.0.1` — the release tag the bundled map documents (map source-commit stamp `9653d18`, tagged `v1.0.1`). The map's own install guidance is "install version-less to float"; the pinned release this map was generated from is `1.0.1`. | sdk-map.md (stamp), paypal-getting-started (Install) |
| Target framework of package | `netstandard2.0`, C# LangVersion 14, Nullable enable | sdk-map.md |

### 2.2 Settings mapping

`PayPal:` config section bound to a `PayPalOptions` class (bind by binding key, not raw env vars):

```json
"PayPal": {
  "ClientId": "<rest-app-client-id>",
  "ClientSecret": "<rest-app-secret>",
  "Environment": "Sandbox",
  "Currency": "USD",
  "BaseUrl": ""            // optional; when set, used verbatim for EVERY call incl. token
}
```

| Key | Type | Use | Map/source |
|---|---|---|---|
| `ClientId` / `ClientSecret` | string | `OAuth2ClientCredentials.ClientId` / `.ClientSecret` (both `required`) | OAuth2ClientCredentials.cs |
| `Environment` | string | selects `ServerEnvironment` — **only `ServerEnvironment.Sandbox` exists** in this SDK (§3.11) | ServerEnvironment.cs |
| `Currency` | string | fills every `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode` (`!req`, 3-char ISO string) | records-1 (Money, AmountWithBreakdown) |
| `BaseUrl` | string? | when set: `options.Server.Default.Sandbox.BaseUrl = <value>` — base for ALL calls + the token request (§3.11) | DefaultOptions.cs |

### 2.3 Client construction & lifetime

```csharp
using PayPalServerSdk;
using PayPalServerSdk.Servers;                       // ServerEnvironment
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials; // OAuth2ClientCredentials

var options = new PayPalServerSdkClientOptions
{
    Environment = ServerEnvironment.Sandbox,        // the ONLY member of ServerEnvironment
    Oauth2 = new OAuth2ClientCredentials
    {
        ClientId = cfg.ClientId,
        ClientSecret = cfg.ClientSecret,
        // Scope = ... optional
    }
};
if (!string.IsNullOrWhiteSpace(cfg.BaseUrl))
    options.Server.Default.Sandbox.BaseUrl = cfg.BaseUrl;   // ALL calls + token request
var client = new PayPalServerSdkClient(httpClient, options); // httpClient: System.Net.Http.HttpClient
```

DI alternative (extension method in the root `PayPalServerSdk` namespace):

```csharp
services.AddPayPalServerSdkClient(o => { /* same option assignments */ });
```

The extension (ServiceCollectionExtensions.cs) calls `services.AddHttpClient()`, then registers a **singleton** `PayPalServerSdkClient` built from `IHttpClientFactory.CreateClient()`. The `Oauth2` token cache lives inside the client instance — the singleton registration is what makes the access token reused across requests; a transient client would re-fetch a token per request.

Facts about the DI extension you must know before wiring it: it is declared with the C# 14 **extension-block** syntax (`extension(IServiceCollection services)`) — an app compiler that cannot consume extension blocks must call it as a static method or skip it and build the client manually with the public constructor above.

### 2.4 Base-URL override — coverage and bypass

| Question | Answer | Source |
|---|---|---|
| Where does the override plug in? | `options.Server.Default.Sandbox.BaseUrl` (class `DefaultOptions.SandboxOptions`, nested in `DefaultOptions`; `ServerOptions.Default` is the property). Default value `"https://api-m.sandbox.paypal.com"`. | DefaultOptions.cs |
| Does it cover EVERY call? | Yes — every operation builds its URL via `server.Default(path)` → `DefaultOptions.Resolve` → `new UrlTemplate(Sandbox.BaseUrl, path, [])`. | PayPalServerSdkClient.cs, Server.cs, DefaultOptions.cs |
| Does it cover the OAuth token request? | Yes — the default token strategy is built as `OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), rawClient)`, i.e. the token URL is `{BaseUrl}/v1/oauth2/token`. | AuthSchemes.cs, OAuth2ClientCredentialsStrategy.cs |
| "Verbatim" semantics | The override replaces the environment-derived **base**; the SDK still appends each operation's path: final URL = `baseUrl.TrimEnd('/') + "/" + path.TrimStart('/')` (so a BaseUrl of `https://proxy.example/paypal` yields `…/paypal/v2/checkout/orders`, `…/paypal/v1/oauth2/token`). | TemplateParamsFactory.cs |
| Any per-call bypass risk? | No per-call escape hatch exists: `RequestOptions` (the `requestOptions` param on every operation) carries only `LogLevel?` — no URL, no retry override. The **only** bypass is supplying a custom `Oauth2TokenStrategy` (`IOAuth2TokenStrategy<OAuth2ClientCredentials>`); its `GetToken` decides its own URL and ignores the server override. | RequestOptions.cs |

## 3. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**

> **Every SDK type is written fully-qualified with the namespace the map gives it**
> — take each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

Namespace map (add a `using` per row you touch):

| Contents | Namespace |
|---|---|
| Client, options, `ServerOptions`, DI extension | `PayPalServerSdk` |
| Controllers (`Orders`, `Payments`, `Vault`, `TransactionSearch`) | `PayPalServerSdk.Api` |
| `ServerEnvironment`, `DefaultOptions` | `PayPalServerSdk.Servers` |
| Records (requests/responses, `Error`, `Error1`, `RawError` payloads are records too) | `PayPalServerSdk.Models` |
| Enums (`StringEnum<T>`) | `PayPalServerSdk.Models.Enums` |
| Typed error classes (`CreateOrderError`, …) | `PayPalServerSdk.Errors` |
| `SdkException<TError>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `IOAuth2TokenStrategy<T>`, `OAuthToken` | `PayPalServerSdk.Core.Authentication.OAuth2` |

**Prefer header — read this once.** Every write operation defaults `prefer = "return=minimal"`, which the SDK documents as "a minimal response… includes the id, status and HATEOAS links" — **not** the nested fields this integration reads (purchase_units, seller_receivable_breakdown, …). Pass `prefer: "return=representation"` on every call whose response body you consume beyond `Id`/`Status`/`Links` (AuthorizeOrder, CaptureAuthorizedPayment, RefundCapturedPayment, ReauthorizePayment, VoidPayment, and CreateOrder if you read beyond id/status). Source: Api/Orders.cs doc comment on the `prefer` param.

### 3.1 OAuth access token (no explicit operation — internal)

This SDK exposes **no** token-endpoint operation. The client-credentials token exchange happens inside the SDK on the first authenticated call and is cached thereafter.

| Fact | Value | Source |
|---|---|---|
| Token request | `POST {BaseUrl}/v1/oauth2/token`; form body `grant_type=client_credentials` (+ optional `scope`); `Authorization: Basic base64(clientId:clientSecret)`; auto `Idempotency-Key: Guid` header | OAuth2ClientCredentialsStrategy.cs, AuthSchemes.cs |
| Wiring | `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = … }` (both `required`); optional `Scope: string?` | OAuth2ClientCredentials.cs |
| Strategy override | `options.Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?` — replace the default (custom URL/flow); **this is the only way the token request bypasses the BaseUrl override** | PayPalServerSdkClientOptions.cs |
| Caching | Built into `OAuth2Scheme`: per-client cache, single-flight (AsyncLock), reuse until `IsExpired` = `expires_in − 30 s`; if the server omits `expires_in` the token is never considered expired by the client | OAuth2Scheme.cs, OAuthToken.cs |
| Consequence | Keep the client **singleton** (as the DI extension does) so the token cache is app-wide; a per-request client re-fetches a token every time | ServiceCollectionExtensions.cs |
| Error at token time | A 401 on any call means the token itself failed to be obtained/refreshed — check credentials + base URL first | — |

### 3.2 Create order — `client.Orders.CreateOrder`

**HTTP**: `POST /v2/checkout/orders` · **Returns**: `Order` · **Source**: operations/Orders.md (CreateOrder row), Api/Orders.cs; records-1 (OrderRequest, PurchaseUnitRequest, AmountWithBreakdown, Money, CardRequest, Address).

```
Task<Order> CreateOrder(
    string? payPalMockResponse,          // pass null
    string? payPalRequestId,             // pass YOUR stable key — mandatory for single-step card create (see note)
    string? payPalPartnerAttributionId,  // pass null
    string? payPalClientMetadataId,      // pass null
    string? payPalAuthAssertion,         // pass null
    OrderRequest body,                   // required (non-nullable)
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Request body `OrderRequest` (`PayPalServerSdk.Models`):

| Field (wire_name) | Type | Required |
|---|---|---|
| `Intent (intent)` | `CheckoutPaymentIntent` | `!req` → use `CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`) |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | `!req` |
| `Payer (payer)` | `Payer?` | no |
| `PaymentSource (payment_source)` | `PaymentSource?` | no |
| `ApplicationContext (application_context)` | `OrderApplicationContext?` | no |

`PurchaseUnitRequest` fields used: `ReferenceId (reference_id): string?` ← merchant/custom reference id; `Amount (amount): AmountWithBreakdown !req`; `CustomId (custom_id): string?`; `InvoiceId (invoice_id): string?`. `AmountWithBreakdown`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?`. `Money`: `CurrencyCode !req`, `Value !req` (**`Value` is a decimal-formatted string**, e.g. `"12.34"` — app converts).

`PaymentSource.Card` — two variants:
- (a) **full card**: `CardRequest { Name (name): string?, Number (number): string?, Expiry (expiry): string?, SecurityCode (security_code): string?, BillingAddress (billing_address): Address? }`. `Address`: `AddressLine1?, AddressLine2?, AdminArea2?, AdminArea1?, PostalCode?, CountryCode (country_code): string !req`. `Expiry` is a string; the wire format (e.g. `YYYY-MM`) is **not** stated in the map — `UNVERIFIED`, format per PayPal convention.
- (b) **vaulted card**: `CardRequest { VaultId (vault_id): string? = "<payment token id from §3.8>" }`. **Do NOT use `payment_source.token` for this** — this SDK's `TokenType` enum has only `BillingAgreement (BILLING_AGREEMENT)`; the vaulted-card path is `card.vault_id`. (records-1 CardRequest row; enums.md TokenType row)

Response `Order` (fields the integration reads): `Id (id): string?`, `Status (status): OrderStatus?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `Links (links)`. `PurchaseUnit` (response): `ReferenceId?, Amount?, CustomId?, Payments (payments): PaymentCollection?`, … `PaymentCollection`: `Authorizations: IReadOnlyList<AuthorizationWithAdditionalData>?`, `Captures: IReadOnlyList<OrdersCapture>?`, `Refunds: IReadOnlyList<Refund>?`.

**Challenge detection (STOP, do not loop):** `Order.Status == OrderStatus.PayerActionRequired` (wire `PAYER_ACTION_REQUIRED`), or a 422 error whose `Error.Details[].Issue == "PAYER_ACTION_REQUIRED"` (wire value grounded in the OrderStatus enum). On either → abort the flow, never retry. Source: enums.md (OrderStatus), operations/Orders.md (CreateOrder error statuses).

Error: `SdkException<CreateOrderError>` — **Case A (typed)** · `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback]. `Error` (`PayPalServerSdk.Models`): `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`, `Links?`. `ErrorDetails`: `Field?, Value?, Location? = "body"`, `Issue (issue): string !req`, `Description?, Links?`.

Notes (provider prose): `payPalRequestId` is **mandatory** for single-step create-order calls with a card payment source, including `card.vault_id` (Api/Orders.cs doc comment). Server stores order idempotency keys for **6 hours**. Pagination: none.

### 3.3 Authorize an order — `client.Orders.AuthorizeOrder`

**HTTP**: `POST /v2/checkout/orders/{id}/authorize` · **Returns**: `OrderAuthorizeResponse` · **Source**: operations/Orders.md (AuthorizeOrder row), records-1 (OrderAuthorizeRequest, OrderAuthorizeRequestPaymentSource, OrderAuthorizeResponse), records-2 (PurchaseUnit, PaymentCollection), enums.md.

```
Task<OrderAuthorizeResponse> AuthorizeOrder(
    string id,
    string? payPalMockResponse,          // pass null
    string? payPalRequestId,             // pass YOUR stable key (optional here; mandatory on card create)
    string? payPalClientMetadataId,      // pass null
    string? payPalAuthAssertion,         // pass null
    OrderAuthorizeRequest? body,         // pass null when the order is already APPROVED with a valid payment_source
    string? prefer = "return=minimal",   // → pass "return=representation" (you read nested fields)
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

`OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` → `Card (card): CardRequest?` (re-supply full card or `VaultId` when the order was not buyer-approved; provider note: "the buyer must first approve the order **or a valid payment_source must be provided in the request**").

Response `OrderAuthorizeResponse` (same envelope as `Order`): `Id?, Status: OrderStatus?, PurchaseUnits: IReadOnlyList<PurchaseUnit>?, Links?, …`.

**Where the authorization id/status live** (the integration reads): `response.PurchaseUnits[0].Payments.Authorizations[0]` → `AuthorizationWithAdditionalData`: `Id (id): string?` ← authorization id for §3.4/§3.5/§3.6; `Status (status): AuthorizationStatus?`; `StatusDetails (status_details): AuthorizationStatusDetails?` → `Reason: AuthorizationIncompleteReason?`; `ExpirationTime (expiration_time): string?`; `Amount: Money?`; `Links`. (records-1 AuthorizationWithAdditionalData; records-2 PaymentCollection/PurchaseUnit.)

`AuthorizationStatus` (enums.md): `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)`. `AuthorizationIncompleteReason`: `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)`.

**Card challenge in this flow:** surfaces as `OrderAuthorizeResponse.Status == OrderStatus.PayerActionRequired` (wire `PAYER_ACTION_REQUIRED`) or as a 422 `SdkException<AuthorizeOrderError>` → `TryGetError(out Error)` → scan `Error.Details[].Issue` for `"PAYER_ACTION_REQUIRED"`. On either → STOP, never loop/retry.

Error: `SdkException<AuthorizeOrderError>` — **Case A** · `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]. Pagination: none.

### 3.4 Capture an authorization — `client.Payments.CaptureAuthorizedPayment`

**HTTP**: `POST /v2/payments/authorizations/{authorization_id}/capture` · **Returns**: `CapturedPayment` · **Source**: operations/Payments.md (CaptureAuthorizedPayment row), records-1 (CapturedPayment, CaptureRequest, Money), records-2 (SellerReceivableBreakdown), enums.md.

```
Task<CapturedPayment> CaptureAuthorizedPayment(
    string authorizationId,
    string? payPalMockResponse,          // pass null
    string? payPalRequestId,             // pass YOUR stable key (dedup; server stores keys 45 days)
    string? payPalAuthAssertion,         // pass null
    CaptureRequest? body,                // pass null for FULL capture
    string? prefer = "return=minimal",   // → pass "return=representation" (you read SellerReceivableBreakdown)
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

`CaptureRequest`: `Amount (amount): Money?` (omit → full capture; set for partial), `InvoiceId?, FinalCapture (final_capture): bool? = false`, `PaymentInstruction?, NoteToPayer?, SoftDescriptor?`.

Response `CapturedPayment` — **gross / fee / net**:
`Id?, Status: CaptureStatus?, Amount: Money?, SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?, CreateTime?, UpdateTime?, Links?, …` with `SellerReceivableBreakdown`: `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?`, `PaypalFeeInReceivableCurrency?, ReceivableAmount?, ExchangeRate?, PlatformFees?`. Each `Money`: `.CurrencyCode`, `.Value` (string). Provider note: breakdown "is not available for transactions that are in pending state" — treat `null` breakdown on `Pending` captures as expected.

`CaptureStatus` (enums.md): `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)`.

Error: `SdkException<CaptureAuthorizedPaymentError>` — **Case A** · `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]. Pagination: none.

### 3.5 Reauthorize an authorization — `client.Payments.ReauthorizePayment`

**HTTP**: `POST /v2/payments/authorizations/{authorization_id}/reauthorize` · **Returns**: `PaymentAuthorization` · **Source**: operations/Payments.md (ReauthorizePayment row), records-2 (ReauthorizeRequest, PaymentAuthorization), enums.md.

```
Task<PaymentAuthorization> ReauthorizePayment(
    string authorizationId,
    string? payPalRequestId,             // pass YOUR stable key
    string? payPalAuthAssertion,         // pass null
    ReauthorizeRequest? body,
    string? prefer = "return=minimal",   // → "return=representation" if you read nested fields
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

`ReauthorizeRequest`: `Amount (amount): Money?` — provider note: "Supports only the `amount` request parameter."

Response `PaymentAuthorization`: `Id?, Status: AuthorizationStatus?, StatusDetails?, Amount?, ExpirationTime?, Links?, …`.

**When reauthorization is no longer possible** (provider prose, operations/Payments.md Notes): reauthorize only within days 4–29 after the initial 3-day honor period; "If 30 days have transpired since the date of the original authorization, you must create an authorized payment instead of reauthorizing." Failure surfaces as a 4xx — `TryGetError(out Error)` statuses [400, 401, 403, 404, 422]; the exact issue code for "too late to reauthorize" is **not** in the map — detect via status 422 + `Details[].Issue`, exact string `UNVERIFIED`. Internal doc conflict flagged: the operation Notes say "you can issue multiple re-authorizations after the honor period expires" while the `ReauthorizeRequest` summary says "You can reauthorize a payment only once from days four to 29" — the two generated docs disagree; treat reauthorize as best-effort and rely on the response status/error to decide (`UNVERIFIED` which live behavior holds).

Error: `SdkException<ReauthorizePaymentError>` — **Case A** · `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]. Pagination: none.

### 3.6 Void an authorization — `client.Payments.VoidPayment`

**HTTP**: `POST /v2/payments/authorizations/{authorization_id}/void` · **Returns**: `PaymentAuthorization` · **Source**: operations/Payments.md (VoidPayment row), records-2 (PaymentAuthorization), enums.md.

```
Task<PaymentAuthorization> VoidPayment(
    string authorizationId,
    string? payPalMockResponse,          // pass null
    string? payPalAuthAssertion,         // pass null
    string? payPalRequestId,             // pass YOUR stable key
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

No body. On success the hold is released — `PaymentAuthorization.Status` should read `AuthorizationStatus.Voided` (wire `VOIDED`).

Constraint (provider note): "You cannot void an authorized payment that has been fully captured." An already-captured authorization answers with **409** — the error status list includes 409 among [401, 403, 404, 409, 422]; the exact issue code string is **not** in the map (`UNVERIFIED` — detect 409 + `Details[].Issue`).

Error: `SdkException<VoidPaymentError>` — **Case A** · `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]. Pagination: none.

### 3.7 Refund a capture — `client.Payments.RefundCapturedPayment`

**HTTP**: `POST /v2/payments/captures/{capture_id}/refund` · **Returns**: `Refund` · **Source**: operations/Payments.md (RefundCapturedPayment row), records-2 (RefundRequest, Refund, SellerPayableBreakdown), enums.md, Api/Payments.cs.

```
Task<Refund> RefundCapturedPayment(
    string captureId,
    string? payPalMockResponse,          // pass null
    string? payPalRequestId,             // ← THE idempotency key (PayPal-Request-Id header)
    string? payPalAuthAssertion,         // pass null
    RefundRequest? body,                 // pass null for FULL refund
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

`RefundRequest`: `Amount (amount): Money?` (set for partial refund), `CustomId?, InvoiceId?, NoteToPayer?, PaymentInstruction?`. Provider note: "For a full refund, include an empty payload in the JSON request body. For a partial refund, include an amount object."

**Idempotency (exact mechanism):** the caller-supplied key is the `payPalRequestId` parameter — the SDK sends it as the **`PayPal-Request-Id`** header (verified in Api/Orders.cs and Api/Payments.cs: `new HeaderParam("PayPal-Request-Id", payPalRequestId)`). Re-send the **same string** on retry → PayPal does not apply the refund twice. Server stores the key for **45 days** (Api/Payments.cs doc comment). ⚠ The SDK *also* auto-adds a fresh `Idempotency-Key: Guid.NewGuid()` header on every POST — that header is **not** your dedup mechanism; it changes per call. Dedup comes only from a stable `payPalRequestId`.

Response `Refund`: `Id?, Status: RefundStatus?, StatusDetails?, Amount: Money?, SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?, CreateTime?, UpdateTime?, Links?`. `SellerPayableBreakdown`: `GrossAmount?, PaypalFee?, PaypalFeeInReceivableCurrency?, NetAmount?, NetAmountInReceivableCurrency?, PlatformFees?, NetAmountBreakdown?, TotalRefundedAmount?`.

`RefundStatus` (enums.md): `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)`.

Error: `SdkException<RefundCapturedPaymentError>` — **Case A** · `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]. Pagination: none.

### 3.8 Vault — `client.Vault`

All Vault paths are **v3** (`/v3/vault/…`) in this SDK, not v2. **Source**: operations/Vault.md; records-1 (PaymentTokenRequest*, CustomerVaultPaymentTokensResponse, CardPaymentTokenEntity), records-2 (PaymentTokenResponse*, CardPaymentTokenEntity is records-1), enums.md.

**Create a payment token from full card details:**

```
Task<PaymentTokenResponse> CreatePaymentToken(
    string? payPalRequestId,             // idempotency key → PayPal-Request-Id; server stores keys 3 HOURS
    PaymentTokenRequest body,            // required
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

`PaymentTokenRequest`: `Customer (customer): Customer?` (`Id?`, `MerchantCustomerId?`), `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` → `Card (card): PaymentTokenRequestCard?`. `PaymentTokenRequestCard`: `Name?, Number?, Expiry?, SecurityCode?, Brand (brand): CardBrand?, BillingAddress: Address?` (no `vault_id` on the vault-request card).

Response `PaymentTokenResponse`: `Id (id): string?` ← **payment token id** (feed into `CardRequest.VaultId` in §3.2b); `Customer: CustomerResponse?`; `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `Card (card): CardPaymentTokenEntity?`; `Links?`. `CardPaymentTokenEntity` — the safe-to-display fields: `Name?, LastDigits (last_digits): string?`, `Brand (brand): CardBrand?`, `Expiry (expiry): string?`, `BillingAddress: CardResponseAddress?`, `VerificationStatus (verification_status): CardVerificationStatus?`, `BinDetails?, Type: CardType?`. (`CardVerificationStatus`: `Verified (VERIFIED)`, `Failed (FAILED)`; `CardType`: `Credit, Debit, Prepaid, Store, Unknown`; `CardBrand` wire values include `VISA`, `MASTERCARD`, `AMEX`, `DISCOVER`, … — enums.md.) **Never store/persist `Number`/`SecurityCode` from the response — the response model carries only display data.**

Error: `SdkException<CreatePaymentTokenError>` — **Case A** · `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]. `Error1` (`PayPalServerSdk.Models`): `Name !req`, `Message !req`, `DebugId !req`, `Details (details): IReadOnlyList<ErrorDetails1>?`, `Links (links): IReadOnlyList<ErrorLinkDescription>?` (`ErrorDetails1`: `Field?, Value?, Location? = "body"`, `Issue !req`, `Description?`). Pagination: none.

**List a customer's tokens:**

```
Task<CustomerVaultPaymentTokensResponse> ListCustomerPaymentTokens(
    string customerId,                   // required — no payPalRequestId on this op
    int? pageSize = 5,
    int? page = 1,
    bool? totalRequired = false,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
Query wires: `customer_id`, `page_size`, `page`, `total_required`. Response: `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Customer (customer): VaultResponseCustomer?`, `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `Links?`. Error: `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400, 403, 500]. Pagination: none (SDK helper — only `page`; drive the loop).

**Delete a token:**

```
Task DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)   // void return
```
HTTP `DELETE /v3/vault/payment-tokens/{id}`. Error: `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 500]. **No `payPalRequestId` on delete.** Pagination: none.

**Read one token (supporting):** `Task<PaymentTokenResponse> GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `GET /v3/vault/payment-tokens/{id}`; error `SdkException<GetPaymentTokenError>` — `TryGetError1(out Error1)` [403, 404, 422, 500].

### 3.9 Transaction report — `client.TransactionSearch.SearchTransactions`

**HTTP**: `GET /v1/reporting/transactions` · **Returns**: `SearchResponse` · **Source**: operations/TransactionSearch.md (SearchTransactions row), records-2 (SearchResponse, TransactionDetails, TransactionInformation, PayerInformation, CartInformation, ItemDetails, PayerName), records-1 (Money), enums.md.

```
Task<SearchResponse> SearchTransactions(
    string startDate,                    // required — ISO-8601 UTC, e.g. "2026-09-01T00:00:00Z" (wire start_date)
    string endDate,                      // required — ISO-8601 UTC (wire end_date)
    string? transactionId,               // pass null
    string? transactionType,             // pass null
    string? transactionStatus,           // pass null
    string? transactionAmount,           // pass null
    string? transactionCurrency,         // pass null
    string? paymentInstrumentType,       // pass null
    string? storeId,                     // pass null
    string? terminalId,                  // pass null
    string? fields = "transaction_info", // default "transaction_info" — pass the token list you need; the accepted values beyond the default are NOT enumerated in the map (UNVERIFIED)
    string? balanceAffectingRecordsOnly = "Y",  // default "Y" — decide per reconciliation needs
    int? pageSize = 100,
    int? page = 1,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```
All 8 params from `transactionId` to `terminalId` are nullable with **no default → must pass explicitly** (pass `null`). Wire names for the query params: `start_date`, `end_date`, `transaction_id`, `transaction_type`, `transaction_status`, `transaction_amount`, `transaction_currency`, `payment_instrument_type`, `store_id`, `terminal_id`, `fields`, `balance_affecting_records_only`, `page_size`, `page`.

Response `SearchResponse` — **pagination fields**: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `AccountNumber?, StartDate?, EndDate?, LastRefreshedDatetime?, Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Links?`.

`TransactionDetails`: `TransactionInfo (transaction_info): TransactionInformation?`, `PayerInfo (payer_info): PayerInformation?`, `ShippingInfo?, CartInfo (cart_info): CartInformation?`, `StoreInfo?, AuctionInfo?, IncentiveInfo?`.

`TransactionInformation` — the reconciliation fields (**names differ from the brief — see gap note below**): `PaypalAccountId?, TransactionId (transaction_id): string?`, `PaypalReferenceId (paypal_reference_id): string?`, `PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?`, `TransactionEventCode (transaction_event_code): string?`, `TransactionInitiationDate?, TransactionUpdatedDate?, TransactionAmount (transaction_amount): Money?`, `FeeAmount (fee_amount): Money?`, `DiscountAmount?, InsuranceAmount?, SalesTaxAmount?, ShippingAmount?, TransactionStatus (transaction_status): string?`, `InvoiceId?, CustomField?, …`. ⚠ **This SDK model has NO `gross_amount` / `paypal_fee_amount` / `net_amount` members** — it models `transaction_amount` + `fee_amount`, and there is **no net-amount member at all**. Read `TransactionAmount` and `FeeAmount`; the brief's field names do not exist on the model (map divergence, flagged).

`PayerInformation`: `AccountId?, EmailAddress?, PhoneNumber?, AddressStatus?, PayerStatus?, PayerName (payer_name): PayerName?`, `CountryCode?, Address?`. `CartInformation`: `ItemDetails (item_details): IReadOnlyList<ItemDetails>?` (the brief's "transaction_items" ⇒ this SDK's `item_details`), `TaxInclusive: bool? = false`, `PaypalInvoiceId?`. `ItemDetails`: `ItemCode?, ItemName?, ItemDescription?, ItemOptions?, ItemQuantity?, ItemUnitPrice: Money?, ItemAmount: Money?, DiscountAmount?, AdjustmentAmount?, GiftWrapAmount?, TaxAmounts?, BasicShippingAmount?, ExtraShippingAmount?, HandlingAmount?, InsuranceAmount?, TotalItemAmount?, InvoiceNumber?, CheckoutOptions?`.

`PayPalReferenceIdType` (enums.md): `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` — `transaction_event_code` itself is a plain `string?` (no enum in this SDK).

**Pagination — page through the WHOLE range:** there is no SDK paging helper (operations page: "none (only `page`, no `perPage`)"). Loop `page` from 1 while `page <= response.TotalPages` (or until a page returns fewer than `pageSize` rows); `TotalItems` gives the full count; `Page` echoes the current page. Defaults `pageSize = 100`, `page = 1`.

Error: **Case B** — `SdkException<RawError>` · accessors on `ex.Error`: `StatusCode: HttpStatusCode`, `ReadAsBytes(): ReadOnlyMemory<byte>`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`. There are **no** typed accessors on this operation. Pagination: none (caller-driven).

### 3.10 Error handling — everything you catch

| Fact | Value | Source |
|---|---|---|
| Throw model | Every operation is **throw-only** (no `…Result` variants in this SDK) and throws `SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`), exposing `.Error` of type `TError`. | sdk-map.md (error-handling model, op-stats) |
| Case A (39 of 40 ops) | `TError` = generated `{Operation}Error` (`PayPalServerSdk.Errors`) with status-specific `TryGet…(out …)` accessors + inherited `TryGetRawError(out RawError)`. The accessor tells you the status: e.g. `TryGetError(out Error)` covers the statuses listed in that op's row; `TryGetNoContent(out RawError)` covers 500 on Payments ops; `TryGetRawError(out RawError)` is the fallback. | sdk-map.md; per-op rows |
| Case B (1 op) | `SearchTransactions` → `SdkException<RawError>`; `RawError` (`PayPalServerSdk.Core.ErrorResponse`): `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. | operations/TransactionSearch.md |
| Structured body (Case A payloads) | `Error`/`Error1`: `Name (name)`, `Message (message)`, `DebugId (debug_id)`, `Details[]` with `Issue (issue) !req` (+ `Field`, `Value`, `Location`, `Description`). Issue codes are the per-detail machine-readable keys (e.g. `PAYER_ACTION_REQUIRED` — the only issue string this sheet grounds, via the OrderStatus enum wire value). | records-1 (Error, Error1, ErrorDetails, ErrorDetails1) |
| Status → meaning | 404 → not found (every get/find op lists 404: GetOrder, GetAuthorizedPayment, GetCapturedPayment, GetRefund, GetPaymentToken); 422 → unprocessable/validation (all create/authorize/capture/refund/reauthorize ops); 409 → conflict (capture/void/refund — e.g. void of a fully-captured authorization, §3.6); 401/403 → authentication/authorization; 400 → bad request; 500 → `TryGetNoContent(out RawError)` (no body) on Payments ops. | per-op error status lists |
| Read the status code safely | Use the accessors, not `Exception.ToString()`. Case B: `ex.Error.StatusCode` + `ReadAsString()`/`ReadAsJson<T>()`. | dotnet-error-handling (load it) |

**First-sheet mandatory hazards — the boundary must handle both directions:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

### 3.11 Server & auth facts

| Fact | Value | Source |
|---|---|---|
| Environments | `ServerEnvironment` has **one member: `ServerEnvironment.Sandbox`** (wire `"Sandbox"`). There is **no `Live` member** — the client cannot select live via `Environment`; targeting live requires overriding `Server.Default.Sandbox.BaseUrl` to the live origin. `Environment` defaults to `ServerEnvironment.Default()` = Sandbox. | ServerEnvironment.cs |
| Default base URL | `https://api-m.sandbox.paypal.com` (the `DefaultOptions.SandboxOptions.BaseUrl` default) | DefaultOptions.cs |
| Auth scheme | OAuth2 client-credentials; every operation sends `Authorization: Bearer <token>` from the built-in scheme (`[_auth.Oauth2]` on every call) | Api/Orders.cs etc. |
| Credentials property | `options.Oauth2: OAuth2ClientCredentials?` — `ClientId`/`ClientSecret` both `required`; `Scope: string?` optional | OAuth2ClientCredentials.cs |
| Token strategy property | `options.Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?` — custom strategy bypasses the BaseUrl override for the token request | PayPalServerSdkClientOptions.cs |
| Retry options | `options.Retry: RetryOptions` — members `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`; build fully (all members `required`) or start from `RetryOptions.Default()` | sdk-map.md (client options) |

## 4. Trap notes (hazard → consequence; load the skill for the resolved answer)

| Step | Hazard | Note |
|---|---|---|
| 2 (client registration) | SDK retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; `HttpMethodsToRetry` gates only the status trigger while a transport failure retries **every** verb — a non-idempotent `POST` (create/authorize/capture/refund) can execute more than once. | ⚠ **MUST load `dotnet-configuration-resilience`** before wiring the client and setting `Retry`. |
| 2 (client registration) | `HttpClient`/handler pipeline must be long-lived via `IHttpClientFactory`; the SDK client wrapper over it is a singleton in the DI extension. | ⚠ **MUST load `dotnet-client-initialization`** before `new PayPalServerSdkClient(...)` or `AddPayPalServerSdkClient(...)`. |
| 2 (auth) | Credentials must be set before/at client construction; secrets come from configuration, not code. A 401 at any call usually means the internal token request failed — check credentials + BaseUrl. | ⚠ **MUST load `dotnet-authentication`** before wiring `Oauth2`. |
| 5, 6, 7 (calls) | The 5 nullable header params (`payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion`) have **no C# defaults** and mis-bind in positional calls. | ⚠ **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}(...)` — call with named arguments. |
| 4, 5, 8 (models) | Enums are `StringEnum<T>` records, not C# enums (build with the static member or `FromValue`); records are immutable `init`-only with `required` members; unmodeled JSON fields are dropped on deserialize. | ⚠ **MUST load `dotnet-models`** before constructing `OrderRequest`/`CardRequest`/`PaymentTokenRequest`. |
| 3 (error boundary) | The two JsonException hazards in §3.10 — catch ladders must handle both the 2xx-deserialization direction and the error-body direction. | ⚠ **MUST load `dotnet-error-handling`** before writing the boundary (this skill is required regardless — every integration writes an error boundary). |
| 10 (reconciliation) | No SDK paging helper — the `page`/`TotalPages` loop is yours; and the `fields`/`balanceAffectingRecordsOnly` defaults shape what comes back. | ⚠ **MUST load `dotnet-configuration-resilience`** for the pagination/limits discussion before writing the paging loop. |
| 11 (tests) | The `HttpClient` constructor argument is the test seam; the SDK client itself is sealed. | ⚠ **MUST load `dotnet-testing`** before stubbing the SDK in tests. |

## 5. Assumptions & Blockers

Assumptions (app decisions / directives taken from the brief):

- `PayPal:BaseUrl`, when set, replaces the environment-derived base for **every** call including the token request; the SDK appends each operation's path after the base (`baseUrl.TrimEnd('/') + "/" + path`). This is what "verbatim" means in this SDK.
- Currency comes from config and fills every `Money.CurrencyCode`/`AmountWithBreakdown.CurrencyCode`.
- Vaulted-card payments go through `payment_source.card.vault_id` (`CardRequest.VaultId` = the id returned by `CreatePaymentToken`) — the `payment_source.token` path only supports `TokenType.BillingAgreement` in this SDK.
- `transaction_event_code` / `transaction_status` are plain strings in the SDK (no enum); mapping event codes to order vs authorization vs capture vs refund transactions is **not in the map** — the app must maintain its own code table (as the brief anticipated).
- Vaulting a card without a customer profile: `PaymentTokenRequest.Customer` is optional; whether to pass `MerchantCustomerId` and which value is the app's own customer-identity decision (`YOUR CALL — not in the map`). `ListCustomerPaymentTokens` requires `customerId`, so reconcile with the value you chose at vault time.
- Card `Expiry` wire format (e.g. `YYYY-MM`) is not stated in the map — `UNVERIFIED`; send per PayPal convention and test in sandbox.

Blockers / genuine gaps in the source material (not design choices):

1. **No `Live` environment exists in this SDK.** `ServerEnvironment` has only `Sandbox`; "sandbox vs live" cannot be expressed through `Environment`. Live can only be reached by overriding `Server.Default.Sandbox.BaseUrl` (which, per §2.4, covers every call and the token request). If the app must officially support live, this is the only SDK-supported mechanism.
2. **`TransactionInformation` lacks `gross_amount` / `paypal_fee_amount` / `net_amount`.** The map models `transaction_amount` + `fee_amount` and has **no net-amount member**; the reconciliation report (§3.9) cannot read net proceeds from the SDK model — read `TransactionAmount`/`FeeAmount`, or extend from the raw body (Case B `RawError` has no 2xx path — the response is deserialized to `SearchResponse`; unmodeled JSON is dropped).
3. **Reauthorize doc conflict inside the SDK itself**: operation Notes ("you can issue multiple re-authorizations") vs `ReauthorizeRequest` summary ("only once from days four to 29"). The exact live behavior is `UNVERIFIED`; the sheet's directive: rely on response status/error and treat reauthorize as best-effort.
4. **`SearchTransactions` `fields` accepted tokens** beyond the documented default `"transaction_info"` are not enumerated in the map — `UNVERIFIED`; pass the comma-separated token list you need and verify in sandbox.
5. **Exact 409/422 issue-code strings** for "already captured" (void), "too late to reauthorize", and "PAYER_ACTION_REQUIRED" beyond the one grounded via the `OrderStatus` enum wire value are not in the map — detect by status + `Details[].Issue` comparison; the PAYER_ACTION_REQUIRED literal is grounded, the others `UNVERIFIED`.
6. **NuGet version pin**: the map documents release tag `v1.0.1` (commit `9653d18`) and the package id `AsadAli.Checkout.Sdk`; the exact published NuGet version string is not printed in the map — pin `1.0.1` per the tag; if the restored version differs, trust the compiler and re-check the map-named source files (drift procedure in paypal-getting-started).

## 6. REQUIRED READING — load ALL of these BEFORE implementation starts

The sheet deliberately does not carry these skills' contents; each governs a step above and its hazard is named in §4.

| Skill | Governs | Hazards it covers for THIS work |
|---|---|---|
| `dotnet-client-initialization` | Step 2 — client construction, DI, HttpClient lifetime | singleton client + `IHttpClientFactory`, C# 14 extension-block DI extension, manual construction fallback |
| `dotnet-authentication` | Step 2 — `Oauth2` credentials wiring | credentials-vs-config, 401 diagnosis, token-strategy override |
| `dotnet-calling-endpoints` | Steps 5–9 — first call per operation | named arguments for the no-default nullable header params, `ct:` literal name, async usage |
| `dotnet-configuration-resilience` | Steps 2, 10 — Retry/Timeout, base URL, paging | what `Retry`/`Timeout` actually bound, transport-failure retry on POST (idempotency risk), no paging helper |
| `dotnet-error-handling` | Step 3 — the error boundary | Case A/B mechanics, `TryGet…` accessors, the two JsonException directions (§3.10) |
| `dotnet-models` | Steps 4, 5, 8 — request/response models | `StringEnum<T>` enums, `init`-only `required` records, wire names |
| `dotnet-testing` | Step 11 — tests | HttpClient constructor seam, error/edge path coverage |

Sources cited throughout: `sdk-map.md`, `map/operations/Orders.md`, `map/operations/Payments.md`, `map/operations/Vault.md`, `map/operations/TransactionSearch.md`, `map/models/records-1-Ac-Pa.md`, `map/models/records-2-Pa-Ve.md`, `map/models/enums.md`, and — for facts the map names source files for — `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`, `PayPalServerSdkClient.cs`, `PayPalServerSdkClientOptions.cs`, `AuthSchemes.cs`, `Server.cs`, `ServerOptions.cs`, `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs`, `Core/UriFactory.cs`, `Core/TemplateParamsFactory.cs`, `Core/RequestOptions.cs`, `ServiceCollectionExtensions.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentialsStrategy.cs`, `Core/Authentication/OAuth2/OAuth2Scheme.cs`, `Core/Authentication/OAuth2/OAuthToken.cs`, `Core/Authentication/OAuth2/IOAuth2TokenStrategy.cs`.