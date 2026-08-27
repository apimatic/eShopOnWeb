# PayPal integration plan — eShopOnWeb `src/PublicApi` (ASP.NET Core, sandbox)

## 1. Scope & sequence

| # | Step | Operations used |
|---|------|-----------------|
| 1 | Install SDK, bind `PayPal:*` config, register client + auth + BaseUrl override in DI | — (client construction) |
| 2 | Authorize with a **raw card**: create order (`intent=AUTHORIZE`, `payment_source.card`), then authorize it; persist PayPal order id + authorization id | `Orders.CreateOrder`, `Orders.AuthorizeOrder` |
| 3 | Authorize with a **vaulted card**: same flow, `CardRequest.VaultId` instead of raw PAN | `Orders.CreateOrder`, `Orders.AuthorizeOrder` |
| 4 | **Capture** at fulfilment; read gross/fee/net from seller receivable breakdown | `Payments.CaptureAuthorizedPayment` (readback: `Payments.GetCapturedPayment`) |
| 5 | **Reauthorize** a stale authorization; detect non-renewable auths | `Payments.GetAuthorizedPayment`, `Payments.ReauthorizePayment` |
| 6 | **Void** an authorization before fulfilment | `Payments.VoidPayment` |
| 7 | **Refund** a capture (full/partial) under a caller idempotency key | `Payments.RefundCapturedPayment` (readback: `Payments.GetRefund`) |
| 8 | **Vault** a card; describe it safely (brand/last digits); unvault | `Vault.CreatePaymentToken`, `Vault.GetPaymentToken`, `Vault.DeletePaymentToken` |
| 9 | **List** a shopper's vaulted cards | `Vault.ListCustomerPaymentTokens` |
| 10 | **Transaction search** over a date range, all pages | `TransactionSearch.SearchTransactions` |
| 11 | Error boundary + 3DS/payer-action contingency detection | all of the above |

The app must persist, keyed to its own order/shopper: PayPal order id, authorization id, capture id, refund ids, vault customer id (`CustomerResponse.Id`), and vault payment-token ids.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

### SDK identity, client construction, auth, servers

| Fact | Value | Source |
|---|---|---|
| NuGet package | `AsadAli.Checkout.Sdk` — install **version-less** (`dotnet add package AsadAli.Checkout.Sdk`) | `sdk-map.md` |
| Root namespace / client / options | `PayPalServerSdk` / `PayPalServerSdkClient` / `PayPalServerSdkClientOptions` | `sdk-map.md` |
| Client ctor | `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI registration | `services.AddPayPalServerSdkClient(o => { … })` — registers the client as a **singleton** built on `IHttpClientFactory` | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| Controllers | properties on the client: `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch` | `sdk-map.md` |
| Environment | `options.Environment = ServerEnvironment.Sandbox` — `PayPalServerSdk.Servers.ServerEnvironment` has **only** the `Sandbox` member in this release | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Credentials | `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = … }` — `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials`, both properties `required string` (init-only); optional `Scope` | `PayPalServerSdkClientOptions.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs` |
| OAuth mechanics | SDK fetches `POST /v1/oauth2/token` lazily on first call (Basic auth with `clientId:clientSecret`, form body `grant_type=client_credentials`), caches the token until expiry, thread-safe; custom strategy via `options.Oauth2TokenStrategy` (`PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>`) | `AuthSchemes.cs`, `Core/Authentication/OAuth2/OAuth2Scheme.cs`, `…/OAuth2ClientCredentialsStrategy.cs` |
| **BaseUrl override** | `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>` — `ServerOptions` (root ns `PayPalServerSdk`) → `DefaultOptions` (`PayPalServerSdk.Servers`) → `SandboxOptions.BaseUrl` (default `https://api-m.sandbox.paypal.com`). This one override governs **every** call **including the OAuth token request**: the default token strategy is built as `OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), …)` and `server.Default(path)` resolves through the same `DefaultOptions.Sandbox.BaseUrl` as all API paths | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Server.cs`, `AuthSchemes.cs` |
| `RequestOptions` (last-but-one param of every op) | `PayPalServerSdk.Core.RequestOptions` — single member `LogLevel? LogLevel`; pass `null` | `Core/RequestOptions.cs` |
| Idempotency | First-class `string? payPalRequestId` parameter (wire header `PayPal-Request-Id`) on: `CreateOrder`, `AuthorizeOrder`, `CaptureOrder`, `CaptureAuthorizedPayment`, `ReauthorizePayment`, `VoidPayment`, `RefundCapturedPayment`, `CreatePaymentToken`, `CreateSetupToken`. Same key ⇒ deduped; distinct keys ⇒ distinct operations (distinct partial refunds of one capture need distinct keys) | `operations/Orders.md`, `operations/Payments.md`, `operations/Vault.md` |
| Error model | Every op throws `SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`), whose **only** member is `.Error`. Case A (39 of 40 ops): typed `{Op}Error : ApiError` (`PayPalServerSdk.Errors`) with status-mapped `TryGet…(out …)` + inherited `TryGetRawError(out RawError)` fallback. **No status on the typed path**: neither `SdkException<TError>`, `ApiError`, nor any `{Operation}Error` exposes a `StatusCode` property, and each typed error holds the typed shape **or** the raw fallback, never both (`Errors/AuthorizeOrderError.cs`, `Core/ErrorResponse/ApiError.cs`, `Core/Exceptions/SdkException.cs`) — so when several statuses share one accessor (e.g. `TryGetError` [400, 401, 403, 404, 422, 500]) the firing status is not recoverable from the exception; discriminate via the payload's `Error.Name` / `Details[].Issue` (specific strings `UNVERIFIED` — match defensively, surface verbatim). No-throw `…Result` variants: **absent across the SDK** — every operation is throw-only. Case B (only `SearchTransactions`): `SdkException<RawError>` (`PayPalServerSdk.Core.ErrorResponse`): `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | `sdk-map.md`, `Core/Exceptions/SdkException.cs`, `Core/ErrorResponse/ApiError.cs`, `Errors/AuthorizeOrderError.cs` |

