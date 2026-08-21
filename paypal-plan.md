# PayPal .NET SDK integration plan — eShopOnWeb (SANDBOX, direct card + vault)

SDK: APIMatic-generated PayPal .NET SDK — package `AsadAli.Checkout.Sdk`, root namespace
`PayPalServerSdk`, client `PayPalServerSdkClient`. Map provenance: tag `v1.0.1`, source commit
`9653d18`. Install version-less: `dotnet add package AsadAli.Checkout.Sdk`.

All facts below are grounded in the bundled SDK map (page cited per row) or, where the map fell
short (base-URL override + OAuth token routing, capability 1), in the SDK source that the map
names. Target framework of the SDK is `netstandard2.0`.

---

## 1. Scope & sequence

1. **Client & DI setup** — register a long-lived `HttpClient` + `PayPalServerSdkClient` with OAuth2
   client-credentials, `ServerEnvironment.Sandbox`, and (optional) verbatim base-URL override.
   Uses: `AddPayPalServerSdkClient` / `new PayPalServerSdkClient(...)`.
2. **Authorize order (direct card, hold)** — `client.Orders.CreateOrder` (intent AUTHORIZE +
   `payment_source.card`), read the authorization from the response; explicit variant
   `client.Orders.AuthorizeOrder`. Vaulted-card variant sets `card.vault_id`.
3. **Capture at fulfilment** — `client.Payments.CaptureAuthorizedPayment`.
4. **Re-authorize stale auth** — `client.Payments.ReauthorizePayment`.
5. **Void** — `client.Payments.VoidPayment`.
6. **Refund** — `client.Payments.RefundCapturedPayment`.
7. **Idempotency** — `payPalRequestId` param (→ `PayPal-Request-Id` header) on every write above.
8. **Vault a card / delete** — `client.Vault.CreatePaymentToken`, `client.Vault.DeletePaymentToken`.
9. **Reconciliation** — `client.TransactionSearch.SearchTransactions` (paged over the whole range).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one
> from that type's own map row, never from where a neighbouring type sits. Enums, unions, auth,
> server and client-config types are spread across different child namespaces, and two types
> configured side by side in the same options object routinely live in different ones. Dropping a
> type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build
> breaks.

### 2.0 Namespaces (using-directives)

| Type group | Namespace |
|---|---|
| Client, options, `ServerOptions` | `PayPalServerSdk` |
| Controllers (`client.Orders` etc.) | `PayPalServerSdk.Api` |
| Records (all request/response models) | `PayPalServerSdk.Models` |
| Enums (`CheckoutPaymentIntent`, `AuthorizationStatus`, …) | `PayPalServerSdk.Models.Enums` |
| Typed error classes (`AuthorizeOrderError`, `Error`, `Error1`, `DefaultError`, `RawError` as `TryGetRawError` out-type is in ErrorResponse) | `PayPalServerSdk.Errors` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError` | `PayPalServerSdk.Core.ErrorResponse` |
| `ServerEnvironment`, `DefaultOptions` (`.Sandbox.BaseUrl`) | `PayPalServerSdk.Servers` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `RequestOptions` | `PayPalServerSdk.Core` |

C# does NOT import child namespaces transitively — add a separate `using` for each. (sdk-map.md
"Namespaces"; `SdkException`/`RawError`/`OAuth2ClientCredentials` namespaces confirmed from SDK source.)

### 2.1 Capability 1 — Client construction, auth, environment, base-URL override

**Client ctor** (root ns): `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`. (sdk-map.md "Getting a client")

**Options** (`PayPalServerSdkClientOptions`, root ns; confirmed source `PayPalServerSdkClientOptions.cs`):

| Property | Type | Use |
|---|---|---|
| `Environment` | `ServerEnvironment` | set `ServerEnvironment.Sandbox` (the ONLY member; also the default `ServerEnvironment.Default()`). ns `PayPalServerSdk.Servers` |
| `Oauth2` | `OAuth2ClientCredentials?` | the client-credentials (see below) |
| `Oauth2TokenStrategy` | `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | leave null → SDK builds the default client-credentials strategy |
| `Server` | `ServerOptions` | base-URL override (see below) |
| `Retry` | `RetryOptions` | resilience (see trap note) |
| `Logging` | `LoggingOptions` | logging |

