# PayPal .NET SDK Integration Plan — eShopOnWeb PublicApi

SDK: `AsadAli.Checkout.Sdk` (root namespace `PayPalServerSdk`), map-grounded this session (map + one scoped
source clone on real gaps only — clone not referenced below).

## 1. Scope & sequence

0. **Client & auth wiring** (PublicApi startup) — register `PayPalServerSdkClient` via DI, bind
   `PayPal:ClientId/ClientSecret/Environment/Currency/BaseUrl` from configuration. Uses: `OAuth2ClientCredentials`,
   `ServerEnvironment`, `ServerOptions`.
1. **Authorize on "pay" endpoint** (raw card OR vaulted token, no redirect) — `Orders.CreateOrder` only
   (single-step create+authorize). `Orders.AuthorizeOrder` is **not used** — see row note.
2. **Fulfil ("capture") endpoint** — `Payments.GetAuthorizedPayment` (staleness check) →
   `Payments.ReauthorizePayment` (only if stale but in-window) → `Payments.CaptureAuthorizedPayment`.
3. **Cancel-before-fulfilment endpoint** — `Payments.VoidPayment`.
4. **Refund-after-fulfilment endpoint** — `Payments.GetCapturedPayment` (compute remaining refundable) →
   `Payments.RefundCapturedPayment`.
5. **Save-card endpoint** — `Vault.CreatePaymentToken`; **list saved cards** — `Vault.ListCustomerPaymentTokens`;
   **detach** — `Vault.DeletePaymentToken`; (`Vault.GetPaymentToken` for single-token reads.)
6. **Reconciliation report endpoint** — `TransactionSearch.SearchTransactions`, manually paginated.

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

Namespaces used below: records/enums-as-fields → `PayPalServerSdk.Models` (records) /
`PayPalServerSdk.Models.Enums` (enums); controllers → `PayPalServerSdk.Api`; typed errors →
`PayPalServerSdk.Errors`; `SdkException<T>`, `RawError`, `ApiError` → `PayPalServerSdk.Core.ErrorResponse` /
`PayPalServerSdk.Core.Exceptions`; `OAuth2ClientCredentials` →
`PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`; `ServerEnvironment` → `PayPalServerSdk.Servers`;
`ServerOptions`, `PayPalServerSdkClientOptions`, `PayPalServerSdkClient` → root `PayPalServerSdk`.

### 2.1 Client construction, auth, base-URL override (resolved facts)

