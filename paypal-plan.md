# PayPal payments + vaulted cards — eShopOnWeb contract sheet

NuGet `AsadAli.Checkout.Sdk` (install version-less) · root namespace `PayPalServerSdk` · map stamp `v1.0.1` / `9653d18`.

## Scope & sequence

1. **Client** — construct `PayPalServerSdk.PayPalServerSdkClient` with OAuth2 client-credentials, `ServerEnvironment.Sandbox`, optional verbatim `PayPal:BaseUrl` on the Default/Sandbox server (covers API **and** token).
2. **Place eShop order** (app-side, awaiting payment). Persist nothing PayPal-owned yet.
3. **AUTHORIZE (hold)** — `client.Orders.CreateOrder` with `CheckoutPaymentIntent.Authorize` + one `PurchaseUnit` whose amount **equals the eShop total to the cent**, then `client.Orders.AuthorizeOrder`. Payment source is either raw `CardRequest` (PAN/expiry/CVC/name/address) **or** `CardRequest.VaultId` of a saved token. Stop on 3DS / payer-action (no browser round-trip). Persist PayPal order id, authorization id, status, amount, expiration.
4. **FULFIL (capture)** — `client.Payments.CaptureAuthorizedPayment`. If the hold is stale, `client.Payments.ReauthorizePayment` first and persist the **new** authorization id. Persist capture id, status, captured amount, PayPal fee, net proceeds (`SellerReceivableBreakdown`). If fee/net missing (pending), `GetCapturedPayment`.
5. **CANCEL** — `client.Payments.VoidPayment` on the authorization id (before capture).
6. **REFUND** — `client.Payments.RefundCapturedPayment` (full: body `null`/empty; partial: `RefundRequest.Amount`). Caller-supplied idempotency via `payPalRequestId`. Never refund more than captured; persist refund ids and remaining.
7. **RECONCILE** — `client.TransactionSearch.SearchTransactions` over the ISO-8601 range, walking **every page** (and 31-day windows if the range is longer).
8. **VAULT** — `client.Vault.CreatePaymentToken` (save), `ListCustomerPaymentTokens` (list), `DeletePaymentToken` (delete). Never store PAN/CVC in the app DB. Persist PayPal payment-token id + PayPal customer id + last digits / brand / expiry.
9. **REFRESH** — `GetOrder` / `GetAuthorizedPayment` / `GetCapturedPayment` / `GetRefund` / `GetPaymentToken` by the ids stored above.

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

### 1. Client construction / builder / auth / servers

| Fact | Value | Cite |
|---|---|---|
| NuGet | `AsadAli.Checkout.Sdk` (version-less `dotnet add package AsadAli.Checkout.Sdk`) | `paypal-getting-started` / `sdk-map.md` |
| Root namespace | `PayPalServerSdk` | `sdk-map.md` |
| Client | `PayPalServerSdk.PayPalServerSdkClient` | `PayPalServerSdkClient.cs` |
| Ctor | `PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(this IServiceCollection, Action<PayPalServerSdkClientOptions>? configure = null)` — registers the client as singleton over `IHttpClientFactory` | `ServiceCollectionExtensions.cs` |
| Options | `PayPalServerSdk.PayPalServerSdkClientOptions` | `PayPalServerSdkClientOptions.cs` |

`PayPalServerSdkClientOptions` members (`PayPalServerSdkClientOptions.cs` / `sdk-map.md`):

| Property | Type | Namespace |
|---|---|---|
| `Environment` | `ServerEnvironment` | `PayPalServerSdk.Servers` |
| `Retry` | `RetryOptions` | `PayPalServerSdk.Core.Configuration` |
| `Logging` | `LoggingOptions` | (options type on client options; wire via companion skill) |
| `Server` | `ServerOptions` | `PayPalServerSdk` (root — file `ServerOptions.cs`) |
| `Oauth2` | `OAuth2ClientCredentials?` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `Oauth2TokenStrategy` | `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `PayPalServerSdk.Core.Authentication.OAuth2` |

**Environment.** `PayPalServerSdk.Servers.ServerEnvironment` is a `StringEnum<ServerEnvironment>`, **only member** `ServerEnvironment.Sandbox` (wire `"Sandbox"`). `ServerEnvironment.Default()` returns `Sandbox`. **There is no Live/Production member.** (`Servers/ServerEnvironment.cs`)

**Credentials.** Bind config `PayPal:ClientId` / `PayPal:ClientSecret` onto:

```
options.Oauth2 = new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials
{
    ClientId = ...,      // required string
    ClientSecret = ...,  // required string
    Scope = ...          // optional string?
};
```

(`OAuth2ClientCredentials.cs`)

**Base URL override (PayPal:BaseUrl) — ALL calls including the token request.**

- Single server node: `options.Server.Default` is `PayPalServerSdk.Servers.DefaultOptions`.
- Only environment nest: `Default.Sandbox` is `DefaultOptions.SandboxOptions`.
- Property: `SandboxOptions.BaseUrl` (`string`, default `"https://api-m.sandbox.paypal.com"`).
- **Set verbatim** when config `PayPal:BaseUrl` is present: `options.Server.Default.Sandbox.BaseUrl = configuredBaseUrl;`
- Token URL is `server.Default("/v1/oauth2/token")` — same Default/Sandbox `BaseUrl` as every Orders/Payments/Vault/TransactionSearch call. One override covers credential/token **and** API. (`AuthSchemes.cs`, `Servers/DefaultOptions.cs`, `ServerOptions.cs`)

**Retries / timeout.** `options.Retry` is `PayPalServerSdk.Core.Configuration.RetryOptions` (all members `required`; or `RetryOptions.Default()`). Members: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout` (`TimeSpan?`), `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. (`sdk-map.md`, `RetryOptions.cs`)

**HttpClient.** Caller owns it. Ctor takes `HttpClient`. DI path uses `IHttpClientFactory.CreateClient()`.

**Controllers on the client** (`PayPalServerSdkClient.cs`): `Orders`, `Payments`, `Vault`, `TransactionSearch` (plus `Subscriptions`, out of scope).

⚠ Step 1 (client registration) — `HttpClient` lifetime / factory vs per-request construction, and whether the SDK wrapper is long-lived. **MUST load `dotnet-client-initialization`** before writing the factory or `AddPayPalServerSdkClient`.

⚠ Step 1 (auth) — where credentials are set relative to construction, and loading secrets from config rather than hardcoding. **MUST load `dotnet-authentication`** before wiring `Oauth2`.

⚠ Step 1 (BaseUrl / retries / timeout) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; `Environment` vs live `Server` mutation; whether a failed write can be re-sent. **MUST load `dotnet-configuration-resilience`** before wiring `Retry` or `Server.Default.Sandbox.BaseUrl`.

---

### 2–3. Authorize a payment (raw card OR vaulted card) — hold, not capture

**Sequence (both payment sources):**

1. `client.Orders.CreateOrder` with `Intent = CheckoutPaymentIntent.Authorize` and exactly **one** `PurchaseUnit` (`CheckoutPaymentIntent.Authorize` is **not** supported with more than one purchase unit — `Models/Enums/CheckoutPaymentIntent.cs`).
2. Detect 3DS / payer-action on the create (and again on authorize) response — **STOP**, do not call authorize/capture, do not open a browser.
3. `client.Orders.AuthorizeOrder` to place the hold.
4. Read authorization id/status/amount from the authorize response (requires `prefer: "return=representation"` **or** a follow-up `GetOrder` / `GetAuthorizedPayment`).

Idempotency: pass the same caller key as `payPalRequestId` (header `PayPal-Request-Id`). CreateOrder XML: this header is **mandatory for single-step create order calls with payment source** (card / vault_id). Keys stored 6 hours (Orders) unless Account Manager extends. (`Api/Orders.cs`)

The SDK **also** sends `Idempotency-Key: Guid.NewGuid()` on every write. Merchant idempotency is **`payPalRequestId` → `PayPal-Request-Id`**, not that auto header. Whether the random `Idempotency-Key` interacts with PayPal-Request-Id replay is **UNVERIFIED** — still always pass `payPalRequestId`.

#### CreateOrder — `client.Orders.CreateOrder`

| | |
|---|---|
| HTTP | `POST /v2/checkout/orders` |
| Signature | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion` (pass `null` to skip) |
| Returns | `PayPalServerSdk.Models.Order` (not wrapped) |
| Error | **Case A** `SdkException<PayPalServerSdk.Errors.CreateOrderError>` |
| Accessors | `TryGetError(out PayPalServerSdk.Models.Error)` **[400, 401, 422]** · `TryGetRawError(out RawError)` fallback |
| Pagination | none |
| Cite | `operations/Orders.md`, `Api/Orders.cs` |

