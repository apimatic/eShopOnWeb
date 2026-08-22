# eShopOnWeb × PayPal — implementation plan + CONTRACT SHEET

NuGet: `AsadAli.Checkout.Sdk` (version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`. Map stamp: tag `v1.0.1` / commit `9653d18`.

This sheet is the only SDK contract the implementer uses. Do not open the SDK map or source.

---

## Scope & sequence

| Step | eShop endpoint | SDK operations (in order) |
|---:|---|---|
| 0 | App startup | Construct `PayPalServerSdkClient` with OAuth2 client-credentials + `ServerEnvironment.Sandbox` + optional BaseUrl override. Bind config section `PayPal:`. |
| 1 | `POST /api/payment-methods` | `Vault.CreatePaymentToken` — vault a card; persist PayPal customer id + payment-token id; return last digits / brand / expiry only. |
| 2 | `GET /api/payment-methods` | `Vault.ListCustomerPaymentTokens` — **every page** (`page` + `totalRequired: true`). |
| 3 | `DELETE /api/payment-methods/{paymentMethodId}` | `Vault.DeletePaymentToken` (`id` = vault payment-token id). |
| 4 | `POST /api/orders/{orderId}/pay` | `Orders.CreateOrder` (`CheckoutPaymentIntent.Authorize`) then `Orders.AuthorizeOrder` with **direct card** *or* **vaulted** `CardRequest.VaultId`. Hold amount = order total. |
| 5 | `POST /api/orders/{orderId}/fulfil` | `Payments.GetAuthorizedPayment` → if stale, `Payments.ReauthorizePayment` → `Payments.CaptureAuthorizedPayment`. Persist captured amount, PayPal fee, net proceeds. |
| 6 | `POST /api/orders/{orderId}/cancel` | `Payments.VoidPayment` (only if not yet captured). |
| 7 | `POST /api/orders/{orderId}/refunds` | `Payments.RefundCapturedPayment` (full: body `null`; partial: `RefundRequest.Amount`). Caller idempotency key → `payPalRequestId`. Cap remaining vs captured. |
| 8 | `GET /api/reconciliation?from=&to=` | `TransactionSearch.SearchTransactions` — **every page**, and **every ≤31-day window** covering `[from, to]`. Empty range is success. |

Do **not** call `Orders.CaptureOrder` (that is intent-`CAPTURE`, not this hold-then-capture flow). Do **not** call `Vault.CreateSetupToken` (3DS / browser setup-token path).

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

Enums are `StringEnum<T>` records in `PayPalServerSdk.Models.Enums` — write `CheckoutPaymentIntent.Authorize`, not a C# enum. Compare with `==` against the static members (or `Type.FromValue("wire")`).

---

### 0 — Client construction, credentials, servers

**Config keys (hard-code none of the values):**

| .NET config | Env var | Use |
|---|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | `OAuth2ClientCredentials.ClientId` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | `OAuth2ClientCredentials.ClientSecret` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `PayPalServerSdkClientOptions.Environment` — this SDK’s only member is `ServerEnvironment.Sandbox` (wire `"Sandbox"`). Default if unset: `ServerEnvironment.Default()` → Sandbox. |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | ISO-4217 string for every `Money` / `AmountWithBreakdown` `CurrencyCode` |
| `PayPal:BaseUrl` | (optional) | When set, assign **verbatim** to `options.Server.Default.Sandbox.BaseUrl`. That template is used for **every** path including the token URL `/v1/oauth2/token`. |

**Builder / options APIs** (cite: `sdk-map.md` Getting a client + Servers & auth; `PayPalServerSdkClientOptions.cs`; `ServerOptions.cs`; `Servers/DefaultOptions.cs`; `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`; `AuthSchemes.cs`):

| Type | Namespace | Members used |
|---|---|---|
| `PayPalServerSdkClient` | `PayPalServerSdk` | ctor `(HttpClient httpClient, PayPalServerSdkClientOptions options)` |
| `PayPalServerSdkClientOptions` | `PayPalServerSdk` | `Environment`, `Retry`, `Logging`, `Server`, `Oauth2`, `Oauth2TokenStrategy` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | `ClientId: string` **required**, `ClientSecret: string` **required**, `Scope: string?` |
| `IOAuth2TokenStrategy<OAuth2ClientCredentials>` | `PayPalServerSdk.Core.Authentication.OAuth2` | Leave **unset** so the SDK uses `OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), …)` — token POST to `{BaseUrl}/v1/oauth2/token` with HTTP Basic `clientId:clientSecret` and form `grant_type=client_credentials`. |
| `ServerEnvironment` | `PayPalServerSdk.Servers` | `Sandbox` (wire `Sandbox`) only. **No Production member in this SDK.** |
| `ServerOptions` | `PayPalServerSdk` | `Default: DefaultOptions` |
| `DefaultOptions` | `PayPalServerSdk.Servers` | `Sandbox: SandboxOptions` |
| `DefaultOptions.SandboxOptions` | `PayPalServerSdk.Servers` | `BaseUrl: string` default `"https://api-m.sandbox.paypal.com"` |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` | All members `required` — use `RetryOptions.Default()` or `RetryOptions.Disabled()`, or a full initializer: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout: TimeSpan?`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. |
| `RequestOptions` | `PayPalServerSdk.Core` | Per-call only: `LogLevel: LogLevel?`. **No per-call timeout/base-URL override.** |
| DI | `PayPalServerSdk.ServiceCollectionExtensions` | `AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` |

**BaseUrl override (token + all API calls):** `options.Server.Default.Sandbox.BaseUrl = configuration["PayPal:BaseUrl"]` when that key is non-empty. `Server.Default(path)` builds every operation URL and the OAuth token URL from that one property.

**Controller accessors on the client:** `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch` (`PayPalServerSdk.Api.*`).

⚠ Step 0 (client registration) — `Retry` / `Timeout` on `PayPalServerSdkClientOptions` are **not** the timeout on the `HttpClient` you register, and they do **not** by themselves make writes safe to retry. **MUST load `dotnet-client-initialization`** and **`dotnet-configuration-resilience`** before wiring the client.

⚠ Step 0 (auth) — credentials must be set on `Oauth2` **before** the client is constructed (or inside the DI configure callback); never hard-code secrets. Mapping `PayPal:Environment` onto `ServerEnvironment` can fail if the string is not the wire value `Sandbox`. **MUST load `dotnet-authentication`**.

---

### Amounts (every money field)

| Model | Namespace | Fields | Cite |
|---|---|---|---|
| `Money` | `PayPalServerSdk.Models` | `CurrencyCode (currency_code): string !req` (ISO-4217, length 3), `Value (value): string !req` (not `decimal`) | `records-1-Ac-Pa.md` / `Models/Money.cs` |
| `AmountWithBreakdown` | `PayPalServerSdk.Models` | `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?` | `records-1-Ac-Pa.md` |

**Formatting:** `Value` is a **string**. For USD-style currencies format to **2 decimal places** (e.g. `"10.00"` not `"10"`). Regex from source: `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`. Currency code always from `PayPal:Currency`. Compare amounts as decimal-parsed strings to the cent; never send `decimal`/`double` on the wire.

---

### Direct card vs vaulted card (payment source)

`PaymentSource` (`payment_source`) and `OrderAuthorizeRequestPaymentSource` both take `Card (card): CardRequest?`. Do **not** use `Token (token): Token` for vaulted cards — `TokenType` has only `BillingAgreement`. Vaulted cards use `CardRequest.VaultId`.

**`CardRequest`** (`PayPalServerSdk.Models`, `records-1-Ac-Pa.md`, `Models/CardRequest.cs`):

| C# (wire) | Type | Direct card | Vaulted card |
|---|---|---|---|
| `Name (name)` | `string?` | cardholder name | omit |
| `Number (number)` | `string?` | PAN, 13–19 digits e.g. `4111111111111111` | **omit** (never send PAN) |
| `Expiry (expiry)` | `string?` | **`YYYY-MM`** exactly (regex `^[0-9]{4}-(0[1-9]|1[0-2])$`) | omit |
| `SecurityCode (security_code)` | `string?` | CVC 3–4 digits | omit (XML: cannot be present when `payment_initiator=MERCHANT`) |
| `BillingAddress (billing_address)` | `Address?` | send; `Address.CountryCode (country_code): string !req`; optional `AddressLine1`, `AddressLine2`, `AdminArea2` (city), `AdminArea1` (state), `PostalCode` | omit |
| `VaultId (vault_id)` | `string?` | omit | **payment-token id** from `PaymentTokenResponse.Id` |
| `Attributes (attributes)` | `CardAttributes?` | **omit on AuthorizeOrder** (see below) | omit |
| `StoredCredential (stored_credential)` | `CardStoredCredential?` | omit on first one-off | see below |
| `ExperienceContext (experience_context)` | `CardExperienceContext?` | **omit** (`ReturnUrl`/`CancelUrl` are the 3DS browser path — out of product scope) | omit |

**Vaulted MIT/CIT extras — `CardStoredCredential`** (`!req` `PaymentInitiator`, `PaymentType`):

- Shopper present at checkout: `PaymentInitiator.Customer` (wire `CUSTOMER`), `PaymentType.Unscheduled` or `OneTime`, `Usage = StoredPaymentSourceUsageType.Subsequent`.
- `PaymentType.OneTime` is compatible only with `PaymentInitiator.Customer`.

**Never** persist or log `Number` or `SecurityCode`.

---

### Operation rows

#### A. `Vault.CreatePaymentToken` — save a card

- **HTTP:** `POST /v3/vault/payment-tokens`
- **Controller:** `PayPalServerSdk.Api.Vault` · `client.Vault`
- **Signature:** `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId` — nullable, no default → **must pass explicitly** (header `PayPal-Request-Id`; XML: stored 3 hours)
- **Request `PaymentTokenRequest`:** `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`
  - `Customer`: `Id (id): string?` (PayPal customer id if already known), `MerchantCustomerId (merchant_customer_id): string?` (**set to eShop shopper id** on first vault)
  - `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?` — `Name`, `Number`, `Expiry` (`YYYY-MM`), `SecurityCode`, `Brand: CardBrand?`, `BillingAddress: Address?`
- **Returns:** `PaymentTokenResponse` — **no envelope wrapper**. Read:
  - `Id (id): string?` → this is the **saved-card id** (`paymentMethodId` / later `CardRequest.VaultId`)
  - `Customer (customer): CustomerResponse?` → persist `Customer.Id` as PayPal vault customer id (required for list)
  - `PaymentSource.Card`: `CardPaymentTokenEntity` — `LastDigits (last_digits)`, `Brand (brand): CardBrand?`, `Expiry (expiry)`, `Name` — **safe description**; never `Number`
- **Error:** `SdkException<CreatePaymentTokenError>` Case A · `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` fallback
- **Pagination:** none
- **Cite:** `operations/Vault.md`, `records-2-Pa-Ve.md`

#### B. `Vault.ListCustomerPaymentTokens` — list saved cards

- **HTTP:** `GET /v3/vault/payment-tokens`
- **Signature:** `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - Query: `customer_id` ← `customerId` (**PayPal** customer id from create, not eShop shopper id), `page_size`, `page`, `total_required`
- **Returns:** `CustomerVaultPaymentTokensResponse` — `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `TotalItems`, `TotalPages`, `Customer`, `Links`
- **Full pagination (SDK has no auto-pager):** call with `totalRequired: true`, `pageSize` ≥ 5, `page: 1`, then `page = 2..TotalPages`. Map: “Pagination: none (only `page`, no `perPage`)”.
- **Error:** `SdkException<ListCustomerPaymentTokensError>` Case A · `TryGetError1(out Error1)` [400, 403, 500] · fallback `TryGetRawError` (404 lands here)
- **Cite:** `operations/Vault.md`

#### C. `Vault.DeletePaymentToken` — delete saved card

- **HTTP:** `DELETE /v3/vault/payment-tokens/{id}`
- **Signature:** `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns:** `void`
- **Error:** `SdkException<DeletePaymentTokenError>` Case A · `TryGetError1(out Error1)` [400, 403, 500] · **404 is not typed** → `TryGetRawError` / `RawError.StatusCode`
- **Cite:** `operations/Vault.md`

