# maxio-advanced-billing-plan.md — Maxio subscription billing for eShopOnWeb

## Scope & sequence

Additive capability: recurring subscription billing on `src/PublicApi`, Maxio Advanced Billing as system of record. No change to the existing cart/checkout flow.

1. **Settings + client registration** — bind `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:Environment` (US/EU, default US), optional `Maxio:BaseUrl` (verbatim override). Fail-fast at startup when a required part is blank. Register a singleton `MaxioAdvancedBillingClient` built once from the bound options. (No SDK operations.)
2. **Local link store** — `MaxioCustomerLink` (PK = eShop user id = Maxio customer reference) and `MaxioSubscriptionLink` (PK = Maxio subscription reference) on `AppIdentityDbContext` + EF migration. These are the DUPLICATE CLAIMS stores. (No SDK operations.)
3. **Plan catalogue service** — `ListProductsForProductFamily` (`ProductFamilyId = "handle:" + Maxio:ProductFamilyHandle`, `PerPage 200`, page loop) → `GET /api/subscription-plans`.
4. **Ensure customer** — `ReadCustomerByReference` (reference = eShop user id); on 404, claim local link then `CreateCustomer`. → used by subscribe + my-subscriptions.
5. **Subscribe** — validate `productHandle` ∈ family product set (step 3's operation); `FindSubscription` (reference = `{userId}:{handle}`) → existing? return it : claim local link then `CreateSubscription` (`product_handle`, `customer_reference`, `reference`). → `POST /api/subscriptions`.
6. **My subscriptions** — `ReadCustomerByReference` → `ListCustomerSubscriptions(customerId)` → `GET /api/my-subscriptions`.
7. **Verification** — live end-to-end against the sandbox: JWT from `/api/authenticate`, plan catalogue, subscribe (active, price, next-billing-date confirmed), double-click idempotency, concurrent double-click race (one subscription), unknown plan 404, unauthenticated 401, second shopper independent; typed-error translation exercised live (422 from automatic collection → caller-safe 422, fixed with `PaymentCollectionMethod = Remittance`).

Signatures below are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter, built with an object initializer whose property names are the record's own, never flat arguments. Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

## CONTRACT SHEET

| # | Controller property · method | Request record + members | Body model + fields | Response envelope + fields read | Error case | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 1 | `client.ProductFamilies.ListProductsForProductFamily` | `Requests.ProductFamilies.ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` (accepts `handle:<handle>`); `Page: int` (default 1); `PerPage: int` (default 20, max 200) | — | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; read `.Product` (required) → `Handle`, `Name`, `Description`, `PriceInCents: long?`, `Interval: int?`, `IntervalUnit` (enum `IntervalUnit`), `RequireCreditCard: bool?`, `ProductFamily` | Case A `ApiException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404], `TryGetRawError(out RawError)` | Page/PerPage params; walk until short page, cap 10 pages, expose truncated flag | map/operations/ProductFamilies.md; `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/ProductResponse.cs`; `Models/Product.cs` |
| 2 | `client.Customers.ReadCustomerByReference` | `Requests.Customers.ReadCustomerByReferenceRequest`: `Reference: string, required` | — | `Models.CustomerResponse`; read `.Customer` (required) → `Id: int?`, `Email`, `FirstName`, `LastName`, `Reference` | Case B `ApiException<RawError>`: `.Error.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`; 404 ⇒ customer absent | none | map/operations/Customers.md; `Models/CustomerResponse.cs`; `Models/Customer.cs` |
| 3 | `client.Customers.CreateCustomer` | `Requests.Customers.CreateCustomerOperationRequest`: `Body: Models.CreateCustomerRequest?` | `Models.CreateCustomerRequest.Customer` (wire `customer`, required) : `Models.CreateCustomer` — `FirstName: string, required`, `LastName: string, required`, `Email: string, required`, `Reference: string?` (optional — we set it = eShop user id) | `Models.CustomerResponse`; read `.Customer.Id`, `.Customer.Reference` | Case A `ApiException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out Models.CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` | none | map/operations/Customers.md; `Requests/Customers/CreateCustomerOperationRequest.cs`; `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs` |
| 4 | `client.Subscriptions.FindSubscription` | `Requests.Subscriptions.FindSubscriptionRequest`: `Reference: string?` (query `reference`) | — | `Models.SubscriptionResponse`; read `.Subscription` → `Id`, `State` (enum `Models.Enums.SubscriptionState`), `ProductPriceInCents`, `CurrentPeriodEndsAt`, `Product`, `Reference` | Case A `ApiException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404 ⇒ no match], `TryGetRawError(out RawError)` | none | map/operations/Subscriptions.md; `Requests/Subscriptions/FindSubscriptionRequest.cs`; `Models/Subscription.cs` |
| 5 | `client.Subscriptions.CreateSubscription` | `Requests.Subscriptions.CreateSubscriptionOperationRequest`: `Body: Models.CreateSubscriptionRequest?` | `Models.CreateSubscriptionRequest.Subscription` (wire `subscription`, required) : `Models.CreateSubscription` — `ProductHandle: string?` (wire `product_handle`), `CustomerReference: string?` (wire `customer_reference`), `Reference: string?` (wire `reference`), `PaymentCollectionMethod: CollectionMethod?` (wire `payment_collection_method`; set to `Remittance` — this integration captures no payment method; automatic collection was rejected live with "No payment method was on file") | `Models.SubscriptionResponse`; read `.Subscription.Id`, `.State`, `.ProductPriceInCents`, `.CurrentPeriodEndsAt`, `.Product.Name`/`.Product.Handle`, `.Customer.Id`, `.Reference` | Case A `ApiException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out Models.ErrorListResponse1)` [422; `.Errors: IReadOnlyList<string>`], `TryGetRawError(out RawError)` | none | map/operations/Subscriptions.md; `Requests/Subscriptions/CreateSubscriptionOperationRequest.cs`; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs`; `Models/ErrorListResponse1.cs`; `Models/Enums/CollectionMethod.cs` (verified live) |
| 6 | `client.Customers.ListCustomerSubscriptions` | `Requests.Customers.ListCustomerSubscriptionsRequest`: `CustomerId: int, required` | — | `IReadOnlyList<Models.SubscriptionResponse>`; read `.Subscription` → same fields as row 4/5 | Case B `ApiException<RawError>` | none stated on the page ⇒ default (no pagination) | map/operations/Customers.md; `Requests/Customers/ListCustomerSubscriptionsRequest.cs` |

