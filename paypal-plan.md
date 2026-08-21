# PayPal .NET SDK — contract sheet (authorize-then-capture + vault + transaction search)

NuGet: `AsadAli.Checkout.Sdk` (install version-less: `dotnet add package AsadAli.Checkout.Sdk`). Map documents tag `v1.0.1` / commit `9653d18`. Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdk.PayPalServerSdkClient`.

---

## Scope & sequence

1. **Client** — construct `PayPalServerSdkClient` with `HttpClient` + `PayPalServerSdkClientOptions` (`Oauth2.ClientId`/`ClientSecret`, `Environment`, custom `BaseUrl`).
2. **Create order (AUTHORIZE)** — `client.Orders.CreateOrder` with `OrderRequest.Intent = CheckoutPaymentIntent.Authorize`, amount as **string**, `PurchaseUnits`, `PaymentSource.Card` (raw PAN **or** `VaultId`).
3. **STOP gate (3DS / payer-action)** — if `Order.Status == OrderStatus.PayerActionRequired`, persist order id + links and **do not** authorize/capture.
4. **Read hold** — authorization id/status from `Order.PurchaseUnits[].Payments.Authorizations[]` (needs `prefer: "return=representation"`). If missing, `Orders.AuthorizeOrder` then re-read.
5. **Capture** — `client.Payments.CaptureAuthorizedPayment` by authorization id; persist capture id, amount, fee, net.
6. **Reauthorize / void** — `Payments.ReauthorizePayment` (stale honor period) / `Payments.VoidPayment`.
7. **Refund** — `Payments.RefundCapturedPayment` (full = no amount; partial = `Money`); stable `payPalRequestId`.
8. **Vault** — `Vault.CreatePaymentToken` → persist token id + customer id; `ListCustomerPaymentTokens`; `DeletePaymentToken`.
9. **Transaction search** — `TransactionSearch.SearchTransactions` date range + page loop.
10. **Error boundary** — typed `SdkException<{Op}Error>` / `SdkException<RawError>` + `JsonException` (see REQUIRED READING).

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

Enums are `StringEnum<T>` (namespace `PayPalServerSdk.Models.Enums`) — use static members (e.g. `CheckoutPaymentIntent.Authorize`), **not** C# enums. Records: `PayPalServerSdk.Models`. Errors: `PayPalServerSdk.Errors`. Controllers: `PayPalServerSdk.Api` via `client.{Group}`. No `…Result` no-throw variants exist on this SDK.

Nullable operation params **without a C# default must be passed explicitly** (`null` to skip).

---

### CLIENT / AUTH / BASE URL

| Fact | Contract | Cite |
|---|---|---|
| Constructor | `PayPalServerSdk.PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` — caller **owns** `HttpClient` lifetime | `sdk-map.md` Getting a client |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` — registers **singleton** client; internally `IHttpClientFactory.CreateClient()` (unnamed) | `ServiceCollectionExtensions.cs` |
| Options members | `Environment: PayPalServerSdk.Servers.ServerEnvironment` (default `ServerEnvironment.Default()` → `Sandbox`); `Retry: PayPalServerSdk.Core.Configuration.RetryOptions`; `Logging: LoggingOptions`; `Server: PayPalServerSdk.ServerOptions`; `Oauth2: PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?`; `Oauth2TokenStrategy: PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `sdk-map.md` + `PayPalServerSdkClientOptions.cs` |
| Credentials | `new OAuth2ClientCredentials { ClientId = …, ClientSecret = …, Scope = … }` — `ClientId`/`ClientSecret` are `required string`; `Scope` is `string?`. **Not** properties on `PayPalServerSdkClientOptions`. Set `options.Oauth2` before constructing the client. | `OAuth2ClientCredentials.cs`; `sdk-map.md` Servers & auth |
| Sandbox vs Live from a string | `ServerEnvironment` members: **`Sandbox` only** (wire `"Sandbox"`). `Default()` → `Sandbox`. **GAP: no `Live` / `Production` member.** A string `"live"` cannot be mapped to an environment enum. | `Servers/ServerEnvironment.cs`; `sdk-map.md` |
| **Custom BaseUrl (every call including OAuth token)** | **Property (not a method):** `options.Server.Default.Sandbox.BaseUrl` (`string`). Type path: `PayPalServerSdk.ServerOptions.Default` → `PayPalServerSdk.Servers.DefaultOptions.Sandbox` → `SandboxOptions.BaseUrl`. Default value `"https://api-m.sandbox.paypal.com"`. Resolver: `DefaultOptions.Resolve` → `UrlTemplate(Sandbox.BaseUrl, path)`. Token request uses `server.Default("/v1/oauth2/token")` — **same BaseUrl**. API ops use `_server.Default("/v2/…")` — **same BaseUrl**. `RequestOptions` has **only** `LogLevel` — **cannot** override host per call. **Token host CAN be overridden; not a blocker.** | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Server.cs`, `AuthSchemes.cs` |
| Per-request options | `PayPalServerSdk.Core.RequestOptions` — `LogLevel: Microsoft.Extensions.Logging.LogLevel?` only | `Core/RequestOptions.cs` |

