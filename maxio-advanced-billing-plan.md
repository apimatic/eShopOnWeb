# Maxio Advanced Billing integration plan (PublicApi)

SDK source (plugin-relative): `sdk/dotnet/` of the `maxio` plugin. Referenced as a `ProjectReference`; never copied.
Planner agent `maxio:maxio-advanced-billing-planner` is not listed in this session, so this plan is written by the main agent.

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | Settings + client (DI, fail-fast) | `MaxioAdvancedBillingClient` |
| 2 | Plans = non-archived products whose product family is named like a catalog brand | `Products.ListProducts` (page loop) |
| 3 | Shopper ↔ Maxio customer (customer `reference` = JWT name) | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 4 | Subscribe (remittance, no card) | `Subscriptions.CreateSubscription`, `Customers.ListCustomerSubscriptions` |
| 5 | Subscription page (+ `paid`) | `Subscriptions.ReadSubscription` |
| 6 | Pause / resume | `SubscriptionStatus.PauseSubscription`, `ResumeSubscription` |
| 7 | Cancel / restart | `SubscriptionStatus.CancelSubscription`, `ReactivateSubscription` |
| 8 | Switch plan | `SubscriptionProducts.MigrateSubscriptionProduct` (+ `ReactivateSubscription` first when cancelled) |
| 9 | Usage for a day | `SubscriptionComponents.ListSubscriptionComponents`, `ListUsages` (page loop) |
| 10 | Admin: new plan | `ProductFamilies.ListProductFamilies/CreateProductFamily`, `Products.CreateProduct` |
| 11 | Admin: currency price | `Products.ReadProduct`, `ProductPricePoints.ListProductPricePoints/CreateProductCurrencyPrices/UpdateProductCurrencyPrices` |
| 12 | Admin: next billing date | `Subscriptions.UpdateSubscription` (`next_billing_at`) |

## 2. CONTRACT SHEET

> Signatures are generated code, verbatim: each operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names — never flat arguments.
> Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type.

Server group `Production`, env `Us`/`Eu`; auth `options.BasicAuth` (username = API key, password = `x`). All operations Case A unless noted.

