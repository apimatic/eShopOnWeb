# PayPal integration plan — eShopOnWeb `src/PublicApi`

SDK: NuGet `AsadAli.Checkout.Sdk` · root namespace `PayPalServerSdk` · client `PayPalServerSdkClient` ·
map/source stamp: repo tag `v1.0.1`, commit `9653d18`. Every contract fact below is grounded in the
bundled SDK map (`operations/*.md`, `models/*.md`) or the named SDK source file.

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Add NuGet package + client registration/DI + auth + base-URL override | — (client construction) |
| 2 | `POST /api/orders` — create local order, then PayPal order, `intent=AUTHORIZE` | `Orders.CreateOrder` |
| 3 | `POST /api/orders/{orderId}/pay` — authorize (inline card **or** vault token) | `Orders.AuthorizeOrder` |
| 4 | `POST /api/orders/{orderId}/fulfil` — capture; on stale auth reauthorize, else actionable error | `Payments.CaptureAuthorizedPayment`, `Payments.ReauthorizePayment`, `Payments.GetAuthorizedPayment` |
| 5 | `POST /api/orders/{orderId}/cancel` — void the authorization | `Payments.VoidPayment` |
| 6 | `POST /api/orders/{orderId}/refunds` — full/partial refund with caller idempotency key | `Payments.RefundCapturedPayment`, `Payments.GetRefund` |
| 7 | `GET /api/reconciliation?from&to` — transaction search, all pages, match to local orders | `TransactionSearch.SearchTransactions` |
| 8 | `POST /api/payment-methods` — vault a card (direct, or setup-token sequence) | `Vault.CreatePaymentToken`, (`Vault.CreateSetupToken`) |
| 9 | `GET /api/payment-methods` — list shopper's vaulted cards | `Vault.ListCustomerPaymentTokens` |
| 10 | `DELETE /api/payment-methods/{id}` — delete a vaulted card | `Vault.DeletePaymentToken` |
| 11 | Error boundary mapping 4xx/409/422 → actionable API responses | all of the above |

Local persistence required (app-side, not SDK): per order — PayPal order id, authorization id, capture id,
refund ids; per shopper — PayPal **customer id** (`CustomerResponse.Id` from vault create) and payment-token ids.

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

**Namespaces in play** (sdk-map.md *Namespaces*): `PayPalServerSdk` (client, `PayPalServerSdkClientOptions`,
`ServerOptions` — root files) · `PayPalServerSdk.Servers` (`ServerEnvironment`, `DefaultOptions`) ·
`PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` (`OAuth2ClientCredentials`) ·
`PayPalServerSdk.Core.Configuration` (`RetryOptions`) · `PayPalServerSdk.Core` (`RequestOptions`) ·
`PayPalServerSdk.Core.ErrorResponse` (`RawError`, `ApiError`) · `PayPalServerSdk.Core.Exceptions`
(`SdkException<T>`) · `PayPalServerSdk.Models` (all records) · `PayPalServerSdk.Models.Enums` (all enums) ·
`PayPalServerSdk.Errors` (`CreateOrderError`, `AuthorizeOrderError`, … `Error1` payloads live in
`PayPalServerSdk.Models`).

**Response envelope:** there is **no `ApiResponse<T>` wrapper** — every operation returns the bare model
(`Task<Order>`, `Task<CapturedPayment>`, …). Errors surface only as thrown `SdkException<TError>`
(sdk-map.md *Error-handling model*). No-throw `…Result` variants: **absent across the whole SDK**.

### Step 2 — Create PayPal order (`operations/Orders.md`)

`client.Orders.CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<Order>`
- 5 nullable no-default params (`payPalMockResponse`…`payPalAuthAssertion`) **must be passed explicitly** (pass `null`); `body` is non-nullable.
- Error: `SdkException<CreateOrderError>` — `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)`.
- Idempotency: pass the caller key as `payPalRequestId:` (wire header `PayPal-Request-Id`).