Enum values needed (from declaring files): `SubscriptionState` (`Models/Enums/SubscriptionState.cs`) — live states we accept as "existing": `Active`, `Trialing`, `Pending`, `Assessing`; end-of-life ⇒ a fresh subscription may be created: `Canceled`, `Expired`, `FailedToCreate`, `TrialEnded`, `OnHold`, `Unpaid`, `Suspended`, `Paused`; transient/problem (`PastDue`, `SoftFailure`) treated as existing. Compare with `SubscriptionState.Active` etc. static members; unknown values via `.Match(..., otherwise:)` → treat as existing (conservative). `IntervalUnit` (`Models/Enums/IntervalUnit.cs`) read from product for display.

Client construction/auth/server-node facts:

- Client: `MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)` — the only ctor. Auth: `options.BasicAuth = new BasicAuthCredentials { Username = <API key>, Password = "x" }` (Basic; map: *username is the API key, password is `x`*).
- Environments: `ServerEnvironment.Us` (default) / `ServerEnvironment.Eu` (`Servers/ServerEnvironment.cs`). Config `Maxio:Environment` US/EU selects it; unknown value ⇒ fail-fast.
- Server nodes: `options.Server.Production.Us.Site` = subdomain (default `"subdomain"`); `options.Server.Production.Us.BaseUrl` = `Maxio:BaseUrl` verbatim when configured (template default `https://{site}.chargify.com`). Only the `Production` group is touched by this scope (no Ebb operation used). (Source: `Servers/ProductionOptions.cs`.)
- All six operations authenticate with `options.BasicAuth` (map "Auth" bullets).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
|---|---|---|
| a `productHandle` accepted by CreateSubscription must be one product this application offers = a product of the configured family | `CreateSubscription` ← `ListProductsForProductFamily` | `CreateSubscriptionEndpoint.HandleAsync`: fetch family product set, reject unknown handle with 404 before any Maxio write |
| the `customer_reference` passed to CreateSubscription must reference a customer ensured by `ReadCustomerByReference`/`CreateCustomer` (reference = eShop user id) | `CreateSubscription` ← `ReadCustomerByReference` / `CreateCustomer` | `MaxioBillingService.SubscribeAsync` calls EnsureCustomer first and passes the same reference value |
| the subscription `reference` must be the value recorded in the local claim (used for FindSubscription reconciliation) | `CreateSubscription` ← local claim row (`MaxioSubscriptionLink.PK`) | `MaxioBillingService.SubscribeAsync` builds the reference once, claims it, and passes the identical string |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
|---|---|---|---|---|
| CreateCustomer (ref = eShop user id) | `MaxioCustomerLink` row, PK `MaxioReference` = user id | PK violation on second insert (`DbUpdateException`) | catch in `MaxioBillingService.EnsureCustomerAsync` → re-read by reference | claim: `AddAsync` before `CreateCustomer`, `EnsureCustomerAsync`; settle: `ReadCustomerByReferenceOrNullAsync` |
| CreateSubscription (ref = `{userId}:{handle}` or `-N` suffix) | `MaxioSubscriptionLink` row, PK `MaxioReference` = subscription reference | PK violation on second insert (`DbUpdateException`) | catch in `MaxioBillingService.SubscribeAsync` → FindSubscription with the claimed reference | claim: `AddAsync` before `CreateSubscription`, `SubscribeAsync` step 3; settle: `FindSubscriptionOrNullAsync` in the `DbUpdateException` catch |

