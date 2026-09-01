# PayPal integration plan — eShopOnWeb `src/PublicApi` (ASP.NET Core)

**SDK**: NuGet `AsadAli.Checkout.Sdk` — install version-less (`dotnet add package AsadAli.Checkout.Sdk`), floats to latest; this sheet is grounded against source tag `v1.0.1` (commit `9653d18`). Root namespace `PayPalServerSdk`; client `PayPalServerSdkClient`; options `PayPalServerSdkClientOptions`. All PayPal interaction goes through this SDK. (`sdk-map.md`)

**Namespace cheat-sheet** (C# does not import child namespaces transitively — one `using` per row):

| Types | Namespace |
|---|---|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` | `PayPalServerSdk` |
| `ServerEnvironment`, `DefaultOptions` | `PayPalServerSdk.Servers` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `IOAuth2TokenStrategy<T>` | `PayPalServerSdk.Core.Authentication.OAuth2` |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` |
| `RequestOptions` | `PayPalServerSdk.Core` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` |
| Controllers behind `client.Orders` / `.Payments` / `.Vault` / `.TransactionSearch` | `PayPalServerSdk.Api` |
| All records (`OrderRequest`, `Order`, `Money`, `Error`, `Error1`, …) | `PayPalServerSdk.Models` |
| All enums (`CheckoutPaymentIntent`, `OrderStatus`, …) — `StringEnum<T>`, not C# enums | `PayPalServerSdk.Models.Enums` |
| `{Operation}Error` classes (`CreateOrderError`, …) | `PayPalServerSdk.Errors` |

## 1. Scope & sequence

1. **Client & DI** — register `PayPalServerSdkClient` in `src/PublicApi` via `services.AddPayPalServerSdkClient(o => …)`; credentials + environment/base-URL from configuration. (Step 5 of brief.)
2. **Direct card authorize** — `Orders.CreateOrder` with `Intent = Authorize` and inline `payment_source.card`; read authorization id/status from the response. (Brief 1.)
3. **Capture / reauthorize / void** — `Payments.CaptureAuthorizedPayment`, `Payments.ReauthorizePayment`, `Payments.VoidPayment`; `Payments.GetAuthorizedPayment` for stale-authorization checks (expiry). (Brief 1.)
4. **Vault** — `Vault.CreatePaymentToken` (direct card vault), optionally `Vault.CreateSetupToken` → `CreatePaymentToken` (two-step), `Vault.ListCustomerPaymentTokens`, `Vault.GetPaymentToken`, `Vault.DeletePaymentToken`; pay with a saved token via `payment_source.card.vault_id` on `Orders.CreateOrder`. (Brief 2.)
5. **Refunds** — `Payments.RefundCapturedPayment` (full/partial, idempotency key), `Payments.GetRefund`. (Brief 3.)
6. **Reconciliation** — `TransactionSearch.SearchTransactions` with page loop over the date range. (Brief 4.)
7. **Error boundary** — one translation layer around all SDK calls in the PublicApi services. (Brief 6.)

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

### 2.0 Client construction, auth, environments, base-URL override (`sdk-map.md` *Getting a client* / *Servers & auth*; `PayPalServerSdkClientOptions.cs`, `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs`, `ServerOptions.cs`, `AuthSchemes.cs`)

| Fact | Value |
|---|---|
| Constructor | `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` |
| DI | `services.AddPayPalServerSdkClient(o => { … })` (`ServiceCollectionExtensions.cs`) |
| Credentials | `o.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = … }` — both `required init`; `Scope` optional. Optional `o.Oauth2TokenStrategy` (`IOAuth2TokenStrategy<OAuth2ClientCredentials>`) for custom token handling. |
| Environment | `o.Environment` is `PayPalServerSdk.Servers.ServerEnvironment` — the **only** member is `ServerEnvironment.Sandbox` (also the `Default()`). **There is no Production member.** |
| Sandbox base URL | `https://api-m.sandbox.paypal.com` (declared default in `DefaultOptions.SandboxOptions`). |
| Base-URL override (→ production) | `o.Server.Default.Sandbox.BaseUrl = "<base-url-from-configuration>"`. `ServerOptions` (root namespace) has one member `Default` (`DefaultOptions`, `PayPalServerSdk.Servers`), which has `Sandbox.BaseUrl`. |
| Override covers OAuth token request? | **Yes.** The token call is built as `server.Default("/v1/oauth2/token")` (`AuthSchemes.cs`) and resolves through the same `DefaultOptions.Resolve` as every API path — one `BaseUrl` override covers API calls **and** `/v1/oauth2/token`. |
| Other options | `o.Retry` (`RetryOptions`, all members `required` — use `RetryOptions.Default()` as the starting instance), `o.Logging` (`LoggingOptions`). |

