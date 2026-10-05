# PayPal Server SDK (.NET) — integration plan & contract sheet

SDK source: `sdk/dotnet/` inside the **paypal** plugin (plugin-relative). Map index: `sdk/dotnet/sdk-map.md`.
All `source` cells below are relative to that SDK root.

## 1. Scope & sequence

| # | Step | SDK operations |
| --- | --- | --- |
| 1 | `global.json` roll-forward; new project `src/Infrastructure.PayPal` referencing the plugin SDK project + ApplicationCore; PublicApi references it | — |
| 2 | Domain (ApplicationCore): `Order` gains payment/fulfilment status; new `Payment` aggregate (+ `PaymentRefund`), `PaymentMethod` (existing BuyerAggregate type, made a persisted aggregate), `PaymentClaim` (PK-refused claim/lock row); `IPaymentGateway` port; payment services | — |
| 3 | Infrastructure: EF configs + DbSets + SQL migration; claim store | — |
| 4 | Client/DI: options bound from `PayPal:` section, fail-fast validation, singleton `PayPalServerSdkClient` over a long-lived `HttpClient` | client construction, OAuth2 |
| 5 | Saved cards: create setup token from card → create payment token from setup token; delete | `Vault.CreateSetupToken`, `Vault.CreatePaymentToken`, `Vault.DeletePaymentToken` |
| 6 | Pay (authorize): create order (intent AUTHORIZE, card or vault_id); if order comes back APPROVED, authorize it | `Orders.CreateOrder`, `Orders.AuthorizeOrder`, `Orders.GetOrder` (settle) |
| 7 | Fulfil (capture): read authorization; reauthorize when honor period elapsed; capture full amount | `Payments.GetAuthorizedPayment`, `Payments.ReauthorizePayment`, `Payments.CaptureAuthorizedPayment`, `Orders.GetOrder` (settle), `Payments.GetCapturedPayment` (refresh fee) |
| 8 | Cancel (void) | `Payments.VoidPayment`, `Payments.GetAuthorizedPayment` (settle) |
| 9 | Refund (full/partial, caller idempotency key) | `Payments.RefundCapturedPayment` |
| 10 | Reconciliation: ≤31-day windows × all pages | `TransactionSearch.SearchTransactions` |
| 11 | PublicApi endpoints, exception mapping, tests (fake `HttpMessageHandler`), sandbox self-verification | — |

The `…1` duplicates (`CaptureAuthorizedPayment1`, `VoidPayment1`, …) hit identical routes (`Api/Payments.cs`, `Api/Vault.cs`); only the un-suffixed operations are used.

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter (none takes zero here), built with an object initializer using the record's own property names — never flat arguments.
> ⚠ Every SDK type is written fully-qualified-by-namespace from ITS OWN source path: `Models/` → `PayPalServerSdk.Models`; `Models/Enums/` → `PayPalServerSdk.Models.Enums`; `Errors/` → `PayPalServerSdk.Errors`; `Requests/<Controller>/` → `PayPalServerSdk.Requests.<Controller>`; `ApiException<T>` → `PayPalServerSdk.Core.Exceptions`; `RawError` → `PayPalServerSdk.Core.ErrorResponse`; `RequestOptions` → `PayPalServerSdk.Core`; `RetryOptions`/`LoggingOptions` → `PayPalServerSdk.Core.Configuration`; `ServerEnvironment` → `PayPalServerSdk.Servers`; `OAuth2ClientCredentials` → `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`.

All operations: `(XRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)`; throw-only; no `Pageable`; server group `Default`; auth `options.Oauth2`.

