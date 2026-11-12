# Maxio Advanced Billing plan (eShopOnWeb `src/PublicApi`)

SDK root (plugin-relative): `sdk/dotnet/` (map: `sdk/dotnet/sdk-map.md`). Plan written by the main agent (planner agent not listed in this host).

## 1. Scope & sequence
1. Config: bind `Maxio:ApiKey|Subdomain|Environment|BaseUrl`; fail fast at startup if ApiKey/Subdomain/Environment blank; client singleton in DI. Secrets in user-secrets only.
2. `GET /api/plans` — Products.ListProducts (paged) → non-archived plans (+ ProductFamilies.ListProductFamilies not needed; `Product.ProductFamily` is embedded).
3. `POST /api/subscriptions` — plan handle must be in the offered set (ListProducts) → Customers.ReadCustomerByReference (reference = JWT name) / Customers.CreateCustomer → Subscriptions.CreateSubscription (no card: remittance/invoice collection).
4. `GET /api/subscriptions/{id}` — Subscriptions.ReadSubscription + ownership check; `paid` from balance.
5. pause/resume — SubscriptionStatus.PauseSubscription / ResumeSubscription.
6. cancel/restart — SubscriptionStatus.CancelSubscription / ReactivateSubscription.
7. switch — SubscriptionProducts.MigrateSubscriptionProduct (reactivate first if canceled).
8. usage for a day — SubscriptionComponents.ListSubscriptionComponents (metered only) → ListUsages per component, since_date=D, until_date=D+1.
9. admin new plan — catalog brand check (IRepository<CatalogBrand>) → ProductFamilies.ListProductFamilies/CreateProductFamily (family == brand) → Products.CreateProduct.
10. admin currency price — Products.ReadProduct → ProductPricePoints.CreateProductCurrencyPrices on the product's default price point.
11. admin next billing date — Subscriptions.UpdateSubscription `NextBillingAt`, then ReadSubscription to verify (`CurrentPeriodEndsAt`).

## 2. CONTRACT SHEET
⚠ Signatures are generated code, verbatim: each operation taking input takes ONE request record as first parameter, built by object initializer with the record's own property names.
⚠ Every SDK type is fully qualified by the namespace its source path implies (Models → `MaxioAdvancedBilling.Models`, enums → `.Models.Enums`, AnyOf → `.Models.AnyOf`, requests → `.Requests.<Controller>`, errors → `.Errors`).

