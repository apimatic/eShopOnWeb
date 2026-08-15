# PayPal .NET SDK — CONTRACT SHEET (eShopOnWeb, SANDBOX, direct-card + vault)

SDK: `AsadAli.Checkout.Sdk` · root namespace `PayPalServerSdk` · client `PayPalServerSdkClient` ·
map release `v1.0.1` (source stamp `9653d18`). Every fact below is grounded in the bundled SDK map
(`sdk-map.md`, `map/operations/*.md`, `map/models/*.md`) or, where the map is silent, in the named SDK
source file. This file is a contract reference only — it carries **no secret values**; client id/secret
come from configuration at runtime.

---

## 1. Scope & sequence

1. **Client + DI + auth + base-URL override** — one long-lived client, OAuth2 client-credentials, optional
   `PayPal:BaseUrl` override applied to every call incl. the token request.
2. **Order create (intent=AUTHORIZE)** — direct card (`CreateOrder`) → **`AuthorizeOrder`** to obtain the
   authorization id. (Vaulted-card variant: same two calls, card referenced by `vault_id`.)
3. **Fulfilment / lifecycle** — `CaptureAuthorizedPayment`, `ReauthorizePayment`, `VoidPayment`,
   `RefundCapturedPayment`, plus `GetAuthorizedPayment` / `GetCapturedPayment` reads.
4. **Vault** — `CreatePaymentToken` (direct-from-card, no browser), `ListCustomerPaymentTokens`,
   `GetPaymentToken`, `DeletePaymentToken`.
5. **Reconciliation** — `SearchTransactions`, paginated across the whole date range.

---

## 2. Client construction, auth, base-URL override  (source-grounded — map does not carry these member names)

**Using-directives (each type from its own namespace — do NOT collapse to `.Models`):**

| Type(s) | Namespace |
|---|---|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` | `PayPalServerSdk` |
| `AddPayPalServerSdkClient` (DI extension) | `PayPalServerSdk` (`ServiceCollectionExtensions.cs`) |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `ServerEnvironment`, `DefaultOptions` (+ nested `DefaultOptions.SandboxOptions`) | `PayPalServerSdk.Servers` |
| `RetryOptions`, `LoggingOptions` | `PayPalServerSdk.Core.Configuration` |
| Controllers (`client.Orders` etc. are properties; controller types) | `PayPalServerSdk.Api` |
| Records (request/response models) | `PayPalServerSdk.Models` |
| Enums | `PayPalServerSdk.Models.Enums` |
| Typed error classes (`CreateOrderError`, …) | `PayPalServerSdk.Errors` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError` | `PayPalServerSdk.Core.ErrorResponse` |

**Options shape** (`PayPalServerSdkClientOptions.cs`): `Environment: ServerEnvironment` (defaults to
`ServerEnvironment.Sandbox` — Sandbox is the only member; `Servers/ServerEnvironment.cs`), `Retry:
RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `Oauth2: OAuth2ClientCredentials?`,
`Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.

**Auth — OAuth2 client-credentials** (`OAuth2ClientCredentials.cs`): set `options.Oauth2 = new
OAuth2ClientCredentials { ClientId = <cfg>, ClientSecret = <cfg> }`. Members: `required string ClientId`
(init), `required string ClientSecret` (init), `string? Scope` (init, optional). Leave
`Oauth2TokenStrategy` null — the SDK supplies the default client-credentials strategy.

**Base-URL override — the one knob, and it also governs the token request.** There is exactly one
resolvable base URL: `options.Server.Default.Sandbox.BaseUrl` (`ServerOptions.Default: DefaultOptions` →
`DefaultOptions.Sandbox: SandboxOptions` → `SandboxOptions.BaseUrl`, default
`https://api-m.sandbox.paypal.com`; `ServerOptions.cs`, `Servers/DefaultOptions.cs`). To honor
`PayPal:BaseUrl` verbatim for ALL calls, set `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>`.
**This reaches the OAuth2 token request too:** the token endpoint is built as
`server.Default("/v1/oauth2/token")`, which resolves through the same `DefaultOptions.Resolve →
Sandbox.BaseUrl` (`AuthSchemes.cs` line 17, `Servers/DefaultOptions.cs`) — so overriding `BaseUrl`
redirects both the API calls and the token fetch to the custom host. No separate token-host setting exists.

