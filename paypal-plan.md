# PayPal .NET SDK plan — eShopOnWeb (authorize / capture / refund / vault)

NuGet: `AsadAli.Checkout.Sdk` (install version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`.

## Scope & sequence

1. **Client + config** — bind `PayPal:ClientId/ClientSecret/Environment/Currency/BaseUrl` (env: `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, `PAYPAL_ENVIRONMENT`, `PAYPAL_CURRENCY`). Construct `PayPalServerSdkClient` with OAuth2 client-credentials and optional BaseUrl override. Target sandbox.
2. **Place order (local)** — existing eShop checkout writes an Order row in *awaiting payment*. Persist enough PayPal ids/status after step 3 that later capture/void/refund/reauthorize can act.
3. **AUTHORIZE (hold, no capture)** — `Orders.CreateOrder` with `CheckoutPaymentIntent.Authorize` + amount equal to order total; then `Orders.AuthorizeOrder` with either PAN (`CardRequest` number/expiry/CVC/name/billing) **or** `CardRequest.VaultId`. Idempotent via `payPalRequestId`. If `OrderStatus.PayerActionRequired` (browser challenge) → **STOP and report**; do not follow approve links or build a round-trip.
4. **FULFIL (capture)** — `Payments.GetAuthorizedPayment`; if honor/expiry window is stale, `Payments.ReauthorizePayment` then persist the new authorization id; then `Payments.CaptureAuthorizedPayment`. Read captured amount, PayPal fee, net proceeds from `SellerReceivableBreakdown`. If reauthorize fails as unrenewable, surface `Error.Name` / `Message` / `Details[].Issue` / `DebugId` in operator-actionable terms.
5. **CANCEL (pre-fulfil)** — `Payments.VoidPayment` on the authorization id (releases hold; no money moved).
6. **REFUND (post-fulfil)** — `Payments.RefundCapturedPayment` full (null/empty body) or partial (`RefundRequest.Amount`). Gate against captured total so a partly-refunded order cannot refund beyond captured. Caller-supplied idempotency key → `payPalRequestId`.
7. **Reconcile** — `TransactionSearch.SearchTransactions` over the ISO-8601 from/to range; page until `TotalPages` exhausted; if the range exceeds 31 days, split into ≤31-day windows and page each. Match to eShop orders via stored PayPal ids / `invoice_id` / `custom_id`.
8. **Saved cards** — `Vault.CreatePaymentToken` (PAN never persisted locally); list via `Vault.ListCustomerPaymentTokens`; delete via `Vault.DeletePaymentToken`. Pay later using `CardRequest.VaultId` in step 3.
9. **Error boundary** — Case A/B catches + both `JsonException` directions (see REQUIRED READING).
10. **Tests** — fake `HttpClient` seam; no live PAN in logs or DB.

---

## CONTRACT SHEET

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

No-throw `…Result` variants: **absent** on every operation (throw-only). `sdk-map.md`.

### Client construction / auth / server-node

| Fact | Value | Cite |
|---|---|---|
| Client ctor | `PayPalServerSdk.PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI | `services.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` | `ServiceCollectionExtensions.cs` |
| Options | `Environment: PayPalServerSdk.Servers.ServerEnvironment` · `Retry: PayPalServerSdk.Core.Configuration.RetryOptions` · `Logging: PayPalServerSdk.Core.Configuration.LoggingOptions` · `Server: PayPalServerSdk.ServerOptions` · `Oauth2: PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?` · `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `sdk-map.md`, `PayPalServerSdkClientOptions.cs` |
| Credentials | `new OAuth2ClientCredentials { ClientId = …, ClientSecret = …, Scope = optional }` — both id/secret are `required string` | `OAuth2ClientCredentials.cs` |
| Environment | **Only** `ServerEnvironment.Sandbox` (wire `"Sandbox"`). `Default()` → Sandbox. **No Live member.** | `Servers/ServerEnvironment.cs` |
| Default API host | `options.Server.Default.Sandbox.BaseUrl` default `"https://api-m.sandbox.paypal.com"` (`PayPalServerSdk.Servers.DefaultOptions.SandboxOptions.BaseUrl`) | `Servers/DefaultOptions.cs` |
| **PayPal:BaseUrl override** | When set, assign **verbatim** to `options.Server.Default.Sandbox.BaseUrl`. Every call resolves via `Server.Default(path)`, including the token request `POST {BaseUrl}/v1/oauth2/token` (`OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), …)`). Do **not** use `HttpClient.BaseAddress` as the override. | `Server.cs`, `AuthSchemes.cs`, `DefaultOptions.cs` |
| Auth scheme | OAuth2 client-credentials on `options.Oauth2`; token strategy defaults if `Oauth2TokenStrategy` is null | `sdk-map.md` *Servers & auth* |

