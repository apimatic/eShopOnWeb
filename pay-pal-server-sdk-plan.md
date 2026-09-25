# PayPal Server SDK integration plan — eShopOnWeb PublicApi

Additive card-payments + saved-cards capability on `src/PublicApi`, using the PayPal Server SDK
(.NET, root namespace `PayPalServerSdk`) as the **sole** way to talk to PayPal. Every contract
fact below is taken from the SDK map / map-named source; nothing from memory.

---

## 1. Scope & sequence

App-side identity: the caller is `HttpContext.User.Identity.Name` (JWT `ClaimTypes.Name`,
issued by the existing `/api/authenticate`). eShop `Order.BuyerId` = that username; saved cards
and orders are scoped by it. `YOUR CALL` decisions are marked in §7.

The **eShop order id** (`int`, our DB) is what every `/api/orders/{orderId}/…` route names; the
**PayPal order id** (string) and the authorization / capture / refund ids live in the order's new
`OrderPayment` state. PayPal owns those ids; we persist them so a later request can act on them.

Steps, in build order (each names the SDK operations it uses; `client` = `PayPalServerSdkClient`):

1. **Client + config + fail-fast** — vendor SDK source as a buildable project; bind `PayPal:` options;
   register singleton `PayPalServerSdkClient` over an `IHttpClientFactory` client. No SDK op.
2. **Save a card** (`POST /api/payment-methods`) — `client.Vault.CreateSetupToken` (card) →
   `client.Vault.CreatePaymentToken` (token=setup-token-id). Returns vault token id + safe descriptor
   (last digits / brand / expiry). Fallback: direct `CreatePaymentToken` with raw card if setup-token
   path is not needed. Persist `{paymentMethodId(GUID), buyerId, payPalVaultId, brand, last4, expiry}`.
3. **List / delete saved cards** — `GET` served from our own store (safe descriptors only);
   `DELETE` → `client.Vault.DeletePaymentToken(Id)` then remove our row.
4. **Place order** (`POST /api/orders`) — reuse `Order`/`OrderItem`; no SDK op; state = `AwaitingPayment`.
5. **Pay = authorize/hold** (`POST /api/orders/{id}/pay`) — `client.Orders.CreateOrder`
   (intent=`Authorize`, one purchase unit, amount = order total, `invoice_id`+`custom_id` = our reference,
   `payment_source.card` with raw card **or** `card.vault_id` = saved card) → then
   `client.Orders.AuthorizeOrder(id)` to get the authorization id + status (unless already present inline).
   Held amount = order total to the cent.
6. **Fulfil = capture** (`POST /api/orders/{id}/fulfil`, operator) — `client.Payments.CaptureAuthorizedPayment(authorizationId)`;
   record captured amount + `seller_receivable_breakdown.paypal_fee` + `net_amount`. Stale hold →
   `client.Payments.ReauthorizePayment(authorizationId)` first, then capture the (re)authorization; a hold
   that can no longer be renewed → operator-actionable error.
7. **Cancel = void** (`POST /api/orders/{id}/cancel`, operator) — `client.Payments.VoidPayment(authorizationId)`
   before capture; funds released.
8. **Refund** (`POST /api/orders/{id}/refunds`) — `client.Payments.RefundCapturedPayment(captureId)`,
   full or partial (`amount`), caller idempotency key. Enforce Σ refunds ≤ captured.
9. **My orders** (`GET /api/my-orders`) — our store, scoped to caller, with payment state.
10. **Reconciliation** (`GET /api/reconciliation`, operator) — `client.TransactionSearch.SearchTransactions`
    over `[from,to]`, **all pages** (loop 1..total_pages), lined up against our orders' PayPal references.

Settle/re-read after a broken connection uses `client.Orders.GetOrder`,
`client.Payments.GetAuthorizedPayment`, `client.Payments.GetCapturedPayment`, `client.Payments.GetRefund`.

---

## 2. CONTRACT SHEET

