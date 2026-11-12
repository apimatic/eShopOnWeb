# Maxio Advanced Billing plan — eShopOnWeb `src/PublicApi`

SDK source: `sdk/dotnet/` of the maxio plugin (`MaxioAdvancedBilling`, referenced as a ProjectReference from `PublicApi.csproj`). Written by the main agent (the planner agent is not offered in this host).

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | Settings/DI: bind `Maxio:*`, fail-fast, singleton client | — |
| 2 | Plans list (`GET /api/plans`) | `Products.ListProducts` |
| 3 | Subscribe | `Customers.ReadCustomerByReference`, `Subscriptions.CreateSubscription`, `Subscriptions.FindSubscription` (reconcile) |
| 4 | Subscription page + `paid` | `Subscriptions.ReadSubscription`, `Invoices.ListInvoices` |
| 5 | Pause / resume | `SubscriptionStatus.PauseSubscription`, `ResumeSubscription` |
| 6 | Cancel / restart | `SubscriptionStatus.CancelSubscription`, `ReactivateSubscription` |
| 7 | Switch plan | `SubscriptionProducts.MigrateSubscriptionProduct` (active/trialing), `Subscriptions.UpdateSubscription` (all other states, incl. canceled) |
| 8 | Usage for a day | `SubscriptionComponents.ListSubscriptionComponents`, `ListUsages` |
| 9 | Admin: new plan | `ProductFamilies.ListProductFamilies`/`CreateProductFamily`, `Products.CreateProduct` |
| 10 | Admin: currency price | `Products.ReadProduct`, `ProductPricePoints.ReadProductPricePoint`, `CreateProductCurrencyPrices`/`UpdateProductCurrencyPrices` |
| 11 | Admin: next billing date | `Subscriptions.ReadSubscription`, `Subscriptions.UpdateSubscription` |

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim: each operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names — never flat arguments.
⚠ Every SDK type is written fully-qualified with the namespace its source path implies (`MaxioAdvancedBilling.Models`, `.Models.Enums`→ same `MaxioAdvancedBilling.Models` namespace per file header, `.Requests.<Controller>`, `.Errors`, `.Core.Exceptions`, `.Core.ErrorResponse`, `.Servers`), taken from the path of THAT type.

| Operation | Request record (members) | Body / response fields read | Error | Source |
| --- | --- | --- | --- | --- |
| `client.Products.ListProducts(ListProductsRequest)` | `Page`, `PerPage` (≤200); archived excluded by default | `ProductResponse.Product`: `Id, Name, Handle, Description, PriceInCents, Interval, IntervalUnit, RequireCreditCard, ArchivedAt, ProductFamily, DefaultProductPricePointId` | B (`RawError`); paged by `Page` — empty page ends | `map/operations/Products.md` |
| `client.Customers.ReadCustomerByReference(ReadCustomerByReferenceRequest)` | `Reference` (required) | `CustomerResponse.Customer.Id` | B; 404 = absent | `map/operations/Customers.md` |
| `client.Subscriptions.CreateSubscription(CreateSubscriptionOperationRequest)` | `Body: CreateSubscriptionRequest{ Subscription: CreateSubscription }` — `ProductHandle`, `PaymentCollectionMethod`, `CustomerId` / `CustomerAttributes`, `Reference` | `SubscriptionResponse.Subscription` | A (`CreateSubscriptionError`) | `map/operations/Subscriptions.md` |
| `client.Subscriptions.FindSubscription(FindSubscriptionRequest)` | `Reference` | `SubscriptionResponse` | A | same |
| `client.Subscriptions.ReadSubscription(ReadSubscriptionRequest)` | `SubscriptionId` (req.) | `Subscription`: `Id, State, BalanceInCents, ProductPriceInCents, CurrentPeriodEndsAt, CanceledAt, Customer.Reference, Product, Currency, PaymentCollectionMethod` | per map page | same |
| `client.Invoices.ListInvoices(ListInvoicesRequest)` | `SubscriptionId`, `Page`, `PerPage` | `ListInvoicesResponse.Invoices[]`: `Status`, `DueAmount` (string decimal) | per map page | `map/operations/Invoices.md` |
| `client.SubscriptionStatus.PauseSubscription / ResumeSubscription / CancelSubscription / ReactivateSubscriptionOperationRequest` | `SubscriptionId` (req.) | `SubscriptionResponse` | A | `map/operations/SubscriptionStatus.md` |
| `client.SubscriptionProducts.MigrateSubscriptionProduct` | `SubscriptionId`; `Body: SubscriptionProductMigrationRequest{ Migration: { ProductHandle } }` | `SubscriptionResponse` | A | `map/operations/SubscriptionProducts.md` |
| `client.Subscriptions.UpdateSubscription` | `SubscriptionId`; `Body: UpdateSubscriptionRequest{ Subscription: UpdateSubscription{ ProductHandle \| NextBillingAt } }` | `SubscriptionResponse` | A | `Models/UpdateSubscription.cs` |
| `client.SubscriptionComponents.ListSubscriptionComponents` | `SubscriptionId` | `SubscriptionComponentResponse.Component`: `ComponentId, Name, Kind, UnitName` | per map page | `map/operations/SubscriptionComponents.md` |
| `client.SubscriptionComponents.ListUsages(ListUsagesRequest)` | `SubscriptionIdOrReference`, `ComponentId` (both required, AnyOf int/string), `SinceDate`/`UntilDate` (sent date-only), `Page`, `PerPage` ≤200 | `UsageResponse.Usage`: `Id, Quantity (Quantity1 int/string), CreatedAt, Memo` | per map page | same |
| `client.ProductFamilies.ListProductFamilies / CreateProductFamily` | `CreateProductFamilyOperationRequest.Body` | `ProductFamily.Id, Handle` | A / B | `map/operations/ProductFamilies.md` |
| `client.Products.CreateProduct(CreateProductRequest)` | `ProductFamilyId` (req.); `Body: CreateOrUpdateProductRequest{ Product: { Name, Description, PriceInCents, Interval, IntervalUnit, RequireCreditCard } }` | `ProductResponse.Product` | A | `Models/CreateOrUpdateProduct.cs` |
| `client.ProductPricePoints.CreateProductCurrencyPrices / UpdateProductCurrencyPrices` | `ProductPricePointId`; `CurrencyPrices[]: { Currency, Price: int, Role }` | `CurrencyPricesResponse` | per map page | `map/operations/ProductPricePoints.md` |