Config mapping: `PayPal:ClientId` → `Oauth2.ClientId`; `PayPal:ClientSecret` → `Oauth2.ClientSecret`; `PayPal:Environment` → only sandbox is valid (`ServerEnvironment.Sandbox`); `PayPal:Currency` → every `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode`; `PayPal:BaseUrl` → `Server.Default.Sandbox.BaseUrl` when non-empty.

### Amounts

| Type | Namespace | Fields | Cite |
|---|---|---|---|
| `Money` | `PayPalServerSdk.Models` | `CurrencyCode (currency_code): string !req` (ISO-4217, length 3) · `Value (value): string !req` (not `decimal`; regex integer or decimal fraction) | `records-1-Ac-Pa.md`, `Models/Money.cs` |
| `AmountWithBreakdown` | `PayPalServerSdk.Models` | `CurrencyCode (currency_code): string !req` · `Value (value): string !req` · `Breakdown (breakdown): AmountBreakdown?` | `records-1-Ac-Pa.md` |

Format `Value` to the currency’s minor units (e.g. `"19.99"`) so it **equals the eShop order total to the cent**. Put `PAYPAL_CURRENCY` / `PayPal:Currency` on every amount’s `CurrencyCode`.

### Enums in scope (`PayPalServerSdk.Models.Enums`, `StringEnum<T>` — use static members or `Type.FromValue("wire")`)

