# PayPal .NET SDK plan — eShopOnWeb payments + saved cards

NuGet: `AsadAli.Checkout.Sdk` (version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`. Map stamp: tag `v1.0.1` / commit `9653d18`.

---

## Scope & sequence

| Step | What | Operations |
|---|---|---|
| 1 | Register SDK client (Sandbox, OAuth2 ClientId/ClientSecret, optional verbatim `BaseUrl` that also covers the token request) | construction / `AddPayPalServerSdkClient` |
| 2 | Authorize (hold) at checkout — one-off card **or** vaulted card; amount = order total to the cent; idempotent | `Orders.CreateOrder` (`intent=AUTHORIZE` + `payment_source`); optional `Orders.GetOrder` if the create response is minimal |
| 3 | Persist PayPal-owned ids/status (order, authorization, later capture + refunds). Never persist PAN/CVC | — |
| 4 | Capture at fulfilment; if the hold is stale, reauthorize then capture; surface gross / PayPal fee / net | `Payments.GetAuthorizedPayment` → `Payments.ReauthorizePayment` (if stale) → `Payments.CaptureAuthorizedPayment`; `Payments.GetCapturedPayment` if fee breakdown is missing |
| 5 | Void on cancel-before-fulfilment | `Payments.VoidPayment` |
| 6 | Refund captured payment (full or partial); never refund more than captured; caller-supplied idempotency key | `Payments.RefundCapturedPayment`; `Payments.GetCapturedPayment` / `Payments.GetRefund` to enforce remaining |
| 7 | Save a card for a signed-in shopper (PayPal vault only) | `Vault.CreatePaymentToken` |
| 8 | List shopper saved cards (safe descriptors only) | `Vault.ListCustomerPaymentTokens` (page the whole list); `Vault.GetPaymentToken` if a single token must be refreshed |
| 9 | Delete a saved card | `Vault.DeletePaymentToken` |
| 10 | Reconcile PayPal transactions vs eShop orders over a full ISO-8601 range | `TransactionSearch.SearchTransactions` (every page; split >31-day ranges) |

Out of scope for this integration (SDK has them; do **not** call): `Orders.CaptureOrder` (immediate-capture intent), `Orders.ConfirmOrder`, `Vault.CreateSetupToken` / `GetSetupToken` (setup-token / return-URL / 3DS-challenge path), `Orders.PatchOrder`, tracking, subscriptions.

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

### Client construction, auth, BaseUrl (token + every API call)

| Fact | Value | Source |
|---|---|---|
| Package | `AsadAli.Checkout.Sdk` | `sdk-map.md` |
| Client | `PayPalServerSdk.PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` | `sdk-map.md`, `PayPalServerSdkClient.cs` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(this IServiceCollection, Action<PayPalServerSdkClientOptions>? configure = null)` — singleton client, unnamed `IHttpClientFactory` client | `ServiceCollectionExtensions.cs` |
| Options members | `Environment: PayPalServerSdk.Servers.ServerEnvironment`; `Retry: PayPalServerSdk.Core.Configuration.RetryOptions`; `Logging: LoggingOptions`; `Server: PayPalServerSdk.ServerOptions`; `Oauth2: PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?`; `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `sdk-map.md`, `PayPalServerSdkClientOptions.cs` |
| Environment members | **Only** `PayPalServerSdk.Servers.ServerEnvironment.Sandbox` (wire `"Sandbox"`). `Default()` → Sandbox. **No Production member.** | `sdk-map.md` *Servers & auth*, `Servers/ServerEnvironment.cs` |
| Credentials | `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = …, Scope = optional }` — all `init`; `ClientId`/`ClientSecret` are `required string` | `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs` |
| Server tree | `PayPalServerSdk.ServerOptions.Default` → `PayPalServerSdk.Servers.DefaultOptions.Sandbox` → `BaseUrl` (default `"https://api-m.sandbox.paypal.com"`) | `ServerOptions.cs`, `Servers/DefaultOptions.cs` |
| Custom `PayPal:BaseUrl` | When set, assign **verbatim** to `options.Server.Default.Sandbox.BaseUrl` **before** constructing the client. There is one server (`Default`). `Server.Default(path)` builds every URL from that BaseUrl, including the token URL. | `Server.cs`, `Servers/DefaultOptions.cs` |
| Token request | OAuth strategy is `OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), rawClient)` — POST `{BaseUrl}/v1/oauth2/token`, `grant_type=client_credentials`, HTTP Basic `clientId:clientSecret`. Same BaseUrl as every other call. | `AuthSchemes.cs` |
| Config keys (do not invent) | `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, `PayPal:BaseUrl` (optional) | brief |

