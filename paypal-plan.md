# PayPal .NET SDK — Contract Sheet (eShopOnWeb, direct card / sandbox)

SDK identity (from `sdk-map.md`): NuGet package **`AsadAli.Checkout.Sdk`** (install version-less:
`dotnet add package AsadAli.Checkout.Sdk` — do NOT pin). Root namespace **`PayPalServerSdk`**;
client class **`PayPalServerSdkClient`**; options **`PayPalServerSdkClientOptions`**. Map generated
from source commit `9653d18`, tag `v1.0.1`, target `netstandard2.0`. The installed package floats to
latest — if any name below fails to compile, trust the compiler and report drift.

This sheet is contract facts only. It does NOT contain application code — implement against it.

---

## 1. Scope & sequence

| # | Capability | Operations (in call order) |
|---|---|---|
| 0 | Client + DI + auth + base-URL/env wiring | `AddPayPalServerSdkClient(...)`; set `Oauth2`, `Environment`, `Server` |
| 1 | Authorize w/ raw card (no browser) | `Orders.CreateOrder` (intent=AUTHORIZE, card) → `Orders.AuthorizeOrder` (fallback if auth not already in create response) |
| 2 | Capture an authorization | `Payments.CaptureAuthorizedPayment` |
| 3 | Re-authorize stale auth / read status | `Payments.ReauthorizePayment`; `Payments.GetAuthorizedPayment` |
| 4 | Void/release an authorization | `Payments.VoidPayment` |
| 5 | Refund a capture (full/partial) | `Payments.RefundCapturedPayment` |
| 6 | Vault a card, then pay with it | `Vault.CreateSetupToken` → `Vault.CreatePaymentToken` (or `CreatePaymentToken` direct); later `Orders.CreateOrder`/`AuthorizeOrder` with `card.vault_id` |
| 7 | Transaction search + full pagination | `TransactionSearch.SearchTransactions` looped over `TotalPages` |
| 8 | Error handling | `SdkException<TError>` around every call |

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

### 2.0 Namespaces (add a `using` per kind — child namespaces are NOT imported transitively)

| Kind | Namespace | Examples |
|---|---|---|
| Client, options, `ServerOptions` | `PayPalServerSdk` | `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` |
| Controllers | `PayPalServerSdk.Api` | `Orders`, `Payments`, `Vault`, `TransactionSearch` (reached via `client.X`) |
| Records (request/response models) | `PayPalServerSdk.Models` | `OrderRequest`, `CardRequest`, `CapturedPayment`, `Error`, `Error1`, `DefaultError`, … |
| Enums (`StringEnum<T>`) | `PayPalServerSdk.Models.Enums` | `CheckoutPaymentIntent`, `AuthorizationStatus`, `CardBrand`, … |
| Typed `{Op}Error` classes | `PayPalServerSdk.Errors` | `AuthorizeOrderError`, `CaptureAuthorizedPaymentError`, … |
| `ServerEnvironment`, `DefaultOptions` | `PayPalServerSdk.Servers` | `ServerEnvironment.Sandbox`, `options.Server.Default` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | credentials object |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` | catch type |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` | raw-error fallback |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` | `options.Retry` |

### 2.1 Client construction, auth, environment, base-URL override

Source: `sdk-map.md` (*Getting a client*, *Servers & auth*, client-options table); confirmed against
SDK source `PayPalServerSdkClientOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/ServerEnvironment.cs`,
`Server.cs`, `AuthSchemes.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`.

- **Client ctor**: `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`.
  DI: `services.AddPayPalServerSdkClient(o => { … })` (from `ServiceCollectionExtensions.cs`). API groups
  are client properties: `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch`.
- **Auth (OAuth2 client-credentials)**: set `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = … }`.
  `OAuth2ClientCredentials` members (verbatim from source): `ClientId: string` **required**,
  `ClientSecret: string` **required**, `Scope: string?` optional. The SDK fetches/refreshes the bearer
  token itself; do NOT hand-roll the token call. Bind `PayPal:ClientId` / `PayPal:ClientSecret` here.
- **(a) Sandbox vs live selection**: `options.Environment` is a `ServerEnvironment`
  (`PayPalServerSdk.Servers`). **The only member that exists in this SDK build is `ServerEnvironment.Sandbox`**
  (source `Servers/ServerEnvironment.cs` — there is NO `Live`/`Production` member; `ServerEnvironment.Default()`
  also returns `Sandbox`). Default sandbox base URL is `https://api-m.sandbox.paypal.com`. Bind
  `PayPal:Environment="sandbox"` → `options.Environment = ServerEnvironment.Sandbox`. **See Blockers B1** for
  what "live" requires given no live enum member.