### 2.1 Direct card authorize + capture (brief 1)

**`client.Orders.CreateOrder`** — `POST /v2/checkout/orders` (`operations/Orders.md`)

```csharp
CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId,
    string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body,
    string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- The 5 nullable params `payPalMockResponse`…`payPalAuthAssertion` have **no C# default — pass explicitly** (`null` to skip), except pass a real `payPalRequestId`: it is **mandatory for single-step create-order with a payment source** (source doc comment, `Api/Orders.cs`); keys stored 6 h. Wire header: `PayPal-Request-Id`.
- Returns `Order`. Error: `SdkException<CreateOrderError>` (Case A) — `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback.
- `prefer`: `"return=minimal"` (default) = id, status, HATEOAS links only; `"return=representation"` = complete resource (source doc comment). To read the authorization from the create response, pass `prefer: "return=representation"` — or call `GetOrder` after.

Request models (`records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`):

| Record | Fields (`Name (wire_name): Type`, `!req` = C# `required`) |
|---|---|
| `OrderRequest` | `Intent (intent): CheckoutPaymentIntent !req` → `CheckoutPaymentIntent.Authorize`; `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`; `PaymentSource (payment_source): PaymentSource?`; `Payer (payer): Payer?`; `ApplicationContext (application_context): OrderApplicationContext?` |
| `PurchaseUnitRequest` | `Amount (amount): AmountWithBreakdown !req`; `ReferenceId (reference_id): string?`; `CustomId (custom_id): string?`; `InvoiceId (invoice_id): string?`; `Description (description): string?`; `Payee`, `Items`, `Shipping`, … optional |
| `AmountWithBreakdown` | `CurrencyCode (currency_code): string !req`; `Value (value): string !req`; `Breakdown (breakdown): AmountBreakdown?` |
| `PaymentSource` | `Card (card): CardRequest?` ← use this for raw card; `Token (token): Token?`; `Paypal`, `ApplePay`, … (other wallets, out of scope) |
| `CardRequest` | `Number (number): string?`; `Expiry (expiry): string?` (`"YYYY-MM"`); `SecurityCode (security_code): string?`; `Name (name): string?`; `BillingAddress (billing_address): Address?`; `VaultId (vault_id): string?` ← **pay with a saved vault token**; `Attributes (attributes): CardAttributes?`; `StoredCredential (stored_credential): CardStoredCredential?` |
| `Address` | `CountryCode (country_code): string !req`; `AddressLine1 (address_line_1)`, `AddressLine2`, `AdminArea2 (admin_area_2)` city, `AdminArea1 (admin_area_1)` state, `PostalCode (postal_code)`: all `string?` |
| `CardAttributes` | `Verification (verification): CardVerification?`; `Vault (vault): VaultInstructionBase?`; `Customer (customer): CardCustomerInformation?` |
| `CardVerification` | `Method (method): OrdersCardVerificationMethod? = OrdersCardVerificationMethod.ScaWhenRequired` (default) |
| `VaultInstructionBase` | `StoreInVault (store_in_vault): StoreInVaultInstruction?` → `StoreInVaultInstruction.OnSuccess` = vault the card when the order succeeds |

Response read path — authorization id/status (`records-1-Ac-Pa.md`):

`Order.PurchaseUnits (purchase_units)` → `IReadOnlyList<PurchaseUnit>` → `.Payments (payments): PaymentCollection?` → `.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → per authorization: `Id (id)`, `Status (status): AuthorizationStatus?`, `StatusDetails (status_details).Reason: AuthorizationIncompleteReason?`, `Amount (amount): Money?`, **`ExpirationTime (expiration_time): string?` ← authorization expiry**, `CreateTime (create_time)`. Also on `Order`: `Id (id)`, `Status (status): OrderStatus?`, `Links (links)`.

**`client.Payments.CaptureAuthorizedPayment`** — `POST /v2/payments/authorizations/{authorization_id}/capture` (`operations/Payments.md`)

```csharp
CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId,
    string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal",
    RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- 4 nullable params `payPalMockResponse`…`body` must be passed explicitly (`null` to skip; pass `payPalRequestId` for idempotency — keys stored 45 days). Returns `CapturedPayment`. Error: `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback.
- `CaptureRequest`: `Amount (amount): Money?` (omit = full authorized amount), `FinalCapture (final_capture): bool? = false`, `InvoiceId`, `NoteToPayer (note_to_payer)`, `SoftDescriptor`, `PaymentInstruction`. `Money`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req`.
- `CapturedPayment` reads: `Id (id)`, `Status (status): CaptureStatus?`, `StatusDetails.Reason: CaptureIncompleteReason?`, `Amount: Money?`, `FinalCapture (final_capture): bool?`, and **`SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`** → `GrossAmount (gross_amount): Money !req`, **`PaypalFee (paypal_fee): Money?`** ← PayPal seller fee, **`NetAmount (net_amount): Money?`** ← net proceeds, `PaypalFeeInReceivableCurrency`, `ReceivableAmount`, `ExchangeRate`, `PlatformFees`. Plus `Links`, `ProcessorResponse (processor_response)`, `CreateTime`.

**`client.Payments.ReauthorizePayment`** — `POST /v2/payments/authorizations/{authorization_id}/reauthorize` (`operations/Payments.md`)

```csharp
ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion,
    ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

- `payPalRequestId`, `payPalAuthAssertion`, `body` must be passed explicitly. `ReauthorizeRequest`: **`Amount (amount): Money?` — the only supported parameter**. Returns `PaymentAuthorization`. Error: `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · fallback.
- Constraints (map op notes): 3-day honor period; reauthorize allowed days 4–29 (multiple reauthorizations allowed within the 29-day window; each gets a fresh 3-day honor period); after 30 days create a new authorization instead; amount cap e.g. US ≤115% of original, ≤ +$75.

**`client.Payments.VoidPayment`** — `POST /v2/payments/authorizations/{authorization_id}/void` (`operations/Payments.md`)

```csharp
VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion,
    string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

- 3 nullable params must be passed explicitly. No body. Returns `PaymentAuthorization`. Error: `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401, 403, 404, **409**, 422] · `TryGetNoContent(out RawError)` [500] · fallback. Cannot void a fully-captured authorization.

