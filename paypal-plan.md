# PayPal .NET SDK integration plan — eShopOnWeb (`src/PublicApi`)

SDK: `AsadAli.Checkout.Sdk` · root namespace `PayPalServerSdk` · client `PayPalServerSdkClient`
(map stamp: source commit `9653d18`, tag `v1.0.1`). All facts below are grounded in the bundled
SDK map (`sdk-map.md`, `map/operations/*.md`, `map/models/*.md`); the base-URL/token-endpoint and
auth-credential facts in Step 1 were settled from the SDK source (`ServerOptions.cs`, `Servers/DefaultOptions.cs`,
`AuthSchemes.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`) because the map
does not carry those bodies. Target: PayPal **Sandbox**, REST client-id/secret, **direct card** processing
and **card vaulting** — no browser/approval round-trip (see Assumptions & Blockers for the one place that
constraint bites).

---

## 1. Scope & sequence

| # | Step | Operations / types used |
|---|---|---|
| 1 | Client construction, auth, base-URL override, DI | `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `OAuth2ClientCredentials`, `ServerOptions`/`DefaultOptions`, `AddPayPalServerSdkClient` |
| 2 | Create order intent=AUTHORIZE, direct card | `client.Orders.CreateOrder` → `Order` |
| 3 | Idempotency key (cross-cutting) | `payPalRequestId` param on create/capture/refund/reauth/void/vault ops |
| 4 | Authorize (inline vs separate) | authorization read from Step-2 `Order`; fallback `client.Orders.AuthorizeOrder` |
| 5 | Capture the authorization ("fulfil") | `client.Payments.CaptureAuthorizedPayment` → `CapturedPayment` |
| 6 | Reauthorize a stale authorization | `client.Payments.ReauthorizePayment` → `PaymentAuthorization` |
| 7 | Void an authorization ("cancel before fulfil") | `client.Payments.VoidPayment` → `PaymentAuthorization` |
| 8 | Refund a captured payment (full/partial) | `client.Payments.RefundCapturedPayment` → `Refund` |
| 9 | Vault a card without payment | `client.Vault.CreateSetupToken` + `client.Vault.CreatePaymentToken` (or direct `CreatePaymentToken`) |
| 10 | Pay with a vaulted card token | `CreateOrder` with `payment_source.card.vault_id` |
| 11 | Delete a vaulted token | `client.Vault.DeletePaymentToken` |
| 12 | Transaction reporting over a date range | `client.TransactionSearch.SearchTransactions` → `SearchResponse` (paginate) |
| 13 | Error handling boundary | `SdkException<TError>`, typed `{Op}Error` + `RawError` |

---

## CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one
> from that type's own map row, never from where a neighbouring type sits. Enums, unions, auth,
> server and client-config types are spread across different child namespaces, and two types
> configured side by side in the same options object routinely live in different ones. Dropping a
> type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Namespaces (add a `using` per kind — C# does not import child namespaces transitively)

| Namespace | Contents used here |
|---|---|
| `PayPalServerSdk` | `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions`, `AddPayPalServerSdkClient` (ext.) |
| `PayPalServerSdk.Servers` | `ServerEnvironment`, `DefaultOptions` (+ nested `DefaultOptions.SandboxOptions`) |
| `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | `OAuth2ClientCredentials` |
| `PayPalServerSdk.Core.Authentication.OAuth2` | `IOAuth2TokenStrategy<T>` |
| `PayPalServerSdk.Core.Configuration` | `RetryOptions` |
| `PayPalServerSdk.Core.Exceptions` | `SdkException<TError>` |
| `PayPalServerSdk.Core.ErrorResponse` | `RawError`, `ApiError` |
| `PayPalServerSdk.Api` | operation controller classes (accessed via `client.Orders` etc.) |
| `PayPalServerSdk.Models` | all request/response records + typed error payload records (`Error`, `Error1`, `DefaultError`, `ErrorDetails`) |
| `PayPalServerSdk.Models.Enums` | all enums (`CheckoutPaymentIntent`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus`, `CardBrand`, …) |
| `PayPalServerSdk.Errors` | typed op error classes (`CreateOrderError`, `AuthorizeOrderError`, `CaptureAuthorizedPaymentError`, …) |

### Step 1 — Client, auth, environment, base-URL override, DI

`PayPalServerSdkClientOptions` properties (source `PayPalServerSdkClientOptions.cs`): `Environment: ServerEnvironment`,
`Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `Oauth2: OAuth2ClientCredentials?`,
`Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.

- **Constructor:** `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`.
- **Auth (client-id/secret):** set `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = …, Scope = null }`
  — `ClientId` and `ClientSecret` are C# `required`; `Scope` is optional (`string?`). Load both from config, never hardcode.
- **Environment:** `options.Environment = ServerEnvironment.Sandbox` (the only member; `ServerEnvironment` is a
  `StringEnum`, use the static member).
- **Base-URL override (verbatim, and it DOES cover the token request):** set
  `options.Server = new ServerOptions { Default = new DefaultOptions { Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = <PayPal:BaseUrl> } } }`.
  The default `SandboxOptions.BaseUrl` is `"https://api-m.sandbox.paypal.com"`; the value you set is used verbatim as the
  URL-template base. **Confirmed from source (`AuthSchemes.cs`):** the OAuth2 token request is built as
  `server.Default("/v1/oauth2/token")`, which resolves through the SAME `ServerOptions.Default.Sandbox.BaseUrl` — so
  overriding `BaseUrl` redirects the token/credential request too. Apply the override only when the `PayPal:BaseUrl`
  config value is present; otherwise leave `options.Server` at its default so Sandbox is used. `ServerOptions` lives in
  the root `PayPalServerSdk` namespace; `DefaultOptions` (+ its nested `SandboxOptions`) in `PayPalServerSdk.Servers`.
- **DI (ASP.NET Core, `src/PublicApi`):** `services.AddPayPalServerSdkClient(o => { o.Oauth2 = …; o.Environment = …; o.Server = …; });`
  Source: the extension calls `services.AddHttpClient()` and registers the client as a **singleton** built from an
  `IHttpClientFactory`-created `HttpClient`. HttpClient ownership therefore sits with the SDK's DI extension — do not new-up
  and dispose your own per-request `HttpClient`.

### Step 2 — Create order, intent=AUTHORIZE, direct card

- **Signature:** `client.Orders.CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  — the 5 leading `string?` params have **no default**: pass `null` to skip each. Pass `prefer: "return=representation"`
  so the response includes `purchase_units[].payments` (needed to read the authorization inline — see Step 4). `map/operations/Orders.md`
