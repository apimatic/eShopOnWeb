# PayPal payments + saved cards — eShopOnWeb CONTRACT SHEET

Package: `AsadAli.Checkout.Sdk` (install version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`. Map stamp: tag `v1.0.1` / commit `9653d18`.

Additive capability in PublicApi + application/infrastructure. Does not replace catalog/basket/order flow.

---

## Scope & sequence

| Step | What | SDK operations |
|---|---|---|
| 1 | Bind `PayPal:*` from config/env; construct/register `PayPalServerSdkClient` (sandbox, optional verbatim `BaseUrl`) | client options / `AddPayPalServerSdkClient` |
| 2 | Persist a payment record on the eShop order (PayPal order id, auth/capture/refund ids + statuses, amounts). Local idempotency keys for pay/fulfil/cancel/refund. | — (app state) |
| 3 | POST pay — create PayPal order `INTENT=AUTHORIZE` for the order total, then authorize (raw card **or** `vault_id`). Persist hold ids/status. Detect `PAYER_ACTION_REQUIRED` / 3DS; do not invent an approval UI. | `Orders.CreateOrder`, `Orders.AuthorizeOrder` (`Orders.GetOrder` to refresh) |
| 4 | POST fulfil — if auth is stale, `ReauthorizePayment` first (or tell the operator it cannot be renewed). Then `CaptureAuthorizedPayment`. Read seller receivable breakdown (gross / PayPal fee / net). | `Payments.GetAuthorizedPayment`, `Payments.ReauthorizePayment`, `Payments.CaptureAuthorizedPayment`, `Payments.GetCapturedPayment` |
| 5 | POST cancel (before fulfil) — void the authorization. | `Payments.VoidPayment` |
| 6 | POST refunds (after fulfil) — full (`body: null`) or partial (`RefundRequest.Amount`). Never refund more than captured minus completed refunds. Caller idempotency key → `payPalRequestId`. | `Payments.RefundCapturedPayment`, `Payments.GetRefund`, `Payments.GetCapturedPayment` |
| 7 | GET my-orders — eShop orders + stored PayPal payment state (refresh from PayPal if needed). | `Orders.GetOrder`, `Payments.GetAuthorizedPayment`, `Payments.GetCapturedPayment`, `Payments.GetRefund` |
| 8 | GET reconciliation?from=&to= — page `SearchTransactions` over the **whole** range (chunk if the window exceeds 31 days). Line up against eShop orders via `custom_id` / `invoice_id` / transaction id. | `TransactionSearch.SearchTransactions` |
| 9 | POST/GET/DELETE payment-methods — vault a card, list, delete. Pay later with `CardRequest.VaultId`. | `Vault.CreatePaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.GetPaymentToken`, `Vault.DeletePaymentToken` |

Out of this flow (do **not** use for the hold/capture path): `Orders.CaptureOrder` (order-level capture; this integration captures the **authorization**), `Orders.ConfirmOrder`, tracking ops, `Vault.CreateSetupToken` / `GetSetupToken` (only if a vault call surfaces `PAYER_ACTION_REQUIRED` — report it, do not build a 3DS UI), `Subscriptions.*`, `TransactionSearch.SearchBalances`.

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

All operations are **throw-only** (no `…Result` variants). Returns are the model itself — **no extra envelope wrapper**.

### 0. Client construction, auth, servers, BaseUrl

| Fact | Value | Source |
|---|---|---|
| Client type | `PayPalServerSdk.PayPalServerSdkClient` | `sdk-map.md` |
| Ctor | `PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` — registers `IHttpClientFactory` + singleton client | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| Options | `PayPalServerSdk.PayPalServerSdkClientOptions`: `Environment` (`PayPalServerSdk.Servers.ServerEnvironment`), `Retry` (`PayPalServerSdk.Core.Configuration.RetryOptions`), `Logging` (`PayPalServerSdk.Core.Configuration.LoggingOptions`), `Server` (`PayPalServerSdk.ServerOptions`), `Oauth2` (`PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?`), `Oauth2TokenStrategy` (`PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>?`) | `sdk-map.md`, `PayPalServerSdkClientOptions.cs` |
| Environment members | **`ServerEnvironment.Sandbox` only** (wire `"Sandbox"`). `Default()` → Sandbox. **No Live member.** | `sdk-map.md` *Servers & auth*, `Servers/ServerEnvironment.cs` |
| Credentials | `new OAuth2ClientCredentials { ClientId = required string, ClientSecret = required string, Scope = string? }` | `OAuth2ClientCredentials.cs` |
| Token request | Default strategy POSTs `{BaseUrl}/v1/oauth2/token` with HTTP Basic (`ClientId:ClientSecret`) and form `grant_type=client_credentials` (+ `scope` if set). Same `Server.Default` resolver as every API call. | `AuthSchemes.cs`, `OAuth2ClientCredentialsStrategy.cs` |
| Default sandbox BaseUrl | `https://api-m.sandbox.paypal.com` | `Servers/DefaultOptions.cs` |
| **`PayPal:BaseUrl` verbatim override** | When set, assign **`options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl as-is>`**. `Server.Default(path)` builds `UrlTemplate(Sandbox.BaseUrl, path, [])` for **every** controller path **and** `/v1/oauth2/token`. Do **not** set `HttpClient.BaseAddress` for this — the SDK does not take the API host from the `HttpClient`. | `ServerOptions.cs` (`PayPalServerSdk`), `Servers/DefaultOptions.cs`, `Server.cs`, `PayPalServerSdkClient.cs` |
| Config bind (never hard-code) | `PayPal:ClientId` ← `PAYPAL_CLIENT_ID`; `PayPal:ClientSecret` ← `PAYPAL_CLIENT_SECRET`; `PayPal:Environment` ← `PAYPAL_ENVIRONMENT` (sandbox → `ServerEnvironment.Sandbox`); `PayPal:Currency` ← `PAYPAL_CURRENCY`; `PayPal:BaseUrl` optional | user request |
| Per-call options | `PayPalServerSdk.Core.RequestOptions` — only `LogLevel? LogLevel`. Headers are **method parameters**, not `RequestOptions`. | `Core/RequestOptions.cs` |

Controllers on the client: `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch` (`PayPalServerSdk.Api.*`).

Namespaces to import by kind (`sdk-map.md`): client/options/root types `PayPalServerSdk`; servers `PayPalServerSdk.Servers`; controllers `PayPalServerSdk.Api`; records `PayPalServerSdk.Models`; enums `PayPalServerSdk.Models.Enums`; errors `PayPalServerSdk.Errors`; `SdkException<T>` `PayPalServerSdk.Core.Exceptions`; `RawError`/`ApiError` `PayPalServerSdk.Core.ErrorResponse`; OAuth credentials `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`; retry/logging `PayPalServerSdk.Core.Configuration`.

---

### 1–3. Orders — create AUTHORIZE + card / vaulted card; read authorization id

#### `client.Orders.CreateOrder` — `POST /v2/checkout/orders`

- **Signature** (`operations/Orders.md`): `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly (nullable, no default):** `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion` (pass `null` to skip).
- **Headers:** `PayPal-Mock-Response` ← `payPalMockResponse`; `PayPal-Request-Id` ← `payPalRequestId`; `PayPal-Partner-Attribution-Id` ← `payPalPartnerAttributionId`; `PayPal-Client-Metadata-Id` ← `payPalClientMetadataId`; `PayPal-Auth-Assertion` ← `payPalAuthAssertion`; `Prefer` ← `prefer`; plus generated `Idempotency-Key: Guid.NewGuid()` every call (`Api/Orders.cs`).
- **`prefer`:** default `"return=minimal"` (id/status/links only). Pass **`prefer: "return=representation"`** so purchase-unit payments (auth id) are present.
- **`payPalRequestId`:** stored 6 hours (up to 72h via account manager). XML: **mandatory for single-step create with payment source** (Card, vault_id, …) (`Api/Orders.cs`).
- **Returns:** `PayPalServerSdk.Models.Order` (not wrapped).
- **Error Case A:** `SdkException<PayPalServerSdk.Errors.CreateOrderError>` — `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback.

**`OrderRequest`** (`records-1-Ac-Pa.md`, `Models/OrderRequest.cs`):

| Field | Wire | Type | Required |
|---|---|---|---|
| `Intent` | `intent` | `CheckoutPaymentIntent` | **required** — use `CheckoutPaymentIntent.Authorize` |
| `Payer` | `payer` | `Payer?` | no |
| `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnitRequest>` | **required** |
| `PaymentSource` | `payment_source` | `PaymentSource?` | set for one-off card or vaulted card |
| `ApplicationContext` | `application_context` | `OrderApplicationContext?` | no |

**`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`): `Amount (amount): AmountWithBreakdown !req`; `ReferenceId (reference_id): string?`; `CustomId (custom_id): string?` (eShop order id — reconciliation); `InvoiceId (invoice_id): string?`; `Description (description): string?`; `Items`, `Shipping`, … optional.

