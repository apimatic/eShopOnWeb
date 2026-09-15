# PayPal .NET SDK integration plan — eShopOnWeb (SANDBOX, direct card + vault)

SDK: `AsadAli.Checkout.Sdk` (install version-less) · root namespace `PayPalServerSdk` · client
`PayPalServerSdkClient` · map provenance tag `v1.0.1` (commit `9653d18`). Target = PayPal
**Sandbox**. All operations below are **throw-based** (no `…Result` no-throw variants exist).

This plan is grounded entirely in the bundled SDK map (pages cited per row) plus, for the base-URL /
auth-token-URL derivation the map does not carry, the pinned SDK source. Every type is written
fully-qualified with the namespace its map row / source gives it.

---

## 1. Scope & sequence

| # | Step | Operations (controller.method) |
|---|------|-------------------------------|
| 0 | Client + DI + auth + base-URL config | `AddPayPalServerSdkClient` (DI); options: `Oauth2`, `Environment`, `Server` |
| 1 | Authorize a hold (direct card OR vaulted token) | `Orders.CreateOrder` (intent=AUTHORIZE) → `Orders.AuthorizeOrder` |
| 2 | Capture at fulfilment | `Payments.CaptureAuthorizedPayment` |
| 3 | Re-authorize a stale hold | `Payments.ReauthorizePayment` (read staleness from `Payments.GetAuthorizedPayment`) |
| 4 | Cancel before fulfilment (void) | `Payments.VoidPayment` |
| 5 | Refund after fulfilment (full/partial) | `Payments.RefundCapturedPayment` (remaining via `Payments.GetCapturedPayment`) |
| 6 | Reconciliation (date-range, paged) | `TransactionSearch.SearchTransactions` |
| 7 | Vault: save card (no payment) | `Vault.CreatePaymentToken` |
| 8 | Vault: pay with saved card | reference token id in step 1 (`OrderAuthorizeRequestPaymentSource.Token`) |
| 9 | Vault: delete saved card | `Vault.DeletePaymentToken` |

**Design note on the two ways to reach an authorization hold** (both fully supported by the map):

- **Recommended two-call flow:** `CreateOrder` with `Intent = CheckoutPaymentIntent.Authorize` and the
  `payment_source` on the **OrderRequest** (`OrderRequest.PaymentSource`), then `AuthorizeOrder(orderId,
  …, body: null, …)`. With `payment_source` supplied at create time no buyer redirect is needed for a
  sandbox card, and `AuthorizeOrder` produces the hold.
- **Alternative single-call authorize:** `CreateOrder` (intent=AUTHORIZE) then `AuthorizeOrder` with the
  card/token supplied on the **OrderAuthorizeRequest** body (`OrderAuthorizeRequestPaymentSource`). Use
  this if you prefer to defer the payment source to the authorize call.

Pick one and keep it consistent. Rows below give both request shapes.

**3DS / challenge STOP condition:** if any response comes back `status = PAYER_ACTION_REQUIRED`
(`OrderStatus.PayerActionRequired`) or a HATEOAS link with `rel = "payer-action"`/`rel="approve"` is
present on the order, the card requires browser approval — **STOP and report**, do not build a redirect.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The
> cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from
> that type's own map row, never from where a neighbouring type sits. Enums, unions, auth, server and
> client-config types are spread across different child namespaces, and two types configured side by side
> in the same options object routinely live in different ones. Dropping a type to the root or to `.Models`
> makes the implementer guess the wrong `using`, and the build breaks.

### 2.1 Namespaces (using-directives) — confirmed per type

| Type(s) | Namespace | Source |
|---|---|---|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions`, `AddPayPalServerSdkClient` (ext. on `IServiceCollection`) | `PayPalServerSdk` | sdk-map.md; `ServerOptions.cs`, `ServiceCollectionExtensions.cs` (source) |
| `ServerEnvironment`, `DefaultOptions` (+ nested `DefaultOptions.SandboxOptions`) | `PayPalServerSdk.Servers` | `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs` (source) |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | source (`Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`) |
| Controllers `Orders`, `Payments`, `Vault`, `TransactionSearch` | `PayPalServerSdk.Api` | sdk-map.md namespaces table |
| All request/response records (`OrderRequest`, `Money`, `CardRequest`, `Refund`, …) | `PayPalServerSdk.Models` | records-1/records-2 headers |
| All enums (`CheckoutPaymentIntent`, `OrderStatus`, `AuthorizationStatus`, …) | `PayPalServerSdk.Models.Enums` | sdk-map.md namespaces table |
| Typed error payloads `Error`, `Error1`, `DefaultError` | `PayPalServerSdk.Models` | records-1 (they are ordinary records) |
| Per-op error classes `{Op}Error` (e.g. `AuthorizeOrderError`) | `PayPalServerSdk.Errors` | sdk-map.md namespaces table |
| `SdkException<TError>` | `PayPalServerSdk.Core.Exceptions` | source (`Core/Exceptions/SdkException.cs`) |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` | source (`Core/ErrorResponse/*.cs`) |

