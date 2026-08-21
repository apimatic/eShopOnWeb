# PayPal Server SDK — eShopOnWeb contract sheet

Package: `AsadAli.Checkout.Sdk` (install version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`. Map stamp: tag `v1.0.1` / commit `9653d18`.

## Scope & sequence

1. **Client registration** — construct `PayPalServerSdkClient` from config section `PayPal:`; OAuth2 client-credentials; sandbox; optional verbatim BaseUrl for API **and** token.
2. **Place order (app-side)** — existing eShop place-order; persist a payment record with PayPal ids/status (empty until authorize).
3. **AUTHORIZE (direct card)** — `Orders.CreateOrder` with `CheckoutPaymentIntent.Authorize` + `PaymentSource.Card` (PAN/expiry/CVC/name/address). Amount = order total to the cent. Set `invoice_id` + `custom_id` to the eShop order id.
4. **AUTHORIZE (vaulted card)** — same `CreateOrder`, but `CardRequest.VaultId` instead of PAN.
5. **Read hold / detect stale** — `Orders.GetOrder` and/or `Payments.GetAuthorizedPayment`; compare `expiration_time` and `AuthorizationStatus`.
6. **REAUTHORIZE stale hold** — `Payments.ReauthorizePayment` before capture; persist the new authorization id.
7. **CAPTURE on fulfil** — `Payments.CaptureAuthorizedPayment`; persist captured amount, PayPal fee, net proceeds from `seller_receivable_breakdown`.
8. **VOID on cancel-before-fulfil** — `Payments.VoidPayment`.
9. **REFUND after fulfil** — `Payments.RefundCapturedPayment` (full or partial); never refund more than captured.
10. **VAULT save** — `Vault.CreatePaymentToken` (card, no charge). Optional setup-token path only if needed.
11. **VAULT list / get / delete** — `Vault.ListCustomerPaymentTokens`, `Vault.GetPaymentToken`, `Vault.DeletePaymentToken`.
12. **Reconciliation** — `TransactionSearch.SearchTransactions` over the ISO-8601 range, **chunked to 31 days** and **paged until `page == total_pages`**.
13. **Error boundary** — per-operation `SdkException<{Op}Error>` / `SdkException<RawError>` catch ladder + `JsonException` handling.
14. **Tests** — stub `HttpClient` seam; never send real PAN to logs/DB.

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

Enums are `PayPalServerSdk.Models.Enums` `StringEnum<T>` records, **not** C# enums. Use the static member (e.g. `CheckoutPaymentIntent.Authorize`) or `Type.FromValue("AUTHORIZE")`. Wire values are in parentheses below.

No-throw `…Result` variants: **absent** on every operation. Pagination helpers: **absent** — loop `page` yourself.

### 0. Client construction / auth / BaseUrl

| Item | Fact | Cite |
|---|---|---|
| Constructor | `PayPalServerSdk.PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(this IServiceCollection, Action<PayPalServerSdkClientOptions>? configure = null)` — registers the client as **singleton** via unnamed `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Options | `PayPalServerSdk.PayPalServerSdkClientOptions`: `Environment` (`PayPalServerSdk.Servers.ServerEnvironment`), `Retry` (`PayPalServerSdk.Core.Configuration.RetryOptions`), `Logging` (`PayPalServerSdk.Core.Configuration.LoggingOptions`), `Server` (`PayPalServerSdk.ServerOptions`), `Oauth2` (`PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?`), `Oauth2TokenStrategy` (`PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>?`) | `sdk-map.md`, `PayPalServerSdkClientOptions.cs` |
| Environment members | **Only** `PayPalServerSdk.Servers.ServerEnvironment.Sandbox` (wire `"Sandbox"`). `Default()` → `Sandbox`. **No Live/Production member exists.** | `Servers/ServerEnvironment.cs` |
| Credentials | `new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials { ClientId = …, ClientSecret = …, Scope = … }` — `ClientId` and `ClientSecret` are `required string`; `Scope` is `string?` | `OAuth2ClientCredentials.cs` |
| Config mapping | `PayPal:ClientId` → `options.Oauth2.ClientId`; `PayPal:ClientSecret` → `options.Oauth2.ClientSecret`; `PayPal:Environment` → only `Sandbox` is a valid SDK value (sandbox for all dev/test); `PayPal:Currency` → app-side ISO-4217 string for `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode` (not an SDK client option); `PayPal:BaseUrl` — when set, assign **verbatim** (see next row) | brief + this sheet |
| BaseUrl override (API **and** token) | `options.Server.Default.Sandbox.BaseUrl` (`PayPalServerSdk.ServerOptions.Default` is `PayPalServerSdk.Servers.DefaultOptions`; nested `SandboxOptions.BaseUrl` defaults to `"https://api-m.sandbox.paypal.com"`). Token request is `server.Default("/v1/oauth2/token")` — **same** `Default`/`Sandbox.BaseUrl` as every API call. There is no separate token-server options object. | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Server.cs`, `AuthSchemes.cs` |
| Controllers | `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch` (also `client.Subscriptions` — **out of scope**) | `sdk-map.md` |
| Per-request options | `PayPalServerSdk.Core.RequestOptions` has **only** `LogLevel? LogLevel` — cannot set headers, BaseUrl, or idempotency keys | `Core/RequestOptions.cs` |
| Retry options | `PayPalServerSdk.Core.Configuration.RetryOptions` required members: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout` (`TimeSpan?`), `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. Build a full instance or `RetryOptions.Default()`. | `sdk-map.md` |

⚠ Step 1 (client registration) — `HttpClient` lifetime / factory vs singleton client, and whether handler rotation ever reaches you. **MUST load `dotnet-client-initialization`** before wiring DI.
⚠ Step 1 (auth) — credentials must be set before construct / in the DI callback; do not hardcode secrets. **MUST load `dotnet-authentication`**.
⚠ Step 1 (BaseUrl / retries / timeout) — SDK retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; a transport failure can retry a write. **MUST load `dotnet-configuration-resilience`** before wiring the client.

### 1–4. Orders — create (authorize intent), get

#### `Orders.CreateOrder` — primary AUTHORIZE (direct card **or** vaulted token)

- **HTTP**: `POST /v2/checkout/orders` via `_server.Default(...)`
- **Controller**: `client.Orders`
- **Signature** (named args; 5 leading nullables **must be passed explicitly**):
  `Task<PayPalServerSdk.Models.Order> CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderRequest body, string? prefer = "return=minimal", PayPalServerSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Headers sent**: `PayPal-Mock-Response` ← `payPalMockResponse`; **`PayPal-Request-Id` ← `payPalRequestId`** (caller idempotency key; stored 6 hours; **mandatory** for single-step create with a payment source); `PayPal-Partner-Attribution-Id`; `PayPal-Client-Metadata-Id`; `Prefer` ← `prefer`; `PayPal-Auth-Assertion`; **plus `Idempotency-Key: Guid.NewGuid()` on every call** (not caller-controlled; `RequestOptions` cannot override it).
- **prefer**: default `"return=minimal"` returns only id/status/HATEOAS links. Pass **`prefer: "return=representation"`** so `purchase_units[].payments.authorizations` (hold id, amount, status, expiration) is present.
- **Returns**: `PayPalServerSdk.Models.Order` (not an envelope wrapper — the record **is** the payload).
- **Error**: Case A `SdkException<PayPalServerSdk.Errors.CreateOrderError>` — `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out PayPalServerSdk.Core.ErrorResponse.RawError)` [fallback].
- **Pagination**: none.
- Cite: `operations/Orders.md`, `Api/Orders.cs`.

**Request `PayPalServerSdk.Models.OrderRequest`** (`Models/OrderRequest.cs`, `records-1-Ac-Pa.md`):

| Member (wire) | Type | Required |
|---|---|---|
| `Intent (intent)` | `PayPalServerSdk.Models.Enums.CheckoutPaymentIntent` | **required** — use `CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`), **not** `Capture` |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PayPalServerSdk.Models.PurchaseUnitRequest>` | **required** |
| `PaymentSource (payment_source)` | `PayPalServerSdk.Models.PaymentSource?` | set for single-step card/vault authorize |
| `Payer (payer)` | `PayPalServerSdk.Models.Payer?` | optional |
| `ApplicationContext (application_context)` | `PayPalServerSdk.Models.OrderApplicationContext?` | optional; not a browser-approval substitute |

**`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`): `Amount (amount): AmountWithBreakdown` **required**; `CustomId (custom_id): string?` (max 255) — **set to eShop order id** (reconcile key; appears in reports, not shown to payer); `InvoiceId (invoice_id): string?` (max 127, unique per merchant) — **also set to eShop order id**; `ReferenceId (reference_id): string?`; `Description`, `SoftDescriptor`, `Items`, `Shipping`, `Payee`, `PaymentInstruction`, `SupplementaryData` optional.

**`AmountWithBreakdown`** (`records-1-Ac-Pa.md`): `CurrencyCode (currency_code): string` **required** (ISO-4217, length 3); `Value (value): string` **required** (regex integer or decimal; max 32); `Breakdown (breakdown): AmountBreakdown?`. If `breakdown` is set, value **must** equal item_total + tax_total + shipping + handling + insurance − shipping_discount − discount.

**Direct card — `PaymentSource.Card` = `PayPalServerSdk.Models.CardRequest`** (`records-1-Ac-Pa.md`, `Models/CardRequest.cs`):

| Member (wire) | Type | Notes |
|---|---|---|
| `Number (number)` | `string?` | PAN, 13–19 digits. **Never persist or log.** PCI SAQ D if used outside sandbox hosted-fields. |
| `Expiry (expiry)` | `string?` | ISO-8601 **`YYYY-MM`** (exactly 7 chars). |
| `SecurityCode (security_code)` | `string?` | 3–4 digit CVC. |
| `Name (name)` | `string?` | cardholder name, 1–300. |
| `BillingAddress (billing_address)` | `PayPalServerSdk.Models.Address?` | `CountryCode (country_code): string` **required** (ISO-3166-1 alpha-2); `AddressLine1/2`, `AdminArea2` (city), `AdminArea1` (state), `PostalCode` optional. |
| `VaultId (vault_id)` | `string?` | **vaulted-pay path** — PayPal payment-token id. Do not send PAN+vault_id together. |
| `ExperienceContext (experience_context)` | `CardExperienceContext?` | `ReturnUrl`/`CancelUrl` for **3DS browser** — do not invent a UI; see Blockers. |
| `Attributes (attributes)` | `CardAttributes?` | `Verification.Method` defaults to `OrdersCardVerificationMethod.ScaWhenRequired` if set; `Vault.StoreInVault` = `StoreInVaultInstruction.OnSuccess` vaults **on successful payment** (separate from Flow 2). |
| `StoredCredential (stored_credential)` | `CardStoredCredential?` | for stored-credential CIT/MIT; `PaymentInitiator` + `PaymentType` required if used. |

**Vaulted card:** `PaymentSource = new PaymentSource { Card = new CardRequest { VaultId = savedPaymentTokenId } }` (no `Number`/`SecurityCode`).

**Do not** use `PaymentSource.Token` (`PayPalServerSdk.Models.Token`) for vaulted cards — `TokenType` has only `BillingAgreement (BILLING_AGREEMENT)`. Vault pay is `CardRequest.VaultId`.

**Response `PayPalServerSdk.Models.Order`** (`records-1-Ac-Pa.md`) — no extra envelope field:

| Member (wire) | Type | Integration reads |
|---|---|---|
| `Id (id)` | `string?` | **PayPal order id** — persist |
| `Status (status)` | `OrderStatus?` | see enum table; **`PayerActionRequired` = 3DS/browser blocker** |
| `Intent (intent)` | `CheckoutPaymentIntent?` | expect `Authorize` |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnit>?` | hold lives here |
| `PaymentSource (payment_source)` | `PaymentSourceResponse?` | `Card.LastDigits`, `Card.Brand`, `Card.Expiry` only — never a PAN |
| `Links (links)` | `IReadOnlyList<LinkDescription>?` | if status is payer-action, links would describe a browser step — **do not follow**; fail |
| `CreateTime` / `UpdateTime` | `string?` | RFC3339 |

