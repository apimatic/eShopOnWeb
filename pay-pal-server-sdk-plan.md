# PayPal Server SDK integration plan — eShopOnWeb PublicApi

Add card payments (authorize → capture → cancel/refund) and saved cards (vault) to `src/PublicApi`,
additive to the existing catalog/basket/order flow. All PayPal interaction goes through the
vendored PayPal Server SDK (.NET). Contract facts below come from the SDK map/source; nothing from memory.

## 1. Scope & sequence

| Step | What | PayPal operations used |
| --- | --- | --- |
| S0 | Vendor SDK source into `src/PayPalServerSdk` (`ManagePackageVersionsCentrally=false`); add to sln; `ProjectReference` from Infrastructure | — |
| S1 | Domain: `Payment` aggregate (per Order, owns PayPal state + refunds) and `SavedPaymentMethod` aggregate (per shopper). EF configs + DbSets on `CatalogContext`. | — |
| S2 | `PayPalSettings` bound from `PayPal:` section; fail-fast validation; client registered as singleton via SDK options; `IPayPalPaymentGateway` abstraction in ApplicationCore, impl in Infrastructure | client construction |
| S3 | `POST /api/payment-methods` save card; `GET` list; `DELETE` remove | `Vault.CreatePaymentToken`, `Vault.DeletePaymentToken` |
| S4 | `POST /api/orders` place (AwaitingPayment); `POST /api/orders/{id}/pay` authorize hold | `Orders.CreateOrder` (single-step, intent=AUTHORIZE, card OR vault_id) |
| S5 | `POST /api/orders/{id}/fulfil` capture (renew if stale); `POST /api/orders/{id}/cancel` void; `POST /api/orders/{id}/refunds` refund | `Payments.CaptureAuthorizedPayment`, `Payments.GetAuthorizedPayment`, `Payments.ReauthorizePayment`, `Payments.VoidPayment`, `Payments.RefundCapturedPayment` |
| S6 | `GET /api/my-orders`; `GET /api/reconciliation?from&to` (page loop over whole range) | `TransactionSearch.SearchTransactions` |

Auth: place/pay/refund/my-orders/payment-methods = shopper-scoped (JWT `ClaimTypes.Name`); fulfil/cancel/reconciliation = ADMINISTRATORS role.

## 2. CONTRACT SHEET

⚠ Signatures are **generated code, verbatim**. Every operation that takes input takes **ONE request record** as its first parameter, built with an object initializer using the record's own property names — never flat args.
⚠ Every SDK type is written **fully-qualified from the namespace its source path implies** (`Models/` → `PayPalServerSdk.Models`, `Models/Enums/` → `PayPalServerSdk.Models.Enums`, `Requests/<Ctrl>/` → `PayPalServerSdk.Requests.<Ctrl>`, `Errors/` → `PayPalServerSdk.Errors`), taken from THAT type's path.