**Auth credentials** — `OAuth2ClientCredentials` (ns `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`; confirmed source):
```
new OAuth2ClientCredentials { ClientId = "<id>", ClientSecret = "<secret>", Scope = null }
```
`ClientId` and `ClientSecret` are `required string` (init-only); `Scope` is optional `string?`.
Set `options.Oauth2` to this. (Map "Servers & auth"; shape confirmed source `OAuth2ClientCredentials.cs`.)

**Verbatim base-URL override** (confirmed source `ServerOptions.cs`, `Servers/DefaultOptions.cs`):
```
options.Server.Default.Sandbox.BaseUrl = "<custom absolute base url>";
```
`options.Server` is `ServerOptions` (root ns) → `.Default` is `DefaultOptions` (ns `PayPalServerSdk.Servers`)
→ `.Sandbox` is `DefaultOptions.SandboxOptions` → `.BaseUrl` is a plain `string`, default
`"https://api-m.sandbox.paypal.com"`. The value is used verbatim (no template substitution for the host).

**Does the custom BaseUrl reach the OAuth token/credential request? YES — source-confirmed, not a
guess.** `AuthSchemes.cs` builds the token strategy as
`OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), rawClient)`,
and `server.Default(path)` resolves through the SAME `DefaultOptions.Resolve` → `Sandbox.BaseUrl`
that every operation URL uses. So overriding `options.Server.Default.Sandbox.BaseUrl` also redirects
the `POST /v1/oauth2/token` credential call. No separate token-host setting exists. (Confirmed source:
`AuthSchemes.cs`, `Server.cs`, `Servers/DefaultOptions.cs`.)

**DI** (`ServiceCollectionExtensions.cs`): `services.AddPayPalServerSdkClient(o => { /* set Environment, Oauth2, Server on o */ });`
HttpClient ownership/lifetime → **trap, MUST load `dotnet-client-initialization`** (see §3).

### 2.2 Capabilities 2–9 — operations

