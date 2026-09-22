# PayPal Server SDK (.NET) integration plan — eShopOnWeb PublicApi

Additive PayPal card payments + saved cards on `src/PublicApi`. Reuses the existing
`Order`/`OrderItem` model; adds an `OrderPayment` aggregate (payment/fulfilment state) and a
`SavedPaymentMethod` aggregate (vaulted cards). All SDK facts below come from the SDK map
(`sdk-map.md` + `map/operations/*`) and the declaring model files it names.

## 1. Scope & sequence

PayPal maps to the **Orders (intent=AUTHORIZE)** + **Payments** + **Vault** + **TransactionSearch** controllers.

| Step | App endpoint | PayPal operations |
| --- | --- | --- |
| 1 | `POST /api/orders` | *(none — local Order + OrderPayment(AwaitingPayment) only)* |
| 2 | `POST /api/orders/{id}/pay` (authorize hold) | `Orders.CreateOrder` (intent AUTHORIZE, payment_source.card) → `Orders.AuthorizeOrder` |
| 3 | `POST /api/orders/{id}/fulfil` (capture) | `Payments.CaptureAuthorizedPayment` (+ `GetAuthorizedPayment`/`ReauthorizePayment` on stale auth) |
| 4 | `POST /api/orders/{id}/cancel` (release hold) | `Payments.VoidPayment` |
| 5 | `POST /api/orders/{id}/refunds` | `Payments.RefundCapturedPayment` |
| 6 | `GET /api/my-orders` | *(none — local read)* |
| 7 | `GET /api/reconciliation` | `TransactionSearch.SearchTransactions` (all pages) |
| 8 | `POST /api/payment-methods` (save card) | `Vault.CreateSetupToken` → `Vault.CreatePaymentToken` |
| 9 | `GET /api/payment-methods` | *(local read; PayPal is `Vault.ListCustomerPaymentTokens`)* |
| 10 | `DELETE /api/payment-methods/{id}` | `Vault.DeletePaymentToken` |

Pay-with-saved-card (step 2 variant): `CreateOrder` with `payment_source.card.vault_id = <stored PayPal payment-token id>`.

## 2. CONTRACT SHEET

⚠ **Signatures are generated code, verbatim.** Every parameter name is the literal C# identifier; the
cancellation-token parameter is named `ct` (named args write `ct:`). Optional-but-no-default params
(`string? payPalMockResponse, …`) have **no** C# default → must be passed explicitly (pass `null` to skip).

⚠ **Every SDK type is written fully-qualified with the namespace its source path implies.** Records/unions →
`PayPalServerSdk.Models` (`Models/`); enums → `PayPalServerSdk.Models.Enums` (`Models/Enums/`); typed errors →
`PayPalServerSdk.Errors` (`Errors/`); client/options → `PayPalServerSdk`; `ServerEnvironment` → `PayPalServerSdk.Servers`;
`SdkException<T>` → `PayPalServerSdk.Core.Exceptions`; `RawError` → `PayPalServerSdk.Core.ErrorResponse`;
`OAuth2ClientCredentials` → `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`.