- **Returns:** `Order`. **Error:** `SdkException<CreateOrderError>` — Case A; `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` [fallback].
- **Request tree** (all records `PayPalServerSdk.Models`, enums `PayPalServerSdk.Models.Enums`): `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`
  - `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req` = `CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`) ·
    `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` · `PaymentSource (payment_source): PaymentSource?` ·
    `Payer (payer): Payer?` · `ApplicationContext (application_context): OrderApplicationContext?`
  - `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req` (+ optional `ReferenceId`, `CustomId`, `InvoiceId`, `Description`, `Items`)
  - `AmountWithBreakdown`: `CurrencyCode (currency_code): string !req` (from config) · `Value (value): string !req`
    (exact-to-the-cent decimal string, e.g. `"12.34"`) · `Breakdown (breakdown): AmountBreakdown?`
  - `PaymentSource`: set `Card (card): CardRequest?` for a direct card.
  - `CardRequest`: `Name (name): string?` (cardholder) · `Number (number): string?` (PAN) · `Expiry (expiry): string?`
    (`"YYYY-MM"`) · `SecurityCode (security_code): string?` (cvv) · `BillingAddress (billing_address): Address?` ·
    `VaultId (vault_id): string?` (see Step 10) · `Attributes (attributes): CardAttributes?` (vault-on-purchase) ·
    `ExperienceContext (experience_context): CardExperienceContext?`
  - `Address`: `AddressLine1 (address_line_1): string?` · `AddressLine2 (address_line_2): string?` · `AdminArea2 (admin_area_2): string?` (city) ·
    `AdminArea1 (admin_area_1): string?` (state) · `PostalCode (postal_code): string?` · `CountryCode (country_code): string !req`
