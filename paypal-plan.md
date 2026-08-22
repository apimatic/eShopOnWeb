# PayPal .NET SDK — eShopOnWeb integration plan + CONTRACT SHEET

NuGet: `AsadAli.Checkout.Sdk` (version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`. Map stamp: tag `v1.0.1` / commit `9653d18`.

---

## Scope & sequence

| Step | Capability | Operations |
|---:|---|---|
| 1 | Client + credentials + optional BaseUrl | `new PayPalServerSdkClient` / `AddPayPalServerSdkClient` |
| 2 | Direct card authorize (hold, no capture) | `Orders.CreateOrder` → `Orders.AuthorizeOrder` (intent `AUTHORIZE`) |
| 3 | Pay with vaulted card | Same pair; `CardRequest.VaultId` instead of PAN |
| 4 | Capture at fulfilment | `Payments.GetAuthorizedPayment` → (optional) `Payments.ReauthorizePayment` → `Payments.CaptureAuthorizedPayment` |
| 5 | Reauthorize stale hold | `Payments.GetAuthorizedPayment` + `Payments.ReauthorizePayment` |
| 6 | Void on cancel | `Payments.VoidPayment` |
| 7 | Refund after capture (full/partial) | `Payments.RefundCapturedPayment` (+ `Payments.GetCapturedPayment` / `GetRefund` as needed) |
| 8 | Save card (vault) | `Vault.CreatePaymentToken` |
| 9 | Delete saved card | `Vault.DeletePaymentToken` |
| 10 | Transaction search (all pages) | `TransactionSearch.SearchTransactions` |
| 11 | Error boundary + persist PayPal ids/statuses | all of the above |

Supporting reads: `Orders.GetOrder`, `Payments.GetAuthorizedPayment`, `Payments.GetCapturedPayment`, `Payments.GetRefund`, `Vault.GetPaymentToken`, `Vault.ListCustomerPaymentTokens`.

Not used: `Orders.CaptureOrder` (that captures the *order*, not the authorization hold), Subscriptions, `CreateSetupToken` (optional two-step vault; this plan vaults in one shot via `CreatePaymentToken`).

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

Unions: **none** in this SDK (`map/models/unions.md` — 0 OneOf, 0 AnyOf). Nested objects, not unions.

Enums are `StringEnum<T>` records in `PayPalServerSdk.Models.Enums` — write `CheckoutPaymentIntent.Authorize`, not a C# enum. Wire values in parentheses below.

No-throw `…Result` variants: **absent** on every operation.

---

### Client construction, auth, environment, BaseUrl

| Fact | Value | Cite |
|---|---|---|
| Client | `PayPalServerSdk.PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI | `services.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` — singleton client, `IHttpClientFactory` | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| Options type | `PayPalServerSdk.PayPalServerSdkClientOptions` | `PayPalServerSdkClientOptions.cs` |
| Auth property | `Oauth2: PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?` | `sdk-map.md` *Servers & auth* |
| Credentials members | `ClientId: string` **required**, `ClientSecret: string` **required**, `Scope: string?` | `OAuth2ClientCredentials.cs` |
| Token strategy | `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?` — leave **unset**; the client default is Basic-auth client-credentials against `POST {BaseUrl}/v1/oauth2/token` | `AuthSchemes.cs` |
| Environment type | `PayPalServerSdk.Servers.ServerEnvironment` — **only member** `Sandbox` (wire `"Sandbox"`). `Default()` → `Sandbox`. **No Production member.** | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Config → SDK | `PayPal:ClientId` → `options.Oauth2.ClientId`; `PayPal:ClientSecret` → `options.Oauth2.ClientSecret`; `PayPal:Environment` → `options.Environment` (**only** `ServerEnvironment.Sandbox` is valid); `PayPal:Currency` → `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode` on every amount (not a client option); `PayPal:BaseUrl` → see next row | this sheet |
| **BaseUrl override (verbatim, every call including token)** | `options.Server` is `PayPalServerSdk.ServerOptions` (root ns). Nested: `Server.Default` is `PayPalServerSdk.Servers.DefaultOptions`. Nested: `Default.Sandbox` is `DefaultOptions.SandboxOptions` with `BaseUrl: string` default `"https://api-m.sandbox.paypal.com"`. **Set `options.Server.Default.Sandbox.BaseUrl = configuration["PayPal:BaseUrl"]` when that key is non-empty, as-is.** Token request uses the same resolver: `server.Default("/v1/oauth2/token")` → `{BaseUrl}/v1/oauth2/token`. API calls use `server.Default("/v2/…")` etc. **Not a gap** — one property covers credential/token and every REST call. Only Sandbox exists, so there is no other environment node to set. | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Server.cs`, `AuthSchemes.cs` |
| Retry/logging | `options.Retry: PayPalServerSdk.Core.Configuration.RetryOptions`; `options.Logging: LoggingOptions` | `sdk-map.md` |

```csharp
using PayPalServerSdk;
using PayPalServerSdk.Servers;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;

var options = new PayPalServerSdkClientOptions
{
    Environment = ServerEnvironment.Sandbox,
    Oauth2 = new OAuth2ClientCredentials
    {
        ClientId = config["PayPal:ClientId"]!,
        ClientSecret = config["PayPal:ClientSecret"]!,
    },
};
var baseUrl = config["PayPal:BaseUrl"];
if (!string.IsNullOrWhiteSpace(baseUrl))
    options.Server.Default.Sandbox.BaseUrl = baseUrl; // verbatim; token + API
```

---

### Idempotency (`PayPal-Request-Id`)

Caller-supplied idempotency is the C# parameter `payPalRequestId` (nullable, **must pass explicitly** — `null` to skip). It is sent as HTTP header **`PayPal-Request-Id`**. `RequestOptions` (`PayPalServerSdk.Core.RequestOptions`) has **only** `LogLevel` — it cannot carry this header. `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`.

| Operation | `payPalRequestId` param? | Header | Server key TTL (XML) |
|---|---|---|---|
| `CreateOrder` | yes | `PayPal-Request-Id` | 6h (mandatory when Create includes a payment source) |
| `AuthorizeOrder` | yes | `PayPal-Request-Id` | 6h |
| `CaptureAuthorizedPayment` | yes | `PayPal-Request-Id` | 45 days |
| `ReauthorizePayment` | yes | `PayPal-Request-Id` | 45 days |
| `VoidPayment` | yes | `PayPal-Request-Id` | 45 days |
| `RefundCapturedPayment` | yes — **this is the caller-supplied refund idempotency key** | `PayPal-Request-Id` | 45 days |
| `CreatePaymentToken` | yes | `PayPal-Request-Id` | 3h |
| `DeletePaymentToken` | **no parameter** | SDK sends only `Idempotency-Key: Guid.NewGuid()` (not caller-controlled, not `PayPal-Request-Id`) | — |
| GETs (`GetOrder`, `GetAuthorizedPayment`, `GetCapturedPayment`, `GetRefund`, `GetPaymentToken`, `SearchTransactions`) | none | n/a | n/a |

Generate one key per shopper/operator intent (checkout id, fulfilment id, refund id) and reuse it on retry/double-click. Do **not** generate a new key on retry.

The SDK also attaches a fresh `Idempotency-Key: Guid.NewGuid()` on these POSTs — that is **not** the PayPal-Request-Id contract and is not caller-stable. Idempotency-in-effect for writes is `payPalRequestId`.

---

### Prefer header (minimal vs representation)

Several mutating ops default `prefer = "return=minimal"` (id/status/links only). Fee, net, authorizations collection, processor_response need the full resource: pass **`prefer: "return=representation"`** on `AuthorizeOrder`, `CaptureAuthorizedPayment`, `ReauthorizePayment`, `VoidPayment`, `RefundCapturedPayment`. XML on `VoidPayment` / capture: `return=representation` returns the complete resource.

---

### Amounts

`PayPalServerSdk.Models.Money` and `AmountWithBreakdown` both: `CurrencyCode (currency_code): string !req` (ISO-4217, length 3), `Value (value): string !req`. Format `Value` as a decimal string with **2 decimal places** for USD-style currencies (e.g. `"12.50"`), equal to the eShop order total to the cent. `AmountWithBreakdown.Breakdown` is optional; omit unless line items are sent. Cite: `records-1-Ac-Pa.md` (`Money`, `AmountWithBreakdown`).

---

### Enums actually used

| Enum | Members (C# (wire)) | Use |
|---|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` | CreateOrder **must** be `Authorize` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` | After create/authorize. **`PayerActionRequired` = 3DS/challenge — BLOCKER** (XML: redirect to `rel:payer-action`; we will **not** implement that) |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` | Hold state. **No `Expired` member** — expiry is `ExpirationTime` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` | When status `Pending` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` | After capture / refund remaining |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` | After refund |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, … `Unknown (UNKNOWN)` | Safe card display only |
| `ParesStatus` | `Y (Y)` success, `N (N)` failed, `U (U)` unable, `A (A)` attempt, **`C (C)` challenge required**, `R (R)` rejected, **`D (D)` challenge/decoupled**, `I (I)` informational | 3DS outcome |
| `EnrollmentStatus` | `Y (Y)`, `N (N)`, `U (U)`, `B (B)` | 3DS enrollment |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` | 3DS liability |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` | Default on `CardVerification.Method` is `ScaWhenRequired` |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` | Only if vaulting *during* checkout (not required for dedicated save-card) |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` | Stored credential on vaulted-card pay |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` | Same |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` | Same |
| `TokenType` | **`BillingAgreement (BILLING_AGREEMENT)` only** | Do **not** use `PaymentSource.Token` for vaulted cards |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` | Only if converting a setup token; not this plan |
| `LinkHttpMethod` | `Get`, `Post`, `Put`, `Delete`, … | HATEOAS `LinkDescription.Method` |
| `ProcessorResponseCode` (subset) | `_0000 (0000)` APPROVED; `_5120 (5120)` INSUFFICIENT_FUNDS; `_0500 (0500)` DO_NOT_HONOR; `_5180 (5180)` INVALID_OR_RESTRICTED_CARD; `_5200 (5200)` DUPLICATE_TRANSACTION; `_5400 (5400)` EXPIRED_CARD; `_5650 (5650)` DECLINED_SCA_REQUIRED; `Ppef (PPEF)` EXPIRED_FUNDING_INSTRUMENT; `Ppfi (PPFI)` INVALID_FUNDING_INSTRUMENT; `Pps2 (PPS2)` BANKAUTH_ROW_VOIDED; `Pps3 (PPS3)` BANKAUTH_EXPIRED | Decline / duplicate / expired-auth / 3DS |
| `AvsCode` / `CvvCode` | listed in `enums.md` | Processor AVS/CVV |

Cite: `map/models/enums.md`; XML on `OrderStatus.PayerActionRequired` and `ParesStatus.C`/`D`; `ProcessorResponseCode.cs`.

---

### Step 1 — CreateOrder (`client.Orders`)

- **HTTP**: `POST /v2/checkout/orders` · `operations/Orders.md` · `Api/Orders.cs`
- **Signature**: `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params (`payPalMockResponse` … `payPalAuthAssertion`) nullable, **no default → pass `null` to skip**
- **Request** `PayPalServerSdk.Models.OrderRequest` (`records-1-Ac-Pa.md`):
  - `Intent (intent): CheckoutPaymentIntent !req` → `CheckoutPaymentIntent.Authorize`
  - `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` — one unit
  - `PaymentSource (payment_source): PaymentSource?` — **omit on create** (card goes on AuthorizeOrder)
  - `Payer`, `ApplicationContext` — omit
- **PurchaseUnitRequest** (`records-2-Pa-Ve.md`): `Amount (amount): AmountWithBreakdown !req`; set `CustomId (custom_id): string?` and/or `InvoiceId (invoice_id): string?` to the eShop order number (reconciliation key). Other fields optional.
- **Response** `Order` (not an envelope wrapper — the return *is* the order): `Id (id)`, `Status (status)`, `Intent (intent)`, `PurchaseUnits (purchase_units)`, `Links (links)`, `CreateTime`/`UpdateTime`. Persist `Id`.
- **Error**: Case A `SdkException<PayPalServerSdk.Errors.CreateOrderError>` — `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback
- **Idempotency**: `payPalRequestId` → `PayPal-Request-Id`

### Step 2 — AuthorizeOrder (raw card or vaulted)

- **HTTP**: `POST /v2/checkout/orders/{id}/authorize` · `operations/Orders.md`
- **Signature**: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params (`payPalMockResponse` … `body`) nullable, **must pass explicitly**
- **Pass** `id:` = PayPal order id from CreateOrder; `prefer: "return=representation"`; stable `payPalRequestId`.
- **Request** `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`
  - **Raw card**: `payment_source.card` = `CardRequest` (`records-1-Ac-Pa.md`):
    - `Name (name): string?`
    - `Number (number): string?` — PAN, 13–19 digits; **never persist**
    - `Expiry (expiry): string?` — **`YYYY-MM`** (length 7)
    - `SecurityCode (security_code): string?` — 3–4 digits; **never persist**
    - `BillingAddress (billing_address): Address?` — `Address.CountryCode (country_code): string !req`; also `AddressLine1 (address_line_1)`, `AdminArea2 (admin_area_2)` city, `AdminArea1 (admin_area_1)` state, `PostalCode (postal_code)`
    - Do **not** set `ExperienceContext` (that is the 3DS browser return/cancel round-trip — out of scope)
  - **Vaulted card**: `CardRequest { VaultId = <PaymentTokenResponse.Id> }` only (no PAN). Optional `StoredCredential`: `PaymentInitiator !req` = `Customer`, `PaymentType !req` = `OneTime`, `Usage` = `Subsequent`.
  - **Do not** use `OrderAuthorizeRequestPaymentSource.Token` (`Token.Type` is only `BillingAgreement`).
- **Sandbox test card**: Visa `4111111111111111`, any future `YYYY-MM`, any CVC, any name/address.
- **Response** `OrderAuthorizeResponse` (`records-1-Ac-Pa.md`): `Id (id)`, `Status (status)`, `Intent (intent)`, `PaymentSource (payment_source): OrderAuthorizeResponsePaymentSource?` → `Card (card): CardResponse?` (`LastDigits (last_digits)`, `Brand (brand)`, `Expiry (expiry)`, `AuthenticationResult (authentication_result): AuthenticationResponse?` → `ThreeDSecure (three_d_secure): ThreeDSecureAuthenticationResponse?` with `AuthenticationStatus (authentication_status): ParesStatus?`, `EnrollmentStatus`), `PurchaseUnits (purchase_units)`, `Links (links)`.
- **Authorization id path**: `PurchaseUnits[0].Payments.Authorizations[0]` is `AuthorizationWithAdditionalData` (`records-1-Ac-Pa.md`): `Id (id)`, `Status (status)`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`, `CreateTime (create_time)`, `ProcessorResponse (processor_response): ProcessorResponse?` (`AvsCode`, `CvvCode`, `ResponseCode`, `PaymentAdviceCode`), `Links`. Nested: `PurchaseUnit.Payments (payments): PaymentCollection` → `Authorizations (authorizations)`.
- **3DS / challenge BLOCKER (do not design a browser round-trip)**: if `Status == OrderStatus.PayerActionRequired`, or any `Links` entry has `Rel == "payer-action"`, or `ParesStatus` is `C` or `D`, or `ProcessorResponse.ResponseCode == ProcessorResponseCode._5650` — **stop**. Surface a shopper-visible failure. Do not follow `payer-action` href. (XML on `OrderStatus.PayerActionRequired` tells the caller to redirect; this integration must not.)
- **Success hold**: `AuthorizationStatus.Created` (or `Pending` only if product accepts review). Persist authorization `Id`, `ExpirationTime`, `Status`, PayPal order `Id`.
- **Error**: Case A `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError` fallback

`CardRequest` PCI note (XML): passing PAN/CVV/expiry via API requires PCI SAQ D. Full PAN is never stored in our DB.

### Step 3 — GetAuthorizedPayment (staleness / status)

- **HTTP**: `GET /v2/payments/authorizations/{authorization_id}` · `operations/Payments.md`
- **Signature**: `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — two nullables **must pass explicitly**
- **Returns** `PaymentAuthorization` (`records-2-Pa-Ve.md`): `Id`, `Status`, `StatusDetails.Reason`, `Amount`, `ExpirationTime`, `CreateTime`, `UpdateTime`, `Links`
- **Error**: Case A `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback
- **When reauthorize is needed** (operation notes, `operations/Payments.md`): honor period is **3 days**; authorization window **29 days**. Reauthorize **after** the 3-day honor period expires and **before** day 30. Signals: `ExpirationTime` is in the past or within an operator-chosen safety margin; status still `Created`. If `CreateTime` is ≥ 30 days ago, **do not** call reauthorize — it cannot be renewed (notes: create a new authorized payment instead; we have no shopper present at fulfilment → operator-actionable failure).
- **Cannot be renewed** (any of): status `Captured` / `PartiallyCaptured` / `Voided` / `Denied`; `CreateTime` ≥ 30 days; `ReauthorizePayment` returns **422** (or 404); map-internal disagreement — op notes say multiple reauths in the 29-day window, `ReauthorizeRequest` summary says **only once** from days 4–29. Treat a 422 as terminal. Surface `Error.Name`, `Details[].Issue`, `Details[].Description`, `DebugId` to the operator.

### Step 4 — ReauthorizePayment

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/reauthorize` · `operations/Payments.md`
- **Signature**: `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 nullables **must pass explicitly**
- **Request** `ReauthorizeRequest`: `Amount (amount): Money?` only (notes: supports only `amount`). Pass the original hold amount (2 decimal string + currency).
- **Returns** `PaymentAuthorization` — **new** `Id`. Persist it and use **this** id for later capture/void. `prefer: "return=representation"`.
- **Error**: Case A `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent` [500] · `TryGetRawError` fallback. **422 / 404 = cannot renew** → operator-actionable.
- **Idempotency**: `payPalRequestId` → `PayPal-Request-Id`

### Step 5 — CaptureAuthorizedPayment (fulfilment)

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/capture` · `operations/Payments.md`
- **Signature**: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullables **must pass explicitly**
- **Request** `CaptureRequest` (`records-1-Ac-Pa.md`): `Amount (amount): Money?` (omit or set equal to authorized amount), `FinalCapture (final_capture): bool? = false` → set **`true`**, optional `InvoiceId`, `NoteToPayer`, `SoftDescriptor`
- **Must** `prefer: "return=representation"` so breakdown is present
- **Returns** `CapturedPayment` (`records-1-Ac-Pa.md`): `Id (id)` **capture id**, `Status (status)`, `Amount (amount)` captured amount, `SellerReceivableBreakdown (seller_receivable_breakdown)`:
  - `GrossAmount (gross_amount): Money !req` — captured amount
  - `PaypalFee (paypal_fee): Money?` — PayPal's fee
  - `NetAmount (net_amount): Money?` — net proceeds
  - also `PaypalFeeInReceivableCurrency`, `ReceivableAmount`, `ExchangeRate`, `PlatformFees`
  - `ProcessorResponse`, `Links`, `CreateTime`
- If breakdown is null (pending capture), `Status` may be `Pending`; retry read via `GetCapturedPayment`.
- **Error**: Case A `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent` [500] · `TryGetRawError`. **409** = conflict (already captured / terminal auth).
- **Idempotency**: `payPalRequestId` → `PayPal-Request-Id` (same key per fulfilment)

`GetCapturedPayment(string captureId, string? payPalMockResponse, …)` returns the same `CapturedPayment` if the capture call used minimal prefer. Error: `GetCapturedPaymentError` `TryGetError` [401, 403, 404] · `TryGetNoContent` [500].

**Do not** use `Orders.CaptureOrder` for this capability — that captures an *order*, not `POST /v2/payments/authorizations/{id}/capture`.

### Step 6 — VoidPayment (cancel before fulfilment)

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/void` · `operations/Payments.md`
- **Signature**: `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 nullables **must pass explicitly** (note param order: mock, auth assertion, **then** request id)
- **Returns** `PaymentAuthorization` with `Status` → `Voided`. Notes: cannot void a fully captured auth.
- **Error**: Case A `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401, 403, 404, **409**, 422] · `TryGetNoContent` [500] · `TryGetRawError`. **409** = already captured / already voided conflict.
- **Idempotency**: `payPalRequestId` → `PayPal-Request-Id`

### Step 7 — RefundCapturedPayment

- **HTTP**: `POST /v2/payments/captures/{capture_id}/refund` · `operations/Payments.md`
- **Signature**: `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullables **must pass explicitly**
- **Request** `RefundRequest` (`records-2-Pa-Ve.md`): full refund → `body: null` (or empty `new RefundRequest()`); partial → `Amount (amount): Money?` with currency + 2-decimal value. Optional `CustomId`, `InvoiceId`, `NoteToPayer`.
- **Ceiling**: never refund more than captured. Persist `CapturedPayment.Amount.Value` and running sum of successful `Refund.Amount`. Refuse if `alreadyRefunded + requested > captured`. Also treat `CaptureStatus.Refunded` as not further refundable; `PartiallyRefunded` still allows remainder.
- **Returns** `Refund`: `Id (id)`, `Status (status)`, `Amount`, `SellerPayableBreakdown` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount (total_refunded_amount)`), `Links`, `CreateTime`
- **Error**: Case A `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent` [500] · `TryGetRawError`
- **Idempotency**: **caller-supplied** `payPalRequestId` → `PayPal-Request-Id` (required by this capability)
- `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, …)` for later status. Error: `GetRefundError` `TryGetError` [401, 403, 404] · `TryGetNoContent` [500]

### Step 8 — CreatePaymentToken (save card)

- **HTTP**: `POST /v3/vault/payment-tokens` · `operations/Vault.md`
- **Signature**: `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` **must pass explicitly**
- **Request** `PaymentTokenRequest` (`records-2-Pa-Ve.md`):
  - `Customer (customer): Customer?` — set `MerchantCustomerId (merchant_customer_id)` to the eShop user id (stable). Optional PayPal `Id (id)` if already known.
  - `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` → `Card (card): PaymentTokenRequestCard?`: `Name`, `Number`, `Expiry` (`YYYY-MM`), `SecurityCode`, `BillingAddress` (`CountryCode` !req). **Never persist PAN/CVV.**
- **Returns** `PaymentTokenResponse`: `Id (id)` **vault token / payment-method id**, `Customer (customer): CustomerResponse?` (`Id`, `MerchantCustomerId`) — persist **both** `PaymentTokenResponse.Id` and `Customer.Id` (list-tokens query uses PayPal customer id), `PaymentSource.Card: CardPaymentTokenEntity` — **safe display only**: `LastDigits (last_digits)`, `Brand (brand)`, `Expiry (expiry)`, `Name`, `VerificationStatus`. No PAN in this model.
- **Error**: Case A `SdkException<CreatePaymentTokenError>` — **`TryGetError1(out Error1)`** [400, 403, 404, 422, 500] · `TryGetRawError` fallback. Payload `Error1` (`records-1-Ac-Pa.md`): `Name`, `Message`, `DebugId` all !req, `Details: IReadOnlyList<ErrorDetails1>?` (`Issue` !req), `Links: IReadOnlyList<ErrorLinkDescription>?` (`Rel` is **optional** on error links)
- **Idempotency**: `payPalRequestId` → `PayPal-Request-Id` (3h)

`GetPaymentToken(string id, …)` same response shape. `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, …)` — query `customer_id` ← `customerId` (PayPal customer id). SDK pagination: **none** (only `page`). Walk `page = 1..TotalPages` with `totalRequired: true`. Error: `TryGetError1(out Error1)` [400, 403, 500].

### Step 9 — DeletePaymentToken

- **HTTP**: `DELETE /v3/vault/payment-tokens/{id}` · `operations/Vault.md`
- **Signature**: `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` — **no** `payPalRequestId`
- **Returns**: `void` (`Task`)
- **Error**: Case A `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError` fallback. 404 is **not** in the typed accessor list → `TryGetRawError`; treat 404 as already deleted.
- **Idempotency**: **GAP at the PayPal-Request-Id layer** — no caller header. Rely on HTTP DELETE semantics + persist “deleted” locally. Double-click may send two DELETEs with different SDK `Idempotency-Key` values.

### Step 10 — SearchTransactions (whole range)

- **HTTP**: `GET /v1/reporting/transactions` · `operations/TransactionSearch.md`
- **Signature**: `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - **8 params** (`transactionId` … `terminalId`) nullable, **no default → pass `null`**
  - Call with **named arguments**