> ⚠ **Signatures are generated code, verbatim.** Each operation that takes input takes **ONE request
> record** as its first parameter, built with an object initializer whose property names are the
> record's own — never flat arguments. An operation with no inputs takes none.
> ⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**, taken
> from the path the map gives for THAT type (`Models/` → `PayPalServerSdk.Models`; `Models/Enums/` →
> `PayPalServerSdk.Models.Enums`; `Errors/` → `PayPalServerSdk.Errors`; `Requests/<Ctrl>/` →
> `PayPalServerSdk.Requests.<Ctrl>`; client/options/servers per getting-started). Never borrow a
> neighbour's namespace.

Client construction / auth / servers (source: `sdk-map.md` *Getting a client*, *Servers & auth*;
`PayPalServerSdkClient.cs`; `Servers/DefaultOptions.cs`; `AuthSchemes.cs`):

- Ctor: `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` — the only ctor.
- Auth: `options.Oauth2 = new OAuth2ClientCredentials { ClientId=…, ClientSecret=… }`
  (ns `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`). OAuth2 client-credentials; token
  fetched by the SDK from `server.Default("/v1/oauth2/token")`.
- Environment: only `ServerEnvironment.Sandbox` exists (ns `PayPalServerSdk.Servers`); `options.Environment = ServerEnvironment.Sandbox`.
- **Base-URL override**: `options.Server.Default.Sandbox.BaseUrl` (default `https://api-m.sandbox.paypal.com`).
  `AuthSchemes.cs` builds the token URL through the **same** `server.Default(...)`, so setting this one
  property redirects **every** call including the token request — exactly what `PayPal:BaseUrl` requires.

Per-operation rows (controller · signature · request record + required members · body model + fields
read/written · response envelope + inner fields read · error case + accessors + payload · pagination · source):

