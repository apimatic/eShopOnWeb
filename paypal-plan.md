# paypal-plan.md — eShopOnWeb × PayPalServerSdk integration plan

SDK: `AsadAli.Checkout.Sdk` (NuGet, install version-less) · root namespace `PayPalServerSdk` · client `PayPalServerSdkClient` · options `PayPalServerSdkClientOptions` · map provenance: source commit `9653d18`, tag `v1.0.1` (sdk-map.md).

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Client construction, OAuth2 client-credentials auth, environment + base-URL override, retry/timeout surface | — (client setup) |
| 2 | Create order, AUTHORIZE intent, amount = eShop order total, currency from config; pay with raw card OR vaulted token | `Orders.CreateOrder` |
| 3 | Read authorization id + status (from create response; fall back to get-order / authorize-order) | `Orders.CreateOrder` → `Orders.GetOrder` / `Orders.AuthorizeOrder` |
| 4 | Capture at fulfilment; read captured amount, PayPal fee, net proceeds | `Payments.CaptureAuthorizedPayment` |
| 5 | Stale/expired authorization → reauthorize; detect no-longer-reauthorizable | `Payments.ReauthorizePayment`, `Payments.GetAuthorizedPayment` |
| 6 | Void on cancel (release held funds) | `Payments.VoidPayment` |
| 7 | Refund after capture, full or partial, caller-supplied idempotency key; read refund id + status | `Payments.RefundCapturedPayment`, `Payments.GetRefund` |
| 8 | Vault: setup-token flow and direct card vault; list tokens per customer; delete token; vaulted token as order payment_source; customer-id mapping | `Vault.CreateSetupToken`, `Vault.CreatePaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.DeletePaymentToken`, `Vault.GetPaymentToken` |
| 9 | Transaction search by date range, page through ALL results | `TransactionSearch.SearchTransactions` |
| 10 | Error boundary: typed/raw errors, auth-expired / over-refund / card-declined detection | all of the above |

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

### 2.0 Client construction, auth, environment, base-URL override (map: sdk-map.md *Getting a client* / *Servers & auth*; source: `PayPalServerSdkClientOptions.cs`, `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/ServerEnvironment.cs`, `AuthSchemes.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`, `Core/Authentication/OAuth2/OAuth2Scheme.cs`, `ServiceCollectionExtensions.cs`)

| Fact | Contract |
|---|---|
| Constructor | `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` — both in namespace `PayPalServerSdk` |
| Options members | `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Logging: LoggingOptions` · `Server: ServerOptions` · `Oauth2: OAuth2ClientCredentials?` · `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?` |
| Credentials | `Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = … }` — `ClientId`/`ClientSecret` are `required string`, `Scope: string?` optional. Namespace `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`. If `Oauth2` is left null the client silently sends **unauthenticated** requests (`NoneAuthScheme`) |
| Token behaviour | SDK fetches `POST /v1/oauth2/token` lazily on first call (HTTP-Basic with `clientId:clientSecret`, form body `grant_type=client_credentials`), caches the token until expiry, thread-safe; no token code to write |
| Environment | `ServerEnvironment` (namespace `PayPalServerSdk.Servers`) declares **only `ServerEnvironment.Sandbox`** — there is no Production member. Production = keep `Environment = ServerEnvironment.Sandbox` and override the base URL (next row) |
| **Base-URL override (applies to EVERY call, OAuth token included)** | `options.Server.Default.Sandbox.BaseUrl = "<PayPal:BaseUrl>"`. Chain: `ServerOptions.Default` (`DefaultOptions`, ns `PayPalServerSdk.Servers`) → `.Sandbox` (`DefaultOptions.SandboxOptions`) → `.BaseUrl` (default `"https://api-m.sandbox.paypal.com"`). The OAuth token URL is built as `server.Default("/v1/oauth2/token")` resolved through the **same** `DefaultOptions` (`AuthSchemes.cs`), so this one override covers the token request and all API calls verbatim |
| Retry surface | `RetryOptions` (ns `PayPalServerSdk.Core.Configuration`): `StatusCodesToRetry: IReadOnlyList<HttpStatusCode>` · `HttpMethodsToRetry: IReadOnlyList<HttpMethod>` · `MaxRetries: int` · `Delay: TimeSpan` · `Timeout: TimeSpan?` · `BackOffFactor: int` · `UseExponentialBackoff: bool` · `MaxJitter: TimeSpan` · `OnRetry: Action<RetryAttempt>?` — all members `required`; start from `RetryOptions.Default()` and mutate |
| Logging surface | `LoggingOptions` (record, ns `PayPalServerSdk.Core.Configuration`): `LoggerFactory`, `LogRequestHeaders`, `LogResponseHeaders`, `LogRequestBody`, `BodySizeLimit = 32*1024`, `RedactedHeaders`, `RedactedKeys` (already masks `client_secret`, `access_token`, …), `RedactionPlaceholder = "***"` |
| Per-call `RequestOptions` | `PayPalServerSdk.Core.RequestOptions` — only member is `LogLevel?`. **No per-call headers/timeout hook**; idempotency and `Prefer` go through the dedicated parameters below |
| DI helper | `services.AddPayPalServerSdkClient(o => { … })` exists (`ServiceCollectionExtensions.cs`, registers a singleton over `IHttpClientFactory`) but is declared as a C# 14 `extension` member — a host project compiled with an older C# toolchain may not bind it. Toolchain-agnostic path: register your own factory that calls the constructor. **MUST load `dotnet-client-initialization`** |

