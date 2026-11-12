# Maxio Advanced Billing integration plan (written by the main agent — the planner agent is not listed in this host)

SDK source: `sdk/dotnet/` inside the maxio plugin (referenced in place via `<ProjectReference>`; never copied). Namespaces: client `MaxioAdvancedBilling`; models `.Models`; enums `.Models.Enums`; AnyOf `.Models.AnyOf`; requests `.Requests.<Controller>`; errors `.Errors`; core `.Core.Exceptions`, `.Core.ErrorResponse`, `.Core.Authentication.Basic`; servers `.Servers`.

## 1. Scope & sequence
1. Settings + client DI (`Maxio:ApiKey|Subdomain|Environment|BaseUrl`), fail-fast at start.
2. Plans (`Products.ListProducts` + brand mapping) → `GET /api/plans`.
3. Customer resolution (`Customers.ReadCustomerByReference`/`CreateCustomer`) keyed by JWT user name.
4. Subscribe (`Subscriptions.CreateSubscription`), read (`Subscriptions.ReadSubscription`), status ops (`SubscriptionStatus.*`), switch (`SubscriptionProducts.MigrateSubscriptionProduct`), usage (`SubscriptionComponents.ListSubscriptionComponents` + `ListUsages`).
5. Staff: create plan (`ProductFamilies.ListProductFamilies`/`CreateProductFamily`, `Products.CreateProduct`), currency price (`ProductPricePoints.CreateProductCurrencyPrices`), next billing date (`Subscriptions.UpdateSubscription`).

## 2. CONTRACT SHEET
⚠ Signatures are generated code, verbatim: each operation takes ONE request record as first parameter, built with an object initializer using the record's own property names.
⚠ Every SDK type is written fully-qualified with the namespace its source path implies.

