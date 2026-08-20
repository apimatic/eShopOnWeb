# PayPal .NET SDK — Payments Integration Contract Sheet (eShopOnWeb)

SDK: `AsadAli.Checkout.Sdk` (install version-less). Root namespace `PayPalServerSdk`, client
`PayPalServerSdkClient`. Map provenance: source commit `9653d18`, tag `v1.0.1`. Every fact below
was read from the bundled SDK map this session; the environment/base-URL/auth-shape facts were
confirmed against the pinned SDK source where the map was silent (noted inline).

---

## 1. Scope & sequence

| # | Capability | Operation(s) — controller.method | Map page |
|---|---|---|---|
| 0 | Client + DI + auth + base-URL config | `AddPayPalServerSdkClient` / `new PayPalServerSdkClient` | sdk-map.md |
| 1 | Authorize an order total (hold) — raw card (1a) or vaulted token (1b) | `client.Orders.CreateOrder` (intent AUTHORIZE, payment_source) → `client.Orders.AuthorizeOrder` | operations/Orders.md |
| 2 | Capture the authorization at fulfilment | `client.Payments.CaptureAuthorizedPayment` | operations/Payments.md |
| 3 | Re-authorize a stale authorization | `client.Payments.ReauthorizePayment` (+ `GetAuthorizedPayment` to pre-check) | operations/Payments.md |
| 4 | Void an authorization | `client.Payments.VoidPayment` | operations/Payments.md |
| 5 | Refund a capture (full/partial) | `client.Payments.RefundCapturedPayment` | operations/Payments.md |
| 6 | Transaction search / reconciliation (paginate whole range) | `client.TransactionSearch.SearchTransactions` | operations/TransactionSearch.md |
| 7 | Vault a card + list/get/delete token | `client.Vault.CreatePaymentToken` / `CreateSetupToken` / `GetPaymentToken` / `ListCustomerPaymentTokens` / `DeletePaymentToken` | operations/Vault.md |

Implementation order: 0 (client/auth) → 7-create (so a vault id exists for 1b) → 1 → 3 → 4 →
2 → 5 → 6. Each of 1–5 and 7-create passes a **PayPal-Request-Id** idempotency key (see §6).

---

## 2. Client construction, auth, environment & base-URL override

Bind `PayPal:` config to a POCO: `ClientId`, `ClientSecret`, `Environment` (`sandbox|production`),
`Currency`, `BaseUrl` (optional).

**Auth credentials shape** (`OAuth2ClientCredentials`, namespace
`PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`; source `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`):

| Member | Type | Notes |
|---|---|---|
| `ClientId` | `string` | `required` |
| `ClientSecret` | `string` | `required` |
| `Scope` | `string?` | optional — leave null |

Set it on the options object: `options.Oauth2 = new OAuth2ClientCredentials { ClientId = cfg.ClientId, ClientSecret = cfg.ClientSecret };`
The SDK obtains the OAuth token itself via the built-in client-credentials strategy (HTTP Basic on
`POST /v1/oauth2/token`) — you do **not** call a token endpoint yourself.

**`PayPalServerSdkClientOptions`** (namespace `PayPalServerSdk`; source `PayPalServerSdkClientOptions.cs`):
`Environment: ServerEnvironment`, `Server: ServerOptions`, `Retry: RetryOptions`,
`Logging: LoggingOptions`, `Oauth2: OAuth2ClientCredentials?`,
`Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.

**Environment selection — HARD LIMITATION (confirmed in source, see Blockers B1).**
`ServerEnvironment` (namespace `PayPalServerSdk.Servers`; source `Servers/ServerEnvironment.cs`)
exposes **only `ServerEnvironment.Sandbox`** — there is **no `Production` member**. `DefaultOptions.Resolve`
(`Servers/DefaultOptions.cs`) resolves *every* URL from `Sandbox.BaseUrl` (default
`https://api-m.sandbox.paypal.com`). So "select production" cannot be done through
`options.Environment`; it is done only by overriding the base URL (below).

**Base-URL override (verbatim, applies to EVERY call including OAuth/token).**
The base URL string lives at `options.Server.Default.Sandbox.BaseUrl` — chain of:
`ServerOptions.Default` (`DefaultOptions`) → `.Sandbox` (`DefaultOptions.SandboxOptions`) → `.BaseUrl` (`string`).
Confirmed: the OAuth token URL is built from the same server object
(`server.Default("/v1/oauth2/token")` in `AuthSchemes.cs`), so setting `BaseUrl` here changes the
token request too. Wire it as:

