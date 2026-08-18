# PayPal .NET SDK integration plan — eShopOnWeb (sandbox)

SDK: `PayPalServerSdk` (NuGet `AsadAli.Checkout.Sdk`, install version-less). Map release `v1.0.1`
(source stamp `9653d18`). Target framework of SDK: `netstandard2.0`. All facts below are grounded in
the bundled SDK map (page cited per row); the base-URL/token-URL and auth-credential facts in Step 1
were confirmed from SDK source because the map only gestured at them.

App root namespace: `Microsoft.eShopWeb`. Put the integration in its own namespace (e.g.
`Microsoft.eShopWeb.PayPal`) so the `PayPalServerSdk.*` usings never collide with the app's `Order`,
`Address`, etc. (the SDK has its own `Order`, `Address`, `Money`, `Payer` records — **always
fully-qualify or alias** to avoid `CS0104` ambiguity against eShop's domain types).

---

## 1. Scope & sequence

| # | Step | Operation(s) | Controller |
|---|---|---|---|
| 1 | Client + DI + auth + base-URL override | `AddPayPalServerSdkClient` / `new PayPalServerSdkClient` | — |
| 2 | Create order (intent AUTHORIZE) + authorize with direct card | `CreateOrder` → `AuthorizeOrder` | `Orders` |
| 3 | Capture at fulfilment; read fee/net | `CaptureAuthorizedPayment` | `Payments` |
| 4 | Re-authorize a stale authorization | `GetAuthorizedPayment` (detect) → `ReauthorizePayment` | `Payments` |
| 5 | Void/release authorization on cancel | `VoidPayment` | `Payments` |
| 6 | Refund a capture (full/partial, idempotent) | `RefundCapturedPayment` | `Payments` |
| 7 | Idempotency for authorize/capture | (header param on the above) | — |
| 8 | Vault a card + pay later with vaulted token | `CreatePaymentToken` (+ optional `CreateSetupToken`); pay via `CreateOrder` with `vault_id` | `Vault` / `Orders` |
| 9 | Delete a vaulted card | `DeletePaymentToken` | `Vault` |
| 10 | Transaction reporting / reconciliation (paged) | `SearchTransactions` | `TransactionSearch` |

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

### Namespaces used in this sheet (add a `using` per kind)

| Type kind | Namespace |
|---|---|
| Client / options / `ServerOptions` | `PayPalServerSdk` |
| Controllers (accessed via `client.Orders` etc.) | `PayPalServerSdk.Api` |
| Records (request/response models: `OrderRequest`, `Money`, `CardRequest`, `Order`, `CapturedPayment`, …) | `PayPalServerSdk.Models` |
| Enums (`CheckoutPaymentIntent`, `OrderStatus`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus`, `CardBrand`) | `PayPalServerSdk.Models.Enums` |
| Typed error payloads referenced by `TryGet…` (`Error`, `Error1`, `DefaultError`, `ErrorDetails`) | `PayPalServerSdk.Models` |
| Per-operation error classes (`CreateOrderError`, `AuthorizeOrderError`, …) | `PayPalServerSdk.Errors` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `ServerEnvironment`, `DefaultOptions` (and nested `DefaultOptions.SandboxOptions`) | `PayPalServerSdk.Servers` |

### Step 1 — Client construction, DI, auth, environment, base-URL override

Source-confirmed (SDK source, not just the map — the map only pointed at `Servers/`/`options.Server`):

- **Construction:** `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`
  — the `HttpClient` is the first ctor arg (also the test seam). (`sdk-map.md` → *Getting a client*.)
- **DI:** `services.AddPayPalServerSdkClient(o => { … })` registers the client as a **singleton**, built
  from `IHttpClientFactory.CreateClient()` (it calls `services.AddHttpClient()` for you). Configure `o`
  inside the callback. (`ServiceCollectionExtensions.cs`.)
- **Options** (`PayPalServerSdkClientOptions`, `sdk-map.md` → client-options): `Environment: ServerEnvironment`,
  `Server: ServerOptions`, `Retry: RetryOptions`, `Logging: LoggingOptions`,
  `Oauth2: OAuth2ClientCredentials?`, `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.
- **Auth (OAuth2 client-credentials):** set `o.Oauth2 = new OAuth2ClientCredentials { ClientId = cfg["PayPal:ClientId"], ClientSecret = cfg["PayPal:ClientSecret"] }`.
  `OAuth2ClientCredentials` has `required string ClientId`, `required string ClientSecret`, `string? Scope`
  (source `OAuth2ClientCredentials.cs`). You do **not** call a token endpoint yourself — the SDK's default
  token strategy fetches/refreshes the bearer token on first call. Leave `Oauth2TokenStrategy` null.
- **Environment:** `o.Environment = ServerEnvironment.Sandbox`. **Sandbox is the ONLY environment the SDK
  ships** — there is no `Production` member on `ServerEnvironment` (source `Servers/ServerEnvironment.cs`,
  confirmed: only `Sandbox`). So `PayPal:Environment` cannot select a built-in production server; see
  Assumptions & Blockers.
- **Base URL (default):** `https://api-m.sandbox.paypal.com` (source `Servers/DefaultOptions.cs`
  `SandboxOptions.BaseUrl`).
- **Verbatim base-URL override (`PayPal:BaseUrl`):** set
  `o.Server.Default.Sandbox.BaseUrl = cfg["PayPal:BaseUrl"]` (type path:
  `ServerOptions.Default` → `DefaultOptions.Sandbox` → `DefaultOptions.SandboxOptions.BaseUrl`, a plain
  `string`). **Source-confirmed that this same value is used for the OAuth2 token request:** the token
  strategy resolves its URL via `server.Default("/v1/oauth2/token")`, which flows through
  `SandboxOptions.BaseUrl` (source `AuthSchemes.cs` + `Server.cs` + `DefaultOptions.cs`). So setting this
  one property covers **both** the token/credential request and every API call, verbatim. Only apply it
  when `PayPal:BaseUrl` is present; otherwise leave the default.

### Step 2–10 — operation rows

| Op | Signature (params in order; `!null`=nullable-no-default, must pass explicitly) | Request model + fields used | Response envelope + fields read | Error case + accessors + payload | Page |
|---|---|---|---|---|---|
| **CreateOrder** | `CreateOrder(string? payPalMockResponse!null, string? payPalRequestId!null, string? payPalPartnerAttributionId!null, string? payPalClientMetadataId!null, string? payPalAuthAssertion!null, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Order` | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req` = `CheckoutPaymentIntent.Authorize`; `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`; `PaymentSource (payment_source): PaymentSource?`. `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req`. `AmountWithBreakdown`: `CurrencyCode (currency_code): string !req` (=`PayPal:Currency`), `Value (value): string !req` (order total to the cent, as decimal string e.g. "12.34"). `PaymentSource`: `Card (card): CardRequest?` (see card fields below). | `Order`: `Id (id): string?` (order id — persist it), `Status (status): OrderStatus?`, `PurchaseUnits: IReadOnlyList<PurchaseUnit>?`, `PaymentSource: PaymentSourceResponse?`, `Links: IReadOnlyList<LinkDescription>?`. | Case A `SdkException<CreateOrderError>`: `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)`. `Error` payload: `Name`, `Message`, `DebugId`, `Details: IReadOnlyList<ErrorDetails>?` (each `ErrorDetails`: `Field`, `Value`, `Issue !req`, `Description`). | operations/Orders.md; records-1 (`OrderRequest`,`PurchaseUnitRequest`,`AmountWithBreakdown`,`Money`,`Order`,`Error`,`ErrorDetails`) |
| **AuthorizeOrder** | `AuthorizeOrder(string id, string? payPalMockResponse!null, string? payPalRequestId!null, string? payPalClientMetadataId!null, string? payPalAuthAssertion!null, OrderAuthorizeRequest? body!null, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `OrderAuthorizeResponse` | `id` = order id from CreateOrder. `payPalRequestId` = idempotency key (Step 7). `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` → `Card (card): CardRequest?`. **Direct-card path** (see CardRequest below). (If the card was supplied at CreateOrder you may pass `body: null`.) | `OrderAuthorizeResponse`: `Id`, `Status (status): OrderStatus?`, `PaymentSource: OrderAuthorizeResponsePaymentSource?` (`.Card: CardResponse?`), `PurchaseUnits: IReadOnlyList<PurchaseUnit>?`, `Links: IReadOnlyList<LinkDescription>?`. **Authorization id + status:** `resp.PurchaseUnits[i].Payments (PaymentCollection).Authorizations (IReadOnlyList<AuthorizationWithAdditionalData>)[j]` → `.Id`, `.Status (AuthorizationStatus?)`, `.Amount (Money?)`, `.ExpirationTime (string?)`, `.ProcessorResponse`. Persist the authorization `Id`. | Case A `SdkException<AuthorizeOrderError>`: `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError(out RawError)`. | operations/Orders.md; records-1 (`OrderAuthorizeRequest`,`OrderAuthorizeRequestPaymentSource`,`OrderAuthorizeResponse`,`OrderAuthorizeResponsePaymentSource`,`AuthorizationWithAdditionalData`); records-2 (`PaymentCollection`) |
| **CaptureAuthorizedPayment** (fulfilment) | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse!null, string? payPalRequestId!null, string? payPalAuthAssertion!null, CaptureRequest? body!null, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `CapturedPayment` | `authorizationId` = authorization `Id` from Step 2. `payPalRequestId` = idempotency key (Step 7). `CaptureRequest` (optional; pass `null` for full capture): `Amount (amount): Money?`, `FinalCapture (final_capture): bool? = false`, `InvoiceId`, `NoteToPayer`, `SoftDescriptor`. | `CapturedPayment`: `Id (id): string?` (capture id — persist for refunds), `Status (status): CaptureStatus?`, `Amount (amount): Money?` (**captured amount** = `.Amount.Value`/`.CurrencyCode`), `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?` → `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?` (**PayPal fee**), `NetAmount (net_amount): Money?` (**net proceeds to merchant**), `ReceivableAmount (receivable_amount): Money?` (net in settlement currency when FX applies). | Case A `SdkException<CaptureAuthorizedPaymentError>`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`. | operations/Payments.md; records-1 (`CaptureRequest`,`CapturedPayment`); records-2 (`SellerReceivableBreakdown`) |
| **ReauthorizePayment** (stale auth) | `ReauthorizePayment(string authorizationId, string? payPalRequestId!null, string? payPalAuthAssertion!null, ReauthorizeRequest? body!null, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization` | `authorizationId` = the stale authorization `Id`. `ReauthorizeRequest`: `Amount (amount): Money?` (**only `amount` is supported** — set it to the still-owed total; note PayPal caps reauth uplift). | `PaymentAuthorization`: `Id` (new authorization id), `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?` (new 3-day honor window). | Case A `SdkException<ReauthorizePaymentError>`: `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`. | operations/Payments.md; records-2 (`ReauthorizeRequest`,`PaymentAuthorization`) |
| **GetAuthorizedPayment** (detect stale before reauth) | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse!null, string? payPalAuthAssertion!null, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization` | `authorizationId`. | `PaymentAuthorization.Status (AuthorizationStatus?)`, `.ExpirationTime (string?)`. **Staleness detection:** the generated `AuthorizationStatus` enum has members `Created, Captured, Denied, PartiallyCaptured, Voided, Pending` — **there is NO `Expired` member** (enums.md, source-visible). So detect expiry by parsing `ExpirationTime` (ISO-8601) and comparing to now, and/or by catching the 422 error from `CaptureAuthorizedPayment`/`ReauthorizePayment` and reading `Error.Details[].Issue` (the expiry issue string is API-defined — treat defensively, see UNVERIFIED note). | Case A `SdkException<GetAuthorizedPaymentError>`: `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`. | operations/Payments.md; records-2 (`PaymentAuthorization`); enums.md (`AuthorizationStatus`) |
| **VoidPayment** (cancel/release) | `VoidPayment(string authorizationId, string? payPalMockResponse!null, string? payPalAuthAssertion!null, string? payPalRequestId!null, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization` | `authorizationId` = the authorization to release. **No request body.** Note the param order here is `payPalAuthAssertion` **before** `payPalRequestId` (differs from other Payments ops). | `PaymentAuthorization.Status` should read `AuthorizationStatus.Voided`. (Cannot void an authorization already fully captured — surfaces as 409/422.) | Case A `SdkException<VoidPaymentError>`: `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`. | operations/Payments.md; records-2 (`PaymentAuthorization`) |
| **RefundCapturedPayment** | `RefundCapturedPayment(string captureId, string? payPalMockResponse!null, string? payPalRequestId!null, string? payPalAuthAssertion!null, RefundRequest? body!null, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Refund` | `captureId` = capture `Id` from Step 3. **Full refund:** pass `body: null` (empty payload). **Partial refund:** `RefundRequest { Amount = new Money { CurrencyCode = …, Value = "…" } }` (`Amount (amount): Money?`; also `CustomId`, `InvoiceId`, `NoteToPayer`). **Idempotency key:** `payPalRequestId` param (Step 7). | `Refund`: `Id (id): string?` (refund id), `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount`). | Case A `SdkException<RefundCapturedPaymentError>`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`. **Over-refund enforcement:** PayPal rejects a refund exceeding the captured amount with a 422; read the reason from `Error.Details[].Issue` (issue string is API-defined — treat defensively, see UNVERIFIED). | operations/Payments.md; records-2 (`RefundRequest`,`Refund`,`SellerPayableBreakdown`) |
| **CreatePaymentToken** (vault a card, one-step) | `CreatePaymentToken(string? payPalRequestId!null, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentTokenResponse` | `PaymentTokenRequest`: `Customer (customer): Customer?` (set `Customer.Id` to your shopper key to group tokens), `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` → `Card (card): PaymentTokenRequestCard?`. `PaymentTokenRequestCard`: `Name`, `Number` (`4111111111111111`), `Expiry` (`"YYYY-MM"`), `SecurityCode`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`. | `PaymentTokenResponse`: `Id (id): string?` (**vaulted payment-method / token id** — persist), `Customer: CustomerResponse?`, `PaymentSource: PaymentTokenResponsePaymentSource?` → `Card (card): CardPaymentTokenEntity?`. **Safe descriptor:** `CardPaymentTokenEntity.Brand (CardBrand?)` + `.LastDigits (last_digits): string?` + `.Expiry (string?)` — never persist full PAN/CVV. | Case A `SdkException<CreatePaymentTokenError>`: `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError(out RawError)`. `Error1` payload: `Name`,`Message`,`DebugId`,`Details: IReadOnlyList<ErrorDetails1>?`. | operations/Vault.md; records-2 (`PaymentTokenRequest`,`PaymentTokenRequestPaymentSource`,`PaymentTokenRequestCard`,`PaymentTokenResponse`,`PaymentTokenResponsePaymentSource`); records-1 (`CardPaymentTokenEntity`,`Error1`) |
| **CreateSetupToken** (optional two-step vault) | `CreateSetupToken(string? payPalRequestId!null, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `SetupTokenResponse` | `SetupTokenRequest`: `Customer?`, `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req` → `Card (card): SetupTokenRequestCard?` (`Name`,`Number`,`Expiry`,`SecurityCode`,`Brand`,`BillingAddress`). Then feed `SetupTokenResponse.Id` into `CreatePaymentToken` via `PaymentTokenRequestPaymentSource.Token = new VaultTokenRequest { Id = setupTokenId, Type = VaultTokenRequestType.SetupToken }`. | `SetupTokenResponse`: `Id (id): string?` (setup token id), `Status (status): PaymentTokenStatus?`, `PaymentSource: SetupTokenResponsePaymentSource?`. | Case A `SdkException<CreateSetupTokenError>`: `TryGetError1(out Error1)` [400,403,422,500] · `TryGetRawError(out RawError)`. | operations/Vault.md; records-2 (`SetupTokenRequest`,`SetupTokenRequestPaymentSource`,`SetupTokenRequestCard`,`SetupTokenResponse`,`VaultTokenRequest`) |
| **Pay later with vaulted card** (referenced card) | Reuse **CreateOrder** (row above) with intent AUTHORIZE or CAPTURE. | On `OrderRequest.PaymentSource` set `Card = new CardRequest { VaultId = savedTokenId }`. `CardRequest.VaultId (vault_id): string?` references the stored token — do **not** resend PAN. (`CardRequest` also has `SingleUseToken`, `Attributes`, etc.) | Same `Order` envelope as CreateOrder; proceed to AuthorizeOrder/Capture as usual. | Same as CreateOrder. | operations/Orders.md; records-1 (`CardRequest`,`PaymentSource`) |
| **DeletePaymentToken** | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `void` (Task) | `id` = vaulted token id. | 204 No Content (no body). | Case A `SdkException<DeletePaymentTokenError>`: `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError(out RawError)`. | operations/Vault.md |
| **SearchTransactions** (reconciliation, paged) | `SearchTransactions(string startDate, string endDate, string? transactionId!null, string? transactionType!null, string? transactionStatus!null, string? transactionAmount!null, string? transactionCurrency!null, string? paymentInstrumentType!null, string? storeId!null, string? terminalId!null, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `SearchResponse` | `startDate`/`endDate` = ISO-8601 range (wire `start_date`/`end_date`). Pass the 8 middle nullable params explicitly as `null`. **Call with named arguments** (many optional params, positional mis-binds). `fields="transaction_info"` returns the `TransactionInfo` block. | `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`. Per row: `TransactionDetails.TransactionInfo (TransactionInformation)` → `TransactionId (transaction_id): string?`, `TransactionAmount (transaction_amount): Money?`, `TransactionStatus (transaction_status): string?`, `FeeAmount (fee_amount): Money?`. **Pagination:** page-number based — read `TotalPages`, then loop `page = 1 … TotalPages` (each call re-passing the same window and `pageSize`) to cover the whole range. There is NO `perPage`/cursor; only `page` + `pageSize` (default 100). | **Case B** `SdkException<RawError>` — the **only** Case-B op in this SDK. Read `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, or `ex.Error.ReadAsJson<T>()` (no typed `TryGet…`). | operations/TransactionSearch.md; records-2 (`SearchResponse`,`TransactionDetails`,`TransactionInformation`) |

### Direct-card `CardRequest` fields (Steps 2, 8 pay-later)

`CardRequest` (`PayPalServerSdk.Models`, records-1): `Name (name): string?`, `Number (number): string?`
(`"4111111111111111"`), `Expiry (expiry): string?` (**format `"YYYY-MM"`**, any future month),
`SecurityCode (security_code): string?` (any CVC), `BillingAddress (billing_address): Address?`,
`VaultId (vault_id): string?` (reference a stored card instead of PAN), `SingleUseToken`, `Attributes`,
`StoredCredential`, `ExperienceContext`.

`Address` (records-1): `AddressLine1 (address_line_1): string?`, `AddressLine2 (address_line_2): string?`,
`AdminArea2 (admin_area_2): string?` (city), `AdminArea1 (admin_area_1): string?` (state/region),
`PostalCode (postal_code): string?`, `CountryCode (country_code): string !req`.

`Money` (records-1): `CurrencyCode (currency_code): string !req`, `Value (value): string !req` — value is a
**decimal string** (e.g. `"12.34"`), so format the eShop total to the currency's minor-unit scale.

### Enum value tables (all `PayPalServerSdk.Models.Enums`; write the C# member, not the wire value)

| Enum | Members (`CSharpMember` → `WIRE`) | Use |
|---|---|---|
| `CheckoutPaymentIntent` | `Capture`→CAPTURE, `Authorize`→AUTHORIZE | `OrderRequest.Intent = CheckoutPaymentIntent.Authorize` |
| `OrderStatus` | `Created`→CREATED, `Saved`→SAVED, `Approved`→APPROVED, `Voided`→VOIDED, `Completed`→COMPLETED, `PayerActionRequired`→PAYER_ACTION_REQUIRED | order/authorize status; 3DS challenge = `PayerActionRequired` |
| `AuthorizationStatus` | `Created`→CREATED, `Captured`→CAPTURED, `Denied`→DENIED, `PartiallyCaptured`→PARTIALLY_CAPTURED, `Voided`→VOIDED, `Pending`→PENDING **(no `Expired`)** | authorization state; void → `Voided` |
| `CaptureStatus` | `Completed`→COMPLETED, `Declined`→DECLINED, `PartiallyRefunded`→PARTIALLY_REFUNDED, `Pending`→PENDING, `Refunded`→REFUNDED, `Failed`→FAILED | capture result |
| `RefundStatus` | `Cancelled`→CANCELLED, `Failed`→FAILED, `Pending`→PENDING, `Completed`→COMPLETED | refund result |
| `CardBrand` | `Visa`→VISA, `Mastercard`→MASTERCARD, `Amex`→AMEX, `Discover`→DISCOVER, … (30 members) | `CardBrand.Visa` for the test card; safe descriptor |
| `PaymentTokenStatus` | `Created`, `PayerActionRequired`, `Approved`, `Vaulted`, `Tokenized` | setup/payment token status |
| `VaultTokenRequestType` | `SetupToken`→SETUP_TOKEN (only member) | `VaultTokenRequest.Type` in two-step vault |

Enums are `StringEnum<T>`, **not** C# enums: write `CheckoutPaymentIntent.Authorize` (or
`CheckoutPaymentIntent.FromValue("AUTHORIZE")`), never `"AUTHORIZE"` directly, and read back by comparing
to the static member.

### 3DS / challenge detection (Step 2 — STOP-and-report, no approval round-trip)

After `AuthorizeOrder`, treat the payment as requiring browser approval when **any** of:
- `resp.Status == OrderStatus.PayerActionRequired`; and/or
- `resp.Links` contains a `LinkDescription` whose `Rel` indicates payer action (e.g. `"payer-action"` /
  `"approve"`) — `LinkDescription`: `Href (href): string !req`, `Rel (rel): string !req`,
  `Method (method): LinkHttpMethod?` (records-1);
- the card authentication result under
  `resp.PaymentSource?.Card (CardResponse).AuthenticationResult (AuthenticationResponse)` →
  `ThreeDSecure (ThreeDSecureAuthenticationResponse)` (`AuthenticationStatus`, `EnrollmentStatus`) /
  `LiabilityShift` signals a challenge (records-1 `CardResponse`,`AuthenticationResponse`; records-2
  `ThreeDSecureAuthenticationResponse`).

`UNVERIFIED` — exactly which of these fields the live sandbox populates for a 3DS challenge on a direct
card can only be confirmed against live traffic. **Directive:** detect a challenge defensively — check
`Status == PayerActionRequired` **and** scan `Links` for a payer-action/approve `Rel` (case-insensitive,
best-effort); if either is present, STOP, surface the approval URL (the `Href`) and the status to the
operator, and do NOT auto-capture. Do not depend on `AuthenticationResult` being non-null.

### Idempotency (Step 7)

The idempotency key is the **`payPalRequestId`** parameter (PayPal-Request-Id header) — present on
`CreateOrder`, `AuthorizeOrder`, `CaptureAuthorizedPayment`, `RefundCapturedPayment`,
`ReauthorizePayment`, `VoidPayment`, `CreatePaymentToken`, `CreateSetupToken`. Generate a **stable** key
per logical action (e.g. derive from the eShop order id + action name, not `Guid.NewGuid()` per attempt)
and re-send the same value on retries so a double-click cannot authorize/capture/refund twice.

---

## 3. Trap notes (load the named skill before writing that step)

- ⚠ **Step 1 (client + DI):** the `HttpClient`/handler pipeline must be long-lived and shared; how the
  DI helper (`AddPayPalServerSdkClient`) manages lifetime, and what you must still wire yourself, is not
  visible from the ctor. **MUST load `dotnet-client-initialization`** before registering the client.
- ⚠ **Step 1 (auth):** where/when credentials must be set relative to client construction, and how the
  token is fetched/refreshed, is not shown by the property types. **MUST load `dotnet-authentication`**
  before wiring `Oauth2`.
- ⚠ **Step 1 (base URL + resilience):** the SDK's retry/timeout options do **not** bound a whole call and
  are not the `HttpClient` timeout; and which verbs actually retry is not what the option names imply —
  this matters because authorize/capture/refund are non-idempotent writes. **MUST load
  `dotnet-configuration-resilience`** before setting `Retry`, `Timeout`, or the base URL.
- ⚠ **Steps 2–10 (all calls):** list/search ops have optional params with no C# default that mis-bind
  positionally — call with **named arguments**; and confirm async/`ct` usage. **MUST load
  `dotnet-calling-endpoints`** before the first call.
- ⚠ **Steps 2–10 (models):** enums are `StringEnum<T>` (not C# enums), unmodeled JSON fields are dropped
  on deserialize, and `required` members must be set in the initializer. **MUST load `dotnet-models`**
  before building any request payload or mapping a response.
- ⚠ **Step 10 (reconciliation):** `SearchTransactions` is the **only Case-B** op — its exception is
  `SdkException<RawError>` with no typed `TryGet…`; every other op is Case A. Don't write a Case-A catch
  for it. **MUST load `dotnet-error-handling`.**
- ⚠ **Testing:** the `HttpClient` ctor arg is the test seam. **MUST load `dotnet-testing`** before
  stubbing the SDK.

---

## 4. REQUIRED READING — load every skill below BEFORE implementation starts

This sheet deliberately does **not** carry these skills' contents (defaults, worked examples, the parts a
one-line note cannot show). Load each before coding the step it governs:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, `HttpClient` lifetime, DI registration |
| `dotnet-authentication` | Step 1 — setting OAuth2 credentials, token fetch/refresh, 401/403 |
| `dotnet-configuration-resilience` | Step 1 & retries — base-URL override, retry/timeout semantics on non-idempotent writes, pagination |
| `dotnet-calling-endpoints` | Steps 2–10 — named-argument calls, async, cancellation |
| `dotnet-models` | Steps 2–10 — building request models, `StringEnum<T>`, required/nullable, dropped-field trap |
| `dotnet-error-handling` | The error boundary (always) — Case A vs Case B, reading status/body safely |
| `dotnet-testing` | Integration tests — the `HttpClient` seam, error/edge paths |

**Two mandatory `System.Text.Json.JsonException` hazards at the error boundary** (they reach it from two
directions and need opposite handling):

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so a catch ladder that only catches `SdkException<…>`
  lets it escape the integration boundary. Especially relevant here: every response record has almost all
  fields nullable, but `AmountWithBreakdown` (`CurrencyCode`,`Value`), `SellerReceivableBreakdown`
  (`GrossAmount`), `Money`, `LinkDescription` (`Href`,`Rel`), and the `Error`/`Error1` payloads carry
  `required` members — a wire shape that omits them throws on deserialize.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to
  a 5xx then reports a deterministic rejection (e.g. a 422 over-refund) as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- `PayPal:Currency` is a single ISO-4217 code applied to all `Money.CurrencyCode`/`AmountWithBreakdown.CurrencyCode`
  values; the eShop order total is formatted to that currency's minor-unit scale as a decimal string.
- `PayPal:ClientId`/`PayPal:ClientSecret` are sandbox REST-app credentials; the SDK's default OAuth2
  client-credentials token strategy is used (`Oauth2TokenStrategy` left null).
- Idempotency keys are derived deterministically per logical action from the eShop order id (not random
  per attempt), so retries reuse the same `payPalRequestId`.
- The vault flow groups tokens under a customer via `PaymentTokenRequest.Customer.Id` (your shopper key);
  listing a customer's saved cards, if needed, is `client.Vault.ListCustomerPaymentTokens(customerId, …)`
  (page/pageSize; returns `CustomerVaultPaymentTokensResponse` with `TotalItems`/`TotalPages`).

**Blockers / gaps to report to the user**
- **`PayPal:Environment` cannot select a built-in Production server.** The SDK's `ServerEnvironment` ships
  **only** `Sandbox` (source-confirmed) and `DefaultOptions` resolves the base URL solely from
  `Sandbox.BaseUrl` (default `https://api-m.sandbox.paypal.com`). To ever point at live PayPal you MUST
  supply `PayPal:BaseUrl = https://api-m.paypal.com` verbatim (which, source-confirmed, also redirects the
  OAuth2 token request). So `PayPal:Environment` is effectively advisory in this SDK; production routing
  depends entirely on `PayPal:BaseUrl`. Flag to the user: this is a genuine SDK limitation, matching the
  brief's target of sandbox.
- **No capability is missing for items 1–10.** All ten interactions are covered:
  create/authorize (`Orders`), capture/reauthorize/void/refund (`Payments`), vault
  create/delete + pay-with-vault (`Vault`/`Orders`), and transaction search with page/pageSize/TotalPages
  pagination (`TransactionSearch`). No requested operation is absent from the SDK.
- **Stale-authorization detection has no dedicated status.** `AuthorizationStatus` has no `Expired`
  member, so "stale" must be inferred from `ExpirationTime` and/or the 422 error issue from a failed
  capture/reauthorize (issue string is API-defined, `UNVERIFIED` — code defensively against
  `Error.Details[].Issue`).
- **3DS challenge fields are `UNVERIFIED`** (live-traffic-only): implement the defensive detection
  directive in the 3DS section rather than assuming a specific field is populated.
