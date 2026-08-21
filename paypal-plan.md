# PayPal .NET SDK — eShopOnWeb contract sheet

Package: `AsadAli.Checkout.Sdk` (install version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`. Map stamp: tag `v1.0.1` / commit `9653d18`. Controllers on the client: `Orders`, `Payments`, `Vault`, `TransactionSearch` (`sdk-map.md`).

---

## 1. Scope & sequence

| Step | App endpoint | SDK operations | Purpose |
|---|---|---|---|
| 0 | App startup | Client construction + OAuth2 client-credentials | Register `PayPalServerSdkClient`; optional verbatim `PayPal:BaseUrl` |
| 1 | `POST /api/orders/{orderId}/pay` | `Orders.CreateOrder` (intent `AUTHORIZE` + card or vault id). Optional follow-up: `Orders.GetOrder` | Hold funds; do **not** call `Orders.CaptureOrder` (that is sale/capture-at-create) |
| 1b | Same, if create returned an order id without an authorization | `Orders.AuthorizeOrder` | Authorize an already-created order when `payment_source` was omitted on create |
| 2 | `POST /api/orders/{orderId}/fulfil` | `Payments.GetAuthorizedPayment` → if honor/expiry stale: `Payments.ReauthorizePayment` → `Payments.CaptureAuthorizedPayment` | Take the money; persist fee/net |
| 3 | `POST /api/orders/{orderId}/cancel` | `Payments.VoidPayment` | Release the hold; no money moved |
| 4 | `POST /api/orders/{orderId}/refunds` | `Payments.GetCapturedPayment` (refundable check) → `Payments.RefundCapturedPayment` | Full or partial refund; caller idempotency key |
| 5 | Save card | `Vault.CreatePaymentToken` (direct PAN). Optional two-step: `Vault.CreateSetupToken` then `Vault.CreatePaymentToken` with setup-token | Vault id + display fields |
| 6 | List saved cards | `Vault.ListCustomerPaymentTokens` (walk every `page`) | Prefer SDK list over local-only cache |
| 7 | Delete saved card | `Vault.DeletePaymentToken` | Unvault |
| 8 | `GET /api/reconciliation?from&to` | `TransactionSearch.SearchTransactions` (walk every `page`; split windows > 31 days) | Match PayPal ledger to stored ids |

Do **not** use `Orders.CaptureOrder` in this product. Fulfilment captures the **authorization**, via `Payments.CaptureAuthorizedPayment`.

---

## 2. CONTRACT SHEET

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

Namespaces used below (`sdk-map.md`): `PayPalServerSdk` (client, options, `ServerOptions`) · `PayPalServerSdk.Api` (controllers) · `PayPalServerSdk.Models` (records) · `PayPalServerSdk.Models.Enums` (enums are `StringEnum<T>`, **not** C# enums — use static members or `Type.FromValue("wire")`) · `PayPalServerSdk.Errors` · `PayPalServerSdk.Servers` (`ServerEnvironment`, `DefaultOptions`) · `PayPalServerSdk.Core` (`RequestOptions`) · `PayPalServerSdk.Core.Configuration` (`RetryOptions`, `LoggingOptions`) · `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` (`OAuth2ClientCredentials`) · `PayPalServerSdk.Core.Authentication.OAuth2` (`IOAuth2TokenStrategy<>`) · `PayPalServerSdk.Core.Exceptions` (`SdkException<T>`) · `PayPalServerSdk.Core.ErrorResponse` (`ApiError`, `RawError`).

No-throw `…Result` variants: **absent** on every operation (`sdk-map.md`). Every call is throw-only.

`prefer` default on create/authorize/capture/void/refund is `"return=minimal"` (id, status, HATEOAS links only). Pass `prefer: "return=representation"` whenever the integration must read authorization/capture/refund bodies (ids, amounts, `seller_receivable_breakdown`). `operations/Orders.md`, `operations/Payments.md`.

---

### 2.1 Client construction, auth, servers, BaseUrl

| Fact | Value | Cite |
|---|---|---|
| Constructor | `PayPalServerSdk.PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` | `sdk-map.md`, `PayPalServerSdkClient.cs` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` — registers a **singleton** client; internally `IHttpClientFactory.CreateClient()` (unnamed) | `ServiceCollectionExtensions.cs` |
| Options members | `Environment: PayPalServerSdk.Servers.ServerEnvironment` · `Retry: PayPalServerSdk.Core.Configuration.RetryOptions` · `Logging: PayPalServerSdk.Core.Configuration.LoggingOptions` · `Server: PayPalServerSdk.ServerOptions` · `Oauth2: PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?` · `Oauth2TokenStrategy: PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `sdk-map.md`, `PayPalServerSdkClientOptions.cs` |
| Credentials | `new OAuth2ClientCredentials { ClientId = … !req, ClientSecret = … !req, Scope = string? }` on `options.Oauth2` | `OAuth2ClientCredentials.cs` |
| Token request | Default strategy `OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), rawClient)` — POST form `grant_type=client_credentials` (+ `scope` if set), `Authorization: Basic base64(clientId:clientSecret)` | `AuthSchemes.cs`, `OAuth2ClientCredentialsStrategy.cs` |
| Environments | `PayPalServerSdk.Servers.ServerEnvironment` members: **`Sandbox` only** (wire `"Sandbox"`). `Default()` → `Sandbox`. **No `Live` member.** | `Servers/ServerEnvironment.cs` |
| Default sandbox URL | `options.Server.Default.Sandbox.BaseUrl` defaults to `"https://api-m.sandbox.paypal.com"` | `Servers/DefaultOptions.cs` |
| **Custom `PayPal:BaseUrl`** | When set, assign it **verbatim** to `options.Server.Default.Sandbox.BaseUrl`. There is **one** server node (`Default`). Every operation and the token URL are built as `server.Default(path)` → `{BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}`. Token path is `/v1/oauth2/token`. There is **no separate token-URL setter**. One assignment covers **all** PayPal HTTP, including credentials. | `ServerOptions.cs`, `Server.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs`, `Core/TemplateParamsFactory.cs` |
| Currency | App config string `PayPal:Currency` → `AmountWithBreakdown.CurrencyCode` / `Money.CurrencyCode` (both `string !req`). Not an SDK option. | `records-1-Ac-Pa.md` (`AmountWithBreakdown`, `Money`) |
| Per-call options | `PayPalServerSdk.Core.RequestOptions` — only `LogLevel?: Microsoft.Extensions.Logging.LogLevel?`. Cannot set headers/base URL per call. | `Core/RequestOptions.cs` |
| Timeouts / retries | `options.Retry` is `RetryOptions` (all members `required`; use `RetryOptions.Default()` or `RetryOptions.Disabled()`). Members: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout` (`TimeSpan?`), `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. | `sdk-map.md`, `Core/Configuration/RetryOptions.cs` |

---

### 2.2 Idempotency (authorize, capture, refund, void, vault)

Caller-controlled key is the method parameter **`payPalRequestId`** (`string?`, nullable with no default → **must pass explicitly**; pass the key or `null`).

| Operation | Header from `payPalRequestId` | Key retention (XML) | Also sent (SDK-injected, **not** caller-settable) | Cite |
|---|---|---|---|---|
| `Orders.CreateOrder` / `Orders.AuthorizeOrder` | `PayPal-Request-Id` | 6 hours (up to 72h via Account Manager). **Mandatory** for single-step create with a payment source (card / vault_id) | `Idempotency-Key: Guid.NewGuid()` on **every** call | `Api/Orders.cs` |
| `Payments.CaptureAuthorizedPayment` / `ReauthorizePayment` / `RefundCapturedPayment` / `VoidPayment` | `PayPal-Request-Id` | 45 days | `Idempotency-Key: Guid.NewGuid()` | `Api/Payments.cs` |
| `Vault.CreatePaymentToken` / `CreateSetupToken` | `PayPal-Request-Id` | 3 hours | `Idempotency-Key: Guid.NewGuid()` | `Api/Vault.cs` |

Null `payPalRequestId`: `ParameterFlattener.Flatten(null)` yields no values (header omitted). `Core/ParameterFlattener.cs`.

**Double-click:** pass the **same** `payPalRequestId` on retries of the **same** logical action. Distinct partial refunds of the same capture **must** use distinct keys. The SDK **always** adds a fresh `Idempotency-Key`; the integration cannot replace it. Whether PayPal dedupes on `PayPal-Request-Id` when `Idempotency-Key` differs is **UNVERIFIED** (live). Persist our key alongside the PayPal resource id and short-circuit in-app if the same key already succeeded.

---

### 2.3 Operations

#### A. `client.Orders.CreateOrder` — hold money (authorize, not sale)

- **HTTP**: `POST /v2/checkout/orders` · `operations/Orders.md`
- **Signature**: `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderRequest body, string? prefer = "return=minimal", PayPalServerSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params `payPalMockResponse` … `payPalAuthAssertion`: nullable, no default → **must pass explicitly** (`null` to skip)
- **Returns**: `PayPalServerSdk.Models.Order` (not an envelope wrapper — the order **is** the response)
- **Error**: `SdkException<PayPalServerSdk.Errors.CreateOrderError>` Case A · `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback

**Request `OrderRequest`** (`records-1-Ac-Pa.md`):

| Member (wire) | Type | Req |
|---|---|---|
| `Intent (intent)` | `CheckoutPaymentIntent` | **!req** — must be `CheckoutPaymentIntent.Authorize` (`AUTHORIZE`) |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | **!req** |
| `PaymentSource (payment_source)` | `PaymentSource?` | optional; **set for single-step card/vault pay** |
| `Payer (payer)` | `Payer?` | optional |
| `ApplicationContext (application_context)` | `OrderApplicationContext?` | optional |

**`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`): `Amount (amount): AmountWithBreakdown !req` · `ReferenceId (reference_id): string?` · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · others optional. Put the eShop order id in `CustomId` and/or `InvoiceId` for reconciliation matching.

**`AmountWithBreakdown`** (`records-1-Ac-Pa.md`): `CurrencyCode (currency_code): string !req` · `Value (value): string !req` · `Breakdown (breakdown): AmountBreakdown?`. `Value` is a **string** amount; it MUST equal the eShop order total to the cent (same decimal string the app already uses for the total). Currency = config `PayPal:Currency`.

**Direct card vs vaulted card** — both go on `PaymentSource.Card` (`CardRequest`), **not** on `PaymentSource.Token` (`Token.Type` is only `TokenType.BillingAgreement`). `records-2-Pa-Ve.md`, `enums.md`.

`PaymentSource` (`records-2-Pa-Ve.md`): `Card (card): CardRequest?` · `Token (token): Token?` · plus wallets unused here.

`CardRequest` (`records-1-Ac-Pa.md`): `Name (name): string?` · `Number (number): string?` · `Expiry (expiry): string?` · `SecurityCode (security_code): string?` · `BillingAddress (billing_address): Address?` · `VaultId (vault_id): string?` · `SingleUseToken (single_use_token): string?` · `Attributes (attributes): CardAttributes?` · `StoredCredential (stored_credential): CardStoredCredential?` · `ExperienceContext (experience_context): CardExperienceContext?` · `NetworkToken (network_token): NetworkToken?`.

- **One-off PAN:** set `Number`, `Expiry`, `SecurityCode`, `Name`, `BillingAddress`. Sandbox: Visa `4111111111111111`, any future expiry, any CVC, any name/address. `Expiry` format is **UNVERIFIED** (string; send the same `YYYY-MM` shape the vault display field uses). `Address.CountryCode (country_code): string !req` (`records-1-Ac-Pa.md`).
- **Vaulted card:** set `VaultId` to `PaymentTokenResponse.Id` from Flow 2; omit PAN/CVC.
- PCI: XML on `CardRequest` states passing number/cvv/expiry directly requires PCI SAQ D (`records-1-Ac-Pa.md`).

`CardAttributes.Verification (verification): CardVerification?` with `Method (method): OrdersCardVerificationMethod? = OrdersCardVerificationMethod.ScaWhenRequired`. `CardExperienceContext`: `ReturnUrl (return_url): string?`, `CancelUrl (cancel_url): string?` — 3DS return URLs. **Do not build an approval round-trip.** If PayPal requires shopper approval, STOP (see 3DS below).

**Response `Order`** (`records-1-Ac-Pa.md`): `Id (id): string?` · `Status (status): OrderStatus?` · `Intent (intent): CheckoutPaymentIntent?` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` · `PaymentSource (payment_source): PaymentSourceResponse?` · `Links (links): IReadOnlyList<LinkDescription>?` · timestamps.

