# PayPal .NET SDK — eShopOnWeb contract sheet

NuGet: `AsadAli.Checkout.Sdk` (install version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`. Provenance: SDK map stamp `9653d18` / tag `v1.0.1`.

**Chosen payment path:** Orders v2 **create + authorize** (`CheckoutPaymentIntent.Authorize`), then Payments v2 **capture / void / reauthorize / refund** by authorization id / capture id. There is **no** Payments “create authorization from raw/vaulted card” operation — `client.Payments` only acts on an existing authorization/capture. Raw card and vaulted card both go on `payment_source.card` (`CardRequest` PAN fields **or** `vault_id`).

---

## Scope & sequence

| Step | App endpoint | SDK operations | Persist |
|---|---|---|---|
| 0 | Startup / DI | Construct `PayPalServerSdkClient` from `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, optional `PayPal:BaseUrl` | — |
| 1 | `POST /api/orders/{orderId}/pay` | `Orders.CreateOrder` then `Orders.AuthorizeOrder`. On idempotent replay, skip if a PayPal authorization id is already stored for this eShop order. | PayPal order id, authorization id, `AuthorizationStatus`, `expiration_time`, amount |
| 2 | `POST /api/orders/{orderId}/fulfil` | `Payments.GetAuthorizedPayment` (staleness). If honor period expired and still renewable → `Payments.ReauthorizePayment` (persist **returned** auth id). Then `Payments.CaptureAuthorizedPayment`. | Capture id, `CaptureStatus`, captured amount, `paypal_fee`, `net_amount` |
| 3 | `POST /api/orders/{orderId}/cancel` (pre-fulfil) | `Payments.VoidPayment` | Authorization status `VOIDED` |
| 4 | `POST /api/orders/{orderId}/refunds` | `Payments.RefundCapturedPayment` with caller idempotency key as `payPalRequestId`. Partial vs full by optional `amount`. Never refund more than captured (enforce locally **and** handle 422). | Refund id, `RefundStatus`, refunded amount, remaining refundable |
| 5 | `POST /api/payment-methods` | `Vault.CreatePaymentToken` with raw card (no setup-token / no browser). | Payment-token id, PayPal `customer.id` (if returned), `merchant_customer_id`, brand, last4, expiry — **never PAN** |
| 6 | List saved cards | Serve from **our** shopper→token store. Optional refresh: `Vault.GetPaymentToken` / `Vault.ListCustomerPaymentTokens`. | — |
| 7 | `DELETE /api/payment-methods/{paymentMethodId}` | `Vault.DeletePaymentToken` then drop our row | — |
| 8 | `GET /api/reconciliation?from=&to=` | `TransactionSearch.SearchTransactions` — loop **every page** | Match on `invoice_id` / `custom_field` (set to eShop order id at create/capture) vs `transaction_id` |

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

**No-throw `…Result` variants: absent** on every operation below. All calls throw. (`sdk-map.md`)

**`prefer`:** default `"return=minimal"` returns only id, status, HATEOAS links. Pass **`prefer: "return=representation"`** on create/authorize/capture/reauthorize/void/refund so nested payments, fees, and expiration are present. (`Api/Orders.cs`, `Api/Payments.cs`)

### Client construction & auth

| Fact | Value | Cite |
|---|---|---|
| Constructor | `PayPalServerSdk.PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` — SDK does **not** own `HttpClient` | `sdk-map.md` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` — registers the client as **singleton** via unnamed `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Options type | `PayPalServerSdk.PayPalServerSdkClientOptions` | `PayPalServerSdkClientOptions.cs` |
| Options members | `Environment: PayPalServerSdk.Servers.ServerEnvironment` (default `ServerEnvironment.Default()` = `Sandbox`); `Retry: PayPalServerSdk.Core.Configuration.RetryOptions`; `Logging: LoggingOptions`; `Server: PayPalServerSdk.ServerOptions`; `Oauth2: PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?`; `Oauth2TokenStrategy: PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `sdk-map.md`, `PayPalServerSdkClientOptions.cs` |
| Environment enum | `PayPalServerSdk.Servers.ServerEnvironment` : `StringEnum<ServerEnvironment>`. **Only member:** `Sandbox` (wire `"Sandbox"`). `Default()` → `Sandbox`. **No Live/Production member.** | `Servers/ServerEnvironment.cs` |
| Auth credentials | `options.Oauth2 = new OAuth2ClientCredentials { ClientId (required string), ClientSecret (required string), Scope (string?) }` — map `PayPal:ClientId` / `PayPal:ClientSecret`. Leave `Scope` unset. | `OAuth2ClientCredentials.cs` |
| Token URL | Default strategy POSTs `grant_type=client_credentials` to **`{BaseUrl}/v1/oauth2/token`** resolved via `server.Default("/v1/oauth2/token")` — **same server BaseUrl as every API call** | `AuthSchemes.cs` |
| Server override | `PayPalServerSdk.ServerOptions.Default` → `PayPalServerSdk.Servers.DefaultOptions`; `Default.Sandbox` → nested `SandboxOptions`; `Sandbox.BaseUrl: string` default `"https://api-m.sandbox.paypal.com"`. When `PayPal:BaseUrl` is set, assign it **verbatim** to `options.Server.Default.Sandbox.BaseUrl` **before** constructing the client. That value is the API base for **every** call **including** the OAuth token request. | `ServerOptions.cs`, `Servers/DefaultOptions.cs` |
| Config `PayPal:Environment` | Target sandbox: `options.Environment = ServerEnvironment.Sandbox`. The enum wire value is `"Sandbox"` (capital S). | `Servers/ServerEnvironment.cs` |
| Config `PayPal:Currency` | **Not** a client option. Pass as `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode` on every amount (ISO-4217, 3 chars). | `records-1-Ac-Pa.md` |
| Retry | `RetryOptions` — all members `required`; start from `RetryOptions.Default()` or `RetryOptions.Disabled()`. | `sdk-map.md` |

### Amount representation

| Type | Namespace | Fields | Cite |
|---|---|---|---|
| `Money` | `PayPalServerSdk.Models` | `CurrencyCode (currency_code): string !req` (ISO-4217, length 3); `Value (value): string !req` (regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`, max 32). **Not** `decimal`. Integer string for JPY-like; fractional string for others (USD/EUR: two decimal places, e.g. `"10.00"`). | `Models/Money.cs`, `records-1-Ac-Pa.md` |
| `AmountWithBreakdown` | `PayPalServerSdk.Models` | `CurrencyCode (currency_code): string !req`; `Value (value): string !req` (same rules); `Breakdown (breakdown): AmountBreakdown?` | `records-1-Ac-Pa.md` |

