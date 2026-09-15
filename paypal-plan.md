# PayPal card + vaulted-card integration for eShopOnWeb — implementation plan & contract sheet

Scope: PayPal card payments (authorize → capture → refund, plus reauthorize/void), vaulted
(saved) cards, and reconciliation, exposed as JWT endpoints on `src/PublicApi`. SDK: APIMatic-
generated PayPal .NET SDK, root namespace `PayPalServerSdk`, NuGet `AsadAli.Checkout.Sdk`
(install version-less). Currency, environment (sandbox), client id/secret, and the optional
`PayPal:BaseUrl` override all come from configuration.

All contract facts below are grounded in the bundled SDK map (map page cited per row) or, where
noted, the SDK source at tag `v1.0.1`. Nothing here is open for "whoever implements."

---

## 1. Scope & sequence

| # | Step | Operations (controller.method) |
|---|---|---|
| 0 | Register client + auth + base-URL override (DI) | `AddPayPalServerSdkClient` |
| 1 | Authorize order total (raw card OR vaulted card) | `Orders.CreateOrder` (intent AUTHORIZE) → `Orders.AuthorizeOrder` |
| 2 | Capture at fulfilment; read gross/fee/net | `Payments.CaptureAuthorizedPayment` |
| 3 | Reauthorize a stale authorization | `Payments.ReauthorizePayment` |
| 4 | Void an authorization before capture | `Payments.VoidPayment` |
| 5 | Refund a capture (full/partial, idempotent) | `Payments.RefundCapturedPayment` |
| 6 | Idempotency on create/authorize/capture | `PayPalRequestId` param on the above |
| 7 | Reconciliation over an ISO-8601 range (paged) | `TransactionSearch.SearchTransactions` |
| 8 | Save/vault a card; read safe description | `Vault.CreateSetupToken` → `Vault.CreatePaymentToken` (or `CreatePaymentToken` alone) |
| 9 | Authorize using a vaulted card | vault id → `CardRequest.VaultId` in step 1 |
| 10 | Delete a vaulted card | `Vault.DeletePaymentToken` |

Retrieval helpers available if needed: `Payments.GetAuthorizedPayment`,
`Payments.GetCapturedPayment`, `Payments.GetRefund`, `Vault.GetPaymentToken`,
`Vault.ListCustomerPaymentTokens` (all `map/operations/Payments.md` / `Vault.md`).

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

### 2a. Namespaces used in this sheet (add a `using` for each kind)

