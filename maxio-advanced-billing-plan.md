# Maxio Advanced Billing plan (eShopOnWeb `src/PublicApi`)

Written by the main agent (the `maxio-advanced-billing-planner` agent is not listed by the subagent tool). SDK source: `sdk/dotnet/` of the maxio plugin (plugin-relative paths below). Behaviour marked *(sandbox)* was observed against the Maxio sandbox.

## 1. Scope & sequence

Plans -> Subscribe -> Read -> Pause/Resume -> Cancel/Restart -> Switch -> Usage -> Admin plan -> Currency price -> Next billing date.
No local persistence: Maxio is the system of record. A shopper is a Maxio customer whose `reference` is the JWT `ClaimTypes.Name`.

## 2. CONTRACT SHEET

> Signatures are generated code, verbatim: an operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's property names.
> Every SDK type is written fully-qualified with the namespace its source path implies (`MaxioAdvancedBilling.Models`, `.Models.Enums`, `.Requests.<Controller>`, `.Errors`, `.Core.Exceptions`).

| Operation | Request record (required) | Body / response | Error | Source |
| --- | --- | --- | --- | --- |
| `Products.ListProducts` | `Requests.Products.ListProductsRequest` (`Page`, `PerPage` ≤200, `IncludeArchived`) | `IReadOnlyList<ProductResponse>` (`.Product`: Id, Name, Handle?, PriceInCents, Interval, IntervalUnit, ArchivedAt, ProductFamily, DefaultProductPricePointId) | B | map/operations/Products.md |
| `Products.ReadProduct` | `ReadProductRequest` (`ProductId`) | `ProductResponse` | B (404) | Products.md |
| `Products.CreateProduct` | `CreateProductRequest` (`ProductFamilyId`; Body) | `Models.CreateOrUpdateProductRequest{Product=CreateOrUpdateProduct{Name, Description, PriceInCents, Interval, IntervalUnit required; Handle opt}}` | A: `TryGetErrorListResponse1` 422 | Products.md |
| `ProductFamilies.ListProductFamilies` / `CreateProductFamily` | `ListProductFamiliesRequest` / `CreateProductFamilyOperationRequest{Body=CreateProductFamilyRequest{ProductFamily=CreateProductFamily{Name req, Handle}}}` | `ProductFamilyResponse.ProductFamily` | A 422 | ProductFamilies.md |
| `ProductPricePoints.CreateProductCurrencyPrices` | `CreateProductCurrencyPricesOperationRequest{ProductPricePointId req, Body=CreateProductCurrencyPricesRequest{CurrencyPrices=[CreateProductCurrencyPrice{Currency, Price:int, Role}]}}` | `CurrencyPricesResponse` | A: `TryGetErrorArrayMapResponse1` 422 | ProductPricePoints.md |
| `Sites.ReadSite` | none | `SiteResponse.Site` (`Currency`, `NonPrimaryCurrencies`) | B | Sites.md |
| `Customers.ReadCustomerByReference` | `ReadCustomerByReferenceRequest{Reference}` | `CustomerResponse.Customer` | B (404 when absent) | Customers.md |
| `Subscriptions.ListSubscriptions` | `ListSubscriptionsRequest{CustomerId, Page, PerPage}` | `IReadOnlyList<SubscriptionResponse>` | B | Subscriptions.md |
| `Subscriptions.CreateSubscription` | `CreateSubscriptionOperationRequest{Body=CreateSubscriptionRequest{Subscription=CreateSubscription{ProductHandle, PaymentCollectionMethod=Remittance, CustomerId \| CustomerAttributes}}}` | `SubscriptionResponse.Subscription` | A 422 `TryGetErrorListResponse1` | Subscriptions.md |
| `Subscriptions.ReadSubscription` | `ReadSubscriptionRequest{SubscriptionId}` | `SubscriptionResponse` (State, BalanceInCents, Customer.Reference, Product, CurrentPeriodEndsAt) | B | Subscriptions.md |
| `Subscriptions.UpdateSubscription` | `UpdateSubscriptionOperationRequest{SubscriptionId; Body=UpdateSubscriptionRequest{Subscription=UpdateSubscription{NextBillingAt \| ProductHandle}}}` | `SubscriptionResponse` | A 422 | Subscriptions.md |
| `SubscriptionStatus.PauseSubscription` / `ResumeSubscription` | `PauseSubscriptionRequest` / `ResumeSubscriptionRequest{SubscriptionId}` | `SubscriptionResponse` | A 422 | SubscriptionStatus.md |
| `SubscriptionStatus.CancelSubscription` / `ReactivateSubscription` | `CancelSubscriptionRequest` / `ReactivateSubscriptionOperationRequest{SubscriptionId}` | `SubscriptionResponse` | A 404/422 / A 422 | SubscriptionStatus.md |
| `SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProductRequest{SubscriptionId; Body=SubscriptionProductMigrationRequest{Migration{ProductHandle}}}` | `SubscriptionResponse` | A 422 | SubscriptionProducts.md |
| `Invoices.ListInvoices` | `ListInvoicesRequest{SubscriptionId, Status, Page, PerPage}` | `ListInvoicesResponse.Invoices` (`DueAmount` string) | B | Invoices.md |
| `SubscriptionComponents.ListSubscriptionComponents` | `ListSubscriptionComponentsRequest{SubscriptionId}` | `IReadOnlyList<SubscriptionComponentResponse>` (`.Component.Kind == ComponentKind.MeteredComponent`, ComponentId, Name, UnitName) | B | SubscriptionComponents.md |
| `SubscriptionComponents.ListUsages` | `ListUsagesRequest{SubscriptionIdOrReference, ComponentId, SinceDate, UntilDate, Page, PerPage ≤200}` | `IReadOnlyList<UsageResponse>` (`.Usage.CreatedAt`, `Quantity: Quantity1` int\|string) | B | SubscriptionComponents.md |