| Op | Request record (members) | Body / response | Error case | Source |
| --- | --- | --- | --- | --- |
| `Products.ListProducts` | `ListProductsRequest{Page,PerPage,IncludeArchived}` (none required) | `IReadOnlyList<ProductResponse>` → `.Product{Id,Handle,Name,PriceInCents,Interval,IntervalUnit,ArchivedAt,ProductFamily{Id,Name,Handle},DefaultProductPricePointId,ProductPricePointId}` | B | map/operations/Products.md |
| `Products.ReadProductByHandle` | `ReadProductByHandleRequest{ApiHandle*}` | `ProductResponse` | B | Products.md |
| `Products.CreateProduct` | `CreateProductRequest{ProductFamilyId*: string, Body}` | `CreateOrUpdateProductRequest{Product: CreateOrUpdateProduct{Name*,Description*,PriceInCents*:long,Interval*:int,IntervalUnit*,Handle,RequireCreditCard}}` → `ProductResponse` | A (`TryGetErrorListResponse1` 422) | Products.md |
| `ProductFamilies.ListProductFamilies` | `ListProductFamiliesRequest{}` | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily{Id,Name,Handle}` | B | ProductFamilies.md |
| `ProductFamilies.CreateProductFamily` | `CreateProductFamilyOperationRequest{Body}` | `CreateProductFamilyRequest{ProductFamily: CreateProductFamily{Name*,Handle,Description}}` → `ProductFamilyResponse` | A (422) | ProductFamilies.md |
| `ProductPricePoints.CreateProductCurrencyPrices` | `CreateProductCurrencyPricesOperationRequest{ProductPricePointId*:int, Body}` | `CreateProductCurrencyPricesRequest{CurrencyPrices*: IReadOnlyList<CreateProductCurrencyPrice{Currency*,Price*:int,Role*: CurrencyPriceRole}>}` → `CurrencyPricesResponse` | A (`TryGetErrorArrayMapResponse1` 422) | ProductPricePoints.md |
| `Customers.ReadCustomerByReference` | `ReadCustomerByReferenceRequest{Reference*}` | `CustomerResponse.Customer{Id,Reference,Email}` | B (404 = absent) | Customers.md |
| `Customers.CreateCustomer` | `CreateCustomerOperationRequest{Body}` | `CreateCustomerRequest{Customer: CreateCustomer{FirstName*,LastName*,Email*,Reference}}` | A (422) | Customers.md |
| `Subscriptions.CreateSubscription` | `CreateSubscriptionOperationRequest{Body}` | `CreateSubscriptionRequest{Subscription: CreateSubscription{ProductHandle,CustomerId,PaymentCollectionMethod,Reference}}` → `SubscriptionResponse.Subscription` | A (422) | Subscriptions.md |
| `Subscriptions.ReadSubscription` | `ReadSubscriptionRequest{SubscriptionId*:int}` | `SubscriptionResponse.Subscription{Id,State,BalanceInCents,Customer{Reference},Product{Handle,Name},NextAssessmentAt,CanceledAt,Currency}` | B (404) | Subscriptions.md |
| `Subscriptions.UpdateSubscription` | `UpdateSubscriptionOperationRequest{SubscriptionId*, Body}` | `UpdateSubscriptionRequest{Subscription: UpdateSubscription{NextBillingAt: DateTimeOffset?}}` | A (422) | Subscriptions.md |
| `Invoices.ListInvoices` | `ListInvoicesRequest{SubscriptionId, Status, PerPage}` | `ListInvoicesResponse.Invoices[].DueAmount/Status` | B | Invoices.md |
| `SubscriptionStatus.PauseSubscription` | `PauseSubscriptionRequest{SubscriptionId*, Body: PauseRequest?}` | `SubscriptionResponse` | A (422) | SubscriptionStatus.md |
| `SubscriptionStatus.ResumeSubscription` | `ResumeSubscriptionRequest{SubscriptionId*}` | `SubscriptionResponse` | A (422) | SubscriptionStatus.md |
| `SubscriptionStatus.CancelSubscription` | `CancelSubscriptionRequest{SubscriptionId*, Body: CancellationRequest{Subscription: CancellationOptions{CancellationMessage}}}` | `SubscriptionResponse` | A (404 `TryGetNoContent`, 422) | SubscriptionStatus.md |
| `SubscriptionStatus.ReactivateSubscription` | `ReactivateSubscriptionOperationRequest{SubscriptionId*, Body?}` | `SubscriptionResponse` | A (422) | SubscriptionStatus.md |
| `SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProductRequest{SubscriptionId*, Body: SubscriptionProductMigrationRequest{Migration: SubscriptionProductMigration{ProductHandle,...}}}` | `SubscriptionResponse` | A (422) | SubscriptionProducts.md |
| `SubscriptionComponents.ListSubscriptionComponents` | `ListSubscriptionComponentsRequest{SubscriptionId*}` | `IReadOnlyList<SubscriptionComponentResponse>` → `.Component{ComponentId,Name,Kind,UnitName}` | B | SubscriptionComponents.md |
| `SubscriptionComponents.ListUsages` | `ListUsagesRequest{SubscriptionIdOrReference*,ComponentId*,SinceDate,UntilDate,Page,PerPage(≤200)}` | `IReadOnlyList<UsageResponse>` → `.Usage{Id,Quantity,Memo,CreatedAt}`; since/until are midnight-inclusive (remarks in Api/SubscriptionComponents.cs) | B | SubscriptionComponents.md |

Client: `new MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions{BasicAuth=BasicAuthCredentials{Username=<ApiKey>,Password="x"}, Environment=ServerEnvironment.Us|Eu})`; site via `options.Server.Production.Us.Site` / `.Eu.Site`; verbatim base URL override via `options.Server.Production.{Us|Eu}.BaseUrl`. Registered via `AddMaxioAdvancedBillingClient` (singleton).

### CROSS-OPERATION INVARIANTS
| Invariant | Operations | Enforced where |
| --- | --- | --- |
| a plan handle accepted by subscribe/switch must be one the plans list offers (non-archived product in a catalog-brand family) | `CreateSubscription`/`MigrateSubscriptionProduct` ← `ListProducts` + catalog brands | `PlanCatalog.FindOfferedAsync` before the write |
| a subscription id acted on must belong to the caller | status/read/usage ← `ReadSubscription` (Customer.Reference == JWT user) | `SubscriptionOwnership` before every shopper write/read |
| a plan's brand must be one in the shop catalog | `CreateProduct` ← `IRepository<CatalogBrand>.ListAsync` | admin create endpoint before the write |
| a currency-price target plan must be an offered plan | `CreateProductCurrencyPrices` ← `ListProducts` | admin endpoint before the write |

## 3. Trap notes
- Client is a singleton capturing options at registration: MUST load dotnet-client-initialization.
- Credentials are Basic username=ApiKey, password `x`; blank part ≠ missing: MUST load dotnet-authentication.
- Request records, `required` members, AnyOf (`SubscriptionIdOrReference`, `ComponentIdModel`), OpenStringEnum: MUST load dotnet-models / dotnet-calling-endpoints.
- Failures arrive as several exception kinds: MUST load dotnet-error-handling.
- Retries resend only some verbs; ambiguous writes: MUST load dotnet-configuration-resilience.

## 4. REQUIRED READING
dotnet-error-handling (every call) · dotnet-client-initialization (DI) · dotnet-authentication (credentials) · dotnet-calling-endpoints / dotnet-models (request building) · dotnet-configuration-resilience (retries, pagination, base URL) · dotnet-testing (tests).
Hazard row: a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS
| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioOptions` validated with `ValidateOnStart`: ApiKey and Subdomain (and a valid Environment) must be non-blank, else host refuses to start. YOUR CALL — settled in `MaxioServiceCollectionExtensions`. |
| 2 | Secret sourcing & rotation | `Maxio:*` from IConfiguration (user-secrets/env); options captured once at registration; rotation needs restart. |
| 3 | Total timeout budget | SDK default per-attempt timeout + default retries; every call additionally bounded by the HTTP request's cancellation token. |
| 4 | Write-retry ownership | SDK default retries GET/HEAD/PUT/OPTIONS only; POST writes are never resent by the SDK. |
| 5 | Idempotency | No write has a real key (`Idempotency-Key` header is not one). Subscribe is guarded by an in-process claim per (customer, plan) plus a Maxio lookup of existing live subscriptions; reconciliation = list customer subscriptions by product. |
| 6 | Observability | Failures logged at Warning with Maxio status; 5xx unknowns at Error. |
| 7 | Sensitive data | No card data in scope; SDK body logging stays off (Logging defaults not widened). |
| 8 | Environment selection | Group `Production` only (Us → `chargify.com`, Eu → `ebilling.maxio.com`); `Maxio:Environment` selects, `Maxio:BaseUrl` overrides verbatim. `Ebb` group not touched. Sandbox site supplied via `Maxio:Subdomain`. |
| 9 | Duplicate prevention | see DUPLICATE CLAIMS |
| 10 | Partial results | see PAGED READS |
| 11 | Unknown outcomes | see UNKNOWN OUTCOMES |

