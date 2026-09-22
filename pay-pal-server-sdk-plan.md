# PayPal Server SDK integration plan — eShopOnWeb (PublicApi)

Additive PayPal card payments + saved cards for eShopOnWeb. All flows drivable through
`src/PublicApi` (JWT). SDK = APIMatic-generated **PayPal Server SDK .NET** (root namespace
`PayPalServerSdk`). Every SDK fact below is grounded in the SDK map / source; no memory.

## 1. Scope & sequence

1. **Vendor the SDK** into the repo (`src/PaymentSdk/PayPalServerSdk/`, copied from source clone,
   `netstandard2.0`) and add it to the solution; `Infrastructure` gets a `ProjectReference`.
   Reason: SDK is not on NuGet — must build from source and reference the assembly.
2. **Domain (ApplicationCore)** — new `PaymentAggregate`: `OrderPayment` (PK=OrderId),
   `OrderRefund`, `SavedPaymentMethod`, `PaymentStatus` enum, `IPayPalPaymentGateway` abstraction +
   gateway result records, specifications. Reuses existing `Order`/`OrderItem` for the order itself.
3. **Infrastructure** — `PayPalOptions`, client factory (`PayPalServerSdkClient` singleton),
   `PayPalPaymentGateway : IPayPalPaymentGateway` (the only place SDK types are touched), EF config +
   DbSets, DI registration + fail-fast validation.
4. **PublicApi endpoints** (MinimalApi.Endpoint `IEndpoint`, JWT):
   - `POST /api/orders` (shopper) → `CreateOrder` op **not** used here; this creates the *eShop* Order only.
   - `POST /api/orders/{orderId}/pay` (shopper) → `Orders.CreateOrder` (intent=AUTHORIZE, payment_source.card/vault_id) + `Orders.AuthorizeOrder`.
   - `POST /api/orders/{orderId}/fulfil` (**admin**) → `Payments.CaptureAuthorizedPayment`; stale → `Payments.ReauthorizePayment` then re-capture.
   - `POST /api/orders/{orderId}/cancel` (**admin**) → `Payments.VoidPayment`.
   - `POST /api/orders/{orderId}/refunds` (shopper) → `Payments.RefundCapturedPayment`.
   - `GET /api/my-orders` (shopper).
   - `GET /api/reconciliation?from&to` (**admin**) → `TransactionSearch.SearchTransactions` (all pages).
   - `POST/GET/DELETE /api/payment-methods` (shopper) → `Vault.CreatePaymentToken` / DB list / `Vault.DeletePaymentToken`.
5. Tests + end-to-end sandbox verification.

## 2. CONTRACT SHEET

> ⚠ **Signatures are generated code, verbatim.** Every parameter name is the literal C#
> identifier; named arguments must use them exactly (the cancellation-token parameter is named
> `ct`, so `ct:`). "must pass explicitly" params are nullable-with-no-default — pass `null` to skip.
> ⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**
> (`PayPalServerSdk.Models` for `Models/*`, `PayPalServerSdk.Models.Enums` for `Models/Enums/*`,
> `PayPalServerSdk.Errors` for `Errors/*`, `PayPalServerSdk.Api` for controllers), taken from the
> path the map gives for THAT type.

### Client construction & auth (source: sdk-map.md §Getting a client / §Servers & auth)

- `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` — only ctor.
- `PayPalServerSdkClientOptions { Oauth2 = new OAuth2ClientCredentials { ClientId, ClientSecret }, Environment = ServerEnvironment.Sandbox, Server = new ServerOptions{...} }`.
  - `OAuth2ClientCredentials` ns `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`.
  - `ServerEnvironment` ns `PayPalServerSdk.Servers`; only member is `.Sandbox` (default) → `https://api-m.sandbox.paypal.com`.
  - Base-URL override point: `options.Server.Default.Sandbox.BaseUrl` (source `Servers/DefaultOptions.cs`; default `https://api-m.sandbox.paypal.com`).
- OAuth2 client-credentials; token fetched from `{sandbox}/v1/oauth2/token`. Whether the `BaseUrl`
  override also redirects the token fetch = trap, see §3 (MUST load dotnet-authentication).
- Every op has **Auth: options.Oauth2**.