**`AmountWithBreakdown`** (`records-1-Ac-Pa.md`): `CurrencyCode (currency_code): string !req` (ISO-4217, length 3); `Value (value): string !req`; `Breakdown (breakdown): AmountBreakdown?`. **`Value` must equal the eShop order total to the cent** (same currency as `PayPal:Currency`).

**One-off card — `PaymentSource.Card` = `CardRequest`** (`records-1-Ac-Pa.md`, `Models/CardRequest.cs`):

| Field | Wire | Type | Notes |
|---|---|---|---|
| `Name` | `name` | `string?` | cardholder name, 1–300 |
| `Number` | `number` | `string?` | PAN, `[0-9]{13,19}` — sandbox Visa `4111111111111111` |
| `Expiry` | `expiry` | `string?` | **ISO-8601 `YYYY-MM`**, regex `^[0-9]{4}-(0[1-9]|1[0-2])$` |
| `SecurityCode` | `security_code` | `string?` | CVC `[0-9]{3,4}` — cannot be present when `payment_initiator=MERCHANT` |
| `BillingAddress` | `billing_address` | `Address?` | `CountryCode (country_code): string !req` (ISO-3166-1 alpha-2); `AddressLine1/2`, `AdminArea1` (state), `AdminArea2` (city), `PostalCode` |
| `VaultId` | `vault_id` | `string?` | **saved card** — PayPal payment-token id (`^[0-9a-zA-Z_-]+$`, 1–255). Do **not** send PAN when paying with a saved card. |
| `ExperienceContext` | `experience_context` | `CardExperienceContext?` | `ReturnUrl`/`CancelUrl` for 3DS — this app must **not** invent an approval UI |
| `StoredCredential` | `stored_credential` | `CardStoredCredential?` | if set: `PaymentInitiator !req`, `PaymentType !req`, `Usage?` |
| `Attributes` | `attributes` | `CardAttributes?` | vault-during-pay: `Vault.StoreInVault` |