Request model `OrderRequest` (`records-1-Ac-Pa.md`): `Intent (intent): CheckoutPaymentIntent !req`,
`PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`,
`Payer (payer): Payer?`, `ApplicationContext (application_context): OrderApplicationContext?`.
`PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req`, `ReferenceId (reference_id): string?`,
`CustomId (custom_id): string?`, `InvoiceId (invoice_id): string?`, `Description (description): string?`.
`AmountWithBreakdown`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req` — **string**
amount, format to the minor unit (e.g. `"49.99"`); currency from config. Set `CustomId`/`InvoiceId` to the
local order id — they come back on transactions for reconciliation.

Response `Order`: `Id (id): string?`, `Status (status): OrderStatus?`, `Intent (intent): CheckoutPaymentIntent?`,
`PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `Links (links): IReadOnlyList<LinkDescription>?`.
Persist `Order.Id`.

### Step 3 — Authorize (`operations/Orders.md`)

`client.Orders.AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<OrderAuthorizeResponse>`
- `id` = PayPal order id. 5 nullable no-default params must be passed explicitly.
- Error: `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError`.
- Pass `prefer: "return=representation"` — the default `"return=minimal"` can strip the fields step 4 reads.

Body `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`.
`OrderAuthorizeRequestPaymentSource` (`records-1-Ac-Pa.md`): `Card (card): CardRequest?`, `Token (token): Token?`,
`Paypal (paypal): PayPalWallet?`, `ApplePay …`, `GooglePay …`, `Venmo …`.

Variant (a) inline card — `CardRequest` (`records-1-Ac-Pa.md`): `Number (number): string?`,
`Expiry (expiry): string?` (`YYYY-MM`), `SecurityCode (security_code): string?`, `Name (name): string?`,
`BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?`,
`StoredCredential (stored_credential): CardStoredCredential?`, `Attributes (attributes): CardAttributes?`,
`ExperienceContext (experience_context): CardExperienceContext?`. `Address`: only
`CountryCode (country_code): string !req` is required (+ optional `AddressLine1 (address_line_1)`,
`AddressLine2`, `AdminArea2 (admin_area_2)` = city, `AdminArea1 (admin_area_1)` = state,
`PostalCode (postal_code)`). Inline PAN via API implies PCI SAQ D (noted on the `CardRequest` map row).

Variant (b) saved card — same `CardRequest` but set **`VaultId (vault_id)` = the vault payment-token id**
and `StoredCredential = new CardStoredCredential { PaymentInitiator = PaymentInitiator.Customer, PaymentType = StoredPaymentSourcePaymentType.Unscheduled, Usage = StoredPaymentSourceUsageType.Subsequent }`
(`CardStoredCredential`: `PaymentInitiator !req`, `PaymentType !req`, `Usage` defaults `Derived`;
compatibility note on its map row: `ONE_TIME` and `FIRST` pair only with `CUSTOMER`). Do **not** send PAN fields.

Response `OrderAuthorizeResponse`: `Id`, `Status (status): OrderStatus?`,
`PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`. Read the authorization id one envelope
level down: `PurchaseUnits[0].Payments (payments): PaymentCollection?` → `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → `[0].Id (id)`,
`.Status (status): AuthorizationStatus?`, `.ExpirationTime (expiration_time): string?`. Persist the
authorization id. (`PaymentCollection` also has `Captures`, `Refunds` — `records-2-Pa-Ve.md`.)

### Step 4 — Capture / reauthorize (`operations/Payments.md`)

`client.Payments.CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<CapturedPayment>`
- 4 nullable no-default params must be passed explicitly. Pass `prefer: "return=representation"`.
- Error: `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, **409**, **422**] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.