| Operation | Request record (required) | Body / response | Error case | Source |
| --- | --- | --- | --- | --- |
| `Products.ListProducts` | `Requests.Products.ListProductsRequest` (none required; `Page`, `PerPage`, `IncludeArchived`) | `IReadOnlyList<ProductResponse>`; `.Product` | B (`RawError`) | map/operations/Products.md |
| `Products.ReadProductByHandle` | `ReadProductByHandleRequest` (`ApiHandle`) | `ProductResponse` | B | Products.md |
| `Products.ReadProduct` | `ReadProductRequest` (`ProductId`) | `ProductResponse` | B | Products.md |
| `Products.CreateProduct` | `CreateProductRequest` (`ProductFamilyId: string`; `Body`) | `CreateOrUpdateProductRequest{Product: CreateOrUpdateProduct{Name, Description, PriceInCents, Interval, IntervalUnit required; Handle?}}` → `ProductResponse` | A `CreateProductError.TryGetErrorListResponse1` [422] | Products.md, Models/CreateOrUpdateProduct.cs |
| `ProductFamilies.ListProductFamilies` | `ListProductFamiliesRequest` (none) | `IReadOnlyList<ProductFamilyResponse>` (`.ProductFamily?`) | B | ProductFamilies.md |
| `ProductFamilies.CreateProductFamily` | `CreateProductFamilyOperationRequest{Body}` | `CreateProductFamilyRequest{ProductFamily: CreateProductFamily{Name required, Handle?}}` → `ProductFamilyResponse` | A [422] | ProductFamilies.md |
| `ProductPricePoints.CreateProductCurrencyPrices` | `CreateProductCurrencyPricesOperationRequest{ProductPricePointId:int; Body}` | `CreateProductCurrencyPricesRequest{CurrencyPrices: IReadOnlyList<CreateProductCurrencyPrice>}` → `CurrencyPricesResponse` | A `TryGetErrorArrayMapResponse1` [422] | ProductPricePoints.md |
| `Customers.ReadCustomerByReference` | `ReadCustomerByReferenceRequest{Reference}` | `CustomerResponse` | B (404 via `RawError.StatusCode`) | Customers.md |
| `Customers.CreateCustomer` | `CreateCustomerOperationRequest{Body}` | `CustomerResponse` | A [422] | Customers.md |
| `Subscriptions.CreateSubscription` | `CreateSubscriptionOperationRequest{Body: CreateSubscriptionRequest{Subscription: CreateSubscription{ProductHandle, CustomerId/CustomerReference, PaymentCollectionMethod}}}` | `SubscriptionResponse.Subscription` | A [422] | Subscriptions.md |
| `Subscriptions.ReadSubscription` | `ReadSubscriptionRequest{SubscriptionId:int}` | `SubscriptionResponse` (`Customer.Reference`, `State`, `BalanceInCents`, `Product`) | B | Subscriptions.md |
| `Subscriptions.UpdateSubscription` | `UpdateSubscriptionOperationRequest{SubscriptionId; Body: UpdateSubscriptionRequest{Subscription: UpdateSubscription{NextBillingAt: DateTimeOffset?}}}` | `SubscriptionResponse` | A [422] | Subscriptions.md |
| `SubscriptionStatus.PauseSubscription` | `PauseSubscriptionRequest{SubscriptionId}` | `SubscriptionResponse` | A [422] | SubscriptionStatus.md |
| `SubscriptionStatus.ResumeSubscription` | `ResumeSubscriptionRequest{SubscriptionId}` | `SubscriptionResponse` | A [422] | SubscriptionStatus.md |
| `SubscriptionStatus.CancelSubscription` | `CancelSubscriptionRequest{SubscriptionId; Body?}` | `SubscriptionResponse` | A [404, 422] | SubscriptionStatus.md |
| `SubscriptionStatus.ReactivateSubscription` | `ReactivateSubscriptionOperationRequest{SubscriptionId; Body?}` | `SubscriptionResponse` | A [422] | SubscriptionStatus.md |
| `SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProductRequest{SubscriptionId; Body: SubscriptionProductMigrationRequest{Migration{ProductHandle,...}}}` | `SubscriptionResponse` | A [422] | SubscriptionProducts.md |
| `SubscriptionComponents.ListSubscriptionComponents` | `ListSubscriptionComponentsRequest{SubscriptionId}` | `IReadOnlyList<SubscriptionComponentResponse>` (`.Component.Kind`, `.ComponentId`) | A/B per page | SubscriptionComponents.md |
| `SubscriptionComponents.ListUsages` | `ListUsagesRequest{SubscriptionIdOrReference, ComponentId, SinceDate, UntilDate, Page, PerPage}` | `IReadOnlyList<UsageResponse>` (`.Usage.Quantity: AnyOf.Quantity1` int/string) | per map | SubscriptionComponents.md |

Enums: `SubscriptionState` {active, canceled, on_hold, trialing, past_due, unpaid, ...}; `CollectionMethod` {automatic, remittance, prepaid, invoice}; `ComponentKind.MeteredComponent`; `IntervalUnit.Month`.

Client: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; `options.BasicAuth = new BasicAuthCredentials{Username=<api key>, Password="x"}`; `options.Environment = ServerEnvironment.Us|Eu` (`MaxioAdvancedBilling.Servers`); base URL override `options.Server.Production.Us.BaseUrl` / `.Eu.BaseUrl`; the site subdomain is substituted in `{site}` (see Servers & auth).

### CROSS-OPERATION INVARIANTS
| Invariant | Operations | Enforced where |
| --- | --- | --- |
| plan handle at subscribe/switch must be a plan this app offers | `CreateSubscription`/`MigrateSubscriptionProduct`/`UpdateSubscription` ← `ListProducts` (non-archived, no card required) | `MaxioBillingService.FindOfferedPlanAsync`, before the write |
| subscription id on every shopper op must belong to the JWT caller | `Pause/Resume/Cancel/Reactivate/Migrate/ListUsages/ReadSubscription` ← `CreateSubscription` stored `Customer.Reference` | `MaxioBillingService.GetOwnedAsync`, before the write (others' subscriptions answer 404) |
| plan brand must be a catalog brand | `CreateProductFamily`/`CreateProduct` ← `IRepository<CatalogBrand>.ListAsync` | `CreatePlanEndpoint.HandleAsync`, before `CreatePlanAsync` |
| currency price targets the product's default price point | `CreateProductCurrencyPrices` ← `ReadProduct` (`DefaultProductPricePointId`) | `MaxioBillingService.AddCurrencyPriceAsync`, before the write |

## 3. Trap notes
- Writes can be resent by the SDK; a non-GET timeout may have succeeded — MUST load dotnet-configuration-resilience.
- Typed vs raw errors, deserialization drift — MUST load dotnet-error-handling.
- Usage window: `until_date` is exclusive at midnight; the page size caps at 200 — rules `subscription-components` ListUsages.
- Enums are not C# enums — MUST load dotnet-models.

## 4. REQUIRED READING
- maxio:dotnet-client-initialization · DI/client setup
- maxio:dotnet-authentication · Basic auth credentials
- maxio:dotnet-calling-endpoints · request records
- maxio:dotnet-models · enums/unions
- maxio:dotnet-error-handling · try/catch (hazard row: a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).)
- maxio:dotnet-configuration-resilience · retries/timeouts/base URL
- maxio:dotnet-testing · tests

