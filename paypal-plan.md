# PayPal .NET SDK — eShopOnWeb contract sheet

Package: `AsadAli.Checkout.Sdk` (install version-less). Root namespace: `PayPalServerSdk`. Target: sandbox. Direct card + vault — no PayPal wallet redirect.

---

## Scope & sequence

| Step | eShop endpoint | SDK operations | Persist |
|---|---|---|---|
| 0 | App startup | Construct `PayPalServerSdkClient` from `PayPal:` config | — |
| 1 | `POST /api/orders/{orderId}/pay` | `Orders.CreateOrder` (intent AUTHORIZE + card or `vault_id`). Fallback `Orders.AuthorizeOrder` only if create returned no authorization. Refuse 3DS. | PayPal order id, authorization id, authorization status, amount |
| 2 | `POST /api/orders/{orderId}/fulfil` | If honor period expired and still inside 29-day window: `Payments.ReauthorizePayment`, then persist the **new** authorization id. Then `Payments.CaptureAuthorizedPayment` (`final_capture: true`). Read fee/net from capture (or `Payments.GetCapturedPayment`). | Capture id, capture status, captured amount, paypal fee, net amount; replace auth id if reauthorized |
| 3 | `POST /api/orders/{orderId}/cancel` (before fulfilment) | `Payments.VoidPayment` | Authorization status VOIDED |
| 4 | `POST /api/orders/{orderId}/refunds` | `Payments.RefundCapturedPayment` (omit amount = full; set amount = partial). Caller idempotency key → `payPalRequestId`. | Refund id, refund status, refunded amount; remaining refundable |
| 5 | `GET /api/reconciliation?from=&to=` | `TransactionSearch.SearchTransactions` — page until exhausted; split ranges > 31 days | — |
| 6 | `POST /api/payment-methods` | `Vault.CreatePaymentToken` | PayPal payment-token id, PayPal `customer.id`, `merchant_customer_id` (our buyer id). Never PAN/CVC. |
| 7 | List saved cards | `Vault.ListCustomerPaymentTokens` (page until exhausted) | — |
| 8 | Delete saved card | `Vault.DeletePaymentToken` | Drop local mapping; token must not be used to pay |
| — | Supporting reads | `Orders.GetOrder`, `Payments.GetAuthorizedPayment`, `Payments.GetCapturedPayment`, `Payments.GetRefund`, `Vault.GetPaymentToken` | Refresh status |

Do **not** use `Orders.CaptureOrder` (that path is for intent CAPTURE). Do **not** use `PaymentSource.Paypal` / wallet approve links. Do **not** use `Token` (`TokenType` is only `BILLING_AGREEMENT`) to pay with a vaulted card — use `CardRequest.VaultId`.

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

No-throw `…Result` variants: **absent** across this SDK. Every call throws. (`sdk-map.md`)

---

### Client construction, auth, servers

