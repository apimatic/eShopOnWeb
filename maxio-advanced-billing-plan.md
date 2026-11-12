# Maxio Advanced Billing plan (written by the main agent: planner agent not listed in this host)

SDK root (plugin-relative): `sdk/dotnet/`. All SDK types fully qualified by the namespace their path implies.
Signatures are generated code, verbatim: each operation takes ONE request record, built with an object initializer.

## 1. Scope & sequence
1. Config: bind `Maxio:ApiKey|Subdomain|Environment|BaseUrl`; fail fast at startup; register one singleton `MaxioAdvancedBillingClient`.
2. `MaxioBilling` service (PublicApi) wrapping SDK calls and mapping SDK errors to a typed `BillingException` (status + reason) -> 4xx via middleware.
3. Endpoints 1-10 (Plans, Subscribe, Get, Pause/Resume, Cancel/Restart, Switch, Usage, Admin plan, Admin currency price, Admin next billing date).
4. Tests against the sandbox.

## 2. CONTRACT SHEET (all errors throw; no pagination object, but list ops take `Page`/`PerPage`, we loop pages ourselves)

| Op | Request record | Body | Returns | Error | Source |
| --- | --- | --- | --- | --- | --- |
| `Products.ListProducts` | `ListProductsRequest{Page,PerPage,IncludeArchived}` | - | `IReadOnlyList<ProductResponse>` (`.Product`) | B RawError | map/operations/Products.md |
| `Products.ReadProduct` | `ReadProductRequest{ProductId:int req}` | - | `ProductResponse` | B | Products.md |
| `Products.CreateProduct` | `CreateProductRequest{ProductFamilyId:string req, Body}` | `CreateOrUpdateProductRequest{Product: CreateOrUpdateProduct{Name req, Description req, PriceInCents:long req, Interval:int req, IntervalUnit req, RequireCreditCard?}}` | `ProductResponse` | A `CreateProductError`.TryGetErrorListResponse1 [422] | Models/CreateOrUpdateProduct.cs |
| `ProductFamilies.ListProductFamilies` | `ListProductFamiliesRequest{}` | - | `IReadOnlyList<ProductFamilyResponse>` | B | ProductFamilies.md |
| `ProductFamilies.CreateProductFamily` | `CreateProductFamilyOperationRequest{Body}` | `CreateProductFamilyRequest` | `ProductFamilyResponse` | A [422] | ProductFamilies.md |
| `ProductPricePoints.CreateProductCurrencyPrices` | `{ProductPricePointId:int req, Body}` | `CreateProductCurrencyPricesRequest{CurrencyPrices: list of CreateProductCurrencyPrice{Currency:string req, Price:int req, Role req}}` | `CurrencyPricesResponse` | A `TryGetErrorArrayMapResponse1` [422] | ProductPricePoints.md |
| `Customers.ReadCustomerByReference` / `CreateCustomer` | `{Reference}` / `{Body: CreateCustomerRequest{Customer{FirstName,LastName,Email req, Reference}}}` | | `CustomerResponse` | B / A [422] | Customers.md |
| `Subscriptions.CreateSubscription` | `CreateSubscriptionOperationRequest{Body: CreateSubscriptionRequest{Subscription: CreateSubscription{ProductHandle, CustomerId, PaymentCollectionMethod}}}` | | `SubscriptionResponse` (`.Subscription`) | A [422] | Models/CreateSubscription.cs |
| `Subscriptions.ReadSubscription` | `{SubscriptionId:int req}` | | `SubscriptionResponse` | B (404 -> StatusCode) | Subscriptions.md |
| `Subscriptions.UpdateSubscription` | `UpdateSubscriptionOperationRequest{SubscriptionId req, Body: UpdateSubscriptionRequest{Subscription{NextBillingAt}}}` | | `SubscriptionResponse` | A [422] | Api/Subscriptions.cs remarks (next_billing_at; response does not echo it) |
| `SubscriptionStatus.PauseSubscription` / `ResumeSubscription` | `{SubscriptionId req}` | `PauseRequest{Hold}` optional | `SubscriptionResponse` | A [422] | SubscriptionStatus.md |
| `SubscriptionStatus.CancelSubscription` | `{SubscriptionId req, Body: CancellationRequest{Subscription: CancellationOptions}}` | | `SubscriptionResponse` | A [404],[422] | SubscriptionStatus.md |
| `SubscriptionStatus.ReactivateSubscription` | `ReactivateSubscriptionOperationRequest{SubscriptionId req, Body?}` | | `SubscriptionResponse` | A [422] | SubscriptionStatus.md |
| `SubscriptionProducts.MigrateSubscriptionProduct` | `{SubscriptionId req, Body: SubscriptionProductMigrationRequest{Migration{ProductHandle}}}` | | `SubscriptionResponse` | A [422] | SubscriptionProducts.md (valid only for active/trialing) |
| `SubscriptionComponents.ListSubscriptionComponents` | `{SubscriptionId req}` | | list of `SubscriptionComponentResponse` (`.Component.Kind`, `.ComponentId`) | B | SubscriptionComponents.md |
| `SubscriptionComponents.ListUsages` | `{SubscriptionIdOrReference req, ComponentId req, SinceDate, UntilDate, Page, PerPage}` | | list of `UsageResponse` (`.Usage.Quantity`, `CreatedAt`) | B | Api remarks: since/until default to midnight; one day = since D, until D+1 |
| `Invoices.ListInvoices` | `{SubscriptionId, Page, PerPage}` | | `ListInvoicesResponse.Invoices` (`Status`, `DueAmount` string) | B | Invoices.md |

