# PayPal Server SDK integration plan — eShopOnWeb PublicApi

PayPal payments (authorize → capture at fulfilment → cancel/refund) and vaulted (saved) cards,
exposed as JWT endpoints under `/api/` on `src/PublicApi`. Additive to the existing catalog /
basket / order flow. SDK: PayPal Server SDK .NET (`PayPalServerSdk`, root namespace), OAuth2
client-credentials, sandbox.

---

## 1. Scope & sequence

1. **Vendor + reference the SDK** — copy the SDK source into `src/PayPalServerSdk/`, disable CPM on
   it (`ManagePackageVersionsCentrally=false`), add a `ProjectReference` from `PublicApi`.
2. **Config + client** — bind `PayPal:` section, fail-fast, register `PayPalServerSdkClient`
   singleton (`AddPayPalServerSdkClient`), set `Oauth2` + `Environment` + optional `BaseUrl` override.
3. **Domain (ApplicationCore/Entities/OrderAggregate)** — reuse existing `Order`; add related
   aggregates `OrderPayment` (+ child `PaymentRefund`) and `ShopperPaymentProfile`; add
   `OrderPaymentStatus` enum. EF config + DbSets in `CatalogContext`.
4. **PayPal gateway service** (`Infrastructure`) — thin wrapper translating SDK calls/models ↔ domain,
   owning the error boundary. Ops used:
   - Authorize (hold): `Orders.CreateOrder` (intent=AUTHORIZE, no payment_source) → `Orders.AuthorizeOrder` (payment_source.card = raw card **or** vault_id).
   - Capture (fulfil): `Payments.CaptureAuthorizedPayment`; on stale auth → `Payments.ReauthorizePayment` then re-capture.
   - Cancel (pre-fulfil): `Payments.VoidPayment`.
   - Refund (post-fulfil): `Payments.RefundCapturedPayment`.
   - Save card: `Vault.CreatePaymentToken`; list: `Vault.ListCustomerPaymentTokens`; delete: `Vault.DeletePaymentToken`.
   - Reconciliation: `TransactionSearch.SearchTransactions` (loop **all** pages).
5. **Application service** — order/payment orchestration + idempotency + refund cap + ownership.
6. **PublicApi endpoints** — the routes below, following `IEndpoint<...>` (MinimalApi.Endpoint) convention.
7. **Tests** (`tests/UnitTests` or a new project) — gateway mapping + idempotency/refund-cap logic via the `HttpClient` seam.
8. **Self-verify** live on sandbox; write verification guide.

Endpoints (all `/api/`, JWT). Operator (Roles=ADMINISTRATORS): fulfil, cancel, reconciliation.
Shopper-scoped (own data only): create order, pay, refund, my-orders, all payment-methods.

| Route | Method | PayPal ops |
| --- | --- | --- |
| `/api/orders` | POST | none (local order, AwaitingPayment) — returns `orderId` |
| `/api/orders/{orderId}/pay` | POST | CreateOrder + AuthorizeOrder |
| `/api/orders/{orderId}/fulfil` | POST | CaptureAuthorizedPayment (+ Reauthorize on stale) |
| `/api/orders/{orderId}/cancel` | POST | VoidPayment |
| `/api/orders/{orderId}/refunds` | POST | RefundCapturedPayment — returns `refundId` |
| `/api/my-orders` | GET | none (local read) |
| `/api/reconciliation?from&to` | GET | SearchTransactions (all pages) |
| `/api/payment-methods` | POST | CreatePaymentToken — returns `paymentMethodId` |
| `/api/payment-methods` | GET | ListCustomerPaymentTokens |
| `/api/payment-methods/{paymentMethodId}` | DELETE | DeletePaymentToken |

---

## 2. CONTRACT SHEET

⚠ **Signatures are generated code, verbatim.** Every parameter name is the literal C# identifier;
named arguments must use them exactly (the cancellation-token parameter is `ct`, so write `ct:`).
⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**
(`Models/*` → `PayPalServerSdk.Models`; `Models/Enums/*` → `PayPalServerSdk.Models.Enums`;
`Errors/*` → `PayPalServerSdk.Errors`; client/options → `PayPalServerSdk`;
`ServerEnvironment` → `PayPalServerSdk.Servers`), taken from THAT type's own path.