Namespaces to add as `using`s: `PayPalServerSdk` (client/options/`ServerOptions`), `PayPalServerSdk.Servers` (`ServerEnvironment`, `DefaultOptions`), `PayPalServerSdk.Api` (controllers — usually not needed), `PayPalServerSdk.Models` (records incl. `Error`, `Error1`), `PayPalServerSdk.Models.Enums` (all enums), `PayPalServerSdk.Errors` (`{Operation}Error` types), `PayPalServerSdk.Core` (`RequestOptions`), `PayPalServerSdk.Core.Configuration` (`RetryOptions`, `LoggingOptions`), `PayPalServerSdk.Core.Exceptions` (`SdkException<T>`), `PayPalServerSdk.Core.ErrorResponse` (`RawError`, `ApiError`), `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` (`OAuth2ClientCredentials`).

### 2.1 Orders (`client.Orders`, source `Api/Orders.cs`; map page operations/Orders.md)

| Operation | Signature (verbatim) | Returns | Error |
|---|---|---|---|
| CreateOrder | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 5 nullable params before `body` **must be passed explicitly** (pass `null`) | `Order` | Case A `SdkException<CreateOrderError>`: `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` |
| GetOrder | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `fields`, `payPalMockResponse`, `payPalAuthAssertion` must be passed explicitly (`null`) | `Order` | Case A `SdkException<GetOrderError>`: `TryGetError(out Error)` [401, 404] · `TryGetRawError` |
| AuthorizeOrder (only if the create response carries no authorization) | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — pass `body: null` when the order already has its payment source | `OrderAuthorizeResponse` | Case A `SdkException<AuthorizeOrderError>`: `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError` |