| Namespace | Types from this sheet |
|---|---|
| `PayPalServerSdk` | `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` |
| `PayPalServerSdk.Servers` | `ServerEnvironment`, `DefaultOptions`, `DefaultOptions.SandboxOptions` |
| `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | `OAuth2ClientCredentials` |
| `PayPalServerSdk.Core.Configuration` | `RetryOptions` |
| `PayPalServerSdk.Core.Exceptions` | `SdkException<TError>` |
| `PayPalServerSdk.Core.ErrorResponse` | `RawError`, `ApiError` |
| `PayPalServerSdk.Models` | all request/response records below (`OrderRequest`, `Order`, `CapturedPayment`, `Refund`, `SearchResponse`, `PaymentTokenRequest`, `Error`, `Error1`, …) |
| `PayPalServerSdk.Models.Enums` | all enums below (`CheckoutPaymentIntent`, `OrderStatus`, `AuthorizationStatus`, …) |
| `PayPalServerSdk.Errors` | all typed `{Operation}Error` classes below |

### 2b. Operations

Legend: params listed in exact order; `null`⇒must-pass-explicit nullable (pass `null` to skip);
`=…`⇒has a default (omit or pass named). Every op is **throw-only** (no `…Result` variant).

**Orders — `client.Orders`** (map: `operations/Orders.md`)

| Op | Signature (params in order) | Request model + key fields | Response envelope → fields read | Error case |
|---|---|---|---|---|
| `CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req` (=`Authorize`), `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `Payer?`, `ApplicationContext?` | `Order`: `Id`, `Status (status): OrderStatus?`, `PurchaseUnits`, `PaymentSource (payment_source): PaymentSourceResponse?`, `Links` | A: `SdkException<CreateOrderError>`; `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` |
| `AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` (optional if payment_source already on the order) | `OrderAuthorizeResponse`: `Id`, `Status (status): OrderStatus?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` → `[0].Payments (payments): PaymentCollection?` → `.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → `[0].Id`, `.Status (status): AuthorizationStatus?`, `.ExpirationTime (expiration_time): string?`, `.Amount (amount): Money?` | A: `SdkException<AuthorizeOrderError>`; `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError(out RawError)` |

**Payments — `client.Payments`** (map: `operations/Payments.md`)

| Op | Signature | Request model | Response → fields read | Error case |
|---|---|---|---|---|
| `CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `CaptureRequest` (all optional): `Amount (amount): Money?` (omit for full capture of held amount), `FinalCapture (final_capture): bool?=false`, `InvoiceId?`, `NoteToPayer?` | `CapturedPayment`: `Id`, `Status (status): CaptureStatus?`, `Amount (amount): Money?`, `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?` → `.GrossAmount (gross_amount): Money !req`, `.PaypalFee (paypal_fee): Money?`, `.NetAmount (net_amount): Money?` | A: `SdkException<CaptureAuthorizedPaymentError>`; `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` |
| `ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `ReauthorizeRequest`: `Amount (amount): Money?` (only `amount` supported) | `PaymentAuthorization`: `Id`, `Status (status): AuthorizationStatus?`, `Amount`, `ExpirationTime (expiration_time): string?` | A: `SdkException<ReauthorizePaymentError>`; `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` |
| `VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | none (no body) | `PaymentAuthorization`: `Status (status): AuthorizationStatus?` (→ `Voided`) | A: `SdkException<VoidPaymentError>`; `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` |
| `RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `RefundRequest` (all optional): `Amount (amount): Money?` (**omit body/Amount ⇒ full refund; supply `Amount` ⇒ partial**), `CustomId?`, `InvoiceId?`, `NoteToPayer?` | `Refund`: `Id`, `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` → `.TotalRefundedAmount (total_refunded_amount): Money?` (cumulative — use to enforce "never refund beyond captured") | A: `SdkException<RefundCapturedPaymentError>`; `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` |

**TransactionSearch — `client.TransactionSearch`** (map: `operations/TransactionSearch.md`)

| Op | Signature | Response → fields read | Error case |
|---|---|---|---|
| `SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, RequestOptions? requestOptions=null, CancellationToken ct=default)` — `startDate`/`endDate` are ISO-8601 strings (wire `start_date`/`end_date`); the 8 nullable filters (`transactionId`…`terminalId`) **must be passed explicitly** (`null` to skip) | `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`. Each `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?` → `.TransactionId`, `.TransactionAmount (transaction_amount): Money?`, `.TransactionStatus (transaction_status): string?`, `.TransactionInitiationDate (transaction_initiation_date): string?` | **B: `SdkException<RawError>`** — no typed accessors; read `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()` |

Pagination: the SDK exposes **no auto-pager** (map: "only `page`, no `perPage`"). Loop yourself:
start `page=1`; after each call read `SearchResponse.TotalPages` and re-call incrementing `page`
until `page > TotalPages`. Empty ranges legitimately return `TransactionDetails == null`/empty and
`TotalPages == 0`; reporting lags up to ~3h (per op notes) so recent transactions may be absent.

**Vault — `client.Vault`** (map: `operations/Vault.md`)

| Op | Signature | Request model | Response → fields read | Error case |
|---|---|---|---|---|
| `CreateSetupToken` | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `SetupTokenRequest`: `Customer (customer): Customer?`, `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req` → `.Card (card): SetupTokenRequestCard?` (`Name?`, `Number?`, `Expiry?`, `SecurityCode?`, `Brand?`, `BillingAddress?`, `VerificationMethod?`, `ExperienceContext?`) | `SetupTokenResponse`: `Id`, `Status (status): PaymentTokenStatus?`, `PaymentSource` | A: `SdkException<CreateSetupTokenError>`; `TryGetError1(out Error1)` [400,403,422,500] · `TryGetRawError(out RawError)` |
| `CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `PaymentTokenRequest`: `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` → **either** `.Card (card): PaymentTokenRequestCard?` (`Number`,`Expiry`,`SecurityCode`,`Name`,`Brand`,`BillingAddress` — vault-on-the-fly) **or** `.Token (token): VaultTokenRequest?` (`Id (id): string !req`=setup-token id, `Type (type): VaultTokenRequestType !req`=`SetupToken`) | `PaymentTokenResponse`: `Id (id): string?` (**the vault/payment-token id**), `Customer`, `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `.Card (card): CardPaymentTokenEntity?` → `.LastDigits (last_digits): string?`, `.Brand (brand): CardBrand?`, `.Expiry (expiry): string?` (**safe description — never full PAN**) | A: `SdkException<CreatePaymentTokenError>`; `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError(out RawError)` |
| `DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions=null, CancellationToken ct=default)` | none | `void` (Task) — success = no throw | A: `SdkException<DeletePaymentTokenError>`; `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError(out RawError)` |

### 2c. Building the payment source (card in / vaulted card reference)

`OrderRequest.PaymentSource` is `PaymentSource` (map: `records-2`, `Models/PaymentSource.cs`);
`OrderAuthorizeRequest.PaymentSource` is `OrderAuthorizeRequestPaymentSource` (map: `records-1`).
Both expose `Card (card): CardRequest?`. `CardRequest` (map: `records-1`,
`Models/CardRequest.cs`) fields that matter:

- **Raw one-off card:** `Number (number): string?` (`"4111111111111111"`), `Expiry (expiry): string?`
  (`"YYYY-MM"`), `SecurityCode (security_code): string?`, `Name (name): string?`,
  `BillingAddress (billing_address): Address?`.
- **Pay with a saved (vaulted) card:** set `VaultId (vault_id): string?` = the
  `PaymentTokenResponse.Id` from step 8 — leave `Number`/`Expiry`/`SecurityCode` unset. This is
  the correct reference path for a vaulted **card**. Do **not** use the `Token`/`payment_source.token`
  path for a vaulted card: `Token.Type` is `TokenType`, whose **only** member is
  `BillingAgreement (BILLING_AGREEMENT)` (map: `enums.md`), i.e. that path is for billing
  agreements, not saved cards.
- Optional stored-credential hint: `StoredCredential (stored_credential): CardStoredCredential?`
  (`PaymentInitiator !req`, `PaymentType (StoredPaymentSourcePaymentType) !req`,
  `Usage (StoredPaymentSourceUsageType)?`) — map: `records-1`, `Models/CardStoredCredential.cs`.

`PurchaseUnitRequest` (map: `records-1`, `Models/PurchaseUnitRequest.cs`):
`Amount (amount): AmountWithBreakdown !req`, `ReferenceId?`, `CustomId?`, `InvoiceId?`,
`Description?`. `AmountWithBreakdown` = `CurrencyCode (currency_code): string !req`,
`Value (value): string !req` (decimal string, e.g. `"49.95"`), `Breakdown?`. **Authorized amount
== order total to the cent** ⇒ format the order total as a fixed-2-decimal string (invariant
culture) into `AmountWithBreakdown.Value`; `CurrencyCode` from `PayPal:Currency`. `Money` (for
capture/refund/reauthorize amounts) has the same two `!req` fields (`Models/Money.cs`).

### 2c-bis. Vaulted-card reuse — customer context (map-grounded; server-semantics gaps flagged)

**Vault reference path (confirmed by models):** paying with a saved card uses
`payment_source.card.vault_id` → `CardRequest.VaultId (vault_id): string?` with
`Number`/`Expiry`/`SecurityCode` unset. This is the only model-supported reference for a vaulted
**card** — the `payment_source.token` path is not it (`Token.Type` is `TokenType`, sole member
`BillingAgreement (BILLING_AGREEMENT)`, map `enums.md`).

**Customer models (map: `records-1`/`records-2`, namespace `PayPalServerSdk.Models`):**

| Model | Members (CSharp (wire): type) | Used by |
|---|---|---|
| `Customer` | `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?` | `PaymentTokenRequest.Customer (customer)`, `SetupTokenRequest.Customer (customer)` (`Models/Customer.cs`) |
| `CustomerResponse` | `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?` | `PaymentTokenResponse.Customer (customer)` — the **owning customer id returned after vaulting** (`Models/CustomerResponse.cs`) |
| `CardAttributes` | `Customer (customer): CardCustomerInformation?`, `Vault (vault): VaultInstructionBase?`, `Verification (verification): CardVerification?` | `CardRequest.Attributes (attributes)` (`Models/CardAttributes.cs`) |
| `CardCustomerInformation` | `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?`, `EmailAddress (email_address): string?`, `Phone (phone): PhoneWithType?`, `Name (name): Name?` | `CardAttributes.Customer` (`Models/CardCustomerInformation.cs`) |

**Where a customer id can be carried on the paying order (only model-supported path):**
`payment_source.card.attributes.customer` — i.e. `CardRequest.Attributes` (`CardAttributes`) →
`.Customer` (`CardCustomerInformation`) → set `.Id (id)` (PayPal customer id) and/or
`.MerchantCustomerId (merchant_customer_id)` (your stable id). Wire path:
`payment_source.card.attributes.customer.id`. **`OrderRequest.Payer` is NOT this** — `Payer`
(`Models/Payer.cs`) exposes only `EmailAddress`, `PayerId (payer_id)`, `Name`, `Phone`,
`BirthDate`, `TaxInfo`, `Address` — no vault-owning customer id. There is no `customer_id`
directly on `PaymentSource`/`CardRequest`; it lives under `card.attributes.customer`.

**Model-supported approach to make reuse work:** at vault time, either set
`PaymentTokenRequest.Customer` (`Customer.MerchantCustomerId` = your stable per-shopper id, and/or
`Customer.Id` if you already hold a PayPal customer id), **or** vault without a customer and read
back the PayPal-generated owner from `PaymentTokenResponse.Customer.Id`. Persist that owning
customer id with the token. At pay time, supply the same id via
`payment_source.card.attributes.customer.id` (and `vault_id` = the token id).

**vault_id placement (CreateOrder vs AuthorizeOrder) and the read path:** the models allow the
card (with `VaultId` + `Attributes.Customer`) on **either** `CreateOrder`'s
`OrderRequest.PaymentSource` (`PaymentSource`) **or** `AuthorizeOrder`'s
`OrderAuthorizeRequest.PaymentSource` (`OrderAuthorizeRequestPaymentSource`) — both expose
`Card (card): CardRequest?`. The models do **not** dictate which, and whether a vaulted card on
`CreateOrder` auto-authorizes inline (as the raw card did, yielding `ORDER_ALREADY_AUTHORIZED` on a
following `AuthorizeOrder`) is the same server behaviour flagged as a gap below — mirror the working
raw-card decision (card on `AuthorizeOrder`) unless sandbox shows otherwise. **The read path is
identical regardless of placement**, because `Order` (returned by `CreateOrder`) and
`OrderAuthorizeResponse` (returned by `AuthorizeOrder`) both type `PurchaseUnits` as
`IReadOnlyList<PurchaseUnit>?` (**same `PurchaseUnit` type**, map `records-1`): read
`PurchaseUnits[0].Payments (payments): PaymentCollection?` → `.Authorizations (authorizations):
IReadOnlyList<AuthorizationWithAdditionalData>?` → `[0].Id`, `.Status (status):
AuthorizationStatus?`, `.ExpirationTime (expiration_time): string?`, `.Amount (amount): Money?`. So
if a vaulted card on `CreateOrder` does auto-authorize, read the authorization from the
`CreateOrder` `Order` response using this exact path.

> **Genuine gap (server semantics, not settleable from the SDK map/source — `UNVERIFIED`):**
> the map/models describe *where* a customer id may be placed, but not PayPal's authorization
> rule that produces `PERMISSION_DENIED` (422) for a vaulted-card charge, nor whether populating
> `card.attributes.customer.id` with the token's owning customer resolves it. That is PayPal
> account/vault-ownership behaviour confirmable only against the live sandbox. Implement the
> model-supported approach above (owner id captured at vault time via `Customer`/read from
> `PaymentTokenResponse.Customer.Id`, replayed via `card.attributes.customer.id` at pay time) and
> verify against sandbox; treat the exact ownership rule as unverified.

### 2d. Enum value tables (map: `models/enums.md`, namespace `PayPalServerSdk.Models.Enums`)

Enums are `StringEnum<T>`, **not** C# enums — compare against static members
(`OrderStatus.Completed`) or build with `T.FromValue("WIRE")`. Member listed as `CSharpName (WIRE)`.

| Enum | Members (CSharp = WIRE) |
|---|---|
| `CheckoutPaymentIntent` | `Capture` (CAPTURE), `Authorize` (AUTHORIZE) |
| `OrderStatus` | `Created` (CREATED), `Saved` (SAVED), `Approved` (APPROVED), `Voided` (VOIDED), `Completed` (COMPLETED), **`PayerActionRequired` (PAYER_ACTION_REQUIRED)** |
| `AuthorizationStatus` | `Created` (CREATED), `Captured` (CAPTURED), `Denied` (DENIED), `PartiallyCaptured` (PARTIALLY_CAPTURED), `Voided` (VOIDED), `Pending` (PENDING) — **no `Expired` member** |
| `AuthorizationIncompleteReason` | `PendingReview` (PENDING_REVIEW), `DeclinedByRiskFraudFilters` (DECLINED_BY_RISK_FRAUD_FILTERS) |
| `CaptureStatus` | `Completed` (COMPLETED), `Declined` (DECLINED), `PartiallyRefunded` (PARTIALLY_REFUNDED), `Pending` (PENDING), `Refunded` (REFUNDED), `Failed` (FAILED) |
| `RefundStatus` | `Cancelled` (CANCELLED), `Failed` (FAILED), `Pending` (PENDING), `Completed` (COMPLETED) |
| `PaymentTokenStatus` | `Created` (CREATED), **`PayerActionRequired` (PAYER_ACTION_REQUIRED)**, `Approved` (APPROVED), `Vaulted` (VAULTED), `Tokenized` (TOKENIZED) |
| `VaultTokenRequestType` | `SetupToken` (SETUP_TOKEN) |
| `TokenType` | `BillingAgreement` (BILLING_AGREEMENT) |
| `CardBrand` | `Visa` (VISA), `Mastercard` (MASTERCARD), `Amex` (AMEX), `Discover` (DISCOVER), … (30 members) |
| `VaultStatus` | `Vaulted` (VAULTED), `Created` (CREATED), `Approved` (APPROVED) |

### 2e. Typed error payloads (for actionable operator messages)

`TryGetError(out Error)` → `Error` (map: `records-1`, `Models/Error.cs`, namespace
`PayPalServerSdk.Models`): `Name (name): string !req`, `Message (message): string !req`,
`DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`.
`ErrorDetails`: `Field?`, `Value?`, `Location?`, `Issue (issue): string !req`, `Description?`.
Vault ops use `TryGetError1(out Error1)` → `Error1` (same shape, `Details` is
`IReadOnlyList<ErrorDetails1>?`). Surface `Error.Message` + each `Details[].Issue`/`Description`
to the operator. (`Error` here is the payload record in `.Models`; the thrown wrapper is
`PayPalServerSdk.Errors.{Op}Error` — distinct namespace.)

### 2f. Client construction, auth, and base-URL override (DI)

Register via `services.AddPayPalServerSdkClient(o => { … })` (map: `sdk-map.md` "Getting a
client"; `ServiceCollectionExtensions.cs`). Options set on `o` (`PayPalServerSdkClientOptions`):

- **Environment:** `o.Environment = ServerEnvironment.Sandbox;` (`PayPalServerSdk.Servers`; the
  only member — `Servers/ServerEnvironment.cs`).
- **Auth (OAuth2 client credentials):** `o.Oauth2 = new OAuth2ClientCredentials { ClientId = …,
  ClientSecret = … };` — type in `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`;
  members `ClientId` (required), `ClientSecret` (required), `Scope?` (SDK source
  `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`). Load id/secret from
  config, never hardcode. The SDK fetches/refreshes the bearer token itself.
- **Base-URL override (`PayPal:BaseUrl`) — applies to EVERY call including the OAuth token
  request.** Set:
  ```
  o.Server = new PayPalServerSdk.ServerOptions {
      Default = new PayPalServerSdk.Servers.DefaultOptions {
          Sandbox = new PayPalServerSdk.Servers.DefaultOptions.SandboxOptions { BaseUrl = <PayPal:BaseUrl verbatim> }
      }
  };
  ```
  Grounding (SDK source, verified this session): the OAuth token endpoint is built as
  `server.Default("/v1/oauth2/token")`, and `Server.Default` resolves through the **same**
  `DefaultOptions.Resolve(environment, path)` that every API call uses — for `Sandbox` it returns
  `new UrlTemplate(Sandbox.BaseUrl, path)`. So overriding `DefaultOptions.Sandbox.BaseUrl` redirects
  both the API calls **and** the token request to the custom host. The default `BaseUrl` is
  `https://api-m.sandbox.paypal.com`. Only apply the override when `PayPal:BaseUrl` is set;
  otherwise leave `o.Server` unset so the default sandbox host is used. `ServerOptions` is in the
  **root** namespace `PayPalServerSdk`; `DefaultOptions` (and nested `SandboxOptions`) are in
  `PayPalServerSdk.Servers` — two types configured together, two different namespaces.
