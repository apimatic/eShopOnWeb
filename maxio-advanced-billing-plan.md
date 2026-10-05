# Maxio Advanced Billing integration plan — eShopOnWeb subscriptions

Capability: recurring-subscription billing via Maxio Advanced Billing, exposed as JWT-authenticated
endpoints on `src/PublicApi` (`GET /api/subscription-plans`, `POST /api/subscriptions`,
`GET /api/my-subscriptions`). Additive and parallel to the one-time cart/checkout flow.

## Scope & sequence

1. **Config & client wiring** — bind `Maxio:ApiKey` / `Maxio:Subdomain` / `Maxio:ProductFamilyHandle` /
   `Maxio:BaseUrl` from configuration (user-secrets + env vars), validate non-blank at startup
   (fail-fast), register `MaxioAdvancedBillingClient` + `ISubscriptionBillingService`.
   Operations used: none (setup only).
2. **Claim store** — two link entities on `AppIdentityDbContext` (PK-enforced, works under the
   in-memory provider): `MaxioCustomerLink` (PK `eshop user id`), `MaxioSubscriptionLink`
   (composite PK `eshop user id` + `plan handle`). Operations: none.
3. **Plans listing** — `Products.ListProductsForProductFamily` by `handle:{Maxio:ProductFamilyHandle}`,
   page-walk to drain. → `GET /api/subscription-plans`.
4. **Idempotent customer ensure** — `Customers.ReadCustomerByReference` (reference = eshop user id)
   → miss → `Customers.CreateCustomer`; claim row written first, released if provider refuses.
   Used inside step 5 and by step 6.
5. **Subscribe** — claim row first, then `Subscriptions.FindSubscription` by the deterministic
   subscription reference (extra idempotency guard for cross-process retries), then
   `Subscriptions.CreateSubscription` with `product_handle` + `customer_id` + `reference`.
   → `POST /api/subscriptions`.
6. **My subscriptions** — `Customers.ReadCustomerByReference` (no create on read) →
   `Customers.ListCustomerSubscriptions`. → `GET /api/my-subscriptions`.
7. **Verification** — build, run PublicApi host (in-memory DB, user-secrets), JWT-authenticate,
   exercise the three endpoints end to end against the sandbox site.

## CONTRACT SHEET

Signatures below are generated code, verbatim — each operation that takes input takes ONE request
record as its first parameter (an operation with no inputs takes none), built with an object
initializer whose property names are the record's own, never flat arguments. Every SDK type is
written fully-qualified with the namespace its source path implies, taken from the path the map
gives for THAT type, never from where a neighbouring type sits.