Request models (records-1-Ac-Pa.md, records-2-Pa-Ve.md; `!req` = C# `required`):

- `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req` → use `CheckoutPaymentIntent.Authorize` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` · `PaymentSource (payment_source): PaymentSource?` · `Payer (payer): Payer?` · `ApplicationContext (application_context): OrderApplicationContext?`
- `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req` · `ReferenceId (reference_id): string?` · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `Description (description): string?`
- `AmountWithBreakdown`: `CurrencyCode (currency_code): string !req` · `Value (value): string !req` · `Breakdown (breakdown): AmountBreakdown?`. **Amounts are strings** — format the eShop total with invariant culture, 2 decimals (`total.ToString("F2", CultureInfo.InvariantCulture)`); `CurrencyCode` from config.
- `PaymentSource` (set exactly one): `Card (card): CardRequest?` · `Token (token): Token?` · (`Paypal`, `ApplePay`, … out of scope)
- `CardRequest` (raw card): `Number (number): string?` · `Expiry (expiry): string?` (format `"YYYY-MM"`) · `SecurityCode (security_code): string?` · `Name (name): string?` · `BillingAddress (billing_address): Address?` · `Attributes (attributes): CardAttributes?` · `VaultId (vault_id): string?`. Sandbox test card: `Number = "4111111111111111"`.
- `Address`: `CountryCode (country_code): string !req` · `AddressLine1 (address_line_1)` · `AddressLine2 (address_line_2)` · `AdminArea2 (admin_area_2)` (city) · `AdminArea1 (admin_area_1)` (state) · `PostalCode (postal_code)` — all `string?`
- `Token` (vaulted token as payment source): `Id (id): string !req` = the vault payment-token id · `Type (type): TokenType !req`. ⚠ `TokenType` declares **only** `TokenType.BillingAgreement` (`"BILLING_AGREEMENT"`); for a vault payment token construct the wire value via `TokenType.FromValue("PAYMENT_METHOD_TOKEN")` — `UNVERIFIED`: the generated enum is incomplete relative to the vault flow it must serve, and only live traffic confirms the wire value the Orders API accepts (evidence: operations/Vault.md returns payment-token ids meant for `payment_source.token`, while enums.md lists no vault member on `TokenType`).
- `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` (same card/token slots) — pass `body: null` when unused.

Response envelope (create/get → `Order`; authorize → `OrderAuthorizeResponse`, same shape):

- `Order.Id (id): string?` · `Order.Status (status): OrderStatus?` · `Order.PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`
- `PurchaseUnit.Payments (payments): PaymentCollection?` → `PaymentCollection.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`
- `AuthorizationWithAdditionalData.Id (id): string?` ← **the authorization id to persist** · `.Status (status): AuthorizationStatus?` · `.Amount (amount): Money?` · `.ExpirationTime (expiration_time): string?` · `.ProcessorResponse (processor_response): ProcessorResponse?`
- `Money`: `CurrencyCode (currency_code): string !req` · `Value (value): string !req`
- Whether a create with `payment_source` + AUTHORIZE intent returns the authorization inline in `PurchaseUnits[0].Payments.Authorizations` (vs requiring a follow-up `AuthorizeOrder`) is `UNVERIFIED` from the map — **directive**: read the create response's `Authorizations` first; if null/empty, call `AuthorizeOrder(id, null, null, null, null, body: null)` and read `OrderAuthorizeResponse.PurchaseUnits[0].Payments.Authorizations` instead. `GetOrder` re-reads the same envelope any time.

### 2.2 Payments — capture / reauthorize / void / refund (`client.Payments`, source `Api/Payments.cs`; map page operations/Payments.md)

| Operation | Signature (verbatim) | Returns | Error |
|---|---|---|---|
| CaptureAuthorizedPayment | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullable params must be passed explicitly | `CapturedPayment` | Case A `SdkException<CaptureAuthorizedPaymentError>`: `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| ReauthorizePayment | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentAuthorization` | Case A `SdkException<ReauthorizePaymentError>`: `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| VoidPayment | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — ⚠ nullable-param order differs from capture: `payPalAuthAssertion` comes **before** `payPalRequestId` here | `PaymentAuthorization` | Case A `SdkException<VoidPaymentError>`: `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| RefundCapturedPayment | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `Refund` | Case A `SdkException<RefundCapturedPaymentError>`: `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| GetAuthorizedPayment | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentAuthorization` | Case A `SdkException<GetAuthorizedPaymentError>`: `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError` |
| GetCapturedPayment | `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CapturedPayment` | Case A `SdkException<GetCapturedPaymentError>`: `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError` |
| GetRefund | `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `Refund` | Case A `SdkException<GetRefundError>`: `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError` |

Request/response models (records-1-Ac-Pa.md, records-2-Pa-Ve.md):

- **Capture** — `CaptureRequest`: `Amount (amount): Money?` · `InvoiceId (invoice_id): string?` · `FinalCapture (final_capture): bool? = false` · `NoteToPayer (note_to_payer): string?` · `SoftDescriptor (soft_descriptor): string?`. Pass `body: null` to capture the full authorized amount, or set `Amount` for a partial.
- **Capture response** — `CapturedPayment`: `Id (id): string?` · `Status (status): CaptureStatus?` · `StatusDetails (status_details): CaptureStatusDetails?` (`.Reason: CaptureIncompleteReason?`) · `Amount (amount): Money?` · `FinalCapture (final_capture): bool?` · `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?` · `ProcessorResponse (processor_response): ProcessorResponse?`.
  - `SellerReceivableBreakdown` (the money-out fields; map warns it is **absent for pending captures** — null-check): `GrossAmount (gross_amount): Money !req` = captured amount · `PaypalFee (paypal_fee): Money?` = PayPal processing fee · `NetAmount (net_amount): Money?` = net proceeds to merchant · also `PaypalFeeInReceivableCurrency (paypal_fee_in_receivable_currency)`, `ReceivableAmount (receivable_amount)`, `ExchangeRate (exchange_rate)`, `PlatformFees (platform_fees)`.
- **Reauthorize** — `ReauthorizeRequest`: `Amount (amount): Money?` (the only supported request field). Constraints from the operation contract (operations/Payments.md): reauthorize only from day 4 to day 29 after the 3-day honor period; at 30+ days the authorization is dead — create a new order/authorization instead; amount capped (US: ≤115% of original and ≤ +$75).
- **Reauthorize/void/get-auth response** — `PaymentAuthorization`: `Id (id)` · `Status (status): AuthorizationStatus?` · `StatusDetails (status_details): AuthorizationStatusDetails?` (`.Reason: AuthorizationIncompleteReason?`) · `Amount (amount): Money?` · `ExpirationTime (expiration_time): string?` · `CreateTime (create_time)`.
- **Refund** — `RefundRequest`: `Amount (amount): Money?` · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `NoteToPayer (note_to_payer): string?`. **Full refund**: the API wants an *empty payload* — pass `body: new RefundRequest()` (all members optional). **Partial refund**: set `Amount = new Money { CurrencyCode = …, Value = … }`.
- **Refund response** — `Refund`: `Id (id): string?` ← refund id · `Status (status): RefundStatus?` · `StatusDetails (status_details): RefundStatusDetails?` (`.Reason: RefundIncompleteReason?`) · `Amount (amount): Money?` · `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (`.TotalRefundedAmount (total_refunded_amount): Money?`, `.GrossAmount`, `.PaypalFee`, `.NetAmount`).
- **Idempotency (`PayPal-Request-Id`)** — the caller-supplied key goes in the **`payPalRequestId` parameter** (present on `CreateOrder`, `CaptureAuthorizedPayment`, `ReauthorizePayment`, `VoidPayment`, `RefundCapturedPayment`, and both Vault creates); the SDK emits it as the `PayPal-Request-Id` header verbatim (source: `Api/Payments.cs` request-builder list). API doc on the parameter: "The server stores keys for 45 days." Generate one key per logical operation (e.g. `refund-{captureId}-{refundIdFromUs}`) and reuse it on retries.

### 2.3 Vault / saved cards (`client.Vault`, source `Api/Vault.cs`; map page operations/Vault.md)

| Operation | Signature (verbatim) | Returns | Error |
|---|---|---|---|
| CreateSetupToken | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must be passed explicitly | `SetupTokenResponse` | Case A `SdkException<CreateSetupTokenError>`: `TryGetError1(out Error1)` [400, 403, 422, 500] · `TryGetRawError` |
| CreatePaymentToken | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must be passed explicitly | `PaymentTokenResponse` | Case A `SdkException<CreatePaymentTokenError>`: `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError` |
| ListCustomerPaymentTokens | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` — query wires: `customer_id` ← `customerId`, `page_size`, `page`, `total_required` | `CustomerVaultPaymentTokensResponse` | Case A `SdkException<ListCustomerPaymentTokensError>`: `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError` |
| GetPaymentToken | `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenResponse` | Case A `SdkException<GetPaymentTokenError>`: `TryGetError1(out Error1)` [403, 404, 422, 500] · `TryGetRawError` |
| DeletePaymentToken | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `void` (`Task`) — success = no throw | Case A `SdkException<DeletePaymentTokenError>`: `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError` |
| GetSetupToken | `GetSetupToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `SetupTokenResponse` | Case A `SdkException<GetSetupTokenError>`: `TryGetError1(out Error1)` [403, 404, 422, 500] · `TryGetRawError` |

What the SDK exposes (both flows exist):

- **Direct vault** — `CreatePaymentToken` with `PaymentTokenRequest { Customer = …, PaymentSource = new PaymentTokenRequestPaymentSource { Card = new PaymentTokenRequestCard { Number, Expiry, SecurityCode, Name, Brand, BillingAddress } } }`.
- **Setup-token flow** — (1) `CreateSetupToken` with `SetupTokenRequest { Customer = …, PaymentSource = new SetupTokenRequestPaymentSource { Card = new SetupTokenRequestCard { Number, Expiry, SecurityCode, Name, Brand, BillingAddress, VerificationMethod (verification_method): VaultCardVerificationMethod? , ExperienceContext (experience_context): VaultCardExperienceContext? } } }` → `SetupTokenResponse.Id`; (2) `CreatePaymentToken` with `PaymentSource = new PaymentTokenRequestPaymentSource { Token = new VaultTokenRequest { Id = <setupTokenId>, Type = VaultTokenRequestType.SetupToken } }` (`VaultTokenRequest.Id (id): string !req`, `.Type (type): VaultTokenRequestType !req`; the only declared member is `SetupToken` = `"SETUP_TOKEN"`).
- Models: `PaymentTokenRequest.Customer (customer): Customer?` · `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` (`Card (card): PaymentTokenRequestCard?` / `Token (token): VaultTokenRequest?`). `SetupTokenRequest` mirrors it with `SetupTokenRequestPaymentSource` (`Card`/`Paypal`/`Venmo`/`ApplePay`/`Token`/`Bank`).
- `Customer`: `Id (id): string?` · `MerchantCustomerId (merchant_customer_id): string?`. **Customer mapping**: `MerchantCustomerId` is the merchant-side key — set it to the shopper's identity (e.g. eShop buyer id); `Id` is PayPal's customer id, returned on responses and **required as `customerId` for `ListCustomerPaymentTokens`**. Persist both against the shopper at first vault.
- Responses: `PaymentTokenResponse.Id (id)` ← **vault token id used as `payment_source.token.id` in orders** · `.Customer (customer): CustomerResponse?` (`Id`, `MerchantCustomerId`) · `.PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `.Card (card): CardPaymentTokenEntity?` (`LastDigits (last_digits)`, `Brand`, `Expiry`). `SetupTokenResponse`: `Id`, `Status (status): PaymentTokenStatus? = Created`, `Customer`, `Links`.
- `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Customer (customer): VaultResponseCustomer?` · `Links`. No SDK pager — loop `page` from 1 while `page < TotalPages`.
- ⚠ Vault typed errors use **`Error1`/`ErrorDetails1`/`ErrorLinkDescription`** (not `Error`): `Error1.Name (name) !req`, `.Message (message) !req`, `.DebugId (debug_id) !req`, `.Details (details): IReadOnlyList<ErrorDetails1>?` (`Issue (issue): string !req`, `Field`, `Value`, `Description`), `.Links: IReadOnlyList<ErrorLinkDescription>?` whose `Rel` is **optional** (records-1-Ac-Pa.md). Accessor name is `TryGetError1`, not `TryGetError`.

### 2.4 Transaction search (`client.TransactionSearch`, source `Api/TransactionSearch.cs`; map page operations/TransactionSearch.md)

| Operation | Signature (verbatim) | Returns | Error |
|---|---|---|---|
| SearchTransactions | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 8 nullable filters **must be passed explicitly** (`null`); use named arguments | `SearchResponse` | **Case B** `SdkException<RawError>`: `.Error.StatusCode: HttpStatusCode` · `.Error.ReadAsString()` · `.Error.ReadAsJson<T>()` — no typed accessors |

- `startDate`/`endDate` (wire `start_date`/`end_date`): RFC 3339 / ISO-8601 strings, required, seconds mandatory (e.g. `DateTimeOffset.UtcNow.AddDays(-7).ToString("o")`); **maximum supported range is 31 days** — chunk longer reconciliation windows into ≤31-day calls (source: `Api/TransactionSearch.cs` param docs). API notes (operations/TransactionSearch.md): transactions appear up to 3 h late; window is the previous 3 years.
- `fields` valid values (source: `Api/TransactionSearch.cs`): `transaction_info` (default), `payer_info`, `shipping_info`, `auction_info`, `cart_info`, `incentive_info`, `store_info`, comma-separated combinations, or `all`. Only `transaction_info` carries `InvoiceId (invoice_id)` / `CustomField (custom_field)` — no other field group's model contains them; whether the live API populates them from purchase-unit `invoice_id`/`custom_id` is `UNVERIFIED`. Reconciliation keys the contract does carry: `transactionId` filter accepts an **order id** (19 chars; transaction ids are 17) — query `SearchTransactions(..., transactionId: <orderId>, ...)` to fetch all transactions for an order; `transactionStatus` filter codes: `D` denied, `P` pending, `S` success, `V` reversed; `transactionAmount` range format `"[500 TO 1005]"` in lower denominations, URL-encoded.
- ⚠ Doc ambiguity (source: `Api/TransactionSearch.cs`): the `page` doc says "zero-relative start index" yet its own example has `page=1` returning the first items and the signature defaults `page = 1` — the sheet's loop (start at `page: 1`, stop when `page >= TotalPages`) stands on the response's own `Page`/`TotalPages` fields, not the doc wording.
- Response envelope — `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` · `Page (page): int?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `StartDate`, `EndDate`, `LastRefreshedDatetime`, `AccountNumber`, `Links (links): IReadOnlyList<LinkDescription>?`.
- Row — `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?`: `TransactionId (transaction_id)` · `TransactionEventCode (transaction_event_code)` · `TransactionInitiationDate (transaction_initiation_date)` · `TransactionUpdatedDate (transaction_updated_date)` · `TransactionAmount (transaction_amount): Money?` · `FeeAmount (fee_amount): Money?` · `TransactionStatus (transaction_status): string?` · `InvoiceId (invoice_id)` · `CustomField (custom_field)` · `PaypalReferenceId (paypal_reference_id)` · `EndingBalance (ending_balance): Money?` · `ProtectionEligibility (protection_eligibility)` (all `string?` unless noted).
- **Paging through ALL results**: no SDK pager (map: "only `page`, no `perPage`"). Loop: call with `page: 1`, read `TotalPages`; re-call with identical filters and `page: 2 … TotalPages`, concatenating `TransactionDetails`. Keep `pageSize` (default 100, max per API is 100 — request `pageSize: 100`) and the date range constant across pages.

### 2.5 Enums actually needed (map page models/enums.md; all ns `PayPalServerSdk.Models.Enums`, all `StringEnum<T>` — construct/compare with static members or `Type.FromValue("wire")`; deserialization accepts **any** wire string, source `Core/Enum/StringEnum.cs`, so undeclared values never throw on read)

| Enum | Members (wire) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created`, `Saved`, `Approved`, `Voided`, `Completed`, `PayerActionRequired` (`CREATED`/`SAVED`/`APPROVED`/`VOIDED`/`COMPLETED`/`PAYER_ACTION_REQUIRED`) |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` — ⚠ no `Expired` member; an `"EXPIRED"` wire value still deserializes (StringEnum accepts any value) — compare via `AuthorizationStatus.FromValue("EXPIRED")`; that wire value itself is `UNVERIFIED` (not in the generated enum) |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `CaptureIncompleteReason` | `BuyerComplaint`, `Chargeback`, `Echeck`, `InternationalWithdrawal`, `Other`, `PendingReview`, `ReceivingPreferenceMandatesManualAction`, `Refunded`, `TransactionApprovedAwaitingFunding`, `Unilateral`, `VerificationRequired`, `DeclinedByRiskFraudFilters` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `RefundIncompleteReason` | `Echeck (ECHECK)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, … (28 members) |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` only — see §2.1 `Token` row for the vault-token directive |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `VaultCardVerificationMethod` | `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `ScaAlways (SCA_ALWAYS)` |
| `ProcessorResponseCode` | `_0000 (0000)`, `_0100 (0100)`, `_0500 (0500)`, `_5100 (5100)`, `_5110 (5110)`, `_5120 (5120)`, `_5140 (5140)`, `_5400 (5400)`, `_5500 (5500)`, `_5700 (5700)`, `_5900 (5900)`, `_5910 (5910)`, `_9500 (9500)` … (full list on enums.md; construct any code via `ProcessorResponseCode.FromValue("…")`) |

### 2.6 Error handling & detection recipes (map: sdk-map.md *Error-handling model*; records-1-Ac-Pa.md)

- Every operation is **throw-only** (no `…Result` variants exist in this SDK). Catch `SdkException<{Operation}Error>` (Case A — everything in scope except search) or `SdkException<RawError>` (Case B — `SearchTransactions` only). `SdkException<T>` (ns `PayPalServerSdk.Core.Exceptions`) exposes `.Error: T`.
- Typed payload `Error` (Orders/Payments): `Name (name): string !req` · `Message (message): string !req` · `DebugId (debug_id): string !req` · `Details (details): IReadOnlyList<ErrorDetails>?` → `Issue (issue): string !req`, `Field (field)`, `Value (value)`, `Description (description)`. Vault uses `Error1`/`ErrorDetails1` (same shape; accessor `TryGetError1`).
- HTTP status per shape is fixed by the accessor list in each operation row above (e.g. capture: `TryGetError` covers 400/401/403/404/409/422, `TryGetNoContent` covers 500). Always end the ladder with `TryGetRawError(out var raw)` for unlisted statuses.
- **Authorization expired / cannot reauthorize**: catch `SdkException<ReauthorizePaymentError>` → `TryGetError(out var e)` → inspect `e.Name` and `e.Details[].Issue` (plain `string`; the SDK does **not** enumerate issue codes, so exact strings are `UNVERIFIED` — match defensively, e.g. 422 + `Name` containing `AUTHORIZATION`, and treat 404 as "unknown authorization"). Also read state first via `GetAuthorizedPayment` → `PaymentAuthorization.Status` / `.ExpirationTime` (see `AuthorizationStatus` row for the undeclared-`EXPIRED` handling). At 30+ days the API contract says reauthorize is impossible — create a new order instead (operations/Payments.md notes).
- **Capture already refunded / refund exceeds amount**: catch `SdkException<RefundCapturedPaymentError>` → `TryGetError` covers 409 (conflict — e.g. refund already in flight) and 422 (unprocessable — e.g. amount exceeds remaining) → inspect `Name`/`Details[].Issue` (exact issue strings `UNVERIFIED` — same defensive matching). Current state via `GetCapturedPayment` → `CapturedPayment.Status` (`PartiallyRefunded`/`Refunded`) and `GetRefund` → `Refund.Status`.
- **Card declined**: usually **not** an exception — a 2xx create/authorize/capture with `Status` = `AuthorizationStatus.Denied` / `CaptureStatus.Declined`, with `ProcessorResponse (processor_response): ProcessorResponse?` → `ResponseCode (response_code): ProcessorResponseCode?`, `AvsCode (avs_code)`, `CvvCode (cvv_code)`, `PaymentAdviceCode (payment_advice_code)` carrying the processor detail. Check status enums on the 2xx path **and** keep the 422 `Error` branch for validation-style rejections.

## 3. Trap notes

> ⚠ Step 1 (client registration) — the DI helper `AddPayPalServerSdkClient` is a C# 14 `extension` member and registers a **singleton**; how the `HttpClient`/handler pipeline must be owned and recycled, and whether the helper even binds from the host project's toolchain, are not visible from the signature. **MUST load `dotnet-client-initialization`** before wiring the client into eShopOnWeb's service collection.

> ⚠ Step 1 (auth) — credentials must be set on `options.Oauth2` before construction and loaded from configuration, never hardcoded; what the SDK caches vs what you must rotate yourself is not visible from the property list. **MUST load `dotnet-authentication`**.

> ⚠ Steps 2–9 (every call) — most operations carry nullable parameters with **no C# default** that mis-bind or fail to compile in a positional call; call with named arguments and pass `null` explicitly, and the cancellation token is named `ct`. **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 2–9 (models) — enums are `StringEnum<T>` records (not C# enums), records are immutable with `required` init members, and JSON fields the SDK doesn't model are silently dropped on deserialize. **MUST load `dotnet-models`** the moment a field isn't a plain string/number.

> ⚠ Step 10 (error boundary) — which operations are Case A vs Case B, and what `TryGetRawError` does and does not cover on a typed error, are per-operation facts (rows above); the catch-ladder mechanics are the companion skill's. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

> ⚠ Step 1 (resilience) — what `RetryOptions.Timeout` actually bounds, which failures the retry layer re-sends (and therefore whether a failed write can be executed twice), and what you must still wire yourself, are not derivable from the member list. Until that skill is loaded, treat every write as possibly-executed-more-than-once and always send `payPalRequestId`. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts.

> ⚠ Testing — the test seam and how to fake the SDK without binding to its internals are not visible from the constructor. **MUST load `dotnet-testing`** before stubbing.

## 4. REQUIRED READING

Load **before implementation starts** (this sheet deliberately does not carry their contents):

- `dotnet-client-initialization` — governs step 1 (client construction, `HttpClient` ownership, DI registration).
- `dotnet-authentication` — governs step 1 (credentials wiring, secret sourcing, token lifecycle).
- `dotnet-calling-endpoints` — governs steps 2–9 (must-pass-explicitly params, named arguments, envelopes, `ct`).
- `dotnet-models` — governs steps 2–9 (required/init members, `StringEnum<T>`, wire names, dropped fields).
- `dotnet-error-handling` — governs step 10 (Case A/B mechanics, accessor ladders, the `JsonException` boundary).
- `dotnet-configuration-resilience` — governs step 1 (retry/timeout semantics, base URL, logging).
- `dotnet-testing` — governs the test layer (the fakeable seam, error-path coverage).

Mandatory hazard rows for the error boundary (verbatim):

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

Assumptions (about intent, not contract):

1. AUTHORIZE-intent create with a `payment_source` is expected to authorize inline; the sheet covers both readings (read `Authorizations` off the create response, else call `AuthorizeOrder`) — flagged `UNVERIFIED` in §2.1.
2. Vault customers are keyed by shopper identity via `Customer.MerchantCustomerId`; the PayPal-side `Customer.Id` is persisted at first vault for `ListCustomerPaymentTokens`.
3. "PayPal:BaseUrl override applies verbatim" is satisfied by `options.Server.Default.Sandbox.BaseUrl`, which also covers the OAuth token request (source-verified); there is no separate token-endpoint override.
4. Production is reached by base-URL override because the SDK declares no Production environment member (source-verified).
5. Refund idempotency uses one stable `payPalRequestId` per logical refund; full refund sends `new RefundRequest()` (empty payload per the operation's own doc note).

Blockers: none.

`UNVERIFIED` items (live-traffic-only, with defensive directives inline): the `payment_source.token.type` wire value for vaulted tokens (§2.1); whether create-with-payment_source authorizes inline (§2.1); the `"EXPIRED"` authorization-status wire value (§2.5); exact `Error.Name`/`Details[].Issue` strings for auth-expired / over-refund / decline rejections (§2.6 — the SDK types them as plain `string`, so match defensively and never branch on one exact literal).
