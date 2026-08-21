# PayPal .NET SDK — Integration Plan & Contract Sheet (eShopOnWeb `src/PublicApi`)

SDK: `AsadAli.Checkout.Sdk` · root namespace `PayPalServerSdk` · client `PayPalServerSdkClient`
· map release tag `v1.0.1` (source stamp `9653d18`) · **target = PayPal Sandbox**.
Grounded against the bundled SDK map (`sdk-map.md`, `map/operations/*`, `map/models/*`); the four
client-config/auth facts marked *(source)* were confirmed from the named SDK source files because the
map does not carry their shapes.

Install version-less into `src/PublicApi`: `dotnet add package AsadAli.Checkout.Sdk`.

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 0 | Client + DI registration, Sandbox env, OAuth2 creds, optional `PayPal:BaseUrl` override | `AddPayPalServerSdkClient` / `new PayPalServerSdkClient` |
| 1 | Create order, intent AUTHORIZE, direct card payment_source | `client.Orders.CreateOrder` |
| 2 | Authorize the created order (hold, no capture) | `client.Orders.AuthorizeOrder` |
| 3 | Capture the authorization at fulfilment | `client.Payments.CaptureAuthorizedPayment` |
| 4 | Re-authorize a stale authorization | `client.Payments.ReauthorizePayment` |
| 5 | Void an authorization | `client.Payments.VoidPayment` |
| 6 | Refund a captured payment (full/partial, idempotent) | `client.Payments.RefundCapturedPayment` |
| 7 | Idempotency header on create/authorize/capture/refund | `payPalRequestId` param (per op) |
| 8 | Read state by id | `Orders.GetOrder`, `Payments.GetAuthorizedPayment`, `Payments.GetCapturedPayment`, `Payments.GetRefund` |
| 9 | Vault a card (setup-token → payment-token, OR store-in-vault on order) | `client.Vault.CreateSetupToken`, `client.Vault.CreatePaymentToken` |
| 10 | Pay with a vaulted card token | `client.Orders.CreateOrder` (payment_source.card.vault_id) |
| 11 | Delete a vaulted token | `client.Vault.DeletePaymentToken` |
| 12 | Reconciliation: transaction search over a date range, full pagination | `client.TransactionSearch.SearchTransactions` |

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

### 2a. Namespaces (add a separate `using` per type kind — child namespaces are NOT transitive)

