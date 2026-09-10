# PayPal Server SDK (.NET) — integration plan for eShopOnWeb payments

Adds PayPal card payments + saved cards to eShopOnWeb, exposed on `src/PublicApi`.
SDK: `PayPalServerSdk` (APIMatic, netstandard2.0, spec 2.29), vendored into the repo at
`src/PayPalServerSdk/` and referenced by `Infrastructure` (the only project that touches the SDK).

## 1. Scope & sequence

| Step | What | SDK operations |
| --- | --- | --- |
| A | Vendor SDK source into repo; ProjectReference from Infrastructure; `global.json` rollForward=latestMajor | — |
| B | ApplicationCore domain: `OrderPayment` aggregate (+ `PaymentRefund` child, status enum), `SavedPaymentMethod` aggregate; `IPaymentProcessor` + neutral result records; `IPaymentApplicationService`; exceptions | — |
| C | Infrastructure `PayPalPaymentProcessor : IPaymentProcessor` — the ONLY PayPal call site | all below |
| D | Infrastructure DI: bind `PayPal:` options, fail-fast, register `PayPalServerSdkClient` singleton, `AddPayPalServerSdkClient` | client ctor |
| E | Register `OrderPayment`/`SavedPaymentMethod` in `CatalogContext` (+ EF Config) | — |
| F | PublicApi endpoints (all `/api/…`, JWT), Program.cs wiring | — |
| G | user-secrets load from env; build; self-verify live sandbox; tests | — |

Pay = **CreateOrder(intent=AUTHORIZE, payment_source.card{…} | card.vault_id)** — **single-step direct card**: the card
(or saved-card vault id) goes in the CREATE payment_source and PayPal returns the authorization (the hold) in that one response's
`purchase_units[].payments.authorizations[]`. *(This is the VERIFIED shape. The initial plan used a two-step CreateOrder→AuthorizeOrder;
that returns 422 for a card-at-authorize on a bare order — the account requires the card at create time, so `AuthorizeOrder` is not used
by this integration. `PayPal-Request-Id` is mandatory on the single-step card create, and is set to the unique payment reference.)*
The invoice reference is split: **custom_id = `ESHOP-{orderId}`** (stable, reconciliation) and **invoice_id = a globally-unique payment
reference** (the account enforces invoice-id uniqueness). Fulfil = **CaptureAuthorizedPayment(authorizationId, prefer=return=representation)**
(representation is required for the fee/net breakdown); stale → **ReauthorizePayment** then re-capture. Cancel = **VoidPayment** (returns 204
No Content — the resulting deserialize is treated as success). Refund = **RefundCapturedPayment(prefer=return=representation)**, request-id =
`refund-{captureId}-{callerKey}`. Reconciliation = **SearchTransactions** (paged, in ≤30-day windows to cover an arbitrarily long range;
matched on `custom_field`). Save card = **CreatePaymentToken** (direct card source, one call). List/delete saved = our own store + **DeletePaymentToken**.

A capability the map lacks is a Blocker (§6), not an invented path.

## 2. CONTRACT SHEET

> ⚠ Signatures below are generated code, verbatim. Every parameter name is the literal C# identifier; named args use exactly those
> names (the cancellation-token parameter is literally `ct`, so `ct:`). Nullable-no-default params **must be passed explicitly** (pass `null` to skip).
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies (`Models/`→`PayPalServerSdk.Models`,
> `Models/Enums/`→`PayPalServerSdk.Models.Enums`, `Errors/`→`PayPalServerSdk.Errors`, client/options root→`PayPalServerSdk`,
> `Servers/`→`PayPalServerSdk.Servers`), taken from THAT type's own path.

Client: `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` — sole ctor. Groups: `client.Orders`,
`client.Payments`, `client.Vault`, `client.TransactionSearch`. Auth: `options.Oauth2 = new OAuth2ClientCredentials { ClientId, ClientSecret }`
(ns `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`). Env: `options.Environment = ServerEnvironment.Sandbox` (ns `PayPalServerSdk.Servers`; only member).
Base-URL override: `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>` — **verified** to feed both API calls AND the token request
(`AuthSchemes` builds the token URL as `server.Default("/v1/oauth2/token")` → `DefaultOptions.Resolve` → `Sandbox.BaseUrl`). Source: `PayPalServerSdkClient.cs`, `AuthSchemes.cs`, `Servers/DefaultOptions.cs`.

