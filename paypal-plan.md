# PayPal .NET SDK integration plan — eShopOnWeb `src/PublicApi`

SDK: `AsadAli.Checkout.Sdk` (NuGet; install **version-less** — `dotnet add package AsadAli.Checkout.Sdk`, floats to latest; this sheet is grounded against the SDK map for tag `v1.0.1`). Root namespace `PayPalServerSdk`; client `PayPalServerSdkClient`; options `PayPalServerSdkClientOptions`.

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Install package into `Infrastructure` (SDK-calling layer); wire client + auth + base-URL override in `PublicApi` DI | — (client construction, §3.4) |
| 2 | Create PayPal order, intent=AUTHORIZE, with direct card payment source (single-step) | `Orders.CreateOrder` |
| 3 | (Alternative to 2) Two-step: create bare order, then authorize with payment source | `Orders.CreateOrder` + `Orders.AuthorizeOrder` |
| 4 | Inspect order status / authorization ids | `Orders.GetOrder` |
| 5 | Capture the authorization at fulfilment (full amount, fee/net breakdown) | `Payments.CaptureAuthorizedPayment` (+ `Payments.GetCapturedPayment`) |
| 6 | Void (release) an authorization on cancel | `Payments.VoidPayment` |
| 7 | Reauthorize a stale authorization; detect "cannot be renewed" | `Payments.GetAuthorizedPayment` + `Payments.ReauthorizePayment` |
| 8 | Refund a capture (full or partial) with caller idempotency key | `Payments.RefundCapturedPayment` (+ `Payments.GetRefund`) |
| 9 | Vault: save a card, list a customer's tokens, get one, delete one | `Vault.CreatePaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.GetPaymentToken`, `Vault.DeletePaymentToken` (setup-token alternative: `Vault.CreateSetupToken` → `Vault.CreatePaymentToken`) |
| 10 | Pay an order (intent=AUTHORIZE) with a vaulted card | `Orders.CreateOrder` with `PaymentSource.Card.VaultId` |
| 11 | Reconciliation: transaction search over a date range, paged | `TransactionSearch.SearchTransactions` |
| 12 | Error boundary + resilience + tests (cross-cutting) | all above |

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

Namespaces for this scope (`sdk-map.md`): client/options `PayPalServerSdk` · controllers `PayPalServerSdk.Api` · records `PayPalServerSdk.Models` · enums `PayPalServerSdk.Models.Enums` · typed error classes `PayPalServerSdk.Errors` · `SdkException<T>` `PayPalServerSdk.Core.Exceptions` (source path `Core/Exceptions/SdkException.cs`) · `RawError` `PayPalServerSdk.Core.ErrorResponse` · `RetryOptions` `PayPalServerSdk.Core.Configuration` · `OAuth2ClientCredentials` `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` · `ServerEnvironment`/`DefaultOptions` `PayPalServerSdk.Servers`.

### 2.1 Orders (`client.Orders` — `operations/Orders.md`)

| Op | Signature (verbatim) | Request body | Returns / envelope | Error |
|---|---|---|---|---|
| CreateOrder | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 5 nullable params **must be passed explicitly** (`null` to skip) | `OrderRequest` (§2.5). `payPalRequestId` → `PayPal-Request-Id` header; per SDK doc it is **mandatory for single-step create-with-payment-source** calls — always pass the caller's idempotency key here. `prefer` → `Prefer` header | `Order` (§2.5) — flat, no wrapper; authorization ids surface only after authorize: `PurchaseUnits[i].Payments.Authorizations[j].Id` | Case A `SdkException<CreateOrderError>`: `TryGetError(out Error)` [400, 401, 422], `TryGetRawError(out RawError)` fallback |
| AuthorizeOrder (two-step alt) | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 5 must-pass-explicitly | `OrderAuthorizeRequest { PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = … /* CardRequest */ } }` | `OrderAuthorizeResponse` — same fields as `Order` (`Id`, `Status`, `PurchaseUnits`…); read auth id at `PurchaseUnits[i].Payments.Authorizations[j].Id` | Case A `SdkException<AuthorizeOrderError>`: `TryGetError(out Error)` [400, 401, 403, 404, 422, 500], fallback raw |
| GetOrder | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `fields`, `payPalMockResponse`, `payPalAuthAssertion` must-pass-explicitly; `fields` is a query param (pass `null`) | — | `Order` — `Status` (`OrderStatus`), `PurchaseUnits[i].Payments.Authorizations/Captures/Refunds` | Case A `SdkException<GetOrderError>`: `TryGetError(out Error)` [401, 404], fallback raw |

