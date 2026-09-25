# PayPal Server SDK (.NET) — integration plan for eShopOnWeb

Additive "collect money" capability on `src/PublicApi`: pay an order (authorize → capture at
fulfilment → void/refund), and vault + reuse a card. PayPal Server SDK (.NET), OAuth2 client
credentials, sandbox, direct card processing. Every SDK fact below is grounded in the SDK map /
source (paths relative to SDK root).

## 1. Scope & sequence

| # | Build step | PayPal operations used |
| --- | --- | --- |
| 1 | Vendor SDK source into repo (`src/PayPalServerSdk/`), `ProjectReference` from `Infrastructure` | — |
| 2 | `PayPalSettings` (ApplicationCore) + fail-fast bind + SDK client DI in `PublicApi/Program.cs` | client construction, `Oauth2`, `Server.Default.Sandbox.BaseUrl` |
| 3 | Domain: `Order.Status` + methods; `Payment` aggregate (+ owned `Refund`); `SavedPaymentMethod` aggregate; EF configs | — |
| 4 | `IPaymentGateway` (ApplicationCore) + `PayPalGateway` (Infrastructure) — the only SDK caller; error boundary | all ops below |
| 5 | App services: place order; authorize; fulfil(capture); cancel(void); refund; my-orders; reconcile; save/list/delete card | — |
| 6 | PublicApi endpoints (`IEndpoint<>` style) under `/api/` | — |
| 7 | Self-verify on sandbox card + tests | all |

Operation → flow map:
- **pay (authorize a hold)** = `Orders.CreateOrder` (intent `AUTHORIZE`, `payment_source.card` with card **or** `card.vault_id`) → `Orders.AuthorizeOrder` (reads authorization id from `purchase_units[].payments.authorizations[].id`).
- **fulfil (take the money)** = `Payments.CaptureAuthorizedPayment`; if authorization stale, `Payments.ReauthorizePayment` first, then capture. Read fee/net from `seller_receivable_breakdown`.
- **cancel (release hold)** = `Payments.VoidPayment`.
- **refund (after capture)** = `Payments.RefundCapturedPayment` (empty body = full; `amount` = partial).
- **my-orders** = local read.
- **reconciliation** = `TransactionSearch.SearchTransactions` (page 1..`total_pages`).
- **save card** = `Vault.CreateSetupToken` (card) → `Vault.CreatePaymentToken` (token=setup id) → returns vault id + safe descriptor.
- **delete card** = `Vault.DeletePaymentToken`.
- Reads for reconciling unknown write outcomes: `Payments.GetAuthorizedPayment`, `Payments.GetCapturedPayment`, `Orders.GetOrder`.

## 2. CONTRACT SHEET

⚠ **Signatures are generated code, verbatim.** Each operation that takes input takes ONE request
record as its first parameter, built with an object initializer whose property names are the
record's own — never flat arguments. An operation with no inputs takes none.
⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**
(`Models/` → `PayPalServerSdk.Models`; `Models/Enums/` → `.Models.Enums`; `Errors/` →
`.Errors`; `Requests/<Ctrl>/` → `.Requests.<Ctrl>`; client/options → `PayPalServerSdk`;
`ServerEnvironment` → `.Servers`; OAuth creds → `.Core.Authentication.OAuth2.ClientCredentials`;
exceptions → `.Core.Exceptions`; `RawError`/`ApiError` → `.Core.ErrorResponse`). Take each from
the path the map gives for THAT type.

Client group accessors: `client.Orders`, `client.Payments`, `client.Vault`,
`client.TransactionSearch`. All ops **Auth: `options.Oauth2`**, server group **Default**, throw-only.