| # | Controller.Method (signature verbatim) | Request model + key fields (`C# (wire): type, req?`) | Returns / envelope → fields you read | Error case + accessors | Map page |
|---|---|---|---|---|---|
| 2a | `client.Orders.CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderRequest { Intent (intent): CheckoutPaymentIntent !req = Authorize; PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req; PaymentSource (payment_source): PaymentSource? }`. `PurchaseUnitRequest { Amount (amount): AmountWithBreakdown !req }`. `AmountWithBreakdown { CurrencyCode (currency_code): string !req; Value (value): string !req; Breakdown? }`. Direct card: `PaymentSource { Card (card): CardRequest? }`; `CardRequest { Name; Number (number); Expiry (expiry); SecurityCode (security_code); BillingAddress (billing_address): Address; VaultId (vault_id): string? }`. `Address { AddressLine1; AddressLine2; AdminArea2; AdminArea1; PostalCode; CountryCode (country_code): string !req }` | `Order`. Auth id/status path: `order.PurchaseUnits[0].Payments (PaymentCollection).Authorizations[0] (AuthorizationWithAdditionalData)` → `.Id` (authorization id), `.Status` (AuthorizationStatus). Also `order.Status` (OrderStatus), `order.Id` (order id) | Case A `SdkException<CreateOrderError>`: `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` | operations/Orders.md; records-1 (`OrderRequest`,`PurchaseUnitRequest`,`AmountWithBreakdown`,`CardRequest`,`Address`,`Order`); records-2 (`PaymentSource`,`PaymentCollection`); enums.md |
| 2b | `client.Orders.AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | (create-then-authorize variant, order created WITHOUT payment_source) `OrderAuthorizeRequest { PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource? }`; `OrderAuthorizeRequestPaymentSource { Card (card): CardRequest?; Token; Paypal; … }` | `OrderAuthorizeResponse`. Same auth path: `.PurchaseUnits[0].Payments.Authorizations[0].Id/.Status`; `.Status` (OrderStatus); `.Id` | Case A `SdkException<AuthorizeOrderError>`: `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` | operations/Orders.md; records-1 (`OrderAuthorizeRequest`,`OrderAuthorizeRequestPaymentSource`,`OrderAuthorizeResponse`) |
| 2c | Vaulted-card variant of 2a/2b | Same models, but `CardRequest { VaultId (vault_id) = "<PaymentTokenResponse.Id>" }` INSTEAD of raw Number/Expiry/SecurityCode. Ties to capability 8. | as 2a/2b | as 2a/2b | records-1 (`CardRequest`) |
| 3 | `client.Payments.CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | Full capture: `body = null`. Partial/final: `CaptureRequest { Amount (amount): Money?; FinalCapture (final_capture): bool? = false; InvoiceId; NoteToPayer; SoftDescriptor }`. `Money { CurrencyCode (currency_code): string !req; Value (value): string !req }` | `CapturedPayment` → `.Id` (capture id), `.Status` (CaptureStatus), `.Amount` (Money captured). **Breakdown** `.SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown` → `.GrossAmount (gross_amount): Money !req` (captured gross), `.PaypalFee (paypal_fee): Money?` (PayPal fee), `.NetAmount (net_amount): Money?` (net proceeds to merchant), `.ReceivableAmount`, `.ExchangeRate` | Case A `SdkException<CaptureAuthorizedPaymentError>`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | operations/Payments.md; records-1 (`CapturedPayment`,`CaptureRequest`,`Money`); records-2 (`SellerReceivableBreakdown`) |
| 4 | `client.Payments.ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ReauthorizeRequest { Amount (amount): Money? }` (only `amount` supported) | `PaymentAuthorization` → `.Id`, `.Status` (AuthorizationStatus), `.Amount`, `.ExpirationTime` (new 3-day honor period) | Case A `SdkException<ReauthorizePaymentError>`: `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. "No longer renewable" surfaces as a **422** (or 404) business error — read `Error.Details[].Issue`/`Error.Name`, fall back to `Error.Message`, report to operator (exact issue string UNVERIFIED — live-wire) | operations/Payments.md; records-2 (`ReauthorizeRequest`,`PaymentAuthorization`); records-1 (`Error`,`ErrorDetails`) |
| 5 | `client.Payments.VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | none (no body) | `PaymentAuthorization`. **Success = call returns without throwing.** With default `prefer="return=minimal"` PayPal may return no representation; to read `.Status == AuthorizationStatus.Voided` inline pass `prefer:"return=representation"`, else re-fetch via `GetAuthorizedPayment` (exact 204/empty-body behaviour UNVERIFIED — live-wire) | Case A `SdkException<VoidPaymentError>`: `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | operations/Payments.md; records-2 (`PaymentAuthorization`); enums.md (`AuthorizationStatus`) |
| 6 | `client.Payments.RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | Full refund: `body = null` (empty payload). Partial: `RefundRequest { Amount (amount): Money?; CustomId; InvoiceId; NoteToPayer; PaymentInstruction }` with explicit `Amount = Money{CurrencyCode, Value}` | `Refund` → `.Id` (refund id), `.Status` (RefundStatus), `.Amount`. Breakdown `.SellerPayableBreakdown` → `.GrossAmount`, `.PaypalFee`, `.NetAmount`, `.TotalRefundedAmount (total_refunded_amount): Money?` (cumulative — use to prove refunds never exceed capture) | Case A `SdkException<RefundCapturedPaymentError>`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. **Over-refund is validated PayPal-side, NOT by the SDK** — exceeding the remaining capturable amount throws (422); read `Error.Details[].Issue`, fall back to `Error.Message` (exact issue string UNVERIFIED — live-wire) | operations/Payments.md; records-2 (`RefundRequest`,`Refund`,`SellerPayableBreakdown`); records-1 (`Money`) |
| 8a | `client.Vault.CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenRequest { Customer (customer): Customer?; PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req }`. `PaymentTokenRequestPaymentSource { Card (card): PaymentTokenRequestCard? }`. `PaymentTokenRequestCard { Name; Number (number); Expiry (expiry); SecurityCode (security_code); Brand (brand): CardBrand?; BillingAddress (billing_address): Address? }`. `Customer { Id; MerchantCustomerId (merchant_customer_id) }` | `PaymentTokenResponse` → `.Id` = **vault id** (pass later as `CardRequest.VaultId`). SAFE description: `.PaymentSource.Card (CardPaymentTokenEntity)` → `.Brand` (CardBrand), `.LastDigits (last_digits)`, `.Expiry`, `.Name`, `.Type`. **No full PAN in the response.** | Case A `SdkException<CreatePaymentTokenError>`: `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError` | operations/Vault.md; records-2 (`PaymentTokenRequest`,`PaymentTokenRequestPaymentSource`,`PaymentTokenRequestCard`,`PaymentTokenResponse`,`PaymentTokenResponsePaymentSource`); records-1 (`CardPaymentTokenEntity`,`Customer`) |
| 8b | `client.Vault.DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | none — `id` = vault id | `void` (Task). Success = no throw (HTTP 204) | Case A `SdkException<DeletePaymentTokenError>`: `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` | operations/Vault.md |
| 9 | `client.TransactionSearch.SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `startDate`/`endDate` **required** ISO-8601 strings (wire `start_date`/`end_date`). `pageSize` (wire `page_size`) default 100, `page` default 1. The 8 middle params have NO C# default → **must pass explicitly (pass `null`)**, and use **named args** | `SearchResponse` → `.TransactionDetails: IReadOnlyList<TransactionDetails>`; per item `.TransactionInfo (TransactionInformation)` → `.TransactionId (transaction_id)`, `.TransactionAmount (transaction_amount): Money`, `.TransactionStatus (transaction_status): string`, `.TransactionInitiationDate`, `.FeeAmount`. **Pagination**: `.Page (page): int?`, `.TotalPages (total_pages): int?`, `.TotalItems (total_items): int?` — loop `page = 1..TotalPages` (same start/end + pageSize) to cover the WHOLE range | **Case B `SdkException<RawError>`** (the ONLY Case B op) — NO typed accessors: `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`, `ex.Error.ReadAsBytes()` | operations/TransactionSearch.md; records-2 (`SearchResponse`,`TransactionDetails`,`TransactionInformation`) |