Format the eShop order total with `PayPal:Currency` as a 2-decimal string for USD-style currencies. Amount PayPal holds **must** equal that string.

---

### Operation: `Orders.CreateOrder`

- **Controller:** `client.Orders` · **HTTP:** `POST /v2/checkout/orders` · **Cite:** `operations/Orders.md`
- **Signature:** `Task<PayPalServerSdk.Models.Order> CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderRequest body, string? prefer = "return=minimal", PayPalServerSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion` (use `null` to skip). `body` is required.
- **Idempotency:** `payPalRequestId` → header `PayPal-Request-Id`. Stored **6 hours**. XML: mandatory for single-step create with payment source (card / `vault_id`). Same key + same body = same effect. (`Api/Orders.cs`)
- **Also sent (not overridable):** header `Idempotency-Key: Guid.NewGuid()` on every invocation. (`Api/Orders.cs`)
- **Request `OrderRequest`** (`records-1-Ac-Pa.md`):

  | Set | Wire | Type | Req? |
  |---|---|---|---|
  | `Intent` | `intent` | `CheckoutPaymentIntent` | **!req** — use `CheckoutPaymentIntent.Authorize` (`AUTHORIZE`) **not** `Capture` |
  | `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnitRequest>` | **!req** (min 1, max 10) |
  | `PaymentSource` | `payment_source` | `PaymentSource?` | set for raw/vaulted card |
  | `Payer` | `payer` | `Payer?` | omit (deprecated) |
  | `ApplicationContext` | `application_context` | `OrderApplicationContext?` | omit (PayPal-wallet UX; not used for direct card) |

  **`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`): `Amount (amount): AmountWithBreakdown !req` — `CurrencyCode` + `Value` = order total; `CustomId (custom_id): string?` (max 255) — eShop order id for reconciliation; `InvoiceId (invoice_id): string?` (max 127) — same; `ReferenceId (reference_id): string?` — omit (single PU → PayPal uses `default`).

  **Raw card `PaymentSource`:** `Card (card): CardRequest?` (`records-2-Pa-Ve.md`). **`CardRequest`** (`records-1-Ac-Pa.md` / `Models/CardRequest.cs`):

  | Set | Wire | Type | Notes |
  |---|---|---|---|
  | `Name` | `name` | `string?` | cardholder name, 1–300 |
  | `Number` | `number` | `string?` | PAN, 13–19 digits. Sandbox Visa `4111111111111111` |
  | `Expiry` | `expiry` | `string?` | **`YYYY-MM` only** (length 7) |
  | `SecurityCode` | `security_code` | `string?` | 3–4 digits (CVC) |
  | `BillingAddress` | `billing_address` | `Address?` | `Address.CountryCode (country_code): string !req` (ISO-3166-1 alpha-2). Optional: `AddressLine1 (address_line_1)`, `AddressLine2 (address_line_2)`, `AdminArea2 (admin_area_2)` city, `AdminArea1 (admin_area_1)` state, `PostalCode (postal_code)` |
  | `VaultId` | `vault_id` | `string?` | **vaulted-card path** — PayPal payment-token id from step 5. Do **not** send PAN when using this |
  | `ExperienceContext` | `experience_context` | `CardExperienceContext?` | `ReturnUrl`/`CancelUrl` customize **3DS approval**. **Do not set** — this app has no browser challenge round-trip |

  **Vaulted card:** `PaymentSource.Card = new CardRequest { VaultId = storedPaymentTokenId }`. Do **not** use `PaymentSource.Token` (`Token.Type` is only `TokenType.BillingAgreement`).