| Op | Signature (verbatim) · request fields used · response fields read · error case · source |
| --- | --- |
| `Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", …)`. Req `OrderRequest`: `Intent`(req, `CheckoutPaymentIntent.Authorize`), `PurchaseUnits`(req, `IReadOnlyList<PurchaseUnitRequest>`). `PurchaseUnitRequest`: `Amount`(req `AmountWithBreakdown`{`CurrencyCode` req, `Value` req}), `InvoiceId`, `CustomId`, `ReferenceId`. Resp `Order`: `.Id`, `.Status`. Case A `CreateOrderError`, `TryGetError(out Error)`[400,401,422]. src: map/operations/Orders.md; Models/OrderRequest.cs, PurchaseUnitRequest.cs, AmountWithBreakdown.cs, Order.cs |
| `Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", …)`. Req `OrderAuthorizeRequest`: `PaymentSource`(`OrderAuthorizeRequestPaymentSource`{`Card`:`CardRequest`{`Number`,`Expiry`(YYYY-MM),`SecurityCode`,`Name`,`BillingAddress`,`VaultId`} OR `Token`:`Token`}). Resp `OrderAuthorizeResponse`: `.Id`,`.Status`(`OrderStatus`),`.PurchaseUnits[].Payments.Authorizations[]`(`AuthorizationWithAdditionalData`{`.Id`,`.Status`(`AuthorizationStatus`),`.Amount`(`Money`),`.ExpirationTime`}),`.Links[]`(`.Rel`). Case A `AuthorizeOrderError`, `TryGetError(out Error)`[400,401,403,404,422,500]. src: Orders.md; Models/OrderAuthorizeRequest.cs, OrderAuthorizeRequestPaymentSource.cs, CardRequest.cs, OrderAuthorizeResponse.cs, PurchaseUnit.cs, PaymentCollection.cs, AuthorizationWithAdditionalData.cs |
| `Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", …)`. Req `CaptureRequest`: `Amount`(`Money`, omit⇒full), `FinalCapture=true`, `InvoiceId`. Resp `CapturedPayment`: `.Id`,`.Status`(`CaptureStatus`),`.Amount`,`.SellerReceivableBreakdown`{`GrossAmount` req,`PaypalFee`,`NetAmount`}. Case A `CaptureAuthorizedPaymentError`: `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500]. src: Payments.md; Models/CaptureRequest.cs, CapturedPayment.cs, SellerReceivableBreakdown.cs, Money.cs |
| `Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", …)`. Req `ReauthorizeRequest`:`Amount`(`Money`). Resp `PaymentAuthorization`:`.Id`,`.Status`,`.ExpirationTime`. Case A `ReauthorizePaymentError`:`TryGetError`[400,401,403,404,422]·`TryGetNoContent`[500]. src: Payments.md; Models/ReauthorizeRequest.cs, PaymentAuthorization.cs |
| `Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", …)`. Resp `PaymentAuthorization`:`.Status`(VOIDED). Case A `VoidPaymentError`:`TryGetError`[401,403,404,409,422]·`TryGetNoContent`[500]. src: Payments.md |
| `Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", …)`. Req `RefundRequest`:`Amount`(`Money`, omit⇒full),`InvoiceId`,`NoteToPayer`. Resp `Refund`:`.Id`,`.Status`(`RefundStatus`),`.Amount`. **`payPalRequestId` = caller idempotency key**. Case A `RefundCapturedPaymentError`:`TryGetError`[400,401,403,404,409,422]·`TryGetNoContent`[500]. src: Payments.md; Models/RefundRequest.cs, Refund.cs |
| `Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, …)`. Req `PaymentTokenRequest`:`Customer`(`Customer`{`Id`,`MerchantCustomerId`}),`PaymentSource`(req `PaymentTokenRequestPaymentSource`{`Card`:`PaymentTokenRequestCard`{`Number`,`Expiry`,`SecurityCode`,`Name`,`BillingAddress`}}). Resp `PaymentTokenResponse`:`.Id`(vault id),`.Customer.Id`,`.PaymentSource.Card`(`CardPaymentTokenEntity`{`LastDigits`,`Brand`(`CardBrand`),`Expiry`,`Name`}). Case A `CreatePaymentTokenError`:`TryGetError`[400,403,404,422,500]. src: Vault.md; Models/PaymentTokenRequest.cs, PaymentTokenRequestPaymentSource.cs, PaymentTokenRequestCard.cs, PaymentTokenResponse.cs, PaymentTokenResponsePaymentSource.cs, CardPaymentTokenEntity.cs, Customer.cs |
| `Vault.DeletePaymentToken` | `DeletePaymentToken(string id, …)` → `void`. Case A `DeletePaymentTokenError`:`TryGetError`[400,403,500]. src: Vault.md |
| `Vault.ListCustomerPaymentTokens` | `ListCustomerPaymentTokens(string customerId, int? pageSize=5, int? page=1, bool? totalRequired=false, …)` → `CustomerVaultPaymentTokensResponse{ .PaymentTokens[] , .TotalPages }`. Case A. (Secondary — primary card list is our own store.) src: Vault.md; Models/CustomerVaultPaymentTokensResponse.cs |
| `TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, …)`. Resp `SearchResponse`:`.TransactionDetails[]`(`TransactionDetails.TransactionInfo`→`TransactionInformation`{`.TransactionId`,`.TransactionStatus`,`.TransactionAmount`(`Money`),`.InvoiceId`,`.TransactionInitiationDate`}),`.TotalPages`,`.Page`. **Case B `SdkException<RawError>`** (no typed accessors). Pagination: `page`/`page_size`, loop `page` 1..`TotalPages`. src: TransactionSearch.md; Models/SearchResponse.cs, TransactionDetails.cs, TransactionInformation.cs |

