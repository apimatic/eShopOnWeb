# PayPal Payment Integration — Implementation Plan

## 1. Scope & Sequence

| Step | API endpoint | SDK operations used |
|---|---|---|
| 0 | Install + DI | `dotnet add package AsadAli.Checkout.Sdk` · `services.AddPayPalServerSdkClient(...)` |
| 1 | `POST /api/orders` | No PayPal call; create local Order record (status = AwaitingPayment) |
| 2 | `POST /api/orders/{id}/pay` | `client.Orders.CreateOrder` → `client.Orders.AuthorizeOrder` |
| 3 | `POST /api/orders/{id}/fulfil` | `client.Payments.GetAuthorizedPayment` (staleness check) → `client.Payments.ReauthorizePayment` (if stale, days 4-29) → `client.Payments.CaptureAuthorizedPayment` |
| 4 | `POST /api/orders/{id}/cancel` | `client.Payments.VoidPayment` |
| 5 | `POST /api/orders/{id}/refunds` | `client.Payments.RefundCapturedPayment` |
| 6 | `GET /api/my-orders` | Query local DB only; return stored payment state |
| 7 | `GET /api/reconciliation?from=&to=` | `client.TransactionSearch.SearchTransactions` (paginated loop page=1..TotalPages) |
| 8 | `POST /api/payment-methods` | `client.Vault.CreateSetupToken` → `client.Vault.CreatePaymentToken` |
| 9 | `GET /api/payment-methods` | `client.Vault.ListCustomerPaymentTokens` |
| 10 | `DELETE /api/payment-methods/{id}` | `client.Vault.DeletePaymentToken` |
| 11 | Error boundary | See REQUIRED READING — `dotnet-error-handling` |

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

### 2.1 Namespaces required

```csharp
using PayPalServerSdk;                    // PayPalServerSdkClient, PayPalServerSdkClientOptions
using PayPalServerSdk.Models;             // all request/response records
using PayPalServerSdk.Models.Enums;       // CheckoutPaymentIntent, AuthorizationStatus, CaptureStatus, …
using PayPalServerSdk.Errors;             // CreateOrderError, AuthorizeOrderError, …
using PayPalServerSdk.Servers;            // ServerEnvironment
```

### 2.2 Client construction & auth

| Item | Value | Source |
|---|---|---|
| Client class | `PayPalServerSdkClient` | `PayPalServerSdkClient.cs` |
| Options class | `PayPalServerSdkClientOptions` | `PayPalServerSdkClientOptions.cs` |
| DI helper | `services.AddPayPalServerSdkClient(o => { … })` | `ServiceCollectionExtensions.cs` |
| Auth credential property | `options.Oauth2 = new OAuth2ClientCredentials { … }` | `PayPalServerSdkClientOptions.cs` |
| Exact `OAuth2ClientCredentials` field names | **load `dotnet-authentication`** (fields are in `Core/`; map does not list them) | — |
| Sandbox environment | `options.Environment = ServerEnvironment.Sandbox` | `Servers/ServerEnvironment.cs` |
| Live environment | `ServerEnvironment` exposes **only** `Sandbox` in this SDK version; for live, override base URL via `options.Server.Default.Sandbox.BaseUrl` (see row below) | `ServerOptions.cs`, `Servers/DefaultOptions.cs` |
| BaseUrl override | **`options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"`** — `ServerOptions.Default` is `DefaultOptions`; `DefaultOptions.Sandbox` is `SandboxOptions`; `SandboxOptions.BaseUrl` is `string` (default `"https://api-m.sandbox.paypal.com"`). This is the only URL slot; set it to the live URL when `PayPal:Environment == "live"`. Load `dotnet-configuration-resilience` for retry/timeout wiring. | `ServerOptions.cs`, `Servers/DefaultOptions.cs` |
| Config keys | `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, `PayPal:BaseUrl` | brief |

**`prefer` parameter — critical:** Every authorize/capture operation defaults to `prefer = "return=minimal"`. To receive the full response body (authorization ID, seller breakdown, etc.), callers **must pass `prefer: "return=representation"`**. With minimal, the authorization ID and `SellerReceivableBreakdown` are absent from the response body.

---

### 2.3 Operations — Full contract rows

#### Step 2 — Pay: CreateOrder

| | |
|---|---|
| Controller | `client.Orders` · source: `Api/Orders.cs` |
| Method | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion` — nullable, no default; pass `null` to skip |
| Idempotency key | `payPalRequestId` parameter → becomes `PayPal-Request-Id` header |
| prefer | Pass `"return=representation"` to get `Order.Id` reliably |
| Returns | `Order` (`PayPalServerSdk.Models`) |
| Error | `SdkException<CreateOrderError>` — Case A · `TryGetError(out Error typed)` [400, 401, 422] · `TryGetRawError(out RawError raw)` [fallback] · source: `Errors/CreateOrderError.cs` |

