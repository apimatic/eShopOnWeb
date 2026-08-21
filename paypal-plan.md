# PayPal .NET SDK — eShopOnWeb payments + saved cards

Package: `AsadAli.Checkout.Sdk` (install version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`. Map stamp: tag `v1.0.1` / commit `9653d18`.

No in-scope operation is missing from the map. `Orders.CaptureOrder` is **out of this flow** (this integration uses intent `AUTHORIZE` then `Payments.CaptureAuthorizedPayment`). `Vault.CreateSetupToken` is not required (direct-card vault uses `CreatePaymentToken`). Unions: none in this SDK.

---

## Scope & sequence

| Step | What | Operations |
|---|---|---|
| 1 | Bind `PayPal:*` settings + env vars; construct/register `PayPalServerSdkClient` (sandbox; optional BaseUrl covering token + every call) | client ctor / `AddPayPalServerSdkClient` |
| 2 | Place eShop order (awaiting payment). Persist a payment record that will hold PayPal ids/statuses. | (app) |
| 3 | **AUTHORIZE (hold, do not take money)** — create a PayPal order with `CheckoutPaymentIntent.Authorize` and amount = eShop total to the cent; then authorize. Payment source is either raw `CardRequest` **or** `CardRequest.VaultId` of a saved token. Persist order id, authorization id, authorization status, expiration. | `Orders.CreateOrder` → `Orders.AuthorizeOrder` → `Orders.GetOrder` / `Payments.GetAuthorizedPayment` if nested payments are absent |
| 4 | **FULFIL → CAPTURE.** If the hold is stale (`ExpirationTime` passed), **REAUTHORIZE** first and persist the **new** authorization id. If reauthorize fails, stop with an operator-actionable message (do not capture). Then capture; persist capture id, status, gross, PayPal fee, net. | `Payments.GetAuthorizedPayment` → `Payments.ReauthorizePayment` (if stale) → `Payments.CaptureAuthorizedPayment` → `Payments.GetCapturedPayment` if fee/net missing |
| 5 | **CANCEL before fulfilment → VOID** the authorization so no money moves. | `Payments.VoidPayment` |
| 6 | **REFUND after fulfilment** (full or partial). Never refund more than captured minus already-refunded. Persist each refund id/status/amount. | `Payments.RefundCapturedPayment` → `Payments.GetRefund` / `Payments.GetCapturedPayment` to refresh remaining |
| 7 | **Reconciliation.** Search PayPal transactions for `[from, to]` (ISO-8601), covering the **whole** range (chunk ≤31 days; page through every page). Line up against eShop orders via `invoice_id` / custom id. | `TransactionSearch.SearchTransactions` |
| 8 | **Save card** for the signed-in shopper. Persist PayPal token id + safe descriptor (last digits, brand, expiry) + PayPal customer id. Never persist PAN/CVV. | `Vault.CreatePaymentToken` |
| 9 | **List** the caller’s saved cards (all pages). | `Vault.ListCustomerPaymentTokens` |
| 10 | **Delete** a saved card; afterwards it must not list and must not pay. | `Vault.DeletePaymentToken` |

**Idempotency in effect:** before any write, if the local payment already has the target PayPal id/status (authorized / captured / voided / refunded), return that state — do not call PayPal again. On every write that accepts `payPalRequestId`, pass a stable caller key. Refunds **must** use the caller-supplied idempotency key as `payPalRequestId`.

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

No-throw `…Result` variants: **absent** — every operation is throw-only. (`sdk-map.md`)

---

### Client construction, auth, environment, BaseUrl

**Namespaces**

| Type | Namespace | Source |
|---|---|---|
| `PayPalServerSdkClient` | `PayPalServerSdk` | `PayPalServerSdkClient.cs` |
| `PayPalServerSdkClientOptions` | `PayPalServerSdk` | `PayPalServerSdkClientOptions.cs` |
| `ServerOptions` | `PayPalServerSdk` | `ServerOptions.cs` |
| `DefaultOptions` / `DefaultOptions.SandboxOptions` | `PayPalServerSdk.Servers` | `Servers/DefaultOptions.cs` |
| `ServerEnvironment` | `PayPalServerSdk.Servers` | `Servers/ServerEnvironment.cs` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs` |
| `IOAuth2TokenStrategy<T>` | `PayPalServerSdk.Core.Authentication.OAuth2` | `Core/Authentication/OAuth2/IOAuth2TokenStrategy.cs` |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` | `Core/Configuration/RetryOptions.cs` |
| `LoggingOptions` | `PayPalServerSdk.Core.Configuration` | `Core/Configuration/LoggingOptions.cs` |
| `RequestOptions` | `PayPalServerSdk.Core` | `Core/RequestOptions.cs` |
| `SdkException<TError>` | `PayPalServerSdk.Core.Exceptions` | `Core/Exceptions/SdkException.cs` |
| `RawError` / `ApiError` | `PayPalServerSdk.Core.ErrorResponse` | `Core/ErrorResponse/` |
| Controllers (`Orders`, `Payments`, `Vault`, `TransactionSearch`) | `PayPalServerSdk.Api` | `Api/*.cs` |
| Records | `PayPalServerSdk.Models` | `Models/` |
| Enums (`StringEnum<T>`, **not** C# enums) | `PayPalServerSdk.Models.Enums` | `Models/Enums/` |
| Typed `{Operation}Error` | `PayPalServerSdk.Errors` | `Errors/` |

**Ctor:** `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` (`sdk-map.md`)

**DI:** `services.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` (`ServiceCollectionExtensions.cs`)

**`PayPalServerSdkClientOptions` members** (`sdk-map.md` + `PayPalServerSdkClientOptions.cs`):

| Property | Type |
|---|---|
| `Environment` | `PayPalServerSdk.Servers.ServerEnvironment` (default `ServerEnvironment.Default()` → `Sandbox`) |
| `Retry` | `PayPalServerSdk.Core.Configuration.RetryOptions` |
| `Logging` | `PayPalServerSdk.Core.Configuration.LoggingOptions` |
| `Server` | `PayPalServerSdk.ServerOptions` |
| `Oauth2` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?` |
| `Oauth2TokenStrategy` | `PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>?` |

**Auth — `OAuth2ClientCredentials`:** `ClientId: string !req`, `ClientSecret: string !req`, `Scope: string?`. Set `options.Oauth2` before constructing the client. Token grant is client_credentials; default strategy POSTs to `{BaseUrl}/v1/oauth2/token` with HTTP Basic (`ClientId:ClientSecret`) (`AuthSchemes.cs`, `OAuth2ClientCredentialsStrategy.cs`).

**Environment:** `ServerEnvironment` members: **`Sandbox` only** (wire `"Sandbox"`). There is no Live/Production member. Target sandbox for all development and testing. If `PayPal:Environment` / `PAYPAL_ENVIRONMENT` is anything other than sandbox, fail fast — do not invent a live URL. (`sdk-map.md` Servers & auth; `Servers/ServerEnvironment.cs`)

**Custom BaseUrl (covers every call including the token request):**

```
options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl verbatim>
```

- `ServerOptions.Default` is `PayPalServerSdk.Servers.DefaultOptions`.
- `DefaultOptions.Sandbox.BaseUrl` defaults to `"https://api-m.sandbox.paypal.com"`.
- `Server.Default(path)` builds `UrlTemplate(Sandbox.BaseUrl, path)`.
- Token URL is `server.Default("/v1/oauth2/token")` — **the same BaseUrl**. When `PayPal:BaseUrl` is set, use it verbatim for **every** PayPal call including the credential/token request. When unset, leave the sandbox default. (`ServerOptions.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs`)

**Settings bind (exact keys):**

| Config key | Env var | Maps to |
|---|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | `OAuth2ClientCredentials.ClientId` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | `OAuth2ClientCredentials.ClientSecret` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | must resolve to `ServerEnvironment.Sandbox` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | ISO-4217 string on every `Money` / `AmountWithBreakdown` |
| `PayPal:BaseUrl` | (optional; no env var specified) | `options.Server.Default.Sandbox.BaseUrl` when set |

**`RequestOptions`:** only `LogLevel: Microsoft.Extensions.Logging.LogLevel?`. **No extra-header bag** — idempotency is the `payPalRequestId` parameter, not `RequestOptions`. (`Core/RequestOptions.cs`)

**`prefer` (Orders/Payments writes that take it):** default `"return=minimal"` (id, status, HATEOAS links only). Pass **`prefer: "return=representation"`** so the response includes the full resource (purchase-unit payments, seller receivable breakdown). Values documented on the operation XML: `return=minimal` \| `return=representation`. (`Api/Orders.cs`, `Api/Payments.cs`)

**Headers the SDK sends on writes (caller cannot replace `Idempotency-Key`):**

| Header | Source | Notes |
|---|---|---|
| `PayPal-Request-Id` | `payPalRequestId` argument | Caller-controlled. CreateOrder: stored 6h (up to 72h). Payments capture/refund/void: stored 45 days. Vault create: stored 3 hours. **Mandatory** on CreateOrder when the body includes a payment source (card / vault_id). |
| `Prefer` | `prefer` argument | see above |
| `Idempotency-Key` | **`Guid.NewGuid()` inside the SDK on every call** | Not exposable. A double-click therefore always sends a **new** `Idempotency-Key`. Effect-idempotency = local short-circuit on persisted PayPal ids **plus** a stable `payPalRequestId`. **UNVERIFIED** which header the live API keys on. |

---

### Amount, currency, intent, customer identity

**Intent:** `PayPalServerSdk.Models.Enums.CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`). Do **not** use `.Capture` (`CAPTURE`) — that would take money on capture-order, which is not this flow. (`enums.md`)

**Amount = order total to the cent.** Types:

- `PayPalServerSdk.Models.AmountWithBreakdown` — `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?` (`records-1-Ac-Pa.md`)
- `PayPalServerSdk.Models.Money` — `CurrencyCode (currency_code): string !req`, `Value (value): string !req` (`records-1-Ac-Pa.md`)

`CurrencyCode` = `PayPal:Currency` (3-char ISO-4217). `Value` is a **string** matching `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`. Format the eShop order total as that decimal string with the fraction digits required for the currency (USD/EUR: two digits, e.g. `"19.99"`; integer currencies like JPY: no fraction). Put this on `PurchaseUnitRequest.Amount` (create) and on capture/reauthorize/refund `Money` when sending an amount. (`Models/Money.cs`, `Models/AmountWithBreakdown.cs`)

**Reconciliation join key:** set `PurchaseUnitRequest.InvoiceId (invoice_id): string?` (and optionally `CustomId (custom_id)`) to the eShop order identifier so `SearchTransactions` rows (`TransactionInformation.InvoiceId` / `CustomField`) can line up.

**Shopper ↔ PayPal customer (vaulting):**

| Field | Wire | Type | Role |
|---|---|---|---|
| `Customer.MerchantCustomerId` | `merchant_customer_id` | `string?` (1–64, `^[0-9a-zA-Z-_.^*$@#]+$`) | eShop signed-in shopper id. Send on every `CreatePaymentToken`. |
| `Customer.Id` | `id` | `string?` (PayPal-generated, 1–22) | PayPal’s customer id. Persist from `PaymentTokenResponse.Customer`. |
| `ListCustomerPaymentTokens.customerId` | query `customer_id` | `string` **required** | SDK XML: “unique identifier representing a specific customer in merchant's/partner's system or records.” Pass the **same** merchant shopper id used as `MerchantCustomerId`. **UNVERIFIED** if the live list filter actually wants PayPal’s `Customer.Id` instead: persist both; if list is empty/400 with the merchant id, retry with the PayPal-generated `Id`. |

Saved **card** id for pay-with-token is `PaymentTokenResponse.Id` (vault token), **not** the customer id. Pay with `CardRequest.VaultId (vault_id)`. Do **not** use `PaymentSource.Token` / `Token` — `TokenType` has only `BillingAgreement (BILLING_AGREEMENT)`. (`records-2-Pa-Ve.md`, `enums.md`, `Models/Customer.cs`, `Api/Vault.cs`)

---

### Card field construction (direct card, sandbox)

Sandbox test card: Visa `4111111111111111`, any future expiry, any CVC, any name and billing address. No browser step.

**Pay (raw card)** — `PayPalServerSdk.Models.CardRequest` (`records-1-Ac-Pa.md`, `Models/CardRequest.cs`):

| Member | Wire | Type | Required for raw card? |
|---|---|---|---|
| `Name` | `name` | `string?` | optional (1–300) |
| `Number` | `number` | `string?` | **yes** — digits 13–19; test `4111111111111111` |
| `Expiry` | `expiry` | `string?` | **yes** — ISO `YYYY-MM` (exactly 7 chars) |
| `SecurityCode` | `security_code` | `string?` | **yes** for first customer-present card; 3–4 digits. **Must not** be sent when `payment_initiator=MERCHANT`. |
| `BillingAddress` | `billing_address` | `Address?` | send for the test (any valid address) |
| `VaultId` | `vault_id` | `string?` | **omit** for raw card; **set** (only this + stored credential) for saved-card pay |
| `StoredCredential` | `stored_credential` | `CardStoredCredential?` | see saved-card pay |
| `ExperienceContext` | `experience_context` | `CardExperienceContext?` | `ReturnUrl`/`CancelUrl` exist for 3DS — **do not** implement a browser round-trip |
| `Attributes.Verification.Method` | | `OrdersCardVerificationMethod?` default `ScaWhenRequired` | leave default |

**Billing address** — `PayPalServerSdk.Models.Address`: `AddressLine1 (address_line_1): string?`, `AddressLine2 (address_line_2): string?`, `AdminArea2 (admin_area_2): string?` (city), `AdminArea1 (admin_area_1): string?` (state), `PostalCode (postal_code): string?`, `CountryCode (country_code): string !req` (ISO 3166-1 alpha-2). (`records-1-Ac-Pa.md`)

**Vault (save card)** — `PayPalServerSdk.Models.PaymentTokenRequestCard`: `Name`, `Number`, `Expiry` (`YYYY-MM`), `SecurityCode`, `Brand` (`CardBrand?`), `BillingAddress` (`Address?`). Same number/expiry/CVC rules. (`records-2-Pa-Ve.md`)

**Pay with saved token** — `PaymentSource.Card = new CardRequest { VaultId = <PaymentTokenResponse.Id>, StoredCredential = … }`. Do not send `Number`/`SecurityCode`.

`CardStoredCredential` (`records-1-Ac-Pa.md`): `PaymentInitiator (payment_initiator): PaymentInitiator !req`, `PaymentType (payment_type): StoredPaymentSourcePaymentType !req`, `Usage (usage): StoredPaymentSourceUsageType?` default `Derived`. Compatibility from the model summary: `ONE_TIME` only with initiator `CUSTOMER`; `usage=FIRST` only with `CUSTOMER`. Shopper-present pay with a previously vaulted card: `PaymentInitiator.Customer` + `StoredPaymentSourcePaymentType.Unscheduled` (or `OneTime`) + `StoredPaymentSourceUsageType.Subsequent`.

**Payment source wrappers**

- Create order: `OrderRequest.PaymentSource` → `PaymentSource.Card`
- Authorize: `OrderAuthorizeRequest.PaymentSource` → `OrderAuthorizeRequestPaymentSource.Card` (same `CardRequest`)
- If card/vault_id was already sent on CreateOrder, AuthorizeOrder `body` may be `null` (must still **pass `null` explicitly** — no C# default)

---

### 3DS / payer-action (GAP if it occurs)

The SDK **models** a shopper-browser challenge. This integration **must not** design an approval round-trip. If PayPal returns a challenge, treat it as a **runtime GAP** and fail with an operator/shopper-readable message.

Detect on authorize (and vault) responses:

| Signal | Where | Values |
|---|---|---|
| Order status | `Order.Status` / `OrderAuthorizeResponse.Status` | `OrderStatus.PayerActionRequired` (wire `PAYER_ACTION_REQUIRED`) |
| HATEOAS | `Links[].Rel` / `Href` | payer-action / approve style rels (`LinkDescription.Rel: string !req`) |
| 3DS result | `PaymentSource.Card.AuthenticationResult` → `AuthenticationResponse` | `LiabilityShift (liability_shift): LiabilityShiftIndicator?` (`No`/`Possible`/`Unknown`); `ThreeDSecure.AuthenticationStatus: ParesStatus?` (`Y`,`N`,`U`,`A`,`C`,`R`,`D`,`I`); `EnrollmentStatus: EnrollmentStatus?` (`Y`,`N`,`U`,`B`) |
| Vault card | `CardPaymentTokenEntity.VerificationStatus` | `Verified` / `Failed`; `AuthenticationResult` on the token entity |

**UNVERIFIED** whether sandbox Visa `4111111111111111` actually returns a challenge. Always inspect `Status` after authorize/vault.

Do **not** set `CardExperienceContext.ReturnUrl`/`CancelUrl` to start a 3DS redirect. (`records-1-Ac-Pa.md`, `enums.md`)

---

### Persist PayPal-owned state (local payment record)

After each call, store enough to act later. Never log or store PAN/CVV/`SecurityCode`.

| Field | Source |
|---|---|
| PayPal order id | `Order.Id` / `OrderAuthorizeResponse.Id` |
| Order status | `Order.Status` |
| Authorization id | `PurchaseUnits[0].Payments.Authorizations[0].Id` (`AuthorizationWithAdditionalData`) or `PaymentAuthorization.Id` |
| Authorization status | `AuthorizationStatus` on that object |
| Authorization expiration | `ExpirationTime (expiration_time)` |
| Capture id | `CapturedPayment.Id` / `OrdersCapture.Id` |
| Capture status | `CaptureStatus` |
| Captured amount | `CapturedPayment.Amount` |
| PayPal fee | `SellerReceivableBreakdown.PaypalFee` |
| Net proceeds | `SellerReceivableBreakdown.NetAmount` |
| Gross | `SellerReceivableBreakdown.GrossAmount` (!req on that record) |
| Refund ids / statuses / amounts | `Refund.Id`, `Refund.Status`, `Refund.Amount`; running total from `SellerPayableBreakdown.TotalRefundedAmount` |
| Vault token id | `PaymentTokenResponse.Id` |
| PayPal customer id / merchant customer id | `PaymentTokenResponse.Customer` |
| Safe card descriptor | `CardPaymentTokenEntity.LastDigits`, `Brand`, `Expiry`, `Name` (never `Number`) |

If `prefer=return=representation` still omits nested payments or fee/net, GET: `GetOrder` / `GetAuthorizedPayment` / `GetCapturedPayment` / `GetRefund`. **UNVERIFIED** whether fee/net are populated on a pending capture — map says `SellerReceivableBreakdown` “is not available for transactions that are in pending state.”

---

### Operations

#### 1–2. Direct-card / vaulted-token AUTHORIZE (hold)

**`client.Orders.CreateOrder`** — `POST /v2/checkout/orders` · `map/operations/Orders.md`

```
Task<Order> CreateOrder(
    string? payPalMockResponse,          // must pass explicitly (null to skip)
    string? payPalRequestId,             // must pass explicitly — REQUIRED when body has payment_source
    string? payPalPartnerAttributionId,  // must pass explicitly
    string? payPalClientMetadataId,      // must pass explicitly
    string? payPalAuthAssertion,         // must pass explicitly
    OrderRequest body,                   // required
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns: `PayPalServerSdk.Models.Order` (not an envelope wrapper).

Request `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req`, `Payer (payer): Payer?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `ApplicationContext (application_context): OrderApplicationContext?`.

`PurchaseUnitRequest` (fields used): `Amount (amount): AmountWithBreakdown !req`, `InvoiceId (invoice_id): string?`, `CustomId (custom_id): string?`, `Description (description): string?`, `ReferenceId (reference_id): string?`.

`PaymentSource`: `Card (card): CardRequest?` (plus unrelated wallets — leave unset).

Response `Order` (fields used): `Id (id): string?`, `Status (status): OrderStatus?`, `Intent (intent): CheckoutPaymentIntent?`, `PaymentSource (payment_source): PaymentSourceResponse?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `Links (links): IReadOnlyList<LinkDescription>?`, `CreateTime`/`UpdateTime`.

Error: **Case A** `SdkException<CreateOrderError>` — `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback.

**`client.Orders.AuthorizeOrder`** — `POST /v2/checkout/orders/{id}/authorize` · `Orders.md`

```
Task<OrderAuthorizeResponse> AuthorizeOrder(
    string id,
    string? payPalMockResponse,          // must pass explicitly
    string? payPalRequestId,             // must pass explicitly
    string? payPalClientMetadataId,      // must pass explicitly
    string? payPalAuthAssertion,         // must pass explicitly
    OrderAuthorizeRequest? body,         // must pass explicitly (null if payment_source already on the order)
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Request `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` → `Card (card): CardRequest?`.

Response `OrderAuthorizeResponse`: same shape as `Order` (id, status, payment_source, purchase_units, links). Authorization lives at `PurchaseUnits[].Payments.Authorizations[]` (`AuthorizationWithAdditionalData`: `Id`, `Status`, `Amount`, `ExpirationTime`, `CreateTime`).

Error: **Case A** `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError`.

Pass `prefer: "return=representation"`. If `Payments.Authorizations` is empty, `GetOrder`. Hold amount **must** equal the order total (`Authorization.Amount.Value`).

---

#### 3. CAPTURE an authorization (fulfil)

**`client.Payments.CaptureAuthorizedPayment`** — `POST /v2/payments/authorizations/{authorization_id}/capture` · `map/operations/Payments.md`

```
Task<CapturedPayment> CaptureAuthorizedPayment(
    string authorizationId,
    string? payPalMockResponse,     // must pass explicitly
    string? payPalRequestId,        // must pass explicitly — stable key (double-click)
    string? payPalAuthAssertion,    // must pass explicitly
    CaptureRequest? body,           // must pass explicitly; send FinalCapture=true + Amount=order total
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Request `CaptureRequest`: `Amount (amount): Money?`, `InvoiceId (invoice_id): string?`, `FinalCapture (final_capture): bool? = false`, `PaymentInstruction (payment_instruction): CapturePaymentInstruction?`, `NoteToPayer (note_to_payer): string?`, `SoftDescriptor (soft_descriptor): string?`.

Response `CapturedPayment` (not wrapped): `Id`, `Status (CaptureStatus)`, `Amount`, `SellerReceivableBreakdown`, `FinalCapture`, `CreateTime`, `UpdateTime`, `InvoiceId`, `CustomId`, `Links`, `ProcessorResponse`.

**Fee / net (fulfilment display):** `SellerReceivableBreakdown` (`records-2-Pa-Ve.md`):

| Member | Wire | Type |
|---|---|---|
| `GrossAmount` | `gross_amount` | `Money !req` |
| `PaypalFee` | `paypal_fee` | `Money?` |
| `PaypalFeeInReceivableCurrency` | `paypal_fee_in_receivable_currency` | `Money?` |
| `NetAmount` | `net_amount` | `Money?` |
| `ReceivableAmount` | `receivable_amount` | `Money?` |
| `ExchangeRate` | `exchange_rate` | `ExchangeRate?` |
| `PlatformFees` | `platform_fees` | `IReadOnlyList<PlatformFee>?` |

Pass `prefer: "return=representation"`. If `PaypalFee`/`NetAmount` are null and status is not pending, `GetCapturedPayment`.

Error: **Case A** `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. 409 = conflict (already captured) — treat as idempotent success after GET.

---

#### 4. REAUTHORIZE / RENEW a stale authorization

**`client.Payments.ReauthorizePayment`** — `POST /v2/payments/authorizations/{authorization_id}/reauthorize` · `Payments.md`

```
Task<PaymentAuthorization> ReauthorizePayment(
    string authorizationId,
    string? payPalRequestId,        // must pass explicitly
    string? payPalAuthAssertion,    // must pass explicitly
    ReauthorizeRequest? body,       // must pass explicitly; Amount only
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Request `ReauthorizeRequest`: `Amount (amount): Money?` (map: supports only `amount`). Response: `PaymentAuthorization` — **new `Id`**, `Status`, `Amount`, `ExpirationTime`. **Replace** the stored authorization id with this new id before capture.

Map notes: honor period ~3 days; reauthorize from day 4–29 of the original 29-day window; after ~30 days you cannot reauthorize — you must create a **new** authorized payment (shopper must pay again).

**Operator-actionable failure:** on `TryGetError` (400/401/403/404/422) or `TryGetNoContent` (500), surface `Error.Name`, `Error.Message`, `Error.Details[].Issue`/`Description` (and HTTP-ish meaning of 422 = cannot renew). Do **not** capture. Tell the operator: the hold cannot be renewed; the shopper must authorize a new payment.

Error: **Case A** `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.

---

#### 5. VOID / release (cancel before capture)

**`client.Payments.VoidPayment`** — `POST /v2/payments/authorizations/{authorization_id}/void` · `Payments.md`

```
Task<PaymentAuthorization> VoidPayment(
    string authorizationId,
    string? payPalMockResponse,     // must pass explicitly
    string? payPalAuthAssertion,    // must pass explicitly
    string? payPalRequestId,        // must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

No body. Returns `PaymentAuthorization` (`Status` expected `Voided`). Cannot void a fully captured authorization (409/422).

Error: **Case A** `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.

---

#### 6. REFUND a capture (full and partial)

**`client.Payments.RefundCapturedPayment`** — `POST /v2/payments/captures/{capture_id}/refund` · `Payments.md`

```
Task<Refund> RefundCapturedPayment(
    string captureId,
    string? payPalMockResponse,     // must pass explicitly
    string? payPalRequestId,        // must pass explicitly — **caller-supplied idempotency key**
    string? payPalAuthAssertion,    // must pass explicitly
    RefundRequest? body,            // must pass explicitly; null/empty = full; Amount = partial
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Request `RefundRequest`: `Amount (amount): Money?`, `CustomId (custom_id): string?`, `InvoiceId (invoice_id): string?`, `NoteToPayer (note_to_payer): string?`, `PaymentInstruction (payment_instruction): RefundPaymentInstruction?`.

App rule **before** calling: remaining = captured `Amount.Value` − sum of completed refunds (or `SellerPayableBreakdown.TotalRefundedAmount`). Reject if requested refund > remaining. Never let a partly-refunded order refund beyond captured.

Response `Refund`: `Id`, `Status (RefundStatus)`, `Amount`, `SellerPayableBreakdown` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount`), `Links`.

Error: **Case A** `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.

---

#### 7. VAULT / save a card

**`client.Vault.CreatePaymentToken`** — `POST /v3/vault/payment-tokens` · `map/operations/Vault.md`

```
Task<PaymentTokenResponse> CreatePaymentToken(
    string? payPalRequestId,        // must pass explicitly — idempotency (stored 3 hours)
    PaymentTokenRequest body,       // required
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Request `PaymentTokenRequest`: `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.

`PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?` (set this; ignore `Token`).

Response `PaymentTokenResponse`: `Id (id): string?` (saved-card handle), `Customer (customer): CustomerResponse?` (`Id`, `MerchantCustomerId`), `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `Card (card): CardPaymentTokenEntity?` (`Name`, `LastDigits`, `Brand`, `Expiry`, `BillingAddress`, `Type`, `VerificationStatus` — **no PAN**), `Links`.

Return to the API caller: token `Id` + safe descriptor (`LastDigits`, `Brand`, `Expiry`). Persist `Customer.Id` + `MerchantCustomerId` + token `Id`. **Never** write `Number`/`SecurityCode` to DB or logs.

Error: **Case A** `SdkException<CreatePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError`. **Note the accessor name `TryGetError1`**, not `TryGetError`. Payload type `Error1` (not `Error`).

---

#### 8. LIST saved payment tokens

**`client.Vault.ListCustomerPaymentTokens`** — `GET /v3/vault/payment-tokens` · `Vault.md`

```
Task<CustomerVaultPaymentTokensResponse> ListCustomerPaymentTokens(
    string customerId,                 // required — query customer_id
    int? pageSize = 5,
    int? page = 1,
    bool? totalRequired = false,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Query wire ← C#: `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired`.

Response `CustomerVaultPaymentTokensResponse`: `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Customer (customer): VaultResponseCustomer?`, `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `Links`.

SDK pagination field: none (`page` only, no `perPage`). To list **all** cards: `totalRequired: true`, then loop `page = 1 .. TotalPages` (page is 1-based per defaults). Map: “pagination: none (only `page`, no `perPage`)”.

Error: **Case A** `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`.

---

#### 9. DELETE a saved payment token

**`client.Vault.DeletePaymentToken`** — `DELETE /v3/vault/payment-tokens/{id}` · `Vault.md`

```
Task DeletePaymentToken(
    string id,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns `void`. Error: **Case A** `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`. After success, list must omit it; `CardRequest.VaultId` pay must fail. No `payPalRequestId` on this operation.

Optional verify: `GetPaymentToken(string id)` → `PaymentTokenResponse`; error `TryGetError1` [403, 404, 422, 500].

---

#### 10. Transaction search (reconciliation, whole range)

**`client.TransactionSearch.SearchTransactions`** — `GET /v1/reporting/transactions` · `map/operations/TransactionSearch.md`

```
Task<SearchResponse> SearchTransactions(
    string startDate,                   // required — RFC3339; **seconds required**
    string endDate,                     // required — RFC3339; **seconds required**; **max span 31 days**
    string? transactionId,              // must pass explicitly
    string? transactionType,            // must pass explicitly
    string? transactionStatus,          // must pass explicitly
    string? transactionAmount,          // must pass explicitly
    string? transactionCurrency,        // must pass explicitly
    string? paymentInstrumentType,      // must pass explicitly
    string? storeId,                    // must pass explicitly
    string? terminalId,                 // must pass explicitly
    string? fields = "transaction_info",
    string? balanceAffectingRecordsOnly = "Y",
    int? pageSize = 100,
    int? page = 1,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

**Call with named arguments** — eight consecutive nullable params have no C# default.

Query wire ← C#: `start_date`, `end_date`, `transaction_id`, `transaction_type`, `transaction_status`, `transaction_amount`, `transaction_currency`, `payment_instrument_type`, `store_id`, `terminal_id`, `fields`, `balance_affecting_records_only`, `page_size`, `page`.

from/to in the eShop API are ISO-8601 date-times → pass as `startDate`/`endDate` (seconds required, fractional optional). **Maximum supported range is 31 days** (`Api/TransactionSearch.cs`). For a longer `[from, to]`, split into adjacent windows ≤31 days, then paginate each window.

**Cover the whole range:** SDK has no auto-paginator (`page` only). Loop `page = 1, 2, …` while `page <= SearchResponse.TotalPages` (or until a page returns no `TransactionDetails`). `pageSize` default 100.

Response `SearchResponse` (`records-2-Pa-Ve.md`):

| Member | Wire | Type |
|---|---|---|
| `TransactionDetails` | `transaction_details` | `IReadOnlyList<TransactionDetails>?` |
| `AccountNumber` | `account_number` | `string?` |
| `StartDate` / `EndDate` | `start_date` / `end_date` | `string?` |
| `LastRefreshedDatetime` | `last_refreshed_datetime` | `string?` |
| `Page` | `page` | `int?` |
| `TotalItems` | `total_items` | `int?` |
| `TotalPages` | `total_pages` | `int?` |
| `Links` | `links` | `IReadOnlyList<LinkDescription>?` |

`TransactionDetails.TransactionInfo` (`TransactionInformation`) used for lining up: `TransactionId (transaction_id)`, `PaypalReferenceId (paypal_reference_id)`, `TransactionInitiationDate`, `TransactionAmount (transaction_amount): Money?`, `FeeAmount (fee_amount): Money?`, `TransactionStatus (transaction_status): string?`, `InvoiceId (invoice_id)`, `CustomField (custom_field)`, `PaymentTrackingId`.

`fields`: default `transaction_info`. For payer/cart too, pass `"all"` or a comma list (`transaction_info,payer_info,cart_info,…`).

Notes from operation: executed transactions can take up to **three hours** to appear; history up to three years.

Error: **Case B** `SdkException<RawError>` — **the only Case B operation in this SDK**. Read `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`. Optional typed body `SearchError` (`Name`, `Message`, `DebugId`, `Details`, `TotalItems`, `MaximumItems`) — use only after `ReadAsJson<SearchError>()`. Do **not** catch `SdkException<SearchTransactionsError>` (that type is not what this operation throws).

---

#### 11. GET order / authorization / capture / refund

**`client.Orders.GetOrder`** — `GET /v2/checkout/orders/{id}` · `Orders.md`

```
Task<Order> GetOrder(
    string id,
    string? fields,                 // must pass explicitly
    string? payPalMockResponse,     // must pass explicitly
    string? payPalAuthAssertion,    // must pass explicitly
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Error: **Case A** `SdkException<GetOrderError>` — `TryGetError(out Error)` [401, 404] · `TryGetRawError`.

**`client.Payments.GetAuthorizedPayment`** — `GET /v2/payments/authorizations/{authorization_id}` · `Payments.md`

```
Task<PaymentAuthorization> GetAuthorizedPayment(
    string authorizationId,
    string? payPalMockResponse,     // must pass explicitly
    string? payPalAuthAssertion,    // must pass explicitly
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns `PaymentAuthorization`. Error: **Case A** `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.

**`client.Payments.GetCapturedPayment`** — `GET /v2/payments/captures/{capture_id}` · `Payments.md`

```
Task<CapturedPayment> GetCapturedPayment(
    string captureId,
    string? payPalMockResponse,     // must pass explicitly
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns `CapturedPayment` (fee/net on `SellerReceivableBreakdown`). Error: **Case A** `SdkException<GetCapturedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.

**`client.Payments.GetRefund`** — `GET /v2/payments/refunds/{refund_id}` · `Payments.md`

```
Task<Refund> GetRefund(
    string refundId,
    string? payPalMockResponse,     // must pass explicitly
    string? payPalAuthAssertion,    // must pass explicitly
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Error: **Case A** `SdkException<GetRefundError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.

---

### Error payloads (how to read status and body)

`SdkException<TError>` has **only** `Error` (`required TError`). **No** `StatusCode` on the exception. (`Core/Exceptions/SdkException.cs`)

**Case A (all in-scope ops except SearchTransactions):**

```
catch (SdkException<{Op}Error> ex)
{
    if (ex.Error.TryGetError(out Error typed)) { /* Orders/Payments */ }
    else if (ex.Error.TryGetError1(out Error1 typed1)) { /* Vault */ }
    else if (ex.Error.TryGetNoContent(out RawError nc)) { /* Payments 500 */ }
    else if (ex.Error.TryGetRawError(out RawError raw)) { /* other HTTP */ }
}
```

Typed `Error` / `Error1` **do not include HTTP status**. Status is only the accessor’s mapped set (e.g. CreateOrder `TryGetError` ⇒ 400/401/422). `RawError.StatusCode` is available on the fallback / NoContent / Case B paths.

`Error` (`records-1-Ac-Pa.md`): `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`, `Links (links): IReadOnlyList<LinkDescription>?`.

`ErrorDetails`: `Field (field): string?`, `Value (value): string?`, `Location (location): string? = "body"`, `Issue (issue): string !req`, `Description (description): string?`, `Links`.

`Error1` (Vault): same idea with `ErrorDetails1` and `ErrorLinkDescription` (`Rel` is **optional** on vault error links).

Operator-facing text: `Name` + `Message` + each `Details[].Issue`/`Description`. Do not parse `Exception.ToString()` when an accessor exists.

**Case B (`SearchTransactions`):** `catch (SdkException<RawError> ex)` → `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`.

---

### Enums actually used (`PayPalServerSdk.Models.Enums` — `StringEnum<T>`)

Construct with static members (`CheckoutPaymentIntent.Authorize`) or `Type.FromValue("AUTHORIZE")`. Never C# enum casts. (`map/models/enums.md`)

| Enum | Members (C# (wire)) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` — **use Authorize** |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, … `Unknown (UNKNOWN)` — read from vault/pay response |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` |
| `CardVerificationStatus` | `Verified (VERIFIED)`, `Failed (FAILED)` |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `ParesStatus` | `Y (Y)`, `N (N)`, `U (U)`, `A (A)`, `C (C)`, `R (R)`, `D (D)`, `I (I)` |
| `EnrollmentStatus` | `Y (Y)`, `N (N)`, `U (U)`, `B (B)` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` — on **setup** tokens, not `PaymentTokenResponse` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` only — **do not use for vault pay** |
| `LinkHttpMethod` | `Get`, `Post`, `Put`, `Delete`, … |

Unions: **none** (`map/models/unions.md`).

---

### Related nested records (read path)

`PurchaseUnit.Payments` → `PaymentCollection`: `Authorizations: IReadOnlyList<AuthorizationWithAdditionalData>?`, `Captures: IReadOnlyList<OrdersCapture>?`, `Refunds: IReadOnlyList<Refund>?`. (`records-2-Pa-Ve.md`)

`PaymentSourceResponse.Card` → `CardResponse`: `LastDigits`, `Brand`, `AuthenticationResult`, `Attributes` (no PAN). (`records-1-Ac-Pa.md`)

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` lifetime and DI vs `new PayPalServerSdkClient` are not obvious from the ctor. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 1 (auth) — credentials belong on `Oauth2` (`OAuth2ClientCredentials.ClientId`/`ClientSecret`) from `PayPal:ClientId`/`PayPal:ClientSecret` (env `PAYPAL_CLIENT_ID`/`PAYPAL_CLIENT_SECRET`), not hardcoded. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Step 1 (BaseUrl / retries / timeouts / logging) — `Retry`/`Timeout` on `PayPalServerSdkClientOptions` are not the timeout of the `HttpClient` you register; a failed **write** may still be sent more than once; request-body logging can persist PAN/CVV (`Number`, `SecurityCode` are not in the SDK’s default redacted-key list). **MUST load `dotnet-configuration-resilience`** before setting `Retry`, `Logging`, or `Server.Default.Sandbox.BaseUrl`.