Enums (build via static members / `Type.FromValue("wire")`; these are `StringEnum<T>`, not C# enums):
- `CheckoutPaymentIntent.Authorize`("AUTHORIZE") / `.Capture`. src Models/Enums/CheckoutPaymentIntent.cs
- `OrderStatus`: `.Created .Approved .Completed .Voided .PayerActionRequired`("PAYER_ACTION_REQUIRED"). src OrderStatus.cs
- `AuthorizationStatus`: `.Created .Captured .Denied .PartiallyCaptured .Voided .Pending`. src AuthorizationStatus.cs
- `CaptureStatus`: `.Completed .Declined .PartiallyRefunded .Pending .Refunded .Failed`. src CaptureStatus.cs
- `RefundStatus`: `.Completed .Cancelled .Failed .Pending`. src RefundStatus.cs
- `CardBrand` (read-only, for display). src Models/Enums/CardBrand.cs
- `VaultTokenRequestType.SetupToken` (only if setup-token flow used; primary flow is direct card, no setup token). src VaultTokenRequestType.cs

Error payload `Error` (Case A `out`): `.Name`(req) `.Message`(req) `.DebugId`(req, PayPal correlation id) `.Details[]`. src Models/Error.cs.
`RawError` (Case B / fallback): `.StatusCode` `.ReadAsString()` `.ReadAsJson<T>()`. src sdk-map.md.

## 3. Trap notes (hazard + skill pointer — deliberately unresolved)

- **T1 (Step C, all calls):** `System.Text.Json.JsonException` reaches the boundary from a drifted 2xx body (missing `required` member) as a NON-`SdkException`, and a non-2xx body that doesn't match `{Operation}Error` throws `JsonException` *while building the error object*, replacing the `SdkException` and destroying the status. `MUST load dotnet-error-handling`.
- **T2 (Step C, Case A vs B):** `SearchTransactions` is Case B (`SdkException<RawError>`) while every other op is Case A (`SdkException<{Op}Error>`) with `TryGetError`/`TryGetNoContent`/`TryGetRawError` — one catch ladder must handle both families and not assume typed accessors exist. `MUST load dotnet-error-handling`.
- **T3 (Step C, capture/void/refund):** these are `POST`; the SDK's default `HttpMethodsToRetry` never resends `POST`, so a socket failure mid-write is ambiguous, not auto-recovered. `MUST load dotnet-configuration-resilience`.
- **T4 (Step C, idempotency):** the generator injects `Idempotency-Key: Guid.NewGuid()` (fresh per call) — NOT a real key; the real caller-supplied keys are the `payPalRequestId` params. `MUST load dotnet-calling-endpoints`.
- **T5 (Step C, timeouts):** `RetryOptions.Timeout` is per-attempt not total; only a `CancellationToken` deadline bounds a whole call. `MUST load dotnet-configuration-resilience`.
- **T6 (Step C, SearchTransactions call):** 14 params, many optional-with-no-C#-default → positional binding mis-binds; call with named arguments. `MUST load dotnet-calling-endpoints`.
- **T7 (Step C, models):** response enums are `StringEnum<T>` compared via static members, unions/nested optionals are `T?` needing null-guards at every hop (`purchase_units?[0]?.payments?.authorizations?[0]?.id`); build enums with static members not string literals. `MUST load dotnet-models`.
- **T8 (Step C/D, sensitive data + logging):** request bodies carry raw PAN/CVV on authorize & vault; `LogRequestBody` logs JSON UNREDACTED and the `PAYPALSERVERSDKCLIENT_LOG` env var can force it on unless `LoggerFactory` is set explicitly. `MUST load dotnet-configuration-resilience`.
- **T9 (Step D, client lifetime):** the `HttpClient` must be long-lived via `IHttpClientFactory`, client wrapper reused, not rebuilt per request. `MUST load dotnet-client-initialization`.
- **T10 (Step D, auth):** a never-set credential is skipped silently and the call still goes out, surfacing as a 401 rather than a config error — hence the fail-fast in §5.1. `MUST load dotnet-authentication`.
- **T11 (Step G, tests):** the `HttpClient` ctor arg is the fake seam; assert the request the SDK built, cover error/decode paths, match xUnit+NSubstitute. `MUST load dotnet-testing`.

## 4. REQUIRED READING (load ALL before implementation — sheet omits their contents by design)

| Skill (load plugin-qualified `paypal-platforms-team:<name>`) | Governs |
| --- | --- |
| `dotnet-error-handling` | Step C error boundary (T1,T2) — always required |
| `dotnet-calling-endpoints` | Step C call sites, named args, real idempotency keys (T4,T6) |
| `dotnet-models` | Step C request/response mapping, StringEnum, nested optionals (T7) |
| `dotnet-configuration-resilience` | Step C/D retries, timeouts, logging/redaction (T3,T5,T8) |
| `dotnet-client-initialization` | Step D client construction & lifetime (T9) |
| `dotnet-authentication` | Step D credentials & fail-fast (T10) |
| `dotnet-testing` | Step G integration tests (T11) |

Mandatory hazard rows (verbatim): (a) a drifted/malformed **2xx** body surfaces as `System.Text.Json.JsonException` from deserialization, **not** `SdkException` — an SDK-exception-only catch ladder lets it escape; (b) a **non-2xx** body not matching its operation's `{Operation}Error` throws `JsonException` **while the error object is constructed**, replacing the `SdkException` and destroying the HTTP status.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` (ClientId, ClientSecret, Environment, Currency, BaseUrl) bound from `PayPal:` in Program.cs. A `ValidateOnStart` validator throws (host refuses to start) if ClientId, ClientSecret, Environment, or Currency is null/whitespace — **each part checked** (a blank part ≠ missing). BaseUrl optional. |
| 2 | Secret sourcing & rotation | Secrets from .NET user-secrets (loaded from `PAYPAL_*` env vars at setup; never written to repo). `AddPayPalServerSdkClient` builds `PayPalServerSdkClientOptions` **once at registration** and captures it in the singleton `PayPalServerSdkClient` — a rotated secret takes effect only on process restart. Documented; restart-to-rotate is acceptable for this app. |
| 3 | Total timeout budget | `RetryOptions.Timeout` is per-attempt. Each `IPaymentProcessor` call is bounded by a `CancellationToken` from a `CancellationTokenSource(TimeSpan)` (default 100s) created in the processor, passed as `ct:` — that is the real per-call budget. Retries left at SDK default (writes not resent, see #4). |
| 4 | Write-retry ownership | All money-moving calls (AuthorizeOrder, Capture, Void, Refund, CreateOrder, CreatePaymentToken) are `POST` — default `HttpMethodsToRetry`={GET,HEAD,PUT,OPTIONS} never resends them, so no silent double-charge from SDK retry. Ambiguous failures handled by app idempotency (#5). |
| 5 | Idempotency & ambiguous writes | Real keys = `payPalRequestId`. Deterministic per-order keys: `eshop-auth-{orderId}`, `eshop-capture-{orderId}`, `eshop-void-{orderId}`, `eshop-reauth-{orderId}-{n}`, `eshop-order-{orderId}`. Refund uses the **caller-supplied idempotency key** verbatim as `payPalRequestId`. App-level guards in `OrderPayment`: authorize/capture/void short-circuit if already in that state; refunds keyed by idempotency key in a dictionary (repeat key ⇒ return stored refund; distinct keys ⇒ separate partial refunds) and `sum(refunds) ≤ captured` enforced before any PayPal call. |
| 6 | Observability | Structured logs at Info (state transitions: authorized/captured/voided/refunded with orderId + PayPal ids), Warning (reauthorization, gaps), Error (declines, SDK errors). On any `SdkException` the PayPal `Error.DebugId` (correlation id) is logged. Request-body logging OFF (see #7). |
| 7 | Sensitive data | Authorize & vault requests carry raw PAN/CVV. Therefore: `options.Logging.LogRequestBody` stays **off** and `options.Logging.LoggerFactory` is assigned **explicitly** (via `AddPayPalServerSdkClient` DI, which sets it from `ILoggerFactory`) so the `PAYPALSERVERSDKCLIENT_LOG` env var cannot force body logging on. The app never logs card fields; only `LastDigits`/`Brand`/`Expiry` (already-masked) are persisted/returned. PAN/CVV never persisted. |
| 8 | Environment selection | SDK declares only `ServerEnvironment.Sandbox`. All deployments set `Environment=Sandbox`. `PayPal:BaseUrl` (optional) overrides `options.Server.Default.Sandbox.BaseUrl` — verified to also redirect the token request — keeping test/self-host traffic off the live host; unset ⇒ real sandbox `https://api-m.sandbox.paypal.com`. No production/live environment member exists, so there is nothing to accidentally target. |

## 6. Assumptions & Blockers

- **No Blockers.** Every required capability maps to an SDK operation above.
- Assumption (minor): direct-card authorize with sandbox test Visa returns no 3DS challenge. If PayPal returns `OrderStatus.PayerActionRequired` or a `payer-action`/3DS `Links.Rel`, the processor STOPS and returns a `ChallengeRequired` result (endpoint → 402/409 with a clear message); we do NOT build a browser approval round-trip (per task).
- Assumption (minor): amounts formatted to 2 decimals invariant (USD-style). Currency from `PayPal:Currency`. Matches "equal to the cent".
- Assumption (minor): `GET /api/payment-methods` is served from our own `SavedPaymentMethod` store (authoritative for ownership + resilient to PayPal reporting lag); PayPal `ListCustomerPaymentTokens` is available as a cross-check but not the source of truth.

## 7. Source labels
All rows above cite a map page or a map-named `Models/…`/`Errors/…` source file (resolved this session). Application-shaped
decisions (persistence, idempotency keys, endpoint contracts, status machine) are **YOUR CALL — not in the map** and decided here
against the task. No row is left open for a later lookup.