| Controller.Method | Request record (members used) | Body model (fields used, `wire`) | Returns (fields read) | Error | Source |
| --- | --- | --- | --- | --- | --- |
| `Orders.CreateOrder` | `CreateOrderRequest { Body: OrderRequest (required), PayPalRequestId: string? (≤108; doc: mandatory for single-step create with card/vault payment source; kept 6 h), Prefer: string = "return=minimal" }` | `OrderRequest { Intent (intent): CheckoutPaymentIntent required; PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> required 1..10; PaymentSource (payment_source): PaymentSource? }`; `PurchaseUnitRequest { Amount (amount): AmountWithBreakdown required; ReferenceId (reference_id)?; CustomId (custom_id)? ≤255; InvoiceId (invoice_id)? ≤127; Description? }`; `AmountWithBreakdown { CurrencyCode (currency_code) required len 3; Value (value) required, regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$` }`; `PaymentSource { Card (card): CardRequest? }`; `CardRequest { Name (name)?, Number (number)? `^[0-9]{13,19}$`, Expiry (expiry)? `YYYY-MM`, SecurityCode (security_code)? 3–4 digits, BillingAddress (billing_address): Address?, VaultId (vault_id)? }`; `Address { CountryCode (country_code) required 2, AddressLine1, AddressLine2, AdminArea2, AdminArea1, PostalCode }` | `Order { Id, Status: OrderStatus?, PurchaseUnits[0].Payments.Authorizations[]: AuthorizationWithAdditionalData { Id, Status: AuthorizationStatus?, Amount: Money?, ExpirationTime, CreateTime }, PaymentSource.Card: CardResponse { LastDigits, Brand: CardBrand?, Expiry } }` | A: `ApiException<CreateOrderError>` · `TryGetError(out Error)` [400,401,422] · `TryGetRawError` | `map/operations/Orders.md`; `Requests/Orders/CreateOrderRequest.cs`; `Models/OrderRequest.cs`, `PurchaseUnitRequest.cs`, `AmountWithBreakdown.cs`, `PaymentSource.cs`, `CardRequest.cs`, `Address.cs`, `Order.cs`, `PurchaseUnit.cs`, `PaymentCollection.cs`, `AuthorizationWithAdditionalData.cs`, `CardResponse.cs` |
| `Orders.AuthorizeOrder` | `AuthorizeOrderRequest { Id: string required (`^[A-Z0-9]+$`), PayPalRequestId?, Prefer = "return=minimal", Body: OrderAuthorizeRequest? }` | none sent | `OrderAuthorizeResponse { Id, Status, PurchaseUnits[0].Payments.Authorizations[], PaymentSource.Card: CardResponse }` | A: `ApiException<AuthorizeOrderError>` · `TryGetError(out Error)` [400,401,403,404,422,500] | `map/operations/Orders.md`; `Requests/Orders/AuthorizeOrderRequest.cs`; `Models/OrderAuthorizeResponse.cs`, `OrderAuthorizeResponsePaymentSource.cs` |
| `Orders.GetOrder` | `GetOrderRequest { Id required }` | — | `Order` (as above) incl. `Payments.Captures[]: OrdersCapture`, `Payments.Refunds[]: Refund` | A: `ApiException<GetOrderError>` · `TryGetError(out Error)` [401,404] | `map/operations/Orders.md`; `Requests/Orders/GetOrderRequest.cs`; `Models/OrdersCapture.cs` |
| `Payments.GetAuthorizedPayment` | `GetAuthorizedPaymentRequest { AuthorizationId required }` | — | `PaymentAuthorization { Id, Status: AuthorizationStatus?, Amount, ExpirationTime, CreateTime }` | A: `ApiException<GetAuthorizedPaymentError>` · `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] | `map/operations/Payments.md`; `Requests/Payments/GetAuthorizedPaymentRequest.cs`; `Models/PaymentAuthorization.cs` |
| `Payments.ReauthorizePayment` | `ReauthorizePaymentRequest { AuthorizationId required, PayPalRequestId? (kept 45 d), Prefer, Body: ReauthorizeRequest? }` | `ReauthorizeRequest { Amount (amount): Money? }` — remarks: "Supports only the amount request parameter"; honor period 3 days; reauthorize allowed within 29-day authorization period; after 30 days a new authorization is required | `PaymentAuthorization` (new `Id`) | A: `ApiException<ReauthorizePaymentError>` · `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent` [500] | `map/operations/Payments.md`; `Api/Payments.cs` remarks; `Requests/Payments/ReauthorizePaymentRequest.cs`; `Models/ReauthorizeRequest.cs` |
| `Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPaymentRequest { AuthorizationId required, PayPalRequestId? (kept 45 d), Prefer = "return=minimal", Body: CaptureRequest? }` | `CaptureRequest { Amount (amount): Money?, InvoiceId (invoice_id)?, FinalCapture (final_capture): bool? = **false** }`; `Money { CurrencyCode required, Value required }` | `CapturedPayment { Id, Status: CaptureStatus?, Amount: Money?, SellerReceivableBreakdown { GrossAmount required, PaypalFee?, NetAmount? } }` | A: `ApiException<CaptureAuthorizedPaymentError>` · `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent` [500] | `map/operations/Payments.md`; `Requests/Payments/CaptureAuthorizedPaymentRequest.cs`; `Models/CaptureRequest.cs`, `CapturedPayment.cs`, `SellerReceivableBreakdown.cs`, `Money.cs` |
| `Payments.GetCapturedPayment` | `GetCapturedPaymentRequest { CaptureId required }` | — | `CapturedPayment` | A: `ApiException<GetCapturedPaymentError>` · `TryGetError(out Error)` [401,403,404] · `TryGetNoContent` [500] | `map/operations/Payments.md`; `Requests/Payments/GetCapturedPaymentRequest.cs` |
| `Payments.VoidPayment` | `VoidPaymentRequest { AuthorizationId required, PayPalRequestId? (45 d), Prefer }` — no body; remarks: cannot void a fully captured authorization | — | `PaymentAuthorization { Status }` (minimal response may omit fields → re-read when null) | A: `ApiException<VoidPaymentError>` · `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent` [500] | `map/operations/Payments.md`; `Requests/Payments/VoidPaymentRequest.cs` |
| `Payments.RefundCapturedPayment` | `RefundCapturedPaymentRequest { CaptureId required, PayPalRequestId? (45 d), Prefer, Body: RefundRequest? }` — remarks: empty payload = full refund; amount = partial | `RefundRequest { Amount (amount): Money?, CustomId (custom_id)?, InvoiceId (invoice_id)?, NoteToPayer (note_to_payer)? }` | `Refund { Id, Status: RefundStatus?, Amount: Money?, SellerPayableBreakdown { TotalRefundedAmount? } }` | A: `ApiException<RefundCapturedPaymentError>` · `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent` [500] | `map/operations/Payments.md`; `Api/Payments.cs` remarks; `Requests/Payments/RefundCapturedPaymentRequest.cs`; `Models/RefundRequest.cs`, `Refund.cs`, `SellerPayableBreakdown.cs` |
| `Vault.CreateSetupToken` | `CreateSetupTokenRequest { Body: SetupTokenRequest required, PayPalRequestId? (≤108, kept 3 h) }` | `SetupTokenRequest { Customer (customer): Customer? { Id (id)? }, PaymentSource (payment_source): SetupTokenRequestPaymentSource required { Card (card): SetupTokenRequestCard { Name, Number, Expiry, SecurityCode, BillingAddress: Address? } } }` | `SetupTokenResponse { Id, Status: PaymentTokenStatus? (default Created), Customer { Id } }` | A: `ApiException<CreateSetupTokenError>` · `TryGetError(out Error)` [400,403,422,500] | `map/operations/Vault.md`; `Requests/Vault/CreateSetupTokenRequest.cs`; `Models/SetupTokenRequest.cs`, `SetupTokenRequestPaymentSource.cs`, `SetupTokenRequestCard.cs`, `SetupTokenResponse.cs`, `Customer.cs` |
| `Vault.CreatePaymentToken` | `CreatePaymentTokenRequest { Body: PaymentTokenRequest required, PayPalRequestId? (kept 3 h) }` | `PaymentTokenRequest { Customer?, PaymentSource (payment_source): PaymentTokenRequestPaymentSource required { Token (token): VaultTokenRequest { Id (id) required, Type (type): VaultTokenRequestType required } } }` | `PaymentTokenResponse { Id, Customer: CustomerResponse?, PaymentSource.Card: CardPaymentTokenEntity { LastDigits, Brand, Expiry } }` | A: `ApiException<CreatePaymentTokenError>` · `TryGetError(out Error)` [400,403,404,422,500] | `map/operations/Vault.md`; `Requests/Vault/CreatePaymentTokenRequest.cs`; `Models/PaymentTokenRequest.cs`, `PaymentTokenRequestPaymentSource.cs`, `VaultTokenRequest.cs`, `PaymentTokenResponse.cs`, `PaymentTokenResponsePaymentSource.cs`, `CardPaymentTokenEntity.cs` |
| `Vault.DeletePaymentToken` | `DeletePaymentTokenRequest { Id required (`^[0-9a-zA-Z_-]+$`, ≤36) }` — HTTP DELETE | — | `void` | A: `ApiException<DeletePaymentTokenError>` · `TryGetError(out Error)` [400,403,500] | `map/operations/Vault.md`; `Requests/Vault/DeletePaymentTokenRequest.cs` |
| `TransactionSearch.SearchTransactions` | `SearchTransactionsRequest { StartDate: string required, EndDate: string required (RFC3339, seconds required; **max range 31 days**), Fields = "transaction_info", BalanceAffectingRecordsOnly = "Y", PageSize: int = 100 (1..500), Page: int = 1 (≥1) }` — remarks: up to 3 h reporting lag; last 3 years only | — | `SearchResponse { TransactionDetails: IReadOnlyList<TransactionDetails>? , Page?, TotalItems?, TotalPages? }`; `TransactionDetails.TransactionInfo: TransactionInformation { TransactionId, PaypalReferenceId, TransactionEventCode, TransactionInitiationDate, TransactionAmount: Money?, FeeAmount: Money?, TransactionStatus, InvoiceId, CustomField }` | **B**: `ApiException<RawError>` | `map/operations/TransactionSearch.md`; `Api/TransactionSearch.cs` remarks; `Requests/TransactionSearch/SearchTransactionsRequest.cs`; `Models/SearchResponse.cs`, `TransactionDetails.cs`, `TransactionInformation.cs` |

