# PayPal .NET SDK integration plan — eShopOnWeb (ASP.NET Core)

**SDK**: NuGet `AsadAli.Checkout.Sdk` — install **version-less** (`dotnet add package AsadAli.Checkout.Sdk`); this sheet is grounded in the SDK map for release tag `v1.0.1` (source commit `9653d18`). Root namespace `PayPalServerSdk`; client class `PayPalServerSdkClient`; options `PayPalServerSdkClientOptions`. (`sdk-map.md`)

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Client registration, credentials, environment/base-URL override, resilience | — (client construction) |
| 2 | Vault a card; list/retrieve/delete vaulted tokens | `Vault.CreatePaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.GetPaymentToken`, `Vault.DeletePaymentToken` |
| 3 | Authorize order total (raw card **or** vaulted token) | `Orders.CreateOrder` (intent `AUTHORIZE`) + `Orders.AuthorizeOrder` |
| 4 | Capture at fulfilment + read fee/net breakdown | `Payments.CaptureAuthorizedPayment` (verify with `Payments.GetCapturedPayment`) |
| 5 | Reauthorize stale authorization before capture | `Payments.GetAuthorizedPayment` → `Payments.ReauthorizePayment` |
| 6 | Void authorization on cancel | `Payments.VoidPayment` |
| 7 | Refund capture (full/partial, idempotent) | `Payments.RefundCapturedPayment` (verify with `Payments.GetRefund`) |
| 8 | Reconciliation over a date range, fully paged | `TransactionSearch.SearchTransactions` |
| 9 | Integration error boundary | all of the above |

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

**Namespaces** (`sdk-map.md`): client/options/`ServerOptions` — `PayPalServerSdk` · controllers — `PayPalServerSdk.Api` · records — `PayPalServerSdk.Models` · enums — `PayPalServerSdk.Models.Enums` · error classes — `PayPalServerSdk.Errors` · `ServerEnvironment`/`DefaultOptions` — `PayPalServerSdk.Servers` · `OAuth2ClientCredentials` — `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` · `SdkException<T>` — `PayPalServerSdk.Core.Exceptions` · `RawError`/`ApiError` — `PayPalServerSdk.Core.ErrorResponse` · `RetryOptions` — `PayPalServerSdk.Core.Configuration` · `RequestOptions` — `PayPalServerSdk.Core`.

**Model conventions** (`sdk-map.md`): records are immutable, `init`-only; `!req` = C# `required` (must be set in the initializer); `T?` = optional. Field listed as `CSharpName (wire_name): Type`. Enums are `StringEnum<T>` records, **not** C# enums — use static members (`CheckoutPaymentIntent.Authorize`) or `Type.FromValue("wire")`.

### 2.1 Client construction, auth, environment, base-URL override (`sdk-map.md`; `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs`, `ServerOptions.cs`, `AuthSchemes.cs` — source-verified)

| Fact | Value |
|---|---|
| Constructor | `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` — single ctor |
| DI alternative | `services.AddPayPalServerSdkClient(o => { … })` (`ServiceCollectionExtensions.cs`) |
| Credentials | `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = … }` — both `required string`; optional `Scope`. Optional `options.Oauth2TokenStrategy` (`IOAuth2TokenStrategy<OAuth2ClientCredentials>?`) for custom token handling |
| Environment | `options.Environment` : `PayPalServerSdk.Servers.ServerEnvironment` — **the only member is `ServerEnvironment.Sandbox`** (source-verified: no `Production` member exists; `Default()` ⇒ `Sandbox`) |
| **Base-URL override (the `PayPal:BaseUrl` requirement)** | `options.Server.Default.Sandbox.BaseUrl = "<url>"` — `ServerOptions.Default` is `PayPalServerSdk.Servers.DefaultOptions`, whose `Sandbox` (`SandboxOptions`) has a settable `BaseUrl` (default `https://api-m.sandbox.paypal.com`). **The override covers the OAuth token request too**: the token URL is built as `server.Default("/v1/oauth2/token")` through the same `ServerOptions` resolution (source-verified in `AuthSchemes.cs`). **Production = keep `Environment = ServerEnvironment.Sandbox` and set `BaseUrl = "https://api-m.paypal.com"`** — the production host string is config, not an SDK fact |
| Retry/timeout | `options.Retry` : `RetryOptions` — all members `required`; start from `RetryOptions.Default()`. Semantics: see Trap notes (step 1) |

