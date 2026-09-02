# PayPal .NET SDK integration plan — eShopOnWeb (ASP.NET Core, .NET 8)

SDK: NuGet `AsadAli.Checkout.Sdk` (install version-less) · root namespace `PayPalServerSdk` · client `PayPalServerSdkClient` · targets `netstandard2.0` (.NET 8 compatible) · map provenance: source commit `9653d18`, tag `v1.0.1`.

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Install package; construct/DI-register `PayPalServerSdkClient`; OAuth client-credentials auth; environment + base-URL override; retry/timeout/logging options | — (client setup) |
| 2 | Shared error boundary (translate `SdkException<T>` + `JsonException` into app errors) | — (all operations) |
| 3 | Authorize order total with a **raw card** | `Orders.CreateOrder` → `Orders.AuthorizeOrder` |
| 4 | Authorize order total with a **vaulted card** | `Orders.CreateOrder` → `Orders.AuthorizeOrder` (card `vault_id`) |
| 5 | Capture authorization at fulfilment (read gross/fee/net) | `Payments.CaptureAuthorizedPayment` |
| 6 | Reauthorize a stale authorization before capture | `Payments.GetAuthorizedPayment` → `Payments.ReauthorizePayment` |
| 7 | Void an authorization (cancel before fulfilment) | `Payments.VoidPayment` |
| 8 | Refund a capture, full or partial, idempotency-keyed | `Payments.RefundCapturedPayment` (+ `Payments.GetRefund`) |
| 9 | Vault cards: setup token → payment token (or direct), list, get, delete | `Vault.CreateSetupToken`, `Vault.CreatePaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.GetPaymentToken`, `Vault.DeletePaymentToken` |
| 10 | Reconciliation: transaction search over a date range, all pages | `TransactionSearch.SearchTransactions` |

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

### 2a. Client construction, auth, environment, base URL (map: `sdk-map.md`; source-confirmed where marked)

