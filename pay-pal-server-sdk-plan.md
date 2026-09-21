# PayPal Server SDK (.NET) integration plan — eShopOnWeb PublicApi

Payments + saved cards, additive to the existing catalog/basket/order flow. All PayPal traffic
goes through the **PayPal Server SDK .NET SDK** (`PayPalServerSdk`, spec 2.29), vendored from
source (not on NuGet). Contracts below come from the SDK map + declaring source files.

---

## 1. Scope & sequence

The SDK is not published to NuGet, so it is **vendored** into the repo as a buildable project
`src/PayPalServerSdk/` and referenced by `Infrastructure` (the map's *install from source*).

| # | Step | PayPal operations used |
| --- | --- | --- |
| S1 | Vendor SDK source → `src/PayPalServerSdk/`; add to both solutions; `ProjectReference` from `Infrastructure`. | — |
| S2 | `PayPalSettings` bound from `PayPal:` section; **fail-fast** validator; register SDK client (DI) + `PayPalPaymentGateway`. | client construction, `Oauth2` |
| S3 | Domain (ApplicationCore): `OrderPayment` (1:1 Order), `PaymentRefund` (child), `SavedPaymentMethod` aggregates; `PaymentStatus` enum; `IPayPalPaymentGateway` + result DTOs; domain exceptions. | — |
| S4 | Infrastructure: `PayPalPaymentGateway : IPayPalPaymentGateway` over the SDK; EF configs; gateway/error mapping. | all of §2 |
| S5 | PublicApi: app services + `IEndpoint` endpoints (Orders + PaymentMethods). | via gateway |
| S6 | Build; verify end-to-end on sandbox (authorize, capture, refund, saved-card reuse, void, reconciliation). | — |

**Endpoint → operation map** (each caller action stays separately invocable):

| Endpoint | Role | PayPal call(s) |
| --- | --- | --- |
| `POST /api/orders` | shopper | none (eShop order only; `OrderPayment` = `AwaitingPayment`) |
| `POST /api/orders/{id}/pay` | shopper | `Orders.CreateOrder` (intent AUTHORIZE, card **or** `card.vault_id`) → `Orders.AuthorizeOrder` |
| `POST /api/orders/{id}/fulfil` | **admin** | *(if stale)* `Payments.ReauthorizePayment` → `Payments.CaptureAuthorizedPayment` |
| `POST /api/orders/{id}/cancel` | **admin** | `Payments.VoidPayment` |
| `POST /api/orders/{id}/refunds` | shopper (own order) | `Payments.RefundCapturedPayment` |
| `GET /api/my-orders` | shopper | none (reads own persisted state) |
| `GET /api/reconciliation?from&to` | **admin** | `TransactionSearch.SearchTransactions` (all pages) |
| `POST /api/payment-methods` | shopper | `Vault.CreatePaymentToken` (raw card → token) |
| `GET /api/payment-methods` | shopper | none (own persisted cards) |
| `DELETE /api/payment-methods/{id}` | shopper (own) | `Vault.DeletePaymentToken` |

Operator set = **fulfil, cancel, reconciliation** (admin role `Administrators`), per the task's
explicit rule; every other endpoint is shopper-scoped on the caller's own data (this includes
refunds). Staleness handling at fulfil uses `Payments.GetAuthorizedPayment` to read status /
`expiration_time`; an authorization that cannot be renewed surfaces an operator-actionable error.

---

## 2. CONTRACT SHEET

> ⚠ **Signatures are generated code, verbatim.** Every parameter name is the literal C#
> identifier; named arguments must use them exactly (the cancellation-token parameter is `ct`).
> ⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**
> (`Models/` → `PayPalServerSdk.Models`; `Models/Enums/` → `PayPalServerSdk.Models.Enums`;
> `Errors/` → `PayPalServerSdk.Errors`; `Core/...` per its own file). Take each type's namespace
> from the path the map gives for THAT type.