- **(b) Explicit base-URL override (applies to API calls AND the OAuth token request)**: set
  `options.Server.Default.Sandbox.BaseUrl = "<PayPal:BaseUrl verbatim>"`. `options.Server` is `ServerOptions`
  (`PayPalServerSdk`); `.Default` is `DefaultOptions` (`PayPalServerSdk.Servers`); `.Sandbox` is
  `DefaultOptions.SandboxOptions` with a single `string BaseUrl`. Verified from source: `Server.Default(path)`
  → `DefaultOptions.Resolve(env, path)` builds every request URL from `Sandbox.BaseUrl`, and `AuthSchemes.cs`
  builds the OAuth endpoint as `server.Default("/v1/oauth2/token")` through the **same** resolver — so one
  `BaseUrl` override drives both the token endpoint and all API calls. The SDK appends the fixed paths
  (`/v1/oauth2/token`, `/v2/checkout/orders`, …) to your base; set `BaseUrl` to the host/origin, not a full
  endpoint path. When `PayPal:BaseUrl` is unset, leave `BaseUrl` at its default (sandbox host).
- **Currency**: `PayPal:Currency` (e.g. "USD") is passed as `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode`
  wire field on every amount you build; the SDK has no global currency setting.

### 2.2 Operations

Legend: fields shown `CSharpName (wire_name): Type` · `!req` = C# `required` · trailing `?` = nullable/optional.
Every op below is **throw-based, no `…Result` variant**. `payPalRequestId` = the `PayPal-Request-Id`
idempotency header. Pass explicit `null` for every nullable-no-default header param you are skipping.

#### Capability 1 — Authorize with a raw card