**Saved card payment-source shape (later order):** `PaymentSource { Card = new CardRequest { VaultId = <PaymentTokenResponse.Id> } }` — **not** `PaymentSource.Token`. `Token` is `{ Id !req, Type: TokenType !req }` and `TokenType` has **only** `BillingAgreement (BILLING_AGREEMENT)` (`enums.md`, `records-2-Pa-Ve.md`).

There is **no** `PaymentSourceType` enum — `PaymentSource` is a bag of optional members (`Card`, `Token`, `Paypal`, …) (`records-2-Pa-Ve.md`).

#### `client.Orders.AuthorizeOrder` — `POST /v2/checkout/orders/{id}/authorize`

- **Signature:** `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse` … `body` (5 params). Pass `body: null` if `payment_source` was already on create; or `new OrderAuthorizeRequest { PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = cardRequest } }`.
- **Headers:** same pattern as create (`PayPal-Request-Id`, `Prefer`, `PayPal-Client-Metadata-Id`, …) + generated `Idempotency-Key`.
- **Returns:** `PayPalServerSdk.Models.OrderAuthorizeResponse`
- **Error Case A:** `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError` fallback.

**Authorization id path (persist all of these):**

`OrderAuthorizeResponse.PurchaseUnits[i].Payments.Authorizations[j].Id`  
(`OrderAuthorizeResponse.PurchaseUnits`: `IReadOnlyList<PurchaseUnit>`; `PurchaseUnit.Payments`: `PaymentCollection`; `PaymentCollection.Authorizations`: `IReadOnlyList<AuthorizationWithAdditionalData>`; `AuthorizationWithAdditionalData.Id (id): string?`, `Status (status): AuthorizationStatus?`, `ExpirationTime (expiration_time): string?`, `Amount (amount): Money?`) — `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`.

Also persist: `OrderAuthorizeResponse.Id` (PayPal checkout order id), `Status` (`OrderStatus`).

**3DS / buyer-approval detection (no browser round-trip):** if `Order.Status` / `OrderAuthorizeResponse.Status` == `OrderStatus.PayerActionRequired` (`PAYER_ACTION_REQUIRED`), **fail the pay call with that status** (and any `Links` / `Error.Details`). Do not implement an approval UI. Optional extra signals: `PaymentSourceResponse.Card.AuthenticationResult` (`LiabilityShift`, `ThreeDSecure.AuthenticationStatus` / `EnrollmentStatus`).

#### `client.Orders.GetOrder` — `GET /v2/checkout/orders/{id}`

