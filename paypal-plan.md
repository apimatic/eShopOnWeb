# PayPal .NET SDK — eShopOnWeb payments + saved cards

NuGet: `AsadAli.Checkout.Sdk` (version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`.

## Scope & sequence

| Step | App surface | SDK operations |
|---|---|---|
| 1 | Bind `PayPal:*` / env (`PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, `PAYPAL_ENVIRONMENT`, `PAYPAL_CURRENCY`); register client; optional `PayPal:BaseUrl` override | Client construction only |
| 2 | `POST /api/orders` — local order, awaiting payment (no PayPal call) | — |
| 3 | `POST /api/orders/{orderId}/pay` — hold funds (not capture). One-off PAN **or** saved `vault_id`. Amount = order total to the cent. Idempotent. | `Orders.CreateOrder` then `Orders.AuthorizeOrder`. Persist PayPal order id + authorization id/status/`expiration_time`. If `PAYER_ACTION_REQUIRED` / 3DS challenge → **STOP** (no browser round-trip). |
| 4 | `POST /api/orders/{orderId}/fulfil` — capture hold. If authorization honor/expiry is stale, `Payments.ReauthorizePayment` then capture the **new** authorization id. Surface operator-actionable errors when reauth is impossible. Read captured amount, PayPal fee, net. Idempotent. | `Payments.GetAuthorizedPayment` (status/expiry) → optional `Payments.ReauthorizePayment` → `Payments.CaptureAuthorizedPayment` |
| 5 | `POST /api/orders/{orderId}/cancel` — release hold, no money moved. Idempotent. | `Payments.VoidPayment` |
| 6 | `POST /api/orders/{orderId}/refunds` — full or partial after fulfilment. Caller-supplied idempotency key. Never refund more than captured − already refunded. | `Payments.GetCapturedPayment` (remaining) → `Payments.RefundCapturedPayment` |
| 7 | `GET /api/my-orders` — caller orders + payment state from persisted PayPal ids/statuses (refresh via GET if stale) | `Payments.GetAuthorizedPayment` / `GetCapturedPayment` / `GetRefund` as needed |
| 8 | `GET /api/reconciliation?from=&to=` — **all pages** of PayPal transactions in the ISO-8601 range, lined up to eShop orders | `TransactionSearch.SearchTransactions` (loop `page`) |
| 9 | `POST /api/payment-methods` — vault card for signed-in shopper; safe descriptor in response | `Vault.CreatePaymentToken` |
| 10 | `GET /api/payment-methods` — list saved cards (last digits, brand, expiry; never PAN) | `Vault.ListCustomerPaymentTokens` and/or `Vault.GetPaymentToken` per stored id |
| 11 | `DELETE /api/payment-methods/{paymentMethodId}` | `Vault.DeletePaymentToken` |
| 12 | Pay with saved card (step 3) | `AuthorizeOrder` with `CardRequest.VaultId` = payment-token id |

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

No-throw `…Result` variants: **absent** on every operation below. Every call is throw-only.

`RequestOptions? requestOptions` is optional with default `null` on every operation — omit it (or pass `null`). Do not invent extra headers there; idempotency is the `payPalRequestId` parameter.

Prefer header: signatures default `prefer: "return=minimal"`. Pass **`prefer: "return=representation"`** on authorize / capture / reauthorize / void / refund so the returned model is populated for persistence. Same C# return type either way; whether nested payment collections are filled on `minimal` is **UNVERIFIED** — if `PurchaseUnits[].Payments` is null/empty after authorize, follow up with `Orders.GetOrder` (or `Payments.GetAuthorizedPayment` once you have the id).

---

### 1. Client construction, credentials, sandbox, BaseUrl

| Fact | Value | Source |
|---|---|---|
| Client | `PayPalServerSdk.PayPalServerSdkClient` | `sdk-map.md` · `PayPalServerSdkClient.cs` |
| Constructor | `PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(this IServiceCollection, Action<PayPalServerSdkClientOptions>? configure = null)` — registers a singleton client via `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Options type | `PayPalServerSdk.PayPalServerSdkClientOptions` | `sdk-map.md` · `PayPalServerSdkClientOptions.cs` |

**`PayPalServerSdkClientOptions` members** (`sdk-map.md` · `PayPalServerSdkClientOptions.cs`):

| Property | Type | Namespace |
|---|---|---|
| `Environment` | `ServerEnvironment` | `PayPalServerSdk.Servers` |
| `Retry` | `RetryOptions` | `PayPalServerSdk.Core.Configuration` |
| `Logging` | `LoggingOptions` | (options type on client options; leave default unless you intentionally wire logging) |
| `Server` | `ServerOptions` | `PayPalServerSdk` (repo-root type) |
| `Oauth2` | `OAuth2ClientCredentials?` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `Oauth2TokenStrategy` | `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `PayPalServerSdk.Core.Authentication.OAuth2` — **leave null**; the client installs the built-in token strategy |

**Credentials** — `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials`:

| Member | Type | Notes |
|---|---|---|
| `ClientId` | `string` **required** (`init`) | bind `PayPal:ClientId` / `PAYPAL_CLIENT_ID` |
| `ClientSecret` | `string` **required** (`init`) | bind `PayPal:ClientSecret` / `PAYPAL_CLIENT_SECRET` |
| `Scope` | `string?` | omit |

Token request (when `Oauth2TokenStrategy` is null): `POST {BaseUrl}/v1/oauth2/token` with HTTP Basic (`clientId:clientSecret`) and form `grant_type=client_credentials`. That URL is built from the **same** server template as every other call (`Server.Default("/v1/oauth2/token")`). Source: `AuthSchemes.cs`, `OAuth2ClientCredentialsStrategy.cs`.