Hold ids live at `Order.PurchaseUnits[*].Payments.Authorizations[*]`:

`PurchaseUnit.Payments (payments): PaymentCollection?` → `PaymentCollection.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` (`records-2-Pa-Ve.md`).

`AuthorizationWithAdditionalData` (`records-1-Ac-Pa.md`): `Id (id): string?` · `Status (status): AuthorizationStatus?` · `StatusDetails (status_details): AuthorizationStatusDetails?` · `Amount (amount): Money?` · `ExpirationTime (expiration_time): string?` · `InvoiceId` / `CustomId` · `ProcessorResponse (processor_response): ProcessorResponse?` · `Links` · timestamps.

**3DS / browser challenge — STOP.** If `Order.Status == OrderStatus.PayerActionRequired` (`PAYER_ACTION_REQUIRED`), PayPal is asking for shopper approval (3DS / payer-action links). **Do not** implement `CardExperienceContext` return/cancel, do not follow `Links`, do not call `ConfirmOrder`. Fail the pay API with an operator/shopper-visible “payer action required / 3DS challenge — not supported” and persist nothing as a successful hold. The map does **not** say sandbox Visa `4111111111111111` always takes this path; default verification is `SCA_WHEN_REQUIRED`. Treat `PayerActionRequired` as a **runtime stop**, not as “always required.” `enums.md`, `records-1-Ac-Pa.md`.

