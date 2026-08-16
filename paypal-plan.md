# PayPal .NET SDK integration plan — card payments + vaulted cards (server-side, no browser)

SDK: `AsadAli.Checkout.Sdk` (install version-less) · root namespace `PayPalServerSdk` · client
`PayPalServerSdkClient` · map source commit `9653d18` (tag `v1.0.1`). Every row cites the map page (or
named SDK source file) it came from. Target app: eShopOnWeb (ASP.NET Core). All work is C#/.NET only.

This SDK is APIMatic-generated; **all operations are throw-based, and NO `…Result` no-throw variants
exist** (`sdk-map.md`, error-handling model). Of the operations in scope, all are Case A (typed error)
except `SearchTransactions`, which is Case B (`RawError`).

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Client construction + OAuth2 + custom BaseUrl override (incl. token call) | client options + auth wiring |
| 2 | Create order, intent=AUTHORIZE, direct raw card as payment source | `Orders.CreateOrder` |
| 3 | Authorize the order (place hold); read authorization id + status | `Orders.AuthorizeOrder` |
| 4 | Capture the authorization; read gross / paypal_fee / net | `Payments.CaptureAuthorizedPayment` |
| 5 | Re-authorize a stale authorization | `Payments.ReauthorizePayment` |
| 6 | Void an authorization before capture | `Payments.VoidPayment` |
| 7 | Refund a captured payment (full/partial) with idempotency key | `Payments.RefundCapturedPayment` |
| 8 | Vault a raw card (server-side), list/get/delete token, pay with vaulted token | `Vault.CreatePaymentToken`, `Vault.CreateSetupToken`, `Vault.GetPaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.DeletePaymentToken`, then `Orders.CreateOrder` with `vault_id` |
| 9 | Transaction search / reporting with pagination | `TransactionSearch.SearchTransactions` |
| 10 | Error handling boundary (status + body + idempotency replay) | all of the above |

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

### 2.0 Namespaces in scope (add a separate `using` for each — child namespaces are NOT transitively imported)

| Namespace | Types used from it |
|---|---|
| `PayPalServerSdk` | `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` (root file `ServerOptions.cs`) |
| `PayPalServerSdk.Servers` | `ServerEnvironment`, `DefaultOptions` (+ nested `DefaultOptions.SandboxOptions`) |
| `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | `OAuth2ClientCredentials` (source `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`) |
| `PayPalServerSdk.Api` | `Orders`, `Payments`, `Vault`, `TransactionSearch` controllers |
| `PayPalServerSdk.Models` | all request/response records + error payload records (`Error`, `Error1`, `DefaultError`, `Money`, `CardRequest`, …) |
| `PayPalServerSdk.Models.Enums` | `CheckoutPaymentIntent`, `OrderStatus`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus`, `CardBrand`, `VaultTokenRequestType`, `PaymentTokenStatus`, … |
| `PayPalServerSdk.Errors` | `{Operation}Error` classes (`CreateOrderError`, `CaptureAuthorizedPaymentError`, …) — source `Errors/…` |
| `PayPalServerSdk.Core.Exceptions` | `SdkException<TError>` (source `Core/Exceptions/SdkException.cs`) |
| `PayPalServerSdk.Core.ErrorResponse` | `RawError` (source `Core/ErrorResponse/RawError.cs`) |

### 2.1 Step 1 — Client construction, OAuth2, custom BaseUrl override

Grounded in `sdk-map.md` (client-options, servers-auth) plus SDK source `PayPalServerSdkClientOptions.cs`,
`ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/ServerEnvironment.cs`, `Server.cs`,
`AuthSchemes.cs`, and `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials{,Strategy}.cs`.

Constructor (only one): `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`.
DI alternative: `services.AddPayPalServerSdkClient(o => { … })` (`ServiceCollectionExtensions.cs`).

`PayPalServerSdkClientOptions` members (source `PayPalServerSdkClientOptions.cs`, verbatim):