| Item | Fact | Cite |
|---|---|---|
| Client | `PayPalServerSdk.PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` — singleton client via `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Options type | `PayPalServerSdk.PayPalServerSdkClientOptions` | `sdk-map.md` |
| `Environment` | `PayPalServerSdk.Servers.ServerEnvironment` — **only member: `ServerEnvironment.Sandbox` (wire `"Sandbox"`)**. `Default()` returns Sandbox. **No Live member exists.** | `Servers/ServerEnvironment.cs` |
| `Oauth2` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?` | `sdk-map.md` |
| Credentials record | `required string ClientId`, `required string ClientSecret`, `string? Scope` | `OAuth2ClientCredentials` |
| `Oauth2TokenStrategy` | `PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>?` — leave unset; client default posts to `server.Default("/v1/oauth2/token")` (same base URL as every API call) | `AuthSchemes.cs` |
| `Server` | `PayPalServerSdk.ServerOptions` (root namespace) | `PayPalServerSdkClientOptions.cs` |
| Base URL override | `options.Server.Default.Sandbox.BaseUrl` (`PayPalServerSdk.Servers.DefaultOptions.SandboxOptions.BaseUrl`, default `"https://api-m.sandbox.paypal.com"`). **When `PayPal:BaseUrl` is set, assign it verbatim here.** Token request and every controller call resolve through `Server.Default(path)` using this value. | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Server.cs`, `AuthSchemes.cs` |
| `Retry` | `PayPalServerSdk.Core.Configuration.RetryOptions` — all members `required`; start from `RetryOptions.Default()` | `sdk-map.md` |
| Config mapping | `PayPal:ClientId` → `Oauth2.ClientId`; `PayPal:ClientSecret` → `Oauth2.ClientSecret`; `PayPal:Environment` → must be sandbox (`ServerEnvironment.Sandbox`); `PayPal:Currency` is **not** a client option — it is `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode` on every amount; `PayPal:BaseUrl` → `Server.Default.Sandbox.BaseUrl` when set | this sheet |
| Controllers | `client.Orders` → `PayPalServerSdk.Api.Orders`; `client.Payments` → `PayPalServerSdk.Api.Payments`; `client.Vault` → `PayPalServerSdk.Api.Vault`; `client.TransactionSearch` → `PayPalServerSdk.Api.TransactionSearch` | `sdk-map.md` |

⚠ Step 0 (client registration) — `HttpClient` lifetime vs the SDK wrapper, and DI vs `new`, are not visible from the constructor. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 0 (auth) — credentials must be set before construct / in the DI callback; load from config, never hard-code. **MUST load `dotnet-authentication`**.

⚠ Step 0 (base URL / retries / timeout) — `Retry`/`Timeout` on options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; a transport failure can re-execute a write. **MUST load `dotnet-configuration-resilience`** before wiring the client.

---

### Idempotency (all writes)

| Caller input (C# param) | HTTP header | Retention (XML) | Operations |
|---|---|---|---|
| `payPalRequestId` | `PayPal-Request-Id` | Orders: 6 hours (mandatory for single-step create with payment source / `vault_id`). Payments capture/void/reauthorize/refund: 45 days. Vault create: 3 hours. | CreateOrder, AuthorizeOrder, CaptureOrder, CaptureAuthorizedPayment, ReauthorizePayment, RefundCapturedPayment, VoidPayment, CreatePaymentToken |

Cite: `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`.

**Additional header the caller cannot set:** every write also sends `Idempotency-Key` with `Guid.NewGuid()` (new value every invocation). App-controlled idempotency is **only** `payPalRequestId` → `PayPal-Request-Id`. Whether PayPal dedupes on `PayPal-Request-Id` vs `Idempotency-Key` is **UNVERIFIED**. Defensive: (1) pass a stable `payPalRequestId` per logical action (order-pay, order-fulfil, each distinct refund key); (2) persist PayPal ids **before** treating success as unknown; (3) reject a second pay/capture in application state if an authorization/capture id already exists.

`prefer` header (C# param `prefer`, default `"return=minimal"`): minimal returns only id, status, HATEOAS links. **Pass `prefer: "return=representation"`** on CreateOrder / AuthorizeOrder / CaptureAuthorizedPayment / RefundCapturedPayment / VoidPayment / ReauthorizePayment so purchase-units, authorizations, captures, and `seller_receivable_breakdown` are present. Cite: `Api/Orders.cs`, `Api/Payments.cs`.

---

### Intent — hold funds (AUTHORIZE), do not capture at pay

`PayPalServerSdk.Models.OrderRequest.Intent` (`intent`): `PayPalServerSdk.Models.Enums.CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`). **Not** `CheckoutPaymentIntent.Capture` (`CAPTURE`). There is no separate “processing strategy” field on `OrderRequest`. (`records-1-Ac-Pa.md`, `enums.md`)

---

### 3DS / payer-action — detect and STOP

| Signal | How to read | Action |
|---|---|---|
| Order status | `Order.Status == PayPalServerSdk.Models.Enums.OrderStatus.PayerActionRequired` (wire `PAYER_ACTION_REQUIRED`) | **STOP.** Do not authorize/capture. Do not redirect. Report to the caller that a browser challenge is required and this integration refuses that path. |
| HATEOAS | `Order.Links` where `LinkDescription.Rel == "payer-action"` (`Rel` is `string`, not an enum). XML: *Redirect the payer to the `"rel":"payer-action"` HATEOAS link … (e.g. 3DS authentication).* | Same — treat presence as 3DS/payer-action. Do not follow the link. |
| Optional 3DS fields (do not build a round-trip) | `CardRequest.ExperienceContext` (`CardExperienceContext.ReturnUrl` / `CancelUrl`); `CardAttributes.Verification.Method` default `OrdersCardVerificationMethod.ScaWhenRequired` (wire `SCA_WHEN_REQUIRED`); response `PaymentSourceResponse.Card.AuthenticationResult` (`AuthenticationResponse.LiabilityShift`, `ThreeDSecure.AuthenticationStatus` / `EnrollmentStatus`) | If status is `PAYER_ACTION_REQUIRED`, refuse. Do not set return/cancel URLs to implement a challenge. |

Cite: `Models/Enums/OrderStatus.cs` XML, `records-1-Ac-Pa.md` (`CardExperienceContext`, `AuthenticationResponse`, `ThreeDSecureAuthenticationResponse`), `enums.md` (`ParesStatus`, `EnrollmentStatus`, `LiabilityShiftIndicator`).

`PaymentTokenResponse` has **no `Status` field** (`Id`, `Customer`, `PaymentSource`, `Links` only — `records-2-Pa-Ve.md`). Do **not** check `PaymentTokenStatus` on vault create; 3DS refuse applies to `Order` / `OrderAuthorizeResponse` only. The `PaymentTokenStatus` enum exists but is not a member of this response.

---

### Operation: CreateOrder — pay (authorize hold)

| | |
|---|---|
| Controller | `PayPalServerSdk.Api.Orders` · `client.Orders.CreateOrder` |
| HTTP | `POST /v2/checkout/orders` |
| Signature | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 5 nullable no-default params (`payPalMockResponse` … `payPalAuthAssertion`) — pass `null` to skip. `body` is required (`OrderRequest`). |
| Returns | `PayPalServerSdk.Models.Order` (not wrapped) |
| Error | **Case A** `PayPalServerSdk.Core.Exceptions.SdkException<PayPalServerSdk.Errors.CreateOrderError>` · `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out PayPalServerSdk.Core.ErrorResponse.RawError)` fallback |
| Pagination | none |
| Cite | `operations/Orders.md`, `Api/Orders.cs` |

**Request `OrderRequest`** (`records-1-Ac-Pa.md`):

| C# (wire) | Type | Required |
|---|---|---|
| `Intent` (`intent`) | `CheckoutPaymentIntent` | **!req** — use `Authorize` |
| `PurchaseUnits` (`purchase_units`) | `IReadOnlyList<PurchaseUnitRequest>` | **!req** — one unit |
| `PaymentSource` (`payment_source`) | `PaymentSource?` | set for direct card / vault pay |
| `Payer` (`payer`) | `Payer?` | optional |
| `ApplicationContext` (`application_context`) | `OrderApplicationContext?` | omit (wallet UX; we are not redirecting) |

**`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`): `Amount` (`amount`): `AmountWithBreakdown` **!req**; `CustomId` (`custom_id`): `string?` — set to eShop order id; `InvoiceId` (`invoice_id`): `string?`; `ReferenceId` (`reference_id`): `string?`.

**`AmountWithBreakdown`** (`records-1-Ac-Pa.md`): `CurrencyCode` (`currency_code`): `string` **!req** ← `PayPal:Currency`; `Value` (`value`): `string` **!req** ← order total to the cent (e.g. `"19.99"`); `Breakdown` optional. `Money` is the same `currency_code` + `value` pair (`records-1-Ac-Pa.md`).

**Raw card — `PaymentSource.Card` = `CardRequest`** (`records-1-Ac-Pa.md`, `Models/CardRequest.cs`):

| C# (wire) | Type | Notes |
|---|---|---|
| `Number` (`number`) | `string?` | PAN, 13–19 digits. Sandbox Visa `4111111111111111` |
| `Expiry` (`expiry`) | `string?` | ISO `YYYY-MM` |
| `SecurityCode` (`security_code`) | `string?` | 3–4 digit CVC |
| `Name` (`name`) | `string?` | cardholder name |
| `BillingAddress` (`billing_address`) | `Address?` | see Address |
| `VaultId` (`vault_id`) | `string?` | **vaulted pay — mutually exclusive with raw PAN** |
| `SingleUseToken` / `StoredCredential` / `NetworkToken` / `ExperienceContext` / `Attributes` | optional | do not set ExperienceContext (would invite 3DS return URLs) |

PCI: passing number/CVC/expiry via API requires PCI SAQ D (`CardRequest` XML). In-scope for this sandbox task.

**Vaulted card pay:** set only `PaymentSource = new PaymentSource { Card = new CardRequest { VaultId = <PaymentTokenResponse.Id> } }` — do **not** resend PAN. `Token` (`id` + `TokenType`) is **not** the vault payment-token path (`TokenType` only `BillingAgreement`).

**`Address`** (`records-1-Ac-Pa.md`): `AddressLine1` (`address_line_1`), `AddressLine2` (`address_line_2`), `AdminArea2` (`admin_area_2` = city), `AdminArea1` (`admin_area_1` = state), `PostalCode` (`postal_code`), `CountryCode` (`country_code`): `string` **!req**.

**Response `Order` — read** (`records-1-Ac-Pa.md`):

| C# (wire) | Use |
|---|---|
| `Id` (`id`) | PayPal order id — persist |
| `Status` (`status`) | `OrderStatus` — if `PayerActionRequired`, STOP (3DS) |
| `Intent` (`intent`) | expect `Authorize` |
| `PurchaseUnits` (`purchase_units`) | `[0].Payments.Authorizations` |
| `Links` (`links`) | scan `Rel == "payer-action"` |
| `PaymentSource` (`payment_source`) | `PaymentSourceResponse.Card` (last digits/brand only — no PAN) |

**Authorization from create** — `PurchaseUnit.Payments` → `PaymentCollection.Authorizations` → `AuthorizationWithAdditionalData`:

| C# (wire) | Use |
|---|---|
| `Id` (`id`) | **authorization id** for capture/void/reauthorize |
| `Status` (`status`) | `AuthorizationStatus` — expect `Created` |
| `Amount` (`amount`) | held `Money` — must match order total |
| `ExpirationTime` (`expiration_time`) | RFC-3339 — used to decide reauthorize |
| `CreateTime` (`create_time`) | honor-period clock |
| `ProcessorResponse` (`processor_response`) | AVS/CVV/response_code if present |

If `prefer=return=representation` and `Authorizations` is empty and status is not `PayerActionRequired`, call `AuthorizeOrder` (same payment source) before failing.

⚠ Step 1 (call) — many optional params have no C# default; positional calls mis-bind. Use named arguments; cancellation is `ct:`. **MUST load `dotnet-calling-endpoints`**.

⚠ Step 1 (models) — records are `init`-only; `required` members must be in the object initializer; enums are `StringEnum<T>` (`CheckoutPaymentIntent.Authorize`, not a C# enum). **MUST load `dotnet-models`**.

---

### Operation: AuthorizeOrder — fallback authorize

| | |
|---|---|
| Controller | `client.Orders.AuthorizeOrder` |
| HTTP | `POST /v2/checkout/orders/{id}/authorize` |
| Signature | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 5 nullable no-default (`payPalMockResponse` … `body`) |
| Returns | `PayPalServerSdk.Models.OrderAuthorizeResponse` (same shape as `Order` for the fields we read) |
| Error | **Case A** `SdkException<AuthorizeOrderError>` · `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError` |
| Cite | `operations/Orders.md` |

**`OrderAuthorizeRequest`**: `PaymentSource` (`payment_source`): `OrderAuthorizeRequestPaymentSource?` with `Card` (`CardRequest?`) — same raw-card / `VaultId` rules. Notes: buyer must have approved **or** a valid `payment_source` must be provided (we always send `payment_source`; we never follow `rel:approve`).

Read authorization id from `OrderAuthorizeResponse.PurchaseUnits[0].Payments.Authorizations[0].Id`. Same 3DS stop on `Status == PayerActionRequired`.

---

### Operation: GetOrder

| | |
|---|---|
| Signature | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `fields`, `payPalMockResponse`, `payPalAuthAssertion` (nullable, no default) |
| Returns | `Order` |
| Error | **Case A** `SdkException<GetOrderError>` · `TryGetError(out Error)` [401, 404] · `TryGetRawError` |
| Cite | `operations/Orders.md` |

---

### Operation: CaptureAuthorizedPayment — fulfil (take money)

| | |
|---|---|
| Controller | `client.Payments.CaptureAuthorizedPayment` |
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/capture` |
| Signature | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 4 nullable no-default (`payPalMockResponse` … `body`) |
| Returns | `PayPalServerSdk.Models.CapturedPayment` (not wrapped) |
| Error | **Case A** `SdkException<CaptureAuthorizedPaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| Cite | `operations/Payments.md` |

**`CaptureRequest`** (`records-1-Ac-Pa.md`): `Amount` (`amount`): `Money?` (omit to capture full authorized amount); `FinalCapture` (`final_capture`): `bool?` default `false` — **set `true`** for fulfilment of the whole order; `InvoiceId`, `NoteToPayer`, `SoftDescriptor`, `PaymentInstruction` optional.

**Response `CapturedPayment` — read:**

| C# (wire) | Use |
|---|---|
| `Id` (`id`) | capture id — persist |
| `Status` (`status`) | `CaptureStatus` — expect `Completed` before treating as taken |
| `Amount` (`amount`) | captured `Money` |
| `SellerReceivableBreakdown` (`seller_receivable_breakdown`) | fee / net — **absent when status is pending** |
| `CreateTime` / `UpdateTime` | timestamps |
| `InvoiceId` / `CustomId` | reconciliation |

**`SellerReceivableBreakdown`** (`records-2-Pa-Ve.md`): `GrossAmount` (`gross_amount`): `Money` **!req**; `PaypalFee` (`paypal_fee`): `Money?`; `NetAmount` (`net_amount`): `Money?`; also `PaypalFeeInReceivableCurrency`, `ReceivableAmount`, `ExchangeRate`, `PlatformFees`.

If capture returned `prefer=minimal` or breakdown is null, call `GetCapturedPayment`. Idempotent in effect: if local state already has a capture id, do not call again; a 409 from PayPal is the conflict path — read `Error.Details[].Issue` best-effort.

---

### Operation: GetCapturedPayment

| | |
|---|---|
| Signature | `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `payPalMockResponse` |
| Returns | `CapturedPayment` |
| Error | **Case A** `SdkException<GetCapturedPaymentError>` · `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError` |
| Cite | `operations/Payments.md` |