- `Environment == sandbox`, no `BaseUrl` → leave defaults (resolves to `https://api-m.sandbox.paypal.com`).
- `Environment == production`, no `BaseUrl` → **must** set `options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"` (no Production enum exists).
- `BaseUrl` provided (any value) → set `options.Server.Default.Sandbox.BaseUrl = cfg.BaseUrl` **verbatim**, ignore Environment for URL purposes.

  > Trap: the property you assign for production/override is still literally named `Sandbox`
  > (`options.Server.Default.Sandbox.BaseUrl`) — that is the only base-URL slot the resolver reads.
  > `options.Environment` is decorative here; do not rely on it to reach production.

**HttpClient ownership / DI.** Register via `services.AddPayPalServerSdkClient(o => { ... })`
(source `ServiceCollectionExtensions.cs`) OR construct `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`.
The `HttpClient`/handler pipeline must be long-lived and factory-managed (see trap T0 / `dotnet-client-initialization`).

Client-config `using` namespaces (each type's own row):
`PayPalServerSdk` (client, options, `ServerOptions`) · `PayPalServerSdk.Servers` (`ServerEnvironment`,
`DefaultOptions`) · `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`
(`OAuth2ClientCredentials`) · `PayPalServerSdk.Core.Configuration` (`RetryOptions`).

---

## 3. CONTRACT SHEET

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

**Namespace key for the models below**: request/response records + `Error`/`Error1`/`DefaultError`
payload types ⇒ `PayPalServerSdk.Models`; enums ⇒ `PayPalServerSdk.Models.Enums`; controllers ⇒
`PayPalServerSdk.Api` (reached via `client.X`, no `using` needed for the property); `SdkException<T>`
⇒ `PayPalServerSdk.Core.Exceptions`; `RawError` ⇒ `PayPalServerSdk.Core.ErrorResponse`; per-operation
`{Op}Error` classes ⇒ `PayPalServerSdk.Errors`.

All operations are **async** (return `Task<…>`), **throw-based**, and have **no `…Result`
no-throw variant**. `prefer` defaults to `"return=minimal"` — pass `"return=representation"`
when you need the full body back (see trap T4).

### 3.1 Operation rows

**CreateOrder** — `client.Orders.CreateOrder` — [operations/Orders.md]
- Signature: `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - First 5 params nullable-no-default → **must pass explicitly** (pass `null` to skip). `payPalRequestId` = idempotency key. `body` is **non-null required**, positional.
- Request `OrderRequest` (`Models`): `Intent (intent): CheckoutPaymentIntent !req` (= `CheckoutPaymentIntent.Authorize`), `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `Payer (payer): Payer?`, `PaymentSource (payment_source): PaymentSource?`, `ApplicationContext (application_context): OrderApplicationContext?`
- Returns: `Order` (`Models`): `Id (id): string?`, `Status (status): OrderStatus?`, `PaymentSource (payment_source): PaymentSourceResponse?`, `Intent`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `Links (links): IReadOnlyList<LinkDescription>?`
- Error: **Case A** `SdkException<CreateOrderError>` (`Errors`) — `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` [fallback]. `Error` payload ⇒ `Models`.

**AuthorizeOrder** — `client.Orders.AuthorizeOrder` — [operations/Orders.md]
- Signature: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `id` = order id from CreateOrder. First 4 nullable header params must pass explicitly; `payPalRequestId` = idempotency key. `body` nullable — pass `null` when the payment source was already set on CreateOrder, OR pass an `OrderAuthorizeRequest` to supply it here.
  - **Use `prefer: "return=representation"`** so the response carries the authorization id (trap T4).
- Request `OrderAuthorizeRequest` (`Models`): `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`
- Returns: `OrderAuthorizeResponse` (`Models`): `Id (id): string?`, `Status (status): OrderStatus?`, `PaymentSource (payment_source): OrderAuthorizeResponsePaymentSource?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `Links (links): IReadOnlyList<LinkDescription>?`
  - **Read authorization id + status**: `resp.PurchaseUnits[0].Payments (PaymentCollection).Authorizations[0]` → `AuthorizationWithAdditionalData` → `.Id (string?)`, `.Status (AuthorizationStatus?)`, `.ExpirationTime (string?)`. (`PurchaseUnit.Payments (payments): PaymentCollection?`; `PaymentCollection.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`.)
- Error: **Case A** `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError(out RawError)`.

**CaptureAuthorizedPayment** — `client.Payments.CaptureAuthorizedPayment` — [operations/Payments.md]
- Signature: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId` = idempotency key. `body` nullable — `null` = full capture; or `CaptureRequest`. Use `prefer: "return=representation"` to read breakdown.
- Request `CaptureRequest` (`Models`): `Amount (amount): Money?`, `InvoiceId (invoice_id): string?`, `FinalCapture (final_capture): bool? = false`, `NoteToPayer (note_to_payer): string?`, `SoftDescriptor (soft_descriptor): string?`, `PaymentInstruction (payment_instruction): CapturePaymentInstruction?`. For "take the money in full" set `FinalCapture = true`.
- Returns: `CapturedPayment` (`Models`): `Id (id): string?`, `Status (status): CaptureStatus?`, `Amount (amount): Money?`, `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`, `FinalCapture (final_capture): bool?`
  - **Readback accessors**: capture id = `.Id`; status = `.Status`; captured amount = `.Amount` (`Money`); PayPal fee = `.SellerReceivableBreakdown.PaypalFee` (`Money?`); net proceeds = `.SellerReceivableBreakdown.NetAmount` (`Money?`); gross = `.SellerReceivableBreakdown.GrossAmount` (`Money !req`). `SellerReceivableBreakdown` also: `ReceivableAmount (receivable_amount): Money?`, `ExchangeRate (exchange_rate): ExchangeRate?`, `PlatformFees (platform_fees): IReadOnlyList<PlatformFee>?`. Each `Money` = `CurrencyCode (currency_code): string !req` + `Value (value): string !req` (decimal string — parse yourself).
- Error: **Case A** `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

**ReauthorizePayment** — `client.Payments.ReauthorizePayment` — [operations/Payments.md]
- Signature: `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId` = idempotency key. Supports only the `amount` param.
- Request `ReauthorizeRequest` (`Models`): `Amount (amount): Money?`
- Returns: `PaymentAuthorization` (`Models`): `Id (id): string?`, `Status (status): AuthorizationStatus?`, `StatusDetails (status_details): AuthorizationStatusDetails?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`
- Error: **Case A** `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.
  - **"Can no longer reauthorize" signal**: the SDK reports this as a failure — catch `SdkException<ReauthorizePaymentError>`, `TryGetError(out var e)` and surface `e.Name`/`e.Message` to the operator (422/404 are the expired/invalid-state statuses). To pre-check before calling, `GetAuthorizedPayment` and inspect `PaymentAuthorization.Status`: reauth is only viable while `AuthorizationStatus.Created`; `Expired`(*see note*)/`Voided`/`Captured`/`PartiallyCaptured` cannot be reauthorized. NOTE: the `AuthorizationStatus` enum in this SDK is `{Created, Captured, Denied, PartiallyCaptured, Voided, Pending}` — **there is no `Expired` member**; an expired hold surfaces via the failed reauthorize call / `StatusDetails.Reason`, not a distinct status enum value. `UNVERIFIED` (live-only) which exact `Error.Name` string PayPal returns for an un-reauthorizable hold — code defensively: treat any `TryGetError` success on this call as "report to operator with `e.Message`", fall back to raw status via `TryGetRawError`.

**VoidPayment** — `client.Payments.VoidPayment` — [operations/Payments.md]
- Signature: `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - **Param-order trap**: here `payPalRequestId` is the **4th** param (after `payPalAuthAssertion`), unlike the other writes where it is 3rd. All three header params must pass explicitly.
- Returns: `PaymentAuthorization` (`Models`) — same shape as ReauthorizePayment's return.
  - **Confirm voided**: `resp.Status == AuthorizationStatus.Voided`. With the default `prefer="return=minimal"` the body may be sparse/empty; pass `prefer: "return=representation"` to read `Status`, or re-`GetAuthorizedPayment` to confirm `Voided` (trap T4).
- Error: **Case A** `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

**RefundCapturedPayment** — `client.Payments.RefundCapturedPayment` — [operations/Payments.md]
- Signature: `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId` = idempotency key. `body` nullable — `null`/empty = **full** refund; set `Amount` for **partial**.
- Request `RefundRequest` (`Models`): `Amount (amount): Money?` (omit for full; for partial set — your app must ensure it does not exceed the captured amount), `CustomId (custom_id): string?`, `InvoiceId (invoice_id): string?`, `NoteToPayer (note_to_payer): string?`, `PaymentInstruction (payment_instruction): RefundPaymentInstruction?`
- Returns: `Refund` (`Models`): `Id (id): string?`, `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount` — all `Money?`), `StatusDetails (status_details): RefundStatusDetails?`
  - Readback: refund id = `.Id`; status = `.Status` (`RefundStatus`).