⚠ Step 1 (client registration) — `HttpClient`/handler pipeline lifetime vs SDK wrapper lifetime is not visible from the constructor. **MUST load `dotnet-client-initialization`** before writing `new PayPalServerSdkClient` or `AddPayPalServerSdkClient`.

⚠ Step 1 (auth) — credentials must be on the options object before construct/DI; secret loading is not in the signature. **MUST load `dotnet-authentication`**.

⚠ Step 1 (BaseUrl / retries / timeout) — SDK retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; a transport failure can retry writes. **MUST load `dotnet-configuration-resilience`** before wiring the client.

---

### ORDERS — Create / authorize / 3DS stop

#### `client.Orders.CreateOrder` — `POST /v2/checkout/orders`

- **Signature:** `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderRequest body, string? prefer = "return=minimal", PayPalServerSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion` (`null` to skip).
- **Headers:** `payPalRequestId` → `PayPal-Request-Id` (idempotency; XML: stored 6 hours; **mandatory for single-step create with payment source** Card / vault_id). `prefer` → `Prefer`. SDK also sends a **fresh** `Idempotency-Key: Guid.NewGuid()` every invocation (not caller-stable).
- **Returns:** `PayPalServerSdk.Models.Order` (no envelope wrapper — the record **is** the payload).
- **Error:** `PayPalServerSdk.Core.Exceptions.SdkException<PayPalServerSdk.Errors.CreateOrderError>` Case A. Accessors: `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out PayPalServerSdk.Core.ErrorResponse.RawError)` fallback.
- **Cite:** `map/operations/Orders.md`; `Api/Orders.cs`

**`OrderRequest` fields** (`Models/OrderRequest.cs`, `records-1-Ac-Pa.md`):

| C# (wire) | Type | Required |
|---|---|---|
| `Intent (intent)` | `CheckoutPaymentIntent` | **!req** — use `CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`) |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | **!req** |
| `PaymentSource (payment_source)` | `PaymentSource?` | optional |
| `Payer (payer)` | `Payer?` | optional |
| `ApplicationContext (application_context)` | `OrderApplicationContext?` | optional |

**`PurchaseUnitRequest`:** `Amount (amount): AmountWithBreakdown !req`; `ReferenceId (reference_id): string?`; `CustomId (custom_id): string?`; `InvoiceId (invoice_id): string?`; `Description (description): string?`; `Payee (payee): PayeeBase?`; … (`records-2-Pa-Ve.md`)

**Amount is STRING not decimal.** `AmountWithBreakdown`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?`. Same for `Money`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req` (regex integer or decimal string, max 32). Cite: `records-1-Ac-Pa.md`; `Models/Money.cs`.

**Payment source — raw card (no JS/hosted-fields required by the SDK):**

`PaymentSource.Card (card): CardRequest?`

`CardRequest` (`records-1-Ac-Pa.md`; `Models/CardRequest.cs`):