---

### Operation: GetAuthorizedPayment

| | |
|---|---|
| Signature | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `payPalMockResponse`, `payPalAuthAssertion` |
| Returns | `PayPalServerSdk.Models.PaymentAuthorization` |
| Error | **Case A** `SdkException<GetAuthorizedPaymentError>` · `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError` |
| Cite | `operations/Payments.md` |

Use `ExpirationTime` / `CreateTime` / `Status` to decide reauthorize vs fail. `PaymentAuthorization` fields match `Authorization` plus `SupplementaryData.RelatedIds` (`order_id`, `authorization_id`, `capture_id`).

---

### Operation: ReauthorizePayment — stale authorization

| | |
|---|---|
| Controller | `client.Payments.ReauthorizePayment` |
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/reauthorize` |
| Signature | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `payPalRequestId`, `payPalAuthAssertion`, `body` |
| Returns | `PaymentAuthorization` — **new authorization id** in `Id`; persist it (the old id is no longer the capture target) |
| Error | **Case A** `SdkException<ReauthorizePaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent` [500] · `TryGetRawError` |
| Cite | `operations/Payments.md`, `Api/Payments.cs` remarks |

**`ReauthorizeRequest`**: `Amount` (`amount`): `Money?` — **only supported request field**. Pass the original authorized amount (order total).

**When an auth is stale (from operation remarks — not inferred):**

| Clock | Meaning | Action |
|---|---|---|
| Days 0–3 | Honor period — funds guaranteed | Capture directly; do not reauthorize |
| Days 4–29 after original authorization (after honor period) | Reauthorize window. Reauthorized payment gets a **new 3-day honor period**. Multiple re-auths allowed inside 29 days. | Call `ReauthorizePayment`; capture the **returned** authorization id |
| ≥ 30 days since original authorization | **Cannot renew** | Do not call reauthorize. Operator error: obtain a new authorization (shopper must pay again — `CreateOrder` AUTHORIZE with card/`vault_id`). |

`PaymentAuthorization.ExpirationTime` is the RFC-3339 expiry to persist and compare.

**Errors — “must reauthorize” vs “cannot renew”:** the SDK does **not** enumerate `details[].issue` strings (GAP). Read `Error.Name`, `Error.Message`, `Error.DebugId`, `Error.Details[i].Issue` (`string` **!req**) + `Description`. Defensive mapping:

- If wall-clock ≥ 30 days since original `CreateTime` → treat as **cannot renew** without calling PayPal.
- If `ReauthorizePayment` returns 4xx: extract `Details[].Issue` best-effort; surface `Name` + `Issue` + `Description` + `DebugId` to the operator as actionable text. **UNVERIFIED** which live issue strings mean “expired, reauthorize” vs “authorization no longer reauthorizable”.
- Capture 422 after honor expiry → try reauthorize once (if still < 30 days), then retry capture on the new id.

---

### Operation: VoidPayment — cancel before fulfilment

| | |
|---|---|
| Controller | `client.Payments.VoidPayment` |
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/void` |
| Signature | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` |
| Returns | `PaymentAuthorization` — expect `Status == AuthorizationStatus.Voided` |
| Error | **Case A** `SdkException<VoidPaymentError>` · `TryGetError(out Error)` [401, 403, 404, **409**, 422] · `TryGetNoContent` [500] · `TryGetRawError` |
| Cite | `operations/Payments.md` |

Notes: cannot void an authorization that has been fully captured. 409 = conflict (already voided or captured) — extract `Issue` best-effort. No request body.

---

### Operation: RefundCapturedPayment

| | |
|---|---|
| Controller | `client.Payments.RefundCapturedPayment` |
| HTTP | `POST /v2/payments/captures/{capture_id}/refund` |
| Signature | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 4 nullable no-default (`payPalMockResponse` … `body`) |
| Returns | `PayPalServerSdk.Models.Refund` |
| Error | **Case A** `SdkException<RefundCapturedPaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent` [500] · `TryGetRawError` |
| Cite | `operations/Payments.md` |

**Idempotency:** caller-supplied key → `payPalRequestId` (`PayPal-Request-Id`, stored 45 days). Same key must not refund twice; two distinct keys for two partials of the same capture are legitimate. App must also persist refund ids and refuse a new refund that would exceed captured amount minus completed refunds.

**`RefundRequest`**: `Amount` (`amount`): `Money?` — **omit / null body = full refund**; set `Amount` for partial. Also `CustomId`, `InvoiceId`, `NoteToPayer`, `PaymentInstruction` optional. (`records-2-Pa-Ve.md`)

**Response `Refund` — read:** `Id` (`id`), `Status` (`RefundStatus`: `Cancelled`, `Failed`, `Pending`, `Completed`), `Amount`, `SellerPayableBreakdown` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount`), `CreateTime`.