- **Response envelope:** `Order` itself (no wrapper). Read (`records-1-Ac-Pa.md`):

  | Read | Wire | Type |
  |---|---|---|
  | `Id` | `id` | `string?` — persist as PayPal order id |
  | `Status` | `status` | `OrderStatus?` |
  | `Intent` | `intent` | `CheckoutPaymentIntent?` |
  | `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnit>?` |
  | `PurchaseUnits[i].Payments` | `payments` | `PaymentCollection?` |
  | `PurchaseUnits[i].Payments.Authorizations` | `authorizations` | `IReadOnlyList<AuthorizationWithAdditionalData>?` |
  | `PaymentSource.Card` | `payment_source.card` | `CardResponse?` — `LastDigits`, `Brand`, `Expiry` (no PAN) |
  | `Links` | `links` | `IReadOnlyList<LinkDescription>?` |

- **Error:** Case A `SdkException<PayPalServerSdk.Errors.CreateOrderError>` · `TryGetError(out PayPalServerSdk.Models.Error)` **[400, 401, 422]** · `TryGetRawError(out RawError)` fallback. (`operations/Orders.md`)
- **Pagination:** none.

If `Status == OrderStatus.PayerActionRequired` after create → **3DS/browser required** — fail; do not follow approve links.

---

### Operation: `Orders.AuthorizeOrder`

- **Controller:** `client.Orders` · **HTTP:** `POST /v2/checkout/orders/{id}/authorize` · **Cite:** `operations/Orders.md`
- **Signature:** `Task<PayPalServerSdk.Models.OrderAuthorizeResponse> AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalRequestId`, `payPalClientMetadataId`, `payPalAuthAssertion`, `body`.
- **`id`:** PayPal **order** id from `CreateOrder` (not authorization id).
- **Idempotency:** `payPalRequestId` → `PayPal-Request-Id` (6 hours). Distinct key from CreateOrder. Plus unoverridable `Idempotency-Key: Guid.NewGuid()`. (`Api/Orders.cs`)
- **Request `OrderAuthorizeRequest`:** `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` — `Card (card): CardRequest?`, `Token (token): Token?`. Pass `body: null` when card/`vault_id` was already on CreateOrder; otherwise same `CardRequest` shape as create.
- **Response envelope:** `OrderAuthorizeResponse` (same field names as `Order`). Authorization id path:

  `response.PurchaseUnits[0].Payments.Authorizations[0].Id` (`id`)

  Also persist: `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?` (RFC 3339, seconds required), `CreateTime (create_time)`, `ProcessorResponse` (avs/cvv/response_code). (`records-1-Ac-Pa.md` `AuthorizationWithAdditionalData`)

- **Error:** Case A `SdkException<AuthorizeOrderError>` · `TryGetError(out Error)` **[400, 401, 403, 404, 422, 500]** · `TryGetRawError` fallback. (`operations/Orders.md`)
- **422 / declined:** read `Error.Name` + `Error.Details[i].Issue` (e.g. match `INSTRUMENT_DECLINED` **UNVERIFIED** which of `name` vs `details[].issue` the live body uses — extract best-effort from both, fall back to `Error.Message`).

If `Status == OrderStatus.PayerActionRequired` → same 3DS blocker as create.

---

### Operation: `Payments.GetAuthorizedPayment` (staleness check)

