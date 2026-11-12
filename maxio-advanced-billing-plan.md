# Maxio Advanced Billing plan — eShopOnWeb `src/PublicApi`

Written by the main agent (the `maxio:maxio-advanced-billing-planner` agent was not listed in this session). SDK: `sdk/dotnet/` in the maxio plugin, referenced by `<ProjectReference>` from `src/PublicApi/PublicApi.csproj` (local to this machine — re-point it with `dotnet remove reference` / `dotnet add reference` where the plugin lives elsewhere). Sandbox facts marked **PROBED** were observed against the Maxio sandbox site during planning.

## 1. Scope & sequence

| # | Feature | Operations | Notes |
| --- | --- | --- | --- |
| 1 | `GET /api/plans` | `Products.ListProducts`, `ProductFamilies.ListProductFamilies` | A plan = Maxio product in the product family of a catalog brand (family handle `catalog-brand-{brandId}`, name = brand). Archived products excluded. |
| 2 | `POST /api/subscriptions` | `Customers.ReadCustomerByReference`, `Subscriptions.CreateSubscription`, `Sites.ReadSite` | Customer reference = JWT user name. No card: `payment_collection_method` = `remittance` (Relationship-Invoicing site, **PROBED**) or `invoice` (statement site). |
| 3 | `GET /api/subscriptions/{id}` | `Subscriptions.ReadSubscription`, `Invoices.ListInvoices` | Ownership by `customer.reference`; `paid` = balance 0 and no open invoice with due amount. |
| 4 | pause / resume | `SubscriptionStatus.PauseSubscription`, `ResumeSubscription` | |
| 5 | cancel / restart | `SubscriptionStatus.CancelSubscription`, `ReactivateSubscription` | Immediate cancel (no schedule options: site has scheduled cancellation disabled). |
| 6 | switch | `SubscriptionProducts.MigrateSubscriptionProduct` (active/trialing), `Subscriptions.UpdateSubscription` (any other state) | Migration of a canceled subscription is refused 422 (**PROBED**); UpdateSubscription with `product_handle` changes it at once (**PROBED**). |
| 7 | usage for a day | `SubscriptionComponents.ListSubscriptionComponents`, `ListUsages` | Metered + prepaid components only (usages of others are refused). Day filtered locally by the record's own offset (site time zone). |
| 8 | `POST /api/admin/plans` | `ProductFamilies.ListProductFamilies/CreateProductFamily`, `Products.CreateProduct` | |
| 9 | currency price | `Products.ReadProduct`, `ProductPricePoints.ReadProductPricePoint/CreateProductCurrencyPrices/UpdateProductCurrencyPrices`, `Sites.ReadSite` | Sandbox site has no extra currency defined (**PROBED**): the call is refused 422 and that reason is returned. |
| 10 | next billing date | `Subscriptions.UpdateSubscription` (`next_billing_at`) | Verified through `current_period_ends_at` (rule `updateSubscription`: response carries no `next_billing_at`). |

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim: each operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names — never flat arguments.
⚠ Every SDK type is written fully-qualified with the namespace its source path implies (`MaxioAdvancedBilling.Models`, `.Models.Enums`, `.Requests.<Controller>`, `.Core.Exceptions`, `.Core.ErrorResponse`), taken from the path the map gives for THAT type.