**CROSS-OPERATION INVARIANTS**

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| a plan handle to subscribe/switch to must be one the app offers (non-archived, has a handle) | `CreateSubscription` / `MigrateSubscriptionProduct` / `UpdateSubscription` ← `ListProducts` | `PlanCatalog.FindOfferedPlanAsync` before the write |
| a subscription id must belong to the caller (customer reference = JWT name) | every by-id shopper write/read ← `ReadSubscription` | `SubscriptionOwnership` check before any write |
| a plan's brand must be a catalog brand | `CreateProduct` ← `IRepository<CatalogBrand>.ListAsync` | before `CreateProduct` |
| a currency must be a site currency other than the plan's own | `CreateProductCurrencyPrices` ← `ReadSite` | before the write |

**Sandbox facts** *(sandbox)*: site currency USD, `non_primary_currencies` empty -> currency prices are rejected 422 "Currency must be defined on the site level". Pause on a paused sub, resume on an active one, cancel on a cancelled one -> 422. `MigrateSubscriptionProduct` on a cancelled subscription -> 422, `UpdateSubscription{ProductHandle}` on a cancelled subscription succeeds and puts it on the new plan (used for cancelled). Migrating to the same product -> 422. A remittance subscription is created active with `BalanceInCents` = price and one open invoice (`DueAmount`). `ListUsages` `since_date`/`until_date` compare `created_at` against **site-local** midnight and `created_at` carries the site offset (+05:00 here); `until_date` is inclusive of midnight. Some sandbox products have a null handle. Fractional usage quantities may be stored as integers by Maxio.