`using` directives needed: `PayPalServerSdk`, `PayPalServerSdk.Models`, `PayPalServerSdk.Models.Enums`, `PayPalServerSdk.Errors`, `PayPalServerSdk.Servers`, `PayPalServerSdk.Core`, `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`, `PayPalServerSdk.Core.Exceptions`, `PayPalServerSdk.Core.ErrorResponse`.

### Operation rows

**Step 2/3 — `client.Orders.CreateOrder`** (`operations/Orders.md`)
- Signature: `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 5 nullable params before `body` have **no defaults: pass explicitly** (`null` to skip); set `payPalRequestId` for idempotency.
- Request `OrderRequest` (`records-1-Ac-Pa.md`): `Intent (intent): CheckoutPaymentIntent !req` → `CheckoutPaymentIntent.Authorize`; `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`; `PaymentSource (payment_source): PaymentSource?`.
  - `PurchaseUnitRequest` (`records-2-Pa-Ve.md`): `Amount (amount): AmountWithBreakdown !req`; optional `ReferenceId (reference_id)`, `InvoiceId (invoice_id)`, `CustomId (custom_id)`, `Description (description)`.
  - `AmountWithBreakdown` (`records-1-Ac-Pa.md`): `CurrencyCode (currency_code): string !req` (from `PayPal:Currency`), `Value (value): string !req` — **string**, format order total invariant-culture to the cent.
  - `PaymentSource` (`records-2-Pa-Ve.md`): `Card (card): CardRequest?`.
  - `CardRequest` (`records-1-Ac-Pa.md`): raw card → `Number (number)`, `Expiry (expiry)` (`"YYYY-MM"`), `SecurityCode (security_code)`, `Name (name)`, `BillingAddress (billing_address): Address?` (`Address.CountryCode (country_code): string !req`, rest optional); vaulted card → `VaultId (vault_id): string?` **instead of** PAN fields; optional `Attributes (attributes): CardAttributes?` → `Verification (verification): CardVerification?` → `Method (method): OrdersCardVerificationMethod? = ScaWhenRequired`.
- Returns `Order` (`records-1-Ac-Pa.md`): `Id (id)`, `Status (status): OrderStatus?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` → `PurchaseUnit.Payments (payments): PaymentCollection?` → `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`; `Links (links): IReadOnlyList<LinkDescription>?` (`Href !req`, `Rel !req`).
- Error: `SdkException<CreateOrderError>` — Case A. `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback.