Enums: `CollectionMethod.Remittance` (no card needed), `IntervalUnit.Month`, `CurrencyPriceRole.Baseline`, `SubscriptionState.{Canceled,Paused,OnHold,Active,...}`, `ComponentKind.MeteredComponent`.
Client: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; `BasicAuth = BasicAuthCredentials{Username=api key, Password="x"}`; `Environment` `ServerEnvironment.Us|Eu`; sites override `options.Server.Production.Us.Site` / `.BaseUrl` (Eu likewise).

### CROSS-OPERATION INVARIANTS
| Invariant | Operations | Enforced where |
| --- | --- | --- |
| Subscribe/Switch plan handle must be one the app offers (non-archived product from the plan list) | `CreateSubscription`/`Migrate` <- `ListProducts` | `MaxioBilling.RequireOfferedPlanAsync`, before the write |
| Subscription id acted on by a shopper must belong to that shopper | `Pause/Resume/Cancel/Reactivate/Migrate/ListUsages/Read` <- `ReadSubscription` (`Customer.Reference` == JWT name) | `MaxioBilling.GetOwnedAsync`, before every write |
| Plan brand must be a catalog brand | `CreateProduct`/`CreateProductFamily` <- `IRepository<CatalogBrand>` | admin endpoint, before the write |
| Usage components must belong to the subscription | `ListUsages` <- `ListSubscriptionComponents` (metered only) | `MaxioBilling.GetUsageAsync` |
| Currency-price `planId` must be an existing product with a default price point | `CreateProductCurrencyPrices` <- `ReadProduct` | admin endpoint |

## 3. Trap notes
- Typed errors are Case A but 2xx/err shape drift surfaces as `ResponseDeserializationException` - MUST load dotnet-error-handling.
- Retries/timeouts bound per attempt, not whole call - MUST load dotnet-configuration-resilience.
- Logging may echo request bodies; customer email is PII - MUST load dotnet-configuration-resilience.

