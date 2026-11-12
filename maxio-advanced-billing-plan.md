# Maxio Advanced Billing plan — eShopOnWeb PublicApi

SDK source (plugin-relative): `sdk/dotnet/` (maxio 0.2.2). Written by the main agent (planner agent not listed by the host).
Binding keys: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment`, `Maxio:BaseUrl` (optional verbatim override).

## 1. Scope & sequence
1. Plans list → `Products.ListProducts` (non-archived) · 2. Subscribe → `Customers.ReadCustomerByReference`/`CreateCustomer`, `Subscriptions.ListSubscriptions`, `Subscriptions.CreateSubscription`
3. Page → `Subscriptions.ReadSubscription` · 4. Pause/resume → `SubscriptionStatus.PauseSubscription`/`ResumeSubscription`
5. Cancel/restart → `SubscriptionStatus.CancelSubscription`/`ReactivateSubscription` · 6. Switch → `SubscriptionProducts.MigrateSubscriptionProduct` (active/trialing), `Subscriptions.UpdateSubscription` (+`ReactivateSubscription` for canceled)
7. Usage → `SubscriptionComponents.ListSubscriptionComponents`, `ListUsages` · 8. New plan → `ProductFamilies.ListProductFamilies`/`CreateProductFamily`, `Products.CreateProduct`
9. Currency price → `ProductPricePoints.CreateProductCurrencyPrices` · 10. Next billing → `Subscriptions.UpdateSubscription` (`next_billing_at`)

## 2. CONTRACT SHEET
⚠ Signatures are generated code, verbatim: each operation takes ONE request record as first parameter, built with an object initializer using the record's own property names.
⚠ Every SDK type is written fully-qualified with the namespace its source path implies (`MaxioAdvancedBilling.Models`, `.Models.Enums`, `.Requests.<Controller>`, `.Errors`, `.Core.Exceptions`, `.Core.ErrorResponse`).

| Op | Request record (required) | Body | Response | Error | Source |
| --- | --- | --- | --- | --- | --- |
| Products.ListProducts | `ListProductsRequest` (none; Page, PerPage≤200, IncludeArchived) | – | `IReadOnlyList<ProductResponse>` (.Product: Id, Name, Handle, Description, PriceInCents, Interval, IntervalUnit, ArchivedAt, DefaultProductPricePointId, ProductFamily) | B | map/operations/Products.md |
| ProductFamilies.ListProductFamilies | `ListProductFamiliesRequest` | – | `IReadOnlyList<ProductFamilyResponse>` (.ProductFamily: Id, Name, Handle, ArchivedAt) | B | ProductFamilies.md |
| ProductFamilies.CreateProductFamily | `CreateProductFamilyOperationRequest` (Body) | `CreateProductFamilyRequest.ProductFamily{Name req, Handle, Description}` | `ProductFamilyResponse` | A: ErrorListResponse1 (422) | ProductFamilies.md |
| Products.CreateProduct | `CreateProductRequest` (ProductFamilyId string; Body) | `CreateOrUpdateProductRequest.Product{Name, Description, PriceInCents, Interval, IntervalUnit req; RequireCreditCard, Handle}` | `ProductResponse` | A: ErrorListResponse1 (422) | Products.md |
| ProductPricePoints.CreateProductCurrencyPrices | `CreateProductCurrencyPricesOperationRequest` (ProductPricePointId; Body) | `CreateProductCurrencyPricesRequest.CurrencyPrices[{Currency, Price:int, Role}]` | `CurrencyPricesResponse` | A: ErrorArrayMapResponse1 (422) | ProductPricePoints.md |
| Customers.ReadCustomerByReference | `ReadCustomerByReferenceRequest` (Reference) | – | `CustomerResponse.Customer` | B (404 if none) | Customers.md |
| Customers.CreateCustomer | `CreateCustomerOperationRequest` (Body) | `CreateCustomerRequest.Customer{FirstName, LastName, Email req; Reference}` | `CustomerResponse` | A: CustomerErrorResponse1 (422) | Customers.md |
| Subscriptions.ListSubscriptions | `ListSubscriptionsRequest` (CustomerId, Page, PerPage) | – | `IReadOnlyList<SubscriptionResponse>` | B | Subscriptions.md |
| Subscriptions.CreateSubscription | `CreateSubscriptionOperationRequest` (Body) | `CreateSubscriptionRequest.Subscription{ProductHandle, CustomerId, PaymentCollectionMethod}` | `SubscriptionResponse.Subscription` | A: ErrorListResponse1 (422) | Subscriptions.md |
| Subscriptions.ReadSubscription | `ReadSubscriptionRequest` (SubscriptionId) | – | `SubscriptionResponse` (State, BalanceInCents, Product, Customer.Reference, CurrentPeriodEndsAt) | B | Subscriptions.md |
| Subscriptions.UpdateSubscription | `UpdateSubscriptionOperationRequest` (SubscriptionId; Body) | `UpdateSubscriptionRequest.Subscription{NextBillingAt, ProductHandle}` | `SubscriptionResponse` (no next_billing_at: read CurrentPeriodEndsAt) | A: ErrorListResponse1 (422) | Subscriptions.md |
| SubscriptionStatus.Pause/Resume/Reactivate | `…Request` (SubscriptionId) | – | `SubscriptionResponse` | A: ErrorListResponse1 (422) | SubscriptionStatus.md |
| SubscriptionStatus.CancelSubscription | `CancelSubscriptionRequest` (SubscriptionId; Body) | `CancellationRequest.Subscription{CancellationMessage}` | `SubscriptionResponse` | A: 404 raw, 422 CancelSubscriptionErrorResponse | SubscriptionStatus.md |
| SubscriptionProducts.MigrateSubscriptionProduct | `MigrateSubscriptionProductRequest` (SubscriptionId; Body) | `SubscriptionProductMigrationRequest.Migration{ProductHandle}` | `SubscriptionResponse` | A: ErrorListResponse1 (422) | SubscriptionProducts.md |
| SubscriptionComponents.ListSubscriptionComponents | `ListSubscriptionComponentsRequest` (SubscriptionId) | – | `IReadOnlyList<SubscriptionComponentResponse>.Component` (ComponentId, Name, Kind, UnitName) | B | SubscriptionComponents.md |
| SubscriptionComponents.ListUsages | `ListUsagesRequest` (SubscriptionIdOrReference, ComponentId; SinceDate, UntilDate, Page, PerPage≤200) | – | `IReadOnlyList<UsageResponse>.Usage` (CreatedAt, Quantity: int\|string) | B | SubscriptionComponents.md |

Enums: `SubscriptionState` (Active, Trialing, OnHold, Canceled, PastDue…), `CollectionMethod.Remittance`, `IntervalUnit.Month`, `CurrencyPriceRole.Baseline`.
Client: `MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; Basic auth (`BasicAuth{Username=ApiKey, Password="x"}`); `Environment` Us/Eu; `Server.Production.Us/Eu.{BaseUrl,Site}`.