| Op | Signature (required members) | Body model + fields used (wire) | Response envelope → fields read | Error case + accessors | Source |
| --- | --- | --- | --- | --- | --- |
| `Orders.CreateOrder` | `CreateOrder(CreateOrderRequest{ Body req; PayPalRequestId?; Prefer="return=representation" })` | `OrderRequest{ Intent(intent) req: CheckoutPaymentIntent; PurchaseUnits(purchase_units) req: IReadOnlyList<PurchaseUnitRequest>; PaymentSource(payment_source)? }` ; `PurchaseUnitRequest{ Amount(amount) req: AmountWithBreakdown; CustomId(custom_id)?; InvoiceId(invoice_id)?; ReferenceId(reference_id)? }` ; `AmountWithBreakdown{ CurrencyCode(currency_code) req; Value(value) req }` ; `PaymentSource{ Card(card)?: CardRequest }` ; `CardRequest{ Number(number)?; Expiry(expiry YYYY-MM)?; SecurityCode(security_code)?; Name(name)?; BillingAddress?; VaultId(vault_id)? }` | `Order` → `Id`, `Status`(OrderStatus) | A `ApiException<CreateOrderError>`: `TryGetError(out Error)`[400,401,422] · `TryGetRawError`[fallback] | `Requests/Orders/CreateOrderRequest.cs`, `Models/OrderRequest.cs`, `Models/PurchaseUnitRequest.cs`, `Models/AmountWithBreakdown.cs`, `Models/PaymentSource.cs`, `Models/CardRequest.cs`, `Models/Order.cs`, `Errors/CreateOrderError.cs` |
| `Orders.AuthorizeOrder` | `AuthorizeOrder(AuthorizeOrderRequest{ Id req; PayPalRequestId?; Prefer="return=representation"; Body?: OrderAuthorizeRequest })` | body optional/none | `OrderAuthorizeResponse` → `Id`, `Status`, `PurchaseUnits[].Payments.Authorizations[]` (`AuthorizationWithAdditionalData`): `Id`, `Status`(AuthorizationStatus), `ExpirationTime`, `Amount` | A `ApiException<AuthorizeOrderError>`: `TryGetError(out Error)`[400,401,403,404,422,500] · `TryGetRawError`[fallback] | `Requests/Orders/AuthorizeOrderRequest.cs`, `Models/OrderAuthorizeResponse.cs`, `Models/PurchaseUnit.cs`, `Models/PaymentCollection.cs`, `Models/AuthorizationWithAdditionalData.cs`, `Errors/AuthorizeOrderError.cs` |
| `Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(CaptureAuthorizedPaymentRequest{ AuthorizationId req; PayPalRequestId?; Prefer="return=representation"; Body?: CaptureRequest })` | `CaptureRequest{ Amount(amount)?: Money; FinalCapture(final_capture)?: bool }` ; `Money{ CurrencyCode req; Value req }` | `CapturedPayment` → `Id`, `Status`(CaptureStatus), `Amount`(Money), `SellerReceivableBreakdown`{ `GrossAmount` req, `PaypalFee?`, `NetAmount?` } | A `ApiException<CaptureAuthorizedPaymentError>`: `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError`[fallback] | `Requests/Payments/CaptureAuthorizedPaymentRequest.cs`, `Models/CaptureRequest.cs`, `Models/CapturedPayment.cs`, `Models/SellerReceivableBreakdown.cs`, `Models/Money.cs`, `Errors/CaptureAuthorizedPaymentError.cs` |
| `Payments.ReauthorizePayment` | `ReauthorizePayment(ReauthorizePaymentRequest{ AuthorizationId req; PayPalRequestId?; Body?: ReauthorizeRequest })` | `ReauthorizeRequest{ Amount(amount)?: Money }` | `PaymentAuthorization` → `Id`, `Status`, `ExpirationTime`, `Amount` | A `ApiException<ReauthorizePaymentError>`: `TryGetError(out Error)`[400,401,403,404,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError`[fallback] | `Requests/Payments/ReauthorizePaymentRequest.cs`, `Models/ReauthorizeRequest.cs`, `Models/PaymentAuthorization.cs`, `Errors/ReauthorizePaymentError.cs` |
| `Payments.VoidPayment` | `VoidPayment(VoidPaymentRequest{ AuthorizationId req; PayPalRequestId?; Prefer })` | none | `PaymentAuthorization` → `Id`, `Status`(AuthorizationStatus) | A `ApiException<VoidPaymentError>`: `TryGetError(out Error)`[401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError`[fallback] | `Requests/Payments/VoidPaymentRequest.cs`, `Models/PaymentAuthorization.cs`, `Errors/VoidPaymentError.cs` |
| `Payments.RefundCapturedPayment` | `RefundCapturedPayment(RefundCapturedPaymentRequest{ CaptureId req; PayPalRequestId?; Prefer="return=representation"; Body?: RefundRequest })` | `RefundRequest{ Amount(amount)?: Money; CustomId(custom_id)?; NoteToPayer(note_to_payer)? }` | `Refund` → `Id`, `Status`(RefundStatus), `Amount`(Money) | A `ApiException<RefundCapturedPaymentError>`: `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError`[fallback] | `Requests/Payments/RefundCapturedPaymentRequest.cs`, `Models/RefundRequest.cs`, `Models/Refund.cs`, `Errors/RefundCapturedPaymentError.cs` |
| `Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(GetAuthorizedPaymentRequest{ AuthorizationId req })` | none | `PaymentAuthorization` → `Id`, `Status`, `ExpirationTime` | A `ApiException<GetAuthorizedPaymentError>`: `TryGetError`[401,403,404] · `TryGetNoContent`[500] · `TryGetRawError` | `Requests/Payments/GetAuthorizedPaymentRequest.cs`, `Models/PaymentAuthorization.cs`, `Errors/GetAuthorizedPaymentError.cs` |
| `Payments.GetCapturedPayment` | `GetCapturedPayment(GetCapturedPaymentRequest{ CaptureId req })` | none | `CapturedPayment` → `Id`, `Status`, `SellerReceivableBreakdown` | A `ApiException<GetCapturedPaymentError>`: `TryGetError`[401,403,404] · `TryGetNoContent`[500] · `TryGetRawError` | `Requests/Payments/GetCapturedPaymentRequest.cs`, `Models/CapturedPayment.cs`, `Errors/GetCapturedPaymentError.cs` |
| `Vault.CreateSetupToken` | `CreateSetupToken(CreateSetupTokenRequest{ Body req; PayPalRequestId? })` | `SetupTokenRequest{ PaymentSource(payment_source) req: SetupTokenRequestPaymentSource{ Card?: SetupTokenRequestCard{ Number,Expiry,SecurityCode,Name,BillingAddress? } }; Customer?: Customer{ MerchantCustomerId? } }` | `SetupTokenResponse` → `Id`, `Status`(PaymentTokenStatus) | A `ApiException<CreateSetupTokenError>`: `TryGetError`[400,403,422,500] · `TryGetRawError` | `Requests/Vault/CreateSetupTokenRequest.cs`, `Models/SetupTokenRequest.cs`, `Models/SetupTokenRequestPaymentSource.cs`, `Models/SetupTokenRequestCard.cs`, `Models/Customer.cs`, `Models/SetupTokenResponse.cs`, `Errors/CreateSetupTokenError.cs` |
| `Vault.CreatePaymentToken` | `CreatePaymentToken(CreatePaymentTokenRequest{ Body req; PayPalRequestId? })` | `PaymentTokenRequest{ PaymentSource(payment_source) req: PaymentTokenRequestPaymentSource{ Token?: VaultTokenRequest{ Id req; Type req: VaultTokenRequestType } }; Customer? }` | `PaymentTokenResponse` → `Id`(vault id), `PaymentSource.Card`(CardPaymentTokenEntity){ `LastDigits`, `Brand`(CardBrand), `Expiry` }, `Customer.Id` | A `ApiException<CreatePaymentTokenError>`: `TryGetError`[400,403,404,422,500] · `TryGetRawError` | `Requests/Vault/CreatePaymentTokenRequest.cs`, `Models/PaymentTokenRequest.cs`, `Models/PaymentTokenRequestPaymentSource.cs`, `Models/VaultTokenRequest.cs`, `Models/PaymentTokenResponse.cs`, `Models/PaymentTokenResponsePaymentSource.cs`, `Models/CardPaymentTokenEntity.cs`, `Errors/CreatePaymentTokenError.cs` |
| `Vault.DeletePaymentToken` | `DeletePaymentToken(DeletePaymentTokenRequest{ Id req })` | none | `void` (Task) | A `ApiException<DeletePaymentTokenError>`: `TryGetError`[400,403,500] · `TryGetRawError` | `Requests/Vault/DeletePaymentTokenRequest.cs`, `Errors/DeletePaymentTokenError.cs` |
| `TransactionSearch.SearchTransactions` | `SearchTransactions(SearchTransactionsRequest{ StartDate req; EndDate req; TransactionCurrency?; Fields="transaction_info"; PageSize<=500; Page>=1 })` | query params (no body) | `SearchResponse` → `TransactionDetails[]` (`TransactionDetails.TransactionInfo`: `TransactionId`,`InvoiceId`,`CustomField`,`TransactionAmount`(Money),`TransactionStatus`), `Page`, `TotalPages`, `TotalItems` | **B** `ApiException<RawError>` (no typed accessors) | `Requests/TransactionSearch/SearchTransactionsRequest.cs`, `Models/SearchResponse.cs`, `Models/TransactionDetails.cs`, `Models/TransactionInformation.cs`, `Models/Money.cs` |

Notes carried from source (not derivable from `required?` alone):
- `CreateOrderRequest.PayPalRequestId` doc: **mandatory for single-step create with a card / vault_id** — always set it. `Prefer` defaults `"return=minimal"`; I set `"return=representation"` on create/authorize/capture/refund to get ids/status/breakdown back in one call.
- `AuthorizeOrderRequest.Body`, `CaptureRequest`, `RefundRequest`, `ReauthorizeRequest`, `VoidPayment` body are all **optional**; a full capture/refund sends no `amount`. Partial refund/capture sets `Money{ CurrencyCode, Value }`.
- `Customer.Id` is `[StringLength(22)]` `^[0-9a-zA-Z_-]+$` — a raw GUID/email will NOT fit; `MerchantCustomerId` (≤64, allows `@ . - _`) is the field for our own id. (`Models/Customer.cs`)
- `CardRequest.VaultId` `^[0-9a-zA-Z_-]+$` — reuse a saved card by setting this to the stored vault token id.
- `VaultTokenRequestType` enum value for a setup token: read `Models/Enums/VaultTokenRequestType.cs` at impl time; use the `SETUP_TOKEN` member (verify member name).
- `Money.Value`/`AmountWithBreakdown.Value` regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$` — format amount as invariant `0.00` (2 dp for USD); must equal order total to the cent.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `AuthorizationId` captured/voided/reauthorized must be one returned by an AuthorizeOrder for this order | `Capture/Void/Reauthorize` ← `AuthorizeOrder` | persisted on `Payment.AuthorizationId`; ops load it from the order, never from caller input |
| `CaptureId` refunded must be the one this order's fulfil produced | `RefundCapturedPayment` ← `CaptureAuthorizedPayment` | persisted on `Payment.CaptureId`; refund reads it, caller never supplies it |
| `card.vault_id` used to pay must be a vault id this shopper saved (and not deleted) | `CreateOrder(vault)` ← `CreatePaymentToken` / `DeletePaymentToken` | `SavedPaymentMethod` row looked up by `(buyerId, paymentMethodId)` before authorize |
| Sum of refunds ≤ captured amount | `RefundCapturedPayment` (repeated) | `Payment.RefundedAmount + requested ≤ CapturedAmount` checked before call |
| An order acted on by any shopper endpoint belongs to the caller | pay/refund/my-orders | `order.BuyerId == caller` guard |

### Enums (members needed — read member names from the declaring file at impl)

- `CheckoutPaymentIntent` (`Models/Enums/CheckoutPaymentIntent.cs`): `.Authorize` (wire `AUTHORIZE`), `.Capture`. **Use `.Authorize`.**
- `OrderStatus` (`Models/Enums/OrderStatus.cs`): `Created, Saved, Approved, Voided, Completed, PayerActionRequired`. `PayerActionRequired` ⇒ a browser challenge → STOP & report (see §6).
- `AuthorizationStatus` (`Models/Enums/AuthorizationStatus.cs`): `Created, Captured, Denied, PartiallyCaptured, Voided, …`. Stale honor period is not a distinct status — decide staleness by capture failure / `ExpirationTime`, then reauthorize.
- `CaptureStatus`: `Completed, Declined, PartiallyRefunded, Pending, Refunded, Failed`.
- `RefundStatus` (`Models/Enums/RefundStatus.cs`): read members at impl (`Completed, Pending, Failed, …`).
- `CardBrand` (`Models/Enums/CardBrand.cs`), `PaymentTokenStatus`, `VaultTokenRequestType` — read at impl for exact member names. Enums are `OpenStringEnum<T>`: compare with `==` to static members, or `.ToString()`/`Value` for storage; branch unknowns via generated `Match(..., otherwise)`.

### Client / auth / server facts

- Construct: `new PayPalServerSdkClient(HttpClient, PayPalServerSdkClientOptions)` — sole ctor. Groups are lazy properties. (`PayPalServerSdkClient.cs`)
- Auth: `options.Oauth2 = new OAuth2ClientCredentials { ClientId, ClientSecret }` (`.Core.Authentication.OAuth2.ClientCredentials`). Token cached per client instance; fetched from `{base}/v1/oauth2/token`.
- **BaseUrl override reaches the token request too**: token URL = `server.Default("/v1/oauth2/token")` → resolves through `options.Server.Default.Sandbox.BaseUrl`. So setting that one value applies to token + every call. (`AuthSchemes.cs`, `Server.cs`, `Servers/DefaultOptions.cs`) → when `PayPal:BaseUrl` is set, assign it verbatim; else leave the default.
- `options.Environment = ServerEnvironment.Sandbox` (only environment; `ClosedStringEnum`, `.Servers`).

## 3. Trap notes (name the hazard + the skill; do not resolve here)

- Client & DI lifetime + which HttpClient the registration shares, and singleton stale-DNS. `MUST load dotnet-client-initialization`.
- Credential fail-fast: an unset/blank credential is a silent 401 one round-trip away, not a startup error. `MUST load dotnet-authentication`.
- What `Retry.Timeout` actually bounds vs a whole-call budget; that `POST/PATCH/DELETE` are never resent while `PUT` is; `Retry-After` clamp. `MUST load dotnet-configuration-resilience`.
- `LogRequestBody` logs JSON bodies **unredacted**, and the `PAYPALSERVERSDKCLIENT_LOG` env var can arm it — card PANs are in scope. `MUST load dotnet-configuration-resilience`.
- Pagination bound: reconciliation must cover the whole range AND never loop unbounded; a truncated page set must be visible in the *result*, not only a log. `MUST load dotnet-configuration-resilience`.
- Case-A ladder must enumerate every `TryGet…` (incl. `TryGetNoContent`) with `TryGetRawError` LAST; a drifted 2xx/error body surfaces as `ResponseDeserializationException` (an `ApiException`, not `ApiException<TError>`) and must be caught. `MUST load dotnet-error-handling`.
- Building request models: unions via factory, enums are `OpenStringEnum` (no `new`, use `Match`), `required` members must be set. `MUST load dotnet-models`.
- Test seam is the `HttpClient` ctor arg; match project's frameworks (xUnit for Unit/Integration, MSTest for PublicApiIntegrationTests). `MUST load dotnet-testing`.

## 4. REQUIRED READING (load all before implementation; contents deliberately not copied here)

| Skill (plugin `sdk-skills-test-dev`) | Governs |
| --- | --- |
| `sdk-skills-test-dev:dotnet-client-initialization` | SDK client construction + DI/HttpClient lifetime (step 2) |
| `sdk-skills-test-dev:dotnet-authentication` | OAuth2 credentials + startup fail-fast (step 2) |
| `sdk-skills-test-dev:dotnet-calling-endpoints` | first calls to each operation, request records (step 4) |
| `sdk-skills-test-dev:dotnet-models` | building request bodies, enums/unions, wire names (step 4) |
| `sdk-skills-test-dev:dotnet-error-handling` | the gateway error boundary — **always required** (step 4) |
| `sdk-skills-test-dev:dotnet-configuration-resilience` | retries/timeouts/base-URL, pagination, logging/redaction (steps 2,4,5) |
| `sdk-skills-test-dev:dotnet-testing` | tests for the integration layer (step 7) |

Mandatory hazard row: a drifted/malformed **2xx** (missing `required` member) or a **non-2xx** body
not matching the operation's `{Operation}Error` shape surfaces as `ResponseDeserializationException`
— an `ApiException` that keeps the status and names the target type but is **not**
`ApiException<TError>`. The gateway catch ladder catches it explicitly (or `ApiException`), else it
escapes.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalSettings` bound from `PayPal:` in `Program.cs` with `[Required]` on `ClientId, ClientSecret, Environment, Currency`; `AddOptions<PayPalSettings>().Bind(section).ValidateDataAnnotations().ValidateOnStart()`. `[Required]` rejects null **and** empty, so a blank part fails too. Host refuses to boot; message names the key, never the value. |
| 2 | Secret sourcing & rotation | Secrets live in .NET user-secrets (dev) / `PAYPAL_*` env vars mapped to `PayPal:*` (prod); never in repo files. DI builds the SDK client **once at registration** (singleton) capturing options — a rotated secret takes effect on process restart. That is acceptable for this app (no hot-rotation requirement); documented here. |
| 3 | Total timeout budget | `Retry.Timeout` is per-attempt; set it to 30s explicitly. The whole-call budget is a `CancellationToken` deadline (`HttpContext.RequestAborted` linked + `CancelAfter`) applied in the gateway `Bounded(...)` wrapper, so a hung retryable call cannot exceed it. Named `HttpClient.Timeout` also set as a backstop. |
| 4 | Write-retry ownership | All PayPal writes here are `POST` (`CreateOrder, AuthorizeOrder, Capture, Void, Reauthorize, Refund, CreateSetupToken, CreatePaymentToken`) and one `DELETE` — default `HttpMethodsToRetry` (`GET,HEAD,PUT,OPTIONS`) never resends them. No `PUT` in scope. Retries left default for reads (`GetOrder`, `Get*Payment`, `SearchTransactions`). |
| 5 | Idempotency & ambiguous writes | Every write takes a **real** caller-supplied `PayPalRequestId` (`PayPal-Request-Id`; 6h for create, 45d for capture/refund/void, 3h for vault). Keys are derived deterministically from a per-order GUID `Order.PaymentReference` (`create-{g}`, `auth-{g}`, `capture-{g}`, `void-{g}`) and for refunds from the **caller-supplied** idempotency key (`refund-{callerKey}`), so a resend dedupes at PayPal. This is the injected `Idempotency-Key` header's replacement — that header is `Guid.NewGuid()` per call and is NOT used. |
| 6 | Observability | Gateway logs at Information: operation + eShop order id + PayPal ids + status; Warning on provider 4xx/5xx with `ex.StatusCode` and the typed error's `Error.Name/Message`/`debug_id` correlation id where present. `LogRequestBody` stays **off**. No request DTO (card) is ever logged. |
| 7 | Sensitive data | Card PAN/CVV are in scope (`CardRequest`, `SetupTokenRequestCard`). `options.Logging.LogRequestBody=false` **and** `LoggerFactory` assigned explicitly in DI (disarms `PAYPALSERVERSDKCLIENT_LOG`). Card fields never persisted (only brand/last4/expiry stored) and never logged. Our own diagnostics never echo the pay/save request body. |
| 8 | Environment selection | Only `ServerEnvironment.Sandbox` exists; all traffic is sandbox. `PayPal:BaseUrl` optional override applied to `Server.Default.Sandbox.BaseUrl` (covers token + calls). No production host in this SDK, so no risk of live traffic. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. Durable claim = EF-persisted state (`Order.Status` transition guard + unique index on `Refund.IdempotencyKey` per payment) rejecting the second writer via `DbUpdateException`; provider `PayPalRequestId` sits **beside** it. NB the mandated in-memory provider does not enforce unique indexes/concurrency tokens — under that config the runtime guarantee rests on the PayPal idempotency key; on SQL Server the index enforces it. Stated as an environment limitation, not a design gap. |
| 10 | Partial results | Reconciliation walks pages 1..`TotalPages` with a `MaxPages` backstop; if capped, the returned report carries `Truncated=true` + `PagesFetched` (a field the caller reads), not just a log line. |
| 11 | Unknown outcomes | A write that fails with `SdkConnectionException`/`SdkTimeoutException` may have landed. Authorize re-reads via `Orders.GetOrder(paypalOrderId)`; capture/void/reauthorize re-read via `Payments.GetAuthorizedPayment(authorizationId)` / `GetCapturedPayment`; refund re-reads by re-sending under the same `PayPalRequestId`. The order is left in an explicit `PaymentPending`/unknown state (not "failed") and the read settles it. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| pay/authorize | `Order.PaymentStatus` (persisted) transitions `AwaitingPayment→Authorized`; PayPal dedupes via `create-{PaymentReference}`/`auth-{PaymentReference}` | status guard + PayPal request-id (identical key ⇒ same authorization) | app service returns existing payment state | `OrderPaymentService.PayAsync` |
| fulfil/capture | `Payment.CaptureId` set once; `Order.PaymentStatus→Paid` | non-null `CaptureId` guard + `capture-{PaymentReference}` at PayPal | app service returns existing capture | `OrderPaymentService.FulfilAsync` |
| cancel/void | `Order.PaymentStatus→Cancelled` guard | status guard + `void-{PaymentReference}` | app service returns cancelled state | `OrderPaymentService.CancelAsync` |
| refund | `Payment.Refunds` row keyed by caller idempotency key (unique per payment) | unique index on `(PaymentId, IdempotencyKey)` in `PaymentConfiguration` → `DbUpdateException`; PayPal `refund-{key}` | `Payment.FindRefundByKey` returns existing → app service returns it | `OrderPaymentService.RefundAsync` |
| save card | `SavedPaymentMethod` row; vault `setup-{g}`/`token-{g}` at PayPal | new row per save (distinct cards allowed); PayPal request-id on retry | n/a (each save is distinct) | `SavedCardService.SaveCardAsync`, `PayPalGateway.VaultCardAsync` |

**PAGED READS**

| Read | What caps it | How the caller learns it was cut short | Where in the code |
| --- | --- | --- | --- |
| `SearchTransactions` reconciliation | `TotalPages` (natural) + `MaxReconciliationPages=50` backstop | `ReconciliationTransactions.Truncated`/`ReconciliationReport.Truncated` + `PagesFetched` fields in the returned DTO | `PayPalGateway.SearchTransactionsAsync` |

**UNKNOWN OUTCOMES**

| Write | Re-read operation | Reference searched by | Where in the code |
| --- | --- | --- | --- |
| CreateOrder/AuthorizeOrder | `Orders.GetOrder` (in-gateway, id in scope) | `payPalOrderId` from the create response | `PayPalGateway.AuthorizeAsync` (GetOrder re-read when no inline authorization); a full retry of `PayAsync` re-uses `create-`/`auth-` keys so PayPal dedupes |
| CaptureAuthorizedPayment | idempotent re-send under `capture-{PaymentReference}` (PayPal returns the existing capture); `GetAuthorizedPayment` available via `IPaymentGateway.GetAuthorizationAsync` | `Payment.AuthorizationId` (persisted before capture) | `OrderPaymentService.FulfilAsync` — `CaptureId` stays null until success, so a retry re-sends with the same key |
| VoidPayment | idempotent re-send under `void-{PaymentReference}` | `Payment.AuthorizationId` | `OrderPaymentService.CancelAsync` |
| RefundCapturedPayment | re-send with same `PayPalRequestId` (dedupes) | `refund-{callerKey}` | `OrderPaymentService.RefundAsync` |

NB `VoidPayment` returns **204 No Content** on success; `PayPalGateway.VoidAsync` treats a 2xx `ResponseDeserializationException` (empty body) as a successful void.

## 6. Assumptions & Blockers

- **Refunds are shopper-scoped**, not operator: the task names only fulfil/cancel/reconciliation as
  operator actions and says "every other endpoint is shopper-scoped." So `POST /api/orders/{id}/refunds`
  is authorized for the owning shopper. (Assumption from explicit task wording.)
- **`POST /api/orders` has no basket** (PublicApi has its own store); request carries
  `{catalogItemId, quantity}[]`. A default ship-to address is synthesized if none supplied (Order
  requires one) — amounts come only from catalog prices, not the request.
- **Saved-card ownership** is enforced by our own `SavedPaymentMethod` table (buyerId-scoped), which
  is also the source for `GET/DELETE /api/payment-methods`; `Vault.ListCustomerPaymentTokens` is not
  used (customer_id ≤22 regex can't hold our buyer id; DB scoping is stricter). Not a gap.
- **Browser challenge**: if CreateOrder/Authorize returns `PAYER_ACTION_REQUIRED` (or a payer-action
  HATEOAS link), STOP and report — no approval round-trip is built (per task). The sandbox test card
  `4111...` is not expected to challenge.
- **In-memory provider limits** (no unique-index/concurrency enforcement, data lost on restart) are
  environment constraints per the task; design targets SQL Server correctness with PayPal idempotency
  as the in-memory runtime guarantee. Not a gap.
- **Vault request shape (verified on the sandbox):** card vaulting uses the two-step
  `CreateSetupToken` (card) → `CreatePaymentToken` (setup-token). During self-verification the
  sandbox returned a **500 INTERNAL_SERVER_ERROR** whenever the setup-token card carried a
  `verification_method` (SCA) or the request carried a `customer.merchant_customer_id` (an email);
  the same card `name`+`billing_address` that the pay flow accepts vaults cleanly once those two are
  omitted. So `PayPalGateway.BuildSetupCard` sends card + name + billing address only, no SCA, and no
  PayPal customer id — card ownership is enforced by the app's own `SavedPaymentMethod` store. This is
  a provider request-shape finding, not an SDK gap; the SDK exposed every field involved.
- No SDK capability gap found for the required flows. All operations exist on the map, and all flows
  were verified end-to-end on the sandbox with the test card (authorize, capture with fee/net, void,
  partial refund + idempotency, saved-card reuse + delete, reconciliation).
