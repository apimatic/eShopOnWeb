# PayPal .NET SDK integration plan — eShopOnWeb (sandbox: direct card auth/capture, vault, refunds, reconciliation)

**SDK identity** (map: `sdk-map.md`)

| | |
|---|---|
| NuGet package | `AsadAli.Checkout.Sdk` — install **version-less** (`dotnet add package AsadAli.Checkout.Sdk`), floats to latest; map documents release tag `v1.0.1` |
| Package TFM | `netstandard2.0` → drops into the net8.0 projects as-is |
| Root namespace | `PayPalServerSdk` |
| Client | `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` |
| Controllers | `client.Orders` · `client.Payments` · `client.Vault` · `client.TransactionSearch` (`Subscriptions` exists, out of scope) |
| Error model | throw-only; `SdkException<TError>`; 39 of 40 ops Case A (typed), `SearchTransactions` is the one Case B (`RawError`) op; **no `…Result` no-throw variants anywhere** |

**Package placement.** Reference the package from `src/Infrastructure` only. `ApplicationCore` stays SDK-free (define the payment/vault gateway interfaces there); `PublicApi` consumes those abstractions and never names an SDK type. Central package management: run `dotnet add package` from `src/Infrastructure` so the version lands in `Directory.Packages.props`.

---

## 1. Scope & sequence

| Step | Work | SDK operations used |
|---|---|---|
| 0 | Package + client registration + auth + BaseUrl override (Infrastructure DI) | — (client construction) |
| 1 | Authorize order total — raw card **or** vaulted card | `Orders.CreateOrder` → `Orders.AuthorizeOrder` |
| 2 | Capture an authorization at fulfilment | `Payments.CaptureAuthorizedPayment` (+ `Payments.GetAuthorizedPayment` pre-check) |
| 3 | Reauthorize a stale authorization | `Payments.ReauthorizePayment` |
| 4 | Void an authorization | `Payments.VoidPayment` |
| 5 | Refund a capture (full/partial, idempotent) | `Payments.RefundCapturedPayment` (+ `Payments.GetRefund`) |
| 6 | Vault a card; list; delete | `Vault.CreateSetupToken` → `Vault.CreatePaymentToken` (or one-step) · `Vault.ListCustomerPaymentTokens` · `Vault.GetPaymentToken` · `Vault.DeletePaymentToken` |
| 7 | Transaction search for reconciliation (full range, all pages) | `TransactionSearch.SearchTransactions` |
| 8 | Error boundary + status-to-operator mapping | all of the above |
| 9 | Tests against the SDK seam | — |

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

**Namespaces in play** (map: `sdk-map.md` + named source files): client/options/`ServerOptions` — `PayPalServerSdk` · controllers — `PayPalServerSdk.Api` · records — `PayPalServerSdk.Models` · enums — `PayPalServerSdk.Models.Enums` · `{Operation}Error` classes — `PayPalServerSdk.Errors` · `SdkException<T>` — `PayPalServerSdk.Core.Exceptions` · `RawError`/`ApiError` — `PayPalServerSdk.Core.ErrorResponse` · `RetryOptions`/`LoggingOptions` — `PayPalServerSdk.Core.Configuration` · `ServerEnvironment`/`DefaultOptions` — `PayPalServerSdk.Servers` · `OAuth2ClientCredentials` — `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` · `IOAuth2TokenStrategy<T>` — `PayPalServerSdk.Core.Authentication.OAuth2` · `RequestOptions` — `PayPalServerSdk.Core`.

### Step 0 — Client construction, auth, environment, BaseUrl override (map: `sdk-map.md`; source: `PayPalServerSdkClientOptions.cs`, `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/ServerEnvironment.cs`, `AuthSchemes.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`, `ServiceCollectionExtensions.cs`)