**Environment** — `PayPalServerSdk.Servers.ServerEnvironment` (`Servers/ServerEnvironment.cs` · `sdk-map.md` *Servers & auth*):

| Member | Wire | Meaning |
|---|---|---|
| `ServerEnvironment.Sandbox` | `"Sandbox"` | **only** member; `Default()` returns Sandbox |

There is **no** Live/Production member. Bind `PayPal:Environment` / `PAYPAL_ENVIRONMENT` to `ServerEnvironment.Sandbox` when the value is sandbox (or omitted). Any other value is a **GAP** (see Assumptions & Blockers).

**Custom BaseUrl that applies to every call including the token request**

| Type | Namespace | Members | Source |
|---|---|---|---|
| `ServerOptions` | `PayPalServerSdk` | `Default: DefaultOptions` | `ServerOptions.cs` |
| `DefaultOptions` | `PayPalServerSdk.Servers` | `Sandbox: SandboxOptions` | `Servers/DefaultOptions.cs` |
| `DefaultOptions.SandboxOptions` | nested | `BaseUrl: string` default `"https://api-m.sandbox.paypal.com"` | `Servers/DefaultOptions.cs` |
| `Server.Default(path)` | `PayPalServerSdk` | resolves `options.Server.Default.Sandbox.BaseUrl` + path for **all** HTTP including OAuth | `Server.cs`, `DefaultOptions.Resolve` |

When `PayPal:BaseUrl` is set, assign it **verbatim** to `options.Server.Default.Sandbox.BaseUrl`. Do not also set `HttpClient.BaseAddress` to a different host — every SDK path (Orders, Payments, Vault, TransactionSearch, **and** `/v1/oauth2/token`) is resolved from this one property.

Settings mapping:

| Config / env | SDK assignment |
|---|---|
| `PayPal:ClientId` / `PAYPAL_CLIENT_ID` | `options.Oauth2.ClientId` |
| `PayPal:ClientSecret` / `PAYPAL_CLIENT_SECRET` | `options.Oauth2.ClientSecret` |
| `PayPal:Environment` / `PAYPAL_ENVIRONMENT` | `options.Environment = ServerEnvironment.Sandbox` (only supported value) |
| `PayPal:Currency` / `PAYPAL_CURRENCY` | `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode` on every amount |
| `PayPal:BaseUrl` (optional) | `options.Server.Default.Sandbox.BaseUrl` verbatim |

Controllers on the client (`PayPalServerSdkClient.cs`): `Orders`, `Payments`, `Vault`, `TransactionSearch` (and `Subscriptions`, unused).

---

### 2. AUTHORIZE a card payment (hold, not capture)

**Do not** call `Orders.CaptureOrder` or create with `CheckoutPaymentIntent.Capture`. Hold path = create with `AUTHORIZE` then `AuthorizeOrder`, later `Payments.CaptureAuthorizedPayment`.

#### 2a. `Orders.CreateOrder` — create the PayPal order (amount only; no card yet)

| | |
|---|---|
| HTTP | `POST /v2/checkout/orders` |
| Controller | `client.Orders` |
| Signature | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly (nullable, no default) | `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion` — pass `null` to skip; **do pass** `payPalRequestId` for idempotency |
| Returns | `PayPalServerSdk.Models.Order` |
| Error | **Case A** `SdkException<PayPalServerSdk.Errors.CreateOrderError>` |
| Accessors | `TryGetError(out PayPalServerSdk.Models.Error)` **[400, 401, 422]** · `TryGetRawError(out PayPalServerSdk.Core.ErrorResponse.RawError)` fallback |
| Pagination | none |
| Map | `operations/Orders.md` |

**Request `PayPalServerSdk.Models.OrderRequest`** (`records-1-Ac-Pa.md`):

| Field (wire) | Type | Required |
|---|---|---|
| `Intent (intent)` | `PayPalServerSdk.Models.Enums.CheckoutPaymentIntent` | **!req** — use `CheckoutPaymentIntent.Authorize` (`AUTHORIZE`) |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | **!req** — one unit |
| `Payer (payer)` | `Payer?` | omit |
| `PaymentSource (payment_source)` | `PaymentSource?` | **omit** (card goes on AuthorizeOrder so create does not hold) |
| `ApplicationContext (application_context)` | `OrderApplicationContext?` | omit (avoid `ReturnUrl`/`CancelUrl` browser flow) |

**`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`):

| Field (wire) | Type | Required |
|---|---|---|
| `Amount (amount)` | `AmountWithBreakdown` | **!req** — must equal eShop order total to the cent |
| `CustomId (custom_id)` | `string?` | persist eShop order id (reconciliation join) |
| `InvoiceId (invoice_id)` | `string?` | same eShop order id / invoice |
| `ReferenceId (reference_id)` | `string?` | optional |

