# PayPal .NET SDK — eShopOnWeb contract sheet

Package: `AsadAli.Checkout.Sdk` (install version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`. Map stamp: tag `v1.0.1` / commit `9653d18`.

---

## Scope & sequence

1. **Client registration** — bind `PayPal:*` config → `PayPalServerSdkClientOptions` (OAuth2, `ServerEnvironment.Sandbox`, optional verbatim `BaseUrl`). Register via `AddPayPalServerSdkClient` or `new PayPalServerSdkClient(httpClient, options)`.
2. **POST /api/orders** — eShop order only (existing order/order-item + JWT). Persist `AwaitingPayment`. **No PayPal call.**
3. **POST /api/orders/{orderId}/pay** — `Orders.CreateOrder` (`CheckoutPaymentIntent.Authorize`) then `Orders.AuthorizeOrder` with either raw `CardRequest` or `CardRequest.VaultId`. Persist PayPal order id, authorization id/status/`ExpirationTime`. Amount string must equal eShop order total to the cent. Idempotent via stored state + `payPalRequestId`.
4. **POST /api/orders/{orderId}/fulfil** — `Payments.GetAuthorizedPayment`; if hold is stale, `Payments.ReauthorizePayment` then persist the **new** authorization id; `Payments.CaptureAuthorizedPayment` (`FinalCapture = true`). Persist capture id, `SellerReceivableBreakdown` (gross / `PaypalFee` / `NetAmount`). Idempotent. If reauthorize cannot renew, fail with operator-actionable SDK error text — do not capture, do not create a new PayPal order.
5. **POST /api/orders/{orderId}/cancel** — `Payments.VoidPayment` on the stored authorization id. Idempotent.
6. **POST /api/orders/{orderId}/refunds** — `Payments.RefundCapturedPayment` (full: `body: null`; partial: `RefundRequest.Amount`). Caller idempotency key → `payPalRequestId`. Reject when remaining refundable would go below zero.
7. **GET /api/my-orders** — shopper-scoped eShop orders + persisted PayPal ids/statuses (refresh via `GetAuthorizedPayment` / `GetCapturedPayment` / `GetRefund` if needed).
8. **GET /api/reconciliation?from&to** — `TransactionSearch.SearchTransactions` looping `page` until the whole range is consumed; join on `PurchaseUnitRequest.CustomId` / `InvoiceId` (eShop order id) vs `TransactionInformation.CustomField` / `InvoiceId`.
9. **POST /api/payment-methods** — `Vault.CreatePaymentToken` with card + `Customer.MerchantCustomerId` = shopper id. Persist token id + PayPal customer id + safe display fields only.
10. **GET /api/payment-methods** — shopper-scoped persisted tokens (optional `Vault.ListCustomerPaymentTokens` / `GetPaymentToken` refresh).
11. **DELETE /api/payment-methods/{paymentMethodId}** — `Vault.DeletePaymentToken`; drop local row so it cannot be used to pay.

**STOP / report-gap (do not implement an approval round-trip):** if `CreateOrder` / `AuthorizeOrder` / vault create returns `OrderStatus.PayerActionRequired` or `PaymentTokenStatus.PayerActionRequired`, or `Links` contains `rel` that requires a browser redirect (`approve` / payer-action). Do not set `CardExperienceContext.ReturnUrl` / `CancelUrl` to start a 3DS loop.

Persist enough PayPal-owned state that a later request can act: PayPal order id; authorization id + status + `ExpirationTime`; capture id + status + gross/fee/net; each refund id + status + amount; vault customer id + payment-token id. Never persist PAN, expiry+CVV together as full card, or `SecurityCode`. Never log those fields.

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

Operations are **throw-only** (no `…Result` variants). `prefer` defaults to `"return=minimal"` — pass `prefer: "return=representation"` on create/authorize/capture/reauthorize/void/refund so ids, amounts, and `SellerReceivableBreakdown` are present on the deserialized records.

Nullable parameters **without a C# default must be passed explicitly** (`null` to skip). Call with **named arguments**.

---

### 0. Client construction / auth / BaseUrl

| Item | Contract | Cite |
|---|---|---|
| NuGet | `AsadAli.Checkout.Sdk` (version-less) | `paypal-getting-started` |
| Client | `PayPalServerSdk.PayPalServerSdkClient` | `sdk-map.md` |
| Constructor | `PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` — `httpClient` is required | `sdk-map.md` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(this IServiceCollection, Action<PayPalServerSdkClientOptions>? configure = null)` | `sdk-map.md` |
| Options type | `PayPalServerSdk.PayPalServerSdkClientOptions` | `PayPalServerSdkClientOptions.cs` / `sdk-map.md` |

**`PayPalServerSdkClientOptions` properties** (`sdk-map.md`, `PayPalServerSdkClientOptions.cs`):

| Property | Type | Namespace |
|---|---|---|
| `Environment` | `ServerEnvironment` | `PayPalServerSdk.Servers` |
| `Retry` | `RetryOptions` | `PayPalServerSdk.Core.Configuration` |
| `Logging` | `LoggingOptions` | (options type on client options; do not guess a `using`) |
| `Server` | `ServerOptions` | `PayPalServerSdk` (repo-root type) |
| `Oauth2` | `OAuth2ClientCredentials?` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `Oauth2TokenStrategy` | `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `PayPalServerSdk.Core.Authentication.OAuth2` |

**Auth — `OAuth2ClientCredentials`** (`Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`):

| Member | Type | Required |
|---|---|---|
| `ClientId` | `string` | `required` — bind `PayPal:ClientId` / `PAYPAL_CLIENT_ID` |
| `ClientSecret` | `string` | `required` — bind `PayPal:ClientSecret` / `PAYPAL_CLIENT_SECRET` |
| `Scope` | `string?` | optional; omit for this integration |

Token request is **not** a public operation. The SDK builds it as `server.Default("/v1/oauth2/token")` using the same `Server` BaseUrl as every other call (`AuthSchemes.cs`). Scheme: HTTP Basic over client id/secret (`OAuth2ClientCredentialsStrategy.ForBasicAuthRequest`). Leave `Oauth2TokenStrategy` unset unless tests inject one.

**Environment — `PayPalServerSdk.Servers.ServerEnvironment`** (`Servers/ServerEnvironment.cs`, `sdk-map.md` *Servers & auth*):

| Member | Wire value | Notes |
|---|---|---|
| `ServerEnvironment.Sandbox` | `"Sandbox"` | **Only documented member.** `Default()` returns `Sandbox`. **No `Live` member exists in this SDK.** |

Bind `PayPal:Environment` / `PAYPAL_ENVIRONMENT` to `Sandbox`. Any other value is an Assumptions & Blockers item — do not invent a live URL.

**Custom BaseUrl (verbatim, every call including token)** (`ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Server.cs`, `AuthSchemes.cs`):

```
options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl verbatim>
```

| Type | Member | Default |
|---|---|---|
| `PayPalServerSdk.ServerOptions` | `Default` : `PayPalServerSdk.Servers.DefaultOptions` | `new()` |
| `PayPalServerSdk.Servers.DefaultOptions` | `Sandbox` : `DefaultOptions.SandboxOptions` | `new()` |
| `DefaultOptions.SandboxOptions` | `BaseUrl` : `string` | `"https://api-m.sandbox.paypal.com"` |

When `PayPal:BaseUrl` is unset, leave the default. When set, assign that string **unchanged** to `options.Server.Default.Sandbox.BaseUrl`. This is the host for Orders, Payments, Vault, TransactionSearch, **and** `POST /v1/oauth2/token`.

**Config keys**

| Config | Env | SDK mapping |
|---|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | `options.Oauth2.ClientId` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | `options.Oauth2.ClientSecret` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `options.Environment` → `ServerEnvironment.Sandbox` only |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | **not an SDK option** — pass as `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode` on every amount |
| `PayPal:BaseUrl` | (optional) | `options.Server.Default.Sandbox.BaseUrl` verbatim |

Controllers on the client: `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch` (`sdk-map.md`).

---

### 1–2. Direct card AUTHORIZE (hold, do not capture)

PayPal order + authorize happen **inside** eShop `POST /api/orders/{orderId}/pay`, not at eShop order create.

#### `Orders.CreateOrder` — `map/operations/Orders.md`

- **HTTP:** `POST /v2/checkout/orders`
- **Signature:** `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly (nullable, no default):** `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion`
- **Returns:** `PayPalServerSdk.Models.Order` (not wrapped)
- **Error:** `SdkException<PayPalServerSdk.Errors.CreateOrderError>` Case A — `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback
- **Idempotency:** `payPalRequestId` (PayPal-Request-Id). Pass a stable key per eShop pay attempt.
- **Pagination:** none

**Request `PayPalServerSdk.Models.OrderRequest`** (`records-1-Ac-Pa.md`):

| Field | Wire | Type | Required |
|---|---|---|---|
| `Intent` | `intent` | `PayPalServerSdk.Models.Enums.CheckoutPaymentIntent` | **required** — use `CheckoutPaymentIntent.Authorize` |
| `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnitRequest>` | **required** |
| `Payer` | `payer` | `Payer?` | optional — omit |
| `PaymentSource` | `payment_source` | `PaymentSource?` | optional on create; card may instead go on `AuthorizeOrder` |
| `ApplicationContext` | `application_context` | `OrderApplicationContext?` | omit (contains return/cancel URLs; we do not do browser approval) |

**`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`):

| Field | Wire | Type | Required |
|---|---|---|---|
| `Amount` | `amount` | `AmountWithBreakdown` | **required** — must equal eShop order total |
| `CustomId` | `custom_id` | `string?` | set to eShop order id (reconciliation join) |
| `InvoiceId` | `invoice_id` | `string?` | set to eShop order id (reconciliation join) |
| `ReferenceId` | `reference_id` | `string?` | optional |

**`AmountWithBreakdown`** (`records-1-Ac-Pa.md`, `Models/AmountWithBreakdown.cs`): `CurrencyCode (currency_code): string !req` (ISO-4217, length 3, from `PayPal:Currency`); `Value (value): string !req` (decimal **string**, not minor units — see Amount formatting); `Breakdown (breakdown): AmountBreakdown?` optional.

**Response `Order`** (`records-1-Ac-Pa.md`) — fields this step reads:

| Field | Wire | Type |
|---|---|---|
| `Id` | `id` | `string?` — persist as PayPal order id |
| `Status` | `status` | `OrderStatus?` — if `PayerActionRequired` → **STOP** |
| `Intent` | `intent` | `CheckoutPaymentIntent?` |
| `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnit>?` |
| `Links` | `links` | `IReadOnlyList<LinkDescription>?` — `Rel == "approve"` → **STOP** |
| `PaymentSource` | `payment_source` | `PaymentSourceResponse?` |

#### `Orders.AuthorizeOrder` — `map/operations/Orders.md`

- **HTTP:** `POST /v2/checkout/orders/{id}/authorize`
- **Signature:** `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalRequestId`, `payPalClientMetadataId`, `payPalAuthAssertion`, `body`
- **Returns:** `PayPalServerSdk.Models.OrderAuthorizeResponse` (same shape as an order; not wrapped)
- **Error:** `SdkException<AuthorizeOrderError>` Case A — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError` fallback
- **Idempotency:** `payPalRequestId`

**Request `OrderAuthorizeRequest`** (`records-1-Ac-Pa.md`): `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`

**`OrderAuthorizeRequestPaymentSource`:** `Card (card): CardRequest?` (use this). `Token (token): Token?` is **not** the vault payment-token path (`TokenType` is only `BillingAgreement`).

**One-off card — `PayPalServerSdk.Models.CardRequest`** (`records-1-Ac-Pa.md`, `Models/CardRequest.cs`):

| Field | Wire | Type | Notes |
|---|---|---|---|
| `Name` | `name` | `string?` | cardholder name |
| `Number` | `number` | `string?` | PAN 13–19 digits; test Visa `4111111111111111` |
| `Expiry` | `expiry` | `string?` | **ISO-8601 `YYYY-MM` only** (length 7, regex `^[0-9]{4}-(0[1-9]|1[0-2])$`) |
| `SecurityCode` | `security_code` | `string?` | 3–4 digit CVC; **never persist or log** |
| `BillingAddress` | `billing_address` | `Address?` | optional; `Address.CountryCode` is required if address is sent |
| `VaultId` | `vault_id` | `string?` | **omit** for one-off PAN |
| `ExperienceContext` | `experience_context` | `CardExperienceContext?` | **omit** (return/cancel would start a 3DS round-trip) |
| `StoredCredential` | `stored_credential` | `CardStoredCredential?` | omit for one-off |

**Authorization id path on `OrderAuthorizeResponse`:**

`PurchaseUnits` → `Payments` (`PaymentCollection`) → `Authorizations` (`IReadOnlyList<AuthorizationWithAdditionalData>`) → `[0].Id` / `.Status` / `.Amount` / `.ExpirationTime`

| Record | Fields used | Cite |
|---|---|---|
| `PurchaseUnit` | `Payments (payments): PaymentCollection?`, `Amount`, `CustomId` | `records-2-Pa-Ve.md` |
| `PaymentCollection` | `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` | `records-2-Pa-Ve.md` |
| `AuthorizationWithAdditionalData` | `Id (id)`, `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`, `CreateTime`, `UpdateTime`, `ProcessorResponse` | `records-1-Ac-Pa.md` |

If `Status` on the order is `PayerActionRequired`, **STOP**. Do not capture.

---

### 3. AUTHORIZE with a vaulted / saved card

Same `CreateOrder` + `AuthorizeOrder` as §1–2. On `CardRequest` set **only** `VaultId` to the PayPal payment-token id from `CreatePaymentToken` / local `paymentMethodId` mapping. Do **not** send `Number` or `SecurityCode`.

Shopper is signed-in at pay time. Optional `CardStoredCredential` (`records-1-Ac-Pa.md`): `PaymentInitiator !req`, `PaymentType !req`, `Usage` default `StoredPaymentSourceUsageType.Derived`. Suggested: `PaymentInitiator.Customer`, `StoredPaymentSourcePaymentType.Unscheduled` (or `OneTime`), `StoredPaymentSourceUsageType.Subsequent`.

`PaymentSource.Token` / `OrderAuthorizeRequestPaymentSource.Token` is `Token { Id !req, Type !req TokenType }` and `TokenType` has **only** `BillingAgreement` — **do not** put a vault payment-token id there.

Confirm the token still exists (`Vault.GetPaymentToken`) and is owned by the JWT shopper before paying. After `DeletePaymentToken`, refuse pay with that id.

---

### 4. VAULT — save / list / delete card

#### `Vault.CreatePaymentToken` — `map/operations/Vault.md`

- **HTTP:** `POST /v3/vault/payment-tokens`
- **Signature:** `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalRequestId`
- **Returns:** `PayPalServerSdk.Models.PaymentTokenResponse` (not wrapped)
- **Error:** `SdkException<CreatePaymentTokenError>` Case A — `TryGetError1(out PayPalServerSdk.Models.Error1)` [400, 403, 404, 422, 500] · `TryGetRawError` fallback
- **Idempotency:** `payPalRequestId`

**Request `PaymentTokenRequest`** (`records-2-Pa-Ve.md`):

| Field | Wire | Type | Required |
|---|---|---|---|
| `Customer` | `customer` | `Customer?` | set `MerchantCustomerId` = shopper id |
| `PaymentSource` | `payment_source` | `PaymentTokenRequestPaymentSource` | **required** |

**`Customer`:** `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?` (`records-1-Ac-Pa.md`).

**`PaymentTokenRequestPaymentSource`:** `Card (card): PaymentTokenRequestCard?`

**`PaymentTokenRequestCard`** (`Models/PaymentTokenRequestCard.cs`): `Name`, `Number` (PAN), `Expiry` (`YYYY-MM`), `SecurityCode`, `Brand`, `BillingAddress` — same formatting as `CardRequest`. Never persist Number/SecurityCode.

**Response `PaymentTokenResponse`:**

| Field | Wire | Type | Persist? |
|---|---|---|---|
| `Id` | `id` | `string?` | **yes** — this is `vault_id` / eShop `paymentMethodId` |
| `Customer` | `customer` | `CustomerResponse?` | persist `CustomerResponse.Id` (PayPal customer id) + `MerchantCustomerId` |
| `PaymentSource` | `payment_source` | `PaymentTokenResponsePaymentSource?` | safe display only |
| `Links` | `links` | `IReadOnlyList<LinkDescription>?` | no |

**Safe display — `CardPaymentTokenEntity`** (`records-1-Ac-Pa.md`): `LastDigits (last_digits)`, `Brand (brand): CardBrand?`, `Expiry (expiry)`, `Name`, `Type (type): CardType?`. **Never** a PAN.

If vault create implies payer action (`PaymentTokenStatus.PayerActionRequired` exists on **setup** tokens, not on `PaymentTokenResponse`), **STOP** rather than calling `CreateSetupToken` (that type has `ReturnUrl`/`CancelUrl`). Do not plan `CreateSetupToken` / `GetSetupToken`.

#### `Vault.GetPaymentToken` — `map/operations/Vault.md`

- **HTTP:** `GET /v3/vault/payment-tokens/{id}`
- **Signature:** `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns:** `PaymentTokenResponse`
- **Error:** `SdkException<GetPaymentTokenError>` Case A — `TryGetError1(out Error1)` [403, 404, 422, 500] · `TryGetRawError`

#### `Vault.ListCustomerPaymentTokens` — `map/operations/Vault.md`

- **HTTP:** `GET /v3/vault/payment-tokens`
- **Signature:** `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query:** `customer_id` ← `customerId` (**PayPal** vault customer id from `CustomerResponse.Id`, not the eShop user id), `page_size`, `page`, `total_required`
- **Returns:** `CustomerVaultPaymentTokensResponse`: `PaymentTokens`, `TotalItems`, `TotalPages`, `Customer` (`VaultResponseCustomer`), `Links`
- **Error:** `SdkException<ListCustomerPaymentTokensError>` Case A — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`
- **Pagination:** SDK has no pager helper. Loop `page` while `page <= TotalPages` (set `totalRequired: true` so totals are populated). Default `pageSize` is 5.

#### `Vault.DeletePaymentToken` — `map/operations/Vault.md`

- **HTTP:** `DELETE /v3/vault/payment-tokens/{id}`
- **Signature:** `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns:** `void` (`Task`)
- **Error:** `SdkException<DeletePaymentTokenError>` Case A — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`

After delete: remove the local row. Pay with that id must fail. Subsequent GET list must not include it.

---

### 5. CAPTURE authorization (fulfilment)

#### `Payments.CaptureAuthorizedPayment` — `map/operations/Payments.md`

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/capture`
- **Signature:** `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body`
- **Returns:** `PayPalServerSdk.Models.CapturedPayment` (not wrapped)
- **Error:** `SdkException<CaptureAuthorizedPaymentError>` Case A — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback
- **Idempotency:** `payPalRequestId`. Treat **409** as a conflict (duplicate capture) — recover via `GetCapturedPayment` / stored capture id, do not capture again.
- **`prefer`:** `"return=representation"` so breakdown is present.

**Request `CaptureRequest`** (`records-1-Ac-Pa.md`):

| Field | Wire | Type | Fulfilment |
|---|---|---|---|
| `Amount` | `amount` | `Money?` | omit to capture the authorized total; if sent, must equal remaining authorized amount / order total |
| `FinalCapture` | `final_capture` | `bool?` default `false` | set **`true`** |
| `InvoiceId` | `invoice_id` | `string?` | optional eShop order id |
| `NoteToPayer` / `SoftDescriptor` / `PaymentInstruction` | | | omit |

**Response `CapturedPayment` fields to persist/show:**

| Field | Wire | Type |
|---|---|---|
| `Id` | `id` | `string?` — capture id |
| `Status` | `status` | `CaptureStatus?` |
| `Amount` | `amount` | `Money?` — captured amount |
| `SellerReceivableBreakdown` | `seller_receivable_breakdown` | `SellerReceivableBreakdown?` |
| `CreateTime` / `UpdateTime` | | `string?` |

**`SellerReceivableBreakdown`** (`records-2-Pa-Ve.md`) — **not populated when capture is pending**:

| Field | Wire | Type | Use |
|---|---|---|---|
| `GrossAmount` | `gross_amount` | `Money !req` | captured gross |
| `PaypalFee` | `paypal_fee` | `Money?` | PayPal fee |
| `NetAmount` | `net_amount` | `Money?` | net proceeds |
| `PaypalFeeInReceivableCurrency` / `ReceivableAmount` / `ExchangeRate` / `PlatformFees` | | | only if present |

**UNVERIFIED:** whether `prefer=return=minimal` omits `SellerReceivableBreakdown`. Always pass representation. If breakdown is still null after a completed capture, persist capture id + `Amount` and show fee/net as unavailable — do not invent numbers.

#### `Payments.GetCapturedPayment` — `map/operations/Payments.md`

- **HTTP:** `GET /v2/payments/captures/{capture_id}`
- **Signature:** `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`
- **Returns:** `CapturedPayment`
- **Error:** `SdkException<GetCapturedPaymentError>` Case A — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`

Use after 409, and to refresh status (`PartiallyRefunded` / `Refunded`) before a new refund.

---

### 6. REAUTHORIZE / renew stale hold

#### `Payments.GetAuthorizedPayment` — `map/operations/Payments.md`

- **HTTP:** `GET /v2/payments/authorizations/{authorization_id}`
- **Signature:** `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalAuthAssertion`
- **Returns:** `PayPalServerSdk.Models.PaymentAuthorization`
- **Error:** `SdkException<GetAuthorizedPaymentError>` Case A — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError`

**`PaymentAuthorization`** (`records-2-Pa-Ve.md`): `Id`, `Status: AuthorizationStatus?`, `Amount: Money?`, `ExpirationTime (expiration_time): string?`, `StatusDetails.Reason: AuthorizationIncompleteReason?`.

**What indicates stale (map-grounded):**

- `ExpirationTime` is in the past (ISO-8601 string on the resource).
- Capture fails with **422** on `CaptureAuthorizedPayment` (`TryGetError`). The map does **not** enumerate PayPal `ErrorDetails.Issue` strings. Read `Error.Name`, `Error.Message`, `Error.DebugId`, `Error.Details[].Issue`, `Error.Details[].Description` and treat those as the operator-facing reason. **UNVERIFIED:** exact `Issue` values for “honor period expired” vs “authorization expired”.
- Operation notes (`ReauthorizePayment`): honor period ~3 days; reauthorize from day 4–29; **after 30 days you cannot reauthorize** — you would have to create a **new** authorized payment. This integration **must not** silently create a new PayPal order/charge; that is the “cannot be renewed” path.

#### `Payments.ReauthorizePayment` — `map/operations/Payments.md`

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/reauthorize`
- **Signature:** `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalRequestId`, `payPalAuthAssertion`, `body`
- **Returns:** `PaymentAuthorization` — persist the **returned `Id`** (new hold) and new `ExpirationTime`; subsequent capture/void use this id.
- **Error:** `SdkException<ReauthorizePaymentError>` Case A — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent` [500] · `TryGetRawError`
- **Idempotency:** `payPalRequestId`
- **Request `ReauthorizeRequest`:** `Amount (amount): Money?` only (`records-2-Pa-Ve.md`). Pass the original authorized amount (order total, same currency). Notes: “Supports only the `amount` request parameter.”

**Cannot renew:** `TryGetError` on 422 (or 404 if the authorization is gone). Surface `Error.Name` + `Message` + each `Details.Issue`/`Description` + `DebugId` to the operator. Do not capture. Do not void-as-success. Do not CreateOrder again.

---

### 7. VOID authorization (cancel before capture)

#### `Payments.VoidPayment` — `map/operations/Payments.md`

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/void`
- **Notes:** cannot void a fully captured authorization.
- **Signature:** `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId`
- **Returns:** `PaymentAuthorization` — expect `Status == AuthorizationStatus.Voided`
- **Error:** `SdkException<VoidPaymentError>` Case A — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError`
- **Idempotency:** `payPalRequestId`. If already voided (local state or `Status == Voided`), return success without calling again. 409 → recover via `GetAuthorizedPayment`.

---

### 8. REFUND a capture (full / partial)

#### `Payments.RefundCapturedPayment` — `map/operations/Payments.md`

- **HTTP:** `POST /v2/payments/captures/{capture_id}/refund`
- **Notes:** full refund = empty payload; partial = amount object.
- **Signature:** `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body`
- **Returns:** `PayPalServerSdk.Models.Refund` (not wrapped)
- **Error:** `SdkException<RefundCapturedPaymentError>` Case A — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError`
- **Idempotency:** caller-supplied key **must** be passed as `payPalRequestId`. 409 → `GetRefund` / stored refund id.

**Request `RefundRequest`** (`records-2-Pa-Ve.md`):

| Field | Wire | Type | Full | Partial |
|---|---|---|---|---|
| `Amount` | `amount` | `Money?` | omit / pass `body: null` | required — amount ≤ remaining refundable |
| `CustomId` / `InvoiceId` / `NoteToPayer` / `PaymentInstruction` | | | optional | optional |

**Response `Refund`:** `Id`, `Status: RefundStatus?`, `Amount: Money?`, `SellerPayableBreakdown` (`GrossAmount`, `PaypalFee`, `NetAmount`, **`TotalRefundedAmount (total_refunded_amount): Money?`**).

**Remaining refundable (no dedicated field on `CapturedPayment`):**

1. Refresh `GetCapturedPayment`. If `CaptureStatus.Refunded`, remaining = 0. If `Declined`/`Failed`/`Pending`, do not refund.
2. Remaining = captured `Amount.Value` minus sum of local + PayPal refunds with `RefundStatus.Completed` (parse decimal strings in the same currency). Prefer `SellerPayableBreakdown.TotalRefundedAmount` on the latest refund when present.
3. Reject a partial amount that exceeds remaining **before** calling PayPal; a 422 `TryGetError` is the PayPal-side backstop — surface `Details.Issue`.

#### `Payments.GetRefund` — `map/operations/Payments.md`

- **Signature:** `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalAuthAssertion`
- **Returns:** `Refund`
- **Error:** `SdkException<GetRefundError>` Case A — `TryGetError` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError`

---

### 9. Transaction search (whole date range)

#### `TransactionSearch.SearchTransactions` — `map/operations/TransactionSearch.md`

- **HTTP:** `GET /v1/reporting/transactions`
- **Signature:** `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly (nullable, no default):** `transactionId`, `transactionType`, `transactionStatus`, `transactionAmount`, `transactionCurrency`, `paymentInstrumentType`, `storeId`, `terminalId`
- **Query:** `start_date` ← `startDate`, `end_date` ← `endDate`, … `page_size` ← `pageSize`, `page` ← `page`
- **Returns:** `PayPalServerSdk.Models.SearchResponse` (not wrapped)
- **Error:** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` **Case B** (the **only** Case B op in this SDK). Accessors: `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. Catch `SdkException<RawError>`, not a typed `SearchTransactionsError`.
- **Pagination:** none (no SDK pager). `page` / `pageSize` only. **Cover the whole range:** start `page: 1`, `pageSize: 100` (max default); read `TotalPages` / `TotalItems`; increment `page` until `page > TotalPages` (or collected count ≥ `TotalItems`). Do not stop after the first page.
- **`fields`:** default `"transaction_info"` is enough for ids/amounts/dates. Pass that or `null` (then default applies). Named-args only.

**`startDate` / `endDate`:** required `string`s. Pass the query’s ISO-8601 `from` / `to` **verbatim**.

**`SearchResponse`** (`records-2-Pa-Ve.md`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `StartDate`, `EndDate`, `Page`, `TotalItems`, `TotalPages`, `LastRefreshedDatetime`, `AccountNumber`, `Links`.

**`TransactionDetails.TransactionInfo` → `TransactionInformation`** (join fields): `TransactionId`, `PaypalReferenceId`, `TransactionInitiationDate`, `TransactionUpdatedDate`, `TransactionAmount: Money?`, `FeeAmount: Money?`, `TransactionStatus: string?`, `InvoiceId`, `CustomField`, `PaymentTrackingId`.

Join to eShop: `CustomField` / `InvoiceId` vs values sent on `PurchaseUnitRequest.CustomId` / `InvoiceId`. Unmatched PayPal rows still belong in the report (PayPal’s record of the range).

Notes from the op: executed txns can take up to three hours to appear; history up to three years. Optional filters empty `ending_balance` (not used here).

---

### 10. Error types (what reaches catch)

`PayPalServerSdk.Core.Exceptions.SdkException<TError>` — public member `Error` : `TError` only (no `StatusCode` on the exception). (`Core/Exceptions/SdkException.cs`)

| Op | `TError` | Case | Accessors | HTTP mapped to typed body |
|---|---|---|---|---|
| `CreateOrder` | `CreateOrderError` | A | `TryGetError(out Error)` | 400, 401, 422 |
| `AuthorizeOrder` | `AuthorizeOrderError` | A | `TryGetError(out Error)` | 400, 401, 403, 404, 422, 500 |
| `CaptureAuthorizedPayment` | `CaptureAuthorizedPaymentError` | A | `TryGetError(out Error)` · `TryGetNoContent(out RawError)` | 400/401/403/404/409/422 · 500 raw |
| `GetAuthorizedPayment` | `GetAuthorizedPaymentError` | A | `TryGetError` · `TryGetNoContent` | 401/403/404 · 500 |
| `GetCapturedPayment` | `GetCapturedPaymentError` | A | same pattern | 401/403/404 · 500 |
| `ReauthorizePayment` | `ReauthorizePaymentError` | A | `TryGetError` · `TryGetNoContent` | 400/401/403/404/422 · 500 |
| `VoidPayment` | `VoidPaymentError` | A | `TryGetError` · `TryGetNoContent` | 401/403/404/409/422 · 500 |
| `RefundCapturedPayment` | `RefundCapturedPaymentError` | A | `TryGetError` · `TryGetNoContent` | 400/401/403/404/409/422 · 500 |
| `GetRefund` | `GetRefundError` | A | `TryGetError` · `TryGetNoContent` | 401/403/404 · 500 |
| `CreatePaymentToken` | `CreatePaymentTokenError` | A | **`TryGetError1(out Error1)`** | 400, 403, 404, 422, 500 |
| `GetPaymentToken` | `GetPaymentTokenError` | A | `TryGetError1` | 403, 404, 422, 500 |
| `ListCustomerPaymentTokens` | `ListCustomerPaymentTokensError` | A | `TryGetError1` | 400, 403, 500 |
| `DeletePaymentToken` | `DeletePaymentTokenError` | A | `TryGetError1` | 400, 403, 500 |
| `SearchTransactions` | `RawError` | **B** | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | (all error statuses) |

Namespaces: errors `PayPalServerSdk.Errors`; `Error`/`Error1` `PayPalServerSdk.Models`; `RawError`/`ApiError` `PayPalServerSdk.Core.ErrorResponse`.

**`Error`** (Orders/Payments): `Name !req`, `Message !req`, `DebugId !req`, `Details: IReadOnlyList<ErrorDetails>?`, `Links`. **`ErrorDetails`:** `Issue !req`, `Description?`, `Field?`, `Value?`, `Location?` default `"body"`.

**`Error1`** (Vault): same scalars; `Details: IReadOnlyList<ErrorDetails1>?`; `Links: IReadOnlyList<ErrorLinkDescription>?` (`Rel` **optional**).

For mapped Case A statuses, `TryGetRawError` is **not** populated (fallback only for unmapped codes). Do not expect a numeric status on `SdkException` when `TryGetError`/`TryGetError1` succeeds — several statuses share one accessor. Case B: `ex.Error.StatusCode` is the HTTP status.

Catch **per operation type** (`SdkException<CaptureAuthorizedPaymentError>` etc.). A single `catch (SdkException<Error>)` will not compile / will not match.

---

### 11. Amount formatting & currency

| Rule | Contract | Cite |
|---|---|---|
| Not minor units | `Money.Value` and `AmountWithBreakdown.Value` are **decimal strings**, max length 32, regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$` | `Models/Money.cs`, `AmountWithBreakdown.cs` |
| Currency | `CurrencyCode` **required** string, length 3 (ISO-4217). Always `PayPal:Currency` | same |
| Precision | Integer string for zero-decimal currencies (e.g. `JPY`); fractional string for others (e.g. `"10.00"` not `"1000"` for ten USD) | XML on `Money.Value` |
| Equality to the cent | Format eShop catalog total with the same scale PayPal expects for that currency so the held amount equals the order total | requirement + `Money.Value` |
| Sign | regex allows a leading `-`; **do not** send negative values for authorize/capture/refund amounts | `Money.cs` |

`Money`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req` (`records-1-Ac-Pa.md`).

---

### 12. Enums in scope (`map/models/enums.md`) — `PayPalServerSdk.Models.Enums`

These are `StringEnum<T>` records, **not** C# enums. Use static members (e.g. `CheckoutPaymentIntent.Authorize`) or `Type.FromValue("wire")`. Member name ≠ wire value.

| Enum | Members (C# (wire)) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` — pay flow uses **`Authorize`** |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** ← STOP |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `RefundIncompleteReason` | `Echeck (ECHECK)` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` — STOP on payer-action (setup-token responses) |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, … `Unknown (UNKNOWN)` — display `LastDigits` + `Brand` |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` |
| `TokenType` | **`BillingAgreement (BILLING_AGREEMENT)` only** — not a vault payment token |
| `ParesStatus` | `Y`, `N`, `U`, `A`, `C`, `R`, `D`, `I` — on 3DS results; STOP is **`OrderStatus.PayerActionRequired`**, not a guessed Pares mapping |
| `LinkHttpMethod` | `Get`, `Post`, `Put`, `Delete`, … `Patch` |

`ServerEnvironment` is **`PayPalServerSdk.Servers.ServerEnvironment`**, not `Models.Enums`: member `Sandbox` only.

---

### Idempotency map (app + SDK)

| eShop API | SDK `payPalRequestId` | App short-circuit |
|---|---|---|
| `POST .../pay` | stable key per order pay (e.g. `{orderId}:pay`) on **both** `CreateOrder` and `AuthorizeOrder` | if authorization id already stored and status is `Created`/`Pending`, return existing hold |
| `POST .../fulfil` | `{orderId}:capture` (and `{orderId}:reauth` if reauthorize runs) | if capture id stored and status `Completed`, return stored fee/net |
| `POST .../cancel` | `{orderId}:void` | if `Voided`, no-op success |
| `POST .../refunds` | **caller-supplied key** | same key + stored refund id → return that refund |
| `POST /api/payment-methods` | stable key per shopper+card fingerprint **without logging PAN** (e.g. shopper id + last-4 + expiry only if already in request memory) | if token id already stored for that shopper display identity, return it |

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` is a constructor argument and the DI helper creates one client from `IHttpClientFactory`; lifetime/ownership mistakes show up as socket exhaustion or disposed-handler failures. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 1 (auth) — credentials live on `options.Oauth2` as `OAuth2ClientCredentials` in `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`, not under `Models`; a wrong `using` or setting secrets after first use fails 401. **MUST load `dotnet-authentication`** before wiring `ClientId`/`ClientSecret`.

⚠ Step 1 (BaseUrl / retries / timeouts) — `options.Server.Default.Sandbox.BaseUrl` is the override; retry/timeout options on the SDK are **not** the timeout on the `HttpClient` you pass in, and they do **not** bound a whole business operation (pay = create+authorize). Whether a failed **write** can be re-sent is decided by those options plus our `payPalRequestId`. **MUST load `dotnet-configuration-resilience`** before wiring the client or looping `SearchTransactions` pages.

⚠ Steps 3–11 (every call) — five-plus nullable parameters have **no C# default** and the token is named `ct`. Positional calls bind the wrong argument; `cancellationToken:` does not compile. **MUST load `dotnet-calling-endpoints`** before the first `client.Orders`/`Payments`/`Vault`/`TransactionSearch` call.

⚠ Steps 3–11 (models/enums) — intents, statuses, and brands are `StringEnum<T>` with `init`/`required` records; `new` on a union does not apply here (no unions in scope) but dropping `required` members or using C# enum-style casts fails at compile or serialize. **MUST load `dotnet-models`** before building `OrderRequest` / `CardRequest` / `Money`.

⚠ Steps 3–11 (error boundary) — Case A (`TryGetError` vs Vault `TryGetError1`) and Case B (`SearchTransactions` → `RawError`) throw different `SdkException<>` closed types; `TryGetRawError` is not a catch-all on typed errors; no `…Result` variants exist. **MUST load `dotnet-error-handling`** before any try/catch.

⚠ Step 7 (reconciliation pagination) — `SearchTransactions` exposes `page`/`pageSize`/`TotalPages` only; there is no enumerator. Stopping after page 1 silently truncates the report. **MUST load `dotnet-configuration-resilience`** for how to page.

⚠ Tests — the `HttpClient` constructor argument is the seam; do not mock generated records’ private constructors. **MUST load `dotnet-testing`** before writing integration-layer tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing/registering `PayPalServerSdkClient`, `HttpClient` ownership |
| `dotnet-authentication` | Step 1 — `Oauth2` / `OAuth2ClientCredentials` |
| `dotnet-calling-endpoints` | Steps 3–11 — named args, must-pass-explicitly nulls, `ct:` |
| `dotnet-models` | Steps 3–11 — records, `required`, `StringEnum<T>`, wire names |
| `dotnet-error-handling` | Steps 3–11 — Case A/B catch ladder, accessors, JsonException |
| `dotnet-configuration-resilience` | Step 1 + Step 7 — retries/timeouts, BaseUrl, list pagination |
| `dotnet-testing` | Tests for the PayPal integration layer |

**`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

**Assumptions**

- eShop `POST /api/orders` does not call PayPal; the PayPal order is created at `.../pay`.
- One `PurchaseUnit` per eShop order; catalog totals are converted to `Money.Value` strings in `PayPal:Currency`.
- Saved-card `paymentMethodId` is the PayPal payment-token `Id` (or a local surrogate mapped 1:1 to it). Pay with saved card uses `CardRequest.VaultId`, not `PaymentSource.Token`.
- Target is sandbox. Test card 4111111111111111 / any future `YYYY-MM` / any CVC.
- Direct card processing and vaulting are already enabled on the sandbox app (product assumption; not an SDK switch).
- Browser 3DS / `PayerActionRequired` is out of scope (STOP/report-gap), per the user request.

**Blockers**

- **`ServerEnvironment` has only `Sandbox`.** There is no `Live` (or other) member in `Servers/ServerEnvironment.cs`. If `PayPal:Environment` is anything other than sandbox, this SDK cannot select a live host. Do not invent a live environment or a second BaseUrl field besides `Server.Default.Sandbox.BaseUrl`.
- **No remaining-refundable field on `CapturedPayment`.** Remaining must be computed from capture `Amount` minus completed refunds / `TotalRefundedAmount`. Not a missing operation, but not a single SDK property.
- **No dedicated “authorization stale” enum.** Staleness is `ExpirationTime` plus capture/reauthorize `Error.Details.Issue` strings (values **UNVERIFIED** in the map). Operator text must come from those payload fields, not invented issue codes.
- **Vault list requires PayPal `customer_id`.** If `CreatePaymentToken` returns a null `Customer.Id`, `ListCustomerPaymentTokens` cannot be keyed by eShop user id. Serve GET payment-methods from app-persisted token ids in that case; do not invent a different list API.
- **3DS / browser challenge:** `CreateSetupToken` + `VaultCardExperienceContext.ReturnUrl`/`CancelUrl` exist but **must not** be used to implement an approval round-trip. If pay/vault returns `PayerActionRequired` or an `approve` link, stop and report the gap.

No in-scope operation is missing from the map: authorize, vault CRUD, capture, reauthorize, void, refund, and paged transaction search are all present.