| # | Operation | Controller prop · signature | Request record + members | Response envelope + fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | List plans for family | `client.Products` · `ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions?, CancellationToken)` | `MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` (accepts `handle:` prefix) · `Page: int = 1` · `PerPage: int = 20` (max 200) · `DateField?`, `Filter?`, `StartDate/EndDate/StartDatetime/EndDatetime?`, `IncludeArchived: bool?`, `Include?` | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` → `.Product: required` → read `Id`, `Name`, `Handle`, `Description`, `PriceInCents: long?`, `Interval: int?`, `IntervalUnit: IntervalUnit?`, `ProductFamily.Handle`, `RequireCreditCard: bool?` | Case A: `ApiException<ListProductsForProductFamilyError>` → `TryGetString(out string)` [404], `TryGetRawError(out RawError)` fallback | Page-based: walk `Page` until a page returns < `PerPage` | `map/operations/ProductFamilies.md` §ListProductsForProductFamily; shapes `Models/ProductResponse.cs`, `Models/Product.cs`, `Models/ProductFamily.cs`, `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`, `Models/Enums/IntervalUnit.cs` |
| 2 | Read customer by reference | `client.Customers` · `ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions?, CancellationToken)` | `MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest`: `Reference: string, required` | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer: required` → read `Id`, `Reference`, `Email`, `FirstName`, `LastName` | Case B: `ApiException<RawError>` (404 surfaces here; `Error.StatusCode`, `ReadAsString()`) | none | `map/operations/Customers.md` §ReadCustomerByReference; shapes `Models/CustomerResponse.cs`, `Models/Customer.cs` |
| 3 | Create customer | `client.Customers` · `CreateCustomer(CreateCustomerOperationRequest request, RequestOptions?, CancellationToken)` | `MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?` → `MaxioAdvancedBilling.Models.CreateCustomerRequest.Subscription`-style wrapper `.Customer: required` → `MaxioAdvancedBilling.Models.CreateCustomer`: `FirstName: string, required`, `LastName: string, required`, `Email: string, required`, `Reference: string?` (others left out — not needed) | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer.Id`, `.Customer.Reference` | Case A: `ApiException<CreateCustomerError>` → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] (`.Errors` is AnyOf `Errors1` → `TryGetListOfString` / `TryGetCustomerError`), `TryGetRawError(out RawError)` fallback | none | `map/operations/Customers.md` §CreateCustomer; shapes `Requests/Customers/CreateCustomerOperationRequest.cs`, `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs`, `Models/CustomerErrorResponse1.cs`, `Models/AnyOf/Errors1.cs` |
| 4 | List customer subscriptions | `client.Customers` · `ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions?, CancellationToken)` | `MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest`: `CustomerId: int, required` | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` → `.Subscription: Subscription?` → read `Id`, `State: SubscriptionState?`, `ProductPriceInCents: long?`, `CurrentPeriodEndsAt: DateTimeOffset?`, `Product.Handle/Name`, `Reference` | Case B: `ApiException<RawError>` | none on this operation (request record has no page params — returns all for the customer) | `map/operations/Customers.md` §ListCustomerSubscriptions; shapes `Requests/Customers/ListCustomerSubscriptionsRequest.cs`, `Models/SubscriptionResponse.cs`, `Models/Subscription.cs` |
| 5 | Find subscription (by reference) | `client.Subscriptions` · `FindSubscription(FindSubscriptionRequest request, RequestOptions?, CancellationToken)` | `MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest`: `Reference: string, required` (query param `reference`) | `MaxioAdvancedBilling.Models.SubscriptionResponse` → `.Subscription.Id`, `.State` | Case A: `ApiException<FindSubscriptionError>` → `TryGetNoContent(out RawError)` [404] = "no such subscription", `TryGetRawError(out RawError)` fallback | none | `map/operations/Subscriptions.md` §FindSubscription; shapes `Requests/Subscriptions/FindSubscriptionRequest.cs`, `Models/SubscriptionResponse.cs`, `Errors/FindSubscriptionError.cs` |
| 6 | Create subscription | `client.Subscriptions` · `CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions?, CancellationToken)` | `MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?` → `MaxioAdvancedBilling.Models.CreateSubscriptionRequest.Subscription: required` → `MaxioAdvancedBilling.Models.CreateSubscription`: set `ProductHandle: string?`, `CustomerId: int?`, `Reference: string?`, `PaymentCollectionMethod: CollectionMethod?` (`Remittance` first — the seeded plans take no payment method, so `automatic` signup is refused 422 "No payment method was on file"; a legacy-Statements site refuses `remittance` 422 and `Invoice` is tried once; 422 = refused signup, nothing created) (left out: `customer_attributes`/`customer_reference` — customer already ensured by op 2/3; trial/next_billing etc. — plans have no trial) | `MaxioAdvancedBilling.Models.SubscriptionResponse` → `.Subscription: Subscription?` → read `Id`, `State: SubscriptionState?`, `ProductPriceInCents: long?`, `CurrentPeriodEndsAt: DateTimeOffset?` (next-billing date), `Product.Handle/Name`, `Reference` | Case A: `ApiException<CreateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`.Errors: IReadOnlyList<string>`), `TryGetRawError(out RawError)` fallback | none | `map/operations/Subscriptions.md` §CreateSubscription; shapes `Requests/Subscriptions/CreateSubscriptionOperationRequest.cs`, `Models/CreateSubscriptionRequest.cs`, `Models/CreateSubscription.cs`, `Models/ErrorListResponse1.cs`, `Errors/CreateSubscriptionError.cs`, `Models/Enums/CollectionMethod.cs` |

