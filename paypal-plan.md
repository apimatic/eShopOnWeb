# PayPal .NET SDK integration plan — eShopOnWeb (src/PublicApi, JWT HTTP endpoints)

SDK: `AsadAli.Checkout.Sdk` (NuGet, install version-less), root namespace `PayPalServerSdk`,
client `PayPalServerSdkClient`. Target: PayPal **Sandbox**, direct (advanced) card processing +
card vaulting. Map provenance: tag `v1.0.1`, commit `9653d18`.

All facts below are grounded in the bundled SDK map (`sdk-map.md`, `map/operations/*.md`,
`map/models/*.md`); the base-URL/OAuth and auth-credential facts marked *(source-confirmed)* were
verified against the named SDK source files because the map did not settle them.

---

## 1. Scope & sequence

| # | Feature | Operations (in call order) |
|---|---|---|
| 0 | Client construction / DI / auth / base-URL override | `PayPalServerSdkClientOptions`, `AddPayPalServerSdkClient` |
| 1 | Authorize order total by raw card, no approval | `Orders.CreateOrder` **only** (card-on-create auto-authorizes; do **NOT** call `AuthorizeOrder`) |
| 2 | Authorize using a vaulted card | `Orders.CreateOrder` **only** (card by `vault_id`; auto-authorizes; do **NOT** call `AuthorizeOrder`) |
| 3 | Capture an authorization at fulfilment | `Payments.CaptureAuthorizedPayment` |
| 4 | Re-authorize a stale authorization | `Payments.GetAuthorizedPayment` (detect) → `Payments.ReauthorizePayment` |
| 5 | Void an authorization | `Payments.VoidPayment` |
| 6 | Refund a capture (full/partial, idempotent) | `Payments.RefundCapturedPayment` |
| 7 | Vault a raw card without charging (Flow 2) | `Vault.CreatePaymentToken` |
| 8 | Delete a saved card | `Vault.DeletePaymentToken` |
| 9 | Idempotency for authorize/capture | `payPalRequestId` param on create/authorize/capture |
| 10 | Reconciliation over a date range (all pages) | `TransactionSearch.SearchTransactions` (loop pages) |

**Flow-1 (direct raw card) and Flow-2 (vault raw card) are both achievable server-side with NO
browser/redirect** — the card is supplied in `payment_source` on the server call, so no shopper
approval URL is involved. Neither STOP/blocker condition in the brief is triggered. See
Assumptions & Blockers for the live-only caveats.

**CORRECTION (confirmed by live sandbox evidence):** for the AUTHORIZE hold, supplying the card in
`CreateOrder.payment_source.card` **auto-authorizes during create** — a follow-up `AuthorizeOrder`
returns `ORDER_ALREADY_AUTHORIZED`. The correct flow is a **single** `CreateOrder` call
(`intent=AUTHORIZE`, `purchase_units`, `payment_source.card`); read the authorization from the
**`Order` create-response** envelope; **do not** call `AuthorizeOrder`. The two-call
create→authorize sequence in an earlier draft was wrong. The map models both card placements but
does not define the auto-authorize *timing* (live-only); the sandbox behaviour resolves it.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each
> one from that type's own map row, never from where a neighbouring type sits. A members table
> names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace).
> Enums, unions, auth, server and client-config types are spread across different child
> namespaces, and two types configured side by side in the same options object routinely live in
> different ones. Dropping a type to the root or to `.Models` makes the implementer guess the
> wrong `using`, and the build breaks.

### Namespaces used in this sheet
| Kind | Namespace |
|---|---|
| Client, options, `ServerOptions`, `Server` | `PayPalServerSdk` |
| Operation controllers (`client.Orders` etc.) | `PayPalServerSdk.Api` |
| Records (request/response models) | `PayPalServerSdk.Models` |
| Enums (`CheckoutPaymentIntent`, `CardBrand`, …) | `PayPalServerSdk.Models.Enums` |
| Typed error classes (`{Operation}Error`) | `PayPalServerSdk.Errors` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` *(source-confirmed)* |
| `ServerEnvironment`, `DefaultOptions` (+ nested `SandboxOptions`) | `PayPalServerSdk.Servers` *(source-confirmed)* |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` |