| C# (wire) | Type | Notes |
|---|---|---|
| `Name (name)` | `string?` | cardholder name, 1–300 |
| `Number (number)` | `string?` | PAN 13–19 digits |
| `Expiry (expiry)` | `string?` | **ISO-8601 `YYYY-MM`** (length 7) |
| `SecurityCode (security_code)` | `string?` | 3–4 digit CVV |
| `BillingAddress (billing_address)` | `Address?` | `CountryCode (country_code): string !req`; `AddressLine1/2`, `AdminArea1/2`, `PostalCode` optional |
| `VaultId (vault_id)` | `string?` | **vault payment token id** for saved-card charges |
| `ExperienceContext (experience_context)` | `CardExperienceContext?` | `ReturnUrl (return_url)`, `CancelUrl (cancel_url)` for 3DS return |
| `Attributes (attributes)` | `CardAttributes?` | `Verification.Method` default `OrdersCardVerificationMethod.ScaWhenRequired` |

Hosted fields are a **PCI** alternative (XML: passing PAN/CVV/expiry requires PCI SAQ D). The SDK accepts server-side PAN on `CardRequest` — **no JS/hosted-fields SDK type or required call**.

**Payment source — vault payment token:** set `PaymentSource.Card.VaultId` to the vault token id (do **not** use `PaymentSource.Token` — `Token.Type` is only `TokenType.BillingAgreement`).

**Response `Order` (no wrapper)** — persist / read:

| Path | C# (wire) | Type |
|---|---|---|
| PayPal order id | `Order.Id (id)` | `string?` |
| Order status | `Order.Status (status)` | `OrderStatus?` |
| Intent | `Order.Intent (intent)` | `CheckoutPaymentIntent?` |
| Links (3DS / approve) | `Order.Links (links)` | `IReadOnlyList<LinkDescription>?` — `Href (href): string !req`, `Rel (rel): string !req`, `Method (method): LinkHttpMethod?` |
| Nested auth | `Order.PurchaseUnits (purchase_units)[].Payments (payments).Authorizations (authorizations)[]` | `AuthorizationWithAdditionalData` |
| Auth id | `.Id (id)` | `string?` |
| Auth status | `.Status (status)` | `AuthorizationStatus?` |
| Auth expiry | `.ExpirationTime (expiration_time)` | `string?` |
| 3DS result | `Order.PaymentSource (payment_source).Card (card).AuthenticationResult (authentication_result)` | `AuthenticationResponse?` — `LiabilityShift (liability_shift): LiabilityShiftIndicator?`; `ThreeDSecure (three_d_secure): ThreeDSecureAuthenticationResponse?` (`AuthenticationStatus (authentication_status): ParesStatus?`, `EnrollmentStatus (enrollment_status): EnrollmentStatus?`) |

**`prefer` default `"return=minimal"`** returns only id, status, HATEOAS links. Pass **`prefer: "return=representation"`** to get nested `purchase_units[].payments.authorizations[]` and 3DS card results. Cite: `Api/Orders.cs` prefer XML.

**3DS / payer-action STOP (do not capture):**

1. **`Order.Status == OrderStatus.PayerActionRequired`** (wire `PAYER_ACTION_REQUIRED`) — **primary stop**. Persist `Order.Id` + `Order.Links`.
2. Links: `Rel` is an unconstrained `string`. Map documents buyer approval via **`rel:approve`** (`operations/Orders.md` AuthorizeOrder/CaptureOrder notes). **GAP:** map does not enumerate a card-3DS-specific `rel` value — inspect `Links[].Rel`/`Href` when status is `PAYER_ACTION_REQUIRED`; do not proceed to capture.
3. Optional 3DS payload (representation only): `PaymentSource.Card.AuthenticationResult`. `ParesStatus` members: `Y, N, U, A, C, R, D, I`. **UNVERIFIED:** which `ParesStatus` value means “challenge required” (enum summary does not define `C`).
4. Challenge is a **2xx `Order`**, not an `SdkException`. Errors (`Error.Details[].Issue`) are a separate failure path.