**HttpClient / DI** (`ServiceCollectionExtensions.cs`): `services.AddPayPalServerSdkClient(o => { … })`
calls `services.AddHttpClient()` and registers `PayPalServerSdkClient` as a **singleton** built from an
`IHttpClientFactory`-created `HttpClient`. Constructor for manual use:
`new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`. HttpClient
lifetime/reuse is a companion-skill hazard — see Trap notes.

**Idempotency key (`PayPal-Request-Id`)** — passed as the `payPalRequestId` string parameter on each write
op that supports it (`CreateOrder`, `AuthorizeOrder`, `CaptureAuthorizedPayment`, `ReauthorizePayment`,
`VoidPayment`, `RefundCapturedPayment`, `CreatePaymentToken`, `CreateSetupToken`). Reusing the same value
de-dupes the request server-side. Pass your own key; pass `null` to skip.

**`prefer` header (representation vs minimal)** — every order/payment write defaults `prefer =
"return=minimal"`, whose body carries only top-level `id`/`status`/`links`. To read the nested fields this
integration needs (authorization id under `purchase_units → payments → authorizations`, capture
`seller_receivable_breakdown`, etc.) pass **`prefer: "return=representation"`**. Exactly which nested
fields each Prefer value populates is live-wire behavior — treat as **UNVERIFIED**: request
`return=representation`, and code defensively (null-check every nested list/field before indexing).

---

## 3. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The
> cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from
> that type's own map row, never from where a neighbouring type sits. Enums live in
> `PayPalServerSdk.Models.Enums`, records in `PayPalServerSdk.Models`, typed errors in
> `PayPalServerSdk.Errors`, `SdkException<T>` in `PayPalServerSdk.Core.Exceptions`, `RawError` in
> `PayPalServerSdk.Core.ErrorResponse`, auth/server/client-config types in the child namespaces in §2.
> Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using` and the build
> breaks.

**Nullable-but-no-default parameters must be passed explicitly** (pass `null` to skip) — they are the
leading `payPal*`/`body` params on most ops. Below, `…` in a signature = those explicit-null header params.

### Orders — `client.Orders` (`operations/Orders.md`; models `records-1-Ac-Pa.md` unless noted)

| # | Op — full signature | Request model + fields (`C# (wire): type, req?`) | Response + accessor path | Error (Case A) accessors | Page |
|---|---|---|---|---|---|
| 2 | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req` (=`Authorize`), `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `Payer?`, `ApplicationContext (application_context): OrderApplicationContext?` | `Order`: `Id (id)`, `Status (status): OrderStatus`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>` | `SdkException<CreateOrderError>` → `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` | Orders |
| 3 | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` (optional — supply card here **or** at CreateOrder) | `OrderAuthorizeResponse`: `Id`, `Status: OrderStatus`, `PurchaseUnits[].Payments (payments): PaymentCollection` → `.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>` → `[i].Id (id)`, `[i].Status (status): AuthorizationStatus` | `SdkException<AuthorizeOrderError>` → `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` | Orders |
| 9 | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (query `fields` ← `fields`) | `Order` (same shape as #2; read `Status`, nested `Payments`) | `SdkException<GetOrderError>` → `TryGetError(out Error)` [401,404] · `TryGetRawError` | Orders |

- **`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`): `ReferenceId (reference_id): string?`, `Amount
  (amount): AmountWithBreakdown !req`, `Description?`, `CustomId (custom_id): string?`, `InvoiceId
  (invoice_id): string?`, `Items (items): IReadOnlyList<ItemRequest>?`, `Payee?`, `Shipping?`. Use
  `CustomId`/`InvoiceId` to correlate to your eShop order for reconciliation (§14).
- **`AmountWithBreakdown`** (`records-1`): `CurrencyCode (currency_code): string !req`, `Value (value):
  string !req` (both **strings**; `Value` is a decimal-as-string, e.g. `"12.99"` — format to the currency's
  minor units yourself), `Breakdown (breakdown): AmountBreakdown?`. `Money` (fees/refund amounts) has the
  identical `CurrencyCode`/`Value` string pair.
