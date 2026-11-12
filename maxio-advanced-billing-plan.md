# Maxio Advanced Billing plan — eShopOnWeb `src/PublicApi`

SDK: `<maxio plugin>/sdk/dotnet` (plugin-relative; map `sdk-map.md` + `map/operations/*`). Project reference to `MaxioAdvancedBilling.csproj`.

## 1. Scope & sequence
1. Config + client (`Maxio:` section, fail-fast, DI singleton).
2. `MaxioGateway` service in PublicApi wrapping SDK calls; endpoints call it.
3. Plans (list) → Subscribe → Read(+paid) → Pause/Resume → Cancel/Restart → Switch → Usage(day) → Admin: create plan, currency price, next-billing date.

Design: no local persistence. Plan ↔ brand link lives in Maxio: one product family per catalog brand, handle `eshop-brand-{brandId}`. "Plans the app offers" = non-archived products of families whose handle is `eshop-brand-{id}` for an existing catalog brand. Plan id (admin route) = Maxio product id. Subscription ownership = Maxio customer `reference` == `eshop-user-{JWT name}`; foreign subscription → 404.

## 2. CONTRACT SHEET
⚠ Signatures are generated code, verbatim: each operation takes ONE request record built with an object initializer using the record's own property names. ⚠ Every SDK type is fully qualified by the namespace its source path implies (Models → `MaxioAdvancedBilling.Models`, Enums → `.Models.Enums`, AnyOf → `.Models.AnyOf`, Requests → `.Requests.<Controller>`).