| Op | Signature (required members) | Body model → fields used | Returns → inner fields read | Error | Source |
| --- | --- | --- | --- | --- | --- |
| `client.Vault.CreateSetupToken` | `CreateSetupToken(CreateSetupTokenRequest{ required Body; PayPalRequestId? })` | `SetupTokenRequest{ required PaymentSource: SetupTokenRequestPaymentSource{ Card: SetupTokenRequestCard{ Number(number), Expiry(expiry), SecurityCode(security_code), Name(name), BillingAddress } }, Customer? }` | `SetupTokenResponse{ Id, Customer, Status, PaymentSource }` → `Id` | A `ApiException<CreateSetupTokenError>`; `TryGetError(out Error)`[400,403,422,500] · `TryGetRawError` | Vault.md; `Models/SetupTokenRequest.cs`, `Models/SetupTokenRequestPaymentSource.cs`, `Models/SetupTokenRequestCard.cs`, `Models/SetupTokenResponse.cs` |
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(CreatePaymentTokenRequest{ required Body; PayPalRequestId? })` | `PaymentTokenRequest{ required PaymentSource: PaymentTokenRequestPaymentSource{ Card: PaymentTokenRequestCard{ Number,Expiry,SecurityCode,Name }, Token: VaultTokenRequest{ required Id, required Type=VaultTokenRequestType.SetupToken } }, Customer? }` | `PaymentTokenResponse{ Id, Customer(CustomerResponse), PaymentSource: PaymentTokenResponsePaymentSource{ Card: CardPaymentTokenEntity{ LastDigits(last_digits), Brand, Expiry, Name } } }` → `Id`, `PaymentSource.Card.{LastDigits,Brand,Expiry}` | A `ApiException<CreatePaymentTokenError>`; `TryGetError`[400,403,404,422,500] · `TryGetRawError` | Vault.md; `Models/PaymentTokenRequest.cs`, `…PaymentSource.cs`, `…Card.cs`, `Models/VaultTokenRequest.cs`, `Models/PaymentTokenResponse.cs`, `Models/PaymentTokenResponsePaymentSource.cs`, `Models/CardPaymentTokenEntity.cs` |
| `client.Vault.DeletePaymentToken` | `DeletePaymentToken(DeletePaymentTokenRequest{ required Id })` | — | `void` (Task) | A `ApiException<DeletePaymentTokenError>`; `TryGetError`[400,403,500] · `TryGetRawError` | Vault.md |
| `client.Vault.ListCustomerPaymentTokens` | `ListCustomerPaymentTokens(ListCustomerPaymentTokensRequest{ required CustomerId; PageSize=5; Page=1; TotalRequired=false })` | query: `customer_id,page_size,page,total_required` | `CustomerVaultPaymentTokensResponse{ TotalItems, TotalPages, PaymentTokens[], Customer }` | A `TryGetError`[400,403,500] · `TryGetRawError` | Vault.md; `Models/CustomerVaultPaymentTokensResponse.cs` (used only if reconciling vault; primary list = our DB — §7) |
| `client.Orders.CreateOrder` | `CreateOrder(CreateOrderRequest{ required Body; PayPalRequestId?; Prefer="return=minimal" })` | `OrderRequest{ required Intent: CheckoutPaymentIntent.Authorize, required PurchaseUnits:[ PurchaseUnitRequest{ required Amount: AmountWithBreakdown{ required CurrencyCode, required Value }, InvoiceId, CustomId, Description } ], PaymentSource: PaymentSource{ Card: CardRequest{ Number,Expiry,SecurityCode,Name, VaultId } } }` | `Order{ Id, Status:OrderStatus, PurchaseUnits[PurchaseUnit{ Payments:PaymentCollection{ Authorizations[AuthorizationWithAdditionalData{ Id, Status, ExpirationTime, Amount }], Captures[OrdersCapture], Refunds[Refund] } }] }` → `Id`, `Status`, inline auth if present | A `ApiException<CreateOrderError>`; `TryGetError`[400,401,422] · `TryGetRawError` | Orders.md; `Models/OrderRequest.cs`, `Models/PurchaseUnitRequest.cs`, `Models/AmountWithBreakdown.cs`, `Models/PaymentSource.cs`, `Models/CardRequest.cs`, `Models/Order.cs`, `Models/PurchaseUnit.cs`, `Models/PaymentCollection.cs`, `Models/AuthorizationWithAdditionalData.cs` |
| `client.Orders.AuthorizeOrder` | `AuthorizeOrder(AuthorizeOrderRequest{ required Id; Body:OrderAuthorizeRequest?; PayPalRequestId?; Prefer })` | `OrderAuthorizeRequest{ PaymentSource? }` (send `null` body when card already on the created order) | `OrderAuthorizeResponse{ Id, Status:OrderStatus, PurchaseUnits[PurchaseUnit{ Payments.Authorizations[AuthorizationWithAdditionalData{ Id, Status:AuthorizationStatus, ExpirationTime, Amount:Money }] }] }` → auth `Id`,`Status`,`ExpirationTime`,`Amount` | A `ApiException<AuthorizeOrderError>`; `TryGetError`[400,401,403,404,422,500] · `TryGetRawError` | Orders.md; `Models/OrderAuthorizeRequest.cs`, `Models/OrderAuthorizeResponse.cs`, `Models/AuthorizationWithAdditionalData.cs` |
| `client.Orders.GetOrder` | `GetOrder(GetOrderRequest{ required Id; Fields? })` | query `fields` | `Order` (same as above) → walk `PurchaseUnits[].Payments.{Authorizations,Captures,Refunds}` to settle | A `TryGetError`[401,404] · `TryGetRawError` | Orders.md |
| `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(CaptureAuthorizedPaymentRequest{ required AuthorizationId; Body:CaptureRequest?; PayPalRequestId?; Prefer })` | `CaptureRequest{ Amount:Money?, FinalCapture=false, InvoiceId? }` (omit Amount ⇒ full capture; FinalCapture=true) | `CapturedPayment{ Id, Status:CaptureStatus, Amount:Money, SellerReceivableBreakdown{ GrossAmount, PaypalFee, NetAmount } }` → `Id`,`Status`,`Amount`,`SellerReceivableBreakdown.{PaypalFee,NetAmount}` | A `ApiException<CaptureAuthorizedPaymentError>`; `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent(out RawError)`[500] · `TryGetRawError` | Payments.md; `Models/CaptureRequest.cs`, `Models/CapturedPayment.cs`, `Models/SellerReceivableBreakdown.cs`, `Models/Money.cs` |
| `client.Payments.ReauthorizePayment` | `ReauthorizePayment(ReauthorizePaymentRequest{ required AuthorizationId; Body:ReauthorizeRequest?; PayPalRequestId?; Prefer })` | `ReauthorizeRequest{ Amount:Money? }` (send order-total amount) | `PaymentAuthorization{ Id, Status:AuthorizationStatus, ExpirationTime, Amount }` → new `Id`,`Status`,`ExpirationTime` | A `TryGetError`[400,401,403,404,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; `Models/ReauthorizeRequest.cs`, `Models/PaymentAuthorization.cs` |
| `client.Payments.VoidPayment` | `VoidPayment(VoidPaymentRequest{ required AuthorizationId; PayPalRequestId?; Prefer })` | — | `PaymentAuthorization{ Id, Status:AuthorizationStatus (→ Voided) }` → `Status` | A `TryGetError`[401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md |
| `client.Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(GetAuthorizedPaymentRequest{ required AuthorizationId })` | — | `PaymentAuthorization{ Id, Status, ExpirationTime, Amount }` | A `TryGetError`[401,403,404] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(RefundCapturedPaymentRequest{ required CaptureId; Body:RefundRequest?; PayPalRequestId?; Prefer })` | `RefundRequest{ Amount:Money?, CustomId?, NoteToPayer? }` (omit Amount ⇒ full) | `Refund{ Id, Status:RefundStatus, Amount:Money }` → `Id`,`Status`,`Amount` | A `ApiException<RefundCapturedPaymentError>`; `TryGetError`[400,401,403,404,409,422] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md; `Models/RefundRequest.cs`, `Models/Refund.cs` |
| `client.Payments.GetCapturedPayment` | `GetCapturedPayment(GetCapturedPaymentRequest{ required CaptureId })` | — | `CapturedPayment` (settle read) | A `TryGetError`[401,403,404] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md |
| `client.Payments.GetRefund` | `GetRefund(GetRefundRequest{ required RefundId })` | — | `Refund` (settle read) | A `TryGetError`[401,403,404] · `TryGetNoContent`[500] · `TryGetRawError` | Payments.md |
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(SearchTransactionsRequest{ required StartDate, required EndDate; Fields="transaction_info"; BalanceAffectingRecordsOnly="Y"; PageSize=100; Page=1; TransactionCurrency? })` | query params per row | `SearchResponse{ TransactionDetails[TransactionDetails{ TransactionInfo:TransactionInformation{ TransactionId, TransactionStatus, TransactionAmount:Money, InvoiceId, CustomField, TransactionInitiationDate } }], TotalPages, TotalItems, Page }` → loop pages | **B** `ApiException<RawError>` (StatusCode, ReadAsString, ReadAsJson<T>) | TransactionSearch.md; `Requests/TransactionSearch/SearchTransactionsRequest.cs`, `Models/SearchResponse.cs`, `Models/TransactionDetails.cs`, `Models/TransactionInformation.cs` |

Enum value tables (C# member ← wire; source `Models/Enums/<Type>.cs`):

- `CheckoutPaymentIntent`: `Capture`, **`Authorize`** (we use Authorize).
- `OrderStatus`: `Created, Saved, Approved, Voided, Completed, PayerActionRequired`.
- `AuthorizationStatus`: `Created, Captured, Denied, PartiallyCaptured, Voided, Pending`.
- `CaptureStatus`: `Completed, Declined, PartiallyRefunded, Pending, Refunded, Failed`.
- `RefundStatus`: `Cancelled, Failed, Pending, Completed`.
- `VaultTokenRequestType`: `SetupToken`.
- `DisbursementMode`: `Instant, Delayed`.

> ⚠ Enums are `OpenStringEnum<T>` records, **not** C# enums — no public factory; use the static
> members (`CheckoutPaymentIntent.Authorize`) and read via `.Match(...)`/`TryGetKnownValue` — see
> `dotnet-models`. The `PayerActionRequired` order status / a card that returns an `approve` link is
> the browser-challenge signal → STOP and report (task mandate), do not build an approval round-trip.

> ⚠ **Required-members caveat.** Several bodies mark *nothing* required beyond a currency/value pair.
> `OrderRequest` requires only `Intent`+`PurchaseUnits`; the *card* lives on the optional
> `PaymentSource` and is what makes a card auth possible — omitting it yields an unfunded order that
> needs browser approval. So although not `required` by the record, `PaymentSource.card` (raw number
> +expiry+security_code, or `vault_id`) is **mandatory for this integration's accept path**.
> `CaptureRequest`/`RefundRequest`/`ReauthorizeRequest` require nothing; we send `Amount` for partials
> and omit it for a full capture/refund. Doc-named fields deliberately left out: `payer`,
> `application_context`, `shipping`, `items`, `soft_descriptor`, `payment_instruction`,
> `experience_context`, `stored_credential`, `network_token` — none needed for a single-unit
> unbranded card auth; `experience_context`/3DS left default (no forced challenge).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| A saved card named in `pay` must be one the caller owns and that still exists | pay (`CreateOrder` card.vault_id) ← `POST /api/payment-methods` (our store) | before `CreateOrder`: look up `{paymentMethodId→payPalVaultId}` filtered by `buyerId`; 404 if not owned/absent |
| `authorizationId` passed to capture/void/reauthorize must be one produced by this order's authorize | fulfil/cancel (`CaptureAuthorizedPayment`/`VoidPayment`/`ReauthorizePayment`) ← pay (`AuthorizeOrder`) | never taken from caller — read from persisted `OrderPayment.AuthorizationId` |
| `captureId` passed to refund must be this order's capture | refund (`RefundCapturedPayment`) ← fulfil (`CaptureAuthorizedPayment`) | read from persisted `OrderPayment.CaptureId`; refund only when state=`Fulfilled`/`PartiallyRefunded` |
| Σ(refund amounts) ≤ captured amount | refund ← fulfil | computed from persisted refunds before `RefundCapturedPayment`; reject over-refund with 422 |
| `orderId`/`paymentMethodId` in any route must belong to the caller | all shopper routes | ownership check on `buyerId` before any SDK call; operator routes require admin role |

> ⚠⚠ The map states none of these — derived from the task.

---

## 3. Trap notes (hazard + skill pointer; not resolved here)

- **Client lifetime / DI**: the `HttpClient`+handler must be long-lived and reused, and the options
  object built once; getting it wrong leaks sockets or rebuilds auth per call. `MUST load dotnet-client-initialization`.
- **Credentials placement**: where credentials are set relative to client construction, and reading
  them from configuration not literals, is not what the property type shows. `MUST load dotnet-authentication`.
- **Request records & the injected `Idempotency-Key`**: which member is a *real* caller key vs the
  per-call `Guid.NewGuid()` the generator injects. `MUST load dotnet-calling-endpoints`.
- **Enums / unions / extension-data**: `OpenStringEnum` handling, `Match`, and whether unknown response
  fields are retained. `MUST load dotnet-models`.
- **Error boundary**: which exception types actually reach the catch, Case A vs B accessors, and the
  `TryGetNoContent`[500] arm on the Payments ops. `MUST load dotnet-error-handling`.
- **`ResponseDeserializationException`**: a drifted 2xx (missing `required`) or a non-2xx body that
  doesn't match `{Operation}Error` surfaces as `ResponseDeserializationException` — an `ApiException`
  that is **not** `ApiException<TError>`; a ladder catching only `ApiException<TError>` lets it escape.
  `MUST load dotnet-error-handling`.
- **Timeout is per-attempt / retry eligibility / base-URL / pagination / body logging**: the whole-call
  budget, which verbs the SDK resends, and that `LogRequestBody` prints JSON unredacted.
  `MUST load dotnet-configuration-resilience`.
- **Test seam**: the `HttpClient` ctor arg is the seam; match the project's xUnit style.
  `MUST load dotnet-testing`.

---

## 4. REQUIRED READING (load all before implementation; contents deliberately not copied here)

| Skill (this plugin: `sdk-skills-test-dev:…`) | Governs |
| --- | --- |
| `dotnet-client-initialization` | Step 1 — construct + DI-register the client |
| `dotnet-authentication` | Step 1 — set OAuth2 client-credentials from config |
| `dotnet-calling-endpoints` | Steps 2–10 — first call to each op; real idempotency key |
| `dotnet-models` | Steps 2–10 — building bodies, enums, unions, extension data |
| `dotnet-error-handling` | all — try/catch, Case A/B, `ResponseDeserializationException` |
| `dotnet-configuration-resilience` | Step 1 — retries, per-attempt timeout, base URL, pagination, body logging |
| `dotnet-testing` | tests — fake the `HttpClient` seam |

Mandatory hazard row (verbatim): *a body that does not match its declared type — a drifted/malformed
2xx (missing `required`) or a non-2xx body not matching its `{Operation}Error` — surfaces as
`ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target
type but is not `ApiException<TError>`; a catch ladder handling only `ApiException<TError>` lets it
escape, so also catch `ResponseDeserializationException` (or `ApiException`).*

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | **Credential fail-fast** | `PayPalOptions` validated at startup via `IValidateOptions`/`ValidateOnStart`: `ClientId`, `ClientSecret`, `Environment`, `Currency` must be non-blank (each part checked separately — a blank part ≠ missing); `BaseUrl` optional but if present must be an absolute URI. Host refuses to start otherwise — not discovered as a first-call 401. |
| 2 | **Secret sourcing & rotation** | Values come from env vars `PAYPAL_CLIENT_ID/SECRET/ENVIRONMENT/CURRENCY`, loaded by me into **.NET user-secrets** (`PayPal:ClientId` etc.); never written into any repo file. The DI registration builds `PayPalServerSdkClientOptions` **once** and captures it in the singleton client → a rotated secret takes effect only on process restart. Rotation-without-restart is out of scope; documented as such. |
| 3 | **Total timeout budget** | `RetryOptions.Timeout` is **per attempt**, so a hung retry costs a multiple. I bound the whole call with a `CancellationToken` (linked: request-aborted + a fixed ceiling, ~30s) passed to every SDK call. `MUST load dotnet-configuration-resilience`. |
| 4 | **Write-retry ownership** | Default `HttpMethodsToRetry` = `GET,HEAD,PUT,OPTIONS`; our writes are all `POST`/`DELETE`, so the SDK never resends them. Idempotency of writes is therefore ours to own (rows 5, 11), not the retry layer's. |
| 5 | **Idempotency & ambiguous writes** | Real caller-supplied key = the **`PayPalRequestId`** member (→ `PayPal-Request-Id` header) on `CreateOrder`, `AuthorizeOrder`, `CaptureAuthorizedPayment`, `ReauthorizePayment`, `VoidPayment`, `RefundCapturedPayment`, `CreatePaymentToken`, `CreateSetupToken`. We set it **deterministically** per (order, operation): `pay-{orderId}`, `capture-{orderId}`, `void-{orderId}`, `reauth-{orderId}`; refund uses the **caller's** idempotency key. PayPal dedups the money movement server-side under that key even under a race. The injected `Idempotency-Key: Guid.NewGuid()` header is **not** a key and is not relied on. Beside the provider key we persist an idempotency-claim row (§9). |
| 6 | **Observability** | Structured logs at Information for each operator/shopper action with the eShop order id + PayPal ids + PayPal's `debug_id`/correlation from error bodies; Warning on translated provider errors; Error on unexpected. `LogRequestBody` stays **off** (row 7). Provider `debug_id` (from `RawError.ReadAsString()`/typed `Error`) is echoed to our logs for cross-referencing. |
| 7 | **Sensitive data** | Request models `CardRequest`, `SetupTokenRequestCard`, `PaymentTokenRequestCard` carry PAN/`security_code`. Therefore: `options.Logging.LogRequestBody` is **off**, `options.Logging.LoggerFactory` is assigned **explicitly** (so `PAYPALSERVERSDKCLIENT_LOG` cannot switch body logging on from outside code), and our own handlers never echo card fields — only `last4`/`brand`/`expiry`. PAN/CVV are never persisted (only the PayPal vault id + safe descriptor). `MUST load dotnet-configuration-resilience`. |
| 8 | **Environment selection** | One server group (`Default`), one environment (`Sandbox`) — the SDK declares no live environment, so **all** traffic is sandbox and there is no live-vs-test crossover risk. `PayPal:Environment` binds to `ServerEnvironment.Sandbox`; `PayPal:BaseUrl`, when set, overrides `options.Server.Default.Sandbox.BaseUrl` verbatim and (per `AuthSchemes.cs`) also redirects the token request. |
| 9 | **Duplicate prevention under concurrency** | See DUPLICATE CLAIMS. |
| 10 | **Partial results** | See PAGED READS. |
| 11 | **Unknown outcomes** | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| pay/authorize | `IdempotencyClaim` row, PK = `pay-{orderId}` (EF store) | store PK-uniqueness on `SaveChanges` (second insert → `DbUpdateException`/`ArgumentException`); **and** a unique, order-persisted `PayPal-Request-Id` (the reference `ESHOP-{orderId}-{guid}`) dedups the hold at PayPal | `OrderPaymentService.PayAsync`: `TryClaimAsync` fails → reload, return the already-persisted authorization or 409 | `OrderPaymentService.PayAsync` (claim `pay-{orderId}`, `IdempotencyService.TryClaimAsync`); reference persisted before the call via `_orderRepository.UpdateAsync` |
| fulfil/capture | `IdempotencyClaim` row, PK = `capture-{orderId}` | store PK-uniqueness; `PayPal-Request-Id = {reference}-cap` dedups the capture at PayPal | `OrderPaymentService.FulfilAsync`: `TryClaimAsync` fails → reload, return persisted capture or 409 | `OrderPaymentService.FulfilAsync` (`CaptureAsync(..., reference + "-cap")`) |
| cancel/void | `IdempotencyClaim` row, PK = `void-{orderId}` | store PK-uniqueness; `PayPal-Request-Id = {reference}-void` | `OrderPaymentService.CancelAsync`: `TryClaimAsync` fails → reload, return Cancelled or 409 | `OrderPaymentService.CancelAsync` (`VoidAsync(..., reference + "-void")`) |
| refund | `IdempotencyClaim` row, PK = `refund-{orderId}-{callerKey}` | store PK-uniqueness; `PayPal-Request-Id = {reference}-refund-{callerKey}` dedups at PayPal; plus `OrderPayment.FindRefundByKey` short-circuits a replay | `OrderPaymentService.RefundAsync`: `FindRefundByKey`/`TryClaimAsync` → return the refund recorded under that key | `OrderPaymentService.RefundAsync` |
| save card | none (a re-save is a benign new vault token) | `PayPal-Request-Id = {guid}-setup`/`{guid}` on the vault calls, fresh per attempt | n/a — saving the same card twice yields two distinct saved cards, which is harmless | `PaymentMethodService.SaveAsync` (`VaultCardCommand.IdempotencyKey = Guid.NewGuid()`) |

