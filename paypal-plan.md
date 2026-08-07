# PayPal .NET SDK integration plan — eShopOnWeb (sandbox)

SDK: `AsadAli.Checkout.Sdk` (root namespace `PayPalServerSdk`, client `PayPalServerSdkClient`).
Map release stamp: tag `v1.0.1`, source commit `9653d18`. Grounded against the bundled SDK map;
two facts (base-URL override shape, OAuth credential property names) were read from the pinned
SDK source because the map carries only the type names, not their members.

Install (version-less, per getting-started): `dotnet add package AsadAli.Checkout.Sdk`.

---

## 1. Scope & sequence

1. **Client & DI setup** — register `PayPalServerSdkClient` via `AddPayPalServerSdkClient`, wire
   `IHttpClientFactory`, set environment/credentials/optional base URL from config.
2. **Flow 1 — one-off raw-card charge**: `client.Orders.CreateOrder` (intent=CAPTURE, card payment
   source) → `client.Orders.CaptureOrder`. Read PayPal order id, capture id, status.
3. **Flow 4 — full refund**: `client.Payments.RefundCapturedPayment` (empty body) → read refund id/status.
4. **Flow 3a — vault a card**: `client.Vault.CreatePaymentToken` (card) — or two-step
   `client.Vault.CreateSetupToken` → `client.Vault.CreatePaymentToken` (token). Read token id + brand/last4/expiry.
5. **Flow 3b — pay with saved card**: `client.Orders.CreateOrder` with
   `PaymentSource.Card = new CardRequest { VaultId = <payment-token-id> }` → `CaptureOrder`.
6. **Flow 5 — error boundary**: catch `SdkException<{Op}Error>` per operation; read typed payload +
   status via the accessors below.

**Capability verdict: NO GAP for the requested flows.** Raw-card direct capture (Flow 1) and
raw-card vaulting (Flow 3a) are both exposed (`CardRequest`, `PaymentTokenRequestCard`,
`SetupTokenRequestCard` all carry `number`/`expiry`/`security_code`). Charging a *saved card*
(Flow 3b) is exposed via `CardRequest.vault_id`, not via the token payment source — see the GAP-ADJACENT
note in Assumptions & Blockers about `PaymentSource.Token`.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one
> from that type's own map row, never from where a neighbouring type sits. A members table names the
> namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒
> `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server
> and client-config types are spread across different child namespaces, and two types configured
> side by side in the same options object routinely live in different ones. Dropping a type to the
> root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.1 Namespaces (add a `using` per kind — child namespaces are NOT imported transitively)

| Type(s) | Namespace |
|---|---|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` | `PayPalServerSdk` |
| Controllers reached via `client.Orders` / `.Payments` / `.Vault` | `PayPalServerSdk.Api` |
| `ServerEnvironment`, `DefaultOptions` (+ nested `DefaultOptions.SandboxOptions`) | `PayPalServerSdk.Servers` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| All request/response records (`OrderRequest`, `Order`, `CardRequest`, `PaymentSource`, `Money`, `Address`, `Error`, `Error1`, `ErrorDetails`, `PaymentTokenRequest`, `Refund`, …) | `PayPalServerSdk.Models` |
| All enums (`CheckoutPaymentIntent`, `CaptureStatus`, `RefundStatus`, `CardBrand`, `VaultTokenRequestType`, `TokenType`, `StoreInVaultInstruction`, …) | `PayPalServerSdk.Models.Enums` |
| Typed error wrappers (`CreateOrderError`, `CaptureOrderError`, `RefundCapturedPaymentError`, `CreatePaymentTokenError`, `CreateSetupTokenError`) | `PayPalServerSdk.Errors` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError` | `PayPalServerSdk.Core.ErrorResponse` |

### 2.2 Client construction, auth, environment, base URL

Source: `sdk-map.md` (*Getting a client*, *Servers & auth*, client-options table) + SDK source
`ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/ServerEnvironment.cs`,
`Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`.

- **Client ctor**: `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`.
- **DI**: `services.AddPayPalServerSdkClient(o => { … })` (source `ServiceCollectionExtensions.cs`).
- **`PayPalServerSdkClientOptions` properties**: `Environment: ServerEnvironment`, `Retry: RetryOptions`,
  `Logging: LoggingOptions`, `Server: ServerOptions`, `Oauth2: OAuth2ClientCredentials?`,
  `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.