#### `client.Orders.AuthorizeOrder` — `POST /v2/checkout/orders/{id}/authorize`

Use when CreateOrder did not nest an authorization (minimal response, or status `CREATED`/`APPROVED` without `Payments.Authorizations`).

- **Signature:** `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Must pass: `payPalMockResponse` … `body` explicitly.
- **Returns:** `OrderAuthorizeResponse` (same persistable shape as `Order`: `Id`, `Status`, `PurchaseUnits`, `Links`, `PaymentSource`).
- **Error:** `SdkException<AuthorizeOrderError>` Case A: `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError`.
- Body: `OrderAuthorizeRequest.PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` with `Card (card): CardRequest?`.
- Cite: `map/operations/Orders.md`

#### `client.Orders.GetOrder` — `GET /v2/checkout/orders/{id}`

- **Signature:** `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- `fields` query: XML — valid filter is `payment_source` only.
- **Returns:** `Order`. **Error:** `SdkException<GetOrderError>` Case A: `TryGetError(out Error)` [401, 404] · `TryGetRawError`.
- Cite: `map/operations/Orders.md`; `Api/Orders.cs`

⚠ Step 2 (calls) — many optional params have no C# default; positional calls mis-bind. **MUST load `dotnet-calling-endpoints`**.

⚠ Step 2 (models) — StringEnum members, required init properties, unmodeled JSON dropped. **MUST load `dotnet-models`**.

---

### PAYMENTS — capture / reauthorize / void / refund

#### `client.Payments.CaptureAuthorizedPayment` — `POST /v2/payments/authorizations/{authorization_id}/capture`

- **Signature:** `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Must pass: `payPalMockResponse` … `body` explicitly.
- **Idempotency:** `payPalRequestId` → `PayPal-Request-Id` (XML: stored **45 days**). Pass a stable key. SDK also sends fresh `Idempotency-Key` UUID.
- Pass `prefer: "return=representation"` to get fee/net breakdown.
- **Returns:** `CapturedPayment` (no wrapper).
- **Error:** `SdkException<CaptureAuthorizedPaymentError>` Case A: `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.
- Cite: `map/operations/Payments.md`; `Api/Payments.cs`

**`CaptureRequest`:** `Amount (amount): Money?` (omit for full remaining); `FinalCapture (final_capture): bool? = false`; `InvoiceId`, `NoteToPayer`, `SoftDescriptor`, `PaymentInstruction`. Cite: `records-1-Ac-Pa.md`

**Captured amount / PayPal fee / net to merchant:**

| Meaning | Path on `CapturedPayment` | C# (wire) |
|---|---|---|
| Capture id | `.Id` | `Id (id): string?` |
| Status | `.Status` | `Status (status): CaptureStatus?` |
| Captured amount | `.Amount` | `Amount (amount): Money?` |
| Gross | `.SellerReceivableBreakdown.GrossAmount` | `GrossAmount (gross_amount): Money !req` |
| **PayPal fee** | `.SellerReceivableBreakdown.PaypalFee` | `PaypalFee (paypal_fee): Money?` |
| **Net to merchant** | `.SellerReceivableBreakdown.NetAmount` | `NetAmount (net_amount): Money?` |
| Fee in receivable ccy | `.SellerReceivableBreakdown.PaypalFeeInReceivableCurrency` | `PaypalFeeInReceivableCurrency (paypal_fee_in_receivable_currency): Money?` |
| Times | `.CreateTime` / `.UpdateTime` | `create_time` / `update_time` |

`SellerReceivableBreakdown` XML: **not available for pending captures**. Cite: `records-2-Pa-Ve.md`

#### `client.Payments.ReauthorizePayment` — `POST /v2/payments/authorizations/{authorization_id}/reauthorize`