> Rationale for column 3: the reject is a **persisted unique-key constraint at `SaveChanges`** (an atomic
> insert whose collision fails the second writer — not an in-process lock, not a read-before-write) plus
> a provider idempotency key beside it. On the mandated in-memory provider the store is a single shared
> root within the PublicApi process; the same shape backs onto a SQL unique index in a multi-host deploy,
> and the `PayPal-Request-Id` guarantees single money movement regardless of host. In-memory data does
> not survive restart (env constraint) — acceptable for the single-run verification.

**PAGED READS**

| Read | What caps it | How the caller learns it was cut short | Where in the code |
| --- | --- | --- | --- |
| `SearchTransactions` (reconciliation) | `page_size` (100); response `TotalPages`; a 500-page safety backstop | We loop `page=1..TotalPages` (no cap) so the report covers the whole range; `ReconciliationReport` carries `PagesScanned`, `TotalPages`, and `Complete` (`false` only if the 500-page backstop is hit, which is also logged). | `PayPalGateway.SearchTransactionsAsync` → `TransactionSearchResult` → `ReconciliationService.ReconcileAsync` → `ReconciliationReport.{PagesScanned,TotalPages,Complete}` |
| `ListCustomerPaymentTokens` | not used for the caller-facing list (served from our store, §7) | N/A for caller list | N/A |