- **Query wire ← C#**: `start_date` ← `startDate`, `end_date` ← `endDate`, … `page_size` ← `pageSize`, `page` ← `page` (ISO-8601 date-times)
- **Returns** `SearchResponse` (`records-2-Pa-Ve.md`): `TransactionDetails (transaction_details)`, `StartDate`, `EndDate`, `Page (page)`, **`TotalItems (total_items)`**, **`TotalPages (total_pages)`**, `Links`, `LastRefreshedDatetime`
- **Pagination contract**: SDK has **no auto-paginator** (map: “Pagination: none (only `page`, no `perPage`)”). Walk every page:

  1. `page = 1`, `pageSize = 100` (max default), `fields = "transaction_info"` (includes amounts/fees/invoice)
  2. Call `SearchTransactions(startDate, endDate, transactionId: null, transactionType: null, transactionStatus: null, transactionAmount: null, transactionCurrency: null, paymentInstrumentType: null, storeId: null, terminalId: null, fields: "transaction_info", pageSize: 100, page: page, ct: ct)`
  3. Align rows via `TransactionDetails[].TransactionInfo`: `TransactionId (transaction_id)`, `TransactionInitiationDate`, `TransactionAmount`, `FeeAmount (fee_amount)`, `InvoiceId (invoice_id)`, `CustomField (custom_field)`, `TransactionStatus (transaction_status)` (`TransactionInformation`, `records-2-Pa-Ve.md`) against persisted eShop `InvoiceId`/`CustomId`
  4. If `TotalPages` has a value, continue while `page < TotalPages` (`page++`). If `TotalPages` is null, continue while `TransactionDetails` is non-empty, then stop (**UNVERIFIED** whether live always sends `total_pages` — defensive stop on empty page)
