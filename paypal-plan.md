# paypal-plan.md — eShopOnWeb × PayPal .NET SDK (`PayPalServerSdk`)

NuGet: `AsadAli.Checkout.Sdk` (install version-less: `dotnet add package AsadAli.Checkout.Sdk`).
Root namespace `PayPalServerSdk`. Sandbox target. All signatures below are generated code taken from the
SDK map (`map/operations/Orders.md`, `map/operations/Payments.md`, `map/operations/Vault.md`,
`map/operations/TransactionSearch.md`) and, where noted, from the SDK source files named in the row.

---

## 1. Scope & sequence

| # | Step | PayPal operations used |
|---|---|---|
| 1 | Install package; bind config keys `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Currency`, `PayPal:Environment` (sandbox/live), `PayPal:BaseUrl` (optional override) | — |
| 2 | Register the SDK client in DI (HttpClient + options; base-URL override applies to **every** call incl. the OAuth token request — verified in source `AuthSchemes.cs`) | — |
| 3 | Local persistence for PayPal ids/statuses (wire names in §2.6) — schema is the implementer's decision | — |
| 4 | Vault saved cards — `POST /api/payment-methods`, `GET …`, `DELETE /api/payment-methods/{id}` | `Vault.CreatePaymentToken`, `Vault.GetPaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.DeletePaymentToken` |
| 5 | Pay (authorize, hold funds) — `POST /api/orders/{orderId}/pay`; idempotent | `Orders.CreateOrder` (intent AUTHORIZE) → `Orders.AuthorizeOrder` |
| 6 | Fulfil (capture; reauthorize if stale) — `POST /api/orders/{orderId}/fulfil` | `Payments.GetAuthorizedPayment`, `Payments.ReauthorizePayment`, `Payments.CaptureAuthorizedPayment` |
| 7 | Cancel (void auth) — `POST /api/orders/{orderId}/cancel` | `Payments.VoidPayment` |
| 8 | Refund (full/partial, multi-partial, idempotent) — `POST /api/orders/{orderId}/refunds` | `Payments.RefundCapturedPayment`, `Payments.GetRefund` |
| 9 | Reconciliation over a date range, all pages — `GET /api/reconciliation?from=&to=` | `TransactionSearch.SearchTransactions` |
| 10 | Error boundary + JsonException ladder around every PayPal call | — |

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

All operations are on the client `PayPalServerSdk.PayPalServerSdkClient`; controllers are
properties `Orders`, `Payments`, `Vault`, `TransactionSearch` (namespace `PayPalServerSdk.Api`).
Every operation is **throw-only** (no `…Result` variants). 39 of 40 ops are Case A (typed
`SdkException<{Operation}Error>`); `SearchTransactions` is the single **Case B** op
(`SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>`).

### 2.1 Client construction, auth, base URL (source-verified)

```csharp
using PayPalServerSdk;                                              // client, options, ServerOptions
using PayPalServerSdk.Servers;                                      // ServerEnvironment, DefaultOptions
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials; // OAuth2ClientCredentials

var options = new PayPalServerSdkClientOptions
{
    Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox,
    Server = new PayPalServerSdk.ServerOptions
    {
        Default = new PayPalServerSdk.Servers.DefaultOptions
        {
            Sandbox = new PayPalServerSdk.Servers.DefaultOptions.SandboxOptions
            {
                BaseUrl = baseUrlFromConfig   // "https://api-m.sandbox.paypal.com" by default
            }
        }
    },
    Oauth2 = new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials
    {
        ClientId = clientIdFromConfig,
        ClientSecret = clientSecretFromConfig
    },
    Retry = PayPalServerSdk.Core.Configuration.RetryOptions.Default()
};
var client = new PayPalServerSdk.PayPalServerSdkClient(httpClient, options);
// DI alternative: services.AddPayPalServerSdkClient(o => { …set the same properties on o… });
```