### 2.2 Operation rows

Legend: `Name (wire_name): type` for fields; `!req` = C# `required`; trailing `?` = optional/nullable.
"must-pass params" = nullable parameters with **no C# default** — pass `null` explicitly to skip, or a
positional call mis-binds. `ct` is `CancellationToken ct = default`.

---

#### Step 1a — `client.Orders.CreateOrder` — `operations/Orders.md`
- **Signature:** `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass params:** `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion` (all nullable, no default). **Idempotency key = `payPalRequestId`** (PayPal-Request-Id). `body` is non-nullable required.
- **`prefer`:** default `"return=minimal"`. To read back the full order (status, links, embedded payments) pass `prefer: "return=representation"`.
- **Request `OrderRequest`** (`records-1`): `Intent (intent): CheckoutPaymentIntent !req`, `Payer (payer): Payer?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `ApplicationContext (application_context): OrderApplicationContext?`.
  - Set `Intent = PayPalServerSdk.Models.Enums.CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`).
  - `PurchaseUnitRequest` (`records-2`): `ReferenceId (reference_id): string?`, `Amount (amount): AmountWithBreakdown !req`, `CustomId (custom_id): string?`, `InvoiceId (invoice_id): string?`, `Description (description): string?`, `SoftDescriptor (soft_descriptor): string?`, `Items (items): IReadOnlyList<ItemRequest>?`, … — put your local order id in **`CustomId`** and/or **`InvoiceId`** so reconciliation (step 6) can tie back.
  - `AmountWithBreakdown` (`records-1`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?`. **Amount is a string to the cent** — e.g. `"49.99"` for USD (2 decimal places); `CurrencyCode` from config (USD).
  - **PaymentSource for a direct card (option "payment_source at create"):** `PaymentSource` (`records-2`) `.Card = CardRequest`. `CardRequest` (`records-1`): `Name (name): string?`, `Number (number): string?` (e.g. `"4111111111111111"`), `Expiry (expiry): string?` (`"YYYY-MM"`), `SecurityCode (security_code): string?` (cvc), `BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?`, `StoredCredential (stored_credential): CardStoredCredential?`, `ExperienceContext (experience_context): CardExperienceContext?`.
    - `Address` (`records-1`): `AddressLine1 (address_line_1): string?`, `AddressLine2 (address_line_2): string?`, `AdminArea2 (admin_area_2): string?` (city), `AdminArea1 (admin_area_1): string?` (state), `PostalCode (postal_code): string?`, `CountryCode (country_code): string !req`.
  - **PaymentSource for a vaulted card:** on `CardRequest` set `VaultId = <payment-token-id>` (the id returned by `Vault.CreatePaymentToken`). (The `PaymentSource.Token` variant, type `Token`, is for `BILLING_AGREEMENT` tokens only — see enum note — not for vaulted cards; use `CardRequest.VaultId` for a saved card.)
- **Returns:** `Order` (`records-1`): `Id (id): string?`, `Status (status): OrderStatus?`, `Intent (intent): CheckoutPaymentIntent?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `PaymentSource (payment_source): PaymentSourceResponse?`, `Links (links): IReadOnlyList<LinkDescription>?`. **Order id = `Order.Id`.**
- **Error:** `SdkException<PayPalServerSdk.Errors.CreateOrderError>` — **Case A**. Accessors: `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback].
- Pagination: none.

#### Step 1b — `client.Orders.AuthorizeOrder` — `operations/Orders.md`
- **Signature:** `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass params:** `payPalMockResponse`, `payPalRequestId`, `payPalClientMetadataId`, `payPalAuthAssertion`, `body` (all nullable, no default). **Idempotency key = `payPalRequestId`.**
- **`id`** = the `Order.Id` from step 1a.
- **`body` (optional `OrderAuthorizeRequest`)** — pass `null` if the payment source was already supplied at create (option A). For the "supply card/token at authorize" flow (option B): `OrderAuthorizeRequest` (`records-1`) `.PaymentSource = OrderAuthorizeRequestPaymentSource`. `OrderAuthorizeRequestPaymentSource` (`records-1`): `Card (card): CardRequest?`, `Token (token): Token?`, `Paypal`, `ApplePay`, `GooglePay`, `Venmo`. Direct card → set `.Card` (same `CardRequest` shape as 1a); vaulted card → set `.Card = new CardRequest { VaultId = <token id> }`.
- **Returns:** `OrderAuthorizeResponse` (`records-1`): `Id (id): string?`, `Status (status): OrderStatus?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `PaymentSource (payment_source): OrderAuthorizeResponsePaymentSource?`, `Links (links): IReadOnlyList<LinkDescription>?`.
- **Where the authorization id + status live:** `OrderAuthorizeResponse.PurchaseUnits[i].Payments (payments): PaymentCollection?` → `PaymentCollection.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → `AuthorizationWithAdditionalData.Id (id): string?` (**authorization id → use for capture/void/reauthorize**) and `.Status (status): AuthorizationStatus?`, `.ExpirationTime (expiration_time): string?` (honor-period expiry timestamp — see staleness, step 3). (`PurchaseUnit`, `PaymentCollection`, `AuthorizationWithAdditionalData` per records-1/records-2.)
- **Error:** `SdkException<PayPalServerSdk.Errors.AuthorizeOrderError>` — **Case A**. Accessors: `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback].
- Pagination: none.

#### Step 2 — `client.Payments.CaptureAuthorizedPayment` — `operations/Payments.md`
- **Signature:** `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass params:** `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body`. **Idempotency key = `payPalRequestId`.**
- **`authorizationId`** = the authorization id from step 1b.
- **`body` (optional `CaptureRequest`)** (`records-1`): `Amount (amount): Money?` (omit / `null` for full capture; set for partial), `InvoiceId (invoice_id): string?`, `FinalCapture (final_capture): bool? = false` (set `true` on the last capture so PayPal releases any remaining hold), `NoteToPayer (note_to_payer): string?`, `SoftDescriptor (soft_descriptor): string?`. `Money` = `CurrencyCode (currency_code): string !req`, `Value (value): string !req`.
- **Returns:** `CapturedPayment` (`records-1`): `Id (id): string?` (**capture id → use for refund**), `Status (status): CaptureStatus?`, `Amount (amount): Money?` (captured amount), `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`, `FinalCapture (final_capture): bool?`, `CustomId`, `InvoiceId`.
- **Fee / net proceeds (`SellerReceivableBreakdown`, records-2):**
  - `GrossAmount (gross_amount): Money !req` — gross captured.
  - `PaypalFee (paypal_fee): Money?` — **PayPal fee**.
  - `NetAmount (net_amount): Money?` — **net proceeds to merchant**.
  - (also `PaypalFeeInReceivableCurrency`, `ReceivableAmount`, `ExchangeRate` when cross-currency.)
  - Each is a `Money { CurrencyCode, Value }`. `PaypalFee`/`NetAmount` are nullable — read defensively (may be absent for pending captures; `GrossAmount` is the only `!req` member).
- **Error:** `SdkException<PayPalServerSdk.Errors.CaptureAuthorizedPaymentError>` — **Case A**. Accessors: `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]. **409 = already-captured / state conflict** (see error-mapping note).
- Pagination: none.

#### Step 3 — `client.Payments.ReauthorizePayment` — `operations/Payments.md`
- **Signature:** `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass params:** `payPalRequestId`, `payPalAuthAssertion`, `body`. **Idempotency key = `payPalRequestId`.**
- **`body` `ReauthorizeRequest`** (`records-2`): `Amount (amount): Money?` — **only `amount` is supported** by this endpoint (per map notes).
- **Returns:** `PaymentAuthorization` (`records-2`): `Id (id): string?`, `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?` (new 3-day honor window), `SellerProtection`, `CreateTime`, `UpdateTime`.
- **Honor period / validity window (from map notes on `ReauthorizePayment` + `ReauthorizeRequest`):** initial authorization has a **3-day honor period**; you may reauthorize from **day 4 to day 29**; a reauthorized payment gets a **new 3-day honor period**; **after 30 days from the original authorization you must create a new authorized payment** (reauthorize is no longer allowed). Allowed amount is contextual (in the US up to ~115% of original, capped at +$75 USD).
- **Detecting stale vs permanently un-renewable — map-grounded contract facts:**
  - There is **no `EXPIRED`/`STALE` member** in `AuthorizationStatus` (values: `Created, Captured, Denied, PartiallyCaptured, Voided, Pending` — enums.md). So staleness is **not** readable as a status enum value.
  - Detect the honor-period boundary by reading `PaymentAuthorization.ExpirationTime` (or `AuthorizationWithAdditionalData.ExpirationTime` on the order) via `client.Payments.GetAuthorizedPayment(authorizationId, null, null)` (returns `PaymentAuthorization`) and comparing to now: past `ExpirationTime` but within 29 days ⇒ eligible for reauthorize; ≥30 days from original ⇒ not renewable, create a fresh order/authorization.
  - The **operator-actionable distinction at runtime** comes from the reauthorize error: a renewable-but-stale hold that simply needs reauthorizing vs. a permanently un-renewable one (>30 days) both surface as `SdkException<ReauthorizePaymentError>` (typically 422). The SDK error payload only carries a free-form `Issue`/`Description` string (`PayPalServerSdk.Models.Error.Details[]` — `ErrorDetails.Issue (issue): string !req`, `.Description (description): string?`); the **exact issue code strings are set by the live PayPal API and are not in the SDK map or source**. Directive: read `Details[].Issue`/`Description` best-effort to compose the operator message, and branch on your own `ExpirationTime`-vs-30-day computation for the renewable/not-renewable decision rather than on an assumed issue string. `UNVERIFIED` — the precise issue code text can only be confirmed against live sandbox traffic.
- **Error:** `SdkException<PayPalServerSdk.Errors.ReauthorizePaymentError>` — **Case A**. Accessors: `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].
- Pagination: none.

#### Step 3-aux — `client.Payments.GetAuthorizedPayment` — `operations/Payments.md`
- **Signature:** `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — must-pass: `payPalMockResponse`, `payPalAuthAssertion`.
- **Returns:** `PaymentAuthorization` (fields as step 3). Use for the `Status`/`ExpirationTime` staleness check before capture.
- **Error:** `SdkException<PayPalServerSdk.Errors.GetAuthorizedPaymentError>` — **Case A**. `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].

#### Step 4 — `client.Payments.VoidPayment` — `operations/Payments.md`
- **Signature:** `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass params:** `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId`. **Idempotency key = `payPalRequestId`.** No request body.
- **Returns:** `PaymentAuthorization` (its `Status` should read `AuthorizationStatus.Voided` after a successful void). Note: **cannot void a fully-captured authorization** (map note) — surfaces as 409/422.
- **Error:** `SdkException<PayPalServerSdk.Errors.VoidPaymentError>` — **Case A**. `TryGetError(out Error)` [401, 403, 404, **409**, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].
- Pagination: none.

#### Step 5 — `client.Payments.RefundCapturedPayment` — `operations/Payments.md`
- **Signature:** `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass params:** `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body`. **Idempotency / caller-supplied key = `payPalRequestId`.**
- **`captureId`** = the `CapturedPayment.Id` from step 2.
- **`body` `RefundRequest`** (`records-2`): `Amount (amount): Money?` — **omit / pass `body: null` for a FULL refund; set `Amount` for a PARTIAL refund**. Also `CustomId (custom_id): string?`, `InvoiceId (invoice_id): string?`, `NoteToPayer (note_to_payer): string?`. `Money` = `{ CurrencyCode !req, Value !req }`.
- **Returns:** `Refund` (`records-2`): `Id (id): string?` (**refund id**), `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?`, `CustomId`, `InvoiceId`, `CreateTime`, `UpdateTime`.
- **Remaining-refundable guard (so a partial-refunded order never over-refunds):** read it from the **capture**, not the refund. `client.Payments.GetCapturedPayment(captureId, null)` returns `CapturedPayment`; its `SellerReceivableBreakdown.GrossAmount` is the total captured. Sum prior refunds for that capture. Also `Refund.SellerPayableBreakdown` (`records-2`) carries `TotalRefundedAmount (total_refunded_amount): Money?` and `GrossAmount (gross_amount): Money?` per refund — track cumulative refunded against captured gross before issuing a partial refund. (`SellerPayableBreakdown`: `GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount`, all `Money?`.)
- **Error:** `SdkException<PayPalServerSdk.Errors.RefundCapturedPaymentError>` — **Case A**. Accessors: `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]. **422 = insufficient/over-refund conditions** (exact `Issue` string is live-wire — see error-mapping note).
- Pagination: none.

