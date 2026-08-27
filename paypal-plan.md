# PayPal integration plan — eShopOnWeb `src/PublicApi` (.NET 8, ASP.NET Core, JWT-authenticated REST API)

SDK: `AsadAli.Checkout.Sdk` (NuGet) — install **version-less** (`dotnet add package AsadAli.Checkout.Sdk`, floats to latest; this sheet is grounded against release tag `v1.0.1`, source commit `9653d18`). Root namespace `PayPalServerSdk`. Target environment: **PayPal Sandbox** (`ServerEnvironment.Sandbox`). Map provenance: `sdk-map.md` @ `v1.0.1`.

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Install package; construct/DI-register the client for Sandbox with OAuth client-credentials and (if needed) a base-URL override | — (client options) |
| 2 | Save a card: setup-token flow (buyer-present) or direct vault (server-side, PCI scope) | `Vault.CreateSetupToken`, `Vault.CreatePaymentToken`, `Vault.GetPaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.DeletePaymentToken` |
| 3 | Create order, intent `AUTHORIZE`, paid with raw card **or** vaulted card | `Orders.CreateOrder` |
| 4 | Authorize the order; read authorization id/status/amount | `Orders.AuthorizeOrder` (+ `Orders.GetOrder`, `Payments.GetAuthorizedPayment` for reads) |
| 5 | Capture the authorization later; read capture id/status/amount/fee/net | `Payments.CaptureAuthorizedPayment` (+ `Payments.GetCapturedPayment`) |
| 6 | Reauthorize a stale authorization before capture | `Payments.ReauthorizePayment` |
| 7 | Void an authorization (cancel before fulfilment) | `Payments.VoidPayment` |
| 8 | Refund a capture (full/partial) with caller idempotency key | `Payments.RefundCapturedPayment` (+ `Payments.GetRefund`) |
| 9 | Reconciliation: transaction search over a date range, all pages | `TransactionSearch.SearchTransactions` |
| 10 | Cross-cutting: error boundary, resilience, tests | all of the above |

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

### 2.0 Client construction, auth, servers (map: `sdk-map.md` *Getting a client* / *Servers & auth*; source-verified where noted)