Headers: `PayPal-Mock-Response`, `PayPal-Request-Id`, `PayPal-Partner-Attribution-Id`, `PayPal-Client-Metadata-Id`, `Prefer`, `PayPal-Auth-Assertion`, plus auto `Idempotency-Key`.

**`prefer`:** default `"return=minimal"` returns only `id`, `status`, HATEOAS links. Pass **`prefer: "return=representation"`** to get `purchase_units`, `payment_source`, amounts. (`Api/Orders.cs` XML)

**Request `PayPalServerSdk.Models.OrderRequest`** (`records-1-Ac-Pa.md`, `Models/OrderRequest.cs`):

| Member (wire) | Type | Required? |
|---|---|---|
| `Intent (intent)` | `PayPalServerSdk.Models.Enums.CheckoutPaymentIntent` | **required** — use `CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`) |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | **required** — length 1 |
| `PaymentSource (payment_source)` | `PaymentSource?` | set for direct card / vaulted card |
| `Payer (payer)` | `Payer?` | do not use (deprecated) |
| `ApplicationContext (application_context)` | `OrderApplicationContext?` | omit (PayPal-wallet checkout UX; not used for direct card) |

**`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`, `Models/PurchaseUnitRequest.cs`):

| Member (wire) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `AmountWithBreakdown` **required** | must equal eShop order total |
| `CustomId (custom_id)` | `string?` max 255 | **set to eShop order id** — join key for reporting (`custom_field` / reports) |
| `InvoiceId (invoice_id)` | `string?` max 127 | unique per merchant by default; set to eShop order id / invoice |
| `ReferenceId (reference_id)` | `string?` | omit OK; PayPal sets `"default"` for a single unit |
| `Description (description)` | `string?` | optional |

**`AmountWithBreakdown` / `Money`** (`records-1-Ac-Pa.md`, `Models/Money.cs`, `Models/AmountWithBreakdown.cs`):