- **HTTP:** `GET /v2/payments/authorizations/{authorization_id}` · **Cite:** `operations/Payments.md`
- **Signature:** `Task<PayPalServerSdk.Models.PaymentAuthorization> GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalAuthAssertion`.
- **Returns:** `PaymentAuthorization` — `Id`, `Status`, `Amount`, `ExpirationTime`, `CreateTime`, `UpdateTime`. (`records-2-Pa-Ve.md`)
- **Error:** Case A `SdkException<GetAuthorizedPaymentError>` · `TryGetError(out Error)` **[401, 403, 404]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError` fallback.

**Stale / expired / cannot reauthorize (grounded in operation notes + model XML, `operations/Payments.md`, `Models/ReauthorizeRequest.cs`):**

| Condition | Action |
|---|---|
| `Status` is `Captured` / `PartiallyCaptured` / `Voided` / `Denied` | Do not reauthorize. Captured → skip to capture-id path; Voided/Denied → operator-actionable error |
| Now still inside the **3-day honor period** (`CreateTime` + 3 days, or `ExpirationTime` still in the future **and** age ≤ 3 days) | Capture directly |
| Honor period over, age **4–29 days** from original `CreateTime` | `ReauthorizePayment` then capture |
| **≥ 30 days** since original authorization `CreateTime` | **Cannot reauthorize.** SDK: “you must create an authorized payment instead”. This app must **not** silently re-hold the shopper’s card — surface an operator-actionable error |
| `ReauthorizePayment` throws 422 (`TryGetError`) | Cannot renew — operator-actionable error; persist `Error.Name` / `Details[].Issue` / `Message` |

Operation notes also say multiple reauthorizations are allowed within 29 days; `ReauthorizeRequest` XML says “only once”. Treat a 422 as the source of truth for “cannot renew”.

---

### Operation: `Payments.ReauthorizePayment`

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/reauthorize` · **Cite:** `operations/Payments.md`
- **Signature:** `Task<PayPalServerSdk.Models.PaymentAuthorization> ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalRequestId`, `payPalAuthAssertion`, `body`.
- **Idempotency:** `payPalRequestId` → `PayPal-Request-Id` (**45 days**). Plus `Idempotency-Key: Guid.NewGuid()`. (`Api/Payments.cs`)
- **Request `ReauthorizeRequest`:** `Amount (amount): Money?` — **only** supported body field. Pass original hold amount (same `currency_code`/`value`) or `body: null`. (`records-2-Pa-Ve.md`)
- **Response:** `PaymentAuthorization` — persist **returned** `Id` (replace stored auth id if different), `Status`, `ExpirationTime` (new 3-day honor period).
- **Error:** Case A `SdkException<ReauthorizePaymentError>` · `TryGetError(out Error)` **[400, 401, 403, 404, 422]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError` fallback. **422 = cannot renew** (read `Name`/`Details[].Issue`).

---

### Operation: `Payments.CaptureAuthorizedPayment` (fulfil)

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/capture` · **Cite:** `operations/Payments.md`
- **Signature:** `Task<PayPalServerSdk.Models.CapturedPayment> CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body`.
- **Which id:** **authorization id** (not PayPal order id, not eShop order id).
- **Idempotency:** `payPalRequestId` → `PayPal-Request-Id` (**45 days**). Stable per eShop order. Plus `Idempotency-Key: Guid.NewGuid()`. Local: if capture id already stored with `CaptureStatus.Completed`, do not call again.
- **Request `CaptureRequest`** (`records-1-Ac-Pa.md`):

  | Set | Wire | Type | Notes |
  |---|---|---|---|
  | `Amount` | `amount` | `Money?` | **Optional.** Omit for full remaining authorized amount; or send order-total `Money` |
  | `FinalCapture` | `final_capture` | `bool?` default `false` | Set **`true`** (single capture of the hold) |
  | `InvoiceId` | `invoice_id` | `string?` | eShop order id (feeds transaction search) |

- **Response envelope:** `CapturedPayment` (no wrapper). Fee / net path (`records-1-Ac-Pa.md`, `SellerReceivableBreakdown`):

  | Read | Wire | Type |
  |---|---|---|
  | `Id` | `id` | capture id — persist |
  | `Status` | `status` | `CaptureStatus?` |
  | `Amount` | `amount` | `Money?` — **captured amount** (`currency_code`/`value`) |
  | `SellerReceivableBreakdown.GrossAmount` | `seller_receivable_breakdown.gross_amount` | `Money !req` |
  | `SellerReceivableBreakdown.PaypalFee` | `seller_receivable_breakdown.paypal_fee` | `Money?` — **PayPal fee** |
  | `SellerReceivableBreakdown.NetAmount` | `seller_receivable_breakdown.net_amount` | `Money?` — **net proceeds** |
  | `CreateTime` / `UpdateTime` | `create_time` / `update_time` | RFC 3339 |

  Breakdown is **not** populated when capture `Status` is `Pending`. (`SellerReceivableBreakdown` summary)