**Step 2/3 — `client.Orders.AuthorizeOrder`** (`operations/Orders.md`)
- Signature: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 5 nullable params (`payPalMockResponse`…`body`) **must be passed explicitly**. When the order was created with `payment_source`, pass `body: null`; otherwise `OrderAuthorizeRequest.PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` with `Card (card): CardRequest?` (same `CardRequest` as above, incl. `VaultId`) or `Token (token): Token?` (`Token.Id (id): string !req`, `Token.Type (type): TokenType !req`).
- Returns `OrderAuthorizeResponse` (`records-1-Ac-Pa.md`): `Id`, `Status (status): OrderStatus?`, `PurchaseUnits` → `Payments.Authorizations` → **`AuthorizationWithAdditionalData`** (`records-1-Ac-Pa.md`): `Id (id)` ← **the authorization id to persist**, `Status (status): AuthorizationStatus?`, `StatusDetails (status_details).Reason: AuthorizationIncompleteReason?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`, `ProcessorResponse (processor_response): ProcessorResponse?` (`ResponseCode (response_code): ProcessorResponseCode?`, `AvsCode`, `CvvCode`).
- Error: `SdkException<AuthorizeOrderError>` — Case A. `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · fallback.

**Step 4 — `client.Payments.CaptureAuthorizedPayment`** (`operations/Payments.md`)
- Signature: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullable params **must be passed explicitly**; set `payPalRequestId`.
- Request `CaptureRequest` (`records-1-Ac-Pa.md`): `Amount (amount): Money?` (`Money.CurrencyCode (currency_code): string !req`, `Money.Value (value): string !req`), `InvoiceId (invoice_id)`, `FinalCapture (final_capture): bool? = false` (set `true` when capturing the full remaining amount), `NoteToPayer (note_to_payer)`.
- Returns **`CapturedPayment`** (`records-1-Ac-Pa.md`): `Id (id)` ← capture id to persist, `Status (status): CaptureStatus?`, `Amount (amount): Money?`, `FinalCapture (final_capture)`, and the settlement readback: **`SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`** (`records-2-Pa-Ve.md`) → `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?`. (Not populated while the capture is pending.)
- Error: `SdkException<CaptureAuthorizedPaymentError>` — Case A. `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · fallback. **Capture on a voided/consumed auth lands here as 409/422.**

**Step 5 — `client.Payments.GetAuthorizedPayment`** (`operations/Payments.md`)
- Signature: `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 2 nullable params **must be passed explicitly**.
- Returns **`PaymentAuthorization`** (`records-2-Pa-Ve.md`): `Id`, `Status (status): AuthorizationStatus?`, `StatusDetails.Reason`, `Amount: Money?`, `ExpirationTime (expiration_time): string?` (ISO-8601), `CreateTime (create_time)`.
- Error: `SdkException<GetAuthorizedPaymentError>` — Case A. `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · fallback.

**Step 5 — `client.Payments.ReauthorizePayment`** (`operations/Payments.md`)
- Signature: `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 nullable params **must be passed explicitly**.
- Request `ReauthorizeRequest` (`records-2-Pa-Ve.md`): `Amount (amount): Money?` — the only supported parameter.
- Returns `PaymentAuthorization` (as above; fresh 3-day honor period).
- Contract limits (from the operation's map notes): reauthorize only from day 4 to day 29 after the original authorization, only once, amount ≤ ~115% of original (max +$75 in US); at 30 days you must create a **new** authorization instead.
- Error: `SdkException<ReauthorizePaymentError>` — Case A. `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · fallback. **A non-renewable authorization surfaces as 422** — treat any 422 here as "cannot renew; re-authorize from scratch" and surface `Error.Name` + first `Details[].Issue` verbatim (specific issue strings: `UNVERIFIED` — match defensively, never parse `Message`).

**Step 6 — `client.Payments.VoidPayment`** (`operations/Payments.md`)
- Signature: `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 nullable params **must be passed explicitly** (note the order: `payPalAuthAssertion` **before** `payPalRequestId`).
- Returns `PaymentAuthorization` (expect `Status = AuthorizationStatus.Voided`). Cannot void a fully captured authorization.
- Error: `SdkException<VoidPaymentError>` — Case A. `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · fallback.