> Empty result for a just-created range is an expected sandbox lag, not truncation — reported as
> `complete:true, matched:[], count:0`, never as a gap.

**UNKNOWN OUTCOMES**

The unknown-outcome guarantee is implemented as: (a) a per-payment **PayPal-Request-Id persisted on the
order before the call**, so a retry replays idempotently at PayPal (returns the original outcome rather
than acting twice); and (b) the **reconciliation report** as the sweep that surfaces any PayPal-side
state eShop does not reflect, keyed by the same reference. The gateway also exposes the re-read
operations below for a targeted settle.

| Write | The operation you re-read with | The reference you search by | Where in the code |
| --- | --- | --- | --- |
| pay/authorize (conn fails after PayPal may have acted) | replay `CreateOrder` under the persisted `PayPal-Request-Id` (idempotent); or `client.Orders.GetOrder` | persisted `OrderPayment.ReferenceId` / `PayPalOrderId` | `OrderPaymentService.PayAsync` persists the reference before the call; `PayPalGateway.TryGetAuthorizationForOrderAsync` is the GetOrder re-read; residual drift → reconciliation |
| fulfil/capture | replay `CaptureAuthorizedPayment` under `{reference}-cap` (idempotent); or `client.Payments.GetAuthorizedPayment` | persisted `AuthorizationId` | `OrderPaymentService.FulfilAsync`; `PayPalGateway.TryGetAuthorizationAsync` |
| cancel/void | replay `VoidPayment` under `{reference}-void` (idempotent); or `GetAuthorizedPayment` | persisted `AuthorizationId` | `OrderPaymentService.CancelAsync`; `PayPalGateway.TryGetAuthorizationAsync` |
| refund | replay `RefundCapturedPayment` under `{reference}-refund-{callerKey}` (idempotent → same `Refund`) | caller idempotency key (stored on the refund) | `OrderPaymentService.RefundAsync` (`FindRefundByKey` + provider replay) |
| save card | replay `CreatePaymentToken` / reconcile the vault | `PayPalRequestId` / customer id | `PaymentMethodService.SaveAsync` (re-save yields a new token; harmless) |

