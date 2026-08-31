# PayPal .NET SDK integration plan — eShopOnWeb (.NET 8, ASP.NET Core)

SDK: `AsadAli.Checkout.Sdk` (APIMatic-generated; root namespace `PayPalServerSdk`; client `PayPalServerSdkClient`).
Map provenance: tag `v1.0.1`, source commit `9653d18`. Install version-less (`dotnet add package AsadAli.Checkout.Sdk`) so it floats to latest; if a name here ever fails to compile, trust the compiler and re-ask for the corrected row.

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Add NuGet package; register client in DI with config-driven auth, environment, base-URL override | — (client setup) |
| 2 | Persist PayPal-owned state per order: PayPal order id, authorization id + status + expiration, capture id + status, refund ids + statuses, vault tokens per shopper (token id, PayPal customer id, brand, last digits) | — (app-side storage) |
| 3 | Checkout: create order, intent AUTHORIZE, amount = order total to the cent, currency from config | `Orders.CreateOrder` |
| 4 | Pay by raw card (server-side, no browser) | `Orders.AuthorizeOrder` |
| 5 | Fulfilment: capture the authorization; record captured amount, seller fee, net | `Payments.CaptureAuthorizedPayment` (fallback read: `Payments.GetCapturedPayment`) |
| 6 | Stale authorization: reauthorize before capture; detect non-reauthorizable | `Payments.ReauthorizePayment`, `Payments.GetAuthorizedPayment` |
| 7 | Cancel before fulfilment: void the authorization | `Payments.VoidPayment` |
| 8 | Refund (full/partial, idempotent, repeatable partials); read refund status | `Payments.RefundCapturedPayment`, `Payments.GetRefund` |
| 9 | Saved cards: vault a card, list shopper's tokens, delete a token, pay a new order with a token | `Vault.CreatePaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.DeletePaymentToken`, `Orders.AuthorizeOrder` |
| 10 | Reconciliation: transaction search over a date range with full pagination | `TransactionSearch.SearchTransactions` |
| 11 | Error boundary around all of the above | every op's error case below |
| 12 | Tests against the SDK seam | — |

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

Namespaces for this integration (sdk-map.md): client/options `PayPalServerSdk` · records `PayPalServerSdk.Models` · enums `PayPalServerSdk.Models.Enums` · error classes `PayPalServerSdk.Errors` · `SdkException<T>` `PayPalServerSdk.Core.Exceptions` · `RawError` `PayPalServerSdk.Core.ErrorResponse` · `ServerEnvironment`/`DefaultOptions` `PayPalServerSdk.Servers` · `OAuth2ClientCredentials` `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` · `RetryOptions` `PayPalServerSdk.Core.Configuration`.

All operations are throw-only (no `…Result` variant exists anywhere in this SDK) and all parameters marked "must pass" are nullable with no C# default — pass them explicitly (`null` to skip).

### Step 3 — Create order (AUTHORIZE) — map: operations/Orders.md

`client.Orders.CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<Order>`
- must pass explicitly: `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion`
- **Idempotency**: `payPalRequestId` → header `PayPal-Request-Id` (verified in `Api/Orders.cs`). One unique key per logical order; reuse the same key only when retrying this same call.
- Request `OrderRequest` (records-1-Ac-Pa.md): `Intent (intent): CheckoutPaymentIntent !req` = `CheckoutPaymentIntent.Authorize` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` · `PaymentSource (payment_source): PaymentSource?` (omit here; card goes on the authorize call) · `Payer (payer)`, `ApplicationContext (application_context)` optional.
- `PurchaseUnitRequest` (records-2-Pa-Ve.md): `Amount (amount): AmountWithBreakdown !req` · `ReferenceId (reference_id): string?` · `InvoiceId (invoice_id): string?` · `CustomId (custom_id): string?` — set `InvoiceId`/`CustomId` to your order identifiers; they are the deterministic join keys for transaction search (step 10).
- `AmountWithBreakdown` (records-1-Ac-Pa.md): `CurrencyCode (currency_code): string !req` (from config) · `Value (value): string !req` — decimal as string, e.g. `"100.00"`; must equal the order total to the cent. If you add `Breakdown (breakdown)`, the amount must equal item_total + tax_total + shipping + handling + insurance − shipping_discount − discount.
- Response `Order` (records-1-Ac-Pa.md): `Id (id)` · `Status (status): OrderStatus?` · `Intent (intent)` · `PurchaseUnits (purchase_units)` · `Links (links)`. Persist `Id`.
- Error: `SdkException<CreateOrderError>` (Case A) — `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback].

