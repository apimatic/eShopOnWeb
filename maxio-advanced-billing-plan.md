# Maxio Advanced Billing — subscription capability for eShopOnWeb (plan + contract sheet)

SDK root (plugin-relative): `sdk/dotnet/` · namespace root `MaxioAdvancedBilling` · baseline `dotnet build src/PublicApi` clean (warnings only) before any change.

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | Bind/validate `Maxio:` settings, register SDK client (singleton) | — |
| 2 | `GET /api/subscription-plans` — list offered plans | `ProductFamilies.ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions` — validate plan ∈ step 2 → claim → ensure customer → create subscription → record | `ListProductsForProductFamily`, `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.CreateSubscription`, `Subscriptions.FindSubscription` (reconcile) |
| 4 | `GET /api/my-subscriptions` — read from Maxio (system of record) | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |

Caller identity = JWT `ClaimTypes.Name` (the only identity claim the existing token service emits: `src/Infrastructure/Identity/IdentityTokenClaimService.cs`). Maxio customer `reference` = that name; subscription `reference` = `eshop:{name}:{planHandle}`.
Layering: interface + DTOs + entity in ApplicationCore; SDK-using implementation in Infrastructure; endpoints in PublicApi (exemplar `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs`, errors via `src/PublicApi/Middleware/ExceptionMiddleware.cs`).

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim: each operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names — never flat arguments.
⚠ Every SDK type is written fully-qualified with the namespace its source path implies (path given per type), never from a neighbour's location.

| controller · signature | request record (members) | body model (wire) | response | error | pagination | source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies.ListProductsForProductFamily(req, requestOptions?, ct)` | `MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` (accepts `handle:{handle}` per Api remarks); `Page:int=1`; `PerPage:int=20 (max 200)`; `IncludeArchived: bool?` | — | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` → `.Product` (`Models.Product`): `Id:int?`, `Name`, `Handle`, `Description`, `PriceInCents:long?`, `Interval:int?`, `IntervalUnit:IntervalUnit?`, `TrialInterval:int?`, `InitialChargeInCents:long?`, `ArchivedAt` | A: `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404], `TryGetRawError` fallback | none (plain list; page/per_page driven by us) | map/operations/ProductFamilies.md; Api/ProductFamilies.cs:144 |
| `client.Customers.ReadCustomerByReference(req,…)` | `Requests.Customers.ReadCustomerByReferenceRequest`: `Reference: string, required` | — | `Models.CustomerResponse` → `.Customer` (`Models.Customer`): `Id:int?`, `Reference`, `Email` | B: `ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; miss = 404 | none | map/operations/Customers.md |
| `client.Customers.CreateCustomer(req,…)` | `Requests.Customers.CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?` (nothing marked required) | `Models.CreateCustomerRequest{ Customer (customer): CreateCustomer, required }`; `Models.CreateCustomer`: `FirstName (first_name), LastName (last_name), Email (email)` all `string, required`; optional `Reference (reference)`; Api remarks: `reference` must be unique | `Models.CustomerResponse` | A: `ApiException<Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out Models.CustomerErrorResponse1)` [422], `TryGetRawError` | none | map/operations/Customers.md; Models/CreateCustomer.cs; Api/Customers.cs:42 |
| `client.Subscriptions.CreateSubscription(req,…)` | `Requests.Subscriptions.CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?` | `Models.CreateSubscriptionRequest{ Subscription (subscription): CreateSubscription, required }`; `Models.CreateSubscription` (all optional): `ProductHandle (product_handle):string?`, `CustomerId (customer_id):int?`, `Reference (reference):string?`; left out: ids/price points/coupons/payment profile (plans need no payment method — task) | `Models.SubscriptionResponse` → `.Subscription?` (`Models.Subscription`): `Id:int?`, `State:SubscriptionState?`, `ProductPriceInCents:long?`, `CurrentPeriodEndsAt`, `NextAssessmentAt:DateTimeOffset?`, `Product:Product?`, `Customer:Customer?`, `Reference` | A: `ApiException<Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out Models.ErrorListResponse1)` [422], `TryGetRawError` | none | map/operations/Subscriptions.md; Models/CreateSubscription.cs |
| `client.Subscriptions.FindSubscription(req,…)` | `Requests.Subscriptions.FindSubscriptionRequest`: `Reference` (query `reference`) | — | `Models.SubscriptionResponse` | A: `ApiException<Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404], `TryGetRawError` | none | map/operations/Subscriptions.md |
| `client.Customers.ListCustomerSubscriptions(req,…)` | `Requests.Customers.ListCustomerSubscriptionsRequest`: `CustomerId` required | — | `IReadOnlyList<Models.SubscriptionResponse>` | B | none | map/operations/Customers.md |

Enums: `Models.Enums.SubscriptionState` (open string; static `Active`, `Canceled`, …; use `.Value` for wire text — never interpolate). Response `Subscription` has `AdditionalProperties`.
Client: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials{Username,Password}` — Username = Maxio API key, Password = `x` (sdk-map Servers & auth). Server group `Production` only (all ops in scope); `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` default; `options.Server.Production.Us.Site` (template var `{site}`) and `.BaseUrl` (template `https://{site}.chargify.com`). `Maxio:BaseUrl` set ⇒ assigned verbatim to `options.Server.Production.Us.BaseUrl`. Namespaces needed: `MaxioAdvancedBilling.Core.Exceptions`, `.Core.ErrorResponse`, `.Errors`, `.Models`, `.Models.Enums`, `.Requests.*`.