Enums: `CollectionMethod.Remittance` (Relationship-Invoicing site: automatic/remittance/prepaid), `SubscriptionState` (`active, trialing, on_hold, canceled, past_due, unpaid, …`), `InvoiceStatus` (`open, paid, …`), `CurrencyPriceRole` (`baseline, trial, initial`), `ComponentKind`.

Client: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions)`; auth `BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials{ Username = <api key>, Password = "x" }`; `Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us|Eu`; `Server.Production.Us|Eu.Site` = subdomain; `Server.Production.Us|Eu.BaseUrl` is the override point (`Maxio:BaseUrl` is applied verbatim there).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| A plan handle accepted by subscribe/switch must be one the shopper may subscribe to (listed by `GET /api/plans`: non-archived, `RequireCreditCard != true` since no card is collected) | `CreateSubscription`/`MigrateSubscriptionProduct`/`UpdateSubscription` ← `ListProducts` | `MaxioBillingService.RequireOfferedPlanAsync`, before the write |
| A subscription id acted on by a shopper must belong to the shopper's Maxio customer (`Customer.Reference` = JWT name) | every shopper write/read ← `CreateSubscription` (which stamped the customer reference) | `MaxioBillingService.GetOwnedSubscriptionAsync`, before any call |
| A plan's brand must be a brand of the shop's catalog | `CreateProductFamily`/`CreateProduct` ← `IRepository<CatalogBrand>` | `MaxioBillingService.CreatePlanAsync`, before any Maxio write |
| Usage components queried must be those on the subscription | `ListUsages` ← `ListSubscriptionComponents` | `MaxioBillingService.GetUsageAsync` |

## 3. Trap notes

- Pause is refused within 24 h of the next billing date and for non-eligible states; reactivate/migrate need specific states (business rules `subscription-status`, `subscription-products`) — MUST load `dotnet-error-handling`.
- Migrate is only legal for active/trialing; canceled/on-hold need the update path — MUST load `dotnet-calling-endpoints`.
- `ListUsages` `UntilDate` is inclusive to midnight and sent date-only; timestamps carry the site's offset — MUST load `dotnet-models`.
- Retries of non-idempotent writes — MUST load `dotnet-configuration-resilience`.
- Date-only values are interpreted at noon site time by Maxio; the response of a billing-date change has no `next_billing_at` (read `CurrentPeriodEndsAt`) — MUST load `dotnet-models`.

## 4. REQUIRED READING

- `maxio:dotnet-error-handling` · every endpoint's catch ladder
- `maxio:dotnet-client-initialization` · DI/client setup
- `maxio:dotnet-authentication` · basic auth credentials
- `maxio:dotnet-calling-endpoints` · request records
- `maxio:dotnet-models` · AnyOf/enum/DateTimeOffset
- `maxio:dotnet-configuration-resilience` · retries/timeouts/logging
- `maxio:dotnet-testing` · tests

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` bound via `IConfiguration` (user-secrets in dev); `ValidateOnStart` refuses to start when any is missing/blank or the environment is not US/EU. |
| 2 | Secret sourcing & rotation | .NET user-secrets / environment-provided configuration; client options are built once and captured by the singleton, so rotation needs a restart. |
| 3 | Total timeout budget | SDK retry options kept at defaults with `Retry.Timeout` per attempt; whole call also bounded by the request-abort token passed from the endpoint (`CancellationToken`) — HttpClient timeout 100 s default. |
| 4 | Write-retry ownership | SDK retries only its configured idempotent verbs/statuses (defaults); the app never loops over writes itself. |
| 5 | Idempotency & ambiguous writes | No write has a caller key (`Idempotency-Key` header is not one). Subscribe stamps its own `Reference` (GUID) and reconciles with `FindSubscription` after an ambiguous failure; others are state-guarded (pause/resume/cancel/restart reject wrong state; switch rejects same plan; billing-date set is naturally idempotent). |
| 6 | Observability | `ILogger`: warning with Maxio HTTP status + body text on 4xx/5xx; no API key and no bodies logged. |
| 7 | Sensitive data | No card data is handled (remittance). `LogRequestBody` stays off; `LoggerFactory` set explicitly. Customer email is sent. |
| 8 | Environment selection | Production server group only (`https://{site}.chargify.com` US / `.ebilling.maxio.com` EU) chosen by `Maxio:Environment`; `Maxio:BaseUrl` overrides verbatim. Sandbox = the sandbox site's subdomain; the Ebb group is not touched. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | Paged reads loop until an empty/short page; no cap, so none is cut short. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Subscribe (`CreateSubscription`) | none — this codebase has no store for claims that outlives the process; several subscriptions to the same plan are allowed by the task. The write carries a unique `Reference`. | n/a (accepted risk — Blocker-free: duplicates are visible and cancellable) | n/a | `MaxioBillingService.SubscribeAsync` |