- **Error**: **Case B** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. No `TryGetError`.
- Notes: up to 3 hours delay; previous 3 years only. There is **no** `payPalRequestId` on this GET.

---

### Error handling (all operations)

Thrown type is always `PayPalServerSdk.Core.Exceptions.SdkException<TError>` with `TError Error { get; init; }`. No `…Result` variants.

| Case | TError | How to read status / body |
|---|---|---|
| A (Orders + Payments + Vault) | `{Operation}Error : ApiError` in `PayPalServerSdk.Errors` | `ex.Error.TryGetError(out Error)` **or** Vault `TryGetError1(out Error1)`; Payments also `TryGetNoContent(out RawError)` on 500; always `TryGetRawError(out RawError)` fallback. Typed `Error`/`Error1`: `Name (name)`, `Message (message)`, `DebugId (debug_id)` all !req; `Details[].Issue (issue)` !req, `Description`, `Field`. HTTP status is implied by which accessor returned true (see each op row). For statuses not in the typed list, `RawError.StatusCode` + `ReadAsString()`. |
| B (`SearchTransactions` only) | `RawError` | `ex.Error.StatusCode`, `ReadAsString()` / `ReadAsJson<Error>()` — do not call `TryGet*` |

`Error` vs `Error1`: same fields; Vault error links use `ErrorLinkDescription` (`Rel` optional). Do not parse `ex.ToString()`.