**Authorization id path (representation body):** `order.PurchaseUnits[0].Payments.Authorizations[0].Id`  
`PurchaseUnit.Payments` is `PayPalServerSdk.Models.PaymentCollection`: `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`, `Captures (captures): IReadOnlyList<OrdersCapture>?`, `Refunds (refunds): IReadOnlyList<Refund>?`.  
`AuthorizationWithAdditionalData` / `Authorization`: `Id (id)`, `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?` (RFC3339), `InvoiceId`, `CustomId`, `CreateTime`, `UpdateTime`. Cite: `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`.

Sandbox test card (app input only): Visa `4111111111111111`, any future `YYYY-MM`, any CVC, any name/address with valid `country_code`.

#### `Orders.AuthorizeOrder` — alternate two-step authorize (only if CreateOrder was created **without** a payment source)

- **HTTP**: `POST /v2/checkout/orders/{id}/authorize`
- **Signature**: `Task<PayPalServerSdk.Models.OrderAuthorizeResponse> AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 5 nullables after `id` must be passed explicitly.
- **Body**: `OrderAuthorizeRequest.PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` with `Card (card): CardRequest?` (same card/vault_id shape).
- **Returns**: `OrderAuthorizeResponse` — same fields as `Order` (id/status/purchase_units/payment_source/links). Authorization id: `PurchaseUnits[].Payments.Authorizations[].Id`.
- **Error**: Case A `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError`.
- Cite: `operations/Orders.md`. Prefer the single-step `CreateOrder` path above for this integration.

#### `Orders.GetOrder` — current order + hold/captures/refunds

- **HTTP**: `GET /v2/checkout/orders/{id}`
- **Signature**: `Task<Order> GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `fields`, `payPalMockResponse`, `payPalAuthAssertion` must be passed explicitly (`null` to skip).
- **Query**: `fields` ← `fields`. Valid filter documented on the method: **`payment_source`**. Pass `null` to get the default representation (includes `purchase_units.payments`).
- **Returns**: `Order` (same shape).
- **Error**: Case A `SdkException<GetOrderError>` — `TryGetError(out Error)` [401, 404] · `TryGetRawError`.
- Cite: `operations/Orders.md`, `Api/Orders.cs`.