| Operation | Request record (required) | Body / response | Error | Source |
| --- | --- | --- | --- | --- |
| `Products.ListProducts` | `Requests.Products.ListProductsRequest` {Page=1, PerPage=20, IncludeArchived?} | `IReadOnlyList<Models.ProductResponse>` → `.Product` {Id, Name, Handle, Description, PriceInCents, Interval, IntervalUnit, ArchivedAt, ProductFamily{Id,Name,Handle}, DefaultProductPricePointId} | B (RawError) | map/operations/Products.md |
| `Products.ReadProduct` | `ReadProductRequest` {ProductId: int, required} | `ProductResponse` | B | idem |
| `Products.CreateProduct` | `CreateProductRequest` {ProductFamilyId: string, required; Body} | `CreateOrUpdateProductRequest{Product: CreateOrUpdateProduct{Name*, Handle, Description*, PriceInCents*:long, Interval*:int, IntervalUnit*: Enums.IntervalUnit, RequireCreditCard?}}` → `ProductResponse` | A `ErrorListResponse1` [422] | idem |
| `ProductFamilies.ListProductFamilies` | `ListProductFamiliesRequest` (none required) | `IReadOnlyList<ProductFamilyResponse>` | B | map/operations/ProductFamilies.md |
| `ProductFamilies.CreateProductFamily` | `CreateProductFamilyOperationRequest` {Body: `CreateProductFamilyRequest{ProductFamily: CreateProductFamily{Name*, Handle}}`} | `ProductFamilyResponse` | A [422] | idem |
| `Customers.ReadCustomerByReference` | `ReadCustomerByReferenceRequest` {Reference*} | `CustomerResponse{Customer{Id, Reference, Email}}` | B (404 → not found) | map/operations/Customers.md |
| `Customers.CreateCustomer` | `CreateCustomerOperationRequest` {Body: `CreateCustomerRequest{Customer: CreateCustomer{FirstName*, LastName*, Email*, Reference}}`} | `CustomerResponse` | A [422] | idem |
| `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptionsRequest` {CustomerId*} | `IReadOnlyList<SubscriptionResponse>` | B | idem |
| `Subscriptions.CreateSubscription` | `CreateSubscriptionOperationRequest` {Body: `CreateSubscriptionRequest{Subscription: CreateSubscription{ProductHandle, CustomerId, PaymentCollectionMethod: Enums.CollectionMethod.Remittance, Reference}}`} | `SubscriptionResponse{Subscription{Id, State, BalanceInCents, ProductPriceInCents, Product, Customer{Reference}, NextAssessmentAt, CurrentPeriodEndsAt, Reference}}` | A [422] | map/operations/Subscriptions.md |
| `Subscriptions.FindSubscription` | `FindSubscriptionRequest` {Reference} | `SubscriptionResponse` | A `TryGetNoContent` [404] | idem |
| `Subscriptions.ReadSubscription` | `ReadSubscriptionRequest` {SubscriptionId: int*} | `SubscriptionResponse` | B | idem |
| `Subscriptions.UpdateSubscription` | `UpdateSubscriptionOperationRequest` {SubscriptionId*, Body: `UpdateSubscriptionRequest{Subscription: UpdateSubscription{NextBillingAt: DateTimeOffset?}}`} | `SubscriptionResponse` | A [422] | idem |
| `SubscriptionStatus.PauseSubscription` | `PauseSubscriptionRequest` {SubscriptionId*, Body?: PauseRequest} | `SubscriptionResponse` | A [422] | map/operations/SubscriptionStatus.md |
| `SubscriptionStatus.ResumeSubscription` | `ResumeSubscriptionRequest` {SubscriptionId*} | `SubscriptionResponse` | A [422] | idem |
| `SubscriptionStatus.CancelSubscription` | `CancelSubscriptionRequest` {SubscriptionId*, Body: `CancellationRequest{Subscription: CancellationOptions{CancellationMessage}}`} | `SubscriptionResponse` | A [404],[422] | idem |
| `SubscriptionStatus.ReactivateSubscription` | `ReactivateSubscriptionOperationRequest` {SubscriptionId*, Body?: ReactivateSubscriptionRequest} | `SubscriptionResponse` | A [422] | idem |
| `SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProductRequest` {SubscriptionId*, Body: `SubscriptionProductMigrationRequest{Migration: SubscriptionProductMigration{ProductHandle, IncludeTrial, IncludeInitialCharge, IncludeCoupons, PreservePeriod}}`} | `SubscriptionResponse` | A [422] | map/operations/SubscriptionProducts.md |
| `SubscriptionComponents.ListSubscriptionComponents` | `ListSubscriptionComponentsRequest` {SubscriptionId*} | `IReadOnlyList<SubscriptionComponentResponse{Component{ComponentId, Name, Kind: Enums.ComponentKind, UnitName}}>` | B | map/operations/SubscriptionComponents.md |
| `SubscriptionComponents.ListUsages` | `ListUsagesRequest` {SubscriptionIdOrReference*: AnyOf, ComponentId*: AnyOf, SinceDate, UntilDate, Page, PerPage(≤200)} | `IReadOnlyList<UsageResponse{Usage{Id, CreatedAt, Quantity: AnyOf.Quantity1 (int|string), Memo, ComponentId}}>` | B | idem |
| `ProductPricePoints.ListProductPricePoints` | `ListProductPricePointsRequest` {ProductId*: AnyOf, CurrencyPrices: true} | `ListProductPricePointsResponse{PricePoints[]{Id, Handle, Type, CurrencyPrices[]}}` | B | map/operations/ProductPricePoints.md |
| `ProductPricePoints.CreateProductCurrencyPrices` | `CreateProductCurrencyPricesOperationRequest` {ProductPricePointId*, Body: `CreateProductCurrencyPricesRequest{CurrencyPrices[]: CreateProductCurrencyPrice{Currency*, Price*: int, Role*: Enums.CurrencyPriceRole}}`} | `CurrencyPricesResponse` | A `ErrorArrayMapResponse1` [422] | idem |
| `ProductPricePoints.UpdateProductCurrencyPrices` | `UpdateProductCurrencyPricesRequest` | `CurrencyPricesResponse` | A [422] | idem |

