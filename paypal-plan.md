# PayPal .NET SDK integration plan — eShopOnWeb `PublicApi` (headless direct-card + vault, SANDBOX)

**SDK:** `AsadAli.Checkout.Sdk` (install version-less) · root namespace `PayPalServerSdk` · client `PayPalServerSdkClient` · map release tag `v1.0.1` / source commit `9653d18`.
**Scope of this integration:** direct raw-card payments only, **no browser / no redirect approval step**. All contracts below are grounded in the bundled SDK map (`sdk-map.md` + `map/operations/*` + `map/models/*`); the base-URL/OAuth-token and auth-credential facts are grounded in the pinned SDK source where the map was silent.

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 0 | Client init + auth + optional base-URL override (DI-register the SDK client) | `AddPayPalServerSdkClient` / `new PayPalServerSdkClient` |
| 1 | Authorize an order total against a raw card (funds HELD) | `Orders.CreateOrder` → `Orders.AuthorizeOrder` |
| 2 | Capture an authorization at fulfilment | `Payments.CaptureAuthorizedPayment` |
| 3 | Re-authorize a stale/expired authorization | `Payments.ReauthorizePayment` |
| 4 | Void an authorization before fulfilment | `Payments.VoidPayment` |
| 5 | Refund a captured payment (full/partial); read refunded-so-far | `Payments.RefundCapturedPayment` (+ `Payments.GetCapturedPayment` / `Payments.GetRefund`) |
| 6 | Vault a card, pay later with vault id, delete vaulted card | `Vault.CreatePaymentToken`; then step-1 ops with `vault_id`; `Vault.DeletePaymentToken` |
| 7 | Transaction reporting for reconciliation (paged over date range) | `TransactionSearch.SearchTransactions` |