| Fact | Value |
|---|---|
| Constructor | `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` — both in root namespace `PayPalServerSdk` |
| DI alternative | `services.AddPayPalServerSdkClient(o => { … })` (`ServiceCollectionExtensions.cs`) |
| Options type | `PayPalServerSdkClientOptions` (root ns). Properties: `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Logging: LoggingOptions` · `Server: ServerOptions` · `Oauth2: OAuth2ClientCredentials?` · `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?` |
| Environment | `PayPalServerSdk.Servers.ServerEnvironment` — **the only member is `ServerEnvironment.Sandbox`** (source-confirmed, `Servers/ServerEnvironment.cs`: no `Production` member exists at v1.0.1; `ServerEnvironment.Default()` = Sandbox). Pointing at any other host is done ONLY via the base-URL override below. |
| OAuth credentials | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials` — `required string ClientId`, `required string ClientSecret`, `string? Scope` (source-confirmed). Default strategy (when `Oauth2TokenStrategy` is null): `POST /v1/oauth2/token`, HTTP Basic `base64(clientId:clientSecret)`, form body `grant_type=client_credentials` (+ optional `scope`); token caching is handled inside the SDK's `OAuth2Scheme` (source-confirmed, `AuthSchemes.cs`, `OAuth2ClientCredentialsStrategy.cs`). Custom strategy seam: `PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>` with `Task<OAuthToken> GetToken(OAuth2ClientCredentials credentials, CancellationToken cancellationToken)`. |
| Base-URL override (ALL calls incl. OAuth token) | `options.Server.Default.Sandbox.BaseUrl = "https://…"`. Chain (source-confirmed): `ServerOptions` (root ns `PayPalServerSdk`, `ServerOptions.cs`) → `.Default: PayPalServerSdk.Servers.DefaultOptions` → `.Sandbox: DefaultOptions.SandboxOptions` → `.BaseUrl: string` (default `https://api-m.sandbox.paypal.com`). Every API path AND `/v1/oauth2/token` resolve through `Server.Default(path)` → `DefaultOptions.Resolve(env, path)` → `new UrlTemplate(Sandbox.BaseUrl, path, [])` (`Server.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs`) — one override covers the token request too. |
| Retry options | `PayPalServerSdk.Core.Configuration.RetryOptions` — all members `required` unless you start from `RetryOptions.Default()`: `StatusCodesToRetry: IReadOnlyList<HttpStatusCode>`, `HttpMethodsToRetry: IReadOnlyList<HttpMethod>`, `MaxRetries: int`, `Delay: TimeSpan`, `Timeout: TimeSpan?`, `BackOffFactor: int`, `UseExponentialBackoff: bool`, `MaxJitter: TimeSpan`, `OnRetry: Action<RetryAttempt>?` |
| Logging options | `PayPalServerSdk.Core.Configuration.LoggingOptions` — `LoggerFactory: ILoggerFactory?`, `LogRequestHeaders`, `LogResponseHeaders`, `LogRequestBody` (all `bool`), `BodySizeLimit: int = 32*1024`, `RedactedHeaders`, `RedactedKeys` (defaults include `client_secret`, `access_token`, …), `UnmaskHeaders`, `RedactionPlaceholder = "***"` (source-confirmed, `Core/Configuration/LoggingOptions.cs`). |
| Per-request options | `PayPalServerSdk.Core.RequestOptions` — sealed record, only member `LogLevel: LogLevel?` (source-confirmed, `Core/RequestOptions.cs`). It is **not** a header bag: `PayPal-Request-Id` goes ONLY through each operation's dedicated `payPalRequestId` parameter. |
| Namespaces to import | `PayPalServerSdk` (client/options/ServerOptions) · `PayPalServerSdk.Api` (controllers — usually not needed; accessed via client properties) · `PayPalServerSdk.Models` (records incl. error payloads) · `PayPalServerSdk.Models.Enums` (enums) · `PayPalServerSdk.Errors` (`{Operation}Error` types) · `PayPalServerSdk.Servers` (`ServerEnvironment`, `DefaultOptions`) · `PayPalServerSdk.Core` (`RequestOptions`) · `PayPalServerSdk.Core.Configuration` (`RetryOptions`, `LoggingOptions`) · `PayPalServerSdk.Core.Authentication.OAuth2(.ClientCredentials)` (auth) · `PayPalServerSdk.Core.Exceptions` (`SdkException<T>`, implied by `Core/Exceptions/SdkException.cs`) · `PayPalServerSdk.Core.ErrorResponse` (`RawError`, `ApiError`) |

### 2b. Operations

**`client.Orders.CreateOrder`** — create order (intent=AUTHORIZE) · map: `operations/Orders.md`, models: `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`

| Aspect | Contract |
|---|---|
| Signature | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 5 nullable params before `body` have NO default: pass explicitly (`null` to skip) |
| Request body | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req` → `CheckoutPaymentIntent.Authorize` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` · `PaymentSource (payment_source): PaymentSource?` · `Payer (payer): Payer?` · `ApplicationContext (application_context): OrderApplicationContext?` |
| Purchase unit | `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req` · `ReferenceId (reference_id): string?` · `CustomId (custom_id): string?` ← set to our order id · `InvoiceId (invoice_id): string?` ← set to our order id too · `Description (description)`, `SoftDescriptor (soft_descriptor)`, `Items`, `Shipping`, `Payee`, `PaymentInstruction` (all optional) |
| Amount | `AmountWithBreakdown`: `CurrencyCode (currency_code): string !req` (from config) · `Value (value): string !req` — string amount; format order total `"F2"` invariant so it matches to the cent · `Breakdown (breakdown): AmountBreakdown?` |
| Card payment source | `PaymentSource.Card (card): CardRequest?` → `CardRequest`: `Number (number): string?`, `Expiry (expiry): string?` (`YYYY-MM`), `SecurityCode (security_code): string?`, `Name (name): string?`, `BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?` ← vaulted-card path, `StoredCredential (stored_credential): CardStoredCredential?`, `Attributes (attributes): CardAttributes?`. `Address`: `CountryCode (country_code): string !req`, `AddressLine1/2`, `AdminArea2` (city), `AdminArea1` (state), `PostalCode` optional |
| Returns | `Order`: `Id (id): string?` ← order id · `Status (status): OrderStatus?` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` · `Intent`, `PaymentSource`, `Payer`, `Links` |
| Error | Case A — `SdkException<PayPalServerSdk.Errors.CreateOrderError>`: `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback]. Payload `PayPalServerSdk.Models.Error`: `Name (name)`, `Message (message)`, `DebugId (debug_id)` required strings; `Details (details): IReadOnlyList<ErrorDetails>?` → `ErrorDetails.Issue (issue): string !req`, `Field`, `Value`, `Description` |
| Pagination | none |