| Fact | Value | Source |
|---|---|---|
| Credentials scheme | client-credentials OAuth2: `Oauth2 = OAuth2ClientCredentials { ClientId, ClientSecret (required, init-only), Scope? }`; set **before** the client is used. Token request: `POST {BaseUrl}/v1/oauth2/token`, Basic-auth header | sdk-map *Servers & auth*; source `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs` |
| Base-URL override incl. token endpoint | `PayPalServerSdkClientOptions.Server.Default.Sandbox.BaseUrl` — resolved for **all** API paths **and** the token URL (`AuthSchemes` builds the token URL as `server.Default("/v1/oauth2/token")` from the same options) | source `AuthSchemes.cs`, `Servers/DefaultOptions.cs`, `ServerOptions.cs`, `Server.cs` |
| Environment | `ServerEnvironment` has **only** the member `Sandbox`; a "live" config value has no SDK member (see §5) | sdk-map *Servers & auth*; source `Servers/ServerEnvironment.cs` |
| HttpClient | constructor takes `System.Net.Http.HttpClient` — long-lived, factory-managed (companion skill governs) | sdk-map *Getting a client* |
| Per-call `RequestOptions` | `PayPalServerSdk.Core.RequestOptions { LogLevel? }` only — **cannot carry custom headers** | source `Core/RequestOptions.cs` |
| Retry/timeout | `RetryOptions` members listed in sdk-map *client-options*; semantics (what `Timeout` bounds, which POSTs retry) → companion skill | sdk-map; **MUST load `dotnet-configuration-resilience`** |

### 2.2 Operations — signatures, request/response, errors

Params marked **⇐** are nullable **without default** — they must be passed explicitly (pass `null`).
Named arguments use the literal names shown. Unless stated, request/response/error payload types are in
`PayPalServerSdk.Models`; error classes in `PayPalServerSdk.Errors`.

#### Orders controller — `client.Orders`

