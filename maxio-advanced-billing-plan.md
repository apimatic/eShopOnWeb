# Maxio billing for eShopOnWeb PublicApi — plan and contract sheet

SDK source (plugin-relative): `sdk/dotnet/` of the `maxio` plugin (v0.2.2). Root namespace `MaxioAdvancedBilling`.
Written by the main agent (the `maxio-advanced-billing-planner` agent is not listed in this host).

## 1. Scope & sequence

1. Settings + client: bind `Maxio:ApiKey|Subdomain|Environment|BaseUrl`, fail-fast at startup, singleton `MaxioAdvancedBillingClient`.
2. Plans (`GET /api/plans`): `Products.ListProducts` (paged, non-archived).
3. Subscribe (`POST /api/subscriptions`): `Customers.ReadCustomerByReference` / `CreateCustomer`, `Subscriptions.CreateSubscription`.
4. Read (`GET /api/subscriptions/{id}`): `Subscriptions.ReadSubscription`.
5. Pause/Resume: `SubscriptionStatus.PauseSubscription` / `ResumeSubscription`.
6. Cancel/Restart: `SubscriptionStatus.CancelSubscription` / `ReactivateSubscription`.
7. Switch: `Subscriptions.UpdateSubscription` (product_handle).
8. Usage for a day: `SubscriptionComponents.ListSubscriptionComponents` + `ListUsages`.
9. Admin plans: `ProductFamilies.ListProductFamilies`/`CreateProductFamily`, `Products.CreateProduct`.
10. Admin currency price: `ProductPricePoints.ListProductPricePoints`, `CreateProductCurrencyPrices`.
11. Admin next billing date: `Subscriptions.UpdateSubscription` (next_billing_at).

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim: each operation takes ONE request record as its first parameter, built with an object initializer using the record's own property names — never flat arguments.
⚠ Every SDK type is written fully-qualified/`using`'d with the namespace its source path implies (`Models`, `Models.Enums`, `Models.AnyOf`, `Requests.<Controller>`, `Core.Exceptions`, `Core.ErrorResponse`, `Servers`, `Core.Authentication.Basic`, `Core.Configuration`).