**`client.Payments.GetAuthorizedPayment`** — `GET /v2/payments/authorizations/{authorization_id}` — `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization`. Use it to poll a stale authorization: `Status`, **`ExpirationTime (expiration_time): string?`**. Error: `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · fallback.

`PaymentAuthorization` fields (`records-2-Pa-Ve.md`): `Id`, `Status: AuthorizationStatus?`, `StatusDetails: AuthorizationStatusDetails?`, `Amount: Money?`, `InvoiceId`, `CustomId`, `SellerProtection`, `ExpirationTime (expiration_time): string?`, `Links`, `CreateTime`, `UpdateTime`, `SupplementaryData`, `Payee`.

### 2.2 Vault / saved cards (brief 2) — `operations/Vault.md`

**`client.Vault.CreatePaymentToken`** — `POST /v3/vault/payment-tokens`

```csharp
CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body,
    RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- `payPalRequestId` must be passed explicitly (idempotency; keys stored 3 h). Returns `PaymentTokenResponse`. Error: `SdkException<CreatePaymentTokenError>` — **`TryGetError1(out Error1)`** [400, 403, 404, 422, 500] · `TryGetRawError` fallback. (Note: Vault errors are `Error1`, not `Error`.)

| Record | Fields |
|---|---|
| `PaymentTokenRequest` | `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`; `Customer (customer): Customer?` ← customer association |
| `Customer` | `Id (id): string?` (PayPal customer id); `MerchantCustomerId (merchant_customer_id): string?` ← your own customer key |
| `PaymentTokenRequestPaymentSource` | `Card (card): PaymentTokenRequestCard?` ← vault a raw card; `Token (token): VaultTokenRequest?` ← convert a setup token |
| `PaymentTokenRequestCard` | `Number (number): string?`; `Expiry (expiry): string?`; `SecurityCode (security_code): string?`; `Name (name): string?`; `Brand (brand): CardBrand?`; `BillingAddress (billing_address): Address?` |
| `VaultTokenRequest` | `Id (id): string !req`; `Type (type): VaultTokenRequestType !req` → `VaultTokenRequestType.SetupToken` |
| `PaymentTokenResponse` | **`Id (id): string?` ← the token id to store**; `Customer (customer): CustomerResponse?`; `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?`; `Links` |
| `PaymentTokenResponsePaymentSource` | `Card (card): CardPaymentTokenEntity?` → **`Brand (brand): CardBrand?`**, **`LastDigits (last_digits): string?`** ← safe display, `Expiry`, `Name`, `VerificationStatus (verification_status): CardVerificationStatus?`, `BillingAddress` |