| Property | Type | Purpose |
|---|---|---|
| `Environment` | `ServerEnvironment` (`PayPalServerSdk.Servers`) | only member is `ServerEnvironment.Sandbox` (there is **no** Live/Production env in this SDK); default is `Sandbox` |
| `Retry` | `RetryOptions` | resilience (see trap notes) |
| `Logging` | `LoggingOptions` | — |
| `Server` | `ServerOptions` (`PayPalServerSdk`) | base-URL override — see below |
| `Oauth2` | `OAuth2ClientCredentials?` | client-id/secret — see below |
| `Oauth2TokenStrategy` | `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | leave `null` — default strategy is used |

**OAuth2 client credentials** — `OAuth2ClientCredentials` (sealed class, namespace
`PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`), members verbatim from source:

| Member | Type | Required |
|---|---|---|
| `ClientId` | `string` | `required` |
| `ClientSecret` | `string` | `required` |
| `Scope` | `string?` | optional (omit / `null`) |

Set `options.Oauth2 = new OAuth2ClientCredentials { ClientId = <from config>, ClientSecret = <from config> }`.
The default token strategy POSTs `grant_type=client_credentials` to `/v1/oauth2/token` with HTTP Basic auth
(`Authorization: Basic base64(clientId:clientSecret)`), obtained automatically before calls — you do not call
a token endpoint yourself (source `OAuth2ClientCredentialsStrategy.cs`, `AuthSchemes.cs`). Load credentials
from configuration, never hardcode.

**Custom BaseUrl override for ALL calls including the token call** — the single base URL is
`options.Server.Default.Sandbox.BaseUrl` (a `string`, default `"https://api-m.sandbox.paypal.com"`).
Shape (verbatim from source):
- `ServerOptions.Default` → `DefaultOptions` (namespace `PayPalServerSdk.Servers`)
- `DefaultOptions.Sandbox` → `DefaultOptions.SandboxOptions`
- `DefaultOptions.SandboxOptions.BaseUrl` → `string` (settable)

To force a custom host verbatim:
```
options.Environment = ServerEnvironment.Sandbox;               // only env available
options.Server.Default.Sandbox.BaseUrl = "https://your-host";  // applied to EVERY request
```
This override is authoritative for the OAuth token call too: the token URL is built via
`server.Default("/v1/oauth2/token")` → `DefaultOptions.Resolve` → `Sandbox.BaseUrl` (source `AuthSchemes.cs`
line 17 + `Server.cs` + `DefaultOptions.cs`). There is exactly one base URL; both the token call and every
API call resolve against it, so a custom `BaseUrl` cannot be bypassed by the token call.

### 2.2 Operation contract rows

Legend: request fields as `CSharpName (wire_name): Type` — `!req` = C# `required` (must be set in object
initializer); trailing `?` = nullable/optional. Money is always `Money { CurrencyCode (currency_code):
string !req, Value (value): string !req }` (`records-1-Ac-Pa.md`). Currency comes from app config; amount
`Value` is a decimal string, e.g. `"100.00"`.

#### CreateOrder (Step 2 & 8-pay) — `client.Orders.CreateOrder` · `operations/Orders.md`
- **Signature**: `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - First 5 params are nullable with **no default → pass explicitly** (`null` to skip). `payPalRequestId` = idempotency key (PayPal-Request-Id header). Pass `prefer: "return=representation"` to get the full order body back (default `"return=minimal"` returns a sparse body).
- **Returns**: `Order`
- **Request tree** — `OrderRequest` (`records-1`):
  - `Intent (intent): CheckoutPaymentIntent !req` → set `CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`)
  - `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`
  - `PaymentSource (payment_source): PaymentSource?` ← direct card OR vaulted token here
  - `Payer (payer): Payer?`, `ApplicationContext (application_context): OrderApplicationContext?` (optional)
  - `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req` (+ optional `ReferenceId`, `Description`, `CustomId`, `InvoiceId`, `Items`, `Shipping`, `Payee`)
  - `AmountWithBreakdown`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?` — this is the amount object; set currency_code + value to the order total.
  - `PaymentSource`: `Card (card): CardRequest?` (also `Token`, `Paypal`, wallets — use `Card` only)
  - **Direct raw card** = `CardRequest` (`records-1`): `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?` (format `"YYYY-MM"`), `SecurityCode (security_code): string?` (CVC), `BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?`, `Attributes (attributes): CardAttributes?`, `ExperienceContext (experience_context): CardExperienceContext?`, `StoredCredential`, `NetworkToken`, `SingleUseToken`. Sandbox test card → `Number = "4111111111111111"`.
  - **Pay with vaulted token** (Step 8): same `CardRequest` but set only `VaultId (vault_id) = <payment-token id>` (leave Number/Expiry/SecurityCode null).
- **Response envelope** — `Order` (`records-1`): `Id (id): string?`, `Status (status): OrderStatus?`, `PaymentSource (payment_source): PaymentSourceResponse?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `Links (links): IReadOnlyList<LinkDescription>?`. Read order id ← `Order.Id`; status ← `Order.Status`.
- **Error**: `SdkException<CreateOrderError>` — Case A. Accessors: `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` [fallback].