#### D. `Vault.GetPaymentToken` — optional read-back / 404 after delete

- **Signature:** `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentTokenResponse`
- **Error:** `SdkException<GetPaymentTokenError>` · `TryGetError1(out Error1)` [403, **404**, 422, 500]
- **Cite:** `operations/Vault.md`

#### E. `Orders.CreateOrder` — create hold (intent authorize)

- **HTTP:** `POST /v2/checkout/orders`
- **Signature:** `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params `payPalMockResponse` … `payPalAuthAssertion` — nullable, no default → **must pass explicitly** (`null` to skip)
  - XML: `payPalRequestId` **mandatory** for create-with-payment-source (card / vault_id). Header `PayPal-Request-Id`.
- **Request `OrderRequest`:** `Intent (intent): CheckoutPaymentIntent !req` = **`CheckoutPaymentIntent.Authorize`**, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` (one unit), `PaymentSource (payment_source): PaymentSource?` (card or vault — may instead be sent only on authorize), `Payer?`, `ApplicationContext?`
  - `PurchaseUnitRequest.Amount (amount): AmountWithBreakdown !req` — `Value` = order total to the cent, `CurrencyCode` = `PayPal:Currency`
  - Set `InvoiceId (invoice_id)` and `CustomId (custom_id)` to the eShop order id (reconciliation join keys)