**Distinguish these outcomes** (map does **not** enum `Error.Name` / `Issue` strings — match `Issue`/`Name` case-insensitively when present, but **do not** depend on a closed list; **UNVERIFIED** exact live issue strings). Grounded discriminators:

| Outcome | Grounded signals |
|---|---|
| Already captured | Auth `Status == Captured` / `PartiallyCaptured`; capture **409**; `ProcessorResponseCode.Pps1` BANKAUTH_ROW_SETTLED |
| Already voided | Auth `Status == Voided`; void **409**; `ProcessorResponseCode.Pps2` BANKAUTH_ROW_VOIDED |
| Expired auth | No `AuthorizationStatus.Expired`. Compare `ExpirationTime` to now; capture/reauthorize **422**; `ProcessorResponseCode.Pps3` BANKAUTH_EXPIRED; `Ppef` EXPIRED_FUNDING_INSTRUMENT |
| 3DS / challenge required | `OrderStatus.PayerActionRequired`; link `rel` = `payer-action`; `ParesStatus.C` or `D`; `ProcessorResponseCode._5650` DECLINED_SCA_REQUIRED — **BLOCKER**, no browser round-trip |
| Insufficient funds | `ProcessorResponseCode._5120` INSUFFICIENT_FUNDS (on authorize `ProcessorResponse` or error path if body includes it). Also `Error.Details[].Issue` best-effort |
| Instrument declined | Auth `Denied`; capture `Declined`; `_0500` DO_NOT_HONOR, `_5180` INVALID_OR_RESTRICTED_CARD, `_5150` PICKUP_CARD, `Ppfi` / `Ppfr` funding instrument |
| Duplicate request | Same `payPalRequestId` replay (success or 4xx); `_5200` DUPLICATE_TRANSACTION; **409** on capture/void/refund |