⚠ Steps 3–5 (calls) — many leading params are nullable **with no C# default**; positional calls mis-bind. **MUST load `dotnet-calling-endpoints`**.
⚠ Steps 3–4 (models) — `required` init members, `StringEnum<T>` members vs wire values, JSON wire names vs C# names. **MUST load `dotnet-models`**.

### 5. Payments — get authorization (stale detection) + reauthorize

#### `Payments.GetAuthorizedPayment`

- **HTTP**: `GET /v2/payments/authorizations/{authorization_id}`
- **Signature**: `Task<PayPalServerSdk.Models.PaymentAuthorization> GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — two nullables must be passed explicitly.
- **Returns**: `PaymentAuthorization` (payload **is** the record).
- **Error**: Case A `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.
- Cite: `operations/Payments.md`.

**`PaymentAuthorization` fields to persist/read** (`records-2-Pa-Ve.md`, `Models/PaymentAuthorization.cs`):

| Member (wire) | Type | Role |
|---|---|---|
| `Id (id)` | `string?` | authorization / hold id |
| `Status (status)` | `AuthorizationStatus?` | `Created` = hold active; `Captured` / `PartiallyCaptured` / `Voided` / `Denied` / `Pending` — see enum |
| `StatusDetails.Reason` | `AuthorizationIncompleteReason?` | only when pending: `PendingReview`, `DeclinedByRiskFraudFilters` |
| `Amount (amount)` | `Money?` | held amount |
| `ExpirationTime (expiration_time)` | `string?` | RFC3339; **stale = this instant is in the past** (there is **no** `EXPIRED` status enum member) |
| `InvoiceId` / `CustomId` | `string?` | echo of values set at create |
| `CreateTime` / `UpdateTime` | `string?` | RFC3339 |

**Stale vs unrenewable (from operation notes + status enum, `operations/Payments.md`):**

- Honor period ~3 days; reauthorize window **days 4–29** after original authorization; after **30 days** you must create a **new** authorized payment — this SDK has no “renew after 30 days” operation other than a new `CreateOrder` (which needs a payment source again).
- Do **not** call reauthorize when `Status` is `Voided`, `Captured`, `Denied`, or `PartiallyCaptured` (already captured).
- If `ExpirationTime` is past **and** original `CreateTime` is ≥ 30 days ago: skip reauthorize; fail fulfilment with an operator message that the hold cannot be renewed and the shopper must be authorized again.
- `ReauthorizePayment` notes describe **“authorized PayPal account payment”** — whether a **direct-card** hold is accepted by this same operation is **UNVERIFIED**. On 422, surface `Error.Name` + `Error.Message` + each `Details[].Issue` + `Description` to the operator (do not guess issue codes).