| Operation (client.X.Op) | Request record (required) / body | Response | Error | Source |
| --- | --- | --- | --- | --- |
| ProductFamilies.ListProductFamilies | `ListProductFamiliesRequest` (none) | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` {Id,Name,Handle} | B | map ProductFamilies |
| ProductFamilies.CreateProductFamily | `CreateProductFamilyOperationRequest{Body=CreateProductFamilyRequest{ProductFamily=CreateProductFamily{Name req,Handle}}}` | `ProductFamilyResponse` | A 422 ErrorListResponse1 | map ProductFamilies |
| ProductFamilies.ListProductsForProductFamily | `ListProductsForProductFamilyRequest{ProductFamilyId: string req, Page, PerPage}` | `IReadOnlyList<ProductResponse>` → `.Product` {Id,Name,Handle,PriceInCents,Interval,IntervalUnit,ArchivedAt,DefaultProductPricePointId} | A (404 string) | map ProductFamilies |
| Products.CreateProduct | `CreateProductRequest{ProductFamilyId: int req, Body=CreateOrUpdateProductRequest{Product=CreateOrUpdateProduct{Name,Description,PriceInCents(long),Interval(int),IntervalUnit all req; Handle, RequireCreditCard}}}` | `ProductResponse` | A 422 ErrorListResponse1 | Models/CreateOrUpdateProduct.cs |
| Products.ReadProduct | `ReadProductRequest{ProductId req}` | `ProductResponse` | B | map Products |
| Products.ReadProductByHandle | `ReadProductByHandleRequest{ApiHandle req}` | `ProductResponse` | B | map Products |
| Customers.ReadCustomerByReference | `ReadCustomerByReferenceRequest{Reference req}` | `CustomerResponse.Customer` | B (404) | map Customers |
| Customers.CreateCustomer | `CreateCustomerOperationRequest{Body=CreateCustomerRequest{Customer=CreateCustomer{FirstName,LastName,Email req; Reference}}}` | `CustomerResponse` | A 422 | map Customers |
| Subscriptions.CreateSubscription | `CreateSubscriptionOperationRequest{Body=CreateSubscriptionRequest{Subscription=CreateSubscription{ProductHandle,CustomerId,PaymentCollectionMethod=Remittance,Reference}}}` | `SubscriptionResponse.Subscription` | A 422 | Models/CreateSubscription.cs |
| Subscriptions.ReadSubscription | `ReadSubscriptionRequest{SubscriptionId req}` | `SubscriptionResponse` {State, BalanceInCents, Customer.Reference, Product.Handle, CurrentPeriodEndsAt} | B | map Subscriptions |
| Subscriptions.UpdateSubscription | `UpdateSubscriptionOperationRequest{SubscriptionId req, Body=UpdateSubscriptionRequest{Subscription=UpdateSubscription{NextBillingAt}}}` | `SubscriptionResponse` (next billing visible via `CurrentPeriodEndsAt`) | A 422 | Api/Subscriptions.cs remarks |
| SubscriptionStatus.PauseSubscription | `PauseSubscriptionRequest{SubscriptionId req}` | `SubscriptionResponse` | A 422 | map SubscriptionStatus |
| SubscriptionStatus.ResumeSubscription | `ResumeSubscriptionRequest{SubscriptionId req}` | `SubscriptionResponse` | A 422 | map SubscriptionStatus |
| SubscriptionStatus.CancelSubscription | `CancelSubscriptionRequest{SubscriptionId req}` | `SubscriptionResponse` | A 404/422 | map SubscriptionStatus |
| SubscriptionStatus.ReactivateSubscription | `ReactivateSubscriptionOperationRequest{SubscriptionId req}` | `SubscriptionResponse` | A 422 | map SubscriptionStatus |
| SubscriptionProducts.MigrateSubscriptionProduct | `MigrateSubscriptionProductRequest{SubscriptionId req, Body=SubscriptionProductMigrationRequest{Migration{ProductHandle}}}` — valid only for `active`/`trialing` subs; migrating to current product fails | `SubscriptionResponse` | A 422 | Api/SubscriptionProducts.cs remarks |
| SubscriptionComponents.ListSubscriptionComponents | `ListSubscriptionComponentsRequest{SubscriptionId req}` → `.Component` {ComponentId, ComponentHandle, Name, UnitName, Kind (`ComponentKind.MeteredComponent`)} | list | B | map SubscriptionComponents |
| SubscriptionComponents.ListUsages | `ListUsagesRequest{SubscriptionIdOrReference req (AnyOf, implicit from int), ComponentId req (AnyOf implicit from int), SinceDate, UntilDate: DateTimeOffset?, Page, PerPage≤200}` — remarks: `since_date`/`until_date` are midnights; one day D = since D, until D+1 | `IReadOnlyList<UsageResponse>` → `.Usage` {Quantity (`AnyOf.Quantity1` int-or-string), CreatedAt} | B; paged (page/per_page) | Api/SubscriptionComponents.cs remarks |
| Invoices.ListInvoices | `ListInvoicesRequest{Status=InvoiceStatus.Open, SubscriptionId, Page, PerPage}` | `ListInvoicesResponse.Invoices` {DueAmount: string} | B; paged | map Invoices |
| ProductPricePoints.CreateProductCurrencyPrices | `CreateProductCurrencyPricesOperationRequest{ProductPricePointId req, Body=CreateProductCurrencyPricesRequest{CurrencyPrices=[CreateProductCurrencyPrice{Currency,Price(int!),Role=CurrencyPriceRole.Baseline} all req]}}`; not for custom price points; site-level currency must exist | `CurrencyPricesResponse` | A 422 ErrorArrayMapResponse1 | Api/ProductPricePoints.cs remarks |

Client: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; `BasicAuth = new BasicAuthCredentials{Username=<api key>, Password="x"}`; `Environment = ServerEnvironment.Us|Eu` (`MaxioAdvancedBilling.Servers`); `Server.Production.{Us|Eu}.Site` (subdomain) and `.BaseUrl` template (override point, literal URL used as-is). `Environment` is captured at construction.

Enums: SubscriptionState {active, trialing, on_hold, paused, canceled, past_due, trial_ended, expired, ...}; CollectionMethod {automatic, remittance, prepaid, invoice}; IntervalUnit {day, month}.

### CROSS-OPERATION INVARIANTS
| Invariant | Operations | Enforced where |
| --- | --- | --- |
| Subscribe/Switch plan handle must be a plan this app offers (product in an `eshop-brand-{id}` family whose brand exists in catalog), not any Maxio product | `CreateSubscription`/`MigrateSubscriptionProduct` ← `ListProductFamilies`+`ListProductsForProductFamily` (+catalog brands) | `MaxioGateway.FindOfferedPlanAsync`, before the write |
| Subscription id acted on by a shopper must belong to the caller | Pause/Resume/Cancel/Reactivate/Migrate/ListUsages/ReadSubscription ← customer reference set at `CreateSubscription` | `MaxioGateway.GetOwnedSubscriptionAsync`, before the write |
| Plan brand must exist in catalog | `CreateProduct` ← `CatalogBrands` | admin create endpoint, before `ProductFamilies` writes |
| `{planId}` for currency prices must be an offered plan | `CreateProductCurrencyPrices` ← product listing | admin endpoint via `FindOfferedPlanByIdAsync` |

### Trap notes
- Retries: SDK resends GET/HEAD/PUT/OPTIONS but not POST; per-attempt timeout default 100s. MUST load dotnet-configuration-resilience.
- Drifted/malformed bodies → `ResponseDeserializationException` (an `SdkException`, not `ApiException<T>`). MUST load dotnet-error-handling.
- Request records carry `required` members; AnyOf/Enums are not plain types. MUST load dotnet-models.

### REQUIRED READING
maxio:dotnet-getting-started · all steps; maxio:dotnet-client-initialization · DI; maxio:dotnet-authentication · BasicAuth; maxio:dotnet-calling-endpoints · calls; maxio:dotnet-models · AnyOf/enums; maxio:dotnet-error-handling · catch ladder; maxio:dotnet-configuration-resilience · timeouts/retries; maxio:dotnet-testing · tests.
Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 3. PRODUCTION READINESS
| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioOptions` bound from `Maxio:`; startup throws if `ApiKey` blank, or `Subdomain` blank while `BaseUrl` blank; `Environment` must parse to US/EU. |
| 2 | Secret sourcing | .NET user-secrets (dev) / `Maxio__*` env in deployment; options built once at registration into a singleton client; rotation needs restart. |
| 3 | Timeout budget | `Retry.Timeout`=20s per attempt, `MaxRetries`=2 (retries only on GET/PUT); worst-case ≈ 65s on GET/PUT, ≤20s on POST; enforced by SDK options. |
| 4 | Write-retry ownership | POSTs (create sub, cancel, pause, resume, migrate, create product, currency prices) are never resent by the SDK; `UpdateSubscription` (PUT next billing date) may be resent — idempotent (same absolute date). |
| 5 | Idempotency/ambiguous writes | No real keys on any request record (injected `Idempotency-Key` is not one). Subscribe: reconcile by re-reading customer subscriptions? Subscribe is not guarded against duplicates because holding several subscriptions to the same plan is allowed by the task; ambiguous failure → 502 with message to check list. Customer create race: on 422 re-read by reference. |
| 6 | Observability | `ILogger` warnings with Maxio HTTP status + operation name on failures; never the key. |
| 7 | Sensitive data | No card data, no request bodies logged; `LogRequestBody` off, SDK logging left at default (no `MAXIO_ADVANCED_BILLING_LOG` set). |
| 8 | Environment selection | `Production` group only (`Ebb` unused); `Maxio:Environment` selects US/EU; sandbox site via subdomain; `Maxio:BaseUrl` verbatim override. |
| 9 | Duplicate prevention | No local store exists for claims; the task permits several subscriptions → none. Plan creation: duplicate name/handle check against Maxio → 409. |
| 10 | Partial results | Product/usage/invoice listings are walked until a short page; no cap. |
| 11 | Unknown outcomes | Connection failure after a write → 502 stating outcome unknown; switch-on-cancelled does reactivate→migrate and on migrate failure re-cancels (best effort). |