On Case A, if `TryGetError`/`TryGetError1` is false, use `TryGetRawError` / `TryGetNoContent` and still extract best-effort JSON `name`/`issue` via `ReadAsJson<Error>()`; if deserialize fails, generic message + `DebugId` when present.

---

### Identifiers & statuses to persist (wire vs C#)

| Persist | C# | Wire | Source |
|---|---|---|---|
| PayPal order id | `Order.Id` / `OrderAuthorizeResponse.Id` | `id` | create/authorize |
| Authorization id | `AuthorizationWithAdditionalData.Id` / `PaymentAuthorization.Id` | `id` | authorize; **replace on reauthorize** |
| Authorization status | `AuthorizationStatus` | `status` | hold lifecycle |
| Authorization expiry | `ExpirationTime` | `expiration_time` | ISO-8601 string |
| Capture id | `CapturedPayment.Id` | `id` | fulfilment |
| Capture status | `CaptureStatus` | `status` | |
| Captured amount | `CapturedPayment.Amount` / breakdown `GrossAmount` | `amount` / `seller_receivable_breakdown.gross_amount` | |
| PayPal fee | `SellerReceivableBreakdown.PaypalFee` | `paypal_fee` | |
| Net proceeds | `SellerReceivableBreakdown.NetAmount` | `net_amount` | |
| Refund ids (list) | `Refund.Id` | `id` | each refund |
| Refund status + amounts | `RefundStatus`, `Refund.Amount`, `TotalRefundedAmount` | `status`, `amount`, `total_refunded_amount` | |
| Vault token / payment-source id | `PaymentTokenResponse.Id` | `id` | save-card; used as `CardRequest.VaultId` (`vault_id`) |
| PayPal customer id | `CustomerResponse.Id` | `id` | `ListCustomerPaymentTokens` |
| Merchant customer id | `MerchantCustomerId` | `merchant_customer_id` | eShop user id we sent |
| Safe card descriptor | `LastDigits`, `Brand`, `Expiry` | `last_digits`, `brand`, `expiry` | never PAN |
| eShop ↔ PayPal join | `PurchaseUnitRequest.CustomId` / `InvoiceId` | `custom_id` / `invoice_id` | search `custom_field` / `invoice_id` |
| Hold/capture/refund current status | enums above | `status` | |
| Idempotency keys we sent | our DB | header `PayPal-Request-Id` | |