**GetRefund:** `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, …)` → `Refund`. Error Case A [401, 403, 404] + 500 no-content.

---

### Operation: SearchTransactions — reconciliation (page the whole range)

| | |
|---|---|
| Controller | `client.TransactionSearch.SearchTransactions` |
| HTTP | `GET /v1/reporting/transactions` |
| Signature | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 8 nullable no-default (`transactionId` … `terminalId`) — pass `null` to skip |
| Query wire | `start_date` ← `startDate`, `end_date` ← `endDate`, `page` ← `page`, `page_size` ← `pageSize`, `fields` ← `fields`, … |
| Returns | `PayPalServerSdk.Models.SearchResponse` |
| Error | **Case B** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — **this is the only Case B operation in the SDK.** Read `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`. Optionally deserialize to `PayPalServerSdk.Models.SearchError` / `DefaultError`. |
| Pagination | Map: “none (only `page`, no `perPage`)”. **Still page:** `page` (default 1) + `pageSize` (default 100). Response: `Page`, `TotalItems`, `TotalPages`, `Links`. |
| Cite | `operations/TransactionSearch.md`, `Api/TransactionSearch.cs` |

**How to page the whole range:**

1. `startDate` / `endDate`: RFC-3339 with **seconds required** (fractional optional). **Maximum supported range is 31 days** — if `from`/`to` exceeds 31 days, split into adjacent windows and union results.
2. Call with `fields: "all"` (or at least include `transaction_info`) so related ids/amounts are present. Default `"transaction_info"` is enough for id/amount/status/date.
3. Start `page: 1`, `pageSize: 100`. After each response, if `TotalPages` is set, continue while `page < TotalPages` incrementing `page`. If `TotalPages` is null, stop when `TransactionDetails` is empty or shorter than `pageSize`.
4. Lag: executed transactions can take **up to three hours** to appear; history up to three years.