### 2.2 Payments (`client.Payments` — `operations/Payments.md`)

| Op | Signature (verbatim) | Request body | Returns / envelope | Error |
|---|---|---|---|---|
| CaptureAuthorizedPayment | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 must-pass-explicitly | `CaptureRequest { Amount = new Money { CurrencyCode, Value }, InvoiceId, FinalCapture = true, NoteToPayer }` (all optional; omit `Amount` for full). Pass `payPalRequestId` = caller idempotency key. Pass `prefer: "return=representation"` (see §5 UNVERIFIED-1) | `CapturedPayment` — flat: `Id`, `Status` (`CaptureStatus`), `StatusDetails.Reason` (`CaptureIncompleteReason`), `Amount`, `FinalCapture`, `SellerReceivableBreakdown` → `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?` | Case A `SdkException<CaptureAuthorizedPaymentError>`: `TryGetError(out Error)` [400, 401, 403, 404, 409, 422], `TryGetNoContent(out RawError)` [500], fallback raw |
| VoidPayment | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 must-pass-explicitly. **Note param order: `payPalAuthAssertion` comes before `payPalRequestId`** | none | `PaymentAuthorization` — `Id`, `Status` (`AuthorizationStatus`; expect `Voided`) | Case A `SdkException<VoidPaymentError>`: `TryGetError(out Error)` [401, 403, 404, 409, 422], `TryGetNoContent(out RawError)` [500], fallback raw. 409/422 = already captured/voided |
| GetAuthorizedPayment | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 2 must-pass-explicitly | — | `PaymentAuthorization` — `Status`, `Amount`, `ExpirationTime (expiration_time): string?`, `StatusDetails.Reason` (`AuthorizationIncompleteReason`) | Case A `SdkException<GetAuthorizedPaymentError>`: `TryGetError(out Error)` [401, 403, 404], `TryGetNoContent(out RawError)` [500], fallback raw |
| ReauthorizePayment | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 must-pass-explicitly | `ReauthorizeRequest { Amount = new Money { … } }` (only `Amount` exists). Pass `payPalRequestId` | `PaymentAuthorization` (new 3-day honor period) | Case A `SdkException<ReauthorizePaymentError>`: `TryGetError(out Error)` [400, 401, 403, 404, 422], `TryGetNoContent(out RawError)` [500], fallback raw. **422 here = cannot be renewed** — surface `Error.Name`/`Message`/`DebugId` + `Details[].Issue` to the operator |
| RefundCapturedPayment | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 must-pass-explicitly | `RefundRequest`: `Amount (amount): Money?`, `CustomId`, `InvoiceId`, `NoteToPayer`. **Full refund = empty body** (`new RefundRequest()` or `null`); partial = set `Amount`. Pass `payPalRequestId` = caller idempotency key | `Refund` — flat: `Id`, `Status` (`RefundStatus`), `StatusDetails.Reason` (`RefundIncompleteReason`), `Amount`, `SellerPayableBreakdown` → `GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount` (all `Money?`) | Case A `SdkException<RefundCapturedPaymentError>`: `TryGetError(out Error)` [400, 401, 403, 404, 409, 422], `TryGetNoContent(out RawError)` [500], fallback raw |
| GetCapturedPayment / GetRefund | `GetCapturedPayment(string captureId, string? payPalMockResponse, …)` / `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, …)` — must-pass-explicitly as listed | — | `CapturedPayment` / `Refund` (shapes above) | Case A `SdkException<GetCapturedPaymentError>` / `SdkException<GetRefundError>`: `TryGetError(out Error)` [401, 403, 404], `TryGetNoContent(out RawError)` [500], fallback raw |