#### `Payments.ReauthorizePayment`

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/reauthorize`
- **Signature**: `Task<PaymentAuthorization> ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId`, `payPalAuthAssertion`, `body` must be passed explicitly.
- **Body**: `ReauthorizeRequest.Amount (amount): Money?` — **only** supported request field; pass the same held total (`currency_code` + `value`) to keep the hold equal to the order total.
- **Headers**: `PayPal-Request-Id` ← `payPalRequestId` (stored 45 days); `Prefer`; `PayPal-Auth-Assertion`; `Idempotency-Key: Guid.NewGuid()`.
- **Returns**: `PaymentAuthorization` — **new** `Id` and `ExpirationTime`; persist the new authorization id (old id is superseded).
- **Error**: Case A `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. Treat 422/`TryGetError` as **cannot renew** — copy `Details[].Issue` + `Description` into the operator error (exact issue strings are not in the map).
- Cite: `operations/Payments.md`, `records-2-Pa-Ve.md`.

### 6. CAPTURE on fulfil — `Payments.CaptureAuthorizedPayment`

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/capture`
- **Signature**: `Task<PayPalServerSdk.Models.CapturedPayment> CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullables after `authorizationId` must be passed explicitly.
- **prefer**: pass **`"return=representation"`** so `seller_receivable_breakdown` (fee/net) is returned; default minimal is id/status/links only.
- **Body `CaptureRequest`**: `Amount (amount): Money?` (omit/`null` to capture the remaining authorized amount); `FinalCapture (final_capture): bool? = false` — set `true` when this capture completes the order; `InvoiceId`, `NoteToPayer`, `SoftDescriptor`, `PaymentInstruction` optional.
- **Returns**: `CapturedPayment` (payload **is** the record).
- **Error**: Case A `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. 409 = conflict (already captured / wrong state).
- Cite: `operations/Payments.md`, `records-1-Ac-Pa.md`.

**Fee / net — `CapturedPayment.SellerReceivableBreakdown` (`PayPalServerSdk.Models.SellerReceivableBreakdown`, `records-2-Pa-Ve.md`):**

| Member (wire) | Type | Meaning |
|---|---|---|
| `GrossAmount (gross_amount)` | `Money` **required** | captured amount |
| `PaypalFee (paypal_fee)` | `Money?` | PayPal fee |
| `NetAmount (net_amount)` | `Money?` | net proceeds to merchant |
| `ReceivableAmount (receivable_amount)` | `Money?` | receivable (fx cases) |
| `PaypalFeeInReceivableCurrency` | `Money?` | fee in receivable currency |
| `ExchangeRate` / `PlatformFees` | optional | not needed for single-merchant eShop |

Also persist `CapturedPayment.Id (id)` (capture id for refunds), `Status (status): CaptureStatus?`, `Amount (amount): Money?`. If breakdown is null (minimal prefer or pending capture), call `GetCapturedPayment`.

#### `Payments.GetCapturedPayment`

- **HTTP**: `GET /v2/payments/captures/{capture_id}`
- **Signature**: `Task<CapturedPayment> GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalMockResponse` must be passed explicitly.
- **Error**: Case A `SdkException<GetCapturedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.
- Cite: `operations/Payments.md`.

Do **not** use `Orders.CaptureOrder` (`POST /v2/checkout/orders/{id}/capture`) for this flow — that captures an order with intent CAPTURE / remaining capture-on-order, not an authorization hold.

