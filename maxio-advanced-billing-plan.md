# Maxio Advanced Billing integration plan

## 1. Scope & sequence

| Step | Work / operations | Repository pattern to follow |
| --- | --- | --- |
| 1 | Bind Maxio settings, startup validation, singleton SDK, named HTTP client | `src/PublicApi/Program.cs` |
| 2 | Browse offered plans with Products.ListProducts, filtering by ProductFamily.Handle | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` |
| 3 | Persist primary-key claims; Customers.ReadCustomerByReference → Customers.CreateCustomer; Subscriptions.FindSubscription → Subscriptions.CreateSubscription | EF: `src/Infrastructure/Data/CatalogContext.cs` |
| 4 | Account read with Customers.ListCustomerSubscriptions; background reconciliation of unfinished claims | Identity: `src/Infrastructure/Identity/IdentityTokenClaimService.cs` |
| 5 | JWT endpoints, transport-stub regression tests, sandbox verification, verification guide | `tests/PublicApiIntegrationTests/AuthEndpoints/AuthenticateEndpointTest.cs` |

## 2. CONTRACT SHEET

Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.

Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

Paths below are relative to plugin `sdk/dotnet/`. All methods return Task of the stated response. Common trailing parameters are `RequestOptions? requestOptions = null, CancellationToken cancellationToken = default`.

| Controller / method signature (before common trailing parameters) | Request members used | Body model / fields | Response envelope / fields read | Error | Paging | Source |
| --- | --- | --- | --- | --- | --- | --- |
| Products.ListProducts(ListProductsRequest request, …) | Page:int default 1; PerPage:int default 20, maximum 200; IncludeArchived:bool? | none | IReadOnlyList<ProductResponse>; ProductResponse.Product (product): required Product; Product fields below | B: ApiException<RawError>; StatusCode / ReadAsBytes / ReadAsString / ReadAsJson | manual Page/PerPage; no Pageable | map/operations/Products.md; src/MaxioAdvancedBilling/Requests/Products/ListProductsRequest.cs; Models/ProductResponse.cs |
| Customers.ReadCustomerByReference(ReadCustomerByReferenceRequest request, …) | Reference:string required | none | CustomerResponse.Customer (customer): required Customer; Id (id):int?; Reference (reference):string? | B as above; genuine 404 alone means absent | none | map/operations/Customers.md; Requests/Customers/ReadCustomerByReferenceRequest.cs; Models/CustomerResponse.cs; Models/Customer.cs |
| Customers.CreateCustomer(CreateCustomerOperationRequest request, …) | Body:CreateCustomerRequest? | CreateCustomerRequest.Customer (customer):required CreateCustomer; FirstName (first_name), LastName (last_name), Email (email):required string; Reference (reference):string? | CustomerResponse.Customer.Id/Reference | A: CreateCustomerError; TryGetCustomerErrorResponse1(out CustomerErrorResponse1) [422], TryGetRawError(out RawError) fallback; CustomerErrorResponse1.Errors(errors):Errors1? (do not expose body) | none | map/operations/Customers.md; Requests/Customers/CreateCustomerOperationRequest.cs; Models/CreateCustomerRequest.cs; Models/CreateCustomer.cs; Api/Customers.cs |
| Subscriptions.FindSubscription(FindSubscriptionRequest request, …) | Reference:string? (always set) | none | SubscriptionResponse.Subscription (subscription):Subscription?; fields below | A: FindSubscriptionError; TryGetNoContent(out RawError) [404], TryGetRawError(out RawError) fallback | none | map/operations/Subscriptions.md; Requests/Subscriptions/FindSubscriptionRequest.cs; Models/SubscriptionResponse.cs; Api/Subscriptions.cs |
| Subscriptions.CreateSubscription(CreateSubscriptionOperationRequest request, …) | Body:CreateSubscriptionRequest? | CreateSubscriptionRequest.Subscription(subscription):required CreateSubscription; ProductHandle(product_handle):string? semantically required unless ProductId; CustomerId(customer_id):int? required unless customer_reference/customer_attributes; Reference(reference):string?; PaymentCollectionMethod(payment_collection_method):Models.Enums.CollectionMethod? | SubscriptionResponse.Subscription; fields below | A: CreateSubscriptionError; TryGetErrorListResponse1(out ErrorListResponse1) [422], TryGetRawError(out RawError) fallback; ErrorListResponse1.Errors(errors):required IReadOnlyList<string> | none | map/operations/Subscriptions.md; Requests/Subscriptions/CreateSubscriptionOperationRequest.cs; Models/CreateSubscriptionRequest.cs; Models/CreateSubscription.cs; Api/Subscriptions.cs |
| Customers.ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, …) | CustomerId:int required | none | IReadOnlyList<SubscriptionResponse> | B as above | none, documented all subscriptions | map/operations/Customers.md; Requests/Customers/ListCustomerSubscriptionsRequest.cs; Api/Customers.cs |

Model paths in the table shorthand are under `src/MaxioAdvancedBilling/`.

| Read model | Fields used (all optional unless stated) | Source |
| --- | --- | --- |
| Product | Id(id):int?; Handle(handle), Name(name), Description(description):string?; PriceInCents(price_in_cents):long?; Interval(interval):int?; IntervalUnit(interval_unit):IntervalUnit?; ArchivedAt(archived_at):DateTimeOffset?; RequireCreditCard(require_credit_card):bool?; ProductFamily(product_family):ProductFamily? | src/MaxioAdvancedBilling/Models/Product.cs |
| ProductFamily | Handle(handle):string?; ArchivedAt(archived_at):DateTimeOffset? | src/MaxioAdvancedBilling/Models/ProductFamily.cs |
| Subscription | Id(id):int?; Reference(reference):string?; State(state):SubscriptionState?; ProductPriceInCents(product_price_in_cents):long?; CurrentPeriodEndsAt(current_period_ends_at), NextAssessmentAt(next_assessment_at):DateTimeOffset?; Customer(customer):Customer?; Product(product):Product?; Currency(currency):string? | src/MaxioAdvancedBilling/Models/Subscription.cs |

Unused optional create fields: product_id, customer_reference, customer_attributes, all payment/profile fields, custom prices, coupons, components, scheduling and imports. Remittance collection is explicitly selected for card-free invoice enrollment. Product/customer handles and IDs above meet the documented alternatives; payment capture is outside scope. No trial/fee overrides are sent.

| Enum | Needed values | Source |
| --- | --- | --- |
| Models.Enums.CollectionMethod | Automatic=automatic; Remittance=remittance; Prepaid=prepaid; Invoice=invoice; writes use Remittance | src/MaxioAdvancedBilling/Models/Enums/CollectionMethod.cs |
| Models.Enums.IntervalUnit | Day=day, Month=month; read raw `.Value`, preserve unknown values | src/MaxioAdvancedBilling/Models/Enums/IntervalUnit.cs |
| Models.Enums.SubscriptionState | Pending=pending; FailedToCreate=failed_to_create; Trialing=trialing; Assessing=assessing; Active=active; SoftFailure=soft_failure; PastDue=past_due; Suspended=suspended; Canceled=canceled; Expired=expired; Paused=paused; Unpaid=unpaid; TrialEnded=trial_ended; OnHold=on_hold; AwaitingSignup=awaiting_signup; read raw `.Value` | src/MaxioAdvancedBilling/Models/Enums/SubscriptionState.cs |

| Client fact | Contract | Source |
| --- | --- | --- |
| Construction | MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions); caller owns HttpClient | sdk-map.md |
| Auth | Core.Authentication.Basic.BasicAuthCredentials Username=API key; Password=x; options.BasicAuth | sdk-map.md |
| Server groups / environments | Production: Us https://{site}.chargify.com, Eu https://{site}.ebilling.maxio.com; Ebb: both https://events.chargify.com/{site}; default Us; Production.Us.Site and BaseUrl overrides | sdk-map.md |
| Dependency | ProjectReference to installed plugin source; no published NuGet package; never vendor SDK | skills/dotnet/dotnet-getting-started/SKILL.md |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| Selected productHandle belongs to active, card-free products returned for the configured family | CreateSubscription ← ListProducts | SubscriptionService.SubscribeAsync before any provider write |
| CustomerId is the ID read/created for the authenticated user's deterministic reference | CreateSubscription ← ReadCustomerByReference / CreateCustomer | SubscriptionService.EnsureCustomerAsync |

## 3. Trap notes

| Step | Hazard |
| --- | --- |
| 1 | Singleton lifetime and DNS handling can leave requests on stale connections. MUST load maxio-advanced-billing:dotnet-client-initialization |
| 1 | Missing credentials may send an unauthenticated request. MUST load maxio-advanced-billing:dotnet-authentication |
| 1–4 | What timeout bounds, write resends, ambiguous outcomes and partial page sets cost correctness. MUST load maxio-advanced-billing:dotnet-configuration-resilience |
| 2–4 | Request-record and response-envelope mismatches break calls. MUST load maxio-advanced-billing:dotnet-calling-endpoints |
| 2–4 | Nullable members and open-enum serialization can silently change replies. MUST load maxio-advanced-billing:dotnet-models |
| 3–4 | Typed errors alone do not cover transport and response drift. MUST load maxio-advanced-billing:dotnet-error-handling |
| 5 | A status failure does not test a dropped connection after a write. MUST load maxio-advanced-billing:dotnet-testing |

## 4. REQUIRED READING

| Skill | Step |
| --- | --- |
| maxio-advanced-billing:dotnet-client-initialization | 1 |
| maxio-advanced-billing:dotnet-authentication | 1 |
| maxio-advanced-billing:dotnet-configuration-resilience | 1–4 |
| maxio-advanced-billing:dotnet-calling-endpoints | 2–4 |
| maxio-advanced-billing:dotnet-models | 2–4 |
| maxio-advanced-billing:dotnet-error-handling | 3–4 |
| maxio-advanced-billing:dotnet-testing | 5 |

a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision | Source |
| --- | --- | --- | --- |
| 1 | Credential fail-fast | PublicApi startup binds Maxio:ApiKey, Subdomain, ProductFamilyHandle, BaseUrl; requires first three nonblank; validates optional absolute HTTPS BaseUrl | YOUR CALL — not in the map |
| 2 | Sourcing / rotation | Import supplied env values into PublicApi user-secrets outside repo; bind section once at registration; rotation requires restart | YOUR CALL — not in the map |
| 3 | Timeout budget | 45-second foreground workflow deadline plus separate 10-second reconciliation reserve; 10-second per-attempt SDK/HTTP bounds; worker has 15-second per-row budget | YOUR CALL — not in the map |
| 4 | Write retry ownership | Disable SDK retries entirely; never resend uncertain POST; definitive refusal releases claim | YOUR CALL — not in the map |
| 5 | Idempotency | Neither request record has a caller key; deterministic customer/subscription references and local claims; unknown subscription write never automatically repeated | Requests/Customers/CreateCustomerOperationRequest.cs; Requests/Subscriptions/CreateSubscriptionOperationRequest.cs |
| 6 | Observability | SDK information request/status lines, bodies/headers off; warning for provider status and local trace ID, no body/PII; no declared correlation member on typed payloads used here | Models/CustomerErrorResponse1.cs; Models/ErrorListResponse1.cs |
| 7 | Sensitive data | Customer names/email are sensitive; explicitly assign LoggerFactory, disable bodies/headers; no form bodies; suppress exception-body output at API boundary | Models/CreateCustomer.cs |
| 8 | Environment | Only Production server group touched; SDK has regions Us/Eu, no sandbox enum. Use supplied sandbox site/credentials; MAXIO_ENVIRONMENT is a region indicator, not a sandbox enum; BaseUrl override used verbatim, no seeded identifiers in code | sdk-map.md |
| 9 | Concurrency | Billing EF context using existing catalog SQL connection in production, InMemory for this machine; primary key refuses second claim before SDK call, caught as DbUpdateException or in-memory ArgumentException | YOUR CALL — not in the map |
| 10 | Partial results | Manual products paging max 50×200; explicit error at cap, never expose incomplete catalog; customer subscriptions is unpaged | Requests/Products/ListProductsRequest.cs; map/operations/Customers.md |
| 11 | Unknown outcomes | Re-read reference in write catch; background worker retries reference lookup for pending claims; no repeat write while unresolved; alert after aging, operator may resolve only with provider evidence | YOUR CALL — not in the map |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| CreateCustomer | BillingClaims, deterministic site+user customer primary key | DB primary key; InMemory also rejects duplicate PK | BillingClaimStore.TryClaimAsync catches DbUpdateException / ArgumentException and verifies existing row | BillingClaimStore.TryClaimAsync → SubscriptionService.EnsureCustomerAsync (Customers.CreateCustomer) |
| CreateSubscription | BillingClaims, deterministic site+user+productHandle subscription primary key | DB primary key; InMemory also rejects duplicate PK | BillingClaimStore.TryClaimAsync catches DbUpdateException / ArgumentException and verifies existing row | BillingClaimStore.TryClaimAsync → SubscriptionService.SubscribeAsync (Subscriptions.CreateSubscription) |

### PAGED READS

| Read | What caps it | How caller learns answer was cut short | Where in the code |
| --- | --- | --- | --- |
| ListProducts | 50 pages, 200 per page, shared deadline | BillingException → 503 ProblemDetails; never return a partial list | SubscriptionService.OfferedProductsAsync |

### UNKNOWN OUTCOMES

| Write | Operation re-read | Reference searched | Where in the code | Test that fails connection |
| --- | --- | --- | --- | --- |
| CreateCustomer | ReadCustomerByReference | deterministic hash of identity user ID | SubscriptionService.EnsureCustomerAsync catch → RecoverCustomerAsync; BillingReconciliationWorker → ReconcileAsync | SubscriptionBillingTests.LostCustomerResponseReconcilesWithoutRepeatingWrite |
| CreateSubscription | FindSubscription | deterministic hash of identity user ID and plan handle | SubscriptionService.SubscribeAsync catch → RecoverSubscriptionAsync; BillingReconciliationWorker → ReconcileAsync | SubscriptionBillingTests.LostSubscriptionResponseReconcilesWithoutRepeatingWrite; UnresolvedWriteRemainsClaimedUntilBackgroundReconciliation |

## 6. Assumptions & Blockers

| Kind | Decision |
| --- | --- |
| Minor assumption | One subscription per user per plan; repeated POST returns same subscription, including terminal state. Additional subscriptions to same plan and lifecycle changes are out of scope. |
| Minor assumption | Request supplies firstName/lastName on enrollment; email always comes from Identity. |
| Minor assumption | InMemory is single-process demo mode explicitly requested by task; claims survive restarts only with production SQL. Stable provider references recover existing records. |
| UNVERIFIED | Successful provider response may omit essential optional fields. Check envelope, IDs, ownership, plan, price/state and date semantics defensively; malformed writes go through reconciliation. |
| Environment | Untouched build cannot select pinned SDK 8.0.x on this machine. After this plan, change global.json rollForward to latestMajor and run with DOTNET_ROLL_FORWARD=Major. |
| Blockers | None. Installed plugin exposes all needed operations; existing EF/SQL store supports durable atomic claims without new infrastructure. |
| Live verification | Automatic collection refused card-free paid signup (422: no payment method for balance). The source-supported CollectionMethod.Remittance is explicitly sent for invoice billing; both offered sandbox plans then enrolled active with next dates. Concurrent reads of a transient pending provider object must remain pending. |

## 7. Source labels

Contract paths refer to the plugin's `sdk/dotnet/` source. `UNVERIFIED` identifies live behavior requiring defensive checks. `YOUR CALL — not in the map` identifies application design decisions.