**`client.Orders.AuthorizeOrder`** — authorize an existing order · map: `operations/Orders.md`

| Aspect | Contract |
|---|---|
| Signature | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 5 nullable params (`payPalMockResponse`…`body`) must be passed explicitly |
| Request body | `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` → `.Card (card): CardRequest?` (same `CardRequest` as above — raw card here, or `VaultId` for vaulted card; may be `null` if the card was already supplied on `CreateOrder`) |
| Returns | `OrderAuthorizeResponse`: `Id (id): string?` ← order id · `Status (status): OrderStatus?` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` |
| **Authorization read path** | `resp.PurchaseUnits?[0].Payments?.Authorizations?[0]` — `PurchaseUnit.Payments (payments): PaymentCollection?` → `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → `.Id (id)` ← **authorization id** · `.Status (status): AuthorizationStatus?` ← **authorization status** · `.Amount (amount): Money?` · `.ExpirationTime (expiration_time): string?` · `.InvoiceId (invoice_id)`, `.CustomId (custom_id)` · `.ProcessorResponse (processor_response): ProcessorResponse?` (decline detail: `ResponseCode (response_code): ProcessorResponseCode?`, `AvsCode`, `CvvCode`, `PaymentAdviceCode`) |
| Error | Case A — `SdkException<AuthorizeOrderError>`: `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` |
| Pagination | none |

**`client.Payments.CaptureAuthorizedPayment`** — capture at fulfilment · map: `operations/Payments.md`

| Aspect | Contract |
|---|---|
| Signature | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullable params (`payPalMockResponse`…`body`) must be passed explicitly |
| Request body | `CaptureRequest`: `Amount (amount): Money?` (omit = capture full authorized amount) · `InvoiceId (invoice_id): string?` · `FinalCapture (final_capture): bool? = false` · `NoteToPayer`, `SoftDescriptor`, `PaymentInstruction` optional. `Money`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req` |
| Returns | `CapturedPayment`: `Id (id): string?` ← **capture id** · `Status (status): CaptureStatus?` ← **capture status** · `StatusDetails (status_details): CaptureStatusDetails?` → `.Reason (reason): CaptureIncompleteReason?` · `Amount (amount): Money?` · `FinalCapture (final_capture): bool?` · `InvoiceId`, `CustomId` · `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?` · `ProcessorResponse`, `Links`, `CreateTime` |
| Fee breakdown | `SellerReceivableBreakdown`: `GrossAmount (gross_amount): Money !req` ← captured gross · `PaypalFee (paypal_fee): Money?` ← PayPal fee · `NetAmount (net_amount): Money?` ← net proceeds · (`PaypalFeeInReceivableCurrency`, `ReceivableAmount`, `ExchangeRate`, `PlatformFees` optional). Map caveat: breakdown is **not available while the capture is PENDING** |
| Error | Case A — `SdkException<CaptureAuthorizedPaymentError>`: `TryGetError(out Error)` [400, 401, 403, 404, **409**, **422**] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` |
| Pagination | none |

**`client.Payments.GetAuthorizedPayment`** — poll an authorization (status/expiry before reauthorize/capture/void) · map: `operations/Payments.md`

| Aspect | Contract |
|---|---|
| Signature | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 2 nullable params must be passed explicitly |
| Returns | `PaymentAuthorization`: `Id`, `Status (status): AuthorizationStatus?`, `StatusDetails (status_details): AuthorizationStatusDetails?` → `.Reason (reason): AuthorizationIncompleteReason?`, `Amount: Money?`, `ExpirationTime (expiration_time): string?`, `InvoiceId`, `CustomId`, `SellerProtection`, `Links`, `CreateTime`, `UpdateTime` |
| Error | Case A — `SdkException<GetAuthorizedPaymentError>`: `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` |