| Controller.Operation | Request record (members) | Body / response | Error case | Source |
| --- | --- | --- | --- | --- |
| `Products.ListProducts` | `Requests.Products.ListProductsRequest` (`Page`=1,`PerPage`≤200,`IncludeArchived?`) | `IReadOnlyList<ProductResponse>` → `.Product` (`Id,Handle,Name,Description,PriceInCents,Interval,IntervalUnit,ArchivedAt,ProductFamily{Id,Name,Handle}`) | B (`RawError`) | map/operations/Products.md |
| `Products.CreateProduct` | `CreateProductRequest` (`ProductFamilyId: string` required — id or `handle:x`; `Body`) | `CreateOrUpdateProductRequest{Product: CreateOrUpdateProduct{Name*, Description*, PriceInCents*: long, Interval*: int, IntervalUnit*: IntervalUnit, Handle?, RequireCreditCard?}}` → `ProductResponse` | A `TryGetErrorListResponse1` (422) | Products.md; Models/CreateOrUpdateProduct.cs |
| `ProductFamilies.ListProductFamilies` | `ListProductFamiliesRequest` (all optional) | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily{Id,Name,Handle}` | B | ProductFamilies.md |
| `ProductFamilies.CreateProductFamily` | `CreateProductFamilyOperationRequest{Body: CreateProductFamilyRequest{ProductFamily: CreateProductFamily}}` | `ProductFamilyResponse` | A 422 | ProductFamilies.md |
| `Customers.ReadCustomerByReference` | `ReadCustomerByReferenceRequest{Reference*}` | `CustomerResponse.Customer{Id,Reference}` | B (404 for unknown) | Customers.md |
| `Customers.CreateCustomer` | `CreateCustomerOperationRequest{Body: CreateCustomerRequest{Customer: CreateCustomer{FirstName*,LastName*,Email*,Reference}}}` | `CustomerResponse` | A `TryGetCustomerErrorResponse1` 422 (duplicate reference = 422, verified live) | Customers.md |
| `Subscriptions.CreateSubscription` | `CreateSubscriptionOperationRequest{Body: CreateSubscriptionRequest{Subscription: CreateSubscription{ProductHandle, CustomerReference, PaymentCollectionMethod: CollectionMethod, Reference}}}` | `SubscriptionResponse.Subscription` | A 422 | Subscriptions.md |
| `Subscriptions.FindSubscription` | `FindSubscriptionRequest{Reference}` | `SubscriptionResponse` | A `TryGetNoContent` 404 | Subscriptions.md |
| `Subscriptions.ReadSubscription` | `ReadSubscriptionRequest{SubscriptionId*: int}` | `SubscriptionResponse.Subscription{Id,State,BalanceInCents,Product{Id,Handle,Name},Customer{Reference},NextAssessmentAt,CurrentPeriodEndsAt,CanceledAt,Currency,PaymentCollectionMethod}` | B (404) | Subscriptions.md |
| `Subscriptions.UpdateSubscription` | `UpdateSubscriptionOperationRequest{SubscriptionId*, Body: UpdateSubscriptionRequest{Subscription: UpdateSubscription{ProductHandle?, NextBillingAt?: DateTimeOffset}}}` | `SubscriptionResponse` | A 422 | Subscriptions.md; remarks in Api/Subscriptions.cs |
| `SubscriptionStatus.PauseSubscription` | `PauseSubscriptionRequest{SubscriptionId*}` | `SubscriptionResponse` | A 422 | SubscriptionStatus.md |
| `SubscriptionStatus.ResumeSubscription` | `ResumeSubscriptionRequest{SubscriptionId*}` | `SubscriptionResponse` | A 422 | SubscriptionStatus.md |
| `SubscriptionStatus.CancelSubscription` | `CancelSubscriptionRequest{SubscriptionId*, Body: CancellationRequest{Subscription: CancellationOptions}}` | `SubscriptionResponse` | A 404 / 422 | SubscriptionStatus.md |
| `SubscriptionStatus.ReactivateSubscription` | `ReactivateSubscriptionOperationRequest{SubscriptionId*}` | `SubscriptionResponse` | A 422 | SubscriptionStatus.md |
| `SubscriptionComponents.ListSubscriptionComponents` | `ListSubscriptionComponentsRequest{SubscriptionId*}` | `IReadOnlyList<SubscriptionComponentResponse>` → `.Component{ComponentId,Name,Kind,UnitName}` | B | SubscriptionComponents.md |
| `SubscriptionComponents.ListUsages` | `ListUsagesRequest{SubscriptionIdOrReference*: AnyOf, ComponentId*: AnyOf, SinceDate?, UntilDate?, Page, PerPage≤200}` | `IReadOnlyList<UsageResponse>` → `.Usage{Id,Memo,CreatedAt,Quantity: Quantity1(int\|string),ComponentId}` | B; **paged** (page/per_page) | SubscriptionComponents.md |
| `ProductPricePoints.ListProductPricePoints` | `ListProductPricePointsRequest{ProductId*: ProductIdModel, CurrencyPrices?}` | `ListProductPricePointsResponse.PricePoints[] {Id,Type,PriceInCents,...}` | B | ProductPricePoints.md |
| `ProductPricePoints.CreateProductCurrencyPrices` | `CreateProductCurrencyPricesOperationRequest{ProductPricePointId*: int, Body: CreateProductCurrencyPricesRequest{CurrencyPrices: [CreateProductCurrencyPrice{Currency*: string, Price*: int, Role*: CurrencyPriceRole}]}}` | `CurrencyPricesResponse` | A `TryGetErrorArrayMapResponse1` 422 | ProductPricePoints.md; Models/CreateProductCurrencyPrice.cs |

Enum values used: `CollectionMethod.Remittance`, `IntervalUnit.Month`, `SubscriptionState.{Active,OnHold,Canceled,...}`, `CurrencyPriceRole.Baseline`, `ComponentKind.MeteredComponent`.

Client/auth/server: `new MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; `options.BasicAuth = new BasicAuthCredentials{Username=<api key>, Password="x"}`; `options.Environment = ServerEnvironment.Us|Eu`; site via `options.Server.Production.{Us|Eu}.Site`; `BaseUrl` via `options.Server.Production.{Us|Eu}.BaseUrl` (a literal URL with no `{site}` is used verbatim). Server group `Production` for every operation used.