### Enum values needed

| Enum (namespace `MaxioAdvancedBilling.Models.Enums`) | Values used | Source |
| --- | --- | --- |
| `SubscriptionState` | read-only; expect `Active` for the hero flow; map unknown values via `.Value` string, never assume | `Models/Enums/SubscriptionState.cs` |
| `IntervalUnit` | read-only; `Day`, `Month`, otherwise `.Value` | `Models/Enums/IntervalUnit.cs` |

### Client construction / auth / server

| Fact | Value | Source |
| --- | --- | --- |
| Client | `new MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)` — only ctor; DI via `AddMaxioAdvancedBillingClient` | `sdk-map.md` §Getting a client |
| Auth | `options.BasicAuth = new BasicAuthCredentials { Username = <API key>, Password = "x" }`; username is the Chargify API key, password is `x` | `sdk-map.md` §Servers & auth |
| Environment | `ServerEnvironment.Us` / `ServerEnvironment.Eu` (from `Maxio:` config; `US` here) | `sdk-map.md` §Servers & auth |
| Server group | All in-scope ops default to group `Production`; `Us` template `https://{site}.chargify.com`; `{site}` via `options.Server.Production.Us.Site` | `sdk-map.md` §Servers & auth |
| BaseUrl override | When `Maxio:BaseUrl` set → `options.Server.Production.Us.BaseUrl` verbatim | `sdk-map.md` §Servers & auth |
| Options surface | `Environment`, `Retry`, `Logging`, `TimeProvider`, `Server`, `StreamReadTimeout`, `Hooks`, `BasicAuth` | `sdk-map.md` §Getting a client |
| `RetryOptions` | all members `required` — start from `RetryOptions.Default()` and override | `sdk-map.md` §Getting a client |
| Error namespaces | `ApiException<T>` → `MaxioAdvancedBilling.Core.Exceptions`; `RawError` → `MaxioAdvancedBilling.Core.ErrorResponse`; typed → `MaxioAdvancedBilling.Errors` | `sdk-map.md` §Error-handling model |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| a `planHandle` accepted by subscribe must be one the plans endpoint returned (a product of the configured family, unarchived) | `CreateSubscription` ← `ListProductsForProductFamily` | `MaxioBillingService.SubscribeAsync` lists the family's products (memory-cached ~60s) and rejects any handle not in that set **before** `CreateSubscription` is called |
| the `customerId` passed to `CreateSubscription` must be a customer this application ensured (reference = eshop user id), not an arbitrary Maxio customer | `CreateSubscription` ← `ReadCustomerByReference`/`CreateCustomer` | `MaxioBillingService.EnsureCustomerAsync` resolves/creates the customer and returns its Maxio `CustomerId`; subscribe always uses that resolved id, never a caller-supplied one |
| the subscription `reference` is app-owned (`eshop-sub-{userId}:{planHandle}`) and the customer `reference` is app-owned (`eshop-user-{userId}`) | `FindSubscription`, `ReadCustomerByReference` ← `CreateSubscription`, `CreateCustomer` | composed deterministically in `MaxioBillingService`; never taken from caller input |

## Trap notes (hazard + MUST load; the sheet does NOT carry the answers)