| Fact | Detail | Source |
|---|---|---|
| Client ctor | `PayPalServerSdk.PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI | `services.AddPayPalServerSdkClient(o => { ... })` | `sdk-map.md` |
| Auth | `options.Oauth2 = new OAuth2ClientCredentials { ClientId = "...", ClientSecret = "...", Scope = null }` (namespace above). `Oauth2TokenStrategy` left `null` → SDK builds its own basic-auth client-credentials strategy internally. | `sdk-map.md` Servers&auth; `OAuth2ClientCredentials.cs`, `AuthSchemes.cs` |
| Environment | `options.Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox` — **`Sandbox` is the only member this generated SDK exposes** (no `Live`/`Production` member exists at all). | `sdk-map.md`; `ServerEnvironment.cs` |
| BaseUrl override | `options.Server.Default.Sandbox.BaseUrl = "<override>"` (`ServerOptions.Default: PayPalServerSdk.Servers.DefaultOptions`, `.Sandbox: DefaultOptions.SandboxOptions { BaseUrl }`, default `"https://api-m.sandbox.paypal.com"`). **Confirmed this base URL is used for the OAuth2 token request too**: `AuthSchemes.cs` builds the token-strategy URL as `server.Default("/v1/oauth2/token")` off the exact same `Server` instance resolved from `options.Server` — i.e. one override point governs every call including auth. | `ServerOptions.cs`, `DefaultOptions.cs`, `AuthSchemes.cs` (source; only opened because the map's Servers&auth section doesn't carry this override-affects-token detail) |
| Idempotency header | Every write op below takes a `payPalRequestId` C# param that the SDK sends as HTTP header `PayPal-Request-Id` (confirmed in `Api/Orders.cs`/`Api/Payments.cs` doc-comments + header-building code). Pass a fresh GUID per **new** logical operation; reuse the *same* value only when retrying the exact same logical request. | `Orders.cs`, `Payments.cs` |

### 2.2 Operations

| Op (`client.X.Y`) | Signature (must-pass-explicit params, in order) | Request model + fields | Response + fields read | Error case + accessors | Notes |
|---|---|---|---|---|---|
| `Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderRequest{ Intent(intent):CheckoutPaymentIntent !req = CheckoutPaymentIntent.Authorize, PurchaseUnits(purchase_units):IReadOnlyList<PurchaseUnitRequest> !req = [ new(){ Amount(amount):AmountWithBreakdown !req = new(){ CurrencyCode!req, Value!req } } ], PaymentSource(payment_source):PaymentSource?{ Card(card):CardRequest{ **raw**: Name?,Number?,Expiry?("YYYY-MM"),SecurityCode?,BillingAddress? — **or vaulted**: VaultId? = saved `PaymentTokenResponse.Id` } } }` | `Order{ Status(status):OrderStatus, PurchaseUnits(purchase_units)[0].Payments(payments):PaymentCollection?.Authorizations(authorizations)[0]:AuthorizationWithAdditionalData{ Id, Status:AuthorizationStatus, Amount, ExpirationTime }, Links:IReadOnlyList<LinkDescription> }` | `SdkException<CreateOrderError>` Case A — `TryGetError(out Error)`[400,401,422] · `TryGetRawError(out RawError)` fallback | **This one call is both create-order and authorize** when `payment_source` is supplied directly (card raw or vaulted) — `payPalRequestId` is *mandatory* per source doc for exactly this "single-step create order with payment source" shape. `AuthorizeOrder` is a **separate** two-step-flow operation for orders created *without* `payment_source` (buyer-approval redirect) — do not call it here. |
| — *(not called)* `Orders.AuthorizeOrder` | n/a | n/a | n/a | n/a | Only relevant to the buyer-redirect-approval flow (order created without `payment_source`, buyer approves via `rel:approve` link, then this finalizes). Out of scope — direct/no-redirect flow uses `CreateOrder` alone. `map/operations/Orders.md` |
| `Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentAuthorization{ Status:AuthorizationStatus, StatusDetails, Id, Amount, ExpirationTime(string?), CreateTime, UpdateTime }` | `SdkException<GetAuthorizedPaymentError>` Case A — `TryGetError(out Error)`[401,403,404] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` fallback | Call before capture to decide reauthorize-vs-capture. **`AuthorizationStatus` has no "Expired" member** (`Created,Captured,Denied,PartiallyCaptured,Voided,Pending`) — staleness must be computed by comparing `ExpirationTime` to now, not by reading `Status`. |
| `Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CaptureRequest{ Amount(amount):Money? — **set explicitly** to the authorization's `Amount` for a deterministic full capture, FinalCapture(final_capture):bool?=false — **set `true`** (no further captures planned), InvoiceId?, NoteToPayer?, SoftDescriptor? }` | `CapturedPayment{ Status:CaptureStatus, Id, Amount, FinalCapture, SellerReceivableBreakdown(seller_receivable_breakdown):SellerReceivableBreakdown{ GrossAmount(gross_amount):Money !req, PaypalFee(paypal_fee):Money?, NetAmount(net_amount):Money?, ReceivableAmount?, ExchangeRate?, PlatformFees? } }` | `SdkException<CaptureAuthorizedPaymentError>` Case A — `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` fallback | **Gross/fee/net = `SellerReceivableBreakdown.GrossAmount`/`.PaypalFee`/`.NetAmount`** — exact fields requested. A 422 here commonly signals a stale/expired authorization; the SDK does **not** enumerate the business error code (`ErrorDetails.Issue` is a free-text `string`, not a typed enum) — see UNVERIFIED row below. |
| `Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ReauthorizeRequest{ Amount(amount):Money? }` (omit to reauthorize the same amount) | `PaymentAuthorization{ Id, Status, ExpirationTime, ... }` — **always re-read `Id` from this response** rather than assuming it equals the original authorization id (not stated either way by the SDK) | `SdkException<ReauthorizePaymentError>` Case A — `TryGetError(out Error)`[400,401,403,404,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` fallback | **Reauthorization window (from the operation's own doc, `map/operations/Payments.md`)**: reauthorize only after the initial 3-day honor period expires, and only within 29 days of the *original* authorization date; multiple reauths allowed inside that window; **once 30 days have elapsed since the original authorization, you must call `Orders.CreateOrder` again (a brand-new authorization) — reauthorization is impossible.** See UNVERIFIED row for how to detect "impossible" vs "still eligible" from an error. |
| `Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentAuthorization{ Status → becomes Voided }` | `SdkException<VoidPaymentError>` Case A — `TryGetError(out Error)`[401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` fallback | Per operation doc: **cannot void an authorization that has been fully captured** (expect 409/422 in that case). |
| `Payments.GetCapturedPayment` | `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `CapturedPayment{ Status:CaptureStatus, Amount, SellerReceivableBreakdown }` | `SdkException<GetCapturedPaymentError>` Case A — `TryGetError(out Error)`[401,403,404] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` fallback | Use before refund to fetch `Amount` for the "remaining refundable" computation (see below — no single field carries it). |
| `Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `RefundRequest{ Amount(amount):Money? — **null = full refund of remaining amount; set explicit `Money` for partial**, CustomId?, InvoiceId?, NoteToPayer?, PaymentInstruction? }` | `Refund{ Status:RefundStatus(Cancelled/Failed/Pending/Completed), Id, Amount, SellerPayableBreakdown(seller_payable_breakdown):SellerPayableBreakdown{ GrossAmount, PaypalFee, NetAmount, TotalRefundedAmount, ... } }` | `SdkException<RefundCapturedPaymentError>` Case A — `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` fallback | **Refund id/status = `Refund.Id`/`Refund.Status`.** **No SDK model exposes "remaining refundable amount" directly** — neither `CapturedPayment` nor `Refund` has such a field. Compute it: `remaining = GetCapturedPayment(captureId).Amount − SellerPayableBreakdown.TotalRefundedAmount` (from the latest `Refund` response, or accumulate app-side), and treat `CapturedPayment.Status == CaptureStatus.Refunded` as fully refunded. **Idempotency**: `payPalRequestId` → `PayPal-Request-Id` header, PayPal stores the key for **45 days** (confirmed in source doc-comment). Derive the header value deterministically from your own idempotency key (e.g. hash of `captureId + amount + your key`) so a retried identical request reuses the same header value, while two distinct partial refunds (different amount/reason) get distinct values and are never conflated. |
| `Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenRequest{ Customer(customer):Customer?{ Id(id)?, MerchantCustomerId(merchant_customer_id)? — supply **your own** `MerchantCustomerId`; leave `Id` null on first save }, PaymentSource(payment_source):PaymentTokenRequestPaymentSource !req{ Card(card):PaymentTokenRequestCard{ Name?,Number?,Expiry?,SecurityCode?,Brand?,BillingAddress? } } }` | `PaymentTokenResponse{ Id(id) — **persist this**: it is the value later used as `CardRequest.VaultId`, Customer(customer):CustomerResponse{ Id — **PayPal-generated customer id; persist this too**, MerchantCustomerId }, PaymentSource(payment_source):PaymentTokenResponsePaymentSource{ Card(card):CardPaymentTokenEntity{ Name, LastDigits, Brand, Expiry, BillingAddress, VerificationStatus } } }` | `SdkException<CreatePaymentTokenError>` Case A — `TryGetError1(out Error1)`[400,403,404,422,500] · `TryGetRawError` fallback | **Safe-to-display fields = `LastDigits`/`Brand`/`Expiry`/`Name`** — the response model has **no raw card-number field at all**, so the raw PAN is never round-tripped back to your app. **Customer identity**: `Customer.Id`/`CustomerResponse.Id` is *PayPal-generated* ("The unique ID for a customer generated by PayPal" — source doc); `MerchantCustomerId` is *yours*, supplied to correlate. Persist the PayPal-generated `CustomerResponse.Id` — it is the `customerId` `ListCustomerPaymentTokens` requires. See UNVERIFIED row for the 3DS-during-vaulting gap. |
| `Vault.ListCustomerPaymentTokens` | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query: `customer_id`←`customerId`, `page_size`←`pageSize`, `page`←`page`, `total_required`←`totalRequired` | `CustomerVaultPaymentTokensResponse{ TotalItems?, TotalPages?, PaymentTokens(payment_tokens):IReadOnlyList<PaymentTokenResponse>?, Links? }` | `SdkException<ListCustomerPaymentTokensError>` Case A — `TryGetError1(out Error1)`[400,403,500] · `TryGetRawError` fallback | Pass `totalRequired: true` to get `TotalPages`; **no built-in pagination helper** ("Pagination: none" per map) — loop `page = 1..TotalPages` yourself if you need every saved card. `customerId` = the PayPal-generated `CustomerResponse.Id` you persisted from `CreatePaymentToken`. |
| `Vault.GetPaymentToken` | `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentTokenResponse` (same shape as above) | `SdkException<GetPaymentTokenError>` Case A — `TryGetError1(out Error1)`[403,404,422,500] · `TryGetRawError` fallback | — |
| `Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `void` (Task) | `SdkException<DeletePaymentTokenError>` Case A — `TryGetError1(out Error1)`[400,403,500] · `TryGetRawError` fallback | Detach saved card by id. |
| `TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query: `start_date`←`startDate`, `end_date`←`endDate` (ISO-8601, required) + 8 nullable filters (pass `null` to skip) + `fields`,`balance_affecting_records_only`,`page_size`,`page` | `SearchResponse{ TransactionDetails(transaction_details):IReadOnlyList<TransactionDetails>?[ TransactionInfo(transaction_info):TransactionInformation{ TransactionId, TransactionInitiationDate, TransactionUpdatedDate, TransactionAmount, FeeAmount, TransactionStatus(string, **not** an enum) } ], Page, TotalItems, TotalPages, Links }` | **`SdkException<RawError>` — Case B, the SDK's only Case-B operation** (all 39 other operations are Case A). Read via `ex.Error.StatusCode`/`.ReadAsString()`/`.ReadAsJson<T>()` — there is **no** `TryGetError`/typed accessor here. | id/amount/status/dates/fees map onto `TransactionInformation.{TransactionId, TransactionAmount, TransactionStatus, TransactionInitiationDate/TransactionUpdatedDate, FeeAmount}`. **No auto-pagination** — loop `page = 1..SearchResponse.TotalPages` to fetch the full date range. Sandbox reporting lags live activity up to ~3 hours (operation's own doc) — empty results for very recent transactions are expected, not an error. |

### 2.3 Enum value tables (as needed above)

| Enum | Members (C# literal ← wire) |
|---|---|
| `CheckoutPaymentIntent` | `Capture←CAPTURE`, `Authorize←AUTHORIZE` |
| `OrderStatus` | `Created←CREATED`, `Saved←SAVED`, `Approved←APPROVED`, `Voided←VOIDED`, `Completed←COMPLETED`, `PayerActionRequired←PAYER_ACTION_REQUIRED` |
| `AuthorizationStatus` | `Created←CREATED`, `Captured←CAPTURED`, `Denied←DENIED`, `PartiallyCaptured←PARTIALLY_CAPTURED`, `Voided←VOIDED`, `Pending←PENDING` (no "Expired" member) |
| `CaptureStatus` | `Completed←COMPLETED`, `Declined←DECLINED`, `PartiallyRefunded←PARTIALLY_REFUNDED`, `Pending←PENDING`, `Refunded←REFUNDED`, `Failed←FAILED` |
| `RefundStatus` | `Cancelled←CANCELLED`, `Failed←FAILED`, `Pending←PENDING`, `Completed←COMPLETED` |
| `PaymentTokenStatus` (only on `SetupTokenResponse`, **not** on `PaymentTokenResponse`) | `Created←CREATED`, `PayerActionRequired←PAYER_ACTION_REQUIRED`, `Approved←APPROVED`, `Vaulted←VAULTED`, `Tokenized←TOKENIZED` |
| `OrdersCardVerificationMethod` (`CardRequest.Attributes.Verification.Method`, default `ScaWhenRequired`) | `ScaAlways←SCA_ALWAYS`, `ScaWhenRequired←SCA_WHEN_REQUIRED` (default), `_3DSecure←3D_SECURE`, `AvsCvv←AVS_CVV` |
| `VaultCardVerificationMethod` (`SetupTokenRequestCard.VerificationMethod` only) | `ScaWhenRequired←SCA_WHEN_REQUIRED`, `ScaAlways←SCA_ALWAYS` |
| `VaultTokenRequestType` | `SetupToken←SETUP_TOKEN` (only member — used for the two-step setup-token→payment-token exchange, not for card vaulting reuse) |
| `TokenType` (`OrderAuthorizeRequestPaymentSource.Token.Type` / `OrderCaptureRequestPaymentSource.Token.Type`) | `BillingAgreement←BILLING_AGREEMENT` (only member) — **do not use this `Token` field to reuse a saved card**; it models legacy PayPal billing-agreement tokens only. Use `CardRequest.VaultId` instead (§2.2 CreateOrder row). |

### 2.4 The direct-card-redirect gap (reportable — resolved, not routed around)

**A direct/single-step card `CreateOrder` call CAN return a browser-redirect challenge instead of an
authorization**, and this is by design, not a bug to work around:

- `CardRequest.Attributes.Verification.Method` (`OrdersCardVerificationMethod`) **defaults to `ScaWhenRequired`**
  if you don't set it. Per its own doc-comment (source): *"...this option will return a contingency and HATEOAS
  link only when local regulations require strong customer authentication (e.g. 3DS in countries/use-cases
  where it is mandated). The API caller should redirect the payer to the link so they can authenticate
  themselves."* `ScaAlways` and `_3DSecure` can trigger it unconditionally/more often; only `AvsCvv`'s
  doc-comment does **not** mention a contingency link, but the SDK gives no guarantee it is exempt.
- **Detection, exact mechanism (source-confirmed, `OrderStatus.cs` doc-comment verbatim)**: if
  `Order.Status == OrderStatus.PayerActionRequired`, no authorization was created. Inspect
  `Order.Links` (`IReadOnlyList<LinkDescription>`) for the entry with `Rel == "payer-action"` — its `Href` is
  the URL to send the shopper's browser to. *("Some payment sources may not return a payer-action HATEOAS
  link (e.g. MB WAY); for those the payer-action is managed by the scheme itself." — not applicable to
  card, called out for completeness.)*
- **Consequence for this integration**: a truly redirect-free direct-card authorize is only guaranteed when
  PayPal/the issuer does not require SCA for that card/region/amount — which the merchant cannot fully
  control by SDK configuration alone. Build the "pay" endpoint to check `Order.Status` after `CreateOrder`
  and return a distinct "requires shopper action" result (with the `payer-action` URL) rather than assuming
  success — **do not** silently retry or treat it as a failure.

---

## 3. Trap notes

⚠ Step 0 (client & DI) — the `HttpClient` passed to `PayPalServerSdkClient` must be long-lived/reused via
`IHttpClientFactory`, not rebuilt per request; the client wrapper itself may be transient. **MUST load
`dotnet-client-initialization`.**

⚠ Step 0 (auth) — where exactly to set `Oauth2`/`Oauth2TokenStrategy` (before construction vs. in the DI
callback) and how secrets should be sourced from configuration rather than hardcoded is not fully decided
here. **MUST load `dotnet-authentication`.**

⚠ Every operation row above (calling) — most operations have 4-8 `nullable, no-default` parameters that
**must** be passed explicitly (`null` to skip) and are easy to mis-bind positionally (e.g. swapping
`payPalMockResponse`/`payPalRequestId`/`payPalAuthAssertion`). Always call with named arguments. **MUST load
`dotnet-calling-endpoints`.**

⚠ Every request-model row above (models) — enums here are `StringEnum<T>`, not C# `enum`, and are built via
static members or `Type.FromValue(...)`; `Money.Value` is a `string` with a currency-specific decimal-places
rule (source: `Money.cs` doc-comment) — naive `decimal.ToString()` can produce a value PayPal's regex rejects
for a currency's minor-unit count. **MUST load `dotnet-models`.**

⚠ Error boundary (all operations) — 39 of 40 operations are Case A (typed `{Operation}Error` with
`TryGetError(out Error)`/`TryGetError1(out Error1)` + a `TryGetNoContent(out RawError)` 500-case on every
`Payments`/`Vault` op) and exactly **one**, `TransactionSearch.SearchTransactions`, is Case B
(`SdkException<RawError>`, no typed accessor at all) — a catch ladder written for Case A only will not compile
against that one call, and a ladder written for Case B misses every typed accessor everywhere else. **MUST
load `dotnet-error-handling`.**

⚠ Step 4/2 (reauthorize/refund/void error interpretation, UNVERIFIED) — the SDK does not enumerate PayPal's
business error codes: `Error.Details[].Issue`/`Error1.Details[].Issue` is a free-text `string`, not a typed
enum, so there is no compiled way to distinguish "authorization is stale but still reauthorizable" from
"reauthorization is no longer possible" (past the 29-day window), or "refund amount exceeds what's capturable"
from any other 422, purely from SDK types. **Defensive-coding directive**: on a `ReauthorizePayment` failure,
do not retry reauthorize — surface the raw `Error.Name`/`Error.Message`/`Error.Details[].Issue` text to the
operator and offer "create a new order" (fresh `CreateOrder`) as the resolution path, since the SDK gives no
stronger signal than "it failed." On a `CaptureAuthorizedPayment` 422, attempt one `GetAuthorizedPayment` +
conditional `ReauthorizePayment` + retry, then surface the raw error verbatim if it still fails. Label:
**UNVERIFIED** (only a live sandbox response against a genuinely 30+-day-old authorization could confirm the
exact `Issue` string PayPal actually sends).

⚠ Step 5 (vault, UNVERIFIED) — `PaymentTokenRequestCard` (used by `CreatePaymentToken`, the one-step
vault-a-raw-card call) has **no** `ExperienceContext`/`VerificationMethod` fields, and `PaymentTokenResponse`
has **no** `Status` field at all — unlike `SetupTokenRequestCard`/`SetupTokenResponse.Status` (which does
include `PayerActionRequired`). Whether PayPal can ever require a 3DS challenge on the one-step vault call,
and what that looks like if the SDK model has nowhere to carry it, is **UNVERIFIED**. **Defensive-coding
directive**: catch `CreatePaymentTokenError` broadly and, if a card cannot be vaulted directly, fall back to
the two-step flow — `Vault.CreateSetupToken` (with `SetupTokenRequestCard.ExperienceContext`/
`VerificationMethod` set) → shopper completes any challenge via the returned HATEOAS link → `CreatePaymentToken`
with `PaymentTokenRequestPaymentSource.Token = new VaultTokenRequest { Id = setupTokenId, Type =
VaultTokenRequestType.SetupToken }`.

⚠ Step 1/2/4 (money formatting) — `Money.Value` must match the currency's minor-unit convention (e.g. `"10.00"`
for USD, no decimal point for JPY) — building it from a naive `decimal` string can violate the SDK's own
validation regex. **MUST load `dotnet-models`.**

⚠ Step 0/6 (resilience & pagination) — `HttpMethodsToRetry` only gates the **status**-based retry trigger; a
transport-level `HttpRequestException` is retried on **every** verb including `POST`, so a
`CreateOrder`/`CaptureAuthorizedPayment`/`RefundCapturedPayment` call can execute twice at the transport layer
even with `payPalRequestId` unset — the `PayPal-Request-Id` header is what actually neutralizes that, not the
retry config. `ListCustomerPaymentTokens`/`SearchTransactions` have no built-in pagination helper ("Pagination:
none" throughout this SDK) — you must loop `page` yourself using each response's `TotalPages`. **MUST load
`dotnet-configuration-resilience`.**

⚠ Testing — the `HttpClient` constructor argument is the test seam for stubbing every operation above,
including the two error cases (Case A/B) and the `PayerActionRequired`/stale-authorization/partial-refund
branches. **MUST load `dotnet-testing`.**

---

## 4. REQUIRED READING

Load all of the following **before implementation starts** — this sheet deliberately does not carry their
contents:

- `dotnet-client-initialization` — governs Step 0 (client/DI registration, `HttpClient` lifetime).
- `dotnet-authentication` — governs Step 0 (OAuth2 client-credentials wiring, where to set credentials).
- `dotnet-calling-endpoints` — governs every operation call in §2.2 (named-argument discipline, async/cancellation).
- `dotnet-models` — governs every request/response model in §2.2 (enum construction, `Money` formatting, nullability).
- `dotnet-error-handling` — governs the single error boundary wrapping every call in §2.2 (Case A vs Case B, `TryGetNoContent`).
- `dotnet-configuration-resilience` — governs Step 0 (base URL, retries/timeouts) and Step 5/6 (manual pagination loops).
- `dotnet-testing` — governs the test suite for the integration layer built from this sheet.

Mandatory hazard rows (verbatim) — `System.Text.Json.JsonException` reaches the error boundary from two
directions and they need **opposite** handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions:**
- "Order total to the cent" is built from the eShopOnWeb basket/order total converted to a `Money.Value`
  string matching `PayPal:Currency`'s minor-unit convention (2 decimals for USD/EUR/GBP; PayPal:Currency is
  assumed to be one of the common 2-decimal currencies unless the implementer confirms otherwise — JPY-style
  zero-decimal currencies need different formatting, not covered here since not requested).
- "Customer identity" for vaulting is modeled as: your own `Order`/user's identity → `Customer.MerchantCustomerId`
  on first `CreatePaymentToken` call; PayPal's returned `CustomerResponse.Id` is what you persist and reuse as
  `ListCustomerPaymentTokens`'s `customerId` argument thereafter (resolved from source doc-comments, not assumed
  from memory).
- `payPalClientMetadataId`/`payPalAuthAssertion`/`payPalPartnerAttributionId`/`payPalMockResponse` parameters
  present on several signatures are out of scope for this integration (no partner/platform or sandbox
  negative-testing requirement stated) — pass `null` for all of them.

**Blockers / genuine SDK gaps (not routed around):**
- **`PayPal:Environment` config value "live" has no corresponding SDK member.** This generated SDK's
  `ServerEnvironment` (`Servers/ServerEnvironment.cs`) exposes **only `Sandbox`** — there is no `Live`/
  `Production` member in `v1.0.1`. If a "live" environment is ever required, `PayPal:BaseUrl` override
  (§2.1) against the `Sandbox`-named environment member is the only mechanism this SDK version offers;
  flag this back to whoever owns SDK-version selection rather than assuming a future SDK release adds it.
- **No SDK field expresses "remaining refundable amount" directly** — must be computed app-side per the
  `RefundCapturedPayment` row in §2.2 (there is no gap in *capability*, just no single ready-made field).
- **Reauthorization "impossible" vs "still eligible" has no typed signal** — see the UNVERIFIED trap note in
  §3; this is a genuine modeling gap in the generated error types (`Issue` is untyped free text), not
  something achievable via a different call.
- **3DS-during-vaulting has no typed signal on the one-step `CreatePaymentToken` path** — see the UNVERIFIED
  trap note in §3; the two-step `CreateSetupToken`/`CreatePaymentToken(Token=...)` fallback is the only
  SDK-modeled path that carries a challenge signal (`SetupTokenResponse.Status`).
- Pagination (`ListCustomerPaymentTokens`, `SearchTransactions`) is confirmed to exist as manual
  `page`/`page_size`/`TotalPages` fields — **not** a blocker, but confirmed **not** to have any SDK-side
  auto-iteration helper, so the reconciliation report and saved-card listing must implement their own
  page-walking loop.
