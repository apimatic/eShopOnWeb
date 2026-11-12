# Maxio Advanced Billing plan — eShopOnWeb subscription billing

SDK source (plugin-relative): `sdk/dotnet/` · root namespace `MaxioAdvancedBilling`. Planner agent was not listed in this session, so the plan is written by the main agent (per `dotnet-integrate-maxio-advanced-billing`, case 2).

## 1. Scope & sequence

| # | Feature | Operations used |
| --- | --- | --- |
| 1 | `GET /api/plans` | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 2 | `POST /api/subscriptions` | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.CreateSubscription` |
| 3 | `GET /api/subscriptions/{id}` | `Subscriptions.ReadSubscription` (ownership check by customer reference) |
| 4 | pause / resume | `SubscriptionStatus.PauseSubscription`, `SubscriptionStatus.ResumeSubscription` |
| 5 | cancel / restart | `SubscriptionStatus.CancelSubscription`, `SubscriptionStatus.ReactivateSubscription` |
| 6 | switch | `SubscriptionProducts.MigrateSubscriptionProduct` (+ `ReactivateSubscription` first for a cancelled one) |
| 7 | usage for a day | `SubscriptionComponents.ListSubscriptionComponents`, `SubscriptionComponents.ListUsages` |
| 8 | `POST /api/admin/plans` | `ProductFamilies.ListProductFamilies/CreateProductFamily`, `Products.CreateProduct` |
| 9 | currency price | `Products.ReadProduct`, `ProductPricePoints.CreateProductCurrencyPrices` |
| 10 | next billing date | `Subscriptions.UpdateSubscription` |

Identity: JWT carries only `ClaimTypes.Name` (user name) + `ClaimTypes.Role` (`IdentityTokenClaimService`). The Maxio customer `reference` = JWT user name (unique per site — rule `createCustomer.1`). A subscription belongs to the caller iff `subscription.customer.reference == user name`. Maxio is the system of record; the app persists nothing.

Plan model: plan = Maxio product; plan `planId` = product id; handle = product handle. A plan belongs to a product family bound to a catalog brand (family handle `brand-{CatalogBrandId}`, name = brand name; created on demand by feature 8).

## 2. CONTRACT SHEET

> Signatures are generated code, verbatim: each operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names — never flat arguments.
> Every SDK type is written fully-qualified with the namespace its source path implies (`MaxioAdvancedBilling.Models`, `.Models.Enums`, `.Requests.<Controller>`, `.Errors`, `.Core.Exceptions`, `.Core.ErrorResponse`), taken from that type's own path.

| Operation | Request record (members) | Body model → response | Error | Source |
| --- | --- | --- | --- | --- |
| `client.ProductFamilies.ListProductFamilies` | `Requests.ProductFamilies.ListProductFamiliesRequest` (all optional) | → `IReadOnlyList<ProductFamilyResponse>` (`.ProductFamily?` : `Id?`,`Name?`,`Handle?`,`ArchivedAt?`) | B | map ProductFamilies.md |
| `client.ProductFamilies.CreateProductFamily` | `CreateProductFamilyOperationRequest { Body: CreateProductFamilyRequest? }` | `CreateProductFamilyRequest{ ProductFamily: CreateProductFamily (required){ Name req, Handle?, Description? } }` → `ProductFamilyResponse` | A, `TryGetErrorListResponse1` 422 | map ProductFamilies.md; Models/CreateProductFamily.cs |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamilyRequest { ProductFamilyId: string req, Page=1, PerPage=20 (max 200), IncludeArchived?: bool }` | → `IReadOnlyList<ProductResponse>` (`.Product`: `Id?`,`Name?`,`Handle?`,`PriceInCents?`,`Interval?`,`IntervalUnit?`,`ArchivedAt?`,`DefaultProductPricePointId?`,`ProductFamily?`) | A, `TryGetString` 404 | map ProductFamilies.md; Models/Product.cs |
| `client.Products.CreateProduct` | `CreateProductRequest { ProductFamilyId: string req, Body: CreateOrUpdateProductRequest? }` | `CreateOrUpdateProductRequest{ Product: CreateOrUpdateProduct req { Name req, Handle?, Description req, PriceInCents: long req, Interval: int req, IntervalUnit: IntervalUnit req } }` → `ProductResponse` | A, `TryGetErrorListResponse1` 422 | map Products.md; Models/CreateOrUpdateProduct.cs |
| `client.Products.ReadProduct` | `ReadProductRequest { ProductId: req }` | → `ProductResponse` | B | map Products.md |
| `client.ProductPricePoints.CreateProductCurrencyPrices` | `CreateProductCurrencyPricesOperationRequest { ProductPricePointId: int req, Body: CreateProductCurrencyPricesRequest? }` | `{ CurrencyPrices: IReadOnlyList<CreateProductCurrencyPrice> req { Currency: string req, Price: **int** req, Role: CurrencyPriceRole req (Baseline/Trial/Initial) } }` → `CurrencyPricesResponse` | A, `TryGetErrorArrayMapResponse1` 422 | map ProductPricePoints.md; Models/CreateProductCurrencyPrice.cs |
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReferenceRequest { Reference: string req }` | → `CustomerResponse { Customer { Id?, Reference? } }` | B | map Customers.md |
| `client.Customers.CreateCustomer` | `CreateCustomerOperationRequest { Body: CreateCustomerRequest? }` | customer attrs (first/last name, email, reference) → `CustomerResponse` | A, `TryGetCustomerErrorResponse1` 422 | map Customers.md |
| `client.Subscriptions.CreateSubscription` | `CreateSubscriptionOperationRequest { Body: CreateSubscriptionRequest? }` | `{ Subscription: CreateSubscription req { ProductHandle?, ProductId?, CustomerId?, PaymentCollectionMethod: CollectionMethod? } }` → `SubscriptionResponse` | A, `TryGetErrorListResponse1` 422 | map Subscriptions.md; Models/CreateSubscription.cs |
| `client.Subscriptions.ReadSubscription` | `ReadSubscriptionRequest { SubscriptionId: int req }` | → `SubscriptionResponse { Subscription? { Id?, State?, BalanceInCents?, Customer?{Reference?}, Product?, CurrentPeriodEndsAt?, ... } }` | B | map Subscriptions.md; Models/Subscription.cs |
| `client.Subscriptions.UpdateSubscription` | `UpdateSubscriptionOperationRequest { SubscriptionId: int req, Body: UpdateSubscriptionRequest? }` | `{ Subscription: UpdateSubscription req { NextBillingAt: DateTimeOffset? } }` → `SubscriptionResponse` | A, 422 | map Subscriptions.md; Models/UpdateSubscription.cs |
| `client.SubscriptionStatus.PauseSubscription` | `PauseSubscriptionRequest { SubscriptionId: int req, Body: PauseRequest? { Hold: AutoResume? } }` | → `SubscriptionResponse` | A, 422 | map SubscriptionStatus.md |
| `client.SubscriptionStatus.ResumeSubscription` | `ResumeSubscriptionRequest { SubscriptionId: int req }` | → `SubscriptionResponse` | A, 422 | map SubscriptionStatus.md |
| `client.SubscriptionStatus.CancelSubscription` | `CancelSubscriptionRequest { SubscriptionId: int req, Body: CancellationRequest? { Subscription: CancellationOptions req } }` | → `SubscriptionResponse` | A, `TryGetNoContent` 404, `TryGetCancelSubscriptionErrorResponse` 422 | map SubscriptionStatus.md |
| `client.SubscriptionStatus.ReactivateSubscription` | `ReactivateSubscriptionOperationRequest { SubscriptionId: int req, Body: ReactivateSubscriptionRequest? { Resume?, IncludeTrial?, PreserveBalance? } }` | → `SubscriptionResponse` | A, 422 | map SubscriptionStatus.md |
| `client.SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProductRequest { SubscriptionId: int req, Body: SubscriptionProductMigrationRequest? { Migration: SubscriptionProductMigration req { ProductId?, ProductHandle?, PreservePeriod? } } }` | → `SubscriptionResponse` | A, 422 | map SubscriptionProducts.md |
| `client.SubscriptionComponents.ListSubscriptionComponents` | `ListSubscriptionComponentsRequest { SubscriptionId: int req, InUse?: bool }` (no page param) | → `IReadOnlyList<SubscriptionComponentResponse>` (`.Component?`: `ComponentId?`,`ComponentHandle?`,`Name?`,`Kind?: ComponentKind`,`UnitName?`) | A/B per map | map SubscriptionComponents.md |
| `client.SubscriptionComponents.ListUsages` | `ListUsagesRequest { SubscriptionIdOrReference req, ComponentId: ComponentIdModel req, SinceDate?, UntilDate?, Page=1, PerPage=20 (max 200) }` | → `IReadOnlyList<UsageResponse { Usage { Id?, Quantity?: Quantity1 (int\|string), Memo?, CreatedAt?, ComponentId?, ComponentHandle? } }>` | B | map SubscriptionComponents.md; Models/Usage.cs |

Enums used: `CollectionMethod.{Automatic,Remittance,Prepaid,Invoice}`; `SubscriptionState.{Active,Trialing,Canceled,OnHold,PastDue,Unpaid,TrialEnded,Expired,...}`; `IntervalUnit.{Day,Month}`; `CurrencyPriceRole.{Baseline,Trial,Initial}`.

Client / auth / server facts: `new MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; Basic auth, username = API key, password = `x`; `ServerEnvironment.{Us,Eu}` in `MaxioAdvancedBilling.Servers`; server group `Production` templates `https://{site}.chargify.com` (Us) / `https://{site}.ebilling.maxio.com` (Eu), `{site}` via `options.Server.Production.<Env>.Site`, whole-URL override via `.BaseUrl`.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| A plan handle given to subscribe / switch must be one the app offers (an unarchived product in a brand family) | `CreateSubscription`, `MigrateSubscriptionProduct` ← `ListProductFamilies` + `ListProductsForProductFamily` | `PlanCatalog.FindOfferedPlanByHandleAsync`, before the write |
| A brand given for a new plan must be a catalog brand of this shop | `CreateProductFamily`/`CreateProduct` ← local `CatalogBrand` repository | endpoint, before any Maxio write |
| A subscription id acted on must belong to the caller | all `/api/subscriptions/{id}…` ← `ReadSubscription` (customer.reference) | `SubscriptionOwnership.LoadOwnedAsync`, before the write |
| A plan id given for a currency price must be an offered plan | `CreateProductCurrencyPrices` ← `ListProductFamilies`+`ListProductsForProductFamily` | `PlanCatalog.FindOfferedPlanByIdAsync` |

