# Maxio Advanced Billing — subscribe flow plan

SDK root (plugin-relative): `sdk/dotnet/` · map: `sdk-map.md` + `map/operations/*.md`.

## 1. Scope & sequence

1. Startup: bind `Maxio:` settings, fail fast, build the SDK client once. (no op)
2. `GET /api/subscription-plans` — `ProductFamilies.ListProductsForProductFamily` (paged by params, capped).
3. `POST /api/subscriptions` — validate plan ∈ step 2 set → claim customer → `Customers.CreateCustomer` (reconcile `Customers.ReadCustomerByReference`) → claim subscription → `Subscriptions.CreateSubscription` (reconcile `Subscriptions.FindSubscription`) → record → `Subscriptions.ReadSubscription` (`SubscriptionId` required; Case B; → `SubscriptionResponse.Subscription`) to confirm plan/price/state/next billing.
4. `GET /api/my-subscriptions` — local customer link → `Customers.ListCustomerSubscriptions`.

No blockers: every capability exists in the map.

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim: each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer using the record's own property names — never flat arguments.
⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type (`MaxioAdvancedBilling.Models`, `.Models.Enums`, `.Requests.<Controller>`, `.Errors`, `.Core.Exceptions`, `.Core.ErrorResponse`).

| Controller · signature | Request record (members) | Body model (wire) | Response → fields read | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies.ListProductsForProductFamily(request, requestOptions?, ct)` | `Requests.ProductFamilies.ListProductsForProductFamilyRequest`: `ProductFamilyId: string` **required** (id or `handle:<handle>`), `Page: int = 1`, `PerPage: int = 20`, `IncludeArchived: bool?` | — | `IReadOnlyList<ProductResponse>`; `.Product`: `Id`, `Handle`, `Name`, `Description`, `PriceInCents: long?`, `Interval: int?`, `IntervalUnit`, `ArchivedAt`, `RequireCreditCard`, `TrialInterval` | Case A `ListProductsForProductFamilyError`: `TryGetString` [404], `TryGetRawError` | none by SDK (`Pageable` absent); params `page`/`per_page` driven by us | map Products/ProductFamilies.md; Models/Product.cs; Requests/ProductFamilies/… |
| `client.Customers.CreateCustomer(request, …)` | `Requests.Customers.CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?` | `CreateCustomerRequest`: `Customer (customer): CreateCustomer` required; `CreateCustomer`: `FirstName (first_name)`, `LastName (last_name)`, `Email (email)` string **required**; `Reference (reference): string?` (set) | `CustomerResponse` → `.Customer.Id: int?`, `.Reference` | Case A `CreateCustomerError`: `TryGetCustomerErrorResponse1` [422], `TryGetRawError` | none | map Customers.md; Models/CreateCustomer.cs |
| `client.Customers.ReadCustomerByReference(request, …)` | `Requests.Customers.ReadCustomerByReferenceRequest`: `Reference: string` required | — | `CustomerResponse` | Case B `ApiException<RawError>` (404 = miss) | none | map Customers.md |
| `client.Customers.ListCustomerSubscriptions(request, …)` | `Requests.Customers.ListCustomerSubscriptionsRequest`: `CustomerId: int` required | — | `IReadOnlyList<SubscriptionResponse>` → `.Subscription` (nullable) | Case B | none | map Customers.md |
| `client.Subscriptions.CreateSubscription(request, …)` | `Requests.Subscriptions.CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?` | `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription` required; `CreateSubscription` (all optional): `ProductHandle (product_handle)`, `CustomerId (customer_id): int?`, `Reference (reference)`. Left out: payment profile/attrs, coupons, components (plans need no card). | `SubscriptionResponse` → `.Subscription?`: `Id`, `State: SubscriptionState?`, `ProductPriceInCents`, `CurrentBillingAmountInCents`, `NextAssessmentAt`, `CurrentPeriodEndsAt`, `Product`, `Reference`, `Customer` | Case A `CreateSubscriptionError`: `TryGetErrorListResponse1` [422] (`Errors: IReadOnlyList<string>` required), `TryGetRawError` | none | map Subscriptions.md; Models/CreateSubscription.cs |
| `client.Subscriptions.FindSubscription(request, …)` | `Requests.Subscriptions.FindSubscriptionRequest`: `Reference` (query `reference`) | — | `SubscriptionResponse` | Case A `FindSubscriptionError`: `TryGetNoContent(out RawError)` [404], `TryGetRawError` | none | map Subscriptions.md |

Enums: `MaxioAdvancedBilling.Models.Enums.SubscriptionState` open-string (`Active`, `Trialing`, `PastDue`, `Canceled`, …) — read `.Value` for wire value, never `ToString()`.
Client: `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <api key>, Password = "x" }` (map: Servers & auth). Base URL group `Production` only (`https://{site}.chargify.com`, EU `…ebilling.maxio.com`); site via `options.Server.Production.Us.Site`; verbatim override `options.Server.Production.Us.BaseUrl`. `ServerEnvironment` in `MaxioAdvancedBilling.Servers`. Retry types in `MaxioAdvancedBilling.Core.Configuration`.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| a plan handle accepted by `POST /api/subscriptions` must be one the configured product family offers (non-archived) | `CreateSubscription` ← `ListProductsForProductFamily` | `MaxioSubscriptionService.SubscribeAsync`, before `CreateCustomer`/`CreateSubscription` (reads the family's plan list) |
| the customer id passed to `CreateSubscription` must be the one this application stored for the caller | `CreateSubscription` ← `CreateCustomer` / local `MaxioCustomerLink` | `SubscribeAsync` uses only the claimed link's `MaxioCustomerId` |
| `my-subscriptions` customer id comes from stored link, never the caller | `ListCustomerSubscriptions` ← `MaxioCustomerLink` | `ListMineAsync` |