Client construction (source `PayPalServerSdkClient.cs`, `PayPalServerSdkClientOptions.cs`,
`ServiceCollectionExtensions.cs`, `AuthSchemes.cs`, `Servers/DefaultOptions.cs`):

- Ctor: `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` — only ctor. DI helper `services.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>)` builds options **once at registration** and captures them in a **singleton** (uses `IHttpClientFactory`).
- Auth: `options.Oauth2 = new OAuth2ClientCredentials { ClientId=…, ClientSecret=… }` (ns `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`). Token fetched from `{base}/v1/oauth2/token`.
- Environment: `options.Environment = ServerEnvironment.Sandbox` (ns `PayPalServerSdk.Servers`) — **only member declared**; `Sandbox` → `https://api-m.sandbox.paypal.com`.
- BaseUrl override: `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>`. **Verified in source** (`AuthSchemes.cs`: token URL is `server.Default("/v1/oauth2/token")`) that this override applies to the token request **and** every API call — satisfies "use it verbatim for every call including the token". Source: `Server.cs`, `Servers/DefaultOptions.cs`.

### Operation rows

| Op | Controller · signature | Request model → fields set | Response envelope → fields read | Error | Source |
| --- | --- | --- | --- | --- | --- |
| CreateOrder | `client.Orders.CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", …)` | `OrderRequest{ Intent(required)=CheckoutPaymentIntent.Authorize; PurchaseUnits(required)=[PurchaseUnitRequest{ Amount(required)=AmountWithBreakdown{CurrencyCode,Value}, InvoiceId=<order ref>, CustomId=<order ref> }]; PaymentSource=PaymentSource{ Card=CardRequest{…} } }` | `Order{ Id, Status(OrderStatus) }` | A `SdkException<CreateOrderError>`; `TryGetError(out Error)`[400,401,422] | ops/Orders.md; Models/OrderRequest.cs, PurchaseUnitRequest.cs, AmountWithBreakdown.cs, PaymentSource.cs, CardRequest.cs, Order.cs |
| AuthorizeOrder | `client.Orders.AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", …)` | `body=null` (card already on the order); pass `payPalRequestId` for idempotency | `OrderAuthorizeResponse{ Id, Status, PurchaseUnits[0].Payments.Authorizations[0]{ Id, Status(AuthorizationStatus), ExpirationTime, Amount } }` | A `SdkException<AuthorizeOrderError>`; `TryGetError`[400,401,403,404,422,500] | ops/Orders.md; Models/OrderAuthorizeResponse.cs, PurchaseUnit.cs, PaymentCollection.cs, AuthorizationWithAdditionalData.cs |
| GetAuthorizedPayment | `client.Payments.GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, …)` | — | `PaymentAuthorization{ Id, Status(AuthorizationStatus), ExpirationTime, Amount }` | A `SdkException<GetAuthorizedPaymentError>`; `TryGetError`[401,403,404]·`TryGetNoContent(out RawError)`[500] | ops/Payments.md; Models/PaymentAuthorization.cs |
| ReauthorizePayment | `client.Payments.ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", …)` | `ReauthorizeRequest{ Amount=Money{CurrencyCode,Value} }` (purpose: renew a stale hold at the order total) | `PaymentAuthorization{ Id, Status, ExpirationTime }` | A `SdkException<ReauthorizePaymentError>`; `TryGetError`[400,401,403,404,422]·`TryGetNoContent`[500] | ops/Payments.md; Models/ReauthorizeRequest.cs, PaymentAuthorization.cs |
| CaptureAuthorizedPayment | `client.Payments.CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", …)` | `CaptureRequest{ Amount=Money{…} (purpose: capture exact order total), FinalCapture=true (purpose: no further captures on this auth), InvoiceId=<order ref> }`; `payPalRequestId` for idempotency | `CapturedPayment{ Id, Status(CaptureStatus), Amount, SellerReceivableBreakdown{ GrossAmount(required), PaypalFee, NetAmount } }` | A `SdkException<CaptureAuthorizedPaymentError>`; `TryGetError`[400,401,403,404,409,422]·`TryGetNoContent`[500] | ops/Payments.md; Models/CaptureRequest.cs, CapturedPayment.cs, SellerReceivableBreakdown.cs, Money.cs |
| VoidPayment | `client.Payments.VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", …)` | — (note param order: mock, auth, request) | `PaymentAuthorization{ Id, Status=Voided }` | A `SdkException<VoidPaymentError>`; `TryGetError`[401,403,404,409,422]·`TryGetNoContent`[500] | ops/Payments.md; Models/PaymentAuthorization.cs |
| RefundCapturedPayment | `client.Payments.RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", …)` | `RefundRequest{ Amount=Money{…} (purpose: partial-refund amount; omit → full refund by provider default), InvoiceId=<order ref> }`; `payPalRequestId=<caller idempotency key>` | `Refund{ Id, Status(RefundStatus), Amount }` | A `SdkException<RefundCapturedPaymentError>`; `TryGetError`[400,401,403,404,409,422]·`TryGetNoContent`[500] | ops/Payments.md; Models/RefundRequest.cs, Refund.cs, Money.cs |
| CreatePaymentToken | `client.Vault.CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, …)` | `PaymentTokenRequest{ PaymentSource(required)=PaymentTokenRequestPaymentSource{ Card=PaymentTokenRequestCard{ Number, Expiry, SecurityCode, Name, BillingAddress } } }` (Customer omitted → ownership tracked in our DB) | `PaymentTokenResponse{ Id, PaymentSource.Card=CardPaymentTokenEntity{ LastDigits, Brand(CardBrand), Expiry, Name } }` | A `SdkException<CreatePaymentTokenError>`; `TryGetError`[400,403,404,422,500] | ops/Vault.md; Models/PaymentTokenRequest.cs, PaymentTokenRequestPaymentSource.cs, PaymentTokenRequestCard.cs, PaymentTokenResponse.cs, PaymentTokenResponsePaymentSource.cs, CardPaymentTokenEntity.cs |
| DeletePaymentToken | `client.Vault.DeletePaymentToken(string id, …)` | — | `void` (Task) | A `SdkException<DeletePaymentTokenError>`; `TryGetError`[400,403,500] | ops/Vault.md |
| SearchTransactions | `client.TransactionSearch.SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, …)` | `startDate`/`endDate` = RFC-3339 date-time, **seconds required**, max range **31 days** (remarks); pass 8 filters as `null`; page over `TotalPages` | `SearchResponse{ TransactionDetails[]{ TransactionInfo=TransactionInformation{ TransactionId, PaypalReferenceId, TransactionAmount(Money), FeeAmount, TransactionStatus, TransactionInitiationDate, InvoiceId, CustomField } }, Page, TotalPages, TotalItems }` | **B** `SdkException<RawError>` (no typed accessors) | ops/TransactionSearch.md; Models/SearchResponse.cs, TransactionDetails.cs, TransactionInformation.cs |

