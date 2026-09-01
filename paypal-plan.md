# PayPal Server SDK (.NET) — eShopOnWeb integration plan

SDK: `AsadAli.Checkout.Sdk` (NuGet; install version-less: `dotnet add package AsadAli.Checkout.Sdk`). Map stamp: tag `v1.0.1`, commit `9653d18`. Library targets `netstandard2.0` — runs fine on `net8.0`. Root namespace `PayPalServerSdk`; client `PayPalServerSdkClient`; options `PayPalServerSdkClientOptions`. (sdk-map.md)

## 1. Scope & sequence

| Step | Work | Operations used |
|---|---|---|
| 1 | Add NuGet package; construct/DI-register client; apply `PayPal:*` config incl. optional `BaseUrl` override | — (client setup, §3.4) |
| 2 | Wire ClientId/ClientSecret auth | — (§3.4) |
| 3 | Direct card authorization, server-to-server, with 3DS/contingency detection | `Orders.CreateOrder` |
| 4 | Capture authorization on fulfilment | `Payments.CaptureAuthorizedPayment` |
| 5 | Void authorization on cancel | `Payments.VoidPayment` |
| 6 | Reauthorize stale authorization | `Payments.ReauthorizePayment` |
| 7 | Refund capture (full/partial, idempotent) | `Payments.RefundCapturedPayment` |
| 8 | Vault card; list/delete vaulted tokens | `Vault.CreatePaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.DeletePaymentToken` |
| 9 | Pay with vaulted card | `Orders.CreateOrder` (vault_id payment source) |
| 10 | Reconciliation report over a date range, paginated | `TransactionSearch.SearchTransactions` |
| 11 | Status-polling reads used by 3–10 | `Orders.GetOrder`, `Payments.GetAuthorizedPayment`, `Payments.GetCapturedPayment`, `Payments.GetRefund`, `Vault.GetPaymentToken` |

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

**Response envelope (all operations):** methods return `Task<TModel>` directly — there is **no** `ApiResponse<T>` wrapper. On success you get the deserialized body model only; HTTP status/headers are not surfaced on the success path. On error status the SDK throws `PayPalServerSdk.Core.Exceptions.SdkException<TError>` with `.Error` (sdk-map.md *Error-handling model*; source `Core/Exceptions/SdkException.cs`). There is no `ApiException` type and no no-throw `…Result` variant anywhere in this SDK (sdk-map.md).

**Namespaces needed:** `PayPalServerSdk` (client, options, `ServerOptions`) · `PayPalServerSdk.Servers` (`ServerEnvironment`, `DefaultOptions`) · `PayPalServerSdk.Core` (`RequestOptions`) · `PayPalServerSdk.Core.Configuration` (`RetryOptions`) · `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` (`OAuth2ClientCredentials`) · `PayPalServerSdk.Core.Authentication.OAuth2` (`IOAuth2TokenStrategy<>`) · `PayPalServerSdk.Core.Exceptions` (`SdkException<>`) · `PayPalServerSdk.Core.ErrorResponse` (`RawError`) · `PayPalServerSdk.Models` (all records) · `PayPalServerSdk.Models.Enums` (all enums) · `PayPalServerSdk.Errors` (`{Operation}Error` classes). (sdk-map.md; source `PayPalServerSdkClientOptions.cs`, `Core/RequestOptions.cs`, `Core/Exceptions/SdkException.cs`)

### 2.1 Orders — direct card & vaulted card authorization (steps 3, 9)