**`SearchResponse`:** `TransactionDetails` (`transaction_details`): `IReadOnlyList<TransactionDetails>?`; `StartDate` / `EndDate`; `LastRefreshedDatetime`; `Page`; `TotalItems`; `TotalPages`; `Links`; `AccountNumber`.

**`TransactionDetails.TransactionInfo` (`TransactionInformation`) — line-up fields:**

| C# (wire) | Use |
|---|---|
| `TransactionId` (`transaction_id`) | PayPal transaction id |
| `TransactionAmount` (`transaction_amount`) | `Money` |
| `FeeAmount` (`fee_amount`) | `Money?` |
| `TransactionStatus` (`transaction_status`) | `string?` — XML codes: `D` denied, `P` pending, `S` success, `V` reversed/refunded |
| `TransactionInitiationDate` (`transaction_initiation_date`) | date |
| `TransactionUpdatedDate` (`transaction_updated_date`) | date |
| `PaypalReferenceId` (`paypal_reference_id`) | related order/capture/auth id when present |
| `PaypalReferenceIdType` (`paypal_reference_id_type`) | `PayPalReferenceIdType`: `Odr` (`ODR`), `Txn` (`TXN`), `Sub` (`SUB`), `Pap` (`PAP`) |
| `InvoiceId` (`invoice_id`) | if we set invoice_id on purchase unit |
| `CustomField` (`custom_field`) | if we set custom_id |
| `TransactionEventCode` (`transaction_event_code`) | event type string |
| `PaymentTrackingId` (`payment_tracking_id`) | optional |