| Fact | Value |
|---|---|
| Client class | `PayPalServerSdk.PayPalServerSdkClient` — ctor `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` |
| Options class | `PayPalServerSdk.PayPalServerSdkClientOptions` — properties: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `Oauth2: OAuth2ClientCredentials?`, `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?` |
| DI registration | `services.AddPayPalServerSdkClient(o => { /* set o.Environment, o.Oauth2, … */ })` (`ServiceCollectionExtensions.cs`) |
| Environment | `PayPalServerSdk.Servers.ServerEnvironment.Sandbox` (only member; `ServerEnvironment.Default()` = Sandbox) |
| Credentials | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials` — `ClientId: string` (**required** init), `ClientSecret: string` (**required** init), `Scope: string?` (source: `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`) |
| Custom token strategy | `PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>` — `Task<OAuthToken> GetToken(OAuth2ClientCredentials credentials, CancellationToken cancellationToken)`; token caching is handled by the SDK scheme, not the strategy (source: `Core/Authentication/OAuth2/IOAuth2TokenStrategy.cs`) |
| **Base-URL override (ALL calls incl. token)** | `options.Server.Default.Sandbox.BaseUrl = "https://your-host"` — `ServerOptions` (root namespace `PayPalServerSdk`, file `ServerOptions.cs`) → `Default: PayPalServerSdk.Servers.DefaultOptions` → `Sandbox: DefaultOptions.SandboxOptions` → `BaseUrl: string` (default `https://api-m.sandbox.paypal.com`). The OAuth token request URL is built through the same resolver (`server.Default("/v1/oauth2/token")` in `AuthSchemes.cs`), so this one override covers **every** API call **and** the token request (source-verified: `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs`, `Server.cs`) |
| Token request shape (default strategy) | `POST {BaseUrl}/v1/oauth2/token`, `Authorization: Basic base64(clientId:clientSecret)`, form body `grant_type=client_credentials` (+`scope` when set); tokens are cached by the SDK until expiry and fetched under a lock (source: `OAuth2ClientCredentialsStrategy.cs`, `OAuth2Scheme.cs`) |
| Per-request options | `PayPalServerSdk.Core.RequestOptions` — only member `LogLevel: LogLevel?`; **no** per-request header hook — idempotency goes through the `payPalRequestId` parameter (source: `Core/RequestOptions.cs`) |
| Retry options | `PayPalServerSdk.Core.Configuration.RetryOptions` — all members `required` (`StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout: TimeSpan?`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`) or start from `RetryOptions.Default()` (map: `sdk-map.md` client-options) |
| `using` directives needed | `PayPalServerSdk` · `PayPalServerSdk.Servers` · `PayPalServerSdk.Api` · `PayPalServerSdk.Models` · `PayPalServerSdk.Models.Enums` · `PayPalServerSdk.Errors` · `PayPalServerSdk.Core` (`RequestOptions`) · `PayPalServerSdk.Core.Exceptions` (`SdkException<T>`) · `PayPalServerSdk.Core.ErrorResponse` (`RawError`) · `PayPalServerSdk.Core.Configuration` (`RetryOptions`) · `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` (`OAuth2ClientCredentials`) |

**Idempotency mechanism (all mutating ops below):** pass a caller-generated key in the `payPalRequestId` parameter → sent as the `PayPal-Request-Id` header. Server-side key retention differs per controller (source: `payPalRequestId` doc comments in `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`): **Orders ops — 6 hours** (extendable to 72 via the account manager) and the key is **mandatory for single-step create-order calls** — i.e. a `CreateOrder` that already carries payment source information ("Card, PayPal.vault_id, PayPal.billing_agreement_id, etc."); **Payments ops — 45 days**; **Vault ops — 3 hours**. The SDK additionally auto-sends `Idempotency-Key: Guid.NewGuid()` on these POSTs (source: `Api/Payments.cs`) — caller-controlled idempotency is via `payPalRequestId`; reuse the same key on safe retries of the same logical operation.

### 2.1 Orders — `client.Orders` (map: `operations/Orders.md`)

**CreateOrder** — `POST /v2/checkout/orders`
`CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<Order>`
- The 5 nullable string params have **no C# default — pass explicitly** (`null` to skip). `payPalRequestId` = idempotency key.
- **Single-step behavior (source: `Api/Orders.cs` CreateOrder `payPalRequestId` doc):** supplying `payment_source` at create (raw card, vaulted card via `CardRequest.VaultId`, or billing agreement) makes this a *single-step create order call* — with `Intent = Authorize` the authorization is performed by the create call itself, and `payPalRequestId` is **mandatory** on such calls. Read the authorization from the create response at `Order.PurchaseUnits[i].Payments.Authorizations[j]` (requires `prefer: "return=representation"`); call `AuthorizeOrder` **only** when the create response carries no authorization. A second authorization on an already-authorized order fails with 422 `ORDER_ALREADY_AUTHORIZED` ("If 'intent=AUTHORIZE' only one authorization per order is allowed").
- Error: `SdkException<PayPalServerSdk.Errors.CreateOrderError>` (Case A) — `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback].

Request model `PayPalServerSdk.Models.OrderRequest` (map: `records-1-Ac-Pa.md`):
- `Intent (intent): CheckoutPaymentIntent` **!req** → `CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`)
- `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest>` **!req**
- `PaymentSource (payment_source): PaymentSource?` · `Payer (payer): Payer?` · `ApplicationContext (application_context): OrderApplicationContext?`

`PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown` **!req** · `ReferenceId (reference_id): string?` · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `Description (description): string?` · `Items (items): IReadOnlyList<ItemRequest>?` · `Shipping (shipping): ShippingDetails?`
`AmountWithBreakdown`: `CurrencyCode (currency_code): string` **!req** · `Value (value): string` **!req** (string decimal, e.g. `"100.00"`) · `Breakdown (breakdown): AmountBreakdown?`
`Money`: `CurrencyCode (currency_code): string` **!req** · `Value (value): string` **!req**

`PaymentSource` (pick exactly one) — relevant members: `Card (card): CardRequest?` · `Token (token): Token?`
`CardRequest` (raw card): `Number (number): string?` · `Expiry (expiry): string?` (`YYYY-MM`) · `SecurityCode (security_code): string?` · `Name (name): string?` · `BillingAddress (billing_address): Address?` · `VaultId (vault_id): string?` · `StoredCredential (stored_credential): CardStoredCredential?` · `Attributes (attributes): CardAttributes?`
`Address`: `CountryCode (country_code): string` **!req** · `AddressLine1 (address_line_1): string?` · `AddressLine2 (address_line_2): string?` · `AdminArea2 (admin_area_2): string?` (city) · `AdminArea1 (admin_area_1): string?` (state) · `PostalCode (postal_code): string?`