**`AmountWithBreakdown`** (`records-1-Ac-Pa.md`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?` (optional). `Money` is the same two required strings (`records-1-Ac-Pa.md`). Format `Value` as a decimal string that matches the order total to the cent (e.g. `"12.50"`).

**Response `Order`** (`records-1-Ac-Pa.md`) — persist:

| Field (wire) | Type | Persist |
|---|---|---|
| `Id (id)` | `string?` | **PayPal order id** — required for AuthorizeOrder / GetOrder |
| `Status (status)` | `OrderStatus?` | expect `Created` |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnit>?` | confirm amount |

#### 2b. `Orders.AuthorizeOrder` — put the hold

| | |
|---|---|
| HTTP | `POST /v2/checkout/orders/{id}/authorize` |
| Signature | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalClientMetadataId`, `payPalAuthAssertion`, `body` |
| Returns | `PayPalServerSdk.Models.OrderAuthorizeResponse` |
| Error | **Case A** `SdkException<AuthorizeOrderError>` |
| Accessors | `TryGetError(out Error)` **[400, 401, 403, 404, 422, 500]** · `TryGetRawError(out RawError)` |
| Map | `operations/Orders.md` |

Idempotency: pass a stable `payPalRequestId` (caller/pay key). 409 is **not** in this operation's accessor list.

**Request `OrderAuthorizeRequest`** (`records-1-Ac-Pa.md`): `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`.

**`OrderAuthorizeRequestPaymentSource`**: `Card (card): CardRequest?` (use this). `Token (token): Token?` is **not** the vaulted-card path — `Token.Type` is only `TokenType.BillingAgreement` (`enums.md`).

**One-off direct card — `CardRequest`** (`records-1-Ac-Pa.md`):

| Field (wire) | Type | Use |
|---|---|---|
| `Name (name)` | `string?` | cardholder name |
| `Number (number)` | `string?` | PAN (sandbox test: `4111111111111111`) — PCI SAQ D; never log/store |
| `Expiry (expiry)` | `string?` | map: string, **no format in map** — **UNVERIFIED**; if 422 on this field, surface `Error.Details` |
| `SecurityCode (security_code)` | `string?` | CVC |
| `BillingAddress (billing_address)` | `Address?` | `Address.CountryCode (country_code): string !req`; optional `AddressLine1/2`, `AdminArea1/2`, `PostalCode` |
| `VaultId (vault_id)` | `string?` | **do not set** on one-off PAN |
| `ExperienceContext (experience_context)` | `CardExperienceContext?` | **omit** (`ReturnUrl`/`CancelUrl` invite a browser) |
| `Attributes (attributes)` | `CardAttributes?` | optional verification (see 3DS) |
| `StoredCredential (stored_credential)` | `CardStoredCredential?` | omit on first one-off |

**Saved card — same `CardRequest`, no PAN:**

| Field (wire) | Use |
|---|---|
| `VaultId (vault_id)` | payment-token `Id` from `CreatePaymentToken` / list |
| `Number` / `SecurityCode` | omit |
| `StoredCredential` | `PaymentInitiator` **!req**, `PaymentType` **!req**, `Usage` optional. Shopper-present: `PaymentInitiator.Customer` + `StoredPaymentSourcePaymentType.Unscheduled` (or `OneTime`) + `StoredPaymentSourceUsageType.Subsequent` |

**3DS / challenge — STOP, no browser round-trip**

| Signal | Where | Action |
|---|---|---|
| `OrderAuthorizeResponse.Status == OrderStatus.PayerActionRequired` (`PAYER_ACTION_REQUIRED`) | `enums.md` `OrderStatus` | **STOP**. Report that a payer/browser challenge is required; do not follow `Links` or build an approval UI. |
| `PaymentSource.Card.AuthenticationResult.ThreeDSecure.AuthenticationStatus == ParesStatus.C` | `AuthenticationResponse` / `ThreeDSecureAuthenticationResponse` (`records-1`, `records-2`); `ParesStatus.C (C)` = challenge (`enums.md`) | **STOP** the same way. |
| `CardExperienceContext.ReturnUrl` / `CancelUrl` | `records-1-Ac-Pa.md` | Do **not** set — that is the browser-return path. |
| `CardAttributes.Verification.Method` | `CardVerification.Method` default `OrdersCardVerificationMethod.ScaWhenRequired` (`SCA_WHEN_REQUIRED`) | To prefer a **non-browser** path, set `OrdersCardVerificationMethod.AvsCvv` (`AVS_CVV`). Whether that always avoids a challenge is **UNVERIFIED** — still inspect `PAYER_ACTION_REQUIRED` and stop. |
| `LiabilityShift` | `AuthenticationResponse.LiabilityShift` (`No` / `Possible` / `Unknown`) | informational; do not treat as a redirect. |

`OrderAuthorizeResponse.Links` is `IReadOnlyList<LinkDescription>` (`Href !req`, `Rel !req`, `Method`). Map does **not** name a challenge-URL rel. Do not implement a redirect from `Links`; status `PAYER_ACTION_REQUIRED` is sufficient to stop.

**Response envelope `OrderAuthorizeResponse`** (`records-1-Ac-Pa.md`) — same shape as `Order` plus authorize payment source. Inner hold lives under **`PurchaseUnits[].Payments.Authorizations[]`** (`PaymentCollection` · `records-2-Pa-Ve.md`). Authorization record: `AuthorizationWithAdditionalData` (`records-1-Ac-Pa.md`).

**Persist (PayPal-owned state for later capture / void / reauth):**

| Field (wire) | Type | Why |
|---|---|---|
| PayPal order `Id` | `string` | GetOrder / correlate |
| `Authorizations[0].Id (id)` | `string?` | capture, void, reauth, GET |
| `Authorizations[0].Status (status)` | `AuthorizationStatus?` | `Created` = hold ready |
| `Authorizations[0].Amount (amount)` | `Money?` | confirm equals order total |
| `Authorizations[0].ExpirationTime (expiration_time)` | `string?` | stale-hold detection |
| `Authorizations[0].CreateTime` / `UpdateTime` | `string?` | honor-period / reauth window |
| Order `Status` | `OrderStatus?` | must **not** be `PayerActionRequired` |

If authorizations array is empty: `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, …)` returns `Order` — pass the three nullable args explicitly (`null` ok). Map: `operations/Orders.md`.

`GetAuthorizedPayment` once you have the authorization id (section 3).

---

### 3. CAPTURE (fulfilment) + REAUTHORIZE (stale hold)

#### 3a. `Payments.GetAuthorizedPayment` — inspect hold before capture

| | |
|---|---|
| HTTP | `GET /v2/payments/authorizations/{authorization_id}` |
| Signature | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalAuthAssertion` |
| Returns | `PayPalServerSdk.Models.PaymentAuthorization` |
| Error | **Case A** `SdkException<GetAuthorizedPaymentError>` |
| Accessors | `TryGetError(out Error)` **[401, 403, 404]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError` |
| Map | `operations/Payments.md` |

**`PaymentAuthorization`** (`records-2-Pa-Ve.md`): `Status`, `StatusDetails`, `Id`, `Amount`, `ExpirationTime`, `CreateTime`, `UpdateTime`, `Links`, …

Capture only when `Status` is `AuthorizationStatus.Created` (or `PartiallyCaptured` if you ever split — this app captures in full). Do **not** capture `Voided` / `Captured` / `Denied`.

#### 3b. `Payments.ReauthorizePayment` — renew a stale authorization

Map notes (`operations/Payments.md` **ReauthorizePayment**), verbatim constraints:

- Reauthorize after the initial **three-day honor period** expires, to ensure funds are still available.
- Within the **29-day** authorization period you can issue re-authorizations after the honor period expires.
- If **30 days** have transpired since the **original** authorization, you **must create an authorized payment** instead of reauthorizing (this app: new `CreateOrder` + `AuthorizeOrder` with card or `vault_id` — **operator-actionable**: authorization past reauth window; shopper must pay again).
- A reauthorized payment has a **new 3-day honor period**.
- You can reauthorize from **day 4 to 29** after the 3-day honor period.
- Supports **only** the `amount` request parameter.

| | |
|---|---|
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/reauthorize` |
| Signature | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalRequestId`, `payPalAuthAssertion`, `body` |
| Returns | `PaymentAuthorization` — **new `Id`**; persist it and use it for capture |
| Error | **Case A** `SdkException<ReauthorizePaymentError>` |
| Accessors | `TryGetError(out Error)` **[400, 401, 403, 404, 422]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError` |
| Map | `operations/Payments.md` |