**CROSS-OPERATION INVARIANTS**

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| a plan handle accepted by `CreateSubscription.ProductHandle` must be one returned by the family listing (non-archived) | `CreateSubscription` ← `ListProductsForProductFamily` | `MaxioSubscriptionService.SubscribeAsync`, before the claim is written (unknown handle → 404, no claim, no Maxio write) |
| `CreateSubscription.CustomerId` must be the customer for this user's reference | `CreateSubscription` ← `ReadCustomerByReference`/`CreateCustomer` | `EnsureCustomerAsync` result used directly |

## 3. Trap notes

- Customer create: a double create under one reference is rejected (422) or duplicates; and a transport failure leaves outcome unknown — MUST load maxio-advanced-billing:dotnet-configuration-resilience.
- Typed-error ladders: Case A accessors order and RawError last; ReadCustomerByReference/ListCustomerSubscriptions are Case B (different catch type) — MUST load maxio-advanced-billing:dotnet-error-handling.
- Singleton registration captures options once; what the DI helper does with HttpClient/DNS and logging — MUST load maxio-advanced-billing:dotnet-client-initialization.
- Blank credential parts are not "missing" — MUST load maxio-advanced-billing:dotnet-authentication.
- What `Timeout` bounds vs what bounds a whole call; default logging on under DI — MUST load maxio-advanced-billing:dotnet-configuration-resilience.
- Enum interpolation and optional/nullable response members — MUST load maxio-advanced-billing:dotnet-models.
- Tests via stub HttpMessageHandler; request body readable only inside handler — MUST load maxio-advanced-billing:dotnet-testing.

## 4. REQUIRED READING (all loaded before implementation)