#### AuthorizeOrder (Step 3) — `client.Orders.AuthorizeOrder` · `operations/Orders.md`
- **Signature**: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `id` = order id from CreateOrder. Middle 5 params nullable, **pass explicitly**. Body may be `null` when the card/payment_source was already supplied at create; pass `prefer: "return=representation"` to get authorizations back.
- **Returns**: `OrderAuthorizeResponse`
- **Request (optional)**: `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` (`Card (card): CardRequest?` etc.)
- **Response — read-back path to authorization id + status** (`records-1`, `records-2`):
  - `OrderAuthorizeResponse.PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`
  - `PurchaseUnit.Payments (payments): PaymentCollection?`
  - `PaymentCollection.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`
  - `AuthorizationWithAdditionalData.Id (id): string?` = **authorization id**; `.Status (status): AuthorizationStatus?` = authorization status; `.Amount (amount): Money?`; `.ExpirationTime (expiration_time): string?`.
  - Full path: `resp.PurchaseUnits[i].Payments.Authorizations[j].Id / .Status`.
- **Error**: `SdkException<AuthorizeOrderError>` — Case A. `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError(out RawError)`.

#### CaptureAuthorizedPayment (Step 4) — `client.Payments.CaptureAuthorizedPayment` · `operations/Payments.md`
- **Signature**: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `authorizationId` from Step 3. 4 middle params nullable, **pass explicitly**. `payPalRequestId` = idempotency key. `prefer: "return=representation"` to get the breakdown.
- **Returns**: `CapturedPayment`
- **Request (optional)**: `CaptureRequest` (`records-1`): `Amount (amount): Money?` (omit for full capture), `InvoiceId?`, `FinalCapture (final_capture): bool? = false`, `NoteToPayer?`, `SoftDescriptor?`, `PaymentInstruction?`.
- **Response — captured amount / fee / net** (`records-1`, `records-2`):
  - `CapturedPayment.Id (id): string?`, `.Status (status): CaptureStatus?`, `.Amount (amount): Money?`
  - `CapturedPayment.SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`
  - `SellerReceivableBreakdown.GrossAmount (gross_amount): Money !req` → gross captured
  - `SellerReceivableBreakdown.PaypalFee (paypal_fee): Money?` → PayPal fee
  - `SellerReceivableBreakdown.NetAmount (net_amount): Money?` → net proceeds
  - Each Money → `.CurrencyCode` + `.Value`. Full path: `captured.SellerReceivableBreakdown.GrossAmount.Value` etc.
  - Note: `SellerReceivableBreakdown` is documented as "not available for transactions that are in pending state" — guard for null when `Status == CaptureStatus.Pending`.