`PayPalServerSdkClientOptions` (ns `PayPalServerSdk`) properties: `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Logging: LoggingOptions` · `Server: ServerOptions` · `Oauth2: OAuth2ClientCredentials?` · `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.

```csharp
services.AddPayPalServerSdkClient(o =>
{
    o.Environment = ServerEnvironment.Sandbox;               // PayPalServerSdk.Servers — the ONLY member
    o.Oauth2 = new OAuth2ClientCredentials                   // PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials
    {
        ClientId = config["PayPal:ClientId"],                // required string (init)
        ClientSecret = config["PayPal:ClientSecret"],        // required string (init)
        // Scope: string? — optional
    };
    var baseUrl = config["PayPal:BaseUrl"];                  // optional override
    if (!string.IsNullOrWhiteSpace(baseUrl))
        o.Server.Default.Sandbox.BaseUrl = baseUrl;          // DefaultOptions.SandboxOptions.BaseUrl, default "https://api-m.sandbox.paypal.com"
});
```

Settled from source (no open lookups):

- **BaseUrl override covers EVERY call including the OAuth token request.** All controllers resolve URLs through `Server.Default(path)`, and the default token strategy is built as `OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), rawClient)` — both root at `ServerOptions.Default` (`DefaultOptions`, ns `PayPalServerSdk.Servers`) → `.Sandbox.BaseUrl`. Setting that one property satisfies the "verbatim base address for every PayPal call including token" requirement.
- **Token acquisition is SDK-managed.** Default strategy POSTs `grant_type=client_credentials` (form body) with HTTP Basic `clientId:clientSecret` to `{BaseUrl}/v1/oauth2/token`; caching is handled inside `OAuth2Scheme<OAuth2ClientCredentials>`. A custom `IOAuth2TokenStrategy<OAuth2ClientCredentials>` (`Task<OAuthToken> GetToken(OAuth2ClientCredentials credentials, CancellationToken cancellationToken)`) may be supplied via `Oauth2TokenStrategy` but is not needed.
- **Environment gap (flag, do not paper over):** `ServerEnvironment` has exactly one member, `Sandbox` (`Default()` = Sandbox). There is **no Live/Production member** in this release. `PayPal:Environment` config can only ever map to `Sandbox` — validate the config value equals `"Sandbox"` and fail fast otherwise; the only mechanical path to another host is the `BaseUrl` override above.
- **DI shape:** `AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>?)` registers the client as a **singleton** built on `IHttpClientFactory`. Manual alternative: `new PayPalServerSdkClient(httpClient, options)`.
- `RequestOptions` (last-but-one param of every call, ns `PayPalServerSdk.Core`) carries only `LogLevel?` — ignore for this integration.

### Step 1 — Authorize an order total (map: `operations/Orders.md`; models: `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`; enums: `enums.md`)

**1a. `client.Orders.CreateOrder`** — `POST /v2/checkout/orders`

```csharp
CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId,
    string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body,
    string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)
```
First 5 params nullable-no-default → **must pass explicitly** (`null` to skip). Returns `Order`. Error: `SdkException<CreateOrderError>` (Case A) — `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback.

`OrderRequest` (ns `PayPalServerSdk.Models`): `Intent (intent): CheckoutPaymentIntent !req` → `CheckoutPaymentIntent.Authorize` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` · `PaymentSource (payment_source): PaymentSource?` · `Payer (payer): Payer?` · `ApplicationContext (application_context): OrderApplicationContext?`.

`PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req` · `ReferenceId (reference_id): string?` ← put the local order id here · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `Description (description): string?` · `Items (items): IReadOnlyList<ItemRequest>?` · `Shipping (shipping): ShippingDetails?`.
`AmountWithBreakdown`: `CurrencyCode (currency_code): string !req` ← `PayPal:Currency` · `Value (value): string !req` ← decimal as string · `Breakdown (breakdown): AmountBreakdown?`.

**1b. `client.Orders.AuthorizeOrder`** — `POST /v2/checkout/orders/{id}/authorize`

```csharp
AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId,
    string? payPalAuthAssertion, OrderAuthorizeRequest? body,
    string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)
```
5 params (`payPalMockResponse`…`body`) nullable-no-default → **must pass explicitly**. Returns `OrderAuthorizeResponse`. Error: `SdkException<AuthorizeOrderError>` (Case A) — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)`.

`OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`. The payment source may be supplied on **either** `CreateOrder` (`OrderRequest.PaymentSource`) **or** `AuthorizeOrder` (the op's doc note: buyer approval "or a valid payment_source must be provided in the request"). Recommended: create with intent+purchase_units only, pass the payment source on `AuthorizeOrder` — one authoritative place.

**Payment-source variants** (`OrderAuthorizeRequestPaymentSource`; same two exist on `PaymentSource` for create):

| Variant | Shape |
|---|---|
| Raw card | `Card (card): CardRequest?` — `Number (number): string?` · `Expiry (expiry): string?` (`YYYY-MM`) · `SecurityCode (security_code): string?` · `Name (name): string?` · `BillingAddress (billing_address): Address?` |
| Vaulted card | `Card (card): CardRequest?` with **`VaultId (vault_id): string?`** = the vault payment-token id from step 6. This is the **only** SDK-exposed path for a vaulted card: `Token (token): Token?` takes `TokenType !req` whose sole member is `BillingAgreement (BILLING_AGREEMENT)` — it does not cover v3 vault payment tokens. |

`Address`: `CountryCode (country_code): string !req` · `AddressLine1 (address_line_1): string?` · `AddressLine2 (address_line_2): string?` · `AdminArea2 (admin_area_2): string?` (city) · `AdminArea1 (admin_area_1): string?` (state) · `PostalCode (postal_code): string?`.
Merchant-initiated subsequent use of a vaulted card: `CardRequest.StoredCredential (stored_credential): CardStoredCredential?` — `PaymentInitiator (payment_initiator): PaymentInitiator !req` (`Merchant (MERCHANT)`) · `PaymentType (payment_type): StoredPaymentSourcePaymentType !req` (`Unscheduled (UNSCHEDULED)` / `Recurring (RECURRING)`) · `Usage (usage): StoredPaymentSourceUsageType? = Derived`.

**Reading the authorize response** — `OrderAuthorizeResponse`: `Id (id): string?` (order id) · `Status (status): OrderStatus?` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` → `PurchaseUnit.Payments (payments): PaymentCollection?` → `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → first element: **`Id (id)` = authorization id** · **`Status (status): AuthorizationStatus?`** · **`ExpirationTime (expiration_time): string?`** (ISO-8601) · `Amount (amount): Money?` · `ProcessorResponse (processor_response): ProcessorResponse?`. Persist order id + authorization id + expiry against the local order.

**A declined card is a 2xx, not an exception** (map-grounded: the response model carries the decline): `Authorizations[0].Status == AuthorizationStatus.Denied` with `StatusDetails (status_details).Reason (reason): AuthorizationIncompleteReason?` (`PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)`) and `ProcessorResponse.ResponseCode (response_code): ProcessorResponseCode?` / `AvsCode` / `CvvCode`. The catch ladder alone never sees a decline — inspect status on the happy path. (Malformed card data instead surfaces as 422 via `TryGetError`.)

### Step 2 — Capture an authorization (map: `operations/Payments.md`; models: `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`)

**`client.Payments.CaptureAuthorizedPayment`** — `POST /v2/payments/authorizations/{authorization_id}/capture`

```csharp
CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId,
    string? payPalAuthAssertion, CaptureRequest? body,
    string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)
```
4 params (`payPalMockResponse`…`body`) nullable-no-default → **must pass explicitly**. Returns `CapturedPayment` (flat — no envelope wrapping). Error: `SdkException<CaptureAuthorizedPaymentError>` (Case A) — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

`CaptureRequest`: `Amount (amount): Money?` (omit → capture full remaining authorized amount) · `FinalCapture (final_capture): bool? = false` · `InvoiceId (invoice_id): string?` · `NoteToPayer (note_to_payer): string?` · `SoftDescriptor (soft_descriptor): string?` · `PaymentInstruction (payment_instruction): CapturePaymentInstruction?`. `Money`: `CurrencyCode (currency_code): string !req` · `Value (value): string !req`.

**Where the money fields are** — `CapturedPayment`: `Id (id): string?` (capture id — persist for refunds) · `Status (status): CaptureStatus?` · `StatusDetails (status_details): CaptureStatusDetails?` (`Reason (reason): CaptureIncompleteReason?`) · `Amount (amount): Money?` = captured amount · **`SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`** → `GrossAmount (gross_amount): Money !req` · **`PaypalFee (paypal_fee): Money?`** = PayPal processing fee · **`NetAmount (net_amount): Money?`** = net proceeds · `PaypalFeeInReceivableCurrency (paypal_fee_in_receivable_currency): Money?` · `ReceivableAmount (receivable_amount): Money?` · `ExchangeRate (exchange_rate): ExchangeRate?`. Map doc note: the breakdown "**is not available for transactions that are in pending state**" — null-guard it when `Status == CaptureStatus.Pending`. Also `FinalCapture (final_capture): bool?` · `CreateTime/UpdateTime (create_time/update_time): string?`.

Pre-check read: **`Payments.GetAuthorizedPayment`**`(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization` (`Status`, `ExpirationTime (expiration_time)`, `Amount`) — both nullable params must be passed explicitly. Error: `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500].

### Step 3 — Reauthorize a stale authorization (map: `operations/Payments.md`; models: `records-2-Pa-Ve.md`)

**`client.Payments.ReauthorizePayment`** — `POST /v2/payments/authorizations/{authorization_id}/reauthorize`

```csharp
ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion,
    ReauthorizeRequest? body, string? prefer = "return=minimal",
    RequestOptions? requestOptions = null, CancellationToken ct = default)