- **Always pass `prefer: "return=representation"`** — default `"return=minimal"` omits purchase-unit payments.
- **Returns:** `Order` — **no extra envelope**. Read `Id`, `Status`, `PurchaseUnits[0].Payments.Authorizations`, `Links`
- **Error:** `SdkException<CreateOrderError>` Case A · `TryGetError(out Error)` [400, 401, 422] · fallback `TryGetRawError`
- **Cite:** `operations/Orders.md`, `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`

#### F. `Orders.AuthorizeOrder` — place the hold

- **HTTP:** `POST /v2/checkout/orders/{id}/authorize`
- **Signature:** `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params `payPalMockResponse` … `body` — nullable, no default → **must pass explicitly**
- **Request `OrderAuthorizeRequest`:** `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` with `Card: CardRequest` (direct PAN **or** `VaultId`)
- **`prefer: "return=representation"`**
- **Returns:** `OrderAuthorizeResponse` — same shape as `Order` for the fields we read: `Id`, `Status`, `PurchaseUnits[].Payments`
- **Where the authorization lives (no wrapper field):** `PurchaseUnits[0].Payments.Authorizations[0]` type `AuthorizationWithAdditionalData`:
  - `Id (id)` → persist as PayPal authorization id
  - `Status (status): AuthorizationStatus?`
  - `Amount (amount): Money?`
  - `ExpirationTime (expiration_time): string?` (ISO-8601)
  - `ProcessorResponse (processor_response)` — decline/AVS/CVV codes
- **Error:** `SdkException<AuthorizeOrderError>` Case A · `TryGetError(out Error)` [400, 401, 403, 404, 422, 500]
- **Cite:** `operations/Orders.md`

**Pay flow (both sources):**

1. If this eShop order already has a non-voided authorization id → return it (application idempotency).
2. `CreateOrder` with `Intent = Authorize`, amount = order total, invoice/custom id = eShop order id, `payPalRequestId` = pay idempotency key, `prefer: "return=representation"`. Attach `PaymentSource.Card` here **or** on step 3 (not both with conflicting cards).
3. `AuthorizeOrder(id: paypalOrderId, payPalRequestId: same-or-derived key, body: OrderAuthorizeRequest with Card, prefer: "return=representation", …nulls…)`.
4. If `Status == OrderStatus.PayerActionRequired` → **fail closed** (browser challenge). See Blockers.
5. Persist PayPal order id, authorization `Id`/`Status`/`Amount`/`ExpirationTime`.

#### G. `Payments.GetAuthorizedPayment` — inspect hold

- **HTTP:** `GET /v2/payments/authorizations/{authorization_id}`
- **Signature:** `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — two nullable no-default params **must pass explicitly**
- **Returns:** `PaymentAuthorization` — `Id`, `Status`, `Amount`, `ExpirationTime`, `CreateTime`, `UpdateTime` (no `ProcessorResponse` on this type)
- **Error:** `SdkException<GetAuthorizedPaymentError>` · `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500]
- **Cite:** `operations/Payments.md`, `records-2-Pa-Ve.md`

#### H. `Payments.ReauthorizePayment` — renew a stale hold

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/reauthorize`
- **Notes (map):** Reauthorizes an authorized **PayPal account** payment. Honor period 3 days; reauthorize from day 4–29; after **30 days** you must create a **new** authorized payment rather than reauthorize. A reauthorization gets a new 3-day honor period. Supports only the `amount` body field.
- **Signature:** `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — three nullable no-default params **must pass explicitly**
- **Request `ReauthorizeRequest`:** `Amount (amount): Money?` — send original hold amount
- **`prefer: "return=representation"`**
- **Returns:** `PaymentAuthorization` — **new** `Id` (replace persisted authorization id), `Status`, `Amount`, `ExpirationTime`
- **Detect “stale”:** `ExpirationTime` in the past and/or honor window elapsed while `Status` is still `Created` / `Pending`.
- **Detect “cannot be renewed” (actionable for operators):**
  - `Status` is `Voided`, `Captured`, `Denied`, or `PartiallyCaptured` → do not call reauthorize; return `Status` + authorization id.
  - `ReauthorizePayment` throws: read `Error.Name`, `Error.Message`, `Error.DebugId`, `Error.Details[].Issue` + `Description` (typical bucket 422). There is **no** typed “expired” enum.
  - Map text: after 30 days this operation is the wrong API — SDK has **no** other renew method. A new `CreateOrder`+`AuthorizeOrder` is a **new** hold, not a renewal of this id.
- **Error:** `SdkException<ReauthorizePaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent` [500]
- **Cite:** `operations/Payments.md`
- **UNVERIFIED:** whether reauthorize succeeds for **direct-card** authorizations (map wording is “PayPal account payment”). If it 422s, surface the error payload; do not invent a second renew API.

#### I. `Payments.CaptureAuthorizedPayment` — take the money

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/capture`
- **Signature:** `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullable no-default params **must pass explicitly**. XML: `PayPal-Request-Id` stored 45 days.
- **Request `CaptureRequest`:** `Amount (amount): Money?` (omit or equal authorized amount for full capture), `FinalCapture (final_capture): bool? = false` → set **`true`**, `InvoiceId?`, `NoteToPayer?`, `SoftDescriptor?`, `PaymentInstruction?`
- **`prefer: "return=representation"`** — fee/net live on the representation
- **Returns:** `CapturedPayment` — **no envelope**. Persist:
  - `Id (id)` capture id
  - `Status (status): CaptureStatus?` — expect `Completed` (or `Pending`)
  - **Captured amount:** `Amount (amount): Money?` **and/or** `SellerReceivableBreakdown.GrossAmount (gross_amount): Money !req`
  - **PayPal fee:** `SellerReceivableBreakdown.PaypalFee (paypal_fee): Money?`
  - **Net proceeds:** `SellerReceivableBreakdown.NetAmount (net_amount): Money?`
  - Map note: `SellerReceivableBreakdown` **is not available while pending** — if `Status == Pending` or breakdown is null, follow up with `GetCapturedPayment`
- **Already captured:** `TryGetError` includes **409**; also `AuthorizationStatus.Captured` on GET. Application: if capture id already persisted, return it.
- **Error:** `SdkException<CaptureAuthorizedPaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent` [500]
- **Cite:** `operations/Payments.md`, `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`

#### J. `Payments.GetCapturedPayment` — refresh fee/net

- **Signature:** `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `CapturedPayment`
- **Error:** `TryGetError` [401, 403, 404] · `TryGetNoContent` [500]
- **Cite:** `operations/Payments.md`