**CROSS-OPERATION INVARIANTS**

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| a plan handle accepted by subscribe/switch must be one the plans list offers (non-archived) | `CreateSubscription`/`Migrate`/`Update` ← `ListProducts` | `BillingService.RequireOfferedPlanAsync` before the write (+ `SwitchPlanAsync`) |
| a plan id given to currency-price endpoint must be an offered plan | `CreateProductCurrencyPrices` ← `ListProducts` | `BillingService.AddCurrencyPriceAsync` |
| a brand given for a new plan must be in the shop catalog | `CreateProduct` ← catalog brands (`IRepository<CatalogBrand>`) | `CreatePlanEndpoint` before service call |
| a subscription id acted on by a shopper must belong to them | all subscription ops ← `ReadSubscription` (Customer.Reference == JWT name) | `BillingService.LoadOwnedAsync` |

## 3. Trap notes
- Currency price `Price` is typed `int` by the SDK → fractional prices can't be expressed. MUST load dotnet-models.
- Usage date filters: `until_date` is exclusive midnight, site-local. MUST load dotnet-calling-endpoints.
- `next_billing_at` update returns no such field; verify by `CurrentPeriodEndsAt`. MUST load dotnet-calling-endpoints.
- Timeout is per attempt; POST is not retried. MUST load dotnet-configuration-resilience.

