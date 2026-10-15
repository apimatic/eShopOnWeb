# Maxio Advanced Billing plan — eShopOnWeb subscriptions

SDK root (plugin-relative): `sdk/dotnet/` · namespace `MaxioAdvancedBilling` · referenced as a local ProjectReference (machine-local; re-point if plugin moves).

## 1. Scope & sequence

1. Options + startup validation (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`); singleton client.
2. Plans read: `ProductFamilies.ListProductFamilies` → match handle → `ProductFamilies.ListProductsForProductFamily` (paged).
3. Subscribe: validate plan ∈ step-2 set → claim (EF) → `Customers.ReadCustomerByReference` / `Customers.CreateCustomer` → `Subscriptions.CreateSubscription` → record.
4. My subscriptions: `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions`.
5. Endpoints `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions` (JWT).
6. Unknown-outcome reconciliation: `Subscriptions.FindSubscription` by `Reference`.

## 2. CONTRACT SHEET

> Signatures are generated code, verbatim: each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer using the record's own property names — never flat arguments.
> Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from a neighbouring type.

| Controller · method | Request record (members) | Body model | Response | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies.ListProductFamilies(MaxioAdvancedBilling.Requests.ProductFamilies.ListProductFamiliesRequest, requestOptions?, ct)` | all optional (DateField, Start/End…) — none left out that matter | — | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; `.ProductFamily?` (`Id: int?`, `Handle: string?`, `ArchivedAt`) | B `ApiException<RawError>` | none | map/operations/ProductFamilies.md; Models/ProductFamily.cs |
| `client.ProductFamilies.ListProductsForProductFamily(…Requests.ProductFamilies.ListProductsForProductFamilyRequest)` | `ProductFamilyId: string, required`; `Page: int=1`, `PerPage: int=20 (max 200)`, `IncludeArchived: bool?` | — | `IReadOnlyList<…Models.ProductResponse>`; `.Product` (`Id`, `Name`, `Handle`, `Description`, `PriceInCents: long?`, `Interval: int?`, `IntervalUnit`, `ArchivedAt`, `RequireCreditCard`) | A `ApiException<…Errors.ListProductsForProductFamilyError>`: `TryGetString`[404], `TryGetRawError` | none server-side; drive `Page`/`PerPage` manually | ProductFamilies.md; Models/Product.cs |
| `client.Customers.ReadCustomerByReference(…Requests.Customers.ReadCustomerByReferenceRequest)` | `Reference: string, required` | — | `…Models.CustomerResponse` → `.Customer` (`Id: int?`, `Reference`, `Email`) | B (404 when absent: `ex.StatusCode`) | none | Customers.md |
| `client.Customers.CreateCustomer(…Requests.Customers.CreateCustomerOperationRequest)` | `Body: CreateCustomerRequest?` | `…Models.CreateCustomerRequest{ Customer (customer): CreateCustomer, required }`; `CreateCustomer`: `FirstName (first_name)`, `LastName (last_name)`, `Email (email)` required string; `Reference (reference)` optional | `CustomerResponse` | A `ApiException<…Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1`[422], `TryGetRawError` | none | Customers.md; Models/CreateCustomer.cs |
| `client.Customers.ListCustomerSubscriptions(…Requests.Customers.ListCustomerSubscriptionsRequest)` | `CustomerId: int, required` | — | `IReadOnlyList<…Models.SubscriptionResponse>`; `.Subscription?` | B | none | Customers.md |
| `client.Subscriptions.CreateSubscription(…Requests.Subscriptions.CreateSubscriptionOperationRequest)` | `Body: CreateSubscriptionRequest?` | `CreateSubscriptionRequest{ Subscription (subscription): CreateSubscription, required }`; carried members: `ProductHandle (product_handle)`, `CustomerId (customer_id): int?`, `Reference (reference): string?`. Left out: payment profile/card, coupons, components, dates (task: no card capture) | `SubscriptionResponse.Subscription?`: `Id`, `State: SubscriptionState?`, `ProductPriceInCents: long?`, `NextAssessmentAt`, `CurrentPeriodEndsAt`, `Reference`, `Product` (`Handle`, `Name`), `Customer` | A `ApiException<…Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1`[422], `TryGetRawError` | none | Subscriptions.md; Models/CreateSubscription.cs, Subscription.cs |
| `client.Subscriptions.FindSubscription(…Requests.Subscriptions.FindSubscriptionRequest)` | `Reference: string?` (optional, but the lookup key) | — | `SubscriptionResponse` | A `ApiException<…Errors.FindSubscriptionError>`: `TryGetNoContent(out RawError)`[404], `TryGetRawError` | none | Subscriptions.md |

`Reference` on `CreateSubscription` is a real caller key (record member) but the provider's dedupe on it is `UNVERIFIED`; it is used as the reconciliation search key, not as a guard.

Enums: `MaxioAdvancedBilling.Models.Enums.SubscriptionState` (open string enum; `Active`, `Trialing`, `PastDue`, `Canceled` …) — surface the wire value.
Client: `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; auth `BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials{ Username = <api key>, Password = "x" }`; env `MaxioAdvancedBilling.Servers.ServerEnvironment.Us`, site via `options.Server.Production.Us.Site`, override `options.Server.Production.Us.BaseUrl` (source: sdk-map.md Servers & auth).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| a plan handle accepted by POST must be one the plans listing offers (configured family, non-archived) | `CreateSubscription` ← `ListProductsForProductFamily` | `MaxioSubscriptionService.SubscribeAsync`, before the claim and before `CreateSubscription` |
| a customer id used for the subscription is the one resolved for the token's user | `CreateSubscription` ← `ReadCustomerByReference`/`CreateCustomer` | same method; customer reference derived from the JWT name |
| family id passed to product listing is the one whose handle equals `Maxio:ProductFamilyHandle` | `ListProductsForProductFamily` ← `ListProductFamilies` | `MaxioSubscriptionService.GetPlansAsync` |

## 3. Trap notes

- Client lifetime/registration: DI extension is singleton on the shared default HttpClient; stale DNS → MUST load maxio-advanced-billing:dotnet-client-initialization.
- Missing credential sends unauthenticated calls without exception → MUST load maxio-advanced-billing:dotnet-authentication.
- `Retry.Timeout` and `HttpClient.Timeout` are per-attempt, not a call budget → MUST load maxio-advanced-billing:dotnet-configuration-resilience.
- Default `Logging` is on under DI and a trace env var can dump bodies (customer PII) → MUST load maxio-advanced-billing:dotnet-configuration-resilience.
- Typed vs raw error cases differ per operation; neighbouring error type compiles but never matches → MUST load maxio-advanced-billing:dotnet-error-handling.
- Transport failure on a write leaves outcome unknown → MUST load maxio-advanced-billing:dotnet-configuration-resilience and maxio-advanced-billing:dotnet-error-handling.
- Models: wire names, optionals, open enums → MUST load maxio-advanced-billing:dotnet-models.
- Tests stubbing the SDK → MUST load maxio-advanced-billing:dotnet-testing.

## 4. REQUIRED READING

- maxio-advanced-billing:dotnet-client-initialization · client + DI registration
- maxio-advanced-billing:dotnet-authentication · BasicAuth + fail-fast
- maxio-advanced-billing:dotnet-configuration-resilience · timeouts, retries, logging, unknown outcomes, paging
- maxio-advanced-billing:dotnet-error-handling · catch ladder / boundary mapping
- maxio-advanced-billing:dotnet-calling-endpoints · call shape
- maxio-advanced-billing:dotnet-models · request/response shapes
- maxio-advanced-billing:dotnet-testing · stubbing HttpClient

Hazard row: a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioOptions` bound from `Maxio:`; `ValidateOnStart` checks ApiKey, Subdomain, ProductFamilyHandle non-blank (BaseUrl optional, must be absolute URI if set). Host refuses to start naming the key, never the value. Password half is the constant `x` from map (not a secret). |
| 2 | Secret sourcing & rotation | .NET user-secrets (`Maxio:ApiKey` etc.) loaded from the MAXIO_* env vars; options built once at registration, captured in the singleton client → rotation needs restart. |
| 3 | Total timeout budget | 30s per Maxio-facing service operation via linked CTS around `HttpContext.RequestAborted` in one `Bounded` helper; per-attempt `Retry.Timeout` 8s and named HttpClient.Timeout 8s. Subscribe makes ≤3 sequential writes/reads, all inside the one 30s token. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` (GET/HEAD/PUT/OPTIONS) kept; POST (CreateCustomer, CreateSubscription) never resent by the SDK. Reads retry. |
| 5 | Idempotency & ambiguous writes | CreateCustomer: key = deterministic `Reference` (`eshop-user:<name>`), reconcile by `ReadCustomerByReference`. CreateSubscription: `Reference` = claim GUID, reconcile by `FindSubscription`. `Idempotency-Key` header not cited. |
| 6 | Observability | Information: subscribe start/finish with claim id & Maxio ids; Warning: reconcile/unknown; Error: provider failures with status. SDK `LoggerFactory` set explicitly to host factory, no body/headers logging. Provider error body fields (422 error list) logged without PII bodies. |
| 7 | Sensitive data | Customer email/name travel in request bodies → `LogRequestBody=false`, `LoggerFactory` explicit (disables `*_LOG` env). |
| 8 | Environment selection | Group `Production`, `ServerEnvironment.Us` (default env); site from `Maxio:Subdomain`; `Maxio:BaseUrl`, if set, assigned verbatim to `Server.Production.Us.BaseUrl`. Group `Ebb` not touched. Sandbox = the sandbox site credentials; no code path differs. |
| 9 | Duplicate prevention | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `catch (Exception ex) when (IsDuplicateKey(ex))` (DbUpdateException on SQL Server; InvalidOperationException on in-memory) | `MaxioSubscriptionService.EnsureCustomerAsync` (claim `SaveChangesAsync`), then `CreateCustomerAsync` (`Customers.CreateCustomer`) |
| `catch (Exception ex) when (IsDuplicateKey(ex))` in `TryClaimAsync` | `TryClaimAsync` then `CreateSubscriptionAsync` (`Subscriptions.CreateSubscription`) |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| plans via `ListProductsForProductFamily` | `MaxPages` = 10 × `PerPage` 200 | `SubscriptionPlans.Truncated` bool in the response body (`truncated`) | `GetPlansCoreAsync` (sets `truncated` when page 10 is full) |

**UNKNOWN OUTCOMES**

| Write | Re-read with | Search reference | Where in the code | Test that fails the connection |
| --- | --- | --- | --- | --- |
| CreateCustomer | `Customers.ReadCustomerByReference` | `eshop-user:<name>` | `CreateCustomerAsync` catch (SdkConnectionException/ResponseDeserializationException) | `tests/UnitTests/Infrastructure/MaxioSubscriptionServiceTests.cs` (subscription case; customer path shares the same catch shape) |
| CreateSubscription | `Subscriptions.FindSubscription` | claim GUID `Reference`; if still unreadable the claim stays `Pending` (unknown) and the next subscribe call or `my-subscriptions` read settles it | `CreateSubscriptionAsync` catch (SdkConnectionException/ResponseDeserializationException) | `Subscribe_WhenConnectionFailsAfterSend_*` and `Subscribe_WhenConnectionFailsButSubscriptionLanded_*` |

## 6. Assumptions & Blockers

- Blockers: none. `Maxio:Environment` is not bound; sandbox vs production is purely which site/key is supplied.
- Assumption: eShop identity is the JWT `Name` claim (`ClaimTypes.Name`); email is looked up through `UserManager`.
- Assumption: one subscription per user per plan (claim key); second subscribe to the same plan returns the existing subscription (200) instead of creating.
- Assumption: no card required (plan setting), so no payment profile is sent.
- `my-subscriptions` is read live from Maxio via the customer reference, so it survives restarts of the in-memory DB.
- Existing PublicApi tests start the host with fail-fast validation: test config supplies placeholder non-secret values.

## 7. Source labels
Rows above cite their map page/file; `UNVERIFIED`: provider-side dedupe on `Reference`; whether a 422 body for a non-existent plan handle is returned as expected; `YOUR CALL — not in the map`: claim keys, 30s budget, page cap.

## 8. Implementation notes (post-build)
- `PaymentCollectionMethod = Remittance` is sent: with automatic collection and no card the sandbox answers 422 "No payment method was on file" (observed live), so invoice-based collection is used for these no-card plans. YOUR CALL — not in the map.
- Before creating, the customer's live subscriptions (`Customers.ListCustomerSubscriptions`) are checked for the same plan, so a lost local claim table (in-memory DB restart) never yields a second subscription.