### 2.2 Step 3 — Authorize (`map/operations/Orders.md`)

**`client.Orders.CreateOrder`** — `POST /v2/checkout/orders`
`CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 5 nullable params have no default → **pass explicitly** (`null` to skip). Returns `Order`. Error: `SdkException<CreateOrderError>` (Case A) — `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback]. **Idempotency: `payPalRequestId`** (PayPal-Request-Id header).

Request `OrderRequest` (`records-1-Ac-Pa.md`): `Intent (intent): CheckoutPaymentIntent !req` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` · `PaymentSource (payment_source): PaymentSource?` · `Payer (payer): Payer?` · `ApplicationContext (application_context): OrderApplicationContext?`

`PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req` · `ReferenceId (reference_id): string?` · `InvoiceId (invoice_id): string?` · `CustomId (custom_id): string?` · `Description (description): string?` · `Payee (payee): PayeeBase?` · `Items (items): IReadOnlyList<ItemRequest>?` · `Shipping (shipping): ShippingDetails?`

`AmountWithBreakdown`: `CurrencyCode (currency_code): string !req` (from config) · `Value (value): string !req` — **string, not decimal**: format the order total invariant-culture, 2 dp, to the cent · `Breakdown (breakdown): AmountBreakdown?`

`PaymentSource` (create-order payment source): `Card (card): CardRequest?` · `Token (token): Token?` · `Paypal (paypal): PayPalWallet?` · (+ Bancontact/Blik/Eps/Giropay/Ideal/Mybank/P24/Sofort/Trustly/ApplePay/GooglePay/Venmo — out of scope)

`CardRequest` (raw card): `Number (number): string?` · `Expiry (expiry): string?` (`YYYY-MM`) · `SecurityCode (security_code): string?` · `Name (name): string?` · `BillingAddress (billing_address): Address?` · `VaultId (vault_id): string?` · `StoredCredential (stored_credential): CardStoredCredential?` · `Attributes (attributes): CardAttributes?`. Map note on the record: passing PAN/CVV directly requires **PCI SAQ D** compliance.

`Token` (vaulted/token payment source): `Id (id): string !req` · `Type (type): TokenType !req` — see §2.8 for the `TokenType` caveat.

Response `Order` (`records-1-Ac-Pa.md`): `Id (id): string?` · `Status (status): OrderStatus?` · `Intent (intent): CheckoutPaymentIntent?` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` · `PaymentSource (payment_source): PaymentSourceResponse?` · `Links (links): IReadOnlyList<LinkDescription>?` · `CreateTime (create_time): string?`

**`client.Orders.AuthorizeOrder`** — `POST /v2/checkout/orders/{id}/authorize`
`AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 5 nullable params must be passed explicitly. Returns `OrderAuthorizeResponse`. Error: `SdkException<AuthorizeOrderError>` (Case A) — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)`. **Idempotency: `payPalRequestId`.**

`OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` — supply the card/token here **or** at create-order (API accepts either; op notes: "a valid payment_source must be provided in the request"). `OrderAuthorizeRequestPaymentSource`: `Card (card): CardRequest?` · `Token (token): Token?` · `Paypal (paypal): PayPalWallet?` · `ApplePay/GooglePay/Venmo`.

`OrderAuthorizeResponse`: `Id (id)` · `Status (status): OrderStatus?` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` · `PaymentSource (payment_source): OrderAuthorizeResponsePaymentSource?` · `Links`. **Read the authorization id one envelope level down**: `PurchaseUnits[i].Payments (payments): PaymentCollection?` → `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → `Id (id)`, `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?` (`records-1/-2`).