**Two-step alternative (setup token first)**: `client.Vault.CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, …)` → `SetupTokenResponse` (`Id`, `Status (status): PaymentTokenStatus? = PaymentTokenStatus.Created`). `SetupTokenRequest`: `Customer (customer): Customer?`, `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req` → `Card (card): SetupTokenRequestCard?` (same card fields + `VerificationMethod (verification_method): VaultCardVerificationMethod?`, `ExperienceContext`). Then `CreatePaymentToken` with `PaymentSource.Token = new VaultTokenRequest { Id = <setup-token-id>, Type = VaultTokenRequestType.SetupToken }`. Errors: `SdkException<CreateSetupTokenError>` — `TryGetError1(out Error1)` [400, 403, 422, 500].

**`client.Vault.ListCustomerPaymentTokens`** — `GET /v3/vault/payment-tokens`

```csharp
ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1,
    bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- `customerId` (wire `customer_id`) is the **PayPal** customer id. Returns `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Customer (customer): VaultResponseCustomer?`, `Links`. Page loop: increment `page` to `TotalPages`. Error: `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400, 403, 500].

**`client.Vault.GetPaymentToken`** — `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentTokenResponse`. Error: `SdkException<GetPaymentTokenError>` — `TryGetError1(out Error1)` [403, 404, 422, 500].

**`client.Vault.DeletePaymentToken`** — `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `void` (Task). Error: `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 500].

**Paying with a saved token (authorize intent)**: `OrderRequest.PaymentSource = new PaymentSource { Card = new CardRequest { VaultId = "<payment-token-id>" } }` — `CardRequest.VaultId (vault_id): string?` (`records-1-Ac-Pa.md`). (`PaymentSource.Token` with `Token { Id !req, Type !req = TokenType.BillingAgreement }` exists but is for billing-agreement tokens, not vault payment tokens.)

**Vault-on-success alternative**: set `CardRequest.Attributes.Vault = new VaultInstructionBase { StoreInVault = StoreInVaultInstruction.OnSuccess }` on the order's card; read the result back from `Order.PaymentSource (payment_source): PaymentSourceResponse?` → `.Card (card): CardResponse?` → `.Attributes (attributes): CardAttributesResponse?` → `.Vault (vault): CardVaultResponse?` → `Id`, `Status: VaultStatus?`.

