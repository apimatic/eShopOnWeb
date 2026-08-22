# PayPal .NET SDK plan — eShopOnWeb payments + saved cards

NuGet `AsadAli.Checkout.Sdk` (version-less). Root namespace `PayPalServerSdk`. Client `PayPalServerSdkClient`. Target `ServerEnvironment.Sandbox`.

## Scope & sequence

1. **Client + config** — construct `PayPalServerSdkClient` from `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, optional `PayPal:BaseUrl`. No PayPal operation yet.
2. **Authorize (hold)** `POST /api/orders/{orderId}/pay` — `Orders.CreateOrder` then `Orders.AuthorizeOrder` with `CheckoutPaymentIntent.Authorize`. Raw card (`CardRequest` PAN/expiry/CVC/name/address) **or** vaulted card (`CardRequest.VaultId`). Persist PayPal order id, authorization id, authorization status, `ExpirationTime`. Idempotency via `payPalRequestId`.
3. **3DS / payer-action STOP** — after create and after authorize, if `Order.Status` / `OrderAuthorizeResponse.Status` is `OrderStatus.PayerActionRequired` (wire `PAYER_ACTION_REQUIRED`), **fail the API call**. Do not follow `rel:payer-action` / `rel:approve` links and do not build a browser round-trip.
4. **Capture** `POST /api/orders/{orderId}/fulfil` — `Payments.GetAuthorizedPayment`; if the honor/auth window is stale, `Payments.ReauthorizePayment` then capture the **new** authorization id; `Payments.CaptureAuthorizedPayment` with `prefer: "return=representation"`. Record `CapturedPayment.Amount`, `SellerReceivableBreakdown.PaypalFee`, `SellerReceivableBreakdown.NetAmount`. Idempotent via `payPalRequestId`.
5. **Void** `POST /api/orders/{orderId}/cancel` — `Payments.VoidPayment` on the stored authorization id. Idempotent via `payPalRequestId`.
6. **Refund** `POST /api/orders/{orderId}/refunds` — `Payments.RefundCapturedPayment`; caller key → `payPalRequestId`. Full = body `null` or `RefundRequest` without `Amount`; partial = `RefundRequest.Amount`. Persist `Refund.Id` + `Refund.Status`. Cap remaining against captured amount / `CaptureStatus`.
7. **Reconciliation** `GET /api/reconciliation` — `TransactionSearch.SearchTransactions` over the whole `from`/`to` range (31-day windows + every `page` until `TotalPages`).
8. **Save card** `POST /api/payment-methods` — `Vault.CreatePaymentToken` (not setup-token for this flow). Persist `PaymentTokenResponse.Id` as `paymentMethodId`, PayPal `Customer.Id`, display `last_digits` / `brand` / `expiry`.
9. **List cards** `GET /api/payment-methods` — `Vault.ListCustomerPaymentTokens` paging all pages.
10. **Delete card** `DELETE /api/payment-methods/{paymentMethodId}` — `Vault.DeletePaymentToken`.

Supporting reads: `Orders.GetOrder`, `Payments.GetCapturedPayment`, `Payments.GetRefund`, `Vault.GetPaymentToken`.

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

Responses are **not** wrapped in an extra envelope type: the method return **is** the model (`Order`, `OrderAuthorizeResponse`, `CapturedPayment`, `Refund`, `PaymentTokenResponse`, `SearchResponse`). Read fields directly. (`operations/*.md`)

### Client construction / auth / server

| Fact | Value | Source |
|---|---|---|
| Client | `PayPalServerSdk.PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` — registers a singleton wrapping `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Options | `Environment`: `PayPalServerSdk.Servers.ServerEnvironment`; `Retry`: `PayPalServerSdk.Core.Configuration.RetryOptions`; `Logging`: `LoggingOptions`; `Server`: `PayPalServerSdk.ServerOptions`; `Oauth2`: `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?`; `Oauth2TokenStrategy`: `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `sdk-map.md`, `PayPalServerSdkClientOptions.cs` |
| Environment members | **Only** `ServerEnvironment.Sandbox` (wire `"Sandbox"`). `Default()` → Sandbox. **No Production member in this SDK.** | `sdk-map.md` Servers & auth, `Servers/ServerEnvironment.cs` |
| Credentials | `new OAuth2ClientCredentials { ClientId = …, ClientSecret = …, Scope = optional }` — `ClientId`/`ClientSecret` are `required string` | `OAuth2ClientCredentials.cs` |
| Token request | OAuth2 client-credentials POST `{BaseUrl}/v1/oauth2/token` via `server.Default("/v1/oauth2/token")` — **same base URL as every other call** | `AuthSchemes.cs` |
| Default sandbox base | `https://api-m.sandbox.paypal.com` | `Servers/DefaultOptions.cs` |
| **`PayPal:BaseUrl` verbatim override** | `options.Server.Default.Sandbox.BaseUrl = configBaseUrl` (`PayPalServerSdk.ServerOptions.Default` is `PayPalServerSdk.Servers.DefaultOptions`; nested `SandboxOptions.BaseUrl: string`). Do **not** invent a top-level `BaseUrl` on options. Setting this one property changes **every** `Server.Default(path)` including `/v1/oauth2/token`. | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Server.cs` |
| Config map | `PayPal:ClientId` → `Oauth2.ClientId`; `PayPal:ClientSecret` → `Oauth2.ClientSecret`; `PayPal:Environment` → `ServerEnvironment.Sandbox` (only legal SDK value); `PayPal:Currency` → `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode`; `PayPal:BaseUrl` optional → `Server.Default.Sandbox.BaseUrl` | this sheet |
| Per-call options | `PayPalServerSdk.Core.RequestOptions` — only `LogLevel? LogLevel`. Not for headers. | `Core/RequestOptions.cs` |
| Retry type | `PayPalServerSdk.Core.Configuration.RetryOptions` — all members `required`, or `RetryOptions.Default()` | `sdk-map.md` |

### Amount / currency formatting

| Field | C# | Wire | Rules | Source |
|---|---|---|---|---|
| Order amount | `PayPalServerSdk.Models.AmountWithBreakdown` `CurrencyCode` `!req`, `Value` `!req`, `Breakdown` optional | `currency_code`, `value`, `breakdown` | `Value` is **string** (not `decimal`). Regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`, max 32 chars. ISO-4217 3-char `CurrencyCode`. Integer for non-fractional currencies (e.g. JPY); decimal fraction otherwise — minor units per PayPal currency codes (cents → two fraction digits, e.g. `"19.99"`). Must be positive. If `Breakdown` is set, `value` must equal the breakdown arithmetic. | `records-1-Ac-Pa.md`, `Models/AmountWithBreakdown.cs`, `Models/Money.cs` |
| Capture/refund/reauth amount | `PayPalServerSdk.Models.Money` `CurrencyCode` `!req`, `Value` `!req` | `currency_code`, `value` | Same string/`CurrencyCode` rules as above. | `records-1-Ac-Pa.md` |
| Hold = order total | Set `PurchaseUnitRequest.Amount.Value` to the eShop order total formatted to the currency's minor unit; `CurrencyCode` from `PayPal:Currency`. | | |

### Unions / AnyOf

`map/models/unions.md` lists **0 OneOf and 0 AnyOf**. Payment source, amount breakdown, and seller receivable are **records with nullable properties**, not unions. Do **not** call `TryGet…` on them — read `Card`, `PaypalFee`, `NetAmount`, etc. directly.

### Enums in scope (`PayPalServerSdk.Models.Enums`, `StringEnum<T>` — use static members or `Type.FromValue("WIRE")`)

| Enum | Members (C# `(wire)`) | Source |
|---|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` | `enums.md` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** | `enums.md`, `Models/Enums/OrderStatus.cs` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` — **no `EXPIRED`** | `enums.md` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` | `enums.md` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` | `enums.md` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` | `enums.md` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, … `Unknown (UNKNOWN)` (full list `enums.md`) | `enums.md` |
| `PaymentTokenStatus` | `Created (CREATED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`**, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` | `enums.md` |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` | `enums.md` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` | `enums.md` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` | `enums.md` |
| `ParesStatus` (3DS outcome) | `Y (Y)`, `N (N)`, `U (U)`, `A (A)`, `C (C)`, `R (R)`, `D (D)`, `I (I)` | `enums.md` |
| `EnrollmentStatus` | `Y (Y)`, `N (N)`, `U (U)`, `B (B)` | `enums.md` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` | `enums.md` |
| `TokenType` | **`BillingAgreement (BILLING_AGREEMENT)` only** — not a vault payment-token type | `enums.md` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` | `enums.md` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` | `enums.md` |
| `LinkHttpMethod` | `Get (GET)`, `Post (POST)`, … | `enums.md` |

### Idempotency — `payPalRequestId` → header `PayPal-Request-Id`

| Operation | Has `payPalRequestId`? | Header | Key TTL (XML) | Source |
|---|---|---|---|---|
| `CreateOrder` | yes (nullable, **must pass explicitly**) | `PayPal-Request-Id` | 6h (up to 72h via AM). **Mandatory for single-step create with payment_source** | `operations/Orders.md`, `Api/Orders.cs` |
| `AuthorizeOrder` | yes | `PayPal-Request-Id` | 6h | same |
| `CaptureAuthorizedPayment` | yes | `PayPal-Request-Id` | 45 days | `operations/Payments.md`, `Api/Payments.cs` |
| `ReauthorizePayment` | yes | `PayPal-Request-Id` | 45 days | same |
| `RefundCapturedPayment` | yes | `PayPal-Request-Id` | 45 days | same |
| `VoidPayment` | yes | `PayPal-Request-Id` | 45 days | same |
| `CreatePaymentToken` / `CreateSetupToken` | yes | `PayPal-Request-Id` | 3 hours | `operations/Vault.md`, `Api/Vault.cs` |
| `Get*` / `List*` / `SearchTransactions` / `DeletePaymentToken` | **no** caller `payPalRequestId` | — | — | operations pages |

**Also:** every write above additionally sends `Idempotency-Key: Guid.NewGuid()` generated **inside the SDK method** (`Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`). Caller-controlled idempotency is **`payPalRequestId` only**. Pass a stable key per eShop action (e.g. `{orderId}:authorize`, `{orderId}:capture`, `{orderId}:void`, caller refund key). Distinct partial refunds → distinct keys.

`prefer` (when present): default `"return=minimal"` (id, status, HATEOAS only). Pass `"return=representation"` to get full resources including `seller_receivable_breakdown`. (`Api/Orders.cs`, `Api/Payments.cs`)

---

### Operations

#### A. `client.Orders.CreateOrder` — create the PayPal order (intent AUTHORIZE)

- **HTTP**: `POST /v2/checkout/orders` · `operations/Orders.md` · `Api/Orders.cs`
- **Signature**: `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params `payPalMockResponse` … `payPalAuthAssertion`: nullable, **no default → pass `null` to skip**
- **Returns**: `PayPalServerSdk.Models.Order` (not wrapped)
- **Error**: Case A `SdkException<PayPalServerSdk.Errors.CreateOrderError>` · `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out RawError)` fallback
- **Request `OrderRequest`** (`records-1-Ac-Pa.md`):

| C# | Wire | Type | Req? |
|---|---|---|---|
| `Intent` | `intent` | `CheckoutPaymentIntent` | **required** — set `CheckoutPaymentIntent.Authorize` |
| `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnitRequest>` | **required** (1–10) |
| `PaymentSource` | `payment_source` | `PaymentSource?` | optional (put card on AuthorizeOrder instead, or here for single-step) |
| `Payer` | `payer` | `Payer?` | optional / deprecated |
| `ApplicationContext` | `application_context` | `OrderApplicationContext?` | optional |

`PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req`; set `CustomId (custom_id)` and/or `InvoiceId (invoice_id)` to the eShop order id for reconciliation. Other fields optional.

- **Read from `Order`**: `Id (id)`, `Status (status)`, `Intent (intent)`, `PurchaseUnits (purchase_units)`, `PaymentSource (payment_source)`, `Links (links)` (`records-1-Ac-Pa.md`)

#### B. `client.Orders.AuthorizeOrder` — hold funds (do not capture)

- **HTTP**: `POST /v2/checkout/orders/{id}/authorize` · `operations/Orders.md`
- **Signature**: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params `payPalMockResponse` … `body`: **must pass explicitly**
- **Returns**: `PayPalServerSdk.Models.OrderAuthorizeResponse`
- **Error**: Case A `SdkException<AuthorizeOrderError>` · `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError`
- **Request `OrderAuthorizeRequest`**: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`
- **`OrderAuthorizeRequestPaymentSource`**: `Card (card): CardRequest?`, `Token (token): Token?`, plus wallet types. **Vaulted card = `Card.VaultId`, not `Token`** (`Token.Type` is only `BILLING_AGREEMENT`).

**Raw card `CardRequest`** (`records-1-Ac-Pa.md`, `Models/CardRequest.cs`):

| C# | Wire | Type | Notes |
|---|---|---|---|
| `Name` | `name` | `string?` | cardholder name |
| `Number` | `number` | `string?` | PAN, 13–19 digits. Sandbox Visa `4111111111111111` |
| `Expiry` | `expiry` | `string?` | **`YYYY-MM`** exactly 7 chars |
| `SecurityCode` | `security_code` | `string?` | CVC 3–4 digits |
| `BillingAddress` | `billing_address` | `Address?` | see Address table |
| `VaultId` | `vault_id` | `string?` | PayPal vault payment-token id (saved card). Mutually exclusive with PAN for this flow |
| `StoredCredential` | `stored_credential` | `CardStoredCredential?` | for vaulted-card CIT: `PaymentInitiator.Customer` `!req`, `PaymentType.OneTime` `!req`, `Usage` default `Derived` (set `Subsequent` when reusing a vaulted card) |
| `ExperienceContext` | `experience_context` | `CardExperienceContext?` | `ReturnUrl`/`CancelUrl` — **do not set** (that is the 3DS browser round-trip). Detect `PAYER_ACTION_REQUIRED` and STOP |
| `Attributes.Verification.Method` | | `OrdersCardVerificationMethod?` default `ScaWhenRequired` | default can trigger SCA/3DS |

**`Address`** (`records-1-Ac-Pa.md`): `AddressLine1 (address_line_1)`, `AddressLine2 (address_line_2)`, `AdminArea2 (admin_area_2)` city, `AdminArea1 (admin_area_1)` state, `PostalCode (postal_code)`, **`CountryCode (country_code): string !req`** (ISO-3166-1 alpha-2).

- **Read hold ids from `OrderAuthorizeResponse`** (same fields as `Order`): `Id`, `Status`, `Links`, `PurchaseUnits[].Payments` → `PaymentCollection.Authorizations` (`AuthorizationWithAdditionalData`: `Id`, `Status`, `Amount`, `ExpirationTime`, `CreateTime`, `ProcessorResponse`). Persist **authorization `Id` + `Status` + `ExpirationTime` + PayPal order `Id`**.

#### C. `client.Orders.GetOrder`

- **Signature**: `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `fields`/`payPalMockResponse`/`payPalAuthAssertion` must be passed (`null` ok)
- **Returns**: `Order` · Error Case A `GetOrderError` `TryGetError(out Error)` [401, 404]
- **Use**: refresh status / payments collection after a 409 or for idempotent re-reads

#### D. 3DS / payer-action — STOP (do not start an approval round-trip)

Detect **all** of these after CreateOrder and AuthorizeOrder (`Models/Enums/OrderStatus.cs`, `records-1-Ac-Pa.md` `LinkDescription`):

1. `order.Status == OrderStatus.PayerActionRequired` (wire **`PAYER_ACTION_REQUIRED`**). XML: *“The order requires an action from the payer (e.g. 3DS authentication). Redirect the payer to the `"rel":"payer-action"` HATEOAS link… prior to authorizing or capturing.”*
2. Any `order.Links` item with `Rel (rel): string !req` equal to **`payer-action`** (or `approve`, named in AuthorizeOrder notes as `rel:approve`).
3. Optional extra signal: `PaymentSource.Card.AuthenticationResult` → `AuthenticationResponse.ThreeDSecure` (`ThreeDSecureAuthenticationResponse.AuthenticationStatus: ParesStatus?`, `EnrollmentStatus`). `LiabilityShift` on `AuthenticationResponse`.

**Required app behavior:** if (1) or (2), return an actionable failure to the shopper/operator and **do not** authorize/capture further and **do not** open `Href`. Vault create: stop on `PaymentTokenStatus.PayerActionRequired` / setup-token `Status` default `Created` with same payer-action status.

#### E. `client.Payments.GetAuthorizedPayment` — inspect hold / staleness

- **HTTP**: `GET /v2/payments/authorizations/{authorization_id}` · `operations/Payments.md`
- **Signature**: `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `PayPalServerSdk.Models.PaymentAuthorization` — `Id`, `Status`, `Amount`, **`ExpirationTime (expiration_time)`**, `CreateTime`, `StatusDetails.Reason`
- **Error**: Case A `GetAuthorizedPaymentError` · `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`

**Staleness (no `EXPIRED` status in the SDK):**

| Signal | Meaning | Source |
|---|---|---|
| `ExpirationTime` parsed vs now | honor/auth window elapsed | `PaymentAuthorization` |
| `ReauthorizePayment` remarks | 3-day honor; reauthorize days 4–29; **after 30 days from original auth you must create a new authorized payment** (shopper present) — fulfilment cannot recover | `operations/Payments.md` |
| `ReauthorizeRequest` remarks | **“only once”** from days 4–29 | `Models/ReauthorizeRequest.cs` — **disagrees** with operation remarks (“multiple re-authorizations”) |
| Capture/reauthorize `Error.Details[].Issue` (`string !req`) and `Error.Name` | fine-grained code; **not an SDK enum**. Map does **not** list `AUTHORIZATION_EXPIRED` / `INSTRUMENT_DECLINED` as closed vocabulary — match those strings if present; otherwise surface `Name` + `Issue` + `Description` + `DebugId` to the operator. **UNVERIFIED** which string is stale-vs-dead. | `records-1-Ac-Pa.md` `Error` / `ErrorDetails` |
| Permanently unrenewable | Reauthorize 422 (in `TryGetError` group) **or** original `CreateTime` ≥ 30 days — operator must re-take payment | this sheet |

#### F. `client.Payments.ReauthorizePayment` — renew stale hold

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/reauthorize`
- **Signature**: `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Body**: `ReauthorizeRequest.Amount (amount): Money?` only (same amount as original hold)
- **Returns**: **new** `PaymentAuthorization` — persist the **new** `Id` for capture
- **Error**: Case A `ReauthorizePaymentError` · `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent` [500]

#### G. `client.Payments.CaptureAuthorizedPayment` — take the money

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/capture`
- **Signature**: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Pass `prefer: "return=representation"`** so fee/net are present. Default minimal omits them.
- **Body `CaptureRequest`**: omit `Amount` for full remaining capture; set `FinalCapture (final_capture): true` for fulfilment (default `false`).
- **Returns**: `PayPalServerSdk.Models.CapturedPayment` — **no wrapper**
- **Read**: `Id (id)`, `Status (status)`, `Amount (amount): Money?` (captured amount), **`SellerReceivableBreakdown (seller_receivable_breakdown)`** (`GrossAmount (gross_amount) !req`, **`PaypalFee (paypal_fee)`**, **`NetAmount (net_amount)`**, `ReceivableAmount (receivable_amount)`). Breakdown **not available when capture is pending**. (`records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`)
- **Error**: Case A `CaptureAuthorizedPaymentError` · `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent` [500]. 409 is **not** distinguishable from 422 via the accessor — read `Error.Name` / `Details[].Issue`. If already captured, `GetCapturedPayment` / stored capture id.

Do **not** use `Orders.CaptureOrder` for this flow (`intent` is `AUTHORIZE`; capture is on the **authorization id**).

#### H. `client.Payments.VoidPayment` — release hold

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/void`
- **Signature**: `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — **no body**
- **Returns**: `PaymentAuthorization` (`Status` → `Voided`)
- **Error**: Case A `VoidPaymentError` · `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent` [500]. Cannot void a fully captured auth (operation notes).

#### I. `client.Payments.RefundCapturedPayment`

- **HTTP**: `POST /v2/payments/captures/{capture_id}/refund`
- **Signature**: `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Idempotency**: caller key → `payPalRequestId` (45-day store). Same key must not create a second refund; different keys for distinct partials.
- **Full**: `body: null` or `RefundRequest` without `Amount`. **Partial**: `RefundRequest.Amount = Money { CurrencyCode, Value }`.
- **Returns**: `PayPalServerSdk.Models.Refund` — expose `Id` as API `refundId`; persist `Status`
- **Read**: `Id (id)`, `Status (status)`, `Amount`, `SellerPayableBreakdown.TotalRefundedAmount (total_refunded_amount)`, `PaypalFee`, `NetAmount`
- **Remaining refundable**: `GetCapturedPayment` → `Status` `PartiallyRefunded` / `Refunded` / `Completed`; never refund when `Refunded` or when remaining (captured − total refunded) < requested. PayPal 422 if over-refunded — still enforce locally.
- **Error**: Case A `RefundCapturedPaymentError` · `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent` [500]

#### J. `client.Payments.GetCapturedPayment` / `GetRefund`

- `GetCapturedPayment(string captureId, string? payPalMockResponse, …)` → `CapturedPayment` · `TryGetError` [401, 403, 404]
- `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, …)` → `Refund` · `TryGetError` [401, 403, 404]

#### K. Vault — setup token vs payment token vs customer id

| Kind | Operation | Persist? | Role |
|---|---|---|---|
| **Setup token** | `CreateSetupToken` / `GetSetupToken` | temporary `SetupTokenResponse.Id`; status `PaymentTokenStatus` | Optional two-step: card → setup token → `CreatePaymentToken` with `PaymentSource.Token = VaultTokenRequest { Id, Type = VaultTokenRequestType.SetupToken }`. **Not required** for this app. |
| **Payment token** | `CreatePaymentToken` | **`PaymentTokenResponse.Id` = `paymentMethodId`** (PayPal-generated vault id; same value as `CardRequest.VaultId` on pay) | Durable saved card |
| **Customer** | `Customer.Id` PayPal-generated; `MerchantCustomerId` = eShop shopper id | Persist **both** from `PaymentTokenResponse.Customer` (`CustomerResponse`). List requires `customerId` query | Vault association |

#### L. `client.Vault.CreatePaymentToken` — save a card

- **HTTP**: `POST /v3/vault/payment-tokens` · `operations/Vault.md`
- **Signature**: `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must be passed explicitly
- **Returns**: `PaymentTokenResponse`
- **Error**: Case A `CreatePaymentTokenError` · **`TryGetError1(out Error1)`** [400, 403, 404, 422, 500] · `TryGetRawError` — vault errors use `Error1` / `ErrorDetails1`, not `Error`
- **Request `PaymentTokenRequest`**: `Customer (customer): Customer?`; **`PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`**
  - `Customer.MerchantCustomerId` = signed-in shopper key; omit `Customer.Id` on first save (PayPal mints it)
  - `PaymentSource.Card`: `PaymentTokenRequestCard` — `Name`, `Number`, `Expiry` (`YYYY-MM`), `SecurityCode`, `BillingAddress` (`Address`), optional `Brand`
  - Do **not** persist PAN/CVC. Response has no PAN.
- **Read for API response**: `Id` → `paymentMethodId`; `PaymentSource.Card` (`CardPaymentTokenEntity`): `LastDigits (last_digits)`, `Brand (brand)`, `Expiry (expiry)`; `Customer.Id` / `MerchantCustomerId`
- **Payer-action**: if create returns links / status requiring payer action (`PaymentTokenStatus.PayerActionRequired`), STOP (same 3DS policy)

#### M. `client.Vault.ListCustomerPaymentTokens`

- **HTTP**: `GET /v3/vault/payment-tokens`
- **Signature**: `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query**: `customer_id` ← `customerId`, `page_size`, `page`, `total_required`
- **Returns**: `CustomerVaultPaymentTokensResponse` — `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `TotalItems`, `TotalPages`, `Customer`
- **Page all pages**: call with `totalRequired: true`, then `page = 1 .. TotalPages` (SDK pagination listed as **none** — only `page`, no `perPage` helper). Map each token like save-card display fields.
- **Error**: Case A `ListCustomerPaymentTokensError` · `TryGetError1(out Error1)` [400, 403, 500]
- Pass stored PayPal `Customer.Id` as `customerId` (wire `customer_id`). XML also says “merchant's/partner's system” — **UNVERIFIED** if `merchant_customer_id` is accepted; persist both.

#### N. `client.Vault.DeletePaymentToken`

- **HTTP**: `DELETE /v3/vault/payment-tokens/{id}`
- **Signature**: `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (`Task`)
- **Error**: Case A `DeletePaymentTokenError` · `TryGetError1(out Error1)` [400, 403, 500]
- After success the token must not be used as `VaultId`. No `payPalRequestId` param.

#### O. `client.Vault.GetPaymentToken`

- **Signature**: `GetPaymentToken(string id, …)` → `PaymentTokenResponse` · `TryGetError1` [403, 404, 422, 500]

#### P. `client.TransactionSearch.SearchTransactions` — reconciliation

- **HTTP**: `GET /v1/reporting/transactions` · `operations/TransactionSearch.md` · **only Case B operation in this SDK**
- **Signature**: `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 8 params `transactionId` … `terminalId`: **must pass explicitly** (`null` to skip)
- **Query wire**: `start_date`, `end_date`, `transaction_id`, `transaction_type`, `transaction_status`, `transaction_amount`, `transaction_currency`, `payment_instrument_type`, `store_id`, `terminal_id`, `fields`, `balance_affecting_records_only`, `page_size`, `page`
- **Date format**: RFC-3339; **seconds required**; fractional seconds optional. **Maximum range 31 days** (`Api/TransactionSearch.cs`). Split `from`/`to` into ≤31-day windows, then page each window.
- **Cover the whole range**: SDK has **no auto-pager**. Loop `page` from 1 while `page <= (TotalPages ?? 1)`. Use `pageSize` up to 100 (default). Optional `fields: "all"` vs default `"transaction_info"`.
- **Returns**: `SearchResponse` — `TransactionDetails (transaction_details)`, `Page`, `TotalItems`, `TotalPages`, `Links`, `LastRefreshedDatetime`
- **Line-up fields** (`TransactionDetails.TransactionInfo` / `TransactionInformation`): `TransactionId (transaction_id)`, `PaypalReferenceId`, `TransactionInitiationDate`, `TransactionAmount`, `FeeAmount (fee_amount)`, `TransactionStatus (transaction_status)` (string, not enum), `InvoiceId (invoice_id)`, `CustomField (custom_field)`, `PaymentMethodType`
- **Lag**: executed txns can take **up to 3 hours** to appear; history up to 3 years (`operations/TransactionSearch.md`)
- **Error**: Case B `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. Optional deserialize to `SearchError` (`Name`/`Message`/`DebugId`/`Details`)

---

### Error types — how to read HTTP status and body

Core: `PayPalServerSdk.Core.Exceptions.SdkException<TError>` has **`TError Error { get; init; }` only** (no `StatusCode` on the exception). (`Core/Exceptions/SdkException.cs`, `sdk-map.md`)

| Case | `TError` | Status | Body |
|---|---|---|---|
| A (39 ops) | `{Operation}Error : ApiError` | Accessors **group** several codes (`TryGetError` may be 400+401+422 together). Typed `Error` **has no status field**. `TryGetNoContent(out RawError)` on Payments 500s **does** expose `RawError.StatusCode`. Fallback `TryGetRawError`. | `Error.Name (name) !req`, `Message (message) !req`, `DebugId (debug_id) !req`, `Details[].Issue (issue) !req`, `Details[].Description`, `Details[].Field` |
| Vault A | `TryGetError1(out Error1)` | same grouping | `Error1` same shape; details `ErrorDetails1`; links `ErrorLinkDescription` (`Rel` optional) |
| B | `RawError` | `StatusCode: HttpStatusCode` | `ReadAsString()` / `ReadAsJson<T>()` |

`INSTRUMENT_DECLINED`, `AUTHORIZATION_EXPIRED`, etc. are **not SDK enums**. Compare `Error.Name` and each `ErrorDetails.Issue` (strings). **UNVERIFIED** live spelling beyond those being ordinary issue/name strings.

No `…Result` (no-throw) variants exist on this SDK (`sdk-map.md`).

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership/lifetime vs the SDK wrapper, and `AddPayPalServerSdkClient` factory shape, are not implied by the constructor. **MUST load `dotnet-client-initialization`** before `new PayPalServerSdkClient` or DI.

⚠ Step 1 (auth) — credentials live on `options.Oauth2` as `OAuth2ClientCredentials`; token fetch uses the same `Server.Default` base as API calls. **MUST load `dotnet-authentication`** before wiring `PayPal:ClientId`/`ClientSecret`.

⚠ Step 1 (BaseUrl / retries / timeouts) — `RetryOptions.Timeout` and `HttpMethodsToRetry` are **not** the timeout on the `HttpClient` you register, and they do **not** bound a whole business operation (authorize+capture). A transport failure can interact with non-idempotent POSTs even when `payPalRequestId` is set, because the client also sends a fresh `Idempotency-Key` per invoke. **MUST load `dotnet-configuration-resilience`** before setting `Retry`, `Server.Default.Sandbox.BaseUrl`, or assuming retries are safe.

⚠ Steps 2–10 (every call) — many parameters are nullable **without C# defaults** and **must be passed** (`null` to skip). Positional calls mis-bind. Prefer named arguments; cancellation is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.Orders`/`Payments`/`Vault`/`TransactionSearch` call.

⚠ Steps 2, 8 (models / enums / no unions) — records are `init`-only with `required`; enums are `StringEnum<T>` not C# enums; payment source / seller breakdown are nullable properties not AnyOf. Unmodeled JSON is dropped. **MUST load `dotnet-models`** before constructing `OrderRequest` / `CardRequest` / `PaymentTokenRequest`.

⚠ Steps 2–10 (error boundary) — Case A vs Case B differ per operation (`SearchTransactions` is the only Case B); vault uses `TryGetError1` not `TryGetError`; accessors collapse multiple HTTP statuses so `INSTRUMENT_DECLINED` / `AUTHORIZATION_EXPIRED` must be read from `Error.Name` / `Details[].Issue`, not from `SdkException` status. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Error boundary — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Error boundary — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (tests) — the `HttpClient` constructor argument is the test seam; do not stub unsealed controllers. **MUST load `dotnet-testing`** before faking PayPal.

---

## REQUIRED READING

Load these **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — `PayPalServerSdkClient` / `AddPayPalServerSdkClient` / `HttpClient` |
| `dotnet-authentication` | Step 1 — `Oauth2` / `OAuth2ClientCredentials` |
| `dotnet-configuration-resilience` | Step 1 — retries, timeouts, `Server.Default.Sandbox.BaseUrl`, list paging |
| `dotnet-calling-endpoints` | Steps 2–10 — named args, must-pass nullables, `ct:`, `prefer` |
| `dotnet-models` | Steps 2, 4, 6, 8 — records, `StringEnum<T>`, no unions |
| `dotnet-error-handling` | Every operation + both `JsonException` directions above |
| `dotnet-testing` | Tests for the integration layer |

---

## Assumptions & Blockers

- **Sandbox only:** `ServerEnvironment` has a single member `Sandbox`. Config `PayPal:Environment` cannot select Production in this SDK release; treat non-sandbox values as an app configuration error.
- **No AnyOf/OneOf** in this SDK (`unions.md` empty). Payment source / amount / seller receivable are records.
- **Issue codes not enumerated:** `INSTRUMENT_DECLINED`, `AUTHORIZATION_EXPIRED`, and similar are not SDK enums. Match `Error.Name` / `ErrorDetails.Issue` strings; exact live spellings and whether they appear on `Name` vs `Issue` are **UNVERIFIED**.
- **No `AuthorizationStatus.Expired`:** staleness is `ExpirationTime` + reauthorize/capture errors + the 3/29/30-day window in operation remarks. After 30 days, the SDK/API require a **new** authorized payment (shopper present) — fulfilment must fail with an operator-actionable error, not invent a new card charge.
- **Reauthorize “once” vs “multiple”:** `ReauthorizeRequest` XML says once; `ReauthorizePayment` operation notes say multiple. **UNVERIFIED** which the live API enforces — attempt reauthorize and surface 422 `Issue` if it fails.
- **`SearchTransactions` max 31 days** per call (`Api/TransactionSearch.cs`). Longer `from`/`to` **must** be split; this is required by the mapped API, not a substitute for a missing SDK op.
- **List `customerId` identity UNVERIFIED:** persist PayPal `CustomerResponse.Id` and `MerchantCustomerId`; pass PayPal `Id` into `ListCustomerPaymentTokens`.
- **Vault availability:** `PayPalServerSdkClient` remarks say the Vault controller is *Available in the US only.* If eShopOnWeb is expected to vault cards outside that, that is a **product/account blocker**, not something the SDK map can lift.
- **`Idempotency-Key` always random:** caller idempotency is `payPalRequestId` → `PayPal-Request-Id`. The extra per-call `Idempotency-Key: Guid.NewGuid()` is generated by the SDK (`Api/*.cs`).
- **Capabilities present:** authorize-then-capture, reauthorize, void, refund (full/partial + idempotency param), transaction search + manual paging, vault create/list/delete, pay-with-`vault_id` are all in the map. Nothing in this scope is missing as an unmapped operation.
- **PCI:** `CardRequest` XML notes passing PAN/CVV requires PCI SAQ D. The reference app still sends raw card fields as specified; hosted fields are out of scope.
- **Prefer header:** fee/net on capture require `prefer: "return=representation"`; default `"return=minimal"` is insufficient for the fulfilment recording requirement.
