# Maxio Advanced Billing — subscription plan (eShopOnWeb PublicApi)

SDK root (plugin-relative): `sdk/dotnet/` of the maxio-advanced-billing plugin. Referenced as a local `ProjectReference` (not on NuGet; machine-local path — re-point if the plugin moves).

## 1. Scope & sequence

1. Config + fail-fast: bind `Maxio:` section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`, optional `Environment`), validate on start, register singleton client.
2. List plans — `Products`/`ProductFamilies.ListProductsForProductFamily` (family given by handle).
3. Subscribe — claim enrollment → ensure customer (`Customers.ReadCustomerByReference` / `CreateCustomer`) → `Subscriptions.CreateSubscription` → record result. Unknown outcomes settled with `Subscriptions.FindSubscription` / `Customers.ReadCustomerByReference`.
4. My subscriptions — `Customers.ListCustomerSubscriptions`.
5. Endpoints on PublicApi: `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions` (JWT, identity = `ClaimTypes.Name`).
6. Tests (stub `HttpMessageHandler`), run end to end against sandbox.

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim: each operation that takes input takes ONE request record as first parameter, built with an object initializer using the record's own property names — never flat arguments.
⚠ Every SDK type is written fully-qualified with the namespace its source path implies (Models → `MaxioAdvancedBilling.Models`, Enums → `.Models.Enums`, Errors → `MaxioAdvancedBilling.Errors`, Requests → `MaxioAdvancedBilling.Requests.<Controller>`, exceptions → `MaxioAdvancedBilling.Core.Exceptions`, `RawError` → `MaxioAdvancedBilling.Core.ErrorResponse`).

| Controller · method | Request record (+ members) | Body model | Response / fields read | Error case | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `ProductFamilies.ListProductsForProductFamily(ListProductsForProductFamilyRequest, RequestOptions?, CancellationToken)` | `Requests.ProductFamilies.ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` ("id or `handle:`+handle"), `Page: int=1`, `PerPage: int=20 (max 200)` | – | `IReadOnlyList<ProductResponse>`; `.Product` (required) → `Id, Handle (handle), Name, Description, PriceInCents (price_in_cents), Interval, IntervalUnit (.Value), ArchivedAt, DefaultProductPricePointId` all nullable | **A** `ApiException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404], `TryGetRawError` | none by SDK; page manually via `Page`/`PerPage` | map/operations/ProductFamilies.md; Models/Product.cs |
| `Customers.ReadCustomerByReference(ReadCustomerByReferenceRequest, …)` | `Requests.Customers.ReadCustomerByReferenceRequest`: `Reference: string, required` | – | `CustomerResponse.Customer` (required) → `Id: int?`, `Reference` | **B** `ApiException<RawError>` (404 when absent: `ex.StatusCode`) | none | map/operations/Customers.md |
| `Customers.CreateCustomer(CreateCustomerOperationRequest, …)` | `Requests.Customers.CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?` | `Models.CreateCustomerRequest`: `Customer (customer): CreateCustomer, required` → `FirstName (first_name) req, LastName (last_name) req, Email (email) req, Reference (reference)` ; other optional fields left out | `CustomerResponse.Customer.Id` | **A** `CreateCustomerError`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] (`.Errors: Errors1?`), `TryGetRawError` | none | Models/CreateCustomer.cs |
| `Customers.ListCustomerSubscriptions(ListCustomerSubscriptionsRequest, …)` | `CustomerId: int, required` | – | `IReadOnlyList<SubscriptionResponse>`; `.Subscription?` | **B** | none (default) | map/operations/Customers.md |
| `Subscriptions.CreateSubscription(CreateSubscriptionOperationRequest, …)` | `Requests.Subscriptions.CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?` (nothing marked required) | `Models.CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription, required` → carried: `ProductHandle (product_handle)` (doc: required unless product_id), `CustomerId (customer_id): int?` (doc: required unless reference/attributes), `Reference (reference): string?`. Left out: payment profile / card fields, components, coupons, price point, dates | `SubscriptionResponse.Subscription?` (nullable) → `Id, State (SubscriptionState, .Value), ProductPriceInCents, CurrentBillingAmountInCents, NextAssessmentAt, CurrentPeriodEndsAt, Product, Reference, CreatedAt` | **A** `CreateSubscriptionError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`Errors: IReadOnlyList<string>` req), `TryGetRawError` | none | Models/CreateSubscription.cs |
| `Subscriptions.FindSubscription(FindSubscriptionRequest, …)` | `Reference: string?` | – | `SubscriptionResponse` | **A** `FindSubscriptionError`: `TryGetNoContent(out RawError)` [404], `TryGetRawError` | none | map/operations/Subscriptions.md |

Real idempotency key on the write records: none (`CreateSubscriptionOperationRequest`/`CreateCustomerOperationRequest` carry only `Body`). `Reference` in the body is a lookup handle, not a provider dedupe key — verified only by live behaviour → `UNVERIFIED`.

Enum: `SubscriptionState` (Models/Enums) values read via `.Value` (wire strings e.g. `active`, `trialing`, `pending`, `canceled`, …); open enum, passed through as string.

Client/auth/server: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <ApiKey>, Password = "x" }` (map: Servers & auth — password is `x`). `options.Environment` = `MaxioAdvancedBilling.Servers.ServerEnvironment.Us|Eu`. Only server group `Production` is touched (`https://{site}.chargify.com` / `https://{site}.ebilling.maxio.com`); set `options.Server.Production.<Env>.Site = Subdomain`, or `.BaseUrl = Maxio:BaseUrl` verbatim when configured. `Ebb` group unused.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| plan handle accepted by Subscribe must be one the plans list returned for the configured family (and not archived) | `CreateSubscription` ← `ListProductsForProductFamily` | `SubscriptionService.SubscribeAsync`, before the claim and before `CreateSubscription` (re-reads the plan list) |
| customer id used on `CreateSubscription` must be the one this app stored/resolved for the caller | `CreateSubscription` ← `ReadCustomerByReference`/`CreateCustomer` (stored in `MaxioCustomerLink`) | `SubscriptionService.EnsureCustomerAsync` before `CreateSubscription` |