Client / auth: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`, `BasicAuth = BasicAuthCredentials{Username=<api key>, Password="x"}`, `Environment = ServerEnvironment.Us|Eu`, `Server.Production.{Us|Eu}.Site` = subdomain, `Server.Production.{Us|Eu}.BaseUrl` = override verbatim.

## 3. Trap notes

- Wire errors surface as `ApiException<TError>`; deserialization drift as `ResponseDeserializationException`; no connection as `SdkConnectionException`/`SdkTimeoutException`. MUST load dotnet-error-handling.
- Retries / timeouts bound a whole call. MUST load dotnet-configuration-resilience.
- Request/model construction (`OpenStringEnum`, `AnyOf` unions). MUST load dotnet-models.

## 4. REQUIRED READING

dotnet-error-handling · all catch ladders; dotnet-configuration-resilience · retries/timeouts; dotnet-client-initialization · DI; dotnet-authentication · BasicAuth; dotnet-models · enums/unions.
Hazard: a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:`; a startup warning names missing keys; every Maxio call fails with 503 and the missing key names until `ApiKey`, `Subdomain` and a valid `Environment` are all present (non-blank). Not a hard startup failure so the existing test host (no Maxio secrets) still boots. |
| 2 | Secret sourcing | .NET user-secrets (`Maxio:ApiKey/Subdomain/Environment`); options built once into a singleton client; rotation needs a restart. |
| 3 | Timeout budget | SDK defaults (`options.Retry`); the HTTP request is also bound by the request's `CancellationToken`. |
| 4 | Write-retry ownership | SDK retries only the verbs in its default `RetryOptions`; no app-level retry. |
| 5 | Idempotency | No real key on any write record; reconciliation: subscribe re-reads the customer's subscriptions before creating, so a retry after an unknown outcome finds the earlier subscription (409). |
| 6 | Observability | Failures logged at Warning with status and Maxio error text; no bodies logged. |
| 7 | Sensitive data | Only names/emails; `LogRequestBody` stays off. |
| 8 | Environment selection | `Maxio:Environment` US/EU selects `Production` group (`Ebb` is not used); `Maxio:BaseUrl` overrides the base of the selected environment verbatim. Sandbox values come from user-secrets. |
| 9 | Duplicate prevention | see DUPLICATE CLAIMS |
| 10 | Partial results | Lists (products, invoices, usages) are paged to exhaustion; none is capped. |
| 11 | Unknown outcomes | see UNKNOWN OUTCOMES |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Subscribe | Blocker: the codebase has no store for a claim that outlives the process; claims are held in-process per user name (`KeyedLock`), and the durable record is Maxio itself (an existing live subscription to the plan -> 409) | the per-user lock serializes in this process; the Maxio read rejects the repeat | `SubscriptionService.SubscribeAsync` throws `BillingException.Conflict` (409) | `KeyedLock.AcquireAsync`, then `ListCustomerSubscriptionsAsync` (`Subscriptions.ListSubscriptions`), then `Subscriptions.CreateSubscription`, all in `SubscriptionService.SubscribeAsync` |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| products, invoices, usages | per_page ≤ 200 | not capped: every page is read until a short page | `PlanCatalog.ListOfferedPlansAsync`, `SubscriptionService.OwesMoneyAsync`, `SubscriptionService.GetUsageAsync` |

**UNKNOWN OUTCOMES**

| Write | Re-read with | Reference | Where in the code | Test that fails the connection |
| --- | --- | --- | --- | --- |
| Subscribe | `Subscriptions.ListSubscriptions` | customer reference + plan handle + created-at >= call start | `SubscriptionService.SubscribeAsync` catch of 502/504 `BillingException` -> `FindJustCreatedAsync` | no connection-failing test: the SDK has no seam to fail the live call without stubbing Maxio; covered by code review only |

## 6. Assumptions & Blockers

- The sandbox site has no non-primary currency, so a currency price is expected to be refused with a 4xx reason until the site enables one.
- Brand -> Maxio product family: the family named `<brand>` or `<brand> plans` (as in the sandbox); created as `<brand> plans` if absent.
- "Day" for usage is the site-local day (the offset Maxio returns on `created_at`).
- Currency `Price` is a whole number of major units (SDK type `int`).
