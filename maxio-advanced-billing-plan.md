# Maxio subscription integration

## 1. Scope & sequence
| Step | Work / operations | Repository exemplar |
| --- | --- | --- |
| 1 | Bind Maxio settings, register bundled SDK and fail fast | src/PublicApi/Program.cs |
| 2 | Persist customer/subscription claims in existing EF catalog database; add migration | src/Infrastructure/Data/CatalogContext.cs |
| 3 | Discover configured-family plans with Products.ListProducts | src/PublicApi/CatalogTypeEndpoints/CatalogTypeListEndpoint.cs |
| 4 | Customers.ReadCustomerByReference / CreateCustomer, Subscriptions.FindSubscription / CreateSubscription; reconcile uncertain writes | src/PublicApi/AuthEndpoints/AuthenticateEndpoint.cs |
| 5 | Customers.ListCustomerSubscriptions for account view; JWT identity resolved through UserManager | src/Infrastructure/Identity/IdentityTokenClaimService.cs |
| 6 | Stub transport failure/concurrency/security tests, build, sandbox verification | tests/PublicApiIntegrationTests/ProgramTest.cs |

## 2. CONTRACT SHEET
Signatures are generated code, verbatim: each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.
Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

All methods below append `(RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` after the request. Requests live in `MaxioAdvancedBilling.Requests.<controller>`; models in `MaxioAdvancedBilling.Models`; errors in `MaxioAdvancedBilling.Errors`.

