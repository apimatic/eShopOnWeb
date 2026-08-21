# PayPal .NET SDK integration plan — eShopOnWeb `src/PublicApi` (SANDBOX)

SDK: `PayPalServerSdk` (NuGet `AsadAli.Checkout.Sdk`, install version-less). Map release tag `v1.0.1`,
source commit `9653d18`. Target framework of SDK: `netstandard2.0`. Every fact below cites the map
page (or, where the map is silent, the SDK source file) it came from.

All PayPal traffic goes through this SDK. Direct-card sandbox flow (Visa `4111111111111111`, any future
expiry, any CVC) — no browser/3DS approval round-trip is designed. See **Assumptions & Blockers** for the
one capability that would require a browser challenge.

---

## 1. Scope & sequence

1. **Client + DI setup** — register `PayPalServerSdkClient` in `src/PublicApi` DI, binding the five
   `PayPal:*` config keys. Uses `AddPayPalServerSdkClient` / `PayPalServerSdkClientOptions`. (Step governed by
   `dotnet-client-initialization`, `dotnet-authentication`, `dotnet-configuration-resilience`.)
2. **Create order (intent=AUTHORIZE) paid by direct card** — `client.Orders.CreateOrder` with
   `OrderRequest` carrying `PaymentSource.Card` (raw PAN). (Features 1.)
3. **Authorize (place hold, no capture)** — read the authorization out of the create response if present,
   else `client.Orders.AuthorizeOrder`. (Features 2.)
4. **Capture at fulfilment** — `client.Payments.CaptureAuthorizedPayment`; read gross/fee/net. (Features 3.)
5. **Re-authorize a stale hold** — `client.Payments.ReauthorizePayment`; detect non-renewable via typed
   error / status. (Features 4.)
6. **Void a hold** — `client.Payments.VoidPayment`. (Features 5.)
7. **Refund captured (full/partial), idempotent** — `client.Payments.RefundCapturedPayment`. (Features 6, 7.)
8. **Vault a card standalone, then pay with the stored token** — `client.Vault.CreatePaymentToken`, then
   `CreateOrder` with `PaymentSource.Card.VaultId`. (Features 8.)
9. **Delete a vaulted card** — `client.Vault.DeletePaymentToken`. (Features 9.)
10. **Reconciliation** — `client.TransactionSearch.SearchTransactions`, paged over the whole range.
    (Features 10.)

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The
> cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from
> that type's own map row, never from where a neighbouring type sits. A members table names the namespace
> outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a
> file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are
> spread across different child namespaces, and two types configured side by side in the same options
> object routinely live in different ones. Dropping a type to the root or to `.Models` makes the
> implementer guess the wrong `using`, and the build breaks.

### 2.0 Namespaces (add a separate `using` per kind — child namespaces are NOT transitively imported)

| Contents | Namespace | Source |
|---|---|---|
| Client, options, `ServerOptions`, `Server` | `PayPalServerSdk` | `sdk-map.md` namespaces table; `ServerOptions.cs` |
| Controllers (`client.Orders` etc. types) | `PayPalServerSdk.Api` | `sdk-map.md` namespaces table |
| Records (all request/response models below) | `PayPalServerSdk.Models` | records pages header |
| Enums (`CheckoutPaymentIntent`, `AuthorizationStatus`, …) | `PayPalServerSdk.Models.Enums` | `enums.md` |
| Typed error classes (`{Op}Error`, `ApiError`) | `PayPalServerSdk.Errors` | `sdk-map.md` namespaces table |
| `ServerEnvironment` | `PayPalServerSdk.Servers` | `Servers/ServerEnvironment.cs` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` (path-implies: `Core/Exceptions/SdkException.cs`) | `sdk-map.md` error-core |
| `RawError` | `PayPalServerSdk.Core.ErrorResponse` (path-implies: `Core/ErrorResponse/RawError.cs`) | `sdk-map.md` error-core |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | `OAuth2ClientCredentials.cs` |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` (path-implies: `Core/Configuration/RetryOptions.cs`) | `sdk-map.md` client-options |