- **Environment select**: `o.Environment = ServerEnvironment.Sandbox;`
  **`ServerEnvironment.Sandbox` is the ONLY member** — there is no Production/Live environment in this
  SDK (`ServerEnvironment.cs` declares Sandbox only; `Match` throws for anything else). Map
  `PAYPAL_ENVIRONMENT` accordingly: `sandbox` ⇒ `Sandbox`; treat any other value as a hard
  configuration error, do not invent a Live environment (see Blockers).
- **OAuth credentials** (`OAuth2ClientCredentials`, sealed): `ClientId` (`required string`),
  `ClientSecret` (`required string`), `Scope` (`string?`, optional). Set:
  `o.Oauth2 = new OAuth2ClientCredentials { ClientId = <PAYPAL_CLIENT_ID>, ClientSecret = <PAYPAL_CLIENT_SECRET> };`
- **Explicit base-URL override** (`PayPal:BaseUrl`): the base URL lives at
  `options.Server.Default.Sandbox.BaseUrl` (type `string`, default `"https://api-m.sandbox.paypal.com"`).
  `Server` is `ServerOptions` (ns `PayPalServerSdk`); `Server.Default` is `DefaultOptions` and
  `Default.Sandbox` is `DefaultOptions.SandboxOptions` (ns `PayPalServerSdk.Servers`). To honor the
  override verbatim:
  `o.Server = new ServerOptions { Default = new DefaultOptions { Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = cfg["PayPal:BaseUrl"] } } };`
  When `PayPal:BaseUrl` is unset, leave `Server` at its default (resolves to the sandbox host).

### 2.3 Operations

Every operation below is **throw-only** (no `…Result` variant), **Case A (typed error)** except where
noted. Signatures are verbatim; params marked *must-pass* are nullable with **no C# default** — pass
`null` explicitly to skip. `prefer` defaults to `"return=minimal"`; to get a full body back
(status/ids populated) pass `prefer: "return=representation"`.