- **Signature:** `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `fields`, `payPalMockResponse`, `payPalAuthAssertion` must be passed explicitly.
- **Returns:** `Order`
- **Error Case A:** `SdkException<GetOrderError>` — `TryGetError(out Error)` [401, 404] · `TryGetRawError`

---

### 4–7. Payments — capture authorization, void, reauthorize, refund, retrieve capture/refund

#### `client.Payments.CaptureAuthorizedPayment` — `POST /v2/payments/authorizations/{authorization_id}/capture`

- **Signature:** `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse` … `body`.
- **Headers:** `PayPal-Mock-Response`, `PayPal-Request-Id` (stored **45 days**), `PayPal-Auth-Assertion`, `Prefer`, generated `Idempotency-Key`.
- **Returns:** `PayPalServerSdk.Models.CapturedPayment`
- **Error Case A:** `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback.

**`CaptureRequest`:** `Amount (amount): Money?` (omit to capture remaining); `InvoiceId`; `FinalCapture (final_capture): bool? = false` (set `true` for the fulfilment capture); `NoteToPayer`; `SoftDescriptor`.

**Seller receivable (fulfilment display)** — `CapturedPayment.SellerReceivableBreakdown` (`records-2-Pa-Ve.md`): `GrossAmount (gross_amount): Money !req`; `PaypalFee (paypal_fee): Money?`; `NetAmount (net_amount): Money?`; also `PaypalFeeInReceivableCurrency`, `ReceivableAmount`, `ExchangeRate`, `PlatformFees`. **Not populated when capture is pending.** If breakdown is missing on a non-pending capture, `GetCapturedPayment`.

#### `client.Payments.GetCapturedPayment` — `GET /v2/payments/captures/{capture_id}`

- **Signature:** `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalMockResponse` must be passed.
- **Returns:** `CapturedPayment`
- **Error Case A:** `SdkException<GetCapturedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`

#### `client.Payments.GetAuthorizedPayment` — `GET /v2/payments/authorizations/{authorization_id}`

- **Signature:** `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns:** `PaymentAuthorization` (`Id`, `Status`, `ExpirationTime`, `Amount`, …)
- **Error Case A:** `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError`

Use `ExpirationTime` (RFC3339, seconds required) vs now to decide stale-before-fulfil.

#### `client.Payments.ReauthorizePayment` — `POST /v2/payments/authorizations/{authorization_id}/reauthorize`

- **Signature:** `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalRequestId`, `payPalAuthAssertion`, `body`.
- **`ReauthorizeRequest`:** `Amount (amount): Money?` only.
- **Returns:** `PaymentAuthorization` — **new** `Id` / `ExpirationTime` / `Status`. Persist the new authorization id; subsequent capture uses **this** id.
- **Error Case A:** `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent` [500] · `TryGetRawError`
- **Notes (operation):** honor period 3 days; reauthorize from day 4–29 of the original 29-day window; **if 30 days since original authorization you must create a new authorized payment rather than reauthorize**; new honor period 3 days; amount rules geography-dependent. (`operations/Payments.md`)
- **Cannot renew — operator-actionable:** (1) `TryGetError` with `Error.Details[].Issue` + `Description` + `DebugId` (typical HTTP 422 bucket); (2) current `AuthorizationStatus` is `Voided`, `Denied`, or `Captured` / `PartiallyCaptured` with nothing left; (3) ≥30 days since original auth (operation notes) — tell the operator a **new** authorization is required, do not silently re-charge. There is **no** `EXPIRED` member on `AuthorizationStatus`.

#### `client.Payments.VoidPayment` — `POST /v2/payments/authorizations/{authorization_id}/void`

- **Signature:** `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId`.
- **Returns:** `PaymentAuthorization` (expect `Status = Voided`)
- **Error Case A:** `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError`
- **Notes:** cannot void a fully captured authorization.

#### `client.Payments.RefundCapturedPayment` — `POST /v2/payments/captures/{capture_id}/refund`

- **Signature:** `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse` … `body`.
- **Idempotency:** caller-supplied key → **`payPalRequestId`** → header `PayPal-Request-Id` (stored 45 days). Pass `prefer: "return=representation"`.
- **Full refund:** `body: null` (or empty `RefundRequest`). **Partial:** `body: new RefundRequest { Amount = new Money { CurrencyCode, Value } }`.
- **Returns:** `PayPalServerSdk.Models.Refund` (`Id`, `Status`, `Amount`, `SellerPayableBreakdown.TotalRefundedAmount`, …)
- **Error Case A:** `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError`

**Partial-refund cap (app + PayPal):** remaining = captured `Money.Value` − sum of refunds with `RefundStatus.Completed`. Refuse if requested amount > remaining. PayPal `CaptureStatus.Refunded` / `PartiallyRefunded` and `GetCapturedPayment` are the source of truth after the call.

#### `client.Payments.GetRefund` — `GET /v2/payments/refunds/{refund_id}`

- **Signature:** `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns:** `Refund`
- **Error Case A:** `SdkException<GetRefundError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError`

