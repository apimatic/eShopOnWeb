# PayPal .NET SDK integration plan — card payments, refunds, and vault (eShopOnWeb, `src/PublicApi`)

SDK: `PayPalServerSdk` · NuGet `AsadAli.Checkout.Sdk` (install **version-less**) · root namespace `PayPalServerSdk`.
Target project: `src/PublicApi` (ASP.NET Core, .NET 8). Every row below cites the map page it came from.
Grounded against the bundled SDK map (spec stamp `9653d18`, tag `v1.0.1`); a handful of source-verified
facts are marked `[src]`.

---

## 1. Scope & sequence

| # | Capability | SDK operation(s) | Notes |
|---|---|---|---|
| 0 | Install + client/DI setup | — | `dotnet add package AsadAli.Checkout.Sdk`; register `PayPalServerSdkClient` via DI |
| 1 | Place order (app-internal) | none | no PayPal call |
| 2 | Pay with **raw card** (create + capture) | `client.Orders.CreateOrder` → then `client.Orders.CaptureOrder` | intent `CAPTURE`; card in `payment_source.card` |
| 3 | Pay with **saved (vault) card** | `client.Orders.CreateOrder` (+ `CaptureOrder`) | `payment_source.card.vault_id` instead of raw PAN |
| 4 | **Full refund** of a capture | `client.Payments.RefundCapturedPayment` | empty body = full refund |
| 5 | **Idempotency** on create/capture/refund | `payPalRequestId` param (all three ops) | maps to `PayPal-Request-Id` header |
| 6 | **Vault a card** standalone | `client.Vault.CreatePaymentToken` (primary) or `CreateSetupToken`→`CreatePaymentToken` (two-step) | raw card → payment token |
| 7 | Vault-id type match (6 ↔ 3) | — | both `string` — confirmed below |
| 8 | Delete a saved card | `client.Vault.DeletePaymentToken` (optional; app-side otherwise) | exists in SDK |

**Flow-2-verified behavioural note (UNVERIFIED):** Whether `CreateOrder` with `intent=CAPTURE` **and** a
raw/vaulted `payment_source.card` auto-captures at creation (order returns `COMPLETED` with the capture
already nested) versus requiring a subsequent `CaptureOrder(id)` call is decided by live PayPal
account/3DS behaviour and cannot be settled from the map or SDK source. **Defensive directive:** after
`CreateOrder`, branch on `order.Status` — if `OrderStatus.Completed`, read the capture already present at
`order.PurchaseUnits[0].Payments.Captures[0]` and do **not** call `CaptureOrder` again (a second capture
throws, typically `ORDER_ALREADY_CAPTURED`); otherwise call `CaptureOrder(id, …)` and read the capture
from its returned `Order`. Extract the capture id/status through the same nested path in both branches.

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

### 2.1 Namespaces (add a `using` per kind — child namespaces are NOT transitively imported)