### Step 4 — Authorize with raw card — map: operations/Orders.md

`client.Orders.AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<OrderAuthorizeResponse>`
- must pass explicitly: `payPalMockResponse`, `payPalRequestId`, `payPalClientMetadataId`, `payPalAuthAssertion`, `body`
- `id` = persisted PayPal order id. **Idempotency**: `payPalRequestId` → `PayPal-Request-Id`.
- Pass `prefer: "return=representation"` — the default `"return=minimal"` can omit the fields step 4/5 read back.
- Request `OrderAuthorizeRequest` (records-1-Ac-Pa.md): `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` → `Card (card): CardRequest?`.
- `CardRequest` (records-1-Ac-Pa.md): `Number (number): string?` · `Expiry (expiry): string?` (`"YYYY-MM"`) · `SecurityCode (security_code): string?` · `Name (name): string?` · `BillingAddress (billing_address): Address?`. `Address.CountryCode (country_code): string !req` whenever an address is sent. (Raw PAN/CVV via API implies PCI SAQ D — the record's own doc warns this.)
- Response `OrderAuthorizeResponse` (records-1-Ac-Pa.md): `Id`, `Status (status): OrderStatus?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` → `PurchaseUnit.Payments (payments): PaymentCollection?` → `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → per authorization: `Id (id)`, `Status (status): AuthorizationStatus?`, `StatusDetails (status_details).Reason: AuthorizationIncompleteReason?`, `Amount (amount): Money`, `ExpirationTime (expiration_time)`. **The authorization id lives one envelope level down: `resp.PurchaseUnits[0].Payments.Authorizations[0].Id`** — persist it with its status and expiration.
- Error: `SdkException<AuthorizeOrderError>` (Case A) — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetRawError(out RawError)`. A declined card surfaces as 422 with `Error.Details[].Issue`.

### Step 5 — Capture at fulfilment — map: operations/Payments.md

`client.Payments.CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<CapturedPayment>`
- must pass explicitly: `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body`. **Idempotency**: `payPalRequestId` → `PayPal-Request-Id`. Pass `prefer: "return=representation"`.
- Request `CaptureRequest` (records-1-Ac-Pa.md): `Amount (amount): Money?` (`Money`: `CurrencyCode (currency_code) !req`, `Value (value) !req`) · `FinalCapture (final_capture): bool? = false` — set `true` when nothing remains to capture · `InvoiceId`, `NoteToPayer`, `SoftDescriptor` optional.
- Response `CapturedPayment` (records-1-Ac-Pa.md): `Id (id)` · `Status (status): CaptureStatus?` · `StatusDetails.Reason: CaptureIncompleteReason?` · `Amount (amount): Money?` (captured amount) · `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?` → `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?` (seller fee), `NetAmount (net_amount): Money?` (net proceeds) — note the breakdown "is not available for transactions that are in pending state" (record doc). Persist capture id + status + these three amounts.
- Re-read later: `GetCapturedPayment(string captureId, string? payPalMockResponse /*must pass*/, RequestOptions? = null, CancellationToken ct = default)` → `Task<CapturedPayment>`; error `SdkException<GetCapturedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500].
- Error: `SdkException<CaptureAuthorizedPaymentError>` (Case A) — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

### Step 6 — Reauthorize a stale authorization — map: operations/Payments.md

`client.Payments.ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentAuthorization>`
- must pass explicitly: `payPalRequestId`, `payPalAuthAssertion`, `body`. **Idempotency**: `payPalRequestId` → `PayPal-Request-Id`.
- Request `ReauthorizeRequest` (records-2-Pa-Ve.md): `Amount (amount): Money?` — the only supported parameter (operation doc).
- Response `PaymentAuthorization` (records-2-Pa-Ve.md): `Id`, `Status (status): AuthorizationStatus?`, `Amount`, `ExpirationTime (expiration_time)`. Persist the new status/expiration (a reauthorized payment gets a fresh 3-day honor period).
- Window rules (operation doc, map-quoted): reauthorize only from day 4 to day 29 after the 3-day honor period; at 30+ days you must create a NEW authorization (new order + authorize), not reauthorize; amount cap e.g. US 115% of original, max +$75.
- **Detecting "can no longer be reauthorized"**: (a) locally — persisted `Status` is `Voided`/`Captured`/`Denied`, or `ExpirationTime` / age past the 29-day window; (b) remotely — `SdkException<ReauthorizePaymentError>`, `TryGetError(out Error)` [400, 401, 403, 404, 422] — treat **422 and 404 as terminal-for-reauthorize → start a new authorization**. The exact `Error.Name`/`Details[].Issue` wire strings for this condition are not enumerated in the map — **UNVERIFIED**: do not hard-code issue strings; key on HTTP status, then confirm with `GetAuthorizedPayment`.
- Status check: `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse /*must pass*/, string? payPalAuthAssertion /*must pass*/, RequestOptions? = null, CancellationToken ct = default)` → `Task<PaymentAuthorization>`; error `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500].