**`OrderRequest` fields** (`Models/OrderRequest.cs`, namespace `PayPalServerSdk.Models`):

| C# name (wire name) | Type | Required? |
|---|---|---|
| `Intent (intent)` | `CheckoutPaymentIntent` | **required** |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | **required** |
| `PaymentSource (payment_source)` | `PaymentSource?` | optional |
| `Payer (payer)` | `Payer?` | optional |
| `ApplicationContext (application_context)` | `OrderApplicationContext?` | optional |

**`PurchaseUnitRequest`** (`Models/PurchaseUnitRequest.cs`): `Amount (amount): AmountWithBreakdown !req`, `ReferenceId (reference_id): string?`, `CustomId (custom_id): string?`, `InvoiceId (invoice_id): string?`

**`AmountWithBreakdown`** (`Models/AmountWithBreakdown.cs`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?`

**Intent enum** (`Models/Enums/CheckoutPaymentIntent.cs`): `CheckoutPaymentIntent.Authorize ("AUTHORIZE")`, `CheckoutPaymentIntent.Capture ("CAPTURE")`
→ Use `CheckoutPaymentIntent.Authorize` for authorize-then-capture flow.

**`Order` response fields** (`Models/Order.cs`): `Id (id): string?`, `Status (status): OrderStatus?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `Links (links): IReadOnlyList<LinkDescription>?`

**`OrderStatus` enum** (`Models/Enums/OrderStatus.cs`): `Created ("CREATED")`, `Saved ("SAVED")`, `Approved ("APPROVED")`, `Voided ("VOIDED")`, `Completed ("COMPLETED")`, `PayerActionRequired ("PAYER_ACTION_REQUIRED")`

---

#### Step 2 — Pay: AuthorizeOrder

| | |
|---|---|
| Controller | `client.Orders` · source: `Api/Orders.cs` |
| Method | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalClientMetadataId`, `payPalAuthAssertion`, `body` — nullable, no default |
| Idempotency key | `payPalRequestId` parameter |
| prefer | **Must pass `"return=representation"`** to get the authorization ID in the response body |
| Returns | `OrderAuthorizeResponse` (`PayPalServerSdk.Models`) |
| Error | `SdkException<AuthorizeOrderError>` — Case A · `TryGetError(out Error typed)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError raw)` [fallback] |

**`OrderAuthorizeRequest`** (`Models/OrderAuthorizeRequest.cs`): `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`

**`OrderAuthorizeRequestPaymentSource`** (`Models/OrderAuthorizeRequestPaymentSource.cs`):
| C# name (wire name) | Type | Use case |
|---|---|---|
| `Card (card)` | `CardRequest?` | one-time card OR saved vault card via VaultId |
| `Token (token)` | `Token?` | billing-agreement tokens only (TokenType.BillingAgreement) |
| `Paypal (paypal)` | `PayPalWallet?` | PayPal wallet |

**One-time card** → `CardRequest { Number = "4111111111111111", Expiry = "YYYY-MM", SecurityCode = "...", Name = "..." }`
**Saved vault card** → `CardRequest { VaultId = paymentTokenId }` where `paymentTokenId` = `PaymentTokenResponse.Id` stored from vault flow.

**`CardRequest`** (`Models/CardRequest.cs`): `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `BillingAddress (billing_address): Address?`, `Attributes (attributes): CardAttributes?`, `VaultId (vault_id): string?`, `StoredCredential (stored_credential): CardStoredCredential?`