| Operation | Signature (params in order) · request fields used · response fields read · error case | source |
| --- | --- | --- |
| `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions?, CancellationToken ct)`. **Body `OrderRequest`**: `Intent (intent): CheckoutPaymentIntent, required` = `Authorize`; `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest>, required` (1 unit); `PaymentSource (payment_source): PaymentSource?` (purpose: attach card — omit → PayPal expects approval, not allowed here). **Returns `Order`**: read `Id`, `Status (OrderStatus)`, `PurchaseUnits[].Payments`. Error **Case A** `SdkException<CreateOrderError>` · `TryGetError(out Error)` [400,401,422] · `TryGetRawError`. Pass `prefer:"return=representation"` to get full payments back. | map/operations/Orders.md; Models/OrderRequest.cs; Models/Order.cs |
| `client.Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", RequestOptions?, CancellationToken ct)`. `body`=null. **Returns `OrderAuthorizeResponse`**: read `Status`, `PurchaseUnits[].Payments.Authorizations[].Id / .Status (AuthorizationStatus) / .Amount / .ExpirationTime`. Error **Case A** `AuthorizeOrderError` · `TryGetError(out Error)` [400,401,403,404,422,500]. | map/operations/Orders.md; Models/OrderAuthorizeResponse.cs |
| `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", RequestOptions?, CancellationToken ct)`. **Body `CaptureRequest`**: `Amount (amount): Money?` (purpose: capture full auth amount to the cent — set explicitly), `FinalCapture (final_capture): bool?`=true (purpose: no further captures). **Returns `CapturedPayment`**: read `Id`, `Status (CaptureStatus)`, `Amount`, `SellerReceivableBreakdown.GrossAmount / .PaypalFee / .NetAmount`. Error **Case A** `CaptureAuthorizedPaymentError` · `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. Use `prefer:"return=representation"` so breakdown is returned. | map/operations/Payments.md; Models/CaptureRequest.cs; Models/CapturedPayment.cs; Models/SellerReceivableBreakdown.cs |
| `client.Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions?, CancellationToken ct)`. **Returns `PaymentAuthorization`**: read `Status (AuthorizationStatus)`, `ExpirationTime`, `Id`, `Amount`. Error **Case A** `GetAuthorizedPaymentError` · `TryGetError` [401,403,404] · `TryGetNoContent` [500]. | map/operations/Payments.md; Models/PaymentAuthorization.cs |
| `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", RequestOptions?, CancellationToken ct)`. **Body `ReauthorizeRequest`**: `Amount (amount): Money?` (purpose: renew hold for full order total). **Returns `PaymentAuthorization`** (read new `Id`, `Status`, `ExpirationTime`). Error **Case A** `ReauthorizePaymentError` · `TryGetError` [400,401,403,404,422] · `TryGetNoContent` [500]. | map/operations/Payments.md; Models/ReauthorizeRequest.cs |
| `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", RequestOptions?, CancellationToken ct)`. **Returns `PaymentAuthorization`** (read `Status`=`Voided`). Error **Case A** `VoidPaymentError` · `TryGetError` [401,403,404,409,422] · `TryGetNoContent` [500]. | map/operations/Payments.md; Models/PaymentAuthorization.cs |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", RequestOptions?, CancellationToken ct)`. **Body `RefundRequest`**: `Amount (amount): Money?` (purpose: **omit → full refund of remaining**; set for partial). **Returns `Refund`**: read `Id`, `Status (RefundStatus)`, `Amount`. `payPalRequestId` = **caller-supplied idempotency key**. Error **Case A** `RefundCapturedPaymentError` · `TryGetError` [400,401,403,404,409,422] · `TryGetNoContent` [500]. | map/operations/Payments.md; Models/RefundRequest.cs; Models/Refund.cs |
| `client.Payments.GetCapturedPayment` | `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions?, CancellationToken ct)`. **Returns `CapturedPayment`**: read `Status`, `Amount`, `SellerReceivableBreakdown`. Error **Case A** `GetCapturedPaymentError` · `TryGetError` [401,403,404]. | map/operations/Payments.md; Models/CapturedPayment.cs |
| `client.Payments.GetRefund` | `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions?, CancellationToken ct)`. **Returns `Refund`** (read `Status`,`Amount`). Error **Case A** `GetRefundError`. | map/operations/Payments.md; Models/Refund.cs |
| `client.Vault.CreateSetupToken` | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions?, CancellationToken ct)`. **Body `SetupTokenRequest`**: `PaymentSource (payment_source): SetupTokenRequestPaymentSource, required` → `.Card = SetupTokenRequestCard {Number,Expiry,SecurityCode,Name,BillingAddress}`; `Customer (customer): Customer?` (purpose: tie vault to a per-shopper customer id — set `MerchantCustomerId` so tokens group per buyer). **Returns `SetupTokenResponse`**: read `Id` (setup-token id), `Customer.Id`. Error **Case A** `CreateSetupTokenError` · `TryGetError` [400,403,422,500]. | map/operations/Vault.md; Models/SetupTokenRequest.cs; Models/SetupTokenRequestCard.cs |
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions?, CancellationToken ct)`. **Body `PaymentTokenRequest`**: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource, required` → `.Token = VaultTokenRequest {Id=<setupTokenId>, Type=VaultTokenRequestType.SetupToken}`; `Customer?`. **Returns `PaymentTokenResponse`**: read `Id` (vault payment-token id — used as `card.vault_id`), `Customer.Id`, `PaymentSource.Card (CardPaymentTokenEntity).LastDigits / .Brand (CardBrand) / .Expiry`. Error **Case A** `CreatePaymentTokenError` · `TryGetError` [400,403,404,422,500]. | map/operations/Vault.md; Models/PaymentTokenRequest.cs; Models/PaymentTokenRequestPaymentSource.cs; Models/CardPaymentTokenEntity.cs |
| `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions?, CancellationToken ct)`. **Returns `void`**. Error **Case A** `DeletePaymentTokenError` · `TryGetError` [400,403,500]. | map/operations/Vault.md |
| `client.Vault.ListCustomerPaymentTokens` | `ListCustomerPaymentTokens(string customerId, int? pageSize=5, int? page=1, bool? totalRequired=false, RequestOptions?, CancellationToken ct)`. **Returns `CustomerVaultPaymentTokensResponse`**: `PaymentTokens[]`, `TotalPages`, `TotalItems`. Error **Case A** `ListCustomerPaymentTokensError`. *(Local store is authoritative + owner-scoped; used only as cross-check.)* | map/operations/Vault.md; Models/CustomerVaultPaymentTokensResponse.cs |
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, RequestOptions?, CancellationToken ct)`. `startDate`/`endDate` are ISO-8601. **Returns `SearchResponse`**: `TransactionDetails[].TransactionInfo (TransactionInformation)` → `.TransactionId / .InvoiceId / .CustomField / .TransactionAmount / .FeeAmount / .TransactionStatus / .TransactionInitiationDate`; plus `Page`, `TotalPages`, `TotalItems`. Error **Case B** `SdkException<RawError>`. **Paginate: loop page=1..TotalPages** to cover the whole range. | map/operations/TransactionSearch.md; Models/SearchResponse.cs; Models/TransactionInformation.cs |