**`client.Payments.ReauthorizePayment`** — renew a stale hold · map: `operations/Payments.md`

| Aspect | Contract |
|---|---|
| Signature | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 nullable params must be passed explicitly |
| Request body | `ReauthorizeRequest`: `Amount (amount): Money?` — the ONLY supported parameter |
| Returns | `PaymentAuthorization` (shape above) — fresh `Status`, new `ExpirationTime` |
| Documented window (from the operation's own notes) | Reauthorize AFTER the initial **3-day honor period** expires; allowed **from day 4 to day 29** after the original authorization; multiple reauthorizations allowed within the 29-day period; each reauthorization starts a NEW 3-day honor period; at **30 days** you must create a NEW authorization instead. Amount may be up to **115%** of the original, max increase **$75 USD** (US example). The SDK exposes no separate "reauthorizable" flag — poll `GetAuthorizedPayment`, inspect `Status`/`ExpirationTime`; an ineligible reauthorize surfaces as 422 via `TryGetError`. Status-level gating beyond the documented day-window: **UNVERIFIED** (live-traffic only) — treat `VOIDED`/`CAPTURED`/`DENIED` as terminal in our own state machine and never attempt reauthorize on them. |
| Error | Case A — `SdkException<ReauthorizePaymentError>`: `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` |

**`client.Payments.VoidPayment`** — release the hold · map: `operations/Payments.md`

| Aspect | Contract |
|---|---|
| Signature | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 nullable params must be passed explicitly. NOTE the unusual order: `payPalAuthAssertion` comes BEFORE `payPalRequestId` here — use named arguments |
| Returns | `PaymentAuthorization` — expect `Status` = `AuthorizationStatus.Voided` |
| Constraint (doc note) | You cannot void an authorized payment that has been fully captured |
| Error | Case A — `SdkException<VoidPaymentError>`: `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` |

**`client.Payments.RefundCapturedPayment`** — full/partial refund, idempotency-keyed · map: `operations/Payments.md`

| Aspect | Contract |
|---|---|
| Signature | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullable params must be passed explicitly |
| Idempotency | `payPalRequestId` parameter ⇒ `PayPal-Request-Id` header. Same key must be reused for retries of the SAME refund; a distinct key per DISTINCT partial refund. Exact replay behaviour (2xx-replay of original vs 409/422) is **UNVERIFIED** from map/source — defensive directive: persist `(idempotencyKey → refund id)` ourselves; on any error replaying a key, call `GetRefund`/reconcile by key before retrying with a new key; never auto-retry a refund with a fresh key. |
| Request body | `RefundRequest`: `Amount (amount): Money?` — set for PARTIAL refund; for FULL refund pass an empty `new RefundRequest()` (doc: "for a full refund, include an empty payload") · `InvoiceId (invoice_id): string?`, `CustomId (custom_id): string?`, `NoteToPayer (note_to_payer): string?` optional |
| Returns | `Refund`: `Id (id): string?` ← **refund id** · `Status (status): RefundStatus?` ← **refund status** · `StatusDetails (status_details): RefundStatusDetails?` → `.Reason (reason): RefundIncompleteReason?` · `Amount (amount): Money?` · `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount (total_refunded_amount)`) · `InvoiceId`, `CustomId`, `Links`, `CreateTime` |
| Error | Case A — `SdkException<RefundCapturedPaymentError>`: `TryGetError(out Error)` [400, 401, 403, 404, **409**, **422**] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` |
| Companion read | `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? = null, CancellationToken ct = default)` → `Refund`; errors [401, 403, 404, 500] |

**`client.Vault.CreateSetupToken`** — begin card vaulting · map: `operations/Vault.md`, models: `records-2-Pa-Ve.md`

| Aspect | Contract |
|---|---|
| Signature | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must be passed explicitly |
| Request body | `SetupTokenRequest`: `Customer (customer): Customer?` · `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req` → `.Card (card): SetupTokenRequestCard?` → `Number (number)`, `Expiry (expiry)`, `SecurityCode (security_code)`, `Name (name)`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`, `VerificationMethod (verification_method): VaultCardVerificationMethod?`, `ExperienceContext (experience_context): VaultCardExperienceContext?` |
| Customer linkage | `Customer.MerchantCustomerId (merchant_customer_id): string?` ← **set OUR user id HERE** — `[StringLength(64, MinimumLength = 1)]`, `[RegularExpression("^[0-9a-zA-Z-_.^*$@#]+$")]` (`@`/`.` allowed). `Customer.Id (id): string?` ← **do NOT set at creation** — doc: "unique ID for a customer generated by PayPal", `[StringLength(22, MinimumLength = 1)]`, `[RegularExpression("^[0-9a-zA-Z_-]+$")]` (no `@`/`.`; an email here is rejected 422 `INVALID_PARAMETER_SYNTAX`); read it back from `PaymentTokenResponse.Customer.Id` / `VaultResponseCustomer.Id` and persist it (source: `Models/Customer.cs`). Listing: the `ListCustomerPaymentTokens(customerId)` param doc says "identifier representing a specific customer in merchant's/partner's system or records" (`Api/Vault.cs`) — pass the same value you set as `merchant_customer_id`; the two generated docs are in tension about the id space, so if listing by your own id fails or empty-lists, fall back to the PayPal `customer.id` from the token response (**UNVERIFIED** which space `customer_id` filters on — verify once in sandbox). |
| Returns | `SetupTokenResponse`: `Id (id): string?` ← setup token id · `Status (status): PaymentTokenStatus? = PaymentTokenStatus.Created` · `Customer (customer): Customer?` · `PaymentSource`, `Links` |
| Error | Case A — `SdkException<CreateSetupTokenError>`: `TryGetError1(out Error1)` [400, 403, 422, 500] · `TryGetRawError(out RawError)`. `Error1`: `Name`, `Message`, `DebugId` required; `Details: IReadOnlyList<ErrorDetails1>?` (`Issue (issue): string !req`); `Links: IReadOnlyList<ErrorLinkDescription>?` — **`ErrorLinkDescription.Rel (rel)` is nullable** (live API omits it on some errors) |