⚠ Step 5 (pagination / date window / retries) — page/token semantics and whether list GETs retry are not in the signature. **MUST load `dotnet-configuration-resilience`**.

---

### Operation: CreatePaymentToken — save a card

| | |
|---|---|
| Controller | `client.Vault.CreatePaymentToken` |
| HTTP | `POST /v3/vault/payment-tokens` |
| Signature | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `payPalRequestId` (nullable, no default) |
| Returns | `PayPalServerSdk.Models.PaymentTokenResponse` |
| Error | **Case A** `SdkException<CreatePaymentTokenError>` · `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError` — note accessor name **`TryGetError1`**, payload **`Error1`** |
| Cite | `operations/Vault.md` |

**`PaymentTokenRequest`:** `Customer` (`customer`): `Customer?`; `PaymentSource` (`payment_source`): `PaymentTokenRequestPaymentSource` **!req**.

**`Customer`:** `Id` (`id`): PayPal-generated customer id (omit on first save); `MerchantCustomerId` (`merchant_customer_id`): **our buyer id** — set this so the token is bound to the shopper.

**`PaymentTokenRequestPaymentSource.Card` = `PaymentTokenRequestCard`:** `Number`, `Expiry` (`YYYY-MM`), `SecurityCode`, `Name`, `BillingAddress` (`Address`), `Brand` optional. Never persist/log these request fields.

**Response `PaymentTokenResponse` — safe description:**

| C# (wire) | Use |
|---|---|
| `Id` (`id`) | vault payment-token id — persist; this is `CardRequest.VaultId` on a later pay |
| `Customer.Id` | PayPal customer id — persist |
| `Customer.MerchantCustomerId` | our buyer id echo |
| `PaymentSource.Card.LastDigits` (`last_digits`) | last digits only |
| `PaymentSource.Card.Brand` | `CardBrand` |
| `PaymentSource.Card.Expiry` | `YYYY-MM` |
| `PaymentSource.Card.Name` | optional name |