Body `CaptureRequest`: `Amount (amount): Money?`, `InvoiceId (invoice_id): string?`,
`FinalCapture (final_capture): bool? = false`, `NoteToPayer (note_to_payer): string?`.
`Money`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req`.

Response `CapturedPayment` (`records-1-Ac-Pa.md`): `Id (id)`, `Status (status): CaptureStatus?`,
`StatusDetails (status_details): CaptureStatusDetails?` (`.Reason: CaptureIncompleteReason?`),
`Amount (amount): Money?`, `FinalCapture (final_capture): bool?`,
**`SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`** —
`GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?`
(the three figures fulfil must record), `CreateTime/UpdateTime`. Persist capture id + breakdown.

**Stale/expired authorization handling.** `AuthorizationStatus` (`enums.md`) has members
`Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`,
`Voided (VOIDED)`, `Pending (PENDING)` — **there is no `Expired` member**. Detect staleness from
`PaymentAuthorization.ExpirationTime (expiration_time): string?` (ISO-8601) via
`GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentAuthorization>`.
Unknown status strings do **not** throw on deserialize (`StringEnumConverter` accepts any value —
`Core/Enum/StringEnum.cs`); compare against known members or read `.Value`.

`client.Payments.ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentAuthorization>`
- Error: `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500].
- Body `ReauthorizeRequest`: `Amount (amount): Money?` — **only `amount` is supported**.
- Constraints (map notes, `operations/Payments.md` + `records-2-Pa-Ve.md`): initial honor period 3 days;
  reauthorize allowed from day 4 to day 29; after 30 days from the original authorization you **must create a
  new authorization** (re-run `Orders.AuthorizeOrder` on the order) — surface that as the actionable error.
  Allowed amount is context/geography-dependent (e.g. US: up to 115% of original, increase ≤ $75).
  ⚠ The two generated docs conflict on frequency — the operation note says "multiple re-authorizations"
  within 29 days, the `ReauthorizeRequest` model summary says "only once from days four to 29". Defensive
  directive: attempt reauthorize **once**; on 422 treat as not-reauthorizable and go the
  new-authorization path. `UNVERIFIED` against live behaviour.

### Step 5 — Void (`operations/Payments.md`)

`client.Payments.VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentAuthorization>`
- 3 nullable no-default params must be passed explicitly (note the **parameter order**: `payPalAuthAssertion`
  comes *before* `payPalRequestId` here — use named arguments).
- Error: `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500]. 409 = already captured/voided — map to a domain conflict.
- Cannot void a fully-captured authorization (map note).

### Step 6 — Refund (`operations/Payments.md`)

`client.Payments.RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<Refund>`
- 4 nullable no-default params must be passed explicitly.
- **Idempotency: the caller-supplied key goes in `payPalRequestId:`** (wire header `PayPal-Request-Id`).
  Same key replayed → PayPal dedupes; distinct keys → distinct partial refunds. There is no other
  header-injection channel: `RequestOptions` carries only `LogLevel?` (`Core/RequestOptions.cs`).
- Error: `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500].

Body `RefundRequest`: `Amount (amount): Money?`, `CustomId (custom_id): string?`, `InvoiceId (invoice_id): string?`,
`NoteToPayer (note_to_payer): string?`. Full refund = **empty payload** — pass `new RefundRequest()` (sends
`{}`); partial = set `Amount`. Pass `prefer: "return=representation"`.