**Money** (`Models/Money.cs`) and **AmountWithBreakdown** (`Models/AmountWithBreakdown.cs`): both `CurrencyCode (currency_code): string, required` + `Value (value): string, required`. `value` is a **decimal string** ("12.34") — format order total to 2 dp invariant culture, currency from `PayPal:Currency`.

**Address** (`Models/Address.cs`, used for card `billing_address`): `CountryCode (country_code): string, required`; optional `AddressLine1/2`, `AdminArea1/2`, `PostalCode`. Test card accepts any billing address.

Enums (source `Models/Enums/`): `CheckoutPaymentIntent.Authorize`("AUTHORIZE"); `AuthorizationStatus` {Created,Captured,Denied,PartiallyCaptured,Voided,Pending}; `CaptureStatus` {Completed,Declined,PartiallyRefunded,Pending,Refunded,Failed}; `RefundStatus` {Cancelled,Failed,Pending,Completed}; `OrderStatus` {Created,Saved,Approved,Voided,Completed,PayerActionRequired}; `VaultTokenRequestType.SetupToken`("SETUP_TOKEN"); `CardBrand` (read-only, display only). Enums are `StringEnum<T>` — compare with `==` to static members, build with `.FromValue("WIRE")`; never a C# `switch` on a raw string.

**Client/auth/server** (source `sdk-map.md` §Getting a client, §Servers & auth, `ServiceCollectionExtensions.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs`):
`services.AddPayPalServerSdkClient(o => { o.Oauth2 = new OAuth2ClientCredentials{ClientId=…,ClientSecret=…}; o.Environment = ServerEnvironment.Sandbox; })`. Only `ServerEnvironment.Sandbox` exists. **BaseUrl override**: `o.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>` — the OAuth token endpoint is `server.Default("/v1/oauth2/token")` (AuthSchemes.cs) which resolves through `DefaultOptions.Sandbox.BaseUrl`, so this override reaches **every** call including the token request, exactly as the task requires. The DI extension builds the options **once at registration** and captures them in the singleton client.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| a saved card named in pay must be one the caller owns/returned by list | `PayOrder` ← `ListPaymentMethods`/`CreatePaymentToken` | application (query `SavedPaymentMethods` by `BuyerId`+`Id`; use its `PayPalVaultId`) |
| `authorizationId` captured/voided/reauthorized must be the one `AuthorizeOrder` returned for that order | `CaptureAuthorizedPayment`/`VoidPayment`/`ReauthorizePayment` ← `AuthorizeOrder` | application (stored `OrderPayment.AuthorizationId`) |
| `captureId` refunded must be the one `CaptureAuthorizedPayment` returned for that order | `RefundCapturedPayment` ← `CaptureAuthorizedPayment` | application (stored `OrderPayment.CaptureId`) |
| reconciliation lines PayPal txns to eShop orders by a shared reference | `SearchTransactions` ↔ `CreateOrder` | application (order id written to purchase-unit `custom_id` + `invoice_id`; matched on `TransactionInformation.InvoiceId`/`CustomField`) |