### Operations

| op | controller · signature | request model (fields used) | response (fields read) | error case | source |
| --- | --- | --- | --- | --- | --- |
| CreateOrder | `client.Orders.CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions?, CancellationToken ct)` | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent` **req** = Authorize; `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest>` **req** (1..10). PurchaseUnitRequest: `Amount (amount): AmountWithBreakdown` **req**; `InvoiceId (invoice_id): string?` (purpose: our reconcile key = order ref); `CustomId (custom_id): string?` (purpose: our reconcile key = buyer/order); `Description (description): string?` (purpose: human label). AmountWithBreakdown: `CurrencyCode (currency_code): string` **req**; `Value (value): string` **req**. Omit payment_source here (provided at Authorize). | `Order`: `Id (id): string?` (PayPal order id); `Status (status): OrderStatus?` | A: `SdkException<CreateOrderError>` `.TryGetError(out Error)` [400,401,422] · `.TryGetRawError(out RawError)` | Orders.md; Models/OrderRequest.cs, PurchaseUnitRequest.cs, AmountWithBreakdown.cs, Order.cs |
| AuthorizeOrder | `client.Orders.AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", RequestOptions?, CancellationToken ct)` — **remarks: a valid payment_source in the request authorizes without buyer approval** | `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` (purpose: supply funding). OrderAuthorizeRequestPaymentSource.`Card (card): CardRequest?`. CardRequest (one-off): `Number (number): string?`, `Expiry (expiry): string? YYYY-MM`, `SecurityCode (security_code): string?`, `Name (name): string?`, `BillingAddress (billing_address): Address?`. CardRequest (saved): `VaultId (vault_id): string?` (purpose: pay with saved token — set instead of number/expiry). | `OrderAuthorizeResponse`: `Id (id): string?`; `Status (status): OrderStatus?`; `PurchaseUnits[].Payments.Authorizations[]` → `AuthorizationWithAdditionalData`: `Id (id): string?` (authorization id), `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?` | A: `SdkException<AuthorizeOrderError>` `.TryGetError(out Error)` [400,401,403,404,422,500] · `.TryGetRawError` | Orders.md; Models/OrderAuthorizeRequest.cs, OrderAuthorizeRequestPaymentSource.cs, CardRequest.cs, OrderAuthorizeResponse.cs, PurchaseUnit.cs, PaymentCollection.cs, AuthorizationWithAdditionalData.cs |
| CaptureAuthorizedPayment | `client.Payments.CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", RequestOptions?, CancellationToken ct)` | `CaptureRequest?` = `null` (full capture; omit → capture full authorized amount). | `CapturedPayment`: `Id (id): string?` (capture id); `Status (status): CaptureStatus?`; `Amount (amount): Money?`; `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?` → `GrossAmount` **req**, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?` | A: `SdkException<CaptureAuthorizedPaymentError>` `.TryGetError(out Error)` [400,401,403,404,409,422] · `.TryGetNoContent(out RawError)` [500] · `.TryGetRawError` | Payments.md; Models/CapturedPayment.cs, SellerReceivableBreakdown.cs, Money.cs |
| ReauthorizePayment | `client.Payments.ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", RequestOptions?, CancellationToken ct)` | `ReauthorizeRequest?`: `Amount (amount): Money?` (purpose: renew hold amount = order total). | `PaymentAuthorization`: `Id (id): string?` (**new** authorization id); `Status`; `ExpirationTime` | A: `SdkException<ReauthorizePaymentError>` `.TryGetError(out Error)` [400,401,403,404,422] · `.TryGetNoContent` [500] · `.TryGetRawError` | Payments.md; Models/ReauthorizeRequest.cs, PaymentAuthorization.cs |
| VoidPayment | `client.Payments.VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", RequestOptions?, CancellationToken ct)` | (no body) | `PaymentAuthorization`: `Status` (Voided) | A: `SdkException<VoidPaymentError>` `.TryGetError` [401,403,404,409,422] · `.TryGetNoContent` [500] · `.TryGetRawError` | Payments.md; Models/PaymentAuthorization.cs |
| RefundCapturedPayment | `client.Payments.RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", RequestOptions?, CancellationToken ct)` | `RefundRequest?`: `Amount (amount): Money?` (purpose: partial refund; omit → full refund); `CustomId (custom_id): string?` (purpose: reconcile). | `Refund`: `Id (id): string?` (refund id); `Status (status): RefundStatus?`; `Amount (amount): Money?` | A: `SdkException<RefundCapturedPaymentError>` `.TryGetError` [400,401,403,404,409,422] · `.TryGetNoContent` [500] · `.TryGetRawError` | Payments.md; Models/RefundRequest.cs, Refund.cs |
| CreatePaymentToken | `client.Vault.CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions?, CancellationToken ct)` | `PaymentTokenRequest`: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource` **req** → `Card (card): PaymentTokenRequestCard?` → `Number (number): string?`, `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `Name (name): string?`, `BillingAddress (billing_address): Address?`; `Customer (customer): Customer?` (purpose: group tokens per shopper — `Id (id): string?` reused after first save; omit on first → PayPal generates). | `PaymentTokenResponse`: `Id (id): string?` (vault token id = paymentMethodId); `Customer (customer): CustomerResponse?` → `Id`; `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `Card (card): CardPaymentTokenEntity?` → `LastDigits (last_digits): string?`, `Brand (brand): CardBrand?`, `Expiry (expiry): string?`, `Name` | A: `SdkException<CreatePaymentTokenError>` `.TryGetError` [400,403,404,422,500] · `.TryGetRawError` | Vault.md; Models/PaymentTokenRequest.cs, PaymentTokenRequestPaymentSource.cs, PaymentTokenRequestCard.cs, Customer.cs, PaymentTokenResponse.cs, PaymentTokenResponsePaymentSource.cs, CardPaymentTokenEntity.cs |
| ListCustomerPaymentTokens | `client.Vault.ListCustomerPaymentTokens(string customerId, int? pageSize=5, int? page=1, bool? totalRequired=false, RequestOptions?, CancellationToken ct)` — call with **named args** | (query) customerId = shopper's PayPal customer id | `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `TotalPages` | A: `SdkException<ListCustomerPaymentTokensError>` `.TryGetError` [400,403,500] · `.TryGetRawError` | Vault.md; Models/CustomerVaultPaymentTokensResponse.cs |
| DeletePaymentToken | `client.Vault.DeletePaymentToken(string id, RequestOptions?, CancellationToken ct)` | id = vault token id | `void` | A: `SdkException<DeletePaymentTokenError>` `.TryGetError` [400,403,500] · `.TryGetRawError` | Vault.md |
| SearchTransactions | `client.TransactionSearch.SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, RequestOptions?, CancellationToken ct)` — **named args**; pass `null` for the 8 unused filters | startDate/endDate ISO-8601; loop `page` 1..`total_pages` | `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` → `TransactionInfo` → `TransactionInformation`: `TransactionId`, `InvoiceId (invoice_id)`, `CustomField (custom_field)`, `TransactionAmount (transaction_amount): Money?`, `TransactionStatus`, `TransactionInitiationDate`; `TotalPages (total_pages): int?`, `Page`, `TotalItems` | **B: `SdkException<RawError>`** — `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()` | TransactionSearch.md; Models/SearchResponse.cs, TransactionDetails.cs, TransactionInformation.cs |