- **Response — where authorization id + status live** (`Order`, `records-1`): `Id (id): string?` (order id) · `Status (status): OrderStatus?` ·
  `PurchaseUnits[]` → `PurchaseUnit.Payments (payments): PaymentCollection?` → `PaymentCollection.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`.
  Each `AuthorizationWithAdditionalData`: `Id (id): string?` (**authorization id** — feeds Steps 5/6/7) · `Status (status): AuthorizationStatus?` ·
  `Amount (amount): Money?` · `ExpirationTime (expiration_time): string?` · `ProcessorResponse (processor_response): ProcessorResponse?`.

### Step 4 — Authorize (inline vs separate call)

- **Primary path:** because `payment_source.card` is supplied at create time with intent=AUTHORIZE, the authorization is
  produced by `CreateOrder` and read from the Step-2 response path above. **No separate call is required for direct card.**
- **Fallback op (approval-redirect flow only):** `client.Orders.AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  → returns `OrderAuthorizeResponse`; error `SdkException<AuthorizeOrderError>` Case A, `TryGetError(out Error)` [400,401,403,404,422,500].
  `OrderAuthorizeResponse.PurchaseUnits[].Payments.Authorizations[]` holds id+status the same way. `map/operations/Orders.md`
- ⚠ **UNVERIFIED (live wire):** whether the sandbox returns the authorization *inline* in the `CreateOrder` response for a
  direct-card AUTHORIZE order, versus requiring the `AuthorizeOrder` follow-up, can only be confirmed against live traffic.
  **Defensive directive:** after `CreateOrder`, extract the authorization id best-effort from
  `Order.PurchaseUnits[0].Payments?.Authorizations` (first non-null `Id`); if that list is empty/absent, fall back to
  calling `AuthorizeOrder(orderId, null, <idempotencyKey>, null, null, body: null, ...)` and read it from the response —
  do not assume one shape.

### Step 5 — Capture the authorization ("fulfil")

- **Signature:** `client.Payments.CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  — 4 leading nullable params no default (pass `null` to skip; `payPalRequestId` = idempotency key). For a full capture pass
  `body: null`; for a partial/final capture pass a `CaptureRequest`. `map/operations/Payments.md`
- **`CaptureRequest`** (`records-1`): `Amount (amount): Money?` · `InvoiceId (invoice_id): string?` · `FinalCapture (final_capture): bool? = false` · `NoteToPayer`, `SoftDescriptor`.
- **Returns:** `CapturedPayment`. **Error:** `SdkException<CaptureAuthorizedPaymentError>` Case A —
  `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].
- **Response accessors** (`CapturedPayment`, `records-1`): `Id (id): string?` (capture id — feeds Step 8) · `Status (status): CaptureStatus?` ·
  `Amount (amount): Money?` (captured amount) · `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`.
  - `SellerReceivableBreakdown` (`records-2`): `GrossAmount (gross_amount): Money !req` · `PaypalFee (paypal_fee): Money?` · `NetAmount (net_amount): Money?` · `ReceivableAmount (receivable_amount): Money?`.
  - `Money`: `CurrencyCode (currency_code): string !req` · `Value (value): string !req`.
  - **Exact paths:** gross = `capturedPayment.SellerReceivableBreakdown?.GrossAmount?.Value` (+`.CurrencyCode`);
    PayPal fee = `…SellerReceivableBreakdown?.PaypalFee?.Value`; net to merchant = `…SellerReceivableBreakdown?.NetAmount?.Value`.

### Step 6 — Reauthorize a stale/expired authorization

- **Signature:** `client.Payments.ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`. `map/operations/Payments.md`
- **`ReauthorizeRequest`** (`records-2`): `Amount (amount): Money?` (only the amount is supported).
- **Returns:** `PaymentAuthorization` (`records-2`): `Id (id): string?`, `Status (status): AuthorizationStatus?`, `Amount`, `ExpirationTime (expiration_time): string?` — a reauthorized payment has a new honor period.
- **Error:** `SdkException<ReauthorizePaymentError>` Case A — `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].
- **Detecting "can no longer be reauthorized":** `AuthorizationStatus` has **no `EXPIRED` member** (members: `Created`,
  `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending`) — expiry is not a status value. It surfaces as an
  **error** on the reauthorize (or capture) call: read `ex.Error.TryGetError(out var e)` then `e.Name` and
  `e.Details[].Issue` (`ErrorDetails.Issue (issue): string !req`, plus `Field`, `Description`) to identify the failure and
  report it to an operator.