#### K. `Payments.VoidPayment` — release hold on cancel

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/void`
- **Notes:** cannot void a fully captured authorization
- **Signature:** `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — three nullable no-default params **must pass explicitly**
- **Returns:** `PaymentAuthorization` (`Status` → `Voided`)
- **Error:** `SdkException<VoidPaymentError>` · `TryGetError(out Error)` [401, 403, 404, **409**, 422] · `TryGetNoContent` [500]
- **Cite:** `operations/Payments.md`

#### L. `Payments.RefundCapturedPayment` — full / partial refund

- **HTTP:** `POST /v2/payments/captures/{capture_id}/refund`
- **Notes:** full refund = empty JSON body; partial = amount object
- **Signature:** `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullable no-default params **must pass explicitly**
- **Idempotency:** pass the **caller-supplied key** as `payPalRequestId` (header `PayPal-Request-Id`). Distinct keys for two legitimate partials.
- **Request `RefundRequest`:** `Amount (amount): Money?` (omit/`body: null` = full), `CustomId?`, `InvoiceId?`, `NoteToPayer?`, `PaymentInstruction?`
- **Application cap:** remaining = captured `Amount.Value` − sum of persisted refund `Amount`s with `RefundStatus.Completed`. Refuse if requested > remaining **before** calling PayPal. PayPal will also 422 an over-refund.
- **`prefer: "return=representation"`**
- **Returns:** `Refund` — `Id`, `Status: RefundStatus?`, `Amount`, `SellerPayableBreakdown` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount`)
- **Error:** `SdkException<RefundCapturedPaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent` [500]
- **Cite:** `operations/Payments.md`, `records-2-Pa-Ve.md`