- **Raw card**: `PaymentSource = new PaymentSource { Card = new CardRequest { Number, Expiry, SecurityCode, Name, BillingAddress = new Address { CountryCode = …, … } } }`. (Passing PAN/CVV directly requires PCI SAQ D — the record's own doc note; sandbox account has direct card processing enabled per the brief.)
- **Vaulted card**: `PaymentSource = new PaymentSource { Card = new CardRequest { VaultId = "<payment-token-id>", StoredCredential = new CardStoredCredential { PaymentInitiator = …, PaymentType = …, Usage = … } } }`. `CardStoredCredential`: `PaymentInitiator (payment_initiator): PaymentInitiator` **!req** (`Customer`/`Merchant`) · `PaymentType (payment_type): StoredPaymentSourcePaymentType` **!req** (`OneTime`/`Recurring`/`Unscheduled`) · `Usage (usage): StoredPaymentSourceUsageType? = Derived` (`First`/`Subsequent`/`Derived`). For a merchant-initiated charge on a saved card the typical combination is `Merchant` + `Unscheduled` + `Subsequent`; for a buyer-present checkout with a saved card, `Customer` + `Unscheduled` (+ `Subsequent`). Compatibility constraints (`ONE_TIME`⇔`CUSTOMER` only; `FIRST`⇔`CUSTOMER` only) are on the record's doc (map: `records-1-Ac-Pa.md` `CardStoredCredential`).
- **Vault-on-success during payment**: set `CardRequest.Attributes = new CardAttributes { Vault = new VaultInstructionBase { StoreInVault = StoreInVaultInstruction.OnSuccess } }` (`VaultInstructionBase.StoreInVault (store_in_vault): StoreInVaultInstruction?`; wire value `ON_SUCCESS`).

Response `Order`: `Id (id): string?` · `Status (status): OrderStatus?` · `Intent (intent): CheckoutPaymentIntent?` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` · `PaymentSource (payment_source): PaymentSourceResponse?` · `Links (links): IReadOnlyList<LinkDescription>?` · `CreateTime (create_time)/UpdateTime (update_time): string?`
`PurchaseUnit.Payments (payments): PaymentCollection?` → `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` · `Captures (captures): IReadOnlyList<OrdersCapture>?` · `Refunds (refunds): IReadOnlyList<Refund>?`

**AuthorizeOrder** — `POST /v2/checkout/orders/{id}/authorize`
`AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<OrderAuthorizeResponse>`
- 5 nullable params (`payPalMockResponse`…`body`) must be passed explicitly. `body` may be `null` when the order already carries the payment source; `OrderAuthorizeRequest.PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` with `Card (card): CardRequest?` / `Token (token): Token?` if you supply/override it here.
- Error: `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)`.

Response `OrderAuthorizeResponse`: `Id`, `Status (status): OrderStatus?`, `PurchaseUnits` → `PurchaseUnit.Payments.Authorizations` → `AuthorizationWithAdditionalData`: **`Id (id): string?` ← authorization id** · `Status (status): AuthorizationStatus?` · `StatusDetails (status_details): AuthorizationStatusDetails?` (`Reason (reason): AuthorizationIncompleteReason?`) · `Amount (amount): Money?` · `ExpirationTime (expiration_time): string?` · `SellerProtection`, `ProcessorResponse`, `Links`, `CreateTime`/`UpdateTime`.

**GetOrder** (supporting read) — `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<Order>`; `fields`/`payPalMockResponse`/`payPalAuthAssertion` must be passed explicitly. Error: `SdkException<GetOrderError>` — `TryGetError(out Error)` [401, 404].

### 2.2 Payments — `client.Payments` (map: `operations/Payments.md`)

**CaptureAuthorizedPayment** — `POST /v2/payments/authorizations/{authorization_id}/capture`
`CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<CapturedPayment>`
- 4 nullable params must be passed explicitly. `CaptureRequest`: `Amount (amount): Money?` (omit/null = capture full authorized amount; set for partial) · `FinalCapture (final_capture): bool? = false` · `InvoiceId (invoice_id): string?` · `NoteToPayer (note_to_payer): string?` · `SoftDescriptor (soft_descriptor): string?`.
- Error: `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