### 7. VOID (cancel before capture) — `Payments.VoidPayment`

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/void`
- **Signature**: `Task<PaymentAuthorization> VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — three nullables after `authorizationId` must be passed explicitly. **No body.**
- **Headers**: `PayPal-Request-Id` ← `payPalRequestId` (45 days); `Idempotency-Key: Guid.NewGuid()`.
- **Returns**: `PaymentAuthorization` (expect `Status = Voided`).
- **Error**: Case A `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. Notes: cannot void a fully captured authorization.
- Cite: `operations/Payments.md`.

### 8. REFUND — `Payments.RefundCapturedPayment` + `GetRefund`

- **HTTP**: `POST /v2/payments/captures/{capture_id}/refund`
- **Signature**: `Task<PayPalServerSdk.Models.Refund> RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullables after `captureId` must be passed explicitly.
- **Idempotency**: pass the **caller-supplied** key as `payPalRequestId:` → header **`PayPal-Request-Id`** (stored 45 days). SDK also sends `Idempotency-Key: Guid.NewGuid()` (not controllable).
- **Full refund**: `body: null` (or empty `RefundRequest` with no `Amount`).
- **Partial refund**: `body: new RefundRequest { Amount = new Money { CurrencyCode = …, Value = … } }`.
- **Returns**: `Refund` — persist `Id (id)` (refund id), `Status`, `Amount`.
- **Error**: Case A `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. Over-refund / already-refunded typically 422/409 — surface `Details[].Issue`.
- Cite: `operations/Payments.md`, `records-2-Pa-Ve.md`.

**`Refund`**: `Id (id)`, `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown.TotalRefundedAmount (total_refunded_amount): Money?` (running total refunded against the capture), `GrossAmount` / `PaypalFee` / `NetAmount` on that breakdown.

**Cap remaining refundable (app-side, before call):** remaining = captured `CapturedPayment.Amount` (or `SellerReceivableBreakdown.GrossAmount`) minus sum of successful refund `Amount`s already stored. Refuse if requested amount > remaining. Also treat `CaptureStatus.Refunded` as not refundable and `PartiallyRefunded` as refundable only for the remainder. Confirm with `GetCapturedPayment` if local state is stale.

#### `Payments.GetRefund`

- **HTTP**: `GET /v2/payments/refunds/{refund_id}`
- **Signature**: `Task<Refund> GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Error**: Case A `SdkException<GetRefundError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.
- Cite: `operations/Payments.md`.

### 9–11. Vault (save / list / get / delete) — no charge

**Order of operations for Flow 2 (no browser, sandbox PAN):**

1. `Vault.CreatePaymentToken` with `PaymentTokenRequestCard` (PAN/expiry/CVC/name/address) + `Customer.MerchantCustomerId` = signed-in shopper id. **Do not** charge; this is not an order.
2. Persist `PaymentTokenResponse.Id` (payment-token id), safe display fields, and `Customer.Id` / `MerchantCustomerId`.
3. List: `Vault.ListCustomerPaymentTokens(customerId: <merchant customer id>)`.
4. Pay later: `CreateOrder` with `CardRequest.VaultId = paymentTokenId`.
5. Delete: `Vault.DeletePaymentToken(id: paymentTokenId)`.

`CreateSetupToken` is **not** required for this no-browser path. It is a temporary token (`VaultTokenRequestType.SetupToken`) that you would then convert via `CreatePaymentToken` with `PaymentSource.Token = new VaultTokenRequest { Id, Type = VaultTokenRequestType.SetupToken }`. Setup tokens expose `PaymentTokenStatus.PayerActionRequired` and `VaultCardExperienceContext.ReturnUrl/CancelUrl` — **browser**. Skip unless CreatePaymentToken cannot accept raw card (then it is the same 3DS blocker).

Vault controller XML: Payment Method Tokens API v3 is **available in the US only**.

#### `Vault.CreatePaymentToken`

- **HTTP**: `POST /v3/vault/payment-tokens`
- **Signature**: `Task<PayPalServerSdk.Models.PaymentTokenResponse> CreatePaymentToken(string? payPalRequestId, PayPalServerSdk.Models.PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must be passed explicitly (stored **3 hours**).
- **Headers**: `PayPal-Request-Id`; `Idempotency-Key: Guid.NewGuid()`.
- **Error**: Case A `SdkException<CreatePaymentTokenError>` — **`TryGetError1(out Error1)`** [400, 403, 404, 422, 500] · `TryGetRawError`. (Accessor name is `TryGetError1`, not `TryGetError`.)
- Cite: `operations/Vault.md`, `Api/Vault.cs`.

**Request `PaymentTokenRequest`**: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource` **required**; `Customer (customer): Customer?`.  
`Customer`: `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?` — set **`MerchantCustomerId`** to the eShop user id.  
`PaymentTokenRequestPaymentSource.Card`: `PaymentTokenRequestCard` — `Number`, `Expiry` (`YYYY-MM`), `SecurityCode`, `Name`, `BillingAddress` (`CountryCode` required), `Brand` optional. **Never persist Number/SecurityCode.**

**Response `PaymentTokenResponse`**: `Id (id)` = **saved payment-token id** (this is `vault_id` later); `Customer`: `CustomerResponse` (`Id`, `MerchantCustomerId`); `PaymentSource.Card`: `CardPaymentTokenEntity` **safe display** — `LastDigits (last_digits)`, `Brand (brand): CardBrand?`, `Expiry (expiry)`, `Name`, `Type (type): CardType?`. No PAN field on the response entity.

#### `Vault.ListCustomerPaymentTokens`

- **HTTP**: `GET /v3/vault/payment-tokens`
- **Signature**: `Task<CustomerVaultPaymentTokensResponse> ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query**: `customer_id` ← `customerId` (XML: identifier in **merchant/partner system of records** — pass the same `MerchantCustomerId`); `page_size`, `page`, `total_required`.
- **Returns**: `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `TotalItems`, `TotalPages`, `Customer`, `Links`. Map pagination: **none** (only `page`, no `perPage`) — loop `page` from 1 to `TotalPages` with `totalRequired: true`.
- **Error**: Case A `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`.
- The app **may** store PayPal payment-token id + display fields locally; list/get still exist on the SDK so the app is not required to be the only source of display data. Persist at least `Id` + `MerchantCustomerId` to pay and delete.
- Cite: `operations/Vault.md`, `Api/Vault.cs`.

#### `Vault.GetPaymentToken`

- **HTTP**: `GET /v3/vault/payment-tokens/{id}`
- **Signature**: `Task<PaymentTokenResponse> GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Error**: Case A `SdkException<GetPaymentTokenError>` — `TryGetError1(out Error1)` [403, 404, 422, 500] · `TryGetRawError`.
- Cite: `operations/Vault.md`.

#### `Vault.DeletePaymentToken`

- **HTTP**: `DELETE /v3/vault/payment-tokens/{id}`
- **Signature**: `Task DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` — returns `void` (`Task`).
- **Error**: Case A `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`.
- No `payPalRequestId` parameter. After success, the token must not be listed or usable as `vault_id`.
- Cite: `operations/Vault.md`.

#### `Vault.CreateSetupToken` / `GetSetupToken` (only if you cannot vault the card in one shot)

- Create: `Task<SetupTokenResponse> CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Get: `Task<SetupTokenResponse> GetSetupToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- `SetupTokenResponse.Status` default `PaymentTokenStatus.Created`; **`PayerActionRequired` is a browser/3DS blocker**.
- Convert: `CreatePaymentToken` body `PaymentSource.Token = new VaultTokenRequest { Id = setupTokenId, Type = VaultTokenRequestType.SetupToken }` (`Type` **required**).
- Errors: `CreateSetupTokenError` / `GetSetupTokenError` — `TryGetError1(out Error1)` (see Vault.md for status lists).
- Cite: `operations/Vault.md`, `records-2-Pa-Ve.md`.

### 12. Reconciliation — `TransactionSearch.SearchTransactions`

- **HTTP**: `GET /v1/reporting/transactions`
- **Signature**: `Task<PayPalServerSdk.Models.SearchResponse> SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` — **8** nullables after `endDate` have **no default** and **must be passed explicitly** (`null` to skip). Use named arguments.
- **Query wire names**: `start_date` ← `startDate`, `end_date` ← `endDate`, then `transaction_id`, `transaction_type`, `transaction_status`, `transaction_amount`, `transaction_currency`, `payment_instrument_type`, `store_id`, `terminal_id`, `fields`, `balance_affecting_records_only`, `page_size`, `page`.
- **Date format**: RFC3339 / Internet date-time; **seconds required**; fractional seconds optional. **Maximum range per call is 31 days.** App must split a longer ISO-8601 from/to into ≤31-day windows, then page each window.
- **Latency note** (operation remarks): up to **three hours** before executed transactions appear; history up to previous three years.
- **Pagination**: SDK has **no** auto-pager (`operations/TransactionSearch.md`: “none (only `page`, no `perPage`)”). Loop: `page = 1..n` while `page < TotalPages` (response `total_pages`). Default `pageSize` 100.
- **fields**: default `"transaction_info"` is enough for match keys + amounts + fees. Use `"all"` only if you need payer/cart. To include invoice/custom, `transaction_info` already carries `invoice_id` and `custom_field`.
- **Returns**: `SearchResponse` — `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `StartDate`, `EndDate`, `Page`, `TotalItems`, `TotalPages`, `AccountNumber`, `LastRefreshedDatetime`, `Links`.
- **Error**: **Case B** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — read `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. **No** `TryGet*`. (This is the only Case B operation in the SDK.)
- Cite: `operations/TransactionSearch.md`, `Api/TransactionSearch.cs`, `records-2-Pa-Ve.md`.

**Match PayPal row ↔ eShop order** (`TransactionInformation`, `records-2-Pa-Ve.md`):

| Member (wire) | Use |
|---|---|
| `InvoiceId (invoice_id)` | match `PurchaseUnitRequest.InvoiceId` (eShop order id) |
| `CustomField (custom_field)` | match `PurchaseUnitRequest.CustomId` (eShop order id) |
| `TransactionId (transaction_id)` | PayPal txn id (not unique in reporting — see XML) |
| `PaypalReferenceId (paypal_reference_id)` + `PaypalReferenceIdType` | `Odr (ODR)` / `Txn` / `Sub` / `Pap` |
| `TransactionAmount (transaction_amount)` | `Money` |
| `FeeAmount (fee_amount)` | `Money` |
| `TransactionStatus (transaction_status)` | string code (`D`/`P`/`S`/`V` per XML) |
| `TransactionInitiationDate` / `TransactionUpdatedDate` | RFC3339 |

`SearchBalances` exists (`GET /v1/reporting/balances`) but is **not** required for order-line reconciliation.

⚠ Step 12 (list/search) — `SearchTransactions` leading optionals have no defaults; named args only. **MUST load `dotnet-calling-endpoints`**.
⚠ Step 12 (pagination / date windows) — no SDK pager; 31-day cap is on the operation, not in C# defaults. **MUST load `dotnet-configuration-resilience`**.

### 13. Error types, status accessors, 3DS / payer-action

`PayPalServerSdk.Core.Exceptions.SdkException<TError>` has **only** `required TError Error { get; init; }` — **no** `StatusCode` on the exception. Namespaces: `SdkException<T>` → `PayPalServerSdk.Core.Exceptions`; `ApiError` / `RawError` → `PayPalServerSdk.Core.ErrorResponse`; `{Op}Error` → `PayPalServerSdk.Errors`.

**Typed payload `PayPalServerSdk.Models.Error`** (Orders/Payments Case A `TryGetError`): `Name (name): string` required, `Message (message): string` required, `DebugId (debug_id): string` required, `Details (details): IReadOnlyList<ErrorDetails>?`, `Links`.  
**`ErrorDetails`**: `Issue (issue): string` required, `Description (description): string?`, `Field (field): string?`, `Value (value): string?`, `Location (location): string? = "body"`.  
HTTP status is **not** on `Error`. When one accessor covers many statuses (e.g. 400/401/422), distinguish via `Name` / `Details[].Issue`. `TryGetRawError` is **false** for statuses that already matched `TryGetError` / `TryGetError1` / `TryGetNoContent`.

**Vault `Error1`**: same `Name`/`Message`/`DebugId`/`Details` but `Details` is `IReadOnlyList<ErrorDetails1>` and `Links` is `IReadOnlyList<ErrorLinkDescription>` (`Rel` is **optional** on the link — required-`rel` would fail RESOURCE_NOT_FOUND deserialize).

**`RawError`**: `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`.

Catch **per operation** (do not catch `ApiError` and expect typed accessors). Cover **every** `TryGet*` on that `{Op}Error`, with `TryGetRawError` **last**.

| Operation | `TError` | Accessors (status) |
|---|---|---|
| `CreateOrder` | `CreateOrderError` | `TryGetError` [400,401,422] · `TryGetRawError` |
| `AuthorizeOrder` | `AuthorizeOrderError` | `TryGetError` [400,401,403,404,422,500] · `TryGetRawError` |
| `GetOrder` | `GetOrderError` | `TryGetError` [401,404] · `TryGetRawError` |
| `CaptureAuthorizedPayment` | `CaptureAuthorizedPaymentError` | `TryGetError` [400,401,403,404,409,422] · `TryGetNoContent` [500] · `TryGetRawError` |
| `GetAuthorizedPayment` | `GetAuthorizedPaymentError` | `TryGetError` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` |
| `GetCapturedPayment` | `GetCapturedPaymentError` | `TryGetError` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` |
| `ReauthorizePayment` | `ReauthorizePaymentError` | `TryGetError` [400,401,403,404,422] · `TryGetNoContent` [500] · `TryGetRawError` |
| `RefundCapturedPayment` | `RefundCapturedPaymentError` | `TryGetError` [400,401,403,404,409,422] · `TryGetNoContent` [500] · `TryGetRawError` |
| `GetRefund` | `GetRefundError` | `TryGetError` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` |
| `VoidPayment` | `VoidPaymentError` | `TryGetError` [401,403,404,409,422] · `TryGetNoContent` [500] · `TryGetRawError` |
| `CreatePaymentToken` | `CreatePaymentTokenError` | `TryGetError1` [400,403,404,422,500] · `TryGetRawError` |
| `CreateSetupToken` | `CreateSetupTokenError` | `TryGetError1` [400,403,422,500] · `TryGetRawError` |
| `DeletePaymentToken` | `DeletePaymentTokenError` | `TryGetError1` [400,403,500] · `TryGetRawError` |
| `GetPaymentToken` | `GetPaymentTokenError` | `TryGetError1` [403,404,422,500] · `TryGetRawError` |
| `GetSetupToken` | `GetSetupTokenError` | `TryGetError1` [403,404,422,500] · `TryGetRawError` |
| `ListCustomerPaymentTokens` | `ListCustomerPaymentTokensError` | `TryGetError1` [400,403,500] · `TryGetRawError` |
| `SearchTransactions` | **`RawError` (Case B)** | `StatusCode` / `ReadAsString` / `ReadAsJson<T>` / `ReadAsBytes` |

**3DS / payer-action (direct card and vault):**

- `OrderStatus.PayerActionRequired` (wire `PAYER_ACTION_REQUIRED`) on `CreateOrder` / `GetOrder`.
- `PaymentTokenStatus.PayerActionRequired` on setup/payment token responses.
- `CardResponse.AuthenticationResult` → `AuthenticationResponse.LiabilityShift` (`LiabilityShiftIndicator`: `No`/`Possible`/`Unknown`) + `ThreeDSecure.AuthenticationStatus` (`ParesStatus`) / `EnrollmentStatus`.
- `CardExperienceContext` / `VaultCardExperienceContext` `ReturnUrl`/`CancelUrl` exist **only** for a shopper browser round-trip.
- **This integration has no browser.** If status is `PayerActionRequired` (or links imply payer action), **fail the operation** with an operator-visible message. Do not implement an approval redirect. Exact `LinkDescription.Rel` values for that challenge are **UNVERIFIED** in the map.

⚠ Error boundary — Case A vs Case B, accessor order, `TryGetRawError` is not a catch-all, no shared `ApiError` helper. **MUST load `dotnet-error-handling`** before writing any try/catch.
⚠ A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`**.
⚠ A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`**.

### 14. Idempotency (how this SDK sends keys)

| Operation | Caller parameter | Header | Key lifetime (XML) | Also always sent |
|---|---|---|---|---|
| `CreateOrder` | `payPalRequestId` | `PayPal-Request-Id` | 6 hours (mandatory for single-step + payment source) | `Idempotency-Key: Guid.NewGuid()` |
| `AuthorizeOrder` | `payPalRequestId` | `PayPal-Request-Id` | 6 hours | `Idempotency-Key: Guid.NewGuid()` |
| `CaptureAuthorizedPayment` | `payPalRequestId` | `PayPal-Request-Id` | 45 days | `Idempotency-Key: Guid.NewGuid()` |
| `ReauthorizePayment` | `payPalRequestId` | `PayPal-Request-Id` | 45 days | `Idempotency-Key: Guid.NewGuid()` |
| `VoidPayment` | `payPalRequestId` | `PayPal-Request-Id` | 45 days | `Idempotency-Key: Guid.NewGuid()` |
| `RefundCapturedPayment` | `payPalRequestId` | `PayPal-Request-Id` | 45 days | `Idempotency-Key: Guid.NewGuid()` |
| `CreatePaymentToken` / `CreateSetupToken` | `payPalRequestId` | `PayPal-Request-Id` | 3 hours | `Idempotency-Key: Guid.NewGuid()` |
| `DeletePaymentToken` | *(none)* | — | — | `Idempotency-Key: Guid.NewGuid()` |
| GETs / `SearchTransactions` | n/a | — | — | no request-id param |

There is **no** body field for idempotency. `RequestOptions` cannot set headers. Pass the app key as **`payPalRequestId:`** on every write. Whether PayPal keys on `PayPal-Request-Id`, `Idempotency-Key`, or both is **UNVERIFIED**; the SDK always generates a **new** `Idempotency-Key` per invocation, so two app retries with the same `payPalRequestId` still differ on `Idempotency-Key`. In-process SDK retries reuse the same parameter list (same Guid) for that invocation.

GETs (`GetOrder`, `GetAuthorizedPayment`, `GetCapturedPayment`, `GetRefund`, `GetPaymentToken`) are naturally idempotent; use them to re-sync after a timeout.

### 15. Amounts (`Money` / `AmountWithBreakdown`)

Both types: `CurrencyCode (currency_code): string` **required** (ISO-4217, length 3); `Value (value): string` **required** (max 32; regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`).