### 2.1 Client construction, auth, environment, BaseUrl override (grounded in SDK source + `sdk-map.md`)

Construction — one constructor only (`sdk-map.md` client-options):
`new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`.
DI alternative (`ServiceCollectionExtensions.cs`): `services.AddPayPalServerSdkClient(o => { … })`.

`PayPalServerSdkClientOptions` properties actually used (`sdk-map.md` client-options table):
- `Environment` : `PayPalServerSdk.Servers.ServerEnvironment` — **only member is `ServerEnvironment.Sandbox`**
  (`Servers/ServerEnvironment.cs`; `ServerEnvironment.Default()` returns `Sandbox`). There is **no Live
  environment in this SDK build.**
- `Oauth2` : `OAuth2ClientCredentials?` — set
  `o.Oauth2 = new OAuth2ClientCredentials { ClientId = <PayPal:ClientId>, ClientSecret = <PayPal:ClientSecret> }`
  (`OAuth2ClientCredentials.cs`: `ClientId`/`ClientSecret` are `required`, `Scope` optional). The default token
  strategy uses HTTP Basic (`client_id:client_secret` base64) against `/v1/oauth2/token` (source
  `AuthSchemes.cs` line 17 + `OAuth2ClientCredentialsStrategy.cs`). Do not set `Oauth2TokenStrategy` — leave
  null to get that default.
- `Server` : `ServerOptions` — the base-URL override point. **`BaseUrl` wiring (confirmed from source):**
  `ServerOptions.Default` (`DefaultOptions`) `.Sandbox` (`SandboxOptions`) `.BaseUrl` (`string`, default
  `"https://api-m.sandbox.paypal.com"`). Set `o.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>` **only when
  the key is present**; when absent, leave the default (that IS the Sandbox derivation).
- `Retry` : `RetryOptions`, `Logging` : `LoggingOptions` — leave defaults unless tuning (see trap note).

**BaseUrl override reaches EVERY call including the OAuth token request — CONFIRMED from source, not
inferred.** `PayPalServerSdkClient.cs` line 25 builds one `Server` object
(`new Server(options.Environment, options.Server)`) and passes it both to the request pipeline and to
`AuthSchemes`, whose token strategy is built from `server.Default("/v1/oauth2/token")` (`AuthSchemes.cs`
line 17). Because the token URL and all API URLs resolve through the same `DefaultOptions.Resolve →
Sandbox.BaseUrl` (`Servers/DefaultOptions.cs`), overriding `Server.Default.Sandbox.BaseUrl` re-hosts the
token request too. So: **bind `PayPal:BaseUrl` once onto `o.Server.Default.Sandbox.BaseUrl`; it applies to
OAuth + every operation.** Environment selection: set `o.Environment = ServerEnvironment.Sandbox` when
`PayPal:Environment == "Sandbox"`; any other value has no server to map to (see Blockers).

**HttpClient / DI lifetime:** the client takes a long-lived `HttpClient` — the handler pipeline must be
reused (`IHttpClientFactory`), never rebuilt per request; the SDK-client wrapper over it may be transient.
Exact registration shape and lifetimes: **MUST load `dotnet-client-initialization`** — do not hand-new an
`HttpClient` per call. `PayPal:Currency` is an app-level value the integration passes into `Money`/
`AmountWithBreakdown.CurrencyCode`; it is not an SDK option.

### 2.2 Operations table

Cancellation-token param is `ct` on every method. "Must pass explicitly" = nullable param with no C#
default → pass `null` to skip. All request/response record types live in `PayPalServerSdk.Models` unless
noted. Error handling: **every operation is throw-only (no `…Result` variant exists)**; on error status the
SDK throws `SdkException<TError>` (`.Error` is `TError`). Case A = typed accessors; Case B = `RawError`.