| Op (controller.method) | Signature (verbatim) | Request model + key fields (`Name (wire): type, req?`) | Response envelope → fields the integration reads | Error case + accessors | Map page |
|---|---|---|---|---|---|
| `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 5 leading params must-pass | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `Payer?`, `ApplicationContext?` | Returns `Order`: `Id (id): string?` = PayPal order id · `Status (status): OrderStatus?` · `PurchaseUnits[].Payments.Captures[]` (see capture path below) | `SdkException<CreateOrderError>` · `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` [fallback] | operations/Orders.md; records-1 |
| `client.Orders.CaptureOrder` | `CaptureOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderCaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 params after `id` must-pass; `body` may be `null` | `OrderCaptureRequest`: `PaymentSource (payment_source): OrderCaptureRequestPaymentSource?` (usually `null` — card already on the created order) | Returns `Order` (same shape). Capture id/status: `order.PurchaseUnits[0].Payments.Captures[0].Id` and `.Status` (`CaptureStatus`) | `SdkException<CaptureOrderError>` · `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` [fallback] | operations/Orders.md |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 params after `captureId` must-pass | `RefundRequest` — **full refund = empty body**: pass `body: null` (or `new RefundRequest {}`). Fields (partial only, out of scope): `Amount (amount): Money?`, `InvoiceId?`, `CustomId?`, `NoteToPayer?` | Returns `Refund`: `Id (id): string?` = refund id · `Status (status): RefundStatus?` · `Amount (amount): Money?` | `SdkException<RefundCapturedPaymentError>` · `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] | operations/Payments.md; records-2 |
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must-pass | `PaymentTokenRequest`: `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` | Returns `PaymentTokenResponse`: `Id (id): string?` = **payment token id** · `PaymentSource.Card` = `CardPaymentTokenEntity` → `Brand (brand): CardBrand?`, `LastDigits (last_digits): string?`, `Expiry (expiry): string?` | `SdkException<CreatePaymentTokenError>` · `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError` [fallback] | operations/Vault.md; records-2/1 |
| `client.Vault.CreateSetupToken` | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must-pass | `SetupTokenRequest`: `Customer (customer): Customer?`, `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req` | Returns `SetupTokenResponse`: `Id (id): string?` = **setup token id** · `Status (status): PaymentTokenStatus?` · `PaymentSource.Card` = `SetupTokenResponseCard` → `Brand`, `LastDigits`, `Expiry` | `SdkException<CreateSetupTokenError>` · `TryGetError1(out Error1)` [400,403,422,500] · `TryGetRawError` [fallback] | operations/Vault.md; records-2 |

**Capture-id read path (Flow 1 & 3b), from `Order`:** `Order.PurchaseUnits` (`IReadOnlyList<PurchaseUnit>?`)
→ `PurchaseUnit.Payments` (`PaymentCollection?`) → `PaymentCollection.Captures` (`IReadOnlyList<OrdersCapture>?`)
→ `OrdersCapture.Id` (`string?`) and `OrdersCapture.Status` (`CaptureStatus?`). Null-guard every hop — all
are nullable and only populated when `prefer: "return=representation"` is sent (see trap note).
(records-1: `Order`, `PurchaseUnit`, `OrdersCapture`; records-2: `PaymentCollection`.)

### 2.4 Request model construction detail

**Flow 1 — one-off raw card** (`OrderRequest` for `CreateOrder`):
- `Intent = CheckoutPaymentIntent.Capture`
- `PurchaseUnits = new[] { new PurchaseUnitRequest { Amount = new AmountWithBreakdown { CurrencyCode = "USD", Value = "<amount>" } } }`
  - `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req`, `ReferenceId (reference_id): string?` (put your domain orderId here), `CustomId?`, `InvoiceId?`, `Description?`.
  - `AmountWithBreakdown`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown?` (value is a **string**, e.g. `"49.99"`).
- `PaymentSource = new PaymentSource { Card = new CardRequest { … } }`
  - `PaymentSource` fields: `Card (card): CardRequest?`, `Token (token): Token?`, `Paypal?`, `Venmo?`, … (records-2 `PaymentSource`).
  - `CardRequest`: `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?` (format `YYYY-MM`), `SecurityCode (security_code): string?`, `BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?`, `Attributes (attributes): CardAttributes?` (records-1 `CardRequest`). **PCI note in source: passing raw number/cvv/expiry requires PCI SAQ D compliance.**
  - `Address` (billing): `AddressLine1 (address_line_1): string?`, `AddressLine2?`, `AdminArea2 (admin_area_2): string?` (city), `AdminArea1 (admin_area_1): string?` (state), `PostalCode (postal_code): string?`, `CountryCode (country_code): string !req` (records-1 `Address`).
- Send `prefer: "return=representation"` on `CreateOrder` so `Order.Status`/capture data come back.

**No single combined create+capture call exists** — `CreateOrder` then `CaptureOrder` are separate ops.
Whether creating a CAPTURE-intent order *with a card payment source* auto-completes the capture (making
the second call unnecessary) is a live-wire behavior — see trap note; branch on `Order.Status`.