- **Error**: `SdkException<CaptureAuthorizedPaymentError>` — Case A. `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

#### ReauthorizePayment (Step 5) — `client.Payments.ReauthorizePayment` · `operations/Payments.md`
- **Signature**: `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId`, `payPalAuthAssertion`, `body` nullable, **pass explicitly**.
- **Returns**: `PaymentAuthorization`
- **Request**: `ReauthorizeRequest` (`records-2`): `Amount (amount): Money?` (only field supported). Honor-period/amount rules in the op notes (US: up to 115% of original, +$75 max).
- **Response**: `PaymentAuthorization` (`records-2`): `Id (id): string?`, `Status (status): AuthorizationStatus?`, `Amount?`, `ExpirationTime (expiration_time): string?`, `StatusDetails (status_details): AuthorizationStatusDetails?`.
  - **Renewal-no-longer-possible signal**: reauthorize is only valid within the 29-day window; past it, the call fails (per op notes, "you must create an authorized payment instead"). The failure surfaces as `SdkException<ReauthorizePaymentError>`; read `TryGetError(out Error)` and inspect `Error.Details[].Issue` (e.g. authorization expired / not reauthorizable) so an operator can act. The exact `issue` string is a live-wire value → treat defensively (see UNVERIFIED note in Assumptions). Also `PaymentAuthorization.Status` may come back `AuthorizationStatus.Denied`/`Voided` (enum below).
- **Error**: `SdkException<ReauthorizePaymentError>` — Case A. `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

#### VoidPayment (Step 6) — `client.Payments.VoidPayment` · `operations/Payments.md`
- **Signature**: `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 3 middle params nullable, **pass explicitly**. No request body. (Cannot void a fully captured authorization — op notes.)
- **Returns**: `PaymentAuthorization` (read `.Status` → expect `AuthorizationStatus.Voided`).
- **Error**: `SdkException<VoidPaymentError>` — Case A. `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

#### RefundCapturedPayment (Step 7) — `client.Payments.RefundCapturedPayment` · `operations/Payments.md`
- **Signature**: `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `captureId` from Step 4 (`CapturedPayment.Id`). 4 middle params nullable, **pass explicitly**. **`payPalRequestId` = the caller idempotency key** (PayPal-Request-Id header) — this is how idempotency is expressed and passed through the SDK.
- **Returns**: `Refund`
- **Request**: `RefundRequest` (`records-2`): `Amount (amount): Money?` (**omit / `null` body → full refund; set `Amount` → partial refund**), `CustomId?`, `InvoiceId?`, `NoteToPayer?`, `PaymentInstruction?`.
- **Response**: `Refund` (`records-2`): `Id (id): string?` = refund id, `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (`GrossAmount`, `PaypalFee`, `NetAmount`).
- **Error**: `SdkException<RefundCapturedPaymentError>` — Case A. `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

#### Vault — save a raw card server-side (Step 8) · `operations/Vault.md`
Two paths; **for a raw card with no browser, use the DIRECT payment-token path (A).** The setup-token
path (B) exists but `SetupTokenRequestCard` carries `experience_context`/`verification_method` that can
trigger an approval round-trip — avoid it unless a setup token is specifically needed.

**A) Direct — `client.Vault.CreatePaymentToken`**
- **Signature**: `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` nullable, **pass explicitly** (idempotency key).
- **Returns**: `PaymentTokenResponse`
- **Request** — `PaymentTokenRequest` (`records-2`): `Customer (customer): Customer?` (`Id`, `MerchantCustomerId` — set/reuse a customer id to group tokens), `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.
  - `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?`, `Token (token): VaultTokenRequest?`
  - `PaymentTokenRequestCard` (`records-2`): `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?` → raw card here.
- **Response — token id + safe brand/last4** — `PaymentTokenResponse` (`records-2`): `Id (id): string?` = **vault/token id** (use as `CardRequest.VaultId` when paying), `Customer (customer): CustomerResponse?`, `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?`, `Links?`.
  - `PaymentTokenResponsePaymentSource.Card (card): CardPaymentTokenEntity?`
  - `CardPaymentTokenEntity` (`records-1`): `Brand (brand): CardBrand?` = brand, `LastDigits (last_digits): string?` = last4, `Expiry (expiry): string?`, `Name?`, `BillingAddress?`.