| # | Controller.Method (signature, params in order) | Request model + key fields (`Name (wire): type, required?`) | Response envelope → fields to read | Error case + accessors (payload type) | Map page |
|---|---|---|---|---|---|
| 1 | `client.Orders.CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — first 5 nullable params must pass explicitly (pass `null`); pass `payPalRequestId` for idempotency | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req` = `CheckoutPaymentIntent.Authorize`; `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`; `PaymentSource (payment_source): PaymentSource?`. `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req`. `AmountWithBreakdown`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req`. `PaymentSource`: `Card (card): CardRequest?`. `CardRequest`: `Number (number): string?`, `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `Name (name): string?`, `BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?` | returns `Order`: `Id (id): string?`, `Status (status): OrderStatus?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`. Auth id path: `Order.PurchaseUnits[i].Payments (PaymentCollection?) .Authorizations (IReadOnlyList<AuthorizationWithAdditionalData>?)[j] .Id / .Status / .ExpirationTime` | Case A `SdkException<CreateOrderError>`: `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` [fallback] | operations/Orders.md; records-1-Ac-Pa.md; records-2-Pa-Ve.md |
| 2 | `client.Orders.AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 5 nullable params must pass explicitly; `payPalRequestId` = idempotency | `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` (may pass `body: null` when the order already carries the card payment source) | returns `OrderAuthorizeResponse`: `Id`, `Status (OrderStatus?)`, `PurchaseUnits (IReadOnlyList<PurchaseUnit>?)`. Auth id: `PurchaseUnits[i].Payments.Authorizations[j].Id` + `.Status (AuthorizationStatus?)` + `.ExpirationTime` | Case A `SdkException<AuthorizeOrderError>`: `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` [fallback] | operations/Orders.md; records-1-Ac-Pa.md |
| 3 | `client.Payments.CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullable params must pass explicitly; `payPalRequestId` = idempotency | `CaptureRequest` (optional; pass `null` for full-amount final capture, or): `Amount (amount): Money?`, `FinalCapture (final_capture): bool? = false`, `InvoiceId (invoice_id): string?`, `NoteToPayer`, `SoftDescriptor`. `Money`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req` | returns `CapturedPayment`: `Id (id): string?`, `Status (status): CaptureStatus?`, `Amount (amount): Money?` (captured amount), `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`. **Fee/net accessor paths:** captured amount `CapturedPayment.Amount.Value`; gross `SellerReceivableBreakdown.GrossAmount (gross_amount): Money !req`; PayPal fee `SellerReceivableBreakdown.PaypalFee (paypal_fee): Money?`; net proceeds `SellerReceivableBreakdown.NetAmount (net_amount): Money?` (each `.Value` + `.CurrencyCode`) | Case A `SdkException<CaptureAuthorizedPaymentError>`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] | operations/Payments.md; records-1-Ac-Pa.md; records-2-Pa-Ve.md |
| 4 | `client.Payments.ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 nullable params must pass explicitly | `ReauthorizeRequest`: `Amount (amount): Money?` (only field the endpoint supports) | returns `PaymentAuthorization`: `Id (id): string?`, `Status (status): AuthorizationStatus?`, `ExpirationTime (expiration_time): string?`, `StatusDetails (status_details): AuthorizationStatusDetails?` | Case A `SdkException<ReauthorizePaymentError>`: `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] | operations/Payments.md; records-2-Pa-Ve.md |
| 5 | `client.Payments.VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 3 nullable params must pass explicitly | (no request body) | returns `PaymentAuthorization`: read `Status (status): AuthorizationStatus?` (expect `AuthorizationStatus.Voided`), `Id` | Case A `SdkException<VoidPaymentError>`: `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] | operations/Payments.md; records-2-Pa-Ve.md |
| 6 | `client.Payments.RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — 4 nullable params must pass explicitly; `payPalRequestId` = idempotency key | `RefundRequest` (pass `null`/empty for FULL refund; supply `Amount` for PARTIAL): `Amount (amount): Money?`, `InvoiceId`, `CustomId`, `NoteToPayer` | returns `Refund`: `Id (id): string?`, `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` | Case A `SdkException<RefundCapturedPaymentError>`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] | operations/Payments.md; records-2-Pa-Ve.md |
| 8a | `client.Vault.CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must pass explicitly (idempotency) | `PaymentTokenRequest`: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`, `Customer (customer): Customer?`. `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?`. `PaymentTokenRequestCard`: `Number (number): string?`, `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `Name (name): string?`, `BillingAddress (billing_address): Address?`. `Customer`: `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?` | returns `PaymentTokenResponse`: **`Id (id): string?` = the stable vault/payment-token id to persist**; `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `.Card (CardPaymentTokenEntity?)` for `LastDigits`/`Brand`/`Expiry` | Case A `SdkException<CreatePaymentTokenError>`: `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError` [fallback] | operations/Vault.md; records-2-Pa-Ve.md; records-1-Ac-Pa.md |
| 8b | (pay later with stored token) `client.Orders.CreateOrder(...)` as row 1, but `OrderRequest.PaymentSource = new PaymentSource { Card = new CardRequest { VaultId = <persisted token id> } }` | **Use `CardRequest.VaultId (vault_id): string?`** — do NOT use `PaymentSource.Token`: that is typed `Token` whose `Type` is `TokenType`, and `TokenType`'s only member is `BillingAgreement (BILLING_AGREEMENT)` (`enums.md`), i.e. `payment_source.token` is for billing agreements, not vaulted cards | as row 1 (`Order`) | as row 1 (`CreateOrderError`) | operations/Orders.md; records-1-Ac-Pa.md (`CardRequest`, `Token`); records-2-Pa-Ve.md (`PaymentSource`); enums.md |
| 9 | `client.Vault.DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | (no body) | returns `void` (Task) — success is HTTP 204 | Case A `SdkException<DeletePaymentTokenError>`: `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` [fallback] | operations/Vault.md |
| 10 | `client.TransactionSearch.SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 nullable params (`transactionId`…`terminalId`) must pass explicitly (`null`); **call with named args** | inputs are query params: `startDate`/`endDate` are ISO-8601 date-time strings (wire `start_date`/`end_date`); `pageSize` (wire `page_size`), `page` (wire `page`) | returns `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Links (links): IReadOnlyList<LinkDescription>?`. Each `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?` | **Case B** `SdkException<RawError>`: `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()` — no typed accessors | operations/TransactionSearch.md; records-2-Pa-Ve.md |