**Flow 3a — vault a raw card, one-step** (`PaymentTokenRequest`):
- `PaymentSource = new PaymentTokenRequestPaymentSource { Card = new PaymentTokenRequestCard { Number=…, Expiry=…, SecurityCode=…, BillingAddress=… } }`
  - `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?`, `Token (token): VaultTokenRequest?`.
  - `PaymentTokenRequestCard`: `Name?`, `Number (number): string?`, `Expiry?`, `SecurityCode?`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?` (records-2).

**Flow 3a — two-step setup→payment token** (if you want a setup-token stage):
1. `CreateSetupToken` with `SetupTokenRequest.PaymentSource = new SetupTokenRequestPaymentSource { Card = new SetupTokenRequestCard { Number=…, Expiry=…, SecurityCode=…, BillingAddress=… } }`.
   - `SetupTokenRequestCard`: `Name?`, `Number?`, `Expiry?`, `SecurityCode?`, `Brand?`, `BillingAddress?`, `VerificationMethod?`, `ExperienceContext?` (records-2). → returns `SetupTokenResponse.Id`.
2. `CreatePaymentToken` with `PaymentTokenRequest.PaymentSource = new PaymentTokenRequestPaymentSource { Token = new VaultTokenRequest { Id = <setupTokenId>, Type = VaultTokenRequestType.SetupToken } }`.
   - `VaultTokenRequest`: `Id (id): string !req`, `Type (type): VaultTokenRequestType !req` (records-2). `VaultTokenRequestType` has one value: `SetupToken (SETUP_TOKEN)`. → returns `PaymentTokenResponse.Id` = durable payment token id.

**Flow 3b — pay with saved card** (future order): `CreateOrder` with
`PaymentSource = new PaymentSource { Card = new CardRequest { VaultId = <paymentTokenId> } }` (do NOT
re-send raw number), then `CaptureOrder`. Read ids exactly as Flow 1.

### 2.5 Idempotency (PayPal-Request-Id) parameter names

Each create/capture/refund/vault op takes the idempotency key as a **positional `string?` parameter
named `payPalRequestId`** (not a body field). Pass a stable per-logical-operation GUID:

| Op | Idempotency param |
|---|---|
| `Orders.CreateOrder` | `payPalRequestId` (2nd param) |
| `Orders.CaptureOrder` | `payPalRequestId` (3rd param) |
| `Payments.RefundCapturedPayment` | `payPalRequestId` (3rd param) |
| `Vault.CreatePaymentToken` | `payPalRequestId` (1st param) |
| `Vault.CreateSetupToken` | `payPalRequestId` (1st param) |

Use named args to avoid mis-binding the several adjacent `string?` params, e.g.
`client.Orders.CreateOrder(payPalMockResponse: null, payPalRequestId: key, payPalPartnerAttributionId: null, payPalClientMetadataId: null, payPalAuthAssertion: null, body: order, prefer: "return=representation", ct: ct)`.

### 2.6 Enum value tables (member `(WIRE_VALUE)`) — ns `PayPalServerSdk.Models.Enums`

Enums are `StringEnum<T>`, **not** C# enums — reference the static member (`CheckoutPaymentIntent.Capture`),
or `Type.FromValue("CAPTURE")`. (Source: `map/models/enums.md`.)

| Enum | Members needed |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` — only value |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` — only value (see GAP-ADJACENT note) |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` — only value |
| `CardBrand` (for safe display) | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, `Jcb (JCB)`, `Diners (DINERS)`, `Maestro (MAESTRO)`, `Elo (ELO)`, `Rupay (RUPAY)`, `ChinaUnionPay (CHINA_UNION_PAY)`, … (30 members total; `Unknown (UNKNOWN)` for the fallback) |

### 2.7 Error handling (Flow 5) — exception type & reading status/body

- **Exception type**: every in-scope op throws `SdkException<{Op}Error>` (`PayPalServerSdk.Core.Exceptions`),
  which exposes exactly one member: `.Error` of type `{Op}Error`. **It carries no `StatusCode` property
  itself** (verified in source `Core/Exceptions/SdkException.cs`).