### Idempotency headers

| Header | How the SDK exposes it | Notes | Source |
|---|---|---|---|
| `PayPal-Request-Id` | Method param `payPalRequestId: string?` (nullable, **no default → pass explicitly**, including `null`) | **This is the caller-controlled idempotency key.** CreateOrder XML: mandatory for single-step create with card / vault_id; stored 6 hours (up to 72h via account manager). Capture/refund/reauth/void/vault-create: stored 45 days (vault create: 3 hours). | `operations/Orders.md`, `operations/Payments.md`, `operations/Vault.md`, `Api/Orders.cs`, `Api/Payments.cs` |
| `Prefer` | Method param `prefer: string? = "return=minimal"` | Pass `"return=representation"` whenever nested payments / `seller_receivable_breakdown` must be in the response. Minimal = `id` + `status` + HATEOAS links only. | `Api/Orders.cs`, `Api/Payments.cs` |
| `Idempotency-Key` | **Not a method parameter.** Generated inside each mutating call as `new HeaderParam("Idempotency-Key", Guid.NewGuid())`. | A new GUID **every method invocation**. Cannot be used for double-click protection. Use `payPalRequestId`. | `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs` |
| `RequestOptions` | `PayPalServerSdk.Core.Request.RequestOptions? requestOptions = null` | Per-call options (e.g. log level). Pass `null`. Not the idempotency seam. | operation signatures |

Double-click rules for this app:

- Authorize: stable `payPalRequestId` per eShop order (e.g. `eshop-auth-{orderId}`).
- Capture: stable `payPalRequestId` per eShop order (e.g. `eshop-capture-{orderId}`).
- Refund: **caller-supplied** key → `payPalRequestId`.
- Vault create: stable key per shopper+card fingerprint (never the PAN).

On `409` from capture/refund, GET the existing resource and treat as the prior success if ids match persisted state (`operations/Payments.md` error accessors include 409).

---

### Operations

#### `client.Orders.CreateOrder` — hold at checkout (primary authorize path)

- **HTTP**: `POST /v2/checkout/orders` · `operations/Orders.md`
- **Signature**: `Task<PayPalServerSdk.Models.Order> CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderRequest body, string? prefer = "return=minimal", PayPalServerSdk.Core.Request.RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: the five nullable headers (`payPalMockResponse` … `payPalAuthAssertion`) — pass `null` to skip. `body` is required (no `?`).
- **Call with named args.** For this app: `payPalMockResponse: null, payPalRequestId: <stable>, payPalPartnerAttributionId: null, payPalClientMetadataId: null, payPalAuthAssertion: null, body: …, prefer: "return=representation", ct: ct`
- **Request `OrderRequest`** (`Models/OrderRequest.cs`, `records-1-Ac-Pa.md`):

  | Member | Wire | Type | Req |
  |---|---|---|---|
  | `Intent` | `intent` | `PayPalServerSdk.Models.Enums.CheckoutPaymentIntent` | **required** — use `CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`) |
  | `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnitRequest>` | **required** |
  | `PaymentSource` | `payment_source` | `PaymentSource?` | optional in C#; **required for no-browser card/vault pay** |
  | `Payer` | `payer` | `Payer?` | optional |
  | `ApplicationContext` | `application_context` | `OrderApplicationContext?` | omit (PayPal-redirect fields) |

- **`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`): `Amount (amount): AmountWithBreakdown !req`; set `InvoiceId (invoice_id): string?` and `CustomId (custom_id): string?` to the eShop order id (reconciliation join keys). Other fields optional.
- **`AmountWithBreakdown`** (`records-1-Ac-Pa.md`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?`. **Amount is a string**, not decimal. `Value` must equal the order total to the cent (e.g. `"123.45"`) in `PayPal:Currency`.
- **`Money`** (capture/refund/reauth amounts): same shape — `CurrencyCode (currency_code): string !req`, `Value (value): string !req` (`records-1-Ac-Pa.md`, `Models/Money.cs`).
- **One-off card — `PaymentSource.Card`** (`PaymentSource` / `CardRequest`, `records-2-Pa-Ve.md` / `records-1-Ac-Pa.md`):

  | Member | Wire | Type | Notes |
  |---|---|---|---|
  | `Name` | `name` | `string?` | cardholder name |
  | `Number` | `number` | `string?` | PAN; test `4111111111111111` |
  | `Expiry` | `expiry` | `string?` | **`YYYY-MM` only** (length 7) |
  | `SecurityCode` | `security_code` | `string?` | CVC 3–4 digits |
  | `BillingAddress` | `billing_address` | `Address?` | if set, `Address.CountryCode (country_code): string !req` (ISO-3166-1 alpha-2). Other address fields optional: `AddressLine1`, `AddressLine2`, `AdminArea2` (city), `AdminArea1` (state), `PostalCode` |
  | `Attributes` | `attributes` | `CardAttributes?` | SCA — see below |
  | `VaultId` | `vault_id` | `string?` | **omit** on one-off |
  | `ExperienceContext` | `experience_context` | `CardExperienceContext?` | **omit** (`return_url`/`cancel_url` is the browser/3DS path) |