Notes on omitted optional fields (deliberate, use provider defaults):
- `OrderRequest`: `ProcessingInstruction`, `Payer`, `ApplicationContext` omitted → provider default (no payer-action/wallet flow; card is server-side).
- `CardRequest`: only `Name`, `Number`, `Expiry`, `SecurityCode`, `BillingAddress` set for a one-off; **`VaultId` set instead** (mutually exclusive) when paying with a saved card. `Attributes`/`ExperienceContext`/`StoredCredential` omitted → no 3-DS/vault-on-purchase side effects.
- `RefundRequest.Amount` omitted **only** for a full refund; partial refunds set it.
- `CaptureRequest.FinalCapture=true` set on purpose (single full capture of the hold).

### Enum values (source `Models/Enums/…`)

| Enum | Members used (C# · wire) |
| --- | --- |
| `CheckoutPaymentIntent` | `Authorize` · `AUTHORIZE` |
| `OrderStatus` | `Created`·CREATED, `Approved`·APPROVED, `Completed`·COMPLETED, `PayerActionRequired`·PAYER_ACTION_REQUIRED, `Voided`·VOIDED |
| `AuthorizationStatus` | `Created`·CREATED, `Captured`·CAPTURED, `PartiallyCaptured`·PARTIALLY_CAPTURED, `Voided`·VOIDED, `Denied`·DENIED, `Pending`·PENDING, `Expired` *(read defensively via `.Value`; not enumerated in source list)* |
| `CaptureStatus` | `Completed`·COMPLETED, `Pending`·PENDING, `Declined`·DECLINED, `PartiallyRefunded`·PARTIALLY_REFUNDED, `Refunded`·REFUNDED, `Failed`·FAILED |
| `RefundStatus` | `Completed`·COMPLETED, `Pending`·PENDING, `Cancelled`·CANCELLED, `Failed`·FAILED |
| `CardBrand` | read-only on response (`Visa`·VISA, …); we store its `.Value` string |

Enums are `StringEnum<T>` — compare with the static members or read `.Value`; a status the source
list does not enumerate (e.g. an `EXPIRED` authorization) must be handled by `.Value` string, not
by assuming a missing member — **MUST load dotnet-models**.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A saved card used to pay (`card.vault_id`) must be a vault token this **caller owns** (issued by `CreatePaymentToken`, row in our `SavedPaymentMethod` with `BuyerId == caller`). | `CreateOrder` ← `CreatePaymentToken` | application (owner check before building `CardRequest.VaultId`) |
| The `captureId` a refund targets must be the capture produced by **this order's** `CaptureAuthorizedPayment`. | `RefundCapturedPayment` ← `CaptureAuthorizedPayment` | application (read capture id off the order's `OrderPayment`) |
| The `authorizationId` captured/voided/reauthorized must be the one produced by **this order's** `AuthorizeOrder`. | `Capture/Void/Reauthorize` ← `AuthorizeOrder` | application (read from `OrderPayment`) |
| A deleted saved card must no longer be usable to pay. | `CreateOrder` ✗ ← `DeletePaymentToken` | application (delete row + vault token; pay looks up by owner) |
| Reconciliation lines PayPal `TransactionInfo.TransactionId`/`InvoiceId` up against stored capture ids / order refs. | `SearchTransactions` ↔ persisted payments | application |
| Sum of a capture's refunds must never exceed the captured amount. | `RefundCapturedPayment` (repeated) | application (guard before call) |