### 2.3 Step 4 — Capture + fee breakdown (`map/operations/Payments.md`)

**`client.Payments.CaptureAuthorizedPayment`** — `POST /v2/payments/authorizations/{authorization_id}/capture`
`CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullable params explicit. Returns `CapturedPayment`. Error: `SdkException<CaptureAuthorizedPaymentError>` (Case A) — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`. **Idempotency: `payPalRequestId`.**

`CaptureRequest` (`records-1-Ac-Pa.md`): `Amount (amount): Money?` (omit/pass null body for full amount) · `InvoiceId (invoice_id): string?` · `FinalCapture (final_capture): bool? = false` · `NoteToPayer (note_to_payer): string?` · `SoftDescriptor (soft_descriptor): string?` · `PaymentInstruction (payment_instruction): CapturePaymentInstruction?`

`Money`: `CurrencyCode (currency_code): string !req` · `Value (value): string !req`

Response `CapturedPayment` — **no wrapper; the payload is top-level**: `Id (id)` · `Status (status): CaptureStatus?` · `StatusDetails (status_details): CaptureStatusDetails?` · `Amount (amount): Money?` · `FinalCapture (final_capture): bool?` · `InvoiceId` · `CustomId` · **`SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`** · `CreateTime (create_time)`.

`SellerReceivableBreakdown` (`records-2-Pa-Ve.md`): `GrossAmount (gross_amount): Money !req` (captured amount) · `PaypalFee (paypal_fee): Money?` (PayPal's fee) · `NetAmount (net_amount): Money?` (net proceeds) · `PaypalFeeInReceivableCurrency`, `ReceivableAmount`, `ExchangeRate`, `PlatformFees`. Record note: not available while the capture is pending — re-read via **`GetCapturedPayment(string captureId, string? payPalMockResponse, …)`** → `CapturedPayment` (error [401, 403, 404] + `TryGetNoContent` [500]).

### 2.4 Step 5 — Reauthorize (`map/operations/Payments.md`)

**`client.Payments.GetAuthorizedPayment`** — `GET /v2/payments/authorizations/{authorization_id}`
`GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization`: `Status (status): AuthorizationStatus?` · `ExpirationTime (expiration_time): string?` · `Amount` · `Id`. Error [401, 403, 404] + `TryGetNoContent` [500]. Use it to check staleness (`Status`, `ExpirationTime`) before deciding to reauthorize.

**`client.Payments.ReauthorizePayment`** — `POST /v2/payments/authorizations/{authorization_id}/reauthorize`
`ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 nullable params explicit. Returns `PaymentAuthorization`. Error: `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500]. **Idempotency: `payPalRequestId`.**

`ReauthorizeRequest`: **`Amount (amount): Money?` — the only supported parameter** (op notes: "Supports only the `amount` request parameter").

Constraints (op notes, `Payments.md`): reauthorize after the initial **3-day honor period** expires; allowed **from day 4 to day 29** after the original authorization; after 30 days you must create a new authorization instead; a reauthorized payment gets a fresh 3-day honor period; allowed amount depends on context/geography — e.g. US: up to **115%** of the original, max increase **$75**. ⚠ Doc drift visible in the map: the operation notes say multiple re-authorizations are allowed within the 29-day window, while the `ReauthorizeRequest` model summary says "reauthorize only once from days four to 29" — **defensive directive: treat reauthorize as once-per-authorization; on a 4xx from reauthorize, fall back to creating a new order/authorization rather than retrying reauthorize.**

### 2.5 Step 6 — Void (`map/operations/Payments.md`)

**`client.Payments.VoidPayment`** — `POST /v2/payments/authorizations/{authorization_id}/void`
`VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — ⚠ **parameter order differs from the other Payments ops: `payPalRequestId` is 4th here** (use named arguments). Returns `PaymentAuthorization`. Error: `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500]. **Idempotency: `payPalRequestId`.** Op note: cannot void an authorization that has been fully captured.