⚠ Steps 3–10 (calls) — many parameters are nullable **with no C# default** and **must be passed explicitly** (`null` to skip). `SearchTransactions` especially mis-binds if called positionally. Named arguments; cancellation token is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.{Api}.{Op}(...)`.

⚠ Steps 3–8 (models) — records are `init`-only with `required` members; enums are `StringEnum<T>` (`CheckoutPaymentIntent.Authorize`, not a C# enum); unmodeled JSON is dropped on deserialize. **MUST load `dotnet-models`** before constructing `OrderRequest` / `CardRequest` / `PaymentTokenRequest` / `Money`.

⚠ Steps 3–10 (errors) — Case A vs Case B differ by operation (Vault accessors are `TryGetError1`; Payments writes also have `TryGetNoContent` for 500; SearchTransactions is Case B `RawError` only). `SdkException<T>` has no `StatusCode`. **MUST load `dotnet-error-handling`** before any try/catch.

⚠ Step 3–10 (errors) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 3–10 (errors) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the `HttpClient` constructor argument is the test seam; do not fake unsealed SDK controllers. **MUST load `dotnet-testing`** before stubbing PayPal.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing `PayPalServerSdkClient` / `AddPayPalServerSdkClient`, `HttpClient` ownership |
| `dotnet-authentication` | Step 1 — `Oauth2` client-credentials wiring from config/env |
| `dotnet-configuration-resilience` | Step 1 — retries, timeouts, `PayPal:BaseUrl`, pagination loops, logging (PAN) |
| `dotnet-calling-endpoints` | Steps 3–10 — every operation call, named args, `ct:`, must-pass-null params |
| `dotnet-models` | Steps 3–8 — `OrderRequest`, `CardRequest`, `Money`, `StringEnum<T>`, vault models |
| `dotnet-error-handling` | Steps 3–10 — Case A/B, `TryGet*`, `JsonException` 2xx vs non-2xx (both rows above) |
| `dotnet-testing` | Tests for the integration layer |

---

## Assumptions & Blockers

**Assumptions**

- Intent is always `AUTHORIZE` then later `CaptureAuthorizedPayment`. `Orders.CaptureOrder` is unused.
- Direct vault is `CreatePaymentToken` from raw card (not setup-token + browser).
- eShop signed-in user id (stringified) is sent as `Customer.MerchantCustomerId` and as `ListCustomerPaymentTokens.customerId`.
- `PayPal:Currency` is a 3-letter ISO code used on every amount.
- Sandbox-only: `ServerEnvironment.Sandbox` is the only environment member in this SDK.
- Application-level idempotency (skip PayPal if local state already has the authorization/capture/refund id) is required because the SDK always sends a fresh `Idempotency-Key: Guid.NewGuid()`.

**Blockers / GAPs (do not work around)**

- **3DS / payer-action challenge:** if authorize or vault returns `OrderStatus.PayerActionRequired` (or equivalent vault verification/challenge links), that is a **GAP** — report it; do not implement a browser approval round-trip. **UNVERIFIED** whether sandbox `4111111111111111` triggers this.
- **No Live environment** in `ServerEnvironment`. Production cannot be selected via this SDK map. Out of current sandbox scope; do not invent a live base URL.
- **List `customer_id` identity:** SDK XML on `ListCustomerPaymentTokens.customerId` says merchant/partner system id; `Customer.Id` is PayPal-generated. **UNVERIFIED** which value the live list filter expects — persist both; if merchant id lists empty/400, retry PayPal `Customer.Id`.
- **Fee/net on pending captures:** map states `SellerReceivableBreakdown` is not available while pending. If capture stays `Pending`, do not invent fees.

**Not GAPs** (present in the map): authorize with raw card; authorize with `vault_id`; capture; reauthorize; void; refund; create/list/delete payment tokens; transaction search with `page`/`pageSize`/`total_pages`; GET order/authorization/capture/refund; `payPalRequestId` on authorize/capture/refund/vault create.