Errors: `Core.Exceptions.ApiException<TError>` (typed `.Error`, `.StatusCode`), `ApiException<Core.ErrorResponse.RawError>` for case B, plus `SdkConnectionException`, `SdkTimeoutException`, `ResponseDeserializationException`, `AuthSchemeException`. Client: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; env `Servers.ServerEnvironment.Us|Eu`; base URL override `options.Server.Production.{Us|Eu}.BaseUrl`, site `…Site`.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| A plan handle accepted by subscribe / switch must be one `GET /api/plans` offers (non-archived, family named like a catalog brand) | `CreateSubscription`, `MigrateSubscriptionProduct` ← `ListProducts` | `MaxioBillingService.FindOfferedPlanAsync`, before the write |
| A plan id accepted by currency-price must be one that is offered | `CreateProductCurrencyPrices` ← `ListProducts` | `MaxioBillingService.FindOfferedPlanByIdAsync`, before the write |
| A brand accepted for a new plan must exist in the shop's catalog | `CreateProduct` ← `IRepository<CatalogBrand>` | `AdminCreatePlanEndpoint`, before the write |
| A subscription id accepted from a shopper must belong to the shopper (Maxio customer reference == JWT name) | all `{subscriptionId}` shopper routes ← `ReadSubscription` | `MaxioBillingService.GetOwnedSubscriptionAsync`, before any write (404 otherwise) |

## 3. Trap notes

- Per-day usage window: `SinceDate`/`UntilDate` are midnight-based and the usage clock is the site's offset — MUST load `dotnet-calling-endpoints`.
- A drifted body surfaces as `ResponseDeserializationException`, not `ApiException<T>` — MUST load `dotnet-error-handling`.
- `Timeout` is per attempt; a call budget needs a token — MUST load `dotnet-configuration-resilience`.
- `AnyOf` unions (`SubscriptionIdOrReference`, `ComponentIdModel`, `Quantity1`) have implicit conversions/`TryGet…` — MUST load `dotnet-models`.
- DI extension registers singleton over the shared default `HttpClient` — MUST load `dotnet-client-initialization`.
- Basic auth credential shape — MUST load `dotnet-authentication`.

## 4. REQUIRED READING