| Contents | Namespace |
|---|---|
| Client, `PayPalServerSdkClientOptions`, `ServerOptions` | `PayPalServerSdk` |
| Controllers (`client.Orders` etc.) | `PayPalServerSdk.Api` |
| Records (all request/response models below) | `PayPalServerSdk.Models` |
| Enums (`CheckoutPaymentIntent`, `OrderStatus`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus`, `PaymentTokenStatus`, `StoreInVaultInstruction`, `VaultTokenRequestType`, `CardBrand` …) | `PayPalServerSdk.Models.Enums` |
| Typed error classes (`CreateOrderError`, `AuthorizeOrderError`, `CaptureAuthorizedPaymentError`, `RefundCapturedPaymentError`, `CreatePaymentTokenError`, …) | `PayPalServerSdk.Errors` |
| `SdkException<TError>` | `PayPalServerSdk.Core.Exceptions` *(source: `Core/Exceptions/SdkException.cs`)* |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` *(source: `Core/ErrorResponse/`)* |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` *(source)* |
| `IOAuth2TokenStrategy<T>` | `PayPalServerSdk.Core.Authentication.OAuth2` *(source)* |
| `ServerEnvironment`, `DefaultOptions` (+ nested `SandboxOptions`) | `PayPalServerSdk.Servers` *(source)* |
| `RetryOptions`, `LoggingOptions` | `PayPalServerSdk.Core.Configuration` *(source: `Core/Configuration/`)* |

### 2b. Client construction, environment, base URL, auth *(all four confirmed from source; map carries only the property names)*

- **Client (two forms):**
  - `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` — the only public constructor (`sdk-map.md`).
  - DI: `services.AddPayPalServerSdkClient(o => { … })` (`ServiceCollectionExtensions.cs`). Prefer DI so the SDK's `HttpClient` is factory-managed — see trap ⚠0.
- **Options object** `PayPalServerSdkClientOptions` *(source `PayPalServerSdkClientOptions.cs`)*, settable properties:
  `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`,
  `Oauth2: OAuth2ClientCredentials?`, `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.
- **Environment (Sandbox):** `options.Environment = ServerEnvironment.Sandbox;` — `ServerEnvironment` is a `StringEnum`; `Sandbox` is the only member (also the default via `ServerEnvironment.Default()`) *(source `Servers/ServerEnvironment.cs`)*.
- **Base-URL override (optional `PayPal:BaseUrl`):** the URL lives at
  `options.Server.Default.Sandbox.BaseUrl` (a `string`; default `"https://api-m.sandbox.paypal.com"`).
  Verbatim: `options.Server.Default.Sandbox.BaseUrl = config["PayPal:BaseUrl"];` — only set it when the config value is non-empty; otherwise leave the default. `ServerOptions.Default` is `DefaultOptions`, whose nested `SandboxOptions.BaseUrl` is the override point *(source `ServerOptions.cs`, `Servers/DefaultOptions.cs`)*.
- **Auth (OAuth2 client-credentials):** set
  `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = … };`
  `OAuth2ClientCredentials` is a sealed class with `required string ClientId`, `required string ClientSecret`, and optional `string? Scope` *(source `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`)*. Load id/secret from configuration (`PayPal:ClientId` / `PayPal:ClientSecret`), never hardcode. The SDK fetches/refreshes the bearer token itself via the built-in `Oauth2TokenStrategy` (leave it null to use the default).

### 2c. Error model (applies to every op; see REQUIRED READING for the mechanics)

Every op is **throw-based**; no `…Result` no-throw variant exists anywhere in this SDK.
`SdkException<TError>` exposes only `.Error` (of type `TError`) — **it carries no StatusCode of its own** *(source `Core/Exceptions/SdkException.cs`)*. Read status/body thus:

- **Case A (typed)** — `TError` is a `…Error : ApiError` in `PayPalServerSdk.Errors`. Use the op's `TryGet…(out …)` accessors (each returns `true` for its status set); the semantic status is which accessor matched. The payload record (`Error` / `Error1` / `DefaultError`, all in `.Models`) carries `Name (name)`, `Message (message)`, `DebugId (debug_id)`, and `Details (details): IReadOnlyList<ErrorDetails…>`. For the numeric HTTP status fall through to the inherited `TryGetRawError(out RawError raw)` → `raw.StatusCode`.
- **Case B (raw)** — only `SearchTransactions`. `ex.Error` is `RawError`: `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`.

### 2d. Operations table

Legend: params listed in order; `!nullable-no-default` = nullable param with no C# default → **must pass explicitly** (pass `null` to skip). `ct` is the trailing `CancellationToken`.

| # | Call | Signature (params in order) | Request model + key fields | Response envelope → fields to read | Error case + accessors → payload | Map page |
|---|---|---|---|---|---|---|
| 1 | `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — first 5 all `!nullable-no-default` | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req` = `CheckoutPaymentIntent.Authorize`; `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`; `PaymentSource (payment_source): PaymentSource?`; `Payer?`; `ApplicationContext?`. `PurchaseUnitRequest.Amount (amount): AmountWithBreakdown !req` → `AmountWithBreakdown{ CurrencyCode (currency_code): string !req, Value (value): string !req, Breakdown? }`. Direct card: `PaymentSource.Card (card): CardRequest?` → `CardRequest{ Name?, Number (number): string?, Expiry (expiry): string?, SecurityCode (security_code): string?, BillingAddress (billing_address): Address?, VaultId?, Attributes? }`. `Address{ AddressLine1?, AddressLine2?, AdminArea2?, AdminArea1?, PostalCode?, CountryCode (country_code): string !req }`. Sandbox Visa `4111111111111111`. | Returns `Order`: `Id (id): string?`, `Status (status): OrderStatus?`, `PurchaseUnits?`, `PaymentSource (payment_source): PaymentSourceResponse?`, `Links (links): IReadOnlyList<LinkDescription>?`. Read created id ← `Order.Id`; status ← `Order.Status`. | Case A `SdkException<CreateOrderError>` — `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` | `operations/Orders.md`; `records-1-Ac-Pa.md` (OrderRequest, AmountWithBreakdown, CardRequest, Address, Order); `records-2-Pa-Ve.md` (PurchaseUnitRequest, PaymentSource); `enums.md` (CheckoutPaymentIntent, OrderStatus) |
| 2 | `client.Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 5 middle params `!nullable-no-default` | `id` = order id from step 1. `body` may be `null` (order already carries the card). If sent: `OrderAuthorizeRequest{ PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource? }`. | Returns `OrderAuthorizeResponse`: `Id?`, `Status (status): OrderStatus?`, `PurchaseUnits?`. **Authorization id/status/amount live one more level down:** `PurchaseUnits[].Payments (payments): PaymentCollection` → `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>` → each has `Id (id): string?`, `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`. | Case A `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` | `operations/Orders.md`; `records-1-Ac-Pa.md` (OrderAuthorizeResponse, AuthorizationWithAdditionalData, Money); `records-2-Pa-Ve.md` (PurchaseUnit, PaymentCollection); `enums.md` (AuthorizationStatus) |
| 3 | `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 middle params `!nullable-no-default` | `authorizationId` from step 2. `CaptureRequest{ Amount (amount): Money?, InvoiceId?, FinalCapture (final_capture): bool? = false, PaymentInstruction?, NoteToPayer?, SoftDescriptor? }`. Full capture: `body = null` or empty. Partial: set `Amount`. | Returns `CapturedPayment`: `Id?`, `Status (status): CaptureStatus?`, `Amount (amount): Money?`, `FinalCapture?`, `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`. Net proceeds: `SellerReceivableBreakdown{ GrossAmount (gross_amount): Money !req, PaypalFee (paypal_fee): Money?, NetAmount (net_amount): Money?, ReceivableAmount?, ExchangeRate? }`. | Case A `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | `operations/Payments.md`; `records-1-Ac-Pa.md` (CaptureRequest, CapturedPayment, Money); `records-2-Pa-Ve.md` (SellerReceivableBreakdown); `enums.md` (CaptureStatus) |
| 4 | `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId`, `payPalAuthAssertion`, `body` all `!nullable-no-default` | `ReauthorizeRequest{ Amount (amount): Money? }` (only `amount` is honored). | Returns `PaymentAuthorization`: `Id?`, `Status (status): AuthorizationStatus?`, `Amount?`, `ExpirationTime?`. New honor period → new `ExpirationTime`. **Cannot re-authorize signal:** a non-2xx `SdkException<ReauthorizePaymentError>` (typically 422 UNPROCESSABLE — read `Error.Details[].Issue` for the operator, e.g. authorization voided/expired/already captured); also a live authorization already in a terminal state reports `AuthorizationStatus` = `Voided`/`Captured`/`Denied`. See UNVERIFIED note. | Case A `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | `operations/Payments.md`; `records-2-Pa-Ve.md` (ReauthorizeRequest, PaymentAuthorization); `enums.md` (AuthorizationStatus) |
| 5 | `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — note order: `payPalRequestId` is the **4th** param; first 3 middle all `!nullable-no-default` | none (no body). | Returns `PaymentAuthorization`. Success signal: no throw; `Status (status): AuthorizationStatus?` = `Voided (VOIDED)`. (With default `prefer=return=minimal` the body may be sparse; confirm via `GetAuthorizedPayment` — step 8 — if you need the full object.) | Case A `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | `operations/Payments.md`; `records-2-Pa-Ve.md` (PaymentAuthorization); `enums.md` (AuthorizationStatus) |
| 6 | `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 middle params `!nullable-no-default`; **pass caller idempotency key as `payPalRequestId`** | `captureId` from step 3. `RefundRequest{ Amount (amount): Money?, CustomId?, InvoiceId?, NoteToPayer?, PaymentInstruction? }`. Full refund: `body = null` / empty. Partial: set `Amount`. | Returns `Refund`: `Id (id): string?`, `Status (status): RefundStatus?`, `Amount?`, `SellerPayableBreakdown?`. Refund id ← `Refund.Id`; status ← `Refund.Status`. | Case A `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | `operations/Payments.md`; `records-2-Pa-Ve.md` (RefundRequest, Refund); `enums.md` (RefundStatus) |
| 8a | `client.Orders.GetOrder` | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `fields`,`payPalMockResponse`,`payPalAuthAssertion` `!nullable-no-default` | `id` = order id. `fields` optional filter. | Returns `Order` (see #1). | Case A `SdkException<GetOrderError>` — `TryGetError(out Error)` [401,404] · `TryGetRawError` | `operations/Orders.md` |
| 8b | `client.Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 2 middle `!nullable-no-default` | `authorizationId`. | Returns `PaymentAuthorization` (`Status`, `Amount`, `ExpirationTime`). | Case A `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | `operations/Payments.md` |
| 8c | `client.Payments.GetCapturedPayment` | `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalMockResponse` `!nullable-no-default` | `captureId`. | Returns `CapturedPayment` (see #3). | Case A `SdkException<GetCapturedPaymentError>` — `TryGetError(out Error)` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` | `operations/Payments.md` |
| 8d | `client.Payments.GetRefund` | `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 2 middle `!nullable-no-default` | `refundId`. | Returns `Refund` (see #6). | Case A `SdkException<GetRefundError>` — `TryGetError(out Error)` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` | `operations/Payments.md` |
| 9a | `client.Vault.CreateSetupToken` | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` `!nullable-no-default` | `SetupTokenRequest{ Customer (customer): Customer?, PaymentSource (payment_source): SetupTokenRequestPaymentSource !req }`. Card: `SetupTokenRequestPaymentSource.Card (card): SetupTokenRequestCard?` → `{ Name?, Number?, Expiry?, SecurityCode?, Brand?, BillingAddress?, VerificationMethod?, ExperienceContext? }`. | Returns `SetupTokenResponse`: `Id (id): string?` (the setup-token id, feed to 9b), `Status (status): PaymentTokenStatus?`, `PaymentSource?`. | Case A `SdkException<CreateSetupTokenError>` — `TryGetError1(out Error1)` [400,403,422,500] · `TryGetRawError` | `operations/Vault.md`; `records-2-Pa-Ve.md` (SetupTokenRequest, SetupTokenRequestPaymentSource, SetupTokenRequestCard, SetupTokenResponse); `enums.md` (PaymentTokenStatus) |
| 9b | `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` `!nullable-no-default` | `PaymentTokenRequest{ Customer?, PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req }`. To promote a setup token: `PaymentTokenRequestPaymentSource.Token (token): VaultTokenRequest?` → `VaultTokenRequest{ Id (id): string !req = setup-token id, Type (type): VaultTokenRequestType !req = VaultTokenRequestType.SetupToken }`. (Direct card also possible via `.Card: PaymentTokenRequestCard`.) | Returns `PaymentTokenResponse`: `Id (id): string?` = **reusable vault/token id**; `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `.Card (card): CardPaymentTokenEntity?` → **safe descriptor**: `Brand (brand): CardBrand?`, `LastDigits (last_digits): string?`, `Expiry (expiry): string?`. | Case A `SdkException<CreatePaymentTokenError>` — `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError` | `operations/Vault.md`; `records-2-Pa-Ve.md` (PaymentTokenRequest, PaymentTokenRequestPaymentSource, VaultTokenRequest, PaymentTokenResponse, PaymentTokenResponsePaymentSource); `records-1-Ac-Pa.md` (CardPaymentTokenEntity); `enums.md` (VaultTokenRequestType, CardBrand) |
| 10 | `client.Orders.CreateOrder` (vaulted) | as #1 | Reference the stored card by id: `OrderRequest.PaymentSource.Card = new CardRequest{ VaultId (vault_id) = <PaymentTokenResponse.Id> }` — no raw PAN. (`CardRequest` also has `SingleUseToken?`; the vault path is `VaultId`.) | Returns `Order` (see #1). | Case A `SdkException<CreateOrderError>` (see #1) | `operations/Orders.md`; `records-1-Ac-Pa.md` (CardRequest) |
| 11 | `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `id` = vault/token id. | Returns `void` (Task). **Confirmation = no throw** (HTTP 204). To prove absence afterwards, `GetPaymentToken(id)` should then throw `SdkException<GetPaymentTokenError>` with 404. | Case A `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` | `operations/Vault.md` |
| 12 | `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 middle params (`transactionId`…`terminalId`) `!nullable-no-default`; **call with named args** | `startDate`/`endDate` = ISO-8601 strings (wire `start_date`/`end_date`). `fields` default `"transaction_info"`; `pageSize` max 500 per PayPal (default 100); `page` 1-based. | Returns `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Links (links): IReadOnlyList<LinkDescription>?`. Per row: `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?` (`TransactionId`, `TransactionAmount`, `FeeAmount`, `TransactionStatus`, dates). | **Case B** `SdkException<RawError>` — `ex.Error.StatusCode` / `ReadAsString()` / `ReadAsJson<T>()` (NO typed accessors) | `operations/TransactionSearch.md`; `records-2-Pa-Ve.md` (SearchResponse, TransactionDetails, TransactionInformation) |

**Pagination for #12 (cover the FULL range):** the response is not auto-paged. Read `TotalPages` from page 1, then loop `page = 2 … TotalPages` re-issuing `SearchTransactions` with the same `startDate`/`endDate`/`pageSize`, accumulating `TransactionDetails`. Use `TotalItems` as the expected accumulated count for a sanity check. Prefer looping on `TotalPages` (also cross-check the `rel = "next"` entry in `Links` — `LinkDescription{ Href, Rel, Method }`). **Max window constraint:** PayPal caps a single `SearchTransactions` request to a **31-day** date window and data is available for the **previous 3 years**; a `[from,to]` wider than 31 days must be split into ≤31-day sub-ranges, each paged to completion. (The 31-day cap is a PayPal API rule, not visible in the generated signature — treat a 400/`INVALID_REQUEST` from an over-wide window as the trigger to chunk. Labeled `UNVERIFIED` below.)

### 2e. Idempotency (PayPal-Request-Id) — exact parameter per op

The `PayPal-Request-Id` header is passed as the string parameter named **`payPalRequestId`**; pass the same caller-supplied key to retry safely (PayPal returns the original result for a repeated key rather than creating a duplicate). Position differs per op:

| Op | `payPalRequestId` position |
|---|---|
| `CreateOrder` | 2nd param (after `payPalMockResponse`) |
| `AuthorizeOrder` | 3rd param |
| `CaptureAuthorizedPayment` | 3rd param |
| `ReauthorizePayment` | 2nd param |
| `RefundCapturedPayment` | 3rd param |
| `VoidPayment` | **4th** param (order: `authorizationId, payPalMockResponse, payPalAuthAssertion, payPalRequestId`) |
| `CreateSetupToken` / `CreatePaymentToken` | 1st param |

`GetOrder`, `GetAuthorizedPayment`, `GetCapturedPayment`, `GetRefund`, `DeletePaymentToken`, `SearchTransactions` have **no** `payPalRequestId` param (reads/deletes aren't idempotency-keyed). *(All from `operations/Orders.md`, `operations/Payments.md`, `operations/Vault.md`.)*

### 2f. Enum value tables (all in `PayPalServerSdk.Models.Enums`; write the C# member, not the wire value)

| Enum | Member (wire) — the ones this integration keys on |
|---|---|
| `CheckoutPaymentIntent` | `Authorize (AUTHORIZE)`, `Capture (CAPTURE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `CardBrand` (descriptor) | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, … |

**Requested status-meaning mapping:** order approved/completed → `OrderStatus.Approved` / `OrderStatus.Completed`; authorization CREATED/CAPTURED/VOIDED/PENDING → `AuthorizationStatus.Created` / `.Captured` / `.Voided` / `.Pending`. **There is no `EXPIRED` member on `AuthorizationStatus`** — an expired hold surfaces as `Voided`/`Denied` plus `AuthorizationStatusDetails.Reason` (`AuthorizationIncompleteReason`), or as a re-authorize/capture rejection (`operations/Payments.md`; `enums.md`); see Assumptions. Capture COMPLETED/DECLINED → `CaptureStatus.Completed` / `.Declined`. Refund COMPLETED → `RefundStatus.Completed`.

### 2g. 3DS / payer-action challenge signal (card path — STOP, do not build a browser round-trip)

- Primary contract signal on the created/authorized order: **`Order.Status == OrderStatus.PayerActionRequired`** (wire `PAYER_ACTION_REQUIRED`) — a generated enum member, so this check is solid.
- Companion signal: a HATEOAS entry in `Order.Links` with `Rel == "payer-action"` (`LinkDescription{ Href, Rel, Method }`) is the approval URL. The `rel` string is a free-form `string` (not a generated enum), so match it case-insensitively and treat the `Status` check as authoritative.
- Card authentication outcome (when present): `Order.PaymentSource.Card.AuthenticationResult` (`CardResponse.AuthenticationResult: AuthenticationResponse`) → `LiabilityShift (liability_shift): LiabilityShiftIndicator?` and `ThreeDSecure (three_d_secure): ThreeDSecureAuthenticationResponse?` (`AuthenticationStatus`, `EnrollmentStatus`).
- **Directive:** if `Order.Status == OrderStatus.PayerActionRequired` (or any `payer-action` link is present), STOP the flow and report "buyer approval required" to the operator — do NOT proceed to authorize/capture and do NOT attempt a browser redirect. (`records-1-Ac-Pa.md`: Order, CardResponse, AuthenticationResponse; `enums.md`: OrderStatus, LiabilityShiftIndicator.)

### 2h. Vault-on-order-creation (alternative to steps 9a/9b)

Instead of the setup→payment-token dance, a card can be stored during `CreateOrder`:
set `OrderRequest.PaymentSource.Card.Attributes (attributes): CardAttributes` →
`Vault (vault): VaultInstructionBase{ StoreInVault (store_in_vault): StoreInVaultInstruction? = StoreInVaultInstruction.OnSuccess }`.
On success the response order's `PaymentSource.Card.Attributes (attributes): CardAttributesResponse` →
`Vault (vault): CardVaultResponse{ Id (id): string? = vault id, Status (status): VaultStatus? }`. Safe descriptor still from `CardResponse` (`Brand`, `LastDigits`, `Expiry`). (`records-1-Ac-Pa.md`: CardAttributes, CardAttributesResponse, CardVaultResponse; `enums.md`: StoreInVaultInstruction, VaultStatus.) **Recommendation:** use the explicit `Vault` controller flow (9a/9b) for a "save card once, reuse later" UX; use store-in-vault-on-order when saving happens as a side effect of a real purchase.

---

## 3. Trap notes (attached to the step where each bites)

⚠ **Step 0 (client & DI registration)** — the SDK's `HttpClient`/handler pipeline must be long-lived and reused (via `IHttpClientFactory`), not rebuilt per request; the wrapper client's lifetime and the handler's lifetime are different questions the signature can't show. **MUST load `dotnet-client-initialization`** before wiring `AddPayPalServerSdkClient` / `new PayPalServerSdkClient` into eShop's container.

⚠ **Step 0 (auth wiring)** — where credentials must be set relative to client construction, and how the token is fetched/rotated/refreshed, is not visible from the `Oauth2` property alone. **MUST load `dotnet-authentication`** before setting `options.Oauth2`.

⚠ **Step 0 (base URL / resilience)** — the SDK's `Retry`/`Timeout` options do **not** bound a whole call and `RetryOptions.HttpMethodsToRetry` does not tell the whole retry story; what `Timeout` actually bounds and which failures re-send a non-idempotent POST are exactly what the option names hide. This matters because create/authorize/capture/refund are POSTs. **MUST load `dotnet-configuration-resilience`** before tuning `RetryOptions` or the base URL.

⚠ **Steps 1/9/10 (building request models)** — enums here are `StringEnum<T>` (not C# enums), unions are read via `TryGet…` not `new`, and any JSON field you don't model is dropped on deserialize; required members must be set in the object initializer. **MUST load `dotnet-models`** before constructing `OrderRequest`/`CardRequest`/`SetupTokenRequest`/`PaymentTokenRequest`.

⚠ **Steps 3/6/7 (idempotent POSTs + retries)** — whether a failed write can be silently re-sent under the SDK's retry policy, and how that interacts with your `payPalRequestId`, determines whether a capture/refund can execute twice. **MUST load `dotnet-configuration-resilience`** (retry-on-transport-failure) together with the idempotency-key wiring.

⚠ **Every step (error boundary)** — which exception types actually reach your catch blocks, why `TryGetRawError` is not a catch-all, and how Case A vs Case B differ, are all things a `try/catch` around the call cannot reveal. **MUST load `dotnet-error-handling`** before writing the boundary (see mandatory rows in REQUIRED READING).

⚠ **Step 12 (paginated search)** — the response is not auto-paged and the 31-day window / 3-year retention limits are PayPal server rules the signature doesn't encode; calling `SearchTransactions` positionally will mis-bind the 8 optional string params. **MUST load `dotnet-calling-endpoints`** (named arguments) and **`dotnet-configuration-resilience`** (list pagination) before implementing reconciliation.

⚠ **Tests** — the `HttpClient` constructor argument is the fake seam; match eShop's existing test framework. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, `HttpClient` ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 0 — supplying `OAuth2ClientCredentials`, token fetch/rotation, 401/403 |
| `dotnet-configuration-resilience` | Step 0/3/6/12 — retries, what `Timeout` bounds, base-URL selection, list pagination |
| `dotnet-calling-endpoints` | Steps 1–12 — named arguments for optional params, async/cancellation |
| `dotnet-models` | Steps 1/9/10/2h — building request models, `StringEnum`, unions, wire names |
| `dotnet-error-handling` | Every step — the error/exception boundary (mandatory; see rows below) |
| `dotnet-testing` | Tests — the fake seam, error/edge paths |

**Mandatory `System.Text.Json.JsonException` hazard rows — the exception reaches the boundary from two directions needing opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **Currency** comes from config (per brief). `AmountWithBreakdown.CurrencyCode` / `Money.CurrencyCode` are `string` (ISO-4217, e.g. `"USD"`); the SDK does not validate the code. Assumed a single currency per order.
- **Direct raw-card PAN** (`CardRequest.Number/SecurityCode/Expiry`) requires PCI SAQ-D scope on the merchant account — the SDK's own model doc flags this. This is an operational/compliance prerequisite, not an SDK gap; sandbox test PAN `4111111111111111` is fine for Sandbox.
- **`Expiry` format** for card fields is a `string` in the model; PayPal expects `YYYY-MM` — the SDK does not enforce it. Format at the mapping layer.
- **No `EXPIRED` authorization enum.** The brief asked for an authorization `EXPIRED` value; `AuthorizationStatus` has no such member (`Created/Captured/Denied/PartiallyCaptured/Voided/Pending`). Detect an expired/exhausted honor period by (a) `PaymentAuthorization.ExpirationTime` being in the past, and (b) the re-authorize/capture call throwing `SdkException<…Error>` (typically 422) whose `Error.Details[].Issue` names the reason. Surface that issue string to the operator. **`UNVERIFIED`** — the exact `issue` code string (e.g. `AUTHORIZATION_EXPIRED`) is only confirmable from live wire traffic; extract it best-effort from `Error.Details` and fall back to the generic `Error.Message`.
- **"Cannot re-authorize" signal (step 4).** No generated enum encodes it; it arrives as a non-2xx typed error. Directive: read `Error.Details[].Issue` best-effort, else fall back to `Error.Message`; treat 422 as an operator-actionable rejection, 500 (`TryGetNoContent`) as retryable. **`UNVERIFIED`** — precise issue codes are live-traffic-only.
- **`SearchTransactions` 31-day window / 3-year retention.** Stated in PayPal's public API rules and the operation's own notes ("lists transactions for the previous three years"), but the numeric 31-day cap is **not** in the generated signature. **`UNVERIFIED`** at the SDK level: implement range-chunking to ≤31 days defensively and treat a 400/`INVALID_REQUEST` on an over-wide range as the trigger to split. There is no auto-pagination helper — page manually via `TotalPages`.
- **`prefer = "return=minimal"`** is the default on create/authorize/capture/reauthorize/refund/void; response bodies may be sparse. If the integration needs full seller-receivable/authorization detail immediately, pass `prefer: "return=representation"` OR follow up with the matching `Get…` (step 8). No blocker — just a call-shape choice.
- **Live-wire payload shape** (whether the actual JSON always matches the generated response models for the fields this integration reads) is only confirmable at runtime. Directive: guard all optional reads (`?.`) and wrap the boundary per the `JsonException` rows above; never assume a nested `Payments.Authorizations[0]` exists without a null/empty check. **`UNVERIFIED`.**
- No blockers prevent implementation: every capability the brief requested is exposed by the SDK.