- **Signature:** `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Must pass: `payPalRequestId`, `payPalAuthAssertion`, `body`.
- **Returns:** `PaymentAuthorization`.
- **Error:** `SdkException<ReauthorizePaymentError>` Case A: `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.
- **Request:** `ReauthorizeRequest.Amount (amount): Money?` — **only supported field**.
- **When it applies** (`operations/Payments.md` notes): after the **3-day honor period**; within the **29-day** authorization window (days **4–29**); new honor period is 3 days; if **30 days** since original authorization you **must create a new authorized payment**, not reauthorize. US amount cap example: up to 115% of original, max +$75 USD.
- **Map conflict:** operation notes say “multiple re-authorizations” after honor expiry; `ReauthorizeRequest` summary says “only once from days four to 29”. Treat **422/400** as cannot-renew.
- **Cannot-renew errors:** no typed issue-code enum. Read `Error.Details[].Issue` (`string !req`) + `Error.Name`/`Message`/`DebugId`. **GAP:** map/source do not list the cannot-renew `issue` literal(s).
- Cite: `map/operations/Payments.md`; `records-2-Pa-Ve.md`

#### `client.Payments.VoidPayment` — `POST /v2/payments/authorizations/{authorization_id}/void`

- **Signature:** `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Must pass: `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId`.
- **Returns:** `PaymentAuthorization`. Notes: **cannot void a fully captured** authorization.
- **Error:** `SdkException<VoidPaymentError>` Case A: `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError`.
- Cite: `map/operations/Payments.md`

#### `client.Payments.RefundCapturedPayment` — `POST /v2/payments/captures/{capture_id}/refund`

- **Signature:** `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Must pass: `payPalMockResponse` … `body`.
- **Full refund:** `body: null` (or empty `RefundRequest` with no `Amount`) — notes: “empty payload”.
- **Partial refund:** `RefundRequest.Amount (amount): Money?` (`CurrencyCode` + `Value` strings). Also `CustomId`, `InvoiceId`, `NoteToPayer`.
- **Idempotency:** `payPalRequestId` → `PayPal-Request-Id` (45 days). Same pattern on **CreateOrder** and **CaptureAuthorizedPayment**. Pass a **stable** key so double-click is safe. (SDK’s extra `Idempotency-Key` is a new GUID per call — **UNVERIFIED** whether PayPal treats it as a second idempotency axis.)
- **Returns:** `Refund`.
- **Error:** `SdkException<RefundCapturedPaymentError>` Case A: `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError`.
- Cite: `map/operations/Payments.md`; `records-2-Pa-Ve.md`

**`Refund` persistable / fee paths:** `Id (id)`, `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown)` → `PaypalFee (paypal_fee)`, `NetAmount (net_amount)`, `GrossAmount (gross_amount)`, `TotalRefundedAmount (total_refunded_amount)`.

Lookups: `GetAuthorizedPayment`, `GetCapturedPayment`, `GetRefund` — all Case A typed errors (see Payments.md).

---

### Persistable ids / statuses (hold, capture, refunds)

| Concept | Persist | Status enum (member = wire) |
|---|---|---|
| Order | `Order.Id` | `OrderStatus`: `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| Hold / authorization | `AuthorizationWithAdditionalData.Id` **or** `PaymentAuthorization.Id`; also `ExpirationTime` | `AuthorizationStatus`: `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| Capture | `CapturedPayment.Id` (also `OrdersCapture.Id` on the order’s `PaymentCollection.Captures`) | `CaptureStatus`: `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| Refund | `Refund.Id` | `RefundStatus`: `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| Vault token | `PaymentTokenResponse.Id`; `Customer.Id` (needed to list) | card vault: `CardPaymentTokenEntity` descriptors below; setup-token status `PaymentTokenStatus` if using setup tokens |

Pending reasons: `AuthorizationStatusDetails.Reason` → `AuthorizationIncompleteReason` (`PendingReview`, `DeclinedByRiskFraudFilters`); `CaptureStatusDetails.Reason` → `CaptureIncompleteReason`; `RefundStatusDetails.Reason` → `RefundIncompleteReason.Echeck`.

---

### VAULT