Enums (values used): `CheckoutPaymentIntent.Authorize`("AUTHORIZE"); `AuthorizationStatus` {Created,Captured,Denied,PartiallyCaptured,Voided,Pending}; `CaptureStatus` {Completed,Declined,PartiallyRefunded,Pending,Refunded,Failed}; `OrderStatus` {Created,Saved,Approved,Voided,Completed,PayerActionRequired}; `RefundStatus` {Cancelled,Failed,Pending,Completed}. Build with static members or `T.FromValue("WIRE")`; read via `.Equals`/`==` on the static member. Money.Value/CurrencyCode are `required string`.

Client construction (source: sdk-map.md *Getting a client* / *Servers & auth*):
`services.AddPayPalServerSdkClient(o => { o.Oauth2 = new OAuth2ClientCredentials { ClientId=…, ClientSecret=… }; o.Environment = ServerEnvironment.Sandbox; /* if BaseUrl set: o.Server.Default.Sandbox.BaseUrl = baseUrl */ });`
Namespaces: `PayPalServerSdk`, `PayPalServerSdk.Servers`, `PayPalServerSdk.Models`, `PayPalServerSdk.Models.Enums`, `PayPalServerSdk.Errors`, `PayPalServerSdk.Core.Exceptions` (`SdkException<T>`), `PayPalServerSdk.Core.ErrorResponse` (`RawError`), `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` (`OAuth2ClientCredentials`).

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A saved card named at pay time must be one the caller owns (in the caller's vault list) | `AuthorizeOrder(card.vault_id)` ← `ListCustomerPaymentTokens` | application (verify token id ∈ caller's tokens before use) |
| A deleted card must no longer be usable to pay | `AuthorizeOrder(card.vault_id)` ← `DeletePaymentToken` | application (ownership+existence check re-lists vault before authorize) |
| authorizationId captured/voided/reauthorized must be one AuthorizeOrder returned for that order | `CaptureAuthorizedPayment/VoidPayment/ReauthorizePayment` ← `AuthorizeOrder` | application (persist authorization id on `OrderPayment`) |
| captureId refunded must be the one CaptureAuthorizedPayment returned | `RefundCapturedPayment` ← `CaptureAuthorizedPayment` | application (persist capture id on `OrderPayment`) |