```
`payPalRequestId`, `payPalAuthAssertion`, `body` nullable-no-default → **must pass explicitly**. Returns `PaymentAuthorization`. Error: `SdkException<ReauthorizePaymentError>` (Case A) — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

`ReauthorizeRequest`: `Amount (amount): Money?` — the only field ("Supports only the `amount` request parameter", map doc note). Map doc-note window facts: reauthorize after the 3-day honor period, from day 4 to 29, once; at 30+ days a new authorization must be created instead.

**How "can no longer be reauthorized" surfaces:** a 4xx (expect 422/400) read via `TryGetError(out Error)` → `Error.Name (name): string` · `Message (message): string` · `DebugId (debug_id): string` · `Details (details): IReadOnlyList<ErrorDetails>?` (`Issue (issue): string !req`, `Field`, `Value`, `Description`). **The exact `name` string the API returns for an un-reauthorizable authorization is not part of the SDK surface — `UNVERIFIED`, only live traffic can confirm it.** Defensive directive (mandatory): treat **any** Case-A 4xx from `ReauthorizePayment` as "this authorization can no longer be reauthorized → operator must create a new authorization", and surface `Name` + `Message` + `DebugId` verbatim in the operator message; do not string-match a specific `Name` value. Cheap pre-check: `GetAuthorizedPayment` → `Status`/`ExpirationTime`.

### Step 4 — Void an authorization (map: `operations/Payments.md`)

**`client.Payments.VoidPayment`** — `POST /v2/payments/authorizations/{authorization_id}/void`

```csharp
VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion,
    string? payPalRequestId, string? prefer = "return=minimal",
    RequestOptions? requestOptions = null, CancellationToken ct = default)
```
**Parameter order differs from capture** — `payPalAuthAssertion` comes before `payPalRequestId`; use named arguments. All three nullable-no-default → **must pass explicitly**. No body. Returns `PaymentAuthorization` (expect `Status == AuthorizationStatus.Voided`). Error: `SdkException<VoidPaymentError>` (Case A) — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500]. Map doc note: "You cannot void an authorized payment that has been fully captured" — surface a 4xx here as an operator-actionable state conflict, not a retryable failure.

### Step 5 — Refund a capture, idempotently (map: `operations/Payments.md`; models: `records-2-Pa-Ve.md`)

**`client.Payments.RefundCapturedPayment`** — `POST /v2/payments/captures/{capture_id}/refund`

```csharp
RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId,
    string? payPalAuthAssertion, RefundRequest? body,
    string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)
```
4 params (`payPalMockResponse`…`body`) nullable-no-default → **must pass explicitly**. Returns `Refund`. Error: `SdkException<RefundCapturedPaymentError>` (Case A) — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

`RefundRequest`: `Amount (amount): Money?` — **omit for a full refund; the doc note says "include an empty payload", so pass `new RefundRequest()`, not `body: null`** · `InvoiceId (invoice_id): string?` · `CustomId (custom_id): string?` · `NoteToPayer (note_to_payer): string?` · `PaymentInstruction (payment_instruction): RefundPaymentInstruction?`.

`Refund` response: **`Id (id): string?`** = refund id · **`Status (status): RefundStatus?`** (`Completed (COMPLETED)`, `Pending (PENDING)`, `Failed (FAILED)`, `Cancelled (CANCELLED)`) · `StatusDetails (status_details): RefundStatusDetails?` (`Reason`: `RefundIncompleteReason.Echeck (ECHECK)`) · `Amount (amount): Money?` · `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` → `GrossAmount` · `PaypalFee` · `NetAmount` · `TotalRefundedAmount (total_refunded_amount): Money?` (running total — useful for partial-refund sequences) · `InvoiceId` · `CustomId` · `CreateTime/UpdateTime`.

**Idempotency (settled from source, `Api/Payments.cs` / `Api/Orders.cs`):** the `payPalRequestId` parameter is sent as the **`PayPal-Request-Id`** header; SDK param doc: "The server stores keys for 45 days." Same parameter exists on `CreateOrder`, `AuthorizeOrder`, `CaptureOrder`, `CaptureAuthorizedPayment`, `ReauthorizePayment`, `VoidPayment`, `CreateSetupToken`, `CreatePaymentToken`. Directive: derive one stable key per logical operation (e.g. `refund:{captureId}:{localRefundId}`) persisted with the local command, and re-send the same key on any retry — never a fresh GUID per attempt.

Follow-up read: **`Payments.GetRefund`**`(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Refund`. Error: `SdkException<GetRefundError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500].