Card-response 3DS fields (do not drive a round-trip): `PaymentSourceResponse.Card.AuthenticationResult`: `AuthenticationResponse.LiabilityShift` / `ThreeDSecure.AuthenticationStatus` / `EnrollmentStatus` (`records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`).

---

#### B. `client.Orders.AuthorizeOrder` — authorize an existing order

Use only if Step 1 created the order **without** completing authorization.

- **HTTP**: `POST /v2/checkout/orders/{id}/authorize` · `operations/Orders.md`
- **Signature**: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params `payPalMockResponse` … `body`: nullable, no default → **must pass explicitly**
- **Returns**: `PayPalServerSdk.Models.OrderAuthorizeResponse` (same field set as `Order`)
- **Error**: `SdkException<AuthorizeOrderError>` Case A · `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError` fallback

**Request `OrderAuthorizeRequest`**: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` — `Card (card): CardRequest?` (same PAN / `VaultId` rules). `records-1-Ac-Pa.md`.

XML: buyer must have approved **or** a valid `payment_source` must be provided. Card/vault supplies `payment_source`; do not build a `rel:approve` redirect. `operations/Orders.md`.

Same 3DS STOP on `OrderAuthorizeResponse.Status == OrderStatus.PayerActionRequired`.

---

#### C. `client.Orders.GetOrder` — refresh hold

- **HTTP**: `GET /v2/checkout/orders/{id}` · `operations/Orders.md`
- **Signature**: `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `fields`, `payPalMockResponse`, `payPalAuthAssertion` must be passed explicitly
- **Returns**: `Order`
- **Error**: `SdkException<GetOrderError>` Case A · `TryGetError(out Error)` [401, 404] · `TryGetRawError` fallback

---

#### D. `client.Payments.GetAuthorizedPayment` — inspect hold before capture/void/renew

- **HTTP**: `GET /v2/payments/authorizations/{authorization_id}` · `operations/Payments.md`
- **Signature**: `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — last two nullable, no default → **must pass explicitly**
- **Returns**: `PayPalServerSdk.Models.PaymentAuthorization`
- **Error**: `SdkException<GetAuthorizedPaymentError>` Case A · `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback

`PaymentAuthorization` (`records-2-Pa-Ve.md`): `Id (id): string?` · `Status (status): AuthorizationStatus?` · `StatusDetails.Reason: AuthorizationIncompleteReason?` · `Amount: Money?` · `ExpirationTime (expiration_time): string?` · timestamps · `Links`.