**DUPLICATE CLAIMS**
| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Subscribe | YOUR CALL: this codebase has no durable claim store for PublicApi (catalog DB has no billing table); an in-process `ConcurrentDictionary` claim keyed by customer+plan is used, plus Maxio-side check of live subscriptions. Limitation: not cross-process. | `TryAdd` refusing | the subscribe handler returns 409 | TBD |

**PAGED READS**
| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| list products | 50-page cap × 200 | exceeding the cap throws → 502 (never silently truncated) | TBD |
| list usages | 50-page cap × 200 | same | TBD |

**UNKNOWN OUTCOMES**
| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| Subscribe | `Subscriptions.ListSubscriptions` / `Customers.ListCustomerSubscriptions` | customer id + product handle | TBD | TBD |
| other writes | `ReadSubscription` | subscription id | TBD | TBD |

## 6. Assumptions & Blockers
- Brand ↔ Maxio product family: family named `"{Brand} plans"` (matches the existing sandbox convention); created on demand by staff plan creation.
- "Subscribe without card" = `payment_collection_method: remittance` (invoice), so owing money is expressed by `balance_in_cents`/open invoices.
- Plan "monthly price": interval 1, unit month.
- No blockers.

## 7. Source labels
Rows above cite their map page; behaviours that need live traffic (cancelled-subscription migration, currency price units, remittance without card) are `UNVERIFIED` and will be settled against the sandbox.