- **Error**: `SdkException<CreatePaymentTokenError>` — Case A. `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError(out RawError)`. (Note: Vault ops use `TryGetError1`/`Error1`, NOT `TryGetError`/`Error`.)

**B) Setup-token then payment-token (only if a setup token is required)**
- `client.Vault.CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, …)` → `SetupTokenResponse` (`Id (id): string?`, `Status (status): PaymentTokenStatus?`). Request `SetupTokenRequest.PaymentSource (payment_source): SetupTokenRequestPaymentSource !req` → `Card (card): SetupTokenRequestCard?` (raw card; also has `verification_method`, `experience_context`).
  - Error: `SdkException<CreateSetupTokenError>` — `TryGetError1(out Error1)` [400,403,422,500].
- Then `CreatePaymentToken` with `PaymentTokenRequestPaymentSource.Token = new VaultTokenRequest { Id = <setupTokenId>, Type = VaultTokenRequestType.SetupToken }` (`records-2`; enum `VaultTokenRequestType.SetupToken` wire `SETUP_TOKEN`).

**Retrieve — `client.Vault.GetPaymentToken`**
- **Signature**: `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentTokenResponse` (same shape as above).
- **Error**: `SdkException<GetPaymentTokenError>` — `TryGetError1(out Error1)` [403,404,422,500] · `TryGetRawError`.

**List — `client.Vault.ListCustomerPaymentTokens`**
- **Signature**: `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` — query wire: `customer_id`←`customerId`, `page_size`←`pageSize`, `page`←`page`, `total_required`←`totalRequired`. Call with **named args** (all optionals have C# defaults, but name them to avoid mis-binding).
- **Returns**: `CustomerVaultPaymentTokensResponse` (`records-1`): `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `Customer?`, `Links?`. Pass `totalRequired: true` to populate `TotalItems`/`TotalPages`; page 1..`TotalPages` to cover all tokens.
- **Error**: `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError`.

**Delete — `client.Vault.DeletePaymentToken`**
- **Signature**: `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → **`void` (Task)** (no body returned; success = no throw).
- **Error**: `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError`.

**Pay an order with a vaulted token** (Step 8 completion): call `CreateOrder` with
`OrderRequest.PaymentSource.Card = new CardRequest { VaultId = <PaymentTokenResponse.Id> }` — no raw PAN.
(`CardRequest.VaultId (vault_id)`, `records-1`.)

#### SearchTransactions (Step 9) — `client.TransactionSearch.SearchTransactions` · `operations/TransactionSearch.md`
- **Signature**: `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `startDate`/`endDate` **required** — ISO-8601 date-time strings (wire `start_date`/`end_date`). 8 middle params (`transactionId`…`terminalId`) nullable, **pass explicitly** (`null` to skip). **Call with named args** — many optionals mis-bind positionally. `pageSize` max per PayPal is 500; default 100.
- **Returns**: `SearchResponse` (`records-2`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Links?`.
  - Each `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?` → `TransactionId (transaction_id): string?`, `TransactionAmount (transaction_amount): Money?`, `TransactionStatus (transaction_status): string?` (free string, not an enum), `FeeAmount (fee_amount): Money?`, `TransactionInitiationDate?`, `TransactionUpdatedDate?`.
- **Pagination (cover the whole range)**: request `page = 1`, read `SearchResponse.TotalPages`, then loop `page = 2..TotalPages` re-issuing the same call with the incremented `page`. There is NO `perPage` param — page size is `pageSize`. `TotalItems` gives the overall count.
- **Error**: `SdkException<RawError>` — **Case B (no typed accessors)**. Read `ex.Error.StatusCode` (`HttpStatusCode`), `ex.Error.ReadAsString()`, or `ex.Error.ReadAsJson<DefaultError>()` (`records-1` `DefaultError`: `Name`,`Message`,`DebugId`,`Details`). This is the ONE Case-B op in scope — its catch clause differs from all the others.

### 2.3 Enum value tables (needed by the sheet) — namespace `PayPalServerSdk.Models.Enums`

Written as `CSharpMember (WIRE_VALUE)`; write the C# member in code (`enums.md`).

| Enum | Members |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, `Jcb (JCB)`, `Maestro (MAESTRO)`, `Diners (DINERS)`, `Elo (ELO)`, `Rupay (RUPAY)`, `Unknown (UNKNOWN)`, … (30 members; full list `enums.md`) |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |

### 2.4 Error payload records (Case A `out` types) — namespace `PayPalServerSdk.Models`

| Record | Fields | Used by |
|---|---|---|
| `Error` | `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`, `Links?` | Orders + Payments `TryGetError` |
| `Error1` | `Name`, `Message`, `DebugId`, `Details (IReadOnlyList<ErrorDetails1>)?`, `Links (IReadOnlyList<ErrorLinkDescription>)?` | Vault `TryGetError1` |
| `DefaultError` | `Name`, `Message`, `DebugId`, `InformationLink?`, `Details (IReadOnlyList<TransactionSearchErrorDetails>)?`, `Links?` | `SearchBalances`; also usable via `RawError.ReadAsJson<DefaultError>()` for `SearchTransactions` |
| `ErrorDetails` | `Field?`, `Value?`, `Location? = "body"`, `Issue (issue): string !req`, `Description?`, `Links?` | nested in `Error` |

`SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`) exposes `.Error` of `TError`. `RawError`
(`PayPalServerSdk.Core.ErrorResponse`): `StatusCode: HttpStatusCode`, `ReadAsString(): string`,
`ReadAsJson<T>(): T?`, `ReadAsBytes()`. (`sdk-map.md` error-core.)

