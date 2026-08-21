# PayPal .NET SDK integration plan — eShopOnWeb

SDK: `AsadAli.Checkout.Sdk` (NuGet, install version-less) · root namespace `PayPalServerSdk` ·
client `PayPalServerSdkClient` · map release tag `v1.0.1` (source commit `9653d18`).
Every fact below is grounded in the bundled SDK map (page cited per row); the base-URL override
and OAuth2 token-URL wiring were confirmed against SDK source (files named inline).

---

## 1. Scope & sequence

| # | Step | SDK operation(s) | Controller |
|---|---|---|---|
| 0 | Install pkg, bind `PayPal:` settings, construct + DI-register client, set env=Sandbox, optional BaseUrl override, OAuth2 creds | `AddPayPalServerSdkClient` / `new PayPalServerSdkClient` | — |
| 1 | Create order, `intent=AUTHORIZE`, raw card (Visa 4111…), amount to the cent | `CreateOrder` | `Orders` |
| 2 | Authorize the order (place hold), idempotent | `AuthorizeOrder` | `Orders` |
| 3 | Capture the authorization at fulfilment; read gross/fee/net | `CaptureAuthorizedPayment` | `Payments` |
| 4 | Re-authorize a stale authorization before capture | `ReauthorizePayment` | `Payments` |
| 5 | Void an authorization (release funds) | `VoidPayment` | `Payments` |
| 6 | Refund a capture (full/partial), caller idempotency key | `RefundCapturedPayment` | `Payments` |
| 7a | Vault/save a card (setup→payment token, or direct payment token) | `CreateSetupToken` → `CreatePaymentToken`, or `CreatePaymentToken` | `Vault` |
| 7b | List / get / delete vaulted tokens | `ListCustomerPaymentTokens`, `GetPaymentToken`, `DeletePaymentToken` | `Vault` |
| 7c | Pay a later order using saved vault id | `CreateOrder` (+ `AuthorizeOrder`) with `card.vault_id` | `Orders` |
| 8 | Transaction search / reconciliation over a date range, paginated | `SearchTransactions` | `TransactionSearch` |

Status/detail reads (optional but recommended): `GetAuthorizedPayment`, `GetCapturedPayment`,
`GetRefund`, `GetOrder` (all `Payments`/`Orders`).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

### 2.0 Namespaces (add a `using` for each kind you touch)

| Kind | Namespace | Examples |
|---|---|---|
| Client, options, ServerOptions, DI ext | `PayPalServerSdk` | `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions`, `ServiceCollectionExtensions.AddPayPalServerSdkClient` |
| Controllers | `PayPalServerSdk.Api` | `Orders`, `Payments`, `Vault`, `TransactionSearch` (usually reached as `client.Orders` etc.) |
| Records (request/response/error payloads) | `PayPalServerSdk.Models` | `OrderRequest`, `Money`, `CapturedPayment`, `Refund`, `Error`, `Error1`, `DefaultError`, … |
| Enums | `PayPalServerSdk.Models.Enums` | `CheckoutPaymentIntent`, `OrderStatus`, `CaptureStatus`, `AuthorizationStatus`, `RefundStatus`, `CardBrand`, `VaultTokenRequestType`, `StoreInVaultInstruction` |
| Typed error classes | `PayPalServerSdk.Errors` | `CreateOrderError`, `AuthorizeOrderError`, `CaptureAuthorizedPaymentError`, … |
| Environment & server base-URL types | `PayPalServerSdk.Servers` | `ServerEnvironment`, `DefaultOptions`, `DefaultOptions.SandboxOptions` |
| OAuth2 credentials | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | `OAuth2ClientCredentials` |
| OAuth2 token strategy iface | `PayPalServerSdk.Core.Authentication.OAuth2` | `IOAuth2TokenStrategy<>` |
| Exception wrapper | `PayPalServerSdk.Core.Exceptions` | `SdkException<TError>` |
| Raw error body | `PayPalServerSdk.Core.ErrorResponse` | `RawError` |
| Per-call options | `PayPalServerSdk.Core` | `RequestOptions` |

Source for the four Core namespaces above: `Core/Exceptions/SdkException.cs`,
`Core/ErrorResponse/RawError.cs`, `Core/RequestOptions.cs`, `Api/*.cs` (confirmed from source).

### 2.1 Client construction, environment, base-URL override, auth, DI