| Controller / signature | Request members used | Body fields used | Response fields read | Error | Pagination | Source (sdk/dotnet/) |
| --- | --- | --- | --- | --- | --- | --- |
| Products.ListProducts(ListProductsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Page:int default 1; PerPage:int default 20 max 200; IncludeArchived:bool? | none | IReadOnlyList<ProductResponse>; required Product(product); optional Product fields Handle(handle):string?, Name(name):string?, Description(description):string?, PriceInCents(price_in_cents):long?, Interval(interval):int?, IntervalUnit(interval_unit):enum?, ArchivedAt(archived_at):DateTimeOffset?, RequireCreditCard(require_credit_card):bool?, ProductFamily(product_family):ProductFamily?; family Handle(handle):string?, ArchivedAt(archived_at):DateTimeOffset? | B RawError | Explicit page loop (request source documents pages despite map default) | map/operations/Products.md; src/MaxioAdvancedBilling/Requests/Products/ListProductsRequest.cs; Models/ProductResponse.cs, Product.cs, ProductFamily.cs under src/MaxioAdvancedBilling |
| Customers.ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Reference:string required | none | CustomerResponse.Customer(customer) required; Customer.Id(id):int?, Reference(reference):string? | B RawError; genuine 404 only means absent | none | map/operations/Customers.md; src/MaxioAdvancedBilling/Requests/Customers/ReadCustomerByReferenceRequest.cs; Models/CustomerResponse.cs, Customer.cs; Api/Customers.cs |
| Customers.CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Body:CreateCustomerRequest? | Customer(customer):CreateCustomer required; FirstName(first_name), LastName(last_name), Email(email):string required; Reference(reference):string? | CustomerResponse as above | A CreateCustomerError: TryGetCustomerErrorResponse1(out CustomerErrorResponse1) 422; TryGetRawError(out RawError) fallback; payload Errors(errors):Errors1? | none | map/operations/Customers.md; src/MaxioAdvancedBilling/Requests/Customers/CreateCustomerOperationRequest.cs; Models/CreateCustomerRequest.cs, CreateCustomer.cs, CustomerErrorResponse1.cs; Api/Customers.cs |
| Subscriptions.FindSubscription(FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Reference:string? | none | SubscriptionResponse.Subscription(subscription) required; Subscription.Id(id):int?, Reference(reference):string?, Customer(customer):Customer?, Product(product):Product?, State(state):SubscriptionState?, ProductPriceInCents(product_price_in_cents):long?, NextAssessmentAt(next_assessment_at):DateTimeOffset?, Currency(currency):string? | A FindSubscriptionError: TryGetNoContent(out RawError) 404, TryGetRawError(out RawError) fallback | none | map/operations/Subscriptions.md; src/MaxioAdvancedBilling/Requests/Subscriptions/FindSubscriptionRequest.cs; Models/SubscriptionResponse.cs, Subscription.cs; Api/Subscriptions.cs |
| Subscriptions.CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Body:CreateSubscriptionRequest? | Subscription(subscription):CreateSubscription required; ProductHandle(product_handle):string? required unless product_id; CustomerId(customer_id):int? required unless customer_reference/attributes; Reference(reference):string?; PaymentCollectionMethod(payment_collection_method):CollectionMethod? = Remittance; omit payment profile fields, coupons, imports, custom prices and components | SubscriptionResponse as above | A CreateSubscriptionError: TryGetErrorListResponse1(out ErrorListResponse1) 422; TryGetRawError(out RawError) fallback; payload Errors(errors):IReadOnlyList<string> required | none | map/operations/Subscriptions.md; src/MaxioAdvancedBilling/Requests/Subscriptions/CreateSubscriptionOperationRequest.cs; Models/CreateSubscriptionRequest.cs, CreateSubscription.cs, ErrorListResponse1.cs; Api/Subscriptions.cs |
| Customers.ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | CustomerId:int required | none | IReadOnlyList<SubscriptionResponse> as above; XML states all customer subscriptions | B RawError | none | map/operations/Customers.md; src/MaxioAdvancedBilling/Requests/Customers/ListCustomerSubscriptionsRequest.cs; Api/Customers.cs |

### CROSS-OPERATION INVARIANTS
| Invariant | Operations | Enforced where |
| --- | --- | --- |
| Submitted product handle belongs to nonarchived configured-family products offered by this app | CreateSubscription ← ListProducts | SubscriptionService.SubscribeAsync before claiming/writing |
| Customer id comes from this authenticated user's stable reference | CreateSubscription ← ReadCustomerByReference / CreateCustomer | SubscriptionService.EnsureCustomerAsync |

| Client fact / enum | Value | Source (sdk/dotnet/) |
| --- | --- | --- |
| Client | MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions) | sdk-map.md |
| Auth | Core.Authentication.Basic.BasicAuthCredentials Username=ApiKey; Password=x | sdk-map.md |
| Servers | Production US https://{site}.chargify.com; EU https://{site}.ebilling.maxio.com; Ebb both https://events.chargify.com/{site}. US default; no sandbox enum. Scope only Production. Set Production.Us.Site or BaseUrl verbatim | sdk-map.md |
| CollectionMethod | Automatic=automatic, Remittance=remittance, Prepaid=prepaid, Invoice=invoice; remittance selected for no-card enrollment after live automatic-collection 422 | src/MaxioAdvancedBilling/Models/Enums/CollectionMethod.cs |
| IntervalUnit | Month=month, Day=day; preserve unknown Value | src/MaxioAdvancedBilling/Models/Enums/IntervalUnit.cs |
| SubscriptionState | pending, failed_to_create, trialing, assessing, active, soft_failure, past_due, suspended, canceled, expired, paused, unpaid, trial_ended, on_hold, awaiting_signup; preserve Value | src/MaxioAdvancedBilling/Models/Enums/SubscriptionState.cs |
| Reference uniqueness / visibility | Do not rely on provider rejecting duplicates; enforce database claims; incomplete lookup stays pending | UNVERIFIED |

## 3. Trap notes
| Step | Hazard |
| --- | --- |
| 1 | Client/HttpClient ownership and stale DNS — MUST load maxio-advanced-billing:dotnet-client-initialization |
| 1 | Missing credentials silently send unauthenticated requests — MUST load maxio-advanced-billing:dotnet-authentication |
| 1–5 | Timeout scope, write resend semantics, unknown outcomes, pagination and body logging — MUST load maxio-advanced-billing:dotnet-configuration-resilience |
| 3–5 | Request records and response nesting — MUST load maxio-advanced-billing:dotnet-calling-endpoints |
| 3–5 | Optional model members and open enums — MUST load maxio-advanced-billing:dotnet-models |
| 3–5 | Error shape drift and transport failures — MUST load maxio-advanced-billing:dotnet-error-handling |
| 6 | Transport stubs and disposed request content — MUST load maxio-advanced-billing:dotnet-testing |

## 4. REQUIRED READING
| Skill | Step |
| --- | --- |
| maxio-advanced-billing:dotnet-getting-started | all |
| maxio-advanced-billing:dotnet-client-initialization | 1 |
| maxio-advanced-billing:dotnet-authentication | 1 |
| maxio-advanced-billing:dotnet-configuration-resilience | 1–5 |
| maxio-advanced-billing:dotnet-calling-endpoints | 3–5 |
| maxio-advanced-billing:dotnet-models | 3–5 |
| maxio-advanced-billing:dotnet-error-handling | 3–5 |
| maxio-advanced-billing:dotnet-testing | 6 |

a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS
Application decisions below: YOUR CALL — not in the map.

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind exactly Maxio:ApiKey, Subdomain, ProductFamilyHandle, BaseUrl; reject blank required values and invalid URL before host starts |
| 2 | Secret sourcing & rotation | Import supplied environment values into PublicApi user-secrets outside repo; config captured once at registration, restart to rotate |
| 3 | Total timeout budget | Central SDK boundary gives each read/write 15s total linked token; independent 15s reconciliation; headers attempt 10s; endpoint max bounded page count, cancellation honored |
| 4 | Write retries | Disable SDK retries; no automatic POST resend; database claim authorizes one create only |
| 5 | Idempotency | Neither write request has caller key. Deterministic references plus primary-key claims, reconciliation on ambiguous result |
| 6 | Observability | Log operation, HTTP status, local trace id and exception kind only, no bodies or identity. Inspected typed error models carry no declared provider correlation id |
| 7 | Sensitive data | Customer names/email; SDK logging factory explicitly NullLoggerFactory, bodies/headers off; no form bodies used |
| 8 | Environment | US Production server group with configured subdomain/BaseUrl. Sandbox is site-selected; development script checks MAXIO_ENVIRONMENT before host/test. Ebb unused; EU URLs documented above |
| 9 | Concurrency | Existing CatalogContext database primary key claims (SQL durable; explicitly requested InMemory demo loses claims at restart). Claim before lookup/create, catch duplicate save |
| 10 | Partial results | Products loop 100 pages x 200; return IsTruncated. Reject subscription on truncated catalog rather than accept partial authorization set |
| 11 | Unknown outcomes | Re-read immediately by stable reference, retain pending claim if absent/unavailable; later POST and background sweep reconcile and never blindly resend |

### DUPLICATE CLAIMS
| Write | Where stored | What rejects second | Where caught | Where in code |
| --- | --- | --- | --- | --- |
| CreateCustomer | CatalogContext BillingClaims primary key scoped by site/user/customer | Primary key insertion | ClaimStore.TryClaimAsync catches DbUpdateException / ArgumentException and confirms existing key | ClaimStore.TryClaimAsync → MaxioGateway.CreateCustomerAsync |
| CreateSubscription | CatalogContext BillingClaims primary key scoped by site/user/product handle | Primary key insertion | ClaimStore.TryClaimAsync catches DbUpdateException / ArgumentException and confirms existing key | ClaimStore.TryClaimAsync → MaxioGateway.CreateSubscriptionAsync |

### PAGED READS
| Read | Cap | Caller sees | Where in code |
| --- | --- | --- | --- |
| ListProducts | 100 pages of 200, total SDK call budgets | PlanList.IsTruncated | MaxioGateway.PlansAsync |

### UNKNOWN OUTCOMES
| Write | Re-read | Reference | Where in code | Test failing connection |
| --- | --- | --- | --- | --- |
| CreateCustomer | ReadCustomerByReference | deterministic site/user customer reference | SubscriptionService.CreateOnceAsync catch → ReconcileAsync; BillingReconciler.ExecuteAsync sweep | SubscriptionEndpointsTests.LostCustomerResponseIsReconciledWithoutResend |
| CreateSubscription | FindSubscription | deterministic site/user/plan subscription reference; verify owner and plan | SubscriptionService.CreateOnceAsync catch → ReconcileAsync; BillingReconciler.ExecuteAsync sweep | SubscriptionEndpointsTests.LostSubscriptionResponseIsReconciledWithoutResend; UnknownOutcomeStaysPendingAndLaterRequestReconciles |

## 6. Assumptions & Blockers
No capability gaps. One subscription per user per plan, with repeated requests returning that subscription regardless of state; lifecycle changes are outside this additive scope. POST accepts first/last name because ApplicationUser has no name fields; email comes exclusively from Identity. SQL deployments apply the included migration; InMemory restart limitations are explicitly authorized by task, with remote stable-reference recovery. No cart/checkout changes. Live sandbox automatic collection rejected enrollment without a payment profile; use SDK-declared remittance collection, leaving invoices payable. This is an application policy, verified with live enrollment.

## 7. Source labels
SDK paths above are plugin-relative to sdk/dotnet. UNVERIFIED facts are guarded defensively. Application policy is YOUR CALL — not in the map.


