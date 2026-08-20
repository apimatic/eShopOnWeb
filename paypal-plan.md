# PayPal .NET SDK — eShopOnWeb payments + vaulted cards

Package: `AsadAli.Checkout.Sdk` (install version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`. Map stamp: tag `v1.0.1` / commit `9653d18`.

---

## Scope & sequence

| Step | What | SDK operations |
|---|---|---|
| 1 | Client + Sandbox + OAuth2 credentials + optional BaseUrl override | `new PayPalServerSdkClient(httpClient, options)` or `AddPayPalServerSdkClient` |
| 2 | Place eShop order (app-side). Persist “awaiting payment”. | none |
| 3 | AUTHORIZE hold equal to order total (raw card **or** vault id) | `Orders.CreateOrder` then `Orders.AuthorizeOrder` if `purchase_units[].payments.authorizations` is empty; optional `Orders.GetOrder` |
| 4 | Persist PayPal-owned ids/status: PayPal order id, authorization id/status/`expiration_time` | from create/authorize/get response |
| 5 | FULFIL: if auth stale, `Payments.GetAuthorizedPayment` then `Payments.ReauthorizePayment`; then `Payments.CaptureAuthorizedPayment`. Persist capture id, captured amount, fee, net. | `GetAuthorizedPayment`, `ReauthorizePayment`, `CaptureAuthorizedPayment`; optional `GetCapturedPayment` |
| 6 | CANCEL before capture | `Payments.VoidPayment` |
| 7 | REFUND full/partial after capture; never refund past captured | `Payments.RefundCapturedPayment`; optional `GetRefund` / `GetCapturedPayment` |
| 8 | Reconcile date range (page **and** 31-day windows until covered) | `TransactionSearch.SearchTransactions` |
| 9 | Vault a card for a signed-in shopper | `Vault.CreatePaymentToken` |
| 10 | List shopper’s saved cards | `Vault.ListCustomerPaymentTokens` (page through `total_pages`) |
| 11 | Delete a saved card | `Vault.DeletePaymentToken` |
| 12 | Error boundary around every call | Case A/B per operation row below |

Do **not** use `Orders.CaptureOrder` for fulfilment — that captures a **CAPTURE-intent** order. This flow is **AUTHORIZE** then Payments capture.

Do **not** build a 3DS/browser approval round-trip. If PayPal returns `OrderStatus.PayerActionRequired` or `PaymentTokenStatus.PayerActionRequired`, **STOP** and report it to the operator/shopper.

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

Nullable parameters **without a C# default** must be passed explicitly (`null` to skip).

Every write below also sends a fresh SDK-generated `Idempotency-Key: Guid.NewGuid()` header in addition to caller `PayPal-Request-Id`. Caller idempotency is still the `payPalRequestId` argument. **UNVERIFIED** whether the random `Idempotency-Key` affects PayPal’s `PayPal-Request-Id` dedupe — still pass a stable `payPalRequestId` on every retry of the same logical action. (`Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`)

`prefer` default is `"return=minimal"` (id, status, HATEOAS links only). Pass `prefer: "return=representation"` on create/authorize/capture/reauthorize/void/refund so `purchase_units[].payments`, amounts, and `seller_receivable_breakdown` are in the body. (`Api/Orders.cs`, `Api/Payments.cs`)

---

### 1. Client construction, credentials, environment, BaseUrl

| Item | Contract | Cite |
|---|---|---|
| NuGet | `dotnet add package AsadAli.Checkout.Sdk` (version-less) | `paypal-getting-started` / `sdk-map.md` |
| Client | `PayPalServerSdk.PayPalServerSdkClient` | `sdk-map.md` |
| Ctor | `PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` | `sdk-map.md`, `PayPalServerSdkClient.cs` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` — registers a **singleton** client via `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Options type | `PayPalServerSdk.PayPalServerSdkClientOptions` | `sdk-map.md` |
| Options members | `Environment: PayPalServerSdk.Servers.ServerEnvironment`; `Retry: PayPalServerSdk.Core.Configuration.RetryOptions`; `Logging: LoggingOptions`; `Server: PayPalServerSdk.ServerOptions`; `Oauth2: PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?`; `Oauth2TokenStrategy: PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `sdk-map.md`, `PayPalServerSdkClientOptions.cs` |
| Environment | `PayPalServerSdk.Servers.ServerEnvironment` is a `StringEnum`, **only member** `Sandbox` (wire `"Sandbox"`). `Default()` → `Sandbox`. **No Production member.** Set `options.Environment = ServerEnvironment.Sandbox`. | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Credentials | `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = …, Scope = optional }`. `ClientId` and `ClientSecret` are `required string`. Namespace: `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`. | `OAuth2ClientCredentials.cs` |
| Token URL | Default strategy: `OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), rawClient)` — POST form `grant_type=client_credentials`, Basic auth header. | `AuthSchemes.cs`, `OAuth2ClientCredentialsStrategy.cs` |
| BaseUrl override | `options.Server.Default.Sandbox.BaseUrl = "<config BaseUrl verbatim>"`. Types: `PayPalServerSdk.ServerOptions.Default` → `PayPalServerSdk.Servers.DefaultOptions.Sandbox` → `SandboxOptions.BaseUrl: string` (default `"https://api-m.sandbox.paypal.com"`). `Server.Default(path)` builds **every** API URL from that BaseUrl, **including** `/v1/oauth2/token`. This is the override for **all** PayPal calls. Do **not** rely on `HttpClient.BaseAddress`. | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Server.cs`, `AuthSchemes.cs` |
| Controllers | `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch` (also `Subscriptions` — **out of scope**) | `sdk-map.md`, `PayPalServerSdkClient.cs` |
| Config mapping | `ClientId` → `Oauth2.ClientId`; `ClientSecret` → `Oauth2.ClientSecret`; `Environment` → `ServerEnvironment.Sandbox` (only); `Currency` → `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode` on every amount; optional `BaseUrl` → `Server.Default.Sandbox.BaseUrl` | this section |

⚠ Step 1 (client registration) — `HttpClient` / handler pipeline lifetime versus the SDK wrapper, and whether to use the ctor or `AddPayPalServerSdkClient`, are not visible from the signature. **MUST load `dotnet-client-initialization`** before constructing or DI-registering the client.

⚠ Step 1 (auth) — where credentials are set relative to construction, and how secrets are loaded, are not visible from the options type. **MUST load `dotnet-authentication`** before wiring `Oauth2`.

⚠ Step 1 (BaseUrl / retries / timeouts) — SDK retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; which verbs retry on transport failure affects whether a failed authorize/capture/refund can execute twice. **MUST load `dotnet-configuration-resilience`** before setting `Retry`, `Logging`, or `Server`.

---

### 2. Direct card AUTHORIZE (hold, do not take)

**Create the PayPal order (intent AUTHORIZE) then authorize it.** Put the eShop order id on `purchase_units[0].custom_id` and `invoice_id` so reporting can match.

#### `Orders.CreateOrder` — `POST /v2/checkout/orders`

| | |
|---|---|
| Signature | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must pass explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion` (null to skip). `body` is required. |
| Idempotency | `payPalRequestId` → header `PayPal-Request-Id`. XML: stored **6 hours** (up to 72h via account manager). **Mandatory** for single-step create with a payment source (card / vault_id). |
| prefer | `"return=representation"` so the body includes purchase units / payments. |
| Returns | `PayPalServerSdk.Models.Order` (not an envelope wrapper — the order **is** the response). |
| Error | Case A `SdkException<PayPalServerSdk.Errors.CreateOrderError>`. `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback. |
| Pagination | none |
| Cite | `map/operations/Orders.md`, `Api/Orders.cs` |

**`OrderRequest` (`PayPalServerSdk.Models`, `Models/OrderRequest.cs`)** — `records-1-Ac-Pa.md`

| Field (wire) | Type | Required |
|---|---|---|
| `Intent (intent)` | `PayPalServerSdk.Models.Enums.CheckoutPaymentIntent` | **!req** — use `CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`) |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | **!req** |
| `PaymentSource (payment_source)` | `PaymentSource?` | set `Card` for one-off PAN |
| `Payer (payer)` | `Payer?` | optional |
| `ApplicationContext (application_context)` | `OrderApplicationContext?` | optional; do **not** use to drive a browser approve flow |

**`PurchaseUnitRequest`** — `records-2-Pa-Ve.md`

| Field (wire) | Type | Required |
|---|---|---|
| `Amount (amount)` | `AmountWithBreakdown` | **!req** — must equal eShop order total to the cent |
| `CustomId (custom_id)` | `string?` | persist eShop order id (max 255) |
| `InvoiceId (invoice_id)` | `string?` | same / displayable invoice no. |
| `ReferenceId (reference_id)` | `string?` | optional; omit → PayPal `default` |
| `Description (description)` | `string?` | optional |

**`AmountWithBreakdown`** — `records-1-Ac-Pa.md`, `Models/AmountWithBreakdown.cs`

| Field (wire) | Type | Required |
|---|---|---|
| `CurrencyCode (currency_code)` | `string` | **!req** — ISO-4217, length 3 (config `Currency`) |
| `Value (value)` | `string` | **!req** — **not decimal**. Regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`. For USD-style currencies two decimal places e.g. `"10.00"`. |
| `Breakdown (breakdown)` | `AmountBreakdown?` | optional; if set, sum rules apply |

**`PaymentSource`** for raw card — `records-2-Pa-Ve.md`

| Field (wire) | Type |
|---|---|
| `Card (card)` | `CardRequest?` |

**`CardRequest`** — `records-1-Ac-Pa.md`, `Models/CardRequest.cs`

| Field (wire) | Type | Notes |
|---|---|---|
| `Name (name)` | `string?` | cardholder name, 1–300 |
| `Number (number)` | `string?` | PAN, 13–19 digits. Sandbox: `4111111111111111` |
| `Expiry (expiry)` | `string?` | **ISO-8601 `YYYY-MM`**, length 7, regex `^[0-9]{4}-(0[1-9]|1[0-2])$` |
| `SecurityCode (security_code)` | `string?` | CVC, 3–4 digits. Cannot be present when `payment_initiator=MERCHANT` |
| `BillingAddress (billing_address)` | `Address?` | if sent, `Address.CountryCode (country_code): string !req` |
| `VaultId (vault_id)` | `string?` | **do not set** on one-off PAN path |
| `Attributes (attributes)` | `CardAttributes?` | optional; `Verification.Method` default `ScaWhenRequired` |
| `ExperienceContext (experience_context)` | `CardExperienceContext?` | has `ReturnUrl`/`CancelUrl` for 3DS — **do not** build that round-trip |

PCI: passing number/cvv/expiry via API requires PCI SAQ D (`CardRequest` XML). Full PAN/CVC are **never** stored in the eShop database.

**`Address`** — `records-1-Ac-Pa.md`: `AddressLine1 (address_line_1)`, `AddressLine2`, `AdminArea2` (city), `AdminArea1` (state), `PostalCode`, `CountryCode (country_code): string !req`.

#### `Orders.AuthorizeOrder` — `POST /v2/checkout/orders/{id}/authorize`

| | |
|---|---|
| Signature | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must pass explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalClientMetadataId`, `payPalAuthAssertion`, `body` |
| `id` | PayPal order id from `Order.Id` |
| `body` | `OrderAuthorizeRequest?` — `{ PaymentSource = { Card = <same card or vault_id> } }` if create did not already authorize; `null` if create already carried `payment_source` and you are only confirming the hold |
| Idempotency | `payPalRequestId` → `PayPal-Request-Id` (6 hours) |
| prefer | `"return=representation"` |
| Returns | `PayPalServerSdk.Models.OrderAuthorizeResponse` |
| Error | Case A `SdkException<AuthorizeOrderError>`. `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError` fallback |
| Cite | `map/operations/Orders.md` |

**`OrderAuthorizeRequest`**: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` with `Card (card): CardRequest?`. (`records-1-Ac-Pa.md`)

#### Hold ids / status to persist (from create **or** authorize **or** `GetOrder`)

Response is **not** wrapped — read the record directly.

| Persist | Path | Cite |
|---|---|---|
| PayPal order id | `Order.Id` / `OrderAuthorizeResponse.Id` (`id`) | `records-1-Ac-Pa.md` |
| PayPal order status | `Status (status): OrderStatus?` | same |
| Authorization id | `PurchaseUnits[0].Payments.Authorizations[0].Id` (`purchase_units[].payments.authorizations[].id`) | `PurchaseUnit` → `PaymentCollection` → `AuthorizationWithAdditionalData` (`records-2-Pa-Ve.md`, `records-1-Ac-Pa.md`) |
| Auth status | `Authorizations[0].Status` → `AuthorizationStatus` | same |
| Auth amount | `Authorizations[0].Amount` → `Money` | same |
| Auth expiry | `Authorizations[0].ExpirationTime (expiration_time): string?` RFC3339 | `Models/Authorization.cs` |
| 3DS STOP | `Status == OrderStatus.PayerActionRequired` (wire `PAYER_ACTION_REQUIRED`). Also `PaymentSource.Card.AuthenticationResult.ThreeDSecure` (`AuthenticationStatus`, `EnrollmentStatus`). Do **not** follow `Links` with rel approve. | `enums.md`, `records-1-Ac-Pa.md` |

**`Orders.GetOrder`** — `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Order`. `fields` optional; XML: valid filter `payment_source`. Error Case A `GetOrderError`: `TryGetError(out Error)` [401, 404]. Cite: `map/operations/Orders.md`.

If `prefer=minimal` left authorizations empty, call `GetOrder` before failing.

⚠ Step 3 (calling) — many optional params have no C# default; positional calls mis-bind. **MUST load `dotnet-calling-endpoints`** before the first `client.Orders` / `client.Payments` / `client.Vault` call.

⚠ Step 3 (models) — enums are `StringEnum<T>` (static members / `FromValue`, not C# enums); `required` members must be in the object initializer; unmodeled JSON is dropped. **MUST load `dotnet-models`** before building `OrderRequest` / `CardRequest` / `Money`.

---

### 3. AUTHORIZE with a vaulted card (no PAN)

Same operations as §2. On `CardRequest` set **`VaultId (vault_id)`** to `PaymentTokenResponse.Id` from §8. Leave `Number` / `Expiry` / `SecurityCode` unset.

Do **not** use `PaymentSource.Token` / `OrderAuthorizeRequestPaymentSource.Token`. `PayPalServerSdk.Models.Token` requires `Type: TokenType` whose **only** member is `BillingAgreement` (wire `BILLING_AGREEMENT`) — that is **not** a vault payment token. (`records-2-Pa-Ve.md`, `enums.md`)

Optional stored-credential on `CardRequest.StoredCredential`: `PaymentInitiator` + `PaymentType` both `!req`. For a shopper-present saved-card checkout: `PaymentInitiator.Customer`, `StoredPaymentSourcePaymentType.OneTime`, `Usage = StoredPaymentSourceUsageType.Subsequent` (or default `Derived`). (`records-1-Ac-Pa.md`, `enums.md`)

---

### 4. CAPTURE (fulfilment)

#### `Payments.CaptureAuthorizedPayment` — `POST /v2/payments/authorizations/{authorization_id}/capture`

| | |
|---|---|
| Signature | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must pass explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` |
| Idempotency | `payPalRequestId` → `PayPal-Request-Id` (XML: stored **45 days**) |
| prefer | `"return=representation"` (needed for `seller_receivable_breakdown`) |
| Returns | `PayPalServerSdk.Models.CapturedPayment` |
| Error | Case A `SdkException<CaptureAuthorizedPaymentError>`. `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback. **409** = conflict (already captured / state conflict). |
| Cite | `map/operations/Payments.md`, `Api/Payments.cs` |

**`CaptureRequest`** — `records-1-Ac-Pa.md`

| Field (wire) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | omit to capture the full authorized amount; if set, must match remaining auth |
| `FinalCapture (final_capture)` | `bool?` default `false` | set **`true`** for fulfilment so the auth is completed |
| `InvoiceId (invoice_id)` | `string?` | optional |
| `NoteToPayer (note_to_payer)` | `string?` | optional |

**`CapturedPayment` — read after capture** (`records-1-Ac-Pa.md`)

| Need | Field (wire) | Type |
|---|---|---|
| Capture id | `Id (id)` | `string?` |
| Status | `Status (status)` | `CaptureStatus?` |
| Captured amount | `Amount (amount)` | `Money?` (`CurrencyCode`, `Value` strings) |
| PayPal fee | `SellerReceivableBreakdown.PaypalFee (paypal_fee)` | `Money?` |
| Net proceeds | `SellerReceivableBreakdown.NetAmount (net_amount)` | `Money?` |
| Gross | `SellerReceivableBreakdown.GrossAmount (gross_amount)` | `Money !req` |
| Fee breakdown present? | `SellerReceivableBreakdown` XML: **not available when capture is pending** | if `Status == CaptureStatus.Pending`, fee/net may be null — `GetCapturedPayment` later |

**`SellerReceivableBreakdown`**: `GrossAmount !req`, `PaypalFee?`, `PaypalFeeInReceivableCurrency?`, `NetAmount?`, `ReceivableAmount?`, `ExchangeRate?`, `PlatformFees?`. (`records-2-Pa-Ve.md`)

**`Payments.GetCapturedPayment(string captureId, string? payPalMockResponse, …)`** → `CapturedPayment`. Error `GetCapturedPaymentError`: `TryGetError` [401, 403, 404] · `TryGetNoContent` [500]. Cite: `map/operations/Payments.md`.

Persist: capture id, `CaptureStatus`, captured `Amount`, `PaypalFee`, `NetAmount`.

---

### 5. RENEW a stale authorization

**What “stale” is in this SDK**

`AuthorizationStatus` members: `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending` — **no `Expired` member**. (`enums.md`)

Stale is **time-based**, not a status enum:

- `PaymentAuthorization.ExpirationTime` / `AuthorizationWithAdditionalData.ExpirationTime` (`expiration_time`, RFC3339, seconds required). (`Models/Authorization.cs`, `records-2-Pa-Ve.md`)
- Operation notes on `ReauthorizePayment`: initial **3-day honor period**; reauthorize **from day 4 to day 29** after that; a reauthorized payment gets a **new 3-day honor period**. **If 30 days since the original authorization, you must create a new authorized payment** (new `CreateOrder` + `AuthorizeOrder` with card or `vault_id`) **instead of reauthorizing**. (`map/operations/Payments.md`, `Api/Payments.cs`)

**Map disagreement (cite both; do not pick):** operation notes say you can issue **multiple** re-authorizations within 29 days; `ReauthorizeRequest` summary says you can reauthorize **only once** from days 4–29. (`map/operations/Payments.md` vs `records-2-Pa-Ve.md`) Treat a 422 from reauthorize after a first success as “reauthorization no longer possible” and tell the operator to take a **new** authorization.

Before capture: `GetAuthorizedPayment`. If `ExpirationTime` is in the past **or** capture returns 422 with `Error.Details[].Issue` indicating the auth cannot be captured, call `ReauthorizePayment`. If reauthorize fails, surface `Error.Name`, `Message`, `Details[].Issue`, `Details[].Description`, `DebugId` to the operator — do not fail silently.

#### `Payments.GetAuthorizedPayment` — `GET /v2/payments/authorizations/{authorization_id}`

`GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `PaymentAuthorization`. Error `GetAuthorizedPaymentError`: `TryGetError` [401, 403, 404] · `TryGetNoContent` [500]. Cite: `map/operations/Payments.md`.

#### `Payments.ReauthorizePayment` — `POST /v2/payments/authorizations/{authorization_id}/reauthorize`

| | |
|---|---|
| Signature | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must pass explicitly | `payPalRequestId`, `payPalAuthAssertion`, `body` |
| Body | `ReauthorizeRequest { Amount = Money? }` — **supports only `amount`**. Pass the original hold `Money` (same currency/value as the order total). |
| Returns | `PaymentAuthorization` — **new** `Id`, `Status`, `ExpirationTime`. **Replace** the persisted authorization id with this id before capture. |
| Error | Case A `SdkException<ReauthorizePaymentError>`. `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent` [500] · `TryGetRawError` fallback |
| Idempotency | `payPalRequestId` (45 days) |
| prefer | `"return=representation"` |
| Cite | `map/operations/Payments.md` |

When reauthorize is no longer possible (30-day window / 422): operator message must say a **new** card/vault authorization is required; fulfilment cannot proceed on the old id.

---

### 6. VOID (cancel before capture)

#### `Payments.VoidPayment` — `POST /v2/payments/authorizations/{authorization_id}/void`

| | |
|---|---|
| Signature | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must pass explicitly | `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` |
| Returns | `PaymentAuthorization` — expect `Status == AuthorizationStatus.Voided` |
| Notes | **Cannot void a fully captured authorization.** |
| Error | Case A `SdkException<VoidPaymentError>`. `TryGetError(out Error)` [401, 403, 404, **409**, 422] · `TryGetNoContent` [500] · `TryGetRawError` fallback. 409 = already voided / captured conflict. |
| Idempotency | `payPalRequestId` (45 days) |
| Cite | `map/operations/Payments.md` |

---

### 7. REFUND a capture (full and partial)

#### `Payments.RefundCapturedPayment` — `POST /v2/payments/captures/{capture_id}/refund`

| | |
|---|---|
| Signature | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must pass explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` |
| Full refund | `body: null` **or** `new RefundRequest()` with no `Amount` (XML: empty payload) |
| Partial refund | `body: new RefundRequest { Amount = new Money { CurrencyCode, Value } }` — `Value` string, same currency as capture |
| Caller idempotency key | **`payPalRequestId`** → `PayPal-Request-Id` (stored **45 days**). Pass the caller-supplied key here. |
| prefer | `"return=representation"` |
| Returns | `PayPalServerSdk.Models.Refund` |
| Error | Case A `SdkException<RefundCapturedPaymentError>`. `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent` [500] · `TryGetRawError` fallback. Over-refund / already-refunded → **422 or 409**; read `Error.Details[].Issue` (untyped string — **no issue enum in the SDK**). |
| Cite | `map/operations/Payments.md`, `Api/Payments.cs` |

**`RefundRequest`**: `Amount (amount): Money?`, `CustomId?`, `InvoiceId?`, `NoteToPayer?`, `PaymentInstruction?`. (`records-2-Pa-Ve.md`)

**`Refund` to persist**: `Id (id)`, `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown.TotalRefundedAmount (total_refunded_amount): Money?`.

**Remaining refundable — GAP as a first-class field:** `CapturedPayment` has **no** remaining-refundable member. Compute:

`remaining = captured Amount.Value − Refund.SellerPayableBreakdown.TotalRefundedAmount` (after each refund), or sum persisted refund `Amount`s.

Refuse in-app before calling PayPal if requested refund > remaining. `CaptureStatus.PartiallyRefunded` / `Refunded` / `Completed` on `GetCapturedPayment` is the PayPal-side status. `CaptureStatus.Refunded` means nothing left to refund.

**`Payments.GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, …)`** → `Refund`. Error `GetRefundError`: `TryGetError` [401, 403, 404]. Cite: `map/operations/Payments.md`.

---

### 8. VAULT a card (save payment method)

No separate “create customer” operation exists on `client.Vault` (or any controller). Customer is created/associated **on** `CreatePaymentToken`.

Pass `PaymentTokenRequest.Customer.MerchantCustomerId` = eShop shopper id (not the PAN). Persist returned `Customer.Id` (PayPal-generated) **and** `MerchantCustomerId` on the shopper record — **never** PAN/CVC/expiry-full.

#### `Vault.CreatePaymentToken` — `POST /v3/vault/payment-tokens`

| | |
|---|---|
| Signature | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must pass explicitly | `payPalRequestId` |
| Idempotency | `payPalRequestId` → `PayPal-Request-Id` (XML: stored **3 hours**) |
| Returns | `PayPalServerSdk.Models.PaymentTokenResponse` |
| Error | Case A `SdkException<CreatePaymentTokenError>`. **`TryGetError1(out Error1)`** [400, 403, 404, 422, 500] · `TryGetRawError` fallback. **401 is not in the typed list** → `TryGetRawError` / `RawError.StatusCode`. |
| Cite | `map/operations/Vault.md`, `Api/Vault.cs` |

**`PaymentTokenRequest`** — `records-2-Pa-Ve.md`

| Field (wire) | Type | Required |
|---|---|---|
| `Customer (customer)` | `Customer?` | set it |
| `PaymentSource (payment_source)` | `PaymentTokenRequestPaymentSource` | **!req** |

**`Customer`**: `Id (id): string?` — PayPal-generated, set only if already known; `MerchantCustomerId (merchant_customer_id): string?` — eShop shopper key, 1–64, regex `^[0-9a-zA-Z-_.^*$@#]+$`. (`Models/Customer.cs`)

**`PaymentTokenRequestPaymentSource`**: `Card (card): PaymentTokenRequestCard?` (or `Token: VaultTokenRequest` to promote a setup token — not needed for no-browser PAN vault).

**`PaymentTokenRequestCard`**: `Name?`, `Number?`, `Expiry?` (`YYYY-MM`), `SecurityCode?`, `Brand?: CardBrand?`, `BillingAddress?: Address?`. Same digit/expiry rules as `CardRequest`. (`records-2-Pa-Ve.md`, `Models/PaymentTokenRequestCard.cs`)

**`PaymentTokenResponse` — safe description, no PAN**

| Field (wire) | Type | Use |
|---|---|---|
| `Id (id)` | `string?` | **vault token** — this is later `CardRequest.VaultId` |
| `Customer (customer)` | `CustomerResponse?` | persist `Id` + `MerchantCustomerId` |
| `PaymentSource.Card (payment_source.card)` | `CardPaymentTokenEntity?` | last digits / brand / expiry |
| `Links` | `IReadOnlyList<LinkDescription>?` | if a payer-action link appears → **STOP** (3DS); do not follow |

**`CardPaymentTokenEntity`**: `Name?`, `LastDigits (last_digits)?`, `Brand (brand): CardBrand?`, `Expiry (expiry)?`, `BillingAddress?`, `Type: CardType?`, … — **no `Number` / `SecurityCode`**. (`records-1-Ac-Pa.md`)

`PaymentTokenResponse` has **no** `Status` field. 3DS on vault is modeled on **setup tokens**: `SetupTokenResponse.Status: PaymentTokenStatus` including `PayerActionRequired`. This integration uses `CreatePaymentToken` (no browser). If PayPal still requires a challenge, **STOP** and report; do **not** call `CreateSetupToken` to start an approval loop.

**Out of scope unless 3DS STOP handling needs the type:** `CreateSetupToken` / `GetSetupToken` exist (`map/operations/Vault.md`) but `SetupTokenRequestCard.ExperienceContext` carries `ReturnUrl`/`CancelUrl` — that is the browser path the product forbids.

Client XML: Vault API is *Available in the US only.* (`PayPalServerSdkClient.cs`)

---

### 9. LIST vaulted payment methods

#### `Vault.ListCustomerPaymentTokens` — `GET /v3/vault/payment-tokens`

| | |
|---|---|
| Signature | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Query | `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired` |
| Returns | `CustomerVaultPaymentTokensResponse` |
| Error | Case A `SdkException<ListCustomerPaymentTokensError>`. `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError` fallback |
| Pagination | **no helper**. Default `pageSize=5`, `page=1`, `totalRequired=false`. Pass `totalRequired: true` so `TotalPages`/`TotalItems` are populated; loop `page = 1 .. TotalPages`. |
| Cite | `map/operations/Vault.md`, `Api/Vault.cs` |

**Which id is `customerId`?** Query wire name is `customer_id`. `Customer.Id` XML: PayPal-generated. `ListCustomerPaymentTokens` param XML: “identifier representing a specific customer in merchant's/partner's system or records”. **UNVERIFIED** which of `Customer.Id` vs `MerchantCustomerId` the list filter accepts — persist both from create; pass **`Customer.Id` first** (wire `customer_id`); if the list is empty right after a successful vault, the XML allows trying `MerchantCustomerId`. Do not invent a CreateCustomer call.

**`CustomerVaultPaymentTokensResponse`**: `TotalItems?`, `TotalPages?`, `Customer: VaultResponseCustomer?`, `PaymentTokens: IReadOnlyList<PaymentTokenResponse>?` (each has `Id` + `PaymentSource.Card.LastDigits`/`Brand`/`Expiry`), `Links?`. (`records-1-Ac-Pa.md`)

---

### 10. DELETE a vaulted payment method

#### `Vault.DeletePaymentToken` — `DELETE /v3/vault/payment-tokens/{id}`

| | |
|---|---|
| Signature | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `id` | `PaymentTokenResponse.Id` (vault token), **not** customer id |
| Returns | `void` (`Task`) |
| Error | Case A `SdkException<DeletePaymentTokenError>`. `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError` fallback |
| Cite | `map/operations/Vault.md` |

After delete: `ListCustomerPaymentTokens` must not return it; `CreateOrder`/`AuthorizeOrder` with that `vault_id` must fail (typed vault/orders error — surface `Issue`). Optional `GetPaymentToken(string id)` → `PaymentTokenResponse`; error `GetPaymentTokenError`: `TryGetError1` [403, 404, 422, 500] (404 after delete).

---

### 11. Transaction search / reporting

#### `TransactionSearch.SearchTransactions` — `GET /v1/reporting/transactions`

| | |
|---|---|
| Signature | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must pass explicitly | `transactionId` … `terminalId` (8 params, null to skip) |
| `startDate` / `endDate` | RFC3339 / ISO-8601 date-times; **seconds required**; fractional optional. Query: `start_date`, `end_date`. XML: **maximum range 31 days per call**. Chunk the caller’s from/to into ≤31-day windows, then page each window. |
| `fields` | default `"transaction_info"` (includes id, amounts, **PayPal fee**, status). `"all"` for payer/shipping/cart too. |
| `page` / `pageSize` | default `page=1`, `pageSize=100`. Map: **no pagination helper**. Loop `page` while `page < TotalPages` (and `TotalPages` null + empty `TransactionDetails` = done / lag). |
| Returns | `PayPalServerSdk.Models.SearchResponse` |
| Error | **Case B** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — **only Case B operation in this SDK**. Read `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`. Do **not** catch `SearchTransactionsError`. |
| Lag | XML: up to **three hours** before executed transactions appear; empty range is expected. Lists up to previous three years. |
| Cite | `map/operations/TransactionSearch.md`, `Api/TransactionSearch.cs` |

**`SearchResponse`**: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `StartDate`, `EndDate`, `LastRefreshedDatetime`, `Page`, `TotalItems`, `TotalPages`, `Links`. (`records-2-Pa-Ve.md`)

**Match to eShop orders** from `TransactionDetails.TransactionInfo` (`TransactionInformation`):

| Field (wire) | Use |
|---|---|
| `TransactionId (transaction_id)` | PayPal txn id |
| `PaypalReferenceId (paypal_reference_id)` + `PaypalReferenceIdType` | related order/auth/capture |
| `InvoiceId (invoice_id)` | match `PurchaseUnitRequest.InvoiceId` |
| `CustomField (custom_field)` | match `CustomId` |
| `TransactionAmount (transaction_amount)` | `Money` |
| `FeeAmount (fee_amount)` | `Money` |
| `TransactionStatus (transaction_status)` | `string?` (not an enum) |
| `TransactionInitiationDate` / `TransactionUpdatedDate` | RFC3339 strings |

`SearchBalances` exists but is **out of scope** for order-line reconciliation.

---

### 12. Error types — all in-scope operations

`SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`) exposes **only** `.Error` — **no `.StatusCode`**. For Case A, HTTP status is grouped on the accessor; distinguish 401 vs 422 via `Error.Name` / `Details[].Issue` when both map to the same `TryGetError`. For Case B / fallback, `RawError.StatusCode`. (`sdk-map.md`, `Core/Exceptions/SdkException.cs`)

**`Error` (`PayPalServerSdk.Models`)**: `Name !req`, `Message !req`, `DebugId !req`, `Details?: IReadOnlyList<ErrorDetails>`, `Links?`.

**`ErrorDetails`**: `Issue (issue): string !req` (fine-grained code — **not an enum**; **do not hard-code issue strings from memory**), `Description?`, `Field?`, `Value?`, `Location?`.

**`Error1` (vault)**: same shape with `Details: IReadOnlyList<ErrorDetails1>`, `Links: IReadOnlyList<ErrorLinkDescription>`.

| Situation | How to read | Typical accessor |
|---|---|---|
| 401 / 403 | Case A: `TryGetError`/`TryGetError1` **if that status is in the op’s list**; else `TryGetRawError` → `RawError.StatusCode` + `ReadAsString()`. CreateOrder typed list includes **401** not 403; Vault create typed list includes **403** not 401. | per-op row |
| Card declined | `CreateOrderError`/`AuthorizeOrderError` `TryGetError` (400/422). Surface `Error.Name`, `Message`, `Details[].Issue`/`Description`. On a returned auth, `ProcessorResponse.ResponseCode` (`ProcessorResponseCode` StringEnum) / `CvvCode` / `AvsCode`. | `records-1-Ac-Pa.md`, `enums.md` |
| 3DS / challenge required | **Success-path status**, not necessarily an exception: `Order.Status == OrderStatus.PayerActionRequired`. Vault setup: `PaymentTokenStatus.PayerActionRequired`. **STOP — do not follow Links.** | `enums.md` |
| Authorization expired / not capturable | No `Expired` status. Capture/reauthorize **422** → `TryGetError` → `Details.Issue`. Also compare `ExpirationTime` to now. | `enums.md`, `Payments.md` |
| Already captured | Capture **409** `TryGetError`; `AuthorizationStatus.Captured` on GET | `Payments.md` |
| Already voided | Void **409** / **422** `TryGetError`; `AuthorizationStatus.Voided` | `Payments.md` |
| Refund exceeds captured | Refund **422** or **409** `TryGetError`; in-app remaining check first | `Payments.md` |
| Vault errors | `TryGetError1(out Error1)` — read `Name`/`Message`/`Details[].Issue` | `Vault.md` |
| Transaction search HTTP error | `SdkException<RawError>` only | `TransactionSearch.md` |

No-throw `…Result` variants: **absent** on every operation. (`sdk-map.md`)

⚠ Step 12 (error boundary) — Case A vs Case B differs per operation (`TryGetError` vs `TryGetError1` vs raw); `TryGetRawError` is not a catch-all on typed errors. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.

A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

### 13. Idempotency (`PayPal-Request-Id`)

| Operation | Parameter | Header | Server stores key |
|---|---|---|---|
| `CreateOrder` | `payPalRequestId` | `PayPal-Request-Id` | 6 hours (mandatory with payment_source) |
| `AuthorizeOrder` | `payPalRequestId` | same | 6 hours |
| `CaptureAuthorizedPayment` | `payPalRequestId` | same | 45 days |
| `ReauthorizePayment` | `payPalRequestId` | same | 45 days |
| `VoidPayment` | `payPalRequestId` | same | 45 days |
| `RefundCapturedPayment` | `payPalRequestId` | same | 45 days — **this is the caller-supplied refund idempotency key** |
| `CreatePaymentToken` | `payPalRequestId` | same | 3 hours |

GETs (`GetOrder`, `GetAuthorizedPayment`, `GetCapturedPayment`, `GetRefund`, `GetPaymentToken`, `ListCustomerPaymentTokens`, `SearchTransactions`) have no `payPalRequestId`. `DeletePaymentToken` has none.

Pass a **stable** key per logical action (e.g. `eshop-authorize-{orderId}`, `eshop-refund-{orderId}-{callerKey}`).

---

### 14. Amounts

| | |
|---|---|
| Type | `PayPalServerSdk.Models.Money` and `AmountWithBreakdown` — **`Value` is `string`, not `decimal`/`double`** |
| Currency | `CurrencyCode (currency_code): string !req` — 3-char ISO-4217 from config |
| Scale | XML: integer for JPY-like; decimal fraction for others. Format eShop `decimal` to the cent for USD-style (`"12.34"`) before assign. Parse back with `decimal.TryParse` using invariant culture. |
| Equality to order total | `PurchaseUnitRequest.Amount.Value` must equal the eShop order total string to the cent |
| Cite | `records-1-Ac-Pa.md`, `Models/Money.cs`, `Models/AmountWithBreakdown.cs` |

---

### 15. Customer / vault setup before saving a card

| | |
|---|---|
| CreateCustomer operation | **Does not exist.** Vault ops: `CreatePaymentToken`, `CreateSetupToken`, `DeletePaymentToken`, `GetPaymentToken`, `GetSetupToken`, `ListCustomerPaymentTokens` only. (`map/operations/Vault.md`) |
| Before save | Signed-in shopper id → `PaymentTokenRequest.Customer.MerchantCustomerId`. Do not send a fake `Customer.Id`. |
| After save | Persist `PaymentTokenResponse.Customer.Id` (PayPal customer id) + `PaymentTokenResponse.Id` (vault token) + last4/brand/expiry. |
| Pay with saved card | `CardRequest.VaultId = PaymentTokenResponse.Id` |
| List | `ListCustomerPaymentTokens(customerId: persisted PayPal Customer.Id, …)` — see UNVERIFIED note in §9 |

---

### Enums actually needed (`PayPalServerSdk.Models.Enums`, `map/models/enums.md`)

| Enum | Members to use (C# = wire) |
|---|---|
| `CheckoutPaymentIntent` | `Authorize (AUTHORIZE)`, `Capture (CAPTURE)` — **use Authorize** |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, … `Unknown (UNKNOWN)` — **read** from vault/card response; do not require the shopper to send it |
| `PaymentTokenStatus` | `Created`, `PayerActionRequired`, `Approved`, `Vaulted`, `Tokenized` — setup-token status (**STOP** on `PayerActionRequired`) |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` |
| `TokenType` | **only** `BillingAgreement (BILLING_AGREEMENT)` — not for vault cards |
| `VaultTokenRequestType` | **only** `SetupToken (SETUP_TOKEN)` |
| `StoreInVaultInstruction` | **only** `OnSuccess (ON_SUCCESS)` — only if vault-on-authorize via `CardAttributes.Vault` (Flow 2 uses `CreatePaymentToken` instead) |
| `OrdersCardVerificationMethod` | `ScaAlways`, `ScaWhenRequired` (default), `_3DSecure`, `AvsCvv` |
| `ParesStatus` / `EnrollmentStatus` / `LiabilityShiftIndicator` | 3DS result fields — if present and challenge indicated, **STOP** |

Construct as `CheckoutPaymentIntent.Authorize`, not `CheckoutPaymentIntent.AUTHORIZE`.

---

### PayPal-owned state to persist on the eShop payment

| Field | Source |
|---|---|
| PayPal order id | `Order.Id` |
| PayPal order status | `Order.Status` |
| Authorization id | `PurchaseUnits[].Payments.Authorizations[].Id` then overwritten by `ReauthorizePayment` → `PaymentAuthorization.Id` |
| Authorization status / expiration | `AuthorizationStatus`, `ExpirationTime` |
| Capture id | `CapturedPayment.Id` |
| Capture status / amount / fee / net | `CaptureStatus`, `Amount`, `SellerReceivableBreakdown.PaypalFee`, `NetAmount` |
| Refund ids / amounts / statuses / total refunded | `Refund.Id`, `Amount`, `RefundStatus`, `SellerPayableBreakdown.TotalRefundedAmount` |
| Vault: PayPal customer id, vault token id, last4, brand, expiry | `CustomerResponse.Id`, `PaymentTokenResponse.Id`, `CardPaymentTokenEntity` |

`PurchaseUnitRequest.CustomId` should equal the eShop order id so `SearchTransactions` can line up.

---

## Trap notes

⚠ Step 1 (client registration) — the SDK client vs `HttpClient` lifetime, and ctor vs `AddPayPalServerSdkClient`, will leak or duplicate handlers if guessed from the signature. **MUST load `dotnet-client-initialization`**.

⚠ Step 1 (auth) — credential property names and when they must be set are easy to mis-wire (`Oauth2` vs a made-up `ClientId` on options). **MUST load `dotnet-authentication`**.

⚠ Step 1 (resilience / BaseUrl) — retry/timeout options do **not** bound a whole call and are **not** the `HttpClient` timeout; transport retries can re-send writes. **MUST load `dotnet-configuration-resilience`**.

⚠ Step 3–11 (calling) — nullable no-default parameters mis-bind positionally; cancellation is `ct:`. **MUST load `dotnet-calling-endpoints`**.

⚠ Step 3–11 (models) — `StringEnum<T>`, `required` init, string amounts, dropped extra JSON. **MUST load `dotnet-models`**.

⚠ Step 12 (errors) — Case A `TryGetError` vs vault `TryGetError1` vs search Case B `RawError`; `SdkException<T>` has no status code. **MUST load `dotnet-error-handling`**.

⚠ Step 12 — `JsonException` on **2xx** missing `required` is **not** `SdkException`. **MUST load `dotnet-error-handling`**.

⚠ Step 12 — `JsonException` while constructing a non-2xx `{Operation}Error` **replaces** `SdkException` and destroys HTTP status; mapping all `JsonException` to 5xx mis-classifies deterministic rejects. **MUST load `dotnet-error-handling`**.

⚠ Tests — the `HttpClient` constructor argument is the seam; do not fake controller internals. **MUST load `dotnet-testing`**.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — ctor / DI / `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `Oauth2` / `OAuth2ClientCredentials` |
| `dotnet-configuration-resilience` | Step 1 — `Retry`, timeouts, BaseUrl, SearchTransactions/List paging loops |
| `dotnet-calling-endpoints` | Steps 3–11 — every operation call, named args, `ct:` |
| `dotnet-models` | Steps 3–11 — requests, enums, `Money` strings, vault/card records |
| `dotnet-error-handling` | Step 12 — every catch ladder, Case A/B, both `JsonException` directions |
| `dotnet-testing` | Tests for the integration layer |

---

## Assumptions & Blockers

**Assumptions**

- Fulfilment captures with `Payments.CaptureAuthorizedPayment` (authorization id), not `Orders.CaptureOrder`.
- One purchase unit per eShop order; amount string equals the order total to the cent in config currency.
- Vault pay uses `CardRequest.VaultId`, not `Token`.
- Reconciliation uses `SearchTransactions` windows of ≤31 days, `fields` at least `transaction_info`, matched via `invoice_id` / `custom_field` / amounts.
- Config `Environment` is Sandbox; optional `BaseUrl` is written verbatim to `options.Server.Default.Sandbox.BaseUrl`.

**Blockers / GAPs (do not work around with unmapped APIs)**

- **GAP:** no `CreateCustomer` (or equivalent) operation. Customer id is a side effect of `CreatePaymentToken`.
- **GAP:** `ServerEnvironment` has **only** `Sandbox`. A config value of Production cannot be expressed.
- **GAP:** `AuthorizationStatus` has **no** expired member; expiry is `expiration_time` + error `Issue` strings (untyped).
- **GAP:** no remaining-refundable field on `CapturedPayment`; compute from captured amount vs `total_refunded_amount` / persisted refunds.
- **GAP:** `ErrorDetails.Issue` is a free `string` — the SDK does not enumerate decline / expired-auth / over-refund issue codes.
- **GAP:** `SdkException<T>` has no HTTP status property; grouped accessors share one `TryGetError` for 401 and 422 on several ops.
- **GAP / product stop:** 3DS/challenge (`PAYER_ACTION_REQUIRED` or setup-token `PayerActionRequired`) — do **not** implement an approval round-trip; report and stop.
- **GAP:** `SearchTransactions` max **31 days** per request (XML). Covering a longer from/to requires multiple calls, not one.
- **UNVERIFIED:** whether list `customerId` is PayPal `Customer.Id` or `MerchantCustomerId` (param XML vs field XML disagree). Persist both.
- **UNVERIFIED:** whether the SDK’s per-call random `Idempotency-Key` header interacts with `PayPal-Request-Id` dedupe. Still pass stable `payPalRequestId`.
- **UNVERIFIED (map disagreement):** reauthorize allowed once vs multiple times within 29 days (`ReauthorizeRequest` summary vs `ReauthorizePayment` notes).
- Vault controller XML: available in the US only (`PayPalServerSdkClient.cs`).
- `PaymentTokenResponse` has no `Status`; 3DS-on-vault is only first-class on setup tokens.

Empty `SearchTransactions` for a recent range is **not** a GAP (up to three hours lag).

---

## CANONICAL COMPILE-READY CONTRACT

Picked from SDK source (`Api/*.cs`, models, `PayPalServerSdkClientOptions.cs`) and `map/operations/*.md`. Where the sheet above used a short name and an FQ name, **this section is the only one to implement against.**

**Resolved identifiers (the real C# name only):**

| Question | Canonical identifier |
|---|---|
| Package vs namespace | NuGet ID **`AsadAli.Checkout.Sdk`**. Root namespace **`PayPalServerSdk`**. Not interchangeable. |
| Create / authorize / capture / reauth / void / refund / search / vault | `CreateOrder`, `AuthorizeOrder`, `CaptureAuthorizedPayment`, `ReauthorizePayment`, `VoidPayment`, `RefundCapturedPayment`, `SearchTransactions`, `CreatePaymentToken`, `ListCustomerPaymentTokens`, `DeletePaymentToken` |
| Idempotency param | **`payPalRequestId`** (not `paypalRequestId`, not `cancellationToken`) |
| Prefer complete body | pass **`prefer: "return=representation"`** (string). SDK default is `"return=minimal"`. |
| Cancellation | **`ct`** |
| Vault-to-pay | **`CardRequest.VaultId`** (wire `vault_id`) |
| Create body type | **`OrderRequest`** |
| Intent | **`CheckoutPaymentIntent.Authorize`** (wire `AUTHORIZE`) |
| Amount strings | create: **`AmountWithBreakdown.Value`**; capture/reauth/refund: **`Money.Value`**. Both are `string`. |
| Fee / net | **`SellerReceivableBreakdown.PaypalFee`**, **`.NetAmount`** |
| Auth expiry | **`ExpirationTime`** on `AuthorizationWithAdditionalData` / `PaymentAuthorization` |
| Merchant customer | **`Customer.MerchantCustomerId`** |
| Reporting controller | **`client.TransactionSearch`** |
| Exception | **`PayPalServerSdk.Core.Exceptions.SdkException<TError>`** |
| Orders/Payments errors | **`TryGetError(out PayPalServerSdk.Models.Error)`** |
| Vault errors | **`TryGetError1(out PayPalServerSdk.Models.Error1)`** — not `TryGetError` |

---

### 1. Package

| | |
|---|---|
| NuGet ID | `AsadAli.Checkout.Sdk` (`PayPalServerSdk.csproj` `<PackageId>`) |
| Version in Directory.Packages.props | **version-less** (`dotnet add package AsadAli.Checkout.Sdk` with no version). Do not pin from memory. Mapped release tag is `v1.0.1`; source `<Version>` in that tag is `1.0.0` — install latest from NuGet, not a guessed pin. |
| Root namespace | `PayPalServerSdk` |
| Client | `PayPalServerSdk.PayPalServerSdkClient` |
| Options | `PayPalServerSdk.PayPalServerSdkClientOptions` |

---

### 2. Client construction

`AddPayPalServerSdkClient` calls `IHttpClientFactory.CreateClient()` **with no name** (`ServiceCollectionExtensions.cs`). To use a **named** `HttpClient`, do **not** call `AddPayPalServerSdkClient`. Use the public ctor.

```csharp
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

services.AddHttpClient("PayPal");
services.AddSingleton(sp =>
{
    var options = new PayPalServerSdk.PayPalServerSdkClientOptions
    {
        Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox,
        Oauth2 = new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials
        {
            ClientId = clientId,       // required string
            ClientSecret = clientSecret // required string
        },
        Retry = PayPalServerSdk.Core.Configuration.RetryOptions.Default() with
        {
            MaxRetries = maxRetries,          // int
            Timeout = TimeSpan.FromSeconds(n) // TimeSpan?
        }
    };
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        // Applies to every API path AND /v1/oauth2/token (Server.Default → DefaultOptions.Sandbox.BaseUrl)
        options.Server.Default.Sandbox.BaseUrl = baseUrl;
    }
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("PayPal");
    return new PayPalServerSdk.PayPalServerSdkClient(httpClient, options);
});
```

Exact paths:

- Credentials: `options.Oauth2` type `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials` (`ClientId`, `ClientSecret`, optional `Scope`)
- Environment: `options.Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox` (only member)
- BaseUrl: `options.Server.Default.Sandbox.BaseUrl` — types `PayPalServerSdk.ServerOptions` → `PayPalServerSdk.Servers.DefaultOptions` → nested `SandboxOptions.BaseUrl`
- Retry: `options.Retry` type `PayPalServerSdk.Core.Configuration.RetryOptions`. All members `required`; start from `RetryOptions.Default()` then `with { MaxRetries, Timeout }`. Path: `options.Retry.MaxRetries`, `options.Retry.Timeout`.

---

### 3. Operations (one signature each)

Controller properties: `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch`.  
`RequestOptions` = `PayPalServerSdk.Core.RequestOptions`.  
`SdkException<T>` = `PayPalServerSdk.Core.Exceptions.SdkException<T>`.  
`RawError` = `PayPalServerSdk.Core.ErrorResponse.RawError`.  
Every Case A type also has inherited `TryGetRawError(out RawError)` from `ApiError`. Nullable params without defaults **must** be passed (`null` to skip). Named args: `ct:` not `cancellationToken:`.

#### A. Create order — `client.Orders.CreateOrder`

```csharp
Task<PayPalServerSdk.Models.Order> CreateOrder(
    string? payPalMockResponse,
    string? payPalRequestId,
    string? payPalPartnerAttributionId,
    string? payPalClientMetadataId,
    string? payPalAuthAssertion,
    PayPalServerSdk.Models.OrderRequest body,
    string? prefer = "return=minimal",
    PayPalServerSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default);
```

Call: `prefer: "return=representation"`, `payPalRequestId: <stable key>`.  
Exception: `SdkException<PayPalServerSdk.Errors.CreateOrderError>`  
Accessors: `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback.  
Cite: `map/operations/Orders.md`

#### B. Authorize order — `client.Orders.AuthorizeOrder`

```csharp
Task<PayPalServerSdk.Models.OrderAuthorizeResponse> AuthorizeOrder(
    string id,
    string? payPalMockResponse,
    string? payPalRequestId,
    string? payPalClientMetadataId,
    string? payPalAuthAssertion,
    PayPalServerSdk.Models.OrderAuthorizeRequest? body,
    string? prefer = "return=minimal",
    PayPalServerSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default);
```

Exception: `SdkException<PayPalServerSdk.Errors.AuthorizeOrderError>`  
`TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError` fallback.

#### C. Get order — `client.Orders.GetOrder`

```csharp
Task<PayPalServerSdk.Models.Order> GetOrder(
    string id,
    string? fields,
    string? payPalMockResponse,
    string? payPalAuthAssertion,
    PayPalServerSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default);
```

Exception: `SdkException<PayPalServerSdk.Errors.GetOrderError>`  
`TryGetError(out Error)` [401, 404] · `TryGetRawError` fallback.

#### D. Capture authorized payment — `client.Payments.CaptureAuthorizedPayment`

```csharp
Task<PayPalServerSdk.Models.CapturedPayment> CaptureAuthorizedPayment(
    string authorizationId,
    string? payPalMockResponse,
    string? payPalRequestId,
    string? payPalAuthAssertion,
    PayPalServerSdk.Models.CaptureRequest? body,
    string? prefer = "return=minimal",
    PayPalServerSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default);
```

Exception: `SdkException<PayPalServerSdk.Errors.CaptureAuthorizedPaymentError>`  
`TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback.

#### E. Get authorized payment — `client.Payments.GetAuthorizedPayment`

```csharp
Task<PayPalServerSdk.Models.PaymentAuthorization> GetAuthorizedPayment(
    string authorizationId,
    string? payPalMockResponse,
    string? payPalAuthAssertion,
    PayPalServerSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default);
```

Exception: `SdkException<PayPalServerSdk.Errors.GetAuthorizedPaymentError>`  
`TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback.

#### F. Reauthorize payment — `client.Payments.ReauthorizePayment`

```csharp
Task<PayPalServerSdk.Models.PaymentAuthorization> ReauthorizePayment(
    string authorizationId,
    string? payPalRequestId,
    string? payPalAuthAssertion,
    PayPalServerSdk.Models.ReauthorizeRequest? body,
    string? prefer = "return=minimal",
    PayPalServerSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default);
```

Exception: `SdkException<PayPalServerSdk.Errors.ReauthorizePaymentError>`  
`TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback.

#### G. Void payment — `client.Payments.VoidPayment`

```csharp
Task<PayPalServerSdk.Models.PaymentAuthorization> VoidPayment(
    string authorizationId,
    string? payPalMockResponse,
    string? payPalAuthAssertion,
    string? payPalRequestId,
    string? prefer = "return=minimal",
    PayPalServerSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default);
```

Exception: `SdkException<PayPalServerSdk.Errors.VoidPaymentError>`  
`TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback.

#### H. Refund captured payment — `client.Payments.RefundCapturedPayment`

```csharp
Task<PayPalServerSdk.Models.Refund> RefundCapturedPayment(
    string captureId,
    string? payPalMockResponse,
    string? payPalRequestId,
    string? payPalAuthAssertion,
    PayPalServerSdk.Models.RefundRequest? body,
    string? prefer = "return=minimal",
    PayPalServerSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default);
```

Exception: `SdkException<PayPalServerSdk.Errors.RefundCapturedPaymentError>`  
`TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback.

#### I. Get captured payment — `client.Payments.GetCapturedPayment`

```csharp
Task<PayPalServerSdk.Models.CapturedPayment> GetCapturedPayment(
    string captureId,
    string? payPalMockResponse,
    PayPalServerSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default);
```

Exception: `SdkException<PayPalServerSdk.Errors.GetCapturedPaymentError>`  
`TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback.

#### J. Get refund — `client.Payments.GetRefund`

```csharp
Task<PayPalServerSdk.Models.Refund> GetRefund(
    string refundId,
    string? payPalMockResponse,
    string? payPalAuthAssertion,
    PayPalServerSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default);
```

Exception: `SdkException<PayPalServerSdk.Errors.GetRefundError>`  
`TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback.

#### K. Create payment token — `client.Vault.CreatePaymentToken`

```csharp
Task<PayPalServerSdk.Models.PaymentTokenResponse> CreatePaymentToken(
    string? payPalRequestId,
    PayPalServerSdk.Models.PaymentTokenRequest body,
    PayPalServerSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default);
```

Exception: `SdkException<PayPalServerSdk.Errors.CreatePaymentTokenError>`  
**`TryGetError1(out PayPalServerSdk.Models.Error1)`** [400, 403, 404, 422, 500] · `TryGetRawError` fallback. (401 → fallback `RawError.StatusCode`.)

#### L. List customer payment tokens — `client.Vault.ListCustomerPaymentTokens`

```csharp
Task<PayPalServerSdk.Models.CustomerVaultPaymentTokensResponse> ListCustomerPaymentTokens(
    string customerId,
    int? pageSize = 5,
    int? page = 1,
    bool? totalRequired = false,
    PayPalServerSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default);
```

Exception: `SdkException<PayPalServerSdk.Errors.ListCustomerPaymentTokensError>`  
`TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError` fallback.

#### M. Delete payment token — `client.Vault.DeletePaymentToken`

```csharp
Task DeletePaymentToken(
    string id,
    PayPalServerSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default);
```

Exception: `SdkException<PayPalServerSdk.Errors.DeletePaymentTokenError>`  
`TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError` fallback.

#### N. Search transactions — `client.TransactionSearch.SearchTransactions`

```csharp
Task<PayPalServerSdk.Models.SearchResponse> SearchTransactions(
    string startDate,
    string endDate,
    string? transactionId,
    string? transactionType,
    string? transactionStatus,
    string? transactionAmount,
    string? transactionCurrency,
    string? paymentInstrumentType,
    string? storeId,
    string? terminalId,
    string? fields = "transaction_info",
    string? balanceAffectingRecordsOnly = "Y",
    int? pageSize = 100,
    int? page = 1,
    PayPalServerSdk.Core.RequestOptions? requestOptions = null,
    CancellationToken ct = default);
```

Exception: **`SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>`** (Case B — no `TryGet*`).  
On `ex.Error`: `StatusCode` (`HttpStatusCode`), `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`.

---

### 4. Request / response field names (C# property, wire)

**Create `OrderRequest` (AUTHORIZE, one purchase unit, card or vault):**

```csharp
new PayPalServerSdk.Models.OrderRequest
{
    Intent = PayPalServerSdk.Models.Enums.CheckoutPaymentIntent.Authorize, // intent = AUTHORIZE
    PurchaseUnits = new[]
    {
        new PayPalServerSdk.Models.PurchaseUnitRequest
        {
            Amount = new PayPalServerSdk.Models.AmountWithBreakdown
            {
                CurrencyCode = currency, // currency_code
                Value = amountString     // value (string, e.g. "12.34")
            },
            CustomId = eShopOrderId,  // custom_id
            InvoiceId = eShopOrderId  // invoice_id
        }
    },
    PaymentSource = new PayPalServerSdk.Models.PaymentSource
    {
        Card = rawCard
            ? new PayPalServerSdk.Models.CardRequest
            {
                Name = name,                     // name
                Number = pan,                    // number
                Expiry = "YYYY-MM",              // expiry
                SecurityCode = cvc,              // security_code
                BillingAddress = new PayPalServerSdk.Models.Address
                {
                    AddressLine1 = line1,        // address_line_1
                    AddressLine2 = line2,        // address_line_2
                    AdminArea2 = city,           // admin_area_2
                    AdminArea1 = state,          // admin_area_1
                    PostalCode = postal,         // postal_code
                    CountryCode = country        // country_code !req
                }
            }
            : new PayPalServerSdk.Models.CardRequest
            {
                VaultId = vaultTokenId           // vault_id
            }
    }
};
```

**Authorize body (if needed):** `new OrderAuthorizeRequest { PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = same CardRequest } }` — or `body: null` if create already sent `payment_source`.

**Capture body:** `new CaptureRequest { FinalCapture = true }` — `FinalCapture (final_capture): bool?`. Optional `Amount (amount): Money?`.

**Reauthorize body:** `new ReauthorizeRequest { Amount = new Money { CurrencyCode = currency, Value = amountString } }` — only `Amount (amount)`.

**Refund body:** full: `null` or `new RefundRequest()`. Partial: `new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = amountString } }`.

**Vault create body:**

```csharp
new PayPalServerSdk.Models.PaymentTokenRequest
{
    Customer = new PayPalServerSdk.Models.Customer
    {
        MerchantCustomerId = shopperId // merchant_customer_id
    },
    PaymentSource = new PayPalServerSdk.Models.PaymentTokenRequestPaymentSource
    {
        Card = new PayPalServerSdk.Models.PaymentTokenRequestCard
        {
            Name = name,            // name
            Number = pan,           // number
            Expiry = "YYYY-MM",     // expiry
            SecurityCode = cvc      // security_code
        }
    }
};
```

**How to read (no envelope wrapper — the return type is the payload):**

| Need | C# path (wire) |
|---|---|
| Order id | `Order.Id` / `OrderAuthorizeResponse.Id` (`id`) |
| Auth id | `PurchaseUnits[i].Payments.Authorizations[j].Id` (`purchase_units[].payments.authorizations[].id`) type `AuthorizationWithAdditionalData` |
| Auth status | `.Status` → `AuthorizationStatus` (`status`) |
| Auth expiration | `.ExpirationTime` (`expiration_time`) — also `PaymentAuthorization.ExpirationTime` |
| Capture id | `CapturedPayment.Id` (`id`) |
| Captured amount | `CapturedPayment.Amount.Value` + `.CurrencyCode` |
| PayPal fee | `CapturedPayment.SellerReceivableBreakdown.PaypalFee` (`seller_receivable_breakdown.paypal_fee`) — `Money?` |
| Net amount | `CapturedPayment.SellerReceivableBreakdown.NetAmount` (`net_amount`) — `Money?` |
| Refund id | `Refund.Id` (`id`) |
| Vault token id | `PaymentTokenResponse.Id` (`id`) |
| Last digits | `PaymentTokenResponse.PaymentSource.Card.LastDigits` (`last_digits`) |
| Brand | `PaymentSource.Card.Brand` → `CardBrand?` (`.Value` is wire string e.g. `"VISA"`) |
| Expiry | `PaymentSource.Card.Expiry` (`expiry`) |
| Customer id | `PaymentTokenResponse.Customer.Id` (`id`) PayPal-generated |
| Merchant customer id | `PaymentTokenResponse.Customer.MerchantCustomerId` (`merchant_customer_id`) |

**SearchTransactions (`SearchResponse`):**

| Need | C# (wire) |
|---|---|
| transaction_id | `TransactionDetails[i].TransactionInfo.TransactionId` |
| invoice_id | `TransactionInfo.InvoiceId` |
| custom_field | `TransactionInfo.CustomField` |
| amounts | `TransactionInfo.TransactionAmount` (`Money`) |
| fee | `TransactionInfo.FeeAmount` (`Money`) |
| status | `TransactionInfo.TransactionStatus` (`string?`) |
| TotalPages | `SearchResponse.TotalPages` (`total_pages`) `int?` |
| page | `SearchResponse.Page` (`page`) |

---

### 5. 3DS stop

**Orders (create/authorize/get) — this is the compile-ready check:**

```csharp
if (order.Status == PayPalServerSdk.Models.Enums.OrderStatus.PayerActionRequired)
{
    // STOP. Wire: PAYER_ACTION_REQUIRED. Do not follow Links.
}
```

Same on `OrderAuthorizeResponse.Status`. Enum type: `PayPalServerSdk.Models.Enums.OrderStatus`. Member: **`PayerActionRequired`**. (`Models/Enums/OrderStatus.cs`)

**Vault `CreatePaymentToken`:** `PaymentTokenResponse` has **no** `Status` property. There is no `PayerActionRequired` check on that return type.

**Vault setup-token path only** (do not call for this no-browser integration; documented so you do not invent a status on `PaymentTokenResponse`):

```csharp
if (setupToken.Status == PayPalServerSdk.Models.Enums.PaymentTokenStatus.PayerActionRequired)
{
    // STOP. Wire: PAYER_ACTION_REQUIRED.
}
```

Type: `PayPalServerSdk.Models.Enums.PaymentTokenStatus`. Member: **`PayerActionRequired`**. (`Models/Enums/PaymentTokenStatus.cs`)

---

### 6. Idempotency

Pass the stable key as **`payPalRequestId:`** on:

| Operation | Parameter |
|---|---|
| `CreateOrder` | `payPalRequestId` |
| `AuthorizeOrder` | `payPalRequestId` |
| `CaptureAuthorizedPayment` | `payPalRequestId` |
| `ReauthorizePayment` | `payPalRequestId` |
| `VoidPayment` | `payPalRequestId` |
| `RefundCapturedPayment` | `payPalRequestId` |
| `CreatePaymentToken` | `payPalRequestId` |

Not present on: `GetOrder`, `GetAuthorizedPayment`, `GetCapturedPayment`, `GetRefund`, `ListCustomerPaymentTokens`, `DeletePaymentToken`, `SearchTransactions`.