## 3. Trap notes
- Client/DI registration: default `HttpClient` lifetime/DNS and shared-handler hazards on a singleton client — MUST load maxio-advanced-billing:dotnet-client-initialization.
- Credentials: a blank Username/Password sends unauthenticated calls with no exception — MUST load maxio-advanced-billing:dotnet-authentication.
- Every catch ladder (Case A for create/find/list-products, Case B for customer reads/list) and non-`ApiException<T>` failures — MUST load maxio-advanced-billing:dotnet-error-handling.
- Timeouts/retries/logging/bounding: per-attempt vs total, which verbs resend, default logging on under DI — MUST load maxio-advanced-billing:dotnet-configuration-resilience.
- Response members are nullable/wrapped (`Subscription?`, `Product` fields) — MUST load maxio-advanced-billing:dotnet-models.
- Request-record construction and `RequestOptions` argument order — MUST load maxio-advanced-billing:dotnet-calling-endpoints.
- Stubbing the HttpClient seam, body capture — MUST load maxio-advanced-billing:dotnet-testing.

## 4. REQUIRED READING (loaded before implementation)
- maxio-advanced-billing:dotnet-getting-started · map/identity
- maxio-advanced-billing:dotnet-client-initialization · client + DI registration
- maxio-advanced-billing:dotnet-authentication · BasicAuth + startup validation
- maxio-advanced-billing:dotnet-calling-endpoints · every call
- maxio-advanced-billing:dotnet-models · request/response shapes
- maxio-advanced-billing:dotnet-error-handling · error boundary
- maxio-advanced-billing:dotnet-configuration-resilience · timeouts, retries, logging, base URL
- maxio-advanced-billing:dotnet-testing · tests
- Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:`; `ValidateOnStart()` with a validator rejecting blank `ApiKey`, `Subdomain`, `ProductFamilyHandle` (Password is the constant `x`, not configured). Host refuses to start naming the missing key, never echoing values. |
| 2 | Secret sourcing & rotation | .NET user-secrets in dev (loaded from env vars by me), env vars `Maxio__ApiKey` etc. / secret store in deployment. Options built once at registration and captured by the singleton client; rotation needs a restart (accepted). |
| 3 | Total timeout budget | Per attempt 10s (`Retry.Timeout` and `HttpClient.Timeout`); whole call bounded to 30s by a linked `CancellationTokenSource` in the single `MaxioBillingGateway.Bounded(...)` helper used by every operation. Subscribe makes ≤3-4 sequential SDK calls, each bounded by its own 30s budget; the HTTP request abort token is linked. |
| 4 | Write-retry ownership | `HttpMethodsToRetry` left at default (GET/HEAD/PUT/OPTIONS): reads may be resent; `POST` (create customer, create subscription) are never resent by the SDK. |
| 5 | Idempotency & ambiguous writes | No real provider key on either write. App-owned claim (§DUPLICATE CLAIMS) + deterministic `Reference` on customer and subscription for reconciliation. `Idempotency-Key` header not relied on. |
| 6 | Observability | SDK logger on via DI `ILoggerFactory` (request/response lines at Information). Gateway logs warnings with HTTP status + Maxio `errors` text on 4xx and Error on unknown outcomes. No Maxio correlation id exists in typed error bodies (`errors: string[]` only) — status + error strings + our reference logged. |
| 7 | Sensitive data | Customer name/email travel in JSON bodies: `LogRequestBody=false`, `LoggerFactory` set explicitly, headers logging off. |
| 8 | Environment selection | Group `Production` only. `Maxio:Environment` (default `Us`) selects `Us`/`Eu`; `Maxio:Subdomain` fills `{site}`; `Maxio:BaseUrl` when set overrides verbatim. Sandbox vs live is by which site/key is configured (no SDK sandbox member); dev uses the sandbox site via user-secrets. |
| 9 | Duplicate prevention | see DUPLICATE CLAIMS. |
| 10 | Partial results | see PAGED READS. |
| 11 | Unknown outcomes | see UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateSubscription` (per user+plan) | `CatalogContext.SubscriptionEnrollments` (table owned by this app) | composite primary key (BuyerId, PlanHandle) — `SaveChanges` of the claim row fails for the second caller | `catch (Exception) when (IsDuplicateClaim)` (DbUpdateException; ArgumentException on the in-memory provider) around the claim insert | claim: `SubscriptionService.SubscribeAsync` (`_db.SubscriptionEnrollments.Add` + `SaveChangesAsync`) → SDK call: `MaxioGateway.CreateSubscriptionAsync` |
| `CreateCustomer` (per user) | `CatalogContext.MaxioCustomerLinks` | primary key (BuyerId) | `catch (Exception) when (IsDuplicateClaim)` around the claim insert | claim: `SubscriptionService.EnsureCustomerAsync` (`_db.MaxioCustomerLinks.Add` + `SaveChangesAsync`) → SDK call: `MaxioGateway.CreateCustomerAsync` |