**Capability 7 — Idempotency.** Mechanism: a nullable `string payPalRequestId` positional parameter
on each write op → sent as the `PayPal-Request-Id` HTTP header. It is a plain string param (not a
header dictionary). Present on: `CreateOrder`, `AuthorizeOrder`, `CaptureAuthorizedPayment`,
`ReauthorizePayment`, `RefundCapturedPayment`, `VoidPayment`, `Vault.CreatePaymentToken`,
`Vault.CreateSetupToken`. Pass a **stable, unique key per logical operation** (persist it with the
eShop order/fulfilment record) so a retried request is de-duplicated by PayPal instead of
double-charging/double-refunding. (operations/Orders.md, Payments.md, Vault.md — the `payPalRequestId`
param on each signature above.) This is load-bearing given the resilience trap in §3 (POST is
retried on transport failure).

### 2.3 Enum value tables (only those in scope) — ns `PayPalServerSdk.Models.Enums`

| Enum | Members (`C# (WIRE)`) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` — use `Authorize` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` | incl. `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)` … |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |

Enums are `StringEnum<T>` records, NOT C# enums — write the member (`CheckoutPaymentIntent.Authorize`)
or `CheckoutPaymentIntent.FromValue("AUTHORIZE")`. (enums.md)

### 2.4 3DS / challenge detection (capability 2) — STOP, do not build an approval round-trip