### 2.3 Refunds (brief 3) — `operations/Payments.md`

**`client.Payments.RefundCapturedPayment`** — `POST /v2/payments/captures/{capture_id}/refund`

```csharp
RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId,
    string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal",
    RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- **Idempotency**: the caller-supplied key is the `payPalRequestId` parameter → wire header **`PayPal-Request-Id`** (confirmed in `Api/Payments.cs`); server stores keys **45 days**. Reusing a key replays the same refund instead of creating a second one.
- **Full refund**: pass `body: null` (empty payload). **Partial refund**: `new RefundRequest { Amount = new Money { CurrencyCode = …, Value = … } }`. Other `RefundRequest` fields: `CustomId (custom_id)`, `InvoiceId (invoice_id)`, `NoteToPayer (note_to_payer)`, `PaymentInstruction`.
- Returns `Refund`: **`Id (id)`**, **`Status (status): RefundStatus?`**, `StatusDetails (status_details).Reason: RefundIncompleteReason?`, **`Amount (amount): Money?`**, `SellerPayableBreakdown (seller_payable_breakdown)` → `GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount (total_refunded_amount)`, `Links`, `CreateTime`. (`records-2-Pa-Ve.md`)
- Error: `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent(out RawError)` [500] · fallback.

**`client.Payments.GetRefund`** — `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Refund`. Error: `SdkException<GetRefundError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500].

### 2.4 Transaction search / reconciliation (brief 4) — `operations/TransactionSearch.md`

**`client.TransactionSearch.SearchTransactions`** — `GET /v1/reporting/transactions`