`Error` (typed payload, `Models/Error.cs`): `Name (name) required`, `Message (message) required`, `DebugId (debug_id) required`, `Details (details): IReadOnlyList<ErrorDetails>?`; `ErrorDetails { Issue (issue) required, Description?, Field?, Value? }` (`Models/ErrorDetails.cs`).

### Enum values needed

| Enum (`Models/Enums/…`) | Members used |
| --- | --- |
| `CheckoutPaymentIntent` | `Authorize` ("AUTHORIZE") |
| `OrderStatus` | `Created`, `Saved`, `Approved`, `Voided`, `Completed`, `PayerActionRequired` |
| `AuthorizationStatus` | `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending` (open enum; any other value, e.g. an expiry, arrives via `Value`) |
| `CaptureStatus` | `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed` |
| `RefundStatus` | `Cancelled`, `Failed`, `Pending`, `Completed` |
| `PaymentTokenStatus` | `Created`, `PayerActionRequired`, `Approved`, `Vaulted`, `Tokenized` |
| `VaultTokenRequestType` | `SetupToken` ("SETUP_TOKEN") |
| `CardBrand` | read-only (`.Value` for display) |

### Client construction / auth / servers

- Only constructor: `new PayPalServerSdk.PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` (`PayPalServerSdkClient.cs`); DI alternative `services.AddPayPalServerSdkClient(...)` (`ServiceCollectionExtensions.cs`).
- Auth: `options.Oauth2 = new OAuth2ClientCredentials { ClientId, ClientSecret }`. Token endpoint resolves through `server.Default("/v1/oauth2/token")` (`AuthSchemes.cs`) — i.e. the same base URL as every operation, so a base-URL override also moves the token request. Token cache lives in the `AuthSchemes` created per client instance (`PayPalServerSdkClient.cs` ctor).
- Servers: one group `Default`; one environment `ServerEnvironment.Sandbox` (default) → `https://api-m.sandbox.paypal.com`; override point `options.Server.Default.Sandbox.BaseUrl` (`Servers/DefaultOptions.cs`). No live environment is declared.
- Options surface used: `Environment`, `Server`, `Retry` (`RetryOptions`, all members required — start from `RetryOptions.Default()`), `Logging` (`LoggingOptions`), `Oauth2`.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| A saved-card id the caller pays with must be a payment token this app stored for **that** buyer and not removed | `Orders.CreateOrder` (`card.vault_id`) ← `Vault.CreatePaymentToken` (result persisted as `PaymentMethod`) | `PaymentService.PayAsync` loads `PaymentMethod` by id **and** buyer id, rejects removed, before the gateway call |
| A saved-card id the caller deletes must be one this app stored for that buyer | `Vault.DeletePaymentToken` ← `Vault.CreatePaymentToken` | `SavedCardService.RemoveAsync` (buyer-scoped lookup) |
| Authorization id captured/voided/reauthorized must be the one stored on that order's payment | `Payments.CaptureAuthorizedPayment` / `VoidPayment` / `ReauthorizePayment` / `GetAuthorizedPayment` ← `Orders.CreateOrder` / `AuthorizeOrder` / `ReauthorizePayment` | `PaymentService.FulfilAsync` / `CancelAsync` read it from `Payment.AuthorizationId`; never from the caller |
| Capture id refunded must be the stored capture of that order; refund amount ≤ captured − Σ(non-failed refunds) | `Payments.RefundCapturedPayment` ← `Payments.CaptureAuthorizedPayment` | `PaymentService.RefundAsync` → `Payment.ReserveRefund` (under the order lock claim) |
| Currency of every amount sent = `PayPal:Currency`; amount = order total formatted to the currency's minor units | `CreateOrder` / `CaptureAuthorizedPayment` / `RefundCapturedPayment` ← configuration + `Order.Total()` | `MoneyFormatter` + `Payment` (amount fixed at authorization) |