## 3. Trap notes (name the hazard; load the skill)

- **DI + HttpClient lifetime**: the SDK client is a captured singleton over an `IHttpClientFactory` client; getting ownership/lifetime wrong leaks sockets or captures a disposed handler. **MUST load dotnet-client-initialization** (step 4 — integration layer).
- **Credentials application timing / multi-part credential**: where `Oauth2` is set and how a blank part fails. **MUST load dotnet-authentication** (step 4).
- **Named-argument binding + real idempotency key vs injected header**: optional-no-default params mis-bind positionally; the injected `Idempotency-Key: Guid.NewGuid()` header is **not** a key — the real key is `payPalRequestId`. **MUST load dotnet-calling-endpoints** (steps 2–10).
- **Model building**: `StringEnum<T>` are not C# enums; unions read via `TryGet…`; `required` init members; `Money.Value` is a string. **MUST load dotnet-models** (all steps building bodies).
- **Error boundary — two JsonException directions**: a drifted 2xx body throws `JsonException` from deserialization (not an `SdkException`); a non-2xx body that doesn't match its `{Operation}Error` throws `JsonException` while building the error, destroying the HTTP status. Case A `TryGet…` vs Case B `RawError` (SearchTransactions). **MUST load dotnet-error-handling** (error-translation layer).
- **Timeout is per-attempt, retry eligibility by HTTP method, unredacted request-body logging**: `Timeout` bounds one attempt not the whole call; `HttpMethodsToRetry` default never resends `POST`; `LogRequestBody` prints card PANs in clear. **MUST load dotnet-configuration-resilience** (client tuning + logging posture).
- **Test seam**: the `HttpClient` ctor arg is the fake seam; match the project's MSTest/xUnit style. **MUST load dotnet-testing** (tests).

## 4. REQUIRED READING (load all before implementation)

| skill | governs |
| --- | --- |
| `paypal-platforms-team:dotnet-client-initialization` | client construction + DI registration |
| `paypal-platforms-team:dotnet-authentication` | OAuth2 client-credentials wiring + fail-fast |
| `paypal-platforms-team:dotnet-calling-endpoints` | every `client.X.Operation(...)` call, named args, idempotency key |
| `paypal-platforms-team:dotnet-models` | building request bodies / reading responses (enums, unions, Money) |
| `paypal-platforms-team:dotnet-error-handling` | Case A/B, exception-translation layer, JsonException traps |
| `paypal-platforms-team:dotnet-configuration-resilience` | retries/timeouts/base-URL/pagination/logging |
| `paypal-platforms-team:dotnet-testing` | integration-layer tests |