#### `client.Vault.CreatePaymentToken` — `POST /v3/vault/payment-tokens` (save card → payment token)

- **Signature:** `CreatePaymentToken(string? payPalRequestId, PayPalServerSdk.Models.PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Must pass: `payPalRequestId` (`PayPal-Request-Id`; XML: stored **3 hours**).
- **Returns:** `PaymentTokenResponse`.
- **Error:** `SdkException<CreatePaymentTokenError>` Case A: `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError`. **Note accessor name `TryGetError1`**, payload `Error1` (not `Error`).
- Cite: `map/operations/Vault.md`

**`PaymentTokenRequest`:** `Customer (customer): Customer?` (`Id`, `MerchantCustomerId`); `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` → `Card (card): PaymentTokenRequestCard?` (`Name`, `Number`, `Expiry` **YYYY-MM**, `SecurityCode`, `Brand`, `BillingAddress`) or `Token (token): VaultTokenRequest?` (`Id !req`, `Type: VaultTokenRequestType.SetupToken` only).

**Safe descriptors on `PaymentTokenResponse`:**

| Persist | Path | C# (wire) |
|---|---|---|
| Token id | `.Id` | `Id (id): string?` |
| Customer id | `.Customer.Id` | `CustomerResponse.Id (id): string?` |
| Brand | `.PaymentSource.Card.Brand` | `Brand (brand): CardBrand?` |
| Last4 | `.PaymentSource.Card.LastDigits` | `LastDigits (last_digits): string?` (**not** `Last4`) |
| Expiry | `.PaymentSource.Card.Expiry` | `Expiry (expiry): string?` |

#### `client.Vault.ListCustomerPaymentTokens` — `GET /v3/vault/payment-tokens`

- **Signature:** `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Query: `customer_id` ← `customerId` (**required** — PayPal customer id).
- **Returns:** `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `TotalItems`, `TotalPages`, `Links`.
- **Pagination:** `page` / `pageSize` only (no `perPage`). Loop `page` while `page < TotalPages` (set `totalRequired: true` to populate totals).
- **Error:** `SdkException<ListCustomerPaymentTokensError>` Case A: `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`.
- You **can** list if you persist `Customer.Id`; you should **also** persist token ids. Listing without a customer id is **not** possible (param is required).
- Cite: `map/operations/Vault.md`; `records-1-Ac-Pa.md`

#### `client.Vault.GetPaymentToken` / `DeletePaymentToken`

- `GetPaymentToken(string id, …)` → `PaymentTokenResponse`. Error: `TryGetError1` [403, 404, 422, 500].
- `DeletePaymentToken(string id, …)` → `void`. Error: `TryGetError1` [400, 403, 500].
- Cite: `map/operations/Vault.md`

Vault controller XML: Payment Method Tokens API v3 **available in the US only** (`PayPalServerSdkClient.cs`).

---

### TRANSACTION SEARCH

#### `client.TransactionSearch.SearchTransactions` — `GET /v1/reporting/transactions`

- **Signature:** `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Must pass explicitly: `transactionId` … `terminalId` (`null` to skip).
- **Dates:** RFC3339; **seconds required**; `endDate` max range **31 days**. Chunk longer windows. Latency: up to **3 hours** before a txn appears; last **3 years**.
- **Pagination to exhaust:** `SearchResponse.Page`, `TotalPages`, `TotalItems`, `Links`. Increment `page` (default 1) until `page >= TotalPages`. Map: pagination “none (only `page`, no `perPage`)”.
- **`fields`:** default `"transaction_info"`. XML: comma-separated; `fields=all` for all. Valid: `transaction_info`, `payer_info`, `shipping_info`, `auction_info`, `cart_info`, `incentive_info`, `store_info`.
- **Returns:** `SearchResponse` (no wrapper).
- **Error:** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` **Case B (the only Case B op in this SDK)**. Accessors: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. No `TryGetError`.
- Cite: `map/operations/TransactionSearch.md`; `Api/TransactionSearch.cs`

**Match eShop orders** (`TransactionDetails.TransactionInfo` → `TransactionInformation`):

| eShop need | C# (wire) | Type |
|---|---|---|
| Txn id | `TransactionId (transaction_id)` | `string?` (XML: 17 chars; order id 19; **not unique** in reporting) |
| Amount | `TransactionAmount (transaction_amount)` | `Money?` |
| Status | `TransactionStatus (transaction_status)` | `string?` (not an enum). Filter param codes: `D` denied, `P` pending, `S` success, `V` reversed/refunded |
| Related capture/order ids | `PaypalReferenceId (paypal_reference_id)` + `PaypalReferenceIdType (paypal_reference_id_type)` | `PayPalReferenceIdType`: `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| Time | `TransactionInitiationDate (transaction_initiation_date)`, `TransactionUpdatedDate (transaction_updated_date)` | `string?` |
| Fees | `FeeAmount (fee_amount)` | `Money?` |
| Extra match keys | `InvoiceId (invoice_id)`, `CustomField (custom_field)`, `PaymentTrackingId (payment_tracking_id)` | `string?` |