| Controller.method | Request record (required) | Body → response | Error case | Source |
| --- | --- | --- | --- | --- |
| `Products.ListProducts` | `ListProductsRequest` (none; `Page`, `PerPage` ≤200, `IncludeArchived`) | `IReadOnlyList<ProductResponse>` (`Product`: `Id, Name, Handle, PriceInCents, Interval, IntervalUnit, ArchivedAt, ProductFamily{Id,Handle}, DefaultProductPricePointId`) | B | map/operations/Products.md |
| `Products.CreateProduct` | `CreateProductRequest` (`ProductFamilyId: string`; `Body`) | `CreateOrUpdateProductRequest{Product: CreateOrUpdateProduct{Name, Description, PriceInCents, Interval, IntervalUnit required}}` → `ProductResponse` | A, `TryGetErrorListResponse1` 422 | Models/CreateOrUpdateProduct.cs |
| `Products.ReadProduct` | `ReadProductRequest` (`ProductId`) | `ProductResponse` | B | |
| `ProductFamilies.ListProductFamilies` / `CreateProductFamily` | `ListProductFamiliesRequest` / `CreateProductFamilyOperationRequest{Body: CreateProductFamilyRequest{ProductFamily{Name required, Handle}}}` | `IReadOnlyList<ProductFamilyResponse>` / `ProductFamilyResponse` | B / A 422 | |
| `Customers.ReadCustomerByReference` | `ReadCustomerByReferenceRequest` (`Reference`) | `CustomerResponse{Customer{Id, Reference}}` | B (404 = unknown) | map/operations/Customers.md |
| `Subscriptions.CreateSubscription` | `CreateSubscriptionOperationRequest{Body: CreateSubscriptionRequest{Subscription: CreateSubscription}}`; `CreateSubscription`: `ProductHandle`, `PaymentCollectionMethod: CollectionMethod`, `CustomerId` or `CustomerAttributes{FirstName, LastName, Email, Reference}` | `SubscriptionResponse{Subscription}` | A 422 | Models/CreateSubscription.cs |
| `Subscriptions.ReadSubscription` / `ListSubscriptions` | `ReadSubscriptionRequest` (`SubscriptionId: int`) / `ListSubscriptionsRequest` (`CustomerId`, `Page`, `PerPage`) | `SubscriptionResponse` / list | B | |
| `Subscriptions.UpdateSubscription` | `UpdateSubscriptionOperationRequest` (`SubscriptionId`; `Body: UpdateSubscriptionRequest{Subscription: UpdateSubscription{ProductHandle, ProductChangeDelayed, NextBillingAt: DateTimeOffset?}}`) | `SubscriptionResponse` | A 422 | |
| `SubscriptionStatus.PauseSubscription` / `ResumeSubscription` / `CancelSubscription` / `ReactivateSubscription` | `…Request`/`ReactivateSubscriptionOperationRequest` (`SubscriptionId: int`) | `SubscriptionResponse` | A 422 (Cancel also 404) | map/operations/SubscriptionStatus.md |
| `SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProductRequest` (`SubscriptionId`; `Body: SubscriptionProductMigrationRequest{Migration{ProductHandle}}`) | `SubscriptionResponse` | A 422 | |
| `SubscriptionComponents.ListSubscriptionComponents` | `ListSubscriptionComponentsRequest` (`SubscriptionId`) | `IReadOnlyList<SubscriptionComponentResponse{Component{ComponentId, Name, Kind, UnitName}}>` | B | |
| `SubscriptionComponents.ListUsages` | `ListUsagesRequest` (`SubscriptionIdOrReference`, `ComponentId`; `SinceDate`, `UntilDate`, `Page`, `PerPage` ≤200) | `IReadOnlyList<UsageResponse{Usage{Id, Quantity: Quantity1 (int\|string), Memo, CreatedAt}}>` | B | |
| `Invoices.ListInvoices` | `ListInvoicesRequest` (`SubscriptionId`, `Status`, `Page`, `PerPage`) | `ListInvoicesResponse{Invoices: Invoice{DueAmount: string}}` | B | |
| `ProductPricePoints.ReadProductPricePoint` | `ReadProductPricePointRequest` (`ProductId`, `PricePointId`, `CurrencyPrices`) | `ProductPricePointResponse` | B | |
| `ProductPricePoints.CreateProductCurrencyPrices` | `CreateProductCurrencyPricesOperationRequest` (`ProductPricePointId`; `Body.CurrencyPrices[{Currency, Price: int, Role}]`) | `CurrencyPricesResponse` | A `TryGetErrorArrayMapResponse1` 422 | |
| `ProductPricePoints.UpdateProductCurrencyPrices` | `UpdateProductCurrencyPricesRequest` (`ProductPricePointId`; items `{Id, Price: double}`) | `CurrencyPricesResponse` | A 422 | |
| `Sites.ReadSite` | none | `SiteResponse{Site{Currency, NonPrimaryCurrencies, RelationshipInvoicingEnabled}}` | B | |

Enum members: `CollectionMethod.Remittance/Invoice`, `SubscriptionState.Active/Trialing/OnHold/Canceled/PastDue…`, `IntervalUnit.Month`, `InvoiceStatus.Open`, `CurrencyPriceRole.Baseline`, `ComponentKind.MeteredComponent/PrepaidUsageComponent`.