Config keys to bind (never hard-code values): `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, `PayPal:BaseUrl` (optional override).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.0 `using` directives (namespace by type — confirmed from map + source)

| Type(s) | Namespace |
|---|---|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` | `PayPalServerSdk` |
| `AddPayPalServerSdkClient` (DI extension) | `PayPalServerSdk` (`ServiceCollectionExtensions.cs`) |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `ServerEnvironment`, `DefaultOptions` (+ nested `DefaultOptions.SandboxOptions`) | `PayPalServerSdk.Servers` |
| Controllers (`Orders`, `Payments`, `Vault`, `TransactionSearch`) — reached as `client.Orders` etc.; the controller *types* | `PayPalServerSdk.Api` |
| All request/response records (`OrderRequest`, `CardRequest`, `Money`, `CapturedPayment`, `Refund`, `PaymentTokenRequest`, `SearchResponse`, `Error`, `Error1`, `DefaultError`, `SearchError`, …) | `PayPalServerSdk.Models` |
| All enums (`CheckoutPaymentIntent`, `OrderStatus`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus`, `CardBrand`, …) | `PayPalServerSdk.Models.Enums` |
| Per-operation typed error classes (`AuthorizeOrderError`, `CaptureAuthorizedPaymentError`, …) | `PayPalServerSdk.Errors` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` |

### 2.1 Step 0 — Client construction, auth, environment, base-URL override

`PayPalServerSdkClientOptions` properties (`sdk-map.md`): `Environment: ServerEnvironment`, `Oauth2: OAuth2ClientCredentials?`, `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`, `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`.
Constructor: `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`. DI: `services.AddPayPalServerSdkClient(o => { ... })`.

- **Credentials (contract fact, source `OAuth2ClientCredentials.cs`):** `Oauth2 = new OAuth2ClientCredentials { ClientId = <PayPal:ClientId>, ClientSecret = <PayPal:ClientSecret> }`. Both `ClientId` and `ClientSecret` are `required`; `Scope` is optional (`string?`). Do the *wiring* per `dotnet-authentication` (where to set them — options vs DI callback — and secret loading).
- **Environment:** `Environment = ServerEnvironment.Sandbox`. `ServerEnvironment` (source `Servers/ServerEnvironment.cs`) has exactly one member: **`Sandbox`** (`ServerEnvironment.Default()` == `Sandbox`). Bind `PayPal:Environment` but the only valid sandbox value maps to `ServerEnvironment.Sandbox`; there is no `Live`/`Production` member in this SDK build — treat any non-sandbox config value as a config error, not a new environment.
- **Base-URL override (contract fact, source `ServerOptions.cs` + `Servers/DefaultOptions.cs` + `AuthSchemes.cs`):**
  ```
  options.Server = new ServerOptions {
      Default = new DefaultOptions {
          Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = <PayPal:BaseUrl> }
      }
  };
  ```
  `DefaultOptions.SandboxOptions.BaseUrl` defaults to `https://api-m.sandbox.paypal.com` when `PayPal:BaseUrl` is unset. **When `PayPal:BaseUrl` is set it is used verbatim for EVERY call including the OAuth/token request** — the token endpoint is resolved through the same server path as every operation: `AuthSchemes` builds the token strategy against `server.Default("/v1/oauth2/token")`, and `server.Default(...)` resolves to `DefaultOptions.Sandbox.BaseUrl`. So overriding that one `BaseUrl` re-points both the token request and all API calls; there is no separate OAuth host to set. Only set the override when the config key is present (otherwise leave the SDK default).

### 2.2 Operation rows

Column key — Signature params are listed **in order**; `= x` marks a compile-time default (may omit); everything else must be passed explicitly (pass `null` to skip a nullable-no-default param). `ct:` is the cancellation token.

#### Step 1a — `client.Orders.CreateOrder` → `Order`  · map `operations/Orders.md`
- **Signature:** `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Idempotency:** pass the caller's key as `payPalRequestId:` (→ `PayPal-Request-Id` header). Same key on a double-click ⇒ PayPal returns the same order, never a second one.
- **Request `OrderRequest`** (`records-1`): `Intent (intent): CheckoutPaymentIntent !req`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `Payer (payer): Payer?`, `ApplicationContext (application_context): OrderApplicationContext?`.
  - `Intent = CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`).
  - `PurchaseUnitRequest` (`records-2`): `Amount (amount): AmountWithBreakdown !req` (+ optional `ReferenceId`, `InvoiceId`, `CustomId`, `Description`, `Items`, …).
  - `AmountWithBreakdown` (`records-1`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?`. **`Value` is a decimal-as-string** — format the eShop order total to the currency's minor units (2dp for USD/EUR) as an invariant string, e.g. `total.ToString("F2", CultureInfo.InvariantCulture)`. `CurrencyCode` = `PayPal:Currency`.
  - **Raw card inline** — `PaymentSource (payment_source): PaymentSource?` → `PaymentSource.Card (card): CardRequest?`. `CardRequest` (`records-1`): `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?` (YYYY-MM), `SecurityCode (security_code): string?` (CVC), `BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?`, `Attributes (attributes): CardAttributes?`, `ExperienceContext (experience_context): CardExperienceContext?`. Test Visa: `Number = "4111111111111111"`, any future `Expiry`, any `SecurityCode`.
  - `Address` (`records-1`): `AddressLine1 (address_line_1): string?`, `AddressLine2 (address_line_2): string?`, `AdminArea2 (admin_area_2): string?` (city), `AdminArea1 (admin_area_1): string?` (state), `PostalCode (postal_code): string?`, `CountryCode (country_code): string !req`.
- **Response `Order`** (`records-1`): `Id (id): string?` (the PayPal order id), `Status (status): OrderStatus?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `PaymentSource (payment_source): PaymentSourceResponse?`, `Links (links): IReadOnlyList<LinkDescription>?`.
- **Error:** Case A `SdkException<CreateOrderError>` — `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback]. `Error` (`records-1`): `Name`, `Message`, `DebugId`, `Details: IReadOnlyList<ErrorDetails>?`, `Links`. `ErrorDetails`: `Field`, `Value`, `Location`, `Issue !req`, `Description`.