---

## 3. Trap notes (hazard + skill; not resolved here)

- **Client/HttpClient lifetime & DI** — the SDK client must wrap a long-lived, factory-managed `HttpClient`; getting the registration lifetime wrong exhausts sockets or rebuilds per request. **MUST load dotnet-client-initialization.**
- **Auth wiring** — a credential never set is *skipped*, not thrown, so a 401 can mean "nothing was sent"; the options object is captured once at registration. **MUST load dotnet-authentication.**
- **Calling convention** — many operations have nullable-no-default params that mis-bind positionally; the generator-injected `Idempotency-Key: Guid.NewGuid()` header is **not** a real key. Which write takes a real caller key (`payPalRequestId`) is on the row. **MUST load dotnet-calling-endpoints.**
- **Model building** — enums are `StringEnum<T>` (not C# enums); required members must be set; response envelopes nest one level (`Authorizations[0]`, `SellerReceivableBreakdown`). **MUST load dotnet-models.**
- **Error boundary** — Case A vs Case B differ; a drifted 2xx body throws `JsonException` (not `SdkException`); a non-2xx body that doesn't match `{Operation}Error` throws `JsonException` while building the error, destroying the status. **MUST load dotnet-error-handling.**
- **Timeout / retry / pagination / logging** — `Timeout` is per-attempt (not total); `POST` is never auto-retried but that leaves ambiguous writes to reconcile; `SearchTransactions` paginates by `page`/`TotalPages` and a naive loop is unbounded; `LogRequestBody` logs card JSON unredacted. **MUST load dotnet-configuration-resilience.**
- **Testing seam** — the `HttpClient` ctor arg is the fake seam; don't hit real PayPal in tests. **MUST load dotnet-testing.**

---

## 4. REQUIRED READING (load all before implementation; contents deliberately not inlined here)

- **paypal-platforms-team:dotnet-client-initialization** — S2 client/DI construction & HttpClient lifetime.
- **paypal-platforms-team:dotnet-authentication** — S2 OAuth2 client-credentials wiring.
- **paypal-platforms-team:dotnet-calling-endpoints** — S4 first calls, named args, real vs injected idempotency key.
- **paypal-platforms-team:dotnet-models** — S4 building request models / reading nested response envelopes / `StringEnum<T>`.
- **paypal-platforms-team:dotnet-error-handling** — S4 gateway error boundary. (Always required.) Both mandatory `JsonException` hazards apply: (a) a drifted/malformed **2xx** body (a missing `required` member such as `SellerReceivableBreakdown.GrossAmount`) surfaces as `System.Text.Json.JsonException` from deserialization, **not** `SdkException` — an SDK-only catch ladder lets it escape; (b) a **non-2xx** body not matching its `{Operation}Error` throws `JsonException` **while constructing the error**, replacing the `SdkException` and destroying the HTTP status.
- **paypal-platforms-team:dotnet-configuration-resilience** — S2/S4 retry/timeout/pagination/logging.
- **paypal-platforms-team:dotnet-testing** — S6 tests.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalSettings` bound from `PayPal:` in `Infrastructure` DI; an `IValidateOptions<PayPalSettings>` + `ValidateOnStart()` fails host start if **any** of `ClientId`, `ClientSecret`, `Environment`, `Currency` is missing **or blank** (each part checked separately — a blank part is not a missing one). `BaseUrl` optional. |
| 2 | Secret sourcing & rotation | Values live in **.NET user-secrets** (`PayPal:ClientId/ClientSecret/Environment/Currency`), loaded from env vars by the operator; never in repo files. `AddPayPalServerSdkClient` captures options in a singleton at registration, so a rotated secret takes effect only on **process restart** — acceptable here; no hot-rotation requirement. |
| 3 | Total timeout budget | SDK `Timeout` is per-attempt. The gateway passes a per-call `CancellationToken` with a bounded deadline (config `PayPal:RequestTimeoutSeconds`, default 100s) so a hung retryable call cannot exceed it; enforced at each gateway call site. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS → our writes are all `POST`/`DELETE`, so the SDK **never auto-resends** them. Each write is made idempotent by us (row 5). Reads (`GetAuthorizedPayment`, `SearchTransactions` = GET) may be retried safely. |
| 5 | Idempotency & ambiguous writes | `CreateOrder`/`AuthorizeOrder`: `payPalRequestId = "pay-{orderId}"` + own-state guard (return existing authorization if already `Authorized`). `CaptureAuthorizedPayment`: `payPalRequestId = "cap-{authorizationId}"` + guard (return existing capture if already captured). `RefundCapturedPayment`: `payPalRequestId = <caller idempotency key>` — repeating a key returns the stored refund; two distinct keys = two legitimate partial refunds; a pre-call guard rejects refunds exceeding captured−alreadyRefunded. `VoidPayment` guarded by auth state. An ambiguous write (network cut) is recovered by re-issue under the same key or by reconciliation. |
| 6 | Observability | Gateway logs at Info: operation + eShop order id + PayPal ids (order/auth/capture/refund) + resulting status; at Error: mapped failure + PayPal `debug_id`/error `name` pulled from the typed `Error`/`RawError` body. **`LogRequestBody` stays OFF** (row 7). No card field is ever logged. |
| 7 | Sensitive data | `CardRequest`/`PaymentTokenRequestCard` carry PAN + CVV + expiry. Therefore `options.Logging.LogRequestBody` stays **off** and `options.Logging.LoggerFactory` is set explicitly at registration so the `PAYPALSERVERSDKCLIENT_LOG` env var cannot switch body logging on from outside code. Our own logs never echo request bodies. Full PAN/CVV are never persisted (DB stores only vault-token id + brand + last4 + expiry) and never written to logs. |
| 8 | Environment selection | SDK declares only `ServerEnvironment.Sandbox`; there is **no Live member**. All dev/test targets Sandbox. `PayPal:Environment` must be `sandbox` (validated; any other value → fail-fast, since the SDK cannot address a live host without an explicit `PayPal:BaseUrl`). If `PayPal:BaseUrl` is set it overrides `Server.Default.Sandbox.BaseUrl` for every call incl. token (verified in source). Test traffic is kept off any live system because the only reachable host is sandbox unless BaseUrl is deliberately overridden. |

---

## 6. Assumptions & Blockers

- **Assumption (minor):** "operator" endpoints = fulfil, cancel, reconciliation only (task's explicit rule); refunds are shopper-scoped on the caller's own order. Proceeding.
- **Assumption (minor):** direct card payment on the sandbox business account (enabled for card processing + vaulting) returns no 3-DS challenge for test card `4111 1111 1111 1111`; if a real challenge (`PAYER_ACTION_REQUIRED` + approve link) appears, **STOP and report** — do not build an approval round-trip (per task).
- **Assumption (minor):** vault `Customer` omitted on `CreatePaymentToken`; ownership is enforced in our DB, so PayPal customer grouping / `ListCustomerPaymentTokens` is unnecessary.
- **No blockers.** Every capability the task needs maps to an SDK operation above.

### Verified provider/SDK behaviours (learned during live sandbox verification)

- **A card supplied inline on `CreateOrder` with `intent=AUTHORIZE` is authorized at creation.** The
  authorization is already on the `CreateOrder` response, so a follow-up `AuthorizeOrder` is rejected
  `422 ORDER_ALREADY_AUTHORIZED`. The gateway requests `return=representation` on `CreateOrder`, uses the
  authorization if present, and only calls `AuthorizeOrder` when it is not.
- **`VoidPayment` answers `204 No Content` under `return=minimal`**, which the SDK cannot deserialize into
  `PaymentAuthorization` (surfaces as `JsonException` despite success). The gateway requests
  `return=representation` and uses a tolerant executor that treats a `2xx` empty/undeserializable body as
  success.
- **The sandbox business account enforces unique invoice ids per transaction** (`DUPLICATE_INVOICE_ID`).
  Each order gets a globally-unique `InvoiceReference` (assigned at order creation) that seeds both the
  PayPal `invoice_id`/`custom_id` and the `PayPal-Request-Id` values, so restarts with reset in-memory
  order ids never collide with a cached PayPal response. `invoice_id` is set once on the order (PayPal
  propagates it to the capture); it is omitted on capture and refund to avoid re-tripping the check.
- **Reconciliation lags:** transactions just created may not yet appear in `SearchTransactions`; a matched
  count of 0 over a just-created range is expected, not a defect (task-acknowledged). The report still
  correctly surfaces PayPal-only and eShop-only records over ranges that have data.

### YOUR CALL — not in the map (application design, decided here)

- Persistence: `OrderPayment` (1:1 with `Order` by `OrderId`) holds PayPal order id, authorization id + status, capture id + status, captured/fee/net Money, currency; `PaymentRefund` child holds PayPal refund id, amount, caller idempotency key, status; `SavedPaymentMethod` holds `BuyerId`, vault-token id, brand, last4, expiry. All in `CatalogContext`, owner-scoped by `BuyerId`.
- Amounts: from catalog prices (`Order.Total()`), currency from `PayPal:Currency`, formatted `value.ToString("0.00", InvariantCulture)` to the cent.
- Ownership scoping: `BuyerId == HttpContext.User.Identity.Name`; every shopper endpoint filters by it; cross-owner access → 404 (not 403) to avoid existence disclosure.