| Member (wire) | Type | Rules |
|---|---|---|
| `CurrencyCode (currency_code)` | `string` **required** | 3-char ISO-4217; take from `PayPal:Currency` (e.g. `"USD"`) |
| `Value (value)` | `string` **required** | **not decimal**. Regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`, max 32. Integer for JPY-like; fractional for others. For USD format invariant **2-decimal** string e.g. `"10.00"`. Must be positive. |

**`PaymentSource`** (`records-2-Pa-Ve.md`): set **only** `Card`. Do not set `Paypal` / wallets (that would be a redirect).

**Raw card — `PayPalServerSdk.Models.CardRequest`** (`records-1-Ac-Pa.md`, `Models/CardRequest.cs`):

| Member (wire) | Type | Format |
|---|---|---|
| `Name (name)` | `string?` | cardholder name, 1–300 |
| `Number (number)` | `string?` | PAN, 13–19 digits `^[0-9]{13,19}$`. Sandbox: `4111111111111111` |
| `Expiry (expiry)` | `string?` | **ISO-8601 `YYYY-MM`**, length 7, `^[0-9]{4}-(0[1-9]|1[0-2])$` — not separate month/year fields |
| `SecurityCode (security_code)` | `string?` | CVC, 3–4 digits. **Must not be present when `payment_initiator=MERCHANT`** |
| `BillingAddress (billing_address)` | `Address?` | see Address |
| `VaultId (vault_id)` | `string?` | **leave unset** for raw card |
| `ExperienceContext (experience_context)` | `CardExperienceContext?` | **leave unset** — `ReturnUrl`/`CancelUrl` exist to drive 3DS approval; we do not implement that round-trip |
| `StoredCredential (stored_credential)` | `CardStoredCredential?` | optional for first customer-initiated one-off |
| `Attributes (attributes)` | `CardAttributes?` | omit unless also vaulting-on-success |

**Vaulted card — same `CardRequest`, different fields:** set **`VaultId (vault_id)`** to `PaymentTokenResponse.Id` from vault. Do **not** send `Number` / `Expiry` / `SecurityCode`. Optional `StoredCredential`: `PaymentInitiator.Customer`, `StoredPaymentSourcePaymentType.OneTime` or `Unscheduled`, `StoredPaymentSourceUsageType.Subsequent`. (`CardRequest.cs` XML: vault_id is “the PayPal-generated ID for the vaulted payment source”.)

Do **not** use `PayPalServerSdk.Models.Token` for saved cards — `Token.Type` is only `TokenType.BillingAgreement`. Vault pay is `CardRequest.VaultId`.

**`Address`** (`records-1-Ac-Pa.md`): `AddressLine1 (address_line_1)`, `AddressLine2 (address_line_2)`, `AdminArea2 (admin_area_2)` city, `AdminArea1 (admin_area_1)` state, `PostalCode (postal_code)`, **`CountryCode (country_code): string` required** (2-char ISO-3166-1, `^([A-Z]{2}|C2)$`).

#### AuthorizeOrder — `client.Orders.AuthorizeOrder`

| | |
|---|---|
| HTTP | `POST /v2/checkout/orders/{id}/authorize` |
| Signature | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse` … `body` (5 params) |
| Returns | `PayPalServerSdk.Models.OrderAuthorizeResponse` |
| Error | **Case A** `SdkException<AuthorizeOrderError>` |
| Accessors | `TryGetError(out Error)` **[400, 401, 403, 404, 422, 500]** · `TryGetRawError` fallback |
| Cite | `operations/Orders.md` |

`OrderAuthorizeRequest`: only `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`. If the card/vault_id was already on CreateOrder, pass `body: null`. `OrderAuthorizeRequestPaymentSource.Card` is the same `CardRequest` shape.

Pass `prefer: "return=representation"`.

#### Response — where the hold lives

`Order` / `OrderAuthorizeResponse` (`records-1-Ac-Pa.md`):

| Member (wire) | Type | Persist / branch |
|---|---|---|
| `Id (id)` | `string?` | **PayPal order id** |
| `Status (status)` | `OrderStatus?` | see enum + 3DS stop |
| `Intent (intent)` | `CheckoutPaymentIntent?` | expect `Authorize` |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnit>?` | payments nest here |
| `PaymentSource (payment_source)` | `PaymentSourceResponse?` / `OrderAuthorizeResponsePaymentSource?` | card last digits + 3DS |
| `Links (links)` | `IReadOnlyList<LinkDescription>?` | 3DS `rel` |

**Authorization (the hold)** — `PurchaseUnits[0].Payments.Authorizations[0]` type `AuthorizationWithAdditionalData`:

| Member (wire) | Type | Persist |
|---|---|---|
| `Id (id)` | `string?` | **authorization id** (path param for capture/void/reauthorize) |
| `Status (status)` | `AuthorizationStatus?` | held vs denied |
| `Amount (amount)` | `Money?` | held amount — `Value`/`CurrencyCode` must match order total |
| `ExpirationTime (expiration_time)` | `string?` | RFC 3339 — stale/renew decision |
| `ProcessorResponse (processor_response)` | `ProcessorResponse?` | decline / insufficient funds / SCA |
| `CreateTime` / `UpdateTime` | `string?` | RFC 3339 |

`PurchaseUnit.Payments` is `PaymentCollection`: `Authorizations`, `Captures` (`OrdersCapture`), `Refunds`. (`records-2-Pa-Ve.md`)

**Held vs captured vs declined vs 3DS** — `OrderStatus` (`enums.md`, `Models/Enums/OrderStatus.cs`):

| Member | Wire | Meaning |
|---|---|---|
| `Created` | `CREATED` | order created |
| `Saved` | `SAVED` | persisted, not completed |
| `Approved` | `APPROVED` | payer approved |
| `Voided` | `VOIDED` | all units voided |
| `Completed` | `COMPLETED` | a payments resource exists — **still check** `purchase_units[].payments.captures[].status` / authorizations |
| `PayerActionRequired` | `PAYER_ACTION_REQUIRED` | **3DS / payer action — STOP** |

`AuthorizationStatus` (`enums.md`, `Models/Enums/AuthorizationStatus.cs`):

| Member | Wire | Meaning |
|---|---|---|
| `Created` | `CREATED` | **held** — no captures yet |
| `Captured` | `CAPTURED` | fully captured |
| `Denied` | `DENIED` | **declined** — cannot authorize funds |
| `PartiallyCaptured` | `PARTIALLY_CAPTURED` | partial capture |
| `Voided` | `VOIDED` | released |
| `Pending` | `PENDING` | see `StatusDetails.Reason` (`AuthorizationIncompleteReason`: `PendingReview`, `DeclinedByRiskFraudFilters`) |

There is **no** `EXPIRED` / `AUTHORIZED` member. “Held” = `Created`. Stale = `ExpirationTime` in the past (and/or capture 422).

#### How to DETECT 3DS / browser challenge — STOP

Check **all** of these after CreateOrder and AuthorizeOrder (and on vault save). If any fire: persist PayPal ids/status, surface an operator/shopper error, **do not** redirect, **do not** call authorize/capture.

1. **`Order.Status == OrderStatus.PayerActionRequired`** (wire `PAYER_ACTION_REQUIRED`). Source XML: *“The order requires an action from the payer (e.g. 3DS authentication). Redirect the payer to the `"rel":"payer-action"` HATEOAS link.”* We do **not** follow that link. (`Models/Enums/OrderStatus.cs`)
2. **`Order.Links` / `OrderAuthorizeResponse.Links`:** any `LinkDescription` with `Rel == "payer-action"` (`LinkDescription.Rel` is `string`, required). `Href` would be the challenge URL — do not open it.
3. **Card 3DS payload:** `PaymentSource.Card.AuthenticationResult` (`AuthenticationResponse`):
   - `ThreeDSecure (three_d_secure): ThreeDSecureAuthenticationResponse?`
     - `AuthenticationStatus (authentication_status): ParesStatus?` — **STOP if `ParesStatus.C` (wire `C`, “Challenge required for authentication”) or `ParesStatus.D` (wire `D`, “Challenge required; decoupled authentication confirmed”) or `ParesStatus.R` (rejected — do not submit).** `Y` = successful auth; `N` = failed; `U` = unable; `A` = attempts; `I` = informational.
     - `EnrollmentStatus (enrollment_status): EnrollmentStatus?` — `Y` = bank participates and *would* return ACSUrl (challenge infrastructure).
   - `LiabilityShift (liability_shift): LiabilityShiftIndicator?` — `No` / `Possible` / `Unknown` (not the stop flag; 3DS stop is status/`rel`/ParesStatus).
4. **Processor SCA:** `AuthorizationWithAdditionalData.ProcessorResponse.ResponseCode == ProcessorResponseCode._5650` (wire `5650`, XML “DECLINED_SCA_REQUIRED”).

Do not set `CardExperienceContext.ReturnUrl` / `CancelUrl`.

Sandbox card: Visa `4111111111111111`, any future `YYYY-MM`, any CVC, any name/address. If PayPal still returns (1)–(4), STOP.

⚠ Step 3 (CreateOrder / AuthorizeOrder) — many leading params have **no C# default** and mis-bind positionally; cancellation token is `ct`. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Step 3 (models) — enums are `StringEnum<T>` (compare to static members / `FromValue`), records are init-only with `required` members, unmodeled JSON is dropped. **MUST load `dotnet-models`** before building `OrderRequest` / `CardRequest`.

---

### 4. Capture an authorization (fulfil)

| | |
|---|---|
| Controller | `client.Payments` |
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/capture` |
| Signature | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse` … `body` |
| Returns | `PayPalServerSdk.Models.CapturedPayment` |
| Error | **Case A** `SdkException<CaptureAuthorizedPaymentError>` |
| Accessors | `TryGetError(out Error)` **[400, 401, 403, 404, 409, 422]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError` fallback |
| Idempotency | `payPalRequestId` → `PayPal-Request-Id` (stored **45 days**) |
| Cite | `operations/Payments.md`, `Api/Payments.cs` |