#### Step 1b — `client.Orders.AuthorizeOrder` → `OrderAuthorizeResponse`  · map `operations/Orders.md`
- **Signature:** `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- `id` = the order id from Step 1a. **Idempotency:** `payPalRequestId:` (same key ⇒ no double authorize).
- **Request `OrderAuthorizeRequest`** (`records-1`): `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`. `OrderAuthorizeRequestPaymentSource`: `Card (card): CardRequest?`, `Token (token): Token?`, `Paypal`, `ApplePay`, `GooglePay`, `Venmo`. For a raw-card authorize where the card was already supplied at CreateOrder, `body` may be `null`; to supply the card at authorize instead, set `PaymentSource.Card` (same `CardRequest` shape as above).
- **Response `OrderAuthorizeResponse`** (`records-1`): `Id (id): string?` (order id), `Status (status): OrderStatus?`, `PaymentSource (payment_source): OrderAuthorizeResponsePaymentSource?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `Links (links): IReadOnlyList<LinkDescription>?`.
  - **Authorization id + status** live at: `PurchaseUnits[i].Payments (payments): PaymentCollection?` → `PaymentCollection.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → each has `Id (id): string?` (**authorization id**) and `Status (status): AuthorizationStatus?` (**authorization status**), plus `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?` (honor-period expiry), `ProcessorResponse`. (`PurchaseUnit`, `PaymentCollection`, `AuthorizationWithAdditionalData` on `records-2`/`records-1`.)
- **CHALLENGE / 3DS / browser-approval detection (STOP condition):**
  - Contract fact: `OrderStatus` (enum) member **`PayerActionRequired` (wire `PAYER_ACTION_REQUIRED`)`**. If the returned `Status == OrderStatus.PayerActionRequired`, the flow needs payer/browser approval — **STOP and report; do not build an approval round-trip.**
  - 3DS result surfaces on `OrderAuthorizeResponsePaymentSource.Card (card): CardResponse?` → `AuthenticationResult (authentication_result): AuthenticationResponse?` → `LiabilityShift (liability_shift): LiabilityShiftIndicator?` and `ThreeDSecure (three_d_secure): ThreeDSecureAuthenticationResponse?` (`AuthenticationStatus: ParesStatus?`, `EnrollmentStatus: EnrollmentStatus?`).
  - `Links` may carry a HATEOAS link whose `Rel` is `payer-action`/`approve` (`LinkDescription.Rel (rel): string !req`).
  - **`UNVERIFIED` (live-wire):** exactly which of these the sandbox returns for a card that requires a challenge (status `PAYER_ACTION_REQUIRED` vs. a `payer-action` link vs. an empty `authorizations` list with `LiabilityShift`) is not fixed by the contract. **Defensive directive:** treat the authorize as "needs approval → STOP & report" if ANY of: `Status == OrderStatus.PayerActionRequired`; the `authorizations` collection is null/empty; or a `Links` entry has `Rel` equal to `payer-action` or `approve`. Only proceed to capture when an authorization with a non-terminal-failure `Status` is present.
- **Error:** Case A `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback].

> **Flow note (`UNVERIFIED`, live-wire):** whether supplying `payment_source.card` on `CreateOrder` with `intent=AUTHORIZE` already produces the authorization inline (making the separate `AuthorizeOrder` call unnecessary or an error because the order is already `COMPLETED`), versus requiring the explicit `AuthorizeOrder` call, cannot be settled from the contract. **Defensive directive:** after `CreateOrder`, inspect `Order.Status` and `Order.PurchaseUnits[].Payments.Authorizations`; if an authorization is already present, read it directly and skip `AuthorizeOrder`; otherwise call `AuthorizeOrder`. Read the authorization from whichever response returned it (`Order` and `OrderAuthorizeResponse` expose the identical `PurchaseUnits[].Payments.Authorizations` path).

#### Step 2 — `client.Payments.CaptureAuthorizedPayment` → `CapturedPayment`  · map `operations/Payments.md`
- **Signature:** `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- `authorizationId` from Step 1b. **Idempotency:** `payPalRequestId:` (same key ⇒ no double capture).
- **Request `CaptureRequest`** (`records-1`, all optional): `Amount (amount): Money?`, `InvoiceId (invoice_id): string?`, `FinalCapture (final_capture): bool? = false`, `PaymentInstruction`, `NoteToPayer`, `SoftDescriptor`. For a full take-the-money capture, pass `body: null` (or `FinalCapture = true`). `Money` = `CurrencyCode (currency_code): string !req`, `Value (value): string !req`.
- **Response `CapturedPayment`** (`records-1`): `Id (id): string?` (**capture id**), `Status (status): CaptureStatus?`, `Amount (amount): Money?` (**captured amount**), `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`, `FinalCapture`, `ProcessorResponse`.
  - **Fee / net proceeds** — `SellerReceivableBreakdown` (`records-2`): `GrossAmount (gross_amount): Money !req` (captured gross), `PaypalFee (paypal_fee): Money?` (**PayPal fee**), `NetAmount (net_amount): Money?` (**net proceeds to merchant**), `ReceivableAmount (receivable_amount): Money?`, `ExchangeRate`, `PlatformFees`.