---

#### E. `client.Payments.ReauthorizePayment` — renew a stale authorization

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/reauthorize` · `operations/Payments.md`
- **Signature**: `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId`, `payPalAuthAssertion`, `body` must be passed explicitly
- **Returns**: `PaymentAuthorization` (persist the **returned** `Id` / `ExpirationTime` / `Status` — this is the hold to capture)
- **Error**: `SdkException<ReauthorizePaymentError>` Case A · `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback
- **Request `ReauthorizeRequest`**: `Amount (amount): Money?` only (`records-2-Pa-Ve.md`). XML: supports only `amount`.

**Stale vs permanently unrenewable** (operation notes, `operations/Payments.md` + `ReauthorizeRequest` summary):

| Condition | What the map/source says | Integration |
|---|---|---|
| Honor period | Initial honor ~3 days; reauthorize after it expires | If `GetAuthorizedPayment` shows a still-open auth whose `ExpirationTime` has passed (or capture is rejected as expired), call `ReauthorizePayment` rather than failing fulfilment |
| Renewable window | Reauthorize from day 4 to 29 after original authorization; reauthorized payment gets a new 3-day honor period | Persist new `Id` + `ExpirationTime` |
| Permanently unrenewable | If **30 days** have transpired since the **original** authorization, “you must create an authorized payment instead of reauthorizing” | Do **not** retry reauthorize in a loop. Surface an operator-actionable reason from `Error.Name` + `Error.Details[].Issue` + `Error.Details[].Description` + `Error.DebugId` (and the 30-day rule). A new shopper authorization is required — out of scope for silent fulfilment |
| Terminal statuses | `AuthorizationStatus`: `Captured`, `Voided`, `Denied` | Not renewable; do not reauthorize |
| HTTP | 422 is in `TryGetError` | Unprocessable reauthorize (including expired-beyond-window). Exact `issue` strings are **not** enumerated in the map — read `Error.Details[].Issue` (`string !req`) and show it. **UNVERIFIED** which token means “expired” vs “already captured” |

There is **no** `AuthorizationStatus.Expired` member (`enums.md`). Staleness is `ExpirationTime` + reauthorize notes + 422 body, not an enum.

---

#### F. `client.Payments.CaptureAuthorizedPayment` — take the money at fulfilment

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/capture` · `operations/Payments.md`
- **Signature**: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 params `payPalMockResponse` … `body` must be passed explicitly
- **Returns**: `PayPalServerSdk.Models.CapturedPayment`
- **Error**: `SdkException<CaptureAuthorizedPaymentError>` Case A · `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` fallback
- **409**: conflict (typical already-captured). Read `Error.Name` / `Details[].Issue`. Exact issue tokens **UNVERIFIED**.

**Request `CaptureRequest`** (`records-1-Ac-Pa.md`): `Amount (amount): Money?` · `InvoiceId (invoice_id): string?` · `FinalCapture (final_capture): bool? = false` · `PaymentInstruction` · `NoteToPayer` · `SoftDescriptor`. For full fulfilment set `FinalCapture = true` and omit `Amount` (full remaining) **or** set `Amount` equal to the held total. Pass `prefer: "return=representation"` to receive the breakdown.

**Response amounts — captured / fee / net** (`CapturedPayment`, `records-1-Ac-Pa.md`):

| What to show | Accessor | Wire |
|---|---|---|
| Capture id | `CapturedPayment.Id` | `id` |
| Capture status | `CapturedPayment.Status` (`CaptureStatus`) | `status` |
| Captured amount | `CapturedPayment.Amount` → `Money.Value` / `CurrencyCode` | `amount` |
| Gross | `CapturedPayment.SellerReceivableBreakdown.GrossAmount` (`Money !req`) | `seller_receivable_breakdown.gross_amount` |
| PayPal fee | `SellerReceivableBreakdown.PaypalFee` (`Money?`) | `paypal_fee` |
| Net to merchant | `SellerReceivableBreakdown.NetAmount` (`Money?`) | `net_amount` |

XML on `SellerReceivableBreakdown`: “not available for transactions that are in pending state.” If `Status == CaptureStatus.Pending`, fee/net may be absent — persist capture id/status and re-fetch via `GetCapturedPayment`. `records-2-Pa-Ve.md`.

`Money`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req`. `records-1-Ac-Pa.md`.

---

#### G. `client.Payments.GetCapturedPayment` — refundable remaining

- **HTTP**: `GET /v2/payments/captures/{capture_id}` · `operations/Payments.md`
- **Signature**: `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalMockResponse` must be passed explicitly
- **Returns**: `CapturedPayment`
- **Error**: `SdkException<GetCapturedPaymentError>` Case A · `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError` fallback

**Remaining refundable:** the SDK has **no** `remaining_refundable` field. Gate refunds with `CaptureStatus`: `Completed` / `PartiallyRefunded` may accept a refund; `Refunded` must not; `Declined` / `Failed` / `Pending` are not refundable as captured funds. Remaining = captured `Amount.Value` minus sum of successful refund `Amount`s (and/or `Refund.SellerPayableBreakdown.TotalRefundedAmount` after a refund). Enforce in-app so a partial refund cannot exceed captured. `enums.md`, `records-2-Pa-Ve.md`.

---