- **Error:** Case A `SdkException<CaptureAuthorizedPaymentError>` · `TryGetError(out Error)` **[400, 401, 403, 404, 409, 422]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError` fallback. **409** = conflict (already captured / not capturable) — read `Name`/`Details[].Issue`. If we already stored a completed capture, treat as idempotent success without calling.

Optional follow-up: `Payments.GetCapturedPayment(string captureId, string? payPalMockResponse, …)` returns the same `CapturedPayment` if fulfil used minimal prefer by mistake. Error: `GetCapturedPaymentError` `TryGetError` **[401, 403, 404]**, `TryGetNoContent` **[500]**.

---

### Operation: `Payments.VoidPayment` (cancel-before-fulfil)

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/void` · **Cite:** `operations/Payments.md`
- **Signature:** `Task<PayPalServerSdk.Models.PaymentAuthorization> VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId`. **No body.**
- **Which id:** **authorization id**. Notes: cannot void a fully captured authorization.
- **Idempotency:** `payPalRequestId` → `PayPal-Request-Id` (45 days). Plus `Idempotency-Key: Guid.NewGuid()`.
- **Response:** `PaymentAuthorization` — expect `Status == AuthorizationStatus.Voided`.
- **Error:** Case A `SdkException<VoidPaymentError>` · `TryGetError(out Error)` **[401, 403, 404, 409, 422]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError` fallback. **409** = already voided / already captured.

---

### Operation: `Payments.RefundCapturedPayment`

- **HTTP:** `POST /v2/payments/captures/{capture_id}/refund` · **Cite:** `operations/Payments.md`
- **Signature:** `Task<PayPalServerSdk.Models.Refund> RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body`.
- **Which id:** **capture id** (not authorization id, not order id).
- **Idempotency:** caller-supplied key → **`payPalRequestId`** → header `PayPal-Request-Id` (**45 days**). Same key must not refund twice; **distinct keys** for two partials of the same capture are legitimate. Plus unoverridable `Idempotency-Key: Guid.NewGuid()`. (`Api/Payments.cs`)
- **Request `RefundRequest`:** XML: full refund = empty body (`body: null` or new `RefundRequest` with no `Amount`); partial = `Amount (amount): Money?` with currency + value. Also `CustomId (custom_id)`, `InvoiceId (invoice_id)`, `NoteToPayer (note_to_payer)` optional. (`records-2-Pa-Ve.md`)
- **Response `Refund`:** `Id (id)`, `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown.TotalRefundedAmount (total_refunded_amount): Money?`, `CreateTime`. Persist refund ids + status. (`records-2-Pa-Ve.md`)
- **Error:** Case A `SdkException<RefundCapturedPaymentError>` · `TryGetError(out Error)` **[400, 401, 403, 404, 409, 422]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError` fallback. **422** = insufficient refundable amount (match `Name`/`Details[].Issue` best-effort). **409** = duplicate/conflict under idempotency.

Local cap: remaining = captured `Amount.Value` minus sum of completed refunds. Refuse a partial that exceeds remaining **before** calling. Capture `Status` `Refunded` / `PartiallyRefunded` is confirmatory.

Optional: `Payments.GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, …)` → `Refund`. Error `GetRefundError` `TryGetError` **[401, 403, 404]**.

---

### Operation: `Vault.CreatePaymentToken` (save card — chosen vault path)

**Path pick:** `CreatePaymentToken` with `PaymentTokenRequestCard` (PAN on the request). **Not** setup-token (setup card has `VerificationMethod` SCA + `ExperienceContext` 3DS URLs). **Not** save-from-transaction. Direct card, no browser. (`operations/Vault.md`, `records-2-Pa-Ve.md`)

- **HTTP:** `POST /v3/vault/payment-tokens`
- **Signature:** `Task<PayPalServerSdk.Models.PaymentTokenResponse> CreatePaymentToken(string? payPalRequestId, PayPalServerSdk.Models.PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalRequestId`.
- **Idempotency:** `payPalRequestId` → `PayPal-Request-Id` (**3 hours**). Plus `Idempotency-Key: Guid.NewGuid()`. (`Api/Vault.cs`)
- **Request `PaymentTokenRequest`:** `Customer (customer): Customer?` — set `MerchantCustomerId (merchant_customer_id)` to our shopper id (and `Id (id)` if we already have a PayPal customer id); `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` → `Card (card): PaymentTokenRequestCard?`.

  **`PaymentTokenRequestCard`:** `Name (name)`, `Number (number)` 13–19 digits, `Expiry (expiry)` `YYYY-MM`, `SecurityCode (security_code)` 3–4 digits, `Brand (brand): CardBrand?` omit, `BillingAddress (billing_address): Address?` (`CountryCode` !req). **No** `vault_id` / experience_context on this type.

- **Response `PaymentTokenResponse`:** persist `Id (id)` — **this is the token later passed as `CardRequest.VaultId`**. Safe descriptors: `PaymentSource.Card` → `CardPaymentTokenEntity`: `Brand (brand): CardBrand?`, `LastDigits (last_digits)` (not PAN), `Expiry (expiry)` `YYYY-MM`, `Name (name)`, `Type (type): CardType?`. Also persist `Customer.Id` / `Customer.MerchantCustomerId` if present. (`records-2-Pa-Ve.md`, `records-1-Ac-Pa.md`)
- **Error:** Case A `SdkException<CreatePaymentTokenError>` · `TryGetError1(out Error1)` **[400, 403, 404, 422, 500]** · `TryGetRawError` fallback. Vault errors use **`Error1` / `ErrorDetails1`**, not `Error`. (`operations/Vault.md`)

Client XML: Vault API “*Available in the US only.*” (`PayPalServerSdkClient.cs`)

---

### Operation: `Vault.GetPaymentToken` (optional refresh)