Order: claim insert → SDK call → record result; claim deleted when the provider definitively refuses (422/4xx) or the lookup proves nothing was created.

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| plan list (`ListProductsForProductFamily`, manual paging `PerPage=200`) | page cap of 5 pages (1000 plans) | `SubscriptionPlanList.IsComplete == false` → API response field `isComplete` | `MaxioGateway.ListPlansAsync` (sets `complete`) |

**UNKNOWN OUTCOMES**

| Write | Re-read with | Reference searched by | Where in the code | Test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateSubscription` | `Subscriptions.FindSubscription` | `Reference` = deterministic `eshop-sub-<hash(user|plan)>` | `SubscriptionService.SubscribeAsync` catch `MaxioGatewayException` when UnknownOutcome → `SettleSubscriptionAsync` | `SubscriptionServiceTests.UnknownOutcome*`, `MaxioGatewayTests.ConnectionFailureOnCreateSubscriptionIsAnUnknownOutcomeAndIsSentOnce` |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | `Reference` = `eshop-cust-<hash(user)>` | `SubscriptionService.EnsureCustomerAsync` catch UnknownOutcome → `FindCustomerIdByReferenceAsync` | `MaxioGatewayTests.ConnectionFailureOnCreateCustomerIsAnUnknownOutcome` |

Not found after a connection failure → enrollment stays `Pending` (unknown) and a later Subscribe / my-subscriptions call settles it (re-read; stale Pending with nothing found is released).

## 6. Assumptions & Blockers
- Blockers: none. The plugin exposes everything needed.
- Assumption: JWT carries only `ClaimTypes.Name` (username, the user's email in eShop); it is the stable identity. Names for the Maxio customer are derived from the Identity user's email (first name = local part, last name = "Customer") because eShop stores no names.
- Assumption: one subscription per (user, plan); re-subscribing to the same plan returns the existing one (idempotent 200).
- Assumption: SQL Server schema changes are provided as an EF migration only if `dotnet ef` is available; in-memory ignores migrations.
- Decision (verified live): plans need `payment_collection_method = remittance`; with the default automatic method Maxio returned 422 "No payment method was on file".
- UNVERIFIED: whether Maxio rejects duplicate `reference` on subscriptions — not relied on.

## 7. Source labels
Per-row sources in the contract sheet; application decisions (claims, names, budgets) are `YOUR CALL — not in the map`.