Client/auth/server: `new MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; `BasicAuth = new BasicAuthCredentials{Username = Maxio:ApiKey, Password = "x"}`; `Environment = ServerEnvironment.Us|Eu`; server group `Production` (`options.Server.Production.Us.Site` / `.Eu.Site`; `BaseUrl` override at the same node). Events-based-billing group `Ebb` is not touched.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| a plan handle accepted by subscribe/switch must be a plan this shop offers | `CreateSubscription` / `MigrateSubscriptionProduct` / `UpdateSubscription` ← `ListProducts` + `ListProductFamilies` + catalog brands | `BillingService.GetOfferedPlanAsync` before the write |
| a plan id accepted by currency-price must be an offered plan | `CreateProductCurrencyPrices` ← same list | `BillingService.GetOfferedPlanAsync` before the write |
| a plan's brand must be a catalog brand | `CreateProduct` ← `IReadRepository<CatalogBrand>` | `BillingService.CreatePlanAsync` before `CreateProductFamily`/`CreateProduct` |
| a subscription id accepted from a shopper must be that shopper's | every shopper write/read ← `ReadSubscription.customer.reference` | `BillingService.GetOwnedSubscriptionAsync` before the write |

## 3. Trap notes

- Reference a canceled subscription's migration: Maxio refuses it. MUST load dotnet-calling-endpoints.
- `since_date`/`until_date` of usage listing start at midnight in the site time zone; page size caps at 200. MUST load dotnet-calling-endpoints.
- Retries and a write's connection fault: what is resent and what is not. MUST load dotnet-configuration-resilience.
- Typed vs raw errors and non-matching bodies. MUST load dotnet-error-handling.
- Fail-fast on missing credentials. MUST load dotnet-authentication.

## 4. REQUIRED READING

- maxio:dotnet-error-handling · every try/catch
- maxio:dotnet-client-initialization · DI registration
- maxio:dotnet-authentication · Basic credentials + startup check
- maxio:dotnet-calling-endpoints · request records
- maxio:dotnet-configuration-resilience · retry/timeout/logging/base URL
- maxio:dotnet-testing · tests
- Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Decision |
| --- | --- |
| 1 Credential fail-fast | `Maxio:ApiKey` and `Maxio:Subdomain` bound into `MaxioSettings`; `ValidateOnStart` refuses to boot when either is blank. Names only in the message. `Maxio:Environment` must parse to `US`/`EU` (default `US` when blank). |
| 2 Secret sourcing & rotation | .NET user-secrets in Development, `Maxio__*` environment variables elsewhere. Options built once at registration; rotation needs a restart. |
| 3 Timeout budget | `HttpClient.Timeout` 20 s bounds each attempt, `Retry.Timeout` 15 s, 2 retries only on GET/PUT (SDK default verbs): worst case ≈ 50 s on a read, one attempt (≤15 s) on a POST. Enforced in `MaxioServiceCollectionExtensions`. |
| 4 Write-retry ownership | SDK resends only `PUT` (pause is POST → never; reactivate/update are PUT → may resend, safe: state-guarded). POST writes are never resent by the SDK. |
| 5 Idempotency | No request record carries a real key. Subscribe: in-process claim per (user, plan) + reconciliation by re-listing the customer's subscriptions. Others are state-guarded (a repeat gets 409/422 from Maxio). |
| 6 Observability | `ILogger`: warnings for refused calls with status, Maxio error text; SDK logging pinned to `NullLoggerFactory` (request bodies carry e-mail addresses). |
| 7 Sensitive data | Customer e-mail in create-subscription body → `LogRequestBody` off, `LoggerFactory` set explicitly. |
| 8 Environment selection | Group `Production` only. `US` → `{site}.chargify.com`, `EU` → `{site}.ebilling.maxio.com`; `Maxio:BaseUrl` when set is used verbatim for the selected environment. Sandbox site is selected by the subdomain secret. |
| 9 Duplicate prevention | see DUPLICATE CLAIMS. |
| 10 Partial results | see PAGED READS. |
| 11 Unknown outcomes | see UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Subscribe (same user + plan, concurrently) | process memory (`ConcurrentDictionary` in `SubscribeClaims`) — **the codebase has no durable per-request store and adding a table to the Identity/Catalog DBs is out of scope; claims do not outlive the process or span instances** | `TryAdd` returns false | `BillingService.SubscribeAsync` → `BillingException(409)` | `SubscribeClaims.TryClaim`, then `_client.Subscriptions.CreateSubscription` |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| products, usages, open invoices | 200 per page | never cut short: every page is read until one is short; hard ceiling 50 pages → `BillingException(502)` instead of a silent partial answer | `BillingService.ReadAllPagesAsync` |

**UNKNOWN OUTCOMES**

| Write | Re-read with | Reference | Where in the code | Test |
| --- | --- | --- | --- | --- |
| `CreateSubscription` | `ListSubscriptions` by customer id | newest subscription of that customer on that product created after the call started | `BillingService.SubscribeAsync` catch of `SdkConnectionException`/`SdkTimeoutException` → `FindRecentSubscriptionAsync` | none automated: `BillingService` cannot be built without the app's repositories, and a failed connection cannot be provoked against the sandbox; the path was reviewed by hand |

## 6. Assumptions & Blockers

- Brand ↔ product family: family handle `catalog-brand-{brandId}`, name = brand name (matches families already in the sandbox).
- Plan ids in routes are Maxio product ids; a plan outside a brand family is not offered.
- Staff `price` is a decimal in major units (e.g. `19.99`, free plans `0` allowed); stored as cents. — decided in `BillingService.CreatePlanAsync`.
- Same user may subscribe to the same plan again sequentially; only concurrent identical requests are refused.
- Switching a canceled/paused subscription changes its plan but leaves it canceled/paused (the shopper restarts it separately); an active one is migrated at once with a new billing period. — `BillingService.SwitchPlanAsync`.
- `paid` = Maxio balance is zero and no open invoice has an amount due. — `BillingService.ToDtoAsync`.
- Currency price amounts: Maxio's create call takes whole units, so a fractional price is created rounded and then set exactly with the update call; untestable on the sandbox (no extra currency defined). — `BillingService.SetCurrencyPriceAsync`.
- Next billing date is sent as noon in the site's offset (taken from the subscription's own timestamps) and must be a future date on a subscription that is being billed. — `BillingService.SetNextBillingDateAsync`.
- No extra currency is defined on the sandbox site, and the SDK cannot define one: feature 9 can only be exercised up to Maxio's refusal there.