**`CreateOrder`** — `POST /v2/checkout/orders` — returns `Order`
Signature: `CreateOrder(string? payPalMockResponse ⇐, string? payPalRequestId ⇐, string? payPalPartnerAttributionId ⇐, string? payPalClientMetadataId ⇐, string? payPalAuthAssertion ⇐, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
Errors: `SdkException<PayPalServerSdk.Errors.CreateOrderError>` — `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out RawError)`

**`AuthorizeOrder`** — `POST /v2/checkout/orders/{id}/authorize` — returns `OrderAuthorizeResponse`
Signature: `AuthorizeOrder(string id, string? payPalMockResponse ⇐, string? payPalRequestId ⇐, string? payPalClientMetadataId ⇐, string? payPalAuthAssertion ⇐, OrderAuthorizeRequest? body ⇐, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
Errors: `SdkException<PayPalServerSdk.Errors.AuthorizeOrderError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)`
Map Note (abridged): to authorize, the buyer must have approved the order **or a valid `payment_source` must be provided in the request**.

**`GetOrder`** — `GET /v2/checkout/orders/{id}` — returns `Order`
Signature: `GetOrder(string id, string? fields ⇐, string? payPalMockResponse ⇐, string? payPalAuthAssertion ⇐, RequestOptions? requestOptions = null, CancellationToken ct = default)`
Errors: `SdkException<PayPalServerSdk.Errors.GetOrderError>` — `TryGetError(out Error)` [401, 404] · `TryGetRawError(out RawError)`. **Not-found** detection for orders: 404 here.

#### Payments controller — `client.Payments`

**`CaptureAuthorizedPayment`** — `POST /v2/payments/authorizations/{authorization_id}/capture` — returns `CapturedPayment`
Signature: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse ⇐, string? payPalRequestId ⇐, string? payPalAuthAssertion ⇐, CaptureRequest? body ⇐, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
Errors: `SdkException<PayPalServerSdk.Errors.CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`

**`GetAuthorizedPayment`** — `GET /v2/payments/authorizations/{authorization_id}` — returns `PaymentAuthorization`
Signature: `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse ⇐, string? payPalAuthAssertion ⇐, RequestOptions? requestOptions = null, CancellationToken ct = default)`
Errors: `SdkException<PayPalServerSdk.Errors.GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`. **Not-found / unknown auth id**: 404.

**`ReauthorizePayment`** — `POST /v2/payments/authorizations/{authorization_id}/reauthorize` — returns `PaymentAuthorization`
Signature: `ReauthorizePayment(string authorizationId, string? payPalRequestId ⇐, string? payPalAuthAssertion ⇐, ReauthorizeRequest? body ⇐, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
Errors: `SdkException<PayPalServerSdk.Errors.ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`
Map Note (abridged): reauthorize within the 29-day authorization window after the 3-day honor period; supports only the `amount` parameter; up to 115% of the original amount (US), not exceeding +$75.

**`VoidPayment`** — `POST /v2/payments/authorizations/{authorization_id}/void` — returns `PaymentAuthorization`
Signature: `VoidPayment(string authorizationId, string? payPalMockResponse ⇐, string? payPalAuthAssertion ⇐, string? payPalRequestId ⇐, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
Errors: `SdkException<PayPalServerSdk.Errors.VoidPaymentError>` — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`
Map Note: you cannot void an authorization that has been fully captured.

**`RefundCapturedPayment`** — `POST /v2/payments/captures/{capture_id}/refund` — returns `Refund`
Signature: `RefundCapturedPayment(string captureId, string? payPalMockResponse ⇐, string? payPalRequestId ⇐, string? payPalAuthAssertion ⇐, RefundRequest? body ⇐, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
Errors: `SdkException<PayPalServerSdk.Errors.RefundCapturedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`
Map Note: **full refund = `body: null` (empty payload)**; partial refund = body with `amount`.

**`GetRefund`** — `GET /v2/payments/refunds/{refund_id}` — returns `Refund`
Signature: `GetRefund(string refundId, string? payPalMockResponse ⇐, string? payPalAuthAssertion ⇐, RequestOptions? requestOptions = null, CancellationToken ct = default)`
Errors: `SdkException<PayPalServerSdk.Errors.GetRefundError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`

#### Vault controller — `client.Vault`

**`CreatePaymentToken`** — `POST /v3/vault/payment-tokens` — returns `PaymentTokenResponse`
Signature: `CreatePaymentToken(string? payPalRequestId ⇐, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
Errors: `SdkException<PayPalServerSdk.Errors.CreatePaymentTokenError>` — `TryGetError1(out PayPalServerSdk.Models.Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)`

**`GetPaymentToken`** — `GET /v3/vault/payment-tokens/{id}` — returns `PaymentTokenResponse`
Signature: `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
Errors: `SdkException<PayPalServerSdk.Errors.GetPaymentTokenError>` — `TryGetError1(out Error1)` [403, 404, 422, 500] · `TryGetRawError(out RawError)`. Deleted/unknown token ⇒ 404 ⇒ treat card as unusable.

**`DeletePaymentToken`** — `DELETE /v3/vault/payment-tokens/{id}` — returns `void` (Task)
Signature: `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
Errors: `SdkException<PayPalServerSdk.Errors.DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError(out RawError)` (note: **404 is not a listed typed status** — an unknown id falls to `TryGetRawError`).

**`ListCustomerPaymentTokens`** — `GET /v3/vault/payment-tokens` — returns `CustomerVaultPaymentTokensResponse`
Signature: `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
Query wire: `customer_id ← customerId`, `page_size`, `page`, `total_required`
Errors: `SdkException<PayPalServerSdk.Errors.ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError(out RawError)`

(`SetupToken` ops `CreateSetupToken`/`GetSetupToken` exist on the same controller for the payer-approval vault flow; the direct-card vault flow in scope uses `CreatePaymentToken` only. Both signatures are on `map/operations/Vault.md` if the flow changes.)

#### TransactionSearch controller — `client.TransactionSearch`

**`SearchTransactions`** — `GET /v1/reporting/transactions` — returns `SearchResponse` — **the SDK's only Case B op**
Signature (8 nullable-without-default params must be passed explicitly; pass `null` to skip a filter):
`SearchTransactions(string startDate, string endDate, string? transactionId ⇐, string? transactionType ⇐, string? transactionStatus ⇐, string? transactionAmount ⇐, string? transactionCurrency ⇐, string? paymentInstrumentType ⇐, string? storeId ⇐, string? terminalId ⇐, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
Query wire: `start_date`, `end_date`, `transaction_id`, `transaction_type`, `transaction_status`, `transaction_amount`, `transaction_currency`, `payment_instrument_type`, `store_id`, `terminal_id`, `fields`, `balance_affecting_records_only`, `page_size`, `page`
Error: `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — **Case B** — read `ex.Error.StatusCode` and `ex.Error.ReadAsString()` / `ReadAsJson<T>()` (typed `TryGetDefaultError` exists only on `SearchBalances`, not here).
Map Notes: executed transactions can take **up to 3 hours** to appear; the call covers the previous three years; specifying optional filters empties `ending_balance`.

### 2.3 Request models (fields `CSharpName (wire_name): type` — `!req` = C# `required`; namespace `PayPalServerSdk.Models` unless noted; enums in `PayPalServerSdk.Models.Enums`)

**`OrderRequest`** — `Intent (intent): CheckoutPaymentIntent !req` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` · `Payer (payer): Payer?` · `PaymentSource (payment_source): PaymentSource?` · `ApplicationContext (application_context): OrderApplicationContext?`

**`PurchaseUnitRequest`** — `Amount (amount): AmountWithBreakdown !req` · optional: `ReferenceId`, `InvoiceId (invoice_id)`, `CustomId (custom_id)`, `SoftDescriptor (soft_descriptor)`, `Description`, `Items`, `Shipping`, …
⚠ `Amount` uses **`AmountWithBreakdown`** (`CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown?`) — **not** `Money`.

**`PaymentSource`** — `Card (card): CardRequest?` · `Token (token): Token?` (+ other wallet variants not in scope)

**`CardRequest`** (one-off raw card) — `Name (name)`, `Number (number)`, `Expiry (expiry)`, `SecurityCode (security_code)`, `BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?`, `SingleUseToken`, `StoredCredential`, `Attributes (attributes): CardAttributes?`
- Raw card: set `Name`, `Number`, `Expiry`, `SecurityCode`, `BillingAddress` (sandbox test card `4111111111111111`).
- **Saved-card payment: set `CardRequest.VaultId` to the v3 vault token id.** Do **not** use `PaymentSource.Token` for a v3 payment token: `Token` requires `TokenType` whose only member is `BillingAgreement (BILLING_AGREEMENT)` (billing-agreement id, not a vault token).

**`Address`** — `CountryCode (country_code): string !req` + optional `AddressLine1 (address_line_1)`, `AddressLine2`, `AdminArea2 (admin_area_2)` (city), `AdminArea1 (admin_area_1)` (state), `PostalCode (postal_code)`.

**`OrderAuthorizeRequest`** — `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` — which has `Card (card): CardRequest?` (same `CardRequest` as above).

**`CaptureRequest`** — `Amount (amount): Money?` · `InvoiceId (invoice_id): string?` · `FinalCapture (final_capture): bool? = false` · `NoteToPayer`, `SoftDescriptor (soft_descriptor): string?`, `PaymentInstruction`
- Capture must equal the order total to the cent: `Money { CurrencyCode = config currency, Value = "invariant 2-decimal string" }` and `FinalCapture = true`.

**`ReauthorizeRequest`** — `Amount (amount): Money?` (only supported parameter).

**`RefundRequest`** — `Amount (amount): Money?` (omit = full refund with `body: null`) · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `NoteToPayer (note_to_payer): string?` · `PaymentInstruction`

**`PaymentTokenRequest`** — `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` · `Customer (customer): Customer?`
**`PaymentTokenRequestPaymentSource`** — `Card (card): PaymentTokenRequestCard?` · `Token`
**`PaymentTokenRequestCard`** — `Name`, `Number`, `Expiry`, `SecurityCode`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`
**`Customer`** — `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?` — set this at vault time so the token can be listed back (see 2.4).

### 2.4 Response accessors (what the integration reads)

**`Order` / `OrderAuthorizeResponse`** (top-level, not wrapped) — `Id (id): string?`, `Status (status): OrderStatus?`, `Intent (intent): CheckoutPaymentIntent?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `PaymentSource (payment_source): PaymentSourceResponse?`
- Authorization ids live at `PurchaseUnits[i].Payments (payments): PaymentCollection?` → `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → `.Id (id)`, `.Status (status): AuthorizationStatus?`, `.StatusDetails.StatusDetails (status_details).Reason`, `.Amount (amount): Money?`, `.ExpirationTime (expiration_time): string?`, `.ProcessorResponse (processor_response): ProcessorResponse?` (`.ResponseCode (response_code): ProcessorResponseCode?` — decline codes live here).
- If `prefer` default `"return=minimal"` yields a body without the payments collection, call **`GetOrder`** and re-read — treat either path as best-effort extraction (`UNVERIFIED`: whether the minimal body omits it is live-traffic territory).

**`PaymentAuthorization`** (returns of `GetAuthorizedPayment` / `ReauthorizePayment` / `VoidPayment`) — `Id`, `Status (status): AuthorizationStatus?`, `StatusDetails (status_details): AuthorizationStatusDetails?` (`.Reason (reason): AuthorizationIncompleteReason?`), `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`, `InvoiceId`, `CustomId`, `CreateTime`, `UpdateTime`.

**`CapturedPayment`** (top-level return of `CaptureAuthorizedPayment`; also `GetCapturedPayment`) — `Id (id): string?`, `Status (status): CaptureStatus?`, `StatusDetails (status_details): CaptureStatusDetails?` (`.Reason (reason): CaptureIncompleteReason?`), `Amount (amount): Money?`, `InvoiceId`, `CustomId`, `FinalCapture (final_capture): bool?`, `ProcessorResponse (processor_response): ProcessorResponse?`, and the fee/net breakdown:
**`SellerReceivableBreakdown`** at `SellerReceivableBreakdown (seller_receivable_breakdown)` — `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?`, `PaypalFeeInReceivableCurrency`, `NetAmount (net_amount): Money?`, `ReceivableAmount`, `ExchangeRate`, `PlatformFees`.
⚠ Map: the breakdown "is not available for transactions that are in pending state" — persist fee/net best-effort, fall back to `GetCapturedPayment` later.

**`Refund`** (top-level return of `RefundCapturedPayment`; also `GetRefund`) — `Id (id): string?`, `Status (status): RefundStatus?`, `StatusDetails (status_details): RefundStatusDetails?` (`.Reason (reason): RefundIncompleteReason?` — single member `Echeck`), `Amount (amount): Money?`, `InvoiceId`, `CustomId`, `NoteToPayer`, and **`SellerPayableBreakdown (seller_payable_breakdown)`** — `GrossAmount (gross_amount): Money?`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?`, `TotalRefundedAmount (total_refunded_amount): Money?`.

**`PaymentTokenResponse`** (return of `CreatePaymentToken` / `GetPaymentToken`) — `Id (id): string?`, `Customer (customer): CustomerResponse?` (`Id`, `MerchantCustomerId`), `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `Card (card): CardPaymentTokenEntity?` with `Brand (brand): CardBrand?`, `LastDigits (last_digits): string?`, `Expiry (expiry): string?`, `Name`, `BillingAddress`, `Type (type): CardType?`, `BinDetails`. **Persist only `Id` + brand/last4/expiry — never the PAN/CVC.**

**`CustomerVaultPaymentTokensResponse`** — `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `Customer (customer): VaultResponseCustomer?`.

**`SearchResponse`** — `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `AccountNumber`, `StartDate`, `EndDate`, `LastRefreshedDatetime`.
Per transaction: `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?` with `TransactionId (transaction_id): string?`, `TransactionEventCode (transaction_event_code): string?` (type classifier — plain string, see below), `TransactionStatus (transaction_status): string?` (plain string, **not** an enum), `TransactionAmount (transaction_amount): Money?`, `FeeAmount (fee_amount): Money?`, `TransactionInitiationDate (transaction_initiation_date): string?`, `TransactionUpdatedDate`, `InvoiceId (invoice_id): string?`, `PaypalReferenceId (paypal_reference_id): string?`, `PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?` (`Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)`), `PaymentMethodType`, `InstrumentType`.
⚠ Date-range format for `startDate`/`endDate` is `string` in the map — pass ISO-8601 date-time (`UNVERIFIED` which exact formats the endpoint accepts; extract/match defensively).
⚠ `TransactionEventCode`'s value catalogue is not in the map — reconcile by best-effort match on event code + amount sign, and flag anything unmatched as a mismatch rather than guessing (`UNVERIFIED`).

### 2.5 Enum value tables (all `StringEnum<T>` in `PayPalServerSdk.Models.Enums`; **not** C# enums — use the static members shown, e.g. `CheckoutPaymentIntent.Authorize`)

| Enum | Members (C# name (wire)) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)` · `Authorize (AUTHORIZE)` ← use for hold-funds flow |
| `AuthorizationStatus` | `Created (CREATED)` · `Captured (CAPTURED)` · `Denied (DENIED)` · `PartiallyCaptured (PARTIALLY_CAPTURED)` · `Voided (VOIDED)` · `Pending (PENDING)` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)` · `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)` · `Declined (DECLINED)` · `PartiallyRefunded (PARTIALLY_REFUNDED)` · `Pending (PENDING)` · `Refunded (REFUNDED)` · `Failed (FAILED)` |
| `CaptureIncompleteReason` | `BuyerComplaint`, `Chargeback`, `Echeck`, `InternationalWithdrawal`, `Other`, `PendingReview`, `ReceivingPreferenceMandatesManualAction`, `Refunded`, `TransactionApprovedAwaitingFunding`, `Unilateral`, `VerificationRequired`, `DeclinedByRiskFraudFilters` (all wire `UPPER_SNAKE`) |
| `RefundStatus` | `Cancelled (CANCELLED)` · `Failed (FAILED)` · `Pending (PENDING)` · `Completed (COMPLETED)` |
| `OrderStatus` | `Created (CREATED)` · `Saved (SAVED)` · `Approved (APPROVED)` · `Voided (VOIDED)` · `Completed (COMPLETED)` · `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `CardBrand` | `Visa (VISA)` · `Mastercard (MASTERCARD)` · `Discover (DISCOVER)` · `Amex (AMEX)` · … · `Unknown (UNKNOWN)` (full list: `map/models/enums.md`) |
| `PaymentTokenStatus` | `Created (CREATED)` · `PayerActionRequired (PAYER_ACTION_REQUIRED)` · `Approved (APPROVED)` · `Vaulted (VAULTED)` · `Tokenized (TOKENIZED)` |
| `VaultStatus` | `Vaulted (VAULTED)` · `Created (CREATED)` · `Approved (APPROVED)` |

### 2.6 Wire names to persist (local payment record)

| Concept | Wire path |
|---|---|
| PayPal order id | `id` (order response) |
| Authorization | `purchase_units[].payments.authorizations[].id`, `.status`, `.expiration_time` (or `id`/`status`/`expiration_time` on `PaymentAuthorization`) |
| Capture | `id`, `status`, `amount.currency_code`, `amount.value` |
| Capture fee/net | `seller_receivable_breakdown.paypal_fee.currency_code`/`.value`, `seller_receivable_breakdown.net_amount` |
| Refund | `id`, `status`, `amount`, `seller_payable_breakdown.gross_amount` / `.paypal_fee` / `.net_amount` / `.total_refunded_amount` |
| Vault token | `id` (+ `customer.id`, `customer.merchant_customer_id`), `payment_source.card.brand`, `.last_digits`, `.expiry` |
| Reconciliation row | `transaction_info.transaction_id`, `.transaction_event_code`, `.transaction_status`, `.transaction_amount`, `.fee_amount`, `.transaction_initiation_date`, `.invoice_id`, `.paypal_reference_id` |

`Money.value` is a **string** on the wire: format every amount with invariant culture and exactly 2 decimals ("10.00", never "10" or "10,00"); when reading, parse invariantly. No compiler catches a culture slip.

### 2.7 Error handling — classification & idempotency

**Mechanics** (map facts; the worked Case A/B ladder → **MUST load `dotnet-error-handling`**):

| Condition | How to detect (map-grounded) |
|---|---|
| Declined card | `AuthorizeOrder`/`CaptureAuthorizedPayment` throw typed Case A at 422; read `Error.Details (details): IReadOnlyList<ErrorDetails>?` → `ErrorDetails.Issue (issue): string !req`, `ErrorDetails.Field`, `ErrorDetails.Value`; also `ProcessorResponse.ResponseCode` on authorization data. The specific decline issue strings (`INSTRUMENT_DECLINED` etc.) are wire strings **not in the map** — match best-effort, fall back to a generic "payment declined" message (`UNVERIFIED`). |
| Expired / stale authorization | Not a decline: check locally-stored `expiration_time` **before** capturing, and `GetAuthorizedPayment` → `Status`. Capture on an expired auth surfaces as a typed 422 whose `Details[].Issue` is a wire string; treat unmatchable issue text as "authorization no longer valid" (`UNVERIFIED` for the exact string). Renewal path: `ReauthorizePayment`; if it fails (typed 422/404 or raw), return an actionable error — do not silently mark the order failed. |
| Not found | 404 on the operation's typed error (`GetOrder` [401,404], `GetAuthorizedPayment` [401,403,404], `GetRefund` [401,403,404], `GetPaymentToken` [403,404,422,500]). `VoidPayment` [401,403,404,409,422] / capture [400..404,409,422] — 409 = state conflict (e.g. void after capture). |
| Other statuses | `TryGetRawError(out RawError)` fallback on every Case A op → `RawError.StatusCode` + `ReadAsString()`. Case B (`SearchTransactions`) **only** `RawError`. |
| HTTP status on Case A | The typed `Error` record carries `Name/Message/DebugId/Details/Links` but **no status code**; how the status reaches the handler is Case A/B mechanics governed by `dotnet-error-handling` — do not reconstruct status from exception text. |

**Idempotency:**
- The SDK maps the `payPalRequestId` parameter to the **`PayPal-Request-Id` header** verbatim (source `Api/Payments.cs`, `Api/Orders.cs`, `Api/Vault.cs`). It is the idempotency mechanism for `AuthorizeOrder`, `CaptureOrder`, `CaptureAuthorizedPayment`, `ReauthorizePayment`, `RefundCapturedPayment`, `VoidPayment`, `CreatePaymentToken`.
- The SDK **also** sends `Idempotency-Key: Guid.NewGuid()` on every write call (source `Api/Payments.cs` line 62) — **random per call, provides no dedup; never rely on it.**
- `RequestOptions` cannot add headers (see 2.1). Therefore: generate a **deterministic** key per logical operation (e.g. derived from `{orderId}` + action) and pass it as `payPalRequestId`, persisting it before the first send so a retry reuses the same value. Whether PayPal's dedup semantics cover every retry window is live-traffic territory (`UNVERIFIED`) — also keep the app-level guard: check the local payment record's state before issuing authorize/capture/refund.
- Refund double-spend: caller-supplied idempotency key → same `payPalRequestId` for the same caller key; store issued refund ids per capture and check cumulative refunded vs captured locally (`YOUR CALL — not in the map`: the exact reuse/duplicate-response contract).

### 2.8 Error-payload shapes (all `PayPalServerSdk.Models`)

| Type | Fields |
|---|---|
| `Error` (Orders/Payments Case A) | `Name !req`, `Message !req`, `DebugId !req`, `Details: IReadOnlyList<ErrorDetails>?` (`ErrorDetails`: `Field?`, `Value?`, `Location? = "body"`, `Issue !req`, `Description?`), `Links` |
| `Error1` (Vault Case A) | same shape; `Details: IReadOnlyList<ErrorDetails1>?`, `Links: IReadOnlyList<ErrorLinkDescription>?` |
| `RawError` (`PayPalServerSdk.Core.ErrorResponse`) | `StatusCode: HttpStatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()` |

> a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
> `JsonException` from deserialization, **not** as an `SdkException` — so an
> SDK-exception-only catch ladder lets it escape the integration boundary;
>
> a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
> throws `JsonException` *while the error object is being constructed*, so the `JsonException`
> **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
> maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
> and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 3. Trap notes (hazard + consequence only — load the named skill before wiring that step)

- ⚠ Step 1–2 (client registration & config) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; a transport failure retries **on POST too**, so a non-idempotent write can execute more than once. **MUST load `dotnet-configuration-resilience`** before tuning the client.
- ⚠ Step 2 (DI/HttpClient) — the `HttpClient` handed to `PayPalServerSdkClient` must be long-lived and factory-managed; a per-request client rebuilds the token/auth state each call. **MUST load `dotnet-client-initialization`**.
- ⚠ Step 2 (credentials) — `Oauth2` credentials must be set before the client is used, and secrets belong in configuration, not code; a 401 at runtime is config-shaped first, call-shaped second. **MUST load `dotnet-authentication`**.
- ⚠ Step 4–9 (every call site) — nullable no-default params must be passed explicitly and in order; call with **named arguments** (the `ct:` rule above) — positional calls mis-bind on `SearchTransactions` and the payments/vault writes. **MUST load `dotnet-calling-endpoints`** before the first SDK call.
- ⚠ Step 4–9 (payload construction) — enums are `StringEnum<T>` built from static members (never `new`, never wire strings in code), `required` members must appear in the initializer, and `Money`/`AmountWithBreakdown` values are strings with invariant 2-decimal formatting. **MUST load `dotnet-models`**.
- ⚠ Step 10 (boundary) — the two `JsonException` directions in §2.8 need **opposite** handling; a ladder that only catches `SdkException` leaks both. **MUST load `dotnet-error-handling`** before writing any try/catch around an SDK call.
- ⚠ Step 9 (reconciliation) — pagination is manual (`page`/`page_size`/`TotalPages`): loop pages until exhausted or the whole range is not covered; transactions can lag up to 3 hours, so "absent" is not yet "mismatch" inside that window. **MUST load `dotnet-configuration-resilience`** (pagination section) and `dotnet-calling-endpoints`.
- ⚠ Tests — the `HttpClient` constructor argument is the seam to fake; do not stub SDK types directly. **MUST load `dotnet-testing`** before writing integration tests.

---

## 4. REQUIRED READING (load **before implementation starts** — this sheet deliberately carries none of their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 2 — DI registration, HttpClient lifetime, factory pattern |
| `dotnet-authentication` | Step 2 — credential wiring, 401/403 diagnosis |
| `dotnet-calling-endpoints` | Steps 4–9 — named args, must-pass params, envelope reading |
| `dotnet-models` | Steps 4–9 — `StringEnum<T>`, required members, wire names, unions |
| `dotnet-error-handling` | Step 10 — Case A/B ladder, status reading, the two `JsonException` rows |
| `dotnet-configuration-resilience` | Steps 2, 9 — retries, timeouts, base URL, pagination |
| `dotnet-testing` | tests — faking seam, error-path coverage |

---

## 5. Assumptions & Blockers

1. **`PayPal:Environment = live` has no SDK member.** `ServerEnvironment` exposes only `Sandbox` and throws on any other value (source `Servers/ServerEnvironment.cs`). Assumption recorded: the config key is parsed, but a `live` value can only be routed by keeping `ServerEnvironment.Sandbox` and overriding `Server.Default.Sandbox.BaseUrl` to the live host; whether PayPal accepts that route end-to-end (incl. `/v1/oauth2/token`) is `UNVERIFIED`. In-scope target is sandbox, so this does not block.
2. **Base-URL override verified from source only, not live.** `AuthSchemes.cs` derives the token URL from the same `ServerOptions` as API calls; the override applying "verbatim" to the token request is a source fact, not a live-verified one (`UNVERIFIED` against real traffic).
3. **Decline/expired issue-string catalogue is not in the map.** `ErrorDetails.Issue` is a wire string; the map's enums only cover PENDING reasons. Declined-vs-expired-vs-not-found classification above is defensive (match best-effort, fall back to generic actionable message) and `UNVERIFIED`.
4. **`TransactionEventCode` value catalogue is not in the map** — reconciliation's sale/refund classification is best-effort with an explicit "unmatched ⇒ flagged" fallback (`UNVERIFIED`).
5. **`prefer`/response completeness (`UNVERIFIED`)** — whether `AuthorizeOrder`'s default `"return=minimal"` response always includes `purchase_units[].payments` is live-traffic territory; the plan's fallback is `GetOrder`, and `prefer` is forwarded verbatim as the `Prefer` header (source).
6. **Vault list matching (`UNVERIFIED`)** — whether `ListCustomerPaymentTokens`' `customer_id` matches `Customer.Id` or `Customer.MerchantCustomerId` from the create call is not in the map; persist the response's `Customer.Id` at vault time and pass that value; keep the app's own token table authoritative for `GET /api/payment-methods`.
7. **Card `expiry` format is `string` in the map** — pass caller input validated by the app (`YOUR CALL — not in the map`); `UNVERIFIED` what the endpoint accepts beyond it.
8. **App-side decisions are out of scope** (`YOUR CALL — not in the map`): local payment-record schema, endpoint authz beyond the brief, idempotency-key generation format, customer-identity mapping, and the reconciliation "expected transactions" source.
9. **PayPal-side effect of `DeletePaymentToken`** (deleted token thereafter unusable) is the API's contract per the map's operation Notes (delete by id) and `GetPaymentToken`'s 404 case; behavior after deletion is otherwise live territory (`UNVERIFIED`).