**`OrderAuthorizeResponse`** (`Models/OrderAuthorizeResponse.cs`):
| C# name (wire name) | Type |
|---|---|
| `Id (id)` | `string?` |
| `Status (status)` | `OrderStatus?` |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnit>?` |

**Reading the authorization ID** (requires `prefer = "return=representation"`):
```
response.PurchaseUnits[0].Payments.Authorizations[0].Id          // authorization ID — store this
response.PurchaseUnits[0].Payments.Authorizations[0].Status      // AuthorizationStatus
response.PurchaseUnits[0].Payments.Authorizations[0].ExpirationTime  // ISO-8601 string
response.PurchaseUnits[0].Payments.Authorizations[0].CreateTime  // ISO-8601 string
```

`PurchaseUnit.Payments` → `PaymentCollection` (`Models/PaymentCollection.cs`): `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`, `Captures (captures): IReadOnlyList<OrdersCapture>?`

**`AuthorizationWithAdditionalData`** (`Models/AuthorizationWithAdditionalData.cs`): `Id (id): string?`, `Status (status): AuthorizationStatus?`, `ExpirationTime (expiration_time): string?`, `CreateTime (create_time): string?`, `Amount (amount): Money?`

**`AuthorizationStatus` enum** (`Models/Enums/AuthorizationStatus.cs`): `Created ("CREATED")`, `Captured ("CAPTURED")`, `Denied ("DENIED")`, `PartiallyCaptured ("PARTIALLY_CAPTURED")`, `Voided ("VOIDED")`, `Pending ("PENDING")`

---

#### Step 3 — Fulfil: GetAuthorizedPayment (staleness check)

| | |
|---|---|
| Controller | `client.Payments` · source: `Api/Payments.cs` |
| Method | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalAuthAssertion` — nullable, no default |
| Returns | `PaymentAuthorization` (`PayPalServerSdk.Models`) |
| Error | `SdkException<GetAuthorizedPaymentError>` — Case A · `TryGetError(out Error typed)` [401, 403, 404] · `TryGetNoContent(out RawError raw)` [500] · `TryGetRawError(out RawError raw)` [fallback] |

**`PaymentAuthorization`** (`Models/PaymentAuthorization.cs`): `Id (id): string?`, `Status (status): AuthorizationStatus?`, `ExpirationTime (expiration_time): string?`, `CreateTime (create_time): string?`, `Amount (amount): Money?`, `StatusDetails (status_details): AuthorizationStatusDetails?`

**Staleness decision logic:**
1. `Status == AuthorizationStatus.Voided` → return error (already voided)
2. Parse `CreateTime` and `ExpirationTime`; if still within honor period (0-3 days) → capture directly
3. If expired but `CreateTime` < 29 days ago → attempt `ReauthorizePayment`
4. If `CreateTime` >= 30 days ago → return 422 actionable error: "Authorization expired beyond reauthorizable window; customer must place a new order"
5. If `ReauthorizePayment` returns 422 → return 422 actionable error with PayPal's `Error.Message`

---

#### Step 3 — Fulfil: ReauthorizePayment (if stale, days 4-29)

| | |
|---|---|
| Controller | `client.Payments` · source: `Api/Payments.cs` |
| Method | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalRequestId`, `payPalAuthAssertion`, `body` — nullable, no default |
| Returns | `PaymentAuthorization` — contains the **new** authorization ID |
| Error | `SdkException<ReauthorizePaymentError>` — Case A · `TryGetError(out Error typed)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError raw)` [500] · `TryGetRawError(out RawError raw)` [fallback] |

**`ReauthorizeRequest`** (`Models/ReauthorizeRequest.cs`): `Amount (amount): Money?` — only supported parameter per API notes.

**After ReauthorizePayment:** read `PaymentAuthorization.Id` from the response — this is the **new** authorization ID. Use this new ID (not the original) for the subsequent `CaptureAuthorizedPayment` call.

---

#### Step 3 — Fulfil: CaptureAuthorizedPayment

| | |
|---|---|
| Controller | `client.Payments` · source: `Api/Payments.cs` |
| Method | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` — nullable, no default |
| Idempotency key | `payPalRequestId` parameter |
| prefer | **Pass `"return=representation"`** to read `SellerReceivableBreakdown` (fee, net) |
| Returns | `CapturedPayment` (`PayPalServerSdk.Models`) |
| Error | `SdkException<CaptureAuthorizedPaymentError>` — Case A · `TryGetError(out Error typed)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError raw)` [500] · `TryGetRawError(out RawError raw)` [fallback] · **409 = duplicate idempotency key / already captured** |