#### M. `Payments.GetRefund` — optional

- **Signature:** `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Refund`
- **Cite:** `operations/Payments.md`

#### N. `TransactionSearch.SearchTransactions` — reconciliation (all pages, whole range)

- **HTTP:** `GET /v1/reporting/transactions`
- **Signature:** `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 8 params `transactionId` … `terminalId` — nullable, no default → **must pass explicitly** (`null`)
- **Date params:** RFC-3339 (`Internet date and time format`). **Seconds are required.** Fractional seconds optional. XML: **maximum supported range is 31 days** per call. If `from`/`to` spans longer, split into adjacent windows of ≤31 days and concatenate. Map notes: executed transactions can take **up to three hours** to appear — empty is expected, not a gap.
- **Query wire names:** `start_date` ← `startDate`, `end_date` ← `endDate`, … `page_size` ← `pageSize`, `page` ← `page`
- **Returns:** `SearchResponse` — **no auto-pager**. `TransactionDetails`, `Page`, `TotalItems`, `TotalPages`, `LastRefreshedDatetime`, `Links`
  - Each `TransactionDetails.TransactionInfo` (`TransactionInformation`): `TransactionId`, `PaypalReferenceId`, `PaypalReferenceIdType`, `TransactionEventCode`, `TransactionInitiationDate`, `TransactionAmount`, `FeeAmount`, `TransactionStatus` (string, not an enum), `InvoiceId`, `CustomField`, …
- **Full pagination:** `pageSize: 100`, `page: 1`, `totalRequired` N/A (totals are on the response). Loop `page = 1..TotalPages`. Map: “Pagination: none (only `page`, no `perPage`)”.
- **Join to eShop:** `InvoiceId` / `CustomField` / `PaypalReferenceId` vs persisted PayPal order / capture / refund ids and `PurchaseUnitRequest.InvoiceId`/`CustomId`.
- **Error:** `SdkException<RawError>` **Case B** (the only Case B op in this SDK) · `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` — **no** `TryGetError`
- **Cite:** `operations/TransactionSearch.md`, `records-2-Pa-Ve.md`, `Api/TransactionSearch.cs` XML for 31-day / seconds-required / 3-hour lag

⚠ Step 8 (reconciliation) — list ops have many optional params with **no C# default**; positional calls mis-bind. Use **named arguments**. **MUST load `dotnet-calling-endpoints`**.

---

### Enums actually set or switched on

All `PayPalServerSdk.Models.Enums`. Members: `CSharpName (wire)`.

| Enum | Set / switch | Members used |
|---|---|---|
| `CheckoutPaymentIntent` | **set** on create | `Authorize (AUTHORIZE)` — do not set `Capture` |
| `OrderStatus` | **switch** | `Created`, `Saved`, `Approved`, `Voided`, `Completed`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** → fail closed |
| `AuthorizationStatus` | **switch** | `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending` |
| `CaptureStatus` | **switch** | `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed` |
| `RefundStatus` | **switch** | `Cancelled`, `Failed`, `Pending`, `Completed` |
| `CardBrand` | **read** (never send PAN brand required) | `Visa (VISA)`, `Mastercard`, `Discover`, `Amex`, … `Unknown` |
| `OrdersCardVerificationMethod` | **do not set on AuthorizeOrder** | `AvsCvv (AVS_CVV)` XML: “Places a temporary hold on the card to ensure its validity” — that is a verification hold, not a payment authorization. Live authorize with `Attributes.Verification.Method = AvsCvv` returns **400 `INVALID_PARAMETER_VALUE`**. `CardVerification.Method` defaults to `ScaWhenRequired` and has no `JsonIgnore` when null, so any `Verification` object serializes a method. **Omit `CardRequest.Attributes` entirely** on create/authorize card payloads. AVS/CVV still return on `ProcessorResponse`. |
| `PaymentInitiator` | **set** on vaulted pay | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | **set** | `OneTime`, `Unscheduled` (and `Recurring` unused) |
| `StoredPaymentSourceUsageType` | **set** | `Subsequent` (`First` only with `Customer`) |
| `VaultStatus` / `PaymentTokenStatus` | **read** if present | vault: `Vaulted`, `Created`, `Approved`; setup-token (unused): includes `PayerActionRequired` |
| `ProcessorResponseCode` | **read** on decline | large StringEnum on `ProcessorResponse.ResponseCode` — not listed in full here; switch/log the member or `FromValue` |
| `AvsCode` / `CvvCode` | **read** | on `ProcessorResponse` |
| `PayPalReferenceIdType` | **read** on recon | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `LinkHttpMethod` | **read** | on `LinkDescription.Method` |

Cite: `map/models/enums.md`.

---

### Idempotency (`PayPal-Request-Id`)

| Call | Parameter | Header | Notes |
|---|---|---|---|
| `CreateOrder` | `payPalRequestId` | `PayPal-Request-Id` | Mandatory when payment_source present (XML). |
| `AuthorizeOrder` | `payPalRequestId` | same | Pass the pay-endpoint idempotency key (or a stable derivative). |
| `CaptureAuthorizedPayment` | `payPalRequestId` | same | Stored 45 days (XML). Fulfil double-click key. |
| `VoidPayment` | `payPalRequestId` | same | Cancel double-click. |
| `ReauthorizePayment` | `payPalRequestId` | same | |
| `RefundCapturedPayment` | `payPalRequestId` | same | **Caller-supplied refund key.** Same key must not refund twice. |
| `CreatePaymentToken` | `payPalRequestId` | same | Stored 3 hours (XML). |

Default `prefer` is `"return=minimal"` — always override to `"return=representation"` when reading amounts/ids/status.

**Application-level idempotency is still required:** persist PayPal order / authorization / capture / refund ids and short-circuit when the resource is already in the desired terminal state (`AuthorizationStatus.Captured` / capture id present / refund row for that key).

**UNVERIFIED (source):** generated `Api/*.cs` also sends `Idempotency-Key: {Guid.NewGuid()}` on these POSTs. That header is **not** caller-controllable. Whether PayPal additionally keys off it (and thus weakens `PayPal-Request-Id`) cannot be confirmed from the SDK. Do not skip application-level idempotency.

---

### Response envelopes

None of the in-scope success types wrap the payload in a single `Data`/`Result` field. Read properties directly on:

- `Order` / `OrderAuthorizeResponse`
- `PaymentAuthorization` / `CapturedPayment` / `Refund`
- `PaymentTokenResponse` / `CustomerVaultPaymentTokensResponse`
- `SearchResponse`

Authorization after order-authorize: **one level down** `PurchaseUnits[0].Payments.Authorizations[0]`. Capture after payments-capture: **top-level** `CapturedPayment`.

---

### Errors — types, HTTP status, body, issue codes

**Thrown type:** `PayPalServerSdk.Core.Exceptions.SdkException<TError>` — public member **`Error` only**. **No `StatusCode` on the exception.** No `…Result` variants exist.

**Case A body** (`PayPalServerSdk.Models.Error` for Orders/Payments; `Error1` for Vault):

| Field | Wire | Type |
|---|---|---|
| `Name` | `name` | `string !req` |
| `Message` | `message` | `string !req` |
| `DebugId` | `debug_id` | `string !req` |
| `Details` | `details` | `IReadOnlyList<ErrorDetails>` / `ErrorDetails1` |
| `Links` | `links` | HATEOAS |

`ErrorDetails` / `ErrorDetails1`: `Issue (issue): string !req`, `Description (description): string?`, `Field`, `Value`, `Location` default `"body"`.

**HTTP status on Case A:** when `TryGetError` / `TryGetError1` succeeds, status was used internally to pick the parser and is **not** copied onto `Error`. `TryGetRawError` is only the **fallback** for statuses **not** in that operation’s typed list (then `RawError.StatusCode` exists). `TryGetNoContent(out RawError)` on several Payments ops is **500**.

**Case B** (`SearchTransactions`): `SdkException<RawError>` — `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<Error>()` best-effort.

**Issue codes:** the SDK has **no issue-code enum**. `Issue` is a free-form `string`. Do not hard-code a closed list from memory. Classify with:

1. `Error.Name` + `Details[].Issue` + `Description` (persist all three for operators)
2. HTTP bucket only when you have `RawError.StatusCode` (409 conflict on capture/void/refund is typed onto `TryGetError`, but the numeric 409 is not on `Error`)
3. Authorization/capture **status** enums (`Denied`, `Declined`, `Failed`)
4. `ProcessorResponse.ResponseCode` / `AvsCode` / `CvvCode` on `AuthorizationWithAdditionalData`

| Product situation | Where to look (SDK) |
|---|---|
| Already captured | Capture/void **409** via `TryGetError`; or GET auth `Status == Captured` |
| Expired / cannot renew | Reauthorize **422** `Name`/`Issue`; or `ExpirationTime`; or status `Voided`/`Denied` |
| Insufficient funds / card declined | Authorize/create **422** `Details.Issue`; and/or `AuthorizationStatus.Denied`; and/or `ProcessorResponse` |
| Duplicate request | **409** on capture/refund/void; `Name`/`Issue` strings |
| Vault token not found | `GetPaymentToken` `TryGetError1` [404]; delete 404 via `TryGetRawError`; authorize/create 422 `Issue` |

**JsonException (mandatory — opposite handling):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Every SDK call site — unions/records are `init`/`required`; unmodeled JSON is dropped. **MUST load `dotnet-models`** before building `OrderRequest` / `CardRequest` / vault models.

⚠ Tests — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`** before stubbing.

---

### Persist (PayPal-owned state)

Per eShop order: PayPal `Order.Id`; authorization `Id` + `Status` + `Amount` + `ExpirationTime`; capture `Id` + `Status` + gross `Amount` + `PaypalFee` + `NetAmount`; each refund `Id` + `Status` + `Amount` + caller idempotency key.

Per shopper: PayPal vault `Customer.Id`; each `PaymentTokenResponse.Id` + last digits / brand / expiry only.

---

## Trap notes

⚠ Step 0 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. Transport failures can retry **POST** as well as GET, so a non-idempotent write can execute more than once. **MUST load `dotnet-client-initialization`** and **`dotnet-configuration-resilience`** before wiring the client.

⚠ Step 0 (auth) — set `Oauth2` before constructing the client; BaseUrl override must be on `Server.Default.Sandbox.BaseUrl` so the token request and API calls share it. **MUST load `dotnet-authentication`**.

⚠ Steps 4–8 (calls) — named arguments only; nullable-without-default parameters (`payPalRequestId`, `body`, list filters, …) **must** be passed explicitly. Cancellation token is `ct:`. **MUST load `dotnet-calling-endpoints`**.

⚠ Steps 1 and 4 (models) — `required` members, `StringEnum<T>`, `YYYY-MM` expiry, money as strings; never log PAN/CVC. **MUST load `dotnet-models`**.

⚠ All steps (errors) — Case A vs Case B, `TryGetError` vs `TryGetError1` vs `TryGetNoContent` vs `RawError`; `JsonException` from 2xx and from failed error-body parse need opposite handling; HTTP status is missing on typed `Error`. **MUST load `dotnet-error-handling`**.

⚠ Step 8 (pagination) — `SearchTransactions` / `ListCustomerPaymentTokens` do not auto-iterate pages; `SearchTransactions` also does not span >31 days. **MUST load `dotnet-configuration-resilience`**.

⚠ Tests — **MUST load `dotnet-testing`**.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — `PayPalServerSdkClient` / `AddPayPalServerSdkClient` / `HttpClient` lifetime |
| `dotnet-authentication` | Step 0 — `Oauth2` client-credentials, environment, BaseUrl + token URL |
| `dotnet-configuration-resilience` | Step 0 retry/timeout; Step 8 pagination / date windows |
| `dotnet-calling-endpoints` | Steps 1–8 — named args, `ct:`, `prefer`, must-pass-null params |
| `dotnet-models` | Steps 1, 4 — records, StringEnum, money strings, card/vault models |
| `dotnet-error-handling` | Every call — Case A/B, JsonException 2xx vs non-2xx, no StatusCode on `SdkException` |
| `dotnet-testing` | Test doubles via `HttpClient` |

`dotnet-error-handling` always appears: the integration always writes an error boundary.

---

## Assumptions & Blockers

**Assumptions**

- Merchant sandbox already has advanced card processing + vaulting enabled (stated in the brief); nothing is pre-seeded.
- `PayPal:Currency` is a 3-letter ISO code compatible with the sandbox account.
- eShop `paymentMethodId` **is** `PaymentTokenResponse.Id`; list/delete/pay use that string as vault id / `VaultId`.
- First vault per shopper sends `Customer.MerchantCustomerId` = eShop user id; subsequent vault/list use persisted PayPal `Customer.Id`.
- Fulfilment captures the full remaining authorization (`FinalCapture = true`).
- Reconciliation `from`/`to` are ISO-8601 instants passed through as `startDate`/`endDate` (seconds required — append `:00Z` if the caller omitted seconds).

**Blockers / gaps**

1. **Browser / 3DS (product forbids round-trip).** If create/authorize returns `OrderStatus.PayerActionRequired`, PayPal is demanding a shopper challenge. Do **not** follow HATEOAS approve/payer-action links and do **not** set `CardExperienceContext.ReturnUrl`/`CancelUrl`. Fail the pay call. **Do not** set `CardAttributes.Verification.Method = AvsCvv` on `AuthorizeOrder` (or on create card) — live **400 `INVALID_REQUEST` / `INVALID_PARAMETER_VALUE`**. Omit `CardRequest.Attributes`. AVS/CVV still arrive on `AuthorizationWithAdditionalData.ProcessorResponse`.
2. **`ServerEnvironment` has only `Sandbox`.** There is no Production member. In-scope for this task; a live/production target cannot be selected via this SDK’s environment enum.
3. **No typed issue-code enum** for declined / expired / duplicate / token-not-found — only `Error.Name` + `Details[].Issue` strings + status enums + `ProcessorResponse`.
4. **Reauthorize of direct-card holds is UNVERIFIED** (map: “PayPal account payment”). After 30 days the map says reauthorize is the wrong call; the SDK exposes no dedicated “renew expired card authorization” operation. Surface `ReauthorizePayment` errors as operator-actionable; do not invent a substitute API on this sheet.
5. **`SdkException<T>` does not expose HTTP status** when Case A `TryGetError`/`TryGetError1` succeeds. Operators get `Name`/`Message`/`DebugId`/`Issue`, not a status integer, unless the status hit `TryGetRawError` / `TryGetNoContent`.
6. **`SearchTransactions` max range 31 days per request** — not a missing API; the implementer must chunk. Seconds required on timestamps.
7. **PCI:** `CardRequest` XML states passing PAN/CVV via the API requires PCI SAQ D. Hosted fields are out of this SDK’s operations list (no hosted-fields controller). Direct card is in-scope only because the brief enables it on the merchant account.

No other in-scope capability is missing from the SDK: authorize, capture, void, reauthorize, refund + `payPalRequestId`, vault create/list/delete, transaction search with `page`/`TotalPages` are all present.