**`CaptureRequest`** (`records-1-Ac-Pa.md`): all optional. `Amount (amount): Money?` omit to capture the full hold (must equal remaining authorized). `FinalCapture (final_capture): bool? = false` — set `true` when this is the only/last capture. `InvoiceId`, `NoteToPayer`, `SoftDescriptor` optional. Pass `body: null` for a full final capture, or a body with `FinalCapture = true`.

Pass `prefer: "return=representation"`.

**Fee / net — on the capture resource (same type as GetCapturedPayment). No extra operation if representation is complete.**

`CapturedPayment.SellerReceivableBreakdown` (`SellerReceivableBreakdown`, `records-2-Pa-Ve.md`) — **not available when the capture is pending**:

| Member (wire) | Type | Meaning |
|---|---|---|
| `GrossAmount (gross_amount)` | `Money` **required** | **captured amount** |
| `PaypalFee (paypal_fee)` | `Money?` | **PayPal's fee** |
| `NetAmount (net_amount)` | `Money?` | **net proceeds to the merchant** |
| `ReceivableAmount (receivable_amount)` | `Money?` | receivable (FX cases) |
| `PaypalFeeInReceivableCurrency` | `Money?` | fee in receivable currency |
| `PlatformFees` | `IReadOnlyList<PlatformFee>?` | platform/partner fees |

Also persist `CapturedPayment.Id`, `Status`, `Amount` (gross captured), `CreateTime`.

If `SellerReceivableBreakdown` is null (`CaptureStatus.Pending`): call **`GetCapturedPayment`** (same `CapturedPayment` shape) until completed or failed.

**`CaptureStatus`** (`enums.md`): `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)`.

409 on capture: already captured / conflict. 422: unprocessable (including stale auth — try reauthorize).

---

### 5. Reauthorize / renew a stale authorization

| | |
|---|---|
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/reauthorize` |
| Signature | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalRequestId`, `payPalAuthAssertion`, `body` |
| Returns | `PayPalServerSdk.Models.PaymentAuthorization` |
| Error | **Case A** `SdkException<ReauthorizePaymentError>` |
| Accessors | `TryGetError(out Error)` **[400, 401, 403, 404, 422]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError` fallback |
| Cite | `operations/Payments.md`, `Api/Payments.cs`, `Models/ReauthorizeRequest.cs` |

**When it applies** (operation remarks): honor period **3 days**; reauthorize from **day 4 to 29**; a reauthorized payment gets a **new 3-day honor period**. After **30 days** from the **original** authorization you **must create a new authorized payment** (new CreateOrder+AuthorizeOrder), not reauthorize. `ReauthorizeRequest` supports **only** `Amount (amount): Money?`.

**Stale signals (no `EXPIRED` enum):**

- `PaymentAuthorization.ExpirationTime` / `AuthorizationWithAdditionalData.ExpirationTime` is in the past.
- Capture returns 422 (`TryGetError` → `Error.Details[].Issue` — exact issue string **UNVERIFIED** in the SDK; surface `Name` + `Issue` + `Description` + `DebugId` to the operator).

**New id:** persist `PaymentAuthorization.Id` from the reauthorize response — it **may change**. Subsequent capture/void uses the **new** id. Also persist new `ExpirationTime` and `Status`.

**Can no longer be renewed (operator-facing):**

- Catch `SdkException<ReauthorizePaymentError>`.
- `TryGetError(out Error)` on 400/401/403/404/422: read `Error.Name`, `Error.Message`, `Error.DebugId`, `Error.Details[].Issue` / `Field` / `Description`. 404 = authorization not found. 422 = cannot reauthorize (expired beyond window / invalid state).
- `TryGetNoContent(out RawError)` on 500: `RawError.StatusCode` + `ReadAsString()`.
- XML meaning to show operators: *if 30 days have transpired since the original authorization, create a new authorized payment instead of reauthorizing.* Fulfilment must fail **actionably** with `Name`/`Issue`/`DebugId`, not a generic 500.

There is **no** dedicated “expired authorization” enum value.

---

### 6. Void an authorization (cancel / release hold)

| | |
|---|---|
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/void` |
| Signature | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` |
| Returns | `PayPalServerSdk.Models.PaymentAuthorization` |
| Error | **Case A** `SdkException<VoidPaymentError>` |
| Accessors | `TryGetError(out Error)` **[401, 403, 404, 409, 422]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError` fallback |
| Idempotency | `payPalRequestId` → `PayPal-Request-Id` (45 days) |
| Cite | `operations/Payments.md` |