---

## 3. Trap notes (load the named skill at the step where the hazard bites)

⚠ **Step 1 (client registration)** — the `HttpClient`/handler pipeline lifetime and whether the SDK client
should be transient/singleton is not visible in the constructor signature; getting it wrong causes socket
exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before wiring the client into DI.

⚠ **Step 1 (auth)** — whether credentials must be set before the client is constructed vs in the DI
callback, and how/whether the OAuth token is cached and refreshed across calls, is not shown by the options
shape. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ **Step 1 (resilience/base URL)** — the SDK `Retry` options do **not** bound a whole call and are **not**
the `HttpClient` timeout; and which HTTP methods actually retry (a transport failure can re-send a
non-idempotent POST) is not what the option names suggest — this is why every write below must carry a
`payPalRequestId` idempotency key. **MUST load `dotnet-configuration-resilience`** before tuning retries,
timeouts, or the base URL.

⚠ **Steps 2–9 (calling / building requests)** — optional params with no C# default mis-bind in positional
calls, and enums here are `StringEnum<T>` (not C# enums) built via static members / `FromValue`, while
unmodeled JSON is dropped on deserialize. **MUST load `dotnet-calling-endpoints`** (first call) and
**`dotnet-models`** (first non-scalar field).

⚠ **Steps 3–4 (browser-challenge / 3DS STOP gap)** — a direct-card order that needs browser approval does
NOT surface as an exception; it surfaces in the *success* response as `Order.Status` /
`OrderAuthorizeResponse.Status == OrderStatus.PayerActionRequired` and/or a HATEOAS link in `.Links` whose
`Rel` indicates payer action, plus a populated
`payment_source.card.authentication_result.three_d_secure` (`CardResponse.AuthenticationResult`
→ `AuthenticationResponse.ThreeDSecure`). Detect this and **STOP-and-report** — do not attempt to
capture. The exact link `rel` string is a live-wire value (see UNVERIFIED). **MUST load `dotnet-models`**
to read the response union/enum safely.

⚠ **Steps 4–8 error accessors differ by controller** — Orders/Payments typed errors use
`TryGetError(out Error)`; Vault uses `TryGetError1(out Error1)`; Payments ops also have
`TryGetNoContent(out RawError)` for 500; `SearchTransactions` is Case B (`RawError`, no typed accessor).
`TryGetRawError` is the fallback, not a catch-all over the typed shapes. **MUST load
`dotnet-error-handling`** before writing any catch ladder.

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 1 — supplying OAuth2 client-credentials, token caching/refresh |
| `dotnet-configuration-resilience` | Step 1 — retries/timeouts semantics, base-URL override, pagination loop |
| `dotnet-calling-endpoints` | Steps 2–9 — named args for optional params, async/cancellation |
| `dotnet-models` | Steps 2–9 — building request records, `StringEnum<T>`, reading response enums/nested objects |
| `dotnet-error-handling` | Step 10 — which exceptions reach catch, reading status/body safely, Case A vs B |
| `dotnet-testing` | tests — the `HttpClient` constructor arg is the test seam |

Mandatory hazard rows for the error boundary (write these into the boundary from the FIRST cut — a caveat
that arrives later arrives too late):

- A drifted or malformed **2xx** body (a missing `required` member — e.g. `Money.Value`,
  `SellerReceivableBreakdown.GrossAmount`) surfaces as a `System.Text.Json.JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `System.Text.Json.JsonException` **while the error object is being constructed**, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries
  5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

1. **Plan-file path** — written to `C:\claude-runs\t3ali-task3-plugin-opus48high-015\repo\paypal-plan.md`
   as dictated by the brief.
2. **Environment** — this SDK exposes **only** `ServerEnvironment.Sandbox` (no Live/Production member).
   Production hosting, if ever needed, must be done by overriding `options.Server.Default.Sandbox.BaseUrl`
   to the live host — there is no separate Live environment enum. Assumed acceptable since the merchant is
   a sandbox account.
3. **Currency** — assumed supplied from app config and injected into every `Money`/`AmountWithBreakdown`
   `CurrencyCode`. `Value` is a decimal string; the integration must format the order total as a string.
4. **Direct raw card / PCI** — passing raw PAN/CVC (`CardRequest.Number/SecurityCode`,
   `PaymentTokenRequestCard`) requires PCI SAQ-D scope (SDK doc-comment on `CardRequest`). Assumed the
   merchant account and app are cleared for direct card processing (brief states so).
5. **UNVERIFIED — browser-challenge / 3DS detection strings.** The map/source confirm the *fields*
   (`OrderStatus.PayerActionRequired`, `CardResponse.AuthenticationResult`, `.Links[].Rel`), but the exact
   live-wire `rel` value that flags a required payer approval can only be confirmed against live sandbox
   traffic. **Directive:** treat any of {`Status == PayerActionRequired`, a `Links` entry whose `Rel`
   contains "payer-action"/"approve", a non-null
   `authentication_result.three_d_secure` with a pending/challenge status} as a STOP-and-report signal;
   extract the link href best-effort and fall back to reporting the raw status. Do not assume the card
   payment completed.
6. **UNVERIFIED — idempotency-key replay behaviour.** How a duplicate `payPalRequestId` (PayPal-Request-Id)
   replay surfaces (original response returned, possibly with a duplicate indicator, vs a 4xx) is live-wire
   behaviour not settleable from map/source. **Directive:** on a write, always send a stable
   `payPalRequestId`; on retry reuse the SAME key; in the error boundary, extract the status/`Error` fields
   best-effort and, if the body cannot be parsed as the typed error, fall back to the generic message
   rather than assuming a specific replay shape.
7. **UNVERIFIED — reauthorize "cannot renew" signal.** The map/source confirm the failure is a typed
   `SdkException<ReauthorizePaymentError>` with `Error.Details[].Issue`, but the exact `issue` string for an
   expired/non-renewable authorization is live-wire. **Directive:** surface `Error.Name` +
   `Error.Details[].Issue`/`Description` verbatim to the operator; do not branch on a hardcoded issue code.
8. **No gaps in SDK coverage** — every capability 1–10 is covered by the SDK map (capability 1's base-URL/
   auth internals were confirmed from SDK source). No capability is missing from the SDK surface.