**`client.Vault.CreatePaymentToken`** — mint a reusable vaulted-card token · map: `operations/Vault.md`

| Aspect | Contract |
|---|---|
| Signature | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must be passed explicitly |
| Request body | `PaymentTokenRequest`: `Customer (customer): Customer?` (same linkage as above) · `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` with EITHER `.Token (token): VaultTokenRequest?` → `Id (id): string !req` = setup-token id, `Type (type): VaultTokenRequestType !req` = `VaultTokenRequestType.SetupToken` (setup-token → payment-token flow) OR `.Card (card): PaymentTokenRequestCard?` (`Number`, `Expiry`, `SecurityCode`, `Name`, `Brand`, `BillingAddress`) for direct card → payment token in one call |
| Returns | `PaymentTokenResponse`: `Id (id): string?` ← **vault payment token id — this is the value stored on our user and later passed as `CardRequest.VaultId`** · `Customer (customer): CustomerResponse?` (`Id`, `MerchantCustomerId`) · `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `.Card (card): CardPaymentTokenEntity?` (`LastDigits`, `Brand`, `Expiry`, `BillingAddress`) · `Links` |
| Error | Case A — `SdkException<CreatePaymentTokenError>`: `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` |

**`client.Vault.ListCustomerPaymentTokens` / `GetPaymentToken` / `DeletePaymentToken`** · map: `operations/Vault.md`

| Aspect | Contract |
|---|---|
| List signature | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `customerId` = the merchant-side customer id you set as `merchant_customer_id` (id-space caveat in the Customer linkage row above); wire params `customer_id`, `page_size`, `page`, `total_required` |
| List returns | `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Customer (customer): VaultResponseCustomer?` · `Links`. Paginate manually: loop `page = 1..TotalPages` (pass `totalRequired: true` so totals are populated); default `pageSize` is only 5 — pass an explicit larger `pageSize` |
| Get | `GetPaymentToken(string id, RequestOptions? = null, CancellationToken ct = default)` → `PaymentTokenResponse`; errors `TryGetError1` [403, 404, 422, 500] |
| Delete | `DeletePaymentToken(string id, RequestOptions? = null, CancellationToken ct = default)` → `void` (Task); errors `TryGetError1` [400, 403, 500] |

**`client.TransactionSearch.SearchTransactions`** — reconciliation · map: `operations/TransactionSearch.md`, models: `records-2-Pa-Ve.md`

| Aspect | Contract |
|---|---|
| Signature | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 8 nullable params (`transactionId`…`terminalId`) have NO default: pass explicitly (`null`). `startDate`/`endDate` are ISO-8601 strings (wire `start_date`/`end_date`) |
| Pagination | NO SDK auto-pager — manual loop: call with `page: 1`, read `TotalPages`, keep fetching `page: n+1` until `page >= TotalPages` (or until a page returns 0 items). `pageSize` default 100 |
| Returns | `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` · `Page (page): int?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `StartDate`, `EndDate`, `LastRefreshedDatetime`, `Links` |
| Per-transaction fields | `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?` → `TransactionId (transaction_id): string?` · `TransactionInitiationDate (transaction_initiation_date): string?` · `TransactionUpdatedDate (transaction_updated_date): string?` · `TransactionAmount (transaction_amount): Money?` · `FeeAmount (fee_amount): Money?` · `TransactionStatus (transaction_status): string?` — **plain string, NOT an enum** · `InvoiceId (invoice_id): string?` · `CustomField (custom_field): string?` · `PaypalReferenceId`, `PaypalReferenceIdType` |
| Order linkage | SET at payment time: `PurchaseUnitRequest.CustomId (custom_id)` and `.InvoiceId (invoice_id)` on create-order (both flow onto the authorization/capture records: `AuthorizationWithAdditionalData.CustomId/InvoiceId`, `CapturedPayment.CustomId/InvoiceId`; also settable per-capture via `CaptureRequest.InvoiceId`, per-refund via `RefundRequest.InvoiceId/CustomId`). READ at reconcile time: `TransactionInformation.InvoiceId` / `.CustomField`. Whether the live wire actually populates `custom_field` from purchase-unit `custom_id`: **UNVERIFIED** — defensive directive: set BOTH `custom_id` and `invoice_id` to our order id at create time, reconcile on `invoice_id` first and fall back to `custom_field`, and verify the mapping once in sandbox before relying on it |
| Doc caveats | Transactions appear up to **3 hours** after execution; lookback limited to the previous **3 years** |
| Error | **Case B (the only one in the SDK)** — `SdkException<RawError>`: `.Error.StatusCode: HttpStatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()`, `.Error.ReadAsBytes()`. No typed accessors |

