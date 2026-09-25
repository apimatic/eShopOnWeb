# PayPal Server SDK integration plan — eShopOnWeb `src/PublicApi`

Ground truth for every SDK fact is the SDK map (`sdk-map.md` + `map/operations/*`) and the
map-named source files, read this session. This file carries the plan and the contract sheet.
Nothing here is written from memory; every row cites its map page or declaring file.

## 1. Scope & sequence

New capability is **additive**. The existing `Order`/`OrderItem` aggregate is reused unchanged
for items/total/buyer. A new **`Payment`** aggregate (1:1 with `Order` by `OrderId`) carries the
new money + fulfilment state and every PayPal id/status; a new **`SavedPaymentMethod`** aggregate
holds vaulted-card metadata (never card details). All flows are driven through `src/PublicApi`.

Build order:
1. Reference & build the PayPal SDK from source; add `PayPal:` config binding + credential
   fail-fast in `Program.cs`; load credentials into user-secrets; DI-register a singleton
   `PayPalServerSdkClient` via `IHttpClientFactory`.  (ops: none)
2. Domain: `Payment`, `PaymentRefund`, `SavedPaymentMethod` entities + enums; EF `DbSet`s +
   `IEntityTypeConfiguration`s (auto-registered in Infrastructure); reused via `IRepository<>`.  (ops: none)
3. `IPayPalGateway`/`PayPalGateway` — the only SDK caller. One method per operation used, an
   error-translation boundary, deterministic idempotency keys, unknown-outcome settle, paged
   reconciliation.  (ops: CreateOrder, AuthorizeOrder, GetOrder, CaptureAuthorizedPayment,
   ReauthorizePayment, GetAuthorizedPayment, VoidPayment, RefundCapturedPayment,
   CreatePaymentToken, DeletePaymentToken, SearchTransactions)
4. `IPaymentService`/`PaymentService` — application orchestration mapping PayPal results onto the
   `Payment` aggregate + ownership scoping.  (ops: via gateway)
5. PublicApi endpoints under `/api/` (MinimalApi.Endpoint `IEndpoint`, existing conventions).
6. Tests + end-to-end self-verification against sandbox test card.

A capability the map lacks would be a Blocker (§6); none found — all flows map to operations above.

## 2. CONTRACT SHEET

> ⚠ **Signatures are generated code, verbatim.** Each operation that takes input takes **one
> request record** as its first parameter, built with an object initializer whose property names
> are the record's own — never flat arguments. An operation with no inputs takes none.
> ⚠ **Every SDK type is fully-qualified with the namespace its source path implies**, taken from
> the path the map gives for THAT type (records `PayPalServerSdk.Models`, enums
> `PayPalServerSdk.Models.Enums`, request records `PayPalServerSdk.Requests.<Controller>`, typed
> errors `PayPalServerSdk.Errors`, client/options root `PayPalServerSdk`, `RawError`
> `PayPalServerSdk.Core.ErrorResponse`, `ApiException<T>` `PayPalServerSdk.Core.Exceptions`).

Client construction (source: `sdk-map.md` §Getting a client / §Servers & auth; `PayPalServerSdkClient.cs`,
`PayPalServerSdkClientOptions.cs`, `AuthSchemes.cs`, `Servers/DefaultOptions.cs`):
- `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` — only ctor.
- Options: `Oauth2 = new OAuth2ClientCredentials { ClientId, ClientSecret }`
  (ns `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`); `Environment = ServerEnvironment.Sandbox`
  (ns `PayPalServerSdk.Servers`; **only** environment; `ClosedStringEnum`, `.Match` throws on unknown).
- **BaseUrl override:** `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>` when set. Confirmed in
  `AuthSchemes.cs`: the OAuth token URL is `server.Default("/v1/oauth2/token")`, so this override reaches
  **every** call including the token request. Leave default `https://api-m.sandbox.paypal.com` when unset.
- Auth: OAuth2 client-credentials; token from `<base>/v1/oauth2/token`. A credential never set is skipped
  (an auth failure can mean *no* credential sent) — hence credential fail-fast (§5 row 1).