Sequential double-click is prevented by the provider-side lookups (ReadCustomerByReference / FindSubscription) before each write; the local PK claim closes the concurrent race.

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
|---|---|---|---|
| ListProductsForProductFamily page loop | `PerPage` 200 × 10-page safety cap | `SubscriptionPlanListResponse.Truncated: bool` set true when the cap is hit before a short page | `MaxioBillingService.GetPlansAsync` (cap check + warning log) → `SubscriptionPlanListEndpoint.HandleAsync` sets `response.Truncated` |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
|---|---|---|---|---|
| CreateSubscription | `FindSubscription` | the claimed subscription `reference` (exact string stored in `MaxioSubscriptionLink` before the call) | `SettleUnknownCreateOutcomeAsync` called from the unknown-outcome catch in `MaxioBillingService.SubscribeAsync` → found ⇒ record link, return success; 404 ⇒ release claim, throw "reconciled as not created" | `SubscribeAsync` live exercise |
| CreateCustomer | `ReadCustomerByReference` | the customer `reference` (= eShop user id, also the local claim PK) | unknown-outcome catch in `MaxioBillingService.EnsureCustomerAsync` → found ⇒ record link, return success; miss ⇒ release claim, throw translated | `EnsureCustomerAsync` |

## Trap notes

- Step 1/3: how the `HttpClient`/handler pipeline must live for a singleton client, and what `AddMaxioAdvancedBillingClient` does or does not handle — **MUST load dotnet-client-initialization**.
- Step 1: which property carries the API key vs the literal `x` password, and loading it from configuration rather than hardcoding — **MUST load dotnet-authentication**.
- Steps 3–6: one-request-record rule, whether required members must be set (e.g. `CreateCustomer.FirstName/LastName/Email`), cancellation flow-through — **MUST load dotnet-calling-endpoints**.
- Steps 3–6: `OpenStringEnum` records are not C# enums; build/read via static members + `.Match` with `otherwise`; wire names differ from C# names — **MUST load dotnet-models**.
- Steps 3–6: Case A vs Case B per operation, `TryGetNoContent` 404 detection on FindSubscription, and that `ResponseDeserializationException` is not `ApiException<TError>` and must be caught separately or the catch ladder leaks — **MUST load dotnet-error-handling**.
- Step 1 & 3–6: what `Retry.Timeout` bounds (per-attempt, not total), which HTTP methods the SDK resends (`PUT` is, `POST` is not), `LogRequestBody` redaction policy, and the `MAXIOADVANCEDBILLINGCLIENT_LOG` env-var arming when `LoggerFactory` is unset — **MUST load dotnet-configuration-resilience**.
- Step 7: the `HttpClient` constructor argument is the stub seam; keep tests off SDK internals — **MUST load dotnet-testing** (only if SDK-stubbed tests are written).

## REQUIRED READING

- `dotnet-client-initialization` — Step 1: client/DI registration and HttpClient lifetime.
- `dotnet-authentication` — Step 1: BasicAuth credentials wiring.
- `dotnet-calling-endpoints` — Steps 3–6: request-record construction and call patterns.
- `dotnet-models` — Steps 3–6: enums/unions/wire names.
- `dotnet-error-handling` — Steps 3–6: every try/catch around an SDK call (always required).
- `dotnet-configuration-resilience` — Step 1 + all calls: retries, timeout, base-URL override, logging posture.
- `dotnet-testing` — Step 7 if tests stub the SDK.

These are to be loaded **before implementation starts**; the sheet deliberately does not carry their contents.

**Mandatory hazard row:** a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## PRODUCTION READINESS