Reauthorize window (from the operation's map notes, `operations/Payments.md`): reauthorize from day 4 to day 29 after the original authorization; after 30 days create a **new** authorization instead; allowed amount context-dependent (e.g. US up to 115% of original, max +$75). `AuthorizationStatus` has **no `Expired` member** (§2.6) — detect staleness from `PaymentAuthorization.ExpirationTime` and from a failed reauthorize (422), never from a status value.

### 2.3 Vault (`client.Vault` — `operations/Vault.md`)

| Op | Signature (verbatim) | Request body | Returns / envelope | Error |
|---|---|---|---|---|
| CreatePaymentToken (direct vault) | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must-pass-explicitly (pass the idempotency key) | `PaymentTokenRequest { Customer = new Customer { Id = … /* or */ MerchantCustomerId = … }, PaymentSource = new PaymentTokenRequestPaymentSource { Card = new PaymentTokenRequestCard { Number, Expiry, SecurityCode, Name, BillingAddress = new Address { CountryCode = "US", … } } } }` — `PaymentSource` is `!req` | `PaymentTokenResponse` — flat: `Id (id)` = the vault id to persist; `Customer (CustomerResponse { Id, MerchantCustomerId })`; `PaymentSource.Card` (`CardPaymentTokenEntity`) → `Brand (brand): CardBrand?`, `LastDigits (last_digits)`, `Expiry`, `VerificationStatus` | Case A `SdkException<CreatePaymentTokenError>`: `TryGetError1(out Error1)` [400, 403, 404, 422, 500], fallback raw |
| CreateSetupToken (alt flow) | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must-pass-explicitly | `SetupTokenRequest { Customer, PaymentSource = new SetupTokenRequestPaymentSource { Card = new SetupTokenRequestCard { Number, Expiry, SecurityCode, …, VerificationMethod (VaultCardVerificationMethod?), ExperienceContext (VaultCardExperienceContext?) } } }` | `SetupTokenResponse` — `Id`, `Status (PaymentTokenStatus? = Created)`, `Customer`, `Links`. Then exchange: `CreatePaymentToken` with `PaymentTokenRequestPaymentSource { Token = new VaultTokenRequest { Id = setupTokenId, Type = VaultTokenRequestType.SetupToken } }` | Case A `SdkException<CreateSetupTokenError>`: `TryGetError1(out Error1)` [400, 403, 422, 500], fallback raw |
| ListCustomerPaymentTokens | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `customerId` → query `customer_id` | — | `CustomerVaultPaymentTokensResponse` — `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `TotalItems`, `TotalPages`, `Customer (VaultResponseCustomer { Id, MerchantCustomerId })`, `Links`. Page manually: loop `page` 1…`TotalPages` (no SDK pager) | Case A `SdkException<ListCustomerPaymentTokensError>`: `TryGetError1(out Error1)` [400, 403, 500], fallback raw |
| GetPaymentToken | `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentTokenResponse` (as above) | Case A `SdkException<GetPaymentTokenError>`: `TryGetError1(out Error1)` [403, 404, 422, 500], fallback raw |
| DeletePaymentToken | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `void` (Task) — success = no exception | Case A `SdkException<DeletePaymentTokenError>`: `TryGetError1(out Error1)` [400, 403, 500], fallback raw |

Customer identity (map rows `PaymentTokenRequest`/`Customer`): `Customer` carries both `Id (id)` (PayPal-assigned) and `MerchantCustomerId (merchant_customer_id)` (your reference); both optional. **Directive:** persist `PaymentTokenResponse.Customer.Id` at vault time and pass that as `customerId` to `ListCustomerPaymentTokens`. Whether the list endpoint accepts a merchant-supplied id is UNVERIFIED (§5-2).

### 2.4 Paying with a vaulted card + Transaction Search

**Vaulted card as order payment source** (`records-1`, `Models/CardRequest.cs`): on `CreateOrder`, set `OrderRequest.PaymentSource = new PaymentSource { Card = new CardRequest { VaultId = "<vault payment token id>" } }`. `CardRequest.VaultId (vault_id)` is documented in the SDK as "The PayPal-generated ID for the vaulted payment source… stored on the merchant's server so the saved payment source can be used for future transactions." Do **not** use `PaymentSource.Token` for v3 vault tokens — `Token.Type` is `TokenType`, whose only declared member is `BillingAgreement (BILLING_AGREEMENT)` (`enums.md`).

**TransactionSearch** (`client.TransactionSearch` — `operations/TransactionSearch.md`):

| Op | Signature (verbatim) | Returns / envelope | Error |
|---|---|---|---|
| SearchTransactions | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 8 nullable filters **must be passed explicitly** (`null`); **call with named arguments** | `SearchResponse` — `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page`, `TotalItems`, `TotalPages`, `Links`. Page through ALL results manually: start `page: 1` (SDK default), loop while `page < TotalPages`; no SDK-level pager | **Case B** `SdkException<RawError>` — no typed accessors: `ex.Error.StatusCode`, `ex.Error.ReadAsString()` / `ReadAsJson<T>()` |

`startDate`/`endDate` (source doc, `Api/TransactionSearch.cs`): RFC 3339 §5.6 Internet date-time format, **seconds required**, fractional seconds optional; **maximum supported range is 31 days** — chunk longer reconciliations into ≤31-day windows. `fields` default `"transaction_info"`; pass a comma-separated list (e.g. `"transaction_info,payer_info"`) for more. Reconciliation fields live at `TransactionDetails[i].TransactionInfo` (`TransactionInformation`, `records-2`): `TransactionId (transaction_id)`, `PaypalReferenceId (paypal_reference_id)`, `PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?` (`Odr (ODR)` = order, `Txn (TXN)` = transaction, `Sub`, `Pap`), `TransactionEventCode (transaction_event_code): string?`, `TransactionStatus (transaction_status): string?`, `TransactionAmount (transaction_amount): Money?`, `FeeAmount (fee_amount): Money?`, `TransactionInitiationDate`, `TransactionUpdatedDate`, `InvoiceId`, `CustomField`. Map note: executed transactions take up to 3 hours to appear; history covers the previous 3 years.

### 2.5 Key models (records pages; all `PayPalServerSdk.Models`, immutable, `init`-only, `!req` = C# `required`)

| Model | Fields (`CSharpName (wire_name): Type`) | Map page |
|---|---|---|
| `OrderRequest` | `Intent (intent): CheckoutPaymentIntent !req`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `Payer (payer): Payer?`, `ApplicationContext (application_context): OrderApplicationContext?` | records-1 |
| `PurchaseUnitRequest` | `Amount (amount): AmountWithBreakdown !req`, `ReferenceId (reference_id): string?`, `CustomId (custom_id): string?`, `InvoiceId (invoice_id): string?`, `Description`, `Items`, `Shipping` | records-2 |
| `AmountWithBreakdown` | `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?` | records-1 |
| `Money` | `CurrencyCode (currency_code): string !req`, `Value (value): string !req` — **both are strings**; format the eShop total with invariant culture, 2 decimals, and it must equal the order total to the cent | records-1 |
| `PaymentSource` | `Card (card): CardRequest?`, `Token (token): Token?`, `Paypal (paypal): PayPalWallet?`, … (15 one-of-style optional members; set exactly one) | records-2 |
| `CardRequest` | `Number (number): string?`, `Expiry (expiry): string?` (`"YYYY-MM"`), `SecurityCode (security_code): string?`, `Name (name): string?`, `BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?`, `Attributes (attributes): CardAttributes?`, `ExperienceContext (experience_context): CardExperienceContext?` | records-1 |
| `Address` | `CountryCode (country_code): string !req`, `AddressLine1`, `AddressLine2`, `AdminArea2` (city), `AdminArea1` (state), `PostalCode` — all `string?` except CountryCode | records-1 |
| `Order` / `OrderAuthorizeResponse` | `Id (id): string?`, `Status (status): OrderStatus?`, `Intent (intent): CheckoutPaymentIntent?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `PaymentSource (payment_source): PaymentSourceResponse?`, `Links (links): IReadOnlyList<LinkDescription>?` | records-1 |
| `PurchaseUnit` → `PaymentCollection` | `Payments (payments): PaymentCollection?` → `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`, `Captures (captures): IReadOnlyList<OrdersCapture>?`, `Refunds (refunds): IReadOnlyList<Refund>?` | records-2 |
| `AuthorizationWithAdditionalData` / `PaymentAuthorization` | `Id (id): string?`, `Status (status): AuthorizationStatus?`, `StatusDetails (status_details): AuthorizationStatusDetails?` (→ `Reason: AuthorizationIncompleteReason?`), `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`, `CreateTime`, `UpdateTime` | records-1/2 |
| `CapturedPayment` / `Refund` / `SellerReceivableBreakdown` / `SellerPayableBreakdown` | see §2.2 rows | records-1/2 |
| `Error` (Orders/Payments payload) | `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?` → `Issue (issue): string !req`, `Field`, `Value`, `Description` | records-1 |
| `Error1` (Vault payload) | same shape; `Details: IReadOnlyList<ErrorDetails1>?`, `Links: IReadOnlyList<ErrorLinkDescription>?` (`Rel` is **nullable** here — do not require it) | records-1 |
| `CardResponse` (on `Order.PaymentSource.Card`) | `LastDigits`, `Brand: CardBrand?`, `AuthenticationResult (authentication_result): AuthenticationResponse?` → `LiabilityShift: LiabilityShiftIndicator?`, `ThreeDSecure: ThreeDSecureAuthenticationResponse?` → `AuthenticationStatus: ParesStatus?`, `EnrollmentStatus: EnrollmentStatus?`; `Attributes.Vault` (`CardVaultResponse { Id, Status: VaultStatus?, Customer }`) when vault-on-success was requested | records-1 |

### 2.6 Enum values actually needed (`map/models/enums.md`; all `PayPalServerSdk.Models.Enums`, `StringEnum<T>` — use static members, e.g. `CheckoutPaymentIntent.Authorize`, never string literals in C#)

| Enum | Members (wire) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` — **no Expired member** |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, … (29 members; sandbox test card 4111111111111111 ⇒ `Visa`) |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` — vault-during-payment: `CardRequest.Attributes = new CardAttributes { Customer = new CardCustomerInformation { Id = … }, Vault = new VaultInstructionBase { StoreInVault = StoreInVaultInstruction.OnSuccess } }`; read result at `CardResponse.Attributes.Vault.Id` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` (only member) |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` (only member — see §2.4: do not use for vault cards) |
| `EnrollmentStatus` (3DS) | `Y (Y)`, `N (N)`, `U (U)`, `B (B)` |
| `ParesStatus` (3DS) | `Y (Y)`, `N (N)`, `U (U)`, `A (A)`, `C (C)`, `R (R)`, `D (D)`, `I (I)` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)` (default on `CardVerification.Method`), `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| `VaultCardVerificationMethod` | `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `ScaAlways (SCA_ALWAYS)` |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `PaymentInitiator` / `StoredPaymentSourcePaymentType` / `StoredPaymentSourceUsageType` | `Customer (CUSTOMER)`/`Merchant (MERCHANT)` · `OneTime`/`Recurring`/`Unscheduled` · `First`/`Subsequent`/`Derived` — for merchant-initiated charges on a vaulted card set `CardRequest.StoredCredential` accordingly |

**3DS / contingency detection (grounded shapes; live trigger behavior UNVERIFIED §5-3):** after create/authorize with a card, STOP and report (do not build an approval round-trip) when any of: `Order.Status == OrderStatus.PayerActionRequired`; a `Links` entry whose `Rel` is a payer-action/approval rel (rel values are plain strings — match defensively); or `PaymentSource.Card.AuthenticationResult` present with `ThreeDSecure.EnrollmentStatus == EnrollmentStatus.Y` and `AuthenticationStatus` not in {`Y`, `A`} / `LiabilityShift == LiabilityShiftIndicator.No`. Default verification is `SCA_WHEN_REQUIRED` — leave it, and treat a challenge as an operator-reportable outcome.

### 2.7 Client construction, auth, base-URL override (verified against SDK source where the map was silent)

```csharp
services.AddPayPalServerSdkClient(o =>   // ServiceCollectionExtensions.cs (sdk-map.md)
{
    o.Environment = ServerEnvironment.Sandbox;                    // PayPalServerSdk.Servers — the ONLY member
    o.Oauth2 = new OAuth2ClientCredentials                        // PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials
    {
        ClientId = cfg["PayPal:ClientId"]!,                       // both are `required init`
        ClientSecret = cfg["PayPal:ClientSecret"]!,
    };
    // Optional verbatim base-URL override covering EVERY call INCLUDING the OAuth token request:
    if (cfg["PayPal:BaseUrl"] is { Length: > 0 } baseUrl)
        o.Server.Default.Sandbox.BaseUrl = baseUrl;               // ServerOptions (root ns) → DefaultOptions.SandboxOptions.BaseUrl (PayPalServerSdk.Servers)
});
```

- **Base-URL knob (source-verified):** `PayPalServerSdkClientOptions.Server` (`ServerOptions`, `ServerOptions.cs`, root namespace) → `.Default` (`DefaultOptions`, `Servers/DefaultOptions.cs`) → `.Sandbox.BaseUrl` (default `https://api-m.sandbox.paypal.com`). The OAuth token call is built as `server.Default("/v1/oauth2/token")` inside `AuthSchemes` from the same `options.Server`, so this one override re-targets API calls **and** token fetch. There is no separate auth-URL knob.
- Token acquisition/caching/refresh is internal (`OAuth2Scheme` caches until expiry); `Oauth2TokenStrategy` is an optional override point — leave null.
- Exceptions (`sdk-map.md` error model): every operation is **throw-only** (no `…Result` variants). API errors throw `SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`) with `.Error`: Case A typed (`…Error : ApiError`, `TryGet…` accessors per §2.1–2.4 + inherited `TryGetRawError`), Case B `RawError` (`StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`). 39 of 40 ops are Case A; `SearchTransactions` is the one Case B.