## 3. Trap notes

- Retry ownership: POST/DELETE are never auto-resent; PUT is. `MUST load dotnet-configuration-resilience`.
- Total time: `Timeout` bounds one attempt only. `MUST load dotnet-configuration-resilience`.
- Typed vs raw errors, `ResponseDeserializationException`. `MUST load dotnet-error-handling`.
- Page loops over `ListProducts…`/`ListUsages` must be bounded and a cut-short result must be visible. `MUST load dotnet-configuration-resilience`.
- Non-throwing 2xx with drifted shape. `MUST load dotnet-models`.
- Business rules applied: `createCustomer.1` (reference unique), `migrateSubscriptionProduct` (only active/trialing, target must differ, preserve period), `reactivateSubscription` (only Canceled/Unpaid/TrialEnded), `resumeSubscription.1` (only on-hold), `pauseSubscription.1/2` (>24h before billing, eligible), `cancelSubscription.6` (already cancelled), `updateSubscription.10` (impossible dates roll over silently → validate), `createProductCurrencyPrices.1-3`, `listUsages` (until_date inclusive of midnight → filter by created_at).

## 4. REQUIRED READING (loaded before implementation)

- maxio:dotnet-client-initialization · client + DI
- maxio:dotnet-authentication · Basic auth + fail-fast
- maxio:dotnet-calling-endpoints · every call
- maxio:dotnet-models · AnyOf/Quantity, enums
- maxio:dotnet-error-handling · boundary
- maxio:dotnet-configuration-resilience · retries, timeouts, paging
- maxio:dotnet-testing · tests

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bound from `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` (+ optional `Maxio:BaseUrl`) via an options class with `ValidateOnStart`; host refuses to start if ApiKey/Subdomain/Environment is blank or Environment is not US/EU. Implemented: `MaxioSettings` + `AddMaxioBilling`. |
| 2 | Secret sourcing & rotation | Values come from `IConfiguration` (user-secrets / env). `MaxioAdvancedBillingClientOptions` built once at registration and captured by the singleton; rotation needs a restart. |
| 3 | Total timeout budget | Per-attempt `Retry.Timeout` = 15 s; GET/PUT retries `MaxRetries` = 2 with 1 s backoff, so worst case on a GET ≈ 3×15+1+2 s ≈ 48 s; POST/DELETE single attempt ≈ 15 s. Multi-call endpoints (subscribe, switch, usage) sum these. Enforced in `MaxioClientRegistration`. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` (GET, HEAD, PUT, OPTIONS) kept; SDK never resends POST/DELETE. The only PUT write is `UpdateSubscription` (next billing date), idempotent by value. |
| 5 | Idempotency & ambiguous writes | No write record carries a real idempotency key (`Idempotency-Key` header is not one). Customer: natural key = `reference` (unique in Maxio). Subscription create: no key → reconciliation by listing the customer's subscriptions. Others are state transitions that are naturally repeatable/refused by Maxio (422). |
| 6 | Observability | Warnings with Maxio status + error summary on any `ApiException`; Maxio's error body is summarised into the 4xx/5xx reason; no request bodies logged. |
| 7 | Sensitive data | Scope carries customer email/name only, no card data; `LogRequestBody` off, `LoggerFactory` set explicitly to the host's. |
| 8 | Environment selection | Server group `Production` only (Ebb events server not used). `Maxio:Environment` picks Us/Eu; `Maxio:Subdomain` fills `{site}`; `Maxio:BaseUrl`, when set, is applied verbatim to the selected environment's `Production` BaseUrl. Sandbox site is used for tests; nothing in the repo names a live site. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Create customer (per user) | Maxio (system of record; `reference` unique per site, rule `createCustomer.1`) | Maxio's 422 on duplicate reference | `MaxioCustomers.EnsureCustomerIdAsync` catch of `SdkException`, which re-reads by reference | claim = `MaxioCustomers.EnsureCustomerIdAsync` (create with unique `reference`) → SDK call `client.Customers.CreateCustomer`; settle in the same member via `FindCustomerIdAfterFailureAsync` |
| Create subscription | none — a shopper may hold several subscriptions, even to the same plan, so a second request is legitimate. The app has no store for claims that outlive the request and a unique key would reject legitimate repeats. | n/a | n/a | n/a (decision: not a duplicate) |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| Products of a family | 50 pages × 200 | `PlanCatalog` throws `MaxioIncompleteResultException` → endpoint answers 502 "plan list incomplete" (never a silent partial list) | `PlanCatalog.ListProductsAsync` (throws `MaxioIncompleteResultException`) |
| Usage for a component | 50 pages × 200 | same exception → 502 | `SubscriptionBilling.ReadDayAsync` (throws `MaxioIncompleteResultException`) |

**UNKNOWN OUTCOMES**

| Write | Re-read with | Reference | Where in the code | Test that fails the connection |
| --- | --- | --- | --- | --- |
| Create customer | `ReadCustomerByReference` | user name | `MaxioCustomers.EnsureCustomerIdAsync` catch → `FindCustomerIdAfterFailureAsync` | `MaxioSettingsAndFailureTest.ALostConnectionIsAnsweredAsBadGatewayWithTheOutcomeNamed` |
| Create subscription | `ListSubscriptions` filtered by `CustomerId` | customer id + plan id, created after request start | `SubscriptionBilling.SubscribeAsync` catch of `SdkConnectionException` → `FindCreatedSubscriptionAsync` | same connection-failure test (client with unreachable base address) |
| Other writes (state transitions, price, billing date) | `ReadSubscription` / `ReadProduct` | subscription / product id | `SubscriptionBilling.WriteAsync` catch of `SdkConnectionException` re-reads the subscription and returns what Maxio holds; otherwise `MaxioErrors.Map(..., writeMayHaveLanded: true)` answers 502/504 naming the unknown outcome | same test |

## 6. Assumptions & Blockers

- Assumption (verified live): a subscription without card details uses `PaymentCollectionMethod = Remittance`; Maxio creates an open invoice, `balance_in_cents` = amount owed.
- Assumption (verified live): `paid` is `BalanceInCents <= 0`. Recording a payment against the subscription's invoice dropped `balance_in_cents` 1250 → 0; a null balance is a 502.
- Assumption: a usage "day" is the Maxio site's day. Verified live that `created_at` carries the site's UTC offset; usage is read with `since_date=D`, `until_date=D+1` and rows are kept by the local date of `created_at`.
- No Blockers.

### Decisions (formerly `YOUR CALL — not in the map`) and where they live

- Plan = Maxio product in the family with handle `catalog-brand-{CatalogBrandId}` (name = brand name) — `PlanCatalog.FamilyHandle`; plans without a handle or archived are not offered (`PlanCatalog.ToPlan`).
- Identity: customer `reference` = JWT name claim; ownership = `customer.reference` equals the caller — `MaxioCustomers`, `SubscriptionBilling.LoadOwnedAsync`; a foreign subscription answers 404.
- Status mapping: invalid input 400; unknown referenced thing 404; state conflicts 409; Maxio 422 passed through as 422 with Maxio's reason; Maxio 5xx / unreachable 502; timeout 504 — `MaxioErrors.Map`, `ExceptionMiddleware`.
- Cancel is immediate (no schedule options); restart = `ReactivateSubscription` with `resume: true` — `SubscriptionBilling.CancelAsync/RestartAsync`.
- Switch: live subscription → `MigrateSubscriptionProduct` with `PreservePeriod = true` (falls back to false on Maxio's "not eligible for a prorated migration"); cancelled subscription → `UpdateSubscription` to the new product, then reactivate — `SubscriptionBilling.SwitchPlanAsync`; the result is checked to be on the target plan.
- Next billing date: only Active/Trialing, strictly future, keeps the time of day and UTC offset of the current billing moment, verified by re-reading — `SubscriptionBilling.SetNextBillingDateAsync`.
- Currency price: baseline role only, whole currency units (SDK `int`), 3-letter code; Maxio's refusal (e.g. "Currency must be defined on the site level" — the sandbox site has no non-primary currency) is passed on as a 4xx reason — `PlanCatalog.AddCurrencyPriceAsync`.
- Settings: `Maxio:ApiKey/Subdomain/Environment/BaseUrl`; host start fails naming the key — `MaxioSettingsValidator`; `Maxio:BaseUrl` is applied verbatim to the selected environment's Production base URL — `AddMaxioBilling`.
- `GET /api/plans` is anonymous (like the catalog endpoints); everything else needs a JWT, `/api/admin/*` the `Administrators` role.

## 7. Source labels

UNVERIFIED: that Maxio's currency price `price` is in whole major units (the SDK types it `int`; the sandbox site defines no second currency, so a success could not be observed).