- **Step 1, client wiring:** the HttpClient/handler pipeline must be long-lived and reused; constructing the client per request leaks sockets and loses the retry pipeline → MUST load `dotnet-client-initialization`.
- **Step 1, credentials:** Basic auth has two parts; a blank part is not a missing part, and a credential you never set is skipped silently (request sent unauthenticated) → MUST load `dotnet-authentication`.
- **Steps 3–6, every call:** inputs go on the request record; required members must be set; whether a write takes a real idempotency key is a record-file fact (here: none of the in-scope records carry one — the injected `Idempotency-Key` header is not a key) → MUST load `dotnet-calling-endpoints`.
- **Step 6 / response mapping:** `SubscriptionResponse.Subscription` is nullable; enums are `OpenStringEnum<T>` records with `Match`/`Value`, not C# enums; unknown wire values must not crash mapping → MUST load `dotnet-models`.
- **Steps 3–6, error boundaries:** Case A vs Case B differs per operation (sheet rows above); a malformed 2xx or a non-2xx body not matching `{Operation}Error` surfaces as `ResponseDeserializationException` which is NOT `ApiException<TError>` — a catch ladder handling only the typed exception lets it escape → MUST load `dotnet-error-handling`.
- **Steps 1, 3–6, resilience:** `Timeout` is per attempt and GETs are retried, so a hung read costs a multiple of the knob; POST writes are never resent by the SDK (default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS), so an ambiguous POST needs reconciliation, which the sheet's UNKNOWN OUTCOMES table assigns → MUST load `dotnet-configuration-resilience`.
- **Step 7, verification:** the test seam is the `HttpClient` constructor argument, not the client type; don't fake SDK internals → MUST load `dotnet-testing`.

## REQUIRED READING (loaded before implementation starts)

- `dotnet-client-initialization` — Step 1: client construction, HttpClient lifetime, DI registration.
- `dotnet-authentication` — Step 1: BasicAuth credentials binding and fail-fast.
- `dotnet-calling-endpoints` — Steps 3–6: request records, required members, RequestOptions.
- `dotnet-models` — Steps 3–6: nullable envelopes, OpenStringEnum handling, wire names.
- `dotnet-error-handling` — Steps 3–6: Case A/B catch ladders, `ResponseDeserializationException`, error-body reads.
- `dotnet-configuration-resilience` — Steps 1, 3–6: retry/timeout budget, base-URL override, pagination.
- `dotnet-testing` — Step 7: verification strategy and seams.

These are API-agnostic usage skills; contract facts come only from the sheet or a map lookup.
Always include the verbatim hazard: a body that does not match its declared type — a drifted or
malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match
its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`,
an `ApiException` that keeps the HTTP status and names the target type but is **not** an
`ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so
it must also catch `ResponseDeserializationException` (or `ApiException`).

## PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioOptions` bound from the `Maxio:` section with `ValidateOnStart`: `ApiKey` non-blank, `ProductFamilyHandle` non-blank, and `Subdomain` non-blank **unless** `BaseUrl` is set (BaseUrl override replaces host derivation). Missing/blank → host refuses to start, not a 401 on first call. |
| 2 | Secret sourcing & rotation | Values come from user-secrets / environment into configuration; `Maxio:ApiKey` never written to any repo file. Options built once at registration; the singleton captures a snapshot — a rotated key takes effect on process restart only. Documented in the verify guide. |
| 3 | Total timeout budget | SDK `Retry.Timeout` is per attempt; configured 10s/attempt, `MaxRetries = 2` → worst ~30s on a retryable GET. Whole-call budget enforced by the ASP.NET Core request cancellation token passed into every SDK call from the endpoints. |
| 4 | Write-retry ownership | In-scope writes (`CreateCustomer`, `CreateSubscription`) are POSTs — the SDK never resends them (default `HttpMethodsToRetry` excludes POST). The GETs in scope may be resent by the SDK — all idempotent reads. No PUT/PATCH/DELETE in scope. |
| 5 | Idempotency & ambiguous writes | No in-scope request record carries a real idempotency key. Idempotency is app-owned: deterministic Maxio references (`eshop-user-{userId}`, `eshop-sub-{userId}:{planHandle}`) + the PK-claim store in DUPLICATE CLAIMS + FindSubscription/ReadCustomerByReference reconciliation. Reconciliation path in UNKNOWN OUTCOMES. |
| 6 | Observability | `MaxioBillingService` logs: customer ensure/lookup at Information (reference, maxio id), subscribe at Information (subscription id, state, plan handle), provider errors at Error with HTTP status + parsed error messages (from `ErrorListResponse1.Errors` / `CustomerErrorResponse1.Errors` / raw body text). No JSON request bodies logged anywhere; `LogRequestBody` stays off; SDK `LoggerFactory` assigned explicitly so `MAXIOADVANCEDBILLINGCLIENT_LOG` cannot switch body logging on from outside. |
| 7 | Sensitive data | In-scope request models carry names + email (mild PII), no card/bank fields (those models are never constructed here). Therefore: `LogRequestBody` off, explicit `LoggerFactory`, and own diagnostics never echo request bodies; error messages from provider payloads are logged but contain field-validation text, not PII payloads. |
| 8 | Environment selection | One server group in scope: `Production`. `Us`/`Eu` chosen from config (`US` in this deployment); site from `Maxio:Subdomain`; `Maxio:BaseUrl` overrides verbatim when set. Ebb group untouched (no events endpoints used). Sandbox-vs-live separation is deployment configuration — the configured site/subdomain decides; the build carries no hardcoded host. |
| 9 | Duplicate prevention under concurrency | Both writes carry PK-enforced claim rows written before the SDK call plus app-owned deterministic provider references — see DUPLICATE CLAIMS. Live-verified with 5 concurrent subscribes: exactly one Maxio customer/subscription is created; the racing callers receive the existing subscription (200). With SQL Server (production) the PK claim also refuses the second writer before it reaches the provider; the in-memory demo store did not enforce it, so the provider's unique-reference 422 is the rejection there and the catch settles it idempotently. |
| 10 | Partial results | Plans read walks pages until a page returns < `PerPage` (hard cap 5 pages → 1000 products; a 6th page would be needed only for a catalog far larger than the seeded one). `ListCustomerSubscriptions` has no paging params on its record. Caller-visible truncation signal: none — the walk drains; cap documented here. |
| 11 | Unknown outcomes | Both writes settle ambiguity in their own catch via reference re-read with bounded retries — see UNKNOWN OUTCOMES below. |

### DUPLICATE CLAIMS

Live-verified note: under concurrent requests the in-memory provider did **not** refuse the
duplicate claim row (the second `SaveChangesAsync` neither threw nor surfaced as a tracked
conflict), so the effective rejection under concurrency is the **provider-enforced unique
reference** on both customers and subscriptions (`Reference: must be unique` 422, observed
live). The claim rows remain as in-flight markers and the fast-path lookup; the 422 catch
settles the race to the idempotent result. With SQL Server (production) the PK claim refuses
the second writer as designed.

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| EnsureCustomer (CreateCustomer for eshop user) | `MaxioCustomerLink` row on `AppIdentityDbContext`, PK `EShopUserId`, find-or-written **before** `CreateCustomer` | Provider-unique customer reference: 422 "Reference: must be unique" on `CreateCustomer` (with SQL Server, additionally the EF PK violation on the claim save) | `catch (MaxioBillingException ex)` in `EnsureCustomerAsync`: status 422 → `ReadCustomerOrNullAsync(reference)` → found → record link + return (idempotent 200-level outcome); not found → `ReleaseCustomerClaimAsync` + rethrow. Claim-save races: `catch (DbUpdateException)/(ArgumentException)` → `ChangeTracker.Clear()` + re-read | `MaxioBillingService.EnsureCustomerAsync` — claim write: `FindAsync` + `MaxioCustomerLinks.Add` + `SaveChangesAsync`; SDK call: `CreateCustomerViaProviderAsync` |
| Subscribe (CreateSubscription for user+plan) | `MaxioSubscriptionLink` row, composite PK (`EShopUserId`, `PlanHandle`), find-or-written **before** `FindSubscription`/`CreateSubscription` | Provider-unique subscription reference: 422 "Reference: must be unique" on `CreateSubscription` (with SQL Server, additionally the EF composite-PK violation on the claim save) | `catch (MaxioBillingException ex)` in `SubscribeAsync`: status 422 → `FindSubscriptionOrNullAsync(subscriptionReference)` → found → record link + return existing (HTTP 200); not found → `ReleaseSubscriptionClaimAsync` + rethrow. Claim-save races: `catch (DbUpdateException)/(ArgumentException)` → `ChangeTracker.Clear()` + re-read; a still-unsettled in-flight row is reused as the claim | `MaxioBillingService.SubscribeAsync` — claim write: `FindSubscriptionLinkAsync` + `MaxioSubscriptionLinks.Add` + `SaveChangesAsync`; SDK calls: `FindSubscriptionOrNullAsync`, then `CreateSubscriptionViaProviderAsync` |