### 2.0 Client construction / DI / auth / environment / base-URL override

`PayPalServerSdkClientOptions` (root ns) properties (`sdk-map.md` → *Getting a client*):
`Environment: ServerEnvironment`, `Retry: RetryOptions`, `Logging: LoggingOptions`,
`Server: ServerOptions`, `Oauth2: OAuth2ClientCredentials?`,
`Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.

- **Constructor**: `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`.
- **DI**: `services.AddPayPalServerSdkClient(o => { … })` (`ServiceCollectionExtensions.cs`).
- **Auth (OAuth2 client-credentials)** *(source-confirmed `AuthSchemes.cs`, `OAuth2ClientCredentials.cs`)*:
  set `options.Oauth2 = new OAuth2ClientCredentials { ClientId = <PayPal:ClientId>, ClientSecret = <PayPal:Secret> }`.
  `ClientId` and `ClientSecret` are `required string`; `Scope` is optional (`string?`). The SDK
  fetches and caches the bearer token itself — do not call any token endpoint by hand.
- **Environment**: `options.Environment = ServerEnvironment.Sandbox` (the only member;
  `ServerEnvironment.Default()` also returns `Sandbox`). *(source-confirmed `ServerEnvironment.cs`)*
- **Base-URL override `PayPal:BaseUrl` (verbatim, incl. the OAuth token call)** *(source-confirmed
  `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs`)*: the effective base URL is
  `options.Server.Default.Sandbox.BaseUrl` (a plain `string`, default
  `"https://api-m.sandbox.paypal.com"`). Set it when `PayPal:BaseUrl` is present:
  `options.Server = new ServerOptions { Default = new DefaultOptions { Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = <PayPal:BaseUrl> } } };`
  The OAuth token request is built as `server.Default("/v1/oauth2/token")`, i.e. it resolves
  through the **same** `Sandbox.BaseUrl` as every v1/v2/v3 call — so this single override governs
  the token endpoint too, exactly as the brief requires. When `PayPal:BaseUrl` is absent, leave
  `Server` at its default (sandbox host derived from `Environment`).

### 2.1 Orders — `client.Orders` (`operations/Orders.md`; models `records-1-Ac-Pa.md`)

| Op | Signature (params in order) | Request model + fields | Response envelope + fields to read | Error |
|---|---|---|---|---|
| `CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — first 5 nullable **must pass explicitly** (`null` to skip); pass `payPalRequestId` for idempotency; for AUTHORIZE-with-card pass `prefer: "return=representation"` (see UNVERIFIED note §4) | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `Payer (payer): Payer?`, `PaymentSource (payment_source): PaymentSource?`, `ApplicationContext (application_context): OrderApplicationContext?` | `Order`: `Id (id): string?`, `Status (status): OrderStatus?`, `Intent`, `PaymentSource (payment_source): PaymentSourceResponse?`, `Links`, and **the authorization is nested**: `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` → `PurchaseUnit.Payments (payments): PaymentCollection?` → `PaymentCollection.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → each `AuthorizationWithAdditionalData` has `Id (id): string?` (**the authorization id you keep**), `Status (status): AuthorizationStatus?`, `ExpirationTime (expiration_time): string?` | `SdkException<CreateOrderError>` — **Case A**; `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` |
| `AuthorizeOrder` — **NOT used in the card flows** (card-on-create already authorizes; calling this returns `ORDER_ALREADY_AUTHORIZED`). Documented for completeness / the buyer-approval path only. | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 5 nullable params **must pass explicitly** | `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` | `OrderAuthorizeResponse`: `Id`, `Status (status): OrderStatus?`, **`PurchaseUnits[].Payments.Authorizations[]`** → each `AuthorizationWithAdditionalData` has `Id (id): string?` and `Status (status): AuthorizationStatus?` | `SdkException<AuthorizeOrderError>` — **Case A**; `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` |

**Request-body construction — direct raw card (Feature 1):**
- `OrderRequest.PurchaseUnits = [ new PurchaseUnitRequest { Amount = new AmountWithBreakdown { CurrencyCode = <PayPal:Currency>, Value = "<amount>" } } ]` (`AmountWithBreakdown`: `CurrencyCode !req`, `Value !req`, `Breakdown: AmountBreakdown?`).
- `OrderRequest.Intent = CheckoutPaymentIntent.Authorize` (enum wire `AUTHORIZE`).
- Card goes in the **CreateOrder** payment source: `OrderRequest.PaymentSource = new PaymentSource { Card = new CardRequest { … } }`. (Do **not** also build an `OrderAuthorizeRequest` — that path is unused; card-on-create authorizes.)
- `CardRequest` fields (`records-1`): `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?` (format `YYYY-MM`), `SecurityCode (security_code): string?`, `BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?`, `Attributes (attributes): CardAttributes?`, `StoredCredential`, `ExperienceContext (experience_context): CardExperienceContext?`. For 4111… test card set `Number`, `Expiry`, `SecurityCode`, `Name`, `BillingAddress`.
- **Optional card verification knob** (`records-1`; none `!req`): `CardRequest.Attributes = new CardAttributes { Verification = new CardVerification { Method = <OrdersCardVerificationMethod> } }`. `CardVerification.Method (method): OrdersCardVerificationMethod?`, source default `ScaWhenRequired`. `OrdersCardVerificationMethod` members (ns `PayPalServerSdk.Models.Enums`): `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)`. Setting this is legal but the map does **not** promise it changes a `TRANSACTION_REFUSED` outcome (that is a gateway decline, live-only — see §4).
- `Address` (`records-1`): `AddressLine1`, `AddressLine2`, `AdminArea2` (city), `AdminArea1` (state), `PostalCode`, `CountryCode (country_code): string !req`.
- **Single server call, no browser**: `CreateOrder(intent=AUTHORIZE, purchase_units, payment_source.card)` — this both creates the order AND places the authorization (auto-authorize confirmed by live sandbox: a follow-up `AuthorizeOrder` returns `ORDER_ALREADY_AUTHORIZED`). **Do NOT call `AuthorizeOrder`.** Read the authorization id/status from the `Order` create-response envelope: `Order.PurchaseUnits[].Payments.Authorizations[].Id` / `.Status` (`AuthorizationWithAdditionalData`) — it is NOT a top-level field. If that block is absent in the response, fall back to `GetOrder(order.Id, …)` (see §4 UNVERIFIED on `prefer`).

**Vaulted card (Feature 2):** identical single-call flow — put the vaulted card in the
**CreateOrder** payment source as `PaymentSource = new PaymentSource { Card = new CardRequest { VaultId = "<payment-token-id>" } }`
(no PAN/CVC), and read the authorization from the **same** `Order.PurchaseUnits[].Payments.Authorizations[].Id`/`.Status`
envelope; do **not** call `AuthorizeOrder`. Whether create-with-vaulted-card auto-authorizes
exactly like the raw card is the same live-behaviour parallel — code it the same and detect a
pre-existing authorization (presence of `authorizations`, or an `ORDER_ALREADY_AUTHORIZED` issue)
rather than assuming. (`CardRequest.VaultId` is the vaulted-card reference.) The alternative
`PaymentSource.Token` (`Token { Id, Type: TokenType }`) is **not** for vaulted cards — `TokenType`
has only `BillingAgreement (BILLING_AGREEMENT)`; use `Card.VaultId`.

### 2.2 Payments — `client.Payments` (`operations/Payments.md`; models `records-1`/`records-2`)

| Op | Signature | Request model | Response + fields to read | Error |
|---|---|---|---|---|
| `CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullable params **must pass explicitly**; pass `payPalRequestId` for idempotency; set `prefer:"return=representation"` to get the breakdown | `CaptureRequest?`: `Amount (amount): Money?`, `InvoiceId`, `FinalCapture (final_capture): bool? = false`, `NoteToPayer`, `SoftDescriptor`. Pass `null` body for full-amount capture | `CapturedPayment`: `Id`, `Status (status): CaptureStatus?`, `Amount (amount): Money?` (captured gross), **`SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`** → `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?` (each `Money`: `CurrencyCode`, `Value`) | `SdkException<CaptureAuthorizedPaymentError>` — **Case A**; `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| `ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 nullable params **must pass explicitly** | `ReauthorizeRequest?`: `Amount (amount): Money?` (only field supported) | `PaymentAuthorization`: `Id`, `Status (status): AuthorizationStatus?`, `ExpirationTime (expiration_time): string?`, `Amount` | `SdkException<ReauthorizePaymentError>` — **Case A**; `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| `VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 nullable params **must pass explicitly** | none (no body) | `PaymentAuthorization`: `Id`, `Status (status): AuthorizationStatus?` (expect `Voided`) | `SdkException<VoidPaymentError>` — **Case A**; `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| `RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullable params **must pass explicitly**; pass caller idempotency key as `payPalRequestId` | `RefundRequest?`: `Amount (amount): Money?` (omit/`null` body ⇒ full refund; set `Amount` ⇒ partial), `CustomId`, `InvoiceId`, `NoteToPayer`, `PaymentInstruction` | `Refund`: `Id (id): string?`, `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` | `SdkException<RefundCapturedPaymentError>` — **Case A**; `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| `GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 2 nullable params **must pass explicitly** | none | `PaymentAuthorization`: `Status (status): AuthorizationStatus?`, `ExpirationTime (expiration_time): string?` (used to detect staleness) | `SdkException<GetAuthorizedPaymentError>` — **Case A**; `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |

- **Idempotency key wire (Features 6 & 9):** the `payPalRequestId` parameter is the SDK's
  first-class surface for the `PayPal-Request-Id` header. Pass the caller's key there on
  `CreateOrder` (which now carries the authorize), `CaptureAuthorizedPayment`, and
  `RefundCapturedPayment`.
  Reusing the same key does not double-execute; two distinct partial refunds use two distinct
  keys. There is no need to hand-set the header via `RequestOptions` when a dedicated param exists.
- **`prefer` (full breakdowns):** default is `"return=minimal"`. To receive
  `seller_receivable_breakdown` (capture) / `seller_payable_breakdown` (refund) populated, pass
  `prefer: "return=representation"`. See the UNVERIFIED note in §4 about reading the breakdown
  defensively.
- **Detecting reauthorization need / terminal state (Feature 4):** an authorization is stale when
  its honor period has passed — read `PaymentAuthorization.ExpirationTime` (via
  `GetAuthorizedPayment`) and compare to now; `Status` (`AuthorizationStatus`) members are
  `Created, Captured, Denied, PartiallyCaptured, Voided, Pending` — there is **no** `EXPIRED`
  member, so expiry is inferred from `ExpirationTime`, not `Status`. When it can no longer be
  reauthorized (past the 29-day window), `ReauthorizePayment` throws
  `SdkException<ReauthorizePaymentError>` (typically 422); read the reason via
  `ex.Error.TryGetError(out Error e)` then `e.Details[].Issue` — see UNVERIFIED note in §4.

**Money / breakdown types** (`records-1`/`records-2`, ns `PayPalServerSdk.Models`):
`Money`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req`.
`SellerReceivableBreakdown`: `GrossAmount !req`, `PaypalFee: Money?`, `NetAmount: Money?`,
`ReceivableAmount: Money?`, `ExchangeRate`, `PlatformFees`.
`SellerPayableBreakdown` (refund): `GrossAmount: Money?`, `PaypalFee: Money?`, `NetAmount: Money?`,
`TotalRefundedAmount: Money?`.