### Facts verified live against the sandbox (not in the map)
- Site `relationship_invoicing_enabled=true`, no non-primary currencies. A `remittance` subscription needs no card and starts `active` with `balance_in_cents` = price (an open invoice) — so `paid=false` until paid.
- `CreateCustomer` with an existing `reference` → 422. Customers are therefore read-by-reference first.
- Pause → `on_hold`; resume → `active`. Migration (`MigrateSubscriptionProduct`) **422s on a canceled subscription**; `UpdateSubscription{ProductHandle}` works in every state (canceled stays canceled; charges at next period). `UpdateSubscription{NextBillingAt}` on a canceled subscription → 422.
- Maxio returns timestamps in the site's zone offset (+05:00 here); `ListUsages` `since_date`/`until_date` are created_at bounds at midnight (inclusive), so a single day must be filtered by the usage's own local date.
- Sandbox has families `.NET`(`brand-2`), `Azure`(`brand-1`) — i.e. the convention is family handle `brand-{catalogBrandId}`, name = brand name — and an unrelated `eShopSubscribe` family whose plans carry the `api-call` metered component.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| a plan handle accepted by subscribe / switch must be one `GET /api/plans` returns (non-archived product of the site) | `CreateSubscription`, `UpdateSubscription(ProductHandle)` ← `ListProducts` | `PlanCatalog.FindOfferedAsync`, before the write |
| a subscription id a shopper acts on must belong to them | all `{subscriptionId}` shopper ops ← `ReadSubscription` | `SubscriptionAccess.LoadOwnedAsync`: `Customer.Reference` == JWT name, else 404 |
| a plan's brand must be a catalog brand | `CreateProduct` ← `IReadRepository<CatalogBrand>` | `AdminPlanCreateEndpoint`, before `CreateProduct` |
| `planId` for currency price must be an existing plan | `CreateProductCurrencyPrices` ← `ListProducts` / price points | `ListProductPricePoints` on the product id (404 → 404) |
| a usage day must be the usage's own local date | `ListUsages` | client-side local-date filter |

## 3. Trap notes
- Credential/options built once at registration → rotation needs restart. MUST load dotnet-client-initialization, dotnet-authentication.
- Per-attempt timeout is not a call budget; GET/PUT retried, POST not. MUST load dotnet-configuration-resilience.
- 422 vs transport vs deserialization failures take different exception types. MUST load dotnet-error-handling.
- Unions (`Quantity1`, `ComponentIdModel`, `SubscriptionIdOrReference`) and open enums are not plain values. MUST load dotnet-models.