- **Typed payload accessors** (Case A):
  - Orders + Payments ops → `ex.Error.TryGetError(out Error typed)` returns the mapped-status shape;
    Payments also has `ex.Error.TryGetNoContent(out RawError)` for 500.
  - Vault ops → `ex.Error.TryGetError1(out Error1 typed)`.
  - All → inherited `ex.Error.TryGetRawError(out RawError raw)` fallback for unmapped statuses.
- **Typed body fields** — `Error` (and `Error1`, same shape): `Name (name): string !req`,
  `Message (message): string !req`, `DebugId (debug_id): string !req`,
  `Details (details): IReadOnlyList<ErrorDetails>?` (`Error1` uses `ErrorDetails1`).
  `ErrorDetails`: `Field (field): string?`, `Value?`, `Issue (issue): string !req`, `Description?`.
  These payloads do **not** contain the numeric HTTP status.
- **Reading the HTTP status code**: only `RawError.StatusCode` (`HttpStatusCode`) carries it, and
  `RawError` is reached only via `TryGetRawError`/`TryGetNoContent` (the raw/fallback path). For a
  *mapped* status (e.g. 422 on CreateOrder) you get the typed `Error` but not the code — infer the
  category from `Error.Details[].Issue`, or, when the exact code matters, note that mapped statuses are
  the documented set per op (CreateOrder ⇒ 400/401/422, etc.). **Load `dotnet-error-handling` for the
  safe status/body-extraction pattern before writing the boundary** (trap note below).

---

## 3. Trap notes (load the named skill at that step — do not resolve inline)

> ⚠ Step 1 (client & DI) — the `HttpClient`/handler pipeline the SDK client wraps has lifetime rules a
> ctor signature can't show (long-lived vs per-request), and the SDK client wrapper's own lifetime is a
> separate decision. **MUST load `dotnet-client-initialization`** before writing `AddPayPalServerSdkClient`
> / `new PayPalServerSdkClient(...)`.

> ⚠ Step 1 (auth) — *when* credentials must be set relative to client construction, and how to source
> the secret from configuration rather than hardcode, are not visible in the property type. **MUST load
> `dotnet-authentication`** before wiring `Oauth2`.

> ⚠ Step 1 (base URL / resilience) — the SDK `Retry.Timeout` does **not** bound a whole call and is
> **not** the timeout on the `HttpClient` you register; and whether a failed non-idempotent write (a
> `POST` create/capture/refund) can be **re-sent** by the retry layer is governed by rules the option
> names hide — which matters directly for how you scope your `payPalRequestId` idempotency keys. **MUST
> load `dotnet-configuration-resilience`** before tuning retries/timeouts/base URL.

> ⚠ Step 2/5 (calling + models) — optional params on these ops have no C# default and mis-bind
> positionally; unions/enums are built and read differently from plain records (`StringEnum` factory,
> `TryGet…` for unions), and JSON fields the SDK doesn't model are dropped on deserialize. **MUST load
> `dotnet-calling-endpoints`** and **`dotnet-models`** before constructing payloads / reading responses.

> ⚠ Step 2 (Flow 1 create→capture) — whether a CAPTURE-intent order created *with a card payment source*
> auto-captures (so the separate `CaptureOrder` call would double-charge or 422) versus still needing an
> explicit capture is confirmable only from live sandbox traffic. **`UNVERIFIED`.** Defensive directive:
> after `CreateOrder`, branch on `Order.Status` — if already `OrderStatus.Completed`, read the capture
> from `PurchaseUnits[0].Payments.Captures[0]` and **skip** `CaptureOrder`; otherwise call `CaptureOrder`.
> Reuse the same `payPalRequestId` on the capture so a retry cannot double-capture.

> ⚠ Step 2/3b (reading ids) — the capture-id path is entirely nullable and only populated when
> `prefer: "return=representation"` was sent; whether the live 2xx body actually carries
> `purchase_units[].payments.captures[]` for a card capture is a live-wire fact. **`UNVERIFIED`.**
> Defensive directive: null-guard every hop (`PurchaseUnits`, `Payments`, `Captures`, first element),
> and on a missing capture id fall back to re-`GetOrder(id, …)` or surface a clear "capture id
> unavailable" error rather than NRE.