No body. Remarks: **cannot void a fully captured authorization.** After success, `PaymentAuthorization.Status == AuthorizationStatus.Voided` (wire `VOIDED`). 409 = already voided / conflict.

---

### 7. Refund a capture (full and partial)

| | |
|---|---|
| HTTP | `POST /v2/payments/captures/{capture_id}/refund` |
| Signature | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalMockResponse` … `body` |
| Returns | `PayPalServerSdk.Models.Refund` |
| Error | **Case A** `SdkException<RefundCapturedPaymentError>` |
| Accessors | `TryGetError(out Error)` **[400, 401, 403, 404, 409, 422]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError` fallback |
| Cite | `operations/Payments.md`, `Models/RefundRequest.cs` |

**Full refund:** `body: null` (empty payload). **Partial:** `RefundRequest` with `Amount (amount): Money` (`CurrencyCode` + `Value` string). Other fields optional: `CustomId`, `InvoiceId`, `NoteToPayer`.

**Caller idempotency:** `payPalRequestId` → `PayPal-Request-Id` (45 days). Same key + same capture ⇒ do not refund twice. **Distinct keys** for two legitimate partials. App must also refuse a refund whose amount exceeds (captured − already refunded); persist refund ids and running total. `CaptureStatus.PartiallyRefunded` / `Refunded` after refresh via `GetCapturedPayment`.

**Response `Refund`:** `Id (id)`, `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount`).

**`RefundStatus`:** `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)`.

**Over-refund / already refunded:** 409 or 422 via `TryGetError(out Error)` — read `Details[].Issue`. Exact issue strings **UNVERIFIED**. Refresh capture; if `CaptureStatus.Refunded`, refuse further refunds in the app.

---

### 8. Vault / save a card

**No separate “create customer” operation exists** in this SDK (Vault has 6 ops; none create a customer). Customer is created as a side-effect of `CreatePaymentToken` when you send `Customer.MerchantCustomerId` (app shopper id) and omit `Customer.Id`. Persist the returned PayPal `Customer.Id`.

| | |
|---|---|
| HTTP | `POST /v3/vault/payment-tokens` |
| Signature | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `payPalRequestId` |
| Returns | `PayPalServerSdk.Models.PaymentTokenResponse` |
| Error | **Case A** `SdkException<CreatePaymentTokenError>` |
| Accessors | `TryGetError1(out Error1)` **[400, 403, 404, 422, 500]** · `TryGetRawError` fallback |
| Idempotency | `payPalRequestId` → `PayPal-Request-Id` (stored **3 hours**) |
| Cite | `operations/Vault.md`, `records-2-Pa-Ve.md` |

**`PaymentTokenRequest`:** `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource` **required**.

**`Customer`:** `Id (id)` PayPal-generated (omit on first save); `MerchantCustomerId (merchant_customer_id)` **app user id** (1–64, `^[0-9a-zA-Z-_.^*$@#]+$`).

**`PaymentTokenRequestPaymentSource.Card`:** `PaymentTokenRequestCard` — `Name`, `Number` (PAN), `Expiry` (`YYYY-MM`), `SecurityCode`, `Brand?`, `BillingAddress?`. Same digit/expiry rules as `CardRequest`. Never persist these in the app DB.

**Response — safe display (never PAN):**

| Path | Wire | Type |
|---|---|---|
| `PaymentTokenResponse.Id` | `id` | vault / payment-token id — **store; this is `CardRequest.VaultId`** |
| `Customer.Id` | `customer.id` | **PayPal customer id** — store for list |
| `Customer.MerchantCustomerId` | `customer.merchant_customer_id` | echo of app user id |
| `PaymentSource.Card.LastDigits` | `payment_source.card.last_digits` | last 2–4 digits |
| `PaymentSource.Card.Brand` | `payment_source.card.brand` | `CardBrand` |
| `PaymentSource.Card.Expiry` | `payment_source.card.expiry` | `YYYY-MM` |
| `PaymentSource.Card.Name` | `payment_source.card.name` | optional |
| `PaymentSource.Card.VerificationStatus` | `verification_status` | `CardVerificationStatus` (`Verified`/`Failed`) |

`CardPaymentTokenEntity.AuthenticationResult` is `CardAuthenticationResponse.ThreeDSecure` (`ParesStatus` / `EnrollmentStatus`) — if `ParesStatus.C`/`D`, STOP (same 3DS policy).

Vault controller XML: Payment Method Tokens API v3 is *Available in the US only.* (`PayPalServerSdkClient.cs`)

`CreateSetupToken` is **not** required for this save-card path (it is the setup-token / payer-approval flow). Use `CreatePaymentToken` with raw card.

---

### 9. List saved cards

| | |
|---|---|
| HTTP | `GET /v3/vault/payment-tokens` |
| Signature | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Query | `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired` |
| Returns | `PayPalServerSdk.Models.CustomerVaultPaymentTokensResponse` |
| Error | **Case A** `SdkException<ListCustomerPaymentTokensError>` |
| Accessors | `TryGetError1(out Error1)` **[400, 403, 500]** · `TryGetRawError` fallback |
| Pagination | **page + pageSize** (no `perPage`). Default `pageSize=5`, `page=1`. Pass **`totalRequired: true`** so `TotalPages`/`TotalItems` are populated; loop `page = 1 .. TotalPages`. |
| Cite | `operations/Vault.md` |

Pass `customerId:` the PayPal-generated `CustomerResponse.Id` stored at vault-create time (query `customer_id`). XML on the method also says “identifier representing a specific customer in merchant's/partner's system” — persist **both** PayPal `Customer.Id` and `MerchantCustomerId`; list with the PayPal `Customer.Id` returned by create. **UNVERIFIED** whether `merchant_customer_id` is accepted as this query param.