- **Vaulted card — same `PaymentSource.Card`, do not send PAN**:

  | Member | Wire | Notes |
  |---|---|---|
  | `VaultId` | `vault_id` | PayPal payment-token id from `CreatePaymentToken` / list |
  | `StoredCredential` | `stored_credential` | `CardStoredCredential`: `PaymentInitiator (payment_initiator): PaymentInitiator !req` = `PaymentInitiator.Customer`; `PaymentType (payment_type): StoredPaymentSourcePaymentType !req` = `StoredPaymentSourcePaymentType.OneTime`; `Usage (usage)` = `StoredPaymentSourceUsageType.Subsequent` |
  | `Number` / `SecurityCode` | — | omit |

- **Do not use `PaymentSource.Token`.** `Token.Type` is `TokenType.BillingAgreement` only (`enums.md`) — not a vault payment token.
- **3DS / SCA (avoid browser if possible):** set `Card.Attributes.Verification.Method` to `OrdersCardVerificationMethod.AvsCvv` (wire `AVS_CVV`) or leave default `ScaWhenRequired` (wire `SCA_WHEN_REQUIRED`). Default on `CardVerification.Method` is `ScaWhenRequired` (`Models/CardVerification.cs`). Do **not** set `ScaAlways` or `_3DSecure`. Do **not** populate `CardExperienceContext.ReturnUrl`/`CancelUrl`.
- **Response `Order`** (`records-1-Ac-Pa.md`) — not an envelope wrapper; the order **is** the payload:

  | Member | Wire | Read |
  |---|---|---|
  | `Id` | `id` | persist — PayPal order id |
  | `Status` | `status` | `OrderStatus` |
  | `Intent` | `intent` | expect `Authorize` |
  | `PurchaseUnits` | `purchase_units` | `[0].Payments.Authorizations[0]` |
  | `PaymentSource` | `payment_source` | `PaymentSourceResponse.Card` — `LastDigits`, `Brand`, `Expiry` only |
  | `Links` | `links` | `LinkDescription` (`Href`, `Rel`, `Method`) |

- **Authorization from create:** `PurchaseUnit.Payments` is `PaymentCollection` (`records-2-Pa-Ve.md`): `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`. Read `Id`, `Status`, `Amount`, `ExpirationTime (expiration_time)`, `CreateTime`. That `Id` is `authorizationId` for capture/void/reauth.
- **Error**: Case A `SdkException<PayPalServerSdk.Errors.CreateOrderError>` — `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback. `Error`: `Name`, `Message`, `DebugId` required; `Details[].Issue` required (`records-1-Ac-Pa.md`).

#### `client.Orders.GetOrder` — reload hold if create was minimal / confirm status

- **HTTP**: `GET /v2/checkout/orders/{id}` · `operations/Orders.md`
- **Signature**: `Task<Order> GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: `fields`, `payPalMockResponse`, `payPalAuthAssertion` (nullable, no default).
- **Returns**: `Order` (same shape). **Error**: Case A `GetOrderError` — `TryGetError(out Error)` [401, 404] · `TryGetRawError`.

#### `client.Orders.AuthorizeOrder` — only if create did **not** include a payment source (not the primary path)