| | |
|---|---|
| Call | `client.Orders.CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<Order>`. First 5 params nullable, no default — **must pass explicitly** (pass `null`). `payPalRequestId` → `PayPal-Request-Id` idempotency header. (operations/Orders.md; header mapping source `Api/Orders.cs` pattern per `Api/Payments.cs`) |
| Request | `OrderRequest` (records-1): `Intent (intent): CheckoutPaymentIntent !req` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` · `PaymentSource (payment_source): PaymentSource?` · `Payer (payer): Payer?` · `ApplicationContext (application_context): OrderApplicationContext?` |
| | `PurchaseUnitRequest` (records-2): `Amount (amount): AmountWithBreakdown !req` · `ReferenceId (reference_id): string?` · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `Description (description): string?` · `SoftDescriptor (soft_descriptor): string?` |
| | `AmountWithBreakdown` (records-1): `CurrencyCode (currency_code): string !req` · `Value (value): string !req` (amount is a **string**, e.g. `"100.00"`) · `Breakdown (breakdown): AmountBreakdown?` |
| | **Raw card** — `PaymentSource` (records-2): `Card (card): CardRequest?`. `CardRequest` (records-1): `Number (number): string?` · `Expiry (expiry): string?` (`"YYYY-MM"`) · `SecurityCode (security_code): string?` · `Name (name): string?` · `BillingAddress (billing_address): Address?` · `Attributes (attributes): CardAttributes?` · `VaultId (vault_id): string?`. `Address` (records-1): `AddressLine1 (address_line_1)`, `AddressLine2 (address_line_2)`, `AdminArea2 (admin_area_2)` city, `AdminArea1 (admin_area_1)` state, `PostalCode (postal_code)` — all `string?` — and `CountryCode (country_code): string !req`. 3DS opt-in: `CardAttributes.Verification (verification): CardVerification?` → `CardVerification.Method (method): OrdersCardVerificationMethod? = ScaWhenRequired` (records-1). |
| | **Vaulted card** — same `PaymentSource.Card` = `new CardRequest { VaultId = <payment-token-id> }`. `vault_id` doc (source `Models/CardRequest.cs`): "The PayPal-generated ID for the vaulted payment source … stored on the merchant's server … used for future transactions." (Alternative `PaymentSource.Token (token): Token?` with `Token { Id !req, Type: TokenType !req }` exists, but `TokenType` models only `BillingAgreement (BILLING_AGREEMENT)`; the payment-method-token wire value is **not** in the SDK source — prefer the `VaultId` path. records-2, enums.md; source `Models/Enums/TokenType.cs`) |
| Response | `Order` (records-1): `Id (id)` · `Status (status): OrderStatus?` · `Intent (intent)` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` · `PaymentSource (payment_source): PaymentSourceResponse?` · `Links (links): IReadOnlyList<LinkDescription>?`. Authorization result: `PurchaseUnits[0].Payments (payments): PaymentCollection?` → `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → `[0].Id (id)`, `.Status (status): AuthorizationStatus?`, `.Amount (amount): Money?`, `.ProcessorResponse (processor_response): ProcessorResponse?`, `.ExpirationTime (expiration_time)`. (records-1 `AuthorizationWithAdditionalData`; records-2 `PaymentCollection`) |
| 3DS / contingency detection | `Order.Status == OrderStatus.PayerActionRequired` ⇒ a payer-action (3DS) challenge is required — **detect and report; do not follow the link**. The challenge URL is in `Order.Links` (`LinkDescription`: `Href (href) !req`, `Rel (rel) !req`, `Method (method)`); the specific `rel` string is not modeled as an enum — key on `Status`, surface `Links` in the report. `UNVERIFIED` (live-traffic): the exact `rel` value (PayPal docs call it `payer-action`) — match defensively, e.g. `Rel` containing `"payer"`. Post-auth 3DS outcome: `Order.PaymentSource.Card (CardResponse).AuthenticationResult (authentication_result): AuthenticationResponse?` → `LiabilityShift (liability_shift): LiabilityShiftIndicator?`, `ThreeDSecure (three_d_secure): ThreeDSecureAuthenticationResponse?` → `AuthenticationStatus: ParesStatus?`, `EnrollmentStatus: EnrollmentStatus?`. (records-1, records-2, enums.md) |
| Error | **Case A**: `SdkException<CreateOrderError>` — `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback]. `Error` (records-1, `PayPalServerSdk.Models`): `Name (name) !req`, `Message (message) !req`, `DebugId (debug_id) !req`, `Details (details): IReadOnlyList<ErrorDetails>?` → `ErrorDetails.Issue (issue) !req`, `.Field`, `.Value`, `.Description`. (operations/Orders.md) |
| Pagination | none |

Supporting read: `client.Orders.GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<Order>`; `fields`, `payPalMockResponse`, `payPalAuthAssertion` must be passed explicitly. Error: `SdkException<GetOrderError>`, `TryGetError(out Error)` [401, 404]. (operations/Orders.md)

### 2.2 Payments — capture / void / reauthorize / refund (steps 4–7)