For direct card and vaulted card, a challenge/3DS-approval requirement surfaces on the create/
authorize **response** as an order state that is not the straight-through authorized outcome. The
map-grounded signal is `OrderStatus.PayerActionRequired` (`PAYER_ACTION_REQUIRED`) together with a
HATEOAS entry in `response.Links` (`LinkDescription { Href, Rel, Method }`) pointing at buyer
approval. Card authentication detail also appears at
`response.PaymentSource.Card.AuthenticationResult` (`AuthenticationResponse { LiabilityShift,
ThreeDSecure }`) — records-1 `CardResponse`/`AuthenticationResponse`, `ThreeDSecureAuthenticationResponse`.

**Directive:** after CreateOrder/AuthorizeOrder, if `Status == OrderStatus.PayerActionRequired`
(or any status other than one carrying a usable authorization in `PurchaseUnits[].Payments.Authorizations`),
do **not** attempt an approval flow — extract best-effort the approval `Links` entry and the
`three_d_secure` result if present, log them, and report the order as "requires browser approval —
stopped." The exact `Rel` string of the approval link is **UNVERIFIED** (only live traffic confirms
it; commonly `"payer-action"`/`"approve"`) — match defensively (case-insensitive contains) and fall
back to reporting the raw status + links. (operations/Orders.md; records-1 `Order`,`OrderAuthorizeResponse`,`LinkDescription`; enums.md `OrderStatus`.)

Note on card `Expiry`: the map types it as plain `string`; PayPal's wire format for card expiry
(`YYYY-MM`) is **UNVERIFIED** here (not asserted by map/source) — validate/format defensively before send.

---

## 3. Trap notes (attach to the step where each bites)

⚠ **Step 1 (client & DI)** — the `HttpClient`/handler pipeline must be long-lived and reused (via
`IHttpClientFactory`), not rebuilt per request; the SDK client wrapper's lifetime is a separate
decision. The signature won't tell you which. **MUST load `dotnet-client-initialization`** before
wiring `new PayPalServerSdkClient(...)` / `AddPayPalServerSdkClient`.

⚠ **Step 1 (auth)** — where/when to set `Oauth2` credentials relative to client construction, how to
load the secret from configuration rather than hardcode, and how token acquisition/refresh is
handled. **MUST load `dotnet-authentication`** before setting credentials, and when any call returns
401/403.

⚠ **Step 1 (resilience) — bears on idempotency (capability 7).** The SDK's `RetryOptions.HttpMethodsToRetry`
gates only the **status-code** retry trigger, but a **transport failure** is retried on **every**
verb including POST — so a create-order/authorize/capture/refund can execute more than once on a
flaky connection. What that means for whether a failed write can be safely re-sent, what `Timeout`
actually bounds, and what you must still wire yourself are exactly what the option names hide.
**MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts/base-URL/pagination.
(Consequence: the `payPalRequestId` idempotency key in §2.2 capability 7 is the mitigation — set it
on every write.)

⚠ **Step 2–9 (models)** — enums are `StringEnum<T>` not C# enums; `required` members must be set in
the initializer; union/`AnyOf` fields (none in the exact paths above, but present on neighbouring
models) are built via factories and read via `TryGet…`; unmodeled JSON is dropped on deserialize.
**MUST load `dotnet-models`** before constructing any request payload or mapping responses to eShop types.

⚠ **Step 9 (calling SearchTransactions)** — the 8 middle nullable params have no C# default and
mis-bind in a positional call; call with **named arguments**. **MUST load `dotnet-calling-endpoints`**
before the first call. Same skill covers async/`ct` cancellation.