- **HTTP**: `POST /v2/checkout/orders/{id}/authorize` · `operations/Orders.md`
- **Signature**: `Task<OrderAuthorizeResponse> AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Body `OrderAuthorizeRequest`**: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` with `Card (card): CardRequest?` (same card / `vault_id` shape).
- **Returns `OrderAuthorizeResponse`**: same fields as `Order` (`Id`, `Status`, `PurchaseUnits`, …) — `records-1-Ac-Pa.md`.
- **Error**: Case A `AuthorizeOrderError` — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError`.

Primary path is **CreateOrder with `payment_source` already set** (single-step; `payPalRequestId` mandatory per XML). Use AuthorizeOrder only if an order was created without a source.

---

#### `client.Payments.GetAuthorizedPayment` — inspect hold before capture / renew

- **HTTP**: `GET /v2/payments/authorizations/{authorization_id}` · `operations/Payments.md`
- **Signature**: `Task<PayPalServerSdk.Models.PaymentAuthorization> GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: `payPalMockResponse`, `payPalAuthAssertion`.
- **Returns `PaymentAuthorization`** (`records-2-Pa-Ve.md`): `Id (id)`, `Status (status): AuthorizationStatus?`, `StatusDetails`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`, `CreateTime`, `UpdateTime`, `InvoiceId`, `CustomId`.
- **Error**: Case A `GetAuthorizedPaymentError` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.

**Stale hold:** there is **no** `EXPIRED` / `AUTHORIZED` member on `AuthorizationStatus` (`enums.md`). Detect staleness from `ExpirationTime` (ISO datetime string) and/or a failed capture. Status values that still allow capture: `Created`, `Pending`, `PartiallyCaptured`. Do not capture `Voided`, `Captured`, `Denied`.

#### `client.Payments.ReauthorizePayment` — renew stale hold

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/reauthorize` · `operations/Payments.md`
- **Signature**: `Task<PaymentAuthorization> ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=representation", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: `payPalRequestId`, `payPalAuthAssertion`, `body`.
- **Body `ReauthorizeRequest`**: `Amount (amount): Money?` only (`records-2-Pa-Ve.md`). Pass the original hold amount (`PayPal:Currency` + same `Value` string).
- **Returns**: `PaymentAuthorization` — **persist the new `Id`** (replaces the old authorization id for the subsequent capture).
- **Error**: Case A `ReauthorizePaymentError` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.
- XML (`Api/Payments.cs`): honor period ~3 days; reauth from day 4–29; after 30 days you must create a **new** authorized payment (not reauth). If reauth cannot proceed (422 / denied / past window), return an **operator-actionable** error — do not invent a new card charge.

#### `client.Payments.CaptureAuthorizedPayment` — capture at fulfilment

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/capture` · `operations/Payments.md`
- **Signature**: `Task<PayPalServerSdk.Models.CapturedPayment> CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body`.
- **Always pass `prefer: "return=representation"`** so `seller_receivable_breakdown` is present.
- **Body `CaptureRequest`** (`records-1-Ac-Pa.md`): `Amount (amount): Money?` (omit for full remaining); `FinalCapture (final_capture): bool? = false` — set `true` for eShop fulfilment (one capture); `InvoiceId`, `NoteToPayer`, `SoftDescriptor` optional.
- **Returns `CapturedPayment`** (`records-1-Ac-Pa.md`) — payload **is** the capture (no extra envelope):

  | Member | Wire | Read |
  |---|---|---|
  | `Id` | `id` | persist capture id |
  | `Status` | `status` | `CaptureStatus` — **completed** = `Completed` (`COMPLETED`); **pending** = `Pending` (`PENDING`) |
  | `Amount` | `amount` | captured `Money` |
  | `SellerReceivableBreakdown` | `seller_receivable_breakdown` | fees — **null while pending** (record summary) |
  | `CreateTime` / `UpdateTime` | `create_time` / `update_time` | |
  | `InvoiceId` / `CustomId` | `invoice_id` / `custom_id` | |