- **Idempotency (`PayPal-Request-Id`):** pass a caller-supplied key via the `payPalRequestId`
  parameter on `CreateOrder`, `AuthorizeOrder`, `CaptureAuthorizedPayment`,
  `RefundCapturedPayment` (and `CreatePaymentToken`/`CreateSetupToken`). Repeating a call with the
  same key returns the original result rather than re-executing; two distinct partial refunds use
  two distinct keys. (The SDK passes this as the header; the idempotency *guarantee* is PayPal's
  server behaviour — treat as `UNVERIFIED` and still enforce your own once-only guard around
  authorize/capture/refund, see trap ⚠-R.)

---

## 3. Trap notes (attach to the step where each bites)

⚠ Step 0 (client registration) — the `HttpClient`/handler pipeline must be long-lived and reused
(via `IHttpClientFactory`), **not** rebuilt per request; the SDK client wrapper's lifetime is a
separate decision. **MUST load `dotnet-client-initialization`** before writing
`AddPayPalServerSdkClient` / constructing the client.

⚠ Step 0 (auth) — *when* credentials must be set relative to client construction, and how the
token is cached/refreshed across the app's single process run, are not visible in the signature.
**MUST load `dotnet-authentication`** before wiring `Oauth2`.

⚠ Step 0 (base URL + resilience) — the SDK retry/timeout options do **not** bound a whole call and
are **not** the `HttpClient` timeout; and `HttpMethodsToRetry` gates only the *status* trigger
while a *transport* failure can re-send a non-idempotent `POST` (authorize/capture/refund) — which
is why the caller-supplied `PayPal-Request-Id` guard matters. What `RetryOptions.Timeout` actually
bounds, and which failures re-send a write, come from the skill, not the option names. **MUST load
`dotnet-configuration-resilience`** before tuning retries/timeouts/base URL.