XML (`Models/Money.cs`, `Models/AmountWithBreakdown.cs`): integer for currencies like `JPY`; decimal fraction for currencies like `TND`; required decimal places are per PayPal currency-codes table (not duplicated as an SDK enum). **Defensive:** format `PayPal:Currency` with that currency’s minor units (USD/EUR: two fraction digits, e.g. `"19.99"`; JPY: integer string). Do not send `decimal`/`double` — only the formatted string. Hold/capture/refund `value` must equal the eShop major-unit total to the cent for that currency.

`PayPal:Currency` is **application** config, not an SDK client property.

### Enums actually needed (`map/models/enums.md`)

| Enum | Members (C# (wire)) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `RefundIncompleteReason` | `Echeck (ECHECK)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, … `Unknown (UNKNOWN)` (full list in `enums.md`) |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` — **not** for vault cards |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` |
| `DisbursementMode` | `Instant (INSTANT)`, `Delayed (DELAYED)` |

### Persist on the eShop payment (PayPal-owned state)

| Field | Source |
|---|---|
| PayPal order id | `Order.Id` |
| Order status | `Order.Status` |
| Authorization / hold id | `PurchaseUnits[].Payments.Authorizations[].Id` or `PaymentAuthorization.Id` (replace after reauthorize) |
| Authorization status + `expiration_time` | `PaymentAuthorization` |
| Capture id | `CapturedPayment.Id` |
| Capture status | `CapturedPayment.Status` |
| Captured amount / fee / net | `SellerReceivableBreakdown.GrossAmount` / `PaypalFee` / `NetAmount` |
| Refund ids + amounts + statuses | each `Refund` |
| Vault payment-token id | `PaymentTokenResponse.Id` |
| Vault customer merchant id | `Customer.MerchantCustomerId` |
| Last4 / brand / expiry | `CardPaymentTokenEntity` / `CardResponse` |

### Logging / PAN

`LoggingOptions.LogRequestBody` defaults **false**. `RedactedKeys` does **not** include `number` or `security_code`. Never enable request-body logging on card/vault calls. Never write PAN/CVC to the eShop DB or app logs.

⚠ Tests — `HttpClient` constructor argument is the fake-handler seam; match eShop’s existing test framework. **MUST load `dotnet-testing`**.

---

## Trap notes

⚠ Step 1 (client registration) — singleton DI vs `IHttpClientFactory` handler rotation, and HttpClient ownership. **MUST load `dotnet-client-initialization`**.
⚠ Step 1 (auth) — where credentials are set relative to construct, and rotating secrets. **MUST load `dotnet-authentication`**.
⚠ Step 1 (BaseUrl / retry / timeout) — whether a failed write can be re-sent; what `Timeout` actually bounds; Environment captured at construct vs live `Server` reference. **MUST load `dotnet-configuration-resilience`**.
⚠ Steps 3–12 (calls) — nullable-without-default parameters and list/search positional mis-bind; `ct:` not `cancellationToken:`. **MUST load `dotnet-calling-endpoints`**.
⚠ Steps 3–11 (models) — `required` init, `StringEnum<T>` vs C# enum, wire names vs C# names, unmodeled JSON dropped. **MUST load `dotnet-models`**.
⚠ Step 13 (error boundary) — Case A vs B, every `TryGet*` including `TryGetError1` / `TryGetNoContent`, `TryGetRawError` last and not a catch-all. **MUST load `dotnet-error-handling`**.
⚠ JsonException from a drifted/malformed **2xx** body (missing `required` member) is **not** an `SdkException`. **MUST load `dotnet-error-handling`**.
⚠ JsonException while constructing a non-2xx `{Operation}Error` **replaces** `SdkException` and destroys the HTTP status. **MUST load `dotnet-error-handling`**.
⚠ Step 14 (tests) — stub the `HttpClient` seam; do not mock SDK internals. **MUST load `dotnet-testing`**.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — construct / `AddPayPalServerSdkClient` / HttpClient lifetime |
| `dotnet-authentication` | Step 1 — `Oauth2` client-credentials |
| `dotnet-configuration-resilience` | Step 1 BaseUrl/retry/timeout; Step 12 pagination |
| `dotnet-calling-endpoints` | Steps 3–12 — every SDK call |
| `dotnet-models` | Steps 3–11 — request/response records and enums |
| `dotnet-error-handling` | Step 13 — catch ladder + both `JsonException` directions |
| `dotnet-testing` | Step 14 — HttpClient test seam |

---

## Assumptions & Blockers

**Assumptions**

- eShop place-order remains app-side; PayPal is invoked only after an order exists and is awaiting payment.
- Single purchase unit per PayPal order; `PayPal:Currency` matches the order currency.
- Sandbox direct-card (PAN in API) is acceptable for this task; `CardRequest` XML requires PCI SAQ D for production PAN-in-API.
- `MerchantCustomerId` used on vault create is the same string passed as `ListCustomerPaymentTokens(customerId:)` (method XML: merchant/partner system of records).
- Reconciliation matching uses `invoice_id` and `custom_id` both set to the eShop order id at `CreateOrder`.

**Blockers / genuine gaps**

1. **3DS / payer-action / browser challenge — BLOCKER.** `OrderStatus.PayerActionRequired` and `PaymentTokenStatus.PayerActionRequired` plus `CardExperienceContext` return/cancel URLs describe a shopper browser round-trip. This integration must **not** invent that flow. If sandbox (or a card) returns that status, fail the payment/vault with an operator-visible message. Whether Visa `4111111111111111` triggers it is **UNVERIFIED**.
2. **`ServerEnvironment` has only `Sandbox`.** There is no Live/Production enum member in this SDK. Go-live cannot `options.Environment = Live`. A `PayPal:BaseUrl` override still writes `options.Server.Default.Sandbox.BaseUrl` (the only nested options object). Using that to point at `https://api-m.paypal.com` is **UNVERIFIED** as a supported production path.
3. **Reauthorize of a direct-card hold is UNVERIFIED.** `ReauthorizePayment` notes say “authorized PayPal account payment”. If card holds cannot be renewed, fulfilment must fail with the 422 `Error.Details[].Issue` text; the SDK has no other renew operation. After 30 days from original authorization the notes require a **new** authorized payment, not reauthorize.
4. **Caller idempotency vs SDK `Idempotency-Key`.** Every write also sends `Idempotency-Key: Guid.NewGuid()`; `RequestOptions` cannot override headers. App retries with a stable `payPalRequestId` still get a new `Idempotency-Key`. Which header the live API actually de-duplicates on is **UNVERIFIED**.
5. **SearchTransactions max 31-day window** per call — a longer eShop from/to must be chunked in the app (not an SDK helper). Reporting lag up to three hours.
6. **Vault v3 “available in the US only”** (client XML). Non-US merchant accounts may not be able to save cards through this controller.
7. **No hosted-fields / JS SDK** in this .NET package — PAN-in-API is the only no-browser card path the SDK models.
8. Subscriptions controller is out of scope and unused.

**Not gaps:** GET-after-write to refresh representation; looping `page` for vault list and transaction search; storing payment-token id + last4/brand/expiry in the app in addition to listing from PayPal.