- **HTTP:** `GET /v3/vault/payment-tokens/{id}`
- **Signature:** `Task<PaymentTokenResponse> GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns:** same `PaymentTokenResponse` (safe card view + verification_status).
- **Error:** `SdkException<GetPaymentTokenError>` · `TryGetError1(out Error1)` **[403, 404, 422, 500]**.

**List saved cards:** persist our own shopper → token-id + brand/last4/expiry at save time and serve the list from that store. Calling PayPal is **not** required to list. Use `GetPaymentToken` only to refresh one method. Use `ListCustomerPaymentTokens` only if we stored PayPal’s `customer.id`.

---

### Operation: `Vault.ListCustomerPaymentTokens` (optional)

- **HTTP:** `GET /v3/vault/payment-tokens` · **Cite:** `operations/Vault.md`
- **Signature:** `Task<CustomerVaultPaymentTokensResponse> ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query:** `customer_id` ← `customerId` (PayPal vault customer id, **not** eShop user id), `page_size`, `page`, `total_required`.
- **Returns:** `TotalItems`, `TotalPages`, `PaymentTokens` (`IReadOnlyList<PaymentTokenResponse>?`). Page yourself if `total_pages` > 1 (`page` / `pageSize`; map: “Pagination: none (only `page`, no `perPage`)”).
- **Error:** `SdkException<ListCustomerPaymentTokensError>` · `TryGetError1(out Error1)` **[400, 403, 500]**.

---

### Operation: `Vault.DeletePaymentToken`

- **HTTP:** `DELETE /v3/vault/payment-tokens/{id}` · **Cite:** `operations/Vault.md`
- **Signature:** `Task DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `id` = payment-token id.
- **Returns:** `void`.
- **Error:** Case A `SdkException<DeletePaymentTokenError>` · `TryGetError1(out Error1)` **[400, 403, 500]** · `TryGetRawError` fallback. (404 is **not** in the typed accessor list — it lands on `TryGetRawError` with `RawError.StatusCode`.)

Then delete our shopper→token row so it cannot be used on pay.

---

### Operation: `TransactionSearch.SearchTransactions`

- **Controller:** `client.TransactionSearch` · **HTTP:** `GET /v1/reporting/transactions` · **Cite:** `operations/TransactionSearch.md`, `Api/TransactionSearch.cs`
- **Signature:** `Task<PayPalServerSdk.Models.SearchResponse> SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly (null to skip):** `transactionId`, `transactionType`, `transactionStatus`, `transactionAmount`, `transactionCurrency`, `paymentInstrumentType`, `storeId`, `terminalId`.
- **Date params:** `startDate` / `endDate` are **`string`**, query `start_date` / `end_date`. Format: RFC 3339 / Internet date-time; **seconds required**, fractional optional (e.g. `2026-08-01T00:00:00Z`). **Maximum range 31 days** — split `from`/`to` into ≤31-day windows. (`Api/TransactionSearch.cs`)
- **Pagination:** **no SDK auto-pager.** Use `page` (default 1) + `pageSize` (default 100). Response: `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Links`. Loop `page = 1 .. TotalPages` (or until a page returns no `transaction_details`). Cover the **whole** range, not the first page. (`operations/TransactionSearch.md`, `Models/SearchResponse.cs`)
- **Lag:** “maximum of three hours for executed transactions to appear”. Empty `transaction_details` is legitimate. Lists previous three years.
- **`fields`:** default `"transaction_info"` is enough for matching. `"all"` adds payer/shipping/cart. Matching fields live on `SearchResponse.TransactionDetails[i].TransactionInfo` (`TransactionInformation`, `records-2-Pa-Ve.md` / `Models/TransactionInformation.cs`):

  | Read | Wire | Use |
  |---|---|---|
  | `TransactionId` | `transaction_id` | PayPal txn id |
  | `PaypalReferenceId` | `paypal_reference_id` | related id |
  | `TransactionAmount` | `transaction_amount` | `Money?` |
  | `FeeAmount` | `fee_amount` | `Money?` |
  | `TransactionStatus` | `transaction_status` | **string** (not an enum): `D` denied, `P` pending, `S` success, `V` reversed/refunded |
  | `TransactionInitiationDate` / `TransactionUpdatedDate` | `transaction_initiation_date` / `transaction_updated_date` | RFC 3339 |
  | `InvoiceId` | `invoice_id` | match eShop order (we set `invoice_id` on purchase unit / capture) |
  | `CustomField` | `custom_field` | match eShop order (we set `custom_id`) |
  | `TransactionEventCode` | `transaction_event_code` | e.g. T0001 |

- **Error:** Case B **`SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>`** — **the only Case B operation in this SDK.** No `TryGet…`. Read `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`. (`operations/TransactionSearch.md`, `sdk-map.md`)

Call with **named arguments**. Pass `fields: "transaction_info"` (or `"all"`), `pageSize: 100`, increment `page`.

---

### Error payload shapes (all Case A Orders/Payments)