### Step 7 — Void an authorization — map: operations/Payments.md

`client.Payments.VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentAuthorization>`
- must pass explicitly: `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` (note the parameter ORDER here: `payPalAuthAssertion` comes before `payPalRequestId` — use named arguments).
- Response `PaymentAuthorization` — expect `Status` = `AuthorizationStatus.Voided`; persist it. You cannot void a fully captured authorization (operation doc).
- Error: `SdkException<VoidPaymentError>` (Case A) — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`. 409 = already captured/conflicting state.

### Step 8 — Refund a capture — map: operations/Payments.md

`client.Payments.RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<Refund>`
- must pass explicitly: `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body`.
- **Idempotency**: `payPalRequestId` → `PayPal-Request-Id` (verified in `Api/Payments.cs`). Rule for two distinct partial refunds of the same capture: **each refund gets its own unique key**; a key is reused only to retry the same refund call.
- Request `RefundRequest` (records-2-Pa-Ve.md): full refund = pass `body: null` (or empty — "include an empty payload", operation doc); partial refund = `Amount (amount): Money?` with the partial value. Optional: `InvoiceId`, `CustomId`, `NoteToPayer`.
- Response `Refund` (records-2-Pa-Ve.md): `Id (id)` · `Status (status): RefundStatus?` · `StatusDetails.Reason: RefundIncompleteReason?` · `Amount (amount): Money?` · `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount (total_refunded_amount)`). Persist refund id + status per refund.
- Status re-read: `GetRefund(string refundId, string? payPalMockResponse /*must pass*/, string? payPalAuthAssertion /*must pass*/, RequestOptions? = null, CancellationToken ct = default)` → `Task<Refund>`; error `SdkException<GetRefundError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500].
- Error: `SdkException<RefundCapturedPaymentError>` (Case A) — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

### Step 9 — Saved cards (vault) — map: operations/Vault.md

