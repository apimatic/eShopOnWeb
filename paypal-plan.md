# eShopOnWeb × PayPalServerSdk — plan + contract sheet

Package: `AsadAli.Checkout.Sdk` (version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`. Additive PayPal money-collection + saved cards; does not replace catalog/basket/order flow.

## Scope & sequence

1. **Client + config** — construct `PayPalServerSdkClient` from `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, `PayPal:BaseUrl`. Sandbox host by default; when `PayPal:BaseUrl` is set, apply it verbatim to the Default server (token request **and** every API call).
2. **Authorize (hold)** — `Orders.CreateOrder` with `CheckoutPaymentIntent.Authorize` and `PaymentSource.Card` (raw PAN **or** `VaultId`). If `purchase_units[].payments.authorizations` is empty and status is not a 3DS STOP, `Orders.AuthorizeOrder`. Persist PayPal order id, authorization id, status, amount, `expiration_time`.
3. **3DS STOP** — if the SDK surfaces a shopper-browser challenge, halt. Do not design an approval round-trip.
4. **Fulfil = capture** — `Payments.CaptureAuthorizedPayment` on the persisted authorization id (`prefer: "return=representation"`). Persist capture id, status, captured amount, PayPal fee, net to merchant.
5. **Stale hold** — if `expiration_time` has passed or capture fails as expired, `Payments.ReauthorizePayment`; persist the **new** authorization id. If reauthorize fails, return an operator-actionable error (do not capture).
6. **Cancel before fulfil** — `Payments.VoidPayment` on the authorization id.
7. **Refund after fulfil** — `Payments.RefundCapturedPayment` (full: no `Amount`; partial: `Amount` ≤ remaining capturable). Caller-supplied idempotency key. Never refund past captured − already-refunded.
8. **Saved cards** — `Vault.CreatePaymentToken` (direct card, no browser) → list `Vault.ListCustomerPaymentTokens` → delete `Vault.DeletePaymentToken`. Later pay: step 2 with `CardRequest.VaultId`.
9. **Reconciliation** — `TransactionSearch.SearchTransactions` over the ISO-8601 from/to window, **every page**, chunked to the API’s 31-day max per call. Line up via `invoice_id` / `custom_id`.
10. **Idempotency** — every authorize/capture/refund/void/vault-create passes a stable caller `payPalRequestId` (wire header `PayPal-Request-Id`).

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

Enums are `PayPalServerSdk.Models.Enums` `StringEnum<T>` records (not C# enums): use the static members below or `Type.FromValue("wire")`. Records are `PayPalServerSdk.Models` with `init`-only setters; `required` members must be set in the object initializer.

### 0. Client construction / auth / custom base URL

| Item | Fact | Cite |
|---|---|---|
| Client | `PayPalServerSdk.PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` — only ctor | `sdk-map.md`, `PayPalServerSdkClient.cs` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| Controllers | `client.Orders` · `client.Payments` · `client.Vault` · `client.TransactionSearch` (`PayPalServerSdk.Api.*`) | `sdk-map.md` |
| Options members | `Environment: PayPalServerSdk.Servers.ServerEnvironment` · `Retry: PayPalServerSdk.Core.Configuration.RetryOptions` · `Logging: LoggingOptions` · `Server: PayPalServerSdk.ServerOptions` · `Oauth2: PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?` · `Oauth2TokenStrategy: PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `sdk-map.md` |
| Credentials | `OAuth2ClientCredentials` **required** `ClientId: string`, **required** `ClientSecret: string`, optional `Scope: string?` — namespace `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | `OAuth2ClientCredentials.cs` |
| Environment | **Only member:** `PayPalServerSdk.Servers.ServerEnvironment.Sandbox` (wire `"Sandbox"`). `Default()` returns `Sandbox`. There is **no** Live/Production member. | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Server options | `PayPalServerSdk.ServerOptions.Default` → `PayPalServerSdk.Servers.DefaultOptions.Sandbox` → `PayPalServerSdk.Servers.DefaultOptions.SandboxOptions.BaseUrl: string` default `"https://api-m.sandbox.paypal.com"` | `ServerOptions.cs`, `Servers/DefaultOptions.cs` |
| **Custom base URL (token + all API calls)** | Set `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl verbatim>`. Resolver is `UrlTemplate(Sandbox.BaseUrl, path, [])`. OAuth token is `server.Default("/v1/oauth2/token")` — **same Default server / same `BaseUrl`**, POST `grant_type=client_credentials` with HTTP Basic `ClientId:ClientSecret`. Every Orders/Payments/Vault/TransactionSearch path also uses `server.Default(...)`. | `Servers/DefaultOptions.cs`, `AuthSchemes.cs`, `OAuth2ClientCredentialsStrategy.cs`, `PayPalServerSdkClient.cs` |
| Config mapping | `PayPal:ClientId` → `Oauth2.ClientId`; `PayPal:ClientSecret` → `Oauth2.ClientSecret`; `PayPal:Environment` → only `Sandbox` is a valid `ServerEnvironment`; `PayPal:BaseUrl` optional override as above; `PayPal:Currency` → every `Money`/`AmountWithBreakdown` `CurrencyCode` | this sheet |

Exact override (sandbox slot is the only environment the client ever reads):

```csharp
options.Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox;
options.Oauth2 = new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials
{
    ClientId = clientId,
    ClientSecret = clientSecret,
};
if (!string.IsNullOrWhiteSpace(baseUrl))
    options.Server.Default.Sandbox.BaseUrl = baseUrl; // verbatim, no derivation
```

⚠ Step 1 (client registration) — HttpClient lifetime / DI vs `new` is not visible from the ctor. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (auth) — when credentials are applied relative to construction, and how secrets are loaded, is not on the options type. **MUST load `dotnet-authentication`** before setting `Oauth2`.

⚠ Step 1 (BaseUrl + retries) — `Environment` vs `Server` are not read at the same time; retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; whether a failed write can be re-sent is not the `PayPal-Request-Id` story. **MUST load `dotnet-configuration-resilience`** before registering the client or setting `BaseUrl`.

### Amount format (every hold / capture / refund)

| Field | C# | Wire | Rules | Cite |
|---|---|---|---|---|
| Currency | `CurrencyCode: string` **!req** | `currency_code` | Length 3; ISO-4217; take from `PayPal:Currency` | `records-1-Ac-Pa.md` `Money` / `AmountWithBreakdown`; `Models/Money.cs` |
| Amount | `Value: string` **!req** | `value` | Max 32; regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`. Integer currencies (e.g. JPY) send an integer string; fractional currencies send a decimal string. USD (and other 2-decimal codes): **two fraction digits** so the hold equals the eShop total to the cent, e.g. `"19.99"` not `"19.990"` / `"19.9"`. | `Models/Money.cs`, `Models/AmountWithBreakdown.cs` |

Hold amount = eShop order total, one `PurchaseUnitRequest` only (`CheckoutPaymentIntent.Authorize` “is not supported when you have more than one `purchase_unit`”).

### `prefer` (representation vs minimal)

Default on authorize/capture/reauthorize/void/refund is `prefer = "return=minimal"` → XML: *minimal includes id, status, HATEOAS links*. `return=representation` → *complete resource, including current state*. **Always pass `prefer: "return=representation"`** on authorize, capture, reauthorize, void, refund, and create-order so `payments.*`, `seller_receivable_breakdown`, amounts and fees are present. Wire header: `Prefer`. Cite: `Api/Orders.cs`, `Api/Payments.cs`.

### Idempotency

| Mechanism | Where | Fact | Cite |
|---|---|---|---|
| Caller key | C# param `payPalRequestId` → header **`PayPal-Request-Id`** | Present on CreateOrder, AuthorizeOrder, CaptureOrder, CaptureAuthorizedPayment, ReauthorizePayment, RefundCapturedPayment, VoidPayment, CreatePaymentToken, CreateSetupToken. **Not** on Get*/List*/DeletePaymentToken/SearchTransactions. | `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs` |
| Retention | Create/Authorize order: stored **6 hours** (up to 72h via account manager). Vault create: **3 hours**. Reauthorize: **45 days**. | XML on those params | same |
| Mandatory | `payPalRequestId` “is mandatory for all **single-step** create order calls” (payment source Card / vault_id / billing_agreement_id) | CreateOrder XML | `Api/Orders.cs` |
| SDK extra header | Every write also sends `Idempotency-Key: Guid.NewGuid()` **per invocation** | A double-click that omits a stable `payPalRequestId` will **not** be collapsed by this header | `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs` |

Authorize and capture **do** honor `PayPal-Request-Id` via `payPalRequestId`. Refunds **must** pass the caller-supplied key as `payPalRequestId`.

---

### Operations

#### A. `Orders.CreateOrder` — place + authorize (raw card **or** vaulted card)

- **HTTP:** `POST /v2/checkout/orders` · **Controller:** `PayPalServerSdk.Api.Orders` · `client.Orders`
- **Signature:** `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params (`payPalMockResponse` … `payPalAuthAssertion`) nullable, **no default → pass explicitly** (`null` to skip)
- **Returns:** `PayPalServerSdk.Models.Order` (the order **is** the response; no wrapper field)
- **Error:** Case A `PayPalServerSdk.Core.Exceptions.SdkException<PayPalServerSdk.Errors.CreateOrderError>` · `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out PayPalServerSdk.Core.ErrorResponse.RawError)` fallback
- **Pagination:** none
- Cite: `operations/Orders.md`

**Request `OrderRequest`** (`Models/OrderRequest.cs`, `records-1-Ac-Pa.md`):

| Member | Wire | Type | Req |
|---|---|---|---|
| `Intent` | `intent` | `CheckoutPaymentIntent` | **!req** → `CheckoutPaymentIntent.Authorize` (`AUTHORIZE`) |
| `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnitRequest>` | **!req** — exactly one unit |
| `PaymentSource` | `payment_source` | `PaymentSource?` | set for single-step card/vault |
| `Payer` | `payer` | `Payer?` | skip |
| `ApplicationContext` | `application_context` | `OrderApplicationContext?` | skip |

**`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`): `Amount (amount): AmountWithBreakdown` **!req**; persist-correlation: `CustomId (custom_id): string?`, `InvoiceId (invoice_id): string?` (put the eShop order id in **both**). Optional: `ReferenceId (reference_id)`, `Description (description)`.

**Raw card — `PaymentSource.Card` = `CardRequest`** (`records-1-Ac-Pa.md`, `Models/CardRequest.cs`):

| Member | Wire | Type | Notes |
|---|---|---|---|
| `Name` | `name` | `string?` | cardholder, 1–300 |
| `Number` | `number` | `string?` | PAN, regex `^[0-9]{13,19}$` — sandbox Visa `4111111111111111` |
| `Expiry` | `expiry` | `string?` | **ISO `YYYY-MM`**, length 7, regex `^[0-9]{4}-(0[1-9]|1[0-2])$` |
| `SecurityCode` | `security_code` | `string?` | CVC 3–4 digits; cannot be present when `payment_initiator=MERCHANT` |
| `BillingAddress` | `billing_address` | `Address?` | if set, `Address.CountryCode (country_code): string` **!req**; also `AddressLine1/2`, `AdminArea2` (city), `AdminArea1` (state), `PostalCode` |
| `VaultId` | `vault_id` | `string?` | **leave null** on raw-card path |
| `Attributes` | `attributes` | `CardAttributes?` | optional; `Verification.Method` default `OrdersCardVerificationMethod.ScaWhenRequired` |
| `ExperienceContext` | `experience_context` | `CardExperienceContext?` | `ReturnUrl`/`CancelUrl` exist for 3DS — **do not** implement the round-trip |
| `StoredCredential` | `stored_credential` | `CardStoredCredential?` | raw one-off: omit, or `PaymentInitiator.Customer` + `StoredPaymentSourcePaymentType.OneTime` + `Usage = First` |

**Vaulted card — same `CardRequest`, PAN fields omitted:**

| Member | Wire | Value |
|---|---|---|
| `VaultId` | `vault_id` | `PaymentTokenResponse.Id` from vault |
| `StoredCredential` | `stored_credential` | **!req on that nested type:** `PaymentInitiator (payment_initiator)` + `PaymentType (payment_type)`; shopper-present later order: `PaymentInitiator.Customer` + `StoredPaymentSourcePaymentType.OneTime` or `Unscheduled` + `Usage = StoredPaymentSourceUsageType.Subsequent` |
| `Number` / `SecurityCode` | — | omit (do not re-enter PAN) |

Do **not** use `PaymentSource.Token` for saved cards: `Token.Type` is only `TokenType.BillingAgreement`. Vault pay is `card.vault_id`.

**Response `Order` to persist / branch:**

| Member | Wire | Read |
|---|---|---|
| `Id` | `id` | PayPal order id |
| `Status` | `status` | `OrderStatus` — see enum table. **`PayerActionRequired` = 3DS STOP** |
| `Intent` | `intent` | confirm `Authorize` |
| `PurchaseUnits[].Payments` | `purchase_units[].payments` | `PaymentCollection` |
| `PurchaseUnits[].Payments.Authorizations[]` | `authorizations` | `AuthorizationWithAdditionalData`: `Id`, `Status`, `Amount`, `ExpirationTime`, `ProcessorResponse` |
| `PaymentSource.Card` | `payment_source.card` | `CardResponse`: `LastDigits`, `Brand`, `Expiry`, `AuthenticationResult` (3DS) |
| `Links[]` | `links` | `LinkDescription.Rel` / `Href` / `Method` — **`rel == "payer-action"` = 3DS STOP** |

⚠ Step 2 (call) — five leading nullable params have no C# default; a positional call mis-binds. **MUST load `dotnet-calling-endpoints`** before the first `CreateOrder`.

⚠ Step 2 (models) — `StringEnum<T>`, `required` members, and wire vs C# names are not C# `enum` / Pascal-only. **MUST load `dotnet-models`** before constructing `OrderRequest` / `CardRequest`.

#### B. `Orders.AuthorizeOrder` — authorize if create did not already hold funds

- **HTTP:** `POST /v2/checkout/orders/{id}/authorize`
- **Signature:** `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params (`payPalMockResponse` … `body`) nullable, **must pass explicitly**
- **Returns:** `PayPalServerSdk.Models.OrderAuthorizeResponse` (same fields as `Order`, payment source type `OrderAuthorizeResponsePaymentSource`)
- **Error:** Case A `SdkException<AuthorizeOrderError>` · `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError`
- **Body:** `OrderAuthorizeRequest.PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` with `Card: CardRequest?` (raw or `VaultId`)
- Cite: `operations/Orders.md`, `records-1-Ac-Pa.md`

Use when CreateOrder returned no `payments.authorizations` and status is not `PayerActionRequired`. Same `payPalRequestId` family as the pay request. Pass `prefer: "return=representation"`.

#### C. `Payments.CaptureAuthorizedPayment` — FULFIL

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/capture`
- **Signature:** `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 4 params (`payPalMockResponse` … `body`) nullable, **must pass explicitly**
- **Returns:** `PayPalServerSdk.Models.CapturedPayment` (payload **is** the capture; no wrapper)
- **Error:** Case A `SdkException<CaptureAuthorizedPaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`
- Cite: `operations/Payments.md`

**`CaptureRequest`:** `Amount (amount): Money?` (omit to capture the full hold), `FinalCapture (final_capture): bool? = false` → set `true` on fulfil, `InvoiceId`, `NoteToPayer`, `SoftDescriptor`.

**Read after capture** (`CapturedPayment`, `SellerReceivableBreakdown` — `records-1-Ac-Pa.md` / `records-2-Pa-Ve.md`):

| Need | Path (C# / wire) |
|---|---|
| Capture id | `Id (id)` |
| Status | `Status (status): CaptureStatus` — success = `Completed`; do not treat order-level `OrderStatus.Completed` as funds taken |
| Captured amount | `Amount (amount): Money` (`currency_code` + `value`) **and** `SellerReceivableBreakdown.GrossAmount (gross_amount): Money` **!req** when breakdown present |
| PayPal fee | `SellerReceivableBreakdown.PaypalFee (paypal_fee): Money?` |
| Net to merchant | `SellerReceivableBreakdown.NetAmount (net_amount): Money?` |
| Pending caveat | Breakdown “is not available for transactions that are in pending state” — if `Status == Pending`, persist ids and re-GET |

Supporting: `Payments.GetCapturedPayment(string captureId, string? payPalMockResponse, …)` returns the same `CapturedPayment`.

#### D. `Payments.ReauthorizePayment` — renew stale hold

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/reauthorize`
- **Signature:** `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId`, `payPalAuthAssertion`, `body` nullable, **must pass explicitly**
- **Returns:** `PayPalServerSdk.Models.PaymentAuthorization` — persist **new** `Id`, `Status`, `ExpirationTime`, `Amount` (replaces the stale hold id for later capture/void)
- **Error:** Case A `SdkException<ReauthorizePaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`
- **Body:** `ReauthorizeRequest.Amount (amount): Money?` only (“Supports only the `amount` request parameter”) — send the original hold `Money`
- Cite: `operations/Payments.md`, `Models/ReauthorizeRequest.cs`, `Api/Payments.cs`

**Stale detection (no `EXPIRED` status exists):** `PaymentAuthorization.ExpirationTime (expiration_time): string?` RFC-3339 with required seconds. `AuthorizationStatus` values are only Created / Captured / Denied / PartiallyCaptured / Voided / Pending.

**Honor-period remarks (XML):** 3-day honor period; reauthorize from day 4–29; after **30 days** “you must create an authorized payment instead of reauthorizing”. Reauthorized hold gets a new 3-day honor period.

**Cannot reauthorize vs success:**
- **Success:** returns `PaymentAuthorization` with `Status == AuthorizationStatus.Created` (or non-Denied) and a new `Id`.
- **Cannot:** throws `SdkException<ReauthorizePaymentError>`. Operator-actionable payload: `TryGetError` → `Error.Name`, `Error.Message`, `Error.DebugId`, `Error.Details[]` (`Issue` **!req**, `Description`, `Field`). Map those to the API error the operator sees. Do not invent `issue` code lists — none are on the map. 30-day expiry: do not retry reauthorize; return operator-actionable “create a new authorization”.

`Payments.GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, …)` → `PaymentAuthorization` for refresh.

#### E. `Payments.VoidPayment` — CANCEL / release hold

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/void`
- **Signature:** `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` nullable, **must pass explicitly**
- **Returns:** `PaymentAuthorization` — expect `Status == AuthorizationStatus.Voided`
- **Error:** Case A `SdkException<VoidPaymentError>` · `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError`
- **Notes:** “You cannot void an authorized payment that has been fully captured.”
- Cite: `operations/Payments.md`

#### F. `Payments.RefundCapturedPayment` — full / partial refund

- **HTTP:** `POST /v2/payments/captures/{capture_id}/refund`
- **Signature:** `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 4 params (`payPalMockResponse` … `body`) nullable, **must pass explicitly**
- **Returns:** `PayPalServerSdk.Models.Refund`
- **Error:** Case A `SdkException<RefundCapturedPaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError`
- Cite: `operations/Payments.md`, `records-2-Pa-Ve.md`

**`RefundRequest`:** full refund → `body: null` or `new RefundRequest()` with **no** `Amount` (“empty request body”). Partial → `Amount (amount): Money?` with `CurrencyCode` + `Value` ≤ remaining. Also `CustomId`, `InvoiceId`, `NoteToPayer`.

**Remaining refundable (application + PayPal):** remaining = captured `Amount` − sum of refund `Amount`s already persisted. Refuse when remaining ≤ 0 or `CaptureStatus` is `Refunded`. After refund, `Refund.SellerPayableBreakdown.TotalRefundedAmount (total_refunded_amount)` and capture status `PartiallyRefunded` / `Refunded`.

**Read:** `Refund.Id`, `Status (RefundStatus)`, `Amount`, `SellerPayableBreakdown.GrossAmount` / `PaypalFee` / `NetAmount` / `TotalRefundedAmount`.

Idempotency: **caller key → `payPalRequestId`**. Supporting: `Payments.GetRefund`.

#### G. `Vault.CreatePaymentToken` — save card (direct, no browser)

- **HTTP:** `POST /v3/vault/payment-tokens`
- **Signature:** `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId` nullable, **must pass explicitly**
- **Returns:** `PayPalServerSdk.Models.PaymentTokenResponse`
- **Error:** Case A `SdkException<CreatePaymentTokenError>` · `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError` — vault errors use **`Error1` / `TryGetError1`**, not `Error` / `TryGetError`
- Cite: `operations/Vault.md`

**`PaymentTokenRequest`:** `PaymentSource (payment_source): PaymentTokenRequestPaymentSource` **!req**; `Customer (customer): Customer?`.

**`Customer`:** `Id (id)` = PayPal-generated (omit on create); `MerchantCustomerId (merchant_customer_id)` = eShop shopper id (persist; used to list).

**`PaymentTokenRequestPaymentSource.Card` = `PaymentTokenRequestCard`:** `Name`, `Number`, `Expiry` (`YYYY-MM`), `SecurityCode`, `Brand`, `BillingAddress` (`CountryCode` !req if address set). No `vault_id` on this type.

**Response (never persist PAN):** `Id` = saved-card id (this is later `CardRequest.VaultId`); `Customer.Id` / `MerchantCustomerId`; `PaymentSource.Card` = `CardPaymentTokenEntity`: `Brand (brand): CardBrand?`, `LastDigits (last_digits): string?`, `Expiry (expiry): string?`, `Name`, `Type`, `VerificationStatus`, `AuthenticationResult`. `Links` — if any `Rel == "payer-action"`, **STOP**.

`PaymentTokenResponse` has **no** `Status` field. 3DS-on-vault status lives on **setup tokens** (below).

#### H. `Vault.CreateSetupToken` / `GetSetupToken` — 3DS-capable vault path (STOP if challenge)

- **CreateSetupToken** `POST /v3/vault/setup-tokens` · `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, …)` → `SetupTokenResponse`
- **Error:** `SdkException<CreateSetupTokenError>` · `TryGetError1(out Error1)` [400, 403, 422, 500]
- **`SetupTokenRequestCard`** adds `VerificationMethod: VaultCardVerificationMethod?` (`ScaWhenRequired` / `ScaAlways`) and `ExperienceContext: VaultCardExperienceContext?` (`ReturnUrl`, `CancelUrl`, `VaultInstruction`, `UserAction`)
- **`SetupTokenResponse.Status (status): PaymentTokenStatus`** default `Created` — **`PayerActionRequired` = STOP** (do not follow `ReturnUrl` / payer-action)
- Direct card vault **without** browser is **CreatePaymentToken**. Setup tokens are in the SDK but are the challenge-shaped path.
- Cite: `operations/Vault.md`, `records-2-Pa-Ve.md`

#### I. `Vault.GetPaymentToken` / `ListCustomerPaymentTokens` / `DeletePaymentToken`

| Op | HTTP | Signature | Returns | Error |
|---|---|---|---|---|
| GetPaymentToken | `GET /v3/vault/payment-tokens/{id}` | `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenResponse` | `GetPaymentTokenError` `TryGetError1` [403, 404, 422, 500] |
| ListCustomerPaymentTokens | `GET /v3/vault/payment-tokens` | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CustomerVaultPaymentTokensResponse` | `ListCustomerPaymentTokensError` `TryGetError1` [400, 403, 500] |
| DeletePaymentToken | `DELETE /v3/vault/payment-tokens/{id}` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `void` (`Task`) | `DeletePaymentTokenError` `TryGetError1` [400, 403, 500] |

Cite: `operations/Vault.md`. Query wires: `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired`.

**`customerId` XML:** “unique identifier representing a specific customer in **merchant's/partner's system**” — pass the same `MerchantCustomerId` used at vault create (`Api/Vault.cs`).

**List page loop (no SDK paginator):** pass `totalRequired: true`; read `TotalPages (total_pages)`, `TotalItems (total_items)`, `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`; increment `page` until all pages consumed. Default `pageSize` is 5.

After `DeletePaymentToken`, the id must not appear in list and must not be sent as `VaultId`.

#### J. `TransactionSearch.SearchTransactions` — GET reconciliation (all pages)

- **HTTP:** `GET /v1/reporting/transactions`
- **Signature:** `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 8 params (`transactionId` … `terminalId`) nullable, **must pass explicitly** (`null` to skip)
- **Returns:** `PayPalServerSdk.Models.SearchResponse` (no wrapper)
- **Error:** **Case B** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()`. **Only Case B operation in this SDK.**
- **Pagination:** none built-in (only `page` / `pageSize`). Loop `page` using `SearchResponse.TotalPages` / `TotalItems`.
- Cite: `operations/TransactionSearch.md`, `Api/TransactionSearch.cs`

**Date params (required):** RFC-3339 Internet date/time; **seconds required**; fractional seconds optional. `endDate` XML: **maximum supported range is 31 days**. Longer eShop from/to → consecutive ≤31-day windows until the whole range is covered.

**Lag:** “maximum of three hours for executed transactions to appear”; lists previous three years.

**Line-up fields** on `SearchResponse.TransactionDetails[].TransactionInfo` (`TransactionInformation`, `records-2-Pa-Ve.md`): `TransactionId (transaction_id)`, `TransactionInitiationDate`, `TransactionAmount`, `FeeAmount (fee_amount)`, `InvoiceId (invoice_id)`, `CustomField (custom_field)`, `TransactionStatus (transaction_status): string?` (filter codes, not an enum: `D` denied, `P` pending, `S` success, `V` reversed/refunded — XML on `transactionStatus`), `PaypalReferenceId`. Also `LastRefreshedDatetime` on `SearchResponse`.

Pass `fields: "all"` or keep default `transaction_info` (includes invoice id + fee). `transactionCurrency`: ISO-4217 from `PayPal:Currency`.

⚠ Step 9 (pagination / dates) — there is no enumerator; `page` semantics and the 31-day window are call-site concerns. **MUST load `dotnet-configuration-resilience`** before writing the recon loop.

---

### Status enums (success / pending / declined / voided / …)

No `EXPIRED` member on any of these. Stale hold = `expiration_time` and/or reauthorize/capture error.

| Enum | Namespace | Members `(C# / wire)` | Meaning for this integration |
|---|---|---|---|
| `CheckoutPaymentIntent` | `PayPalServerSdk.Models.Enums` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` | Always `Authorize` for Flow 1 |
| `OrderStatus` | same | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** | `PayerActionRequired` = **3DS STOP**. `Completed` = a payments resource exists — **still** read capture/authorization status before fulfilling (XML: completed can mean authorized, captured, **or declined**) |
| `AuthorizationStatus` | same | `Created (CREATED)` hold live; `Captured (CAPTURED)`; `Denied (DENIED)` declined; `PartiallyCaptured (PARTIALLY_CAPTURED)`; `Voided (VOIDED)` cancel success; `Pending (PENDING)` + `AuthorizationStatusDetails.Reason`: `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` | Capture only when `Created` (or `Pending` if product accepts). `Denied` / `Voided` = cannot capture |
| `CaptureStatus` | same | `Completed (COMPLETED)` success; `Declined (DECLINED)`; `PartiallyRefunded (PARTIALLY_REFUNDED)`; `Pending (PENDING)`; `Refunded (REFUNDED)` not further refundable; `Failed (FAILED)` | Fulfil success = `Completed`. Pending → breakdown may be absent |
| `RefundStatus` | same | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)` (+ `RefundIncompleteReason.Echeck`), `Completed (COMPLETED)` | Refund success = `Completed` |
| `PaymentTokenStatus` | same | `Created (CREATED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`**, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` | On **setup** token only. `PayerActionRequired` = STOP |
| `VaultStatus` | same | `Vaulted (VAULTED)`, `Created (CREATED)` deprecated, `Approved (APPROVED)` | On `CardVaultResponse` if present |
| `CardBrand` | same | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, … `Unknown (UNKNOWN)` | Safe display from vault/card response |
| `CardVerificationStatus` | same | `Verified (VERIFIED)`, `Failed (FAILED)` | Vault card |
| `CaptureIncompleteReason` | same | `BuyerComplaint`, `Chargeback`, `Echeck`, `InternationalWithdrawal`, `Other`, `PendingReview`, `ReceivingPreferenceMandatesManualAction`, `Refunded`, `TransactionApprovedAwaitingFunding`, `Unilateral`, `VerificationRequired`, `DeclinedByRiskFraudFilters` (wires in `enums.md`) | When capture `Pending`/`Denied` |
| `OrdersCardVerificationMethod` | same | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)` default, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` | Default can surface 3DS HATEOAS when regulations require it |
| `ParesStatus` | same | `Y` success; `N` failed/denied; `U` unable; `A` attempt; **`C` challenge required**; `R` rejected — merchant must not submit; **`D` challenge required (decoupled)**; `I` informational | **`C` / `D` = STOP** |
| `EnrollmentStatus` | same | `Y` bank in 3DS (ACSUrl); `N` not participating; `U` unavailable; `B` bypass | |
| `LiabilityShiftIndicator` | same | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` | On `AuthenticationResponse` |
| `PaymentInitiator` | same | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` | Shopper-present pay = `Customer` |
| `StoredPaymentSourcePaymentType` | same | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` | |
| `StoredPaymentSourceUsageType` | same | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` | Vaulted later order = `Subsequent` |
| `DisbursementMode` | same | `Instant (INSTANT)`, `Delayed (DELAYED)` | Capture default Instant |
| `LinkHttpMethod` | same | `Get`, `Post`, `Put`, `Delete`, … | On `LinkDescription.Method` |

Cite: `map/models/enums.md` and the enum `.cs` files named there.

### 3DS / payer-action / challenge — STOP (no approval round-trip)

Detect **any** of these after create/authorize/vault and **halt** (do not capture, do not treat as paid):

1. `Order.Status` / `OrderAuthorizeResponse.Status` == `OrderStatus.PayerActionRequired` (`PAYER_ACTION_REQUIRED`). XML: redirect payer to HATEOAS `"rel":"payer-action"` — **we will not**.
2. Any `Links` entry with `Rel == "payer-action"` (`LinkDescription.Href` would be the challenge URL — do not open it).
3. `CardResponse.AuthenticationResult` (`AuthenticationResponse`): `ThreeDSecure.AuthenticationStatus` in `{ ParesStatus.C, ParesStatus.D }` or `ParesStatus.R` (must not submit). Nested: `ThreeDSecureAuthenticationResponse.AuthenticationStatus (authentication_status)`, `EnrollmentStatus (enrollment_status)`.
4. Vault setup: `SetupTokenResponse.Status == PaymentTokenStatus.PayerActionRequired`.
5. `CardExperienceContext` / `VaultCardExperienceContext` `ReturnUrl`/`CancelUrl` exist only to complete that round-trip — unused here.

`CardVerification.Method` default `ScaWhenRequired` **will** return a contingency + HATEOAS link when local regulations require SCA. Sandbox card `4111111111111111` + future `expiry` + any CVC is the documented test PAN; whether a given sandbox call emits 3DS is **UNVERIFIED** (live traffic). Detection above is the contract.

### Error / exception boundary (every call)

- Throw-only: **no** `…Result` variants (`sdk-map.md`).
- Type: `PayPalServerSdk.Core.Exceptions.SdkException<TError>` with **only** `Error { get; init; }` — **no** `StatusCode` on the exception (`Core/Exceptions/SdkException.cs`).
- **Case A (all in-scope ops except SearchTransactions):** `TError` is `{Operation}Error : ApiError`. Accessors as in each row. Typed payload `Error` / `Error1`: `Name`, `Message`, `DebugId` all **!req**; `Details[].Issue` **!req**. `TryGetRawError` is the **fallback slot**, not a second view of the same typed body (`ApiError` stores one Optional). Case A accessors **group** multiple HTTP statuses (e.g. 400+401+403+404+422) onto one `TryGetError`.
- **Case B (`SearchTransactions`):** `ex.Error.StatusCode`, `ex.Error.ReadAsString()`.
- `PayPalServerSdk.Core.ErrorResponse.RawError`: `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`.

⚠ Step 10 (error boundary) — Case A vs Case B, grouped statuses, and `TryGetRawError` not being a catch-all are not visible from `catch (Exception)`. **MUST load `dotnet-error-handling`** before writing any try/catch.

⚠ Step 10 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## Trap notes

⚠ Step 1 (client registration) — HttpClient ownership and whether the SDK client wrapper is long-lived vs per-request is not on the constructor. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (auth) — credential application relative to construction, and loading `PayPal:ClientId` / `PayPal:ClientSecret` from config, is not on `OAuth2ClientCredentials`. **MUST load `dotnet-authentication`** before setting `Oauth2`.

⚠ Step 1 / 9 (BaseUrl, retries, pagination) — `Timeout` / retry options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; `Environment` vs `Server.Default.Sandbox.BaseUrl` are not interchangeable; list ops have no enumerator. **MUST load `dotnet-configuration-resilience`** before registering the client, setting `PayPal:BaseUrl`, or looping `SearchTransactions` / `ListCustomerPaymentTokens`.

⚠ Step 2–9 (calls) — nullable no-default parameters **must** be passed (`null` to skip); positional argument lists mis-bind. **MUST load `dotnet-calling-endpoints`** before the first operation call.

⚠ Step 2–8 (models) — request records, `StringEnum<T>`, `required` vs nullable, and JSON wire names vs C# names. **MUST load `dotnet-models`** before constructing payloads or reading envelopes.

⚠ Step 10 (errors) — Case A typed `{Op}Error` vs Case B `RawError`; `TryGetError` vs vault `TryGetError1`; grouped statuses; `SdkException<T>` has no status code. **MUST load `dotnet-error-handling`** before any try/catch.

⚠ Step 10 (errors) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (errors) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — faking the wrong layer (internals vs `HttpClient`). **MUST load `dotnet-testing`** before writing integration tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — `PayPalServerSdkClient` / `AddPayPalServerSdkClient` / `HttpClient` |
| `dotnet-authentication` | Step 1 — `Oauth2` / `OAuth2ClientCredentials` |
| `dotnet-configuration-resilience` | Step 1 BaseUrl + retries; Step 9 pagination / date windows |
| `dotnet-calling-endpoints` | Steps 2–9 — every operation call |
| `dotnet-models` | Steps 2–8 — request/response records and enums |
| `dotnet-error-handling` | Step 10 — exception boundary (always; includes both `JsonException` directions) |
| `dotnet-testing` | Tests against the `HttpClient` seam |

---

## Assumptions & Blockers

**Assumptions**
- eShop remains source of truth for catalog/basket/order; PayPal ids/status/amounts/fees are stored as additive payment state on the eShop order.
- Flow 1 uses **one** `purchase_unit` and `CheckoutPaymentIntent.Authorize` (not `Capture`).
- Shopper-present vaulted checkout uses `PaymentInitiator.Customer` (not merchant-initiated).
- `PayPal:Currency` is a 3-letter ISO-4217 code used as every `currency_code`.
- `ListCustomerPaymentTokens.customerId` is the merchant shopper id (`Customer.MerchantCustomerId`), per `Api/Vault.cs` XML.
- Reconciliation matches eShop orders via `invoice_id` / `custom_id` set to the eShop order identifier at authorize time.
- Reauthorize remarks describe “PayPal account payment” honor rules; the operation is still the SDK’s reauthorize for a persisted `authorization_id`. Whether a **card** authorization is accepted for reauthorize is **UNVERIFIED** (live traffic). On `SdkException<ReauthorizePaymentError>` / 30-day rule, return operator-actionable error rather than guessing.

**Blockers**
- None that omit an in-scope capability: authorize (raw + vault_id), capture (fees/net on `SellerReceivableBreakdown`), reauthorize, void, refund (+ `PayPal-Request-Id`), vault create/get/list/delete, transaction search with `page`/`total_pages` are all in the map.
- `ServerEnvironment` has **only** `Sandbox` — there is no Live member. Hitting live is **only** via `options.Server.Default.Sandbox.BaseUrl` override (e.g. `https://api-m.paypal.com`). Mapping `PayPal:Environment=Live` to a `ServerEnvironment` constant is **not possible**.
- 3DS/challenge **browser approval is out of product scope**. The SDK **does** surface it (`OrderStatus.PayerActionRequired`, `rel:payer-action`, `ParesStatus.C`/`D`, setup-token `PaymentTokenStatus.PayerActionRequired`). Treat as STOP; no round-trip is specified here.
- `SearchTransactions` enforces a **31-day** max per call (`Api/TransactionSearch.cs`). The whole from/to range is still in-SDK by chunking; a single call cannot cover a longer window.
- No `AuthorizationStatus.Expired` (or equivalent) in the enum list — expiry is `expiration_time` + error paths only.