`PayPalServerSdk.Models.Error` (`records-1-Ac-Pa.md`): `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`, `Links`.

`ErrorDetails`: `Issue (issue): string !req`, `Description (description): string?`, `Field (field): string?`, `Value (value): string?`, `Location (location): string?` default `"body"`.

Vault Case A uses `Error1` / `ErrorDetails1` (same field names; links are `ErrorLinkDescription` with optional `Rel`).

`SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`) exposes **only** `.Error` — **no** `StatusCode` on the exception. HTTP status is implied by which accessor matched, or `RawError.StatusCode` on fallback / Case B / `TryGetNoContent`.

| Situation | How it surfaces |
|---|---|
| 422 card declined / `INSTRUMENT_DECLINED` | Create/Authorize `TryGetError` (422 in map). Switch on `Error.Name` and `Details[].Issue` (live field **UNVERIFIED** — best-effort both, else `Message`) |
| Expired / not renewable auth | Reauthorize `TryGetError` 422; or local ≥30-day rule |
| Already captured | Capture `TryGetError` **409** (and 422). Also `AuthorizationStatus.Captured` on GET |
| Already voided | Void `TryGetError` **409** |
| Insufficient refundable | Refund `TryGetError` **422** |
| Auth failures | 401 on `TryGetError` / Case B `StatusCode` |

---

### Enums in scope (`PayPalServerSdk.Models.Enums`, `StringEnum<T>` — **not** C# enums)

Construct with static members or `Type.FromValue("WIRE")`. Compare to static members, not raw strings. Cite: `map/models/enums.md`.