**Pagination for #10 (reconciliation):** the SDK exposes **no auto-pager** (`Pagination: none` in the map).
Page manually: start `page = 1`, read `SearchResponse.TotalPages`, loop incrementing `page` until
`page > TotalPages`, accumulating `TransactionDetails`. Keep `startDate`/`endDate` fixed across pages. This
is the `page`/`total_pages` mechanism (not `links`-driven).

### 2.3 Enum value tables needed (namespace `PayPalServerSdk.Models.Enums`; write the C# member, not the wire value)

| Enum | Members (`CSharpMember (wire)`) | Used for |
|---|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` | `OrderRequest.Intent` = `CheckoutPaymentIntent.Authorize` (#1) |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` | `Order.Status` / `OrderAuthorizeResponse.Status` (#1,#2) |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` | authorization status (#2,#4,#5) |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` | `CapturedPayment.Status` (#3) |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` | `Refund.Status` (#6) |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` | vault-token status if surfaced (#8) |

Enums are `StringEnum<T>` records, NOT C# enums: build via the static member (`CheckoutPaymentIntent.Authorize`)
or `CheckoutPaymentIntent.FromValue("AUTHORIZE")`. Compare with `==`.

### 2.4 Typed error payload shapes (records in `PayPalServerSdk.Models`; reached via the `TryGet…` accessors above)

- `Error` (out-type of `TryGetError`): `Name (name): string !req`, `Message (message): string !req`,
  `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`, `Links`. (records-1-Ac-Pa.md)
- `Error1` (out-type of `TryGetError1`, Vault ops): same shape, `Details: IReadOnlyList<ErrorDetails1>?`. (records-1-Ac-Pa.md)
- `ErrorDetails`: `Field (field): string?`, `Value (value): string?`, `Location (location): string? = "body"`,
  `Issue (issue): string !req`, `Description (description): string?`. The operator-actionable code lives in
  `Details[].Issue` / human text in `Message`. (records-1-Ac-Pa.md)
- `DefaultError` (TransactionSearch `SearchBalances` only — not #10): `Name`,`Message`,`DebugId`,`Details:
  IReadOnlyList<TransactionSearchErrorDetails>?`. (records-1-Ac-Pa.md)

---

## 3. Per-feature contract clarifications (resolved facts + defensive directives)

**Feature 1 — hold equals total to the cent.** `AmountWithBreakdown.Value` and `Money.Value` are **strings**.
Format the order total to the currency's minor-unit precision (e.g. USD → `"12.34"`) from a `decimal`; do not
pass a culture-formatted or rounded-off string. The authorized hold is the purchase-unit `Amount`, so send the
exact total once — the single `PurchaseUnitRequest.Amount` is the held amount.

**Feature 2 — does create-with-payment-source auto-authorize, or is a separate `AuthorizeOrder` call
needed?** The SDK exposes BOTH paths and the map cannot settle which fires for a given sandbox response —
this is live-traffic behaviour. **Directive (defensive, best-effort with fallback):** after `CreateOrder`,
inspect the returned `Order`: if `PurchaseUnits[i].Payments?.Authorizations` already contains an entry
(status `AuthorizationStatus.Created`), treat that as the placed hold and read its `.Id` — do **not** call
`AuthorizeOrder` again. Only if no authorization is present (e.g. `Order.Status == OrderStatus.Approved` with
empty `Authorizations`) call `client.Orders.AuthorizeOrder(id, null, payPalRequestId, null, null, body: null,
…)` and read the authorization from `OrderAuthorizeResponse.PurchaseUnits[i].Payments.Authorizations[0].Id`.
Whether create alone auto-authorizes is `UNVERIFIED` (confirmable only against live sandbox traffic).

**Feature 3 — fee/net accessor path.** captured amount = `CapturedPayment.Amount.Value`; PayPal fee =
`CapturedPayment.SellerReceivableBreakdown.PaypalFee?.Value`; net proceeds =
`CapturedPayment.SellerReceivableBreakdown.NetAmount?.Value`; gross =
`CapturedPayment.SellerReceivableBreakdown.GrossAmount.Value`. `SellerReceivableBreakdown` is null for
pending captures (per its map summary) — guard the whole chain with null-conditional and fall back to the
generic message when absent. `PaypalFee`/`NetAmount` are nullable even when the breakdown is present.

**Feature 4 — detect a non-renewable authorization.** `ReauthorizePayment` throws
`SdkException<ReauthorizePaymentError>`; a hold that can no longer be renewed surfaces as a `422`/`4xx` typed
`Error` — read `ex.Error.TryGetError(out var e)` then the operator-actionable code from `e.Details[].Issue`
(human text `e.Message`). Complementarily, `PaymentAuthorization.Status` (`AuthorizationStatus.Voided`/
`Denied`/`Captured`) and `ExpirationTime` indicate a hold that is gone/consumed. **Directive:** extract
`Details[].Issue` best-effort and surface it to the operator; **fall back to `e.Message`, then to the generic
message**, if `Details` is empty. The exact issue string that means "beyond the 29-day reauth window / cannot
reauthorize" is `UNVERIFIED` (only live traffic confirms the literal code) — do not hard-code/branch on a
guessed string; branch on presence + show the text.

**Feature 5 — void.** `VoidPayment` returns `PaymentAuthorization`; confirm release via
`Status == AuthorizationStatus.Voided`. A `409` typed error means it cannot be voided (e.g. already captured).

**Feature 6 — never over-refund; idempotency.** There is no SDK field giving "remaining refundable"; the
authoritative guard is PayPal's `422` on over-refund (read `Error.Details[].Issue`). **Directive:** before
refunding, compute remaining = captured `GrossAmount` minus the sum of prior `Refund.Amount`s you have
persisted, and never send `RefundRequest.Amount` above it; still catch the `422` as the backstop. Idempotency:
pass the caller-supplied key as **`payPalRequestId`** (→ `PayPal-Request-Id` header). Same key + same body ⇒
PayPal returns the original refund (no double-refund); **two DISTINCT partial refunds require two DISTINCT
keys** — reusing one key for a second, different partial refund returns the first refund instead of making a
new one. The refund id/status = `Refund.Id` / `Refund.Status (RefundStatus)`. Whether the sandbox returns the
identical `Refund` body on a replayed key is `UNVERIFIED` (live-traffic) — treat a replay as success and read
`Refund.Id`.

**Feature 7 — idempotency for authorize/capture generally.** Same mechanism: the **`PayPal-Request-Id`**
header, supplied via the `payPalRequestId` parameter present on `CreateOrder`, `AuthorizeOrder`,
`CaptureAuthorizedPayment`, `RefundCapturedPayment`, `ReauthorizePayment`, `VoidPayment`, `CreatePaymentToken`.
Generate one stable key per logical user action (e.g. per checkout-submit / per fulfilment) and reuse it on
retries so a double-click cannot authorize/capture twice. See the resilience trap note — a transport-level
retry re-sends POSTs, so the header is what makes the retry safe.

**Feature 8 — vault standalone then pay.** (a) `CreatePaymentToken` with `PaymentTokenRequest.PaymentSource.Card`
(raw PAN) — persist `PaymentTokenResponse.Id`. (b) Pay a later order: `CreateOrder` with
`PaymentSource.Card.VaultId = <persisted id>` (NOT `PaymentSource.Token`; see row 8b). Note there is also a
`Vault.CreateSetupToken` two-step flow (setup-token → payment-token) but the direct `CreatePaymentToken` path
vaults a card in one call and is what this plan uses.

**Feature 9 — delete.** `DeletePaymentToken(id)` → 204; after it the token id can no longer fund an order.

**Feature 10 — reconciliation.** See pagination note in §2.2. `SearchTransactions` is the sole **Case B**
operation — its catch is `SdkException<RawError>` with `StatusCode`/`ReadAsString()`, no typed `Error`.

---

## 4. Trap notes (load the named skill at that step — the note names the hazard, not the answer)

- ⚠ **Step 1 (client & DI)** — how the `HttpClient`/handler pipeline must be owned and reused, and which
  lifetime the SDK client itself gets, is not visible in the constructor signature. **MUST load
  `dotnet-client-initialization`** before wiring `AddPayPalServerSdkClient` / newing the client.
- ⚠ **Step 1 (auth)** — where/when credentials must be set relative to client construction, and how to load
  them from configuration rather than hardcode, is a usage rule the property type does not show. **MUST load
  `dotnet-authentication`** before setting `o.Oauth2`.
- ⚠ **Step 1 (resilience / retries / timeout / base URL)** — whether a failed **write** (POST authorize/
  capture/refund) can be silently re-sent by the retry layer, and what `RetryOptions.Timeout` actually bounds
  (per-attempt vs whole call), and what the `HttpClient` timeout is versus the SDK timeout, are NOT inferable
  from the option names. This directly affects idempotency (features 6,7). **MUST load
  `dotnet-configuration-resilience`** before tuning `Retry`/timeouts/base URL.
- ⚠ **Steps 2–10 (calling list/search ops)** — many optional parameters have no C# default and mis-bind in a
  positional call; whether a param must be passed and how the response envelope is shaped is a usage concern.
  **MUST load `dotnet-calling-endpoints`** before the first call, especially `SearchTransactions` (call with
  named args). 
- ⚠ **Steps 2–10 (models)** — enums are `StringEnum<T>` (not C# enums), unions use factories + `TryGet…`, and
  **unmodeled JSON fields are dropped on deserialize**; required members must be set in the initializer.
  **MUST load `dotnet-models`** before building any request model or mapping a response.
- ⚠ **Steps 2–10 (error boundary)** — which exception type actually reaches each catch, that Case A typed
  errors are not a catch-all, and the `JsonException` traps below, are not shown by the signatures. **MUST
  load `dotnet-error-handling`** before writing the try/catch.
- ⚠ **Testing** — the `HttpClient` constructor argument is the test seam; match eShopOnWeb's existing test
  framework. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 5. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, options/builder, HttpClient ownership & DI lifetime |
| `dotnet-authentication` | Step 1 — supplying OAuth2 client-credentials, when to set them, loading secrets from config |
| `dotnet-configuration-resilience` | Step 1 — retries/backoff, what `Timeout` bounds, base-URL/server selection, pagination |
| `dotnet-calling-endpoints` | Steps 2–10 — finding the controller, required vs optional params, named args, response envelopes, cancellation |
| `dotnet-models` | Steps 2–10 — building request models, required/nullable, `StringEnum<T>`, unions, wire names |
| `dotnet-error-handling` | Steps 2–10 — which exception reaches the catch, reading status/details safely, the traps below |
| `dotnet-testing` | Tests — the `HttpClient` seam, error/edge paths |

These are to be loaded **before implementation starts**; the sheet does not carry their contents. An error
boundary is written for every one of features 1–10, so `dotnet-error-handling` is mandatory.

**Two `System.Text.Json.JsonException` hazards at the error boundary — handle them, they reach the boundary
from two directions and need opposite handling:**

- A drifted or malformed **2xx** body (a missing `required` member such as `Error.Name`, or a response whose
  shape moved) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an
  `SdkException` — an SDK-exception-only catch ladder lets it escape the integration boundary. Catch
  `JsonException` explicitly.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it. A boundary that maps every `JsonException` to a 5xx
  then reports a deterministic rejection (e.g. a 422 over-refund) as an outage, and a caller that retries 5xx
  retries something that can never succeed. Do not blanket-map `JsonException` → 5xx.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 6. Assumptions & Blockers

**Assumptions**
- `src/PublicApi` is the project that installs `AsadAli.Checkout.Sdk` and registers the client; the five
  `PayPal:*` keys are bound from configuration (never hardcoded).
- `PayPal:Currency` is applied to `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode` by the
  integration; it is not an SDK option.
- Idempotency keys are supplied/generated by the calling code (one stable key per logical action) and passed
  as `payPalRequestId`.
- Card `Expiry` is passed as the SDK `string` in PayPal's card-expiry format; exact string format is a
  PayPal-API wire detail, not enforced by the SDK type (`CardRequest.Expiry: string?`).

**Blockers**
- **`PayPal:Environment` values other than `"Sandbox"` have no server in this SDK build.**
  `ServerEnvironment` exposes only `Sandbox` (`Servers/ServerEnvironment.cs`). If configuration ever sets
  `Environment` to `"Live"`/`"Production"`, there is no environment to map to — the integration must either
  reject that value or rely solely on a `PayPal:BaseUrl` override (which does re-host every call including the
  token request). Flag before shipping anything non-sandbox.
- **Any capability requiring a browser approval/challenge is out of scope by directive.** The designed flows
  are all direct-card (no 3DS/redirect round-trip). If, at runtime, a card triggers `PAYER_ACTION_REQUIRED`
  / an authentication challenge (e.g. `Order.Status == OrderStatus.PayerActionRequired`, or a HATEOAS
  `rel:approve`/`payer-action` link), that path needs a browser round-trip and is a **BLOCKER** — surface it,
  do not attempt to auto-complete it. (Whether the sandbox business account with direct card processing +
  vaulting ever returns such a challenge for `4111…` is `UNVERIFIED` until exercised live.)
- **Auto-authorize-on-create behaviour (feature 2) is `UNVERIFIED`** — resolved defensively (inspect the
  create response, else call `AuthorizeOrder`); no blocker, but note the plan handles both outcomes.