**Step 7 — `client.Payments.RefundCapturedPayment`** (`operations/Payments.md`)
- Signature: `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullable params **must be passed explicitly**. **`payPalRequestId` is the caller-supplied idempotency key**: same key ⇒ no double refund; distinct keys ⇒ distinct partial refunds of the same capture.
- Request `RefundRequest` (`records-2-Pa-Ve.md`): full refund ⇒ empty payload — pass `new RefundRequest()` (do **not** pass `body: null`; the API documents an empty JSON body for full refund and a missing body is `UNVERIFIED`); partial refund ⇒ `Amount (amount): Money?`; optional `InvoiceId (invoice_id)`, `CustomId (custom_id)`, `NoteToPayer (note_to_payer)`.
- Returns **`Refund`** (`records-2-Pa-Ve.md`): `Id (id)` ← refund id, `Status (status): RefundStatus?`, `StatusDetails (status_details).Reason: RefundIncompleteReason?`, `Amount: Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount (total_refunded_amount)`).
- Error: `SdkException<RefundCapturedPaymentError>` — Case A. `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · fallback. **Refund exceeding the captured amount ⇒ 422** (issue string `UNVERIFIED` — surface verbatim).

**Readbacks — `client.Payments.GetCapturedPayment` / `GetRefund`** (`operations/Payments.md`)
- `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `CapturedPayment`. Errors: `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500].
- `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Refund`. Errors: same shape.