- ⚠ **UNVERIFIED (live wire):** the exact `Name` / `Details[].Issue` wire strings PayPal returns for a beyond-29-day /
  non-reauthorizable authorization are not in the map or source. **Defensive directive:** extract `e.Name` and each
  `e.Details[].Issue` best-effort, surface them verbatim in the operator report, and treat the reauthorize as failed;
  do not hard-code a single issue string as the sole trigger — fall back to "reauthorization rejected: <name/issue>".

### Step 7 — Void an authorization ("cancel before fulfil")

- **Signature:** `client.Payments.VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  — note the param order here is `payPalMockResponse, payPalAuthAssertion, payPalRequestId` (idempotency key is the 4th param). `map/operations/Payments.md`
- **Returns:** `PaymentAuthorization` (status transitions to `AuthorizationStatus.Voided`). Cannot void a fully-captured authorization.
- **Error:** `SdkException<VoidPaymentError>` Case A — `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].

### Step 8 — Refund a captured payment (full or partial)

- **Signature:** `client.Payments.RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  — `payPalRequestId` = idempotency key. **Full refund:** `body: null` (empty payload). **Partial refund:** supply `RefundRequest.Amount`. `map/operations/Payments.md`
- **`RefundRequest`** (`records-2`): `Amount (amount): Money?` · `CustomId`, `InvoiceId`, `NoteToPayer`, `PaymentInstruction`.
- **Returns:** `Refund` (`records-2`): `Id (id): string?` (refund id) · `Status (status): RefundStatus?` · `Amount (amount): Money?` ·
  `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (→ `GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount (total_refunded_amount): Money?`).
- **Error:** `SdkException<RefundCapturedPaymentError>` Case A — `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].
- **Never refund beyond captured:** read the captured amount first via
  `client.Payments.GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  → `CapturedPayment.Amount?.Value` (and `SellerReceivableBreakdown?.GrossAmount?.Value`); enforce
  `sum(refunds so far) + requested ≤ captured` before calling refund. `GetCapturedPayment` error is `SdkException<GetCapturedPaymentError>` Case A.

### Step 9 — Vault a card WITHOUT taking payment

Two supported direct-card routes (both browser-free). Prefer **setup-token → payment-token** if you want a stable
"cache then confirm" split; **direct payment-token** is a single call.

- **Route A — setup token then payment token:**
  1. `client.Vault.CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `SetupTokenResponse`. `map/operations/Vault.md`
     - `SetupTokenRequest` (`records-2`): `Customer (customer): Customer?` · `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req`.
     - `SetupTokenRequestPaymentSource`: set `Card (card): SetupTokenRequestCard?`.
     - `SetupTokenRequestCard`: `Name`, `Number`, `Expiry (YYYY-MM)`, `SecurityCode`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`, `VerificationMethod (verification_method): VaultCardVerificationMethod?` (`ScaWhenRequired`/`ScaAlways`), `ExperienceContext`.
     - `SetupTokenResponse`: `Id (id): string?` (setup token id) · `Status (status): PaymentTokenStatus? = Created`.
  2. `client.Vault.CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` with the token reference:
     - `PaymentTokenRequest`: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.
     - `PaymentTokenRequestPaymentSource`: set `Token (token): VaultTokenRequest?` = `new VaultTokenRequest { Id = <setupTokenId>, Type = VaultTokenRequestType.SetupToken }`
       (`VaultTokenRequest`: `Id (id): string !req`, `Type (type): VaultTokenRequestType !req`; `VaultTokenRequestType` sole member `SetupToken` wire `SETUP_TOKEN`).
- **Route B — direct card payment token (single call):** `CreatePaymentToken` with
  `PaymentTokenRequestPaymentSource.Card (card): PaymentTokenRequestCard?` = raw card
  (`PaymentTokenRequestCard`: `Name`, `Number`, `Expiry`, `SecurityCode`, `Brand (brand): CardBrand?`, `BillingAddress`).
- **Both return `PaymentTokenResponse`** (`records-2`): `Id (id): string?` (**vault/payment-token id** — feeds Steps 10/11) ·
  `Customer (customer): CustomerResponse?` · `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?`.
  - **SAFE descriptor (never PAN):** `PaymentTokenResponsePaymentSource.Card (card): CardPaymentTokenEntity?` →
    `Brand (brand): CardBrand?` · `LastDigits (last_digits): string?` · `Expiry (expiry): string?` · `Name`, `BillingAddress`.
    (`CardPaymentTokenEntity` exposes `LastDigits`, not a full number.)