### 2.6 Step 7 — Refund (`map/operations/Payments.md`)

**`client.Payments.RefundCapturedPayment`** — `POST /v2/payments/captures/{capture_id}/refund`
`RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullable params explicit. Returns `Refund`. Error: `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500].

`RefundRequest`: `Amount (amount): Money?` — **full refund = empty body** (pass `null` or a `RefundRequest` with no `Amount`); **partial refund = set `Amount`** · `InvoiceId (invoice_id): string?` · `CustomId (custom_id): string?` · `NoteToPayer (note_to_payer): string?` · `PaymentInstruction (payment_instruction): RefundPaymentInstruction?`

Response `Refund` (top-level, no wrapper): `Id (id)` · `Status (status): RefundStatus?` · `StatusDetails (status_details): RefundStatusDetails?` · `Amount (amount): Money?` · `InvoiceId` · `CustomId` · `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (incl. `TotalRefundedAmount (total_refunded_amount): Money?`, `NetAmount`, `PaypalFee`). Verify via **`GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, …)`** (error [401, 403, 404] + `TryGetNoContent` [500]).

**Idempotency mechanism (exact):** the SDK-level mechanism is the **`payPalRequestId` parameter** (PayPal-Request-Id header) — present on `RefundCapturedPayment`, `CreateOrder`, `AuthorizeOrder`, `CaptureOrder`, `CaptureAuthorizedPayment`, `ReauthorizePayment`, `VoidPayment`, `CreatePaymentToken`, `CreateSetupToken`. Directive: generate one key per logical operation (e.g. `refund-{orderId}-{n}`), persist it before the call, reuse it on retry — the same key never refunds twice; two distinct partial refunds get two distinct keys and both remain legitimate. `RefundRequest.InvoiceId`/`CustomId` are correlation fields, not the SDK's dedup mechanism (any API-side invoice-id dedup is `UNVERIFIED` — only live traffic confirms it).

### 2.7 Step 2 — Vault (`map/operations/Vault.md`; records pages)

**`client.Vault.CreatePaymentToken`** — `POST /v3/vault/payment-tokens`
`CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentTokenResponse`. Error: `SdkException<CreatePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)`. **Idempotency: `payPalRequestId`.**

`PaymentTokenRequest`: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` · `Customer (customer): Customer?` — `Customer`: `Id (id): string?` (PayPal customer id) · `MerchantCustomerId (merchant_customer_id): string?`. `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?` · `Token (token): VaultTokenRequest?` (`VaultTokenRequest`: `Id !req`, `Type: VaultTokenRequestType !req` — only member `SetupToken (SETUP_TOKEN)`, i.e. the setup-token flow).

`PaymentTokenRequestCard` (vault a raw card): `Number (number): string?` · `Expiry (expiry): string?` · `SecurityCode (security_code): string?` · `Name (name): string?` · `Brand (brand): CardBrand?` · `BillingAddress (billing_address): Address?`

`PaymentTokenResponse`: `Id (id): string?` (the vault token id — persist this) · `Customer (customer): CustomerResponse?` · `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` · `Links`. `PaymentTokenResponsePaymentSource`: `Card (card): CardPaymentTokenEntity?` · `Paypal/ Venmo/ ApplePay`. **Safe card description** — `CardPaymentTokenEntity`: `Brand (brand): CardBrand?` · `LastDigits (last_digits): string?` · `Expiry (expiry): string?` · `Name (name): string?` · `Type (type): CardType?` — **no PAN field exists on the response entity** (map-visible), so display from these fields only.