**Item shape:** `PaymentTokens: IReadOnlyList<PaymentTokenResponse>?` — same safe fields as §8. `TotalItems` / `TotalPages` (response model documents `TotalItems` 1–50, `TotalPages` 1–10).

---

### 10. Delete / unvault a saved card

| | |
|---|---|
| HTTP | `DELETE /v3/vault/payment-tokens/{id}` |
| Signature | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `void` (`Task`) |
| Error | **Case A** `SdkException<DeletePaymentTokenError>` |
| Accessors | `TryGetError1(out Error1)` **[400, 403, 500]** · `TryGetRawError` fallback |
| Cite | `operations/Vault.md` |

`id` is `PaymentTokenResponse.Id`. No `payPalRequestId` on delete.

**“Gone”:**

- Subsequent `ListCustomerPaymentTokens` does not include that token.
- `GetPaymentToken(id)` → `SdkException<GetPaymentTokenError>` with `TryGetError1` on **404** (also 403/422/500).
- Pay-with-saved-card using that `VaultId` fails CreateOrder/AuthorizeOrder (`TryGetError` 422/404). Treat as not usable.

---

### 11. Transaction search / reconciliation

| | |
|---|---|
| HTTP | `GET /v1/reporting/transactions` |
| Signature | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `transactionId` … `terminalId` (8 nullables — pass `null`) |
| Returns | `PayPalServerSdk.Models.SearchResponse` |
| Error | **Case B** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — **the only Case B op in this SDK** |
| RawError | `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` |
| Cite | `operations/TransactionSearch.md`, `Api/TransactionSearch.cs` |

**from/to types:** **`string`, not `DateTime`.** XML: RFC 3339 / Internet date and time format; **seconds required**; fractional seconds optional. Example: `"2026-08-01T00:00:00Z"`. Regex on response dates is the same RFC 3339 pattern used on Order timestamps. **Maximum range 31 days** (`endDate` XML). If the eShop from/to span is longer, split into adjacent ≤31-day windows and concatenate. Timezone: send offset or `Z`; do not pass a zone-less local `DateTime`.

**Lag:** *maximum of three hours* for executed transactions to appear. Response `LastRefreshedDatetime (last_refreshed_datetime)` is the refresh watermark. Still walk the full requested range.

**Cover the whole range (paging):**

- `pageSize` default **100** (also the documented combination example).
- `page` default **1**. Walk `page = 1, 2, … TotalPages`.
- Read `SearchResponse.TotalPages` / `TotalItems` / `Page`.
- Also inspect `Links` (`LinkDescription.Rel` / `Href`) if present; still page with the `page` query rather than following hrefs only.
- Call with named arguments. Pass `fields: "all"` (XML: `fields=all` includes every block) so fee + join fields are present; default `"transaction_info"` is amount/fee/status only.

**Item shape `SearchResponse.TransactionDetails[]` → `TransactionDetails.TransactionInfo` (`TransactionInformation`):**

| Member (wire) | Type | Use |
|---|---|---|
| `TransactionId (transaction_id)` | `string?` | PayPal transaction id |
| `TransactionAmount (transaction_amount)` | `Money?` | amount + currency |
| `FeeAmount (fee_amount)` | `Money?` | fee |
| `TransactionStatus (transaction_status)` | `string?` | **not an enum** — XML filter codes: `D` denied, `P` pending, `S` success, `V` reversed/refunded |
| `TransactionInitiationDate` / `TransactionUpdatedDate` | `string?` | RFC 3339 timestamps |
| `InvoiceId (invoice_id)` | `string?` | join to eShop invoice / `PurchaseUnit.InvoiceId` |
| `CustomField (custom_field)` | `string?` | join to `PurchaseUnit.CustomId` |
| `PaypalReferenceId (paypal_reference_id)` | `string?` | join |
| `PaypalReferenceIdType` | `PayPalReferenceIdType?` | `Odr (ODR)` order id, `Txn (TXN)` transaction id, `Sub (SUB)`, `Pap (PAP)` |
| `PaymentTrackingId` | `string?` | additional join |
| `TransactionEventCode` | `string?` | event code |

`SearchBalances` exists (`GET /v1/reporting/balances`) but is **not required** for order-line reconciliation.

⚠ Step 7 (SearchTransactions) — 14 parameters, eight leading nullables with no default; page vs pageSize. **MUST load `dotnet-calling-endpoints`** (named arguments). Pagination walk / 31-day window. **MUST load `dotnet-configuration-resilience`**.

---

### 12. GET-by-id (refresh current status)

| Op | Signature (must-pass-explicitly in **bold**) | Returns | Error |
|---|---|---|---|
| `Orders.GetOrder` | `GetOrder(string id,` **`string? fields, string? payPalMockResponse, string? payPalAuthAssertion`** `, RequestOptions? = null, ct = default)` · `fields` XML: only valid filter is `"payment_source"` | `Order` | `GetOrderError` Case A: `TryGetError` **[401, 404]** |
| `Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId,` **`string? payPalMockResponse, string? payPalAuthAssertion`** `, …)` | `PaymentAuthorization` | `GetAuthorizedPaymentError`: `TryGetError` **[401, 403, 404]** · `TryGetNoContent` **[500]** |
| `Payments.GetCapturedPayment` | `GetCapturedPayment(string captureId,` **`string? payPalMockResponse`** `, …)` | `CapturedPayment` | `GetCapturedPaymentError`: `TryGetError` **[401, 403, 404]** · `TryGetNoContent` **[500]** |
| `Payments.GetRefund` | `GetRefund(string refundId,` **`string? payPalMockResponse, string? payPalAuthAssertion`** `, …)` | `Refund` | `GetRefundError`: `TryGetError` **[401, 403, 404]** · `TryGetNoContent` **[500]** |
| `Vault.GetPaymentToken` | `GetPaymentToken(string id, …)` | `PaymentTokenResponse` | `GetPaymentTokenError`: `TryGetError1` **[403, 404, 422, 500]** |

Cite: `operations/Orders.md`, `operations/Payments.md`, `operations/Vault.md`.

**Ids to store so a later request can act:**