## 5. PRODUCTION READINESS
| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` validated at startup: ApiKey, Subdomain, Environment non-blank (every part) or host refuses to start. |
| 2 | Secret sourcing & rotation | `IConfiguration` (user-secrets in dev; env vars `Maxio__*` elsewhere); options built once at registration; rotation needs a restart. |
| 3 | Total timeout budget | SDK default retry/timeout kept; per-call bound by the request cancellation token; 120 s Maxio cut-off. |
| 4 | Write-retry ownership | SDK default retry list (idempotent verbs only); app never loops writes itself. |
| 5 | Idempotency & ambiguous writes | No real key on any request record; reconciliation = re-read the subscription/product before reporting. |
| 6 | Observability | Warning on Maxio 4xx/5xx with status + operation; no bodies logged. |
| 7 | Sensitive data | No card data handled; `LogRequestBody` off. |
| 8 | Environment selection | `Maxio:Environment` (US/EU) → `ServerEnvironment`; the sandbox is a site (`Maxio:Subdomain`), `Maxio:BaseUrl` overrides verbatim on both Production-group nodes. |
| 9 | Duplicate prevention | Task allows several subscriptions per plan; no app-owned claim store is kept (see DUPLICATE CLAIMS). |
| 10 | Partial results | List reads page to exhaustion (`PerPage=200`) with a hard page cap; cap hit → 502 with reason rather than a silent truncation. |
| 11 | Unknown outcomes | Connection/timeout failure on a write → re-read subscription state and answer from it; else 504 with reason. |

**DUPLICATE CLAIMS**: none — the task defines multiple subscriptions to the same plan as legitimate; creation conflicts surface from Maxio (422 → 4xx).

**PAGED READS**
| Read | What caps it | How the caller learns | Where in the code |
| --- | --- | --- | --- |
| ListProducts / ListUsages | page cap 50 × 200 | 502 `BillingException` ("list is too long to be returned/summed completely") instead of a silent truncation | `MaxioBillingService.ListPlansAsync`, `MaxioBillingService.UsageForDayAsync` |

**UNKNOWN OUTCOMES**
| Write | Re-read with | Reference | Where | Test |
| --- | --- | --- | --- | --- |
| subscription writes (pause/resume/cancel/restart/switch/next-billing-date) | `Subscriptions.ReadSubscription` | subscription id | `MaxioBillingService.WriteAsync` (catches `BillingException.OutcomeUnknown` set by `MaxioErrors.Translate` for connection/timeout) | `BillingEndpointsTest.ReportsAnUnreachableBillingSystemAsAGatewayError` (connection failure → 502; read-back path shares `WriteAsync`) |

## 6. Assumptions & Blockers (decisions the code made)
- Shopper identity = JWT `ClaimTypes.Name`, stored as Maxio customer `reference` (`MaxioBillingService.EnsureCustomerAsync`); someone else's subscription answers 404 (`GetOwnedAsync`).
- Card-free collection: `remittance` on Relationship Invoicing sites, `invoice` otherwise, read from `Sites.ReadSite` (`CardFreeCollectionMethodAsync`). Plans with `require_credit_card` are not offered (`ListPlansAsync`).
- Plan "brand" = Maxio product family with handle `catalog-brand-{brandId}`, created on demand (`EnsureBrandFamilyAsync`). Plan handle = slug of the name; a taken handle answers 409.
- `paid` = subscription balance <= 0 (`ToDto`); on the sandbox a fresh remittance subscription shows its open invoice as balance.
- Switch: active/trialing -> `MigrateSubscriptionProduct`; any other state (cancelled, paused ...) -> `UpdateSubscription` product change in place, which keeps a cancelled subscription cancelled on the new plan (`SwitchPlanAsync`); the new plan is verified from the answer/read-back.
- Usage: sum of quantities of the subscription's metered components created on that calendar day in the site's own offset; the Maxio window is widened by a day each side and filtered locally (`UsageForDayAsync`).
- Next billing date: sent as noon in the site's offset (taken from Maxio's own timestamps), at least 24h ahead, only for billable states, verified by re-read via `current_period_ends_at` (`SetNextBillingDateAsync`).
- Currency prices: the SDK types the price as `int`, so only whole currency units are accepted; the currency must be enabled on the Maxio site. **Observed limit:** the sandbox site has no non-primary currency enabled, so this endpoint answers 422 with that reason there (it cannot be enabled through the SDK).
- The SDK is referenced by a machine-local `ProjectReference` into the plugin install directory (see dotnet-getting-started); re-point it on another machine.