| Kind | Namespace | Examples |
|---|---|---|
| Client, options, `ServerOptions`, `Server` | `PayPalServerSdk` | `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` |
| Controllers | `PayPalServerSdk.Api` | (accessed as `client.Orders`, `client.Payments`, `client.Vault`) |
| Records (request/response models) | `PayPalServerSdk.Models` | `OrderRequest`, `CardRequest`, `Order`, `Refund`, `PaymentTokenRequest`, … |
| Enums (`StringEnum<T>`, not C# enums) | `PayPalServerSdk.Models.Enums` | `CheckoutPaymentIntent`, `CaptureStatus`, `RefundStatus`, `CardBrand` |
| Typed error classes | `PayPalServerSdk.Errors` | `CreateOrderError`, `CaptureOrderError`, `RefundCapturedPaymentError`, `CreatePaymentTokenError` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` | catch type `[src]` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` | error-body fallback `[src]` |
| OAuth2 credentials | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | `OAuth2ClientCredentials` `[src]` |
| `ServerEnvironment`, `DefaultOptions` | `PayPalServerSdk.Servers` | environment + base-URL override `[src]` |

### 2.2 Operations

Legend: `!req` = C# `required`; `T?` = nullable/optional; field shown as `CSharpName (wire_name): Type`.
"Must-pass-explicitly" params are nullable with **no** C# default — you must pass `null` to skip them.

| Op (controller.method) | Signature (params in order) | Request model + key fields | Response + fields to read | Error case + accessors | Map page |
|---|---|---|---|---|---|
| `Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 5 leading nullables must-pass-explicitly; `body` **non-null required** | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `Payer?`, `ApplicationContext?` | `Order`: `Id (id): string?`, `Status (status): OrderStatus?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `PaymentSource (payment_source): PaymentSourceResponse?` | Case A `SdkException<CreateOrderError>` · `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` [fallback] | operations/Orders.md; records-1-Ac-Pa.md |
| `Orders.CaptureOrder` | `CaptureOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderCaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — for card capture pass `body: null` | `OrderCaptureRequest?` (optional; `PaymentSource (payment_source): OrderCaptureRequestPaymentSource?`) — pass `null` for a plain capture | `Order` (same as above). **Capture id/status:** `order.PurchaseUnits[0].Payments.Captures[0]` → `Id (id): string?`, `Status (status): CaptureStatus?` (see `OrdersCapture`) | Case A `SdkException<CaptureOrderError>` · `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` [fallback] | operations/Orders.md; records-1-Ac-Pa.md (`OrdersCapture`, `PurchaseUnit`, `PaymentCollection`) |
| `Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — **full refund: `body: null`** (empty payload) | `RefundRequest?`: all fields optional — `Amount (amount): Money?` (omit for full refund), `CustomId?`, `InvoiceId?`, `NoteToPayer?` | `Refund`: `Id (id): string?`, `Status (status): RefundStatus?`, `Amount (amount): Money?` | Case A `SdkException<RefundCapturedPaymentError>` · `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] | operations/Payments.md; records-2-Pa-Ve.md |
| `Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenRequest`: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`, `Customer (customer): Customer?` | `PaymentTokenResponse`: **vault id = `Id (id): string?`**; safe descriptor at `PaymentSource.Card` (`CardPaymentTokenEntity`) → `LastDigits (last_digits): string?`, `Brand (brand): CardBrand?`, `Expiry (expiry): string?` | Case A `SdkException<CreatePaymentTokenError>` · `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError` [fallback] | operations/Vault.md; records-2-Pa-Ve.md |
| `Vault.CreateSetupToken` (alt. two-step) | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `SetupTokenRequest`: `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req`, `Customer?` | `SetupTokenResponse`: `Id (id): string?` (setup-token id → feed into `CreatePaymentToken` via `PaymentTokenRequestPaymentSource.Token = VaultTokenRequest{ Id=setupTokenId, Type=SetupToken }`) | Case A `SdkException<CreateSetupTokenError>` · `TryGetError1(out Error1)` [400,403,422,500] · `TryGetRawError` [fallback] | operations/Vault.md; records-2-Pa-Ve.md |
| `Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | (path id only) | `void` (Task) | Case A `SdkException<DeletePaymentTokenError>` · `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` [fallback] | operations/Vault.md |

### 2.3 Request-body construction

**Raw-card create order (capability 2)** — `OrderRequest`:
```
Intent = CheckoutPaymentIntent.Capture
PurchaseUnits = [ new PurchaseUnitRequest {
    Amount = new AmountWithBreakdown { CurrencyCode = "USD", Value = "<catalog total>" } } ]   // both !req, strings
PaymentSource = new PaymentSource {
    Card = new CardRequest {
        Number = "4111111111111111",     // wire number; 13-19 digits [src regex ^[0-9]{13,19}$]
        Expiry = "2027-11",              // wire expiry; format "YYYY-MM" [src regex ^[0-9]{4}-(0[1-9]|1[0-2])$], length 7
        SecurityCode = "123",            // wire security_code; 3-4 digits [src ^[0-9]{3,4}$]
        Name = "<cardholder>",           // 1-300 chars [src]
        BillingAddress = new Address {   // Address: AddressLine1/2, AdminArea2 (city), AdminArea1 (state),
            CountryCode = "US", ... } } } // PostalCode, CountryCode (country_code) !req — the ONLY required Address field
```
`CardRequest` fields (records-1-Ac-Pa.md): `Name (name)`, `Number (number)`, `Expiry (expiry)`,
`SecurityCode (security_code)`, `BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?`,
`Attributes (attributes): CardAttributes?`, `StoredCredential (stored_credential): CardStoredCredential?`, …
(all nullable). `Address` (records-1-Ac-Pa.md): only `CountryCode (country_code): string !req` is required.

**Vaulted-card create order (capability 3)** — identical `OrderRequest`, but the `CardRequest` carries **no**
raw PAN; set only `VaultId`:
```
PaymentSource = new PaymentSource { Card = new CardRequest { VaultId = "<vault id from capability 6>" } }
```
(Optionally add `StoredCredential = new CardStoredCredential { PaymentInitiator = PaymentInitiator.Merchant,
PaymentType = StoredPaymentSourcePaymentType.Unscheduled }` for merchant-initiated stored-credential
semantics — records-1-Ac-Pa.md `CardStoredCredential`; optional, not required to charge.)

**Full refund (capability 4):** pass `body: null` (the map notes "for a full refund, include an empty
payload"). Do **not** construct an `Amount`.

**Vault a card standalone (capability 6)** — `PaymentTokenRequest`:
```
PaymentSource = new PaymentTokenRequestPaymentSource {
    Card = new PaymentTokenRequestCard {
        Number = "4111111111111111", Expiry = "2027-11", SecurityCode = "123",
        Name = "<cardholder>", BillingAddress = new Address { CountryCode = "US", ... } } }
Customer = new Customer { Id = "<optional existing customer id>" }   // optional; both fields optional
```
`PaymentTokenRequestCard` (records-2-Pa-Ve.md): `Name`, `Number`, `Expiry`, `SecurityCode`,
`Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?` — all nullable.
Read back the **safe descriptor** from `response.PaymentSource.Card` (`CardPaymentTokenEntity`,
records-1-Ac-Pa.md): `LastDigits (last_digits)`, `Brand (brand): CardBrand?`, `Expiry (expiry)`,
`BillingAddress`, `Type (type): CardType?` — **never** a full PAN/CVV (not present in the response type).

### 2.4 Capability 7 — vault-id type match (confirmed from the map, no live traffic needed)

- `CreatePaymentToken` returns `PaymentTokenResponse.Id : string?` (records-2-Pa-Ve.md).
- Pay-with-vault consumes `CardRequest.VaultId : string?` (records-1-Ac-Pa.md).
- **Both are plain `string`.** The value produced by vaulting is the exact type the pay-with-vault
  operation expects — pass `PaymentTokenResponse.Id` straight into `CardRequest.VaultId`. No wrapper type,
  no conversion.

### 2.5 Idempotency (capability 5) — exact parameter names

All create/capture/refund operations expose an idempotency key as a **method parameter** (the
`PayPal-Request-Id` header), not a body field. Supply a stable per-intent key (e.g. per order/refund
attempt) so a double-click replays instead of re-charging:

| Operation | Parameter name | Position in signature |
|---|---|---|
| `Orders.CreateOrder` | `payPalRequestId` | 2nd param |
| `Orders.CaptureOrder` | `payPalRequestId` | 3rd param |
| `Payments.RefundCapturedPayment` | `payPalRequestId` | 3rd param |
| `Vault.CreatePaymentToken` | `payPalRequestId` | 1st param |
| `Vault.CreateSetupToken` | `payPalRequestId` | 1st param |

Call with named args to avoid mis-binding the many nullable positional params, e.g.
`await client.Orders.CreateOrder(payPalMockResponse: null, payPalRequestId: key, payPalPartnerAttributionId: null, payPalClientMetadataId: null, payPalAuthAssertion: null, body: orderRequest, ct: ct)`.
Whether the SDK's *retry* layer re-sends a non-idempotent POST on transport failure is a separate hazard — see trap ⚠-R below; the `payPalRequestId` key is what makes such a replay safe.

### 2.6 Enums needed (`PayPalServerSdk.Models.Enums` — `StringEnum<T>`, write the C# member, not the wire value)

| Enum | Members used (`CSharpMember (WIRE)`) | Used for |
|---|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` | `OrderRequest.Intent` — use `Capture` |
| `OrderStatus` | `Created (CREATED)`, `Approved (APPROVED)`, `Completed (COMPLETED)`, `Voided (VOIDED)`, `Saved (SAVED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` | branch on `Order.Status` (see §1 note) |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `Pending (PENDING)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Refunded (REFUNDED)`, `Failed (FAILED)` | capture success = `Completed` |
| `RefundStatus` | `Completed (COMPLETED)`, `Pending (PENDING)`, `Failed (FAILED)`, `Cancelled (CANCELLED)` | refund success = `Completed` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, … (30 members) | safe descriptor display |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` | optional `CardStoredCredential` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` | optional `CardStoredCredential` |
| `PaymentTokenStatus` | `Created (CREATED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` | `SetupTokenResponse.Status` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` | two-step vault (`VaultTokenRequest.Type`) |

Source: map/models/enums.md.

### 2.7 Error body — reading status + payload

Typed accessor payloads are ordinary records (records-1-Ac-Pa.md):
- Orders/Payments ops → `TryGetError(out Error)`. `Error`: `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`, `Links?`. `ErrorDetails`: `Field?`, `Value?`, `Location? = "body"`, `Issue (issue): string !req`, `Description?`.
- Vault ops → `TryGetError1(out Error1)` (note the **`1`** suffix on both accessor and payload type). `Error1` mirrors `Error` with `ErrorDetails1`/`ErrorLinkDescription`.
- Fallback / uncovered statuses → `TryGetRawError(out RawError)` (`RawError.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`). Payments ops additionally expose `TryGetNoContent(out RawError)` for `[500]`.

### 2.8 Client construction, auth, environment, base-URL override

**Auth scheme:** OAuth2 **Client Credentials** (client id + secret; the SDK fetches the bearer token from
`/v1/oauth2/token` under the hood `[src]`). Set on options **before** constructing the client:
```
using PayPalServerSdk;
using PayPalServerSdk.Servers;                                   // ServerEnvironment
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials; // OAuth2ClientCredentials

var options = new PayPalServerSdkClientOptions {
    Environment = ServerEnvironment.Sandbox,                     // only member is Sandbox [src]
    Oauth2 = new OAuth2ClientCredentials {
        ClientId = cfg["PayPal:ClientId"],                       // required (init) [src]
        ClientSecret = cfg["PayPal:ClientSecret"],               // required (init) [src]
        // Scope = ... (optional) [src]
    },
    // Custom base-URL override (optional) — default is https://api-m.sandbox.paypal.com [src]:
    // Server = new ServerOptions { Default = new DefaultOptions {
    //     Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = cfg["PayPal:BaseUrl"] } } },
};
var client = new PayPalServerSdkClient(httpClient, options);     // ctor: (HttpClient, options)
```
- Sole constructor: `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` (sdk-map.md).
- Base-URL override path `[src]`: `options.Server.Default.Sandbox.BaseUrl` (`ServerOptions` in `PayPalServerSdk`; `DefaultOptions` + nested `DefaultOptions.SandboxOptions` in `PayPalServerSdk.Servers`). Default sandbox host `https://api-m.sandbox.paypal.com`.
- DI: `services.AddPayPalServerSdkClient(o => { /* set Environment, Oauth2, Server on o */ });` (sdk-map.md, `ServiceCollectionExtensions.cs`). Confirm exact lifetime/HttpClient ownership against `dotnet-client-initialization`.
- Sandbox test card (per brief): Visa `4111111111111111`, any future `YYYY-MM` expiry, any CVC/name/US billing address.

---

## 3. Trap notes (load the named skill at the step where each bites — do not treat these as resolved)

- ⚠ Step 0 (client & DI registration) — the `HttpClient`/handler pipeline the client wraps has lifetime rules a constructor signature can't show, and getting them wrong causes socket exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before wiring the client into DI.
- ⚠-R Step 0 (retry/resilience) — the SDK's retry options do **not** bound a whole call and interact with **which verbs replay on a transport failure**; a `POST` (create-order/capture/refund) can execute more than once regardless of `HttpMethodsToRetry`, which is exactly why the `payPalRequestId` idempotency key (§2.5) matters. What `Timeout` actually bounds is also not what the name suggests. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts or the base URL.
- ⚠ Step 0 (auth) — whether credentials must be set before construction vs in the DI callback, and how the bearer token is cached/refreshed, are not visible in the options shape. **MUST load `dotnet-authentication`** before wiring `Oauth2`.
- ⚠ Steps 2/6 (building requests) — enums here are `StringEnum<T>` (not C# enums) and are built via the static member or `.FromValue("WIRE")`; JSON fields the SDK doesn't model are dropped on (de)serialize. **MUST load `dotnet-models`** before constructing payloads.
- ⚠ Steps 2-6 (calling) — many params are nullable-with-no-default and mis-bind in a positional call; call with named arguments (`payPalRequestId:`, `body:`, `ct:`). **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 2-6 (error boundary) — Orders/Payments are Case A `TryGetError`, Vault is Case A `TryGetError1`; `TryGetRawError` is not a catch-all on the typed error, and no operation has a no-throw `…Result` variant. **MUST load `dotnet-error-handling`** before writing any try/catch (see also the two `JsonException` rows in Required Reading).
- ⚠ Testing — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Client construction, HttpClient lifetime, DI registration (step 0) |
| `dotnet-authentication` | OAuth2 client-credentials wiring, token caching (step 0) |
| `dotnet-configuration-resilience` | Retries/timeouts, base-URL override, what actually replays (step 0) |
| `dotnet-calling-endpoints` | Named-argument calls, required vs optional params (steps 2-6) |
| `dotnet-models` | Building request models, `StringEnum<T>`, wire vs C# names (steps 2,3,6) |
| `dotnet-error-handling` | The try/catch boundary around every SDK call (steps 2-6) |
| `dotnet-testing` | Stubbing the SDK at the HttpClient seam |

**Two mandatory `System.Text.Json.JsonException` hazards for the error boundary** — it reaches the boundary
from two directions that need opposite handling:
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a
  5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something
  that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- The integration lives in `src/PublicApi` (from the brief); the exact call sites/endpoints are the main agent's to place.
- Currency is USD and the amount comes from catalog prices, formatted as a decimal string for `Money.Value`/`AmountWithBreakdown.Value` (both are `string`, not numeric).
- A single purchase unit per order (multi-PU not in scope); capture/refund read index `[0]`.
- Idempotency keys are generated and persisted by the app per payment/refund intent (the SDK only forwards the value as `payPalRequestId`).
- Client id/secret (and optional base-URL override) are supplied via configuration, not hardcoded.

**Blockers**
- None. Every in-scope capability is exposed by the SDK: raw-card pay = `CreateOrder`(+`CaptureOrder`), vaulted-card pay = same with `CardRequest.VaultId`, full refund = `RefundCapturedPayment`, standalone vault = `CreatePaymentToken` (or `CreateSetupToken`→`CreatePaymentToken`), delete = `DeletePaymentToken`, idempotency = `payPalRequestId` on all writes.

**Open/UNVERIFIED (defensive-coding directive, not a blocker)**
- `CreateOrder`-with-card auto-capture vs. explicit `CaptureOrder` — resolved defensively in §1 (branch on `order.Status`); only live sandbox traffic confirms which path a given account takes. Label: UNVERIFIED.