| # | Concern | The decision |
|---|---|---|
| 1 | Credential fail-fast | `MaxioOptions` bound from the `Maxio:` section; `MaxioBillingRegistration.Validate` refuses the first client construction when `ApiKey` is blank, `ProductFamilyHandle` is blank, or (`Subdomain` blank AND `BaseUrl` blank) — the missing key is named and no value is echoed. Validation runs on first singleton resolution rather than host registration, so test hosts that build `Program.cs` without Maxio configuration stay bootable, while the first billing call still fails fast before any request is sent. Surfaced as a config fault, not a 401. |
| 2 | Secret sourcing & rotation | Values come from .NET user-secrets, populated from the `MAXIO_*` env vars at setup time; `BaseUrl` optional. The client options object is built **once at DI registration** and captured in the singleton client ⇒ rotated secret requires process restart; that is accepted and stated here. |
| 3 | Total timeout budget | `Retry.Timeout = 30s` **per attempt**; SDK retries GETs ⇒ a hung GET can cost ~4×30s + backoff. A single `BoundedAsync` helper in `MaxioBillingService` wraps **every** SDK call with a linked CTS deadline of 60s (the only whole-call bound) linked to the caller's token. Recorded worst case for the caller: 60s. |
| 4 | Write-retry ownership | `CreateCustomer` and `CreateSubscription` are `POST` ⇒ never resent by the SDK default method set. No `PUT`/`PATCH`/`DELETE` used in scope. `GET`s may be resent — all are safe reads. |
| 5 | Idempotency & ambiguous writes | No caller-supplied provider idempotency key exists on these request records (the injected `Idempotency-Key` header is not a key). Sequential idempotency: read-before-write (`ReadCustomerByReference`, `FindSubscription`); concurrency: local PK claims (DUPLICATE CLAIMS); ambiguity: reconciliation (UNKNOWN OUTCOMES). |
| 6 | Observability | App-side `ILogger` logs one line per Maxio call boundary (operation, outcome, duration) with the eShop user id and subscription reference as correlation; provider error bodies are logged at Warning (they carry no secrets in these operations). SDK `LogRequestBody` stays **off**; `Logging.LoggerFactory` is assigned explicitly to the app logger factory so `MAXIOADVANCEDBILLINGCLIENT_LOG` cannot arm body logging from outside. |
| 7 | Sensitive data | Request bodies in scope carry: customer first/last name, email, references (PII, not card data). No payment fields are sent (`require_credit_card` products; no `payment_profile_attributes`). ⇒ `LogRequestBody` off, `LoggerFactory` explicit (row 6). Endpoints never echo the raw API key. |
| 8 | Environment selection | Sandbox only for dev/test. `Maxio:Environment` (default US) selects `ServerEnvironment.Us/Eu`; `Maxio:BaseUrl` overrides the Production Us base URL verbatim when set. Only the `Production` server group is used (no Ebb operation). A deployment is kept off live traffic by pointing its config at the sandbox site. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS; local `AppIdentityDbContext` tables with PK-claim inserts precede both writes. |
| 10 | Partial results | See PAGED READS: `SubscriptionPlanListResponse.Truncated` when the page cap is hit. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES: reconcile-on-connection-failure inside the failing call's own catch, before returning. |

## Assumptions & Blockers

- The JWT carries only `ClaimTypes.Name` (= username); the endpoint resolves the `ApplicationUser` (id/email/name) via `UserManager` and uses the user id (GUID string) as the Maxio customer reference. YOUR CALL — not in the map.
- Subscription references may be re-created after an end-of-life subscription by suffixing `-2`, `-3`, … (counted from local link rows). YOUR CALL — not in the map.
- All three endpoints require JWT auth (hero flow is a logged-in shopper); `GET /api/subscription-plans` also requires it. YOUR CALL — not in the map.
- The eShop Web UI is out of scope; the capability is delivered as PublicApi endpoints (per task).
- Known environment caveat (documented by the task): with `UseOnlyInMemoryDatabase=true`, identity users are re-seeded with NEW ids on every restart, so the userId↔Maxio-customer mapping and the `-N` reference counters reset per run — cross-run reconcile works only with a persisted DB (SQL Server). Maxio remains the system of record within a run; this was verified live (restart produced a fresh customer + subscription for the freshly-seeded user id — correct behaviour under the caveat, not a defect).
- The scaffolded `AddMaxioSubscriptionLinks` migration was hand-trimmed to create only the two new tables: EF reported pre-existing model/snapshot drift in the repo (nullability AlterColumns on `Orders`/`OrderItems`/`Catalog` columns unrelated to this feature); shipping those destructive alters with a subscription feature would be wrong.
- No Blockers: every operation needed by the hero flow exists in the map (rows 1–6). Numeric IDs are never used in wire payloads (handles + references only), so re-seed ID churn is not a blocker.