**`CaptureRequest`** (`Models/CaptureRequest.cs`): `Amount (amount): Money?`, `InvoiceId (invoice_id): string?`, `FinalCapture (final_capture): bool? = false`, `NoteToPayer (note_to_payer): string?`

**`CapturedPayment`** (`Models/CapturedPayment.cs`) — fields to read:
| C# name (wire name) | Type | Purpose |
|---|---|---|
| `Id (id)` | `string?` | capture ID — store for refunds |
| `Status (status)` | `CaptureStatus?` | must be `Completed` |
| `Amount (amount)` | `Money?` | captured amount |
| `SellerReceivableBreakdown (seller_receivable_breakdown)` | `SellerReceivableBreakdown?` | fee + net |

**`SellerReceivableBreakdown`** (`Models/SellerReceivableBreakdown.cs`): `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?`

**`Money`** (`Models/Money.cs`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`

**`CaptureStatus` enum** (`Models/Enums/CaptureStatus.cs`): `Completed ("COMPLETED")`, `Declined ("DECLINED")`, `PartiallyRefunded ("PARTIALLY_REFUNDED")`, `Pending ("PENDING")`, `Refunded ("REFUNDED")`, `Failed ("FAILED")`

---

#### Step 4 — Cancel: VoidPayment

| | |
|---|---|
| Controller | `client.Payments` · source: `Api/Payments.cs` |
| Method | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` — nullable, no default |
| **Param order note** | `payPalAuthAssertion` precedes `payPalRequestId` here — different from CaptureAuthorizedPayment |
| Returns | `PaymentAuthorization` |
| Error | `SdkException<VoidPaymentError>` — Case A · `TryGetError(out Error typed)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError raw)` [500] · `TryGetRawError(out RawError raw)` [fallback] |

---

#### Step 5 — Refund: RefundCapturedPayment

| | |
|---|---|
| Controller | `client.Payments` · source: `Api/Payments.cs` |
| Method | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` — nullable, no default |
| Idempotency key | `payPalRequestId` = caller-supplied idempotency key; PayPal returns the existing refund for duplicate keys without charging again |
| Full refund | Pass `body: null` (or `new RefundRequest()` with no Amount) |
| Partial refund | Pass `body: new RefundRequest { Amount = new Money { CurrencyCode = …, Value = "…" } }` |
| Returns | `Refund` (`PayPalServerSdk.Models`) |
| Error | `SdkException<RefundCapturedPaymentError>` — Case A · `TryGetError(out Error typed)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError raw)` [500] · `TryGetRawError(out RawError raw)` [fallback] · **409 = duplicate idempotency key (existing refund returned, not a double-charge)** |

**`RefundRequest`** (`Models/RefundRequest.cs`): `Amount (amount): Money?`, `CustomId (custom_id): string?`, `InvoiceId (invoice_id): string?`, `NoteToPayer (note_to_payer): string?`

**`Refund`** (`Models/Refund.cs`): `Id (id): string?`, `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?`, `CreateTime (create_time): string?`

**`RefundStatus` enum** (`Models/Enums/RefundStatus.cs`): `Cancelled ("CANCELLED")`, `Failed ("FAILED")`, `Pending ("PENDING")`, `Completed ("COMPLETED")`

**Partial-refund guard (application logic):** The application must track the sum of all refunds issued against a given capture ID. Before calling `RefundCapturedPayment` for a partial refund, verify that `existingRefundsTotal + requestedAmount <= capturedAmount`. PayPal may also enforce this (returning 422), but the application must never surface a confusing error; enforce it before the SDK call.

---

#### Step 7 — Reconciliation: SearchTransactions (paginated)

| | |
|---|---|
| Controller | `client.TransactionSearch` · source: `Api/TransactionSearch.cs` |
| Method | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `transactionId` … `terminalId` — 8 nullable params, no default; pass `null` to skip |
| `from`/`to` mapping | `startDate` ← `from`, `endDate` ← `to`; wire names: `start_date`, `end_date`; format: ISO-8601 (e.g. `"2024-01-01T00:00:00-0000"`) |
| Returns | `SearchResponse` (`PayPalServerSdk.Models`) |
| **Error** | **`SdkException<RawError>` — Case B (raw; NOT typed)** · `ex.Error.StatusCode: HttpStatusCode` · `ex.Error.ReadAsString(): string` · `ex.Error.ReadAsJson<T>(): T?` |
| Pagination | Manual loop: call with `page: 1` first, read `SearchResponse.TotalPages`, then loop pages 2..TotalPages. The SDK has no built-in page iterator for this operation. |

**`SearchResponse`** (`Models/SearchResponse.cs`):
| C# name (wire name) | Type |
|---|---|
| `TransactionDetails (transaction_details)` | `IReadOnlyList<TransactionDetails>?` |
| `TotalPages (total_pages)` | `int?` |
| `TotalItems (total_items)` | `int?` |
| `Page (page)` | `int?` |

**`TransactionDetails`** (`Models/TransactionDetails.cs`): `TransactionInfo (transaction_info): TransactionInformation?`, `PayerInfo (payer_info): PayerInformation?`

**`TransactionInformation`** (`Models/TransactionInformation.cs`) key fields: `TransactionId (transaction_id): string?`, `TransactionAmount (transaction_amount): Money?`, `FeeAmount (fee_amount): Money?`, `TransactionStatus (transaction_status): string?`, `TransactionInitiationDate (transaction_initiation_date): string?`, `PaypalReferenceId (paypal_reference_id): string?`, `InvoiceId (invoice_id): string?`

**Paginated sweep pattern:**
```csharp
var allTransactions = new List<TransactionDetails>();
int page = 1;
int totalPages;
do
{
    var resp = await client.TransactionSearch.SearchTransactions(
        startDate: from, endDate: to,
        transactionId: null, transactionType: null, transactionStatus: null,
        transactionAmount: null, transactionCurrency: null,
        paymentInstrumentType: null, storeId: null, terminalId: null,
        page: page,
        ct: ct);
    totalPages = resp.TotalPages ?? 1;
    if (resp.TransactionDetails != null)
        allTransactions.AddRange(resp.TransactionDetails);
    page++;
} while (page <= totalPages);
```

---

#### Step 8 — Vault Card: CreateSetupToken

| | |
|---|---|
| Controller | `client.Vault` · source: `Api/Vault.cs` |
| Method | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalRequestId` — nullable, no default |
| Idempotency key | `payPalRequestId` parameter |
| Returns | `SetupTokenResponse` (`PayPalServerSdk.Models`) |
| Error | `SdkException<CreateSetupTokenError>` — Case A · `TryGetError1(out Error1 typed)` [400, 403, 422, 500] · `TryGetRawError(out RawError raw)` [fallback] |

