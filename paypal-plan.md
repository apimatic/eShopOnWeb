# PayPal .NET SDK integration plan — eShopOnWeb

SDK identity (map: `sdk-map.md`):

| | |
|---|---|
| NuGet package | `AsadAli.Checkout.Sdk` — install **version-less** (`dotnet add package AsadAli.Checkout.Sdk`) |
| Root namespace | `PayPalServerSdk` |
| Client / options | `PayPalServerSdkClient` / `PayPalServerSdkClientOptions` (root namespace) |
| SDK target | `netstandard2.0` → compatible with `net8.0` |
| Map provenance | source tag `v1.0.1`, commit `9653d18` |

Namespaces (C# does not import child namespaces transitively — one `using` per row):

| Contents | Namespace |
|---|---|
| Client, options, `ServerOptions` | `PayPalServerSdk` |
| Controllers (`client.Orders` …) | `PayPalServerSdk.Api` (accessed via client properties — no direct use needed) |
| Records (models) | `PayPalServerSdk.Models` |
| Enums (`StringEnum<T>`) | `PayPalServerSdk.Models.Enums` |
| Typed errors (`CreateOrderError` …) | `PayPalServerSdk.Errors` |
| `SdkException<TError>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` |
| `ServerEnvironment`, `DefaultOptions` | `PayPalServerSdk.Servers` |
| `OAuth2ClientCredentials`, `IOAuth2TokenStrategy<T>` | `PayPalServerSdk.Core.Authentication.OAuth2(.ClientCredentials)` |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` |
| `RequestOptions` | `PayPalServerSdk.Core` |

## 1. Scope & sequence

1. Install package; register client + options + auth + sandbox/BaseUrl override (DI).
2. **Authorize (step 1 of payment):** `Orders.CreateOrder` with intent `AUTHORIZE` + card `payment_source`; then `Orders.AuthorizeOrder`. Detect & reject 3DS/`PAYER_ACTION_REQUIRED`.
3. **Capture (at fulfilment):** `Payments.CaptureAuthorizedPayment` → read gross/fee/net from `SellerReceivableBreakdown`.
4. **Void:** `Payments.VoidPayment` to release a held authorization.
5. **Reauthorize:** `Payments.ReauthorizePayment` for stale authorizations; translate 422 into operator message.
6. **Refund:** `Payments.RefundCapturedPayment` (full = empty body, partial = `Amount`), idempotency via `payPalRequestId`.
7. **Vault:** `Vault.CreateSetupToken` → `Vault.CreatePaymentToken`; `Vault.ListCustomerPaymentTokens`; `Vault.DeletePaymentToken`; pay via `CardRequest.VaultId`.
8. **Reporting:** `TransactionSearch.SearchTransactions` with manual page loop over `TotalPages`.
9. Error boundary around all of the above (Case A typed / Case B raw + `JsonException` rows below).

## 2. Client construction, auth, environment, BaseUrl override

Sources: `sdk-map.md` *Getting a client* / *Servers & auth*; `PayPalServerSdkClientOptions.cs`; `Servers/DefaultOptions.cs`; `Servers/ServerEnvironment.cs`; `AuthSchemes.cs`; `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`.

```csharp
// shape only — companion skills govern the wiring (see Trap notes)
var options = new PayPalServerSdkClientOptions
{
    Environment = ServerEnvironment.Sandbox,                 // only member; also the default
    Oauth2 = new OAuth2ClientCredentials                    // PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials
    {
        ClientId = cfg["PayPal:ClientId"],                  // required (C# required)
        ClientSecret = cfg["PayPal:ClientSecret"],          // required (C# required)
        // Scope: string? optional
    },
    Server = new ServerOptions                              // root namespace PayPalServerSdk
    {
        Default = new DefaultOptions                        // PayPalServerSdk.Servers
        {
            Sandbox = new DefaultOptions.SandboxOptions
            {
                BaseUrl = cfg["PayPal:BaseUrl"]             // optional override, used VERBATIM
                    ?? "https://api-m.sandbox.paypal.com",  // generated default
            },
        },
    },
};
var client = new PayPalServerSdkClient(httpClient, options); // ctor: (HttpClient, PayPalServerSdkClientOptions)
```

- **BaseUrl override semantics (verified in source):** every operation URL is resolved through `Server.Default(path)` → `DefaultOptions.Resolve`, which prefixes the path with `Sandbox.BaseUrl`. The OAuth token request is built the same way — `AuthSchemes` calls `server.Default("/v1/oauth2/token")` — so the override covers the token request too. Set it to the scheme+host (e.g. `https://api-m.sandbox.paypal.com`); the SDK appends the path. When the config value is absent, leave the generated default.
- **DI alternative:** `services.AddPayPalServerSdkClient(o => { /* same property sets */ })` (`ServiceCollectionExtensions.cs`).
- `Oauth2TokenStrategy` (`IOAuth2TokenStrategy<OAuth2ClientCredentials>?`) is optional — leave null; the SDK installs its default client-credentials strategy.
- `RequestOptions` (per-call, last param before `ct`) carries only `LogLevel` — it is **not** an idempotency mechanism.

## 3. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits.

Model conventions (map: `records-*` headers): records immutable, `init`-only setters; `!req` = C# `required`; `T?` = optional. Enums are `StringEnum<T>` — use the static members shown (`CheckoutPaymentIntent.Authorize`), never C# enum syntax or raw strings. All models below are `PayPalServerSdk.Models`; all enums `PayPalServerSdk.Models.Enums`; all typed errors `PayPalServerSdk.Errors`.

### Step 2 — Create order (AUTHORIZE, direct card) — `client.Orders` (map: `operations/Orders.md`)

`CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Order`
The 5 nullable params have no defaults — pass explicitly (`null`), except set `payPalRequestId` to your idempotency key (this is the `PayPal-Request-Id` header). Recommend `prefer: "return=representation"`.
Error: `SdkException<CreateOrderError>` — `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback.

Request model (`records-1-Ac-Pa.md`):

- `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req` → `CheckoutPaymentIntent.Authorize` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` · `PaymentSource (payment_source): PaymentSource?` · `Payer (payer): Payer?` · `ApplicationContext (application_context): OrderApplicationContext?`
- `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req` · `ReferenceId (reference_id): string?` · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `Description (description): string?`
- `AmountWithBreakdown`: `CurrencyCode (currency_code): string !req` (from config) · `Value (value): string !req` — **string, not decimal**: format the order total with invariant culture, 2 decimals (`total.ToString("0.00", CultureInfo.InvariantCulture)`); this is how the to-the-cent equality is expressed · `Breakdown (breakdown): AmountBreakdown?`
- `PaymentSource`: `Card (card): CardRequest?` (plus `Token`, `Paypal`, wallets — unused here)
- `CardRequest`: `Number (number): string?` · `Expiry (expiry): string?` (`"YYYY-MM"`) · `SecurityCode (security_code): string?` · `Name (name): string?` · `BillingAddress (billing_address): Address?` · `Attributes (attributes): CardAttributes?` · `VaultId (vault_id): string?` (vaulted-card payments — step 7) · `StoredCredential (stored_credential): CardStoredCredential?`
- `Address`: `CountryCode (country_code): string !req` · `AddressLine1/AddressLine2 (address_line_1/2): string?` · `AdminArea2 (admin_area_2): string?` (city) · `AdminArea1 (admin_area_1): string?` (state) · `PostalCode (postal_code): string?`
- `CardAttributes`: `Verification (verification): CardVerification?` → `CardVerification.Method (method): OrdersCardVerificationMethod? = OrdersCardVerificationMethod.ScaWhenRequired` (default already `ScaWhenRequired`)

Response `Order`: `Id (id): string?` · `Status (status): OrderStatus?` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` · `Links (links): IReadOnlyList<LinkDescription>?` · `PaymentSource (payment_source): PaymentSourceResponse?`

**3DS / browser-challenge detection (reject, do not follow):** after both `CreateOrder` and `AuthorizeOrder`, check `Status == OrderStatus.PayerActionRequired` (wire `PAYER_ACTION_REQUIRED`) **or** `Links` containing a `LinkDescription` with `Rel (rel) == "payer-action"` (`Href (href): string !req`, `Rel (rel): string !req`, `Method (method): LinkHttpMethod?`). Either signal means the issuer demands a browser challenge → fail the payment with an operator/buyer message; never redirect. 3DS outcomes that completed inline surface in `PaymentSourceResponse.Card.AuthenticationResult` (`AuthenticationResponse`: `LiabilityShift`, `ThreeDSecure` → `ThreeDSecureAuthenticationResponse`: `AuthenticationStatus (ParesStatus)`, `EnrollmentStatus (EnrollmentStatus)`).

### Step 2b — Authorize the order — `client.Orders` (map: `operations/Orders.md`)

`AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `OrderAuthorizeResponse`
Pass the 4 nullable header params explicitly (`payPalRequestId` = idempotency key). `body` may be `null` when the card `payment_source` was already set on create; otherwise `OrderAuthorizeRequest { PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = new CardRequest { … } } }` (`OrderAuthorizeRequestPaymentSource.Card (card): CardRequest?`).
Error: `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)`.

Response `OrderAuthorizeResponse`: `Id`, `Status (OrderStatus?)`, `PurchaseUnits`, `Links` — same 3DS check as above. **Authorization id path:** `resp.PurchaseUnits[0].Payments.Authorizations[0].Id` — `PurchaseUnit.Payments (payments): PaymentCollection?` → `PaymentCollection.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → `.Id`, `.Status (AuthorizationStatus?)`, `.Amount (Money?)`, `.ExpirationTime (expiration_time): string?`. Persist the authorization id — capture/void/reauthorize all key off it.

### Step 3 — Capture the authorization — `client.Payments` (map: `operations/Payments.md`)

`CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `CapturedPayment`
Set `payPalRequestId` = idempotency key; recommend `prefer: "return=representation"` so the breakdown is populated.
Error: `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

- `CaptureRequest`: `Amount (amount): Money?` (omit/null = full remaining authorized amount; partial capture = `Money`) · `InvoiceId (invoice_id): string?` · `FinalCapture (final_capture): bool? = false` — **set `true` on the last/only capture** · `NoteToPayer (note_to_payer): string?` · `SoftDescriptor (soft_descriptor): string?`
- `Money`: `CurrencyCode (currency_code): string !req` · `Value (value): string !req`

Response `CapturedPayment` — money paths (`records-1-Ac-Pa.md`):

| What | Path |
|---|---|
| Capture id | `CapturedPayment.Id` |
| Status | `CapturedPayment.Status` → `CaptureStatus` (`Completed`, `Pending`, `Declined`, `Refunded`, `PartiallyRefunded`, `Failed`); reason in `StatusDetails.Reason` (`CaptureIncompleteReason`) |
| Captured (gross) amount | `CapturedPayment.Amount` (`Money`) and `CapturedPayment.SellerReceivableBreakdown.GrossAmount` |
| PayPal's fee | `CapturedPayment.SellerReceivableBreakdown.PaypalFee` (`Money?`; also `PaypalFeeInReceivableCurrency`) |
| Net proceeds to merchant | `CapturedPayment.SellerReceivableBreakdown.NetAmount` (`Money?`) |

`SellerReceivableBreakdown` (`records-2-Pa-Ve.md`): `GrossAmount (gross_amount): Money !req` · `PaypalFee (paypal_fee): Money?` · `NetAmount (net_amount): Money?` · `ReceivableAmount (receivable_amount): Money?` · `ExchangeRate`, `PlatformFees`. Note (map): breakdown is not available while the capture is `Pending` — read it only when `Status == CaptureStatus.Completed`.

### Step 4 — Void an authorization — `client.Payments` (map: `operations/Payments.md`)

`VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization`
**Parameter order differs from the other Payments ops** — `payPalAuthAssertion` comes before `payPalRequestId`; use named arguments. No body.
Error: `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`. 409 = conflict (e.g. already fully captured — cannot void).

Response `PaymentAuthorization`: `Id` · `Status (AuthorizationStatus?)` → expect `AuthorizationStatus.Voided` · `StatusDetails`, `Amount`, `ExpirationTime`, `Links`.

### Step 5 — Reauthorize a stale authorization — `client.Payments` (map: `operations/Payments.md`)

`ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization`
`ReauthorizeRequest`: `Amount (amount): Money?` — the **only** supported field.
Error: `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

Status rules (`AuthorizationStatus`: `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending`; map + operation notes):

- **Reauthorizable:** `Created` / `Pending`, and only within the window: from day 4 to day 29 after the original authorization (3-day honor period; each reauthorize starts a new 3-day honor period; allowed amount e.g. up to 115% of original, max +$75 in US).
- **Terminal (never renewable):** `Voided`, `Denied`, `Captured`, `PartiallyCaptured`; and **any** authorization ≥ 30 days old — PayPal requires creating a new authorization instead.
- **Failure surface when not renewable:** HTTP 422 → `SdkException<ReauthorizePaymentError>` → `ex.Error.TryGetError(out Error e)` → read `e.Name`, `e.Message`, `e.DebugId`, `e.Details[].Issue` / `.Description`. Translation directive: treat **any** 422 from this call as "authorization can no longer be reauthorized — a new authorization (new order) is required", and surface `Name` + `DebugId` to the operator. The exact `issue` strings PayPal returns for an expired/non-reauthorizable authorization are not enumerated in the SDK surface — `UNVERIFIED`; do not branch on specific issue strings, branch on the 422.

### Step 6 — Refund a capture — `client.Payments` (map: `operations/Payments.md`)

`RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Refund`
**Idempotency key = the `payPalRequestId` parameter** (`PayPal-Request-Id` header). 
Error: `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

- `RefundRequest`: `Amount (amount): Money?` · `CustomId`, `InvoiceId`, `NoteToPayer`, `PaymentInstruction` (all optional).
- **Full refund:** empty payload — pass `body: new RefundRequest()` (no `Amount`). **Partial refund:** `body: new RefundRequest { Amount = new Money { CurrencyCode = …, Value = "12.34" } }`.
- Response `Refund`: `Id` · `Status (RefundStatus?)` (`Completed`, `Pending`, `Failed`, `Cancelled`; reason `StatusDetails.Reason` → `RefundIncompleteReason`) · `Amount (Money?)` · `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount`).
- Status check: `Payments.GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, …)` → `Refund`.

### Step 7 — Vault / saved cards — `client.Vault` (map: `operations/Vault.md`)

The SDK exposes **both** setup tokens and payment tokens. Use **setup token → payment token**: `CreateSetupToken` is the transient first step that collects/verifies the card; `CreatePaymentToken` converts it into the durable vaulted instrument whose `Id` is the `vault_id` used for payments and listing. (Direct `CreatePaymentToken` with a card also exists, but the two-step flow is the one whose request source type — `VaultTokenRequestType.SetupToken` — the SDK models explicitly.)

| Op | Signature (must-pass-explicitly params marked *) | Returns | Error (Case A) |
|---|---|---|---|
| `CreateSetupToken` | `(string? payPalRequestId*, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `SetupTokenResponse` | `CreateSetupTokenError` — `TryGetError1(out Error1)` [400, 403, 422, 500] |
| `CreatePaymentToken` | `(string? payPalRequestId*, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenResponse` | `CreatePaymentTokenError` — `TryGetError1(out Error1)` [400, 403, 404, 422, 500] |
| `ListCustomerPaymentTokens` | `(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` — query wires: `customer_id`, `page_size`, `page`, `total_required` | `CustomerVaultPaymentTokensResponse` | `ListCustomerPaymentTokensError` — `TryGetError1(out Error1)` [400, 403, 500] |
| `GetPaymentToken` | `(string id, …)` | `PaymentTokenResponse` | `GetPaymentTokenError` — `TryGetError1(out Error1)` [403, 404, 422, 500] |
| `DeletePaymentToken` | `(string id, …)` | `void` (Task) | `DeletePaymentTokenError` — `TryGetError1(out Error1)` [400, 403, 500] |

Models (`records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`):

- `SetupTokenRequest`: `Customer (customer): Customer?` (`Customer`: `Id (id): string?` = PayPal customer id, `MerchantCustomerId (merchant_customer_id): string?` = your shopper id) · `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req` → `Card (card): SetupTokenRequestCard?` → `Number`, `Expiry`, `SecurityCode`, `Name`, `Brand (CardBrand?)`, `BillingAddress (Address?)`, `VerificationMethod (verification_method): VaultCardVerificationMethod?` (`ScaWhenRequired` / `ScaAlways`), `ExperienceContext`.
- `SetupTokenResponse`: `Id (id): string?` · `Status (status): PaymentTokenStatus? = Created` (`Created`, `PayerActionRequired`, `Approved`, `Vaulted`, `Tokenized` — if `PayerActionRequired`, a verification challenge is needed: reject, same policy as 3DS) · `Links`.
- `PaymentTokenRequest`: `Customer (customer): Customer?` · `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` → `Token (token): VaultTokenRequest?` → `VaultTokenRequest { Id (id): string !req = <setup-token id>, Type (type): VaultTokenRequestType !req = VaultTokenRequestType.SetupToken }`.
- `PaymentTokenResponse`: `Id (id): string?` — **the vault id to store against the shopper** · `Customer (CustomerResponse?)` · `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `Card (card): CardPaymentTokenEntity?`.
- `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Customer (VaultResponseCustomer?)`. Paginate manually: loop `page` 1…`TotalPages` (no SDK pager).
- **Safe display fields** (`CardPaymentTokenEntity`): `Brand (brand): CardBrand?` · `LastDigits (last_digits): string?` · `Expiry (expiry): string?` · `Name (name): string?`. There is **no** full-PAN field on any response model — never log/store request `Number` either.
- **Pay with a vaulted card** (new authorize order, step 2 shape): `OrderRequest.PaymentSource = new PaymentSource { Card = new CardRequest { VaultId = "<payment-token id>" } }`. `SecurityCode` may be supplied on `CardRequest` if your processor requires it for vaulted cards; `StoredCredential` (`CardStoredCredential`: `PaymentInitiator !req` → `PaymentInitiator.Customer`/`Merchant`, `PaymentType !req` → `StoredPaymentSourcePaymentType.OneTime`/`Recurring`/`Unscheduled`) is available when you need to declare card-on-file usage.

### Step 8 — Transaction search / reporting — `client.TransactionSearch` (map: `operations/TransactionSearch.md`)

`SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `SearchResponse`
`startDate`/`endDate` required — ISO-8601 strings (wires `start_date`/`end_date`). The 8 filter params (`transactionId`…`terminalId`) are nullable with no defaults — pass `null` explicitly. **Use named arguments** (see trap note).
Error: **Case B** — `SdkException<RawError>`: `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, or `ex.Error.ReadAsJson<DefaultError>()` → `DefaultError`: `Name`, `Message`, `DebugId` (all `string !req`), `Details: IReadOnlyList<TransactionSearchErrorDetails>?` (`Issue`, `Field`, `Description`).

Response `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` → `TransactionInfo (transaction_info): TransactionInformation?` → `TransactionId (transaction_id)`, `TransactionAmount (transaction_amount): Money?`, `FeeAmount (fee_amount): Money?`, `TransactionStatus (transaction_status): string?`, `TransactionInitiationDate`/`TransactionUpdatedDate`, `PaypalReferenceId`, … · paging fields: `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?` · `Links`.
**Pagination:** no SDK pager — loop `page` from 1 to `TotalPages` (re-issuing the call with `page: n`), concatenating `TransactionDetails`, to cover the whole range. Note (map): executed transactions take up to 3 hours to appear; range limited to the previous 3 years.

### Step 9 — Error handling (all operations)

Every operation is throw-only (no `…Result` variants anywhere in the SDK). All throw `SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`) with `.Error: TError`.

- **Case A (39 of 40 ops — everything above except SearchTransactions):** `TError` = `{Operation}Error : ApiError`. Accessors per operation rows above: `TryGetError(out Error)` (Orders/Payments) / `TryGetError1(out Error1)` (Vault) / `TryGetDefaultError(out DefaultError)` (SearchBalances) / `TryGetNoContent(out RawError)` (500 on Payments ops) / inherited `TryGetRawError(out RawError)` fallback. `TryGetRawError` is **not** a catch-all substitute for the typed accessors — check the typed accessor first.
- **Case B:** `SearchTransactions` only — `SdkException<RawError>`: `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`.
- PayPal error body shape (`Error` / `Error1`, `records-1-Ac-Pa.md`): `Name (name): string !req` · `Message (message): string !req` · `DebugId (debug_id): string !req` · `Details (details): IReadOnlyList<ErrorDetails>?` → `Issue (issue): string !req`, `Field (field): string?`, `Value (value): string?`, `Description (description): string?` · `Links`. (`Error1` identical except `Details: IReadOnlyList<ErrorDetails1>?`, `Links: IReadOnlyList<ErrorLinkDescription>?` — `ErrorLinkDescription.Rel` is nullable by design.)

## 4. Trap notes (hazards — load the named skill before coding that step)

> ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind `PayPalServerSdkClient` has lifetime rules the constructor signature does not show; building it per request will exhaust sockets. **MUST load `dotnet-client-initialization`** before wiring DI.
> ⚠ Step 1 (auth) — where credentials are set relative to client construction, and how the token strategy caches/refreshes, is not visible from the options shape. **MUST load `dotnet-authentication`**.
> ⚠ Steps 2–8 (every call) — many nullable parameters have no C# default and mis-bind in positional calls; call with named arguments. **MUST load `dotnet-calling-endpoints`**.
> ⚠ Steps 2–8 (models) — enums are `StringEnum<T>` (not C# enums), records are init-only with `required` members, and unmodeled JSON fields are silently dropped on deserialize. **MUST load `dotnet-models`**.
> ⚠ Step 3/6 (idempotency vs retries) — whether a failed non-idempotent write (`POST` capture/refund) can be re-sent by the SDK's retry pipeline, and what `Timeout` actually bounds, is a resilience-semantics question: always send `payPalRequestId` so a retried or duplicated write collapses server-side. **MUST load `dotnet-configuration-resilience`** before tuning retry/timeout.
> ⚠ Step 9 (error boundary) — Case A vs Case B differs per operation (see sheet); `TryGetRawError` is not a catch-all. **MUST load `dotnet-error-handling`**.
> ⚠ Tests — the test seam for stubbing the SDK is the `HttpClient` constructor argument. **MUST load `dotnet-testing`** before writing integration tests.

## 5. REQUIRED READING (load all before implementation starts — this sheet deliberately does not carry their contents)

- `dotnet-client-initialization` — step 1 (client construction, DI, HttpClient lifetime)
- `dotnet-authentication` — step 1 (credentials, token strategy)
- `dotnet-calling-endpoints` — steps 2–8 (invocation patterns, named arguments)
- `dotnet-models` — steps 2–8 (records, StringEnum, required members)
- `dotnet-error-handling` — step 9 (exception boundary, Case A/B mechanics)
- `dotnet-configuration-resilience` — steps 1, 3, 6, 8 (retries, timeouts, base URL, pagination)
- `dotnet-testing` — tests for the integration layer

Mandatory hazard rows for the error boundary (`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling):

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 6. Assumptions & Blockers

- Assumed the checkout amount is a single purchase unit; `AmountWithBreakdown.Value` is the order total formatted `"0.00"` invariant — currency comes from config (`CurrencyCode`).
- Assumed `prefer: "return=representation"` on authorize/capture so response payloads (breakdown, links) are fully populated; the SDK default is `"return=minimal"`.
- Reauthorize failure contract: the 422 surface is map-grounded, but the specific `Error.Name`/`Details[].Issue` strings for "no longer renewable" are not enumerated in the SDK surface — `UNVERIFIED`; the sheet directs branching on the 422 itself, not on issue strings.
- 3DS policy per the brief: any `PAYER_ACTION_REQUIRED` status or `rel: payer-action` link (order create/authorize, or setup-token verification) is detected and rejected; no approval round-trip is built.
- No blockers.