**`client.Vault.ListCustomerPaymentTokens`** — `GET /v3/vault/payment-tokens`
`ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` (query: `customer_id`, `page_size`, `page`, `total_required`) → `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Customer (customer): VaultResponseCustomer?` · `Links`. Error: `TryGetError1(out Error1)` [400, 403, 500]. Pagination: manual page loop over `TotalPages` (no SDK pager). **Integration requirement: persist the PayPal `Customer.Id` per shopper — it is the required `customerId` here.**

**`client.Vault.GetPaymentToken`** — `GET /v3/vault/payment-tokens/{id}`
`GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentTokenResponse`. Error: `TryGetError1(out Error1)` [403, **404**, 422, 500] — **vault token not found = 404** via the typed accessor.

**`client.Vault.DeletePaymentToken`** — `DELETE /v3/vault/payment-tokens/{id}`
`DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `void`. Error: `TryGetError1(out Error1)` [400, 403, 500] — ⚠ **no 404 typed accessor**: deleting a missing token surfaces through `TryGetRawError(out RawError)` (read `StatusCode`) or succeeds silently; treat "already gone" as success at the boundary.

### 2.8 Paying with a vaulted token at order time (`records-1/-2`, `enums.md`; `Core/Enum/StringEnum.cs` — source-verified)

Two map-grounded shapes on `PaymentSource` / `OrderAuthorizeRequestPaymentSource`:

1. `Token = new Token { Id = <vaultTokenId>, Type = … }` — `TokenType` declares **only** `BillingAgreement (BILLING_AGREEMENT)` in the generated enum (`enums.md`). Source-verified: `StringEnum<T>.FromValue` accepts **any** string (unknown values construct via reflection, never throw), so `TokenType.FromValue("PAYMENT_METHOD_TOKEN")` compiles and serializes. Whether the live API accepts `PAYMENT_METHOD_TOKEN` here is **`UNVERIFIED`** (only live traffic confirms) — verify in sandbox first.
2. `Card = new CardRequest { VaultId = <vaultTokenId>, StoredCredential = … }` — `CardStoredCredential`: `PaymentInitiator (payment_initiator): PaymentInitiator !req` (`Customer (CUSTOMER)` / `Merchant (MERCHANT)`) · `PaymentType (payment_type): StoredPaymentSourcePaymentType !req` (`OneTime (ONE_TIME)` / `Recurring (RECURRING)` / `Unscheduled (UNSCHEDULED)`) · `Usage (usage): StoredPaymentSourceUsageType? = Derived` (`First`/`Subsequent`/`Derived`). Record note: `payment_type=ONE_TIME` is compatible only with `payment_initiator=CUSTOMER`.

Defensive directive: implement shape (1) with `TokenType.FromValue("PAYMENT_METHOD_TOKEN")` behind a small payment-source factory; if sandbox rejects it, switch the factory to shape (2) without touching call sites.

### 2.9 Step 8 — Transaction search (`map/operations/TransactionSearch.md`)

**`client.TransactionSearch.SearchTransactions`** — `GET /v1/reporting/transactions`
`SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 8 nullable filters must be passed explicitly (`null`). Query wire names: `start_date`, `end_date` (ISO-8601 strings), `transaction_id`, `transaction_type`, `transaction_status`, `transaction_amount`, `transaction_currency`, `payment_instrument_type`, `store_id`, `terminal_id`, `fields`, `balance_affecting_records_only`, `page_size`, `page`.
Returns `SearchResponse`. **Error: Case B — `SdkException<RawError>`** (the only Case-B op in scope): read `ex.Error.StatusCode` / `ReadAsString()` / `ReadAsJson<T>()`. Op notes: transactions appear with up to **3 hours** delay; range limited to the **previous 3 years**.

**Pagination (whole range):** no SDK pager — manual loop: call with `page: 1`, read `SearchResponse.TotalPages (total_pages): int?` / `TotalItems (total_items): int?` / `Page (page): int?`, iterate `page` 2…TotalPages (or follow `Links (links): IReadOnlyList<LinkDescription>?` where `Rel (rel) == "next"`; `LinkDescription`: `Href (href) !req`, `Rel (rel) !req`, `Method (method): LinkHttpMethod?`). Page size via `pageSize` (default 100).