- **Error:** Case A `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].

#### Step 3 — `client.Payments.ReauthorizePayment` → `PaymentAuthorization`  · map `operations/Payments.md`
- **Signature:** `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Idempotency:** `payPalRequestId:`.
- **Request `ReauthorizeRequest`** (`records-2`): `Amount (amount): Money?` only (per the op notes, only `amount` is supported; re-auth amount is capped relative to the original).
- **Response `PaymentAuthorization`** (`records-2`): `Id (id): string?`, `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?` (new honor-period expiry), `StatusDetails (status_details): AuthorizationStatusDetails?`.
- **"Can no longer be re-authorized → operator must act":** Case A `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]. A 422 / unprocessable is the "give up" class: the original authorization is past the 29-day re-auth window (op notes: after 30 days you must create a fresh authorized payment, not reauthorize).
  - **`UNVERIFIED` (live-wire):** the exact `ErrorDetails.Issue` string PayPal returns for the terminal "cannot reauthorize" case is not fixed by the contract. **Defensive directive:** on `TryGetError`, read `Error.Details[].Issue` (and `Error.Message`) best-effort; treat a 422 (or an issue naming the honor/authorization window) as terminal — **stop retrying and surface for operator action**; fall back to the generic `Error.Message` when no specific issue is present.

#### Step 4 — `client.Payments.VoidPayment` → `PaymentAuthorization`  · map `operations/Payments.md`
- **Signature:** `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — **no request body.** (Note the param order here differs from other Payments ops: `payPalRequestId` is the 4th param, after `payPalAuthAssertion`.)
- **Idempotency:** `payPalRequestId:`.
- **Response `PaymentAuthorization`**: read `Status (status): AuthorizationStatus?` → expect `AuthorizationStatus.Voided` (wire `VOIDED`). (PayPal may return 204/empty; read status defensively — see error-handling reading below.)
- **Error:** Case A `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]. A `409` means it cannot be voided (e.g. already fully captured — op notes: "You cannot void an authorized payment that has been fully captured").

#### Step 5 — `client.Payments.RefundCapturedPayment` → `Refund`  · map `operations/Payments.md`
- **Signature:** `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- `captureId` from Step 2. **Idempotency:** caller-supplied key as `payPalRequestId:` — repeating under the SAME key does not refund twice. **For two DISTINCT partial refunds of the same capture, use two DIFFERENT keys** (same key would collapse them into one).
- **Request `RefundRequest`** (`records-2`, all optional): `Amount (amount): Money?`, `CustomId`, `InvoiceId`, `NoteToPayer`, `PaymentInstruction`. **Full refund** ⇒ `body: null` (empty payload). **Partial refund** ⇒ `body = new RefundRequest { Amount = new Money { CurrencyCode = ..., Value = "x.xx" } }`.
- **Response `Refund`** (`records-2`): `Id (id): string?` (**refund id**), `Status (status): RefundStatus?` (**refund status**), `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?`, `CreateTime`, `UpdateTime`.
- **Refunded-so-far / remaining refundable (contract fact — model exposes no single "refundable_remaining" field; compute it):**
  - `SellerPayableBreakdown` (`records-2`) carries `TotalRefundedAmount (total_refunded_amount): Money?` (cumulative refunded on the capture), `GrossAmount`, `PaypalFee`, `NetAmount`.
  - To guard a partial refund against exceeding the capture: read the capture via `client.Payments.GetCapturedPayment(captureId, payPalMockResponse: null, requestOptions: null, ct:)` → `CapturedPayment.Amount` (captured gross) and `CapturedPayment.Status` (`PartiallyRefunded`/`Refunded` signal prior refunds); and/or read a prior refund via `client.Payments.GetRefund(refundId, payPalMockResponse:null, payPalAuthAssertion:null, ...)` → `Refund.SellerPayableBreakdown.TotalRefundedAmount`. **Remaining = captured `Amount.Value` − `TotalRefundedAmount.Value`** (decimal-string math, invariant culture). Reject a partial-refund request that would exceed the remaining before calling PayPal.