#### H. `client.Payments.VoidPayment` — cancel before fulfilment

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/void` · `operations/Payments.md`
- **Signature**: `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` must be passed explicitly. **No body.**
- **Returns**: `PaymentAuthorization` (`Status` expected `Voided`)
- **Error**: `SdkException<VoidPaymentError>` Case A · `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError` fallback
- XML: “You cannot void an authorized payment that has been fully captured.” `409` = conflict (already captured / already voided). Exact `issue` strings **UNVERIFIED** — surface `Error.Name` + `Details[].Issue` + `Description` + `DebugId`.

---

#### I. `client.Payments.RefundCapturedPayment` — full/partial refund

- **HTTP**: `POST /v2/payments/captures/{capture_id}/refund` · `operations/Payments.md`
- **Signature**: `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 params `payPalMockResponse` … `body` must be passed explicitly
- **Returns**: `PayPalServerSdk.Models.Refund` — **`refundId` = `Refund.Id`**
- **Error**: `SdkException<RefundCapturedPaymentError>` Case A · `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError` fallback
- **409**: conflict / duplicate. Exact issue **UNVERIFIED**.

**Request `RefundRequest`** (`records-2-Pa-Ve.md`): `Amount (amount): Money?` · `CustomId` · `InvoiceId` · `NoteToPayer` · `PaymentInstruction`. XML: **full refund** = empty body (`body: null`); **partial** = `Amount` set. Pass caller idempotency as `payPalRequestId` (same key must not refund twice; different keys for two legitimate partials).

**Response `Refund`**: `Id (id): string?` · `Status (status): RefundStatus?` · `Amount: Money?` · `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` with `GrossAmount`, `PaypalFee`, `NetAmount`, **`TotalRefundedAmount (total_refunded_amount): Money?`**. `records-2-Pa-Ve.md`.

`GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, …)` returns `Refund` if a later read is needed. `operations/Payments.md`.

---

#### J. Vault — save / list / delete / charge later

**Save (direct card) — `client.Vault.CreatePaymentToken`**

- **HTTP**: `POST /v3/vault/payment-tokens` · `operations/Vault.md`
- **Signature**: `CreatePaymentToken(string? payPalRequestId, PayPalServerSdk.Models.PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must be passed explicitly
- **Returns**: `PayPalServerSdk.Models.PaymentTokenResponse`
- **Error**: `SdkException<CreatePaymentTokenError>` Case A · `TryGetError1(out PayPalServerSdk.Models.Error1)` [400, 403, 404, 422, 500] · `TryGetRawError` fallback

**Request `PaymentTokenRequest`** (`records-2-Pa-Ve.md`): `Customer (customer): Customer?` · `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.

`Customer`: `Id (id): string?` · `MerchantCustomerId (merchant_customer_id): string?` — send the eShop shopper id as `MerchantCustomerId`.

`PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?` · `Token (token): VaultTokenRequest?`.

