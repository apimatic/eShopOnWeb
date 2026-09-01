# PayPal .NET SDK integration plan — eShopOnWeb (`src/PublicApi` + supporting layers)

Grounded in the bundled SDK map (`paypal-getting-started` → `sdk-map.md`, `map/operations/*.md`,
`map/models/*.md`) at source stamp `9653d18` / tag `v1.0.1`, plus four facts the map does not carry
that were resolved from the SDK source itself (flagged **source-verified** below).

## SDK identity & install

| Fact | Value | Source |
|---|---|---|
| NuGet package | `AsadAli.Checkout.Sdk` — install **version-less**: `dotnet add package AsadAli.Checkout.Sdk` (floats to latest; do not pin from memory) | `sdk-map.md` |
| Root namespace | `PayPalServerSdk` | `sdk-map.md` |
| Client class / options class | `PayPalServerSdkClient` / `PayPalServerSdkClientOptions` | `sdk-map.md` |
| Client ctor | `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI extension | `services.AddPayPalServerSdkClient(o => { ... })` — registers the client as a **singleton** over an `IHttpClientFactory`-created `HttpClient` | `ServiceCollectionExtensions.cs` (source-verified) |
| Controllers on client | `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch`, `client.Subscriptions` | `sdk-map.md` |

**Usings needed** (C# does not import child namespaces transitively — one `using` per row):

| Types | Namespace |
|---|---|
| Client, options, `ServerOptions`, `ServiceCollectionExtensions` | `PayPalServerSdk` |
| `ServerEnvironment`, `DefaultOptions` | `PayPalServerSdk.Servers` |
| Controllers (rarely named directly) | `PayPalServerSdk.Api` |
| All records (`OrderRequest`, `Money`, `CardRequest`, …) | `PayPalServerSdk.Models` |
| All enums (`CheckoutPaymentIntent`, `AuthorizationStatus`, …) | `PayPalServerSdk.Models.Enums` |
| Typed errors (`CreateOrderError`, …) | `PayPalServerSdk.Errors` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` (source-verified) |
| `IOAuth2TokenStrategy<T>` | `PayPalServerSdk.Core.Authentication.OAuth2` (source-verified) |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` |

## Scope & sequence

1. **Client & DI setup** — register via `AddPayPalServerSdkClient` in `src/PublicApi`; credentials, environment, BaseUrl override from config.
2. **Direct card authorize (features 1–2)** — `Orders.CreateOrder` (intent=AUTHORIZE, `payment_source.card`), then `Orders.AuthorizeOrder`.
3. **Capture at fulfilment (features 2, 5)** — `Payments.CaptureAuthorizedPayment`; read `SellerReceivableBreakdown`.
4. **Stale authorization handling (feature 3)** — `Payments.GetAuthorizedPayment` (status/`ExpirationTime` poll), `Payments.ReauthorizePayment`.
5. **Void (feature 4)** — `Payments.VoidPayment`.
6. **Refunds (feature 6)** — `Payments.RefundCapturedPayment` (+ `Payments.GetRefund`).
7. **Saved cards / vault (feature 7)** — `Vault.CreatePaymentToken` (direct server-side card vault), `Vault.ListCustomerPaymentTokens`, `Vault.GetPaymentToken`, `Vault.DeletePaymentToken`; pay with saved card via `CardRequest.VaultId` on a new `CreateOrder`. Setup-token flow (`Vault.CreateSetupToken` → `Vault.GetSetupToken` → exchange) documented as the browser-assisted alternative.
8. **Transaction search / reconciliation (feature 8)** — `TransactionSearch.SearchTransactions` with a manual page loop.
9. **Error boundary** — one translation layer over `SdkException<T>` per operation case (feature 11).
10. **Tests** — fake the `HttpClient` seam.

---

## CONTRACT SHEET

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

### Client construction, auth, environment, BaseUrl override (features 9)

| Fact | Contract | Source |
|---|---|---|
| Options properties | `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Logging: LoggingOptions` · `Server: ServerOptions` · `Oauth2: OAuth2ClientCredentials?` · `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `sdk-map.md` client-options |
| Credentials | `Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = … }` — both `required string`; optional `Scope: string?`. Namespace `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | source-verified (`OAuth2ClientCredentials.cs`) |
| Token request | SDK auto-issues `POST {BaseUrl}/v1/oauth2/token`, `Authorization: Basic base64(clientId:clientSecret)`, form body `grant_type=client_credentials` (+`scope` if set). Token is **cached and auto-refreshed on expiry**; no manual token code. If `Oauth2` is null the SDK sends **no auth at all** (silent `NoneAuthScheme`) — always set credentials | source-verified (`AuthSchemes.cs`, `OAuth2ClientCredentialsStrategy.cs`, `OAuth2Scheme.cs`) |
| Environment selection | `options.Environment = ServerEnvironment.Sandbox` — the **only** member the SDK models; there is no `Live`/`Production` member. `ServerEnvironment.Default()` = `Sandbox` | source-verified (`ServerEnvironment.cs`) |
| BaseUrl override | `options.Server.Default.Sandbox.BaseUrl = "<url>"` — `ServerOptions` (root namespace) → `Default: DefaultOptions` (`PayPalServerSdk.Servers`) → `Sandbox: DefaultOptions.SandboxOptions` → `BaseUrl: string`, default `"https://api-m.sandbox.paypal.com"`. The override is resolved through the **same** `Server.Default(path)` for every API call **and** for the `/v1/oauth2/token` credential request, so a set BaseUrl is used verbatim for EVERY PayPal call including auth | source-verified (`ServerOptions.cs`, `DefaultOptions.cs`, `AuthSchemes.cs`) |
| Live environment | No modeled member — point `BaseUrl` at the live API base from config. The live hostname is **not present anywhere in the SDK source** (only the sandbox URL is), so its value must come from PayPal configuration/docs, not from this sheet — `UNVERIFIED` | source-verified (absence) |

### Orders (features 1, 2, 7, 10, 11) — `client.Orders` · `operations/Orders.md`

| Op | Signature (verbatim; all `string?` “must-pass-explicitly” params have **no default** — pass `null` to skip) | Request body | Returns / envelope reads | Error case |
|---|---|---|---|---|
| `CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderRequest` (required, non-null) | `PayPalServerSdk.Models.Order` — read `Id (id)`, `Status (status): OrderStatus?`, `PurchaseUnits`; **no wrapper field** | A: `SdkException<CreateOrderError>` · `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` |
| `AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderAuthorizeRequest?` — pass `null` when the card is already on the order's `payment_source` (our flow) | `OrderAuthorizeResponse` — authorization at `resp.PurchaseUnits[0].Payments.Authorizations[0]` (`AuthorizationWithAdditionalData`): `Id`, `Status: AuthorizationStatus?`, `Amount: Money?`, `ExpirationTime (expiration_time): string?` | A: `SdkException<AuthorizeOrderError>` · `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError` |
| `GetOrder` | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `Order` | A: `SdkException<GetOrderError>` · `TryGetError(out Error)` [401, 404] · `TryGetRawError` |

`OrderRequest` fields (`records-1-Ac-Pa.md`): `Intent (intent): CheckoutPaymentIntent !req` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` · `PaymentSource (payment_source): PaymentSource?` · `Payer (payer): Payer?` · `ApplicationContext (application_context): OrderApplicationContext?`