## 4. REQUIRED READING
maxio:dotnet-error-handling (every call) · dotnet-client-initialization (DI) · dotnet-authentication · dotnet-configuration-resilience (timeouts, logging) · dotnet-testing.
Hazard row: a body that does not match its declared type - a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape - surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS
| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` validated at startup: ApiKey and Subdomain (unless BaseUrl set) required non-blank; host throws on start otherwise. |
| 2 | Secret sourcing & rotation | .NET user-secrets / env via IConfiguration; options built once at registration into a singleton; rotation needs restart. |
| 3 | Total timeout budget | `Retry.Timeout` per attempt + `HttpClient.Timeout` 30s as the whole-call ceiling. |
| 4 | Write-retry ownership | SDK default retries only idempotent verbs; we add no write retries. |
| 5 | Idempotency & ambiguous writes | No real key on any record; reconciliation: re-read by customer reference/product handle before reporting failure. |
| 6 | Observability | Warnings for Maxio 4xx/5xx with status; no bodies. |
| 7 | Sensitive data | Customer email only; `LogRequestBody` stays off, explicit LoggerFactory. |
| 8 | Environment selection | Sandbox via `Maxio:Environment`/`Subdomain`; `Maxio:BaseUrl` verbatim overrides both groups' Production base. |
| 9 | Duplicate prevention | N/A beyond DUPLICATE CLAIMS below. |
| 10 | Partial results | list loops walk until a short page; no cap. |
| 11 | Unknown outcomes | Subscribe: on connection failure/timeout re-list customer subscriptions (`FindRecentSubscriptionAsync`); other writes report 502/504 and are naturally re-readable via GET. |

**DUPLICATE CLAIMS**: none (the app has no store of its own for subscriptions; Maxio is system of record. Documented limitation: concurrent subscribes to the same plan create two subscriptions, which the spec allows).
**PAGED READS**
| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| ListProducts | none: page loop runs until a short page | n/a, never cut short | `MaxioBilling.ListAllProductsAsync` |
| ListInvoices | none: page loop | n/a (an unreadable amount fails the call with 502 instead of guessing) | `MaxioBilling.IsPaidAsync` |
| ListUsages | none: page loop | n/a | `MaxioBilling.GetUsageAsync` |
**UNKNOWN OUTCOMES**
| Write | Re-read with | Search by | Where in the code | Test that fails the connection |
| --- | --- | --- | --- | --- |
| CreateSubscription | `Customers.ListCustomerSubscriptions` | customer id + product handle + created since the call began | `MaxioBilling.SubscribeAsync` catch -> `FindRecentSubscriptionAsync` | none: not reproducible against the real sandbox; the catch only settles timeouts/connection failures, other writes report 502/504 and the shopper re-reads the subscription page |

## 6. Assumptions & Blockers (decisions made by the code are recorded in place)
- Sandbox site has no non-primary currencies configured, so `POST /api/admin/plans/{id}/currency-prices` answers 422 with Maxio's reason there ("Currency must be defined on the site level"); the success path is unverified against the sandbox. Site currencies cannot be added through the SDK.
- Maxio does not generate a product handle: `MaxioBilling.MakeHandle` sets one (slug + random suffix) on plan creation.
- Subscriptions use `CollectionMethod.Remittance` (no card): a new subscription owes its first invoice, so `paid` is `false` until it is settled in Maxio (verified in `Paid_IsFalseWhileMoneyIsOwedAndTrueOnceSettled`).
- A bare next-billing date is applied at 12:00 UTC (`BillingRoutes`), so it is that calendar day in every time zone.
- Pause needs active/trialing, resume needs on_hold/paused, restart needs canceled, cancel refuses canceled/expired: otherwise 409 (`MaxioBilling` state checks).
- Customer reference = JWT name (`ClaimTypes.Name`), the only identity claim the token carries.
- Plan brand = Maxio product family named after the catalog brand.
- `paid` = subscription balance <= 0 AND no non-voided invoice with positive due amount.
- Currency price is an `int` in the SDK: whole currency units only.
- Switching a canceled subscription: reactivate first (migration is only valid for active/trialing), then migrate; if migrate fails, re-cancel.