```csharp
SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType,
    string? transactionStatus, string? transactionAmount, string? transactionCurrency,
    string? paymentInstrumentType, string? storeId, string? terminalId,
    string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y",
    int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- The 8 nullable filters `transactionId`…`terminalId` **must be passed explicitly** (`null` to skip) — call with named arguments.
- **Date format** (source doc comments, `Api/TransactionSearch.cs`): RFC 3339 §5.6 Internet date-time, **seconds required**, fractional seconds optional (e.g. `2026-08-01T00:00:00Z`). **Maximum range: 31 days** — chunk longer ranges into ≤31-day windows. Data lag: transactions appear ≤3 h after execution; history covers the previous 3 years.
- **Pagination**: `page` + `pageSize` (default 100). Loop `page` from 1 while `page < response.TotalPages`. Response `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `StartDate`, `EndDate`, `Links`.
- Per-transaction (`TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?`, `records-2-Pa-Ve.md`): **`TransactionId (transaction_id): string?`**, **`TransactionAmount (transaction_amount): Money?`**, **`FeeAmount (fee_amount): Money?`**, **`TransactionInitiationDate (transaction_initiation_date)`**, `TransactionUpdatedDate (transaction_updated_date)`, **`TransactionStatus (transaction_status): string?`** (plain string; codes: `D` denied, `P` pending, `S` success, `V` reversed — source doc comment), `TransactionEventCode (transaction_event_code)`, `InvoiceId (invoice_id)`, `CustomField (custom_field)`, `EndingBalance`, `PaymentMethodType`, `InstrumentType`. **Link back to order/auth/capture**: **`PaypalReferenceId (paypal_reference_id): string?`** + **`PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?`** — `Odr (ODR)` order, `Txn (TXN)` transaction, `Sub (SUB)`, `Pap (PAP)`. Note (source doc comment): a transaction ID is **not unique** in the reporting system (balance-affecting vs not) — reconcile on `(TransactionId, balanceAffectingRecordsOnly)` or reference id.
- **Error: Case B — `SdkException<RawError>`** (the SDK's only Case-B op). No typed accessors: read `ex.Error.StatusCode`, `ex.Error.ReadAsString()` / `ReadAsJson<T>()`.
- (`SearchBalances` exists for balances; out of scope.)

### 2.5 Enums actually needed (`map/models/enums.md`) — all `StringEnum<T>`: use static members (`CheckoutPaymentIntent.Authorize`) or `.FromValue("AUTHORIZE")`; never C# enum syntax

| Enum | Members (wire values) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** ← 3DS/contingency signal |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `CaptureIncompleteReason` | `BuyerComplaint`, `Chargeback`, `Echeck`, `InternationalWithdrawal`, `Other`, `PendingReview`, `ReceivingPreferenceMandatesManualAction`, `Refunded`, `TransactionApprovedAwaitingFunding`, `Unilateral`, `VerificationRequired`, `DeclinedByRiskFraudFilters` (wire = SCREAMING_SNAKE) |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `RefundIncompleteReason` | `Echeck (ECHECK)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, `Maestro`, `Diners`, `Jcb`, `ChinaUnionPay`, `Rupay`, `CarteBancaire`, … `Unknown (UNKNOWN)` (full 30-member list on `enums.md`) |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)` (default), `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| `CardVerificationStatus` | `Verified (VERIFIED)`, `Failed (FAILED)` |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `ParesStatus` / `EnrollmentStatus` (3DS result) | `Y`, `N`, `U`, `A`, `C`, `R`, `D`, `I` / `Y`, `N`, `U`, `B` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |

### 2.6 Error handling (brief 6) — `sdk-map.md` *Error-handling model*

- Every operation is **throw-only** (no `…Result` variants anywhere in this SDK). On error status: `SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`) with `.Error: TError`.
- **Case A (39 of 40 ops — everything in scope except SearchTransactions)**: `TError` = `{Operation}Error : ApiError` in `PayPalServerSdk.Errors`. Accessors per operation in the tables above (`TryGetError(out Error)` for Orders/Payments; **`TryGetError1(out Error1)` for Vault**; `TryGetNoContent(out RawError)` on several Payments 500s; inherited `TryGetRawError(out RawError)` fallback on all).
- **Case B**: `SearchTransactions` only → `SdkException<RawError>`; `RawError`: `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`.
- HTTP status + body on Case A: the accessor's bracketed status tells you which shape a status maps to; for anything else `TryGetRawError` gives `StatusCode` + raw body. `TryGetRawError` is a fallback, **not** a catch-all for the typed shapes.
- Error payload records (all `PayPalServerSdk.Models`): `Error` — `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?` (`Issue (issue): string !req`, `Field`, `Value`, `Description`); `Error1` (Vault) — same core trio, `Details: IReadOnlyList<ErrorDetails1>?`, `Links: IReadOnlyList<ErrorLinkDescription>?` (**`Rel` is optional on `ErrorLinkDescription`** — the live API omits it on `RESOURCE_NOT_FOUND` doc links); `DefaultError` (SearchBalances only).

### 2.7 Direct-card / 3DS gotchas (brief, sandbox notes)

- Raw PAN via API requires **PCI SAQ D** (noted on the `CardRequest` map row); the setup-token/hosted-fields path exists to reduce that burden.
- 3DS/SCA contingency: `CardVerification.Method` defaults to `ScaWhenRequired`. When SCA triggers, the order comes back `Status = OrderStatus.PayerActionRequired` and the buyer must be redirected — `CardRequest.ExperienceContext` (`CardExperienceContext`: `ReturnUrl (return_url)`, `CancelUrl (cancel_url)`) carries the redirect targets, and the result is readable on `CardResponse.AuthenticationResult (authentication_result): AuthenticationResponse?` → `LiabilityShift (liability_shift): LiabilityShiftIndicator?`, `ThreeDSecure (three_d_secure): ThreeDSecureAuthenticationResponse?` (`AuthenticationStatus: ParesStatus?`, `EnrollmentStatus: EnrollmentStatus?`). So "no browser step" is not guaranteed by the API — the integration must detect `PAYER_ACTION_REQUIRED` and decide (fail the payment vs. redirect). Whether a given sandbox card number actually triggers a challenge is live-behaviour — **UNVERIFIED** here; handle both paths.
- `prefer: "return=representation"` on create/capture if you need the full payments collection in the immediate response; otherwise follow up with `GetOrder` / `GetAuthorizedPayment`.

## 3. Trap notes (hazards — load the named skill before coding that step)

- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind the SDK has lifetime requirements (socket-exhaustion vs stale-DNS trade-off) that a signature cannot show; the DI extension and the manual constructor differ in what they manage for you. **MUST load `dotnet-client-initialization`** before wiring the client.
- ⚠ Step 1 (auth) — when credentials must be set relative to client construction, where secrets come from, and what `Oauth2TokenStrategy` does and does not refresh for you. **MUST load `dotnet-authentication`** before wiring credentials.
- ⚠ Steps 2–6 (every call) — many optional parameters have **no C# default** and mis-bind in positional calls; named-argument discipline is required (and `ct:` really is the cancellation-token name). **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 2–6 (models) — enums are `StringEnum<T>` (not C# enums), records are immutable with `required` init members, and JSON fields with no modeled property are silently dropped on deserialize. **MUST load `dotnet-models`** before building payloads or mapping responses onto domain types.
- ⚠ Step 7 (error boundary) — which operations are Case A vs Case B, what `TryGetRawError` does not cover, and the two `JsonException` directions in §4. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ Steps 2–6 (resilience) — whether a failed write (`CreateOrder`, `CaptureAuthorizedPayment`, `RefundCapturedPayment`) can be safely re-sent, what `payPalRequestId` does and does not protect against, and what `RetryOptions.Timeout` actually bounds. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts or relying on idempotency.
- ⚠ Tests — the seam to fake for SDK-calling code and how to cover error paths without depending on SDK internals. **MUST load `dotnet-testing`** before writing integration-layer tests.

## 4. REQUIRED READING — load **before implementation starts**

This sheet deliberately does not carry these skills' contents; load each in full:

- `dotnet-client-initialization` — governs step 1 (client construction & DI registration).
- `dotnet-authentication` — governs step 1 (OAuth2 client-credentials wiring, secret sourcing).
- `dotnet-calling-endpoints` — governs steps 2–6 (every controller call, named-argument discipline).
- `dotnet-models` — governs steps 2–6 (request/response models, `StringEnum<T>` enums, required members).
- `dotnet-error-handling` — governs step 7 (the exception boundary). Mandatory for every integration:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary.
- `dotnet-configuration-resilience` — governs steps 2–6 (retries, timeouts, idempotency interaction, pagination).
- `dotnet-testing` — governs the integration layer's tests.

## 5. Assumptions & Blockers

- **Assumed**: sandbox first. The SDK declares only `ServerEnvironment.Sandbox`; production is reached by overriding `Server.Default.Sandbox.BaseUrl`. The production base-URL **string is not declared anywhere in the SDK source** — supply it from configuration (expected value per PayPal convention: `https://api-m.paypal.com` — **UNVERIFIED** against the SDK; it never appears in source).
- **Assumed**: "no browser/3DS step" means the integration does not *plan* for redirect, but the API can still return `PAYER_ACTION_REQUIRED` (§2.7) — the PublicApi endpoint needs a defined behaviour for that status (reject with a clear error, or return the approval link). Flagged, not decided.
- **Assumed**: the customer association for vaulting uses `Customer.MerchantCustomerId` (your own key) and/or the PayPal `Customer.Id`; `ListCustomerPaymentTokens` filters by the **PayPal** customer id — so the integration must persist the PayPal customer id returned in `PaymentTokenResponse.Customer.Id` alongside the local user.
- **Assumed**: JWT auth on PublicApi endpoints is orthogonal — PayPal credentials are server-side OAuth2 client credentials from configuration, never per-user.
- **Note**: map documents tag `v1.0.1` (commit `9653d18`); `dotnet add package` installs latest — if any name here fails to compile, trust the compiler and ask for a targeted re-lookup (drift report), never patch from memory.
- **Blockers**: none.