## 3. Trap notes
- Client/DI: options captured once, `HttpClient` lifetime/DNS, timeouts on shared default client. MUST load maxio-advanced-billing:dotnet-client-initialization
- Auth: unset credential silently sends unauthenticated, no exception; multi-part credential. MUST load maxio-advanced-billing:dotnet-authentication
- Calls: record-first signatures, `requestOptions` positional trap. MUST load maxio-advanced-billing:dotnet-calling-endpoints
- Models: open enums' `ToString()`, nullable envelopes. MUST load maxio-advanced-billing:dotnet-models
- Error ladder: mixed Case A/B, deserialization failures, caller-vs-our-fault status mapping. MUST load maxio-advanced-billing:dotnet-error-handling
- Retry/timeout/logging: what is resent, what bounds a call, what the logger prints. MUST load maxio-advanced-billing:dotnet-configuration-resilience
- Tests: stub seam, body capture timing. MUST load maxio-advanced-billing:dotnet-testing

## 4. REQUIRED READING
- maxio-advanced-billing:dotnet-client-initialization · client/DI
- maxio-advanced-billing:dotnet-authentication · credentials + fail-fast
- maxio-advanced-billing:dotnet-calling-endpoints · every call
- maxio-advanced-billing:dotnet-models · request/response models, enums
- maxio-advanced-billing:dotnet-error-handling · catch ladder
- maxio-advanced-billing:dotnet-configuration-resilience · retries/timeouts/logging/idempotency
- maxio-advanced-billing:dotnet-testing · unit tests