Response `Refund`: `Id (id)`, `Status (status): RefundStatus?`, `StatusDetails (status_details): RefundStatusDetails?`
(`.Reason: RefundIncompleteReason?`), `Amount (amount): Money?`,
`SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (incl. `TotalRefundedAmount (total_refunded_amount)`).
Status check later: `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<Refund>`.

### Step 7 — Reconciliation (`operations/TransactionSearch.md`)

`client.TransactionSearch.SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<SearchResponse>`
- 8 nullable no-default params (`transactionId`…`terminalId`) must be passed explicitly (pass `null`).
- **Error: Case B — `SdkException<RawError>`** (the only Case-B operation in the SDK). Read
  `ex.Error.StatusCode` + `ex.Error.ReadAsString()`; best-effort `ex.Error.ReadAsJson<DefaultError>()`
  (`DefaultError`: `Name`, `Message`, `DebugId`, `Details: IReadOnlyList<TransactionSearchErrorDetails>`
  with `Issue !req`/`Description`) with fallback to the raw string — live body shape `UNVERIFIED`.
- **Date format** (source XML docs, `Api/TransactionSearch.cs`): RFC 3339 §5.6 internet date-time,
  **seconds required** (e.g. `2026-08-01T00:00:00Z`); **maximum range 31 days** — chunk longer `from`/`to`
  ranges into ≤31-day windows. Data lag: transactions appear up to 3 hours after execution; 3-year lookback.
- **Pagination**: response `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`,
  `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`. Loop
  `page: 1 … TotalPages` at `pageSize: 100`. ⚠ The param doc self-contradicts ("zero-relative start index"
  vs. its own example "page=1 … returns the first 20 items" and default `1`) — follow the example: 1-based.
- **Matching fields** — `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?`
  (`records-2-Pa-Ve.md`): `TransactionId (transaction_id)` (17 chars; order ids 19), 
  **`PaypalReferenceId (paypal_reference_id)` + `PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?`**
  — `Odr (ODR)` = PayPal **order** id, `Txn (TXN)` = transaction (authorization/capture) id, `Sub (SUB)`,
  `Pap (PAP)` — plus `InvoiceId (invoice_id)`, `CustomField (custom_field)` (echo your `custom_id`),
  `TransactionAmount (transaction_amount): Money?`, `FeeAmount (fee_amount): Money?`,
  `TransactionStatus (transaction_status): string?`, `TransactionEventCode (transaction_event_code): string?`,
  `TransactionInitiationDate (transaction_initiation_date)`. Match local orders on
  `paypal_reference_id` (typed) and/or the `invoice_id`/`custom_id` you set at create.

### Steps 8–10 — Vault v3 (`operations/Vault.md`)

`client.Vault.CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentTokenResponse>`
- Error: `SdkException<CreatePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError`.
- Body `PaymentTokenRequest`: `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.
  `Customer`: `Id (id): string?` (PayPal customer id), `MerchantCustomerId (merchant_customer_id): string?`
  (your shopper key — supply it on first vault; persist the returned PayPal customer id).
  `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?`, `Token (token): VaultTokenRequest?`.
  `PaymentTokenRequestCard`: `Number (number)`, `Expiry (expiry)`, `SecurityCode (security_code)`,
  `Name (name)`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?` — all optional
  (`string?`), but number/expiry/security code are de facto required to vault a card.
- Response `PaymentTokenResponse`: **`Id (id): string?` = the vault/payment-token id to store**,
  `Customer (customer): CustomerResponse?` (`Id`, `MerchantCustomerId`),
  `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `Card (card): CardPaymentTokenEntity?`
  with safe descriptors **`Brand (brand): CardBrand?`, `LastDigits (last_digits): string?`**, `Expiry`,
  `VerificationStatus (verification_status): CardVerificationStatus?` — never PAN. `Links`.

**Setup-token sequence** (required when you want SCA/3DS verification *before* vaulting; direct
`CreatePaymentToken` with `Card` works without it):
1. `client.Vault.CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<SetupTokenResponse>`.
   Body `SetupTokenRequest`: `Customer (customer): Customer?`, `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req`
   → `Card (card): SetupTokenRequestCard?` = card fields + `VerificationMethod (verification_method): VaultCardVerificationMethod?`
   (`ScaWhenRequired (SCA_WHEN_REQUIRED)`, `ScaAlways (SCA_ALWAYS)`) + `ExperienceContext (experience_context): VaultCardExperienceContext?`.
   Response `SetupTokenResponse`: `Id (id)`, `Status (status): PaymentTokenStatus?` — `Created (CREATED)`,
   **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** = buyer must complete 3DS (use the `Links` redirect),
   `Approved (APPROVED)`, `Vaulted`, `Tokenized`.
2. Then `CreatePaymentToken` with `PaymentSource = new PaymentTokenRequestPaymentSource { Token = new VaultTokenRequest { Id = <setupTokenId>, Type = VaultTokenRequestType.SetupToken } }`
   (`VaultTokenRequest`: `Id (id): string !req`, `Type (type): VaultTokenRequestType !req` — only member `SetupToken (SETUP_TOKEN)`).