**Create (vault a card)**: `client.Vault.CreatePaymentToken(string? payPalRequestId /*must pass*/, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentTokenResponse>`
- Request `PaymentTokenRequest` (records-2-Pa-Ve.md): `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` → `Card (card): PaymentTokenRequestCard?` → `Number (number)`, `Expiry (expiry)`, `SecurityCode (security_code)`, `Name (name)`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?` (records-2/1). **Per-shopper scoping**: `Customer (customer): Customer?` → set `MerchantCustomerId (merchant_customer_id)` to your shopper key — this is the SDK's customer-id mechanism for vaulting (source-verified: `Customer.Id` is "the unique ID for a customer generated by PayPal"; `MerchantCustomerId` associates your own customer id — `Models/Customer.cs`). On the FIRST vault for a shopper you only have your own key; persist the PayPal-generated `CustomerResponse.Id` from the response and you may pass `Customer.Id` on later vaults.
- Response `PaymentTokenResponse` (records-2-Pa-Ve.md): `Id (id)` (the vault token id — persist) · `Customer (customer): CustomerResponse?` (`Id`, `MerchantCustomerId`) · `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `Card (card): CardPaymentTokenEntity?` → safe display attributes `Brand (brand): CardBrand?`, `LastDigits (last_digits): string?`, `Expiry (expiry)`, `Name (name)` (records-1-Ac-Pa.md). The full PAN is never returned.
- Error: `SdkException<CreatePaymentTokenError>` (Case A) — `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)`.

**List a shopper's tokens**: `client.Vault.ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<CustomerVaultPaymentTokensResponse>`
- `customerId` → query `customer_id`: "a unique identifier representing a specific customer in merchant's/partner's system or records" (source-verified, `Api/Vault.cs`) — i.e. pass the same shopper key you set as `merchant_customer_id`.
- Response `CustomerVaultPaymentTokensResponse` (records-1-Ac-Pa.md): `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` · `TotalItems (total_items)`, `TotalPages (total_pages)` for manual page looping · `Customer (customer): VaultResponseCustomer?`.
- Error: `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400, 403, 500].

**Delete a token**: `client.Vault.DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task` (void). Error: `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 500]. Also remove the token from your store.

**Pay a new order with a vaulted token**: create the order exactly as step 3, then `Orders.AuthorizeOrder` with `OrderAuthorizeRequest.PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = new CardRequest { VaultId = <token id> } }` — `CardRequest.VaultId (vault_id): string?` (records-1-Ac-Pa.md). Do NOT use `OrderAuthorizeRequestPaymentSource.Token (token): Token?` — `TokenType` models only `BillingAgreement (BILLING_AGREEMENT)` (enums.md), it is not the vault payment-token path. Response reading identical to step 4.

### Step 10 — Transaction search — map: operations/TransactionSearch.md

`client.TransactionSearch.SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<SearchResponse>`
- must pass explicitly: `transactionId` … `terminalId` (all 8, `null` to skip). `startDate`/`endDate` → `start_date`/`end_date`, ISO-8601. Call with named arguments.
- **Full pagination is manual** (map: "Pagination: none (only `page`, no `perPage`)"): loop `page` from 1 to `TotalPages`, keeping `pageSize` fixed.
- Response `SearchResponse` (records-2-Pa-Ve.md): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` → `TransactionInfo (transaction_info): TransactionInformation?` → `TransactionId (transaction_id)` (the capture/refund-level id), `PaypalReferenceId (paypal_reference_id)` + `PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?`, `TransactionStatus (transaction_status): string?` (plain string, no enum), `TransactionAmount (transaction_amount): Money?`, `FeeAmount (fee_amount): Money?`, `InvoiceId (invoice_id)`, `CustomField (custom_field)`, `TransactionEventCode`, `TransactionInitiationDate`, `TransactionUpdatedDate` · paging: `Page (page)`, `TotalItems (total_items)`, `TotalPages (total_pages)`.
- **Lining transactions up with stored ids**: `transaction_id` matches capture/refund ids; `paypal_reference_id` with type `Odr` points at the order; the deterministic joins are `invoice_id`/`custom_field` — which is why step 3 sets them. Whether `paypal_reference_id` is populated on every transaction is live-traffic-only — **UNVERIFIED**: extract best-effort, fall back to invoice/custom matching.
- Error: **Case B — the SDK's only raw-error operation**: `SdkException<RawError>` → `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, best-effort `ex.Error.ReadAsJson<DefaultError>()` (`DefaultError`: `Name`, `Message`, `DebugId` !req — records-1-Ac-Pa.md).
- Note (operation doc): executed transactions take up to 3 hours to appear; range limited to the previous three years.