## 4. REQUIRED READING
dotnet-error-handling · all catch ladders; dotnet-client-initialization · client; dotnet-authentication · Basic auth; dotnet-calling-endpoints · request records; dotnet-models · AnyOf/Quantity; dotnet-configuration-resilience · retries/timeouts; dotnet-testing · tests.
Hazard row: a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS
| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Settings bound from `Maxio:` section; client built lazily on first use (existing test host has no secrets). Missing/blank ApiKey/Subdomain/Environment → `BillingException` 503 with the key name; startup logs an error when unset. YOUR CALL — implemented in `MaxioClientProvider`. |
| 2 | Secret sourcing | User-secrets / config; options built once in the singleton provider; rotation needs restart. |
| 3 | Timeout budget | `Retry.Timeout` 20s per attempt, request `CancellationToken` passed; reads/PUT retry up to 3× (worst case ≈ 90s), POSTs never retried. |
| 4 | Write-retry | SDK retries only GET/HEAD/PUT/OPTIONS; POST (subscribe, pause, resume, migrate, create product) never resent by SDK; PUT `UpdateSubscription` (idempotent absolute values) may be resent. |
| 5 | Idempotency | No real key on any request record; `Idempotency-Key` header not relied on. Reconciliation by re-reading. |
| 6 | Observability | Warnings logged with status + Maxio error text; request bodies not logged. |
| 7 | Sensitive data | No card data touched; `LogRequestBody` off, `LoggerFactory` set from DI. |
| 8 | Environment | `Maxio:Environment` US/EU selects `Production` server group (only group used; `Ebb` untouched); `Maxio:BaseUrl` replaces both Production base URLs verbatim. |
| 9 | Duplicate prevention | see DUPLICATE CLAIMS. |
| 10 | Partial results | All lists paged to exhaustion (page size 200); no cap. |
| 11 | Unknown outcomes | see UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**
| Write | Where stored | What rejects the second | Where caught | Where in the code |
| --- | --- | --- | --- | --- |
| Subscribe (same shopper + plan) | no DB for billing in this codebase; per-shopper in-process `SemaphoreSlim` claim, then re-check of live subscriptions | second caller waits, then sees the live subscription → 409 | `BillingException` → `ExceptionMiddleware` | `BillingService.SubscribeAsync` (claim: `ShopperLocks.GetOrAdd(...).WaitAsync`; SDK call: `Client.Subscriptions.CreateSubscription`) |

Limit: the claim does not outlive the process or span instances (no shared store exists in this codebase; Blocker-level for multi-instance deployments, accepted here).

**PAGED READS**
| Read | Cap | Cut-short signal | Where |
| --- | --- | --- | --- |
| products, subscriptions, usages | none (walk until a short page) | n/a — complete or exception | `BillingService.ListOfferedProductsAsync`, `ListCustomerSubscriptionsAsync`, `GetUsageAsync` |

**UNKNOWN OUTCOMES**
| Write | Re-read with | Reference | Where | Test |
| --- | --- | --- | --- | --- |
| Subscribe | `ListSubscriptions` by customer | customer + product handle created after the call started | `BillingService.SubscribeAsync` catch `SdkConnectionException` | `BillingEndpointsTest.ShopperCanSubscribeManageAndSwitch` exercises the flow; a dropped connection is not simulated against the sandbox |
| Pause/resume/cancel/restart/switch | `ReadSubscription` | expected state / product | `BillingService.MutateAsync` | same |

## 6. Assumptions & Blockers
- Brand = Maxio product family whose name equals the catalog brand name (created on demand).
- Shoppers pay by `remittance` collection (invoice; no card needed) — sandbox site is Relationship Invoicing.
- The sandbox site has only USD defined; the currency endpoint relays Maxio's 4xx for others.
- Customer reference = JWT name claim; a Maxio customer is created per shopper on first subscribe.

## 7. Decisions made in code (YOUR CALL entries settled)
- Credential fail-fast: lazy — `MaxioClientProvider.GetClient` throws `BillingException` 503 naming the missing `Maxio:` key; `Program.cs` logs an error at startup. (Existing tests run without secrets.)
- Subscribe collection method `remittance` (no card): `BillingService.SubscribeAsync`. Same plan twice while live → 409.
- `paid` = `balance_in_cents <= 0`: `BillingService.ToSubscription`. Verified live (flips true after a payment is recorded).
- Switch: active/trialing → `MigrateSubscriptionProduct`; canceled → `UpdateSubscription` product change then `ReactivateSubscription` (undo on failure); other states → product change in place: `BillingService.SwitchPlanAsync`.
- Usage: metered components only, window widened and filtered to the site-local day by `CreatedAt`: `BillingService.GetUsageAsync`.
- Plans without a handle are not offered; new plans get `Handle = Slug(name)`: `CreatePlanAsync`.
- Currency price: whole units only (SDK `int`); plans with trial/setup fee refused: `AddCurrencyPriceAsync`. Sandbox site defines only USD, so other currencies are refused by Maxio with a 422 reason.
- Next billing date: only active/trialing; date sent at noon in the site's offset; confirmed via `CurrentPeriodEndsAt`: `SetNextBillingDateAsync`.