## 4. REQUIRED READING
- maxio:dotnet-error-handling · every try/catch (error boundary)
- maxio:dotnet-client-initialization · DI registration
- maxio:dotnet-authentication · BasicAuth + fail-fast
- maxio:dotnet-configuration-resilience · retry/timeouts/logging/BaseUrl
- maxio:dotnet-models · unions/enums
- maxio:dotnet-testing · tests
- Hazard: a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings.Validate` at registration: `Maxio:ApiKey` required and non-blank; `Maxio:Subdomain` required unless `Maxio:BaseUrl` is set; `Maxio:Environment` blank → US, otherwise must be US/EU (case-insensitive) else startup throws; `BaseUrl` must be an absolute http(s) URL. Host refuses to start otherwise. |
| 2 | Secret sourcing & rotation | Dev: .NET user-secrets (loaded from the env vars `MAXIO_API_KEY`/`MAXIO_SITE_SUBDOMAIN`/`MAXIO_ENVIRONMENT`); elsewhere env `Maxio__ApiKey` etc. Options captured once in the singleton client: rotation needs a restart. |
| 3 | Total timeout budget | `Retry.Timeout` 10 s per attempt, `MaxRetries` 2 (GET/PUT), `HttpClient.Timeout` 15 s; worst read ≈ 3×10 s + backoff ≈ 35 s; POST single attempt ≤ 15 s. Enforced in `MaxioServiceCollectionExtensions`. |
| 4 | Write-retry ownership | SDK retries only GET/HEAD/PUT/OPTIONS: `UpdateSubscription` (PUT, idempotent: same product / same date) may be resent; every POST (create, pause, resume, cancel, reactivate, create product, currency price) is single-attempt. |
| 5 | Idempotency & ambiguous writes | Subscribe: real key = `CreateSubscription.Reference` (fresh GUID per request), reconciled with `FindSubscription`. State transitions are idempotent by target state (re-read & compare). Create plan: explicit unique `Handle`, reconciled with `ReadProductByHandle`. Currency price: reconciled by re-reading price point currency prices. |
| 6 | Observability | `ILogger` Warning for Maxio 4xx mapped to caller errors (status + reason, no PII), Error for 5xx/transport/deserialization; Maxio `X-Request-Id` not exposed by SDK on errors → status/uri logged. SDK logger set explicitly. |
| 7 | Sensitive data | Scope carries customer email/name; no card data. `LogRequestBody` off, `LoggerFactory` assigned explicitly (so `MAXIO_ADVANCED_BILLING_LOG=trace` cannot enable body logging). |
| 8 | Environment selection | Groups `Production` (used) and `Ebb` (unused). `Maxio:Environment` US→`ServerEnvironment.Us`, EU→`Eu`; `Maxio:Subdomain`→`Server.Production.<env>.Site`; `Maxio:BaseUrl`, when set, → `Server.Production.<env>.BaseUrl` verbatim. Sandbox site = the configured subdomain (never prod). |
| 9 | Duplicate prevention | See DUPLICATE CLAIMS: no durable store owned by this app for billing state; Maxio is the system of record. |
| 10 | Partial results | `ListProducts` and `ListUsages` walk all pages (no cap) until a short page; there is no cap, so nothing can be cut short. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Subscribe (`POST /api/subscriptions`) | none: the task lets a shopper hold several subscriptions, even to one plan, so there is no uniqueness rule to claim; this app owns no billing store (its EF stores hold catalog and identity only). Maxio is the system of record. | n/a for the subscription (a double submit is two subscriptions by design, each with its own `Reference`). The one-customer-per-shopper rule IS enforced: Maxio refuses a second customer with the same `reference` (422). | `SubscriptionService.EnsureCustomerAsync` catches that refusal and re-reads the winner's customer | `SubscriptionService.EnsureCustomerAsync` (customer claim: `CreateCustomer`), then `SubscriptionService.SubscribeAsync` (`CreateSubscription`) |
| Create plan (`POST /api/admin/plans`) | Maxio product family handle `brand-{id}` (unique per site) | Maxio refuses a second family with that handle (422) | `PlanService.EnsureBrandFamilyAsync` catches it and re-reads the winner's family | `PlanService.EnsureBrandFamilyAsync` (`CreateProductFamily`), then `PlanService.CreatePlanAsync` (`CreateProduct`) |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProducts` | 200 per page, walked until a short page; hard stop at 100 pages | never silently cut: past the hard stop the call fails with 502 instead of returning a partial list | `PlanService.ListOfferedAsync` |
| `ListUsages` | same | same (502) | `SubscriptionService.ListDayUsagesAsync` |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| subscribe | `Subscriptions.FindSubscription` | `Reference` (per-request GUID) | `SubscriptionService.SubscribeAsync` catch → `FindByReferenceAsync` | see note |
| pause / resume / cancel / restart / switch | `Subscriptions.ReadSubscription` | subscription id + target state (`achieved` predicate) | `SubscriptionService.TransitionAsync` catch | see note |
| next billing date | `Subscriptions.ReadSubscription` | subscription id + target date | `SubscriptionService.SetNextBillingDateAsync` (the read-back after the update always runs) | see note |
| create plan | `Products.ReadProductByHandle` | explicit `Handle` (slug + random suffix) | `PlanService.CreatePlanAsync` catch → `FindProductByHandleAsync` | see note |
| currency price | `ProductPricePoints.ListProductPricePoints(CurrencyPrices=true)` | currency on the default price point | `PlanService.AddCurrencyPriceAsync` catch | see note |