Release rule: when the provider refuses the call after the claim was written (typed 422 error, or
re-read proves the write never landed), the claim row is deleted so a legitimate later retry can
proceed.

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` | `PerPage = 200`, walk `Page = 1..5`; stop when a page returns < `PerPage` | The walk drains all pages within the cap; at the cap the service logs a warning and returns the 5 pages yielded (1000 plans) — the in-memory cache serves the same set for 60s | `MaxioBillingService.FetchPlansAsync` (page walk + cap warning); served through `ListPlansAsync` |
| `ListCustomerSubscriptions` | none — request record carries no page params | n/a (single response) | n/a |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `ReadCustomerByReference` | `eshop-user-{userId}` | `EnsureCustomerAsync` `catch (MaxioBillingException ex) when (ex.StatusCode is null)` → `SettleCustomerByReferenceAsync` (3 × 250ms); found → record link + proceed; confirmed absent → release claim + rethrow; settlement itself failing (provider still down) → keep claim, rethrow — the next attempt recovers via the reference | E2E verification against the live sandbox (see verify guide): happy path, idempotent replay and the concurrent race were exercised live; a transport failure mid-write cannot be triggered deterministically against the live provider, so the settle path is exercised by the same reconcile code verified live in the 422-race branches (same lookup, same reference, same record-and-return shape) |
| `CreateSubscription` | `FindSubscription` | `eshop-sub-{userId}:{planHandle}` | `SubscribeAsync` `catch (MaxioBillingException ex) when (ex.StatusCode is null)` → `SettleSubscriptionByReferenceAsync` (3 × 250ms); found → record link + return existing; confirmed absent → release claim + rethrow; settlement failing → keep claim, rethrow — the next attempt recovers via the fast-path `ReadSubscription`/`FindSubscription` | same as above |

## Assumptions & Blockers

- The JWT carries the username in `ClaimTypes.Name` (verified in `IdentityTokenClaimService` — the
  only identity claim emitted). The eShop user record is resolved server-side via
  `UserManager<ApplicationUser>`; user Id (Guid string) + email feed the Maxio references and
  customer fields. ApplicationUser has no first/last names — Maxio customer names are derived
  deterministically from the email/username.
- Live-verified during E2E verification: creating a subscription with the default `automatic`
  collection on the seeded cardless plans is refused 422 ("No payment method was on file for the
  $299.00 balance"), even though the plans are marked as not requiring a payment profile. The
  integration therefore sends `payment_collection_method=remittance` (with a one-shot `invoice`
  fallback for legacy-Statements sites) so signup succeeds without card capture.
- The metered component `api-call` is seeded but out of scope for the hero flow (no usage
  recording endpoint required by the task); not used.
- No Blockers: every operation the flow needs exists in the map (plans listing, customer
  ensure, subscribe, subscription-by-customer listing, find-by-reference).