DUPLICATE CLAIMS: none (several subscriptions per shopper allowed; no local claim store).
PAGED READS: products/usages/invoices — walk to a short page, no cap — n/a.
UNKNOWN OUTCOMES: subscribe/other writes → 502 "outcome unknown", caller re-reads subscription list; settled by the user re-reading (`GET /api/subscriptions/{id}`); no write whose reference we can search exists beyond the customer reference.

## 4. Assumptions & Blockers (settled after implementation)
- Blockers: none in code. Verified live on the sandbox: plans, subscribe, read (`paid`), pause/resume, cancel/restart, switch (incl. cancelled), usage for a day, next billing date, authorization. **Not verifiable here:** currency prices — the sandbox site has no extra site-level currency ("Currency must be defined on the site level"), so `MaxioGateway.AddCurrencyPriceAsync` answers 422 with that reason; the success path is unexercised.
- A "plan" is a Maxio product at its default price point, 1 month interval; price in major units (decimal, ≤2 dp) → cents. Implemented in `MaxioGateway.CreatePlanAsync`.
- Currency price: the SDK `CreateProductCurrencyPrice.Price` is an `int`, so `AddCurrencyPriceAsync` rejects non-whole prices with 400 (UNVERIFIED unit semantics — could not be confirmed live).
- Subscribe uses `CollectionMethod.Remittance` (no card); `paid` = `BalanceInCents <= 0` and no open invoice with `DueAmount > 0` (`MaxioGateway.OwesMoneyAsync`). Verified: paid plan → `false`, free plan → `true`.
- YOUR CALL settled: next billing date is sent as 12:00 UTC on the date (`MaxioGateway.SetNextBillingDateAsync`) — sandbox site is UTC+5 and returned the requested calendar day.
- YOUR CALL settled: usage day filter = `since_date=D`, `until_date=D+1`, then keep `CreatedAt` calendar day == D (`MaxioGateway.UsageForDayAsync`); sums metered components only.
- YOUR CALL settled: switching a cancelled subscription reactivates it then migrates; if the migration fails, it is re-cancelled best-effort (`MaxioGateway.SwitchAsync`).
- YOUR CALL settled: shopper-facing foreign/unknown subscription ids answer 404; state conflicts answer 409; Maxio rejections 422; Maxio outage 502/503/504 (`MaxioGateway.CallAsync`, `BillingException` via `ExceptionMiddleware`).
- Plan↔brand link: Maxio product family per brand with handle `eshop-brand-{brandId}` (`MaxioGateway.EnsureBrandFamilyAsync`); no local persistence.
- Duplicate subscribe: a shopper may hold several subscriptions (even to one plan) per the task, so no claim store (DUPLICATE CLAIMS: none).
- The SDK `ProjectReference` in `PublicApi.csproj` points into this machine's plugin install directory; a teammate/CI needs the plugin installed and the reference re-pointed.