`PurchaseUnitRequest` fields: `Amount (amount): AmountWithBreakdown !req` · `ReferenceId (reference_id): string?` · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `Description (description): string?` · `Items (items): IReadOnlyList<ItemRequest>?` · `Payee`, `Shipping`, …

`AmountWithBreakdown`: `CurrencyCode (currency_code): string !req` · `Value (value): string !req` · `Breakdown (breakdown): AmountBreakdown?`. **`Value` is a string — format the order total with invariant culture to the cent (`"123.45"`); currency from config.**

`PaymentSource` (`records-2-Pa-Ve.md`): `Card (card): CardRequest?` · `Token (token): Token?` · wallets… — for direct card set only `Card`.

`CardRequest` (`records-1-Ac-Pa.md`): `Number (number): string?` · `Expiry (expiry): string?` (`"YYYY-MM"`) · `SecurityCode (security_code): string?` · `Name (name): string?` · `BillingAddress (billing_address): Address?` (`Address.CountryCode (country_code): string !req` is the only required address field) · `VaultId (vault_id): string?` ← **saved-card payments put the vault payment-token id here** · `Attributes (attributes): CardAttributes?` · `ExperienceContext (experience_context): CardExperienceContext?` (`ReturnUrl`/`CancelUrl` — 3DS redirect targets). Doc note on the record: passing PAN/CVV directly requires **PCI SAQ D**.