`PaymentTokenRequestCard`: `Name`, `Number`, `Expiry`, `SecurityCode`, `Brand`, `BillingAddress` (all optional in C#; send PAN + expiry + CVC + name + billing for the sandbox Visa). **No** `VaultId` on this type.

**Response `PaymentTokenResponse`**: `Id (id): string?` — **this is the vault id / payment token** used later as `CardRequest.VaultId`. `Customer (customer): CustomerResponse?` (`Id`, `MerchantCustomerId`) — **`Customer.Id` is the PayPal customer id required by list**. `PaymentSource.Card: CardPaymentTokenEntity` display (never PAN): `LastDigits (last_digits): string?` · `Brand (brand): CardBrand?` · `Expiry (expiry): string?` · `Name (name): string?` · `Type (type): CardType?`. `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`.

Client XML: Vault controller is “Available in the US only.” `PayPalServerSdkClient.cs`.

**Setup token (optional two-step)** — `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, …)` → `SetupTokenResponse`. Then `CreatePaymentToken` with `PaymentSource.Token = new VaultTokenRequest { Id = setupTokenId, Type = VaultTokenRequestType.SetupToken }` (`SETUP_TOKEN`). `SetupTokenRequestCard` adds `VerificationMethod` / `ExperienceContext` (3DS-capable). If `SetupTokenResponse.Status == PaymentTokenStatus.PayerActionRequired`, **STOP** (same 3DS rule). Direct `CreatePaymentToken` with PAN is the path that matches “save via direct card details” without a browser step. `operations/Vault.md`, `records-2-Pa-Ve.md`, `enums.md`.

**List — `client.Vault.ListCustomerPaymentTokens`**

- **HTTP**: `GET /v3/vault/payment-tokens` · `operations/Vault.md`
- **Signature**: `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query wire**: `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired`
- **Returns**: `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` · `TotalItems` · `TotalPages` · `Customer` · `Links`
- **Error**: `SdkException<ListCustomerPaymentTokensError>` Case A · `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError` fallback
- **Pagination**: no auto-pager (`operations/Vault.md`: “only `page`, no `perPage`”). Walk `page = 1 .. TotalPages` (set `totalRequired: true` so totals populate). Default `pageSize` is 5.

`customerId` is the **PayPal** customer id (`PaymentTokenResponse.Customer.Id`), not the eShop user id.

**Get one**: `GetPaymentToken(string id, …)` → `PaymentTokenResponse`. Error `GetPaymentTokenError` · `TryGetError1` [403, 404, 422, 500].

**Delete — `client.Vault.DeletePaymentToken`**

- **HTTP**: `DELETE /v3/vault/payment-tokens/{id}` · `operations/Vault.md`
- **Signature**: `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (`Task`)
- **Error**: `SdkException<DeletePaymentTokenError>` Case A · `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError` fallback. **404 is not** in the typed accessor — it lands on `TryGetRawError` (`RawError.StatusCode`).

**Charge vaulted token:** Flow 1 `CardRequest.VaultId = paymentToken.Id`. Optional `CardStoredCredential`: `PaymentInitiator !req`, `PaymentType !req`, `Usage`. For a shopper-initiated one-off with a saved card: `PaymentInitiator.Customer` + `StoredPaymentSourcePaymentType.OneTime` + `StoredPaymentSourceUsageType.Subsequent` (first save used `First` if you send stored-credential on the vaulting charge). `records-1-Ac-Pa.md`, `enums.md`.

---

#### K. `client.TransactionSearch.SearchTransactions` — reconciliation

- **HTTP**: `GET /v1/reporting/transactions` · `operations/TransactionSearch.md`
- **Signature**: `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 8 params `transactionId` … `terminalId`: nullable, no default → **must pass explicitly** (`null` to skip)
- **Query wire**: `start_date` ← `startDate`, `end_date` ← `endDate`, plus the optional filters above; `fields`, `balance_affecting_records_only`, `page_size`, `page`
- **Returns**: `PayPalServerSdk.Models.SearchResponse` (the list **is** the response)
- **Error**: `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` **Case B** — `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()`. Optional typed body: `ReadAsJson<PayPalServerSdk.Models.SearchError>()` (`Name`, `Message`, `DebugId` all `!req`; `Details: IReadOnlyList<TransactionSearchErrorDetails>?` with `Issue !req`). `records-2-Pa-Ve.md`
- **Pagination**: no auto-pager. Use `page` / `pageSize`. Response: `Page (page): int?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Links`. Loop `page = 1` while `page <= TotalPages` (XML describes `page` as the start index of the list; default `page: 1` returns the first page). Cover the **whole** range — do not stop after page 1. `operations/TransactionSearch.md`

**Date range:** `startDate` / `endDate` are required strings, RFC 3339; **seconds required**. **Maximum window 31 days** per call (`Api/TransactionSearch.cs`). If app `from`/`to` spans more than 31 days, split into adjacent windows and concatenate. Reporting lag: “maximum of three hours” before transactions appear — empty range is expected, not a gap. Lists up to previous three years.

Pass app `from`/`to` (ISO-8601) as `startDate`/`endDate`. Optional: `fields: "all"` for payer/shipping/cart; default `"transaction_info"` is enough to match ids/amounts/status. `transactionCurrency` can be config currency.

**Match fields** — `SearchResponse.TransactionDetails[*].TransactionInfo` (`TransactionInformation`, `records-2-Pa-Ve.md`):

| Use | Member (wire) |
|---|---|
| PayPal transaction id | `TransactionId (transaction_id): string?` |
| Related PayPal id | `PaypalReferenceId (paypal_reference_id): string?` · `PaypalReferenceIdType` |
| Amount | `TransactionAmount (transaction_amount): Money?` |
| Fee | `FeeAmount (fee_amount): Money?` |
| Status | `TransactionStatus (transaction_status): string?` (not an SDK enum; XML: `D` denied, `P` pending, `S` success, `V` reversed/refunded) |
| Invoice / custom | `InvoiceId (invoice_id): string?` · `CustomField (custom_field): string?` |
| Event code | `TransactionEventCode (transaction_event_code): string?` |
| Initiation time | `TransactionInitiationDate (transaction_initiation_date): string?` |

Match `transaction_id` / `paypal_reference_id` against persisted PayPal order, authorization, capture, and refund ids; `invoice_id` / `custom_field` against eShop order id.

`SearchBalances` exists (`GET /v1/reporting/balances`) but is **not** required for this reconciliation endpoint.

---

### 2.4 Enums actually needed (`map/models/enums.md`)

| Enum (`PayPalServerSdk.Models.Enums`) | Members (C# (wire)) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, **`Authorize (AUTHORIZE)`** — pay flow uses **Authorize** |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** ← 3DS STOP |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` — **no Expired** |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, … `Unknown (UNKNOWN)` — persist `Brand` for display |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` only — **not** a vault payment token |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `EnrollmentStatus` / `ParesStatus` | `Y`/`N`/`U`/`B` and `Y`/`N`/`U`/`A`/`C`/`R`/`D`/`I` — 3DS result fields; do not drive a UI round-trip |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |

---

### 2.5 Payment state to persist

| Resource | Persist | Source |
|---|---|---|
| PayPal checkout order | `Order.Id`, `Order.Status`, `Order.Intent` | CreateOrder / GetOrder / AuthorizeOrder |
| Authorization (hold) | `AuthorizationWithAdditionalData.Id` (or `PaymentAuthorization.Id` after reauthorize), `Status`, `Amount.Value`+`CurrencyCode`, `ExpirationTime` | Create/Authorize response; refresh with GetAuthorizedPayment; replace id after ReauthorizePayment |
| Capture | `CapturedPayment.Id`, `Status`, `Amount`, `SellerReceivableBreakdown.GrossAmount` / `PaypalFee` / `NetAmount` | CaptureAuthorizedPayment / GetCapturedPayment |
| Refunds | list of `Refund.Id` (`refundId`), `Status`, `Amount`, `SellerPayableBreakdown.TotalRefundedAmount`; plus the caller `payPalRequestId` used | RefundCapturedPayment |
| Vault | `PaymentTokenResponse.Id` (vault id), `Customer.Id` (PayPal customer id), `MerchantCustomerId`, display `LastDigits`/`Brand`/`Expiry`/`Name` — **never** PAN/CVC | CreatePaymentToken / List / Get |
| Idempotency | last `payPalRequestId` per action (pay, capture, void, refund, vault) | app |

---

### 2.6 Errors — types, HTTP, body, distinction

`SdkException<T>` (`PayPalServerSdk.Core.Exceptions.SdkException`) exposes **only** `Error` (`required TError`). It has **no** `StatusCode` property (`Core/Exceptions/SdkException.cs`).

| Operation | Catch | Read body | HTTP status |
|---|---|---|---|
| CreateOrder | `SdkException<CreateOrderError>` | `TryGetError(out Error)` → `Name`, `Message`, `DebugId`, `Details[].Issue`/`Field`/`Description` | Typed path **does not** put status on `Error`. Status only via `TryGetRawError` for **non**-400/401/422 |
| AuthorizeOrder | `SdkException<AuthorizeOrderError>` | `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] | same pattern |
| GetOrder | `SdkException<GetOrderError>` | `TryGetError` [401, 404] | 404 resource missing on typed `Error` |
| CaptureAuthorizedPayment | `SdkException<CaptureAuthorizedPaymentError>` | `TryGetError` [400, 401, 403, 404, 409, 422]; `TryGetNoContent(out RawError)` [500] | 409 conflict on typed `Error`; 500 via `RawError.StatusCode` |
| GetAuthorizedPayment / GetCapturedPayment / GetRefund | matching `*Error` | `TryGetError` [401, 403, 404]; `TryGetNoContent` [500] | 404 = not found |
| ReauthorizePayment | `SdkException<ReauthorizePaymentError>` | `TryGetError` [400, 401, 403, 404, 422] | 422 unprocessable (stale/unrenewable) on typed `Error` |
| VoidPayment | `SdkException<VoidPaymentError>` | `TryGetError` [401, 403, 404, 409, 422] | 409 already captured/voided |
| RefundCapturedPayment | `SdkException<RefundCapturedPaymentError>` | `TryGetError` [400, 401, 403, 404, 409, 422] | 409 conflict / duplicate |
| CreatePaymentToken / CreateSetupToken / GetPaymentToken / GetSetupToken | `SdkException<…Error>` | **`TryGetError1(out Error1)`** (not `TryGetError`) — `Error1.Name`/`Message`/`DebugId`/`Details[].Issue` | listed statuses on that accessor |
| DeletePaymentToken / ListCustomerPaymentTokens | same Case A `TryGetError1` | 400/403/500 typed; **404 delete → `TryGetRawError`** | `RawError.StatusCode` on fallback |
| SearchTransactions | `SdkException<RawError>` Case B | `StatusCode`, `ReadAsString()`, `ReadAsJson<SearchError>()` | always on `RawError` |