Response `CapturedPayment`: **`Id (id)` ← capture id** · `Status (status): CaptureStatus?` · `StatusDetails.Reason: CaptureIncompleteReason?` · `Amount (amount): Money?` (captured gross) · **`SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`** → `GrossAmount (gross_amount): Money` **!req** · **`PaypalFee (paypal_fee): Money?` ← PayPal fee** · **`NetAmount (net_amount): Money?` ← net proceeds to merchant** · `PaypalFeeInReceivableCurrency`, `ReceivableAmount`, `ExchangeRate`, `PlatformFees` · `FinalCapture (final_capture): bool?` · `InvoiceId`, `CustomId`, `Links`, `ProcessorResponse`, `CreateTime`/`UpdateTime`.
- **`prefer` matters**: the SDK default `"return=minimal"` returns only id, status and HATEOAS links; `"return=representation"` returns the complete resource (source: operation doc comment in `Api/Payments.cs`). To read fee/net from the capture response, pass `prefer: "return=representation"` — and still null-check `SellerReceivableBreakdown` (its own doc: "not available for transactions that are in pending state"). Fallback read: `Payments.GetCapturedPayment`.

**ReauthorizePayment** — `POST /v2/payments/authorizations/{authorization_id}/reauthorize`
`ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentAuthorization>`
- `payPalRequestId`, `payPalAuthAssertion`, `body` must be passed explicitly. `ReauthorizeRequest`: `Amount (amount): Money?` — **only `amount` is supported**.
- Error: `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.
- **When an authorization can/cannot be reauthorized or captured** (map Notes + `AuthorizationStatus` enum): an authorization is capturable/reauthorizable while `AuthorizationStatus` is `Created (CREATED)` or `PartiallyCaptured (PARTIALLY_CAPTURED)`; `Voided (VOIDED)`, `Captured (CAPTURED)` (fully captured), and `Denied (DENIED)` are terminal; `Pending (PENDING)` means wait (see `AuthorizationIncompleteReason`: `PendingReview`, `DeclinedByRiskFraudFilters`). Timing (map Notes, verbatim constraints): initial honor period **3 days**; reauthorize allowed **from day 4 to day 29** after the original authorization (multiple re-authorizations allowed within the 29-day window, each with a new 3-day honor period); **after 30 days you must create a new authorization** instead. Amount cap: up to **115%** of the original authorized amount, not to exceed **+$75 USD** (US example per the doc). Track `PaymentAuthorization.ExpirationTime (expiration_time)`.

**VoidPayment** — `POST /v2/payments/authorizations/{authorization_id}/void`
`VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<PaymentAuthorization>`
- `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` must be passed explicitly. Cannot void an authorization that has been fully captured (map Notes).
- Error: `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