- **Errors:** `CreateSetupToken` → `SdkException<CreateSetupTokenError>` Case A `TryGetError1(out Error1)` [400,403,422,500];
  `CreatePaymentToken` → `SdkException<CreatePaymentTokenError>` Case A `TryGetError1(out Error1)` [400,403,404,422,500].
  (Vault ops use `TryGetError1` returning the `Error1` model — `Name`, `Message`, `DebugId`, `Details: IReadOnlyList<ErrorDetails1>?`.)

### Step 10 — Pay an order using a vaulted card token

- Reuse `CreateOrder` (Step 2), but in `PaymentSource.Card` set **only** `CardRequest.VaultId (vault_id): string?` = the
  `PaymentTokenResponse.Id` from Step 9 — do **not** send `Number`/`SecurityCode`. Intent may be `Authorize` or `Capture`
  (`CheckoutPaymentIntent`). Everything else (amount, response paths) is identical to Step 2. `records-1` (`CardRequest`)

### Step 11 — Delete a vaulted payment token

- **Signature:** `client.Vault.DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` — returns `void` (`Task`). `map/operations/Vault.md`
- **Error:** `SdkException<DeletePaymentTokenError>` Case A — `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError(out RawError)` [fallback].

### Step 12 — Transaction reporting / reconciliation (paginated)

- **Signature:** `client.TransactionSearch.SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  — `startDate`/`endDate` are **required** ISO-8601 strings (wire `start_date`/`end_date`); the 8 middle `string?` filters have no
  default (pass `null` to skip). Call with **named arguments** (many optional params, no C# defaults on the filters). `map/operations/TransactionSearch.md`
- **Returns:** `SearchResponse` (`records-2`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` ·
  `Page (page): int?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Links`.
- **Pagination shape:** page/total_pages. Loop `page = 1 .. TotalPages` (increment `page:`, keep `startDate`/`endDate`/`pageSize`
  fixed) and concatenate `TransactionDetails` across pages to cover the whole range — the first response's `TotalPages`
  bounds the loop. There is no `perPage`/cursor; `pageSize` max is the server default 100.
- **Fields to line up against eShop orders** (`TransactionDetails.TransactionInfo`, type `TransactionInformation`, `records-2`):
  `TransactionId (transaction_id): string?` (identity) · `TransactionStatus (transaction_status): string?` (status, a raw string) ·
  `TransactionAmount (transaction_amount): Money?` (amount) · `TransactionInitiationDate`/`TransactionUpdatedDate` ·
  `InvoiceId (invoice_id): string?` and `CustomField (custom_field): string?` (use whichever you stamped at order-create to
  correlate). Default `fields = "transaction_info"` returns `TransactionInfo`; `PayerInfo`/`CartInfo` etc. require widening `fields`.
- **Error (Case B — the ONLY Case B op in scope):** `SdkException<RawError>` — no typed accessors. Read
  `ex.Error.StatusCode` (`HttpStatusCode`) and `ex.Error.ReadAsString()` / `ex.Error.ReadAsJson<T>()` for the body.

### Step 13 — Error handling boundary

- **Exception type:** every operation is **throw-based** and throws `SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`),
  exposing `.Error` of type `TError`. **No `…Result` no-throw variants exist anywhere in this SDK.**
- **Two cases:**
  - **Case A (39 of 40 ops, all in scope except Step 12):** `TError` is a generated `{Op}Error : ApiError`. Read the typed
    body via the op's `TryGet…(out …)` accessor (returns `true` when present); otherwise `TryGetRawError(out RawError)`.
    Orders/Payments ops use `TryGetError(out Error)`; Vault ops use `TryGetError1(out Error1)`; `SearchBalances` uses
    `TryGetDefaultError(out DefaultError)`. The typed payload records (`PayPalServerSdk.Models`):
    - `Error`: `Name (name): string !req` · `Message (message): string !req` · `DebugId (debug_id): string !req` · `Details (details): IReadOnlyList<ErrorDetails>?` · `Links`.
    - `ErrorDetails`: `Field (field): string?` · `Value (value): string?` · `Location (location): string? = "body"` · `Issue (issue): string !req` · `Description (description): string?`.
    - `Error1` / `ErrorDetails1` are the same shape with `ErrorLinkDescription` links (used by Vault ops).
  - **Case B (Step 12 only):** `TError` is `RawError` (`PayPalServerSdk.Core.ErrorResponse`): `StatusCode (HttpStatusCode)`,
    `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`.