**`SetupTokenRequest`** (`Models/SetupTokenRequest.cs`): `Customer (customer): Customer?`, `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req`

**`Customer`** (`Models/Customer.cs`): `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?`

**`SetupTokenRequestPaymentSource`** (`Models/SetupTokenRequestPaymentSource.cs`): `Card (card): SetupTokenRequestCard?`

**`SetupTokenRequestCard`** (`Models/SetupTokenRequestCard.cs`): `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`, `VerificationMethod (verification_method): VaultCardVerificationMethod?`

**`SetupTokenResponse`** (`Models/SetupTokenResponse.cs`):
| C# name (wire name) | Type | Purpose |
|---|---|---|
| `Id (id)` | `string?` | setup token ID — passed to CreatePaymentToken |
| `Status (status)` | `PaymentTokenStatus?` | expect `Created` after this call |
| `PaymentSource (payment_source)` | `SetupTokenResponsePaymentSource?` | card descriptor for display |

**`SetupTokenResponsePaymentSource`** → `Card (card): SetupTokenResponseCard?`
**`SetupTokenResponseCard`** (`Models/SetupTokenResponseCard.cs`): `LastDigits (last_digits): string?`, `Brand (brand): CardBrand?`, `Expiry (expiry): string?`, `Name (name): string?`

**`PaymentTokenStatus` enum** (`Models/Enums/PaymentTokenStatus.cs`): `Created ("CREATED")`, `PayerActionRequired ("PAYER_ACTION_REQUIRED")`, `Approved ("APPROVED")`, `Vaulted ("VAULTED")`, `Tokenized ("TOKENIZED")`