- **Error:** Case A `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]. A `422`/`409` is PayPal rejecting an over-refund or an already-fully-refunded capture.

#### Step 6a — `client.Vault.CreatePaymentToken` → `PaymentTokenResponse`  · map `operations/Vault.md`
- **Signature:** `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Idempotency:** `payPalRequestId:`.
- **Request `PaymentTokenRequest`** (`records-2`): `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.
  - `PaymentTokenRequestPaymentSource` (`records-2`): `Card (card): PaymentTokenRequestCard?`, `Token (token): VaultTokenRequest?`.
  - `PaymentTokenRequestCard` (`records-2`): `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`. Vaults the raw card directly (no browser). (`Customer.Id`/`MerchantCustomerId` optional to associate with an eShop customer.)
- **Response `PaymentTokenResponse`** (`records-2`): `Id (id): string?` (**vault id / token — store THIS, never the raw PAN**), `Customer (customer): CustomerResponse?`, `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?`, `Links`.
  - Safe card descriptor: `PaymentTokenResponsePaymentSource.Card (card): CardPaymentTokenEntity?` → `Brand (brand): CardBrand?`, `LastDigits (last_digits): string?`, `Expiry (expiry): string?`, `Name (name): string?`, `BillingAddress`. Store `{ vaultId=Id, brand=Brand, last4=LastDigits, expiry=Expiry }` only.
- **Error:** Case A `SdkException<CreatePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]. (Note the accessor is **`TryGetError1`** and the payload type is **`Error1`** — `records-1`: `Name`, `Message`, `DebugId`, `Details: IReadOnlyList<ErrorDetails1>?`, `Links: IReadOnlyList<ErrorLinkDescription>?` — different from the `Error`/`ErrorDetails` used by Orders/Payments.)

#### Step 6b — Pay a later order with the saved vault id (no raw card)
- Reuse **Step 1** ops. Set the card's `VaultId` instead of PAN: on `CreateOrder`, `OrderRequest.PaymentSource.Card = new CardRequest { VaultId = <saved vault id> }` (leave `Number`/`Expiry`/`SecurityCode` null); or on `AuthorizeOrder`, `OrderAuthorizeRequest.PaymentSource.Card = new CardRequest { VaultId = ... }`. `CardRequest.VaultId (vault_id): string?` is the reference field (`records-1`). Same authorize/capture contracts as steps 1–2 apply.