### 2.3 Vault — `client.Vault` (`operations/Vault.md`; models `records-2-Pa-Ve.md`)

| Op | Signature | Request model | Response + fields to read | Error |
|---|---|---|---|---|
| `CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` **must pass explicitly** | `PaymentTokenRequest`: `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` | `PaymentTokenResponse`: **`Id (id): string?`** (vault id), `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `Card (card): CardPaymentTokenEntity?` → `LastDigits (last_digits): string?`, `Brand (brand): CardBrand?`, `Expiry (expiry): string?` | `SdkException<CreatePaymentTokenError>` — **Case A**; `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError` |
| `DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | none | `void` (Task) — success = no throw | `SdkException<DeletePaymentTokenError>` — **Case A**; `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` |

**Vault a raw card without charging (Feature 7)** — direct payment-token-from-card, no setup
token, no experience context ⇒ no browser step:
- `PaymentTokenRequest.PaymentSource = new PaymentTokenRequestPaymentSource { Card = new PaymentTokenRequestCard { … } }`.
- `PaymentTokenRequestCard` (`records-2`): `Name (name): string?`, `Number (number): string?`,
  `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `Brand (brand): CardBrand?`,
  `BillingAddress (billing_address): Address?`. Set `Number`/`Expiry`/`SecurityCode`/`Name` for 4111….
- Optionally attach `Customer = new Customer { Id / MerchantCustomerId }` to group tokens per user.
- **Safe description to return** (never the PAN): from `PaymentTokenResponse.PaymentSource.Card`
  (`CardPaymentTokenEntity`) read `Brand` (enum `CardBrand`, e.g. `Visa`→wire `VISA`), `LastDigits`,
  `Expiry`. The response never carries the full number.
- The two-step alternative (`CreateSetupToken` → `CreatePaymentToken` referencing the setup token
  via `PaymentTokenRequestPaymentSource.Token = new VaultTokenRequest { Id, Type = VaultTokenRequestType.SetupToken }`)
  exists but is **not required** for a raw card and is not planned here.

### 2.4 TransactionSearch — `client.TransactionSearch` (`operations/TransactionSearch.md`)

| Op | Signature | Response + fields to read | Error |
|---|---|---|---|
| `SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 8 params `transactionId`…`terminalId` are nullable with no default ⇒ **must pass explicitly** (`null` to skip); **call with named arguments** | `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`. Each `TransactionDetails.TransactionInfo` (`TransactionInformation`) → `TransactionId (transaction_id): string?`, `TransactionStatus (transaction_status): string?`, `TransactionAmount (transaction_amount): Money?`, `TransactionInitiationDate`/`TransactionUpdatedDate (…): string?` | `SdkException<RawError>` — **Case B (the only one in the SDK)**; read `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()` — **no typed `TryGetError`** |