**`ReauthorizeRequest`** (`records-2-Pa-Ve.md`): `Amount (amount): Money?` — set to the **order total** (`CurrencyCode` + `Value`).

**When reauth is possible vs not (from map notes + errors):**

| Condition | What to do |
|---|---|
| Hold still `Created` and not past honor/expiry | Skip reauth; capture current id |
| Honor period expired, **< 30 days** from original `CreateTime` | `ReauthorizePayment` on current authorization id; persist **new** `PaymentAuthorization.Id` + `ExpirationTime` + `Status`; capture the **new** id |
| **≥ 30 days** from original authorization | Do **not** call reauth expecting success. Operator message: authorization can no longer be renewed; create a new authorized payment (shopper must pay again). |
| `TryGetError` **422** (or 400) | Read `Error.Name`, `Error.Message`, `Error.DebugId`, `Error.Details[].Issue` + `Description` + `Field` and return those strings to the operator. Specific `Issue` codes are **UNVERIFIED** in the map — do not invent them. |
| `TryGetError` **404** | Authorization gone; operator: cannot renew or capture this hold. |
| `TryGetError` **403** | Not permitted; surface `Error` payload. |

Fulfilment algorithm: GET authorization → if expired/stale and within window, reauth (idempotent `payPalRequestId`) → capture **latest** authorization id. If reauth fails, **fail fulfilment** with the PayPal `Error` payload (do not capture blindly).

#### 3c. `Payments.CaptureAuthorizedPayment`

| | |
|---|---|
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/capture` |
| Signature | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` |
| Returns | `PayPalServerSdk.Models.CapturedPayment` |
| Error | **Case A** `SdkException<CaptureAuthorizedPaymentError>` |
| Accessors | `TryGetError(out Error)` **[400, 401, 403, 404, 409, 422]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError` |
| Map | `operations/Payments.md` |

Idempotency: `payPalRequestId`. **409** is on this operation — treat as conflict / possible replay; GET capture (if you have id) or GET authorization and reconcile rather than double-capturing.

**`CaptureRequest`** (`records-1-Ac-Pa.md`): `Amount (amount): Money?` (omit for full remaining), `FinalCapture (final_capture): bool? = false` — set **`true`** for fulfilment, `InvoiceId`, `NoteToPayer`, `SoftDescriptor` optional.

**Captured amount, PayPal fee, net proceeds — `CapturedPayment.SellerReceivableBreakdown`** (`records-1-Ac-Pa.md` + `SellerReceivableBreakdown` `records-2-Pa-Ve.md`):

| Field (wire) | Type | App |
|---|---|---|
| `Id (id)` | `string?` | persist **capture id** (refunds) |
| `Status (status)` | `CaptureStatus?` | expect `Completed` |
| `Amount (amount)` | `Money?` | captured gross (payer) |
| `SellerReceivableBreakdown.GrossAmount (gross_amount)` | `Money !req` | captured amount |
| `SellerReceivableBreakdown.PaypalFee (paypal_fee)` | `Money?` | **PayPal fee** |
| `SellerReceivableBreakdown.NetAmount (net_amount)` | `Money?` | **net proceeds** |
| `CreateTime` / `UpdateTime` | `string?` | persist |
| `SupplementaryData.RelatedIds` | `RelatedIdentifiers?` | `OrderId`, `AuthorizationId`, `CaptureId` (`records-2-Pa-Ve.md`) |

Breakdown is documented as **not available for pending** captures (`SellerReceivableBreakdown` summary). If `Status == Pending`, persist id/status and re-GET via `GetCapturedPayment`.

**`GetCapturedPayment(string captureId, string? payPalMockResponse, …)`** — returns `CapturedPayment`; Case A `GetCapturedPaymentError`; `TryGetError` **[401, 403, 404]** · `TryGetNoContent` **[500]**. Map: `operations/Payments.md`.

---

### 4. VOID / release (cancel before fulfilment)

| | |
|---|---|
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/void` |
| Signature | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` |
| Returns | `PaymentAuthorization` |
| Error | **Case A** `SdkException<VoidPaymentError>` |
| Accessors | `TryGetError(out Error)` **[401, 403, 404, 409, 422]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError` |
| Notes | Voids/cancels an authorized payment. **Cannot void a fully captured** authorization. |
| Map | `operations/Payments.md` |