> ⚠ Step 5 (error boundary) — see the two `JsonException` rows in REQUIRED READING; they must shape the
> boundary from the start. **MUST load `dotnet-error-handling`.**

---

## 4. REQUIRED READING (load BEFORE implementation starts; this sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — supplying `Oauth2` credentials, timing, secret sourcing |
| `dotnet-configuration-resilience` | Step 1 — retries/timeouts semantics, base-URL override, retry-vs-idempotency interplay |
| `dotnet-calling-endpoints` | Steps 2–5 — named-arg binding, async/cancellation, envelope shapes |
| `dotnet-models` | Steps 2–5 — building request records, `StringEnum` enums, unions, wire-name mapping |
| `dotnet-error-handling` | Step 5 — which exceptions actually reach catch, safe status/body extraction |
| `dotnet-testing` | Tests — the `HttpClient` seam, error-path coverage |

**Two mandatory `System.Text.Json.JsonException` hazard rows — the boundary must handle both from the
start (a caveat that arrives later arrives too late):**
- A drifted or malformed **2xx** body (a missing `required` member, e.g. an absent `debug_id` on a
  shape you deserialize) surfaces as a `JsonException` from deserialization, **not** as an
  `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed* (confirmed in source: `CreateOrderError.Create`
  calls `FromJson<Error>` for 400/401/422), so the `JsonException` **replaces** the `SdkException` and the
  HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a
  deterministic 4xx rejection as an outage, and a caller that retries 5xx retries something that can never
  succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- `PAYPAL_ENVIRONMENT` is expected to be `sandbox`; mapped to `ServerEnvironment.Sandbox` (the only member).
- `PayPal:BaseUrl`, when set, is applied verbatim to `options.Server.Default.Sandbox.BaseUrl`.
- Amounts are formatted as decimal **strings** in USD (e.g. `"49.99"`) for `Money.Value` /
  `AmountWithBreakdown.Value` — both `value` and `currency_code` are `string !req`.
- Your domain orderId is carried on `PurchaseUnitRequest.ReferenceId` (and/or `CustomId`); the PayPal
  order id is the separate `Order.Id` returned.
- Card `Expiry` wire format is `YYYY-MM` (PayPal card expiry convention); confirm against a live sandbox
  call if a create returns a validation `Issue` on `expiry`.

**Blockers / GAP notes**
- **No Live/Production environment in this SDK.** `ServerEnvironment` exposes only `Sandbox`. If the app
  is ever pointed at production via `PAYPAL_ENVIRONMENT`, this SDK cannot select a live server through
  `Environment` — the only lever is overriding `Server.Default.Sandbox.BaseUrl` to the live host. Flag to
  the main agent: production go-live needs either that base-URL override or a newer SDK. In scope
  (sandbox) this is **not** a blocker.
- **GAP-ADJACENT (Flow 3b) — saved-card charge path.** Charging a stored card uses
  `CardRequest.VaultId` on `PaymentSource.Card`, **not** `PaymentSource.Token`. `PaymentSource.Token` is a
  `Token { Id, Type }` whose `TokenType` enum has only `BillingAgreement (BILLING_AGREEMENT)` — i.e. the
  token payment source is for PayPal billing-agreement tokens, not vaulted card payment tokens. Whether
  the live API accepts the vault payment-token id in `card.vault_id` for a card-brand token is a live-wire
  fact — **`UNVERIFIED`**; defensive directive: on a `422`/`Issue` referencing the vault id, surface the
  provider `Error.Details[].Issue` message rather than assuming success. Not a hard GAP: the model surface
  to charge a saved card exists.
- No hard capability GAPs for Flows 1–5 as scoped: raw-card capture, raw-card vaulting (one-step and
  setup→payment two-step), full refund, and error typing are all exposed by the SDK.