**PAGED READS**

| Read | What caps it | How the caller learns it was cut short | Where in the code |
| --- | --- | --- | --- |
| Plans, invoices, usages | none (loop until a short/empty page) | not capped | `MaxioBillingService.ListAllProductsAsync`, `…IsPaidAsync`, `…GetUsageAsync` |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| Subscribe | `Subscriptions.FindSubscription` | `Reference` (GUID) | `MaxioBillingService.SubscribeAsync` catch of `SdkConnectionException`/`SdkTimeoutException` | `BillingEndpointsTest.BaseUrlOverrideIsUsedVerbatimAndAnUnreachableBillingSystemIsReported` (unreachable `Maxio:BaseUrl` → 502; the reconciliation branch itself is not exercised) |
| Pause/resume/cancel/restart/switch/next-billing/plan create | re-read subscription/product state | id | endpoint returns 502/504 with reason; the caller re-reads via `GET` | — |

## 6. Assumptions & Blockers

- **Brand → product family** (Maxio products have no brand field): one family per brand, handle `catalog-brand-{brandId}`, name = brand name (matches families already on the sandbox); created on demand. YOUR CALL — settled in `MaxioBillingService.EnsureBrandFamilyAsync`.
- **Customer identity**: the JWT `ClaimTypes.Name` is the Maxio customer `reference`; the customer is created on first subscribe (email = name when it is an email). YOUR CALL — `MaxioBillingService.EnsureCustomerAsync`.
- **No card details** → `payment_collection_method = remittance` (invoice) and plans that require a card are not offered.
- **Switching a canceled/on-hold subscription** uses `UpdateSubscription` product change (migration is refused outside active/trialing); the state is unchanged.
- **Currency prices**: the SDK types `price` as `int`, so only whole currency units can be sent; the sandbox site has no non-primary currency configured, so Maxio refuses such requests with a 422 ("Currency must be defined on the site level.") that the endpoint relays — verified against the sandbox. An existing currency price is updated (decimal allowed) instead of created.
- **Plan handle**: Maxio returns no handle for a product created without one, so `CreatePlanAsync` sends a slug of the name (`MaxioBillingService.Slugify`). YOUR CALL — settled there.
- **Billing date**: a date-only value is sent as noon at the site's offset and verified by re-reading `CurrentPeriodEndsAt` (`SetNextBillingDateAsync`); only active/trialing subscriptions and future dates are accepted.
- **Restart** reactivates with Maxio's defaults (new billing period, charged by invoice).
- **Whole-day usage** is read per component, window widened by a day each side and filtered to the site-local date of each usage's `CreatedAt`.