`SearchResponse.TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` → `TransactionInfo (transaction_info): TransactionInformation?` with: `TransactionId (transaction_id): string?` · `PaypalReferenceId (paypal_reference_id): string?` · `PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?` (`Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)`) · `TransactionEventCode (transaction_event_code): string?` · `TransactionInitiationDate (transaction_initiation_date): string?` · `TransactionUpdatedDate (transaction_updated_date): string?` · `TransactionAmount (transaction_amount): Money?` · `FeeAmount (fee_amount): Money?` · `TransactionStatus (transaction_status): string?` — **plain string, not an enum** · `InvoiceId (invoice_id): string?` · `CustomField (custom_field): string?` · `EndingBalance`, `AvailableBalance`, `ProtectionEligibility`, `PaymentMethodType`, `InstrumentType` (+ more on `records-2-Pa-Ve.md`). Reconciliation keys: `InvoiceId`/`CustomField` ↔ local order; `TransactionId` ↔ stored capture/refund ids.

### 2.10 Enum values needed (`map/models/enums.md`) — namespace `PayPalServerSdk.Models.Enums`

| Enum | Members (C# name = wire value) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` (display) | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, `Maestro (MAESTRO)`, … (29 members; full list on `enums.md`) |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` — only declared member; see §2.8 |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` (for `VaultInstructionBase.StoreInVault` if vaulting at order time via `CardAttributes.Vault`) |
| Status-detail reasons | `AuthorizationIncompleteReason`: `PendingReview`, `DeclinedByRiskFraudFilters` · `CaptureIncompleteReason`: 12 members incl. `Refunded (REFUNDED)`, `PendingReview (PENDING_REVIEW)` · `RefundIncompleteReason`: `Echeck (ECHECK)` |

### 2.11 Error model & expected statuses (`sdk-map.md` error model; per-op rows above)

All ops are **throw-only** (no `…Result` variants anywhere in the SDK). Catch `SdkException<{Operation}Error>` (Case A; `.Error` exposes the typed accessors + inherited `TryGetRawError(out RawError)`), or `SdkException<RawError>` for `SearchTransactions` (Case B). Typed payload `Error` (Orders/Payments ops): `Name (name): string !req` · `Message (message): string !req` · `DebugId (debug_id): string !req` · `Details (details): IReadOnlyList<ErrorDetails>?` — `ErrorDetails`: `Issue (issue): string !req` · `Field (field)` · `Value (value)` · `Location (location) = "body"` · `Description (description)`. Vault ops use `Error1` (same shape; `Details` is `ErrorDetails1`, `Links` is `ErrorLinkDescription` whose `Rel` is **nullable** — the live API omits `rel` on the `RESOURCE_NOT_FOUND` documentation link, per the map row).

Scenario → status (the status **sets** are map-grounded; the exact scenario→status mapping is API behavior — **`UNVERIFIED`**, defensive directive: match on the status set **and** `Error.Name` / `Details[].Issue` strings, never on status alone):

| Scenario | Operation | Candidate statuses (map row) | Directive |
|---|---|---|---|
| Duplicate idempotency key | `RefundCapturedPayment` (also CreateOrder/Authorize/Capture) | 409, 422 (within 400/401/403/404/409/422) | On 409/422 read `Name`+`Issue`; treat duplicate-key issues as already-processed and reconcile via `GetRefund`/`SearchTransactions` — do not retry blindly |
| Authorization expired/voided at capture | `CaptureAuthorizedPayment` | 404, 409, 422 | Pre-check with `GetAuthorizedPayment` (`Status` ∈ `AuthorizationStatus`, `ExpirationTime`); on 4xx route to the reauthorize/new-order flow |
| Capture already done | `CaptureAuthorizedPayment` | 409, 422 | Reconcile via `GetCapturedPayment` before retrying |
| Refund exceeds captured amount | `RefundCapturedPayment` | 400, 422 | Do not retry; surface as a deterministic rejection |
| Vault token not found | `GetPaymentToken` | **404** (typed `Error1` accessor) | Remove the local token reference |
| (same, on delete) | `DeletePaymentToken` | no 404 accessor — falls to `TryGetRawError` | Treat as already-deleted |

## 3. Trap notes

- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind `PayPalServerSdkClient` has lifetime requirements a per-request `new HttpClient` violates. **MUST load `dotnet-client-initialization`** before wiring DI.
- ⚠ Step 1 (auth) — credentials must be set on the options before client construction and sourced from configuration, never hardcoded. **MUST load `dotnet-authentication`**.
- ⚠ Step 1 (resilience) — what `Retry`/`Timeout` on the options actually bound, and whether a failed **write** (`POST`) can be re-executed under the hood — this interacts directly with the `payPalRequestId` idempotency design in §2.6. **MUST load `dotnet-configuration-resilience`** before tuning or registering the client.
- ⚠ Steps 2–8 (every call) — nullable no-default parameters must be passed explicitly and optional filters mis-bind positionally; call with named arguments (`ct:` included). **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 2–8 (payloads) — enums are `StringEnum<T>` (not C# enums), `required` members must be set in the initializer, and unmodeled JSON fields are silently dropped on deserialize. **MUST load `dotnet-models`** before building request models.
- ⚠ Step 9 (error boundary) — which exception types actually reach a catch block, Case A vs Case B, and why `TryGetRawError` is not a catch-all on typed errors. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ Tests — the SDK's test seam is the `HttpClient` constructor argument, not mocking controllers. **MUST load `dotnet-testing`** before stubbing the SDK.

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

- `dotnet-client-initialization` — governs step 1 (client construction & DI).
- `dotnet-authentication` — governs step 1 (credentials wiring).
- `dotnet-configuration-resilience` — governs step 1 (retry/timeout/base-URL tuning) and step 8 (pagination loop).
- `dotnet-calling-endpoints` — governs steps 2–8 (every operation call).
- `dotnet-models` — governs steps 2–8 (request/response model construction).
- `dotnet-error-handling` — governs step 9 (the integration error boundary).
- `dotnet-testing` — governs the integration tests.

Two hazard rows that shape the error boundary, verbatim:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

**Assumptions**
- Direct card processing (raw PAN/CVV through `CardRequest`) assumes PCI SAQ D compliance — the `CardRequest` record itself carries this note. If SAQ D is out of scope, the card path must move to hosted fields / vaulted tokens only.
- The Vault API is marked *Available in the US only* in the SDK's client documentation.
- The integration persists per shopper: PayPal `Customer.Id` (needed by `ListCustomerPaymentTokens`), vault token ids, authorization ids, capture ids, and the idempotency keys used per logical operation.
- Currency comes from config and is passed as `Money.CurrencyCode`; amounts are formatted as invariant-culture strings with 2 decimal places (`Money.Value` is `string`).
- Production targeting uses the base-URL override (`Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"`) because the SDK ships no `Production` environment member; the production host string is environment config, not an SDK contract.
- `Subscriptions` controller (17 ops) is out of scope; `SearchBalances` is available on `TransactionSearch` if balance reporting is later needed.

**Blockers** — none that block planning.

**`UNVERIFIED` items** (only live/sandbox traffic can confirm; defensive directives given inline)
- §2.8: whether the live Orders API accepts `Token.Type = PAYMENT_METHOD_TOKEN` for vault-v3 tokens (fallback shape documented).
- §2.6: any API-side `invoice_id` dedup on refunds (the SDK-level mechanism, `payPalRequestId`, is the contract).
- §2.11: exact scenario→HTTP-status mappings (status sets per operation are map-grounded).
- §2.4: whether multiple reauthorizations per authorization are accepted (two generated docs disagree; treat as once).