**GAP:** no dedicated `order_id` / `capture_id` fields on `TransactionInformation`. Correlate via `TransactionId`, `PaypalReferenceId`+`Type`, `InvoiceId`, `CustomField`.

Filter `transactionStatus` is a **query string**, not `StringEnum`. Response status is a **string**.

---

### ERRORS — types that reach `catch`

| Catch type | When | Status / issue / debug |
|---|---|---|
| `PayPalServerSdk.Core.Exceptions.SdkException<TError>` | Every documented operation on error HTTP status. **`SdkException<T>` members: `Error` only** — no `StatusCode` on the exception | Case A: `ex.Error.TryGetError(out Error)` / `TryGetError1(out Error1)` / `TryGetDefaultError(out DefaultError)` / `TryGetNoContent(out RawError)` then `TryGetRawError`. Case B (`SearchTransactions` only): `ex.Error` **is** `RawError` → `StatusCode` |
| `PayPalServerSdk.Core.Exceptions.AuthSchemeException` | Auth scheme failures (token fetch). `SchemeFailures: IReadOnlyList<Exception>` | Not an HTTP error envelope |
| `System.Text.Json.JsonException` | See REQUIRED READING (2xx drift **and** non-2xx mismatch) — **not** an `SdkException` | |

**`Error` / `Error1` / `DefaultError` accessors (payload records, `PayPalServerSdk.Models`):**

| Field | `Error` | `Error1` (Vault) | `DefaultError` (SearchBalances) |
|---|---|---|---|
| `Name (name)` | `string !req` | `string !req` | `string !req` |
| `Message (message)` | `string !req` | `string !req` | `string !req` |
| `DebugId (debug_id)` | `string !req` | `string !req` | `string !req` |
| Issue codes | `Details[].Issue (issue): string !req` (`ErrorDetails`) | `Details[].Issue (issue): string !req` (`ErrorDetails1`) | `Details[].Issue (issue): string !req` (`TransactionSearchErrorDetails`) |
| HTTP status (Case A) | Inferred from which `TryGet*` succeeded (status lists on each op row) **or** `TryGetRawError` → `RawError.StatusCode` | same | same |
| HTTP status (Case B) | `RawError.StatusCode` | — | — |

**GAP:** issue codes are free-form `string`s — map does not enumerate `ISSUE` literals (e.g. cannot-renew).

`RawError`: `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()`.

No-throw `…Result` variants: **absent** (`sdk-map.md`).

---

### Enums actually needed (`PayPalServerSdk.Models.Enums`, `map/models/enums.md`)