Related ids on some payment objects: `PaymentSupplementaryData.RelatedIds` → `OrderId (order_id)`, `AuthorizationId (authorization_id)`, `CaptureId (capture_id)`.

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` lifetime / factory vs wrapping the SDK client is not visible on the constructor. **MUST load `dotnet-client-initialization`** before `new PayPalServerSdkClient` or `AddPayPalServerSdkClient`.

⚠ Step 1 (credentials) — scheme property names, when credentials are read, and loading from config vs literals. **MUST load `dotnet-authentication`** before setting `options.Oauth2`.

⚠ Step 1 (BaseUrl / retries / timeout) — `Timeout` and retry options do **not** bound a whole call the way `HttpClient.Timeout` does; a transport failure on a write can still be resent even when the verb is not in the status-retry list — consequence: a checkout/capture/refund without a stable `payPalRequestId` can take money twice. Environment is snapshotted at construct; `Server.Default.Sandbox.BaseUrl` is the override node. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ Steps 2–10 (calls) — many optional parameters have **no C# default** and mis-bind if passed positionally (`SearchTransactions` especially). Cancellation token is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.Orders` / `Payments` / `Vault` / `TransactionSearch` call.

⚠ Steps 2–8 (models) — enums are `StringEnum<T>` (`.Authorize` not a CLR enum); `required` members must appear in object initializers; unmodeled JSON is dropped. **MUST load `dotnet-models`** before constructing `OrderRequest` / `CardRequest` / `Money`.