| Enum | Members (C# (wire)) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, **`Authorize (AUTHORIZE)`** ← pay flow |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, `Solo (SOLO)`, `Jcb (JCB)`, `Star (STAR)`, `Delta (DELTA)`, `Switch (SWITCH)`, `Maestro (MAESTRO)`, `CbNationale (CB_NATIONALE)`, `Configoga (CONFIGOGA)`, `Confidis (CONFIDIS)`, `Electron (ELECTRON)`, `Cetelem (CETELEM)`, `ChinaUnionPay (CHINA_UNION_PAY)`, `Diners (DINERS)`, `Elo (ELO)`, `Hiper (HIPER)`, `Hipercard (HIPERCARD)`, `Rupay (RUPAY)`, `Ge (GE)`, `Synchrony (SYNCHRONY)`, `Eftpos (EFTPOS)`, `CarteBancaire (CARTE_BANCAIRE)`, `StarAccess (STAR_ACCESS)`, `Pulse (PULSE)`, `Nyce (NYCE)`, `Accel (ACCEL)`, `Unknown (UNKNOWN)` |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` — on **setup-token** response, not `PaymentTokenResponse` |
| `CardVerificationStatus` | `Verified (VERIFIED)`, `Failed (FAILED)` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` — `CardVerification.Method` defaults `ScaWhenRequired` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` only — **not** for vault cards |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` — only if converting a setup token (not our path) |
| `DisbursementMode` | `Instant (INSTANT)`, `Delayed (DELAYED)` |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` — on `CardResponse.AuthenticationResult` if 3DS ran |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |

Unions: **none** in this SDK (`map/models/unions.md`).

---

### Idempotency summary

| Call | SDK param | Header | Key lifetime |
|---|---|---|---|
| CreateOrder / AuthorizeOrder | `payPalRequestId` | `PayPal-Request-Id` | 6 hours |
| Capture / Void / Reauthorize / Refund | `payPalRequestId` | `PayPal-Request-Id` | 45 days |
| CreatePaymentToken | `payPalRequestId` | `PayPal-Request-Id` | 3 hours |

“Same key” = same `PayPal-Request-Id` string on the same operation. Refund: reuse the **caller’s** key; two partials need two keys.

Every write also sends `Idempotency-Key: Guid.NewGuid()` (fresh, not in the public signature). Whether PayPal honors that header in addition to `PayPal-Request-Id` is **UNVERIFIED**. **Do not rely on the SDK header alone:** persist PayPal ids and skip Create/Authorize/Capture when already present (double-click).

---

## Trap notes

⚠ Step 0 (client registration) — `HttpClient` lifetime, DI singleton vs factory rotation, and unnamed vs named factory clients are not visible from the constructor. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 0 (auth) — credential property name, when tokens are acquired/cached, and 401 retry behaviour are not in the options type alone. **MUST load `dotnet-authentication`** before setting `Oauth2`.

⚠ Step 0 (BaseUrl / retries) — `options.Server` is per-server per-environment; assigning `Environment` after construct vs mutating `BaseUrl`; what `Retry.Timeout` actually bounds; and that `HttpMethodsToRetry` does not describe transport-failure retries on POST (authorize/capture/refund/vault). **MUST load `dotnet-configuration-resilience`** before wiring `Server`, `Retry`, or `PayPal:BaseUrl`.

⚠ Steps 1–8 (calls) — many nullable params have **no C# default** and mis-bind if passed positionally (`SearchTransactions` especially). Cancellation token is `ct`. Response types are the method return (no extra envelope field to unwrap except nested `purchase_units[].payments`). **MUST load `dotnet-calling-endpoints`** before the first `client.*` call.

⚠ Steps 1, 5 (models) — `CheckoutPaymentIntent` / `OrderStatus` / `CardBrand` are `StringEnum<T>` not C# enums; `Money.Value` is `string`; `required` members must be in the object initializer. **MUST load `dotnet-models`** before building `OrderRequest`, `CardRequest`, `Money`, or reading enums.

⚠ Steps 1–8 (errors) — Case A vs Case B differ per operation (`SearchTransactions` is Case B; vault uses `TryGetError1`/`Error1`; payments 500 is `TryGetNoContent`). `SdkException<T>` has no `StatusCode`. **MUST load `dotnet-error-handling`** before any try/catch.

⚠ Step 1–4 (retries vs money movement) — a transport failure on POST can be retried by the SDK independently of `PayPal-Request-Id`; whether a failed write can be re-sent is not settled by the signature. **MUST load `dotnet-configuration-resilience`** before capture/authorize/refund.

⚠ Tests — the test seam is the `HttpClient` argument, not internal types. **MUST load `dotnet-testing`** before stubbing PayPal.

⚠ Error boundary — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.

⚠ Error boundary — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — `PayPalServerSdkClient` / `HttpClient` / `AddPayPalServerSdkClient` |
| `dotnet-authentication` | Step 0 — `options.Oauth2` / token request |
| `dotnet-configuration-resilience` | Step 0 — `Server.Default.Sandbox.BaseUrl`, retries/timeouts; Steps 1–4 POST retry vs money movement; Step 8 pagination loop |
| `dotnet-calling-endpoints` | Steps 1–8 — named args, `ct:`, `prefer`, nullable-without-default params |
| `dotnet-models` | Steps 1, 5 — `StringEnum<T>`, `required`, `Money` strings, request records |
| `dotnet-error-handling` | Steps 1–8 — Case A/B, `TryGet*`, `JsonException` on 2xx **and** on mismatched non-2xx (both rows above) |
| `dotnet-testing` | Tests for the integration layer |

---

## Assumptions & Blockers

**Assumptions**

- Sandbox direct card: Visa `4111111111111111`, future `YYYY-MM` expiry, any CVC/name/billing address, no hosted fields.
- `PayPal:Currency` is a 3-letter ISO code used on every `Money`/`AmountWithBreakdown`.
- `PayPal:Environment` targets sandbox → `ServerEnvironment.Sandbox`.
- One purchase unit per eShop order; `custom_id` and `invoice_id` = eShop order id.
- PCI SAQ D for sending PAN/CVV in `CardRequest` / `PaymentTokenRequestCard` is accepted for this reference app (the SDK XML warns hosted fields avoid that burden).
- List payment methods is served from our DB; PayPal list/get are optional refresh.

**Blockers / gaps (do not invent workarounds)**

1. **3DS / `PAYER_ACTION_REQUIRED`:** If create or authorize returns `OrderStatus.PayerActionRequired`, a shopper-in-browser challenge is required. This SDK has no operation that completes that challenge without a browser (`CardExperienceContext` only carries return/cancel URLs). Treat that card/payment as failed/operator-actionable. Do not add an approval round-trip.
2. **No Live environment in this SDK:** `ServerEnvironment` has **only** `Sandbox`. There is no `Live`/`Production` member. Live targeting is not supported by this package surface. Custom `PayPal:BaseUrl` can point the sandbox client at another host, but that is an override, not a Live enum.
3. **No Payments “create authorization from card”:** after 30 days the SDK says to create a new authorized payment instead of reauthorizing. Doing that would be a **new** Orders create+authorize (new hold). Product rule: surface operator-actionable error rather than silently re-holding.
4. **Vault geographic limit:** client remarks say Payment Method Tokens API v3 is available in the US only.
5. **`Idempotency-Key` always random:** generated client always adds `Idempotency-Key: Guid.NewGuid()` on writes. Caller-controlled idempotency is `payPalRequestId` → `PayPal-Request-Id`. Combine with local “already have auth/capture id” guards. Effect of the extra header on PayPal’s idempotency is **UNVERIFIED**.
6. **Issue-code strings** (`INSTRUMENT_DECLINED`, already-captured, refund-exceeds-capture) are **not** SDK enums. Only HTTP status buckets and `Error.Name` / `Details.Issue` strings exist. Match best-effort; do not invent a typed issue enum.
7. **Reporting lag / 31-day window:** sandbox search can return empty; ranges longer than 31 days must be split. Not a missing operation — document and implement the loop.
8. **`prefer` default `return=minimal`:** if left at default, fee/net/`expiration_time`/authorization id nested under `payments` may be absent. Always pass `return=representation` on money-moving calls.