**`Orders.CreateOrder`** — `POST /v2/checkout/orders` — map `operations/Orders.md`.
- **Signature**: `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 header params (`payPalMockResponse`…`payPalAuthAssertion`) are nullable-no-default → **pass explicitly** (`null` to skip).
  - Idempotency: pass `payPalRequestId:`. To get the fully-populated body (incl. any inline authorization) pass `prefer: "return=representation"` (Prefer values are API-defined strings; default is `"return=minimal"`).
- **Request `OrderRequest`** (`records-1`): `Intent (intent): CheckoutPaymentIntent !req`, `Payer (payer): Payer?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `ApplicationContext (application_context): OrderApplicationContext?`.
  - `Intent` = `CheckoutPaymentIntent.Authorize`.
  - `PurchaseUnitRequest` (`records-1`): `ReferenceId (reference_id): string?`, `Amount (amount): AmountWithBreakdown !req`, `Payee?`, `Description?`, `CustomId?`, `InvoiceId?`, `Items?`, `Shipping?`, …
  - `AmountWithBreakdown` (`records-1`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req` (decimal string, e.g. "12.34"), `Breakdown (breakdown): AmountBreakdown?`.
  - `PaymentSource` (`records-2`): set only `Card (card): CardRequest?` for direct card.
  - `CardRequest` (`records-1`): `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?` (YYYY-MM), `SecurityCode (security_code): string?`, `BillingAddress (billing_address): Address?`, `Attributes (attributes): CardAttributes?`, `VaultId (vault_id): string?`, `StoredCredential (stored_credential): CardStoredCredential?`, `ExperienceContext (experience_context): CardExperienceContext?`. Test card number "4111111111111111".
  - `Address` (`records-1`): `AddressLine1?`, `AddressLine2?`, `AdminArea2 (admin_area_2)?` (city), `AdminArea1 (admin_area_1)?` (state), `PostalCode?`, `CountryCode (country_code): string !req`.
- **Returns `Order`** (`records-1`): `Id (id): string?` (the PayPal order id), `Status (status): OrderStatus?`, `PaymentSource (payment_source): PaymentSourceResponse?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `Links (links): IReadOnlyList<LinkDescription>?`.
- **Error**: `SdkException<CreateOrderError>` (`PayPalServerSdk.Errors`) — Case A. Accessors: `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)`.

**`Orders.AuthorizeOrder`** — `POST /v2/checkout/orders/{id}/authorize` — map `operations/Orders.md`.
- **Signature**: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params (`payPalMockResponse`…`body`) nullable-no-default → pass explicitly. Idempotency via `payPalRequestId:`.
- **Request `OrderAuthorizeRequest`** (`records-1`): `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` — pass `null` body if the card was already supplied at CreateOrder; supply `OrderAuthorizeRequestPaymentSource { Card = CardRequest{…} }` if authorizing with the card here.
  - `OrderAuthorizeRequestPaymentSource` (`records-1`): `Card (card): CardRequest?`, `Token (token): Token?`, `Paypal?`, `ApplePay?`, `GooglePay?`, `Venmo?`.
- **Returns `OrderAuthorizeResponse`** (`records-1`): `Id (id): string?`, `Status (status): OrderStatus?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `PaymentSource (payment_source): OrderAuthorizeResponsePaymentSource?`, `Links?`.
- **Reading the authorization id + status** (both `Order` and `OrderAuthorizeResponse` nest identically):
  `PurchaseUnits[].Payments (payments): PaymentCollection?` → `PaymentCollection.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → `AuthorizationWithAdditionalData.Id (id): string?` and `.Status (status): AuthorizationStatus?` (also `.ExpirationTime (expiration_time): string?`). Source: `records-1` (`PurchaseUnit`, `AuthorizationWithAdditionalData`), `records-2` (`PaymentCollection`).
- **Error**: `SdkException<AuthorizeOrderError>` — Case A. `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError(out RawError)`.
- **One call vs follow-up (UNVERIFIED — live-wire)**: whether the authorization already appears in the `CreateOrder` response (when `prefer:"return=representation"` + inline card) or whether a separate `AuthorizeOrder` (or a prior `ConfirmOrder` = `POST /confirm-payment-source`) is required is NOT determinable from the SDK surface. **Directive**: after CreateOrder, read `purchase_units[].payments.authorizations[0]` best-effort; if null/empty, call `AuthorizeOrder(orderId, …, body: null, …)` and read it from that response. Treat both response types uniformly. `ConfirmOrder` (`OrderRequest`→`ConfirmOrderRequest { PaymentSource !req }`, returns `Order`) exists if a confirm step is needed.
- **3DS / challenge detection → STOP (UNVERIFIED — live-wire)**: the SDK surface cannot confirm a plain
  test card skips buyer action. **Detect and STOP defensively** when any of these appear in the response:
  `Order.Status == OrderStatus.PayerActionRequired`; a `Links[]` entry whose `Rel` is a payer-action/approve
  relation (HATEOAS redirect); or `PaymentSourceResponse.Card.AuthenticationResult` present with
  `LiabilityShift (liability_shift): LiabilityShiftIndicator` = `Possible`/`Unknown`. If none present and an
  authorization with `Status = Created` is returned, treat as server-side complete. Whether "4111…" ever
  triggers a challenge in this sandbox merchant config is UNVERIFIED — code the STOP path regardless.

#### Capability 2 — Capture an authorization

**`Payments.CaptureAuthorizedPayment`** — `POST /v2/payments/authorizations/{authorization_id}/capture` — map `operations/Payments.md`.
- **Signature**: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 4 params (`payPalMockResponse`…`body`) nullable-no-default → pass explicitly. Idempotency via `payPalRequestId:`. Pass `prefer:"return=representation"` to get the fee breakdown populated.
- **Request `CaptureRequest`** (`records-1`, all optional): `Amount (amount): Money?` (omit/`null` body for full capture), `InvoiceId?`, `FinalCapture (final_capture): bool? = false`, `NoteToPayer?`, `SoftDescriptor?`.
  - `Money` (`records-1`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`.
- **Returns `CapturedPayment`** (`records-1`): `Id (id): string?` (capture id), `Status (status): CaptureStatus?`, `Amount (amount): Money?`, `FinalCapture?`, `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`, `Links?`.
  - `SellerReceivableBreakdown` (`records-2`): `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?`, `ReceivableAmount?`, `ExchangeRate?`. → captured amount = `GrossAmount`, fee = `PaypalFee`, net proceeds = `NetAmount`.
- **Error**: `SdkException<CaptureAuthorizedPaymentError>` — Case A. `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

#### Capability 3 — Re-authorize a stale auth; read auth status

**`Payments.ReauthorizePayment`** — `POST /v2/payments/authorizations/{authorization_id}/reauthorize` — map `operations/Payments.md`.
- **Signature**: `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId`, `payPalAuthAssertion`, `body` nullable-no-default → pass explicitly. Idempotency via `payPalRequestId:`.
- **Request `ReauthorizeRequest`** (`records-2`): `Amount (amount): Money?` only (supports only `amount`).
- **Returns `PaymentAuthorization`** (`records-2`): `Id?`, `Status (status): AuthorizationStatus?`, `Amount?`, `ExpirationTime (expiration_time): string?`, `Links?`.
- **Validity window (from map notes)**: reauthorize after the initial 3-day honor period, from days 4–29 of the 29-day authorization; after 30 days you must create a fresh authorization (Capability 1).
- **"Can no longer be re-authorized" signalling (UNVERIFIED — exact issue strings)**: on failure read
  `ex.Error.TryGetError(out Error e)` → `e.Details[].Issue (issue): string` + `e.Message` and surface to the
  operator. The exact issue code strings (e.g. authorization-expired / cannot-reauthorize) are PayPal
  API-defined and are NOT enumerated in the SDK (`ErrorDetails.Issue` is a plain `string`, not an enum), so
  do not switch on a hard-coded constant — display `Name`/`Issue`/`Message`/`DebugId` verbatim.
- **Error**: `SdkException<ReauthorizePaymentError>` — Case A. `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

**`Payments.GetAuthorizedPayment`** (read current status) — `GET /v2/payments/authorizations/{authorization_id}` — map `operations/Payments.md`.
- **Signature**: `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 2 nullable-no-default params → pass explicitly.
- **Returns `PaymentAuthorization`** → `.Status (status): AuthorizationStatus?`, `.ExpirationTime`.
- **Error**: `SdkException<GetAuthorizedPaymentError>` — Case A. `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

#### Capability 4 — Void / release an authorization

**`Payments.VoidPayment`** — `POST /v2/payments/authorizations/{authorization_id}/void` — map `operations/Payments.md`.
- **Signature**: `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 nullable-no-default params → pass explicitly. Idempotency via `payPalRequestId:`.
- **Returns `PaymentAuthorization`**. Cannot void an authorization already fully captured (map note).
- **Response population after void (UNVERIFIED — live-wire)**: a void commonly returns 204 No Content, so the
  returned `PaymentAuthorization` may be empty. **Directive**: do not depend on the void response body for the
  post-void status; if you need to confirm the `Voided` state, call `GetAuthorizedPayment` and read
  `Status == AuthorizationStatus.Voided`.
- **Error**: `SdkException<VoidPaymentError>` — Case A. `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

#### Capability 5 — Refund a capture (full or partial)

**`Payments.RefundCapturedPayment`** — `POST /v2/payments/captures/{capture_id}/refund` — map `operations/Payments.md`.
- **Signature**: `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 4 params (`payPalMockResponse`…`body`) nullable-no-default → pass explicitly. **Caller idempotency key** → `payPalRequestId:`.
- **Request `RefundRequest`** (`records-2`): `Amount (amount): Money?`, `CustomId?`, `InvoiceId?`, `NoteToPayer?`, `PaymentInstruction?`.
  - **Full refund**: pass `body: null` (empty request body). **Partial refund**: `new RefundRequest { Amount = new Money { CurrencyCode = <currency>, Value = "<partial>" } }`.
- **Returns `Refund`** (`records-1`): `Id (id): string?` (refund id), `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?`, `Links?`, `CreateTime?`, `UpdateTime?`.
- **Repeating the same `PayPal-Request-Id` (UNVERIFIED — live-wire replay semantics)**: the `payPalRequestId`
  parameter is the idempotency channel; PayPal's contract is that a repeat with the same id returns the
  original refund rather than double-refunding. The SDK surface confirms the header exists but cannot confirm
  the server replay behavior. **Directive**: reuse a stable per-refund id and treat a success on retry as the
  original refund (read back `Id`); do not assume a fresh refund was created.
- **Error**: `SdkException<RefundCapturedPaymentError>` — Case A. `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

#### Capability 6 — Vault a card (no browser), then pay with it

Two supported shapes (both server-side, no approval step):

**(6a) Direct payment-token in one call — `Vault.CreatePaymentToken`** — `POST /v3/vault/payment-tokens` — map `operations/Vault.md`.
- **Signature**: `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` nullable-no-default → pass explicitly.
- **Request `PaymentTokenRequest`** (`records-2`): `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.
  - `PaymentTokenRequestPaymentSource` (`records-2`): `Card (card): PaymentTokenRequestCard?`, `Token (token): VaultTokenRequest?`.
  - Raw card → `PaymentTokenRequestCard` (`records-2`): `Name?`, `Number?`, `Expiry?`, `SecurityCode (security_code)?`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`.
  - `Customer` (`records-1`): `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?` — supply/reuse to group tokens per customer.

**(6b) Two-step setup-token → payment-token** (use when you want to stage the instrument first):
1. **`Vault.CreateSetupToken`** — `POST /v3/vault/setup-tokens`.
   - **Signature**: `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`.
   - **Request `SetupTokenRequest`** (`records-2`): `Customer?`, `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req`.
     - `SetupTokenRequestPaymentSource` (`records-2`): `Card (card): SetupTokenRequestCard?`, `Paypal?`, `Venmo?`, `ApplePay?`, `Token?`, `Bank?`.
     - `SetupTokenRequestCard` (`records-2`): `Name?`, `Number?`, `Expiry?`, `SecurityCode?`, `Brand?`, `BillingAddress?`, `VerificationMethod (verification_method): VaultCardVerificationMethod?`, `ExperienceContext (experience_context): VaultCardExperienceContext?`.
   - **Returns `SetupTokenResponse`** (`records-2`): `Id (id): string?` (the **setup token id**, temporary), `Status (status): PaymentTokenStatus? = PaymentTokenStatus.Created`, `PaymentSource?`, `Links?`.
   - **Error**: `SdkException<CreateSetupTokenError>` — Case A. `TryGetError1(out Error1)` [400,403,422,500] · `TryGetRawError(out RawError)`. (Note: vault ops use `TryGetError1`/`Error1`, not `TryGetError`.)
2. **`Vault.CreatePaymentToken`** with the setup-token reference:
   - `PaymentTokenRequest { PaymentSource = new PaymentTokenRequestPaymentSource { Token = new VaultTokenRequest { Id = <setupTokenId>, Type = VaultTokenRequestType.SetupToken } } }`.
   - `VaultTokenRequest` (`records-2`): `Id (id): string !req`, `Type (type): VaultTokenRequestType !req` (only member `SetupToken (SETUP_TOKEN)`).

**Response of `CreatePaymentToken` — `PaymentTokenResponse`** (`records-2`) — the persistable id + display metadata:
- `Id (id): string?` → **PERSIST THIS** — this is the vault / payment-token id used later as `card.vault_id`.
- `Customer (customer): CustomerResponse?`, `Links?`.
- `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `.Card (card): CardPaymentTokenEntity?`.
  - `CardPaymentTokenEntity` (`records-1`) safe display fields: `Brand (brand): CardBrand?`, `LastDigits (last_digits): string?`, `Expiry (expiry): string?`, `BillingAddress?`, `Type (type): CardType?`. (No PAN/CVV returned.)
- **Error**: `SdkException<CreatePaymentTokenError>` — Case A. `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError(out RawError)`.

**Pay with the saved card later** — use `payment_source.card.vault_id`, NOT `payment_source.token`:
- In `Orders.CreateOrder` / `Orders.AuthorizeOrder`, build `CardRequest { VaultId = <persisted PaymentTokenResponse.Id> }` (leave `Number`/`SecurityCode` null). `CardRequest.VaultId (vault_id): string?` is the field (`records-1`).
- **Why not `payment_source.token`**: the `Token` record (`records-2`: `Id !req`, `Type: TokenType !req`) has `TokenType` with the single member `BillingAgreement (BILLING_AGREEMENT)` — it is for PayPal billing agreements, not vaulted cards. For a vaulted card always use `card.vault_id`.
- **Optional vault-on-create** (vault the card while placing an order, instead of a separate token call):
  `CardRequest.Attributes = new CardAttributes { Vault = new VaultInstructionBase { StoreInVault = StoreInVaultInstruction.OnSuccess }, Customer = … }` (`CardAttributes` in `records-1`; `VaultInstructionBase` in `records-2`; enum `StoreInVaultInstruction.OnSuccess`). The vaulted id then comes back on the order's `PaymentSourceResponse.Card` attributes.
- **`GetPaymentToken`** (`GET /v3/vault/payment-tokens/{id}`) returns `PaymentTokenResponse` if you need to re-read metadata later; **`DeletePaymentToken`** (`DELETE …/{id}`) removes it; **`ListCustomerPaymentTokens(customerId, pageSize=5, page=1, totalRequired=false, …)`** lists per-customer tokens (`CustomerVaultPaymentTokensResponse` with `TotalItems`/`TotalPages`).

#### Capability 7 — Transaction search + full pagination

**`TransactionSearch.SearchTransactions`** — `GET /v1/reporting/transactions` — map `operations/TransactionSearch.md`.
- **Signature**: `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `startDate`/`endDate` **required**, ISO-8601 strings (wire `start_date`/`end_date`). 8 params (`transactionId`…`terminalId`) nullable-no-default → pass explicitly (`null` to skip). **Call with named arguments** — see trap.
  - Paging inputs: `pageSize` (wire `page_size`, default 100), `page` (wire `page`, default 1).
- **Returns `SearchResponse`** (`records-2`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `StartDate?`, `EndDate?`, `Links?`.
  - **Pagination directive**: the map row marks this op **"Pagination: none (only `page`, no `perPage`)"** — there is **no SDK auto-pager**. To cover the entire range, read `TotalPages` from the first response, then loop `page = 1 … TotalPages` calling `SearchTransactions` each time (or follow `Links[]` `rel="next"`), accumulating `TransactionDetails`.
  - **Per-record fields** — `TransactionDetails` (`records-2`): `TransactionInfo (transaction_info): TransactionInformation?`, `PayerInfo?`, `ShippingInfo?`, `CartInfo?`, `StoreInfo?`. `TransactionInformation` (`records-2`): `TransactionId (transaction_id): string?`, `TransactionStatus (transaction_status): string?` (**plain string, not an enum**), `TransactionAmount (transaction_amount): Money?`, `TransactionInitiationDate (transaction_initiation_date): string?`, `TransactionUpdatedDate (transaction_updated_date): string?`, `FeeAmount?`.
- **Error (Case B — the ONLY Case B op in this SDK)**: `SdkException<RawError>`. Read `ex.Error.StatusCode: HttpStatusCode`, `ex.Error.ReadAsString(): string`, `ex.Error.ReadAsJson<DefaultError>()` (`DefaultError` in `PayPalServerSdk.Models`: `Name`, `Message`, `DebugId`, `Details`). There is **no `TryGetError`** here — do not write a Case-A catch for this op.

#### Capability 8 — Error handling (applies to every call)

Source: `sdk-map.md` *Error-handling model*; confirmed against `Core/Exceptions/SdkException.cs`,
`Core/ErrorResponse/ApiError.cs`, `Core/ErrorResponse/RawError.cs`.
- **Exception type**: `SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`), sealed, exposes **only**
  `.Error` of type `TError` (plus inherited `Exception.Message`). **It carries no direct status-code
  property** — read the HTTP status from the error body accessors below.
- **Case A (39 of 40 ops — everything above except `SearchTransactions`)**: `TError` is a generated
  `{Op}Error : ApiError` (`PayPalServerSdk.Errors`). Read the PayPal error object via the op's typed accessor:
  - Orders/Payments ops → `ex.Error.TryGetError(out Error e)` → `Error` (`PayPalServerSdk.Models`): `Name (name): string`, `Message (message): string`, `DebugId (debug_id): string`, `Details (details): IReadOnlyList<ErrorDetails>?`. `ErrorDetails`: `Field?`, `Value?`, `Issue (issue): string`, `Description?`.
  - Vault ops → `ex.Error.TryGetError1(out Error1 e)` → `Error1`: same shape, `Details` is `IReadOnlyList<ErrorDetails1>?`.
  - `SearchBalances` → `ex.Error.TryGetDefaultError(out DefaultError e)`.
  - **HTTP status code**: `ex.Error.TryGetRawError(out RawError raw)` → `raw.StatusCode: HttpStatusCode` (inherited from `ApiError`, present on every typed error as the fallback).
- **Case B (`SearchTransactions` only)**: `TError` is `RawError` directly → `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<DefaultError>()`.
- No operation has a no-throw `…Result` variant — always wrap in try/catch.

### 2.3 Enum value tables (literal C# member → wire value) — `PayPalServerSdk.Models.Enums`

Source: `map/models/enums.md`. Build via the static member (e.g. `CheckoutPaymentIntent.Authorize`) or `T.FromValue("WIRE")`.

| Enum | Members (C# → wire) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` (billing agreements only — NOT vaulted cards) |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `CardBrand` (subset) | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, … (30 members) |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` |

---

## 3. Trap notes (load the named skill at that step — do not code from the one-liner)

- ⚠ **Step 0 (client & DI)** — the `HttpClient`/handler pipeline must be long-lived and reused, and the
  registration lifetime of the SDK client wrapper is not what the ctor implies. **MUST load
  `dotnet-client-initialization`** before writing `AddPayPalServerSdkClient` / `new PayPalServerSdkClient`.
- ⚠ **Step 0 (auth)** — when to set credentials relative to client construction, and how token
  acquisition/refresh is driven, are not shown by the `Oauth2` property. **MUST load `dotnet-authentication`**
  before wiring `OAuth2ClientCredentials`.
- ⚠ **Step 0 (base URL / resilience)** — the SDK retry/timeout options do **not** bound a whole call and are
  **not** the timeout on the `HttpClient` you register, and which calls retry (transport failures on `POST`
  included) is not visible in the option names — this matters because captures/refunds/authorizations are
  non-idempotent writes gated by `PayPal-Request-Id`. **MUST load `dotnet-configuration-resilience`** before
  tuning `options.Retry`, timeouts, base URL, or the search pagination loop.
- ⚠ **All call steps (building bodies)** — enums are `StringEnum<T>` not C# enums, request bodies nest
  union-shaped `payment_source` records, and unmodeled JSON is dropped on (de)serialize. **MUST load
  `dotnet-models`** before constructing any request payload or mapping a response onto domain types.
- ⚠ **Capability 7 (search)** — `SearchTransactions` has 10 nullable-no-default params before the defaulted
  paging params; a positional call mis-binds. Call it with **named arguments** (`startDate:`, `endDate:`,
  `page:`, `pageSize:`, `ct:`). **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ **Capability 8 (error boundary)** — whether `TryGetRawError` is populated alongside the typed shape (i.e.
  whether you can read both the PayPal `Error` body and the numeric `StatusCode` from one exception), and the
  Case A vs Case B split, decide whether your catch ladder is correct. **MUST load `dotnet-error-handling`**
  before writing any try/catch.

---

## 4. REQUIRED READING — load BEFORE implementation starts

This sheet deliberately does not carry these skills' contents (defaults, worked examples, wiring you must do
yourself). Load each before writing the code for its step:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, `HttpClient` lifetime, DI registration |
| `dotnet-authentication` | Step 0 — supplying `OAuth2ClientCredentials`, token acquisition/refresh |
| `dotnet-configuration-resilience` | Step 0/7 — retries, timeouts, base-URL override, pagination loop |
| `dotnet-calling-endpoints` | Caps 1–7 — named-argument calls, required vs optional params, response envelopes |
| `dotnet-models` | Caps 1–7 — building request records, enums, union `payment_source`, wire names |
| `dotnet-error-handling` | Cap 8 — which exceptions reach the catch, reading status/body safely, Case A/B |
| `dotnet-testing` | Integration tests — the `HttpClient` seam, error/edge paths |

**Two mandatory `System.Text.Json.JsonException` hazards for the error boundary** — this exception reaches
the boundary from two directions needing opposite handling:
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a
  5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something
  that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- A1. "Complete contract sheet, do not write app code" ⇒ **plan mode**. The brief did not dictate an output
  path, so this file was written to the **default** `<project repo root>/paypal-plan.md`
  (`C:\claude-runs\t3ali-task3-plugin-opus48high-012\repo\paypal-plan.md`).
- A2. Amounts (`Money.Value` / `AmountWithBreakdown.Value`) are decimal strings; `CurrencyCode` comes from
  `PayPal:Currency`. The SDK has no global currency setting — it is set per amount object.
- A3. `PayPal:BaseUrl`, when set, is treated as an origin/host; the SDK appends fixed endpoint paths. If the
  config value were ever a full endpoint path, base-URL resolution would double the path — bind it as origin.

**Blockers / limitations to report**
- B1. **No `Live`/`Production` environment member exists in this SDK build.** `ServerEnvironment` exposes only
  `Sandbox` (source `Servers/ServerEnvironment.cs`; `DefaultOptions.Resolve` throws for any non-Sandbox value).
  For sandbox this is fine (`ServerEnvironment.Sandbox`). To ever target live, you cannot select a live
  environment enum — you must override `options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"`
  while leaving `Environment = Sandbox`. Flag this to whoever owns the go-live decision.
- B2. **Single-call vs follow-up authorize is UNVERIFIED (live-wire).** Handled with the defensive directive
  in Capability 1 (read the inline authorization; if absent, call `AuthorizeOrder`).
- B3. **3DS/challenge behavior for the test card is UNVERIFIED (live-wire).** The STOP path (Capability 1) is
  coded from surface signals (`OrderStatus.PayerActionRequired`, payer-action `Links`, `LiabilityShift`)
  regardless of whether "4111…" triggers it.
- B4. **Reauthorize "cannot reauthorize" issue strings and refund idempotency replay are UNVERIFIED
  (live-wire).** Surfaced/handled via the defensive directives in Capabilities 3 and 5.
- B5. **No capability is genuinely absent.** All 8 requested capabilities map to real operations in this SDK.