### Operations (source cells cite the map operations page / declaring file)

| op | signature (verbatim, trimmed) | request model → fields used | response → fields read | error case + accessors | source |
| --- | --- | --- | --- | --- | --- |
| `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", …, ct)` | `OrderRequest{ Intent(req), PurchaseUnits(req), PaymentSource? }` | `Order{ Id, Status, PurchaseUnits[] }` | A: `SdkException<CreateOrderError>`; `TryGetError(out Error)`[400,401,422] · `TryGetRawError` | map/operations/Orders.md; Models/OrderRequest.cs; Models/Order.cs |
| `client.Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", …, ct)` | body = `null` (payment_source already on order; remarks: "a valid payment_source must be provided" — provided at create) | `OrderAuthorizeResponse{ Id, Status, PurchaseUnits[].Payments.Authorizations[].{Id,Status,Amount,ExpirationTime} }` | A: `SdkException<AuthorizeOrderError>`; `TryGetError(out Error)`[400,401,403,404,422,500] · `TryGetRawError` | Orders.md; Models/OrderAuthorizeResponse.cs; Models/PurchaseUnit.cs; Models/PaymentCollection.cs; Models/AuthorizationWithAdditionalData.cs |
| `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", …, ct)` | `CaptureRequest{ FinalCapture=true }` (full capture) | `CapturedPayment{ Id, Status, Amount, SellerReceivableBreakdown{ GrossAmount, PaypalFee, NetAmount } }` | A: `SdkException<CaptureAuthorizedPaymentError>`; `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` | Payments.md; Models/CaptureRequest.cs; Models/CapturedPayment.cs; Models/SellerReceivableBreakdown.cs |
| `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", …, ct)` | `null` (renew same amount) | `PaymentAuthorization{ Id, Status, Amount, ExpirationTime }` | A: `SdkException<ReauthorizePaymentError>`; `TryGetError`[400,401,403,404,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; Models/PaymentAuthorization.cs |
| `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", …, ct)` | — | `PaymentAuthorization{ Id, Status }` | A: `SdkException<VoidPaymentError>`; `TryGetError`[401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", …, ct)` | `RefundRequest{ Amount? }` (null=full, Money=partial) | `Refund{ Id, Status, Amount }` | A: `SdkException<RefundCapturedPaymentError>`; `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; Models/RefundRequest.cs; Models/Refund.cs |
| `client.Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, …, ct)` | — | `PaymentAuthorization{ Status, ExpirationTime }` | A: `SdkException<GetAuthorizedPaymentError>`; `TryGetError`[401,403,404] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md |
| `client.Orders.GetOrder` | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, …, ct)` | fields=`null` | `Order{ Status, PurchaseUnits[].Payments.{Authorizations,Captures} }` | A: `SdkException<GetOrderError>`; `TryGetError`[401,404] · `TryGetRawError` | Orders.md; Models/Order.cs (unknown-outcome re-read) |
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, …, ct)` | `PaymentTokenRequest{ Customer?{ Id? or MerchantCustomerId? }, PaymentSource(req)=PaymentTokenRequestPaymentSource{ Card=PaymentTokenRequestCard{ Number, Expiry, Name?, SecurityCode?, BillingAddress? } } }` | `PaymentTokenResponse{ Id, Customer{Id}, PaymentSource.Card=CardPaymentTokenEntity{ LastDigits, Brand, Expiry, Name } }` | A: `SdkException<CreatePaymentTokenError>`; `TryGetError`[400,403,404,422,500] · `TryGetRawError` | Vault.md; Models/PaymentTokenRequest.cs; Models/PaymentTokenRequestPaymentSource.cs; Models/PaymentTokenRequestCard.cs; Models/PaymentTokenResponse.cs; Models/CardPaymentTokenEntity.cs; Models/Customer.cs |
| `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, …, ct)` | — | void | A: `SdkException<DeletePaymentTokenError>`; `TryGetError`[400,403,500] · `TryGetRawError` | Vault.md |
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, …, ct)` | dates ISO-8601; 8 nullable filters pass `null`; loop `page` 1..`TotalPages` | `SearchResponse{ TransactionDetails[].TransactionInfo{ TransactionId, TransactionAmount, TransactionStatus, InvoiceId }, TotalPages, TotalItems, Page }` | **B: `SdkException<RawError>`** (no typed accessors) | TransactionSearch.md; Models/SearchResponse.cs; Models/TransactionDetails.cs; Models/TransactionInformation.cs |

Shared models: `Money{ CurrencyCode(req), Value(req) }` (Models/Money.cs); `AmountWithBreakdown{ CurrencyCode(req), Value(req) }` (Models/AmountWithBreakdown.cs); `PurchaseUnitRequest{ Amount(req)=AmountWithBreakdown, ReferenceId?, InvoiceId?, CustomId?, Description? }` (Models/PurchaseUnitRequest.cs); `PaymentSource{ Card?=CardRequest }` (Models/PaymentSource.cs); `CardRequest{ Number?, Expiry?("YYYY-MM"), SecurityCode?, Name?, BillingAddress?, VaultId? }` (Models/CardRequest.cs).

### Enums needed

- `CheckoutPaymentIntent` (Models/Enums/CheckoutPaymentIntent.cs): `.Authorize`="AUTHORIZE", `.Capture`="CAPTURE". Use `.Authorize`.
- `AuthorizationStatus` (Models/Enums/AuthorizationStatus.cs): `CREATED, CAPTURED, DENIED, PARTIALLY_CAPTURED, VOIDED, PENDING` — **no `EXPIRED` member** (open StringEnum; an expired auth surfaces via capture error, not a named status). Compare `.Value`.
- `CaptureStatus`, `RefundStatus` (Models/Enums/) — read `.Value` for status branching; do not default absent→success.
- `TokenType` (Models/Enums/TokenType.cs): only `BILLING_AGREEMENT` → **NOT a card token**; reuse a vaulted card via `CardRequest.VaultId`, never `PaymentSource.Token`.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A `paymentMethodId` used at `/pay` must be one the caller previously saved (and still owns) | `POST /orders/{id}/pay` ← `POST /payment-methods` / `GET /payment-methods` | implementation (lookup `SavedPaymentMethod` by (id, buyerId); 400 if absent) — then pass its stored `PayPalVaultId` as `CardRequest.VaultId` |
| `authorizationId` captured/voided/reauthorized must be one returned by the prior authorize of that order | `CaptureAuthorizedPayment`/`VoidPayment`/`ReauthorizePayment` ← `AuthorizeOrder` | implementation (stored on `OrderPayment.AuthorizationId`) |
| `captureId` refunded must be the one returned by that order's capture | `RefundCapturedPayment` ← `CaptureAuthorizedPayment` | implementation (stored on `OrderPayment.CaptureId`) |
| Sum of refund amounts ≤ captured amount | `RefundCapturedPayment` (repeated) | implementation (`OrderPayment` tracks `RefundedAmount`; reject over-refund) + PayPal 422/409 backstop |

## 3. Trap notes (name the hazard + skill; do not resolve inline)

- **Client/HttpClient lifetime & DI**: singleton client over a long-lived `HttpClient`; getting the lifetime wrong leaks sockets or captures a stale handler. MUST load dotnet-client-initialization.
- **Auth base-URL / token endpoint**: whether setting `Server.Default.Sandbox.BaseUrl` also redirects the OAuth2 *token* request (task requires BaseUrl govern **every** call incl. token). MUST load dotnet-authentication.
- **Total timeout budget**: `RetryOptions.Timeout` is per-attempt, not the whole call; a hung retryable call costs a multiple. Only a `CancellationToken` deadline bounds the whole call. MUST load dotnet-configuration-resilience.
- **Write-retry ownership**: which of these writes the SDK may resend by default (POST vs PUT/GET). MUST load dotnet-configuration-resilience.
- **Idempotency vs injected header**: the generator injects a fresh `Idempotency-Key` GUID per call — it dedups nothing; the real key is `PayPal-Request-Id` where the op exposes it. MUST load dotnet-calling-endpoints.
- **Sensitive card data + logging**: request bodies carry raw PAN/CVV on create-order & vault; `LogRequestBody` logs JSON unredacted, and an unset `LoggerFactory` arms `PAYPALSERVERSDKCLIENT_LOG`. MUST load dotnet-configuration-resilience.
- **Models — enums/unions/unknown fields**: enums are `StringEnum<T>` (use `.FromValue`/static members, compare `.Value`), not C# enums. MUST load dotnet-models.
- **Error boundary — two JsonException directions + Case A/B mix**: see REQUIRED READING rows. MUST load dotnet-error-handling.
- **List/search named args + pagination**: `SearchTransactions` has many optional params with no C# default → call with named args; it is offset-paged (`page`/`total_pages`), SDK does not auto-page. MUST load dotnet-calling-endpoints + dotnet-configuration-resilience.
- **Test seam**: fake the `HttpClient`/gateway seam, not SDK internals. MUST load dotnet-testing.

## 4. REQUIRED READING (load ALL before implementation; contents deliberately not copied here)

| skill (plugin: paypal-platforms-team) | governs |
| --- | --- |
| `dotnet-client-initialization` | client construction + DI + HttpClient lifetime (step 3) |
| `dotnet-authentication` | OAuth2 credentials, base-URL/token endpoint (step 3) |
| `dotnet-calling-endpoints` | calling ops, named args, real idempotency key, pagination (steps 3–4) |
| `dotnet-models` | building request bodies, StringEnum, unions, unknown fields (steps 3–4) |
| `dotnet-error-handling` | try/catch around every SDK call + error middleware (steps 3–4) |
| `dotnet-configuration-resilience` | retries, timeouts, base URL, pagination, **logging/sensitive data** (step 3) |
| `dotnet-testing` | testing the integration layer (step 5) |

**Mandatory hazard rows (verbatim):** `System.Text.Json.JsonException` reaches the boundary from two
directions and needs opposite handling: (a) a drifted/malformed **2xx** body (missing `required`
member) surfaces as a `JsonException` from deserialization, **not** `SdkException` — an
SDK-exception-only ladder lets it escape; (b) a **non-2xx** body that doesn't match its operation's
generated `{Operation}Error` shape throws `JsonException` *while constructing the error object*, so it
**replaces** the `SdkException` and the HTTP status is destroyed with it.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` bound from `PayPal:` in Infrastructure DI; `IValidateOptions` checks `ClientId`, `ClientSecret`, `Environment`, `Currency` each non-null/non-blank (each part separately). `.ValidateOnStart()` added **except** in ASP.NET env `Testing` (FunctionalTests) — Development/Production throw at host start. `BaseUrl` optional. |
| 2 | Secret sourcing & rotation | Secrets in .NET user-secrets (loaded from env vars by me, never in repo). DI builds `PayPalServerSdkClientOptions` once at registration → captured in the singleton client; **rotation needs a process restart** (acceptable; documented). |
| 3 | Total timeout budget | Each gateway call passes a `CancellationToken` with a bounded deadline (default 100s from the endpoint) — that, not `RetryOptions.Timeout` (per-attempt), bounds the whole call. Verify exact retry defaults via dotnet-configuration-resilience. |
| 4 | Write-retry ownership | All money-moving calls are `POST` (CreateOrder/Authorize/Capture/Reauthorize/Void/Refund/CreatePaymentToken) and `DELETE` (DeletePaymentToken) — **not** in the SDK's default retryable-method set, so the SDK never silently resends them. Idempotency handled by us (row 5). Confirm default `HttpMethodsToRetry` via dotnet-configuration-resilience. |
| 5 | Idempotency & ambiguous writes | **Authorize**: DB claim `OrderPayment` PK=OrderId inserted before the PayPal call + `PayPal-Request-Id="auth-{orderId}"` on CreateOrder (PayPal stores key 6h) → double-click cannot double-authorize. **Capture**: no caller key on the op → gated on `OrderPayment.CaptureId==null` (skip if already captured); unknown-outcome re-read via `GetOrder`. **Void**: gated on state. **Refund**: caller-supplied `Idempotency-Key` → passed as `PayPal-Request-Id` (server stores key) + DB unique claim `(OrderId, IdempotencyKey)`; two distinct keys = two legit partial refunds. **Vault**: DB is the record; a retry creates a new token (acceptable, superseded row removed). |
| 6 | Observability | Structured logs at Info for each transition (order id, paypal ids, status) and Warning/Error on gateway failures incl. the PayPal `debug_id`/issue from the error body (read via error accessors). `LogRequestBody` OFF. No card fields ever logged. |
| 7 | Sensitive data | CreateOrder & CreatePaymentToken bodies carry raw PAN/CVV. Therefore: `LoggerFactory` set explicitly on options (disarms `PAYPALSERVERSDKCLIENT_LOG`), `LogRequestBody` stays off, gateway never logs request bodies or card fields; card number/CVV never persisted (only brand+last4+expiry). |
| 8 | Environment selection | One server group `Default`; only `ServerEnvironment.Sandbox` exists. `PayPal:Environment` bound + validated; all dev/test target Sandbox. `PayPal:BaseUrl`, when set, sets `options.Server.Default.Sandbox.BaseUrl` verbatim (must also govern token fetch — verify, §3). No non-sandbox host exists in this SDK, so test traffic is inherently sandbox-only. |
| 9 | Duplicate prevention under concurrency | `OrderPayment` — store **CatalogContext.OrderPayments**, claim column **OrderId is the PRIMARY KEY** (enforced by both SqlServer and EF-InMemory). Second concurrent `/pay` insert throws on the PK; caught in the pay endpoint. `OrderRefund` — store **CatalogContext.OrderRefunds**, unique **alternate key (OrderId, IdempotencyKey)**; second same-key insert throws, caught in the refunds endpoint. (Verified at build that InMemory enforces the alternate key; if not, the claim degrades to a composite-string PK — see build note.) |
| 10 | Partial results | `GET /api/reconciliation` walks all pages (`page` 1..`TotalPages`); the response body carries `pagesScanned`, `totalItems`, and a `truncated` bool so the caller learns if a hard page cap was hit. |
| 11 | Startup validation vs test host | Host-booting test = **PublicApiIntegrationTests** (`WebApplicationFactory<Program>`, env Development). Given placeholder `PayPal:*` config in `tests/PublicApiIntegrationTests/appsettings.test.json` so `ValidateOnStart` passes. FunctionalTests boots at env `Testing` where `ValidateOnStart` is skipped. Both run and must be green. |
| 12 | Ordering & no-op side effects | `OrderPayment` claim row is written **before** the PayPal authorize call and completed (status/ids) after it returns. Capture/void/refund are gated on actual state change (`CaptureId==null`, status transitions) so a repeated call performs no second money movement and emits no duplicate side effect. |
| 13 | Unknown outcomes | **Authorize**: on transport failure after claim insert, `OrderPayment` left non-terminal + reconciliation surfaces any PayPal order eShop shows unpaid (searched by `invoice_id`). **Capture**: catch re-reads via `Orders.GetOrder(payPalOrderId)` searching `purchase_units[].payments.captures[]` for the capture id. **Refund**: catch re-reads via reconciliation/`invoice_id`; claim row already persisted. |
| 14 | Provider status & reconciliation clocks | Branch on each status `.Value`: CreateOrder/Authorize (`AuthorizationStatus`), Capture (`CaptureStatus`), Refund (`RefundStatus`) — non-success ⇒ non-terminal local state + surfaced message, never `?? "COMPLETED"`. Reconciliation filters both sides on the **PayPal transaction initiation date** within [from,to] (the search `start_date`/`end_date`); the eShop side is matched by `invoice_id`, not by local row-creation time. |