- **Dates**: `startDate`/`endDate` are ISO-8601 strings (`start_date`/`end_date` query params);
  pass the caller's ISO-8601 range verbatim.
- **`fields` defaults to `"transaction_info"`** — that is exactly the block we read; leave it.
- **Pagination — cover the WHOLE range (Feature 10):** there is no auto-pager. Loop manually:
  call with `page: 1`, read `SearchResponse.TotalPages`, then re-call incrementing `page` from 1
  through `TotalPages` (keep `pageSize` fixed), accumulating `TransactionDetails`. Use named args
  (`startDate:`, `endDate:`, `page:`, `pageSize:`) so the 8 skipped nullable filters bind as `null`.

### 2.5 Enum value tables (needed subset, ns `PayPalServerSdk.Models.Enums`)

| Enum | C# member (wire) — members in scope |
|---|---|
| `CheckoutPaymentIntent` | `Authorize (AUTHORIZE)`, `Capture (CAPTURE)` |
| `OrderStatus` | `Created, Saved, Approved, Voided, Completed, PayerActionRequired` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending` — **no `EXPIRED`** |
| `CaptureStatus` | `Completed, Declined, PartiallyRefunded, Pending, Refunded, Failed` |
| `RefundStatus` | `Cancelled, Failed, Pending, Completed` |
| `CardBrand` | `Visa (VISA)`, `Mastercard`, `Amex`, `Discover`, … `Unknown` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` — **not** for vaulted cards |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `PaymentTokenStatus` | `Created, PayerActionRequired, Approved, Vaulted, Tokenized` |