- **Direct card `PaymentSource`** (`records-2`): `PaymentSource.Card (card): CardRequest?`. **`CardRequest`**
  (`records-1`): `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?` (wire
  `expiry`, `YYYY-MM`), `SecurityCode (security_code): string?` (CVV), `BillingAddress (billing_address):
  Address?`, `VaultId (vault_id): string?`, `Attributes (attributes): CardAttributes?`. **`Address`**
  (`records-1`): `AddressLine1 (address_line_1)`, `AddressLine2 (address_line_2)`, `AdminArea2
  (admin_area_2)` (city), `AdminArea1 (admin_area_1)` (state), `PostalCode (postal_code)`, `CountryCode
  (country_code): string !req`.
- **(#4) Vaulted-card `PaymentSource`** — reference a saved vault token by setting **`payment_source.card.
  vault_id`** (`CardRequest.VaultId (vault_id): string?`) to the `PaymentTokenResponse.Id` from §10,
  leaving `Number`/`Expiry`/`SecurityCode` unset. Same `CreateOrder`→`AuthorizeOrder` sequence.
  (`CardRequest.SingleUseToken (single_use_token)` and `Token (token): Token{Id, Type: TokenType}` exist but
  are for single-use / billing-agreement tokens, not stored cards — use `vault_id`.)
- **Reading the authorization**: `OrderAuthorizeResponse.PurchaseUnits` → `PurchaseUnit.Payments`
  (`PaymentCollection`, `records-2`) → `Authorizations` (`AuthorizationWithAdditionalData`, `records-1`:
  `Id (id): string?`, `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime
  (expiration_time): string?`, `ProcessorResponse?`). Every level is nullable — null-check before indexing.
- **Whether `CreateOrder` alone (with inline card + intent=AUTHORIZE) already yields an authorization, or a
  separate `AuthorizeOrder` is always required — UNVERIFIED (live-only).** Plan for the explicit
  `AuthorizeOrder` call; if a create response already carries `purchase_units[].payments.authorizations`,
  read it best-effort and skip the second call, else call `AuthorizeOrder`.

### Payments — `client.Payments` (`operations/Payments.md`)

| # | Op — full signature | Request | Response + accessor path | Error (Case A) accessors |
|---|---|---|---|---|
| 5 | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", …, ct)` | `CaptureRequest?` (`records-1`): `Amount (amount): Money?`, `InvoiceId?`, `FinalCapture (final_capture): bool? = false`, `NoteToPayer?`, `SoftDescriptor?`. Full capture = `body: null`. | `CapturedPayment` (`records-1`): `Id (id)`, `Status (status): CaptureStatus`, `Amount (amount): Money` (captured amt), `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?` → `.GrossAmount (gross_amount): Money !req`, `.PaypalFee (paypal_fee): Money?`, `.NetAmount (net_amount): Money?` | `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| 6 | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", …, ct)` | `ReauthorizeRequest?` (`records-2`): `Amount (amount): Money?` (only field) | `PaymentAuthorization` (`records-2`): `Id (id)`, `Status (status): AuthorizationStatus`, `ExpirationTime (expiration_time)` | `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| 7 | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", …, ct)` | (no body model) | `PaymentAuthorization`: `Id`, `Status: AuthorizationStatus` (→ `Voided`) | `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| 8 | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", …, ct)` | `RefundRequest?` (`records-2`): `Amount (amount): Money?` (**omit/`body:null` = FULL refund; set `Amount` = partial**), `CustomId?`, `InvoiceId?`, `NoteToPayer?`. Idempotency via `payPalRequestId`. | `Refund` (`records-2`): `Id (id)`, `Status (status): RefundStatus`, `Amount (amount): Money` (refunded), `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (`.GrossAmount`,`.PaypalFee`,`.NetAmount`,`.TotalRefundedAmount`) | `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| 9 | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, …, ct)` | — | `PaymentAuthorization`: `Status (status): AuthorizationStatus`, `Amount`, `ExpirationTime` | `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| 9 | `GetCapturedPayment(string captureId, string? payPalMockResponse, …, ct)` | — | `CapturedPayment`: `Status (status): CaptureStatus`, `Amount`, `SellerReceivableBreakdown` | `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |

- **(#6) "Cannot reauthorize" signal** — a stale/expired-beyond-window authorization surfaces as an
  `SdkException<ReauthorizePaymentError>`; read `ex.Error.TryGetError(out var err)` then walk
  `err.Details (details): IReadOnlyList<ErrorDetails>` → `ErrorDetails.Issue (issue): string !req`
  (+ `.Description`) for the operator-actionable code. The HTTP status is typically 422. **The exact
  `Issue` string that means "no longer reauthorizable" is live-wire — UNVERIFIED:** extract
  `Details[].Issue`/`Description` best-effort and fall back to `err.Message` for the operator message.
- `Error` payload (`records-1`): `Name (name) !req`, `Message (message) !req`, `DebugId (debug_id) !req`,
  `Details (details): IReadOnlyList<ErrorDetails>?`, `Links?`. `ErrorDetails`: `Field?`, `Value?`, `Issue
  (issue) !req`, `Description?`.

### Vault — `client.Vault` (`operations/Vault.md`; models `records-2-Pa-Ve.md` unless noted)

| # | Op — full signature | Request | Response + accessor path | Error (Case A) accessors |
|---|---|---|---|---|
| 10 | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenRequest`: `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` | `PaymentTokenResponse`: `Id (id)` (**vault token id**), `Customer (customer): CustomerResponse?` (`.Id`), `PaymentSource.Card (card): CardPaymentTokenEntity?` → `.Brand (brand): CardBrand?`, `.LastDigits (last_digits): string?` (**safe display**) | `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError` |
| 11 | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query: `customer_id`←`customerId`, `page_size`←`pageSize`, `page`←`page`, `total_required`←`totalRequired` | `CustomerVaultPaymentTokensResponse` (`records-1`): `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` (each → `.Id`, `.PaymentSource.Card.Brand/.LastDigits`), `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Customer (customer): VaultResponseCustomer?` | `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` |
| 12 | `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentTokenResponse` (same accessors as #10) | `TryGetError1(out Error1)` [403,404,422,500] · `TryGetRawError` |
| 13 | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `void` (Task; 204) | `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` |

- **`PaymentTokenRequestPaymentSource`**: `Card (card): PaymentTokenRequestCard?`, `Token (token):
  VaultTokenRequest?`. **`PaymentTokenRequestCard`**: `Name (name): string?`, `Number (number): string?`,
  `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `Brand (brand): CardBrand?`,
  `BillingAddress (billing_address): Address?`. **Direct-from-raw-card vaulting is a single call** — no
  setup-token, no browser. (The two-step `CreateSetupToken`→`CreatePaymentToken` exists but is NOT required
  here; `SetupTokenRequestCard` adds `VerificationMethod`/`ExperienceContext` for 3DS flows you are not
  using.)
- **Customer scoping**: `PaymentTokenRequest.Customer` = `Customer` (`records-1`): `Id (id): string?`
  (PayPal-generated customer id), `MerchantCustomerId (merchant_customer_id): string?` (your own shopper
  id). The `customerId` you pass to `ListCustomerPaymentTokens` (wire `customer_id`) is the **PayPal
  `customer.id`** returned on the token. On first vault for a shopper, omit `customer.id` (PayPal mints one)
  and persist the returned `CustomerResponse.Id`; pass `MerchantCustomerId` if you want your id echoed. Map
  a shopper→PayPal-customer-id yourself.
- **Vaulted-card response representation**: `PaymentTokenResponsePaymentSource.Card` is
  `CardPaymentTokenEntity` (`records-1`): `Brand (brand): CardBrand?`, `LastDigits (last_digits): string?`,
  `Expiry (expiry): string?`, `Type (type): CardType?`, `BillingAddress?`. Show `Brand + " ****" +
  LastDigits`. **`Number`/`SecurityCode` are never returned** — expected; do not attempt to read them.
- `Error1` payload (`records-1`): `Name !req`, `Message !req`, `DebugId !req`, `Details:
  IReadOnlyList<ErrorDetails1>?`, `Links: IReadOnlyList<ErrorLinkDescription>?`. `ErrorDetails1`: `Field?`,
  `Value?`, `Issue !req`, `Description?`. (Note: Vault ops use `TryGetError1`/`Error1`, **not** `TryGetError`
  — the accessor name is literally `TryGetError1`.)

### Pagination — `ListCustomerPaymentTokens`
`page` (1-based) + `pageSize` (default **5**). **`TotalItems`/`TotalPages` are only populated when you pass
`totalRequired: true`** (`total_required=true`) — with the default `false` they may be null and you cannot
compute the page count. To enumerate all tokens: request with `totalRequired: true`, then loop `page` from
1 while `page <= TotalPages` (or until a returned `PaymentTokens` page is empty). Map row marks
"Pagination: none (only `page`, no `perPage`)" — meaning no SDK auto-pager; iterate manually.

### Transaction search — `client.TransactionSearch` (`operations/TransactionSearch.md`)

| # | Op — full signature | Request (query wire ← C#) | Response + accessor path | Error |
|---|---|---|---|---|
| 14 | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `start_date`←`startDate`, `end_date`←`endDate` (ISO-8601 strings, both **required**); `transaction_id`, `transaction_type`, `transaction_status`, `transaction_amount`, `transaction_currency`, `payment_instrument_type`, `store_id`, `terminal_id` (all `string?`, **pass `null` to skip**); `fields`←`fields` (default `"transaction_info"`), `page_size`←`pageSize` (default 100), `page`←`page` (default 1) | `SearchResponse` (`records-2`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?` | **Case B** `SdkException<RawError>` — no typed accessors; read `ex.Error.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()` |

- **This is the ONLY Case-B operation in the SDK** — catch `SdkException<RawError>` (not a typed
  `{Op}Error`), and read `StatusCode` / `ReadAsString()` for diagnostics.
- **Per-transaction accessors**: `TransactionDetails.TransactionInfo` = `TransactionInformation`
  (`records-2`): `TransactionId (transaction_id): string?`, `TransactionAmount (transaction_amount):
  Money?`, `TransactionStatus (transaction_status): string?`, `InvoiceId (invoice_id): string?`,
  `CustomField (custom_field): string?`, `PaypalReferenceId (paypal_reference_id): string?`,
  `TransactionInitiationDate (transaction_initiation_date): string?`. **Correlate to your eShop order via
  `CustomField` (`custom_field`) or `InvoiceId` (`invoice_id`)** — set those on the `PurchaseUnitRequest`
  at order creation.
- **PAGINATION (iterate ALL pages):** 1-based `page` + `pageSize` (default 100). Read `TotalPages` from the
  first response and loop `page = 1 … TotalPages` (fallback: stop when `TransactionDetails` is null/empty).
  `TotalItems` gives the grand count. Note: the map's op note says supplying optional filters empties the
  `ending_balance` field — not `total_pages`.
- **Max date-range window / max page_size: the map does NOT state explicit limits** — the only stated bound
  is the op note "lists transactions for the previous three years" and "up to 3 hours for executed
  transactions to appear." Do **not** hardcode PayPal's real 31-day window or 500-row cap from memory; if
  the API rejects a wide range or large page, it returns Case-B `RawError` — read `StatusCode`/body and
  chunk the range accordingly. (Flagged in Assumptions.)

### Enum value tables (from `enums.md`; namespace `PayPalServerSdk.Models.Enums`; write `Enum.Member`, wire value in parens)

| Enum | Members (C# → wire) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` — use **`Authorize`** |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` (response `.Brand`) | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, `Jcb (JCB)`, `Diners (DINERS)`, `Elo (ELO)`, `Rupay (RUPAY)`, `Maestro (MAESTRO)`, `Unknown (UNKNOWN)`, … (30 members) |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |

Enums are `StringEnum<T>`, **not** C# enums: use static members (`CheckoutPaymentIntent.Authorize`) or
`CheckoutPaymentIntent.FromValue("AUTHORIZE")`; compare with `==`.

---

## 4. Trap notes (load the named skill before coding that step — do not resolve inline)

- ⚠ **Step 1 (client + DI):** the registered `HttpClient`/handler pipeline must be long-lived and reused,
  not rebuilt per request; the wrapper client's lifetime is a separate decision. **MUST load
  `dotnet-client-initialization`** before wiring the client / `AddPayPalServerSdkClient`.
- ⚠ **Step 1 (auth):** where and when credentials must be set relative to client construction, and loading
  secrets from configuration rather than the code. **MUST load `dotnet-authentication`** before setting
  `Oauth2`.
- ⚠ **Step 1 (base URL / retries / timeouts):** what the SDK's `RetryOptions.Timeout` actually bounds
  (per-attempt vs whole call), and whether a failed POST (`CreateOrder`, `CaptureAuthorizedPayment`,
  `RefundCapturedPayment`) can be transparently re-sent on a transport failure regardless of
  `HttpMethodsToRetry` — which decides whether your `PayPal-Request-Id` idempotency key is load-bearing.
  **MUST load `dotnet-configuration-resilience`** before tuning the client.
- ⚠ **Step 2+ (calls):** many ops here have optional params with **no C# default** (the `payPal*`/`body`
  headers) that mis-bind in a positional call. **MUST load `dotnet-calling-endpoints`** before the first
  call; prefer named arguments (`ct:` for the token).
- ⚠ **Step 2+ (models):** enums are `StringEnum<T>` not C# enums, `required` members must be set in the
  initializer, and unmodeled JSON fields are dropped on deserialize (so a live field absent from the
  generated model silently vanishes). **MUST load `dotnet-models`** before building payloads or mapping
  responses.
- ⚠ **Every catch (error boundary):** which exception types actually reach the catch, why Vault ops expose
  `TryGetError1` (not `TryGetError`), why `SearchTransactions` is `SdkException<RawError>` while all others
  are typed, and that `TryGetRawError`/`TryGetNoContent` are distinct fallbacks. **MUST load
  `dotnet-error-handling`** before writing the boundary (see Required reading for the two mandatory
  `JsonException` hazards).
- ⚠ **Tests:** the `HttpClient` constructor argument is the fake seam. **MUST load `dotnet-testing`** before
  stubbing the SDK.

---

## 5. REQUIRED READING (load BEFORE implementation — this sheet deliberately omits their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — supplying OAuth2 client-credentials, secret loading |
| `dotnet-configuration-resilience` | Step 1 — base-URL/server selection, retries, what `Timeout` bounds, POST re-send risk, manual pagination |
| `dotnet-calling-endpoints` | Steps 2–5 — named-argument calls, required vs optional params, async/cancellation |
| `dotnet-models` | Steps 2–5 — building request models, `required`/nullability, `StringEnum<T>`, dropped-field behavior |
| `dotnet-error-handling` | Every catch — which exceptions reach the boundary, reading status/body safely |
| `dotnet-testing` | Tests — faking the HttpClient seam |

**Two mandatory error-boundary hazards — `System.Text.Json.JsonException` reaches the boundary from two
directions and they need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a
  5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something
  that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 6. Assumptions & Blockers

- **Assumption:** currency is configurable and passed as `AmountWithBreakdown.CurrencyCode`
  (`currency_code`) with `Value` (`value`) formatted as a decimal string in that currency's minor units;
  the integration is responsible for cent-accurate formatting (the SDK does not validate scale).
- **Assumption:** intent is `AUTHORIZE` for the whole flow (hold-then-capture); `Capture` intent and the
  `CaptureOrder` op are out of scope.
- **Assumption:** `PayPal:BaseUrl`, when present, is a full scheme+host (e.g.
  `https://api-m.sandbox.paypal.com`) suitable for `SandboxOptions.BaseUrl`; only the single Sandbox
  environment is targeted.
- **Assumption:** a shopper→PayPal-`customer.id` mapping is stored by the app so vault tokens can be scoped
  and listed per shopper; the SDK does not do this mapping.
- **UNVERIFIED (live-only):** whether `CreateOrder` with an inline card + `intent=AUTHORIZE` auto-produces
  an authorization or a separate `AuthorizeOrder` is always required — plan for the explicit call, read a
  create-time authorization best-effort if present.
- **UNVERIFIED (live-only):** exactly which nested fields each `prefer` value populates — request
  `return=representation` and null-check every nested list/field.
- **UNVERIFIED (live-only):** the precise `ErrorDetails.Issue` string signalling "authorization can no
  longer be reauthorized" — extract `Details[].Issue`/`Description` best-effort, fall back to
  `Error.Message`.
- **Not in map:** `SearchTransactions` max date-range window and max `page_size` — the map states only the
  3-year lookback. Do not hardcode limits from memory; handle a Case-B `RawError` rejection by chunking the
  range / lowering `page_size`.
- **No blockers:** every requested capability (client/auth, base-URL override incl. token request, order
  create+authorize direct-card and vaulted-card, capture/void/reauthorize/refund/lookups, direct card
  vaulting + list/get/delete, transaction search with pagination) is present in the SDK map/source. **No
  GAPS.**