Idempotency: `payPalRequestId`. **409** = conflict (already voided / captured) — GET authorization; if `Status == Voided`, treat cancel as success.

Persist `Status == AuthorizationStatus.Voided`. No capture/refund after void.

---

### 5. REFUND a capture (full / partial)

| | |
|---|---|
| HTTP | `POST /v2/payments/captures/{capture_id}/refund` |
| Signature | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` |
| Returns | `PayPalServerSdk.Models.Refund` |
| Error | **Case A** `SdkException<RefundCapturedPaymentError>` |
| Accessors | `TryGetError(out Error)` **[400, 401, 403, 404, 409, 422]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError` |
| Notes | Full refund: empty/null body. Partial: `amount` on body. |
| Map | `operations/Payments.md` |

**Caller-supplied idempotency key** → `payPalRequestId`. **409** = replay/conflict; GET refund if id known or GET capture and reconcile.

**`RefundRequest`** (`records-2-Pa-Ve.md`): `Amount (amount): Money?` (omit = full), `CustomId`, `InvoiceId`, `NoteToPayer` optional.

**`Refund`** persist: `Id`, `Status` (`RefundStatus`: `Cancelled`, `Failed`, `Pending`, `Completed`), `Amount`, `SellerPayableBreakdown`.

**Remaining refundable (no dedicated SDK field on `CapturedPayment`):**

1. Before refund: `GetCapturedPayment`. If `Status == CaptureStatus.Refunded`, remaining = **0** — reject. If `Declined`/`Failed`, not refundable.
2. Remaining = captured gross − already refunded. Use `CapturedPayment.Amount` / `SellerReceivableBreakdown.GrossAmount` minus sum of prior refund `Amount`s **or** latest `SellerPayableBreakdown.TotalRefundedAmount (total_refunded_amount)` on a refund response (`records-2-Pa-Ve.md`).
3. Reject if requested partial `Money.Value` > remaining (same currency).
4. After refund, persist refund `Id` + `Status` + `SellerPayableBreakdown.TotalRefundedAmount`.

**`GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, …)`** — returns `Refund`; Case A `GetRefundError`; `TryGetError` **[401, 403, 404]** · `TryGetNoContent` **[500]**. Map: `operations/Payments.md`.

---

### 6. VAULT / save a card

Vault controller notes (client XML): Payment Method Tokens API v3 — *available in the US only* (see Blockers if that applies).

#### 6a. Save — `Vault.CreatePaymentToken` (direct card; no setup-token browser)

| | |
|---|---|
| HTTP | `POST /v3/vault/payment-tokens` |
| Signature | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalRequestId` |
| Returns | `PayPalServerSdk.Models.PaymentTokenResponse` |
| Error | **Case A** `SdkException<CreatePaymentTokenError>` |
| Accessors | `TryGetError1(out PayPalServerSdk.Models.Error1)` **[400, 403, 404, 422, 500]** · `TryGetRawError` |
| Map | `operations/Vault.md` |

Do **not** use `CreateSetupToken` for this app’s no-browser save. Setup-token has `ExperienceContext.ReturnUrl`/`CancelUrl` and `PaymentTokenStatus.PayerActionRequired` — that is the approval/3DS path. If you ever receive `PayerActionRequired`, **STOP** the same way as authorize.

**`PaymentTokenRequest`** (`records-2-Pa-Ve.md`): `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.

**`Customer`**: `Id (id): string?` (PayPal customer id, if already stored), `MerchantCustomerId (merchant_customer_id): string?` (eShop shopper id). Pass `MerchantCustomerId` on first save; persist returned PayPal `Customer.Id`.

**`PaymentTokenRequestPaymentSource`**: `Card (card): PaymentTokenRequestCard?` (use this). `Token (token): VaultTokenRequest?` is setup-token exchange only (`VaultTokenRequestType.SetupToken`).

**`PaymentTokenRequestCard`**: `Name`, `Number`, `Expiry`, `SecurityCode`, `Brand`, `BillingAddress` — same PAN path as pay; never echo `Number`/`SecurityCode` in API responses.

**`PaymentTokenResponse`** — persist / safe describe:

| Field (wire) | Type | App |
|---|---|---|
| `Id (id)` | `string?` | **payment method id** (vault token); this is `CardRequest.VaultId` on pay |
| `Customer (customer)` | `CustomerResponse?` | persist `Id` (PayPal customer id) + `MerchantCustomerId` |
| `PaymentSource.Card` | `CardPaymentTokenEntity?` | **safe** descriptor (below) |
| `Links` | HATEOAS | ignore for app API |