---

#### Step 8 — Vault Card: CreatePaymentToken

| | |
|---|---|
| Controller | `client.Vault` · source: `Api/Vault.cs` |
| Method | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalRequestId` — nullable, no default |
| Returns | `PaymentTokenResponse` (`PayPalServerSdk.Models`) |
| Error | `SdkException<CreatePaymentTokenError>` — Case A · `TryGetError1(out Error1 typed)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError raw)` [fallback] |

**`PaymentTokenRequest`** (`Models/PaymentTokenRequest.cs`): `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`

**`PaymentTokenRequestPaymentSource`** (`Models/PaymentTokenRequestPaymentSource.cs`): `Token (token): VaultTokenRequest?`

**`VaultTokenRequest`** (`Models/VaultTokenRequest.cs`): `Id (id): string !req` (= setup token ID from step 8a), `Type (type): VaultTokenRequestType !req`

**`VaultTokenRequestType` enum** (`Models/Enums/VaultTokenRequestType.cs`): `SetupToken ("SETUP_TOKEN")`

**`PaymentTokenResponse`** (`Models/PaymentTokenResponse.cs`):
| C# name (wire name) | Type | Purpose |
|---|---|---|
| `Id (id)` | `string?` | payment token ID — store as `paymentMethodId`, pass as `CardRequest.VaultId` when paying |
| `Customer (customer)` | `CustomerResponse?` | customer reference |
| `PaymentSource (payment_source)` | `PaymentTokenResponsePaymentSource?` | card descriptor |

**`PaymentTokenResponsePaymentSource`** → `Card (card): CardPaymentTokenEntity?`
**`CardPaymentTokenEntity`** (`Models/CardPaymentTokenEntity.cs`): `LastDigits (last_digits): string?`, `Brand (brand): CardBrand?`, `Expiry (expiry): string?`, `Name (name): string?`, `Type (type): CardType?`

→ Store `PaymentTokenResponse.Id` as `paymentMethodId`. Return `{ LastDigits, Brand, Expiry, Name }` as safe descriptor (never store `Number` or `SecurityCode`).

**`CardBrand` enum** (partial, `Models/Enums/CardBrand.cs`): `Visa ("VISA")`, `Mastercard ("MASTERCARD")`, `Amex ("AMEX")`, `Discover ("DISCOVER")`, `Jcb ("JCB")`, `Maestro ("MAESTRO")`, `Diners ("DINERS")`

---

#### Step 9 — List vault tokens: ListCustomerPaymentTokens

| | |
|---|---|
| Controller | `client.Vault` · source: `Api/Vault.cs` |
| Method | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `customerId` | not nullable — required positional param; maps to query `customer_id` |
| Returns | `CustomerVaultPaymentTokensResponse` (`PayPalServerSdk.Models`) |
| Error | `SdkException<ListCustomerPaymentTokensError>` — Case A · `TryGetError1(out Error1 typed)` [400, 403, 500] · `TryGetRawError(out RawError raw)` [fallback] |

**`CustomerVaultPaymentTokensResponse`** (`Models/CustomerVaultPaymentTokensResponse.cs`): `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Customer (customer): VaultResponseCustomer?`

---

#### Step 10 — Delete vault token: DeletePaymentToken

| | |
|---|---|
| Controller | `client.Vault` · source: `Api/Vault.cs` |
| Method | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `void` (Task) |
| Error | `SdkException<DeletePaymentTokenError>` — Case A · `TryGetError1(out Error1 typed)` [400, 403, 500] · `TryGetRawError(out RawError raw)` [fallback] |

---

### 2.4 Error payload models

**`Error`** (Orders/Payments error payloads, `Models/Error.cs`, namespace `PayPalServerSdk.Models`):
`Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`

**`ErrorDetails`** (`Models/ErrorDetails.cs`): `Issue (issue): string !req`, `Description (description): string?`, `Field (field): string?`, `Location (location): string?`

**`Error1`** (Vault error payloads, `Models/Error1.cs`): same shape but `Details (details): IReadOnlyList<ErrorDetails1>?` · `Links (links): IReadOnlyList<ErrorLinkDescription>?`

**`RawError`** (`Core/ErrorResponse/RawError.cs`, namespace differs — load `dotnet-error-handling` for exact using): `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`

---

### 2.5 Enum summary (only those used in this integration)

| Enum | Namespace | Values needed |
|---|---|---|
| `CheckoutPaymentIntent` | `PayPalServerSdk.Models.Enums` | `Authorize ("AUTHORIZE")` |
| `OrderStatus` | `PayPalServerSdk.Models.Enums` | `Created`, `Approved`, `Completed`, `Voided`, `PayerActionRequired` |
| `AuthorizationStatus` | `PayPalServerSdk.Models.Enums` | `Created ("CREATED")`, `Voided ("VOIDED")`, `Captured ("CAPTURED")`, `Denied ("DENIED")`, `Pending ("PENDING")`, `PartiallyCaptured ("PARTIALLY_CAPTURED")` |
| `CaptureStatus` | `PayPalServerSdk.Models.Enums` | `Completed ("COMPLETED")`, `Declined ("DECLINED")`, `Pending ("PENDING")`, `Failed ("FAILED")`, `PartiallyRefunded ("PARTIALLY_REFUNDED")`, `Refunded ("REFUNDED")` |
| `RefundStatus` | `PayPalServerSdk.Models.Enums` | `Completed ("COMPLETED")`, `Pending ("PENDING")`, `Failed ("FAILED")`, `Cancelled ("CANCELLED")` |
| `PaymentTokenStatus` | `PayPalServerSdk.Models.Enums` | `Created ("CREATED")`, `Vaulted ("VAULTED")`, `Approved ("APPROVED")` |
| `VaultTokenRequestType` | `PayPalServerSdk.Models.Enums` | `SetupToken ("SETUP_TOKEN")` |
| `CardBrand` | `PayPalServerSdk.Models.Enums` | `Visa`, `Mastercard`, `Amex`, `Discover`, `Jcb`, `Maestro`, `Diners` (see full list in enums.md) |
| `ServerEnvironment` | `PayPalServerSdk.Servers` | `Sandbox ("sandbox")` — only member in this SDK version |

---

## 3. Trap Notes

> ⚠ **Step 2 (pay — `prefer` parameter)** — `AuthorizeOrder` and `CaptureAuthorizedPayment` default to `prefer = "return=minimal"`. With minimal, the PayPal response body may omit the authorization ID and `SellerReceivableBreakdown`. Always pass `prefer: "return=representation"` for these two calls. **MUST load `dotnet-calling-endpoints`** before writing any call to verify the exact named-argument form and how prefer interacts with the response shape.

> ⚠ **Step 0 (client registration)** — the SDK's `HttpClient`/handler pipeline must be long-lived via `IHttpClientFactory`, not rebuilt per request. **MUST load `dotnet-client-initialization`** before writing the DI registration or client factory.

> ⚠ **Step 0 (authentication)** — `OAuth2ClientCredentials` field names are in `Core/` and not listed in the SDK map's records pages. **MUST load `dotnet-authentication`** before wiring credentials; do not guess the property names.

> ⚠ **Steps 0 / config** — the SDK exposes only `ServerEnvironment.Sandbox`. For live/production, the base URL must be overridden via `options.Server` (ServerOptions). The exact `ServerOptions` field names and the retry/timeout semantics are NOT in the map. **MUST load `dotnet-configuration-resilience`** before wiring the `PayPal:BaseUrl` override or tuning retry/timeout. Note also: retries fire on `HttpRequestException` for ALL verbs including POST — a non-idempotent write (AuthorizeOrder without a `payPalRequestId`) can execute more than once under transport failure.

> ⚠ **Step 5 (refund idempotency)** — `payPalRequestId` is the idempotency key for `RefundCapturedPayment`. A 409 response means the key was already used and PayPal returned the existing refund — this is NOT a failure; read the 409 body via `TryGetError(out Error e)` and map it to the existing refund. A missing idempotency key on a retried POST will produce a second refund. **MUST load `dotnet-error-handling`** for the exact catch pattern for 409 vs. other error codes.

> ⚠ **Step 7 (SearchTransactions error — Case B)** — `SearchTransactions` is the only Case B operation in this integration. The catch type is `SdkException<RawError>`, NOT `SdkException<SearchTransactionsError>` (no typed error class exists). A boundary catching only typed SDK exceptions will silently swallow SearchTransactions failures. **MUST load `dotnet-error-handling`** before writing the reconciliation error boundary.

> ⚠ **Steps 8-10 (vault error type)** — Vault operations throw `SdkException<{Op}Error>` with accessor `TryGetError1(out Error1 typed)` (note: `Error1`, not `Error`). These are different types in `PayPalServerSdk.Models`. Catching `Error` where `Error1` is needed will silently miss the typed payload. **MUST load `dotnet-error-handling`**.

> ⚠ **Step 3 (reauthorization window)** — from the `ReauthorizePayment` doc: reauthorization is valid from day 4 to day 29 of the original authorization; day 30+ requires creating a new authorized payment (new `CreateOrder` + `AuthorizeOrder`). The application logic must track `CreateTime` from the `PaymentAuthorization`, not only `ExpirationTime`. **MUST load `dotnet-calling-endpoints`** for handling the case where `ReauthorizePayment` itself returns 422.

> ⚠ **Step 8 (card expiry format on `SetupTokenRequestCard.Expiry`)** — the field type is `string?`; the exact wire format (e.g. `"YYYY-MM"`) is not specified in the map. UNVERIFIED — only live sandbox traffic can confirm. Defensive coding: validate the incoming expiry format in the API layer before forwarding to PayPal, and surface PayPal's 422 `ErrorDetails.Issue` to the caller if PayPal rejects it.

---

## 4. REQUIRED READING

Load every skill below **before implementation starts**. The contract sheet above deliberately does not carry their contents — these skills resolve the usage layer, defaults, and hazards a signature cannot show.

| Skill | Steps it governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — `HttpClient` lifetime, `IHttpClientFactory`, DI registration via `AddPayPalServerSdkClient` |
| `dotnet-authentication` | Step 0 — `OAuth2ClientCredentials` field names, how to set credentials before client construction |
| `dotnet-calling-endpoints` | Steps 2-5, 7-10 — named-argument form, required vs optional params, `prefer` header, async/cancellation |
| `dotnet-models` | Steps 2, 8 — `StringEnum<T>` construction (not C# enum), `init`-only record initializers |
| `dotnet-error-handling` | All steps — Case A vs Case B catch pattern, `TryGetError` vs `TryGetError1` vs `TryGetNoContent`, 409 handling, `JsonException` boundary hazards (see below) |
| `dotnet-configuration-resilience` | Step 0 — `ServerOptions` base URL override for live, retry semantics, `Timeout` per-attempt scope |
| `dotnet-testing` | All steps — SDK test seam, stub pattern |

**Mandatory `dotnet-error-handling` boundary hazards (include in FIRST implementation of the error boundary):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

| # | Item |
|---|---|
| A1 | "Signed-in shopper" identity is assumed to be provided by the existing application's auth middleware. The plan uses `merchantCustomerId` (a stable internal user ID) as the `Customer.Id` passed to vault operations. The exact field for this mapping must be chosen by the implementer from the existing user model. |
| A2 | `GET /api/my-orders` is purely a local DB query; no PayPal call is needed. The payment state (authorization ID, capture ID, status) is assumed to be persisted in the local Order record during Steps 2-5. |
| A3 | The `PayPal:Currency` config value (e.g. `"USD"`) is used as `Money.CurrencyCode` and `AmountWithBreakdown.CurrencyCode` throughout. |
| A4 | The two-step vault flow (`CreateSetupToken` then `CreatePaymentToken`) assumes the card is vaulted server-side without a buyer browser redirect (direct server API call). PayPal may require 3DS for certain cards even in this flow; the `SetupTokenResponse.Status == PayerActionRequired` case must be handled (out of scope of this plan — flag to product). |
| A5 | The `ServerEnvironment` has only `Sandbox` as a named member in this SDK version. Live production will require a base URL override via `options.Server`. The exact live PayPal base URL and `ServerOptions` field names require loading `dotnet-configuration-resilience`. |
| B1 | ~~Blocker for live environment~~ — **Resolved.** `ServerEnvironment` has only `Sandbox` by design in this SDK version; live is handled by overriding `options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"`. No blocker. |