### 2.6 Error boundary — types (see REQUIRED READING before writing the catch ladder)

- All ops throw `SdkException<T>` (ns `PayPalServerSdk.Core.Exceptions`). 39 of 40 ops are
  **Case A** (`T` = a `{Operation}Error : ApiError`, ns `PayPalServerSdk.Errors`);
  `SearchTransactions` is the sole **Case B** (`T` = `RawError`).
- Case-A typed payload readers: Orders + Payments ops expose `TryGetError(out Error)` (payload
  `Error`, ns `PayPalServerSdk.Models`: `Name !req`, `Message !req`, `DebugId !req`,
  `Details: IReadOnlyList<ErrorDetails>?`, `Links`; `ErrorDetails`: `Field`, `Value`, `Location`,
  `Issue !req`, `Description`). Vault ops expose `TryGetError1(out Error1)` (payload `Error1`, with
  `Details: IReadOnlyList<ErrorDetails1>?`). Every Case-A error also inherits
  `TryGetRawError(out RawError)` for statuses outside the typed set; several Payments ops add
  `TryGetNoContent(out RawError)` for 500. `TryGetRawError` is a fallback, not a catch-all over the
  typed shape.
- `RawError` (ns `PayPalServerSdk.Core.ErrorResponse`): `StatusCode: HttpStatusCode`,
  `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` — this is the ONLY way to read status/body
  for `SearchTransactions`.