⚠ **Steps 2–9 (error boundary)** — see REQUIRED READING; the error mechanics (Case A/B accessors,
the two `JsonException` directions) are not restated here on purpose. **MUST load `dotnet-error-handling`**.

⚠ **Testing** — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`**
before stubbing the SDK.

---

## 4. REQUIRED READING — load BEFORE implementation starts

These `dotnet-*` companion skills carry the usage layer (defaults, worked examples, the parts a
one-line note cannot). The contract sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, `HttpClient` lifetime, DI registration |
| `dotnet-authentication` | Step 1 — OAuth2 credentials wiring, secrets from config, 401/403 |
| `dotnet-configuration-resilience` | Step 1 — retries/timeouts, base-URL selection, pagination (POST-retry hazard ↔ idempotency) |
| `dotnet-models` | Steps 2–9 — request models, required members, `StringEnum<T>`, unions, dropped fields |
| `dotnet-calling-endpoints` | Steps 2–9 — named-arg calls, async, cancellation (`ct`) |
| `dotnet-error-handling` | Steps 2–9 — the error/exception boundary (mandatory; every integration writes one) |
| `dotnet-testing` | Tests — the `HttpClient` seam |

**Error-boundary hazards — `System.Text.Json.JsonException` reaches the boundary from two directions,
handled oppositely:**
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException`
  from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it
  escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- Plan written to the dictated path `C:\claude-runs\t3v7ali-task3-plugin-opus48high-022\repo\paypal-plan.md`.
- SANDBOX only; the single `ServerEnvironment` member is `Sandbox` (source-confirmed) — there is no
  Production/Live environment in this SDK build, so a live cut-over would need a base-URL override
  (`options.Server.Default.Sandbox.BaseUrl`) rather than an environment switch. Flagging in case the
  integration must eventually target production.
- Currency comes from eShop config and is applied to `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode`;
  amounts are formatted to the cent as decimal strings in `.Value`.
- "Direct card, no browser" assumes the sandbox test Visa `4111 1111 1111 1111` is non-3DS and returns
  a straight-through authorization; the 3DS-detection directive (§2.4) is the fallback if it does not.

**Genuine gaps / SDK-not-covered (map + source)**
- **3DS approval link `Rel` string** (capability 2): the exact HATEOAS `Rel` value signalling browser
  approval is not fixed by map or source — only live traffic confirms it. Handled by the defensive
  directive in §2.4 (UNVERIFIED).
- **Business-rule error `Issue` strings** (capabilities 4 & 6): the precise `ErrorDetails.Issue` codes
  for "authorization no longer reauthorizable" and "refund exceeds capture" are not enumerated in the
  map/source (they are live server responses). Handled by reading `Error.Details[].Issue`/`Error.Name`
  with a fall-back to `Error.Message` (UNVERIFIED).
- **Void 204/empty-body behaviour** (capability 5) and **card `Expiry` wire format** (capability 2):
  not pinned by map/source; treated defensively (re-fetch / validate), labelled UNVERIFIED.
- **Reporting lag** (capability 9): the map's own operation note states executed transactions take
  **up to three hours** to appear in `SearchTransactions`, and if any optional query param is
  supplied the `ending_balance` field is empty. Reconciliation must tolerate this lag (do not treat a
  just-created transaction's absence as a mismatch).

**Browser/approval steps that cannot be avoided** — for **direct raw-card** and **vaulted-card**
payments there is no mandatory browser step in the happy path (no `experience_context`/redirect is
required). The ONLY forced approval is the conditional **3DS challenge** (capability 2), which the
plan explicitly stops-and-reports on rather than automating. Vaulting a raw card via
`CreatePaymentToken` likewise needs no approval for a non-3DS card (the setup-token + approval flow
`CreateSetupToken` is NOT used here); whether a specific card triggers 3DS on vaulting is UNVERIFIED
(live), so apply the same stop-and-report guard.