**Environment (sandbox).** `options.Environment = ServerEnvironment.Sandbox;`
`ServerEnvironment` (`PayPalServerSdk.Servers`) has exactly ONE member: `Sandbox`. There is **no
Production/Live member in this SDK build** — see Assumptions & Blockers. (map: `sdk-map.md` *Servers & auth*.)

**Base-URL override (verbatim, all calls incl. token request).** `options.Server` is a
`ServerOptions` (`PayPalServerSdk`) whose `.Default` is a `DefaultOptions` (`PayPalServerSdk.Servers`)
whose `.Sandbox` is a `DefaultOptions.SandboxOptions` with a settable `string BaseUrl`
(default `"https://api-m.sandbox.paypal.com"`). Override:
```
options.Server = new ServerOptions {
    Default = new DefaultOptions {
        Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = cfg["PayPal:BaseUrl"] }
    }
};
```
CONTRACT FACT (confirmed from source `AuthSchemes.cs` + `Server.cs` + `Servers/DefaultOptions.cs`):
the OAuth2 client-credentials token request is built as `server.Default("/v1/oauth2/token")`, i.e.
from the **same** `SandboxOptions.BaseUrl`. So overriding `BaseUrl` redirects **every** request
including the token/credential call — exactly the required behaviour. Only set the override when
`PayPal:BaseUrl` is non-empty; otherwise leave `options.Server` at its default.

**Auth (OAuth2 client credentials).** Set `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = … }`
(`PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`; both `ClientId`/`ClientSecret`
are `required`, optional `Scope`). Confirmed from source
`Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`. Leave
`options.Oauth2TokenStrategy` null — the SDK supplies the default client-credentials strategy that
fetches/caches the bearer token. (map: `sdk-map.md` *Servers & auth*.)

**Constructor / DI.** `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`.
DI: `services.AddPayPalServerSdkClient(o => { /* set Environment, Server, Oauth2 on o */ });`
CONTRACT FACT (source `ServiceCollectionExtensions.cs`): this calls `services.AddHttpClient()` and
registers `PayPalServerSdkClient` as a **singleton** built from an `IHttpClientFactory`-pooled
`HttpClient`. Do not also register it transient, and do not `new` a per-request `HttpClient`.
(map: `sdk-map.md` *Getting a client* / *client-options*.)