## 3. Trap notes

| Step | Hazard → consequence | Skill |
| --- | --- | --- |
| 4 | Client/`HttpClient` lifetime and where the OAuth token cache lives → per-request construction re-fetches tokens / exhausts sockets | MUST load `paypal:dotnet-client-initialization` |
| 4 | Credentials applied at the wrong point or blank → every call fails as 401 rather than at startup | MUST load `paypal:dotnet-authentication` |
| 4 | What `RetryOptions.Timeout` bounds, which verbs are resent, and what bounds a whole call → the 30-second promise can be broken by retries | MUST load `paypal:dotnet-configuration-resilience` |
| 4 | SDK logging defaults / environment-variable switch → card numbers could reach logs | MUST load `paypal:dotnet-configuration-resilience` |
| 5–10 | Request-record construction, required members, injected headers vs real idempotency keys → duplicate charges if the wrong header is relied on | MUST load `paypal:dotnet-calling-endpoints` |
| 5–10 | Open enums (no C# enum), equality/`Value` reads, nullable response members → wrong state mapping on unknown status | MUST load `paypal:dotnet-models` |
| 5–10 | Which exception types reach the catch (typed, raw, deserialization, connection, timeout, cancellation) → unknown outcomes reported as failures, or crashes | MUST load `paypal:dotnet-error-handling` |
| 10 | Page walking and caps on `SearchTransactions` → silent truncation of the report | MUST load `paypal:dotnet-configuration-resilience` |
| 11 | Which seam to fake in tests → tests coupled to SDK internals or hitting the network | MUST load `paypal:dotnet-testing` |

## 4. REQUIRED READING (load all before implementation starts; this sheet deliberately does not carry their contents)

- `paypal:dotnet-client-initialization` — step 4 (client + DI)
- `paypal:dotnet-authentication` — step 4 (OAuth2 credentials, fail-fast)
- `paypal:dotnet-calling-endpoints` — steps 5–10 (every call)
- `paypal:dotnet-models` — steps 5–10 (request bodies, enums, response mapping)
- `paypal:dotnet-error-handling` — steps 5–10 (gateway error boundary)
- `paypal:dotnet-configuration-resilience` — steps 4, 10 (timeouts, retries, logging, paging)
- `paypal:dotnet-testing` — step 11 (offline tests)

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` bound from `PayPal:`; validated with `ValidateOnStart`: `ClientId` and `ClientSecret` each non-blank, `Currency` 3 letters, `Environment` = `sandbox` or (any other value) only with `BaseUrl` set, `BaseUrl` absolute https/http URI when present. Host refuses to start otherwise. |
| 2 | Secret sourcing & rotation | Dev: .NET user-secrets (`PayPal:ClientId`, `PayPal:ClientSecret`, …) loaded from `PAYPAL_*` env vars by the operator; any deployment: `PayPal__ClientId`-style env vars or a secret store; `PAYPAL_*` env vars are also mapped onto missing `PayPal:` keys at startup. Options captured once in the singleton client → rotation takes effect on process restart (documented). |
| 3 | Total timeout budget | Every PublicApi request that touches PayPal runs under one `PayPalCallBudget` (scoped): a 28 s `CancellationToken` deadline passed to every SDK call (settlement re-reads included); SDK per-attempt `Timeout` 10 s. Expiry → `PaymentProviderTimeoutException` → HTTP 504 "PayPal did not respond". Reconciliation stops at the deadline and returns a partial report flagged incomplete. |
| 4 | Write-retry ownership | SDK retries only GET (configured `HttpMethodsToRetry` = GET); all scope writes are POST/DELETE and are never resent by the SDK. Our code replays a POST only with the **same** `PayPal-Request-Id`. |
| 5 | Idempotency & ambiguous writes | Real key = `PayPalRequestId` member (header `PayPal-Request-Id`) on CreateOrder (6 h), AuthorizeOrder (6 h), Capture/Reauthorize/Void/Refund (45 d), CreateSetupToken/CreatePaymentToken (3 h). Deterministic per claim, stored in our DB before the call: authorize `eshop-{orderId}-auth-{attemptGuid}`, capture `eshop-{orderId}-capture-{authId}`, reauthorize `eshop-{orderId}-reauth-{authId}`, void `eshop-{orderId}-void-{authId}`, refund `eshop-{orderId}-refund-{refundGuid}`. `DeletePaymentToken` has no key → idempotent by nature (local removal first; repeat DELETE retries provider removal). The injected `Idempotency-Key` header is not relied on. |
| 6 | Observability | Our logs: Information for each PayPal operation outcome (order id, PayPal ids, status), Warning for provider refusals with PayPal `debug_id` + issue codes, Error for unknown outcomes. `debug_id` is surfaced in API error bodies too. SDK `LogRequestBody` = false, `LogRequestHeaders`/`LogResponseHeaders` = false (`LoggingOptions` has no response-body switch). |
| 7 | Sensitive data | Card number/CVV/expiry are in `CardRequest` and `SetupTokenRequestCard`. SDK body logging off and `Logging.LoggerFactory` set explicitly to the host's factory (disarms `PAYPALSERVERSDKCLIENT_LOG`). Card DTOs override `ToString()` to redact; card fields never persisted (only PayPal token id, brand, last digits, expiry). Exception middleware never echoes request bodies. |
| 8 | Environment selection | One server group `Default`, one environment `Sandbox`. `PayPal:Environment=sandbox` → SDK sandbox default URL. Any other environment requires `PayPal:BaseUrl` (startup validation) — the SDK declares no live host, so nothing is guessed. `PayPal:BaseUrl`, when set, is assigned to `options.Server.Default.Sandbox.BaseUrl` and therefore used verbatim for every call including the token request. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. Claims are rows in `PaymentClaims` (string primary key) in `CatalogContext`. |
| 10 | Partial results | Reconciliation: `ReconciliationReport.IsComplete` + `IncompleteReason` + per-window `PagesFetched/TotalPages`. Saved-card list is our DB (no paging). |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES: settled in the failing write's catch with a re-read/replay under the remaining budget; if still unsettled, the payment is marked `…Unknown` with its stored `PayPal-Request-Id`, and the next invocation of the same action replays that request id before doing anything else. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Authorize (pay) | `PaymentClaims` row, PK `order:{orderId}` (order lock) in CatalogContext | PK violation on insert of the same key | `EfPaymentClaimStore.TryAcquireAsync` catch (`DbUpdateException`/`ArgumentException`/`InvalidOperationException`) → `PaymentConflictException` (409) | `OrderPaymentLock.AcquireAsync` (in `PaymentService.PayAsync`), then `PayPalPaymentGateway.AuthorizeAsync` → `Orders.CreateOrder` |
| Capture (fulfil) | same order lock row | same | same | `OrderPaymentLock.AcquireAsync` (in `PaymentService.FulfilAsync`), then `PayPalPaymentGateway.CaptureAsync` → `Payments.CaptureAuthorizedPayment` |
| Void (cancel) | same order lock row | same | same | `OrderPaymentLock.AcquireAsync` (in `PaymentService.CancelAsync`), then `PayPalPaymentGateway.VoidAsync` → `Payments.VoidPayment` |
| Refund | order lock row + `PaymentClaims` PK `refund:{orderId}:{idempotencyKey}` | PK violation | same | `IPaymentClaimStore.TryAcquireAsync("refund:…")` (in `PaymentService.RefundAsync`), then `PaymentService.SendRefundAsync` → `PayPalPaymentGateway.RefundAsync` → `Payments.RefundCapturedPayment` |
| Save card | `PaymentClaims` PK `card:{buyerId}:{idempotencyKey}` when caller supplies a key; otherwise no dedupe (no money moves) | PK violation | same | `IPaymentClaimStore.TryAcquireAsync("card:…")` (in `SavedCardService.SaveAsync`), then `PayPalPaymentGateway.SaveCardAsync` → `Vault.CreateSetupToken` / `Vault.CreatePaymentToken` |

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `SearchTransactions` | 31-day window, `PageSize` 500, `ReconciliationService.MaxPagesPerWindow` (200) cap, 25 s request budget | `ReconciliationReport.IsComplete == false` + `IncompleteReason` (+ per-window `PagesFetched`/`TotalPages`) | `ReconciliationService.BuildAsync` (sets `incompleteReason` on timeout or cap) |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| CreateOrder (authorize) | `Orders.CreateOrder` replay (PayPal returns the original for a repeated `PayPal-Request-Id`) | stored `PayPal-Request-Id` (`Payment.AuthorizationRequestId`) | `PaymentService.PayAsync` catch `IsUnknownOutcome` → replay; still unknown → `PaymentStatus.AuthorizationPending`, the next `PayAsync` replays it | `ResilienceTests.Authorization_whose_response_is_lost_is_settled_by_replaying_the_same_request_id`, `ResilienceTests.An_unresponsive_PayPal_yields_a_did_not_respond_error_within_the_budget` |
| AuthorizeOrder | `Orders.GetOrder` | PayPal order id | `PayPalPaymentGateway.AuthorizeApprovedOrderAsync` catch → `GetOrder` | `OrderPaymentFlowTests.Order_needing_a_second_step_is_authorized_explicitly` (path exercised; the lost-response case shares the CreateOrder replay test) |
| ReauthorizePayment | `Payments.ReauthorizePayment` replay | deterministic `eshop-{orderId}-reauth-{authId}` | `PaymentService.RenewHoldAsync` catch → replay; still unknown → 504, the next fulfil replays the same id | `ResilienceTests.A_stale_authorization_is_renewed_before_capture` (path exercised) |
| CaptureAuthorizedPayment | `Orders.GetOrder` (captures) | PayPal order id (`Payment.ProviderOrderId`) | `PaymentService.FulfilAsync` catch → `TryFindCaptureAsync`; still unknown → `PaymentStatus.CapturePending`, the next fulfil re-reads, then replays `Payment.CaptureRequestId` | `ResilienceTests.Capture_whose_response_is_lost_is_settled_by_re_reading_PayPal`, `PayPalPaymentGatewayTests.A_lost_capture_is_found_again_on_the_PayPal_order` |
| VoidPayment | `Payments.GetAuthorizedPayment` | authorization id | `PaymentService.CancelAsync` catch → `TryGetAuthorizationAsync`; still unknown → `PaymentStatus.VoidPending`, the next cancel re-reads, then replays `Payment.VoidRequestId` | `PayPalPaymentGatewayTests.A_dropped_connection_on_a_write_is_an_unknown_outcome_and_is_never_resent_by_the_SDK` (classification of the failure) |
| RefundCapturedPayment | `Payments.RefundCapturedPayment` replay | stored `PayPal-Request-Id` (`PaymentRefund.ProviderRequestId`) | `PaymentService.SendRefundAsync` catch → replay; still unknown → refund stays `Pending` (still reserving funds), the same idempotency key replays it | `ResilienceTests.Refund_whose_response_is_lost_is_replayed_under_the_same_request_id_and_counted_once` |
| CreateSetupToken / CreatePaymentToken | replay with same `PayPal-Request-Id` | request id generated per save (`eshop-card-…`, `…-token`) | `PayPalPaymentGateway.WithReplayAsync`; still unknown → 504 and the save claim is released so the caller can retry (no money moves; worst case an orphan vault token) | `PayPalPaymentGatewayTests.Saving_a_card_vaults_it_via_a_setup_token_and_returns_only_display_details` (path exercised) |
| DeletePaymentToken | none needed — local removal already done; repeat DELETE retries | payment token id | `SavedCardService.RemoveAsync` catch → `ProviderDeletionPending = true` | `SavedCardTests.Save_list_pay_with_and_delete_a_card` |

## 6. Assumptions & Blockers

- UNVERIFIED: `CreateOrder` with intent AUTHORIZE + card returns either `COMPLETED` with `purchase_units[0].payments.authorizations[0]`, or `APPROVED` requiring `AuthorizeOrder`. Code handles both; `PAYER_ACTION_REQUIRED` is treated as an unsupported challenge (task says STOP and report if it happens).
- UNVERIFIED: `CreateSetupToken` with card details server-side returns `APPROVED` without payer action. `PAYER_ACTION_REQUIRED` → reported as unsupported.
- UNVERIFIED: PayPal replays the original response for a repeated `PayPal-Request-Id` (record docs only say keys are stored N hours).
- UNVERIFIED: whether `ReauthorizePayment` is accepted for card authorizations (remarks say "PayPal account payment"). If refused, fulfilment still attempts capture of the original authorization and, if that fails, returns an operator-actionable error.
- Reconciliation reads `BalanceAffectingRecordsOnly = "Y"` (money movements: captures and refunds); holds are not payments and are not reconciled. Verified live: the sandbox report returned 4 pages / 1600+ balance-affecting transactions for 2026-09.
- Blockers: none.

## 7. Verification log (2026-10-06, PayPal sandbox)

- `CreateOrder` (intent AUTHORIZE, card) returned `COMPLETED` with `purchase_units[0].payments.authorizations[0]` `CREATED` in one step; the `AuthorizeOrder` path is kept for `APPROVED` answers. No `PAYER_ACTION_REQUIRED` challenge was returned for the sandbox test Visa.
- `CreateSetupToken` with card details was accepted; `CreatePaymentToken` from it returned the vault id (brand VISA, last digits 1111).
- Capture reported `seller_receivable_breakdown` with `paypal_fee` and `net_amount` (47.50 → fee 1.72, net 45.78).
- Partial refunds 10 + 20 + remainder 17.50 completed; an over-refund was refused locally (409) before reaching PayPal.
- Void of a saved-card authorization returned `VOIDED`; deleting the vault token returned 204.