#### Step 5-aux — `client.Payments.GetCapturedPayment` — `operations/Payments.md`
- **Signature:** `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` — must-pass: `payPalMockResponse`.
- **Returns:** `CapturedPayment` (fields as step 2). Use for the remaining-refundable computation.
- **Error:** `SdkException<PayPalServerSdk.Errors.GetCapturedPaymentError>` — Case A. `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].

#### Step 6 — `client.TransactionSearch.SearchTransactions` — `operations/TransactionSearch.md`
- **Signature:** `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass params:** `transactionId, transactionType, transactionStatus, transactionAmount, transactionCurrency, paymentInstrumentType, storeId, terminalId` (8 nullable, no default — pass `null` to skip). **Call with named arguments** (many optional params, easy to mis-bind positionally).
- **`startDate`/`endDate`** (wire `start_date`/`end_date`): required, **ISO-8601 with offset**, e.g. `"2026-08-01T00:00:00-0000"`. Window limited to ~31 days per call by PayPal; the range covers the prior three years.
- **Returns:** `SearchResponse` (`records-2`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Links (links): IReadOnlyList<LinkDescription>?`.
- **Paging the ENTIRE range:** there is no `perPage` cursor — page numerically. Read `TotalPages` from page 1, then loop `page = 1..TotalPages` (each call with `pageSize` up to 100, `page: n`), accumulating `TransactionDetails`. `TotalItems` is the total count for the whole range.
- **Fields that tie a transaction back to a local order** — `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?` (`records-2`):
  - `TransactionId (transaction_id): string?` — PayPal's transaction id.
  - `InvoiceId (invoice_id): string?` — matches the `invoice_id` you set on the purchase unit.
  - `CustomField (custom_field): string?` — matches the `custom_id` you set on the purchase unit.
  - `PaypalReferenceId (paypal_reference_id): string?` + `PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?` (values `Odr/Txn/Sub/Pap`) — links back to the order/txn.
  - `TransactionStatus (transaction_status): string?`, `TransactionAmount (transaction_amount): Money?`, `FeeAmount (fee_amount): Money?`.
  - **Note:** to receive these you must request the `transaction_info` field group — it is the `fields` default (`"transaction_info"`); keep it (or extend it) rather than passing `null`.
- **Error:** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — **Case B** (the only Case-B op in the SDK). Read via `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()` — **no typed `TryGet…` accessors here.** (You may deserialize the body with `ReadAsJson<PayPalServerSdk.Models.SearchError>()` — `SearchError` = `Name, Message, DebugId, Details[]`, records-2 — but that is opportunistic, not guaranteed.)
- Pagination: numeric `page` only (no `perPage`).

#### Step 7 — `client.Vault.CreatePaymentToken` — `operations/Vault.md`
- **Signature:** `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass param:** `payPalRequestId` (idempotency key). `body` required.
- **`body` `PaymentTokenRequest`** (`records-2`): `Customer (customer): Customer?` (`Customer` = `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?` — pass your shopper id in `MerchantCustomerId` to group tokens per customer), `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.
  - `PaymentTokenRequestPaymentSource` (`records-2`): `Card (card): PaymentTokenRequestCard?`, `Token (token): VaultTokenRequest?`.
  - **Vault a raw card:** set `.Card = PaymentTokenRequestCard` (`records-2`): `Name (name): string?`, `Number (number): string?` (`"4111111111111111"`), `Expiry (expiry): string?` (`"YYYY-MM"`), `SecurityCode (security_code): string?`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`.
- **Returns:** `PaymentTokenResponse` (`records-2`): `Id (id): string?` (**reusable vault token id → this is the `CardRequest.VaultId` for step 1**), `Customer (customer): CustomerResponse?`, `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?`, `Links`.
  - **Safe descriptor to show the shopper** (`PaymentTokenResponsePaymentSource.Card`, type `CardPaymentTokenEntity`, records-1): `LastDigits (last_digits): string?` (last4), `Brand (brand): CardBrand?`, `Expiry (expiry): string?`, `Name (name): string?`. Never store `Number`/`SecurityCode` — they are not returned.
- **Error:** `SdkException<PayPalServerSdk.Errors.CreatePaymentTokenError>` — **Case A**. Accessors: `TryGetError1(out PayPalServerSdk.Models.Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]. **Note the accessor is `TryGetError1` and the payload type is `Error1` (not `Error`)** — all Vault ops use `Error1`.
- Pagination: none.

#### Step 8 — pay with a saved card
No new operation: in step 1 set `CardRequest.VaultId = <PaymentTokenResponse.Id>` (on `OrderRequest.PaymentSource.Card` for option A, or on `OrderAuthorizeRequestPaymentSource.Card` for option B). Do **not** send `Number`/`Expiry`/`SecurityCode` when using a vaulted token.

#### Step 9 — `client.Vault.DeletePaymentToken` — `operations/Vault.md`
- **Signature:** `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` — only `id` required (the vault token id), no must-pass nullables, no body.
- **Returns:** `void` (Task).
- **Error:** `SdkException<PayPalServerSdk.Errors.DeletePaymentTokenError>` — **Case A**. Accessors: `TryGetError1(out PayPalServerSdk.Models.Error1)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback].
- Pagination: none.