| Op | Signature (all → `Task<…>`; nullable no-default params **must be passed explicitly**) | Request body | Response reads | Error (all Case A) |
|---|---|---|---|---|
| Capture (step 4) | `client.Payments.CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `CapturedPayment` (operations/Payments.md) | `CaptureRequest` (records-1): `Amount (amount): Money?` (omit/null = full remaining) · `FinalCapture (final_capture): bool? = false` · `InvoiceId (invoice_id)` · `NoteToPayer (note_to_payer)` · `SoftDescriptor (soft_descriptor)`. `Money`: `CurrencyCode (currency_code) !req`, `Value (value) !req` (strings). | `CapturedPayment` (records-1): `Id (id)` · `Status (status): CaptureStatus?` · `Amount (amount): Money?` · `FinalCapture (final_capture): bool?` · **`SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`** → `GrossAmount (gross_amount): Money !req`, **`PaypalFee (paypal_fee): Money?`** = PayPal fee, **`NetAmount (net_amount): Money?`** = net proceeds (records-2) · **`SellerProtection (seller_protection): SellerProtection?`** → `Status (status): SellerProtectionStatus?`, `DisputeCategories (dispute_categories)` (records-2) · `ProcessorResponse (processor_response)`. | `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback]. 409 = capture conflict (e.g. already captured). |
| Void (step 5) | `client.Payments.VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization`. ⚠ **Parameter order differs from capture**: `payPalAuthAssertion` is 3rd, `payPalRequestId` 4th — use named arguments. (operations/Payments.md) | none (no body param) | `PaymentAuthorization` (records-2): `Id (id)` · `Status (status): AuthorizationStatus?` → expect `Voided` · `UpdateTime (update_time)`. | `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. 409/422 = cannot void (e.g. fully captured). |
| Reauthorize (step 6) | `client.Payments.ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization` (operations/Payments.md) | `ReauthorizeRequest` (records-2): `Amount (amount): Money?` — the only supported parameter (op notes). | `PaymentAuthorization`: fresh `Id`, `Status` (`Created` again on success), new 3-day honour window. | `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. **No-longer-reauthorizable signal**: 422 (or 404 unknown id) — read `Error.Name` + `Error.Details[].Issue` (plain strings; issue constants are **not** modeled as enums). Op-note window (operations/Payments.md): reauthorize only from day 4 to day 29 after the original authorization; at 30+ days create a **new** order/authorization instead. Defensive directive: on 422 log `Name`/`Issue` and fall back to a fresh authorization. |
| Refund (step 7) | `client.Payments.RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Refund` (operations/Payments.md) | `RefundRequest` (records-2): `Amount (amount): Money?` — **null body (or no Amount) = full refund; Amount present = partial** · `CustomId (custom_id)` · `InvoiceId (invoice_id)` · `NoteToPayer (note_to_payer)`. **Idempotency key: the `payPalRequestId` parameter** → sent as the `PayPal-Request-Id` header; the server stores keys for 45 days (source `Api/Payments.cs`). `RequestOptions` carries **only** `LogLevel` — there is no arbitrary-custom-header hook on any call (source `Core/RequestOptions.cs`), so `payPalRequestId` is the idempotency channel. | `Refund` (records-2): `Id (id)` · `Status (status): RefundStatus?` · `StatusDetails (status_details): RefundStatusDetails?` → `Reason: RefundIncompleteReason?` · `Amount (amount): Money?` · `SellerPayableBreakdown (seller_payable_breakdown)`. | `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. |

Supporting reads: `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, …)` → `PaymentAuthorization`, `TryGetError` [401, 403, 404]; `GetCapturedPayment(string captureId, string? payPalMockResponse, …)` → `CapturedPayment`, `TryGetError` [401, 403, 404]; `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, …)` → `Refund`, `TryGetError` [401, 403, 404]. All add `TryGetNoContent(out RawError)` [500]. (operations/Payments.md)

### 2.3 Vault — save / pay-with / delete / list cards (steps 8–9)

| Op | Signature | Request | Response | Error (all Case A, payload `Error1`) |
|---|---|---|---|---|
| Vault a card (step 8) | `client.Vault.CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentTokenResponse>`; `payPalRequestId` must be passed explicitly. (operations/Vault.md) | `PaymentTokenRequest` (records-2): `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` → `Card (card): PaymentTokenRequestCard?` → `Number (number)`, `Expiry (expiry)`, `SecurityCode (security_code)`, `Name (name)`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?` (all `string?` unless noted) · `Customer (customer): Customer?` → **`MerchantCustomerId (merchant_customer_id): string?` = your own customer id**; `Id (id): string?` = PayPal-generated customer id. **No PayPal-customer creation step exists in this SDK** (5 controllers only: Orders, Payments, Subscriptions, TransactionSearch, Vault) — pass your own id via `MerchantCustomerId`. (records-1 `Customer`; sdk-map.md ops table) | `PaymentTokenResponse` (records-2): **`Id (id)` = the vault token id to store in your DB** · `Customer (customer): CustomerResponse?` → `Id`, `MerchantCustomerId` · `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `Card (card): CardPaymentTokenEntity?` → **safe display attributes: `LastDigits (last_digits)`, `Brand (brand): CardBrand?`, `Expiry (expiry)`, `Name (name)`** · `Links`. (records-1 `CardPaymentTokenEntity`) | `SdkException<CreatePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError`. `Error1` (records-1): `Name`, `Message`, `DebugId` (all `!req`), `Details: IReadOnlyList<ErrorDetails1>?` → `Issue (issue) !req`. |
| Pay with vaulted card (step 9) | `Orders.CreateOrder` exactly as §2.1, with `PaymentSource.Card = new CardRequest { VaultId = <token id> }` (source `Models/CardRequest.cs` `vault_id` doc) | as §2.1 | as §2.1 | as §2.1 |
| Delete token (step 8) | `client.Vault.DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task` (**void** — success = no exception) (operations/Vault.md) | — | — | `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`. |
| List customer's tokens (step 8) | `client.Vault.ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<CustomerVaultPaymentTokensResponse>`. Query wires: `customer_id` ← `customerId`, `page_size`, `page`, `total_required`. `customerId` doc (source `Api/Vault.cs`): "A unique identifier representing a specific customer in **merchant's/partner's system or records**" — i.e. your own customer id. (operations/Vault.md) | — | `CustomerVaultPaymentTokensResponse` (records-1): `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Customer (customer): VaultResponseCustomer?`. **Pagination: loop `page` from 1 while `page < TotalPages`** (no cursor; map lists pagination "none (only `page`)"). | `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`. |

Setup-token alternative (`CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, …)` → `SetupTokenResponse` with `Status: PaymentTokenStatus? = Created`) exists for payer-present vaulting (operations/Vault.md); the direct server-to-server card vault above is `CreatePaymentToken`. Supporting read: `GetPaymentToken(string id, …)` → `PaymentTokenResponse`, `TryGetError1` [403, 404, 422, 500].

### 2.4 Transaction search — reconciliation (step 10)

| | |
|---|---|
| Call | `client.TransactionSearch.SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<SearchResponse>`. The 8 params `transactionId`…`terminalId` are nullable with **no default — pass `null` explicitly** (use named arguments). (operations/TransactionSearch.md) |
| Date params | `start_date`/`end_date` are **strings in RFC 3339 §5.6 Internet date-time format; seconds required, fractional seconds optional** (e.g. `2026-08-01T00:00:00Z`). **Maximum supported range: 31 days** — chunk longer report ranges into ≤31-day windows. (source `Api/TransactionSearch.cs` param docs) |
| Filters | `transactionStatus` is a plain `string?` (no enum): `D` denied · `P` pending · `S` success · `V` reversed/refunded (source `Api/TransactionSearch.cs` param doc). `transactionAmount` range format `"[500 TO 1005]"` in lower denominations, URL-encoded. `transactionId`: 17 chars (order IDs 19). |
| Response | `SearchResponse` (records-2): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` → `TransactionInfo (transaction_info): TransactionInformation?` · `Page (page): int?` · `TotalItems (total_items): int?` · **`TotalPages (total_pages): int?`** · `Links`. **Pagination: loop `page` while `page < TotalPages`.** `TransactionInformation` (records-2): **`TransactionId (transaction_id)`** · **`PaypalReferenceId (paypal_reference_id)` + `PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?` = the tie-back to order/authorization/capture** (`Odr (ODR)` = order id, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)`; enums.md) · **`TransactionStatus (transaction_status): string?`** (D/P/S/V) · **`TransactionAmount (transaction_amount): Money?`** · **`FeeAmount (fee_amount): Money?`** = PayPal fee · **`TransactionInitiationDate (transaction_initiation_date)` / `TransactionUpdatedDate (transaction_updated_date)`** · `InvoiceId (invoice_id)`, `CustomField (custom_field)` for app-level correlation. |
| Error | **Case B — the SDK's only raw-error operation**: `SdkException<RawError>`; `ex.Error.StatusCode: HttpStatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`. No typed accessors. (operations/TransactionSearch.md; sdk-map.md) |
| Caveats | Transactions appear up to 3 hours after execution; window covers the previous 3 years (op notes, operations/TransactionSearch.md). |

### 2.5 Enum values needed (all `PayPalServerSdk.Models.Enums`, `StringEnum<T>` — use static members, e.g. `CheckoutPaymentIntent.Authorize`) (enums.md)

| Enum | Values (C# member (`wire`)) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** ← 3DS/contingency signal |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, … (29 members; `Unknown (UNKNOWN)`) |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)` (default), `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| `SellerProtectionStatus` | `Eligible (ELIGIBLE)`, `PartiallyEligible (PARTIALLY_ELIGIBLE)`, `NotEligible (NOT_ELIGIBLE)` |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `ParesStatus` / `EnrollmentStatus` | `Y`, `N`, `U`, `A`, `C`, `R`, `D`, `I` / `Y`, `N`, `U`, `B` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `RefundIncompleteReason` | `Echeck (ECHECK)` |

### 2.6 Client construction, auth, environment, base-URL override (steps 1–2)

| Fact | Value | Source |
|---|---|---|
| Constructor | `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` | sdk-map.md |
| DI | `services.AddPayPalServerSdkClient(o => { … })` — registers the client as a **singleton** and builds its `HttpClient` from `IHttpClientFactory.CreateClient()` | source `ServiceCollectionExtensions.cs` |
| Credentials | `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = … }` — both `required` init properties; optional `Scope`. Namespace `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`. If `Oauth2` is null the client sends requests **unauthenticated** (falls back to a no-op auth scheme) | source `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`, `AuthSchemes.cs` |
| OAuth mechanics | Automatic: the auth scheme fetches a token and sets `Bearer` on every call; the token is **cached until expiry** (double-checked lock), not re-fetched per call. Default strategy POSTs `grant_type=client_credentials` with a Basic `client_id:client_secret` header to `/v1/oauth2/token`. Custom strategy: `options.Oauth2TokenStrategy` (`IOAuth2TokenStrategy<OAuth2ClientCredentials>`) | source `Core/Authentication/OAuth2/OAuth2Scheme.cs`, `…/OAuth2ClientCredentialsStrategy.cs`, `AuthSchemes.cs` |
| Environment | `options.Environment = ServerEnvironment.Sandbox` (namespace `PayPalServerSdk.Servers`) — **the only member; no Production environment is modeled** | sdk-map.md; source `Servers/ServerEnvironment.cs` |
| **Base-URL override** | `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>` (default `"https://api-m.sandbox.paypal.com"`). Chain: `PayPalServerSdkClientOptions.Server` (`ServerOptions`, root namespace) → `.Default` (`DefaultOptions`, `PayPalServerSdk.Servers`) → `.Sandbox.BaseUrl`. **Applies to every call including the OAuth token request** — the token URL is resolved through the same `Server` object (`server.Default("/v1/oauth2/token")`) | source `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs` |
| Errors | All 40 operations throw `SdkException<TError>`; 39 Case A typed (`TryGet…` + `TryGetRawError`), 1 Case B (`SearchTransactions` → `SdkException<RawError>`). No `…Result` no-throw variants | sdk-map.md |
| Resilience knobs | `options.Retry` (`RetryOptions`, `PayPalServerSdk.Core.Configuration` — all members `required`; start from `RetryOptions.Default()`), `options.Logging`, per-call `RequestOptions.LogLevel` | sdk-map.md; source `Core/RequestOptions.cs` |

## 3. Trap notes (hazard + consequence — resolve by loading the named skill)

- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind the SDK client has a lifetime contract (socket exhaustion vs. stale DNS); the DI helper makes a specific lifetime choice you must understand before hand-rolling your own registration. **MUST load `dotnet-client-initialization`** before constructing or DI-registering the client.
- ⚠ Step 2 (auth) — credentials must be set on the options before the client is constructed (the auth scheme is built in the constructor), secrets must flow from configuration not code, and credential rotation has its own pattern. **MUST load `dotnet-authentication`** before wiring `Oauth2`.
- ⚠ Steps 3–10 (every call) — many optional params are nullable **without C# defaults** and mis-bind in positional calls; pass them explicitly by name (`null` to skip), and note `VoidPayment` orders its nullable params differently from `CaptureAuthorizedPayment`. **MUST load `dotnet-calling-endpoints`** before writing the first call.
- ⚠ Steps 3–10 (models) — enums are `StringEnum<T>`, not C# enums (construction and comparison semantics differ; no switch exhaustiveness); records are immutable with `init`-only/required members; unmodeled JSON response fields are silently dropped on deserialize. **MUST load `dotnet-models`** before building request payloads or mapping responses onto domain types.
- ⚠ Step 5 boundary (errors) — each operation's error case (A typed vs B raw) differs per its sheet row; `TryGetRawError` is not a catch-all on typed errors; a `TryGetNoContent` 500 arm exists on most Payments ops. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ Step 1/6 (resilience) — whether a failed capture/refund (non-idempotent POST) can be re-sent by the retry pipeline, what `RetryOptions.Timeout` actually bounds, and how retries interact with the `PayPal-Request-Id` idempotency key are not visible from the option names. **MUST load `dotnet-configuration-resilience`** before tuning `options.Retry` or relying on the `BaseUrl` override in production.
- ⚠ Step 11 (tests) — the test seam and how to fake error/success paths without SDK internals is a pattern decision. **MUST load `dotnet-testing`** before writing integration-layer tests.

## 4. REQUIRED READING (load **before implementation starts** — this sheet deliberately does not carry their contents)

- `dotnet-client-initialization` — governs step 1 (client construction, options, DI lifetime).
- `dotnet-authentication` — governs step 2 (`Oauth2` credentials wiring, rotation).
- `dotnet-calling-endpoints` — governs steps 3–10 (signatures, must-pass params, async/cancellation).
- `dotnet-models` — governs steps 3–10 (record construction, `StringEnum<T>`, nullability, wire names).
- `dotnet-error-handling` — governs the error boundary every step throws through (Case A/B mechanics, `TryGet…` ladders).
- `dotnet-configuration-resilience` — governs step 1/6 (retries, timeouts, server/base-URL selection, pagination loops, logging).
- `dotnet-testing` — governs step 11 (faking the SDK seam, error-path coverage).

Always include, verbatim, both of these hazard rows — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

**Hard-requirement verdicts (all four are SUPPORTED — no blockers):**

- **(a) Direct card payment without buyer approval — SUPPORTED.** `OrderRequest.PaymentSource.Card` takes the raw card on create (records-1/2); the Orders op notes state a valid `payment_source` in the request is the alternative to buyer approval (operations/Orders.md). `UNVERIFIED` (only live traffic can confirm): that sandbox processes the card synchronously on `CreateOrder` with intent `AUTHORIZE`. Defensive directive: branch on the returned `Order.Status` — `Completed` → read the authorization from `PurchaseUnits[0].Payments.Authorizations[0]`; `PayerActionRequired` → 3DS contingency, report it (do not follow links); anything else → poll `GetOrder` once, then treat as failed. Note (records-1 `CardRequest` doc): raw card data via API carries a **PCI SAQ D** burden.
- **(b) Vaulting cards — SUPPORTED.** `Vault.CreatePaymentToken` with a card source (operations/Vault.md). Note (client doc comment): the Payment Method Tokens API is documented by PayPal as US-only.
- **(c) Transaction search — SUPPORTED.** `TransactionSearch.SearchTransactions` with 31-day-max windows and page/`TotalPages` looping (operations/TransactionSearch.md; source `Api/TransactionSearch.cs`).
- **(d) Base-URL override — SUPPORTED.** `options.Server.Default.Sandbox.BaseUrl`, and it covers OAuth token requests too (verified in source `AuthSchemes.cs`). Caveat: only `ServerEnvironment.Sandbox` is modeled — targeting production means overriding the sandbox base URL; the production URL string itself is not in the SDK source, so it must come from `PayPal:BaseUrl` config.

**Other assumptions:**

- Amounts and currency are strings on the wire (`Money.Value`, `Money.CurrencyCode`); order totals are computed server-side and formatted to the currency's minor units — formatting is the integration's job.
- The refund idempotency key is the `payPalRequestId` parameter (→ `PayPal-Request-Id` header, keys stored 45 days server-side); no other custom-header channel exists (`RequestOptions` exposes only `LogLevel`).
- Vault token ids are stored in the eShopOnWeb DB keyed by our own customer id (`MerchantCustomerId`); `ListCustomerPaymentTokens` accepts that same merchant-side id.
- `SearchTransactions` is the SDK's only Case B (raw error) operation — the reconciliation code path needs the `RawError` handling arm, not typed accessors.
- Plan is repo-agnostic by design (plan mode): the main agent maps these steps onto eShopOnWeb's actual projects (ApplicationCore/Web) and config binding.

**Blockers:** none.