> ⚠ **SDK contract drift found and handled during verification:** `Payments.VoidPayment` is declared to
> return `PaymentAuthorization`, but PayPal answers a successful void with **204 No Content**. The empty
> body raises `ResponseDeserializationException` on what is actually a success. `PayPalGateway.VoidAsync`
> treats a `ResponseDeserializationException` whose `StatusCode` is 2xx as success (regression-tested in
> `PayPalGatewaySeamTests.VoidAsync_treats_204_no_content_as_success`).

---

## 6. Assumptions & Blockers

**Blockers:** none — the map covers every capability the task needs (order create/authorize/capture/
void/reauthorize, refund, transaction search, vault create/delete/list).

**Assumptions (minor — proceeding):**

- Caller identity = JWT username; `Order.BuyerId` = that username; scoping keyed on it. (`YOUR CALL`.)
- Reconciliation "PayPal's own record" = `SearchTransactions`; matching key = our `invoice_id`/`custom_id`
  reference echoed into `TransactionInformation.InvoiceId`/`CustomField`. (`YOUR CALL`.)
- Saved-card save uses the setup-token→payment-token vault flow (most robust for card-without-purchase
  on a vaulting-enabled sandbox business account); direct `CreatePaymentToken(card)` is the fallback if
  the two-step proves unnecessary. Chosen at implementation and confirmed by live sandbox test.
- A 3DS/`approve`-link challenge on the test Visa is not expected; if it occurs we STOP and report
  (mandate) rather than building an approval round-trip.

---

## 7. Source labels for application decisions (`YOUR CALL — not in the map`)

- Persisting payment state on the `Order` aggregate (owned `OrderPayment` + `PaymentRefund`), the
  `IdempotencyClaim` table, and the `SavedPaymentMethod` table — application design, not SDK facts.
- Serving `GET /api/payment-methods` from our own store (safe descriptors only) rather than
  `ListCustomerPaymentTokens` — avoids any cross-shopper leakage and needs no PayPal round-trip; DELETE
  still calls `DeletePaymentToken` so the card is unusable both in our store and in the PayPal vault.
- Deterministic `PayPal-Request-Id` scheme per (order, operation); refund uses the caller's key.
- Reconciliation response shape (matched / paypal-only / eshop-only + coverage fields).
- Role gating: fulfil, cancel, reconciliation = `ADMINISTRATORS` (existing privileged role); all other
  endpoints shopper-scoped to the caller's own data.