**Client options members** (map `sdk-map.md`): `Environment: ServerEnvironment`, `Retry: RetryOptions`,
`Logging: LoggingOptions`, `Server: ServerOptions`, `Oauth2: OAuth2ClientCredentials?`,
`Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.

### 2.2 Idempotency mechanism (applies to steps 1–7)

Every write that supports idempotency takes a **`payPalRequestId`** string parameter (wire header
`PayPal-Request-Id`) — this is PayPal's idempotency key. Pass a caller-supplied/stable key to make
the write safely retryable; pass `null` to opt out. Per-operation parameter position is in each row
below. Operations exposing it: `CreateOrder`, `AuthorizeOrder`, `CaptureAuthorizedPayment`,
`ReauthorizePayment`, `VoidPayment`, `RefundCapturedPayment`, `CreatePaymentToken`,
`CreateSetupToken`. (map: `operations/Orders.md`, `operations/Payments.md`, `operations/Vault.md`.)

### 2.3 Operations

Legend: params listed in signature order; `!` = nullable-but-no-default → **must pass explicitly**
(pass `null` to skip); fields as `CSharpName (wire_name): Type, required?`.

#### Step 1 — CreateOrder (`client.Orders`) · map: `operations/Orders.md`
- **Signature**: `CreateOrder(string? payPalMockResponse!, string? payPalRequestId!, string? payPalPartnerAttributionId!, string? payPalClientMetadataId!, string? payPalAuthAssertion!, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - The 5 header params before `body` are nullable-no-default → pass `null` except `payPalRequestId` (idempotency).
  - Tip: pass `prefer: "return=representation"` to get the full order (payment_source, links, status) back instead of the minimal body.
- **Returns**: `Order`
- **Request body `OrderRequest`** (map `records-1-Ac-Pa.md`): `Intent (intent): CheckoutPaymentIntent, required` · `Payer (payer): Payer?` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest>, required` · `PaymentSource (payment_source): PaymentSource?` · `ApplicationContext (application_context): OrderApplicationContext?`
  - `Intent = CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`).
  - `PurchaseUnitRequest` (map `records-2-Pa-Ve.md`): `Amount (amount): AmountWithBreakdown, required` · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `ReferenceId (reference_id): string?` · `Description (description): string?` (+ others). **Put the eShop order id in `CustomId` and/or `InvoiceId`** so step 8 reconciliation can line it up.
  - `AmountWithBreakdown` (map `records-1-Ac-Pa.md`): `CurrencyCode (currency_code): string, required` · `Value (value): string, required` · `Breakdown (breakdown): AmountBreakdown?`. **`Value` is a STRING** — format the order total to the currency's minor units yourself (e.g. `"12.99"`), do not pass a decimal. `CurrencyCode` from `PayPal:Currency`.
  - **Raw card**: `PaymentSource.Card` = `CardRequest` (map `records-2-Pa-Ve.md`/`records-1-Ac-Pa.md`): `Name (name): string?` · `Number (number): string?` (PAN, e.g. `4111111111111111`) · `Expiry (expiry): string?` (e.g. `"2030-01"`) · `SecurityCode (security_code): string?` (CVC) · `BillingAddress (billing_address): Address?` · `VaultId (vault_id): string?` · `Attributes (attributes): CardAttributes?` · `ExperienceContext (experience_context): CardExperienceContext?`.
  - `PaymentSource` (map `records-2-Pa-Ve.md`): `Card (card): CardRequest?` · `Token (token): Token?` · … (wallets).
- **Error**: `SdkException<CreateOrderError>` — **Case A**. Accessors: `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` [fallback].
- **Pagination**: none.

#### Step 2 — AuthorizeOrder (`client.Orders`) · map: `operations/Orders.md`
- **Signature**: `AuthorizeOrder(string id, string? payPalMockResponse!, string? payPalRequestId!, string? payPalClientMetadataId!, string? payPalAuthAssertion!, OrderAuthorizeRequest? body!, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `id` = order id from step 1. `payPalRequestId` = idempotency key. `body` may be `null` (already has payment source from CreateOrder).
- **Returns**: `OrderAuthorizeResponse` (map `records-1-Ac-Pa.md`): `Id (id)` · `Status (status): OrderStatus?` · `PaymentSource (payment_source): OrderAuthorizeResponsePaymentSource?` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` · `Links (links): IReadOnlyList<LinkDescription>?` · `Intent`.
  - **Authorization id + status**: read from `PurchaseUnits[].Payments.Authorizations[]` — each is `AuthorizationWithAdditionalData` (map `records-1-Ac-Pa.md`): `Id (id): string?` (the authorization id) · `Status (status): AuthorizationStatus?` · `Amount (amount): Money?` · `ExpirationTime (expiration_time): string?`. `PurchaseUnit.Payments` is `PaymentCollection` (map `records-2-Pa-Ve.md`): `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` · `Captures` · `Refunds`. **Envelope note: the authorization id is nested `OrderAuthorizeResponse → purchase_units[] → payments → authorizations[] → id`, NOT at the top level.**
- **Error**: `SdkException<AuthorizeOrderError>` — **Case A**. `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError`.
- `OrderAuthorizeRequest` body (map `records-1-Ac-Pa.md`): `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` (only needed if supplying card at authorize time).

#### 3DS / challenge STOP signal (steps 1 & 2) — CONTRACT FACT
Raw-card orders CAN trigger a 3DS/challenge that needs a browser round-trip. The SDK signals it in
the **response object**, not by an exception:
- `Order.Status` / `OrderAuthorizeResponse.Status` = **`OrderStatus.PayerActionRequired`** (wire
  `PAYER_ACTION_REQUIRED`) — this is the definitive "buyer must be redirected" signal. (map:
  `enums.md` `OrderStatus`; `records-1-Ac-Pa.md` `Order`/`OrderAuthorizeResponse`.)
- Additionally, the card `AuthenticationResult` (`CardResponse.AuthenticationResult` →
  `AuthenticationResponse.ThreeDSecure` → `ThreeDSecureAuthenticationResponse` with
  `AuthenticationStatus (ParesStatus)` / `EnrollmentStatus`) and a HATEOAS entry in `Links`
  carry the challenge detail. (map: `records-1-Ac-Pa.md` `AuthenticationResponse`,
  `records-2-Pa-Ve.md` `ThreeDSecureAuthenticationResponse`.)
- **Directive**: after CreateOrder/AuthorizeOrder, if `Status == OrderStatus.PayerActionRequired`,
  **STOP and report** (do NOT build an approval round-trip). To keep the card path non-interactive,
  set `CardRequest.Attributes.Verification.Method` = `OrdersCardVerificationMethod.ScaWhenRequired`
  (that record's default) rather than forcing SCA; with sandbox test Visa 4111… this normally
  completes without challenge, but a challenge is still possible.
  `UNVERIFIED`: the exact `Rel` string on the challenge link (`LinkDescription.Rel` is a free
  `string`, not an enum in the map) — treat the `Status == PAYER_ACTION_REQUIRED` check as the
  authoritative trigger and read any link best-effort.

#### Step 3 — CaptureAuthorizedPayment (`client.Payments`) · map: `operations/Payments.md`
- **Signature**: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse!, string? payPalRequestId!, string? payPalAuthAssertion!, CaptureRequest? body!, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `authorizationId` from step 2. `payPalRequestId` = idempotency. `body` may be `null` for full capture.
- **Returns**: `CapturedPayment` (map `records-1-Ac-Pa.md`): `Id (id)` · `Status (status): CaptureStatus?` · `Amount (amount): Money?` · `FinalCapture (final_capture): bool? = false` · **`SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`** · `InvoiceId` · `CustomId`.
  - **Gross / fee / net** (map `records-2-Pa-Ve.md` `SellerReceivableBreakdown`): `GrossAmount (gross_amount): Money, required` = amount captured · `PaypalFee (paypal_fee): Money?` = PayPal's fee · `NetAmount (net_amount): Money?` = net proceeds to merchant. Each `Money` = `CurrencyCode (currency_code): string` + `Value (value): string`. (Also `ReceivableAmount`, `PaypalFeeInReceivableCurrency`, `ExchangeRate`, `PlatformFees` for FX/multiparty cases.)
- **Request `CaptureRequest`** (map `records-1-Ac-Pa.md`): `Amount (amount): Money?` (partial capture) · `FinalCapture (final_capture): bool? = false` · `InvoiceId (invoice_id): string?` · `NoteToPayer` · `SoftDescriptor`.
- **Error**: `SdkException<CaptureAuthorizedPaymentError>` — **Case A**. `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback].

#### Step 4 — ReauthorizePayment (`client.Payments`) · map: `operations/Payments.md`
- **Signature**: `ReauthorizePayment(string authorizationId, string? payPalRequestId!, string? payPalAuthAssertion!, ReauthorizeRequest? body!, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `PaymentAuthorization` (map `records-2-Pa-Ve.md`): `Id (id)` · `Status (status): AuthorizationStatus?` · `Amount (amount): Money?` · `ExpirationTime (expiration_time): string?`.
- **Request `ReauthorizeRequest`** (map `records-2-Pa-Ve.md`): `Amount (amount): Money?` — **only `amount` is supported** (per operation note; US allows up to 115% of original, cap +$75).
- **Detecting a stale/expired authorization** (CONTRACT FACTS from map, honor-period semantics from op note `operations/Payments.md`):
  - Read the authorization first via `GetAuthorizedPayment(authorizationId, …)` → `PaymentAuthorization`. `ExpirationTime (expiration_time)` (ISO-8601 string) is when the current authorization/honor window ends; `Status (status): AuthorizationStatus?`. Reauthorize is meaningful only within the 29-day authorization window and after the 3-day honor period; **after ~30 days you must create a NEW authorized payment, not reauthorize**.
  - `AuthorizationStatus` values (map `enums.md`): `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending`. A `Voided`/`Captured`/`Denied` authorization cannot be reauthorized.
  - **"Cannot be renewed" signal**: `SdkException<ReauthorizePaymentError>` — **Case A**; `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. The specific reason is in `Error.Details[].Issue` (see §2.4) — e.g. an authorization outside the reauthorization window / not in a reauthorizable state surfaces as a `422` (or `400`) with a machine-readable `Issue`. **Directive**: branch on `Error.Details[].Issue` string, not on parsing the message; if reauthorize fails as non-renewable, fall back to creating a fresh order+authorization.
  - `UNVERIFIED`: the exact `Issue` token PayPal returns for a non-renewable authorization is not enumerated in the map (it is a free `string` in `ErrorDetails.Issue`) — match defensively and fall back to the generic message when it is unrecognized.

#### Step 5 — VoidPayment (`client.Payments`) · map: `operations/Payments.md`
- **Signature**: `VoidPayment(string authorizationId, string? payPalMockResponse!, string? payPalAuthAssertion!, string? payPalRequestId!, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - Note param order: `payPalRequestId` is the **4th** param (after `payPalAuthAssertion`), no request body.
- **Returns**: `PaymentAuthorization` (fields as step 4). After a successful void, `Status` → `AuthorizationStatus.Voided`.
- **Error**: `SdkException<VoidPaymentError>` — **Case A**. `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. Note (op note): you cannot void an authorization that has been fully captured → expect `409`/`422`.

#### Step 6 — RefundCapturedPayment (`client.Payments`) · map: `operations/Payments.md`
- **Signature**: `RefundCapturedPayment(string captureId, string? payPalMockResponse!, string? payPalRequestId!, string? payPalAuthAssertion!, RefundRequest? body!, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `captureId` from step 3 (`CapturedPayment.Id`). **`payPalRequestId` = the caller-supplied idempotency key.** `body = null` → full refund; supply `RefundRequest.Amount` → partial.
- **Returns**: `Refund` (map `records-2-Pa-Ve.md`): `Id (id)` · `Status (status): RefundStatus?` · `Amount (amount): Money?` · `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` · `InvoiceId` · `CustomId`.
  - **Cumulative-refund tracking** (map `records-2-Pa-Ve.md` `SellerPayableBreakdown`): `GrossAmount (gross_amount)` · `PaypalFee (paypal_fee)` · `NetAmount (net_amount)` · **`TotalRefundedAmount (total_refunded_amount): Money?`** = cumulative amount refunded against the capture. Also `GetCapturedPayment` → `CapturedPayment.Status` = `CaptureStatus.PartiallyRefunded` / `CaptureStatus.Refunded` tracks capture-level refund state (map `enums.md` `CaptureStatus`).
- **Request `RefundRequest`** (map `records-2-Pa-Ve.md`): `Amount (amount): Money?` · `CustomId` · `InvoiceId` · `NoteToPayer` · `PaymentInstruction`.
- **Over-refund enforcement**: PayPal rejects refunds that exceed the remaining capturable amount server-side → `SdkException<RefundCapturedPaymentError>` — **Case A**; `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. An over-refund returns `422`/`409` with the reason in `Error.Details[].Issue`.
- `RefundStatus` values (map `enums.md`): `Cancelled`, `Failed`, `Pending`, `Completed`.

#### Step 7a — Vault a card · map: `operations/Vault.md`
Two supported flows (both grounded):

**(A) Direct card → payment token** (single call):
- `CreatePaymentToken(string? payPalRequestId!, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentTokenResponse`.
- `PaymentTokenRequest` (map `records-2-Pa-Ve.md`): `Customer (customer): Customer?` · `PaymentSource (payment_source): PaymentTokenRequestPaymentSource, required`.
- `PaymentTokenRequestPaymentSource` (map `records-2-Pa-Ve.md`): `Card (card): PaymentTokenRequestCard?` · `Token (token): VaultTokenRequest?`.
- `PaymentTokenRequestCard` (map `records-2-Pa-Ve.md`): `Name` · `Number (number)` (PAN) · `Expiry (expiry)` · `SecurityCode (security_code)` · `Brand (brand): CardBrand?` · `BillingAddress`.

**(B) Setup token → payment token** (two calls; use when card details are collected once and confirmed before vaulting):
- `CreateSetupToken(string? payPalRequestId!, SetupTokenRequest body, …)` → `SetupTokenResponse` (has `Id`, `Status: PaymentTokenStatus?`). `SetupTokenRequest.PaymentSource` = `SetupTokenRequestPaymentSource` → `.Card = SetupTokenRequestCard` (`Number`, `Expiry`, `SecurityCode`, `Brand`, optional `VerificationMethod`, `ExperienceContext`). (map `records-2-Pa-Ve.md`.)
- Then `CreatePaymentToken` with `PaymentTokenRequestPaymentSource.Token = new VaultTokenRequest { Id = <setupTokenId>, Type = VaultTokenRequestType.SetupToken }`. `VaultTokenRequest` (map `records-2-Pa-Ve.md`): `Id (id): string, required` · `Type (type): VaultTokenRequestType, required`. `VaultTokenRequestType` has one member: `SetupToken` (wire `SETUP_TOKEN`) (map `enums.md`).

- **Returns `PaymentTokenResponse`** (map `records-2-Pa-Ve.md`): **`Id (id): string?` = the vault/payment-token id to persist** · `Customer (customer): CustomerResponse?` · `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` · `Links`.
- **SAFE description** (never full PAN): `PaymentTokenResponse.PaymentSource.Card` = `CardPaymentTokenEntity` (map `records-1-Ac-Pa.md`): `Brand (brand): CardBrand?` · `LastDigits (last_digits): string?` · `Expiry (expiry): string?` · `Name`. Build your saved-card label from `Brand` + `LastDigits` + `Expiry` only.
- **Error**: `SdkException<CreatePaymentTokenError>` / `<CreateSetupTokenError>` — **Case A**; accessor is **`TryGetError1(out Error1)`** (NOT `TryGetError`) [CreatePaymentToken: 400,403,404,422,500] · `TryGetRawError`. Error payload is `Error1` (see §2.4).

#### Step 7b — List / get / delete vaulted tokens · map: `operations/Vault.md`
- `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `CustomerVaultPaymentTokensResponse`. Query wire: `customer_id`←`customerId`, `page_size`←`pageSize`, `page`←`page`, `total_required`←`totalRequired`.
  - `CustomerVaultPaymentTokensResponse` (map `records-1-Ac-Pa.md`): `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Customer` · `Links`. **Paginate via `page` + `total_required=true` → read `TotalPages`** to loop all pages (map row: "Pagination: none (only `page`, no `perPage`)" — page manually).
- `GetPaymentToken(string id, …)` → `PaymentTokenResponse`. `DeletePaymentToken(string id, …)` → `void`.
- Errors: all **Case A** with `TryGetError1(out Error1)` · `TryGetRawError`.

#### Step 7c — Pay a later order with a saved vault id · map: `operations/Orders.md` + `records-2-Pa-Ve.md`
- CONTRACT FACT: reference a vaulted **card** token by setting `OrderRequest.PaymentSource.Card = new CardRequest { VaultId = <payment-token-id> }` — `CardRequest.VaultId (vault_id): string?` (map `records-1-Ac-Pa.md`). Do **not** resend PAN/CVC. Then run steps 1→2 (Create then Authorize) as normal.
- Note: `PaymentSource.Token`/`Token` (map `records-2-Pa-Ve.md`) uses `TokenType` whose only member is `BillingAgreement` (map `enums.md`) — that path is for PayPal billing agreements, **not** vaulted cards. For a saved card, use `card.vault_id`.

#### Step 8 — SearchTransactions (`client.TransactionSearch`) · map: `operations/TransactionSearch.md`
- **Signature**: `SearchTransactions(string startDate, string endDate, string? transactionId!, string? transactionType!, string? transactionStatus!, string? transactionAmount!, string? transactionCurrency!, string? paymentInstrumentType!, string? storeId!, string? terminalId!, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `startDate`/`endDate` are **required ISO-8601 strings** (wire `start_date`/`end_date`). The 8 filter params (`transactionId`…`terminalId`) are nullable-no-default → **pass `null`** (call with named args to avoid mis-binding).
  - **Pagination**: `page` (wire `page`) + `pageSize` (wire `page_size`, default 100). Map row: "Pagination: none (only `page`, no `perPage`)" → there is **no auto-pager**; loop `page` from 1 while `page < SearchResponse.TotalPages`. Request `fields: "transaction_info"` (default) to populate `TransactionInfo`.
- **Returns**: `SearchResponse` (map `records-2-Pa-Ve.md`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` · `Page (page): int?` · `TotalItems (total_items): int?` · **`TotalPages (total_pages): int?`** (loop bound) · `StartDate`/`EndDate`/`LastRefreshedDatetime` · `Links`.
  - `TransactionDetails` (map `records-2-Pa-Ve.md`): `TransactionInfo (transaction_info): TransactionInformation?` (+ PayerInfo/ShippingInfo/CartInfo/StoreInfo).
  - **Reconciliation fields** — `TransactionInformation` (map `records-2-Pa-Ve.md`): `TransactionId (transaction_id): string?` · **`InvoiceId (invoice_id): string?`** · **`CustomField (custom_field): string?`** · `PaypalReferenceId (paypal_reference_id): string?` · `TransactionAmount (transaction_amount): Money?` · `FeeAmount (fee_amount): Money?` · `TransactionStatus (transaction_status): string?` · `TransactionInitiationDate`/`TransactionUpdatedDate`. **Line up eShop orders by matching `InvoiceId`/`CustomField` to the `custom_id`/`invoice_id` you set on the purchase unit in step 1.**
- **Error**: `SdkException<RawError>` — **Case B** (the ONLY Case-B op in this SDK). No typed accessors: read `ex.Error.StatusCode` (`HttpStatusCode`), `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`. (map `operations/TransactionSearch.md`.)

### 2.4 Error payload shapes (typed-error `out` records)

| `out` type | Namespace | Used by (accessor) | Fields (map `records-1-Ac-Pa.md`) |
|---|---|---|---|
| `Error` | `PayPalServerSdk.Models` | Orders + Payments ops via `TryGetError(out Error)` | `Name (name): string !req` · `Message (message): string !req` · `DebugId (debug_id): string !req` · `Details (details): IReadOnlyList<ErrorDetails>?` · `Links` |
| `Error1` | `PayPalServerSdk.Models` | Vault ops via `TryGetError1(out Error1)` | `Name` · `Message` · `DebugId` · `Details (details): IReadOnlyList<ErrorDetails1>?` · `Links (links): IReadOnlyList<ErrorLinkDescription>?` |
| `DefaultError` | `PayPalServerSdk.Models` | `SearchBalances` via `TryGetDefaultError` (not in scope) | `Name` · `Message` · `DebugId` · `InformationLink` · `Details` · `Links` |
| `ErrorDetails` | `PayPalServerSdk.Models` | inside `Error.Details` | `Field (field): string?` · `Value` · `Location (location): string? = "body"` · **`Issue (issue): string !req`** · `Description` · `Links` |

**Read status/reason like this**: catch `SdkException<{Op}Error>`; call the op's typed accessor
(`TryGetError` for Orders/Payments, `TryGetError1` for Vault) → inspect `Error.Name`,
`Error.Message`, and per-field `Error.Details[].Issue` for the machine-readable reason (over-refund,
non-renewable auth, etc.); else `TryGetRawError(out RawError)` → `raw.StatusCode` +
`raw.ReadAsString()`. The HTTP status itself is implied by which accessor matched (status lists in
each op row). See `dotnet-error-handling` (REQUIRED READING) for the Case A/B mechanics.

### 2.5 Enum value tables (map `enums.md`) — literal C# member (wire value)

| Enum | Members |
|---|---|
| `CheckoutPaymentIntent` | `Capture` (CAPTURE), `Authorize` (AUTHORIZE) |
| `OrderStatus` | `Created`, `Saved`, `Approved`, `Voided`, `Completed`, `PayerActionRequired` (PAYER_ACTION_REQUIRED) |
| `AuthorizationStatus` | `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending` |
| `CaptureStatus` | `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed` |
| `RefundStatus` | `Cancelled`, `Failed`, `Pending`, `Completed` |
| `VaultTokenRequestType` | `SetupToken` (SETUP_TOKEN) |
| `StoreInVaultInstruction` | `OnSuccess` (ON_SUCCESS) |
| `PaymentTokenStatus` | `Created`, `PayerActionRequired`, `Approved`, `Vaulted`, `Tokenized` |
| `TokenType` | `BillingAgreement` (BILLING_AGREEMENT) — billing agreements only, not vaulted cards |
| `CardBrand` | `Visa`, `Mastercard`, `Discover`, `Amex`, `Jcb`, `Maestro`, `Diners`, `Elo`, … (30 members) |

Enums are `StringEnum<T>`, **not** C# enums: use the static member (`CheckoutPaymentIntent.Authorize`)
or `CheckoutPaymentIntent.FromValue("AUTHORIZE")`. See `dotnet-models`.

---

## 3. Trap notes (load the named skill before that step — do not code from the note alone)

- ⚠ Step 0 (client + DI) — the `HttpClient`/handler pipeline lifetime and whether the SDK client
  should be singleton vs transient is not something the constructor signature reveals, and getting it
  wrong causes socket exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before
  wiring `AddPayPalServerSdkClient` / `new PayPalServerSdkClient`.
- ⚠ Step 0 (auth) — *when* credentials must be set relative to client construction, and how the
  bearer token is cached/refreshed across calls, is not visible in the options shape. **MUST load
  `dotnet-authentication`** before setting `Oauth2`.
- ⚠ Step 0 (base URL / retries / pagination) — what `RetryOptions.Timeout` actually bounds, which
  verbs/statuses actually retry, and whether a failed non-idempotent write can be re-sent are not
  answerable from the option names; the manual `page`-loops in steps 7b/8 also live here. **MUST load
  `dotnet-configuration-resilience`** before tuning the client or writing the pagination loops.
- ⚠ Steps 1–7 (request bodies) — unions/`StringEnum`/`required`-member and dropped-unknown-JSON
  behaviour bite when you build `OrderRequest`/`CardRequest`/vault payloads. **MUST load
  `dotnet-models`** before constructing any request model.
- ⚠ Steps 1–8 (calls) — many optional params here are nullable-with-no-default and mis-bind in a
  positional call; the consequence is a silently wrong request. **MUST load
  `dotnet-calling-endpoints`** before the first `client.*` call (use named args).
- ⚠ All error handling — Case A vs Case B, the `TryGetError` vs `TryGetError1` split, and the two
  `JsonException` directions (see REQUIRED READING) make an obvious catch ladder silently wrong.
  **MUST load `dotnet-error-handling`** before writing the boundary.
- ⚠ Tests — the `HttpClient` constructor arg is the fake seam. **MUST load `dotnet-testing`** before
  stubbing the SDK.

---

## 4. REQUIRED READING — load every one BEFORE implementation starts

This sheet deliberately does NOT carry these skills' contents (defaults, worked examples, the parts
a one-line note can't hold). Load each before writing the step it governs:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, `HttpClient` ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 0 — supplying OAuth2 client-credentials, token refresh/caching |
| `dotnet-configuration-resilience` | Step 0 + steps 7b/8 — retries/timeouts, base-URL selection, pagination loops |
| `dotnet-calling-endpoints` | Steps 1–8 — named-argument calls, required vs optional params, async/cancellation |
| `dotnet-models` | Steps 1–7 — request models, `required`/nullability, `StringEnum`, unions, wire names |
| `dotnet-error-handling` | All catch blocks / error middleware — Case A/B, safe status+body reads |
| `dotnet-testing` | Test project — which seam to fake, error/edge coverage |

**Two mandatory `JsonException` hazards for the error boundary** (`System.Text.Json.JsonException`
reaches the boundary from two directions and they need opposite handling):
- a drifted or malformed **2xx** body (a missing `required` member — e.g. `Money.Value`,
  `SellerReceivableBreakdown.GrossAmount`, `Error.Name`) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it
  escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection (e.g. a `422` over-refund) as an
  outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **ASSUMPTION**: eShop order identity is carried into PayPal via `purchase_units[].custom_id` and/or
  `invoice_id` at CreateOrder (step 1), then reconciled from `TransactionInformation.custom_field` /
  `invoice_id` at step 8. Confirm which field(s) you want authoritative.
- **ASSUMPTION**: `PayPal:Currency` is a 3-letter ISO code and the eShop order total is converted to a
  decimal string in that currency's minor units before being placed in `Money.Value` (SDK takes a
  string, does no rounding).
- **ASSUMPTION**: `PayPal:Environment` is expected to map to `ServerEnvironment.Sandbox`. **BLOCKER /
  GAP**: this SDK build (`v1.0.1`) exposes **only** `ServerEnvironment.Sandbox` — there is **no
  Production/Live environment member** (confirmed: map *Servers & auth* + source
  `Servers/ServerEnvironment.cs`). If `PayPal:Environment` is set to anything but sandbox, the only
  way to hit a non-sandbox host is the explicit `PayPal:BaseUrl` override (which does redirect every
  call incl. the token request). Live/production use beyond that override is **not supported by this
  SDK version** — flag before shipping to production.
- **ASSUMPTION**: the vault business account is vault-enabled (as stated). A `customer_id` is needed to
  list a customer's tokens (step 7b); decide whether you pass your own `Customer.MerchantCustomerId`
  at vault time so `ListCustomerPaymentTokens` can be scoped per eShop user.
- **GAP (none blocking)**: every requested capability maps to a concrete SDK operation — no capability
  is missing. Reauthorize/refund "cannot proceed" reasons live in the free-string
  `ErrorDetails.Issue`; exact issue tokens are `UNVERIFIED` (match defensively, fall back to the
  generic message) — see steps 4 and 6.