### 2.3 Enum value tables (literal C# member ↔ wire value) — `map/models/enums.md`

Enums are `StringEnum<T>`, **not** C# enums. Build via the static member (`CheckoutPaymentIntent.Authorize`) or `CheckoutPaymentIntent.FromValue("AUTHORIZE")`. Compare on the member, never a bare string.

| Enum (`PayPalServerSdk.Models.Enums`) | Members (C# ↔ wire) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureIncompleteReason` | `BuyerComplaint, Chargeback, Echeck, InternationalWithdrawal, Other, PendingReview, ReceivingPreferenceMandatesManualAction, Refunded, TransactionApprovedAwaitingFunding, Unilateral, VerificationRequired, DeclinedByRiskFraudFilters` (wire = SCREAMING_SNAKE of each) |
| `RefundIncompleteReason` | `Echeck (ECHECK)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, `Jcb (JCB)`, `Diners (DINERS)`, `Elo (ELO)`, `Hiper (HIPER)`, `Hipercard (HIPERCARD)`, `Rupay (RUPAY)`, `ChinaUnionPay (CHINA_UNION_PAY)`, `Maestro (MAESTRO)`, … `Unknown (UNKNOWN)` (30 members) |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` |
| `CardVerificationStatus` | `Verified (VERIFIED)`, `Failed (FAILED)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` — **only** value; confirms `Token` is not a vaulted-card reference |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |

### 2.4 Client construction / auth / server (Step 0) — contract facts

**Client + DI** (`sdk-map.md` "Getting a client"; `ServiceCollectionExtensions.cs`):
- Constructor: `new PayPalServerSdk.PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)`.
- DI: `services.AddPayPalServerSdkClient(o => { … })` (extension on `IServiceCollection`, namespace `PayPalServerSdk`). Configure options in the callback.
- `PayPalServerSdkClientOptions` members: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `Oauth2: OAuth2ClientCredentials?`, `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.

**Environment (sandbox):** `options.Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox;` (the only defined environment; `ServerEnvironment.Default()` also returns `Sandbox`).

**Auth scheme = OAuth2 client-credentials.** Set:
```
options.Oauth2 = new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials
{
    ClientId = <PayPal:ClientId>,       // required
    ClientSecret = <PayPal:ClientSecret>, // required
    // Scope = null                      // optional
};
```
- The SDK acquires the token itself: the default strategy POSTs `grant_type=client_credentials` to `/v1/oauth2/token` using HTTP Basic auth built from `ClientId:ClientSecret`. Token acquisition/caching/refresh is handled by the built-in `Oauth2TokenStrategy` — **leave `Oauth2TokenStrategy` unset** (see base-URL note and the auth trap).

**Base URL override (config `PayPal:BaseUrl`, must apply to EVERY call incl. the token request):**
- Override member (verbatim): `options.Server.Default.Sandbox.BaseUrl` — i.e.
```
options.Server = new PayPalServerSdk.ServerOptions
{
    Default = new PayPalServerSdk.Servers.DefaultOptions
    {
        Sandbox = new PayPalServerSdk.Servers.DefaultOptions.SandboxOptions { BaseUrl = <PayPal:BaseUrl> }
    }
};
```
- Default value when unset: `https://api-m.sandbox.paypal.com`.
- **Confirmed against source:** the default OAuth token strategy resolves its token endpoint through the *same* server resolution as every API call (`server.Default("/v1/oauth2/token")` → `DefaultOptions.Resolve` → `Sandbox.BaseUrl`). Therefore setting `Server.Default.Sandbox.BaseUrl` **does** redirect the token/credential request too — **provided you do NOT supply a custom `Oauth2TokenStrategy`** (a custom strategy bypasses this resolution and would keep the default host). So: when `PayPal:BaseUrl` is configured, set `Server.Default.Sandbox.BaseUrl` and rely on the built-in token strategy.

**Error boundary — contract facts (mechanics ⇒ `dotnet-error-handling`):**
- SDK calls throw `PayPalServerSdk.Core.Exceptions.SdkException<TError>`; the type exposes **only** `.Error` (of type `TError`) plus the inherited `Exception.Message`. **There is no `StatusCode` on the exception itself.**
- **Case A** (39 of 40 ops, incl. every op above except SearchTransactions): `TError` is a `PayPalServerSdk.Errors.{Op}Error : ApiError`. Read the typed body via the op's `TryGetError(out Error)` / `TryGetError1(out Error1)` accessor (Orders/Payments use `Error`; Vault uses `Error1`; the accessor's status list is in each op row). For statuses not matched by the typed accessor, `TryGetRawError(out RawError)` (inherited from `ApiError`) gives `RawError.StatusCode` + `ReadAsString()`.
- **Case B** (`SearchTransactions` only): `TError` is `PayPalServerSdk.Core.ErrorResponse.RawError` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`.
- **Error-body fields** (typed payloads `Error`/`Error1`/`DefaultError`, all `PayPalServerSdk.Models`): `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails/ErrorDetails1>?`. `ErrorDetails`: `Field (field): string?`, `Value (value): string?`, `Issue (issue): string !req`, `Description (description): string?`, `Location (location): string? = "body"`. `Name`/`Details[].Issue` are how you map stale-auth / already-captured (409) / insufficient-refund (422) conditions.
- **Mapping stale-auth / already-captured / insufficient-refund by `Issue` string is UNVERIFIED:** the concrete `Name`/`Issue` code strings are produced by the live PayPal API and are **not** in the SDK map or source (the model types carry them only as free-form `string`). Directive: branch primarily on **HTTP status** (from `TryGetRawError`→`RawError.StatusCode`: 409 conflict ⇒ already-captured/void-of-captured; 422 ⇒ unprocessable incl. over-refund/expired-auth) and read `Name`/`Details[].Issue`/`Description` **best-effort** to enrich the operator message; **fall back to the generic `ex.Message`** when they are absent. Do not hard-fail on an expected issue string.

---

## 3. Trap notes (load the named skill before writing that step)

> ⚠ Step 0 (client + DI) — the `HttpClient`/handler pipeline that backs `PayPalServerSdkClient` must be long-lived and reused (via `IHttpClientFactory`), not rebuilt per request; the wrapper's own lifetime is a separate decision. **MUST load `dotnet-client-initialization`** before wiring the client.

> ⚠ Step 0 (auth) — where you set credentials relative to client construction, and how/whether the OAuth token is cached and refreshed across calls, is not visible in the options shape. **MUST load `dotnet-authentication`** before wiring `Oauth2` (and before assuming refresh behaviour).

> ⚠ Step 0 (base URL / resilience) — the SDK's `Retry`/`Timeout` options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; and which verbs actually retry (and whether a transport failure can re-send a non-idempotent POST) is not what the option names imply. This is why every write in this plan carries a `payPalRequestId` idempotency key. **MUST load `dotnet-configuration-resilience`** before tuning the client, base URL, retries, or pagination.

> ⚠ Steps 1–9 (building requests) — `payment_source` unions are objects with per-instrument sub-records, enums are `StringEnum<T>` (not C# enums), and any JSON field you don't model is dropped on deserialize; amounts are decimal **strings**. **MUST load `dotnet-models`** before constructing payloads or mapping responses to domain types.

> ⚠ Steps 1–3 (first calls) — list/search ops (`SearchTransactions`) and the many must-pass nullable params mis-bind in positional calls; call with named arguments and use `ct:` for cancellation. **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 1–9 (error boundary) — Case A vs Case B differ per op, `TryGetError` vs `TryGetError1` differ per controller, and `TryGetRawError` is a fallback, not a catch-all on typed errors. **MUST load `dotnet-error-handling`** before writing the try/catch (see the two mandatory `JsonException` rows below).

> ⚠ Tests — the `HttpClient` constructor argument is the test seam; match eShopOnWeb's existing test framework/assertion style. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING (load BEFORE implementation starts)

These `dotnet-*` companion skills are the usage layer; this sheet deliberately does **not** carry their
contents (defaults, worked examples, and the parts a one-line note cannot convey). Load each before the
step it governs:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, `HttpClient` lifetime, `AddPayPalServerSdkClient` DI |
| `dotnet-authentication` | Step 0 — setting `Oauth2` credentials, token acquisition/refresh timing |
| `dotnet-configuration-resilience` | Step 0 — retries/timeouts semantics, `PayPal:BaseUrl` override, pagination tuning |
| `dotnet-calling-endpoints` | Steps 1–9 — named-argument calls, must-pass params, async/cancellation (`ct:`) |
| `dotnet-models` | Steps 1–9 — building request models, `StringEnum<T>`, unions, wire-name mapping |
| `dotnet-error-handling` | Steps 1–9 — the error boundary; Case A/B, `TryGet…` accessors, status reading |
| `dotnet-testing` | Test phase — faking the `HttpClient` seam |

**Two mandatory `System.Text.Json.JsonException` hazard rows** — this exception reaches the boundary from
two directions and needs opposite handling:

- A drifted or malformed **2xx** body (e.g. a missing `required` member) surfaces as a `JsonException`
  from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it
  escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to
  a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries
  something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- Plan file written to the exact path dictated by the brief: `C:\claude-runs\t3v7ali-task3-plugin-opus48high-033\repo\paypal-plan.md`.
- Currency (`PayPal:Currency`, USD here), `PayPal:ClientId`, `PayPal:ClientSecret`, and optional `PayPal:BaseUrl` come from eShopOnWeb configuration (`IConfiguration`/options), not hardcoded. Config key names are a suggestion; adjust to the app's convention.
- "Amount to the cent" ⇒ decimal-string `Value` with exactly 2 fraction digits for USD (e.g. `"49.99"`); the SDK does not format this for you.
- Direct-card, server-side only: sandbox Visa `4111 1111 1111 1111`. Any `PAYER_ACTION_REQUIRED` / `payer-action` / `approve` challenge is treated as the STOP-and-report condition, per brief.
- Sandbox business account is enabled for direct card processing and card vaulting (per brief) — required for `CardRequest.Number` and `Vault.CreatePaymentToken` to be accepted.

**Blockers / UNVERIFIED (cannot be settled from map or SDK source — live sandbox traffic only)**
- The exact `Name` / `Details[].Issue` code strings that distinguish stale-but-renewable vs
  permanently-un-renewable authorizations (step 3), already-captured/void-of-captured (409), and
  over-refund (422). The SDK models carry these only as free-form `string`. Handle per the defensive
  directives in §2.2 (step 3) and §2.4 (error boundary): branch on HTTP status + your own
  `ExpirationTime`/refunded-total computations, read issue strings best-effort, fall back to the generic
  message. **UNVERIFIED.**
- Whether the live wire actually populates every optional field this plan reads (`SellerReceivableBreakdown.PaypalFee`/`NetAmount`, `TransactionInformation.CustomField`/`InvoiceId`) — these are nullable in the model; extract best-effort and tolerate absence. **UNVERIFIED.**
- No SDK operation exists to *list refunds for a capture* directly; remaining-refundable must be tracked by the integration (from `GetCapturedPayment` gross + locally recorded refunds / `Refund.SellerPayableBreakdown.TotalRefundedAmount`). Not a blocker — a design constraint noted so it is not assumed to be an SDK call.