`Error` / `Error1` (`records-1-Ac-Pa.md`): `Name (name): string !req` · `Message (message): string !req` · `DebugId (debug_id): string !req` · `Details[].Issue (issue): string !req` · `Details[].Description (description): string?`. **No enum of issue/name values in the map or generated error types.** Distinguishing cases:

| Need | What the SDK actually gives | Do not invent issue tokens |
|---|---|---|
| Already captured | Capture/void **409** + `Error.Name`/`Details[].Issue`; auth `Status == Captured` | Surface those strings to the operator |
| Already voided | Void **409** + `Status == Voided` | same |
| Stale auth | `ExpirationTime` elapsed; capture/reauthorize **422** + `Issue` | Renew via `ReauthorizePayment` when still inside 29 days |
| Permanently unrenewable | 30-day rule in reauthorize notes; reauthorize **422** after that | Operator-actionable: cannot renew; new authorization required |
| Insufficient funds / card decline | Create/Authorize `Error` + `Authorization.ProcessorResponse.ResponseCode` (`ProcessorResponseCode`) | Enum lists many codes; **which code is insufficient funds is UNVERIFIED** — show `ResponseCode`, `Error.Name`, `Issue` |
| 3DS required | Success body `OrderStatus.PayerActionRequired` (not necessarily an exception) | **STOP** — no approval round-trip |
| Duplicate / idempotent replay | **409** on capture/refund; or a success replay of `PayPal-Request-Id` (**UNVERIFIED** vs injected `Idempotency-Key`) | In-app short-circuit on stored key |
| Resource not found | **404** on Get*/typed `Error`, or Case B `RawError.StatusCode` | |
| Auth failure | **401** on typed `Error` where listed | Check `options.Oauth2` / BaseUrl |

`TryGetRawError` is **not** populated when the typed `TryGetError`/`TryGetError1` succeeded (typed constructor leaves fallback empty). Do not expect `StatusCode` on Case A typed hits. `Core/ErrorResponse/ApiError.cs`.

---

## 3. Trap notes

⚠ Step 0 (client registration) — `HttpClient` lifetime vs the SDK wrapper, and `AddPayPalServerSdkClient`’s unnamed `CreateClient()`, are not implied by the constructor. **MUST load `dotnet-client-initialization`** before wiring DI.

⚠ Step 0 (auth) — `Oauth2` vs `Oauth2TokenStrategy`, when credentials must be set, and loading client id/secret from config are not in the options table. **MUST load `dotnet-authentication`** before setting `options.Oauth2`.

⚠ Step 0 (BaseUrl / retries / timeout) — `RetryOptions.Timeout` and `HttpMethodsToRetry` do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; a transport failure can retry verbs the status list does not mention, including writes used in pay/capture/refund. **MUST load `dotnet-configuration-resilience`** before setting `options.Retry` or `options.Server`.