**`CardPaymentTokenEntity`** (`records-1-Ac-Pa.md`) — never PAN:

| Field (wire) | Type |
|---|---|
| `LastDigits (last_digits)` | `string?` |
| `Brand (brand)` | `CardBrand?` |
| `Expiry (expiry)` | `string?` |
| `Name (name)` | `string?` |
| `Type (type)` | `CardType?` |
| `VerificationStatus (verification_status)` | `CardVerificationStatus?` (`Verified` / `Failed`) |

#### 6b. List — `Vault.ListCustomerPaymentTokens`

| | |
|---|---|
| HTTP | `GET /v3/vault/payment-tokens` |
| Signature | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Query wire | `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired` |
| Returns | `CustomerVaultPaymentTokensResponse` |
| Error | **Case A** `SdkException<ListCustomerPaymentTokensError>` |
| Accessors | `TryGetError1(out Error1)` **[400, 403, 500]** · `TryGetRawError` |
| Pagination | SDK has **no** auto-pager. Loop `page` using `TotalPages`. Pass `totalRequired: true` so `TotalPages`/`TotalItems` are populated. |
| Map | `operations/Vault.md` |

`customerId` is **required** and is PayPal’s customer id (`CustomerResponse.Id`), not the eShop shopper id.

**`CustomerVaultPaymentTokensResponse`**: `TotalItems`, `TotalPages`, `Customer`, `PaymentTokens: IReadOnlyList<PaymentTokenResponse>?`, `Links`.

If PayPal never returned a customer id: keep token ids in the eShop DB and `GetPaymentToken(string id)` per row (`PaymentTokenResponse`; Case A `GetPaymentTokenError`; `TryGetError1` **[403, 404, 422, 500]**).

#### 6c. Delete — `Vault.DeletePaymentToken`

| | |
|---|---|
| HTTP | `DELETE /v3/vault/payment-tokens/{id}` |
| Signature | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `void` (`Task`) |
| Error | **Case A** `SdkException<DeletePaymentTokenError>` |
| Accessors | `TryGetError1(out Error1)` **[400, 403, 500]** · `TryGetRawError` |
| Map | `operations/Vault.md` |

`id` = payment-token id (same as `POST` response `Id` / `{paymentMethodId}`).

#### 6d. Pay with vaulted token

`AuthorizeOrder` body: `OrderAuthorizeRequest.PaymentSource.Card.VaultId` = payment-token id (section 2). Do **not** use `PaymentSource.Token` / `TokenType` (only `BillingAgreement`).

---

### 7. TRANSACTION SEARCH / reconciliation

| | |
|---|---|
| HTTP | `GET /v1/reporting/transactions` |
| Signature | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly (nullable, no default) | `transactionId`, `transactionType`, `transactionStatus`, `transactionAmount`, `transactionCurrency`, `paymentInstrumentType`, `storeId`, `terminalId` — pass **`null`** for an unfiltered range |
| Query wire | `start_date` ← `startDate`, `end_date` ← `endDate`, … `page_size` ← `pageSize`, `page` ← `page` |
| Returns | `PayPalServerSdk.Models.SearchResponse` |
| Error | **Case B** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — **not** a typed `{Op}Error` |
| Case B accessors | `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` |
| Pagination | **no** SDK auto-pager. Walk **all** pages: `page = 1 .. TotalPages` (same `startDate`/`endDate`/`pageSize`). Default `pageSize` 100. |
| Map | `operations/TransactionSearch.md` |

Pass app `from`/`to` (ISO-8601) as `startDate` / `endDate`. For the **whole** activity set in range, pass `balanceAffectingRecordsOnly: "N"` (default `"Y"` may drop non-balance rows — **UNVERIFIED**; `"N"` matches “whole range”). Keep `fields: "transaction_info"` (default) — that fills `TransactionDetails.TransactionInfo`.

Notes from map: executed transactions can take up to **three hours** to appear; history up to **three years**.

**`SearchResponse`** (`records-2-Pa-Ve.md`): `TransactionDetails`, `AccountNumber`, `StartDate`, `EndDate`, `LastRefreshedDatetime`, `Page`, `TotalItems`, `TotalPages`, `Links`.

**Line-up fields — `TransactionInformation`** (`records-2-Pa-Ve.md`):

| Field (wire) | Join / display |
|---|---|
| `TransactionId (transaction_id)` | PayPal transaction id |
| `PaypalReferenceId (paypal_reference_id)` + `PaypalReferenceIdType (paypal_reference_id_type)` | `PayPalReferenceIdType.Odr (ODR)` ≈ PayPal order id |
| `InvoiceId (invoice_id)` / `CustomField (custom_field)` | eShop order id (set on `PurchaseUnitRequest`) |
| `TransactionAmount (transaction_amount)` | `Money` |
| `FeeAmount (fee_amount)` | `Money` |
| `TransactionInitiationDate (transaction_initiation_date)` / `TransactionUpdatedDate (transaction_updated_date)` | times |
| `TransactionStatus (transaction_status)` | `string?` (not a StringEnum in the map) |
| `PaymentTrackingId (payment_tracking_id)` | extra correlate |
| `EndingBalance` / `AvailableBalance` | empty if optional filters set (map notes) |

---

### 8. Error types

**Core** (`sdk-map.md` error-handling model · `Core/Exceptions/SdkException.cs`):

| Type | Namespace | Members |
|---|---|---|
| `SdkException<TError>` | `PayPalServerSdk.Core.Exceptions` | `Error: TError` (required). Extends `Exception`. **No** `StatusCode` on the exception itself. |
| `ApiError` | `PayPalServerSdk.Core.ErrorResponse` | `TryGetRawError(out RawError): bool` — base of typed `…Error` classes in `PayPalServerSdk.Errors` |
| `RawError` | `PayPalServerSdk.Core.ErrorResponse` | `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` |