No PAN on the response model (`CardPaymentTokenEntity` has `LastDigits`, not `Number`).

Client XML: Vault API *Available in the US only.* (`PayPalServerSdkClient.cs`)

---

### Operation: ListCustomerPaymentTokens

| | |
|---|---|
| Signature | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Query | `customer_id` ← `customerId`, `page_size`, `page`, `total_required` |
| Returns | `CustomerVaultPaymentTokensResponse` |
| Error | **Case A** `SdkException<ListCustomerPaymentTokensError>` · `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError` |
| Pagination | `page` / `pageSize` (default page 1, size 5). Pass `totalRequired: true` to populate `TotalItems` / `TotalPages`. Loop `page = 1 .. TotalPages`. |
| Cite | `operations/Vault.md`, `Api/Vault.cs` |

`customerId` XML: *unique identifier representing a specific customer in merchant's/partner's system or records* — pass our buyer id (`merchant_customer_id`). Also persist PayPal `Customer.Id` from create; if list-by-merchant-id returns empty, retry with PayPal `customer.id` (**UNVERIFIED** which id the live query accepts; try merchant id first per XML).

**Response:** `PaymentTokens` (`payment_tokens`): `IReadOnlyList<PaymentTokenResponse>?`; `Customer` (`VaultResponseCustomer`); `TotalItems`; `TotalPages`; `Links`.

---

### Operation: GetPaymentToken / DeletePaymentToken

| | Get | Delete |
|---|---|---|
| HTTP | `GET /v3/vault/payment-tokens/{id}` | `DELETE /v3/vault/payment-tokens/{id}` |
| Signature | `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `PaymentTokenResponse` | `void` (`Task`) |
| Error | Case A `GetPaymentTokenError` · `TryGetError1(out Error1)` [403, 404, 422, 500] | Case A `DeletePaymentTokenError` · `TryGetError1(out Error1)` [400, 403, 500] |
| Cite | `operations/Vault.md` | |

After delete, refuse pay with that `vault_id`; list must not return it.

---

### Error envelope (every Case A except Vault)

`PayPalServerSdk.Models.Error`: `Name` (`name`) **!req**, `Message` (`message`) **!req**, `DebugId` (`debug_id`) **!req**, `Details` (`details`): `IReadOnlyList<ErrorDetails>?`, `Links`.

`ErrorDetails`: `Issue` (`issue`) **!req** — *fine-grained application-level error code* (plain `string`, **no SDK enum**); `Description`; `Field`; `Value`; `Location` default `"body"`.

Vault Case A uses `Error1` / `ErrorDetails1` / `ErrorLinkDescription` (`Rel` optional on error links) with accessor **`TryGetError1`**.

`SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`): only `TError Error { get; init; }` — **no status property on the exception**. Exact HTTP status: Case A via which accessor matched + `TryGetRawError` → `RawError.StatusCode`; Case B via `RawError.StatusCode`.

`RawError`: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`.

**Issue-code GAP:** insufficient funds, already captured, duplicate, 3DS, stale auth are **not** modeled as enums. Extract `Details[].Issue` + `Error.Name` best-effort; fall back to `Message` + `DebugId`. Label live string values **UNVERIFIED**. HTTP 409 on capture/void/refund is the documented conflict status.

---

### Enums actually used (`PayPalServerSdk.Models.Enums` — `StringEnum<T>`, members as `Type.Member`)