| Enum | Members (C# (wire)) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, … `Unknown (UNKNOWN)` (full list in enums.md) |
| `LinkHttpMethod` | `Get (GET)`, `Post (POST)`, `Put (PUT)`, `Delete (DELETE)`, `Head (HEAD)`, `Connect (CONNECT)`, `Options (OPTIONS)`, `Patch (PATCH)` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `ParesStatus` | `Y (Y)`, `N (N)`, `U (U)`, `A (A)`, `C (C)`, `R (R)`, `D (D)`, `I (I)` |
| `EnrollmentStatus` | `Y (Y)`, `N (N)`, `U (U)`, `B (B)` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| `PayPalReferenceIdType` | `Odr (ODR)`, `Txn (TXN)`, `Sub (SUB)`, `Pap (PAP)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` only |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` only |
| `DisbursementMode` | `Instant (INSTANT)`, `Delayed (DELAYED)` |
| `ServerEnvironment` | **`Sandbox` only** (`PayPalServerSdk.Servers`, **not** `Models.Enums`) |

Unions: **none** (`map/models/unions.md`).

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership and DI singleton vs factory lifetime are not in the constructor signature. **MUST load `dotnet-client-initialization`** before writing the client.

⚠ Step 1 (auth) — `Oauth2` vs `Oauth2TokenStrategy` wiring and when credentials are read are not in the options type alone. **MUST load `dotnet-authentication`**.

⚠ Step 1 (BaseUrl / retries / timeout) — SDK retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; whether a failed write can be re-sent is not visible from `RetryOptions` names. **MUST load `dotnet-configuration-resilience`**.

⚠ Step 2 (first SDK call) — optional params without defaults mis-bind positionally; cancellation-token name is `ct`. **MUST load `dotnet-calling-endpoints`**.

⚠ Step 2 (request/response models) — StringEnum construction, `required` init, `CardRequest.VaultId` vs `Token`, amount-as-string. **MUST load `dotnet-models`**.

⚠ Step 10 (error boundary) — Case A vs Case B, `TryGetError` vs `TryGetError1` vs `TryGetRawError` not being a catch-all, and `JsonException` from two directions (see REQUIRED READING). **MUST load `dotnet-error-handling`**.

⚠ Tests — `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`** before stubbing.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1 client/`HttpClient`/DI
- `dotnet-authentication` — Step 1 `Oauth2` credentials
- `dotnet-configuration-resilience` — Step 1 BaseUrl, retries, timeouts, pagination loops
- `dotnet-calling-endpoints` — Steps 2–9 operation calls
- `dotnet-models` — Steps 2–9 request/response construction
- `dotnet-error-handling` — Step 10 exception boundary (always)
- `dotnet-testing` — tests for the integration layer

**`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

**Assumptions**

- Authorize-then-capture uses **Payments** capture (`CaptureAuthorizedPayment`) on the authorization id, not `Orders.CaptureOrder`.
- Server-side raw card on `CreateOrder` + `payment_source.card` is in scope; hosted fields are PCI guidance only.
- Vault “save card” is `CreatePaymentToken` (not setup-token + convert), unless the app later needs a setup-token flow.
- Transaction search matching uses `TransactionId` / `PaypalReferenceId`+`Type` / `InvoiceId` / `CustomField` because there is no order/capture id field.

**Blockers / GAPs**

- **GAP — no Live environment.** `ServerEnvironment` has only `Sandbox`. Cannot select Live from a string via the enum. Production host must be supplied by setting `options.Server.Default.Sandbox.BaseUrl` (the **only** override; it **does** apply to `/v1/oauth2/token`). SDK ships **no** live default URL.
- **GAP — card 3DS link `rel`.** Map names `rel:approve` for buyer approval; it does not list a card-3DS rel. Stop on `OrderStatus.PayerActionRequired` and persist `Links`.
- **GAP — reauthorize cannot-renew `issue` literals.** Handle 400/422 via `TryGetError` → `Details[].Issue`; codes not in the map.
- **GAP — transaction search has no `order_id`/`capture_id` fields.**
- **UNVERIFIED —** whether `ParesStatus.C` means 3DS challenge; whether the SDK-generated `Idempotency-Key` UUID interferes with caller `PayPal-Request-Id` (live traffic only).
- Token host override: **not a blocker** (`Server.Default.Sandbox.BaseUrl` is used for OAuth and API).