## 3. Trap notes

> ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind `PayPalServerSdkClient` has specific lifetime requirements (factory-managed, long-lived) that `new HttpClient()` per request violates. **MUST load `dotnet-client-initialization`** before wiring DI.
> ⚠ Step 1 (auth) — credentials must be set before client construction / in the DI callback, from configuration not source; the scheme's token caching has its own rules. **MUST load `dotnet-authentication`**.
> ⚠ Steps 2–11 (every call) — many optional parameters have no C# default and mis-bind positionally; the must-pass-explicitly nullables above are the symptom. Call with named arguments. **MUST load `dotnet-calling-endpoints`**.
> ⚠ Steps 2–11 (models) — enums are `StringEnum<T>` (not C# enums), records are immutable with `required` members, and unmodeled JSON fields are silently dropped on deserialize (matters when reading `payments.authorizations` if PayPal adds fields). **MUST load `dotnet-models`**.
> ⚠ Step 12 (error boundary) — Case A vs Case B differs per operation (table above); `TryGetRawError` is not a catch-all on typed errors; and see the two mandatory `JsonException` hazard rows in §4. **MUST load `dotnet-error-handling`**.
> ⚠ Step 12 (resilience) — the SDK's retry/timeout options do **not** bound a whole call, are **not** the timeout on the `HttpClient` you register, and whether a failed write (capture/refund) can be re-sent by the retry layer is exactly the hazard the caller's `payPalRequestId` must cover. **MUST load `dotnet-configuration-resilience`** before tuning `RetryOptions`/`Timeout`.
> ⚠ Step 12 (tests) — the test seam is the `HttpClient` constructor argument, not mocking SDK types. **MUST load `dotnet-testing`**.

## 4. REQUIRED READING

Load **before implementation starts** (this sheet deliberately does not carry their contents):

- `dotnet-client-initialization` — Step 1 (client construction & DI lifetime).
- `dotnet-authentication` — Step 1 (credentials wiring, token strategy).
- `dotnet-calling-endpoints` — Steps 2–11 (named arguments, envelopes, async/ct).
- `dotnet-models` — Steps 2–11 (StringEnum, required members, wire names).
- `dotnet-error-handling` — Step 12 (the exception boundary). Mandatory regardless of trap count, and note both of these:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
- `dotnet-configuration-resilience` — Step 12 (retries, timeout semantics, base URL, manual pagination).
- `dotnet-testing` — Step 12 (faking the `HttpClient` seam).

## 5. Assumptions & Blockers

**Assumptions**
- eShopOnWeb layering: `ApplicationCore` declares a `IPayPalGateway`-style abstraction + DTOs; `Infrastructure` implements it with the SDK; `PublicApi` registers DI and exposes endpoints. Adjust to the repo's actual conventions at implementation time.
- Single-step `CreateOrder` with `PaymentSource.Card` is the primary card flow (the SDK doc makes `payPalRequestId` mandatory there — always supplied); the two-step `CreateOrder`→`AuthorizeOrder` path is documented as the fallback.
- Currency comes from config (`PayPal:Currency`); amounts are formatted as invariant-culture strings with 2 decimals into `Money.Value`, computed from the same order-total source eShop uses, so the cent-equality requirement holds by construction.
- Sandbox only: `ServerEnvironment` has exactly one member (`Sandbox`); production would need an SDK/environment change, not just config.
- Direct card fields via API carry PCI SAQ-D burden (noted on `CardRequest`'s own doc) — assumed acceptable for this sandbox reference integration; the setup-token/hosted alternative exists in §2.3.
- Refund idempotency: the caller-supplied key goes in `payPalRequestId` (→ `PayPal-Request-Id`) on `RefundCapturedPayment`; same parameter exists on create order, capture, void, reauthorize, and both vault creates. GETs take no idempotency key.

**UNVERIFIED (only live traffic can confirm — defensive directives included)**
1. Whether `prefer = "return=minimal"` (the SDK default) omits `SellerReceivableBreakdown`/`SellerPayableBreakdown` on capture/refund responses. Directive: pass `prefer: "return=representation"` on `CaptureAuthorizedPayment` and `RefundCapturedPayment`, and treat every breakdown member as nullable regardless (the model declares them `Money?`).
2. Whether `ListCustomerPaymentTokens`' `customer_id` accepts a merchant-supplied `MerchantCustomerId` or only the PayPal-assigned `Customer.Id`. Directive: persist `PaymentTokenResponse.Customer.Id` at vault time and list by that.
3. Whether the sandbox test card triggers an SCA/3DS contingency, and the exact `Links[].Rel` string PayPal sends for payer action. Directive: implement the §2.6 detection defensively (status + rel-string match + authentication result) and report to the operator rather than attempting an approval flow.
4. The specific `Error.Details[].Issue` strings returned on a 422 from `ReauthorizePayment` (the "cannot be renewed" signal). Directive: treat any 422 from reauthorize as not-renewable and surface `Name`/`Message`/`DebugId`/`Details` verbatim to the operator.

**Blockers** — none.
