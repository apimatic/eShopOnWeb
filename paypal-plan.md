# PayPal eShopOnWeb — plan & contract sheet

NuGet: `AsadAli.Checkout.Sdk` (install version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`. Map stamp: tag `v1.0.1` / commit `9653d18`.

---

## Scope & sequence

Additive PayPal money movement + saved cards. Existing catalog/basket/order flow stays. Persist PayPal-owned ids/status on the eShop payment record so later fulfil/cancel/refund/reauth can act.

| Step | What | Operations |
|---|---|---|
| 1 | Bind `PayPal:*` + env credentials; construct/register client; currency from config | client options only (no API call) |
| 2 | Save card (no browser) | `Vault.CreatePaymentToken` |
| 3 | List caller’s saved cards | `Vault.ListCustomerPaymentTokens` (page through `TotalPages`) |
| 4 | Delete a saved card | `Vault.DeletePaymentToken` (+ list to confirm gone) |
| 5 | Place eShop order, then **hold** funds (intent authorize, not capture). Card PAN **or** `vault_id` | `Orders.CreateOrder` (`CheckoutPaymentIntent.Authorize` + `PaymentSource`). If create returns no authorization, `Orders.AuthorizeOrder`. Refresh: `Orders.GetOrder` / `Payments.GetAuthorizedPayment` |
| 6 | Detect 3DS / payer-action → **STOP** (no approval round-trip) | read `OrderStatus.PayerActionRequired` / `Links` — do not follow approve/payer-action URLs |
| 7 | Fulfil = **capture** the hold. If the auth is stale, **reauthorize** first | `Payments.ReauthorizePayment` then `Payments.CaptureAuthorizedPayment`. Read fee/net from capture. Refresh: `Payments.GetCapturedPayment` |
| 8 | Cancel before fulfil = **void** the hold | `Payments.VoidPayment` |
| 9 | Refund after fulfil (full or partial); never refund more than captured | `Payments.RefundCapturedPayment`. Refresh: `Payments.GetRefund` / `Payments.GetCapturedPayment` |
| 10 | Reconciliation for a from/to ISO-8601 range (every page) | `TransactionSearch.SearchTransactions` |

Supporting reads (state the payment record must keep): PayPal order id, authorization id + `AuthorizationStatus`, capture id + `CaptureStatus` + amounts/fee/net, refund ids + `RefundStatus` + amounts, vault payment-token id, PayPal `Customer.Id`, `MerchantCustomerId`.

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

Responses are the payload records themselves (no wrapper field). Default `prefer` is `"return=minimal"` (id/status/links only). Pass `prefer: "return=representation"` on authorize/capture/reauth/void/refund so amounts, fees, and nested payment ids are present.

No-throw `…Result` variants: **absent**. Every call throws on non-2xx.

### Client construction / auth / BaseUrl

| Fact | Value | Cite |
|---|---|---|
| Client ctor | `PayPalServerSdk.PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` | `sdk-map.md`, `PayPalServerSdkClient.cs` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` — registers **`AddSingleton`** | `ServiceCollectionExtensions.cs` |
| Options type | `PayPalServerSdk.PayPalServerSdkClientOptions` | `PayPalServerSdkClientOptions.cs` |
| `Environment` | `PayPalServerSdk.Servers.ServerEnvironment` — **only member** `Sandbox` (wire `"Sandbox"`). `Default()` → `Sandbox`. **No Live/Production member.** | `Servers/ServerEnvironment.cs`, `sdk-map.md` *Servers & auth* |
| `Oauth2` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?` | `PayPalServerSdkClientOptions.cs` |
| Credentials members | `required string ClientId`, `required string ClientSecret`, `string? Scope` (omit Scope) | `OAuth2ClientCredentials.cs` |
| Token request | SDK uses `OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), rawClient)` — `POST {BaseUrl}/v1/oauth2/token`, `grant_type=client_credentials`, `Authorization: Basic base64(ClientId:ClientSecret)` | `AuthSchemes.cs`, `OAuth2ClientCredentialsStrategy.cs` |
| `Oauth2TokenStrategy` | leave null so the default strategy above is used | `PayPalServerSdkClientOptions.cs` |
| `Server` | `PayPalServerSdk.ServerOptions` → `Default` : `PayPalServerSdk.Servers.DefaultOptions` → `Sandbox` : `DefaultOptions.SandboxOptions` | `ServerOptions.cs`, `Servers/DefaultOptions.cs` |
| Default sandbox URL | `https://api-m.sandbox.paypal.com` | `Servers/DefaultOptions.cs` |
| **Custom BaseUrl (first-class)** | `options.Server.Default.Sandbox.BaseUrl = verbatim override`. `Server.Default(path)` resolves `UrlTemplate(Sandbox.BaseUrl, path)` for **every** API call **and** the OAuth token URL `/v1/oauth2/token`. This **is** a first-class SDK option. | `Servers/DefaultOptions.cs`, `Server.cs`, `AuthSchemes.cs` |
| `Retry` | `PayPalServerSdk.Core.Configuration.RetryOptions` (all members `required`; start from `RetryOptions.Default()`) | `sdk-map.md` |
| `Logging` | `PayPalServerSdk.Core.Configuration.LoggingOptions` — `LogRequestBody` / `LogResponseHeaders` / `LogRequestHeaders` exist. Do **not** turn on body logging (PAN/CVV). `RedactedKeys` does **not** include `number` or `security_code`. | `LoggingOptions.cs` |
| Per-call options | `PayPalServerSdk.Core.RequestOptions` — only `LogLevel? LogLevel`. **Cannot** add/override headers. | `Core/RequestOptions.cs` |
| Controllers | `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch` (`PayPalServerSdk.Api`) | `sdk-map.md` |