| After | Store |
|---|---|
| CreateOrder | `Order.Id`, `Order.Status` |
| AuthorizeOrder | `Order.Id`; `PurchaseUnits[0].Payments.Authorizations[0].Id` (**authorization id**), `.Status`, `.Amount.Value`/`.CurrencyCode`, `.ExpirationTime` |
| ReauthorizePayment | **replace** authorization id with `PaymentAuthorization.Id`; new `ExpirationTime`/`Status` |
| CaptureAuthorizedPayment / GetCapturedPayment | capture `Id`, `Status`, `Amount`, `SellerReceivableBreakdown.GrossAmount` / `PaypalFee` / `NetAmount` |
| RefundCapturedPayment / GetRefund | refund `Id`, `Status`, `Amount`; running refunded total; capture status |
| CreatePaymentToken | token `Id`, PayPal `Customer.Id`, `MerchantCustomerId`, `LastDigits`, `Brand`, `Expiry` |
| eShop join | `PurchaseUnit.CustomId` + `InvoiceId` = eShop order id |

---

### 13. Errors

**Thrown type:** `PayPalServerSdk.Core.Exceptions.SdkException<TError>` — **only** public member `required TError Error { get; init; }`. **No HTTP status on `SdkException` itself.** No `…Result` variants (throw-only). (`sdk-map.md`, `SdkException.cs`)

Namespaces: `SdkException<>` → `PayPalServerSdk.Core.Exceptions`; `ApiError`/`RawError` → `PayPalServerSdk.Core.ErrorResponse`; `{Op}Error` → `PayPalServerSdk.Errors`; payload `Error`/`Error1` → `PayPalServerSdk.Models`.

**Case A (all in-scope ops except SearchTransactions):** catch `SdkException<{Operation}Error>`. Then:

- Payments/Orders typed payload: `ex.Error.TryGetError(out PayPalServerSdk.Models.Error e)` for the statuses listed per op.
- Vault typed payload: `ex.Error.TryGetError1(out PayPalServerSdk.Models.Error1 e)` — **different accessor name and type**.
- Payments 500: `TryGetNoContent(out RawError)` where listed.
- Else `TryGetRawError(out RawError)`.

**`Error` / `Error1` fields** (`records-1-Ac-Pa.md`): `Name (name): string` required, `Message (message): string` required, `DebugId (debug_id): string` required, `Details` list, `Links`. **No status-code property** — do not invent one. Branch on `Name` + `Details[].Issue`.

**`ErrorDetails` / `ErrorDetails1`:** `Field (field)`, `Value (value)`, `Location (location)` default `"body"`, **`Issue (issue): string` required** (fine-grained code), `Description (description)` (must not be depended on as stable), `Links`.

**Case B SearchTransactions:** `catch (SdkException<RawError> ex)` → `ex.Error.StatusCode`, `ReadAsString()`, or `ReadAsJson<PayPalServerSdk.Models.SearchError>()`. `SearchError` adds `InformationLink`, `TotalItems`, `MaximumItems` plus the same Name/Message/DebugId/Details.

**HTTP status is not on `Error`.** Infer from the accessor’s documented status set, or from `RawError.StatusCode` on fallback/no-content/Case B.

**Situation → what the SDK actually gives:**

| Situation | SDK signal (do not invent issue strings) |
|---|---|
| Declined card | 2xx with `AuthorizationStatus.Denied` / `CaptureStatus.Declined`; and/or `ProcessorResponse.ResponseCode == ProcessorResponseCode._5100` (XML **GENERIC_DECLINE**); and/or Case A 422 `TryGetError` → `Error.Details[].Issue` (**UNVERIFIED** exact issue text) |
| Insufficient funds | `ProcessorResponseCode._5120` (XML **INSUFFICIENT_FUNDS**); and/or `Error.Details[].Issue` **UNVERIFIED** |
| Expired / stale authorization | no status enum; `ExpirationTime`; capture/reauthorize **422** `TryGetError`; after 30 days reauthorize XML says create a **new** authorize |
| Already captured | `AuthorizationStatus.Captured`; capture **409** `TryGetError`; void remarks: cannot void fully captured |
| Already voided | `AuthorizationStatus.Voided`; void **409** |
| Already refunded | `CaptureStatus.Refunded`; refund **409/422** `TryGetError`; app remaining-balance check |
| Idempotent replay | `payPalRequestId`; whether PayPal returns the original 2xx body vs 409 is **UNVERIFIED**. Also `ProcessorResponseCode._5200` DUPLICATE_TRANSACTION on processor |
| 3DS required | §2 detection list; `ProcessorResponseCode._5650` DECLINED_SCA_REQUIRED |
| Resource not found | 404 `TryGetError` / `TryGetError1` |
| Duplicate request | 409 where listed; `Error.Name` + `Issue` **UNVERIFIED** |

Card processor codes live on **successful-looking** authorize/capture bodies (`ProcessorResponse`), not only on exceptions. Always read `ProcessorResponse.ResponseCode` on the authorization/capture.

Operator text: `Error.Message` + each `Details.Issue` + `Details.Description` + `DebugId`. Never parse `ex.ToString()` when an accessor exists.

⚠ Error boundary — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`**.

⚠ Error boundary — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`**.

---

### 14. Enums (in-scope)

All `PayPalServerSdk.Models.Enums.*`, `StringEnum<T>` — use static members, not C# enum casts. Cite `map/models/enums.md`.