| Op | Controller | Method signature · required | Body model + fields read/written | Response envelope → fields read | Error case + accessors | Source |
| --- | --- | --- | --- | --- | --- | --- |
| CreateOrder | `client.Orders` | `CreateOrder(CreateOrderRequest request, …)`; req: `Body` (also set `PayPalRequestId`, `Prefer="return=representation"`) | `OrderRequest{ Intent(intent):CheckoutPaymentIntent req, PurchaseUnits(purchase_units):IReadOnlyList<PurchaseUnitRequest> req, PaymentSource(payment_source):PaymentSource? }`; `PurchaseUnitRequest{ Amount(amount):AmountWithBreakdown req, CustomId(custom_id), InvoiceId(invoice_id), Description }`; `AmountWithBreakdown{ CurrencyCode(currency_code) req, Value(value) req }`; `PaymentSource{ Card(card):CardRequest? }`; `CardRequest{ Name,Number,Expiry(YYYY-MM),SecurityCode,BillingAddress:Address,VaultId }`; `Address{ CountryCode req, AddressLine1, AdminArea1, AdminArea2, PostalCode }` | `Order` → `Id`, `Status`(OrderStatus), `PurchaseUnits[0].Payments.Authorizations[0]` → `.Id`,`.Status`(AuthorizationStatus),`.ExpirationTime`,`.Amount` | A `ApiException<CreateOrderError>`: `TryGetError(out Error)`[400,401,422] · `TryGetRawError` | map/operations/Orders.md; Models/OrderRequest.cs; Models/PurchaseUnitRequest.cs; Models/AmountWithBreakdown.cs; Models/PaymentSource.cs; Models/CardRequest.cs; Models/Address.cs; Models/Order.cs; Models/PurchaseUnit.cs; Models/PaymentCollection.cs; Models/AuthorizationWithAdditionalData.cs |
| CaptureAuthorizedPayment | `client.Payments` | `CaptureAuthorizedPayment(CaptureAuthorizedPaymentRequest request, …)`; req: `AuthorizationId` (set `PayPalRequestId`, `Prefer="return=representation"`, `Body`) | `CaptureRequest{ Amount(amount):Money?, FinalCapture(final_capture):bool? }`; `Money{ CurrencyCode req, Value req }` | `CapturedPayment` → `Id`, `Status`(CaptureStatus), `Amount`, `SellerReceivableBreakdown` → `GrossAmount`,`PaypalFee`,`NetAmount` (each `Money`) | A `ApiException<CaptureAuthorizedPaymentError>`: `TryGetError(out Error)`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` | map/operations/Payments.md; Models/CaptureRequest.cs; Models/CapturedPayment.cs; Models/SellerReceivableBreakdown.cs; Models/Money.cs |
| GetAuthorizedPayment | `client.Payments` | `GetAuthorizedPayment(GetAuthorizedPaymentRequest request,…)`; req: `AuthorizationId` | — | `PaymentAuthorization` → `Status`(AuthorizationStatus), `ExpirationTime`, `Id`, `Amount` | A `ApiException<GetAuthorizedPaymentError>`: `TryGetError`[401,403,404] · `TryGetNoContent`[500] · `TryGetRawError` | map/operations/Payments.md; Models/PaymentAuthorization.cs |
| ReauthorizePayment | `client.Payments` | `ReauthorizePayment(ReauthorizePaymentRequest request,…)`; req: `AuthorizationId` (set `PayPalRequestId`, `Prefer="return=representation"`, `Body`) | `ReauthorizeRequest{ Amount(amount):Money? }` | `PaymentAuthorization` → `Id`,`Status`,`ExpirationTime` | A `ApiException<ReauthorizePaymentError>`: `TryGetError`[400,401,403,404,422] · `TryGetNoContent`[500] · `TryGetRawError` | map/operations/Payments.md; Models/ReauthorizeRequest.cs; Models/PaymentAuthorization.cs |
| VoidPayment | `client.Payments` | `VoidPayment(VoidPaymentRequest request,…)`; req: `AuthorizationId` (set `PayPalRequestId`) | — (no body) | `PaymentAuthorization` → `Status`(expect VOIDED) | A `ApiException<VoidPaymentError>`: `TryGetError`[401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | map/operations/Payments.md; Requests/Payments/VoidPaymentRequest.cs |
| RefundCapturedPayment | `client.Payments` | `RefundCapturedPayment(RefundCapturedPaymentRequest request,…)`; req: `CaptureId` (set `PayPalRequestId`=caller key, `Prefer="return=representation"`, `Body`) | `RefundRequest{ Amount(amount):Money?, NoteToPayer, CustomId, InvoiceId }` | `Refund` → `Id`, `Status`(RefundStatus), `Amount` | A `ApiException<RefundCapturedPaymentError>`: `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | map/operations/Payments.md; Models/RefundRequest.cs; Models/Refund.cs |
| GetCapturedPayment | `client.Payments` | `GetCapturedPayment(GetCapturedPaymentRequest request,…)`; req: `CaptureId` | — | `CapturedPayment` → as above | A `ApiException<GetCapturedPaymentError>`: `TryGetError`[401,403,404] · `TryGetNoContent`[500] · `TryGetRawError` | map/operations/Payments.md |
| GetRefund | `client.Payments` | `GetRefund(GetRefundRequest request,…)`; req: `RefundId` | — | `Refund` → `Id`,`Status`,`Amount` | A `ApiException<GetRefundError>`: `TryGetError`[401,403,404] · `TryGetNoContent`[500] · `TryGetRawError` | map/operations/Payments.md |
| CreatePaymentToken | `client.Vault` | `CreatePaymentToken(CreatePaymentTokenRequest request,…)`; req: `Body` (set `PayPalRequestId`) | `PaymentTokenRequest{ Customer(customer):Customer?{Id,MerchantCustomerId}, PaymentSource(payment_source):PaymentTokenRequestPaymentSource req }`; `PaymentTokenRequestPaymentSource{ Card(card):PaymentTokenRequestCard? }`; `PaymentTokenRequestCard{ Name,Number,Expiry,SecurityCode,BillingAddress:Address }` | `PaymentTokenResponse` → `Id`(vault id), `Customer.Id`, `PaymentSource.Card`(CardPaymentTokenEntity) → `LastDigits`,`Brand`(CardBrand),`Expiry`,`Name` | A `ApiException<CreatePaymentTokenError>`: `TryGetError`[400,403,404,422,500] · `TryGetRawError` | map/operations/Vault.md; Models/PaymentTokenRequest.cs; Models/PaymentTokenRequestPaymentSource.cs; Models/PaymentTokenRequestCard.cs; Models/PaymentTokenResponse.cs; Models/PaymentTokenResponsePaymentSource.cs; Models/CardPaymentTokenEntity.cs; Models/Customer.cs |
| DeletePaymentToken | `client.Vault` | `DeletePaymentToken(DeletePaymentTokenRequest request,…)`; req: `Id` | — | `void` (Task) | A `ApiException<DeletePaymentTokenError>`: `TryGetError`[400,403,500] · `TryGetRawError` | map/operations/Vault.md; Requests/Vault/DeletePaymentTokenRequest.cs |
| SearchTransactions | `client.TransactionSearch` | `SearchTransactions(SearchTransactionsRequest request,…)`; req: `StartDate`,`EndDate` (ISO-8601; also `Fields="all"` to get invoice_id/custom_field, `PageSize`, `Page`) | — (query only) | `SearchResponse` → `TransactionDetails[].TransactionInfo`(TransactionInformation → `TransactionId`,`TransactionStatus`,`TransactionAmount`(Money),`FeeAmount`,`CustomField`,`InvoiceId`,`TransactionInitiationDate`,`TransactionEventCode`), `Page`,`TotalItems`,`TotalPages` | **B** `ApiException<RawError>` (StatusCode/ReadAsString/ReadAsJson) | map/operations/TransactionSearch.md; Requests/TransactionSearch/SearchTransactionsRequest.cs; Models/SearchResponse.cs; Models/TransactionDetails.cs; Models/TransactionInformation.cs |