### Step 6 — Vault a card / list / delete (map: `operations/Vault.md`; models: `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`; enums: `enums.md`)

SDK client doc note: the Vault controller wraps the Payment Method Tokens API v3, "*Available in the US only.*"

**6a. Two-step: `client.Vault.CreateSetupToken`** — `POST /v3/vault/setup-tokens`

```csharp
CreateSetupToken(string? payPalRequestId, SetupTokenRequest body,
    RequestOptions? requestOptions = null, CancellationToken ct = default)
```
`payPalRequestId` must be passed explicitly. Returns `SetupTokenResponse`. Error: `SdkException<CreateSetupTokenError>` (Case A) — `TryGetError1(out Error1)` [400, 403, 422, 500] · `TryGetRawError(out RawError)`.

`SetupTokenRequest`: `Customer (customer): Customer?` (`Id (id): string?` = PayPal customer id if already known · `MerchantCustomerId (merchant_customer_id): string?` = your local customer reference) · `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req` → `Card (card): SetupTokenRequestCard?` { `Number (number): string?` · `Expiry (expiry): string?` · `SecurityCode (security_code): string?` · `Name (name): string?` · `Brand (brand): CardBrand?` · `BillingAddress (billing_address): Address?` · `VerificationMethod (verification_method): VaultCardVerificationMethod?` (`ScaWhenRequired (SCA_WHEN_REQUIRED)`, `ScaAlways (SCA_ALWAYS)`) }.

`SetupTokenResponse`: **`Id (id): string?`** = setup token id · `Status (status): PaymentTokenStatus? = Created` · `Customer (customer): Customer?` · `PaymentSource (payment_source): SetupTokenResponsePaymentSource?` → `Card (card): SetupTokenResponseCard?` { `LastDigits (last_digits)` · `Brand (brand)` · `Expiry` · `VerificationStatus (verification_status): CardVerificationStatus?` } · `Links`.

**6b. `client.Vault.CreatePaymentToken`** — `POST /v3/vault/payment-tokens`

```csharp
CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body,
    RequestOptions? requestOptions = null, CancellationToken ct = default)
```
`payPalRequestId` must be passed explicitly. Returns `PaymentTokenResponse`. Error: `SdkException<CreatePaymentTokenError>` (Case A) — `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)`.

`PaymentTokenRequest`: `Customer (customer): Customer?` · `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` → **either** `Token (token): VaultTokenRequest?` { `Id (id): string !req` = the setup-token id from 6a · `Type (type): VaultTokenRequestType !req` = **`VaultTokenRequestType.SetupToken`** (sole member — settles the setup-token→payment-token conversion) } **or** `Card (card): PaymentTokenRequestCard?` { `Number` · `Expiry` · `SecurityCode` · `Name` · `Brand` · `BillingAddress` } — a **one-step direct vault** the SDK also exposes; both vault without a purchase, pick one (two-step keeps PAN handling in the setup-token call; one-step is fewer round trips).

`PaymentTokenResponse`: **`Id (id): string?` = the vault payment-token id — this is what `CardRequest.VaultId` takes at payment time** · `Customer (customer): CustomerResponse?` { **`Id (id): string?` = PayPal customer id — persist it; `ListCustomerPaymentTokens` needs it** · `MerchantCustomerId` } · `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `Card (card): CardPaymentTokenEntity?` { **`Brand (brand): CardBrand?`** · **`LastDigits (last_digits): string?`** · `Expiry (expiry)` · `Name` · `Type (type): CardType?` } — **safe display data; no full-PAN field exists on any vault response model** (map-grounded: `CardPaymentTokenEntity` has no `Number`). · `Links`.

**6c. `client.Vault.ListCustomerPaymentTokens`** — `GET /v3/vault/payment-tokens`

```csharp
ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1,
    bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)