`client.Vault.ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<CustomerVaultPaymentTokensResponse>`
- **Scoping: `customerId` (wire `customer_id`) is the PayPal customer id** — the `CustomerResponse.Id`
  returned at vault time; the app must persist it per shopper. Error: `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400, 403, 500].
- Response `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`,
  `TotalItems (total_items): int?`, `TotalPages (total_pages): int?` — paginate `page` 1…TotalPages.

`client.Vault.DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task` (void)
- Error: `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 500].

`client.Vault.GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentTokenResponse>` (for single-card reads).

**Paying with a vaulted card (Orders v2 payment-source shape)** — two modeled routes:
- **Preferred (fully modeled):** `payment_source.card.vault_id` — set `CardRequest.VaultId` +
  `CardStoredCredential` as in Step 3 variant (b). Works identically on `OrderRequest.PaymentSource`
  (at create) and `OrderAuthorizeRequestPaymentSource` (at authorize).
- **Alternative:** `PaymentSource.Token (token): Token?` — `Token { Id (id): string !req, Type (type): TokenType !req }`.
  ⚠ `TokenType` (`enums.md` + `Models/Enums/TokenType.cs`) models **only `BillingAgreement (BILLING_AGREEMENT)`** —
  the vault-token wire type `PAYMENT_METHOD_TOKEN` is **not** a predefined member. Every enum exposes
  `public static T FromValue(string)` which accepts any value (`Models/Enums/TokenType.cs` →
  `StringEnum<T>.FromValueCore`), so `TokenType.FromValue("PAYMENT_METHOD_TOKEN")` compiles and serializes —
  but the live wire acceptance is `UNVERIFIED`. Prefer the `vault_id` route.
- **3DS/SCA contingencies:** on card payments set `CardRequest.Attributes = new CardAttributes { Verification = new CardVerification { Method = OrdersCardVerificationMethod.ScaWhenRequired } }`
  (that is the generated default; members `ScaAlways`, `ScaWhenRequired`, `_3DSecure (3D_SECURE)`, `AvsCvv`)
  and `CardRequest.ExperienceContext = new CardExperienceContext { ReturnUrl = …, CancelUrl = … }` for the
  3DS redirect. Order status **`OrderStatus.PayerActionRequired (PAYER_ACTION_REQUIRED)`** signals the buyer
  must complete authentication — the API must surface the redirect link (`Order.Links`) rather than treat
  the authorize as failed. Authentication outcome comes back on `CardResponse.AuthenticationResult`.

### Step 1 — Client construction, auth, environment, base-URL override

**Package** (sdk-map.md; repo check done): `AsadAli.Checkout.Sdk` is **not referenced** — absent from both
`src/PublicApi/PublicApi.csproj` and `Directory.Packages.props`. Central package management is on
(`ManagePackageVersionsCentrally=true`), so run `dotnet add src/PublicApi package AsadAli.Checkout.Sdk`
— it pins the resolved latest version into `Directory.Packages.props` and adds a version-less
`<PackageReference>` to `PublicApi.csproj`. Install version-less/floating to latest; do **not** pin a
version from memory (source tag `v1.0.1` builds package version `1.0.0`; NuGet may serve newer).

**Construction** (sdk-map.md *Getting a client* / *Servers & auth*; `PayPalServerSdkClientOptions.cs`,
`ServerOptions.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs`):

```csharp
new PayPalServerSdkClient(httpClient, new PayPalServerSdkClientOptions
{
    Environment = ServerEnvironment.Sandbox,                 // PayPalServerSdk.Servers
    Oauth2 = new OAuth2ClientCredentials                     // PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials
    {
        ClientId = config["PayPal:ClientId"]!,               // required init
        ClientSecret = config["PayPal:ClientSecret"]!,       // required init
    },
    Server = new ServerOptions                               // root namespace PayPalServerSdk
    {
        Default = new DefaultOptions                         // PayPalServerSdk.Servers
        {
            Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = baseUrlOverride } // default "https://api-m.sandbox.paypal.com"
        }
    }
})
```