⚠ Step 11 (error boundary) — Case A vs Case B differ per operation (`SearchTransactions` is Case B; Vault accessors are `TryGetError1`; Payments 500 is `TryGetNoContent`). `TryGetRawError` is not a catch-all on every typed error in the way a single catch of `SdkException` would imply (generic `TError` differs). **MUST load `dotnet-error-handling`** before writing any try/catch.

⚠ Step 11 (2xx JsonException) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 11 (non-2xx JsonException) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the `HttpClient` constructor argument is the seam. **MUST load `dotnet-testing`** before stubbing PayPal.

---

## REQUIRED READING

Load these **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing / DI-registering `PayPalServerSdkClient` |
| `dotnet-authentication` | Step 1 — `Oauth2` client-credentials |
| `dotnet-configuration-resilience` | Step 1 — BaseUrl node, retries, timeouts, pagination walking |
| `dotnet-calling-endpoints` | Steps 2–10 — named args, `ct:`, must-pass nulls |
| `dotnet-models` | Steps 2–8 — records, `StringEnum<T>`, required init |
| `dotnet-error-handling` | Step 11 — Case A/B, accessors, **both** `JsonException` directions |
| `dotnet-testing` | Tests against the `HttpClient` seam |

---

## Assumptions & Blockers

1. **3DS / payer-action is a BLOCKER, not a designed flow.** If authorize (or create) returns `OrderStatus.PayerActionRequired`, a `links[].rel == "payer-action"`, `ParesStatus.C`/`D`, or processor `_5650`, fail checkout. Do not set `CardExperienceContext.ReturnUrl`/`CancelUrl` and do not redirect the shopper. Sandbox Visa `4111111111111111` often completes without 3DS; that is not a guarantee.
2. **`ServerEnvironment` has only `Sandbox`.** `PayPal:Environment` cannot select Production — there is no member. Startup should reject any other value rather than inventing a host.
3. **`PayPal:BaseUrl` is not a gap.** Setting `options.Server.Default.Sandbox.BaseUrl` applies to `/v1/oauth2/token` and every API path.
4. **`DeletePaymentToken` cannot take a caller `PayPal-Request-Id`.** Idempotency for delete is local + HTTP DELETE; the SDK’s `Idempotency-Key` is a random GUID per call.
5. **Vaulted-card pay is `CardRequest.VaultId`, not `PaymentSource.Token`.** `TokenType` has only `BillingAgreement`.
6. **Reauthorize once vs many:** `operations/Payments.md` notes say multiple reauths in the 29-day window; `ReauthorizeRequest` summary says only once (days 4–29). Treat 422 as “cannot be renewed” for the operator.
7. **`Error.Name` / `ErrorDetails.Issue` are open strings** in the map — not an enum. Classification table above uses HTTP status, resource status enums, and `ProcessorResponseCode`. Matching specific issue text is **UNVERIFIED**.
8. **`SearchResponse.TotalPages` always populated** — **UNVERIFIED**. Walk pages with a dual stop: `page >= TotalPages` or empty `TransactionDetails`.
9. **Vault “US only”** is stated on `PayPalServerSdkClient` remarks. Non-US vault may fail at runtime; not contradicted by the map.
10. **PCI SAQ D** is required to send raw PAN through `CardRequest` (model XML). This plan still does that for the requested checkout + sandbox card; production should not store PAN and must accept the PCI burden or switch to hosted fields (hosted fields are **not** in this SDK map — GAP if PCI must be avoided).
11. **No shopper at fulfilment** if the 30-day authorization window has lapsed: reauthorize is impossible; operator must take a new payment. There is no “create authorized payment from a stale id” operation beyond `ReauthorizePayment`.
12. Intent: eShop checkout uses **authorize-then-capture**, never `CheckoutPaymentIntent.Capture` / `Orders.CaptureOrder`.
)