| Enum | Members (C# (wire)) | Cite |
|---|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` | `enums.md` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` | `enums.md` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` | `enums.md` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` | `enums.md` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` | `enums.md` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` | `enums.md` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, `Solo (SOLO)`, `Jcb (JCB)`, `Star (STAR)`, `Delta (DELTA)`, `Switch (SWITCH)`, `Maestro (MAESTRO)`, `CbNationale (CB_NATIONALE)`, `Configoga (CONFIGOGA)`, `Confidis (CONFIDIS)`, `Electron (ELECTRON)`, `Cetelem (CETELEM)`, `ChinaUnionPay (CHINA_UNION_PAY)`, `Diners (DINERS)`, `Elo (ELO)`, `Hiper (HIPER)`, `Hipercard (HIPERCARD)`, `Rupay (RUPAY)`, `Ge (GE)`, `Synchrony (SYNCHRONY)`, `Eftpos (EFTPOS)`, `CarteBancaire (CARTE_BANCAIRE)`, `StarAccess (STAR_ACCESS)`, `Pulse (PULSE)`, `Nyce (NYCE)`, `Accel (ACCEL)`, `Unknown (UNKNOWN)` | `enums.md` |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` | `enums.md` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` | `enums.md` |
| `ParesStatus` | `Y (Y)`, `N (N)`, `U (U)`, `A (A)`, `C (C)`, `R (R)`, `D (D)`, `I (I)` | `enums.md` |
| `EnrollmentStatus` | `Y (Y)`, `N (N)`, `U (U)`, `B (B)` | `enums.md` |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` | `enums.md` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` | `enums.md` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` | `enums.md` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` — **not** a vault payment-token type | `enums.md` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` | `enums.md` |
| `CardVerificationStatus` | `Verified (VERIFIED)`, `Failed (FAILED)` | `enums.md` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` | `enums.md` |
| `ProcessorResponseCode` | large string-enum of processor codes (see `enums.md`) | `enums.md` |

### Idempotency (all writes)

C# param `payPalRequestId` → HTTP header **`PayPal-Request-Id`**. Pass a stable caller key on authorize/create, capture, void, reauthorize, refund, vault-create. XML: Orders keys stored 6h; Payments capture/void/refund/reauthorize 45d; Vault create 3h. `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`.

The generated client **also** sends `Idempotency-Key: Guid.NewGuid()` on every write (caller cannot set it). **UNVERIFIED** whether that header interacts with `PayPal-Request-Id` on the wire. Application-level gate (do not call twice if local payment already holds/captured) is still required.

`prefer` default is `"return=minimal"` (id/status/links only). Pass **`prefer: "return=representation"`** on create/authorize/capture/void/refund/reauthorize so authorization/capture/fee fields are present.

---

### Operations

#### 1. `client.Orders.CreateOrder` — create PayPal order (intent AUTHORIZE)

- **HTTP**: `POST /v2/checkout/orders` · `operations/Orders.md`
- **Signature**: `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params (`payPalMockResponse` … `payPalAuthAssertion`) nullable, **no default → pass explicitly** (`null` to skip). `body` required.
- **Request** `PayPalServerSdk.Models.OrderRequest` (`records-1-Ac-Pa.md`):
  - `Intent (intent): CheckoutPaymentIntent !req` → **`CheckoutPaymentIntent.Authorize`**
  - `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`
  - `Payer (payer): Payer?` · `PaymentSource (payment_source): PaymentSource?` · `ApplicationContext (application_context): OrderApplicationContext?`
- **`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`): `Amount (amount): AmountWithBreakdown !req` · `ReferenceId (reference_id): string?` · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `Description (description): string?` · `Items (items): IReadOnlyList<ItemRequest>?` · `Shipping (shipping): ShippingDetails?` · others optional.
- **Do not** put PAN on create unless treating it as a single-step call (`payPalRequestId` is **mandatory** when `payment_source` like Card / vault_id is on the create body — `Api/Orders.cs`). Preferred: amount-only create, card on `AuthorizeOrder`.
- **Returns** `Order` (not an envelope wrapper). Fields: `Id (id)` · `Status (status): OrderStatus?` · `Intent (intent)` · `PurchaseUnits (purchase_units)` · `PaymentSource (payment_source): PaymentSourceResponse?` · `Links (links)` · timestamps. `records-1-Ac-Pa.md`
- **Error**: Case A `SdkException<PayPalServerSdk.Errors.CreateOrderError>` — `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback.

#### 2. `client.Orders.AuthorizeOrder` — hold funds (direct card **or** vaulted card)

- **HTTP**: `POST /v2/checkout/orders/{id}/authorize` · `operations/Orders.md`
- **Signature**: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params (`payPalMockResponse` … `body`) nullable, **must pass explicitly**.
- **Request** `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`
- **`OrderAuthorizeRequestPaymentSource`**: `Card (card): CardRequest?` · `Token (token): Token?` (billing-agreement only) · wallet types unused here.
- **`CardRequest`** (`records-1-Ac-Pa.md`, `Models/CardRequest.cs`) — one-off PAN **or** vault, not both:
  - One-off: `Name (name)` · `Number (number)` (13–19 digits) · `Expiry (expiry)` **`YYYY-MM`** · `SecurityCode (security_code)` (3–4 digits) · `BillingAddress (billing_address): Address?`
  - Vaulted: `VaultId (vault_id): string?` = `PaymentTokenResponse.Id` (do not send PAN).
  - `Address`: `AddressLine1 (address_line_1)` · `AddressLine2 (address_line_2)` · `AdminArea2 (admin_area_2)` · `AdminArea1 (admin_area_1)` · `PostalCode (postal_code)` · `CountryCode (country_code): string !req`
  - Do **not** set `ExperienceContext.ReturnUrl/CancelUrl` (that is the 3DS browser path). If PayPal still requires a challenge, stop (below).
- **Returns** `OrderAuthorizeResponse` (same shape as `Order` for ids/status/units): `Id` · `Status` · `PurchaseUnits` · `PaymentSource: OrderAuthorizeResponsePaymentSource?` (`Card: CardResponse?` with `LastDigits`, `Brand`, `Expiry`, `AuthenticationResult`). `records-1-Ac-Pa.md`
- **Read after authorize** (requires `prefer: "return=representation"`):
  - `PurchaseUnits[0].Payments.Authorizations[]` → `AuthorizationWithAdditionalData`: `Id (id)` · `Status (status): AuthorizationStatus?` · `Amount (amount): Money?` · `ExpirationTime (expiration_time): string?` · `ProcessorResponse` · timestamps
  - `PaymentCollection`: `Authorizations` · `Captures` · `Refunds` (`records-2-Pa-Ve.md`)
- **3DS / browser challenge — STOP**: if `Status == OrderStatus.PayerActionRequired`, **do not** follow `Links` (`rel` approve) and **do not** build an approval round-trip. Report status + any `CardResponse.AuthenticationResult` (`LiabilityShift`, `ThreeDSecure.AuthenticationStatus` / `EnrollmentStatus`) + `Error.Details` if thrown.
- **Error**: Case A `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError` fallback.

#### 3. `client.Orders.GetOrder` — refresh PayPal order (optional)

- **HTTP**: `GET /v2/checkout/orders/{id}` · `operations/Orders.md`
- **Signature**: `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `fields` / `payPalMockResponse` / `payPalAuthAssertion` must be passed explicitly (`null` ok). Query `fields` ← `fields` (valid filter: `payment_source`).
- **Returns** `Order`
- **Error**: Case A `SdkException<GetOrderError>` — `TryGetError(out Error)` [401, 404] · `TryGetRawError` fallback.

#### 4. `client.Payments.GetAuthorizedPayment` — inspect hold before capture

- **HTTP**: `GET /v2/payments/authorizations/{authorization_id}` · `operations/Payments.md`
- **Signature**: `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — two nullables must be passed.
- **Returns** `PaymentAuthorization`: `Id` · `Status: AuthorizationStatus?` · `StatusDetails.Reason: AuthorizationIncompleteReason?` · `Amount: Money?` · `ExpirationTime (expiration_time)` · timestamps · `Payee` · `SupplementaryData.RelatedIds` (`OrderId`, `AuthorizationId`, `CaptureId`). `records-2-Pa-Ve.md`
- **Stale vs unrenewable (no issue-code enum in the SDK)**:
  - Compare `ExpirationTime` / `Status` to now. If still `Created`/`Pending` and not expired → capture.
  - If honor window elapsed but within the 29-day authorization period described on `ReauthorizePayment` → call reauthorize.
  - If **30 days** since original authorization, reauthorize is not the path (operation notes: must create a new authorized payment). Treat as **unrenewable**; report that to operators with `ExpirationTime` and current `Status`.
  - On `ReauthorizePayment` **422/400/403**, read `Error.Name`, `Message`, `Details[].Issue`, `DebugId` and report those strings as operator-actionable (issue codes are **plain strings**, not an SDK enum). **UNVERIFIED** exact `Issue` literals for expired vs max-reauth.
- **Error**: Case A `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback.

#### 5. `client.Payments.ReauthorizePayment` — renew stale authorization

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/reauthorize` · `operations/Payments.md`
- **Signature**: `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId`, `payPalAuthAssertion`, `body` must be passed explicitly.
- **Request** `ReauthorizeRequest`: `Amount (amount): Money?` only. `records-2-Pa-Ve.md`
- **Returns** `PaymentAuthorization` — persist the **new** `Id` / `Status` / `ExpirationTime` (replace the previous authorization id used for capture).
- **Notes (map)**: described as reauthorizing “an authorized PayPal account payment”; honor period 3 days; reauthorize from day 4–29; after 30 days create a new authorization instead. **UNVERIFIED** whether card authorizations succeed on this operation; on failure, do not invent a second API — report unrenewable from the error payload.
- **Error**: Case A `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback.

#### 6. `client.Payments.CaptureAuthorizedPayment` — fulfil (take the hold)

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/capture` · `operations/Payments.md`
- **Signature**: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullables must be passed.
- **Request** `CaptureRequest`: `Amount (amount): Money?` (omit for full remaining) · `InvoiceId` · `FinalCapture (final_capture): bool? = false` → set **`true`** for a complete fulfil · `NoteToPayer` · `SoftDescriptor` · `PaymentInstruction`. `records-1-Ac-Pa.md`
- **Returns** `CapturedPayment` (not wrapped). Read:
  - `Id (id)` capture id
  - `Status (status): CaptureStatus?`
  - `Amount (amount): Money?` — captured amount
  - **`SellerReceivableBreakdown`**: `GrossAmount (gross_amount): Money !req` · **`PaypalFee (paypal_fee): Money?`** · **`NetAmount (net_amount): Money?`** · `PaypalFeeInReceivableCurrency` · `ReceivableAmount` · `ExchangeRate` · `PlatformFees`
  - `ProcessorResponse` · timestamps
  - Note: breakdown “is not available for transactions that are in pending state”. `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`
- **Do not** use `Orders.CaptureOrder` for this flow (that captures an order, typically `CAPTURE` intent). Fulfilment is this payments capture of the **authorization id**.
- **Error**: Case A `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback. **409** = already-captured / conflict class (read `Details[].Issue`).

#### 7. `client.Payments.VoidPayment` — cancel before fulfilment

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/void` · `operations/Payments.md`
- **Signature**: `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — three nullables must be passed. **No body.**
- **Returns** `PaymentAuthorization` (`Status` expected `Voided`).
- **Notes**: cannot void a fully captured authorization.
- **Error**: Case A `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401, 403, 404, **409**, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback.

#### 8. `client.Payments.GetCapturedPayment` — refresh capture (fee / remaining refundability)

- **HTTP**: `GET /v2/payments/captures/{capture_id}` · `operations/Payments.md`
- **Signature**: `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns** `CapturedPayment` — use `Status` (`Completed` / `PartiallyRefunded` / `Refunded`) and `Amount` vs sum of refunds to refuse refunds beyond captured.
- **Error**: Case A `SdkException<GetCapturedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError`.

#### 9. `client.Payments.RefundCapturedPayment` — full or partial refund

- **HTTP**: `POST /v2/payments/captures/{capture_id}/refund` · `operations/Payments.md`
- **Signature**: `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullables must be passed. **Caller idempotency key → `payPalRequestId`.**
- **Request** `RefundRequest`: full refund → `body: null` (or empty object; map: “empty payload”). Partial → `Amount (amount): Money?` plus optional `CustomId` · `InvoiceId` · `NoteToPayer`. `records-2-Pa-Ve.md`
- **Returns** `Refund`: `Id` · `Status: RefundStatus?` · `Amount: Money?` · `SellerPayableBreakdown` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount`) · timestamps. `records-2-Pa-Ve.md`
- **Guard**: if capture `Status` is `Refunded`, or remaining (`captured Amount.Value` − sum of completed refunds) would go negative, refuse locally before calling.
- **Error**: Case A `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent` [500] · `TryGetRawError`.

#### 10. `client.Payments.GetRefund` (optional lookup)

- **HTTP**: `GET /v2/payments/refunds/{refund_id}` · `operations/Payments.md`
- **Signature**: `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns** `Refund` · **Error**: Case A `SdkException<GetRefundError>` — `TryGetError` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError`.

#### 11. `client.Vault.CreatePaymentToken` — save card

- **HTTP**: `POST /v3/vault/payment-tokens` · `operations/Vault.md`
- **Signature**: `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must be passed explicitly.
- **Request** `PaymentTokenRequest`: `Customer (customer): Customer?` · `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`
  - `Customer`: `Id (id)` = PayPal-generated customer id (omit on first save) · `MerchantCustomerId (merchant_customer_id)` = eShop shopper id (`Models/Customer.cs`)
  - `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?` (`Name`, `Number`, `Expiry` YYYY-MM, `SecurityCode`, `Brand?`, `BillingAddress`) · `Token (token): VaultTokenRequest?` (setup-token exchange; unused for PAN vault)
- **Returns** `PaymentTokenResponse`: `Id (id)` (vault token; later `CardRequest.VaultId`) · `Customer: CustomerResponse?` (`Id`, `MerchantCustomerId`) — **persist `Customer.Id` for list** · `PaymentSource.Card: CardPaymentTokenEntity?` **safe display**: `LastDigits (last_digits)` · `Brand (brand): CardBrand?` · `Expiry (expiry)` · `Name` · `Type` · `VerificationStatus`. Never persist `Number` / `SecurityCode`.
- **Error**: Case A `SdkException<CreatePaymentTokenError>` — **`TryGetError1(out Error1)`** [400, 403, 404, 422, 500] · `TryGetRawError` fallback. (`Error1`, not `Error`.)

#### 12. `client.Vault.ListCustomerPaymentTokens` — list saved cards (SDK-supported, not merchant-only)

- **HTTP**: `GET /v3/vault/payment-tokens` · `operations/Vault.md`
- **Signature**: `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query**: `customer_id` ← `customerId` (PayPal customer id from vault response) · `page_size` · `page` · `total_required`
- **Returns** `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` · `TotalItems` · `TotalPages` · `Customer` · `Links`
- **Pagination**: pass `totalRequired: true`; loop `page = 1 .. TotalPages` (map: only `page`, no `perPage`). Default `pageSize` is 5.
- **Error**: Case A `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`.

#### 13. `client.Vault.GetPaymentToken` / `DeletePaymentToken`

| Op | HTTP | Signature | Returns | Error |
|---|---|---|---|---|
| `GetPaymentToken` | `GET /v3/vault/payment-tokens/{id}` | `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenResponse` | Case A `GetPaymentTokenError` `TryGetError1` [403, 404, 422, 500] |
| `DeletePaymentToken` | `DELETE /v3/vault/payment-tokens/{id}` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `void` (Task) | Case A `DeletePaymentTokenError` `TryGetError1` [400, 403, 500] |

After delete, token must not appear in list and `VaultId` authorize must fail (404/422 on PayPal). `operations/Vault.md`.

`CreateSetupToken` / `GetSetupToken` exist but are a setup-token → payer-action flow (`PaymentTokenStatus.PayerActionRequired`). **Do not use** for this no-browser integration; vault via `CreatePaymentToken`.

#### 14. `client.TransactionSearch.SearchTransactions` — reconciliation

- **HTTP**: `GET /v1/reporting/transactions` · `operations/TransactionSearch.md`
- **Signature**: `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 8 params (`transactionId` … `terminalId`) nullable, **must pass explicitly** (`null` to skip).
- **Query**: `start_date` ← `startDate`, `end_date` ← `endDate` (RFC3339; **seconds required**; max window **31 days** — `Api/TransactionSearch.cs`). For a longer from/to, split into adjacent ≤31-day windows and page each.
- **Whole range**: `pageSize` default 100; increment `page` from 1 while `page <= TotalPages`. Map pagination: “only `page`, no `perPage`”. Pass `fields: "all"` (or at least `transaction_info`) so fee/amount fields populate. Executed txns can take up to **three hours** to appear.
- **Returns** `SearchResponse`: `TransactionDetails (transaction_details)` · `StartDate` · `EndDate` · `Page` · `TotalItems` · `TotalPages` · `LastRefreshedDatetime` · `Links`. Each `TransactionDetails.TransactionInfo: TransactionInformation` includes `TransactionId` · `PaypalReferenceId` · `TransactionInitiationDate` · `TransactionAmount: Money?` · `FeeAmount: Money?` · `TransactionStatus: string?` · `InvoiceId` · `CustomField` · `PaymentMethodType`. `records-2-Pa-Ve.md`
- **Error**: **Case B** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — `StatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()`. (This is the **only** Case B operation in the SDK.) Optional body shape `SearchError` exists as a model if you `ReadAsJson<SearchError>()`.

`SearchBalances` is out of scope.

---

### Error payload shapes (read status / issue / duplicate / 3DS)

| Type | Namespace | Fields | Used by |
|---|---|---|---|
| `SdkException<TError>` | `PayPalServerSdk.Core.Exceptions` | `Error: TError` | all ops |
| `Error` | `PayPalServerSdk.Models` | `Name (name): string !req` · `Message (message): string !req` · `DebugId (debug_id): string !req` · `Details (details): IReadOnlyList<ErrorDetails>?` · `Links` | Orders + Payments Case A `TryGetError` |
| `ErrorDetails` | `PayPalServerSdk.Models` | `Issue (issue): string !req` · `Description (description): string?` · `Field` · `Value` · `Location` default `"body"` · `Links` | issue codes (**strings, not an enum**) |
| `Error1` / `ErrorDetails1` | `PayPalServerSdk.Models` | same idea; vault `TryGetError1` | Vault Case A |
| `RawError` | `PayPalServerSdk.Core.ErrorResponse` | `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` | Case B; `TryGetRawError`; `TryGetNoContent` |
| `ApiError` | `PayPalServerSdk.Core.ErrorResponse` | `TryGetRawError(out RawError)` | base of typed errors |

**How to classify (SDK-grounded; do not hard-code unlisted issue literals):**

| Situation | What to read |
|---|---|
| 3DS / browser challenge | Success path: `OrderStatus.PayerActionRequired` → STOP. Error path: `Error.Details[].Issue` + `CardResponse.AuthenticationResult`. **UNVERIFIED** exact issue string. |
| Insufficient funds / card decline | `TryGetError` 422/400 + `Details[].Issue` + `ProcessorResponse.ResponseCode`. Surface those strings. **UNVERIFIED** which `Issue` / `ProcessorResponseCode` member is NSF. |
| Expired / unrenewable auth | `ExpirationTime` + `AuthorizationStatus`; reauthorize 422 → `Details[].Issue`; 30-day rule from `ReauthorizePayment` notes. |
| Already captured / voided / duplicate | HTTP **409** on capture/void/refund (`TryGetError`); plus `AuthorizationStatus.Captured`/`Voided`, `CaptureStatus`. |
| Duplicate write | Caller `payPalRequestId` + local state gate; 409 `Details[].Issue`. |
| Vault errors | `TryGetError1(out Error1)` then `Error1.Details[].Issue`. |

HTTP status is **not** a property on `Error`; infer from which accessor matched, or `RawError.StatusCode` on fallback / Case B.

Persist on the eShop payment record (PayPal-owned state for later calls): PayPal `Order.Id`, `Authorization.Id` + `Status` + `ExpirationTime`, `CapturedPayment.Id` + `Status` + `Amount` + `PaypalFee` + `NetAmount`, each `Refund.Id` + `Status` + `Amount`, vault `PaymentTokenResponse.Id` + PayPal `Customer.Id`. Never PAN/CVC.

Sandbox card (user): Visa `4111111111111111`, any future `YYYY-MM`, any CVC.

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` lifetime / factory vs per-request construction, and DI vs `new PayPalServerSdkClient`. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (credentials) — `Oauth2` must be set before use; secrets from config/env, not literals. **MUST load `dotnet-authentication`** before assigning credentials.

⚠ Step 1 (BaseUrl + retries) — `Server.Default.Sandbox.BaseUrl` is the override for **all** paths including `/v1/oauth2/token`; retry/timeout options on the SDK are not the timeout on the `HttpClient` you register, and which verbs retry on transport failure vs status codes. **MUST load `dotnet-configuration-resilience`** before registering or tuning the client.

⚠ Step 1 (logging PAN) — enabling request-body logging can write card `number` / `security_code` (not in `LoggingOptions.RedactedKeys`). **MUST load `dotnet-configuration-resilience`** before turning logging on; never log full card details.

⚠ Steps 3–8 (calls) — many optional parameters have **no C# default** and mis-bind if passed positionally; cancellation token is `ct`. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}(...)`.

⚠ Steps 3, 8, 11 (models) — enums are `StringEnum<T>` not C# enums; records are `init`/`required`; amounts are **strings**. **MUST load `dotnet-models`** before building `OrderRequest` / `CardRequest` / `PaymentTokenRequest`.

⚠ Step 9 (errors) — Orders/Payments are `TryGetError(out Error)`; Vault is `TryGetError1(out Error1)`; SearchTransactions is Case B `SdkException<RawError>`; `TryGetRawError` is not a catch-all on typed errors; 500 on several Payments ops is `TryGetNoContent`. **MUST load `dotnet-error-handling`** before writing any try/catch.

⚠ Step 9 (JsonException, 2xx) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 9 (JsonException, non-2xx) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 4 (capture retries) — a failed capture/authorize/void/refund may or may not be safe to re-send depending on retry policy vs `payPalRequestId`. **MUST load `dotnet-configuration-resilience`** before enabling retries on writes.

⚠ Step 10 (tests) — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## REQUIRED READING

Load these **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1 client/DI/`HttpClient` lifetime
- `dotnet-authentication` — Step 1 `Oauth2` credentials
- `dotnet-configuration-resilience` — Step 1 BaseUrl/retries/timeouts/logging; Step 4 write retries; Step 7 pagination
- `dotnet-calling-endpoints` — Steps 3–8 every operation call (named args, `ct`)
- `dotnet-models` — Steps 3, 8, 11 request/response records and `StringEnum<T>`
- `dotnet-error-handling` — Step 9 boundary (Case A/B, vault `TryGetError1`, SearchTransactions Case B, both `JsonException` directions)
- `dotnet-testing` — Step 10 tests

---

## Assumptions & Blockers

- **Assumption:** Fulfilment captures via `Payments.CaptureAuthorizedPayment` (authorization id), not `Orders.CaptureOrder`. Create uses `CheckoutPaymentIntent.Authorize` only.
- **Assumption:** Saved-card pay uses `CardRequest.VaultId` = `PaymentTokenResponse.Id`. `Token` / `TokenType` is billing-agreement only and is not the vault instrument.
- **Assumption:** List saved cards uses PayPal `CustomerResponse.Id` as `ListCustomerPaymentTokens(customerId: …)`. Persist that id at vault time (plus `MerchantCustomerId` for eShop correlation).
- **Assumption:** Target is sandbox only. `ServerEnvironment` has **no Live/Production member** — if `PayPal:Environment` is not sandbox, fail configuration rather than guessing a host.
- **Assumption:** `CreateSetupToken` is out of scope (can return `PayerActionRequired`). Vault is `CreatePaymentToken` with PAN in the vault call only (never stored in the app DB or logs).
- **UNVERIFIED (defensive):** Exact PayPal `ErrorDetails.Issue` strings for 3DS, NSF, expired auth, duplicate. Read and surface `Name`/`Message`/`Issue`/`DebugId`; classify 3DS primarily via `OrderStatus.PayerActionRequired`.
- **UNVERIFIED:** Whether `ReauthorizePayment` succeeds for **card** authorizations (notes say “PayPal account payment”). Try it when stale; on 422/400/403 report unrenewable with the error payload. No second API exists in this SDK to “renew” a card auth after 30 days except creating a new authorize (which would be a new hold — out of scope unless product asks).
- **UNVERIFIED:** Interaction of SDK-generated `Idempotency-Key: Guid.NewGuid()` with caller `PayPal-Request-Id`. Still pass stable `payPalRequestId` and gate on local payment state.
- **Not a GAP:** Transaction search is in the SDK; cover the whole from/to by paging `TotalPages` and splitting >31-day ranges.
- **Not a GAP:** Vault list/get/delete are in the SDK (`ListCustomerPaymentTokens` is not merchant-side-only).
- **GAP:** This SDK’s `ServerEnvironment` cannot target live PayPal. Sandbox-only is in scope; live would be blocked.
- **GAP:** No caller-settable `Idempotency-Key` parameter (SDK always generates a new GUID). Caller-supplied idempotency is `payPalRequestId` only.
- **No GAP invented:** There is no modeled catalog of PayPal issue codes; do not hard-code unofficial literals.
