# PayPal .NET SDK integration plan — eShopOnWeb (SANDBOX, advanced card + card vaulting)

SDK: `AsadAli.Checkout.Sdk` (install version-less) · root namespace `PayPalServerSdk` · client `PayPalServerSdkClient` · map release `v1.0.1` / commit `9653d18`. Throw-based error model; **no `…Result` no-throw variants exist** (every op throws). Target framework of the SDK is `netstandard2.0`.

---

## 1. Scope & sequence

| # | Step | Operation(s) | Controller |
|---|---|---|---|
| 0 | Client construction, config binding, base-URL override, OAuth | — | client build |
| 1 | Authorize order total (hold, don't capture); card OR vaulted-card source; detect 3DS STOP | `Orders.CreateOrder` → `Orders.AuthorizeOrder` | `client.Orders` |
| 2 | Capture the authorization; read gross/fee/net | `Payments.CaptureAuthorizedPayment` | `client.Payments` |
| 3 | Reauthorize a stale authorization; detect "no longer reauthorizable" | `Payments.ReauthorizePayment` | `client.Payments` |
| 4 | Void an authorization | `Payments.VoidPayment` | `client.Payments` |
| 5 | Refund a captured payment (full/partial) with idempotency key | `Payments.RefundCapturedPayment` | `client.Payments` |
| 6 | Vault a card (create payment/setup token); reference it in step 1; delete it | `Vault.CreatePaymentToken` / `Vault.CreateSetupToken` / `Vault.DeletePaymentToken` | `client.Vault` |
| 7 | Idempotency for authorize/capture (`PayPal-Request-Id`) | (params on the ops above) | — |
| 8 | Transaction search over a date range, all pages | `TransactionSearch.SearchTransactions` | `client.TransactionSearch` |

Item 6's "list" is app-side per the brief (persist your own token→customer map); the SDK's `Vault.ListCustomerPaymentTokens` is available but not required here.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.0 Client construction, config binding, base URL & OAuth

Namespaces (source-confirmed):

| Type | `using` namespace | Source |
|---|---|---|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions`, `AddPayPalServerSdkClient` (extension on `IServiceCollection`) | `PayPalServerSdk` | root (`PayPalServerSdkClient.cs`, `ServerOptions.cs`, `ServiceCollectionExtensions.cs`) |
| `ServerEnvironment`, `DefaultOptions` (+ nested `DefaultOptions.SandboxOptions`) | `PayPalServerSdk.Servers` | `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs` |
| `SdkException<TError>` | `PayPalServerSdk.Core.Exceptions` | `Core/Exceptions/SdkException.cs` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` | `Core/ErrorResponse/RawError.cs`, `ApiError.cs` |
| request/response records (`OrderRequest`, `Money`, `CardRequest`, `CapturedPayment`, …) | `PayPalServerSdk.Models` | `Models/*.cs` |
| enums (`CheckoutPaymentIntent`, `OrderStatus`, `TokenType`, …) | `PayPalServerSdk.Models.Enums` | `Models/Enums/*.cs` |
| per-op typed errors (`AuthorizeOrderError`, `RefundCapturedPaymentError`, …) | `PayPalServerSdk.Errors` | `Errors/*.cs` |

`PayPalServerSdkClientOptions` members (source `PayPalServerSdkClientOptions.cs`): `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `Oauth2: OAuth2ClientCredentials?`, `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.

**Environment.** `ServerEnvironment` has exactly one member: `ServerEnvironment.Sandbox` (also `ServerEnvironment.Default()` == `Sandbox`). `PayPal:Environment` = "sandbox" maps to `ServerEnvironment.Sandbox`. There is **no** Live/Production member in this SDK build — treat any non-sandbox config value as an error, do not attempt a Live environment.

**Auth (OAuth2 client-credentials).** Set only:
```
options.Oauth2 = new OAuth2ClientCredentials { ClientId = cfg["PayPal:ClientId"], ClientSecret = cfg["PayPal:ClientSecret"] };
```
`OAuth2ClientCredentials` members (source): `ClientId: string` (required/init), `ClientSecret: string` (required/init), `Scope: string?`. Leave `Oauth2TokenStrategy` null — the client auto-builds the default strategy. Source-confirmed OAuth mechanics (`AuthSchemes.cs`, `OAuth2ClientCredentialsStrategy.cs`): the SDK does HTTP Basic (`Authorization: Basic base64(clientId:clientSecret)`) `POST` to path `/v1/oauth2/token` with form body `grant_type=client_credentials`, fetched lazily and attached as a Bearer token to each API call.

**Base-URL override (applies to token endpoint too — source-confirmed).** The token URL is built as `server.Default("/v1/oauth2/token")` (`AuthSchemes.cs:17`), i.e. the *same* `Server` resolution used for every API call. `Server.Default(path)` → `ServerOptions.Default.Resolve(environment, path)` → concatenates `DefaultOptions.Sandbox.BaseUrl` (default `https://api-m.sandbox.paypal.com`) with the path. Therefore overriding that one `BaseUrl` redirects **every** call including OAuth:
```
// only when PayPal:BaseUrl is set — otherwise leave options.Server at its default
options.Server = new PayPalServerSdk.ServerOptions
{
    Default = new PayPalServerSdk.Servers.DefaultOptions
    {
        Sandbox = new PayPalServerSdk.Servers.DefaultOptions.SandboxOptions
        {
            BaseUrl = cfg["PayPal:BaseUrl"]   // used verbatim, no trailing-slash normalisation guaranteed
        }
    }
};
```
`ServerOptions` (root ns) has one member `Default: DefaultOptions`. `DefaultOptions` (ns `PayPalServerSdk.Servers`) has one member `Sandbox: DefaultOptions.SandboxOptions`. `SandboxOptions` has one member `BaseUrl: string`. The value is used as-is; pass a well-formed absolute URL (scheme + host, no trailing slash) — the SDK appends `/v1/...` and `/v2/...` paths directly.

**Client construction.** `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` — the only constructor. `PayPal:Currency` is app-side config (not an SDK option): use it for every `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode` you build. DI: `services.AddPayPalServerSdkClient(o => { /* set Environment, Oauth2, Server on o */ });` (extension in ns `PayPalServerSdk`; it internally calls `AddHttpClient` and registers the client as a singleton — see trap on lifetime).

### 2.1 Orders — authorize an order total (map: `operations/Orders.md`, records `records-1-Ac-Pa.md`)

Two calls: create the order with `intent = AUTHORIZE` and the amount, then authorize it (supplying the card / vault reference).

| Call | Signature (verbatim) | Body | Returns | Error |
|---|---|---|---|---|
| CreateOrder | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderRequest` | `Order` | `SdkException<CreateOrderError>` (Case A) — `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` |
| AuthorizeOrder | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderAuthorizeRequest?` | `OrderAuthorizeResponse` | `SdkException<AuthorizeOrderError>` (Case A) — `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError(out RawError)` |

The first five params of each (`payPalMockResponse … payPalAuthAssertion`, minus `body`) are nullable with **no default → must pass explicitly** (pass `null` to skip). `prefer` defaults to `"return=minimal"`; pass `"return=representation"` if you want the full authorization objects (incl. `payments.authorizations`) back on the authorize response.

**Request bodies:**
- `OrderRequest` (!req: `Intent`, `PurchaseUnits`): `Intent (intent): CheckoutPaymentIntent !req` → `CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`); `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`; `Payer (payer): Payer?`; `PaymentSource (payment_source): PaymentSource?`; `ApplicationContext (application_context): OrderApplicationContext?`.
- `PurchaseUnitRequest` (!req: `Amount`): `Amount (amount): AmountWithBreakdown !req`; `ReferenceId (reference_id): string?`; `CustomId (custom_id): string?`; `InvoiceId (invoice_id): string?` (set to your order id for later reconciliation, step 8); plus optional `Items`, `Shipping`, etc.
- `AmountWithBreakdown` (!req: `CurrencyCode`, `Value`): `CurrencyCode (currency_code): string !req` (from `PayPal:Currency`); `Value (value): string !req` (the **order total as a string, formatted to the currency's minor units — e.g. "12.34"**; this is the amount held); `Breakdown (breakdown): AmountBreakdown?`.
- `OrderAuthorizeRequest`: single optional field `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`.
- `OrderAuthorizeRequestPaymentSource`: `Card (card): CardRequest?`, `Token (token): Token?`, `Paypal (paypal): PayPalWallet?`, `ApplePay`, `GooglePay`, `Venmo`.

**Payment source — the two required cases:**
- **Raw one-off card** (sandbox test card 4111 1111 1111 1111): `OrderAuthorizeRequestPaymentSource.Card = new CardRequest { Number = "4111111111111111", Expiry = "YYYY-MM", SecurityCode = "123", Name = "...", BillingAddress = new Address { CountryCode = "US", ... } }`. `CardRequest` fields: `Name`, `Number`, `Expiry` (string, `YYYY-MM`), `SecurityCode`, `BillingAddress (Address?)`, `Attributes (CardAttributes?)`, `VaultId (vault_id): string?`, `SingleUseToken`, `StoredCredential`, `NetworkToken`, `ExperienceContext (CardExperienceContext?)`. `Address` requires `CountryCode (country_code): string !req`.
- **Previously vaulted card** referenced by its vault token: `OrderAuthorizeRequestPaymentSource.Card = new CardRequest { VaultId = "<PaymentTokenResponse.Id from step 6>" }`. **Do NOT use the `Token` variant for a vaulted card:** the `Token` record's `Type (type): TokenType !req` enum has exactly one member — `TokenType.BillingAgreement` (wire `BILLING_AGREEMENT`) — so `Token` is only for PayPal-wallet billing agreements, not vaulted cards. Vaulted-card reuse is `CardRequest.VaultId`. (Map-verified from `enums.md` `TokenType`.)

You may instead put the payment source on `OrderRequest.PaymentSource` (type `PaymentSource`, same `Card`/`Token` shape) at CreateOrder time; either placement authorizes the amount fixed at CreateOrder. Pick one and be consistent.

**Response — what to read (`OrderAuthorizeResponse`):**
- Order id ← `OrderAuthorizeResponse.Id (string?)` (equal to the `Order.Id` from CreateOrder).
- Overall status ← `OrderAuthorizeResponse.Status (OrderStatus?)`. `OrderStatus` members: `Created`, `Saved`, `Approved`, `Voided`, `Completed`, `PayerActionRequired` (wire `PAYER_ACTION_REQUIRED`).
- Authorization id + status ← `OrderAuthorizeResponse.PurchaseUnits (IReadOnlyList<PurchaseUnit>?)`[i]`.Payments (PaymentCollection?).Authorizations (IReadOnlyList<AuthorizationWithAdditionalData>?)`[j]`.Id (string?)` and `.Status (AuthorizationStatus?)`. `AuthorizationStatus` members: `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending`. **Every link in that chain is nullable — null-guard each hop.** (To be sure the authorization objects are populated, send `prefer: "return=representation"`.)

**3DS / challenge STOP detection (report, do NOT round-trip the browser):**
- Primary map-verified signal: `OrderAuthorizeResponse.Status == OrderStatus.PayerActionRequired`. If so → STOP, surface to operator, do not capture.
- HATEOAS signal: scan `OrderAuthorizeResponse.Links (IReadOnlyList<LinkDescription>?)` for a link whose `Rel (rel): string !req` marks a required buyer action; that link's `Href` is where a browser approval would occur. `LinkDescription` = `Href (href): string !req`, `Rel (rel): string !req`, `Method (method): LinkHttpMethod?`.
- Card auth outcome (diagnostic only): `OrderAuthorizeResponsePaymentSource.Card (CardResponse?).AuthenticationResult (AuthenticationResponse?).ThreeDSecure (ThreeDSecureAuthenticationResponse?)` → `AuthenticationStatus (ParesStatus?)`, `EnrollmentStatus (EnrollmentStatus?)`; and `.LiabilityShift (LiabilityShiftIndicator?)`.
- `UNVERIFIED` (live-wire only): the exact `rel` string PayPal returns for the challenge link (commonly `"payer-action"`) is not in the map. **Directive:** detect the STOP condition primarily via `Status == PayerActionRequired`; additionally treat *any* `Links` entry whose `Rel` is not one of the ordinary rels (`self`, `capture`, `authorize`) as a possible action link and surface its `Href` and `Rel` verbatim rather than hard-matching one literal string.

### 2.2 Payments — capture the authorization (map: `operations/Payments.md`, records `records-1`)

| Call | Signature (verbatim) | Body | Returns | Error |
|---|---|---|---|---|
| CaptureAuthorizedPayment | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CaptureRequest?` (null = capture full authorized amount) | `CapturedPayment` | `SdkException<CaptureAuthorizedPaymentError>` (Case A) — `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` |

`payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` — must pass explicitly. For a partial capture set `body = new CaptureRequest { Amount = new Money { CurrencyCode = cfg, Value = "..." }, FinalCapture = true/false }`. `CaptureRequest` fields: `Amount (Money?)`, `InvoiceId (string?)`, `FinalCapture (final_capture): bool? = false`, `PaymentInstruction`, `NoteToPayer`, `SoftDescriptor`. Send `prefer: "return=representation"` to guarantee `seller_receivable_breakdown` is populated.

**Response — captured amount, PayPal fee, net proceeds (`CapturedPayment`):**
- Capture id ← `CapturedPayment.Id (string?)` (needed for refund, step 5).
- Status ← `CapturedPayment.Status (CaptureStatus?)`: `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed`.
- Gross captured ← `CapturedPayment.Amount (Money?)` (`.CurrencyCode`, `.Value`).
- Breakdown ← `CapturedPayment.SellerReceivableBreakdown (SellerReceivableBreakdown?)`:
  - `GrossAmount (gross_amount): Money !req` → **gross_amount**
  - `PaypalFee (paypal_fee): Money?` → **paypal_fee**
  - `NetAmount (net_amount): Money?` → **net_amount** (merchant net proceeds)
  - also `PaypalFeeInReceivableCurrency`, `ReceivableAmount`, `ExchangeRate`, `PlatformFees` when cross-currency.
- `Money` = `CurrencyCode (currency_code): string !req`, `Value (value): string !req` (read `.Value` as the decimal string).
- **`SellerReceivableBreakdown` is absent for `PENDING` captures** (documented on the record) — null-guard it; if `Status == Pending`, report pending rather than reading fee/net.

### 2.3 Payments — reauthorize a stale authorization (map: `operations/Payments.md`, records `records-2`)

| Call | Signature (verbatim) | Body | Returns | Error |
|---|---|---|---|---|
| ReauthorizePayment | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ReauthorizeRequest?` | `PaymentAuthorization` | `SdkException<ReauthorizePaymentError>` (Case A) — `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` |

`payPalRequestId`, `payPalAuthAssertion`, `body` — must pass explicitly. `ReauthorizeRequest` has a single field `Amount (amount): Money?` (supports only `amount`; honor/29-day windows enforced server-side). Response `PaymentAuthorization`: `Status (AuthorizationStatus?)`, `Id (string?)`, `Amount (Money?)`, `ExpirationTime (expiration_time): string?` (the new 3-day honor-period expiry), `StatusDetails (AuthorizationStatusDetails?).Reason (AuthorizationIncompleteReason?)`.

**Detecting "can no longer be reauthorized" (report to operator):** `AuthorizationStatus` has **no `EXPIRED` member** — an authorization past the reauth window is not signalled by a status enum value; it surfaces as the **error** on the reauthorize call. Catch `SdkException<ReauthorizePaymentError>`, call `TryGetError(out var err)`, and read `err.Details (IReadOnlyList<ErrorDetails>?)`[k]`.Issue (issue): string !req` (also `err.Name`, `err.Message`, `err.DebugId`). `ErrorDetails` = `Field?`, `Value?`, `Location? = "body"`, `Issue !req`, `Description?`, `Links?`.
- `UNVERIFIED` (live-wire only): the exact `Issue` string for an un-reauthorizable authorization (e.g. `AUTH_CANNOT_BE_REAUTHORIZED` / an "authorization expired / max reauthorization" issue) is not in the map. **Directive:** on a reauthorize failure, extract `err.Details[].Issue` + `err.Message` best-effort and surface them verbatim to the operator; fall back to `err.Message` (then `TryGetRawError` → `RawError.ReadAsString()`) if `Details` is empty. Do not hard-code a single issue string as the sole trigger.

### 2.4 Payments — void an authorization (map: `operations/Payments.md`)

| Call | Signature (verbatim) | Returns | Error |
|---|---|---|---|
| VoidPayment | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentAuthorization` | `SdkException<VoidPaymentError>` (Case A) — `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` |

`payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` — must pass explicitly (no request body). No body. On success `PaymentAuthorization.Status` → expect `AuthorizationStatus.Voided`. Note: **a fully-captured authorization cannot be voided** — that comes back as an error (409 among the mapped statuses); handle via `TryGetError` and report.

### 2.5 Payments — refund a captured payment, full/partial, idempotent (map: `operations/Payments.md`, records `records-2`)

| Call | Signature (verbatim) | Body | Returns | Error |
|---|---|---|---|---|
| RefundCapturedPayment | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `RefundRequest?` (null = **full** refund) | `Refund` | `SdkException<RefundCapturedPaymentError>` (Case A) — `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` |

`payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` — must pass explicitly.
- **Full refund:** `body = null`.
- **Partial refund:** `body = new RefundRequest { Amount = new Money { CurrencyCode = cfg, Value = "..." } }`. `RefundRequest` fields: `Amount (Money?)`, `CustomId (string?)`, `InvoiceId (string?)`, `NoteToPayer (string?)`, `PaymentInstruction (RefundPaymentInstruction?)`.
- **Idempotency key:** pass the caller-supplied key as `payPalRequestId` — source-confirmed to bind to the `PayPal-Request-Id` header (`Api/Payments.cs`). Same key on a retried refund → PayPal will not double-refund.
- **Response (`Refund`):** refund id ← `Refund.Id (string?)`; status ← `Refund.Status (RefundStatus?)` = `Cancelled`, `Failed`, `Pending`, `Completed`; fee reversal ← `Refund.SellerPayableBreakdown (SellerPayableBreakdown?)` (`GrossAmount?`, `PaypalFee?`, `NetAmount?`, `TotalRefundedAmount?`).
- **Over-refund enforcement:** PayPal rejects a refund exceeding the captured (net of prior refunds) amount server-side — it comes back as a Case-A error (422/400), not a silent partial. `UNVERIFIED` (live-wire only): the exact `Issue` string (e.g. a `REFUND_AMOUNT_EXCEEDED` variant) is not in the map. **Directive:** on refund failure read `err.Details[].Issue` + `err.Message` best-effort and surface; fall back to `err.Message`. Track your own captured-minus-refunded running total app-side as a first guard, but treat PayPal's rejection as authoritative.

### 2.6 Vault — save a card, reference it, delete it (map: `operations/Vault.md`, records `records-1`/`records-2`)

**Direct one-step (raw card → payment token):**

| Call | Signature (verbatim) | Body | Returns | Error |
|---|---|---|---|---|
| CreatePaymentToken | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenRequest` | `PaymentTokenResponse` | `SdkException<CreatePaymentTokenError>` (Case A) — `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError(out RawError)` |

`payPalRequestId` must pass explicitly (binds `PayPal-Request-Id`; use it as an idempotency key for vaulting too). Body:
- `PaymentTokenRequest` (!req: `PaymentSource`): `Customer (customer): Customer?` (`Customer` = `Id?`, `MerchantCustomerId?`), `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.
- `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?`, `Token (token): VaultTokenRequest?`.
- `PaymentTokenRequestCard` (all optional): `Name?`, `Number?`, `Expiry?` (`YYYY-MM`), `SecurityCode?`, `Brand (CardBrand?)`, `BillingAddress (Address?)`.
- **Response (`PaymentTokenResponse`):** vault-token id to persist & reuse ← `PaymentTokenResponse.Id (string?)`; also `Customer (CustomerResponse?)`, `PaymentSource (PaymentTokenResponsePaymentSource?)` (`.Card` = `CardPaymentTokenEntity` with `LastDigits`, `Brand`, `Expiry` for app-side display), `Links`.

**Two-step (setup token → payment token)** — use when you must confirm/verify the card first:

| Call | Signature (verbatim) | Body | Returns | Error |
|---|---|---|---|---|
| CreateSetupToken | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `SetupTokenRequest` | `SetupTokenResponse` | `SdkException<CreateSetupTokenError>` (Case A) — `TryGetError1(out Error1)` [400,403,422,500] · `TryGetRawError(out RawError)` |

- `SetupTokenRequest` (!req: `PaymentSource`): `Customer (Customer?)`, `PaymentSource (SetupTokenRequestPaymentSource !req)`.
- `SetupTokenRequestPaymentSource`: `Card (SetupTokenRequestCard?)`, `Paypal`, `Venmo`, `ApplePay`, `Token (VaultTokenRequest?)`, `Bank`. `SetupTokenRequestCard` = `Name?`, `Number?`, `Expiry?`, `SecurityCode?`, `Brand?`, `BillingAddress?`, `VerificationMethod (VaultCardVerificationMethod?)`, `ExperienceContext (VaultCardExperienceContext?)`.
- `SetupTokenResponse.Id` → then exchange it: `CreatePaymentToken` with `PaymentTokenRequestPaymentSource.Token = new VaultTokenRequest { Id = setupTokenId, Type = VaultTokenRequestType.SetupToken }`. `VaultTokenRequest` (!req both): `Id (id): string !req`, `Type (type): VaultTokenRequestType !req`; `VaultTokenRequestType` has one member `SetupToken` (wire `SETUP_TOKEN`).

**Reference the vaulted card when authorizing (feeds step 1):** `OrderAuthorizeRequestPaymentSource.Card = new CardRequest { VaultId = paymentTokenResponse.Id }`. (Again: not the `Token` variant — see 2.1.)

**Delete a vaulted token:**

| Call | Signature (verbatim) | Returns | Error |
|---|---|---|---|
| DeletePaymentToken | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `void` (Task) | `SdkException<DeletePaymentTokenError>` (Case A) — `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError(out RawError)` |

Note the Vault ops' typed accessor is `TryGetError1(out Error1)` (not `TryGetError`/`Error`). `Error1` = `Name !req`, `Message !req`, `DebugId !req`, `Details (IReadOnlyList<ErrorDetails1>?)`, `Links (IReadOnlyList<ErrorLinkDescription>?)`; `ErrorDetails1` = `Field?`, `Value?`, `Location? = "body"`, `Issue !req`, `Description?`.

### 2.7 Idempotency for authorize/capture (`PayPal-Request-Id`)

Source-confirmed: the `payPalRequestId` parameter binds to header `PayPal-Request-Id` in Orders, Payments, and Vault (`Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`). Pass a stable per-logical-operation key (e.g. a GUID persisted with the order/fulfilment record, reused on retry) in that parameter position:
- CreateOrder → param #2 `payPalRequestId`.
- AuthorizeOrder → param #3 `payPalRequestId`.
- CaptureAuthorizedPayment → param #3 `payPalRequestId`.
- RefundCapturedPayment → param #3 `payPalRequestId`.
- ReauthorizePayment → param #2 `payPalRequestId`; VoidPayment → param #4 `payPalRequestId`; CreatePaymentToken/CreateSetupToken → param #1 `payPalRequestId`.

A double-click that re-sends the same key does not authorize/capture twice. Generate the key **before** the first attempt and store it; do not generate a fresh key on retry.

### 2.8 TransactionSearch — reconciliation over a date range, all pages (map: `operations/TransactionSearch.md`, records `records-2`)

| Call | Signature (verbatim) | Returns | Error |
|---|---|---|---|
| SearchTransactions | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `SearchResponse` | `SdkException<RawError>` — **Case B (raw)** |

- `startDate`/`endDate` are **required positional** (wire `start_date`/`end_date`), ISO-8601. `transactionId … terminalId` (8 params) are nullable with no default → **must pass explicitly (`null`)**. Call with **named arguments** to avoid mis-binding the many optionals. Keep `fields: "transaction_info"` so `transaction_info` is populated.
- `UNVERIFIED` (live-wire/format only): PayPal requires full ISO-8601 with offset (e.g. `2026-08-01T00:00:00-0000`) and limits each request to a 31-day window and `pageSize` ≤ 500; these bounds are not in the map. **Directive:** format `start_date`/`end_date` as full offset-qualified ISO-8601; if a reconciliation range exceeds ~31 days, chunk it into ≤31-day sub-ranges app-side and page each; treat a 4xx complaining about range/format as a caller error to fix, not an outage.
- **Error is Case B:** catch `SdkException<RawError>`; read `ex.Error.StatusCode (HttpStatusCode)`, `ex.Error.ReadAsString()`, or `ex.Error.ReadAsJson<T>()`. There are **no typed `TryGet…` accessors** on this operation.

**Pagination (page / total_pages):** `SearchResponse` fields: `TransactionDetails (IReadOnlyList<TransactionDetails>?)`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Links (IReadOnlyList<LinkDescription>?)`, `StartDate`, `EndDate`, `LastRefreshedDatetime`, `AccountNumber`. There is **no SDK auto-pager** — loop yourself: start `page = 1`, read `resp.TotalPages`, and re-call incrementing `page` until `page > TotalPages` (or `TotalPages` is null/1), accumulating `TransactionDetails`. (`Links` also carries `next`/`last` HATEOAS rels as an alternative.)

**Fields to line transactions up against orders** — per `TransactionDetails.TransactionInfo (TransactionInformation?)`:
- transaction id ← `TransactionInformation.TransactionId (transaction_id): string?`
- amount ← `TransactionInformation.TransactionAmount (transaction_amount): Money?` (`.CurrencyCode`, `.Value`); fee ← `FeeAmount (Money?)`
- status ← `TransactionInformation.TransactionStatus (transaction_status): string?` (**plain string, not an enum**)
- correlation keys ← `InvoiceId (invoice_id): string?` and `CustomField (custom_field): string?` — set `PurchaseUnitRequest.InvoiceId`/`CustomId` at CreateOrder (step 1) so these line up with your eShop order ids. Also `TransactionInitiationDate`, `TransactionUpdatedDate`, `PaypalReferenceId`.

### 2.9 Enum value tables used above (map: `map/models/enums.md`, ns `PayPalServerSdk.Models.Enums`)

Enums are `StringEnum<T>` (NOT C# enums): write `EnumType.Member` (e.g. `CheckoutPaymentIntent.Authorize`), or `EnumType.FromValue("WIRE_VALUE")`.

| Enum | Members (`CSharp` = wire) |
|---|---|
| `CheckoutPaymentIntent` | `Capture` = CAPTURE, `Authorize` = AUTHORIZE |
| `OrderStatus` | `Created`, `Saved`, `Approved`, `Voided`, `Completed`, `PayerActionRequired` = PAYER_ACTION_REQUIRED |
| `AuthorizationStatus` | `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending` (no `EXPIRED`) |
| `CaptureStatus` | `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed` |
| `RefundStatus` | `Cancelled`, `Failed`, `Pending`, `Completed` |
| `TokenType` | `BillingAgreement` = BILLING_AGREEMENT (only member) |
| `VaultTokenRequestType` | `SetupToken` = SETUP_TOKEN (only member) |
| `PaymentTokenStatus` | `Created`, `PayerActionRequired`, `Approved`, `Vaulted`, `Tokenized` |
| `AuthorizationIncompleteReason` | `PendingReview` = PENDING_REVIEW, `DeclinedByRiskFraudFilters` = DECLINED_BY_RISK_FRAUD_FILTERS |

---

## 3. Trap notes (load the named companion at the step where it bites)

> ⚠ Step 0 (client & DI) — `AddPayPalServerSdkClient` registers the client and calls `AddHttpClient` internally; whether the SDK client is singleton/transient and how the `HttpClient`/handler pipeline must be owned and reused (vs rebuilt per request) is not something the signature reveals. **MUST load `dotnet-client-initialization`** before wiring the client into DI or writing a factory.

> ⚠ Step 0 (auth) — set `options.Oauth2` before constructing the client / in the DI callback, and source `ClientId`/`ClientSecret` from configuration, never hardcoded; how the token is cached/refreshed and how a 401 should be handled is in the skill. **MUST load `dotnet-authentication`** before wiring credentials.

> ⚠ Step 0 (base URL / resilience) — the SDK `Retry`/`Timeout` options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; `HttpMethodsToRetry` gates only the status-code trigger while a transport failure can re-send even a POST; and the base-URL override interacts with pagination/logging you must still wire yourself. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts/base-URL. (Retry-on-write matters here because authorize/capture/refund are non-idempotent unless the `PayPal-Request-Id` idempotency key from §2.7 is set.)

> ⚠ Steps 1–8 (calls) — list/search ops (`SearchTransactions`) and the many must-pass-explicitly nullable params mis-bind in positional calls; call with named arguments. **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 1–8 (models) — `StringEnum<T>` are not C# enums, unions/`AnyOf` use factories + `TryGet…`, `required` members must be set in the initializer, and unmodeled JSON is dropped on deserialize (so a field you don't see modelled is silently lost). **MUST load `dotnet-models`** before building payloads or mapping responses onto eShop domain types.

> ⚠ Steps 1–6 error boundary — Orders/Payments ops are Case A with `TryGetError(out Error)` (+ some `TryGetNoContent(out RawError)` for 500), Vault ops are Case A with `TryGetError1(out Error1)`, and `SearchTransactions` is Case B (`SdkException<RawError>`, no typed accessors); `TryGetRawError` is a fallback, not a catch-all on every status. **MUST load `dotnet-error-handling`** before writing any try/catch. (See REQUIRED READING for the two `JsonException` hazards that an SDK-exception-only ladder misses.)

> ⚠ Tests — the `HttpClient` constructor argument is the test seam; match eShop's existing test framework/assertion style. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING — load BEFORE implementation starts

The sheet deliberately does not carry these skills' contents (defaults, worked examples, and the parts you must still wire yourself). Load each before writing the step it governs:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, options/builder shape, `HttpClient` lifetime, DI registration |
| `dotnet-authentication` | Step 0 — setting OAuth2 client-credentials, token caching/refresh, 401 handling |
| `dotnet-configuration-resilience` | Step 0 — retries/backoff, what `Timeout` bounds, retry-on-write, base-URL override, pagination/logging |
| `dotnet-calling-endpoints` | Steps 1–8 — named-argument calls, required vs optional params, async/cancellation |
| `dotnet-models` | Steps 1–8 — building request models, `StringEnum<T>`, unions, required/nullable, wire-vs-C# names |
| `dotnet-error-handling` | Steps 1–6, 8 — Case A vs Case B, safe status/body reads, the JsonException traps below |
| `dotnet-testing` | Tests — faking the `HttpClient` seam, covering error/edge paths |

Two mandatory `System.Text.Json.JsonException` hazards for the error boundary (write the boundary to handle BOTH — they need opposite handling):
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions:**
1. **Plan-file path.** The brief dictated `C:\claude-runs\t3v7ali-task3-plugin-opus48high-034\repo\paypal-plan.md`; written there (not a default).
2. **Authorize = two calls.** Modelled as `CreateOrder(intent=AUTHORIZE, amount)` then `AuthorizeOrder(payment_source)`. The amount held equals the `AmountWithBreakdown.Value` fixed at CreateOrder; format it to the currency's minor units as a string so the hold equals the order total to the cent. Payment source may be placed on either `OrderRequest.PaymentSource` (at create) or `OrderAuthorizeRequest.PaymentSource` (at authorize) — pick one consistently.
3. **Vaulted-card reuse = `CardRequest.VaultId`**, not the `Token` payment-source variant (map-verified: `TokenType` has only `BILLING_AGREEMENT`).
4. **Currency** is a single app-config value (`PayPal:Currency`) applied to every `Money`/`AmountWithBreakdown` you construct; the SDK has no global currency setting.
5. **`PayPal:Environment`** is expected to be `sandbox`; the SDK build exposes only `ServerEnvironment.Sandbox`, so any other value should be rejected app-side.
6. **Idempotency keys** are generated and persisted by the caller before the first attempt and reused on retry (GUID acceptable).
7. Transaction-search reconciliation correlates on `InvoiceId`/`CustomField` that you set at CreateOrder; if eShop cannot set those, correlation falls back to PayPal `transaction_id` captured at capture time.

**Items labelled `UNVERIFIED` (live-wire only — resolved as defensive directives in-sheet, not left open):** the challenge-link `rel` string (§2.1), the un-reauthorizable `Issue` string (§2.3), the over-refund `Issue` string (§2.5), and the transaction-search date format / range / pageSize limits (§2.8). Each has a concrete extract-best-effort-with-fallback directive on its row.

**Blockers:** none. Every SDK contract fact needed to implement items 1–8 is resolved from the map or the pinned SDK source; nothing is left for "whoever implements."