### DUPLICATE CLAIMS
| write | where the claim is stored | what rejects the second one | where that rejection is caught | where in the code |
| --- | --- | --- | --- | --- |
| authorize (`/pay`) | `CatalogContext.OrderPayments`, PK `OrderId` | primary-key uniqueness | catch on `AddAsync`/`SaveChanges` → return existing state | TBD |
| refund (`/refunds`) | `CatalogContext.OrderRefunds`, alt key `(OrderId, IdempotencyKey)` | alternate-key uniqueness | catch on `AddAsync`/`SaveChanges` → return existing refund | TBD |

### PAGED READS
| read | what caps it | how the caller learns it was cut short | where in the code |
| --- | --- | --- | --- |
| reconciliation `SearchTransactions` | loop `page` 1..`TotalPages`, hard safety cap `MaxPages` | response fields `truncated`(bool)+`pagesScanned`/`totalItems` | TBD |
| saved cards list | none (DB query, all rows for buyer) | n/a (not paged) | TBD |

### REPEATED OPERATIONS
| operation | what tells you the state actually changed | the effects gated on that | where in the code |
| --- | --- | --- | --- |
| `/pay` | `OrderPayment` absent/Failed → authorize proceeds; already Authorized+ → no-op | the PayPal authorize call | TBD |
| `/fulfil` | `OrderPayment.CaptureId == null` | the PayPal capture call | TBD |
| `/cancel` | status == Authorized (not already Voided/Captured) | the PayPal void call | TBD |
| `/refunds` | new `(OrderId, IdempotencyKey)` claim inserted | the PayPal refund call | TBD |