**RefundCapturedPayment** — `POST /v2/payments/captures/{capture_id}/refund`
`RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<Refund>`
- 4 nullable params must be passed explicitly. **Idempotency: pass your key as `payPalRequestId`** (→ `PayPal-Request-Id`, stored 45 days).
- `RefundRequest`: `Amount (amount): Money?` · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `NoteToPayer (note_to_payer): string?`. **Full refund: empty payload** (operation remark: "For a full refund, include an empty payload in the JSON request body") — pass `body: null`. `UNVERIFIED`: whether a null body serializes as a truly empty body vs `{}` on the wire — both satisfy "empty payload" semantics; if the sandbox ever rejects the null form, pass `new RefundRequest()`. **Partial refund: set `Amount`**.
- Error: `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

Response `Refund`: **`Id (id)` ← refund id** · `Status (status): RefundStatus?` · `StatusDetails.Reason: RefundIncompleteReason?` · `Amount (amount): Money?` · `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` → `GrossAmount`, `PaypalFee (paypal_fee)`, `NetAmount (net_amount)`, `TotalRefundedAmount (total_refunded_amount)`, `NetAmountBreakdown` · `InvoiceId`, `CustomId`, `Links`, `CreateTime`/`UpdateTime`. Same `prefer: "return=representation"` guidance as capture for full readback; fallback `Payments.GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, …)`.

**GetAuthorizedPayment** / **GetCapturedPayment** (supporting reads) — `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, …)` → `PaymentAuthorization`; `GetCapturedPayment(string captureId, string? payPalMockResponse, …)` → `CapturedPayment`. Errors: `TryGetError(out Error)` [401, 403, 404] + `TryGetNoContent(out RawError)` [500].

`PaymentAuthorization` fields: `Id`, `Status: AuthorizationStatus?`, `StatusDetails`, `Amount: Money?`, `InvoiceId`, `CustomId`, `ExpirationTime`, `SellerProtection`, `SupplementaryData (supplementary_data): PaymentSupplementaryData?` → `RelatedIds (related_ids): RelatedIdentifiers?` → `OrderId (order_id)`, `AuthorizationId (authorization_id)`, `CaptureId (capture_id)` — useful for lining up records.

### 2.3 Vault (saved cards) — `client.Vault` (map: `operations/Vault.md`)

All vault errors are Case A with **`TryGetError1(out Error1)`** (not `TryGetError`): `Error1` = `Name (name)`, `Message (message)`, `DebugId (debug_id)` **!req** + `Details (details): IReadOnlyList<ErrorDetails1>?` (`Field`, `Value`, `Location = "body"`, `Issue` **!req**, `Description`) + `Links: IReadOnlyList<ErrorLinkDescription>?` — note `ErrorLinkDescription.Rel (rel)` is **optional** (the live API omits `rel` on the documentation link of `RESOURCE_NOT_FOUND` errors; map-carried drift note on `records-1-Ac-Pa.md`).

**Flow A — setup token (buyer-present / hosted-fields style):**
1. `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `SetupTokenResponse`. `payPalRequestId` must be passed explicitly.
   `SetupTokenRequest`: `PaymentSource (payment_source): SetupTokenRequestPaymentSource` **!req** → `Card (card): SetupTokenRequestCard?`; `Customer (customer): Customer?`.
   `SetupTokenRequestCard`: `Number (number)`, `Expiry (expiry)`, `SecurityCode (security_code)`, `Name (name)`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`, `VerificationMethod (verification_method): VaultCardVerificationMethod?` (`ScaWhenRequired (SCA_WHEN_REQUIRED)` / `ScaAlways (SCA_ALWAYS)`), `ExperienceContext (experience_context): VaultCardExperienceContext?` (`ReturnUrl`, `CancelUrl`, `VaultInstruction (vault_instruction): VaultInstructionAction?` — `OnCreatePaymentTokens`/`OnPayerApproval`).
   `SetupTokenResponse`: **`Id (id)` ← setup token id** · `Status (status): PaymentTokenStatus? = Created` · `Customer (customer): Customer?` · `PaymentSource.Card: SetupTokenResponseCard?` (`LastDigits (last_digits)`, `Brand (brand): CardBrand?`, `Expiry`, `VerificationStatus (verification_status): CardVerificationStatus?` — `Verified`/`Failed`) · `Links`. A setup token is temporary — exchange it:
2. `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, …)` with `PaymentSource = new PaymentTokenRequestPaymentSource { Token = new VaultTokenRequest { Id = "<setup-token-id>", Type = VaultTokenRequestType.SetupToken } }` (`VaultTokenRequest`: `Id (id): string` **!req**, `Type (type): VaultTokenRequestType` **!req**, only value `SetupToken (SETUP_TOKEN)`).

**Flow B — direct vault (server-side, PCI SAQ D):** `CreatePaymentToken` with `PaymentSource = new PaymentTokenRequestPaymentSource { Card = new PaymentTokenRequestCard { Number, Expiry, SecurityCode, Name, Brand, BillingAddress } } }`.

`PaymentTokenRequest`: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource` **!req** (`Card (card): PaymentTokenRequestCard?` / `Token (token): VaultTokenRequest?`) · `Customer (customer): Customer?`.
`CreatePaymentToken` errors: `SdkException<CreatePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 404, 422, 500]. `CreateSetupToken` errors: [400, 403, 422, 500].

`PaymentTokenResponse` (the vault result): **`Id (id)` ← payment token id (use as `CardRequest.VaultId` when paying)** · `Customer (customer): CustomerResponse?` (`Id (id)`, `MerchantCustomerId (merchant_customer_id)`) · `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `Card (card): CardPaymentTokenEntity?` → **safe display attributes only**: `Brand (brand): CardBrand?`, `LastDigits (last_digits): string?`, `Name (name)`, `Expiry (expiry)`, `Type (type): CardType?`, `BillingAddress: CardResponseAddress?`, `VerificationStatus` — **no full PAN is ever returned** · `Links`.

**List:** `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `CustomerVaultPaymentTokensResponse` — query wires: `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired`. Response: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Customer (customer): VaultResponseCustomer?` (`Id`, `MerchantCustomerId`) · `Links`. Iterate `page` 1..`TotalPages`. Errors: `TryGetError1(out Error1)` [400, 403, 500].