- **`SellerReceivableBreakdown`** (`records-2-Pa-Ve.md`): `GrossAmount (gross_amount): Money !req` (captured amount); `PaypalFee (paypal_fee): Money?` (PayPal's fee); `NetAmount (net_amount): Money?` (net to merchant). Also `PaypalFeeInReceivableCurrency`, `ReceivableAmount`, `ExchangeRate`, `PlatformFees` — unused unless currency conversion appears.
- **Error**: Case A `CaptureAuthorizedPaymentError` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.

If breakdown is missing after a `Completed` capture, call `GetCapturedPayment`.

#### `client.Payments.GetCapturedPayment`

- **HTTP**: `GET /v2/payments/captures/{capture_id}` · `operations/Payments.md`
- **Signature**: `Task<CapturedPayment> GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: `payPalMockResponse`.
- **Returns**: `CapturedPayment` (same). Use `Status` (`Completed` / `PartiallyRefunded` / `Refunded`) plus amounts to cap further refunds.
- **Error**: Case A `GetCapturedPaymentError` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError`.

#### `client.Payments.VoidPayment` — cancel before fulfilment

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/void` · `operations/Payments.md`
- **Signature**: `Task<PaymentAuthorization> VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId`. No body (void is header-only).
- **Returns**: `PaymentAuthorization` with `Status = Voided`. Cannot void a fully captured auth (XML).
- **Error**: Case A `VoidPaymentError` — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError`.

#### `client.Payments.RefundCapturedPayment` — full or partial refund

- **HTTP**: `POST /v2/payments/captures/{capture_id}/refund` · `operations/Payments.md`
- **Signature**: `Task<PayPalServerSdk.Models.Refund> RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: `payPalMockResponse`, `payPalRequestId` (**caller-supplied idempotency key**), `payPalAuthAssertion`, `body`.
- Pass `prefer: "return=representation"`.
- **Full refund**: `body: null` or empty `RefundRequest` (XML: empty payload).
- **Partial refund**: `body = new RefundRequest { Amount = new Money { CurrencyCode = …, Value = … } }` — `Value` ≤ remaining capturable refund.
- **`RefundRequest`** (`records-2-Pa-Ve.md`): `Amount (amount): Money?`, `CustomId`, `InvoiceId`, `NoteToPayer`, `PaymentInstruction`.
- **Returns `Refund`**: `Id`, `Status: RefundStatus?`, `Amount`, `SellerPayableBreakdown` (`GrossAmount`, `PaypalFee`, `NetAmount`, **`TotalRefundedAmount (total_refunded_amount): Money?`** — use this plus persisted capture amount so remaining never goes negative), `CreateTime`.
- **Error**: Case A `RefundCapturedPaymentError` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError`.
- App rule: refuse a refund when `CaptureStatus` is `Refunded`, or when requested `Value` > (`captured.Amount.Value` − sum of `RefundStatus.Completed` refunds). `PartiallyRefunded` is still refundable up to the remainder.

#### `client.Payments.GetRefund`

- **HTTP**: `GET /v2/payments/refunds/{refund_id}` · `operations/Payments.md`
- **Signature**: `Task<Refund> GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Error**: Case A `GetRefundError` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError`.

---

#### `client.Vault.CreatePaymentToken` — save a card (no app-side PAN storage)

- **HTTP**: `POST /v3/vault/payment-tokens` · `operations/Vault.md`
- **Signature**: `Task<PayPalServerSdk.Models.PaymentTokenResponse> CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: `payPalRequestId`.
- **Request `PaymentTokenRequest`** (`records-2-Pa-Ve.md`): `Customer (customer): Customer?`; `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.
- **`Customer`**: `Id (id)` = PayPal-generated (omit on first save); `MerchantCustomerId (merchant_customer_id)` = eShop shopper id. Persist the **returned** `Customer.Id` for later list/get.
- **`PaymentTokenRequestPaymentSource.Card`**: `PaymentTokenRequestCard` — `Name`, `Number`, `Expiry` (`YYYY-MM`), `SecurityCode`, `Brand`, `BillingAddress` (`Address.CountryCode` required if address present). No `ExperienceContext` on this type (direct vault, not setup-token).
- **Response `PaymentTokenResponse`**: `Id (id)` = payment token id (this is `CardRequest.VaultId` at pay time); `Customer`; `PaymentSource.Card` = `CardPaymentTokenEntity` — **safe descriptors only**: `LastDigits (last_digits)`, `Brand (brand): CardBrand?`, `Expiry (expiry)`, `Name`, `Type`, `VerificationStatus`. **No PAN/CVC on the response.** Persist token id + last digits + brand + expiry + PayPal customer id. Never write `Number`/`SecurityCode` to the eShop database.
- **Error**: Case A `CreatePaymentTokenError` — **`TryGetError1(out Error1)`** [400, 403, 404, 422, 500] · `TryGetRawError`. Vault errors use `Error1` (`Name`, `Message`, `DebugId`, `Details: IReadOnlyList<ErrorDetails1>` with `Issue`), not `Error`.

Do **not** call `CreateSetupToken` for this product: `SetupTokenRequestCard` carries `ExperienceContext` (`ReturnUrl`/`CancelUrl`) and is the browser/3DS path.

#### `client.Vault.ListCustomerPaymentTokens`

- **HTTP**: `GET /v3/vault/payment-tokens` · `operations/Vault.md`
- **Signature**: `Task<CustomerVaultPaymentTokensResponse> ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query wire**: `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired`.
- XML (`Api/Vault.cs`): `customerId` is “a unique identifier representing a specific customer in merchant's/partner's system or records.” Pass the identifier PayPal keyed the vault on. Persist **both** `Customer.Id` and `Customer.MerchantCustomerId` from create; list with named args. **UNVERIFIED** which of the two ids the live list filter accepts — if the first choice returns empty after a successful save, call again with the other persisted id (do not invent a third API).
- Pass `totalRequired: true`, `pageSize` ≥ 5, and loop `page = 1 .. TotalPages`.
- **Response `CustomerVaultPaymentTokensResponse`**: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `TotalItems`, `TotalPages`, `Customer`, `Links`. Map each token via `PaymentSource.Card` last digits / brand / expiry / `Id`.
- **Error**: Case A `ListCustomerPaymentTokensError` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`.
- Map pagination note: `page` only (no `perPage` alias); `pageSize` is the page-size param.

#### `client.Vault.GetPaymentToken`

- **HTTP**: `GET /v3/vault/payment-tokens/{id}` · `operations/Vault.md`
- **Signature**: `Task<PaymentTokenResponse> GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Error**: Case A `GetPaymentTokenError` — `TryGetError1(out Error1)` [403, 404, 422, 500] · `TryGetRawError`.

#### `client.Vault.DeletePaymentToken`

- **HTTP**: `DELETE /v3/vault/payment-tokens/{id}` · `operations/Vault.md`
- **Signature**: `Task DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` — returns `void`.
- After success the token must not appear in list and must not be usable as `vault_id`.
- **Error**: Case A `DeletePaymentTokenError` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`.

---

#### `client.TransactionSearch.SearchTransactions` — reconciliation (whole range)

- **HTTP**: `GET /v1/reporting/transactions` · `operations/TransactionSearch.md`
- **Signature**: `Task<PayPalServerSdk.Models.SearchResponse> SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly** (nullable, no default): `transactionId`, `transactionType`, `transactionStatus`, `transactionAmount`, `transactionCurrency`, `paymentInstrumentType`, `storeId`, `terminalId` — pass `null` to skip. **Use named arguments.**
- **Query wire**: `start_date` ← `startDate`, `end_date` ← `endDate`, plus the eight optional filters, `fields`, `balance_affecting_records_only`, `page_size`, `page`.
- **Dates**: RFC-3339 / ISO-8601 with **seconds required** (XML). Example: `2026-08-01T00:00:00Z`. **Maximum supported range per call is 31 days** (`Api/TransactionSearch.cs`). To cover a longer caller range, split into adjacent ≤31-day windows and concatenate. Transactions can take up to three hours to appear; search covers up to previous three years.
- **Paging the whole range**: `page` starts at `1`; read `SearchResponse.TotalPages` / `TotalItems`; increment `page` until `page > TotalPages`. Map says pagination is `page` only (no `perPage`); `pageSize` default 100 is the page size. Do **not** stop at page 1.
- **`fields`**: `"transaction_info"` (default) includes invoice/custom/ids/amounts/fees. Use `"all"` if payer/cart/store slices are needed to match eShop orders.
- **Response `SearchResponse`** (`records-2-Pa-Ve.md`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `StartDate`, `EndDate`, `Page`, `TotalItems`, `TotalPages`, `AccountNumber`, `LastRefreshedDatetime`, `Links`.
- **`TransactionDetails.TransactionInfo`** (`TransactionInformation`): join keys `TransactionId (transaction_id)`, `InvoiceId (invoice_id)`, `CustomField (custom_field)`, `PaypalReferenceId`; money `TransactionAmount`, `FeeAmount`; `TransactionStatus (transaction_status): string?` (not a StringEnum — raw codes `D`/`P`/`S`/`V` per XML); `TransactionInitiationDate`.
- Line up against eShop via `PurchaseUnitRequest.InvoiceId` / `CustomId` set at authorize time.
- **Error**: **Case B** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — the **only** Case B operation in this SDK. Read `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`. No `TryGetError`. (`operations/TransactionSearch.md`)

`SearchBalances` is not required for this scope.

---

### Enums actually used (`PayPalServerSdk.Models.Enums`, `StringEnum<T>` — members, not C# enums)

Construct with the static member (e.g. `CheckoutPaymentIntent.Authorize`) or `Type.FromValue("AUTHORIZE")`. Source: `map/models/enums.md`.

| Enum | Members (C# (wire)) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, **`Authorize (AUTHORIZE)`** ← hold, not capture |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)`. **No `AUTHORIZED`. No `EXPIRED`.** Expiry is `ExpirationTime`, not a status. |
| `CaptureStatus` | **`Completed (COMPLETED)`** = funds captured; `Declined (DECLINED)`; `PartiallyRefunded (PARTIALLY_REFUNDED)`; **`Pending (PENDING)`**; `Refunded (REFUNDED)`; `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, **`AvsCvv (AVS_CVV)`** |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, … `Unknown (UNKNOWN)` |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` |
| `PaymentTokenStatus` | `Created`, `PayerActionRequired`, `Approved`, `Vaulted`, `Tokenized` (setup-token path; `PaymentTokenResponse` itself has no status field) |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` only — **not** for vault cards |
| `VaultCardVerificationMethod` | `ScaWhenRequired`, `ScaAlways` (setup-token only — unused here) |

Unions: **none** in this SDK (`map/models/unions.md`).

---

### Persist (PayPal-owned state only)

| eShop order / shopper | Persist |
|---|---|
| Hold | PayPal `Order.Id`, `Order.Status`; authorization `Id`, `Status`, `ExpirationTime`, `Amount.Value` |
| Capture | capture `Id`, `Status`; `SellerReceivableBreakdown.GrossAmount` / `PaypalFee` / `NetAmount` |
| Refunds | each refund `Id`, `Status`, `Amount`; running `TotalRefundedAmount` vs captured amount |
| Shopper vault | PayPal `Customer.Id`, `MerchantCustomerId`; each `PaymentTokenResponse.Id` + `LastDigits` + `Brand` + `Expiry` |
| Never | PAN, CVC, full `CardRequest.Number` / `SecurityCode` |

Idempotency keys (`payPalRequestId` values) should also be stored so retries reuse them.

---

### Error-handling model (all operations)

- Throw-only — **no** `…Result` variants (`sdk-map.md`).
- Case A: `catch (PayPalServerSdk.Core.Exceptions.SdkException<{Op}Error> ex)` then `ex.Error.TryGetError` / `TryGetError1` / `TryGetNoContent` / `TryGetRawError`.
- Case B (SearchTransactions only): `catch (SdkException<RawError> ex)` — `StatusCode`, `ReadAsString()`.
- Typed `{Op}Error` types live in `PayPalServerSdk.Errors`. `Error`/`Error1` live in `PayPalServerSdk.Models`. `RawError`/`ApiError` live in `PayPalServerSdk.Core.ErrorResponse`.
- Read `Error.Name`, `Error.Message`, `Error.DebugId`, and `Details[].Issue` for operator-facing text. Do not parse `ex.ToString()`.

Namespaces to import (C# does not import children transitively): `PayPalServerSdk`; `PayPalServerSdk.Servers`; `PayPalServerSdk.Models`; `PayPalServerSdk.Models.Enums`; `PayPalServerSdk.Errors`; `PayPalServerSdk.Core.Exceptions`; `PayPalServerSdk.Core.ErrorResponse`; `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`; `PayPalServerSdk.Core.Configuration`; `PayPalServerSdk.Core.Request`. Controllers are used via `client.Orders` / `client.Payments` / `client.Vault` / `client.TransactionSearch` (`PayPalServerSdk.Api`).

---

## Trap notes

⚠ Step 1 (client / DI) — `HttpClient` lifetime and whether the SDK client is registered as a singleton over a factory-owned handler. **MUST load `dotnet-client-initialization`** before `new PayPalServerSdkClient` or `AddPayPalServerSdkClient`.

⚠ Step 1 (auth) — which options property takes ClientId/ClientSecret, and what happens on 401 (cached token vs new credentials). **MUST load `dotnet-authentication`** before wiring `Oauth2`.

⚠ Step 1 (BaseUrl / retries) — `Environment` vs nested `Server.{Name}.{Environment}.BaseUrl` are not applied on the same schedule; retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; a transport failure on **POST** can still be retried (authorize/capture/refund/vault-create). **MUST load `dotnet-configuration-resilience`** before registering or tuning the client.

⚠ Step 2/4/6/10 (calls) — list/search and every operation with leading nullable-no-default params mis-bind if called positionally; the token is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}(...)`.

⚠ Step 2/7 (models) — `StringEnum<T>` members vs C# enums; `required` init; `Money.Value` as string; `CardRequest.VaultId` vs `Token` (billing agreement only). **MUST load `dotnet-models`** before building `OrderRequest` / `PaymentTokenRequest` / reading responses.

⚠ Steps 2–10 (errors) — Case A vs Case B differ per operation (SearchTransactions is Case B; Vault uses `TryGetError1`); `JsonException` reaches the boundary from two directions (see REQUIRED READING). **MUST load `dotnet-error-handling`** before any try/catch.

⚠ Tests — the seam is the `HttpClient` constructor argument, not SDK internals. **MUST load `dotnet-testing`** before stubbing.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — construct / DI-register `PayPalServerSdkClient`, `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `options.Oauth2` / `OAuth2ClientCredentials` |
| `dotnet-configuration-resilience` | Step 1 — `Server.Default.Sandbox.BaseUrl`, retries, timeouts, SearchTransactions paging |
| `dotnet-calling-endpoints` | Steps 2–10 — named arguments, `ct:`, request bodies |
| `dotnet-models` | Steps 2–10 — records, StringEnums, required members, wire names |
| `dotnet-error-handling` | Steps 2–10 — `SdkException<T>`, Case A/B, JsonException |
| `dotnet-testing` | Tests for the integration layer |

**JsonException — both of these hazard rows apply to the error boundary (written early):**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

**Assumptions**

- Currency is `PayPal:Currency`; every `Money`/`AmountWithBreakdown` uses that code and a decimal **string** matching the eShop total to the cent.
- Sandbox; test card Visa `4111111111111111`, any future `YYYY-MM` expiry, any CVC, any name, any billing address (always send `CountryCode` if an `Address` is present).
- Direct card processing via `PaymentSource.Card` on `CreateOrder` with `Intent = Authorize`. No PayPal Wallet redirect, no hosted fields.
- Vault is used as the saved-card store; eShop persists only token id + safe descriptors + PayPal customer ids.
- `PayPal:Environment` maps to `ServerEnvironment.Sandbox`. `PayPal:BaseUrl`, when set, is assigned verbatim to `options.Server.Default.Sandbox.BaseUrl`.
- Reconciliation joins `TransactionInformation.InvoiceId` / `CustomField` to the eShop order id written on `PurchaseUnitRequest` at authorize time.

**Blockers / genuine gaps**

1. **`PAYER_ACTION_REQUIRED` / 3DS browser challenge.** The SDK **does** expose a no-browser card path (`CardRequest` number/expiry/cvc, no `return_url`). It also exposes `OrderStatus.PayerActionRequired` and `CardExperienceContext.ReturnUrl`/`CancelUrl`. If CreateOrder/AuthorizeOrder (or vault) returns `PAYER_ACTION_REQUIRED`, completing that payment requires a browser/3DS round-trip this product forbids. **Do not invent a redirect/3DS workaround.** Treat that result as a failed payment with an operator-actionable error. Prefer `OrdersCardVerificationMethod.AvsCvv` (or default `ScaWhenRequired`) and omit `ExperienceContext`. Whether the sandbox test card stays on the no-browser path is **UNVERIFIED** (live traffic only).
2. **`ServerEnvironment` has only `Sandbox`.** There is no Production (or other) member (`Servers/ServerEnvironment.cs`). A non-Sandbox `PayPal:Environment` cannot be expressed by this SDK — fail configuration rather than guessing a host.
3. **`AuthorizationStatus` has no `EXPIRED` and no `AUTHORIZED`.** Stale holds are detected via `ExpirationTime` and/or capture/reauth errors, not a status enum. Do not code against members the SDK does not have.
4. **Reauthorize XML describes a “PayPal account payment”.** The operation exists and is keyed by `authorization_id` (`operations/Payments.md`). Whether a **card** authorization can be renewed through this call is **UNVERIFIED**. If it fails (typically 422), return an operator-actionable “authorization cannot be renewed — recapture/repay required” error. Do not silently CreateOrder again with a stored PAN (none is stored).
5. **Transaction search window is 31 days per call** (XML on `SearchTransactions`). Covering a longer ISO-8601 range requires multiple windowed calls plus in-window paging. Not a missing API; a hard per-call limit.
6. **Vault list `customer_id` identity** (PayPal `Customer.Id` vs `MerchantCustomerId`) is **UNVERIFIED** from map vs operation XML. Persist both; see ListCustomerPaymentTokens row. Not a missing list API.
7. **Client XML notes Vault is “Available in the US only”** (`PayPalServerSdkClient.cs`). If the sandbox merchant is not vault-eligible, `CreatePaymentToken` will fail at runtime — operator-actionable; no alternate store-in-app-DB path.

No capability in the brief is missing from the map: authorize-then-capture with card, vault + pay with `vault_id`, reauthorize, void, refund + `PayPal-Request-Id`, seller receivable breakdown, transaction search paging, and custom BaseUrl including `/v1/oauth2/token` are all present as documented above.