Hazard row: a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:`; `ValidateOnStart` rejects blank `ApiKey`, `Subdomain`, `ProductFamilyHandle` (named key, value never echoed). `BaseUrl` optional but, if set, must be an absolute URI. Host refuses to start. |
| 2 | Secret sourcing | .NET user-secrets (PublicApi `UserSecretsId`) / env-config in deploy; client singleton built once at registration with a captured options object → rotation needs restart (accepted). |
| 3 | Total timeout budget | 30 s per service operation (configurable `const`), enforced by one `Bounded` helper linked to request-abort token; per-attempt `Retry.Timeout` and `HttpClient.Timeout` = 10 s. Subscribe makes ≤ 4 sequential SDK calls, each sharing the single 30 s token. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` (GET/HEAD/PUT/OPTIONS) kept; both writes are POST → SDK never resends. Reads retry (up to 3). |
| 5 | Idempotency | `CreateCustomer`/`CreateSubscription` request records carry no idempotency key (injected header is not one). Real key = deterministic Maxio `reference` (`customer.reference`, `subscription.reference`) used to reconcile through `ReadCustomerByReference` / `FindSubscription`; plus local claim (DUPLICATE CLAIMS). |
| 6 | Observability | Information: subscribe start/finish with user + plan handle + Maxio ids; Warning: reconcile, provider 4xx; Error: unknown/5xx. Provider 422 error strings are logged and surfaced; `ex.Headers` has no correlation id in the map, so our own `CorrelationId` (BaseMessage) is logged with every provider failure. |
| 7 | Sensitive data | Customer email/name are PII: `LogRequestBody=false`, `LoggerFactory` set from DI explicitly (disables env var trace). No card data in scope. |
| 8 | Environment | Only server group `Production`. Default `Us` env; `Maxio:Subdomain` → `Server.Production.Us.Site`; `Maxio:BaseUrl`, when set, assigned verbatim to `Server.Production.Us.BaseUrl` (this is how sandbox/EU/mock is chosen per deployment). `Ebb` untouched. |
| 9 | Duplicate prevention | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `MaxioCustomerLinks` table in `CatalogContext` (app's own store) | primary key `UserName` — second insert fails | `catch (Exception ex) when (IsDuplicateKey(ex))` (DbUpdateException on SQL Server; bare ArgumentException on the in-memory provider) around the claim `SaveChanges` | `EnsureClaimedAsync` (`set.Add(fresh)` + `SaveChangesAsync`), then SDK call `CreateCustomer` inside `EnsureCustomerAsync`'s `create` delegate, invoked at `await create(ct)` |
| `CreateSubscription` | `UserSubscriptions` table in `CatalogContext` | composite primary key (`UserName`, `PlanHandle`) | same `catch (Exception ex) when (IsDuplicateKey(ex))` | `EnsureClaimedAsync` (`set.Add(fresh)` + `SaveChangesAsync`), then SDK call `CreateSubscription` inside `EnsureSubscriptionAsync`'s `create` delegate, invoked at `await create(ct)` |

Policy: one subscription per user per plan; repeat/double-click returns the existing subscription (HTTP 200 `AlreadySubscribed=true`). Claim released (row deleted) when the provider definitively refuses (`ApiException`); retained as `Unknown` when the outcome is unknown.

**PAGED READS**

| Read | What caps it | How the caller learns it was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` | 20 pages × 100 per page | `ListSubscriptionPlansResponse.Truncated: bool` (from `SubscriptionPlanList.Truncated`) | `MaxioSubscriptionService.ReadPlansAsync` returns `(products, true)` after the page loop |

**UNKNOWN OUTCOMES**

| Write | Re-read with | Reference searched by | Where in the code | Test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `ReadCustomerByReference` | `customer.reference` = `eshop-user:<username lowercased>` | `EnsureClaimedAsync` `catch (Exception ex) when (writeSent && IsAmbiguous(ex))` → `SettleUnknownAsync` | `MaxioSubscriptionServiceTests.Subscribe_connection_failure_creating_the_customer_is_settled_by_reconciling_on_the_reference` |
| `CreateSubscription` | `FindSubscription` | `subscription.reference` = `eshop-sub:<username lowercased>:<planhandle>` | same `catch` → `SettleUnknownAsync` | `MaxioSubscriptionServiceTests.Subscribe_connection_failure_on_write_is_settled_by_reconciling_on_the_reference` and `…_with_nothing_found_leaves_an_unknown_claim_and_never_duplicates` (stub throws `HttpRequestException` → `SdkConnectionException`) |

If the re-read finds nothing/fails, the claim row is set to `Unknown`; the next subscribe attempt (same claim) re-runs the reconcile before anything is created, and the caller gets 409 "outcome pending".

## 6. Assumptions & Blockers
- Caller identity = JWT `ClaimTypes.Name` (username/email) — the only identity claim the PublicApi token carries (`IdentityTokenClaimService`).
- Customer name defaults: first name = email local part, last name = `Customer`, unless the request supplies them.
- Persisted mapping lives in `CatalogContext` (in-memory in this sandbox; a migration is generated for SQL Server). On a restart under in-memory the link is lost, but reconcile-by-reference re-adopts the existing Maxio customer/subscription so no duplicates are created.
- SDK is referenced by `ProjectReference` to the plugin's `sdk/dotnet` copy, machine-local; path in a `MaxioSdkProject` MSBuild property.
- `global.json` rollForward → `latestMajor` (permitted by the task).
- Implementation corrections: (a) `CreateSubscription.PaymentCollectionMethod = Remittance` (`Models.Enums.CollectionMethod`) is sent — with the default automatic collection Maxio rejects the $299 signup (422, no payment method on file) even though the plan does not *require* a card; (b) before any create the service reads Maxio by reference and adopts an existing record (so a process restart that loses the in-memory link never duplicates); (c) a stale claim (>2 min, no result) can be taken over, guarded by `ClaimedAt` as concurrency token; (d) the generated EF migration included unrelated pre-existing model drift (`AlterColumn`s); only the two `CreateTable`s were kept.
- Blockers: none.