- **No `…Result` no-throw variants exist** — every op is throw-only; you must `try/catch`.
- **401/403 across every op = auth/config, not payload** — check credentials, base URL, token
  before touching call sites.

---

## 3. Trap notes (load the named skill before writing that step)

> ⚠ Step 0 (client & DI) — how the SDK's `HttpClient`/handler pipeline must be owned and reused,
> and which parts of the client are safe to register transient, is not visible in the constructor
> signature. **MUST load `dotnet-client-initialization`** before wiring `AddPayPalServerSdkClient`
> or `new PayPalServerSdkClient(...)`.

> ⚠ Step 0 (auth) — when and where credentials must be set relative to client construction, and
> how to source them from configuration/rotation, is a usage concern the property name does not
> convey. **MUST load `dotnet-authentication`** before wiring `Oauth2` credentials.

> ⚠ Step 0 (base URL / resilience) — what the SDK's `Retry.Timeout` actually bounds (whole call vs
> per attempt) and whether a failed **write** (`CreateOrder`/`AuthorizeOrder`/`Capture`/`Refund`)
> can be silently re-sent under retry are not derivable from the option names; this directly
> affects idempotency-key strategy. **MUST load `dotnet-configuration-resilience`** before tuning
> `RetryOptions` or the base URL.

> ⚠ Steps 1–10 (every call) — which optional params mis-bind in a positional call, and how the
> request/response envelopes are shaped, is a calling concern beyond the signature (e.g. the
> authorization id lives at `PurchaseUnits[].Payments.Authorizations[].Id`, not top level).
> **MUST load `dotnet-calling-endpoints`** before the first `client.*` call.