#### Step 6c — `client.Vault.DeletePaymentToken` → `void`  · map `operations/Vault.md`
- **Signature:** `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`. `id` = the vault id. Returns `void` (Task) — a non-throwing call means the token is deleted; there is no body to read.
- **Error:** Case A `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback].

#### Step 7 — `client.TransactionSearch.SearchTransactions` → `SearchResponse`  · map `operations/TransactionSearch.md`
- **Signature:** `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Call with named arguments** — 8 middle params (`transactionId` … `terminalId`) are nullable-no-default and must be passed (`null` to skip). `startDate`/`endDate` are **ISO-8601** strings (wire `start_date`/`end_date`), e.g. `2026-08-01T00:00:00-0000`. Keep `fields` at its default `"transaction_info"` (or include it) so `transaction_info` is populated.
- **Response `SearchResponse`** (`records-2`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `StartDate`, `EndDate`, `LastRefreshedDatetime`, `Links`.
  - Per-transaction: `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?` (`records-2`) → `TransactionId (transaction_id): string?` (**id**), `TransactionAmount (transaction_amount): Money?` (**amount**), `TransactionStatus (transaction_status): string?` (**status — plain string, not an enum**), `TransactionInitiationDate (transaction_initiation_date): string?` / `TransactionUpdatedDate (transaction_updated_date): string?` (**date**).
- **Pagination — cover the WHOLE range (manual; there is no auto-pager):** call once with `page: 1`, read `TotalPages` from the response, then loop `page = 2 … TotalPages` re-issuing the same `startDate`/`endDate`/`pageSize`, accumulating `TransactionDetails`. `pageSize` default 100 (raise as needed). The map row states pagination is manual (`page` only, no `perPage`/pager helper) — walk it yourself.
- **Error — THIS IS THE ONE Case B OPERATION:** `SdkException<RawError>` (**not** a typed `{Op}Error`; there are **no `TryGet…` typed accessors**). Read via `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, or `ex.Error.ReadAsJson<SearchError>()` (`SearchError` on `records-2`: `Name`, `Message`, `DebugId`, `Details: IReadOnlyList<TransactionSearchErrorDetails>?`, `TotalItems`, `MaximumItems`). Do **not** write a `catch (SdkException<SearchTransactionsError>)` — no such typed error exists for this op.
- **Sandbox reporting lag is expected:** executed transactions take up to ~3 hours to appear (op notes). Reconciliation must tolerate recently-created eShop orders not yet being in the report.

### 2.3 Enum value tables (only those this scope touches)

| Enum (`PayPalServerSdk.Models.Enums`) | Members used — `CSharpName (wire)` |
|---|---|
| `CheckoutPaymentIntent` | `Authorize (AUTHORIZE)`, `Capture (CAPTURE)` |
| `OrderStatus` | `Created (CREATED)`, `Approved (APPROVED)`, `Completed (COMPLETED)`, `Voided (VOIDED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Saved (SAVED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)`, `Denied (DENIED)` |
| `AuthorizationIncompleteReason` (on `AuthorizationStatusDetails.Reason`) | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Pending (PENDING)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Completed (COMPLETED)`, `Pending (PENDING)`, `Cancelled (CANCELLED)`, `Failed (FAILED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, … (`Unknown (UNKNOWN)`) |
| `LiabilityShiftIndicator` (3DS) | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `EnrollmentStatus` / `ParesStatus` (3DS result) | `EnrollmentStatus`: `Y/N/U/B`; `ParesStatus`: `Y/N/U/A/C/R/D/I` |

Enums are `StringEnum<T>`, **not** C# enums — write the static member (`CheckoutPaymentIntent.Authorize`) or `CheckoutPaymentIntent.FromValue("AUTHORIZE")`; never `.AUTHORIZE`.

---

## 3. Trap notes (load the named skill before writing that step)

- ⚠ **Step 0 (client registration + HttpClient lifetime).** The `HttpClient`/handler passed to `PayPalServerSdkClient` must be long-lived and shared via `IHttpClientFactory`, not rebuilt per request; the SDK wrapper's lifetime is a separate decision. **MUST load `dotnet-client-initialization`** before wiring the client/DI.
- ⚠ **Step 0 (auth credential wiring).** *Where* and *when* the `Oauth2` credentials must be set (options object vs. DI callback, before client construction), and how the token is fetched/refreshed/cached, is not what the property type shows. **MUST load `dotnet-authentication`** before wiring credentials.
- ⚠ **Step 0 (retries/timeout & base-URL semantics).** The SDK's `RetryOptions.Timeout` and retry triggers do **not** bound a whole call and are not the `HttpClient` timeout; and what actually retries on a write is not visible from the option names — this matters because writes here (authorize/capture/refund) are non-idempotent. **MUST load `dotnet-configuration-resilience`** before tuning the client. (The base-URL override *mechanism* is resolved in §2.1; the resilience semantics are not.)
- ⚠ **Every write step (1–6) — idempotency vs. transport retries.** Whether a `POST` that failed at the transport layer can be silently re-sent by the retry pipeline (executing a second authorize/capture/refund) is not shown by the signature. The `PayPal-Request-Id` key protects against this, but the retry behaviour itself must be understood. **MUST load `dotnet-configuration-resilience`.**
- ⚠ **Every step — building request models / reading responses.** Enums are `StringEnum<T>` (not C# enums), `Money.Value` is a decimal-as-string, and unmodeled JSON fields are dropped on deserialize; response payloads nest one+ levels (`PurchaseUnits[].Payments.Authorizations[]`). **MUST load `dotnet-models`** before constructing payloads or mapping SDK models onto eShop domain types.
- ⚠ **Every catch (esp. Step 7).** 39 ops are Case A typed (`TryGetError`/`TryGetError1` + `TryGetRawError`) but `SearchTransactions` is Case B (`SdkException<RawError>`, no typed accessors); `TryGetRawError` is not a catch-all substitute for the status-specific accessor; there is no no-throw `…Result` variant. **MUST load `dotnet-error-handling`** before writing any try/catch. (See REQUIRED READING for the two mandatory `JsonException` hazards.)
- ⚠ **Testing.** The `HttpClient` constructor argument is the fake/stub seam; match eShopOnWeb's existing test framework and assertion style. **MUST load `dotnet-testing`** before writing integration tests.

---

## 4. REQUIRED READING (load ALL of these BEFORE implementation starts — this sheet deliberately does not carry their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, options/builder shape, HttpClient ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 0 — supplying/refreshing the `Oauth2` client-credentials, secret loading |
| `dotnet-configuration-resilience` | Step 0 + all writes — retries/backoff, what `Timeout` bounds, base-URL/server tuning, pagination behaviour |
| `dotnet-calling-endpoints` | Steps 1–7 — invoking operations, named-argument binding for optional params, async/cancellation |
| `dotnet-models` | Steps 1–7 — building request models, required/nullable members, `StringEnum<T>`, JSON wire vs C# names |
| `dotnet-error-handling` | Steps 1–7 — the error/exception boundary (mandatory; every integration writes one) |
| `dotnet-testing` | Test step — faking the SDK at the `HttpClient` seam |

**Mandatory `System.Text.Json.JsonException` boundary hazards (write the error boundary with BOTH in mind):**
- A drifted or malformed **2xx** body (e.g. a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- Plan file written to the exact path dictated by the brief: `C:\claude-runs\t3v7ali-task3-plugin-opus48high-012\repo\paypal-plan.md`.
- The eShopOnWeb `PublicApi` project is the call site; the integration is server-side/headless with no PayPal JS SDK or redirect. All approval-required outcomes are STOP-and-report, never a round-trip.
- `PayPal:Environment` resolves to the SDK's only environment member, `ServerEnvironment.Sandbox`; this SDK build exposes no Live/Production environment. If go-live is later required, that is a blocker to raise then (the map/source document Sandbox only at tag `v1.0.1`).
- `PayPal:Currency` is a valid ISO-4217 code accepted by the business account; amounts are formatted to that currency's minor units as invariant decimal strings.
- The sandbox business account has advanced/direct card processing AND vaulting enabled (as stated in the brief) — required for raw-PAN `CardRequest` and `Vault.CreatePaymentToken` with a card source to succeed.

**Blockers**
- None blocking planning. Two items are labelled **`UNVERIFIED` (live-wire only)** in the sheet and are handled by defensive-coding directives, not open questions: (a) whether providing the card on `CreateOrder` already authorizes inline vs. requiring the explicit `AuthorizeOrder` call, and the exact challenge signal (`PAYER_ACTION_REQUIRED` status vs. `payer-action` link vs. empty authorizations); (b) the exact `ErrorDetails.Issue` string for the terminal "cannot reauthorize" case. Both are resolved defensively (inspect status/collection/links; treat 422 as terminal; fall back to the generic message).