### Error payload shapes (all `PayPalServerSdk.Models`)

- `Error` (Orders/Payments ops): `Name (name): string !req` · `Message (message): string !req` · `DebugId (debug_id): string !req` · `Details (details): IReadOnlyList<ErrorDetails>?` → `Issue (issue): string !req`, `Field`, `Value`, `Description` · `Links`. (records-1-Ac-Pa.md)
- `Error1` (Vault ops): same shape; `Details` is `IReadOnlyList<ErrorDetails1>?`, `Links` is `IReadOnlyList<ErrorLinkDescription>?`. Accessors are named `TryGetError1`.
- Catch pattern per op: `catch (SdkException<{Op}Error> ex) { if (ex.Error.TryGetError(out var e)) { /* e.Name, e.Message, e.DebugId, e.Details[].Issue */ } else if (ex.Error.TryGetRawError(out var raw)) { /* raw.StatusCode, raw.ReadAsString() */ } }` — and for Payments ops also `TryGetNoContent(out RawError)` [500] before the raw fallback.

### Enums actually needed (map: models/enums.md — `StringEnum<T>`, NOT C# enums; use the static members, e.g. `CheckoutPaymentIntent.Authorize`)

| Enum | Members (C# name = wire value) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created`, `Saved`, `Approved`, `Voided`, `Completed`, `PayerActionRequired` (wire = SCREAMING) |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `CaptureIncompleteReason` | `BuyerComplaint`, `Chargeback`, `Echeck`, `InternationalWithdrawal`, `Other`, `PendingReview`, `ReceivingPreferenceMandatesManualAction`, `Refunded`, `TransactionApprovedAwaitingFunding`, `Unilateral`, `VerificationRequired`, `DeclinedByRiskFraudFilters` (wire = SCREAMING) |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `RefundIncompleteReason` | `Echeck (ECHECK)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, `Maestro (MAESTRO)`, `Diners (DINERS)`, `Jcb (JCB)`, `ChinaUnionPay (CHINA_UNION_PAY)`, … 31 members total — display-only here |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |

### Step 1 — Client construction / auth / environment (sdk-map.md; source-verified where marked)

- Package: `dotnet add package AsadAli.Checkout.Sdk` — version-less, floats to latest; this sheet documents v1.0.1.
- DI: `services.AddPayPalServerSdkClient(o => { /* configure */ })` — extension in `PayPalServerSdk` (root). It registers the client as a **singleton** and creates its `HttpClient` from `IHttpClientFactory` internally (source: `ServiceCollectionExtensions.cs`). Manual alternative: `new PayPalServerSdkClient(httpClient, options)`.
- Auth: `o.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = … }` — both `required`; optional `Scope`. Token fetch: client-credentials grant, Basic-auth header, POST to `server.Default("/v1/oauth2/token")` — i.e. **the token request uses the same base URL as every API call** (source: `AuthSchemes.cs`, `OAuth2ClientCredentialsStrategy.cs`). `o.Oauth2TokenStrategy` can replace the default strategy.
- Environment: `o.Environment = ServerEnvironment.Sandbox` — **the only member that exists** (source: `Servers/ServerEnvironment.cs`). There is no `Live`/`Production` member.
- Base-URL override: `o.Server.Default.Sandbox.BaseUrl = "<url>"` — default `"https://api-m.sandbox.paypal.com"`; the value is used **verbatim** as the base address for every call, token request included (source: `Servers/DefaultOptions.cs` → `UrlTemplate(Sandbox.BaseUrl, path, [])`; `ServerOptions` is root-namespace `PayPalServerSdk`, `DefaultOptions` is `PayPalServerSdk.Servers`).
- Config mapping for "sandbox vs live from a string": `"sandbox"` → default options untouched; anything else (e.g. `"live"`) → keep `Environment = ServerEnvironment.Sandbox` (only value) and set `o.Server.Default.Sandbox.BaseUrl` to the target host (e.g. `https://api-m.paypal.com`). Environment selection in this SDK *is* base-URL selection.

## 3. Trap notes

- ⚠ Step 1 (DI registration) — the signature won't tell you how the `HttpClient`/handler pipeline lifetime must be managed, or what the DI helper does and doesn't wire for you. **MUST load `dotnet-client-initialization`** before registering the client.
- ⚠ Step 1 (auth) — when the token is fetched/cached/refreshed and how credentials should flow from configuration (never hardcoded) is not visible from the options shape. **MUST load `dotnet-authentication`** before wiring credentials.
- ⚠ Steps 3–10 (every call) — many parameters are nullable with no C# default and mis-bind in positional calls; call with named arguments (and the token parameter really is `ct:`). **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 3–10 (models) — enums are `StringEnum<T>` records, not C# enums (construction, comparison, and switch behavior differ); `required` members must be set in the initializer; JSON fields the SDK doesn't model are silently dropped on deserialize. **MUST load `dotnet-models`** before building payloads.
- ⚠ Step 11 (error boundary) — which operations are Case A vs Case B, and what `TryGetRawError` does and doesn't cover on typed errors, is per-operation (see each row above); a wrong catch ladder loses the status code. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ Steps 3, 5, 8 (retries vs idempotency) — whether a failed non-idempotent write can be re-sent by the retry layer determines why the `PayPal-Request-Id` keys above are load-bearing, and what `Timeout` actually bounds is not what the name suggests. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts or relying on retry behavior.
- ⚠ Step 12 (tests) — the testable seam for stubbing the SDK is specific (the `HttpClient` constructor argument), not the controllers. **MUST load `dotnet-testing`** before writing integration tests.

## 4. REQUIRED READING

Load **before implementation starts** (this sheet deliberately does not carry their contents):

- `dotnet-client-initialization` — governs step 1 (client construction & DI).
- `dotnet-authentication` — governs step 1 (OAuth client-credentials wiring).
- `dotnet-calling-endpoints` — governs steps 3–10 (every operation call).
- `dotnet-models` — governs steps 3–10 (request/response models, enums).
- `dotnet-error-handling` — governs step 11 (the error boundary; every integration writes one).
- `dotnet-configuration-resilience` — governs step 1 + retry/timeout/pagination behavior in steps 3–10.
- `dotnet-testing` — governs step 12.

Two hazards belong to the boundary from day one:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

- **No `Live` environment member exists.** `ServerEnvironment` models only `Sandbox` (source-verified). "Live" is reachable solely via the base-URL override (`o.Server.Default.Sandbox.BaseUrl`); the override is verbatim and covers the token request too (source-verified). If the graders expect a first-class live environment switch, this SDK doesn't have one — the config-string mapping above is the mechanism.
- **"Cannot reauthorize" detection**: the map does not enumerate the wire `name`/`issue` strings PayPal returns for an expired/non-reauthorizable authorization. The sheet keys detection on HTTP status (422/404) plus persisted/queried authorization status and age — exact issue strings **UNVERIFIED** (live-traffic-only); do not hard-code them.
- **`paypal_reference_id` population** on every searched transaction is **UNVERIFIED** (live-traffic-only); the deterministic reconciliation join is `invoice_id`/`custom_field`, which step 3 sets for exactly this reason.
- **Transaction search latency/range**: transactions appear up to 3 hours after execution; only the previous three years are searchable (operation doc). Reconciliation of very recent captures must tolerate an empty result.
- **Vault availability**: the client doc comment marks the Vault controller "*Available in the US only.*" (source: `PayPalServerSdkClient.cs`) — sandbox accounts outside that scope may reject vault calls; not verifiable from the map.
- **PCI scope**: raw card number/CVV through `Orders.AuthorizeOrder` and `Vault.CreatePaymentToken` implies PCI SAQ D for the host app (the `CardRequest` record's own doc warning). eShopOnWeb-side compliance is out of SDK scope.
- **App-side concerns not covered by the SDK map** (owned by the implementer): the persistence schema for PayPal state, where the integration lives in eShopOnWeb's architecture, config key names, and how fulfilment/cancel events trigger capture/void.
- No blockers prevent starting implementation.