---

## 3. Trap notes

- **Error boundary shape.** Case-A ops throw `SdkException<{Op}Error>`; SearchTransactions is Case-B
  `SdkException<RawError>`. A drifted 2xx body and a non-2xx body that doesn't match `{Op}Error` both
  surface as `JsonException`, not `SdkException`. → **MUST load paypal-platforms-team:dotnet-error-handling**
- **Sensitive card data + logging.** Request bodies here carry PAN/CVV; `LogRequestBody` logs JSON
  unredacted and `PAYPALSERVERSDKCLIENT_LOG` can arm it from outside code unless `LoggerFactory` is set.
  → **MUST load paypal-platforms-team:dotnet-configuration-resilience**
- **Total timeout vs per-attempt; POST not retried by default.** The authorize/capture/refund writes are
  POST → SDK never resends them; but a hung call costs a multiple of `Timeout`. → **MUST load paypal-platforms-team:dotnet-configuration-resilience**
- **Client/HttpClient lifetime + DI.** `AddPayPalServerSdkClient` builds options once at registration and
  captures a singleton; HttpClient must come from `IHttpClientFactory`. → **MUST load paypal-platforms-team:dotnet-client-initialization**
- **Credential binding + failure semantics.** A never-set credential is skipped, not thrown; a bad token
  fetch is where 401 surfaces. → **MUST load paypal-platforms-team:dotnet-authentication**
- **Optional-param mis-binding on list/search.** ListCustomerPaymentTokens / SearchTransactions have
  optional params with no C# default that mis-bind positionally. → **MUST load paypal-platforms-team:dotnet-calling-endpoints**
- **Union/enum construction + response envelope depth.** authorizations live at
  `purchase_units[].payments.authorizations[]`; enums are `StringEnum<T>` not C# enums. → **MUST load paypal-platforms-team:dotnet-models**
- **Test seam.** The `HttpClient` ctor arg is the fake seam. → **MUST load paypal-platforms-team:dotnet-testing**

---

## 4. REQUIRED READING (load all before implementing; contents deliberately not copied here)