- **Environment selection:** `ServerEnvironment` models **only `Sandbox`** (`Servers/ServerEnvironment.cs`)
  — there is no `Production` member. Config string selects environment by choosing the base URL:
  sandbox → `https://api-m.sandbox.paypal.com`, production → `https://api-m.paypal.com`, applied via
  `Server.Default.Sandbox.BaseUrl` as above.
- **The override applies to EVERY call including OAuth:** the token URL is built as
  `server.Default("/v1/oauth2/token")` (`AuthSchemes.cs`), resolved through the same
  `DefaultOptions.Sandbox.BaseUrl` (`Servers/DefaultOptions.cs`), and every API call goes through
  `Server.Default(path)` (`Server.cs`).
- **OAuth mechanics** (`OAuth2ClientCredentialsStrategy.cs`): default strategy posts
  `grant_type=client_credentials` form to `{BaseUrl}/v1/oauth2/token` with a Basic
  `Authorization` header (base64 `clientId:clientSecret`); replaceable via
  `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.
- **DI alternative** (`ServiceCollectionExtensions.cs`): `services.AddPayPalServerSdkClient(o => { … })`.
- `RetryOptions` (`Core/Configuration/RetryOptions.cs`): all members `required` — start from
  `RetryOptions.Default()`; members `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`,
  `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`.

### Error model (step 11) — `Core/Exceptions/SdkException.cs`, sdk-map.md *Error-handling model*

- Every failure throws `SdkException<TError>`; `ex.Error` is the payload. Of 40 operations, 39 are
  **Case A** (typed `{Operation}Error : ApiError`) and exactly one is **Case B**:
  `SearchTransactions` → `SdkException<RawError>`.
- Case A accessors per operation are in the tables above (`TryGetError(out Error)` for Orders/Payments,
  `TryGetError1(out Error1)` for Vault, `TryGetNoContent(out RawError)` for Payments 500s,
  `TryGetRawError(out RawError)` fallback on all).
- Payload `Error` (`records-1-Ac-Pa.md`): `Name (name): string !req`, `Message (message): string !req`,
  `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?` —
  `ErrorDetails`: **`Issue (issue): string !req`**, `Description (description): string?`,
  `Field (field): string?`, `Value (value): string?`, `Location (location): string?`.
  Vault's `Error1`/`ErrorDetails1` are identical except links are `ErrorLinkDescription` (`Rel` optional —
  the live API omits `rel` on `RESOURCE_NOT_FOUND` doc links; map row note). Map
  `name` + `details[].issue`/`description` to actionable API responses; HTTP status comes from which
  accessor returned true (statuses listed per operation above) or `RawError.StatusCode`.

### Enum values needed (`map/models/enums.md`) — namespace `PayPalServerSdk.Models.Enums`, `StringEnum<T>` records, not C# enums

| Enum | Members (C# member = wire value) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` — **no `Expired` member; use `ExpirationTime`** |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `RefundIncompleteReason` | `Echeck (ECHECK)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `CardVerificationStatus` | `Verified (VERIFIED)`, `Failed (FAILED)` |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` only — see Step 3/8 note re `FromValue("PAYMENT_METHOD_TOKEN")` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `VaultCardVerificationMethod` | `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `ScaAlways (SCA_ALWAYS)` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `CardBrand` / `CardType` | `Visa`, `Mastercard`, `Amex`, `Discover`, … / `Credit`, `Debit`, `Prepaid`, `Store`, `Unknown` (display only) |

Construct via static members or `EnumType.FromValue("WIRE")`; unknown wire values deserialize without
throwing (`Core/Enum/StringEnum.cs`).

## 3. Trap notes

> ⚠ Step 1 (client registration) — the SDK wraps a caller-supplied `HttpClient`; how that client/handler
> is produced and lived (vs. the SDK wrapper's own lifetime) is not visible from the constructor signature,
> and getting it wrong sockets-exhausts under load. **MUST load `dotnet-client-initialization`** before
> wiring DI.

> ⚠ Step 1 (auth) — credentials must be set on the options before the client is constructed / in the DI
> callback, and secrets come from configuration, not code. **MUST load `dotnet-authentication`**.

> ⚠ Steps 2–10 (every call) — most optional parameters have no C# default and mis-bind positionally; call
> with named arguments (and the token parameter really is `ct:`). **MUST load `dotnet-calling-endpoints`**.

> ⚠ Steps 2–10 (models) — enums are `StringEnum<T>` records (not C# enums), records are immutable with
> `required` init members, and JSON fields the SDK doesn't model are silently dropped on deserialize —
> which is exactly how a "missing" fee/breakdown field reads as null. **MUST load `dotnet-models`**.

> ⚠ Step 4 (capture/reauthorize) + step 6 (refund) — whether a failed write can be safely re-sent is not
> uniform across verbs and failure kinds in this SDK's retry layer; a non-idempotent `POST` can execute
> more than once unless you pass `payPalRequestId` on every write. **MUST load
> `dotnet-configuration-resilience`** before tuning `Retry`/`Timeout`.

> ⚠ Step 7 (reconciliation) — `SearchTransactions` is the SDK's only Case-B operation: there is no typed
> error, so status/body handling differs from every other call in this plan. **MUST load
> `dotnet-error-handling`**.

> ⚠ Step 11 (error boundary) — which exception types actually reach your `catch`, and how to read status
> and body without destroying either, is a per-operation Case A/B question, not a single catch.
> **MUST load `dotnet-error-handling`** before writing the boundary.

> ⚠ Tests — the test seam is the `HttpClient` constructor argument, not mocking SDK internals.
> **MUST load `dotnet-testing`** before stubbing.

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

- `dotnet-client-initialization` — governs step 1 (client construction, HttpClient lifetime, DI).
- `dotnet-authentication` — governs step 1 (credentials wiring).
- `dotnet-calling-endpoints` — governs steps 2–10 (call shape, named args, async/ct).
- `dotnet-models` — governs steps 2–10 (records, enums, required members, wire names).
- `dotnet-error-handling` — governs step 11 and every `try/catch` (Case A/B, accessors, JsonException).
- `dotnet-configuration-resilience` — governs step 1 tuning + steps 4/6/7 (retries, timeouts, pagination).
- `dotnet-testing` — governs the integration tests.

Always include, verbatim, **both** of these hazard rows — `System.Text.Json.JsonException`
reaches the boundary from two directions and they need opposite handling:
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

**Assumptions**
- Currency comes from config (single currency per deployment); amounts are formatted as decimal strings
  in the major unit (`"49.99"`) — `Money.Value`/`AmountWithBreakdown.Value` are `string`.
- The app persists per-order PayPal ids (order → authorization → capture → refunds) and per-shopper the
  PayPal customer id + vault token ids; the SDK has no merchant-side listing of orders — reconciliation
  depends on Transaction Search plus these stored ids.
- Vault note on the client XML doc (`PayPalServerSdkClient.cs`): the Vault controller is "Available in the
  US only" — confirm the merchant account qualifies before shipping Flow 2.
- `ServerEnvironment` models only `Sandbox`; "production" is expressed as the base-URL override
  (`https://api-m.paypal.com`) on the sandbox server node — mechanically grounded in
  `Servers/DefaultOptions.cs`/`AuthSchemes.cs`; live production traffic through this override is `UNVERIFIED`.
- `TokenType.FromValue("PAYMENT_METHOD_TOKEN")` for the `payment_source.token` route is `UNVERIFIED`
  against the live API; the `payment_source.card.vault_id` route is fully modeled and preferred.
- `SearchTransactions` live error body assumed to resemble `DefaultError`; read best-effort via
  `ReadAsJson<DefaultError>()`, fall back to `ReadAsString()` — `UNVERIFIED`.
- Reauthorize frequency docs conflict (operation note vs. model summary); plan attempts one reauthorize
  then falls back to a fresh authorization — see Step 4.

**Blockers** — none.