```
Wire query: `customer_id` ← `customerId` (**PayPal** customer id, not your local id), `page_size`, `page`, `total_required`. Returns `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Customer (customer): VaultResponseCustomer?` · `Links`. **Pagination: no SDK iterator — manual page loop** (`page = 1 … TotalPages`; pass `totalRequired: true` so the totals are populated). Error: `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400, 403, 500].

**6d. `client.Vault.GetPaymentToken`**`(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentTokenResponse`. Error: `SdkException<GetPaymentTokenError>` — `TryGetError1(out Error1)` [403, 404, 422, 500].

**6e. `client.Vault.DeletePaymentToken`**`(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `void` (Task). Error: `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 500]. (`GetSetupToken` also exists if a setup token must be re-read.)

### Step 7 — Transaction search for reconciliation (map: `operations/TransactionSearch.md`; models: `records-2-Pa-Ve.md`)

**`client.TransactionSearch.SearchTransactions`** — `GET /v1/reporting/transactions`

```csharp
SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType,
    string? transactionStatus, string? transactionAmount, string? transactionCurrency,
    string? paymentInstrumentType, string? storeId, string? terminalId,
    string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y",
    int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)
```
`startDate`/`endDate` required; the 8 middle params (`transactionId`…`terminalId`) nullable-no-default → **must pass explicitly** (`null`). Wire: `start_date`, `end_date`, `transaction_id`, `transaction_type`, `transaction_status`, `transaction_amount`, `transaction_currency`, `payment_instrument_type`, `store_id`, `terminal_id`, `fields`, `balance_affecting_records_only`, `page_size`, `page`. Dates are raw `string` — the app formats them ISO-8601 (exact accepted precision is API-level, `UNVERIFIED` by the map; use UTC round-trip format). Map doc notes: up to 3h latency for transactions to appear; covers the previous three years; specifying optional filters empties `ending_balance`.

**Pagination (whole range): no SDK iterator — manual loop.** `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` · **`Page (page): int?`** · **`TotalPages (total_pages): int?`** · `TotalItems (total_items): int?` · `StartDate`/`EndDate` · `LastRefreshedDatetime` · `Links`. Loop `page = 1 … TotalPages` at `pageSize: 100`, aggregating `TransactionDetails`.