**Config binding (app, not SDK types):**

| Source | Key | Goes to |
|---|---|---|
| env | `PAYPAL_CLIENT_ID` | `options.Oauth2.ClientId` |
| env | `PAYPAL_CLIENT_SECRET` | `options.Oauth2.ClientSecret` |
| env | `PAYPAL_ENVIRONMENT` | `options.Environment` — only `ServerEnvironment.Sandbox` exists |
| env | `PAYPAL_CURRENCY` | every `Money` / `AmountWithBreakdown` `CurrencyCode` (not a client option) |
| section | `PayPal:ClientId` | `Oauth2.ClientId` |
| section | `PayPal:ClientSecret` | `Oauth2.ClientSecret` |
| section | `PayPal:Environment` | `options.Environment` (Sandbox only) |
| section | `PayPal:Currency` | amount `CurrencyCode` |
| section | `PayPal:BaseUrl` | **when set, verbatim** → `options.Server.Default.Sandbox.BaseUrl` (API + token) |

Target sandbox for development: `Environment = ServerEnvironment.Sandbox` and leave BaseUrl unset (default host above), unless `PayPal:BaseUrl` is provided.

### Idempotency headers (all mutating ops)

| C# parameter | HTTP header | Notes | Cite |
|---|---|---|---|
| `payPalRequestId` | `PayPal-Request-Id` | Caller-supplied key. Orders XML: stored 6 hours (up to 72 by account manager); **mandatory for single-step create with payment source** (card / vault_id). Payments XML: stored 45 days. Vault create: stored 3 hours. Null is omitted (flattener skips null). | `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`, `Core/ParameterFlattener.cs` |
| *(none — generated)* | `Idempotency-Key` | SDK **always** sends `new Guid()` on these calls. Caller **cannot** set or suppress it (`RequestOptions` has no header bag). | `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs` |
| `prefer` | `Prefer` | default `"return=minimal"`; use `"return=representation"` when reading amounts/nested payments | `Api/Orders.cs` XML |

Also sent when non-null: `PayPal-Mock-Response`, `PayPal-Client-Metadata-Id`, `PayPal-Auth-Assertion`, `PayPal-Partner-Attribution-Id`. Pass `null` for unused nullable-no-default params.

UNVERIFIED (live): whether PayPal keys idempotency off `PayPal-Request-Id`, `Idempotency-Key`, or both. Because the SDK mints a fresh `Idempotency-Key` every invocation, **app-side short-circuit** (if the eShop payment already has an authorization/capture/refund id, do not call again) is required in addition to passing a stable `payPalRequestId`.

### Amounts