**Step 8 — `client.Vault.CreatePaymentToken`** (`operations/Vault.md`)
- Signature: `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` **must be passed explicitly**.
- Request `PaymentTokenRequest` (`records-2-Pa-Ve.md`): `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` → `Card (card): PaymentTokenRequestCard?` (`Name (name)`, `Number (number)`, `Expiry (expiry)`, `SecurityCode (security_code)`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`); `Customer (customer): Customer?` → `Id (id): string?` (**PayPal** customer id if the shopper already has one), `MerchantCustomerId (merchant_customer_id): string?` (your shopper key).
- Returns **`PaymentTokenResponse`** (`records-2-Pa-Ve.md`): `Id (id)` ← vault token id to persist, `Customer (customer): CustomerResponse?` → `Id (id)` ← **PayPal customer id to persist** (needed for step 9), `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `Card (card): CardPaymentTokenEntity?` → **safe card description**: `Brand (brand): CardBrand?`, `LastDigits (last_digits): string?`, `Expiry (expiry)`, `Name (name)` — never persist PAN/CVV.
- Error: `SdkException<CreatePaymentTokenError>` — Case A. `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · fallback.

**Step 8 — `client.Vault.GetPaymentToken` / `DeletePaymentToken`** (`operations/Vault.md`)
- `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentTokenResponse`. Errors: `TryGetError1(out Error1)` [403, 404, 422, 500]. **Invalid/unknown vault token ⇒ 404.**
- `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `void`. Errors: `TryGetError1(out Error1)` [400, 403, 500].

**Step 9 — `client.Vault.ListCustomerPaymentTokens`** (`operations/Vault.md`)
- Signature: `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `customerId` is the **PayPal** customer id (wire `customer_id`) from `CustomerResponse.Id`; the app must have persisted it. No SDK auto-pager: loop `page` manually.
- Returns `CustomerVaultPaymentTokensResponse` (`records-1-Ac-Pa.md`): `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?` — loop until `page >= TotalPages`.
- Error: `SdkException<ListCustomerPaymentTokensError>` — Case A. `TryGetError1(out Error1)` [400, 403, 500] · fallback.

**Step 10 — `client.TransactionSearch.SearchTransactions`** (`operations/TransactionSearch.md`)
- Signature: `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 8 nullable filter params **must be passed explicitly** (`null` to skip); `startDate`/`endDate` are ISO-8601 strings (wire `start_date`/`end_date`). No SDK auto-pager: loop `page` from 1 to `TotalPages` to cover the whole range.
- Returns `SearchResponse` (`records-2-Pa-Ve.md`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` → `TransactionInfo (transaction_info): TransactionInformation?` → `TransactionId (transaction_id)`, `PaypalReferenceId (paypal_reference_id)` + `PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?` (line up `ODR` = order, `TXN` = authorization/capture/refund against persisted ids), `TransactionEventCode (transaction_event_code)`, `TransactionAmount (transaction_amount): Money?`, `FeeAmount (fee_amount): Money?`, `TransactionStatus (transaction_status): string?` — **plain string, not an enum**, `InvoiceId (invoice_id)`, `CustomField (custom_field)`, `TransactionInitiationDate`, `TransactionUpdatedDate`; plus `Page (page)`, `TotalItems (total_items)`, `TotalPages (total_pages)`.
- Error: **`SdkException<RawError>` — Case B** (the SDK's only one): `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`. No typed accessors.
- Data latency (map notes): executed transactions take up to 3 hours to appear; window is the previous 3 years.

### Enum values needed (`map/models/enums.md`; all are `StringEnum<T>` in `PayPalServerSdk.Models.Enums` — use static members, not C# enum syntax)

| Enum | Members (wire) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` — **no `EXPIRED` member exists** |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `RefundIncompleteReason` | `Echeck (ECHECK)` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, … (29 members — full list in `enums.md`) |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `ProcessorResponseCode` | `_0000 (0000)`, `_0500 (0500)`, `_5100 (5100)`, `_5400 (5400)`, … (very long generated list — `enums.md`) |
| 3DS readback: `ParesStatus` / `EnrollmentStatus` | `Y/N/U/A/C/R/D/I` / `Y/N/U/B` |

### Error payload shapes (the `out` types above; all in `PayPalServerSdk.Models`)

| Shape | Fields | Used by |
|---|---|---|
| `Error` (`records-1-Ac-Pa.md`) | `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?` → `Issue (issue): string !req`, `Field (field)`, `Value (value)`, `Description (description)` | Orders + Payments ops |
| `Error1` (`records-1-Ac-Pa.md`) | same shape; `Details: IReadOnlyList<ErrorDetails1>?`, `Links: IReadOnlyList<ErrorLinkDescription>?` (`Rel` optional) | Vault ops |
| `RawError` (`sdk-map.md`) | `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | `SearchTransactions` + all fallbacks |

### Scenario → error map (step 11)

| Scenario | Where it surfaces | Handling |
|---|---|---|
| Declined card | (a) `CreateOrder`/`AuthorizeOrder` 422 → `TryGetError(out Error)`; (b) **2xx with `AuthorizationWithAdditionalData.Status = AuthorizationStatus.Denied`** + `ProcessorResponse.ResponseCode` — check **both** paths | Map to a deterministic payment-declined result; surface `Error.Name`/`Details[].Issue` or the processor code verbatim (specific issue strings `UNVERIFIED`) |
| Stale/expired authorization | Client-side pre-check via `GetAuthorizedPayment`: not renewable when `Status ∈ {Voided, Captured, Denied}` or `ExpirationTime` elapsed (the enum has **no** `EXPIRED` member — expiry is time-based); server-side: `ReauthorizePayment` 422 | On non-renewable: create a fresh order+authorize instead of retrying |
| Capture on voided auth | `CaptureAuthorizedPayment` 409/422 → `TryGetError(out Error)` | Deterministic failure; reconcile local state via `GetAuthorizedPayment` |
| Refund exceeds captured amount | `RefundCapturedPayment` 422 (possibly 409) → `TryGetError(out Error)` | Deterministic rejection — never retry the same refund; issue string `UNVERIFIED` |
| Invalid vault token | `Vault.GetPaymentToken` 404 → `TryGetError1(out Error1)`; at pay time: `CreateOrder`/`AuthorizeOrder` 422 | Treat as "card no longer saved"; prompt shopper to re-vault |
| 401 (auth/config failure) | Shares `TryGetError`/`TryGetError1` with other statuses — the exception carries **no** status (see the Error model row), so a 401 is indistinguishable from a 422 on the object alone; discriminate via `Error.Name` (`UNVERIFIED` strings — surface verbatim) or treat any typed-error hit on an op whose accessor set includes 401 as potentially auth-related | Config problem (credentials/environment/BaseUrl), not a payment outcome |

### 3DS / payer-action contingency (step 11)

- Primary detection (**map-grounded**): after `CreateOrder`/`AuthorizeOrder`, check `Order.Status` / `OrderAuthorizeResponse.Status` == `OrderStatus.PayerActionRequired`. When detected, the app must **STOP and report a contingency** (no approval round-trip is built in this integration).
- Secondary defensive check: scan `Links` for a `Rel` of `"payer-action"` — the rel string is **UNVERIFIED** from map/source (the status enum above is the grounded signal; treat the link check as best-effort only).
- Knob: `CardRequest.Attributes.Verification.Method` (`OrdersCardVerificationMethod`, default `ScaWhenRequired`) — `ScaAlways` makes payer-action more likely.
- Post-hoc readback: `CardResponse.AuthenticationResult (authentication_result): AuthenticationResponse?` → `LiabilityShift`, `ThreeDSecure.AuthenticationStatus: ParesStatus?`, `EnrollmentStatus: EnrollmentStatus?` (`records-1-Ac-Pa.md`).

---

## 3. Trap notes

> ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind the SDK has lifetime rules (`IHttpClientFactory`, not per-request construction) that the constructor signature does not convey, and the SDK's own DI extension makes a lifetime choice you must not fight. **MUST load `dotnet-client-initialization`** before wiring DI.

> ⚠ Step 1 (auth) — credentials must reach `options.Oauth2` from configuration before the client is constructed, and the SDK's token caching has its own refresh/invalidation semantics. **MUST load `dotnet-authentication`**.

> ⚠ Steps 2–10 (every call) — most optional parameters are nullable **with no C# default** and mis-bind if passed positionally or omitted; named arguments with the literal generated names are required. **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 2–10 (models) — enums are `StringEnum<T>` (not C# enums), records are immutable with `required` init members, and unmodeled JSON fields are silently dropped on deserialize — which is exactly how a drifted response loses data. **MUST load `dotnet-models`**.

> ⚠ Step 11 (error boundary) — which exception types actually reach a `catch`, when `TryGetRawError` does and does not apply, and how status codes survive (or don't) is not derivable from the signatures. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

> ⚠ Step 1 (resilience) — what the SDK's `Retry`/`Timeout` options actually bound, and whether a failed non-idempotent write (capture/refund) can be re-sent by the retry layer, determines why `payPalRequestId` is mandatory rather than optional. **MUST load `dotnet-configuration-resilience`** before tuning or registering the client.

> ⚠ Testing — the SDK's test seam is a specific constructor argument; faking at the wrong layer produces tests that assert nothing. **MUST load `dotnet-testing`** before writing integration tests.

---

## 4. REQUIRED READING

Load **before implementation starts** (this sheet deliberately does not carry their contents):

- `dotnet-client-initialization` — step 1 (client construction, DI lifetime)
- `dotnet-authentication` — step 1 (credentials, token strategy)
- `dotnet-calling-endpoints` — steps 2–10 (signatures, named arguments, `ct:`)
- `dotnet-models` — steps 2–10 (records, `StringEnum<T>`, required members)
- `dotnet-error-handling` — step 11 (exception boundary, Case A/B, accessors)
- `dotnet-configuration-resilience` — step 1 (retries, timeouts, base URL, pagination)
- `dotnet-testing` — tests for the integration layer

Two hazards belong to the first draft of the error boundary, not a later revision — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **`PayPal:Environment`**: this SDK release exposes exactly one environment, `ServerEnvironment.Sandbox` — there is no production member. Treat any configured value other than "sandbox" as a startup config error rather than silently coercing.
- **`PayPal:BaseUrl`**: when set, assign verbatim to `options.Server.Default.Sandbox.BaseUrl`; this governs all API calls and the OAuth token request (source-confirmed). When unset, the SDK default is the sandbox host.
- **Raw-card processing** carries a PCI SAQ D burden (noted on the SDK's `CardRequest` doc); assumed accepted for this reference app.
- **Vault API availability** is documented by the SDK as US-only; assumed fine for sandbox testing.
- **Specific PayPal `issue`/`name` strings** (decline reasons, refund-exceeded, reauthorize-denied) are not in the SDK contract — all such matching is defensive with verbatim surfacing, labeled `UNVERIFIED` in the scenario table. The `"payer-action"` link rel is likewise `UNVERIFIED`; `OrderStatus.PayerActionRequired` is the grounded 3DS signal.
- **Full refund** is documented as an empty JSON payload — send `new RefundRequest()`; whether a missing body also works is `UNVERIFIED`.
- **Transaction search latency**: transactions appear up to 3 hours after execution — reconcile-on-fulfilment flows must tolerate absence.
- No blockers.