**Matching fields** — `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?`: **`TransactionId (transaction_id): string?`** · **`PaypalReferenceId (paypal_reference_id): string?`** with **`PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?`** — `Odr (ODR)` = order reference, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` · `TransactionEventCode (transaction_event_code): string?` · **`TransactionInitiationDate (transaction_initiation_date): string?`** · `TransactionUpdatedDate (transaction_updated_date): string?` · **`TransactionAmount (transaction_amount): Money?`** · **`FeeAmount (fee_amount): Money?`** · **`TransactionStatus (transaction_status): string?`** — a plain string, **not** an enum · **`InvoiceId (invoice_id): string?`** · **`CustomField (custom_field): string?`** ← echoes `custom_id` set at order time · `EndingBalance (ending_balance): Money?`. Match local orders on `InvoiceId`/`CustomField` and `PaypalReferenceId`(+`Odr`).

**Error: this is the SDK's only Case B operation** — `SdkException<RawError>`: `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes()`. No typed accessors; best-effort `ReadAsJson<SearchError>()` (`SearchError`: `Name`/`Message`/`DebugId` !req · `Details: IReadOnlyList<TransactionSearchErrorDetails>?` · `TotalItems`/`MaximumItems`) and fall back to the raw string.

### Step 8 — Error model (map: `sdk-map.md` error section; `operations/*`; models: `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`)

- All failures throw `SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`); `.Error` is the payload. **Case A** (every op above except search): typed `{Operation}Error : ApiError` (`PayPalServerSdk.Errors`) with status-mapped `TryGet…(out …)` returning `true` when that shape is present, plus inherited `TryGetRawError(out RawError)` fallback. **Case B** (`SearchTransactions` only): `SdkException<RawError>`.
- Typed payloads: Orders/Payments ops → **`Error`**: `Name (name): string !req` · `Message (message): string !req` · `DebugId (debug_id): string !req` · `Details (details): IReadOnlyList<ErrorDetails>?` (`Issue (issue): string !req` · `Field` · `Value` · `Description`) · `Links`. Vault ops → **`Error1`**: same core trio, `Details: IReadOnlyList<ErrorDetails1>?`, `Links: IReadOnlyList<ErrorLinkDescription>?` (`Rel` optional — the live API omits it on some errors, per the map's model note). Always log `DebugId`.
- Several Payments ops also expose `TryGetNoContent(out RawError)` [500] — a 500 there has **no** typed body; don't assume `TryGetError` covers every status.
- Status→operator mapping (map-grounded statuses per op): 400/422 validation (`Details[].Issue` tells you which field) · 401 auth/config · 403 not-enabled-for-capability (direct card / vault not on the merchant account) · 404 unknown id · 409 state conflict (void after full capture, duplicate capture) · 500 provider-side (`TryGetNoContent`).

### Enum values needed (map: `models/enums.md`; all ns `PayPalServerSdk.Models.Enums`, all `StringEnum<T>` records — use the static members, not C# enum syntax)

| Enum | Members used |
|---|---|
| `CheckoutPaymentIntent` | `Authorize (AUTHORIZE)` · `Capture (CAPTURE)` |
| `OrderStatus` | `Created (CREATED)` · `Saved (SAVED)` · `Approved (APPROVED)` · `Voided (VOIDED)` · `Completed (COMPLETED)` · `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)` · `Captured (CAPTURED)` · `Denied (DENIED)` · `PartiallyCaptured (PARTIALLY_CAPTURED)` · `Voided (VOIDED)` · `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)` · `Declined (DECLINED)` · `PartiallyRefunded (PARTIALLY_REFUNDED)` · `Pending (PENDING)` · `Refunded (REFUNDED)` · `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)` · `Failed (FAILED)` · `Pending (PENDING)` · `Completed (COMPLETED)` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)` · `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `RefundIncompleteReason` | `Echeck (ECHECK)` |
| `CardBrand` | `Visa (VISA)` · `Mastercard (MASTERCARD)` · `Discover (DISCOVER)` · `Amex (AMEX)` · … (29 members) |
| `PaymentTokenStatus` | `Created (CREATED)` · `PayerActionRequired (PAYER_ACTION_REQUIRED)` · `Approved (APPROVED)` · `Vaulted (VAULTED)` · `Tokenized (TOKENIZED)` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` (sole member) |
| `VaultCardVerificationMethod` | `ScaWhenRequired (SCA_WHEN_REQUIRED)` · `ScaAlways (SCA_ALWAYS)` |
| `PaymentInitiator` | `Customer (CUSTOMER)` · `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)` · `Recurring (RECURRING)` · `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)` · `Subsequent (SUBSEQUENT)` · `Derived (DERIVED)` |
| `PayPalReferenceIdType` | `Odr (ODR)` · `Txn (TXN)` · `Sub (SUB)` · `Pap (PAP)` |
| `ProcessorResponseCode` | full issuer-code list on the enums page (read, don't hardcode) |

---

## 3. Trap notes (hazard + consequence — load the named skill for the resolution)

- ⚠ Step 0 (client registration) — the built-in DI helper fixes a specific lifetime relationship between the singleton SDK client and the `IHttpClientFactory`-produced `HttpClient`; what that relationship is, and whether manual construction must mirror it, is not visible from the signatures. **MUST load `dotnet-client-initialization`** before wiring DI.
- ⚠ Step 0 (auth) — when the first token fetch happens, how expiry/refresh is driven, and what a null `Oauth2` does at call time are not visible from the options shape. **MUST load `dotnet-authentication`** before wiring credentials.
- ⚠ Steps 1–7 (every call) — the must-pass-explicitly nullable parameters mis-bind silently in positional calls; call every operation with named arguments. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 1–7 (models) — enums are `StringEnum<T>` records (no C#-enum switch exhaustiveness; construct via static members or `FromValue`), records are immutable with `required` init members, and unmodeled JSON fields are dropped on deserialize. **MUST load `dotnet-models`** before building request payloads or mapping responses.
- ⚠ Step 8 (error boundary) — which operations are Case A vs Case B, and why `TryGetRawError` is not a catch-all on typed errors, decides whether your catch ladder ever sees the payload. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ Steps 0, 5, 7 (resilience) — what `RetryOptions.Timeout` actually bounds, and which failures get retried on which HTTP verbs, decides whether a non-idempotent write can execute twice client-side (pair every write with `PayPal-Request-Id`); there is no built-in logging hook. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts.
- ⚠ Step 9 (tests) — the test seam is a specific constructor argument, not an interface you can substitute naively. **MUST load `dotnet-testing`** before stubbing the SDK.
- ⚠ Steps 1–5 (response completeness) — every write op defaults `prefer` to `"return=minimal"`; whether the minimal body carries the fields this integration reads (authorization id, seller breakdown) is API behavior the SDK surface does not settle. Directive: pass `prefer: "return=representation"` on `AuthorizeOrder`, `CaptureAuthorizedPayment`, `ReauthorizePayment`, `VoidPayment`, `RefundCapturedPayment`; if a field still comes back null, fall back to the matching `Get*` read (`GetAuthorizedPayment` / `GetCapturedPayment` / `GetRefund`), whose return types are the full resources.

## 4. REQUIRED READING (load all of these **before implementation starts** — this sheet deliberately does not carry their contents)

- `dotnet-client-initialization` — governs step 0 (client construction & DI).
- `dotnet-authentication` — governs step 0 (OAuth client-credentials wiring).
- `dotnet-calling-endpoints` — governs steps 1–7 (signatures, named arguments, envelopes).
- `dotnet-models` — governs steps 1–7 (records, `StringEnum<T>` enums, required members).
- `dotnet-error-handling` — governs step 8 (Case A/B boundary, accessors, the two hazards below).
- `dotnet-configuration-resilience` — governs steps 0/5/7 (retries, timeouts, pagination, logging).
- `dotnet-testing` — governs step 9 (the SDK test seam).

Mandatory hazard rows for the error boundary:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

Assumptions (each labeled):

- **Vaulted-card payment path** — `payment_source.card.vault_id` (=`CardRequest.VaultId`) is the only SDK-exposed way to pay with a v3 vault payment token; `PaymentSource.Token`/`TokenType` covers only `BILLING_AGREEMENT`. That the sandbox Orders API accepts `card.vault_id` is the SDK's designed path but is **`UNVERIFIED`** by anything in the map/source — only a live call confirms it.
- **Reauthorize terminal-failure signal** — the exact `Error.Name` string for "can no longer be reauthorized" is not in the SDK surface: **`UNVERIFIED`**. The sheet's directive (any Case-A 4xx ⇒ operator-actionable, create a new authorization) removes the dependency on the string.
- **Transaction-search date precision** — accepted ISO-8601 precision for `start_date`/`end_date` is API-level, **`UNVERIFIED`**; the SDK takes raw strings.
- **`prefer: "return=representation"`** — the parameter is a free-form string with default `"return=minimal"`; the representation value is the API's documented Prefer convention, not something the SDK validates — treated as a directive with a `Get*`-read fallback (see trap notes).
- **Payment-source placement** — supplying the payment source on `AuthorizeOrder` rather than `CreateOrder` is a recommendation; both are contractually exposed.
- **Full refund body** — pass `new RefundRequest()` (empty object), not `body: null`, per the operation's doc note "include an empty payload"; whether `null` would serialize an empty JSON body is an SDK-serialization detail the plan does not rely on.
- **`customerId` for token listing** is the PayPal customer id (`CustomerResponse.Id` returned at vault time), not `MerchantCustomerId` — persist both at vault time.
- **Sandbox test card without 3DS** — no `ExperienceContext`/3DS fields are set anywhere in this plan; if the sandbox account forces SCA, `CardRequest.ExperienceContext` (`CardExperienceContext`: `ReturnUrl`/`CancelUrl`) and `CardVerification` exist but would introduce a browser flow the brief excludes.

Blockers: none.

Explicit gaps to report upward (do not paper over):

1. **No Live environment.** `ServerEnvironment` has only `Sandbox` in this release; `PayPal:Environment` must be validated as `"Sandbox"` and anything else rejected at startup. The only mechanical path to another host is the `Server.Default.Sandbox.BaseUrl` override.
2. **No SDK pagination helpers** for `SearchTransactions` or `ListCustomerPaymentTokens` — manual page loops as specified.
3. **No no-throw call variants** — every operation is throw-only; the error boundary is mandatory, not optional.