| Operation (`client.X`) | Signature — required members | Body model + fields used | Response envelope → fields read | Error case + accessors | Source |
| --- | --- | --- | --- | --- | --- |
| `Orders.CreateOrder` | `CreateOrder(CreateOrderRequest req)` — req `Body` (req); set `PayPalRequestId` (idempotency, mandatory for single-step card create), `Prefer="return=representation"` | `OrderRequest`: `Intent`(req, `CheckoutPaymentIntent.Authorize`), `PurchaseUnits`(req, `[PurchaseUnitRequest{ Amount(req)=AmountWithBreakdown{CurrencyCode,Value}, InvoiceId, CustomId }]`), `PaymentSource=PaymentSource{ Card=CardRequest{ Number,Expiry,SecurityCode,Name,BillingAddress } OR CardRequest{ VaultId } }` | `Order`: `Id`, `Status`(`OrderStatus`), `PurchaseUnits[].Payments.Authorizations[]`(`AuthorizationWithAdditionalData`: `Id`,`Status`,`ExpirationTime`) | A: `TryGetError(out Error)`[400,401,422] · `TryGetRawError` | Orders.md; `Requests/Orders/CreateOrderRequest.cs`; `Models/OrderRequest.cs`,`PaymentSource.cs`,`CardRequest.cs`,`PurchaseUnitRequest.cs`,`AmountWithBreakdown.cs` |
| `Orders.AuthorizeOrder` | `AuthorizeOrder(AuthorizeOrderRequest req)` — req `Id`; set `PayPalRequestId`, `Prefer="return=representation"`; `Body` optional (omit — payment_source already on order) | — (no body) | `OrderAuthorizeResponse`: `Id`, `Status`, `PurchaseUnits[].Payments.Authorizations[]`(`Id`,`Status`,`ExpirationTime`,`Amount`) | A: `TryGetError(out Error)`[400,401,403,404,422,500] · `TryGetRawError` | Orders.md; `Requests/Orders/AuthorizeOrderRequest.cs`; `Models/OrderAuthorizeResponse.cs` |
| `Orders.GetOrder` | `GetOrder(GetOrderRequest req)` — req `Id`; `Fields` optional | — | `Order`: `Status`, `PurchaseUnits[].Payments.{Authorizations,Captures,Refunds}` | A: `TryGetError(out Error)`[401,404] · `TryGetRawError` | Orders.md; `Requests/Orders/GetOrderRequest.cs`; `Models/Order.cs`,`PaymentCollection.cs` |
| `Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(CaptureAuthorizedPaymentRequest req)` — req `AuthorizationId`; set `PayPalRequestId` (45-day key), `Prefer="return=representation"`; `Body` optional (`CaptureRequest{ FinalCapture=true }` for full capture) | `CaptureRequest`: `Amount`(`Money`) optional, `FinalCapture` | `CapturedPayment`: `Id`, `Status`(`CaptureStatus`), `Amount`(`Money`), `SellerReceivableBreakdown`(`GrossAmount`,`PaypalFee`,`NetAmount` — all `Money`) | A: `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` | Payments.md; `Requests/Payments/CaptureAuthorizedPaymentRequest.cs`; `Models/CaptureRequest.cs`,`CapturedPayment.cs`,`SellerReceivableBreakdown.cs`,`Money.cs` |
| `Payments.ReauthorizePayment` | `ReauthorizePayment(ReauthorizePaymentRequest req)` — req `AuthorizationId`; set `PayPalRequestId`; `Body=ReauthorizeRequest{ Amount }` optional (supports only `amount`) | `ReauthorizeRequest`: `Amount`(`Money`) | `PaymentAuthorization`: `Id`, `Status`(`AuthorizationStatus`), `ExpirationTime` | A: `TryGetError(out Error)`[400,401,403,404,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` | Payments.md; `Requests/Payments/ReauthorizePaymentRequest.cs`; `Models/ReauthorizeRequest.cs`,`PaymentAuthorization.cs` |
| `Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(GetAuthorizedPaymentRequest req)` — req `AuthorizationId` | — | `PaymentAuthorization`: `Id`,`Status`,`ExpirationTime`,`Amount` | A: `TryGetError`[401,403,404] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; `Requests/Payments/GetAuthorizedPaymentRequest.cs`; `Models/PaymentAuthorization.cs` |
| `Payments.VoidPayment` | `VoidPayment(VoidPaymentRequest req)` — req `AuthorizationId`; set `PayPalRequestId` | — (EmptyBody) | `PaymentAuthorization`: `Status`(→`Voided`) | A: `TryGetError`[401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; `Requests/Payments/VoidPaymentRequest.cs` |
| `Payments.RefundCapturedPayment` | `RefundCapturedPayment(RefundCapturedPaymentRequest req)` — req `CaptureId`; set `PayPalRequestId` = **caller idempotency key**; `Body=RefundRequest{ Amount, CustomId, InvoiceId }` optional (empty body = full refund; `Amount` = partial) | `RefundRequest`: `Amount`(`Money`), `CustomId`, `InvoiceId` | `Refund`: `Id`, `Status`(`RefundStatus`), `Amount`(`Money`) | A: `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; `Requests/Payments/RefundCapturedPaymentRequest.cs`; `Models/RefundRequest.cs`,`Refund.cs` |
| `Vault.CreatePaymentToken` | `CreatePaymentToken(CreatePaymentTokenRequest req)` — req `Body`; set `PayPalRequestId` (3-hour key) | `PaymentTokenRequest`: `PaymentSource`(req, `PaymentTokenRequestPaymentSource{ Card=PaymentTokenRequestCard{ Number,Expiry,SecurityCode,Name,BillingAddress } }`), `Customer`(`Customer{ Id }` optional) | `PaymentTokenResponse`: `Id`(vault id), `PaymentSource.Card`(`CardPaymentTokenEntity`: `LastDigits`,`Brand`,`Expiry`,`Name`) | A: `TryGetError`[400,403,404,422,500] · `TryGetRawError` | Vault.md; `Requests/Vault/CreatePaymentTokenRequest.cs`; `Models/PaymentTokenRequest.cs`,`PaymentTokenRequestPaymentSource.cs`,`PaymentTokenRequestCard.cs`,`PaymentTokenResponse.cs`,`PaymentTokenResponsePaymentSource.cs`,`CardPaymentTokenEntity.cs` |
| `Vault.DeletePaymentToken` | `DeletePaymentToken(DeletePaymentTokenRequest req)` — req `Id` | — | `void` (Task) | A: `TryGetError`[400,403,500] · `TryGetRawError` | Vault.md; `Requests/Vault/DeletePaymentTokenRequest.cs` |
| `TransactionSearch.SearchTransactions` | `SearchTransactions(SearchTransactionsRequest req)` — req `StartDate`,`EndDate` (RFC-3339, ≤31-day range); `Page`(1-based),`PageSize`(≤500),`Fields`,`BalanceAffectingRecordsOnly` optional | — (query params) | `SearchResponse`: `TransactionDetails[]`(`TransactionInfo`=`TransactionInformation`: `TransactionId`,`InvoiceId`,`TransactionAmount`(`Money`),`FeeAmount`,`TransactionStatus`,`TransactionInitiationDate`,`TransactionEventCode`), `Page`,`TotalPages`,`TotalItems` | **B**: `ApiException<RawError>` (`StatusCode`,`ReadAsString`,`ReadAsJson<T>`) | TransactionSearch.md; `Requests/TransactionSearch/SearchTransactionsRequest.cs`; `Models/SearchResponse.cs`,`TransactionDetails.cs`,`TransactionInformation.cs` |

Enums (values needed; source `Models/Enums/<Name>.cs` — `OpenStringEnum`, compare to static members, `.Match` with `otherwise` arm):
- `CheckoutPaymentIntent`: `Authorize`("AUTHORIZE"), `Capture`("CAPTURE").
- `OrderStatus`: `Created`,`Saved`,`Approved`,`Voided`,`Completed`,`PayerActionRequired`("PAYER_ACTION_REQUIRED" — 3DS/browser challenge → **stop & report**, do not build approval round-trip).
- `AuthorizationStatus`: `Created`,`Captured`,`Denied`,`PartiallyCaptured`,`Voided`,`Pending`.
- `CaptureStatus`: `Completed`,`Declined`,`PartiallyRefunded`,`Pending`,`Refunded`,`Failed`.
- `RefundStatus`: `Cancelled`,`Failed`,`Pending`,`Completed`.
- `TokenType`: **only** `BillingAgreement`("BILLING_AGREEMENT") — there is **no** card/payment-method-token value, so a vaulted **card** is paid via `CardRequest.VaultId`, never `PaymentSource.Token`.

`Money` (`Models/Money.cs`): `CurrencyCode`(req, 3 chars), `Value`(req, **string**, regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`). Amounts cross the wire as strings → format order total `ToString("0.00", InvariantCulture)` for USD.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The `CaptureId` refunded must be one this order's capture produced (`CapturedPayment.Id` stored on `Payment`) | `RefundCapturedPayment` ← `CaptureAuthorizedPayment` | `PaymentService.RefundAsync` before calling gateway (reject if `Payment.CaptureId` null) |
| The `AuthorizationId` captured/reauthorized/voided must be the one this order's authorize produced (`Payment.AuthorizationId`) | `CaptureAuthorizedPayment`/`ReauthorizePayment`/`VoidPayment` ← `CreateOrder`+`AuthorizeOrder` | `PaymentService` fulfil/cancel guards |
| A saved-card `VaultId` used to pay must be one **this caller** vaulted (`SavedPaymentMethod.BuyerId == caller` & not deleted) | `CreateOrder`(card.VaultId) ← `CreatePaymentToken` / not `DeletePaymentToken` | `PaymentService.AuthorizeAsync` resolves `savedPaymentMethodId`→`VaultId` scoped to caller |
| Cumulative refunded amount must never exceed captured gross | `RefundCapturedPayment` (repeated) | `PaymentService.RefundAsync`: `RefundedAmount + amount ≤ CapturedGross` |
| Reconciliation `StartDate`/`EndDate` span ≤ 31 days per PayPal call | `SearchTransactions` | `ReconciliationService` chunks a wider range into ≤31-day windows |

## 3. Trap notes (name hazard + skill; do not resolve here)

- **Client/DI:** the `HttpClient`/handler pipeline must be long-lived & reused (not per-request), while the SDK wrapper lifetime is a separate question — gets the singleton-vs-transient split wrong and you leak sockets or capture stale config. `MUST load dotnet-client-initialization`.
- **Auth:** where credentials are set relative to client construction, and that an unset credential is silently skipped rather than throwing. `MUST load dotnet-authentication`.
- **Calling endpoints:** which members are truly required vs. carry a spec default, and that the real idempotency key is the request record's `PayPalRequestId`, not the generator-injected per-call `Idempotency-Key` header. `MUST load dotnet-calling-endpoints`.
- **Models:** enums are `OpenStringEnum` (no C# enum, no public ctor — use `Match`/`TryGetKnownValue`), unions use `TryGet…`, `Money.Value` is a string, response bodies keep unknown fields in `AdditionalProperties`. `MUST load dotnet-models`.
- **Error handling:** Case A vs Case B per operation, the extra `TryGetNoContent[500]` on Payments ops, and that a drifted 2xx or mismatched error body surfaces as `ResponseDeserializationException` (an `ApiException` that is **not** `ApiException<TError>`) which a typed-only catch ladder lets escape. `MUST load dotnet-error-handling`.
- **Config/resilience:** `Timeout` is per-attempt not total; `HttpMethodsToRetry` excludes POST/DELETE so my writes are never auto-resent (I own unknown-outcome settle); paging semantics for `SearchTransactions`; and `LogRequestBody` logs JSON bodies **unredacted** (card data). `MUST load dotnet-configuration-resilience`.
- **Testing:** the `HttpClient` ctor arg is the fake seam; match the project's MSTest/xUnit style. `MUST load dotnet-testing`.

## 4. REQUIRED READING (load all before implementation starts; this sheet deliberately omits their contents)

- `sdk-skills-test-dev:dotnet-client-initialization` — step 1 (client + DI).
- `sdk-skills-test-dev:dotnet-authentication` — step 1 (credentials).
- `sdk-skills-test-dev:dotnet-calling-endpoints` — step 3 (first SDK calls, request records, idempotency).
- `sdk-skills-test-dev:dotnet-models` — step 3 (building card/money/enum models, reading responses).
- `sdk-skills-test-dev:dotnet-error-handling` — step 3 (error boundary; always required).
- `sdk-skills-test-dev:dotnet-configuration-resilience` — step 1/3 (timeout budget, retries, paging, logging/redaction).
- `sdk-skills-test-dev:dotnet-testing` — step 6 (integration tests).

Mandatory hazard row: a body that does not match its declared type — a drifted/malformed **2xx**
(missing `required` member) or a **non-2xx** body not matching the operation's `{Operation}Error`
shape — surfaces as `ResponseDeserializationException` (an `ApiException` keeping the HTTP status
and target type, **not** an `ApiException<TError>`). A catch ladder handling only
`ApiException<TError>` lets it escape; the boundary must also catch `ResponseDeserializationException`
(or `ApiException`). `MUST load dotnet-error-handling`.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind `PayPal:` section in `Program.cs`; a `PayPalOptions` validator throws at startup if `ClientId`, `ClientSecret`, or `Currency` is null/blank, or `Environment` does not map to a known `ServerEnvironment` (only `Sandbox`). **Both** credential parts checked (a blank secret ≠ a missing one). Refuses to start rather than surfacing a 401 on first call. |
| 2 | Secret sourcing & rotation | Credentials read from env → loaded into **.NET user-secrets** (values never in repo). DI builds `PayPalServerSdkClientOptions` **once at registration** and captures it in the singleton client → a rotated secret takes effect only on **process restart** (documented; acceptable for this sandbox integration). |
| 3 | Total timeout budget | SDK `Timeout` is **per-attempt**. Every gateway call gets a **linked `CancellationTokenSource`** with a total deadline (default 60 s) linked to `HttpContext.RequestAborted`, passed as the `cancellationToken` — the only thing bounding the whole call incl. retries. Set in `PayPalGateway`. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = `GET,HEAD,PUT,OPTIONS`. All my writes are **POST/DELETE** → SDK never auto-resends them; no duplicate charge from transport retries. I own unknown-outcome settle (row 11). |
| 5 | Idempotency & ambiguous writes | Every write sends a **deterministic `PayPal-Request-Id`** namespaced by the **globally-unique `InvoiceId`** (`ESHOP-{orderId}-{guid}`, stored once on the `Payment`) — **not** the raw eShop order id, which resets to 1,2,3… on every in-memory-DB restart and would otherwise let a 45-day capture/refund key collide with a prior run and make PayPal replay a stale resource (found and fixed during self-verification). Keys: create `paypal-order-{invoiceId}`, authorize `authorize-{invoiceId}`, capture `capture-{invoiceId}`, reauthorize `reauth-{invoiceId}-{authId}`, void `void-{invoiceId}`, refund `refund-{invoiceId}-{callerKey}` (caller-supplied key, scoped by invoice), vault `vault-{buyerHash}-{cardFingerprint}`. PayPal stores keys (create 6 h / payments 45 d / vault 3 h) and replays the same result → double-click is safe. The injected `Idempotency-Key` header (fresh GUID) is **not** used as the key. |
| 6 | Observability | Info: operation + eShop `orderId` + PayPal ids + resulting status. Warning: renewable-authorization renewals. Error: failures with PayPal **`debug_id`** extracted from the `Error`/`RawError` body reaching our logs. `LogRequestBody` stays **off**. |
| 7 | Sensitive data | `CardRequest`/`PaymentTokenRequestCard` carry PAN/CVV/expiry. `options.Logging.LogRequestBody` stays **off** and `options.Logging.LoggerFactory` is set explicitly so the `PAYPALSERVERSDKCLIENT_LOG` env var cannot force body logging on. Card fields flow straight into SDK models — **never persisted** (only vault id + last4/brand/expiry are stored) and **never logged** by our own code. |
| 8 | Environment selection | One server group, one environment (`Sandbox`). `options.Environment = ServerEnvironment.Sandbox`; base URL default sandbox, or `options.Server.Default.Sandbox.BaseUrl = PayPal:BaseUrl` verbatim when set (reaches the token request too). `PayPal:Environment` must map to `Sandbox` else fail-fast — keeps a stray env value from silently hitting sandbox. No live environment exists in this SDK, so all traffic is sandbox by construction. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Authorize (CreateOrder+AuthorizeOrder) | PayPal idempotency store, keyed by `PayPal-Request-Id` `paypal-order-{invoiceId}` / `authorize-{invoiceId}` | PayPal replays the first result for a repeated key (no second authorization created); the eShop `Payment` also short-circuits once `Status == Authorized` | Gateway returns the replayed `Order`/`OrderAuthorizeResponse`; `PaymentService.PayAsync` maps it | `PaymentService.PayAsync`, `PayPalGateway.AuthorizeAsync` |
| Capture (fulfil) | PayPal idempotency store, keyed by `PayPal-Request-Id` `capture-{invoiceId}` (45-day) | PayPal replays the first capture for a repeated key; the `Payment` also returns early once `Status == Fulfilled` | Gateway returns the replayed `CapturedPayment` | `PaymentService.FulfilAsync`, `PayPalGateway.DoCapture` |
| Refund | PayPal idempotency store, keyed by `PayPal-Request-Id` `refund-{invoiceId}-{callerKey}` (45-day) | PayPal replays the first refund for a repeated key; the `Payment` also returns the recorded refund when the caller key is already stored | Gateway returns the replayed `Refund` | `PaymentService.RefundAsync` (`FindRefundByKey`), `PayPalGateway.RefundAsync` |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| Reconciliation `SearchTransactions` | `SearchResponse.TotalPages` (page size ≤500); walked from page 1 → `TotalPages` per ≤31-day window | Report DTO field `CoveredAllPages` (bool) + `PagesRead`/`TotalPages` — set false only if a hard safety cap is hit | `ReconciliationService.BuildReportAsync` |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code |
| --- | --- | --- | --- |
| CreateOrder | retry `CreateOrder` (idempotent replay) then `GetOrder` | `PayPal-Request-Id` `paypal-order-{invoiceId}`; then PayPal order id | `PayPalGateway.AuthorizeAsync` catch (`SdkConnectionException`/`SdkTimeoutException`) |
| AuthorizeOrder | `GetOrder` | PayPal order id (`Payment.PayPalOrderId`) → `PurchaseUnits[].Payments.Authorizations[]` | `PayPalGateway.AuthorizeExistingOrder` catch |
| CaptureAuthorizedPayment | `GetOrder` (→ captures) | PayPal order id → `PurchaseUnits[].Payments.Captures[]` | `PayPalGateway.DoCapture` catch |
| VoidPayment | `GetAuthorizedPayment` | `AuthorizationId` (status `Voided`) — also the settle path for the 204-No-Content void body | `PayPalGateway.ConfirmVoided` |
| RefundCapturedPayment | retry `RefundCapturedPayment` (idempotent replay) | `PayPal-Request-Id` `refund-{invoiceId}-{callerKey}` | `PayPalGateway.RefundAsync` catch |

## 6. Assumptions & Blockers

- **Assumption (minor):** `POST /api/orders/{orderId}/refunds` is **shopper-owner-scoped**, not admin —
  the task's explicit restriction names only fulfil/cancel/reconciliation as operator actions and
  states "every other endpoint is shopper-scoped." (Narrative also mentions an operator refunding;
  the explicit rule governs.)
- **Assumption (minor):** `POST /api/orders` accepts an optional `shipToAddress`; when omitted a
  placeholder address is used, since the reused `Order` requires a non-null `Address` and there is
  no storefront UI. Amounts always come from catalog prices; any client-sent price is ignored.
- **Assumption (minor):** saved-card **listing** is served from the app's own `SavedPaymentMethod`
  table (source of truth for ownership + safe descriptor), populated by `Vault.CreatePaymentToken`;
  `Vault.ListCustomerPaymentTokens` is not called. Every PayPal *interaction* (vault create/delete,
  pay-by-vault-id) still goes through the plugin. `YOUR CALL — not in the map`.
- **UNVERIFIED (settled by defensive code):** whether `CreateOrder(intent=AUTHORIZE, payment_source.card)`
  single-steps to an authorization or leaves the order `APPROVED` requiring a separate `AuthorizeOrder`
  is live-traffic behaviour. Gateway inspects the `CreateOrder` `Order`: if
  `PurchaseUnits[].Payments.Authorizations[]` already carries an authorization use it; else call
  `AuthorizeOrder`; if `Status == PayerActionRequired` → stop & surface a 3DS/browser-challenge error
  (do not build an approval round-trip). Confirmed empirically in self-verification.
- No Blockers: every required capability maps to an operation in §2.