### UNKNOWN OUTCOMES
| write | the operation you re-read with | the reference you search by | where in the code |
| --- | --- | --- | --- |
| authorize | `TransactionSearch.SearchTransactions` (reconciliation) / `Orders.GetOrder` | `invoice_id` / stored `PayPalOrderId` | TBD |
| capture | `Orders.GetOrder` | stored `PayPalOrderId` → `payments.captures[].id` | TBD |
| refund | reconciliation `SearchTransactions` | `invoice_id` | TBD |

### OPERATION OUTCOMES
| write | the status field | every value it can hold, and what the app does with each | where in the code |
| --- | --- | --- | --- |
| authorize | `AuthorizationWithAdditionalData.Status` (`AuthorizationStatus`) | CREATED→Authorized; PENDING→non-terminal (surface pending); DENIED→Failed; others(CAPTURED/PARTIALLY_CAPTURED/VOIDED)→unexpected/surface | TBD |
| capture | `CapturedPayment.Status` (`CaptureStatus`) | COMPLETED→Fulfilled+store gross/fee/net; PENDING→non-terminal; DECLINED/FAILED→surface, stay Authorized; others→surface | TBD |
| refund | `Refund.Status` (`RefundStatus`) | COMPLETED→apply refunded amount; PENDING→non-terminal record; FAILED/CANCELLED→surface | TBD |
| void | `PaymentAuthorization.Status` | VOIDED→Cancelled; else surface | TBD |