### 2c. Enum values needed (all `PayPalServerSdk.Models.Enums`, `StringEnum<T>` records — use static members or `Type.FromValue("wire")`) · map: `models/enums.md`

| Enum | Members (C# member = wire value) |
|---|---|
| `CheckoutPaymentIntent` | `Capture` (CAPTURE), `Authorize` (AUTHORIZE) |
| `OrderStatus` | `Created` (CREATED), `Saved` (SAVED), `Approved` (APPROVED), `Voided` (VOIDED), `Completed` (COMPLETED), `PayerActionRequired` (PAYER_ACTION_REQUIRED) |
| `AuthorizationStatus` | `Created` (CREATED), `Captured` (CAPTURED), `Denied` (DENIED), `PartiallyCaptured` (PARTIALLY_CAPTURED), `Voided` (VOIDED), `Pending` (PENDING) |
| `AuthorizationIncompleteReason` | `PendingReview` (PENDING_REVIEW), `DeclinedByRiskFraudFilters` (DECLINED_BY_RISK_FRAUD_FILTERS) |
| `CaptureStatus` | `Completed` (COMPLETED), `Declined` (DECLINED), `PartiallyRefunded` (PARTIALLY_REFUNDED), `Pending` (PENDING), `Refunded` (REFUNDED), `Failed` (FAILED) |
| `CaptureIncompleteReason` | `BuyerComplaint`, `Chargeback`, `Echeck`, `InternationalWithdrawal`, `Other`, `PendingReview`, `ReceivingPreferenceMandatesManualAction`, `Refunded`, `TransactionApprovedAwaitingFunding`, `Unilateral`, `VerificationRequired`, `DeclinedByRiskFraudFilters` (wire = SCREAMING_SNAKE) |
| `RefundStatus` | `Cancelled` (CANCELLED), `Failed` (FAILED), `Pending` (PENDING), `Completed` (COMPLETED) |
| `RefundIncompleteReason` | `Echeck` (ECHECK) |
| `PaymentTokenStatus` | `Created` (CREATED), `PayerActionRequired` (PAYER_ACTION_REQUIRED), `Approved` (APPROVED), `Vaulted` (VAULTED), `Tokenized` (TOKENIZED) |
| `VaultTokenRequestType` | `SetupToken` (SETUP_TOKEN) |
| `VaultCardVerificationMethod` | `ScaWhenRequired` (SCA_WHEN_REQUIRED), `ScaAlways` (SCA_ALWAYS) |
| `CardBrand` | `Visa` (VISA), `Mastercard` (MASTERCARD), `Discover` (DISCOVER), `Amex` (AMEX), … (29 members; full list in `enums.md`) |
| `PaymentInitiator` | `Customer` (CUSTOMER), `Merchant` (MERCHANT) |
| `StoredPaymentSourcePaymentType` | `OneTime` (ONE_TIME), `Recurring` (RECURRING), `Unscheduled` (UNSCHEDULED) |
| `StoredPaymentSourceUsageType` | `First` (FIRST), `Subsequent` (SUBSEQUENT), `Derived` (DERIVED) |
| `ProcessorResponseCode` / `AvsCode` / `CvvCode` / `PaymentAdviceCode` | large decline-code lists — see `enums.md` when mapping declines |

Vaulted-card subsequent use: `CardRequest.VaultId` + `CardRequest.StoredCredential` = `CardStoredCredential` with `PaymentInitiator (payment_initiator): PaymentInitiator !req` (e.g. `PaymentInitiator.Merchant` for merchant-initiated), `PaymentType (payment_type): StoredPaymentSourcePaymentType !req`, `Usage (usage): StoredPaymentSourceUsageType? = Derived` (e.g. `StoredPaymentSourceUsageType.Subsequent`).

### 2d. Error handling — statuses to expect (grounded in each operation's accessor table)

| Scenario | Operation | Status(es) the SDK surfaces | Read via |
|---|---|---|---|
| Card declined | CreateOrder / AuthorizeOrder | 422 (`TryGetError`) — inspect `Error.Details[].Issue` (plain `string`, not an enum — match defensively, exact issue strings **UNVERIFIED** from map). ALSO check the 2xx path: `AuthorizationWithAdditionalData.Status == AuthorizationStatus.Denied` + `ProcessorResponse` codes | `TryGetError(out Error)` / response model |
| Authorization expired / capture on voided or expired auth | CaptureAuthorizedPayment | 422 or 409 (`TryGetError`); 404 if the id is unknown | `TryGetError(out Error)` |
| Refund exceeds captured amount | RefundCapturedPayment | 422 or 409 (`TryGetError`) | `TryGetError(out Error)` |
| Duplicate idempotency-key replay | any op with `payPalRequestId` | **UNVERIFIED** — map/source do not state the replay status; defensive directive in the Refund row above | — |
| Auth/config failure | any | 401/403 via `TryGetError` (or `TryGetError1`/`TryGetDefaultError`); check credentials, environment, base URL first | typed accessor |
| Transaction search failure | SearchTransactions | any status — Case B only | `SdkException<RawError>.Error.StatusCode` + `ReadAsString()` |

## 3. Trap notes

> ⚠ Step 1 (client registration) — the SDK client wrapper's lifetime and the underlying `HttpClient`/handler pipeline's lifetime are not the same thing; registering both naively in DI is how you get socket exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before wiring the client into eShopOnWeb's service collection.

> ⚠ Step 1 (auth) — credentials must be set before the client is constructed and come from configuration/secret store, never hardcoded; where token caching/refresh sits vs. where credential rotation hooks in is not visible from the options shape. **MUST load `dotnet-authentication`**.

> ⚠ Steps 3–10 (every call) — many nullable parameters carry no C# default and mis-bind in positional calls (`VoidPayment` even orders `payPalAuthAssertion` before `payPalRequestId`); call every operation with named arguments, and the cancellation token argument is `ct:`. **MUST load `dotnet-calling-endpoints`**.

> ⚠ Steps 3–10 (models) — SDK enums are `StringEnum<T>` records, not C# enums (equality, `switch`, and `.ToString()` behave differently than expected); `required` members must be set in the initializer; JSON fields with no model property are silently dropped on deserialize. **MUST load `dotnet-models`**.

> ⚠ Step 2 (error boundary) — which operations are Case A (typed) vs Case B (`RawError`) differs per operation (`SearchTransactions` is the lone Case B here), and `TryGetRawError` on a typed error is not a catch-all. **MUST load `dotnet-error-handling`**.

> ⚠ Step 1 (resilience) — what `RetryOptions.Timeout` actually bounds vs. the `HttpClient` timeout, and whether a failed non-idempotent POST (capture, refund) can be re-sent by the retry layer, decide where idempotency keys are mandatory and how timeouts are configured. **MUST load `dotnet-configuration-resilience`**.

> ⚠ Testing — the SDK's test seam is the `HttpClient` constructor argument, not an interface over the client. **MUST load `dotnet-testing`** before stubbing.

## 4. REQUIRED READING

Load ALL of these **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1: client construction, `HttpClient` ownership, DI registration.
- `dotnet-authentication` — Step 1: OAuth client-credentials wiring, secret loading, rotation.
- `dotnet-calling-endpoints` — Steps 3–10: controller/method usage, named-argument discipline, async/cancellation.
- `dotnet-models` — Steps 3–10: building request records, `StringEnum<T>` handling, required members, wire-name mapping.
- `dotnet-error-handling` — Step 2: the exception boundary, Case A/B mechanics, safe status/body reads.
- `dotnet-configuration-resilience` — Step 1: retry/timeout semantics, base-URL selection, pagination, logging.
- `dotnet-testing` — test seam and coverage of error/edge paths.

Two hazard rows that must shape the error boundary from day one:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

- **Assumption — Sandbox only.** `ServerEnvironment` has exactly one member, `Sandbox` (source-confirmed at v1.0.1). The brief targets a sandbox business account, so this is fine; any future production move happens via `options.Server.Default.Sandbox.BaseUrl` override (which also covers the OAuth token request), not via an environment enum.
- **Assumption — single purchase unit per order.** The read paths above index `PurchaseUnits[0]`; eShopOnWeb orders map to one purchase unit carrying the full order total.
- **Assumption — vault customer linkage.** Our user id goes in `Customer.MerchantCustomerId` (regex allows `@`/`.`, max 64); `Customer.Id` is PayPal-generated (max 22, `^[0-9a-zA-Z_-]+$`) and is read back from token responses, never set at creation (source: `Models/Customer.cs`). `ListCustomerPaymentTokens(customerId)` is documented as taking a merchant-system identifier (`Api/Vault.cs`), but the two generated docs disagree on the id space — **UNVERIFIED**; directive: list by the `merchant_customer_id` value, fall back to the PayPal `customer.id`.
- **Assumption — full refund via empty body.** Doc note says full refund = empty payload; plan passes `new RefundRequest()` (all members optional) rather than `null` so a JSON object is actually sent.
- **UNVERIFIED (live-traffic only), with defensive directives inline above:** exact `PayPal-Request-Id` duplicate-replay status; exact `Error.Details[].Issue` strings for decline/expiry/over-refund; whether transaction-search `custom_field` is populated from purchase-unit `custom_id`; authorization-status-level gating of reauthorize beyond the documented day-4-to-29 window.
- **Blockers:** none.