> ⚠ Steps 1–8 (model building) — enums here are `StringEnum<T>` (not C# enums), unmodeled JSON is
> dropped on deserialize, and required members must be set in the initializer. **MUST load
> `dotnet-models`** before constructing any request payload or mapping a response to a domain type.

> ⚠ Every step (error boundary) — which exception types actually reach the catch, and why an
> SDK-exception-only ladder is silently wrong, is covered only in the companion. **MUST load
> `dotnet-error-handling`** before writing the try/catch (see REQUIRED READING for the two
> `JsonException` hazards).

> ⚠ Step 10 (tests) — the fake seam is the `HttpClient` constructor arg; match eShopOnWeb's
> existing test framework/assertion style. **MUST load `dotnet-testing`** before writing tests.

---

## 4. Assumptions & Blockers

- **No STOP/blocker triggered.** Direct raw-card authorize (Feature 1) and raw-card vaulting
  (Feature 7) are both expressible as server-side calls with `payment_source` supplied and **no**
  approval URL/redirect; transaction search/reporting exists (`TransactionSearch.SearchTransactions`)
  and supports date-range + pagination. All three brief STOP conditions are therefore not met.
- **AUTHORIZE flow corrected from live sandbox evidence.** Card-on-`CreateOrder` (intent=AUTHORIZE)
  **auto-authorizes during create**; calling `AuthorizeOrder` afterward returns
  `ORDER_ALREADY_AUTHORIZED`. The implemented flow is a single `CreateOrder`, reading the
  authorization from `Order.PurchaseUnits[].Payments.Authorizations[].Id`/`.Status`. The map models
  both card placements but does not define auto-authorize *timing* — that is a live fact, now
  resolved. §2.1 rows and the Feature-1/2 bullets are updated accordingly.
- **`TRANSACTION_REFUSED` on the split (create-then-authorize) path — live/gateway, not a schema
  gap.** The map marks **no** `CardRequest` field `!req`, and a missing required member surfaces as
  a 422 field/`issue` error, not `TRANSACTION_REFUSED` (a processor decline). The map cannot name a
  field whose addition guarantees approval. `CardRequest.Attributes.Verification.Method`
  (`OrdersCardVerificationMethod`: `ScaAlways`/`ScaWhenRequired`/`_3DSecure`/`AvsCvv`) and
  `CardExperienceContext` are legal optional knobs but their effect on the gateway decision is
  live-only. Prefer the confirmed card-on-create path over the split path.
- **`prefer: "return=representation"` on `CreateOrder` for the authorizations block — UNVERIFIED
  (live-only).** The map lists only the default `"return=minimal"` and neither the exact
  representation-token nor whether representation is required to inline
  `purchase_units[].payments.authorizations`. **Directive:** send `prefer: "return=representation"`
  on `CreateOrder`, read `Order.PurchaseUnits?.Payments?.Authorizations?` best-effort, and if the
  block is absent fall back to `GetOrder(order.Id, …)` to fetch the authorization id/status rather
  than assuming it is inline.
- **Assumption (merchant enablement).** The brief states the sandbox business account is enabled
  for Advanced (direct) Card Processing and card vaulting. The SDK models permit raw PAN in
  `payment_source.card` / `PaymentTokenRequestCard`; whether the account is actually entitled is a
  merchant-config fact the map/source cannot show. If not entitled, `CreateOrder` (card) /
  `CreatePaymentToken` return a Case-A error — handle it through the typed error boundary.
- **Assumption (config keys).** `PayPal:ClientId`, `PayPal:Secret`, `PayPal:Currency`,
  `PayPal:Environment` (=`sandbox`), and optional `PayPal:BaseUrl` are bound from configuration.
  `PayPal:BaseUrl`, when set, maps verbatim onto `options.Server.Default.Sandbox.BaseUrl`
  (source-confirmed to govern the OAuth token call too). Exact key names are the integrator's to
  finalize against eShopOnWeb conventions.
- **`prefer: "return=representation"` string — UNVERIFIED (live-only).** The map fixes the param
  default `"return=minimal"` but not the exact representation-token or whether a minimal response
  really omits `seller_receivable_breakdown`. **Directive:** send
  `prefer: "return=representation"` on capture/refund, then read the breakdown **best-effort** —
  `SellerReceivableBreakdown` is nullable on `CapturedPayment` and its `PaypalFee`/`NetAmount` are
  nullable `Money?`; if any is null, fall back to `CapturedPayment.Amount` for the gross and
  surface fee/net as unavailable rather than throwing.
- **Reauthorize "cannot reauthorize" reason string — UNVERIFIED (live-only).** The map/source do
  not enumerate the exact `issue` code returned when the 29-day window has passed. **Directive:**
  on `SdkException<ReauthorizePaymentError>`, extract the reason best-effort
  (`TryGetError(out Error e)` → first `e.Details[].Issue`/`e.Message`); if that shape is absent,
  fall back to `TryGetRawError`/status and report a generic "authorization can no longer be
  reauthorized" instead of assuming a specific issue literal.

---

## 5. REQUIRED READING (load ALL before implementation starts)

These companion skills are **not** reproduced in this sheet — load each before its step; the sheet
deliberately omits their defaults, worked examples, and the parts you must still wire yourself.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, `HttpClient` ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 0 — supplying `Oauth2` client-credentials, secret sourcing, rotation |
| `dotnet-configuration-resilience` | Step 0 — retries/backoff, what `Timeout` bounds, base-URL selection, pagination |
| `dotnet-calling-endpoints` | Steps 1–10 — named-arg binding, request/response envelope shapes, async/cancellation |
| `dotnet-models` | Steps 1–8 — building request models, `StringEnum<T>`, required members, dropped-field trap |
| `dotnet-error-handling` | Every step — the error/exception boundary (mandatory; see rows below) |
| `dotnet-testing` | Tests — the `HttpClient` fake seam, error/edge coverage |

**Two `System.Text.Json.JsonException` hazards at the error boundary — both must be handled, and
they need opposite handling:**
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException`
  from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets
  it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.