⚠ Steps 1–10 (calling) — `SearchTransactions` and other list/search-shaped calls have optional
params with no C# default that mis-bind positionally; call with **named arguments**. **MUST load
`dotnet-calling-endpoints`** before the first call.

⚠ Steps 1–10 (models) — enums are `StringEnum<T>` (compare to static members / `FromValue`, not
`==` on a string and not C# enum switch); required members must be set in the object initializer;
unmodeled JSON fields are dropped on deserialize. Build request payloads accordingly. **MUST load
`dotnet-models`** before constructing any request record.

⚠ Step 1/2 (3DS / challenge — STOP-and-report) — a card response that needs browser approval
surfaces as **`OrderStatus.PayerActionRequired` (PAYER_ACTION_REQUIRED)** on the create/authorize
response `Status` (and `PaymentTokenStatus.PayerActionRequired` on a vault response). Detect it and
surface a STOP to the operator; do **not** design an approval round-trip. Grounded in `enums.md`;
whether a given sandbox card triggers it is live-wire (`UNVERIFIED`) — code the detection
defensively regardless.

⚠ Step 3 (staleness detection) — `AuthorizationStatus` has **no `Expired` member** (see 2d), so a
stale authorization is **not** signalled by a distinct status value. Detect staleness from
`PaymentAuthorization.ExpirationTime` (past the honor window) or from a capture failure
(`SdkException<CaptureAuthorizedPaymentError>`, typically 422) whose `Error.Details[].Issue`
carries PayPal's issue code. The exact issue-code string is **not** in the SDK map or source
(server-supplied) — `UNVERIFIED`: read `Error.Message` + `Details[].Issue` best-effort and surface
them verbatim to the operator; fall back to the generic message when absent. Then call
`ReauthorizePayment` and capture the new authorization. **MUST load `dotnet-error-handling`**.

⚠ Step 5/6 (idempotency guard — "R") — the `PayPal-Request-Id` idempotency guarantee is PayPal
server behaviour, not something the SDK enforces (`UNVERIFIED`). Enforce your own once-only guard:
persist the request id ↔ result mapping for authorize/capture/refund within the process run; before
a refund, check cumulative `SellerPayableBreakdown.TotalRefundedAmount` against the captured amount
so a partly-refunded order can never be refunded beyond captured, while two *distinct* partial
refunds (distinct keys) remain legitimate.

⚠ Step 2/5 (nullable money reads) — `SellerReceivableBreakdown.PaypalFee`/`.NetAmount` and
`Refund.Amount` are nullable ("not available for transactions in pending state"). Read defensively;
do not assume fee/net are present on a `Pending` capture.

---

## 4. REQUIRED READING — load BEFORE implementation starts

These carry defaults, worked examples, and semantics this sheet deliberately does **not** restate.
Load each before writing the code for the step it governs:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, `HttpClient` lifetime, DI registration |
| `dotnet-authentication` | Step 0 — setting `Oauth2` credentials, token caching/refresh |
| `dotnet-configuration-resilience` | Step 0 — base-URL override, retries/timeouts, manual pagination (step 7) |
| `dotnet-calling-endpoints` | Steps 1–10 — named-argument calls, async/cancellation |
| `dotnet-models` | Steps 1–10 — building request records, `StringEnum<T>`, required members |
| `dotnet-error-handling` | Every `try/catch` and the integration error boundary |
| `dotnet-testing` | Integration-layer tests (fake the `HttpClient` seam) |

The sheet deliberately does not carry these skills' contents. In particular, **the error boundary
must be written with `dotnet-error-handling` loaded**, because two `System.Text.Json.JsonException`
hazards reach the boundary from opposite directions and need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only
  catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

Also recall the two error *cases*: all operations in scope are **Case A (typed)** — catch
`SdkException<{Op}Error>` and use the `TryGet…` accessors in 2b — **except**
`SearchTransactions`, which is **Case B**: catch `SdkException<RawError>` and read
`StatusCode`/`ReadAsString()`/`ReadAsJson<T>()` (no typed accessors).

---

## 5. Assumptions & Blockers

- **Assumption (authorize flow shape):** the integration creates an order with
  `Intent = Authorize` and the card `payment_source`, then calls `AuthorizeOrder(orderId, …)` to
  place the hold, reading the authorization id from
  `OrderAuthorizeResponse.PurchaseUnits[0].Payments.Authorizations[0].Id`. `payment_source` may be
  supplied on `CreateOrder` **or** on the `AuthorizeOrder` body (`OrderAuthorizeRequestPaymentSource`);
  the sheet documents both. The map does not prescribe which; either is valid.
- **Assumption (capture path):** capture uses `Payments.CaptureAuthorizedPayment(authorizationId)`
  (returns `CapturedPayment` with the seller-receivable breakdown), not `Orders.CaptureOrder`
  (which captures an order directly and returns `Order` without a top-level breakdown). The
  gross/fee/net requirement drives this choice.
- **Assumption (vault flow):** two supported shapes are documented — setup-token → payment-token
  (`CreateSetupToken` then `CreatePaymentToken` with a `VaultTokenRequest`), and vault-on-the-fly
  (`CreatePaymentToken` with a `PaymentTokenRequestCard`). Pick per PCI posture; the setup-token
  flow keeps raw PAN out of the payment-token request.
- **`UNVERIFIED` (live-wire only, coded defensively per traps):** (a) whether a given sandbox card
  yields `PAYER_ACTION_REQUIRED`; (b) the exact staleness issue-code string in
  `Error.Details[].Issue`; (c) that PayPal honours `PayPal-Request-Id` idempotency. None are
  resolvable from the SDK map or source — all handled by defensive extraction + own guard, not by
  assuming the value.
- **Known gap — vaulted-card `PERMISSION_DENIED` (server semantics, `UNVERIFIED`):** the SDK
  map/models fully describe the reference (`payment_source.card.vault_id`) and the only place a
  customer id can be carried on a card payment source
  (`payment_source.card.attributes.customer.id`, via `CardRequest.Attributes` →
  `CardAttributes.Customer` → `CardCustomerInformation`), plus that a vault returns its owning
  customer in `PaymentTokenResponse.Customer.Id`. They do **not** encode PayPal's authorization
  rule that yields `PERMISSION_DENIED (422)` for a vaulted-card charge, nor confirm that supplying
  the owning customer id resolves it — that is live sandbox behaviour. See §2c-bis for the
  model-supported approach to implement and verify. Not an SDK capability gap; a server-semantics
  unknown.
- **No blockers.** Every capability requested (authorize, capture with fee/net breakdown,
  reauthorize, void, full/partial refund with request-id, idempotency headers, transaction search
  with manual pagination, setup/payment/delete vault tokens, vaulted-card reference via
  `card.vault_id`, custom base URL covering the OAuth token request) is exposed by the SDK and
  grounded above. No requested capability is missing.