- Error: **Case A** `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

**SearchTransactions** — `client.TransactionSearch.SearchTransactions` — [operations/TransactionSearch.md]
- Signature: `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `startDate`/`endDate` non-null required, **ISO-8601** (wire `start_date`/`end_date`; PayPal wants e.g. `2026-01-01T00:00:00-0000`, max 31-day window per request). 8 optional filters (`transactionId`…`terminalId`) nullable-no-default → **must pass explicitly** (`null`). Keep `fields="transaction_info"` so transaction rows populate. **Call with named arguments** (many optionals, mis-bind positionally — trap T3).
- Returns: `SearchResponse` (`Models`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Links (links): IReadOnlyList<LinkDescription>?`
  - **Pagination — iterate the WHOLE range**: read `resp.TotalPages`; loop `page = 1 .. TotalPages`, re-calling with the same dates and incremented `page`. There is **no cursor/`perPage`** — only `page`/`pageSize`. Stop when `page >= TotalPages`. (Also `resp.Page` echoes current page.)
  - **Transaction fields to line up against orders**: `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?` → `TransactionId (transaction_id): string?`, `TransactionAmount (transaction_amount): Money?`, `TransactionStatus (transaction_status): string?`, `FeeAmount (fee_amount): Money?`, `TransactionInitiationDate (transaction_initiation_date): string?`, `TransactionUpdatedDate (transaction_updated_date): string?`, `InvoiceId (invoice_id): string?`, `CustomField (custom_field): string?`. (`TransactionStatus` is a **plain string**, not an enum.)
- Error: **Case B** `SdkException<RawError>` (`RawError` ⇒ `Core.ErrorResponse`) — **no typed accessors**; read `ex.Error.StatusCode` and `ex.Error.ReadAsString()` / `ReadAsJson<T>()`. (Sibling `SearchBalances` is Case A with `TryGetDefaultError(out DefaultError)`; not in scope.)

**CreatePaymentToken** — `client.Vault.CreatePaymentToken` — [operations/Vault.md]
- Signature: `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId` nullable-no-default → **must pass explicitly**; also the idempotency key. `body` non-null required.
- Request `PaymentTokenRequest` (`Models`): `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`, `Customer (customer): Customer?` (`Customer`: `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?`)
  - `PaymentTokenRequestPaymentSource` (`Models`): `Card (card): PaymentTokenRequestCard?`, `Token (token): VaultTokenRequest?`
  - Direct card vaulting → `PaymentTokenRequestCard` (`Models`): `Number (number): string?`, `Expiry (expiry): string?` (YYYY-MM), `SecurityCode (security_code): string?`, `Name (name): string?`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`
  - Setup-token → payment-token flow → `Token (token): VaultTokenRequest` (`Models`): `Id (id): string !req` (= setup token id), `Type (type): VaultTokenRequestType !req` (= `VaultTokenRequestType.SetupToken`)
- Returns: `PaymentTokenResponse` (`Models`): `Id (id): string?` (**the vaulted payment-token id — reused as the payment source in item 1b**), `Customer (customer): CustomerResponse?`, `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?`, `Links (links): IReadOnlyList<LinkDescription>?`
  - **Safe descriptor back** (never full PAN): `resp.PaymentSource.Card (CardPaymentTokenEntity)` → `LastDigits (last_digits): string?`, `Brand (brand): CardBrand?`, `Expiry (expiry): string?`, `Name (name): string?`, `BillingAddress (billing_address): CardResponseAddress?`.
- Error: **Case A** `SdkException<CreatePaymentTokenError>` — `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError(out RawError)`. (**Note the accessor is `TryGetError1` and the payload type is `Error1`**, not `Error` — all Vault ops use `Error1`.)

**CreateSetupToken** — `client.Vault.CreateSetupToken` — [operations/Vault.md]
- Signature: `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Request `SetupTokenRequest` (`Models`): `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req`, `Customer (customer): Customer?`
  - `SetupTokenRequestPaymentSource` (`Models`): `Card (card): SetupTokenRequestCard?`, `Token (token): VaultTokenRequest?`, plus paypal/venmo/applePay/bank. `SetupTokenRequestCard`: `Number`, `Expiry`, `SecurityCode`, `Name`, `Brand (CardBrand?)`, `BillingAddress (Address?)`, `VerificationMethod (VaultCardVerificationMethod?)`, `ExperienceContext (VaultCardExperienceContext?)`
- Returns: `SetupTokenResponse` (`Models`): `Id (id): string?` (feed to `CreatePaymentToken` via `VaultTokenRequest`), `Status (status): PaymentTokenStatus? = PaymentTokenStatus.Created`, `PaymentSource (payment_source): SetupTokenResponsePaymentSource?`, `Links`
- Error: **Case A** `SdkException<CreateSetupTokenError>` — `TryGetError1(out Error1)` [400,403,422,500] · `TryGetRawError(out RawError)`.

**GetPaymentToken** — `client.Vault.GetPaymentToken` — [operations/Vault.md]
- Signature: `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Returns: `PaymentTokenResponse` (as above — read `PaymentSource.Card` safe descriptor).
- Error: **Case A** `SdkException<GetPaymentTokenError>` — `TryGetError1(out Error1)` [403,404,422,500] · `TryGetRawError(out RawError)`.

**ListCustomerPaymentTokens** — `client.Vault.ListCustomerPaymentTokens` — [operations/Vault.md]
- Signature: `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - Wire: `customer_id`←`customerId`, `page_size`←`pageSize`, `page`←`page`, `total_required`←`totalRequired`. Set `totalRequired: true` to get `TotalItems`/`TotalPages`. Only `page` (no cursor) — paginate `1..TotalPages` like SearchTransactions.
- Returns: `CustomerVaultPaymentTokensResponse` (`Models`): `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Customer (customer): VaultResponseCustomer?`, `Links`
- Error: **Case A** `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError(out RawError)`.

**DeletePaymentToken** — `client.Vault.DeletePaymentToken` — [operations/Vault.md]
- Signature: `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Returns: `void` (`Task`). Success = no throw (204). No idempotency-key param.
- Error: **Case A** `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError(out RawError)`.

### 3.2 Payment-source construction for AUTHORIZE (item 1)

Set the payment source **on CreateOrder** via `OrderRequest.PaymentSource` = `PaymentSource` (`Models`),
which has `Card (card): CardRequest?` and `Token (token): Token?` among many wallets. (Equivalent
slots exist on `OrderAuthorizeRequestPaymentSource` if you prefer to supply it at AuthorizeOrder time —
same `Card`/`Token` fields.)

- **(1a) raw one-off card** → `PaymentSource.Card` = `CardRequest` (`Models`): `Number (number): string?` (sandbox `4111111111111111`), `Expiry (expiry): string?` (`YYYY-MM`), `SecurityCode (security_code): string?` (CVC), `Name (name): string?`, `BillingAddress (billing_address): Address?`. Do **not** set `VaultId`. App never stores these fields.
- **(1b) previously vaulted card** → `PaymentSource.Card` = `CardRequest` with **only** `VaultId (vault_id): string?` = the `PaymentTokenResponse.Id` from item 7. No PAN/CVC.
  - Do **not** use `PaymentSource.Token` for a vaulted *card*: `Token.Type` is `TokenType`, whose sole member is `BillingAgreement (BILLING_AGREEMENT)` — that path is for PayPal-wallet billing agreements, not cards. Vaulted **cards** are referenced by `CardRequest.VaultId`.

`Address` (`Models`): `AddressLine1`, `AddressLine2`, `AdminArea2` (city), `AdminArea1` (state),
`PostalCode`, `CountryCode (country_code): string !req`.

`PurchaseUnitRequest` (`Models`): `Amount (amount): AmountWithBreakdown !req`, `ReferenceId`,
`CustomId`, `InvoiceId`, `Description`, `Items`, `Shipping`, `Payee`. `AmountWithBreakdown` (`Models`):
`CurrencyCode (currency_code): string !req` (from `PayPal:Currency`), `Value (value): string !req`
(order total to the cent, as a decimal string), `Breakdown (breakdown): AmountBreakdown?`.

### 3.3 3DS / challenge detection (item 1 — STOP-and-report)

After CreateOrder/AuthorizeOrder with a card, the **primary stop signal is the order status**:
`Order.Status` / `OrderAuthorizeResponse.Status == OrderStatus.PayerActionRequired`
(wire `PAYER_ACTION_REQUIRED`). This means the buyer must complete browser (3DS) approval — **do
not proceed to capture; report and halt.** Secondary corroboration (per-card auth outcome) is in the
response payment source: `OrderAuthorizeResponsePaymentSource.Card (CardResponse).AuthenticationResult
(AuthenticationResponse)` → `LiabilityShift (LiabilityShiftIndicator?)` and `ThreeDSecure
(ThreeDSecureAuthenticationResponse?)` → `AuthenticationStatus (ParesStatus?)`, `EnrollmentStatus
(EnrollmentStatus?)`.

- The browser-approval URL is a HATEOAS entry in `.Links` (`LinkDescription`: `Href`, `Rel`, `Method`).
  `UNVERIFIED` (live wire only): the exact `Rel` string PayPal uses is not in the SDK map (`Rel` is a
  plain `string`). **Defensive directive**: key the STOP decision on `OrderStatus.PayerActionRequired`
  (grounded in the enum); when present, extract the approval link best-effort by scanning `.Links` for a
  `Rel` containing `payer-action`/`approve`, and if none is found still stop and report the status —
  never silently capture.

### 3.4 Enum value tables (C# identifier ← wire) — [map/models/enums.md, namespace `PayPalServerSdk.Models.Enums`]

| Enum | Members (`CSharp (WIRE)`) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `EnrollmentStatus` | `Y (Y)`, `N (N)`, `U (U)`, `B (B)` |
| `ParesStatus` | `Y (Y)`, `N (N)`, `U (U)`, `A (A)`, `C (C)`, `R (R)`, `D (D)`, `I (I)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, `Jcb (JCB)`, `Diners (DINERS)`, `Maestro (MAESTRO)`, `Elo (ELO)`, `Rupay (RUPAY)`, `ChinaUnionPay (CHINA_UNION_PAY)`, `Unknown (UNKNOWN)`, … (30 members total; see enums.md if another brand needed) |
| `ServerEnvironment` | `Sandbox` only — **no Production member** (see §2 / Blocker B1) |

Enums are `StringEnum<T>`, **not** C# enums: build with the static member (`CheckoutPaymentIntent.Authorize`)
or `CheckoutPaymentIntent.FromValue("AUTHORIZE")`. (Trap T-models / `dotnet-models`.)

---

## 4. Idempotency (double-click must not authorize/capture twice)

**Mechanism**: the `PayPal-Request-Id` header, surfaced as the **`payPalRequestId`** parameter on
every write operation: `CreateOrder`, `AuthorizeOrder`, `CaptureAuthorizedPayment`,
`ReauthorizePayment`, `VoidPayment`, `RefundCapturedPayment`, `CreatePaymentToken`, `CreateSetupToken`.
Pass a **stable, caller-derived key** (e.g. deterministic from the eShop order id + operation kind) so a
retry/double-click replays the same key and PayPal returns the original result instead of creating a
second authorization/capture/refund. Generating a fresh GUID per attempt defeats this. `GetPaymentToken`,
`ListCustomerPaymentTokens`, `DeletePaymentToken`, and `SearchTransactions` have no such param (reads/delete).

> Reliability caveat (`UNVERIFIED`, live-only): exactly how long PayPal honours a given
> `PayPal-Request-Id` and its dedupe semantics are server-side. Code so a replayed key is treated as
> success (re-read the resulting resource by id) rather than assuming the second call is a no-op.

---

## 5. Trap notes (load the named skill at that step — do not resolve inline)

- ⚠ **T0 — Step 0 (client & DI)**: whether the `HttpClient`/handler must be long-lived and factory-shared vs rebuilt per request, and which part of the SDK client may be transient, is not visible in the constructor signature. **MUST load `dotnet-client-initialization`** before wiring the client/DI.
- ⚠ **T-auth — Step 0 (auth)**: when/where credentials must be set relative to client construction, and how to source them from configuration rather than hardcode, is not shown by the property. **MUST load `dotnet-authentication`** before setting `Oauth2`.
- ⚠ **T-config — Step 0 (base URL / retries / timeouts)**: what `RetryOptions.Timeout` actually bounds, which calls retry (and whether a non-idempotent `POST` can execute more than once on transport failure), and what you must still configure yourself are not inferable from the option names. **MUST load `dotnet-configuration-resilience`** before tuning retries/base-URL/timeouts — this interacts directly with the idempotency design in §4.
- ⚠ **T3 — Steps 1–7 (calls)**: several ops have many nullable-no-default optional params that mis-bind in a positional call (esp. `SearchTransactions`, `AuthorizeOrder`, `VoidPayment`'s shifted `payPalRequestId`). **MUST load `dotnet-calling-endpoints`**; call with named arguments.
- ⚠ **T4 — Steps 1,2,4 (response envelope)**: `prefer` defaults to `"return=minimal"`, so authorization/capture/void responses can come back sparse and the id/status/breakdown you need may be absent; whether to send `"return=representation"` vs re-GET is a usage decision. **MUST load `dotnet-calling-endpoints`** for the envelope/prefer behaviour.
- ⚠ **T-models — Steps 1,7 (models/enums/unions)**: enums are `StringEnum<T>` not C# enums; `required` members must be set in the object initializer; unmodeled JSON is dropped on deserialize. **MUST load `dotnet-models`** before building request payloads.
- ⚠ **T-err — every catch**: Case A vs Case B differ (Orders/Payments → `TryGetError(out Error)`; Vault → `TryGetError1(out Error1)`; `SearchTransactions` → Case B `RawError`, no typed accessors), `TryGetRawError`/`TryGetNoContent` are not catch-alls, and there is no `…Result` no-throw variant. **MUST load `dotnet-error-handling`** before writing the boundary (see §6).

---

## 6. REQUIRED READING (load ALL before implementation starts — this sheet deliberately omits their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 0 — setting `Oauth2` client-credentials, sourcing secrets from config |
| `dotnet-configuration-resilience` | Step 0 — retries/backoff, what `Timeout` bounds, base-URL override, pagination; interacts with idempotency (§4) |
| `dotnet-calling-endpoints` | Steps 1–7 — named-argument calls, `prefer`/response-envelope behaviour, async/cancellation |
| `dotnet-models` | Steps 1,7 — building request models, `required`/nullable, `StringEnum<T>`, wire-vs-C# names |
| `dotnet-error-handling` | The error boundary around every SDK call — Case A/B, `TryGet…` accessors, status/body reading |
| `dotnet-testing` | Tests for the integration layer — the `HttpClient` seam, error/edge paths |

Mandatory `System.Text.Json.JsonException` boundary rows (this SDK is throw-based over
`System.Text.Json`):

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
  `SdkException`-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 7. Assumptions & Blockers

**Assumptions**
- A1: "Authorize an order total" = `CreateOrder(intent=AUTHORIZE, payment_source)` followed by
  `AuthorizeOrder(orderId)`; the authorization id/status are read from the `AuthorizeOrder` response
  (`PurchaseUnits[].Payments.Authorizations[]`). If the intended design is a single CreateOrder-only
  call, revise.
- A2: "Capture the authorization" maps to `client.Payments.CaptureAuthorizedPayment(authorizationId)`
  (capture a payment authorization), **not** `client.Orders.CaptureOrder` (which captures an order and
  is the CAPTURE-intent path). Confirm if you meant order-level capture.
- A3: A vaulted **card** (item 1b) is referenced via `CardRequest.VaultId`, not `PaymentSource.Token`
  (whose only `TokenType` is `BILLING_AGREEMENT`).
- A4: `ListCustomerPaymentTokens` requires a PayPal `customer_id`; your app must persist the
  `customer.id` returned when the token was created to later list a customer's tokens.
- A5: For production without an explicit `BaseUrl`, the integration sets
  `options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"` (the standard PayPal live host).
  The host string itself is well-known but not carried by the SDK map (`UNVERIFIED` from the SDK's
  perspective) — treat it as configuration and prefer supplying `BaseUrl` explicitly in production.

**Blockers / limitations**
- B1 (**limitation, confirmed in SDK source `Servers/ServerEnvironment.cs`**): `ServerEnvironment`
  exposes **only `Sandbox`** — there is **no built-in Production environment**. Selecting production is
  possible **only** by overriding `options.Server.Default.Sandbox.BaseUrl` to the live host (§2). This
  is a real constraint, not invented: the config's `Environment=production` must be honoured by the
  base-URL-override path, since `options.Environment` cannot reach production. No capability in scope is
  missing otherwise — all seven capabilities map to real operations.