---

### 8. Vault — save / list / delete cards; customer mapping

#### `client.Vault.CreatePaymentToken` — `POST /v3/vault/payment-tokens`

- **Signature:** `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must be passed explicitly.
- **Headers:** `PayPal-Request-Id` (stored **3 hours**) + generated `Idempotency-Key`.
- **Returns:** `PaymentTokenResponse`
- **Error Case A:** `SdkException<CreatePaymentTokenError>` — **`TryGetError1(out Error1)`** [400, 403, 404, 422, 500] · `TryGetRawError` (not `TryGetError`).

**`PaymentTokenRequest`:** `Customer (customer): Customer?`; `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` with `Card (card): PaymentTokenRequestCard?` (`Name`, `Number`, `Expiry` `YYYY-MM`, `SecurityCode`, `Brand`, `BillingAddress`) — **no PAN in the response**.

**`Customer`:** `Id (id): string?` (PayPal-generated customer id, 1–22); `MerchantCustomerId (merchant_customer_id): string?` (eShop shopper id, 1–64). On create set `MerchantCustomerId` to the signed-in shopper key; persist returned `CustomerResponse.Id` **and** `MerchantCustomerId`.

**Safe descriptor (response, never PAN):** `PaymentTokenResponse.Id` (vault token — store as payment-method id / later `VaultId`); `PaymentSource.Card` = `CardPaymentTokenEntity`: `LastDigits (last_digits)`, `Brand (brand): CardBrand?`, `Expiry (expiry)` `YYYY-MM`, `Name`, `Type`, `VerificationStatus`. **No `Number` / `SecurityCode` on the entity.**

`PaymentTokenResponse` has **no** `Status` field. `PaymentTokenStatus` (incl. `PayerActionRequired`) is on **`SetupTokenResponse`**, not this record. If vaulting requires buyer approval, report it from `Error1` / do not add a 3DS UI.

#### `client.Vault.ListCustomerPaymentTokens` — `GET /v3/vault/payment-tokens`

- **Signature:** `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query:** `customer_id` ← `customerId`; `page_size` ← `pageSize`; `page` ← `page`; `total_required` ← `totalRequired`
- **Returns:** `CustomerVaultPaymentTokensResponse` — `PaymentTokens`, `TotalItems`, `TotalPages`, `Customer`
- **Error Case A:** `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`
- **Pagination:** map says none (`page` only). Loop `page = 1 .. TotalPages` with `totalRequired: true`. Default `pageSize` is **5**.
- **`customerId` XML** (`Api/Vault.cs`): “unique identifier representing a specific customer in merchant's/partner's system or records.” That matches `MerchantCustomerId`. `Customer.Id` XML is PayPal-generated. Persist both; call list with the merchant shopper id used at create. If the call 400s/empties, the other id is the unresolved mapping (see Blockers).

#### `client.Vault.GetPaymentToken` — `GET /v3/vault/payment-tokens/{id}`

- **Signature:** `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns:** `PaymentTokenResponse`
- **Error Case A:** `SdkException<GetPaymentTokenError>` — `TryGetError1(out Error1)` [403, 404, 422, 500] · `TryGetRawError`

#### `client.Vault.DeletePaymentToken` — `DELETE /v3/vault/payment-tokens/{id}`

- **Signature:** `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns:** `void` (`Task`)
- **Error Case A:** `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`

After delete, the token must not list and must not be usable as `VaultId`.

---

### 9. Transaction search — reconciliation over a date range (whole range)

#### `client.TransactionSearch.SearchTransactions` — `GET /v1/reporting/transactions`