These carry the how-to; the sheet deliberately does not. `dotnet-error-handling` always applies (error boundary).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` bound from `PayPal:` section; a startup validator (`IValidateOptions`/explicit check in `Program.cs` before `app.Run()`) throws if `ClientId`, `ClientSecret`, or `Currency` is null/blank — **each** checked separately (blank ≠ missing). Non-`sandbox` `Environment` with no `BaseUrl` also fails (SDK ships only Sandbox; we never invent a live URL). |
| 2 | Secret sourcing & rotation | Secrets from **.NET user-secrets** (loaded from env vars `PAYPAL_CLIENT_ID`/`_SECRET`), bound to `PayPal:ClientId`/`ClientSecret`. DI builds options **once at registration** → rotation needs a process restart; documented, acceptable for this reference app. |
| 3 | Total timeout budget | SDK `Timeout` is per-attempt. Each PayPal call is bounded by a `CancellationToken` deadline (linked token, ~30 s) created in the gateway per operation, giving the caller a real whole-call ceiling. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS → our `POST`/`DELETE` writes (create/authorize/capture/void/refund/vault) are **never auto-resent** by the SDK. Reads (GET auth/capture/refund/search) may retry. Safe: every write carries a `payPalRequestId` idempotency key. |
| 5 | Idempotency & ambiguous writes | CreateOrder/AuthorizeOrder → `payPalRequestId = "eshop-ord-{orderId}-auth"`; Capture → `"eshop-ord-{orderId}-cap"`; Void → `"eshop-ord-{orderId}-void"`; Vault setup/payment tokens → deterministic per (buyer, request). **Refund** → caller-supplied `Idempotency-Key` header/body field passed verbatim as `payPalRequestId`; repeat under same key = same refund, distinct keys = distinct partial refunds. The generator's `Idempotency-Key: Guid.NewGuid()` is **not** relied on. |
| 6 | Observability | Info: state transitions with orderId + PayPal ids (order/auth/capture/refund). Warning: stale-auth reauth, non-success statuses. Error: SdkException with status + PayPal `debug_id`/`Error.DebugId` when present. `LogRequestBody` stays **off**. |
| 7 | Sensitive data | Card PAN/CVC flow through `CardRequest`/`SetupTokenRequestCard` only. `options.Logging.LogRequestBody` = **off** and `options.Logging.LoggerFactory` set explicitly (so `PAYPALSERVERSDKCLIENT_LOG` can't arm body logging). App never logs request bodies or card fields; only last-4 + brand persisted. PAN never written to our DB or logs. |
| 8 | Environment selection | One server group `Default`, one env `Sandbox` (`https://api-m.sandbox.paypal.com`). `PayPal:Environment`=sandbox. Test traffic stays in sandbox; a non-sandbox value requires an explicit `PayPal:BaseUrl` (fail-fast otherwise) so we never accidentally hit live. |
| 9 | Duplicate prevention under concurrency | `OrderPayments` table, **unique index on `OrderId`** — the row is the single per-order payment claim, created at `POST /api/orders`; a duplicate insert throws `DbUpdateException` (unique violation), caught in the create handler. `SavedPaymentMethods` table, **unique index on (`BuyerId`,`PayPalVaultId`)**, same catch. Transitions (authorize/capture/void) additionally use an EF **concurrency token** (`RowVersion`) so a double-click loses the race with `DbUpdateConcurrencyException`; the money-movement itself is guaranteed single by the `payPalRequestId` idempotency key server-side. (Under the in-memory provider used this run, unique-index/concurrency enforcement is provider-limited; the SQL-Server production target enforces them, and the PayPal idempotency key guarantees no double charge regardless — see §6 note.) |
| 10 | Partial results | `GET /api/reconciliation` loops `page=1..SearchResponse.TotalPages`; the response DTO carries `pagesFetched`/`totalItems` and a `truncated` bool set true only if a hard page cap is ever hit (default: no cap — fetch all pages). Caller learns completeness from those fields, not logs. |
| 11 | Startup validation vs test host | `tests/PublicApiIntegrationTests` boots the real host via `WebApplicationFactory<Program>` with `appsettings.test.json` (`UseOnlyInMemoryDatabase:true`). It has **no** PayPal config → the startup validator must not hard-crash the test host: PayPal validation runs but the client is only exercised on payment endpoints; tests that don't need PayPal pass. I will supply placeholder `PayPal:*` values to the test host (non-secret dummy) so the host boots green, and run that project. |
| 12 | Ordering & no-op side effects | Local `OrderPayment` row is written **before** the PayPal call (status AwaitingPayment/Authorizing) and completed after with returned ids/status. Each idempotent transition gates its outbound PayPal effect on a state check: if already Authorized/Captured/Voided/Refunded-to-target, it returns the existing result **without** re-calling PayPal. No unconditional side effect after a transition. |
| 13 | Unknown outcomes | On transport failure after send: **authorize** → re-read `Orders.GetOrder(payPalOrderId)` / `Payments.GetAuthorizedPayment(authorizationId)`; **capture** → `Payments.GetAuthorizedPayment(authorizationId)` (status CAPTURED / has captures); **void** → `Payments.GetAuthorizedPayment(authorizationId)`; **refund** → `Payments.GetCapturedPayment(captureId)` (status PARTIALLY_REFUNDED/REFUNDED) and same `payPalRequestId` re-drives idempotently. The search reference is the stored PayPal id (order/auth/capture) or the deterministic `payPalRequestId`. Never report a definite failure without a re-read. |
| 14 | Provider status & reconciliation clock | Branch on: `OrderStatus`, `AuthorizationStatus` (Created→ok, Denied/Pending→surface), `CaptureStatus` (Completed→ok, Declined/Pending/Failed→surface), `RefundStatus` (Completed→ok, Pending/Failed→surface). No `status ?? "COMPLETED"`. Reconciliation filters both sides on **`TransactionInformation.TransactionInitiationDate`** vs the `from`/`to` window — PayPal's transaction clock, not a local row-creation column. |