- paypal-platforms-team:dotnet-client-initialization · client + DI registration (step 2)
- paypal-platforms-team:dotnet-authentication · Oauth2 credential wiring (step 2)
- paypal-platforms-team:dotnet-calling-endpoints · every SDK op call, named args (step 4)
- paypal-platforms-team:dotnet-models · request bodies + reading response envelopes/enums (step 4)
- paypal-platforms-team:dotnet-error-handling · the error boundary (step 4) — **always required**
- paypal-platforms-team:dotnet-configuration-resilience · timeouts, retry ownership, logging/redaction (steps 2,4)
- paypal-platforms-team:dotnet-testing · gateway tests (step 7)

Mandatory hazard rows (both apply — `System.Text.Json.JsonException` reaches the boundary from two
directions needing opposite handling): (a) a drifted/malformed **2xx** body (missing `required` member)
throws `JsonException` from deserialization, **not** `SdkException` — an SDK-exception-only catch ladder
lets it escape; (b) a **non-2xx** body not matching its `{Op}Error` shape throws `JsonException` while the
error object is constructed, **replacing** the `SdkException` and destroying the HTTP status.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` bound from `PayPal:` section; a startup validator throws if `ClientId`, `ClientSecret`, `Environment` or `Currency` is missing/blank (each part checked separately). Host refuses to start. |
| 2 | Secret sourcing & rotation | Secrets from **.NET user-secrets** (loaded from env by me at setup; values never in repo). `AddPayPalServerSdkClient` captures options in the singleton at registration → rotation needs a process restart; acceptable for this reference app, documented. |
| 3 | Total timeout budget | Per-call `CancellationToken` deadline (30s) passed as `ct:` bounds the whole call; the SDK `Timeout` is per-attempt only. Enforced in the gateway wrapper. |
| 4 | Write-retry ownership | All money-moving ops are POST → SDK never auto-resends. Idempotency (row 5) makes a manual/caller retry safe. |
| 5 | Idempotency & ambiguous writes | Authorize: stable `payPalRequestId` persisted on `OrderPayment` + status guard (skip if already Authorized). Capture: stable `payPalRequestId` + status guard. Refund: **caller-supplied** idempotency key → `payPalRequestId`; key stored in `PaymentRefund` rows — repeat key returns the existing refund, distinct keys allow distinct partial refunds (capped at captured amount). Void: stable key. |
| 6 | Observability | Info: op + orderId + PayPal ids + status. Warning/Error: PayPal error `name`/`message`/**`debug_id`** (correlation id) extracted from `Error`/`RawError`. `LogRequestBody` stays **off**; no request body echoed on card paths. |
| 7 | Sensitive data | Card number/CVV present in CreatePaymentToken + AuthorizeOrder bodies. Posture: `LogRequestBody` off **and** `LoggerFactory` set explicitly so `PAYPALSERVERSDKCLIENT_LOG` cannot switch body logging on; our own code never logs card fields; only `last_digits`/brand from responses are stored/returned. Full PAN never persisted to the app DB. |
| 8 | Environment selection | One server group `Default`; only environment is `ServerEnvironment.Sandbox`. All traffic → sandbox. `PayPal:Environment` bound (must be `sandbox`); optional `PayPal:BaseUrl` overrides `options.Server.Default.Sandbox.BaseUrl` (verify at impl that the OAuth token fetch also honours it). No production host configured — test traffic cannot reach live. |

---

## 6. Assumptions & Blockers

- **No 3DS/browser challenge expected** with sandbox Visa `4111…` because we send no
  `verification`/`experience_context`; if PayPal returns a `PAYER_ACTION_REQUIRED`/approval challenge,
  STOP and report (per task) — not build an approval round-trip.
- **Saved-card payment** uses `card.vault_id` on `AuthorizeOrder` (not a `Token`, whose only
  `TokenType` value is `BILLING_AGREEMENT`). Verified from CardRequest.cs / TokenType.cs.
- **Per-shopper PayPal customer id**: first `CreatePaymentToken` omits `customer.id`; PayPal returns one;
  persist it on `ShopperPaymentProfile` and reuse for later saves + listing. Lost on in-memory restart
  (documented constraint), which is acceptable.
- **Reconciliation over a just-created range may be empty** (PayPal reporting lag) — expected sandbox
  result, correct over a populated range; loop all pages via `total_pages`.
- No blockers: every required capability maps to an operation above.