| Type | Fields | Cite |
|---|---|---|
| `PayPalServerSdk.Models.Money` | `CurrencyCode (currency_code): string !req` (ISO-4217, length 3); `Value (value): string !req` (regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`, max 32) | `records-1-Ac-Pa.md`, `Models/Money.cs` |
| `PayPalServerSdk.Models.AmountWithBreakdown` | same `CurrencyCode`/`Value` !req + `Breakdown (breakdown): AmountBreakdown?` | `records-1-Ac-Pa.md` |

`Value` is a **string**, not `decimal`. Format the eShop order total as a string whose numeric value equals the order total to the cent, with the decimal precision that currency requires (source: integer for non-fractional currencies e.g. JPY; fraction for others). `CurrencyCode` = configured `PayPal:Currency`. Do not send `double`/`decimal` on the wire.

### Card field names (C# → wire)

`PayPalServerSdk.Models.CardRequest` (`Models/CardRequest.cs`, `records-1-Ac-Pa.md`) and `PaymentTokenRequestCard` (same card fields):

| Purpose | C# | Wire | Constraints (source) |
|---|---|---|---|
| PAN | `Number` | `number` | 13–19 digits `[0-9]{13,19}` |
| Expiry | `Expiry` | `expiry` | **single** ISO-8601 `YYYY-MM` (length 7, regex `^[0-9]{4}-(0[1-9]|1[0-2])$`) — **not** separate month/year |
| CVC | `SecurityCode` | `security_code` | 3–4 digits |
| Cardholder name | `Name` | `name` | 1–300 |
| Saved card | `VaultId` | `vault_id` | on `CardRequest` only (pay with vaulted token) |
| Billing line 1 | `BillingAddress.AddressLine1` | `billing_address.address_line_1` | |
| Billing line 2 | `BillingAddress.AddressLine2` | `billing_address.address_line_2` | |
| City | `BillingAddress.AdminArea2` | `billing_address.admin_area_2` | |
| State/province | `BillingAddress.AdminArea1` | `billing_address.admin_area_1` | |
| Postal | `BillingAddress.PostalCode` | `billing_address.postal_code` | |
| Country | `BillingAddress.CountryCode` | `billing_address.country_code` | **required** on `Address`, ISO-3166-1 alpha-2 |

`PayPalServerSdk.Models.Address`: `CountryCode (country_code): string !req`; others optional (`records-1-Ac-Pa.md`).

Never persist PAN/CVC in the eShop DB or logs. Sandbox test PAN: Visa `4111111111111111`.

`PayPalServerSdk.Models.Token` / `TokenType.BillingAgreement` is **not** a vaulted card. Pay with a saved card via `CardRequest.VaultId`, not `PaymentSource.Token`.

---

### Operations

#### 1. `Orders.CreateOrder` — create + hold (primary authorize path)

- **HTTP**: `POST /v2/checkout/orders` · `operations/Orders.md`
- **Signature**: `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: the five nullables before `body` (pass `null` to skip).
- **Returns**: `PayPalServerSdk.Models.Order` (the payload)
- **Error**: Case A `SdkException<PayPalServerSdk.Errors.CreateOrderError>` — `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback]

**Request `OrderRequest`** (`records-1-Ac-Pa.md`):

| Field | Wire | Type | Req |
|---|---|---|---|
| `Intent` | `intent` | `CheckoutPaymentIntent` | **!req** → `CheckoutPaymentIntent.Authorize` |
| `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnitRequest>` | **!req** |
| `PaymentSource` | `payment_source` | `PaymentSource?` | one-off card **or** vault |
| `Payer` | `payer` | `Payer?` | optional |
| `ApplicationContext` | `application_context` | `OrderApplicationContext?` | omit 3DS return/cancel URLs (product STOP) |

`PurchaseUnitRequest.Amount`: `AmountWithBreakdown !req` — `CurrencyCode` + `Value` = order total. Optional `InvoiceId (invoice_id)`, `CustomId (custom_id)` for reconciliation.

**One-off card:** `PaymentSource { Card = new CardRequest { Number, Expiry, SecurityCode, Name, BillingAddress } }`. Do **not** set `Attributes.Verification.Method`. `OrdersCardVerificationMethod.AvsCvv` serializes as wire `AVS_CVV` (`StringEnumConverter` writes `.Value`; ctor is `new("AVS_CVV")`). Live `POST /v2/checkout/orders` rejected that as `INVALID_PARAMETER_VALUE` on `/payment_source/card/attributes/verification/method`. The other members (`SCA_ALWAYS`, `SCA_WHEN_REQUIRED`, `3D_SECURE`) document a payer HATEOAS redirect — product STOP. Omit `Attributes` (and do not set `ExperienceContext.ReturnUrl`/`CancelUrl`). `records-1-Ac-Pa.md`, `Models/Enums/OrdersCardVerificationMethod.cs`, `Core/Enum/StringEnumConverter.cs`.

**Saved card:** `PaymentSource { Card = new CardRequest { VaultId = paymentTokenId } }` (no PAN)

Optional stored-credential on `CardRequest.StoredCredential`: `PaymentInitiator` !req, `PaymentType` !req, `Usage` optional.

**Response `Order`** — read:

| Field | Wire | Use |
|---|---|---|
| `Id` | `id` | PayPal order id (persist) |
| `Status` | `status` | `OrderStatus` — see enums |
| `Intent` | `intent` | confirm `Authorize` |
| `PurchaseUnits[].Payments.Authorizations[]` | `purchase_units[].payments.authorizations` | `AuthorizationWithAdditionalData.Id`, `Status`, `Amount`, `ExpirationTime` |
| `Links` | `links` | `LinkDescription.Href`/`Rel`/`Method` — if payer-action/approve appears → STOP |
| `PaymentSource.Card` | `payment_source.card` | `LastDigits`, `Brand`, `AuthenticationResult` (safe) |

Envelope: **none** — fields are on `Order`.

#### 2. `Orders.AuthorizeOrder` — authorize an existing PayPal order

Use when CreateOrder did not return an authorization (status `Created`/`Approved` without `payments.authorizations`).

- **HTTP**: `POST /v2/checkout/orders/{id}/authorize` · `operations/Orders.md`
- **Signature**: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: `payPalMockResponse` … `body`
- **Returns**: `PayPalServerSdk.Models.OrderAuthorizeResponse` (same shape as Order: `Id`, `Status`, `PurchaseUnits`, `Links`, …) · `records-1-Ac-Pa.md`
- **Error**: Case A `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError`

**Request `OrderAuthorizeRequest`**: only `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` — set `Card` (`CardRequest`) the same way as create. Amount is **not** on this body (it lives on the order).

Notes (map): buyer must have approved **or** a valid `payment_source` must be provided. Do not implement the `rel:approve` redirect.

#### 3. `Orders.GetOrder`

- **HTTP**: `GET /v2/checkout/orders/{id}`
- **Signature**: `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — pass `fields: null` unless needed
- **Returns**: `Order`
- **Error**: Case A `GetOrderError` — `TryGetError(out Error)` [401, 404] · `TryGetRawError`

#### 4. `Payments.CaptureAuthorizedPayment` — fulfil

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/capture` · `operations/Payments.md`
- **Signature**: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: `payPalMockResponse` … `body`
- **Returns**: `PayPalServerSdk.Models.CapturedPayment`
- **Error**: Case A `CaptureAuthorizedPaymentError` — `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`

**Identify auth:** path `authorizationId` = `Authorization.Id` / `AuthorizationWithAdditionalData.Id` from the order.

**Request `CaptureRequest`**: `Amount (amount): Money?` (omit for remaining authorized amount; set equal to order total for an exact full capture); `FinalCapture (final_capture): bool? = false` (set `true` when this is the last capture); `InvoiceId`, `NoteToPayer`, `SoftDescriptor`, `PaymentInstruction` optional.

**Response — money to show the operator** (`CapturedPayment` / `SellerReceivableBreakdown`, `records-1` / `records-2`):

| Field | Wire | Meaning |
|---|---|---|
| `Id` | `id` | capture id (persist) |
| `Status` | `status` | `CaptureStatus` |
| `Amount` | `amount` | captured `Money` |
| `SellerReceivableBreakdown.GrossAmount` | `seller_receivable_breakdown.gross_amount` | gross (`Money` !req on breakdown) |
| `SellerReceivableBreakdown.PaypalFee` | `paypal_fee` | PayPal fee |
| `SellerReceivableBreakdown.NetAmount` | `net_amount` | net to merchant |
| `SellerReceivableBreakdown.ReceivableAmount` | `receivable_amount` | receivable |
| `CreateTime` / `UpdateTime` | `create_time` / `update_time` | |
| Map note | breakdown “not available for transactions that are in pending state” | if `Status == Pending`, fee/net may be absent — re-GET |

`prefer: "return=representation"`. Idempotency: `payPalRequestId` → `PayPal-Request-Id`.

#### 5. `Payments.GetCapturedPayment`

- **Signature**: `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `CapturedPayment`
- **Error**: Case A `GetCapturedPaymentError` — `TryGetError` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError`

Use after capture (and after refunds) to refresh amount/fee/net/`CaptureStatus`.

#### 6. `Payments.GetAuthorizedPayment`

- **Signature**: `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `PayPalServerSdk.Models.PaymentAuthorization` — `Id`, `Status` (`AuthorizationStatus`), `Amount`, `ExpirationTime`, `CreateTime`, `UpdateTime`
- **Error**: Case A `GetAuthorizedPaymentError` — `TryGetError` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError`

#### 7. `Payments.ReauthorizePayment` — stale hold

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/reauthorize` · `operations/Payments.md`
- **Signature**: `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: `payPalRequestId`, `payPalAuthAssertion`, `body`
- **Returns**: `PaymentAuthorization` (new honor period; persist new `Id`/`ExpirationTime`/`Status`)
- **Error**: Case A `ReauthorizePaymentError` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`

**Request `ReauthorizeRequest`**: only `Amount (amount): Money?` — pass the hold amount (order total) as `Money`. Map notes: supports only `amount`; after **30 days** from original auth you cannot reauthorize (must create a new authorized payment instead). Reauth window described as days 4–29 after the 3-day honor period.

**Operator-readable failure:** there is no dedicated “expired, cannot renew” type. On `SdkException<ReauthorizePaymentError>`:
1. `ex.Error.TryGetError(out Error e)` → show `e.Message`, `e.Name`, `e.DebugId`, and each `e.Details[i].Issue` + `Details[i].Description` (and `Field` if present).
2. `TryGetNoContent` / `TryGetRawError` → `RawError.StatusCode` + `ReadAsString()`.
3. If reauth is impossible, surface that PayPal message and do **not** capture; operator must take a new payment.

Exact `Issue` strings for “auth expired / cannot reauthorize” are **UNVERIFIED** (free-form `string`, not an enum).

#### 8. `Payments.VoidPayment` — cancel before capture

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/void` · `operations/Payments.md`
- **Signature**: `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId`
- **Returns**: `PaymentAuthorization` (`Status` → `Voided`)
- **Error**: Case A `VoidPaymentError` — `TryGetError` [401, 403, 404, **409**, 422] · `TryGetNoContent` [500] · `TryGetRawError`
- **Body**: none
- Map notes: cannot void an authorization that has been fully captured.
- Idempotency: `payPalRequestId` → `PayPal-Request-Id`

#### 9. `Payments.RefundCapturedPayment`

- **HTTP**: `POST /v2/payments/captures/{capture_id}/refund` · `operations/Payments.md`
- **Signature**: `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: `payPalMockResponse` … `body`
- **Returns**: `PayPalServerSdk.Models.Refund`
- **Error**: Case A `RefundCapturedPaymentError` — `TryGetError` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent` [500] · `TryGetRawError`

**Request `RefundRequest`**: full refund → `body: null` or empty `RefundRequest` (map: empty payload). Partial → `Amount (amount): Money` (`CurrencyCode` + `Value`). Also `CustomId`, `InvoiceId`, `NoteToPayer` optional.

**Response `Refund`:**

| Field | Wire | Use |
|---|---|---|
| `Id` | `id` | refund id (persist) |
| `Status` | `status` | `RefundStatus` |
| `Amount` | `amount` | this refund `Money` |
| `SellerPayableBreakdown.GrossAmount` / `PaypalFee` / `NetAmount` | `seller_payable_breakdown.*` | breakdown |
| `SellerPayableBreakdown.TotalRefundedAmount` | `total_refunded_amount` | total refunded against the capture (if present) |

**Remaining refundable:** **not** a first-class field. Compute `capture.Amount.Value − sum(refunds)` / compare `TotalRefundedAmount` to capture amount. `CaptureStatus.PartiallyRefunded` vs `Refunded` on `GetCapturedPayment`. Refuse another refund when status is `Refunded` or remaining is zero. Never refund more than captured — PayPal 422 + `Error.Details.Issue` if it rejects; still enforce in app.

Idempotency: caller-supplied key → `payPalRequestId` (`PayPal-Request-Id`).

#### 10. `Payments.GetRefund`

- **Signature**: `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `Refund`
- **Error**: Case A `GetRefundError` — `TryGetError` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError`

#### 11. `Vault.CreatePaymentToken` — save card (no browser)

- **HTTP**: `POST /v3/vault/payment-tokens` · `operations/Vault.md`
- **Signature**: `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: `payPalRequestId`
- **Returns**: `PayPalServerSdk.Models.PaymentTokenResponse`
- **Error**: Case A `CreatePaymentTokenError` — **`TryGetError1(out Error1)`** [400, 403, 404, 422, 500] · `TryGetRawError`  
  (`Error1` / `ErrorDetails1` / `ErrorLinkDescription` — vault uses these names, not `Error`.)

**No CreateCustomer operation exists** in this SDK (Vault has 6 ops; none create a customer). Pass customer on the token request:

`PaymentTokenRequest` (`records-2-Pa-Ve.md`):

| Field | Wire | Type | Req |
|---|---|---|---|
| `Customer` | `customer` | `Customer?` | set `MerchantCustomerId` = eShop shopper id; `Id` = PayPal customer id if already known |
| `PaymentSource` | `payment_source` | `PaymentTokenRequestPaymentSource` | **!req** → `Card = PaymentTokenRequestCard` |

`Customer.Id` = PayPal-generated (`Models/Customer.cs`); `Customer.MerchantCustomerId` = merchant’s id. Persist **both** from the response.

`PaymentTokenRequestCard`: `Name`, `Number`, `Expiry` (`YYYY-MM`), `SecurityCode`, `Brand`, `BillingAddress` — same wires as CardRequest. **No** `ExperienceContext` / return URL on this type (that is `SetupTokenRequestCard` — do **not** use setup tokens; they are the browser/3DS vault path).

**Response `PaymentTokenResponse`:**

| Field | Wire | Safe display / persist |
|---|---|---|
| `Id` | `id` | vault payment-token id → later `CardRequest.VaultId` |
| `Customer.Id` | `customer.id` | PayPal customer id |
| `Customer.MerchantCustomerId` | `customer.merchant_customer_id` | shopper key |
| `PaymentSource.Card.LastDigits` | `payment_source.card.last_digits` | last digits |
| `PaymentSource.Card.Brand` | `brand` | `CardBrand` |
| `PaymentSource.Card.Expiry` | `expiry` | `YYYY-MM` |
| `PaymentSource.Card.Name` | `name` | cardholder name (not PAN) |
| `PaymentSource.Card.VerificationStatus` | `verification_status` | `CardVerificationStatus` (`Verified` / `Failed`) |
| `PaymentSource.Card.Type` | `type` | `CardType` |

`PaymentTokenResponse` has **no** `Status` field. Vault lifecycle enum `VaultStatus` / `PaymentTokenStatus` apply to other records (`CardVaultResponse`, `SetupTokenResponse`), not this response.

Do **not** call `CreateSetupToken` for this product (`SetupTokenResponse.Status` includes `PayerActionRequired`; `VaultCardExperienceContext` has `ReturnUrl`/`CancelUrl`).

#### 12. `Vault.ListCustomerPaymentTokens`

- **HTTP**: `GET /v3/vault/payment-tokens` · `operations/Vault.md`
- **Signature**: `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query**: `customer_id` ← `customerId`, `page_size`, `page`, `total_required`
- **Returns**: `CustomerVaultPaymentTokensResponse` — `TotalItems`, `TotalPages`, `Customer` (`VaultResponseCustomer`), `PaymentTokens` (`IReadOnlyList<PaymentTokenResponse>`), `Links`
- **Error**: Case A `ListCustomerPaymentTokensError` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`
- **Pagination**: map says none (only `page`, no `perPage`). Page with `page` + `pageSize`; set `totalRequired: true`; loop `page = 1 .. TotalPages` (and/or `Links`). Default `pageSize` is 5 — raise it.

XML on `customerId`: “unique identifier representing a specific customer in merchant's/partner's system or records.” Pass the same `MerchantCustomerId` used at vault time; also persist PayPal `Customer.Id`. Which id the live API accepts is **UNVERIFIED** if those two differ — if list is empty, retry is not invented here; store both from create and use the id the create response’s `Customer` echoes.

#### 13. `Vault.GetPaymentToken` / `Vault.DeletePaymentToken`

| Op | HTTP | Signature | Returns | Error |
|---|---|---|---|---|
| `GetPaymentToken` | `GET /v3/vault/payment-tokens/{id}` | `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenResponse` | Case A `GetPaymentTokenError` — `TryGetError1` [403, 404, 422, 500] |
| `DeletePaymentToken` | `DELETE /v3/vault/payment-tokens/{id}` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `void` | Case A `DeletePaymentTokenError` — `TryGetError1` [400, 403, 500] |

`id` = `PaymentTokenResponse.Id`. Delete has **no** `payPalRequestId` (only SDK `Idempotency-Key`). After delete: token must not list and must not pay (`vault_id` should 4xx).

#### 14. `TransactionSearch.SearchTransactions` — reconciliation

**Present in this SDK** (not a gap). · `operations/TransactionSearch.md`

- **HTTP**: `GET /v1/reporting/transactions`
- **Signature**: `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly**: the eight nullables `transactionId` … `terminalId` (`null` to skip)
- **Query wires**: `start_date` ← `startDate`, `end_date` ← `endDate`, plus the other names in the ops page
- **Returns**: `PayPalServerSdk.Models.SearchResponse`
- **Error**: **Case B** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` (only Case B op in this SDK)
- **Pagination**: map: none (only `page`, no `perPage`). Use `page` + `pageSize` (default 100). Response: `Page`, `TotalItems`, `TotalPages`, `Links` (`Href`/`Rel`/`Method`). Loop until `page > TotalPages` (and/or follow `Links` where `Rel` indicates next — exact `rel` string **UNVERIFIED**). **Whole range = all pages**, not page 1 only.

**Date range:** `startDate` / `endDate` are `string` in RFC 3339 / ISO-8601 (XML: seconds required; fractional seconds optional). Bind UI from/to as those strings. XML: **maximum supported range is 31 days**. If the operator range is longer, split into ≤31-day windows and concatenate (this is source XML, not an invented workaround). Transactions can take up to three hours to appear; search covers up to three years.

**Match fields** (`SearchResponse.TransactionDetails` is `IReadOnlyList<TransactionDetails>?`; each item is `PayPalServerSdk.Models.TransactionDetails`, **not** `TransactionDetail`. Nested `TransactionInfo (transaction_info): TransactionInformation?` holds the fields below. `records-2-Pa-Ve.md`):

| C# | Wire | Use |
|---|---|---|
| `TransactionId` | `transaction_id` | PayPal txn id |
| `PaypalReferenceId` | `paypal_reference_id` | related id |
| `PaypalReferenceIdType` | `paypal_reference_id_type` | `PayPalReferenceIdType`: `Odr` (ODR), `Txn` (TXN), `Sub` (SUB), `Pap` (PAP) |
| `TransactionEventCode` | `transaction_event_code` | event code (`string`) |
| `TransactionInitiationDate` / `TransactionUpdatedDate` | `transaction_initiation_date` / `transaction_updated_date` | time |
| `TransactionAmount` | `transaction_amount` | `Money` |
| `FeeAmount` | `fee_amount` | fee |
| `TransactionStatus` | `transaction_status` | `string` (not an enum) |
| `InvoiceId` | `invoice_id` | match eShop invoice |
| `CustomField` | `custom_field` | if you set `CustomId` on the order |
| `PaymentTrackingId` | `payment_tracking_id` | |

Default `fields` is `"transaction_info"`. Pass `fields: "all"` when payer/cart/store are needed. Optional `transactionCurrency` can filter to `PayPal:Currency`.

`SearchBalances` exists but is **out of scope** (balances, not txn list).

---

### Enums in scope (`PayPalServerSdk.Models.Enums` — `StringEnum<T>`, members not C# enums)

Use `Type.Member` or `Type.FromValue("WIRE")`. `map/models/enums.md`.

| Enum | Members (C# (wire)) | Meaning for this app |
|---|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` | **hold** = `Authorize` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** | **Held/authorized path:** look at nested `AuthorizationStatus` as well as order status. **STOP** on `PayerActionRequired` (browser/3DS). Do not treat this as success. |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` | **Held** = `Created` (and not `Denied`/`Voided`). `Pending` + `AuthorizationStatusDetails.Reason` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` | pending auth |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` | fulfil success ≈ `Completed`; block further refund on `Refunded` |
| `CaptureIncompleteReason` | `BuyerComplaint`, `Chargeback`, `Echeck`, `InternationalWithdrawal`, `Other`, `PendingReview`, `ReceivingPreferenceMandatesManualAction`, `Refunded`, `TransactionApprovedAwaitingFunding`, `Unilateral`, `VerificationRequired`, `DeclinedByRiskFraudFilters` (wires UPPER_SNAKE) | pending/denied capture |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` | refund success ≈ `Completed` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, … `Unknown (UNKNOWN)` (full list on enums.md) | display |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` | display |
| `CardVerificationStatus` | `Verified (VERIFIED)`, `Failed (FAILED)` | vault card |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` | other vault objects, not `PaymentTokenResponse` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` | **setup tokens**; `PayerActionRequired` = STOP if you ever see it |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` | Converter emits the parenthesized wire string. XML: `ScaAlways`/`ScaWhenRequired`/`_3DSecure` tell the caller to redirect the payer to a HATEOAS link (browser). `AvsCvv` XML describes AVS/CVV hold without redirect, but **live sandbox rejected `AVS_CVV`** as `INVALID_PARAMETER_VALUE` for CreateOrder. **Gap:** no-browser direct-card authorize has no live-accepted Method on this field. Omit `CardRequest.Attributes`. If CreateOrder still returns `TRANSACTION_REFUSED` with no `Order`/`PayerActionRequired`, that is a provider/account refusal, not a missing request field. |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` | stored credential |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` | stored credential |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` | stored credential |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` | **not** vault cards |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` | setup-token vault path — out of scope |
| `ParesStatus` | `Y`, `N`, `U`, `A`, `C`, `R`, `D`, `I` | 3DS outcome on `AuthenticationResponse.ThreeDSecure` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` | 3DS |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` | txn search |
| `LinkHttpMethod` | `Get`, `Post`, `Put`, `Delete`, `Head`, `Connect`, `Options`, `Patch` | `Links` |
| `DisbursementMode` | `Instant (INSTANT)`, `Delayed (DELAYED)` | capture default Instant |

**Payer-action / 3DS STOP (product rule, SDK can still return it):**

1. `Order.Status == OrderStatus.PayerActionRequired` or `OrderAuthorizeResponse.Status` same.
2. `Links` with a payer-action/approve `Rel` (exact `rel` strings not enumerated in the map — if any link implies a shopper browser step, STOP).
3. Do **not** set `CardExperienceContext.ReturnUrl` / `CancelUrl` or `OrderApplicationContext.ReturnUrl` / `CancelUrl`.
4. Do **not** implement ConfirmOrder / setup-token approval.

If PayPal still returns payer-action, fail the payment with an operator/shopper message that a browser challenge is required and this app will not collect it.

---

### Error handling

**Thrown type:** `PayPalServerSdk.Core.Exceptions.SdkException<TError>` — property `required TError Error` **only** (no `StatusCode` on the exception). · `Core/Exceptions/SdkException.cs`, `sdk-map.md`

Namespaces: `SdkException<T>` → `PayPalServerSdk.Core.Exceptions`; `ApiError` / `RawError` → `PayPalServerSdk.Core.ErrorResponse`; `{Op}Error` → `PayPalServerSdk.Errors`; payloads → `PayPalServerSdk.Models`.

**Case A payload `Error`** (`records-1-Ac-Pa.md`): `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`, `Links`.  
**`ErrorDetails`**: `Issue (issue): string !req`, `Description (description): string?`, `Field`, `Value`, `Location` default `"body"`.

**Vault Case A payload `Error1`**: same Name/Message/DebugId; `Details`: `ErrorDetails1` (Issue !req, Description, Field, …); `Links`: `ErrorLinkDescription` (`Rel` optional).

**HTTP status:**

- Typed accessor success (`TryGetError` / `TryGetError1`): constructor stores **only** the JSON body — `TryGetRawError` is **false**. Numeric HTTP status is **not** on `Error`. Infer from `Name` / `Details[].Issue` (and the accessor’s documented status set).
- Fallback `TryGetRawError` / `TryGetNoContent`: `RawError.StatusCode` (`HttpStatusCode`) + `ReadAsString()`.
- Case B (`SearchTransactions`): always `ex.Error.StatusCode` / `ReadAsString()`. Optionally `ReadAsJson<SearchError>()` (`Name`/`Message`/`DebugId`/`Details`) — whether the live body matches `SearchError` is **UNVERIFIED**; fall back to `ReadAsString()`.

**409** is modeled on capture, refund, void (`operations/Payments.md`) — treat as conflict (already captured/voided/refunded **or** replay). Distinguish using persisted eShop ids: if we already stored that capture/refund/void id, treat as idempotent success; else show `Error.Message` + `Details.Issue`. Exact `Name`/`Issue` literals for duplicate-invoice vs genuine conflict are **UNVERIFIED** (not an enum).

**Card decline / insufficient funds / instrument declined:** Case A `Error` + `Details.Issue`/`Description`; and/or `ProcessorResponse.ResponseCode` (`ProcessorResponseCode`) on authorization/capture when representation is returned. Do not hard-code issue strings from memory.

**Auth expired / cannot reauth / void of captured / refund exceeds capture:** same `Error` path; payments ops list 422 and 409. Operator text = `Message` + `Issue` + `Description` + `DebugId`.

Catch **per-operation** `SdkException<{Op}Error>` (or `SdkException<RawError>` for search). `TryGetRawError` is not a catch-all on typed errors.

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` lifetime, whether `AddPayPalServerSdkClient`’s **singleton** captures one factory client for process life, and how to bind `IConfiguration` in the DI callback. **MUST load `dotnet-client-initialization`** before writing the factory/DI registration.

⚠ Step 1 (credentials) — where `Oauth2` is set relative to construction, env vs config, and rotating secrets. **MUST load `dotnet-authentication`** before wiring `OAuth2ClientCredentials`.

⚠ Step 1 (BaseUrl / retries) — `options.Server.Default.Sandbox.BaseUrl` vs `options.Environment` (captured at construct); whether retries/timeouts bound a whole call vs a single attempt vs the `HttpClient`; transport failures retrying **POST** (authorize/capture/refund/void/vault) even when status retries do not. **MUST load `dotnet-configuration-resilience`** before setting `Retry`, `Server`, or `Logging`.

⚠ Step 1 (logging) — enabling request-body logging would write PAN/CVV; `RedactedKeys` does not list card fields. Keep body logging off. **MUST load `dotnet-configuration-resilience`**.

⚠ Steps 2–10 (calls) — nullable params with **no C# default** must be passed (`null`); named arguments; `ct:` not `cancellationToken:`; `prefer: "return=representation"` when you need nested payments/amounts. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}` call.

⚠ Steps 2–10 (models) — records are init-only; `required` members in object initializers; enums are `StringEnum<T>` (`CheckoutPaymentIntent.Authorize`, not a C# enum); unmodeled JSON is dropped. **MUST load `dotnet-models`** before building `OrderRequest` / `CardRequest` / `Money`.

⚠ Steps 2–10 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Steps 2–10 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the `HttpClient` constructor argument is the test seam; match eShopOnWeb’s existing test framework. **MUST load `dotnet-testing`** before stubbing PayPal.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — ctor, DI `AddPayPalServerSdkClient`, `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `Oauth2` / `OAuth2ClientCredentials` |
| `dotnet-configuration-resilience` | Step 1 — `Server.Default.Sandbox.BaseUrl`, retries, timeouts, pagination loops, logging |
| `dotnet-calling-endpoints` | Steps 2–10 — signatures, named args, `ct`, `prefer`, envelopes |
| `dotnet-models` | Steps 2–10 — records, `StringEnum`, `required`, wire names |
| `dotnet-error-handling` | Steps 2–10 — Case A/B, `TryGet*`, `JsonException` from 2xx **and** from failed error-body parse (both hazard rows above) |
| `dotnet-testing` | Tests — `HttpClient` seam |

---

## Assumptions & Blockers

**Assumptions**

- Additive integration: eShop still owns catalog/basket/order; PayPal ids live on a payment record attached to the order.
- Direct card (Advanced Card Processing) + vault are enabled on the merchant sandbox account, as stated.
- Shopper is logged in; `MerchantCustomerId` = eShop user id.
- Fulfil/cancel/refund/reauth are operator actions against persisted PayPal authorization/capture ids.
- Currency is a single configured code applied to every amount.
- Primary hold path is `CreateOrder` with `Intent = Authorize` and `PaymentSource` in one call (`payPalRequestId` mandatory per XML); `AuthorizeOrder` only if that create did not produce an authorization.

**Blockers / gaps**

1. **Browser / 3DS challenge:** SDK/API **can** return `OrderStatus.PayerActionRequired` and HATEOAS payer-action links (`CardExperienceContext` / setup tokens exist). Product rule is STOP — do not build an approval round-trip. Any such PayPal response is a failed payment, not a continuation. This is a **runtime blocker**, not a missing operation.
2. **`ServerEnvironment` is Sandbox-only.** There is no Live/Production constant. `PayPal:Environment` / `PAYPAL_ENVIRONMENT` values other than Sandbox cannot be expressed as an SDK environment. Custom hosts use `PayPal:BaseUrl` → `options.Server.Default.Sandbox.BaseUrl` (first-class; applies to token + API). Pointing that override at live is not documented as a supported environment in this SDK.
3. **No CreateCustomer API** in the map. Customer is only a field on vault/order models.
4. **Remaining refundable amount** is not a field; compute from capture vs refunds / `CaptureStatus`.
5. **SDK always sends a unique `Idempotency-Key`**. Caller idempotency is `PayPal-Request-Id` via `payPalRequestId`. Live interaction of the two headers is **UNVERIFIED** — persist local payment state so a double-click cannot start a second hold/capture.
6. **PCI:** passing PAN/CVV in `CardRequest` / `PaymentTokenRequestCard` requires PCI SAQ D (XML on `CardRequest`). Hosted fields are mentioned in that comment but **are not operations in this SDK**. Direct card is in-scope only because the merchant account is enabled and the test card is sandbox.
7. **Vault “US only”** note on `PayPalServerSdkClient` XML — if the merchant is not US-enabled for vault, save-card ops will fail at runtime.
8. **Transaction search 31-day window** (XML on `endDate`). Longer from/to must be split.
9. **Typed errors do not expose HTTP status** when `TryGetError`/`TryGetError1` succeeds. Issue/name strings for decline, duplicate invoice, expired auth, etc. are free-form — **UNVERIFIED** literals.
10. **`SearchTransactions` XML disagrees with itself on `page` (zero-relative vs `page=1` first page).** Generated default is `page = 1`. Start at 1; if a page is empty, stop. **UNVERIFIED** live indexing.

Not blockers: transaction search **is** in the SDK; BaseUrl override **is** first-class and **does** apply to the OAuth token request; list/get/delete vaulted cards **are** in the SDK.