| Enum | Members (C# / wire) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` — use **Authorize** |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, … `Unknown (UNKNOWN)` (full list in `enums.md`) |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `PaymentTokenStatus` | `Created`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved`, `Vaulted`, `Tokenized` |
| `TokenType` | **`BillingAgreement (BILLING_AGREEMENT)` only** — not a vault payment token |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` — setup-token path only; not required if vaulting the card directly |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| `ParesStatus` | `Y`, `N`, `U`, `A`, `C`, `R`, `D`, `I` |
| `EnrollmentStatus` | `Y`, `N`, `U`, `B` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `DisbursementMode` | `Instant (INSTANT)`, `Delayed (DELAYED)` |
| `LinkHttpMethod` | `Get`, `Post`, `Put`, `Delete`, … |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `ServerEnvironment` | **`Sandbox` only** (`PayPalServerSdk.Servers`, not `Models.Enums`) |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` — only if also vaulting *during* an order via `CardAttributes.Vault` (out of primary save-card flow) |
| `PaymentInitiator` / `StoredPaymentSourcePaymentType` / `StoredPaymentSourceUsageType` | only if sending `CardStoredCredential` |

Cite: `map/models/enums.md`.

---

### Persist map (application DB — never PAN)

| Entity | Fields |
|---|---|
| Order payment | eShop `orderId`; PayPal `Order.Id`; `Authorization.Id` (replace on reauthorize); `Authorization.Status`; `Authorization.Amount`; `Authorization.CreateTime`; `Authorization.ExpirationTime`; `Capture.Id`; `Capture.Status`; `Capture.Amount`; `SellerReceivableBreakdown.PaypalFee`; `SellerReceivableBreakdown.NetAmount` |
| Refund | caller idempotency key; `Refund.Id`; `Refund.Status`; `Refund.Amount`; running refunded total vs captured |
| Vault | buyer id (`merchant_customer_id`); PayPal `Customer.Id`; `PaymentTokenResponse.Id`; last digits; brand; expiry |
| Pay idempotency | stable `payPalRequestId` per order-pay (and per fulfil / per refund key) |

---

### RequestOptions

`PayPalServerSdk.Core.RequestOptions`: `LogLevel? LogLevel` only. Pass `null` unless overriding log level. Not a cancellation or timeout knob. Cite: `Core/RequestOptions.cs`.

---

## Trap notes

⚠ Step 0 (client registration) — `HttpClient` / handler pipeline must be long-lived; the SDK wrapper over it may not share that lifetime. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 0 (auth) — set `Oauth2` from config before construct; do not hard-code secrets. **MUST load `dotnet-authentication`**.

⚠ Step 0 (base URL / retry / timeout) — option names do not reveal which calls retry, what `Timeout` bounds, or that a transport failure can re-send a write (`CreateOrder` / capture / refund). **MUST load `dotnet-configuration-resilience`**.

⚠ Step 1–8 (calls) — nullable parameters without defaults **must be passed explicitly** (`null` to skip); named arguments; cancellation token is `ct:`. **MUST load `dotnet-calling-endpoints`**.

⚠ Step 1–8 (models) — `required` + `init`-only records; `StringEnum<T>` static members / `FromValue`; unmodeled JSON dropped on deserialize. **MUST load `dotnet-models`**.

⚠ Step 1–8 (errors) — Case A vs Case B differ by operation (`SearchTransactions` is Case B; Vault uses `TryGetError1`/`Error1`); `TryGetRawError` is not a catch-all that replaces typed accessors. **MUST load `dotnet-error-handling`**.

⚠ Error boundary — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`**.

⚠ Error boundary — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`**.

⚠ Step 9 (tests) — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`** before stubbing.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — constructing / DI-registering `PayPalServerSdkClient` |
| `dotnet-authentication` | Step 0 — `Oauth2` client-id/secret |
| `dotnet-calling-endpoints` | Steps 1–8 — first SDK call, named args, `ct:` |
| `dotnet-models` | Steps 1–8 — request/response records, `StringEnum<T>`, required members |
| `dotnet-error-handling` | Error boundary for every operation (Case A/B, `JsonException` both directions) |
| `dotnet-configuration-resilience` | Step 0 retries/timeout/base URL; Step 5 pagination; write-retry hazard |
| `dotnet-testing` | Tests for the integration layer |

---

## Assumptions & Blockers

1. **GAP — no Live environment.** `ServerEnvironment` has only `Sandbox`. `PayPal:Environment=live` cannot be selected via the SDK enum. Sandbox is in-scope; live would require a later SDK that adds the member. (`Servers/ServerEnvironment.cs`)
2. **GAP — issue codes.** `ErrorDetails.Issue` is a free-form `string`. The SDK has no enum for `AUTHORIZATION_EXPIRED`, already-captured, insufficient funds, duplicate, or 3DS. Extract best-effort; live strings **UNVERIFIED**.
3. **GAP — `Token` is not vault-pay.** `TokenType` only `BILLING_AGREEMENT`. Vaulted cards pay via `CardRequest.VaultId`.
4. **Assumption:** fulfilment uses `Payments.CaptureAuthorizedPayment` (authorization id), not `Orders.CaptureOrder`.
5. **Assumption:** pay is single-step `CreateOrder` with `payment_source` + `AUTHORIZE`; `AuthorizeOrder` only if representation has no authorization and status is not `PAYER_ACTION_REQUIRED`.
6. **UNVERIFIED:** which header PayPal uses for dedupe given the SDK also sends a unique `Idempotency-Key` every call. App-level persistence is mandatory.
7. **UNVERIFIED:** `ListCustomerPaymentTokens(customerId)` live acceptance of merchant vs PayPal customer id. XML says merchant/partner system id; still persist both.
8. **Blocker for ranges > 31 days:** `SearchTransactions` max window is 31 days — split the caller’s `from`/`to`.
9. **3DS:** any `PAYER_ACTION_REQUIRED` / `rel:payer-action` is refused; no browser round-trip.
10. **Assumption:** `PayPal:Currency` is a three-letter ISO-4217 code written into every `Money`/`AmountWithBreakdown`. Amount `Value` is a decimal string matching the order total to the cent.
11. Vault controller XML: *Available in the US only* — if sandbox account is not US, vault calls may fail; that is a provider constraint, not an SDK signature gap.