- **Reading status/name/message/details/debug_id:** for Case A the HTTP status is exposed via the `RawError` fallback
  (`TryGetRawError(out var raw)` → `raw.StatusCode`); the structured fields come from `Error`/`Error1` (`Name`, `Message`,
  `DebugId`, `Details[].Issue`). `SdkException.Message` (the base `Exception`) is not the PayPal body — always read `.Error`.
- **Distinguishing an expired/non-reauthorizable authorization:** not a status enum — inspect `Error.Name` and
  `Error.Details[].Issue` on the reauthorize/capture failure (see Step 6, UNVERIFIED). Route those to the operator path;
  route generic 4xx/5xx to the standard failure path.

### Enum value tables actually needed (all `PayPalServerSdk.Models.Enums`, `StringEnum<T>` — use the C# member, not the wire literal)

| Enum | Members (`CSharpMember` → wire) |
|---|---|
| `CheckoutPaymentIntent` | `Capture`→CAPTURE, `Authorize`→AUTHORIZE |
| `OrderStatus` | `Created`, `Saved`, `Approved`, `Voided`, `Completed`, `PayerActionRequired`→PAYER_ACTION_REQUIRED |
| `AuthorizationStatus` | `Created`, `Captured`, `Denied`, `PartiallyCaptured`→PARTIALLY_CAPTURED, `Voided`, `Pending` (no `EXPIRED`) |
| `CaptureStatus` | `Completed`, `Declined`, `PartiallyRefunded`→PARTIALLY_REFUNDED, `Pending`, `Refunded`, `Failed` |
| `RefundStatus` | `Cancelled`, `Failed`, `Pending`, `Completed` |
| `PaymentTokenStatus` | `Created`, `PayerActionRequired`→PAYER_ACTION_REQUIRED, `Approved`, `Vaulted`, `Tokenized` |
| `CardBrand` | `Visa`, `Mastercard`, `Amex`→AMEX, `Discover`, `Jcb`→JCB, `Diners`, … (30 members; usually read-only from responses) |
| `VaultTokenRequestType` | `SetupToken`→SETUP_TOKEN |
| `VaultCardVerificationMethod` | `ScaWhenRequired`→SCA_WHEN_REQUIRED, `ScaAlways`→SCA_ALWAYS |

### Client construction / auth / server facts (recap)

- Credentials type `OAuth2ClientCredentials` (`…Core.Authentication.OAuth2.ClientCredentials`): `ClientId` (required),
  `ClientSecret` (required), `Scope` (optional). Set on `options.Oauth2` (or in the DI callback).
- Idempotency (Step 3): pass the caller key as the `payPalRequestId` string parameter on `CreateOrder`,
  `CaptureAuthorizedPayment`, `RefundCapturedPayment`, `ReauthorizePayment`, `VoidPayment`, `CreateSetupToken`,
  `CreatePaymentToken` (it maps to the `PayPal-Request-Id` header). It is a positional/named param, **not** a body field.

---

## Trap notes (load the named skill before writing that step — do not treat these as resolved)

- ⚠ Step 1 (client & DI) — HttpClient/handler lifetime and whether the SDK client is singleton vs transient over an
  `IHttpClientFactory` pipeline is a lifetime hazard the signature hides. **MUST load `dotnet-client-initialization`** before
  wiring `AddPayPalServerSdkClient` / `new PayPalServerSdkClient`.
- ⚠ Step 1 (auth) — *when* credentials must be set relative to client construction, and how to source the secret from
  config rather than hardcode. **MUST load `dotnet-authentication`** before wiring `Oauth2`.
- ⚠ Step 1 (base URL / resilience) — what the SDK's `Retry`/`Timeout` options actually bound (per-attempt vs whole call),
  and that a transport failure can re-send a non-idempotent write regardless of `HttpMethodsToRetry` — which is exactly why
  the `payPalRequestId` idempotency key on every write matters. **MUST load `dotnet-configuration-resilience`** before tuning
  retries/timeouts or setting the base URL.