**Delete:** `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `void` (Task). Errors: `TryGetError1(out Error1)` [400, 403, 500].

**Get one:** `GetPaymentToken(string id, …)` → `PaymentTokenResponse`. Errors: [403, 404, 422, 500].

**Customer scoping:** vault calls scope to a customer via the `Customer` record on the request — `Customer.Id (id): string?` is the **PayPal-generated customer id**, `Customer.MerchantCustomerId (merchant_customer_id): string?` is **your own customer id** (e.g. the eShopOnWeb buyer id). Listing is by PayPal customer id (`customerId` → `customer_id` query param). Persist both the PayPal customer id and the payment token id against your user; never persist PAN/CVV.

### 2.4 Reconciliation — `client.TransactionSearch` (map: `operations/TransactionSearch.md`; source: `Api/TransactionSearch.cs`)

**SearchTransactions** — `GET /v1/reporting/transactions`
`SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<SearchResponse>`
- The 8 nullable filters (`transactionId`…`terminalId`) **must be passed explicitly** (`null` to skip) — call with named arguments.
- **Date format** (source doc): RFC 3339 §5.6 Internet date-time — e.g. `2026-08-01T00:00:00Z`; **seconds required**, fractional seconds optional. **Maximum range: 31 days** — chunk longer windows. Data lag: executed transactions take up to **3 hours** to appear; history limited to the previous **3 years**.
- `transactionStatus` filter codes: `D` denied · `P` pending · `S` completed · `V` reversed/refunded. `transactionAmount` range syntax: `[500 TO 1005]` in minor units, URL-encoded.
- Query wires: `start_date` ← `startDate`, `end_date` ← `endDate`, `transaction_id`, `transaction_type`, `transaction_status`, `transaction_amount`, `transaction_currency`, `payment_instrument_type`, `store_id`, `terminal_id`, `fields`, `balance_affecting_records_only`, `page_size` ← `pageSize`, `page` ← `page`.
- **Pagination / iterate ALL pages:** `pageSize` default 100, `page` default 1. Loop `page = 1 … response.TotalPages` (response carries `Page (page): int?`, `TotalPages (total_pages): int?`, `TotalItems (total_items): int?`, plus HATEOAS `Links`). The map marks pagination "none (only `page`, no `perPage`)" — i.e. there is no SDK auto-pager; you drive `page` yourself.
- **Error: Case B** — `SdkException<RawError>` (the only Case-B op in scope; source-verified: the method passes `RawErrorResponse.Instance` in `Api/TransactionSearch.cs`, so **no** typed `{Operation}Error` or `TryGet…` accessor exists for it): `ex.Error.StatusCode: HttpStatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`. For a typed parse use `ReadAsJson<SearchError>()` (`SearchError`: `Name`, `Message`, `DebugId` **!req**, `InformationLink`, `Details: IReadOnlyList<TransactionSearchErrorDetails>?` (`Field`, `Value`, `Location`, `Issue` **!req**, `Description`), `TotalItems`, `MaximumItems`).
- **404 = empty result set** (`UNVERIFIED` by map/source docs — neither documents any 404 semantic for this operation; observed live in sandbox): the endpoint is a collection GET with **no path/template parameter** (source: `Api/TransactionSearch.cs` — empty template-param list), so a 404 cannot mean a missing sub-resource; combined with the live observation (200 with data vs 404 with none), treat `SdkException<RawError>` with `StatusCode == HttpStatusCode.NotFound` from this operation as an **empty page/result set, not a failure**. Directive: catch-filter on the 404 status, best-effort confirm via `ReadAsJson<SearchError>()` (`Name`/`Message`, log `DebugId`), return an empty result; every other status remains a real error.

Response `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` · `AccountNumber`, `StartDate`, `EndDate`, `LastRefreshedDatetime`, `Page`, `TotalItems`, `TotalPages`, `Links`.
`TransactionDetails`: `TransactionInfo (transaction_info): TransactionInformation?` · `PayerInfo` · `ShippingInfo` · `CartInfo` · `StoreInfo` · `AuctionInfo` · `IncentiveInfo` (non-`transaction_info` blocks appear when requested via `fields`; default `fields = "transaction_info"`).
`TransactionInformation` (reconciliation fields): **`TransactionId (transaction_id): string?`** · **`PaypalReferenceId (paypal_reference_id): string?` + `PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?`** — `Odr (ODR)` = reference is an **order id** (line up against your order records), `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` · `TransactionEventCode (transaction_event_code): string?` · **`TransactionStatus (transaction_status): string?`** (D/P/S/V codes) · **`TransactionAmount (transaction_amount): Money?`** · **`FeeAmount (fee_amount): Money?`** · **`TransactionInitiationDate (transaction_initiation_date)` / `TransactionUpdatedDate (transaction_updated_date): string?`** · `InvoiceId (invoice_id): string?` · `CustomField (custom_field): string?` · `EndingBalance`, `AvailableBalance`, `ProtectionEligibility`, `PaymentMethodType`, `InstrumentType`. Note (source doc): a transaction id is **not unique** in the reporting system (balance-affecting vs non-balance-affecting rows can share it) — dedupe on (`TransactionId`, `TransactionEventCode`, `balanceAffectingRecordsOnly`) when lining up.

### 2.5 Error model (all operations) — map: `sdk-map.md` *Error-handling model*

- Throw-based only; **no `…Result` no-throw variants exist anywhere in this SDK**. On error status: `PayPalServerSdk.Core.Exceptions.SdkException<TError>` with `.Error: TError`.
- **Case A (every in-scope op except SearchTransactions):** `TError` = `{Operation}Error : ApiError` in `PayPalServerSdk.Errors`. Use the operation's `TryGet…` accessor from its row above; fall back to the inherited `TryGetRawError(out RawError)` for unlisted statuses. `TryGetRawError` is **not** a catch-all for the listed statuses — check the typed accessor first.
- **Case B (SearchTransactions only):** `SdkException<RawError>` — `RawError` (`PayPalServerSdk.Core.ErrorResponse`): `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>`.
- Typed payload `Error` (Orders/Payments): `Name (name): string` **!req** · `Message (message): string` **!req** · `DebugId (debug_id): string` **!req** · `Details (details): IReadOnlyList<ErrorDetails>?` — `Issue (issue): string` **!req**, `Field (field)`, `Value (value)`, `Location (location) = "body"`, `Description (description)` · `Links`. (Vault uses `Error1`/`ErrorDetails1`/`ErrorLinkDescription` — see 2.3.)

### 2.6 Enum values needed (map: `models/enums.md`; all `PayPalServerSdk.Models.Enums`, `StringEnum<T>` — use static members or `Type.FromValue("wire")`, **not** C# enum syntax)

| Enum (C# type) | Members (wire values) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, `Solo (SOLO)`, `Jcb (JCB)`, `Star (STAR)`, `Delta (DELTA)`, `Switch (SWITCH)`, `Maestro (MAESTRO)`, `CbNationale (CB_NATIONALE)`, `Configoga (CONFIGOGA)`, `Confidis (CONFIDIS)`, `Electron (ELECTRON)`, `Cetelem (CETELEM)`, `ChinaUnionPay (CHINA_UNION_PAY)`, `Diners (DINERS)`, `Elo (ELO)`, `Hiper (HIPER)`, `Hipercard (HIPERCARD)`, `Rupay (RUPAY)`, `Ge (GE)`, `Synchrony (SYNCHRONY)`, `Eftpos (EFTPOS)`, `CarteBancaire (CARTE_BANCAIRE)`, `StarAccess (STAR_ACCESS)`, `Pulse (PULSE)`, `Nyce (NYCE)`, `Accel (ACCEL)`, `Unknown (UNKNOWN)` |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureIncompleteReason` | `BuyerComplaint`, `Chargeback`, `Echeck`, `InternationalWithdrawal`, `Other`, `PendingReview`, `ReceivingPreferenceMandatesManualAction`, `Refunded`, `TransactionApprovedAwaitingFunding`, `Unilateral`, `VerificationRequired`, `DeclinedByRiskFraudFilters` (wires = SCREAMING_SNAKE of the member) |
| `RefundIncompleteReason` | `Echeck (ECHECK)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` |
| `VaultCardVerificationMethod` | `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `ScaAlways (SCA_ALWAYS)` |
| `CardVerificationStatus` | `Verified (VERIFIED)`, `Failed (FAILED)` |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `DisbursementMode` | `Instant (INSTANT)`, `Delayed (DELAYED)` |
| `LinkHttpMethod` | `Get (GET)`, `Post (POST)`, `Put (PUT)`, `Delete (DELETE)`, `Head (HEAD)`, `Connect (CONNECT)`, `Options (OPTIONS)`, `Patch (PATCH)` |

## 3. Trap notes (hazard + pointer — the named skill carries the resolution)

> ⚠ Step 1 (client registration) — the SDK wraps a caller-supplied `HttpClient`; who owns that client's lifetime and handler pipeline (and what breaks if you `new` one per request) is not visible from the constructor. **MUST load `dotnet-client-initialization`** before writing `new PayPalServerSdkClient(...)` or the `AddPayPalServerSdkClient` registration.
>
> ⚠ Step 1 (auth) — when in the client lifecycle credentials must be set, how the cached token is refreshed/invalidated, and where secrets may live (not in code) are not visible from the options shape. **MUST load `dotnet-authentication`** before wiring `Oauth2`/`Oauth2TokenStrategy`.
>
> ⚠ Steps 3–9 (every call) — several parameters are nullable **without C# defaults** and mis-bind in positional calls; the cancellation token parameter is literally named `ct`. **MUST load `dotnet-calling-endpoints`** before the first `client.Orders.*`/`client.Payments.*`/`client.Vault.*`/`client.TransactionSearch.*` call.
>
> ⚠ Steps 2–8 (models) — enums are `StringEnum<T>` (not C# enums), records are immutable with `required` init members, and JSON fields the SDK doesn't model are silently dropped on deserialize. **MUST load `dotnet-models`** before constructing request payloads or mapping responses onto eShopOnWeb domain types.
>
> ⚠ Step 10 (error boundary) — which exception types actually reach a `catch`, how Case A vs Case B differ per operation, and why `TryGetRawError` is not a catch-all are not visible from the signatures. **MUST load `dotnet-error-handling`** before writing any `try/catch` or error middleware (see also the two mandatory rows in REQUIRED READING).
>
> ⚠ Steps 1, 5–9 (resilience) — what `RetryOptions.Timeout` actually bounds, and whether a failed non-idempotent write (capture/refund) can be re-sent by the retry layer, are not visible from the option names; this interacts directly with the `payPalRequestId` idempotency keys above. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts or relying on retry behavior for writes.
>
> ⚠ Step 10 (tests) — the test seam for SDK-calling code (and which framework/assertion style to match in eShopOnWeb) is not visible from the client API. **MUST load `dotnet-testing`** before writing tests for the integration layer.

## 4. REQUIRED READING (load **before implementation starts** — this sheet deliberately does not carry their contents)

- `dotnet-client-initialization` — governs step 1 (client construction & DI registration).
- `dotnet-authentication` — governs step 1 (OAuth credentials, token lifecycle, secret storage).
- `dotnet-calling-endpoints` — governs steps 3–9 (parameter passing, named arguments, async/cancellation).
- `dotnet-models` — governs steps 2–8 (request/response models, enums, required members, wire names).
- `dotnet-error-handling` — governs step 10 (the exception boundary; mandatory for every integration).
- `dotnet-configuration-resilience` — governs steps 1, 5–9 (retries, timeouts, base URL, pagination, logging).
- `dotnet-testing` — governs step 10 (faking the SDK seam in tests).

Always include, verbatim, both of these hazard rows — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:
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
- The PublicApi's JWT authentication is orthogonal to PayPal: all PayPal calls are server-to-server using the sandbox OAuth client id/secret; no buyer-facing PayPal redirect flow is in scope (card + vaulted-card only).
- Direct card processing (raw PAN/CVV through `CardRequest`) is enabled on the sandbox business account per the brief; this puts the integration in PCI SAQ D scope — noted on the `CardRequest` record itself. The setup-token flow (Flow A) is the lower-scope alternative for saving cards.
- Vault (Payment Method Tokens v3) is available on the sandbox account — the SDK's own client doc notes this API is "Available in the US only".
- eShopOnWeb will wrap the SDK behind an application service inside `src/PublicApi`; this plan fixes the SDK contracts, not the route/controller shape.
- Amounts are string decimals (`Money.Value: string`); currency is a three-character ISO-4217 code.
- `UNVERIFIED` items (live-wire only): (a) exact serialization of a null `RefundRequest` body for full refunds — directive: pass `body: null`, fall back to `new RefundRequest()` if rejected; (b) whether `prefer = "return=minimal"` omits `SellerReceivableBreakdown`/`SellerPayableBreakdown` on the live wire — directive: always pass `prefer: "return=representation"` when reading amounts/fees back, and null-check the breakdown regardless (it is documented absent for pending transactions).

**Blockers** — none.