- **Signature:** `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `transactionId` … `terminalId` (8 params) — pass `null` to skip.
- **Query:** `start_date` ← `startDate`; `end_date` ← `endDate`; plus the other names in `operations/TransactionSearch.md`.
- **Date format:** RFC3339; **seconds required**; fractional seconds optional. **Maximum range 31 days** (`Api/TransactionSearch.cs`). If `from`/`to` span more than 31 days, **chunk** into ≤31-day windows and concatenate. Executed txns can take **up to three hours** to appear; history up to **three years**.
- **Whole range:** map pagination is “none (only `page`)”. Read `SearchResponse.TotalPages` / `TotalItems` / `Page`; loop `page` from 1 while `page <= TotalPages` (`pageSize` default 100). XML example: `page=1` + `page_size=20` is the first page (1-based despite a “zero-relative” phrase in the same comment).
- **`fields`:** default `"transaction_info"`. Pass `"all"` (or a comma list) when lining up payer/cart/invoice. Valid tokens include `transaction_info`, `payer_info`, `shipping_info`, `cart_info`, `store_info`, `auction_info`, `incentive_info`.
- **Returns:** `SearchResponse` — `TransactionDetails[]` each with `TransactionInfo` (`TransactionId`, `PaypalReferenceId`, `TransactionAmount`, `FeeAmount`, `InvoiceId`, `CustomField`, `TransactionStatus`, `TransactionInitiationDate`, …), `AccountNumber`, `StartDate`, `EndDate`, `LastRefreshedDatetime`, `Links`.
- **Error Case B (only Case B op in this SDK):** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. No `TryGetError`.

Line-up: eShop `PurchaseUnitRequest.CustomId` / `InvoiceId` ↔ `TransactionInformation.CustomField` / `InvoiceId`; capture/refund/order ids ↔ `TransactionId` / `PaypalReferenceId`.

---

### 10. Idempotency

| Operation | Caller key parameter | Header | Server stores |
|---|---|---|---|
| `CreateOrder`, `AuthorizeOrder` | `payPalRequestId` | `PayPal-Request-Id` | 6 hours |
| `CaptureAuthorizedPayment`, `ReauthorizePayment`, `RefundCapturedPayment`, `VoidPayment` | `payPalRequestId` | `PayPal-Request-Id` | 45 days |
| `CreatePaymentToken` | `payPalRequestId` | `PayPal-Request-Id` | 3 hours |

Refunds: the HTTP idempotency key **is** `payPalRequestId` (there is no separate SDK parameter).

**Also always sent by the generated client (caller cannot set):** `Idempotency-Key: Guid.NewGuid()` on these writes (`Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`) **and** on the token POST. A retry therefore gets a **new** `Idempotency-Key` even with the same `PayPal-Request-Id`. **UNVERIFIED** whether PayPal treats that as a new request. **Application idempotency is mandatory:** before calling, if this eShop order already has a PayPal authorization/capture/refund id for that action, return the stored result (double-click must not authorize/capture twice).

`RequestOptions` cannot carry these headers.

---

### 11. Errors — types, HTTP status, issue codes, debug id, 3DS

`SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`) exposes **only** `TError Error` — **no** `StatusCode` on the exception.

| Case | When `TryGet*` succeeds | HTTP status |
|---|---|---|
| A typed `TryGetError` / `TryGetError1` | payload `Error` / `Error1` | **Not on the payload.** Accessor is shared across several statuses (see each op). Infer from `Error.Name` or, for unmapped statuses, `TryGetRawError`. |
| A `TryGetNoContent` (Payments 500s) | `RawError` | `RawError.StatusCode` |
| A / B `TryGetRawError` | `RawError` | `RawError.StatusCode` |
| B `SearchTransactions` | `ex.Error` is `RawError` | `ex.Error.StatusCode` |

**`Error`** (`records-1-Ac-Pa.md`): `Name (name): string !req`; `Message (message): string !req`; `DebugId (debug_id): string !req`; `Details (details): IReadOnlyList<ErrorDetails>?`; `Links`.

**`ErrorDetails`:** `Issue (issue): string !req` (fine-grained code); `Description (description): string?`; `Field`, `Value`, `Location` default `"body"`; `Links`.

**Vault errors use `Error1` / `ErrorDetails1` / `ErrorLinkDescription`** (`Rel` optional on error links) — same `Name`/`Message`/`DebugId`/`Issue` shape.

**Issue codes are not an SDK enum.** Compare `Details[i].Issue` as strings. The map does **not** list `INSTRUMENT_DECLINED`, `PAYMENT_DENIED`, `AUTHORIZATION_EXPIRED`, or AUTH/CAPTURE mismatch literals. Match those strings when present; also read `Error.Name`, `Message`, `DebugId`. `ReasonCode.PaymentDenied` (`PAYMENT_DENIED`) exists only on subscription `FailedPaymentDetails`, **not** on Orders/Payments errors.

**3DS / contingency:** `OrderStatus.PayerActionRequired`; `PaymentTokenStatus.PayerActionRequired` (setup-token path only); `AuthenticationResponse` / `ThreeDSecureAuthenticationResponse`; `ErrorDetails.Issue` + `Error.Links`. Report to the caller; **no approval UI**.

---

### 12. Enums actually used (`PayPalServerSdk.Models.Enums`, `StringEnum<T>` — members are C# identifiers, wire in parens) — `enums.md`

| Enum | Members (C# (wire)) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, **`Authorize (AUTHORIZE)`** |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` — **no EXPIRED** |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `CaptureIncompleteReason` | `BuyerComplaint`, `Chargeback`, `Echeck`, `InternationalWithdrawal`, `Other`, `PendingReview`, `ReceivingPreferenceMandatesManualAction`, `Refunded`, `TransactionApprovedAwaitingFunding`, `Unilateral`, `VerificationRequired`, `DeclinedByRiskFraudFilters` (wires = SCREAMING_SNAKE of those names) |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `RefundIncompleteReason` | `Echeck (ECHECK)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, `Solo`, `Jcb`, `Star`, `Delta`, `Switch`, `Maestro`, `CbNationale (CB_NATIONALE)`, `Configoga`, `Confidis`, `Electron`, `Cetelem`, `ChinaUnionPay (CHINA_UNION_PAY)`, `Diners`, `Elo`, `Hiper`, `Hipercard`, `Rupay`, `Ge`, `Synchrony`, `Eftpos`, `CarteBancaire (CARTE_BANCAIRE)`, `StarAccess (STAR_ACCESS)`, `Pulse`, `Nyce`, `Accel`, `Unknown (UNKNOWN)` |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` (on `CardVaultResponse` / `VaultResponse`, **not** on `PaymentTokenResponse`) |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` — **`SetupTokenResponse.Status`** |
| `CardVerificationStatus` | `Verified (VERIFIED)`, `Failed (FAILED)` |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` |
| `TokenType` | **`BillingAgreement (BILLING_AGREEMENT)` only** |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `ParesStatus` | `Y`, `N`, `U`, `A`, `C`, `R`, `D`, `I` |
| `EnrollmentStatus` | `Y`, `N`, `U`, `B` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` (CardVerification default `ScaWhenRequired`) |
| `DisbursementMode` | `Instant (INSTANT)`, `Delayed (DELAYED)` |

No payment-source-type enum.

---

### 13. Money amount format

`PayPalServerSdk.Models.Money` and `AmountWithBreakdown.Value` are **`string`**, not `decimal` (`records-1-Ac-Pa.md`, `Models/Money.cs`):

- Wire: `currency_code` (exactly 3 chars), `value` (max 32, regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`).
- Integer string for non-fractional currencies (e.g. `JPY`); decimal-fraction string for currencies subdivided (e.g. `TND` thousandths). **Required fraction digits follow PayPal ISO-4217 currency codes** (not a C# decimal scale). For a typical 2-decimal currency format the eShop total as `"12.34"` with **no extra/missing cents**.
- Convert `decimal` → string with the currency’s fraction digits; convert back by parsing the string. Do not send a .NET decimal over the wire.

---

### 14. Persist on the eShop payment (PayPal-owned state a later request needs)

| Persist | From |
|---|---|
| PayPal checkout order id + `OrderStatus` | `Order.Id` / `OrderAuthorizeResponse.Id`, `.Status` |
| Authorization id, status, expiration, amount | `PurchaseUnit.Payments.Authorizations[]` or `PaymentAuthorization` |
| Capture id, status, amount, `SellerReceivableBreakdown` (gross/fee/net) | `CapturedPayment` / `PaymentCollection.Captures` (`OrdersCapture`) |
| Each refund id, status, amount, `TotalRefundedAmount` | `Refund` |
| Vault: payment token id, PayPal `customer.id`, `merchant_customer_id`, last digits, brand, expiry | `PaymentTokenResponse` |
| Idempotency keys used | local, keyed by eShop action |

`GET my-orders` is **not** a PayPal list-orders API — it is eShop data plus this stored state (refresh via Get*).

---

## Trap notes

⚠ Step 1 (client / DI) — `HttpClient` vs SDK-client lifetime and `AddPayPalServerSdkClient` ownership are not obvious from the ctor. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 1 (credentials) — `Oauth2` / `Oauth2TokenStrategy` wiring and secret loading are not obvious from the property types. **MUST load `dotnet-authentication`** before setting credentials.

⚠ Step 1 (BaseUrl, retries, timeouts) — `Server.Default.Sandbox.BaseUrl`, retry/timeout options, and `HttpClient` timeouts are different knobs; which calls retry and what `Timeout` bounds is not on the options type. **MUST load `dotnet-configuration-resilience`** before wiring the client or `PayPal:BaseUrl`.

⚠ Steps 3–9 (every call) — many parameters are nullable with **no C# default** and mis-bind if passed positionally; cancellation is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.*.*` call.

⚠ Steps 3–9 (models / enums / money strings) — records are `required`/`init`; enums are `StringEnum<T>` not C# enums; `Money.Value` is a string. **MUST load `dotnet-models`** before building payloads or reading responses.

⚠ Steps 3–9 (errors) — Case A vs Case B, `TryGetError` vs `TryGetError1` vs `TryGetNoContent`, and HTTP status **not** living on `SdkException` will make a naive catch ladder miss declines. **MUST load `dotnet-error-handling`** before writing the integration boundary.

⚠ Step 1 / writes — transport failures vs status retries decide whether a failed authorize/capture/refund can execute more than once even with `PayPal-Request-Id`. **MUST load `dotnet-configuration-resilience`** before enabling retries on this client.

⚠ Step 8 (reconciliation pages) — `SearchTransactions` / `ListCustomerPaymentTokens` expose `page`/`pageSize`/`TotalPages` but the map marks SDK pagination as absent. **MUST load `dotnet-configuration-resilience`** before looping the date range.

⚠ Tests — the `HttpClient` constructor argument is the seam; do not fake SDK internals. **MUST load `dotnet-testing`** before writing integration tests.

⚠ Boundary — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Boundary — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing/`AddPayPalServerSdkClient`, `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `Oauth2` client-credentials |
| `dotnet-configuration-resilience` | Step 1 BaseUrl/retry/timeout; Step 8 pagination loops; write-retry hazard |
| `dotnet-calling-endpoints` | Steps 3–9 — named args, must-pass nullables, `ct:` |
| `dotnet-models` | Steps 3–9 — records, `StringEnum<T>`, `Money` strings, vault/card shapes |
| `dotnet-error-handling` | Every operation’s catch boundary, Case A/B, both `JsonException` paths above |
| `dotnet-testing` | Tests for the PayPal integration layer |

---

## Assumptions & Blockers

- **Assumption:** Fulfilment captures the **authorization** (`Payments.CaptureAuthorizedPayment`), not `Orders.CaptureOrder`. Cancel voids that authorization. Pay is `CreateOrder(Intent=Authorize)` + `AuthorizeOrder` with card or `vault_id`.
- **Assumption:** `PayPal:Environment` value `sandbox` maps to `ServerEnvironment.Sandbox`. `PayPal:Currency` is the ISO code placed on every `Money`/`AmountWithBreakdown`.
- **Assumption:** GET my-orders is eShop-local plus stored PayPal ids/status (optionally refreshed). There is no list-orders operation on `client.Orders`.
- **GAP:** `ServerEnvironment` has **only `Sandbox`**. There is no Live member. Live would require a BaseUrl override and is out of this sandbox task.
- **GAP:** Issue strings `INSTRUMENT_DECLINED`, `PAYMENT_DENIED`, `AUTHORIZATION_EXPIRED`, AUTH/CAPTURE mismatch are **not** in the SDK enum map. Handle as `ErrorDetails.Issue` string compares; do not invent an enum.
- **GAP / ambiguity:** `ListCustomerPaymentTokens(customerId)` XML says merchant/partner records (`merchant_customer_id`); `Customer.Id` XML says PayPal-generated. Persist both from `CreatePaymentToken`; list with the merchant shopper id used at create. If list is empty/400, that mapping is unresolved — do not guess a second protocol.
- **UNVERIFIED:** Whether a retry with the same `PayPal-Request-Id` but a new generated `Idempotency-Key` is treated as the same PayPal request. Local “already has auth/capture/refund id → return stored” is required regardless.
- **UNVERIFIED:** Live wire may omit `SellerReceivableBreakdown` on pending captures (model says so). After `CaptureStatus.Completed`, re-fetch via `GetCapturedPayment` if fee/net are null rather than inventing numbers.
- **Not a GAP:** Direct card + vault operations exist (`CardRequest`, `CreatePaymentToken`, `VaultId`). 3DS is detectable via `OrderStatus.PayerActionRequired` (and setup-token `PaymentTokenStatus.PayerActionRequired`); the SDK has no hosted-fields / 3DS challenge API in this map — reporting that status is the integration, not a missing capture/void/refund op.