Note on the test: a connection that fails *after* Maxio acted can only be produced by a fake Maxio, and the task forbids stubbing it. `MaxioSettingsTest.BaseUrlIsUsedVerbatimInsteadOfTheSubdomainAddress` fails the connection before Maxio acts (unreachable `Maxio:BaseUrl`) and proves a transport failure answers 502/504 rather than a 500; the re-read branches themselves are verified by code reading only.

## 6. Assumptions & Blockers
No blockers. Decisions the code made (all were `YOUR CALL — not in the map`), with the member that implements each:
- Offered plans = every non-archived product on the site, all pages (`PlanService.ListOfferedAsync`); the sandbox `eShopSubscribe` plans carry the metered component, so they must stay subscribable. Brand binding applies at plan creation (`PlanService.EnsureBrandFamilyAsync`, family handle `brand-{catalogBrandId}`, the convention already present in the sandbox). Subscribe and switch accept only handles from that list (`PlanService.GetOfferedAsync`).
- Customer identity = JWT `ClaimTypes.Name`, lower-cased, used as the Maxio customer `reference` (`SubscriptionService.CustomerReference`); a subscription is the caller's only if its customer reference matches, otherwise 404 (`SubscriptionService.LoadOwnedAsync`).
- `paid` = `balance_in_cents <= 0` (`SubscriptionService.ToDto`). Verified live: a fresh remittance subscription carries an open invoice and a balance equal to its price.
- Subscribe uses `CollectionMethod.Remittance`, no card (`SubscriptionService.SubscribeAsync`).
- Switch uses `UpdateSubscription{ProductHandle}` in every state because a migration 422s on a canceled subscription (`SubscriptionService.SwitchPlanAsync`); a canceled subscription stays canceled on the new plan until restarted; no charge is raised until the next period.
- Plan price is given in major currency units with at most two decimals and converted to cents (`CreatePlanEndpoint`); a currency price must be a whole number of units because the SDK models it as `int` (`PlanService.AddCurrencyPriceAsync`). The sandbox site has no non-primary currency, so a real currency price is refused by Maxio (422) and the app passes that reason on; the success path could not be exercised in this sandbox.
- Next billing date: the date plus the subscription's existing local time of day in the site's offset, strictly in the future, refused with 409 for a canceled/ended subscription, then read back to confirm (`SubscriptionService.SetNextBillingDateAsync`).
- Usage for a day: metered components of the subscription only; Maxio is asked for a day either side and the result filtered by each usage's own site-local date, all pages (`SubscriptionService.GetUsageAsync`/`ListDayUsagesAsync`).
- Error mapping: Maxio 404 → 404, other 4xx → 422 with Maxio's reason; state conflicts checked first → 409 with a reason; Maxio 401/403/5xx/429 or transport failure → 502/503/504 without leaking details (`MaxioErrors`). Errors are written by `ExceptionMiddleware` as the project's existing `ErrorDetails` JSON.
- Request property names are not fixed by the task, so a few aliases are accepted (`planHandle`/`handle`/`productHandle`, `price`/`monthlyPrice`, `brandId`/`brand`, `date`/`nextBillingDate`).
- The project references the SDK at its plugin install path (`PublicApi.csproj`); that path is local to this machine and must be re-pointed (`dotnet remove reference` / `dotnet add reference`) wherever the plugin is installed elsewhere.