### Enums (Models/Enums/, namespace PayPalServerSdk.Models.Enums)

| Enum | Members used (C# static · wire) | Source |
| --- | --- | --- |
| `CheckoutPaymentIntent` | `.Authorize`·AUTHORIZE (also `.Capture`) | Models/Enums/CheckoutPaymentIntent.cs |
| `OrderStatus` | `.Created`,`.Completed`,`.Voided`,`.PayerActionRequired`(→ STOP/report challenge),`.Approved` | Models/Enums/OrderStatus.cs |
| `AuthorizationStatus` | `.Created`,`.Captured`,`.PartiallyCaptured`,`.Voided`,`.Denied`,`.Pending` | Models/Enums/AuthorizationStatus.cs |
| `CaptureStatus` | read `.ToString()`/compare; expect COMPLETED / PENDING / DECLINED | Models/Enums/CaptureStatus.cs (open enum — `otherwise` arm) |
| `RefundStatus` | COMPLETED / PENDING / CANCELLED / FAILED | Models/Enums/RefundStatus.cs |
| `CardBrand` | display only (`.ToString()`) | Models/Enums/CardBrand.cs |

Enums are `OpenStringEnum<T>` records (not C# enums): compare with static members via `==`; persist raw wire value via `.ToString()`; unknown values reach `Match`'s `otherwise` arm. (Confirm `.ToString()`/`.Value` accessor in **dotnet-models**.)

### Client construction / auth / servers

- Client: `new PayPalServerSdkClient(HttpClient, PayPalServerSdkClientOptions)`. Only constructor. `client.Orders/Payments/Vault/TransactionSearch`. Source: PayPalServerSdkClient.cs, sdk-map.md.
- Auth: `options.Oauth2 = new OAuth2ClientCredentials{ ClientId, ClientSecret }` (namespace `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`). OAuth2 token from `<base>/v1/oauth2/token`. Source: sdk-map.md Servers & auth.
- Environment: only `ServerEnvironment.Sandbox` exists (namespace `PayPalServerSdk.Servers`). No Live member — see §6/PR-8. Base-URL override: `options.Server.Default.Sandbox.BaseUrl`. Source: sdk-map.md; Servers/ServerEnvironment.cs.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `vault_id` used to pay must be a card the caller saved | `Orders.CreateOrder`(card.vault_id) ← `Vault.CreatePaymentToken` | before CreateOrder: look up `SavedPaymentMethod` by (paymentMethodId, BuyerId); reject if not owned/deleted |
| `AuthorizationId` captured/reauthorized/voided must come from this order's authorize | `Payments.Capture/Reauthorize/Void` ← `Orders.CreateOrder` | read `Payment.AuthorizationId` for the caller's order; never from request body |
| `CaptureId` refunded must come from this order's capture | `Payments.RefundCapturedPayment` ← `Payments.CaptureAuthorizedPayment` | read `Payment.CaptureId`; refund amount + prior refunds must not exceed captured amount |
| `orderId` acted on belongs to caller (or caller is admin) | all order ops | check `Order.BuyerId == caller` (shopper) before any op |

⚠ These are derived from the task, not the map.

## 3. Trap notes (hazard + skill; not resolved here)

- Client is a long-lived singleton wrapping one `HttpClient`; getting HttpClient/handler lifetime wrong leaks sockets or breaks DNS refresh. `MUST load dotnet-client-initialization`.
- Missing/blank credential surfaces as a 401 on first call, not at startup, unless we fail-fast. `MUST load dotnet-authentication`.
- Every input rides on the request record; setting the wrong member (or flat args) won't compile the way I expect, and `Prefer`/`PayPalRequestId` placement decides idempotency + whether the breakdown comes back. `MUST load dotnet-calling-endpoints`.
- Enums are `OpenStringEnum`, unions use `TryGet…`, and unknown response fields land in an extension bag — treating them as plain C# enums/objects is wrong. `MUST load dotnet-models`.
- Two error cases coexist (A typed vs B raw for SearchTransactions); a drifted 2xx/non-2xx body throws `ResponseDeserializationException` which is NOT `ApiException<TError>`; `TryGetNoContent`/`TryGetRawError` are distinct. `MUST load dotnet-error-handling`.
- `Timeout` is per-attempt not total; `POST`/`DELETE` are never auto-retried but a hung call still multiplies the timeout; `LogRequestBody` logs card PANs unredacted and the `PAYPALSERVERSDKCLIENT_LOG` env var can arm it unless `LoggerFactory` is set explicitly. `MUST load dotnet-configuration-resilience`.
- The SDK seam for tests is the `HttpClient` ctor arg; assert behaviour not execution. `MUST load dotnet-testing`.

## 4. REQUIRED READING (load all before implementing; sheet omits their contents)

- `sdk-skills-test-dev:dotnet-client-initialization` — S2 client/DI construction.
- `sdk-skills-test-dev:dotnet-authentication` — S2 credential wiring + fail-fast.
- `sdk-skills-test-dev:dotnet-calling-endpoints` — S3–S6 every SDK call + request records + idempotency members.
- `sdk-skills-test-dev:dotnet-models` — building card/amount models, reading enums/unions/extension bag.
- `sdk-skills-test-dev:dotnet-error-handling` — the error boundary around every SDK call (always required). Hazard row: a drifted/malformed **2xx** (missing `required`) or a **non-2xx** body not matching `{Operation}Error` surfaces as `ResponseDeserializationException` — an `ApiException` that keeps status + target type but is **not** `ApiException<TError>`; a ladder catching only `ApiException<TError>` lets it escape, so also catch `ResponseDeserializationException` (or `ApiException`).
- `sdk-skills-test-dev:dotnet-configuration-resilience` — timeouts/retries/base-URL/pagination/logging.
- `sdk-skills-test-dev:dotnet-testing` — S? tests for the gateway.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalSettings` bound from `PayPal:`. On startup validate `ClientId`, `ClientSecret`, `Currency` all non-blank (each part of the credential checked separately); `Environment` present. Throw at registration if any blank → host refuses to start. |
| 2 | Secret sourcing & rotation | Secrets from .NET user-secrets (loaded from env vars by an out-of-repo step). SDK client is a **singleton built once at registration**, capturing the options → rotated secret needs a process restart. Acceptable; documented. Rotation-without-restart not required. |
| 3 | Total timeout budget | SDK `Timeout` is per-attempt. Enforce a whole-call budget by passing a `CancellationToken` with a deadline (from ASP.NET `RequestAborted` linked to a ~30s timeout) to every SDK call. Configure `RetryOptions` explicitly. |
| 4 | Write-retry ownership | Default retries cover GET/HEAD/PUT/OPTIONS only; our writes are POST/DELETE → SDK never auto-resends them. GET re-reads (GetAuthorizedPayment/GetCapturedPayment/GetRefund) may be retried safely. We keep default `HttpMethodsToRetry`. |
| 5 | Idempotency & ambiguous writes | Real key = `PayPalRequestId` (create-order/authorize: 6h; capture/reauth/void/refund: 45d). Derived from `Payment.PublicId` (a GUID minted once per payment) — stable so a double-click reuses the same key → PayPal dedups, and unique across orders/runs so the in-memory `OrderId` resetting to 1 never collides on PayPal's side. **Learned in sandbox:** keys/refs based on `OrderId` collided (`DUPLICATE_INVOICE_ID`, `DUPLICATE_REQUEST_ID`) — hence `PublicId`. Refund's **caller-supplied** key is stored locally verbatim for per-order dedup, and namespaced as `eshop-refund-{PublicId}-{callerKey}` for the `PayPalRequestId` so the same caller key on a different order does not false-collide; two distinct partial refunds use distinct keys. Local claim = `Payment` row state transition (see DUPLICATE CLAIMS). |
| 6 | Observability | Log at Information: operation, eShop orderId, PayPal ids (order/auth/capture/refund) and status. Log at Error: PayPal error `debug_id`/name/`details[].issue` from the error body. `LogRequestBody` stays **off**. Correlation id = PayPal `debug_id`. |
| 7 | Sensitive data | Card PAN/CVV flow through `CreateOrder` and `CreatePaymentToken` request bodies. So: `LogRequestBody=false` **and** `options.Logging.LoggerFactory` assigned explicitly so `PAYPALSERVERSDKCLIENT_LOG` cannot switch body logging on. Never store PAN/CVV in our DB (only vault id + brand/last4/expiry). Never echo card fields in our own logs/exceptions. |
| 8 | Environment selection | Only `ServerEnvironment.Sandbox` is declared by the SDK — **no Live member**. All traffic targets sandbox. `PayPal:Environment` config is validated but maps only to Sandbox; if a value other than sandbox is supplied we still select Sandbox and log a warning (no Live host exists to send to). `PayPal:BaseUrl`, when set, overrides `options.Server.Default.Sandbox.BaseUrl` verbatim for every call incl. token. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| authorize (`/pay`) | `Payment` row (unique index on `OrderId`), status transition guarded by the `ConcurrencyStamp` concurrency token (rotated on every mutation) | EF `DbUpdateConcurrencyException` on the guarded status transition; PayPal `PayPalRequestId` (from `PublicId`) dedups at provider | `SaveWithConcurrencyGuard` in `PaymentService.PayAsync` catches `DbUpdateConcurrencyException` → `PaymentConflictException` (409); the losing racer re-reads `Authorized` | `PaymentService.PayAsync` → `SaveWithConcurrencyGuard` (Infrastructure/Payments/PaymentService.cs) |
| capture (`/fulfil`) | `Payment` row status `Authorized`→`Fulfilled` guarded by `ConcurrencyStamp` | `DbUpdateConcurrencyException` + `PayPalRequestId` (`eshop-cap-{PublicId}`, 45d) at provider | `PaymentService.FulfilAsync` → `SaveWithConcurrencyGuard` | `PaymentService.FulfilAsync` (before-check `Status != Authorized` → 409; guard on save) |
| refund (`/refunds`) | `PaymentRefund` row keyed by caller key (unique index on (PaymentId, IdempotencyKey)); pre-check `Payment.FindRefundByKey` returns the earlier refund | unique-index violation on second insert + namespaced `PayPalRequestId` at provider | `catch (DbUpdateException)` in `PaymentService.RefundAsync` → reloads and returns the stored refund | `PaymentService.RefundAsync` (`FindRefundByKey` + `AddRefund` + `catch (DbUpdateException)`) |

⚠ EF Core **in-memory** provider (the mandated run) does honour `ConcurrencyStamp` optimistic-concurrency checks across separate scoped `DbContext` instances (one per request), so the guard fires there too; unique-index *enforcement* is SQL-Server-only, so on the in-memory run the effective refund guarantee is the pre-check plus the namespaced `PayPalRequestId` at PayPal. Provider key is *beside* the claim, not instead of it. **Verified in sandbox:** repeating a refund under the same key returned the same `refundId` with no second movement.

**PAGED READS**

| Read | What caps it | How the caller learns it was cut short | Where in the code |
| --- | --- | --- | --- |
| reconciliation (`SearchTransactions`) | PayPal `page_size` (≤500); loop pages `1..TotalPages`, windows of ≤31 days | `ReconciliationResult.Complete`/`PagesFetched`/`TruncatedAtPage` carry it; loops the whole range so it is not cut short; a `MaxPagesPerWindow` backstop sets `Complete=false`+`TruncatedAtPage` | `PayPalPaymentGateway.SearchTransactionsAsync` sets `ReconciliationReport.Complete/TruncatedAtPage`; `PaymentService.ReconcileAsync` maps them onto `ReconciliationResult` (**verified**: 60-day range walked 21 pages, `complete=true`) |

⚠ Report covers the whole `[from,to]`; PayPal max window is 31 days — if range >31d, split into ≤31d sub-windows and loop each. Empty result over a fresh range is a valid sandbox answer, not a gap.

**UNKNOWN OUTCOMES**

| Write | Re-read with | Reference searched by | Where in the code | Test that fails the connection |
| --- | --- | --- | --- | --- |
| authorize | replay `CreateOrder` under same `PayPalRequestId` (`eshop-auth-{PublicId}`, idempotent) | `PayPalRequestId` (derived from `Payment.PublicId`, stable before the call) | gateway maps `SdkConnection/Timeout`→`Unknown`; `PaymentService.PayAsync` → `WithUnknownReplay(..., "authorize")` replays once | `IntegrationTests/PayPal/PayPalPaymentGatewayTests.CaptureAsync_ConnectionFailure_MapsToUnknownOutcome` (transport fault → `Unknown`) |
| capture | replay `CaptureAuthorizedPayment` under same `PayPalRequestId` (`eshop-cap-{PublicId}`) | `PayPalRequestId` + `AuthorizationId` | `PaymentService.FulfilAsync` → `WithUnknownReplay(..., "capture")` | same gateway `Unknown`-classification test |
| refund | replay `RefundCapturedPayment` under same namespaced `PayPalRequestId` | namespaced provider key + `CaptureId` | `PaymentService.RefundAsync` → `WithUnknownReplay(..., "refund")` | gateway `Unknown`-classification test |
| void | replay `VoidPayment` under same `PayPalRequestId` (`eshop-void-{PublicId}`) | `PayPalRequestId` + `AuthorizationId` | `PaymentService.CancelAsync` → `WithUnknownReplay(..., "void")`; a 204 (empty body) is treated as success in the gateway | gateway `Unknown`-classification test |

## 6. Assumptions & Blockers

- **No Blockers.** Every task capability maps to an SDK operation (place=domain only; pay=CreateOrder single-step; fulfil=Capture; cancel=Void; refund=Refund; save card=Vault.CreatePaymentToken; delete=Vault.DeletePaymentToken; reconcile=SearchTransactions).
- Assumption: `/pay` uses **single-step CreateOrder with card/vault_id + intent=AUTHORIZE** (SDK doc: `PayPalRequestId` "mandatory for all single-step create order calls … with payment source information like Card, PayPal.vault_id"). The authorization hold appears at `Order.PurchaseUnits[0].Payments.Authorizations[0]`. `AuthorizeOrder` (2-step) not used. `YOUR CALL — not in the map`.
- Assumption: caller identity = JWT `ClaimTypes.Name` (= username/email) == `Order.BuyerId`, matching existing app. Each shopper gets a stable PayPal `customer_id` (generated once, stored on first saved card) so vaulted cards associate to a customer. `YOUR CALL`.
- `UNVERIFIED` (live-only): exact PayPal error `issue` string signalling an expired/uncapturable authorization. Defensive directive: on capture failure, if `GetAuthorizedPayment` status ∈ {expired/EXPIRED, not CREATED/PENDING} or `ExpirationTime` is past → `ReauthorizePayment` then retry capture; if reauthorize itself errors, surface an operator-actionable message (PayPal `details[].issue`/`description`) rather than a bare 500. Also: a `PAYER_ACTION_REQUIRED`/3DS challenge on a card payment → STOP and report (do NOT build an approval round-trip). No such challenge expected for sandbox `4111…`.
- `UNVERIFIED`: whether `custom_id`/`invoice_id` we set on the purchase unit surface as `custom_field`/`invoice_id` in TransactionSearch reporting (documented to, but reporting lags). Reconciliation matches on both fields and also reports PayPal-only / eShop-only rows.

### Sandbox verification (real calls on the sandbox business account)

All flows were driven end-to-end through PublicApi with the sandbox Visa test card `4111 1111 1111 1111`:
- **Authorize** — hold placed = order total to the cent (39.00 for 19.50×2), `authorizationId`+`status=CREATED` returned; a double-click returned the **same** authorization id (idempotent).
- **Fulfil/capture** — captured 39.00 with `paypal_fee=1.50`, `net=37.50`; admin-only (demouser → 403).
- **Refund** — partial 10.00 then 5.00 (distinct keys), repeat of the same key returned the same `refundId` (no double refund), over-remaining rejected (400).
- **Cancel/void** — authorization VOIDED, idempotent, and a fulfilled/cancelled order rejects the wrong transition (409). PayPal void returns **204 No Content** — handled as success (the SDK cannot deserialise the empty body).
- **Saved cards** — vaulted (VISA ****1111, no PAN stored), reused to pay a second order via `savedPaymentMethodId`, deleted (→ 204, then unusable → 404).
- **Reconciliation** — 60-day range walked 21 pages (`complete=true`); this run's just-created orders showed as **eShop-only** (PayPal reporting lag, an expected sandbox result, not a gap) alongside historical **PayPal-only** rows.
- **Ownership/roles** — cross-shopper access returns 404; operator endpoints reject non-admins (403).
- **Fail-fast** — blanking a credential aborts startup naming the key, without binding a port.

No card challenge (3DS / PAYER_ACTION_REQUIRED) was encountered for the sandbox card; the code stops and reports it if one ever occurs.