`CardAttributes`: `Verification (verification): CardVerification?` → `Method (method): OrdersCardVerificationMethod? = ScaWhenRequired` · `Vault (vault): VaultInstructionBase?` → `StoreInVault (store_in_vault): StoreInVaultInstruction?` (`OnSuccess (ON_SUCCESS)` — vault-on-authorize alternative to the Vault controller).

3DS detection: `Order.Status == OrderStatus.PayerActionRequired` and/or `Order.PaymentSource.Card.AuthenticationResult` (`AuthenticationResponse` → `ThreeDSecure (three_d_secure): ThreeDSecureAuthenticationResponse?` → `AuthenticationStatus: ParesStatus?`, `EnrollmentStatus: EnrollmentStatus?`; `LiabilityShift: LiabilityShiftIndicator?`). If status is `PayerActionRequired`, a browser challenge is required and the pure server-side flow stops there — see Blockers.

### Payments (features 2–6, 10, 11) — `client.Payments` · `operations/Payments.md`

| Op | Signature (verbatim) | Request body | Returns / envelope reads | Error case |
|---|---|---|---|---|
| `CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CaptureRequest?`: `Amount (amount): Money?` (omit = full remaining) · `InvoiceId` · `NoteToPayer (note_to_payer)` · `FinalCapture (final_capture): bool? = false` | `CapturedPayment` — **no wrapper**. Capture details: `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?` → `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?` → each `Money.CurrencyCode`/`Money.Value`. Full path: `capture.SellerReceivableBreakdown.PaypalFee.Value`. Also `Id`, `Status: CaptureStatus?`, `FinalCapture` | A: `SdkException<CaptureAuthorizedPaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| `GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentAuthorization`: `Status: AuthorizationStatus?`, `StatusDetails.Reason: AuthorizationIncompleteReason?`, `Amount: Money?`, `ExpirationTime (expiration_time): string?`, `Id` | A: `SdkException<GetAuthorizedPaymentError>` · `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] |
| `ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ReauthorizeRequest?`: `Amount (amount): Money?` — **only** supported param | `PaymentAuthorization` (new 3-day honor period). Constraints from the op contract: reauthorizable days 4–29 after the 3-day honor period; ≤115% of original amount (≤ +$75) in US; after 30 days a reauthorize is rejected → detect via 422 `TryGetError(out Error)` → `Error.Details[].Issue` | A: `SdkException<ReauthorizePaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] |
| `VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — **note param order: `payPalRequestId` is 4th here, unlike the other ops** | — | `PaymentAuthorization` (expect `Status == AuthorizationStatus.Voided`) | A: `SdkException<VoidPaymentError>` · `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] |
| `RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `RefundRequest?`: **full refund = pass `null` (or empty body)**; partial = `Amount (amount): Money?` · `CustomId (custom_id): string?` · `InvoiceId` · `NoteToPayer (note_to_payer): string?` | `Refund`: `Id`, `Status: RefundStatus?`, `StatusDetails.Reason: RefundIncompleteReason?`, `Amount: Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount`) | A: `SdkException<RefundCapturedPaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] |
| `GetRefund` | `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `Refund` | A: `SdkException<GetRefundError>` · `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] |
| `GetCapturedPayment` | `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `CapturedPayment` (re-read `SellerReceivableBreakdown` any time) | A: `SdkException<GetCapturedPaymentError>` · `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] |

**Idempotency (feature 10)** — the `PayPal-Request-Id` header is the `payPalRequestId` parameter, present on: `CreateOrder`, `AuthorizeOrder`, `CaptureOrder`, `CaptureAuthorizedPayment`, `ReauthorizePayment`, `RefundCapturedPayment`, `VoidPayment`, `Vault.CreatePaymentToken`, `Vault.CreateSetupToken`. Exact parameter name on every signature: **`payPalRequestId`** (position varies — `VoidPayment` has it 4th). Caller supplies a stable key per logical operation (e.g. `refund-{captureId}-{attempt-n}` or a stored GUID): repeats under the same key do not double-refund; distinct keys per partial refund of the same capture work independently. `GetAuthorizedPayment`, `GetCapturedPayment`, `GetRefund`, `GetOrder`, `ListCustomerPaymentTokens`, `GetPaymentToken`, `DeletePaymentToken`, `SearchTransactions` take **no** idempotency key (reads/deletes).

`prefer`: default `"return=minimal"` on all write ops. Pass `prefer: "return=representation"` on capture/refund/authorize when you need the full body (e.g. `SellerReceivableBreakdown`) in the immediate response; otherwise re-read via the GET ops.

### Vault / saved cards (feature 7) — `client.Vault` · `operations/Vault.md`

| Op | Signature (verbatim) | Request body | Returns / envelope reads | Error case |
|---|---|---|---|---|
| `CreatePaymentToken` (direct server-side card vault — **the no-browser path**) | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenRequest`: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` → `Card (card): PaymentTokenRequestCard?` (`Number`, `Expiry`, `SecurityCode`, `Name`, `Brand: CardBrand?`, `BillingAddress`) · `Customer (customer): Customer?` → `Id (id): string?` (PayPal customer id) / `MerchantCustomerId (merchant_customer_id): string?` (your shopper id) | `PaymentTokenResponse`: `Id (id)` = the vault token to store · `Customer: CustomerResponse?` · `PaymentSource: PaymentTokenResponsePaymentSource?` → `Card: CardPaymentTokenEntity?` → `Brand: CardBrand?`, `LastDigits (last_digits): string?`, `Expiry` — **display these; the PAN is never returned** | A: `SdkException<CreatePaymentTokenError>` · `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError` |
| `ListCustomerPaymentTokens` | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` — wire: `customer_id` ← `customerId` (query param; there is **no** `PayPal-Customer-Id` header param in this SDK) | — | `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `TotalItems`, `TotalPages`, `Links`. Loop `page` to `TotalPages` (default `pageSize` is only **5**) | A: `SdkException<ListCustomerPaymentTokensError>` · `TryGetError1(out Error1)` [400, 403, 500] |
| `GetPaymentToken` | `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentTokenResponse` (brand/last-digits as above) | A: `SdkException<GetPaymentTokenError>` · `TryGetError1(out Error1)` [403, 404, 422, 500] |
| `DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `void` (Task) — success = no throw | A: `SdkException<DeletePaymentTokenError>` · `TryGetError1(out Error1)` [400, 403, 500] |
| `CreateSetupToken` (browser-assisted alternative) | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `SetupTokenRequest`: `PaymentSource: SetupTokenRequestPaymentSource !req` → `Card: SetupTokenRequestCard?` (adds `VerificationMethod: VaultCardVerificationMethod?` — `ScaWhenRequired`/`ScaAlways` — and `ExperienceContext: VaultCardExperienceContext?`) · `Customer` | `SetupTokenResponse`: `Id`, `Status: PaymentTokenStatus? = Created` — becomes `PayerActionRequired` when verification needs the buyer (browser) | A: `SdkException<CreateSetupTokenError>` · `TryGetError1(out Error1)` [400, 403, 422, 500] |
| `GetSetupToken` | `GetSetupToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `SetupTokenResponse` | A: `SdkException<GetSetupTokenError>` · `TryGetError1(out Error1)` [403, 404, 422, 500] |

**Paying with a saved card**: `CreateOrder` with `OrderRequest.PaymentSource = new PaymentSource { Card = new CardRequest { VaultId = "<payment-token-id>" } }`, intent=AUTHORIZE, then `AuthorizeOrder` as usual. Do **not** use `PaymentSource.Token` — its `Token.Type: TokenType` models only `BillingAgreement (BILLING_AGREEMENT)`, not vault payment tokens (`records-2-Pa-Ve.md`, `enums.md`). Setup-token exchange (if the browser flow is ever used): `PaymentTokenRequestPaymentSource.Token = new VaultTokenRequest { Id = setupTokenId, Type = VaultTokenRequestType.SetupToken }`.

Vault customer mechanics: the customer linkage is the `Customer.Id` / `Customer.MerchantCustomerId` body field at create time and the `customer_id` query param at list time. Store the PayPal `CustomerResponse.Id` (or your `MerchantCustomerId`) against the shopper. Doc note on the client: the Vault API is **US only**.

### Transaction search / reconciliation (feature 8) — `client.TransactionSearch` · `operations/TransactionSearch.md`

`SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`

- `startDate`/`endDate` are **required strings** — ISO-8601 (`yyyy-MM-ddTHH:mm:ssZ`); wire names `start_date`/`end_date`. The 8 filter params (`transactionId`…`terminalId`) are must-pass-explicitly — pass `null`.
- Returns `SearchResponse` (`records-2-Pa-Ve.md`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` · `Page (page): int?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Links`. **Whole-range coverage = manual loop**: keep calling with `page: n` until `n >= TotalPages` (or `TransactionDetails` comes back empty); the SDK has no pagination helper.
- `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?` — reconciliation fields: `TransactionId (transaction_id): string?` · `TransactionStatus (transaction_status): string?` — **plain `string`, not an enum**; compare literally · `TransactionAmount (transaction_amount): Money?` · `FeeAmount (fee_amount): Money?` · `TransactionInitiationDate` / `TransactionUpdatedDate: string?` · `InvoiceId (invoice_id): string?` · `CustomField (custom_field): string?` ← ties back to the order if you set `PurchaseUnitRequest.CustomId`/`InvoiceId` · `PaypalReferenceId (paypal_reference_id): string?` + `PaypalReferenceIdType: PayPalReferenceIdType?` (`Odr (ODR)` = order id, `Txn (TXN)`).
- **Error case B (the only one in scope)**: `SdkException<RawError>` — no typed accessors; read `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, or `ReadAsJson<DefaultError>()` (`DefaultError`: `Name`, `Message`, `DebugId`, `Details: IReadOnlyList<TransactionSearchErrorDetails>?` — `Issue` is the required field).
- Contract notes from the op row: executed transactions take up to **3 hours** to appear; history limited to the previous 3 years; if you pass any optional filter, `ending_balance` is empty.
- (`SearchBalances` exists on the same controller but is out of scope.)

### Enum value tables actually needed (`map/models/enums.md`; all `StringEnum<T>` in `PayPalServerSdk.Models.Enums` — construct via static members, e.g. `CheckoutPaymentIntent.Authorize`, never quoted strings)

| Enum | Members (wire values) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created`, `Saved`, `Approved`, `Voided`, `Completed`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` — **there is no `EXPIRED` member**; a stale/expired authorization is detected via `PaymentAuthorization.ExpirationTime`, a `Denied`/`Voided` status, or a rejected reauthorize (422), not via an enum value |
| `AuthorizationIncompleteReason` | `PendingReview`, `DeclinedByRiskFraudFilters` |
| `CaptureStatus` | `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed` |
| `CaptureIncompleteReason` | `BuyerComplaint`, `Chargeback`, `Echeck`, `InternationalWithdrawal`, `Other`, `PendingReview`, `ReceivingPreferenceMandatesManualAction`, `Refunded`, `TransactionApprovedAwaitingFunding`, `Unilateral`, `VerificationRequired`, `DeclinedByRiskFraudFilters` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `RefundIncompleteReason` | `Echeck (ECHECK)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard`, `Discover`, `Amex`, … `Unknown (UNKNOWN)` |
| `CardType` | `Credit`, `Debit`, `Prepaid`, `Store`, `Unknown` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| `VaultCardVerificationMethod` | `ScaWhenRequired`, `ScaAlways` |
| `CardVerificationStatus` | `Verified (VERIFIED)`, `Failed (FAILED)` |
| `PaymentTokenStatus` | `Created`, `PayerActionRequired`, `Approved`, `Vaulted`, `Tokenized` |
| `VaultStatus` | `Vaulted`, `Created`, `Approved` |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` only |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` only |
| `SellerProtectionStatus` | `Eligible`, `PartiallyEligible`, `NotEligible` |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `ParesStatus` / `EnrollmentStatus` | `Y`,`N`,`U`,`A`,`C`,`R`,`D`,`I` / `Y`,`N`,`U`,`B` |
| `DisbursementMode` | `Instant (INSTANT)`, `Delayed (DELAYED)` |

### Error model (feature 11) — `sdk-map.md` error-handling section

- Every operation is **throw-only** (no `…Result` variants exist anywhere in this SDK). On error status it throws `SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`) with `.Error: TError`.
- **Case A (39 of 40 ops — everything above except `SearchTransactions`)**: `TError` is a generated `{Operation}Error : ApiError` in `PayPalServerSdk.Errors` with the status-specific `TryGet…` accessors listed per operation above, plus inherited `TryGetRawError(out RawError)` for unlisted statuses. `TryGetRawError` is a fallback, not a catch-all — check the typed accessor first.
- Typed payload shapes: Orders/Payments ops → `Error` (`Name`, `Message`, `DebugId` — all `string !req` — `Details: IReadOnlyList<ErrorDetails>?` with `Issue !req`, `Field`, `Value`, `Description`; `Links: IReadOnlyList<LinkDescription>?`). Vault ops → `Error1` (same, but `Details` is `ErrorDetails1` and `Links` is `ErrorLinkDescription` whose `Rel` is **optional**). TransactionSearch balances → `DefaultError`. (`records-1-Ac-Pa.md`)
- **Case B (`SearchTransactions` only)**: `SdkException<RawError>` — `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`.
- HTTP status per failure: from the accessor brackets above (e.g. 409 on capture/refund = state conflict; 422 on reauthorize = not renewable).

---

## Trap notes

> ⚠ Step 1 (client registration) — the DI extension registers the SDK client as a singleton over a factory-created `HttpClient`; hand-rolling `new PayPalServerSdkClient(new HttpClient(), …)` per request socket-exhausts, and the options object's lifetime interacts with that. **MUST load `dotnet-client-initialization`** before wiring DI.

> ⚠ Step 1 (auth) — credentials must be set on the options **before** the client is constructed (the DI callback is the place); a null `Oauth2` silently produces an unauthenticated client rather than a config error. **MUST load `dotnet-authentication`**.

> ⚠ Steps 2–8 (every call) — most optional parameters have **no C# default** and mis-bind positionally; call with named arguments (`payPalRequestId:`, `body:`, `ct:`) and pass explicit `null` for skipped must-pass params. **MUST load `dotnet-calling-endpoints`**.

> ⚠ Steps 2–8 (models) — enums are `StringEnum<T>` records, not C# enums (no `switch` exhaustiveness, construct via static members); records are immutable with `required` members that must be set in the initializer; JSON fields the SDK doesn't model are **dropped on deserialize** (matters for `TransactionInformation.TransactionStatus` being a bare string and for any extra wire fields). **MUST load `dotnet-models`**.

> ⚠ Step 9 (error boundary) — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:
> - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
> - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
>
> **MUST load `dotnet-error-handling`** before writing that boundary.

> ⚠ Step 9 (error boundary, cont.) — which operations are Case A vs Case B, and whether a failed write may have executed server-side before the error surfaced (a 500/`TryGetNoContent` on `CaptureAuthorizedPayment` or `RefundCapturedPayment` is exactly the case your idempotency key exists for). **MUST load `dotnet-error-handling`**.

> ⚠ Steps 1, 2, 6, 8 (resilience) — what `RetryOptions.Timeout` actually bounds, which verbs/statuses retry, and whether a transport failure on a non-idempotent `POST` (create-order, capture, refund) can execute twice despite your `payPalRequestId`; also whether any pagination helper exists (it does not — the page loops above are manual). **MUST load `dotnet-configuration-resilience`** before tuning `Retry` or relying on retry behaviour for writes.

> ⚠ Step 10 (tests) — the test seam and how to fake error envelopes (`SdkException<T>` construction) without the live API. **MUST load `dotnet-testing`**.

---

## REQUIRED READING

Load every one of these **before implementation starts**. This sheet deliberately does not carry their contents; the trap notes above name the hazards, the skills carry the resolutions.

- `dotnet-client-initialization` — governs step 1 (client construction, `HttpClient` ownership, DI registration).
- `dotnet-authentication` — governs step 1 (credentials wiring, secret loading, token refresh behaviour).
- `dotnet-calling-endpoints` — governs steps 2–8 (named-argument calling convention, must-pass-explicitly nulls, async/cancellation).
- `dotnet-models` — governs steps 2–8 (required members, `StringEnum<T>`, wire names vs C# names, dropped unmodeled fields).
- `dotnet-error-handling` — governs step 9 (Case A/B mechanics, `TryGet…` usage, the two `JsonException` boundary hazards above).
- `dotnet-configuration-resilience` — governs steps 1, 2, 6, 8 (retry/timeout semantics, base-URL/server selection, manual pagination, logging).
- `dotnet-testing` — governs step 10 (faking the SDK seam, covering error paths).

---

## Assumptions & Blockers

**Assumptions**
- Currency comes from config and amounts are formatted as invariant-culture strings to the cent (`Money.Value` is `string`); no currency conversion is performed by the SDK.
- "Save a card for a shopper" maps to the direct server-side `CreatePaymentToken` flow (merchant is PCI SAQ D eligible, per the brief's "enabled for direct card processing and card vaulting"); the setup-token flow is documented as the browser-assisted fallback.
- The shopper linkage for vault list/lookup uses `MerchantCustomerId` = eShopOnWeb buyer id, with PayPal's `Customer.Id` stored after first vault.
- Refund idempotency keys are generated and persisted by the caller per logical refund; the SDK only transports them via `payPalRequestId`.
- Live-environment base URL is supplied via configuration (the SDK models only `Sandbox`; the live hostname is not present in the SDK source — `UNVERIFIED` from the SDK side).

**Blockers / caveats**
- **3DS**: if PayPal/jurisdiction requires SCA for a direct card authorization, the order returns `OrderStatus.PayerActionRequired` and a browser challenge is mandatory — the pure no-browser flow cannot complete for that transaction. Mitigation available in-contract: `CardVerification.Method = OrdersCardVerificationMethod.ScaWhenRequired` (default) or `AvsCvv`, and detecting `PayerActionRequired` to fall back. Whether the sandbox test card `4111111111111111` authorizes without a challenge is only confirmable against the live sandbox — `UNVERIFIED`.
- **No `EXPIRED` authorization status**: `AuthorizationStatus` models only `Created/Captured/Denied/PartiallyCaptured/Voided/Pending`. Staleness detection must use `PaymentAuthorization.ExpirationTime` plus a 422 from `ReauthorizePayment` (`Error.Details[].Issue`) as the not-renewable signal.
- **Reauthorize window**: contractually limited to days 4–29 after the 3-day honor period (≤115% / +$75 US amount cap); after 30 days a new order+authorize is required — the integration must treat a 422 here as "create a new authorization", not retry.
- **Transaction search latency**: up to 3 hours for executed transactions to appear; reconciliation must tolerate this lag. 3-year history cap.
- **Vault API is documented US-only** on the client; sandbox availability for the merchant account is per the brief.
- `SearchTransactions` is the SDK's only Case B operation — its error body has no typed accessor; use `ReadAsJson<DefaultError>()` best-effort and fall back to `ReadAsString()`.