Catch **`SdkException<{Operation}Error>`** (Case A) or **`SdkException<RawError>`** (Case B — **only** `SearchTransactions` in this plan). Do not catch `SdkException<Error>` / `SdkException<Error1>` — those are payload types, not the thrown wrapper.

**Case A payload `Error`** (`records-1-Ac-Pa.md`) — Orders + Payments `TryGetError`:

| Field (wire) | Type |
|---|---|
| `Name (name)` | `string !req` |
| `Message (message)` | `string !req` |
| `DebugId (debug_id)` | `string !req` |
| `Details (details)` | `IReadOnlyList<ErrorDetails>?` |
| `Links (links)` | `IReadOnlyList<LinkDescription>?` |

**`ErrorDetails`**: `Field (field): string?`, `Value (value): string?`, `Location (location): string? = "body"`, `Issue (issue): string !req`, `Description (description): string?`, `Links`.

**Vault Case A payload `Error1`** (`TryGetError1`): same `Name`/`Message`/`DebugId`/`Details` but `Details` is `ErrorDetails1` and `Links` is `IReadOnlyList<ErrorLinkDescription>?` (`Rel` **optional** on `ErrorLinkDescription` — do not require `rel`).

HTTP status on Case A is **not** a separate property: it is implied by which `TryGet…` succeeded. Several statuses share **one** accessor (e.g. authorize `TryGetError` covers 400/401/403/404/422/500). Distinguish 401 vs 422 vs 409 by **`Error.Name` + `Details[].Issue`**, and by **which accessor** matched. For statuses not listed, `TryGetRawError` then `raw.StatusCode`.

| HTTP (from map accessors) | Typical handling |
|---|---|
| **401** | Auth failure — credentials / token (`TryGetError` or Case B `StatusCode`) |
| **403** | Not permitted |
| **404** | Unknown order / authorization / capture / token |
| **409** | Capture / void / refund **only** (in `TryGetError` lists) — idempotent replay or state conflict; GET and reconcile |
| **422** | Unprocessable (validation, expired auth, cannot reauth, cannot refund more than captured) — return `Details[].Issue` + `Description` to operator/shopper |
| **400** | Bad request |
| **500** | Orders: inside `TryGetError`. Payments: often `TryGetNoContent(out RawError)` — **no** JSON body |

**Case B `SearchTransactions`:** `catch (SdkException<RawError> ex) { var status = ex.Error.StatusCode; var body = ex.Error.ReadAsString(); }`. Optional `ReadAsJson<SearchError>()` (`records-2-Pa-Ve.md`: `Name`, `Message`, `DebugId`, `Details`, `TotalItems`, `MaximumItems`) — if deserialize fails, keep `ReadAsString()`.

Idempotency conflicts: **409** only on capture / void / refund accessors. Create/Authorize do not list 409 — retries must use the **same** `payPalRequestId` and persist ids from the first success so a second attempt does not create a second hold.

---

### Enums actually needed (`map/models/enums.md` — `PayPalServerSdk.Models.Enums`, `StringEnum<T>`, members **not** C# enums)