- ⚠ Steps 2–12 (calling) — optional params with no C# default mis-bind in a positional call; list/search ops especially
  must use named arguments. **MUST load `dotnet-calling-endpoints`** before the first `client.X.Op(...)` call.
- ⚠ Steps 2/9 (models) — enums are `StringEnum<T>` (not C# enums; build from static members / `FromValue`), and JSON fields
  the model does not declare are dropped on deserialize. **MUST load `dotnet-models`** before building payloads or mapping responses.
- ⚠ Step 13 (error boundary) — how to read status on a Case A typed error, why `TryGetRawError` is not a catch-all, and the
  `JsonException` traps below. **MUST load `dotnet-error-handling`** before writing the try/catch.
- ⚠ Steps 5–8 (testing the money paths) — which seam to fake (the `HttpClient` ctor arg) when covering capture/refund/void
  edge cases. **MUST load `dotnet-testing`** before writing tests.

---

## REQUIRED READING — load BEFORE implementation starts (this sheet deliberately omits their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient ownership, DI registration |
| `dotnet-authentication` | Step 1 — supplying `Oauth2` client-id/secret, when to set credentials |
| `dotnet-configuration-resilience` | Step 1 — base-URL/server selection, retries, timeouts, pagination |
| `dotnet-calling-endpoints` | Steps 2–12 — required vs optional params, named-argument calls, async/`ct` |
| `dotnet-models` | Steps 2/9/10 — building request models, `StringEnum<T>`, required/nullable, wire names |
| `dotnet-error-handling` | Step 13 — exception types, Case A/B, reading status/body safely |
| `dotnet-testing` | Steps 5–8 — faking the `HttpClient` seam, covering error/edge paths |

Two `System.Text.Json.JsonException` hazards for the error boundary (`JsonException` reaches it from two directions,
needing opposite handling):

- a drifted or malformed **2xx** body (a missing `required` member — e.g. `AmountWithBreakdown.CurrencyCode`,
  `SellerReceivableBreakdown.GrossAmount`, `Error.Name`) surfaces as a `JsonException` from deserialization, **not** as an
  `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Op}Error` shape throws `JsonException` *while the error
  object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with
  it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller
  that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

1. **Plan-file path defaulted?** No — written to the exact path dictated by the brief
   (`…/repo/paypal-plan.md`).
2. **Direct-card + vault covers everything in scope, browser-free.** Confirmed from the contract: `CreateOrder`
   (`payment_source.card`), `CaptureAuthorizedPayment`, `ReauthorizePayment`, `VoidPayment`, `RefundCapturedPayment`,
   `CreateSetupToken`/`CreatePaymentToken` (card / setup-token source), `DeletePaymentToken`, and `SearchTransactions` all
   accept a direct-card or id-based input — **none requires a browser approval/challenge** in their request contract.
   **Caveat (not a blocker):** whether the sandbox business account actually completes a raw-PAN card AUTHORIZE without a
   3DS/SCA challenge depends on account configuration and live processor behaviour (the `CardRequest.ExperienceContext` /
   `attributes.verification` fields exist precisely because SCA *can* be demanded). This is a live-wire property. If the
   sandbox returns `PAYER_ACTION_REQUIRED`/a 3DS challenge, that specific order cannot be driven card-only — surface it to
   the operator rather than retrying. Labelled UNVERIFIED.
3. **Inline authorization vs `AuthorizeOrder`** (Step 4) and the **expired-authorization error strings** (Steps 6/13) are
   UNVERIFIED live-wire facts; both carry defensive directives above (extract best-effort, fall back). No open lookup remains.
4. **Config keys assumed:** `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:BaseUrl` (optional override), and a
   currency key (e.g. `PayPal:Currency`) feeding `AmountWithBreakdown.CurrencyCode`. Confirm exact key names against the
   `src/PublicApi` configuration binding when implementing.
5. **Amount formatting:** `Money.Value` / `AmountWithBreakdown.Value` are **strings** — format the exact-to-the-cent amount
   with invariant culture and the currency's decimal places (e.g. `"12.34"`), never a locale-formatted number.
6. No other blockers: every in-scope capability resolved to concrete signatures, wire names, envelope paths, error
   accessors, and enum values.