⚠ Steps 1–8 (calls) — many nullable parameters have **no C# default** and mis-bind if passed positionally; the token parameter is `ct`. **MUST load `dotnet-calling-endpoints`** before the first `client.*` call.

⚠ Steps 1, 5 (models) — enums are `StringEnum<T>` (static members / `FromValue`), records are `init`/`required`, `CardRequest.VaultId` vs `Token`/`VaultTokenRequest`. **MUST load `dotnet-models`** before constructing `OrderRequest` / `PaymentTokenRequest`.

⚠ All steps (errors) — Case A vs Case B differ per operation (`TryGetError` vs `TryGetError1` vs `RawError`); typed hits do not carry HTTP status; `TryGetRawError` is not a catch-all on typed errors. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Tests — the `HttpClient` constructor argument is the seam. **MUST load `dotnet-testing`** before stubbing the SDK.

A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.

A **non-2xx** body that does not match its operation’s generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 4. REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — construct / DI-register `PayPalServerSdkClient`, `HttpClient` ownership |
| `dotnet-authentication` | Step 0 — `options.Oauth2` client-credentials |
| `dotnet-configuration-resilience` | Step 0 — `Retry` / `Timeout` / BaseUrl; Step 8 — pagination walk |
| `dotnet-calling-endpoints` | Steps 1–8 — every operation call (`ct:`, named args, must-pass nullables) |
| `dotnet-models` | Steps 1, 2, 4, 5 — request/response records, enums, vault vs card source |
| `dotnet-error-handling` | All steps — Case A/B, `JsonException` 2xx vs non-2xx, accessors |
| `dotnet-testing` | Tests for the integration layer |

---

## 5. Assumptions & Blockers

**Assumptions**

- Fulfilment captures a **Payments authorization** (`CaptureAuthorizedPayment`), not `Orders.CaptureOrder`.
- Single-step pay = `CreateOrder` with `Intent = Authorize` and `PaymentSource.Card` (PAN or `VaultId`). `AuthorizeOrder` is only the fallback if create did not authorize.
- Shopper identity for vault list = PayPal `Customer.Id` returned at save time; eShop user id is `MerchantCustomerId`.
- Amount strings use the same cent-precision decimal text as eShop totals; currency is the config string as-is.
- When `PayPal:BaseUrl` is unset, leave `Server.Default.Sandbox.BaseUrl` at its sandbox default. When set, overwrite that one property verbatim.
- Live vs sandbox: the SDK exposes **only** `ServerEnvironment.Sandbox`. Hitting another host is the BaseUrl override, still with `Environment = Sandbox`.
- Card `Expiry` wire shape is **UNVERIFIED**; treat as a string and keep vault display + request consistent.
- Exact PayPal `Error.details[].issue` tokens (already captured, expired, duplicate, insufficient funds) are **UNVERIFIED** — persist and return `Name`/`Issue`/`Description`/`DebugId`.
- Whether `PayPal-Request-Id` dedupes while the SDK sends a unique `Idempotency-Key` is **UNVERIFIED**; keep in-app idempotency on stored keys.
- 3DS for `4111111111111111` is **not** documented as mandatory; `PayerActionRequired` at runtime is a STOP for that payment.

**Blockers (map/SDK surface)**

- **No `ServerEnvironment.Live`.** Only `Sandbox`. Custom BaseUrl is the supported override for every call including `/v1/oauth2/token`. There is no second token-base-URL property.
- **3DS / browser challenge is not implemented in this product.** If PayPal returns `OrderStatus.PayerActionRequired` (or vault `PaymentTokenStatus.PayerActionRequired`), STOP. The SDK **does** expose `CardExperienceContext` return/cancel URLs; using them would be an approval round-trip, which is out of scope. This is **not** a blocker that sandbox Visa 4111 can only succeed via 3DS — the map does not say that. It **is** a blocker on building payer-action completion.
- **No remaining-refundable field** — remaining must be computed from capture + refunds / `TotalRefundedAmount` / `CaptureStatus`.
- **No `AuthorizationStatus.Expired`.** Stale vs dead uses `ExpirationTime` + the 3/29/30-day reauthorize notes + 422 bodies.
- **Vault “US only”** (client XML). If the merchant account is outside that, vault operations may fail at runtime — not a missing SDK method.
- **Direct PAN** on Orders and Vault is modeled (`CardRequest` / `PaymentTokenRequestCard`) and is the intended no-redirect path; PCI SAQ D is a compliance constraint, not an SDK gap.
- Transaction search **31-day max window** and **manual page walk** (no SDK auto-paginator). Empty pages from reporting lag are expected.
- Caller cannot set `Idempotency-Key`; only `payPalRequestId` → `PayPal-Request-Id`.

**Not blockers (exposed and in scope)**

- Direct card processing without browser redirect: `CardRequest` on `CreateOrder` / `AuthorizeOrder`.
- Vault then charge: `CreatePaymentToken` + `CardRequest.VaultId`; list `ListCustomerPaymentTokens`; delete `DeletePaymentToken`.
- Authorize vs capture intent: `CheckoutPaymentIntent.Authorize` then `CaptureAuthorizedPayment`.
- Reauthorize stale hold: `ReauthorizePayment`.
- Void: `VoidPayment`.
- Refund + caller key: `RefundCapturedPayment` + `payPalRequestId`.
- Transaction search + full pagination: `SearchTransactions` + `TotalPages` loop + 31-day splits.
- Custom base URL for API **and** token: `options.Server.Default.Sandbox.BaseUrl`.