| Enum | Members to use (C# · wire) |
|---|---|
| `CheckoutPaymentIntent` | `Authorize (AUTHORIZE)`, `Capture (CAPTURE)` — **use Authorize** |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` | include `Visa (VISA)` (test card); others as returned |
| `CardType` | `Credit`, `Debit`, `Prepaid`, `Store`, `Unknown` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| `ParesStatus` | `Y`, `N`, `U`, `A`, `C` (challenge), `R`, `D`, `I` |
| `EnrollmentStatus` | `Y`, `N`, `U`, `B` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` |
| `TokenType` | **`BillingAgreement (BILLING_AGREEMENT)` only** — not for vault cards |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` — only if exchanging a setup token |
| `PaymentTokenStatus` | `Created`, `PayerActionRequired`, `Approved`, `Vaulted`, `Tokenized` |
| `CardVerificationStatus` | `Verified (VERIFIED)`, `Failed (FAILED)` |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `LinkHttpMethod` | `Get`, `Post`, `Put`, `Delete`, … |

Unions: **none** in this SDK (`map/models/unions.md`).

---

### Persist map (PayPal-owned state)

| After | Store |
|---|---|
| CreateOrder | PayPal `Order.Id`, `Status`, create `payPalRequestId` |
| AuthorizeOrder | authorization `Id`, `Status`, `Amount`, `ExpirationTime`, `CreateTime`; order `Status`; authorize `payPalRequestId` |
| Reauthorize | **replace** authorization `Id` + `ExpirationTime` + `Status` |
| Capture | capture `Id`, `Status`, `GrossAmount`, `PaypalFee`, `NetAmount` |
| Void | authorization `Status = Voided` |
| Refund | refund `Id`, `Status`, `Amount`, `TotalRefundedAmount` |
| Vault | payment-token `Id`, PayPal `Customer.Id`, last digits / brand / expiry |

---

## Trap notes

⚠ Step 1 (client / DI) — `PayPalServerSdkClient` takes an `HttpClient`; the DI helper constructs it via `IHttpClientFactory`. Handler/client lifetime mistakes show up as socket exhaustion or as options captured once at first resolve. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 1 (auth) — credentials are `Oauth2: OAuth2ClientCredentials` (`ClientId`/`ClientSecret` required init) on options, not a separate “PayPal auth” object; leaving `Oauth2` null or setting it after first use produces 401s that look like “PayPal is down”. **MUST load `dotnet-authentication`** before wiring secrets.

⚠ Step 1 (BaseUrl / retries / timeout) — custom `PayPal:BaseUrl` is `ServerOptions.Default.Sandbox.BaseUrl`, not a private `HttpClient.BaseAddress` that would miss the token URL; retry/timeout options on the SDK do **not** replace the `HttpClient` you register and do **not** bound a whole business operation (pay = create+authorize). A transport failure can retry a **POST** (duplicate hold/capture risk) even when you also send `payPalRequestId`. **MUST load `dotnet-configuration-resilience`** before setting `Retry`, `Timeout`, or `BaseUrl`.

⚠ Steps 3–11 (calls) — many parameters are nullable **without** C# defaults (`payPalRequestId`, `body`, SearchTransactions filters, …). Positional calls skip/mis-bind them. Named arguments; pass `null` to skip; cancellation token is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.Orders` / `Payments` / `Vault` / `TransactionSearch` call.

⚠ Steps 3–11 (models) — records are init-only; `!req` members must be in the object initializer; enums are `StringEnum<T>` (`CheckoutPaymentIntent.Authorize`, not a C# enum); unmodeled JSON is dropped. Nested envelopes (`PurchaseUnits[].Payments.Authorizations[]`, `SellerReceivableBreakdown`) are easy to skip. **MUST load `dotnet-models`** before building `OrderRequest` / `CardRequest` / reading captures.

⚠ Steps 3–11 (errors) — catch `SdkException<CreateOrderError>` (etc.), not the inner `Error` type; Vault uses `TryGetError1` not `TryGetError`; SearchTransactions is Case B `SdkException<RawError>`. `TryGetRawError` is not a catch-all on typed errors. **MUST load `dotnet-error-handling`** before any try/catch.

⚠ Step 8 (reconciliation pages) and Step 10 (vault list) — the SDK does not walk pages for you (`Pagination: none` / only `page`). Stopping after page 1 silently truncates the date range / saved cards. **MUST load `dotnet-configuration-resilience`** (pagination) before those loops.

⚠ Step 12 (tests) — the test seam is the `HttpClient` constructor argument, not internal controller types. **MUST load `dotnet-testing`** before stubbing PayPal.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — construct / DI-register `PayPalServerSdkClient` and `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `Oauth2` client-credentials wiring, 401/403 |
| `dotnet-configuration-resilience` | Step 1 retries/timeouts/BaseUrl; Steps 8 & 10 pagination loops |
| `dotnet-calling-endpoints` | Steps 3–11 — named args, must-pass-null params, `ct:` |
| `dotnet-models` | Steps 3–11 — request/response records, StringEnums, envelopes |
| `dotnet-error-handling` | All payment/vault/search boundaries (every integration writes one) |
| `dotnet-testing` | Tests around the HttpClient seam |

**`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

- **Environment GAP:** `ServerEnvironment` has only `Sandbox`. There is no Live/Production member in this SDK map/source. `PAYPAL_ENVIRONMENT` / `PayPal:Environment` can only select sandbox. Production is **not** in the map — do not invent a live URL unless `PayPal:BaseUrl` is set (that override still uses the Sandbox server node’s `BaseUrl` slot).
- **Vaulted cards vs `Token`:** `TokenType` has only `BillingAgreement`. Paying with a saved card is **`CardRequest.VaultId`**, not `PaymentSource.Token`. Not a GAP for this flow.
- **No remaining-refundable field** on `CapturedPayment` — compute from capture gross minus refunds / `TotalRefundedAmount`. Not missing an operation; missing a single field.
- **Reauth issue codes** for “no longer renewable” are not listed in the map — surface `Error.Name` / `Details[].Issue` / `Description` to operators (**UNVERIFIED** exact `Issue` strings). 30-day rule is from the ReauthorizePayment map notes.
- **3DS challenge URL rel** is not named on the map. Stop on `OrderStatus.PayerActionRequired` and/or `ParesStatus.C`. Do not build a browser approval round-trip.
- **`CardRequest.Expiry` format** is untyped string in the map (**UNVERIFIED**). On 422, return `Details` for `payment_source.card.expiry`.
- **`prefer=return=minimal` vs representation:** same return types; nested authorization/fee population on minimal is **UNVERIFIED**. Always pass `return=representation`; fall back to GET.
- **Vault geography:** client documentation states Payment Method Tokens v3 is available in the US only. If sandbox/vault calls 403 with a regional `Issue`, that is an environment/account blocker, not a missing SDK operation.
- **PCI:** map notes on `CardRequest` — sending PAN/CVV via API requires PCI SAQ D. Hosted fields are mentioned in that summary but **are not operations in this SDK**. Direct card is in-map; a hosted-fields SDK is a **GAP** if PCI forbids raw PAN.
- **`CreateSetupToken` / payer-approval vault** is in-map but out of scope for the no-browser requirement. Using it would re-introduce a browser path.
- **Subscriptions** controller is unused.
- **SearchTransactions** is the only Case B operation in this plan; do not copy Case A `TryGetError` onto it.
- Assumed one `purchase_unit` per eShop order; amount `Value` string equals the local total to the cent in `PayPal:Currency`.
- Assumed eShop persists PayPal ids locally so GET my-orders and refund remaining can work without listing all PayPal payments (no “list authorizations by invoice” operation in the map besides Transaction Search).
- `ListCustomerPaymentTokens` requires PayPal `customerId`. If create-token does not return `Customer.Id`, listing must use locally stored token ids + `GetPaymentToken` (both in-map).