- maxio-advanced-billing:dotnet-error-handling · every try/catch
- maxio-advanced-billing:dotnet-client-initialization · DI registration
- maxio-advanced-billing:dotnet-authentication · BasicAuth + startup validation
- maxio-advanced-billing:dotnet-configuration-resilience · retries/timeouts/logging/unknown outcomes
- maxio-advanced-billing:dotnet-calling-endpoints · request records
- maxio-advanced-billing:dotnet-models · enums/optional members
- maxio-advanced-billing:dotnet-testing · stub handler tests
- Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:` section; `ValidateOnStart` fails host start if `ApiKey`, `Subdomain` or `ProductFamilyHandle` is null/whitespace (message names key, never value). `BaseUrl` optional. |
| 2 | Secret sourcing & rotation | Values come from .NET user-secrets/env (keys `Maxio:ApiKey` etc.); options built once at registration in the singleton client — rotation needs restart (accepted). |
| 3 | Total timeout budget | 30 s per service call, enforced by a linked `CancellationTokenSource.CancelAfter` in the one `Bounded(...)` helper in the service that fronts the SDK; per-attempt 10 s via `options.Retry.Timeout` and named `HttpClient.Timeout` 10 s. Subscribe makes ≤4 SDK calls under one Bounded token. |
| 4 | Write-retry ownership | Both writes are POST → default `HttpMethodsToRetry` (GET/HEAD/PUT/OPTIONS) is left unchanged, so the SDK never resends them; reads may be retried. |
| 5 | Idempotency & ambiguous writes | No write has a real idempotency key in its request record (`Idempotency-Key` header is injected random Guid — not cited). Keys: local claim PK (BuyerId, PlanHandle) + customer `reference` + subscription `reference`; reconciliation by `ReadCustomerByReference` / `FindSubscription`. |
| 6 | Observability | Information: subscribe start/finish with plan handle; Warning: reconciliation, unknown outcome; Error: unexpected provider failure. Maxio error `errors` list / HTTP status logged; no response correlation id exists on these error models (not in map), so status + our reference are the correlation. SDK logger factory set explicitly. |
| 7 | Sensitive data | Request bodies carry name/email (personal data). `LogRequestBody=false`, `LoggerFactory` assigned explicitly (disables env-var trace override), headers off. Our own logs don't echo bodies. |
| 8 | Environment selection | Only server group `Production` touched (Ebb not used). Environment `Us` (default); site from `Maxio:Subdomain`; `Maxio:BaseUrl` verbatim override (also how EU/mock hosts are targeted). Dev/test use the sandbox site supplied via secrets; no sandbox environment constant exists in SDK — traffic separation is by site subdomain. |
| 9 | Duplicate prevention | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Subscribe (CreateCustomer + CreateSubscription) | `SubscriptionClaims` table in `CatalogContext` (the app's own EF store); row inserted (status Pending) before any Maxio write | composite primary key (BuyerId, PlanHandle) — enforced by in-memory and SQL providers | `catch (Exception ex) when (ex is DbUpdateException or ArgumentException or InvalidOperationException)` around the claim insert (in-memory provider raises ArgumentException); the loser then reads the existing row and returns / reconciles it | `MaxioSubscriptionService.ClaimAsync` (claim insert) → `MaxioSubscriptionService.SubscribeCoreAsync` (`CreateSubscription` call) |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| plan listing (`ListProductsForProductFamily`) | page cap 5 × 200 per page | `SubscriptionPlanList.IsPartial` → response field `isPartial` | `MaxioSubscriptionService.FetchPlansAsync` (sets `partial`) |

**UNKNOWN OUTCOMES**

| Write | Re-read with | Searched by | Where in the code | Test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `ReadCustomerByReference` | customer reference (user name) | `MaxioSubscriptionService.EnsureCustomerAsync` (catch → `FindCustomerAsync`) | `Subscribe_CustomerCreateConnectionFails_ButCustomerExists_UsesThatCustomer` |
| `CreateSubscription` | `FindSubscription` | subscription reference `eshop:{name}:{plan}`; if not found, claim row set to Unknown and settled by the next Subscribe for that (user, plan) | `MaxioSubscriptionService.SubscribeCoreAsync` catch → `SettleAsync` / `MarkUnknownAsync`; later `ClaimAsync` reconciliation | `Subscribe_ConnectionFailsAfterWrite_*`, `Subscribe_UnknownOutcomeThatActuallyLanded_*` (UnitTests) |

## 6. Assumptions & Blockers

- Blockers: none. All needed capabilities exist in the plugin.
- Assumptions: Identity user has no first/last name → customer `FirstName` = email local part, `LastName` = "Shopper"; user email = JWT name claim. One subscription per (user, plan). Plan IDs in the task are stale-able → only handles are used. `Maxio:Environment` is not a bound key; default `Us` (EU hosts via `Maxio:BaseUrl`).
- **Payment**: with the default `automatic` collection the sandbox answered 422 "No payment method was on file for the $299.00 balance" even though the plans do not require a card at signup; `CreateSubscription.PaymentCollectionMethod` is documented on the request model (`remittance` = invoice), so the subscription is created with `CollectionMethod.Remittance` and no card is captured. `YOUR CALL — not in the map`.
- The SDK is a `ProjectReference` into the plugin directory (local to this machine; re-point if the plugin moves).

## 7. Source labels
Every row above cites its map page or map-named file in the `source` column / inline; decisions in §5/§6 are `YOUR CALL — not in the map`.