### WRITE ORDER
| write | what exists locally BEFORE the call | what is written after it returns | where in the code |
| --- | --- | --- | --- |
| authorize | `OrderPayment{OrderId,BuyerId,InvoiceId,amount,Status=Authorizing}` | PayPalOrderId, AuthorizationId, Status=Authorized | TBD |
| capture | existing `OrderPayment` (Authorized) | CaptureId, gross/fee/net, Status=Fulfilled | TBD |
| refund | `OrderRefund{Id,OrderId,IdempotencyKey,amount,Status=Pending}` | PayPalRefundId, Status; `OrderPayment.RefundedAmount`+state | TBD |
| vault card | (none required — DB record created after token exists, but token id is the only server state) | `SavedPaymentMethod{Id,BuyerId,PayPalVaultId,PayPalCustomerId,brand,last4,expiry}` | TBD |

> Note on vault WRITE ORDER: vaulting has no local mutable resource to pre-claim; the PayPal token
> is created first, then the DB row records it. A transport failure after token creation leaves an
> orphan vault token (no eShop row) — surfaced by listing vault tokens; not a money movement.

## 6. Assumptions & Blockers

- **Assumption**: configured currency uses 2 minor-unit decimals (sandbox = USD); `Money.Value`
  formatted to the currency's decimals (2 default) invariant-culture, equal to the order total to the cent.
- **Assumption**: sandbox card `4111 1111 1111 1111` authorizes with no 3DS challenge. If PayPal
  returns an approval/challenge requirement, **STOP and report** (no browser round-trip) — per task.
- **Assumption**: SDK vendored into `src/PaymentSdk/PayPalServerSdk` (netstandard2.0) is compatible
  with the net8.0 (roll-forward net10) consumers.
- **No Blockers**: every capability the task needs maps to an SDK operation above.

## 7. Source labels — all contract rows cite a map page or declaring file (§2). Application-shape
decisions (persistence, endpoints, idempotency stores) are `YOUR CALL — not in the map` and decided
against the task in §5. No `UNVERIFIED` rows except the two runtime checks flagged in §3
(BaseUrl→token endpoint; exact retry defaults) resolved by loading the named skills before coding.