- maxio:dotnet-getting-started · map + install
- maxio:dotnet-client-initialization · DI/client
- maxio:dotnet-authentication · BasicAuth
- maxio:dotnet-calling-endpoints · request records
- maxio:dotnet-models · AnyOf unions, enums
- maxio:dotnet-error-handling · catch ladder (always)
- maxio:dotnet-configuration-resilience · timeouts, retries, server override
- maxio:dotnet-testing

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `Maxio:ApiKey` and `Maxio:Subdomain` bound via `IConfiguration`; host refuses to start (validated options, `ValidateOnStart`) if either is blank, and if `Maxio:Environment` is not US/EU. |
| 2 | Secret sourcing & rotation | .NET user-secrets in development / environment (`Maxio__*`) in deployment; options built once at registration into a singleton client — rotation needs a restart. |
| 3 | Total timeout budget | 10 s per attempt (`Retry.Timeout`), `HttpClient.Timeout` 15 s, whole-call budget 30 s via a linked `CancellationTokenSource` in `MaxioBillingService.Bounded`, covering every operation of a handler. |
| 4 | Write-retry ownership | SDK default: POST not resent; PUT (UpdateSubscription, Pause/Resume/Cancel are PUT/POST per SDK) may be resent — all of our writes are state-setting and idempotent in effect. |
| 5 | Idempotency & ambiguous writes | CreateSubscription: real key = request member `Reference` (a fresh GUID per request) — used for reconciliation via `FindSubscription`. Other writes have no key: reconcile by re-reading the subscription state. |
| 6 | Observability | SDK logger from the host `ILoggerFactory`; `LogRequestBody` off. Maxio failures are logged with status; the user-facing reason is the Maxio error text. |
| 7 | Sensitive data | Scope carries customer name/email only; no card data is ever sent. `LogRequestBody` stays off, `LoggerFactory` set explicitly. |
| 8 | Environment selection | `Maxio:Environment` US→`ServerEnvironment.Us`, EU→`ServerEnvironment.Eu`; `Maxio:Subdomain` → `Server.Production.{env}.Site`; `Maxio:BaseUrl`, when set, is assigned verbatim to `Server.Production.{env}.BaseUrl`. Only the `Production` group is touched (the `Ebb` group is not used). |
| 9 | Duplicate prevention | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Customer create (per shopper) | Maxio customer `reference` (system of record) | Maxio rejects a repeated customer reference (422) | `MaxioBillingService.EnsureCustomerAsync` re-reads by reference | `EnsureCustomerAsync` claim = `Customers.CreateCustomer` (its `catch (ApiException)` re-reads) |
| Subscribe | Same-plan live-subscription check + per-(shopper,plan) in-process gate (`SubscribeGates`). LIMITATION: the codebase has no durable claim store (no table for it, adding one needs a migration); across several instances the check-then-create window remains | Existing live subscription → 409 `BillingException` | `SubscribeAsync` throws it; `ExceptionMiddleware` maps it | `SubscribeGates` wait + `ListCustomerSubscriptions` check, then `Subscriptions.CreateSubscription` (in `SubscribeAsync`) |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| Plans list (`ListProducts`) | page loop with a hard page cap (50 × 200) | the loop throws a 502 rather than returning a silently truncated list | `LoadOfferedPlansAsync` (`MAX_PAGES` check throws `BillingException(502)`) |
| Usages (`ListUsages`) | page loop with a hard page cap | same 502 | `GetUsageAsync` (`MAX_PAGES` check throws `BillingException(502)`) |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| CreateSubscription | `FindSubscription` | `Reference` (GUID sent) | `SubscribeAsync` catch `IsUnknownOutcome` → `SettleCreatedAsync` | `SubscribeUnknownOutcomeTest` (both tests: connection fails on POST; found → returned, not found → 502 `BillingException`) |
| Pause/Resume/Cancel/Reactivate/Migrate/UpdateSubscription | `ReadSubscription` | subscription id; compare state/product/date to the expected result | `MaxioBillingService.Write` (catch `IsUnknownOutcome` → `GetSubscriptionOrNotFoundAsync` → `reachedExpectedState`) | none yet: shares `Write` with the subscribe test's fault injection style; not separately covered |

## 6. Assumptions & Blockers

- DECIDED: plan = Maxio product; brand = the product family whose name equals the catalog brand name (family created on demand) — `LoadOfferedPlansAsync`, `EnsureFamilyAsync`.
- DECIDED: shopper identity = `ClaimTypes.Name` of the JWT; Maxio customer `reference` = that name — `BillingEndpointSupport.UserName`, `EnsureCustomerAsync`, `GetOwnedSubscriptionAsync`.
- DECIDED: `paid` = `BalanceInCents <= 0` — `MaxioBillingService.ToDto`; confirmed against the sandbox (remittance plan → balance due, `paid:false`; free plan → `paid:true`).
- DECIDED: collection method `remittance` so no card is required — `SubscribeAsync`.
- DECIDED: a second subscription to a plan the shopper already holds (not cancelled/expired) is refused with 409; several different plans are allowed — `SubscribeAsync`.
- DECIDED: switching a cancelled subscription reactivates it first, then migrates (verified on the sandbox) — `SwitchPlanAsync`.
- DECIDED: next billing date is sent as noon on the subscription's own UTC offset so the date cannot shift with the site's time zone; it must be in the future — `SetNextBillingDateAsync`.
- DECIDED: a currency price needs the currency defined on the Maxio site (`non_primary_currencies`); otherwise 422 with the available list. The sandbox site defines none, so the success path could not be exercised live (the SDK offers no operation to define site currencies) — `SetCurrencyPriceAsync`.
- DECIDED: usage day is cut on the site's own clock (`created_at` offset); the Maxio query window is widened by a day each side — `GetUsageAsync`. Only `metered_component` components are reported.
- No blockers.