| Enum | Members to branch on (C# = wire) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` — **use Authorize** |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, `Jcb (JCB)`, `Maestro (MAESTRO)`, `Diners (DINERS)`, `Unknown (UNKNOWN)`, … (full list in `enums.md`) |
| `ParesStatus` | `Y`, `N`, `U`, `A`, **`C` challenge**, `R` rejected, **`D` decoupled challenge**, `I` |
| `EnrollmentStatus` | `Y`, `N`, `U`, `B` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `ProcessorResponseCode` (selected) | `_0000 (0000)` APPROVED; `_5100 (5100)` GENERIC_DECLINE; `_5110` CVV2_FAILURE; `_5120` INSUFFICIENT_FUNDS; `_5400` EXPIRED_CARD; `_5650` DECLINED_SCA_REQUIRED; `_5200` DUPLICATE_TRANSACTION |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `CardVerificationStatus` | `Verified (VERIFIED)`, `Failed (FAILED)` |
| `ServerEnvironment` | **`Sandbox` only** (wire `"Sandbox"`) — `PayPalServerSdk.Servers` |

---

### 15. Amount representation

| Rule | Detail | Cite |
|---|---|---|
| Type | `string` on `Money.Value` and `AmountWithBreakdown.Value` — **never** `decimal` on the wire | `Models/Money.cs` |
| Currency field | `CurrencyCode (currency_code)` 3-char ISO-4217 | same |
| Pattern | `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`, max length 32 | same |
| Precision | Integer for non-fractional currencies (e.g. JPY); fractional for others (USD/TND per PayPal currency-codes table). For USD emit **exactly 2 decimal places**, invariant (`"10.00"` not `"10"` / current-culture commas). | `Money` / `AmountWithBreakdown` XML |
| Sign / positivity | Amount on the purchase unit **must be a positive number** | `AmountWithBreakdown` XML |
| Equality | Hold/capture `Value` must equal the eShop total **to the cent** for USD (same string after formatting) | requirement + Money format |

Format in the app with invariant culture to 2 places for USD, then assign the string.

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership/lifetime vs per-request construction; DI `AddPayPalServerSdkClient` vs manual ctor. **MUST load `dotnet-client-initialization`**.

⚠ Step 1 (auth) — `Oauth2` credentials shape, when they must be set, config vs hardcoded secrets, 401/token refresh. **MUST load `dotnet-authentication`**.

⚠ Step 1 (BaseUrl / retries / timeout) — retry/timeout options do **not** bound a whole call and are **not** the `HttpClient` timeout; whether a failed write can be re-sent; `Environment` vs mutating `Server` after construct. **MUST load `dotnet-configuration-resilience`**.

⚠ Step 3+ (every call) — leading nullable params have **no C# default**; named arguments; `ct:` not `cancellationToken:`; `prefer` default hides purchase_units/fees. **MUST load `dotnet-calling-endpoints`**.

⚠ Step 3 (models) — `StringEnum<T>`, `required` init records, `YYYY-MM` expiry, amount-as-string. **MUST load `dotnet-models`**.

⚠ Error boundary — malformed **2xx** missing `required` member → `JsonException`, not `SdkException`. **MUST load `dotnet-error-handling`**.

⚠ Error boundary — **non-2xx** body that fails `{Operation}Error` deserialize → `JsonException` **replaces** `SdkException` and destroys HTTP status; mapping all `JsonException` to 5xx mis-classifies deterministic rejects and causes poison retries. **MUST load `dotnet-error-handling`**.

⚠ Tests — `HttpClient` ctor is the test seam; match eShop test framework. **MUST load `dotnet-testing`**.

⚠ Reconcile paging — `page`/`pageSize`/`TotalPages` plus 31-day window; search lag. **MUST load `dotnet-configuration-resilience`**.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — `PayPalServerSdkClient` ctor, `HttpClient`, `AddPayPalServerSdkClient` |
| `dotnet-authentication` | Step 1 — `Oauth2` / `OAuth2ClientCredentials` |
| `dotnet-configuration-resilience` | Step 1 BaseUrl/retries/timeout; Step 7 pagination |
| `dotnet-calling-endpoints` | Steps 3–12 — every SDK call, named args, `ct`, envelopes |
| `dotnet-models` | Steps 3–10 — records, enums, amounts, vault/card fields |
| `dotnet-error-handling` | Error boundary for every operation (Case A vs B, accessors, JsonException) |
| `dotnet-testing` | Tests around the `HttpClient` seam |

Mandatory JsonException hazards (also in Trap notes):

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

**Assumptions**

- eShop places the shop order first; PayPal `CreateOrder`+`AuthorizeOrder` is the hold, not eShop’s own “order id” reused as PayPal’s id.
- Currency comes from `PayPal:Currency` and is applied as `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode` on every amount.
- Direct card + vaulted card share the same authorize path; only `CardRequest` fields differ (`Number`/`Expiry`/`SecurityCode` vs `VaultId`).
- One `purchase_unit` per PayPal order (`AUTHORIZE` forbids more).
- `prefer: "return=representation"` on create/authorize/capture/refund/void/reauthorize so ids and `SellerReceivableBreakdown` are present without an extra GET (GET remains the refresh path).
- List vault tokens with the PayPal-generated `Customer.Id` returned by `CreatePaymentToken`.
- Reconciliation joins eShop orders via `PurchaseUnit.CustomId`/`InvoiceId` ↔ `TransactionInformation.CustomField`/`InvoiceId` (and `PaypalReferenceId` when type `Odr`).
- `PayPal:Environment` in development is sandbox; `ServerEnvironment` only has `Sandbox`.
- Vault API geographic availability is US-only per client XML; sandbox testing still uses these operations.

**Blockers / GAPs**

- **No Live environment in this SDK.** `ServerEnvironment` has only `Sandbox`. Selecting live via `options.Environment` is impossible. Do not invent a Live member or an HTTP fallback. If production must target `api-m.paypal.com` as a distinct environment, that capability is **not in the map**.
- **PayPal `Error.Details.Issue` strings are not enumerated** (expired-auth, already-captured, already-voided, already-refunded, idempotent replay, 3DS, RESOURCE_NOT_FOUND). The integration must branch on HTTP accessor + `Error.Name` + `Issue` **as returned**, and on `ProcessorResponseCode` / status enums above. Exact issue literals are **UNVERIFIED**.
- **Whether the SDK’s auto `Idempotency-Key: Guid.NewGuid()` on every write defeats `PayPal-Request-Id` replay** is **UNVERIFIED**. Caller must still pass `payPalRequestId`. There is no SDK option to suppress `Idempotency-Key`.
- **Transaction Search `customer_id` vs merchant id for vault list** — method XML vs `Customer.Id` docs disagree slightly; listed assumption above. If list returns empty for a known vault, that is a live-API question, not an HTTP fallback.
- **No create-customer operation** — not a blocker; customer is created by vault-create. Do not add a raw HTTP customer API.
- Fee/net on **pending** captures are absent on the model (`SellerReceivableBreakdown` XML). Follow up with `GetCapturedPayment`; do not scrape another resource.