### DUPLICATE CLAIMS

| write | where the claim is stored | what rejects the second one | where that rejection is caught |
| --- | --- | --- | --- |
| `POST /api/orders` (one payment per order) | `OrderPayments.OrderId` | unique index on `OrderId` → `DbUpdateException` | create-order handler / order-payment service |
| `POST /api/payment-methods` (save card) | `SavedPaymentMethods.(BuyerId,PayPalVaultId)` | unique index → `DbUpdateException` | save-payment-method handler |
| `pay`/`fulfil`/`cancel` transition | `OrderPayments.RowVersion` (concurrency token) + PayPal `payPalRequestId` | `DbUpdateConcurrencyException` on SaveChanges; PayPal idempotency key server-side | pay/fulfil/cancel handler |

### PAGED READS

| read | what caps it | how the caller learns the answer was cut short |
| --- | --- | --- |
| `GET /api/reconciliation` (SearchTransactions) | loop over `TotalPages`, `pageSize=100`; no hard cap by default | response fields `pagesFetched`, `totalItems`, `truncated` (bool) |
| `GET /api/payment-methods` | local DB list (no external paging) | n/a — full list returned |

### REPEATED OPERATIONS

| operation | what tells you the state actually changed | the effects gated on that |
| --- | --- | --- |
| `pay` | `OrderPayment.Status` moved AwaitingPayment→Authorized | PayPal CreateOrder/AuthorizeOrder call; if already Authorized, skip and return stored auth |
| `fulfil` | Status Authorized→Captured | PayPal CaptureAuthorizedPayment; if already Captured, skip and return stored capture |
| `cancel` | Status Authorized→Voided | PayPal VoidPayment; if already Voided/Captured, skip/reject |
| `refunds` | a new `OrderPaymentRefund` row for that idempotency key | PayPal RefundCapturedPayment; same key → return existing refund |
| `DELETE /api/payment-methods/{id}` | row removed / already absent | PayPal DeletePaymentToken; if already gone, treat as success (idempotent) |

### UNKNOWN OUTCOMES

| write | the operation you re-read with | the reference you search by |
| --- | --- | --- |
| authorize (CreateOrder/AuthorizeOrder) | `Orders.GetOrder` / `Payments.GetAuthorizedPayment` | stored `PayPalOrderId` / `AuthorizationId` (or `payPalRequestId`) |
| capture | `Payments.GetAuthorizedPayment` | stored `AuthorizationId` |
| void | `Payments.GetAuthorizedPayment` | stored `AuthorizationId` |
| refund | `Payments.GetCapturedPayment` | stored `CaptureId` (+ same `payPalRequestId`) |
| save card | `Vault.ListCustomerPaymentTokens` | stored PayPal `Customer.Id` |

## 6. Assumptions & Blockers

- **No Blockers.** Every capability the task needs maps to a documented operation.
- Assumption: direct-card authorize (intent AUTHORIZE + `payment_source.card`) does not return a browser challenge for the sandbox test card, per task. If a real challenge (`PAYER_ACTION_REQUIRED` / 3DS approval link) comes back, **STOP and report** — do not build an approval round-trip.
- Assumption: `POST /api/orders` accepts an optional ship-to address; when omitted a default placeholder address is used (existing `Order` requires a non-null `ShipToAddress`). Order amount is computed from catalog prices, not client input.
- Environment note (this run): in-memory EF provider does not enforce unique indexes/concurrency tokens and loses data on restart; declared constraints target the SQL-Server production config, and PayPal's `payPalRequestId` idempotency guarantees single money movement regardless of provider. Pay/fulfil/refund within one process run.
