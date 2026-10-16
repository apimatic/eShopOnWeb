# Maxio Advanced Billing integration plan

## 1. Scope & sequence
| Step | Work |
|---|---|
| 1 | Register plugin SDK, bind Maxio settings, validate credentials; preserve commerce. |
| 2 | Discover offered plans with ProductFamilies.ListProductsForProductFamily using configured handle. |
| 3 | Authenticate shopper using PublicApi JWT, resolve Identity user; claim customer creation, ReadCustomerByReference then CreateCustomer if absent. |
| 4 | Validate selected handle against offered plans; claim subscription creation, FindSubscription then CreateSubscription if absent; return live result. |
| 5 | ListCustomerSubscriptions for current customer; tests and live sandbox verification. |

## 2. CONTRACT SHEET
Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.
Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

Paths below are relative to plugin sdk/dotnet/. Namespace root is MaxioAdvancedBilling; models are .Models; request records are .Requests plus controller; errors are .Errors. Each signature below has trailing RequestOptions? requestOptions = null, CancellationToken cancellationToken = default and returns Task of stated result.

| Controller · signature | Request members | Body / consumed response | Error | Pagination | source |
|---|---|---|---|---|---|
| ProductFamilies · ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | ProductFamilyId:string required (handle: prefix accepted); Page:int=1; PerPage:int=20 max200; IncludeArchived:bool? | IReadOnlyList<ProductResponse>, required Product (product):Product. Product: Id(id):int?, Name(name):string?, Handle(handle):string?, Description(description):string?, PriceInCents(price_in_cents):long?, Interval(interval):int?, IntervalUnit(interval_unit):Enums.IntervalUnit?, ArchivedAt(archived_at):DateTimeOffset?, ProductFamily(product_family):ProductFamily?; family Handle(handle):string? | A ListProductsForProductFamilyError: TryGetString(out string) 404, TryGetRawError fallback | explicit page/per_page; walk to empty/short page | map/operations/ProductFamilies.md; src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs; Models/ProductResponse.cs; Models/Product.cs; Models/ProductFamily.cs |
| Customers · ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Reference:string required | CustomerResponse.Customer(customer):Customer required; Id(id):int?, Reference(reference):string? | B RawError | none | map/operations/Customers.md; Requests/Customers/ReadCustomerByReferenceRequest.cs; Models/CustomerResponse.cs; Models/Customer.cs |
| Customers · CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Body:CreateCustomerRequest? | Customer(customer):CreateCustomer required; FirstName(first_name), LastName(last_name), Email(email):string required; Reference(reference):string? supplied; CustomerResponse as above. Address, tax, organization and payment fields omitted. Reference unique per Api/Customers.cs remarks. | A CreateCustomerError: TryGetCustomerErrorResponse1(out CustomerErrorResponse1)422, TryGetRawError fallback | none | map/operations/Customers.md; Requests/Customers/CreateCustomerOperationRequest.cs; Models/CreateCustomerRequest.cs; Models/CreateCustomer.cs; Api/Customers.cs |
| Subscriptions · FindSubscription(FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Reference:string? supplied | SubscriptionResponse.Subscription(subscription):Subscription?; Id(id):int?, Reference(reference):string?, Customer(customer):Customer?, Product(product):Product?, ProductPriceInCents(product_price_in_cents):long?, State(state):Enums.SubscriptionState?, CurrentPeriodEndsAt(current_period_ends_at):DateTimeOffset?, NextAssessmentAt(next_assessment_at):DateTimeOffset?, Currency(currency):string? | A FindSubscriptionError: TryGetNoContent(out RawError)404, TryGetRawError fallback | none | map/operations/Subscriptions.md; Requests/Subscriptions/FindSubscriptionRequest.cs; Models/SubscriptionResponse.cs; Models/Subscription.cs |
| Subscriptions · CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Body:CreateSubscriptionRequest? | Subscription(subscription):CreateSubscription required; ProductHandle(product_handle):string? supplied (required unless product_id); CustomerId(customer_id):int? supplied (required unless customer_reference/customer_attributes); Reference(reference):string? supplied; PaymentCollectionMethod(payment_collection_method):Enums.CollectionMethod? selected from site architecture (Remittance for relationship invoicing, Invoice for legacy) for no-card recurring invoices. DeferSignup(defer_signup):bool? default false. Payment profile, custom price, coupons and components omitted. Live422 showed automatic collection requires a payment method despite RequireCreditCard=false; SDK-supported invoice collection is selected explicitly. Returns SubscriptionResponse above. | A CreateSubscriptionError: TryGetErrorListResponse1(out ErrorListResponse1)422, TryGetRawError fallback | none | map/operations/Subscriptions.md; Requests/Subscriptions/CreateSubscriptionOperationRequest.cs; Models/CreateSubscriptionRequest.cs; Models/CreateSubscription.cs; Api/Subscriptions.cs |
| Customers · ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | CustomerId:int required | IReadOnlyList<SubscriptionResponse>, above | B RawError | none per map default | map/operations/Customers.md; Requests/Customers/ListCustomerSubscriptionsRequest.cs |
| Sites · ReadSite(RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | no request record | SiteResponse.Site(site):Site required; RelationshipInvoicingEnabled(relationship_invoicing_enabled):bool? controls supported no-card collection; Test(test):bool? verified true in external read-only diagnostic | B RawError | none | map/operations/Sites.md; Models/SiteResponse.cs; Models/Site.cs |

### CROSS-OPERATION INVARIANTS
| Invariant | Operations | Enforced where |
|---|---|---|
| ProductHandle accepted for signup must be an unarchived offered plan from configured family | CreateSubscription ← ListProductsForProductFamily | subscription service before CreateSubscription |
| CustomerId belongs to JWT-resolved Identity user | CreateSubscription/ListCustomerSubscriptions ← ReadCustomerByReference/CreateCustomer | subscription service before calls |

### Enums and construction
| Fact | source |
|---|---|
| CollectionMethod.Remittance=remittance (current relationship invoicing); Invoice=invoice (legacy), per CreateSubscription.PaymentCollectionMethod docs. | Models/Enums/CollectionMethod.cs; Models/CreateSubscription.cs |
| IntervalUnit Day=day, Month=month; project wire value without restricting future open values. SubscriptionState Pending=pending, FailedToCreate=failed_to_create, Trialing=trialing, Assessing=assessing, Active=active, SoftFailure=soft_failure, PastDue=past_due, Suspended=suspended, Canceled=canceled, Expired=expired, Paused=paused, Unpaid=unpaid, TrialEnded=trial_ended, OnHold=on_hold; preserve unknown states. | Models/Enums/IntervalUnit.cs; Models/Enums/SubscriptionState.cs |
| new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions); BasicAuth Core.Authentication.Basic.BasicAuthCredentials Username=API key Password=x. | sdk-map.md |
| ServerEnvironment Us(default), Eu; Production Us https://{site}.chargify.com, Eu https://{site}.ebilling.maxio.com; Ebb both https://events.chargify.com/{site}; site default subdomain. Only Production touched; use Server.Production.Us.Site and BaseUrl override verbatim; no SDK sandbox enum, sandbox selected by supplied site's credentials. | sdk-map.md |

## 3. Trap notes
- Registration: HttpClient lifetime and credential placement can break calls. MUST load maxio-advanced-billing:dotnet-client-initialization and maxio-advanced-billing:dotnet-authentication.
- Reads/writes: request-record overloads and nested envelopes can cause compile or mapping failures. MUST load maxio-advanced-billing:dotnet-calling-endpoints and maxio-advanced-billing:dotnet-models.
- Resilience: what timeout bounds, write retry behavior and unknown outcomes affect duplicate safety. MUST load maxio-advanced-billing:dotnet-configuration-resilience.
- Error boundary: typed errors alone miss malformed responses. MUST load maxio-advanced-billing:dotnet-error-handling.
- Verification: fake HTTP seam and transport fault shape affect meaningful assertions. MUST load maxio-advanced-billing:dotnet-testing.

## 4. REQUIRED READING
- maxio-advanced-billing:dotnet-getting-started — all contracts.
- maxio-advanced-billing:dotnet-client-initialization — step 1.
- maxio-advanced-billing:dotnet-authentication — step 1.
- maxio-advanced-billing:dotnet-calling-endpoints — steps 2–5.
- maxio-advanced-billing:dotnet-models — steps 2–5.
- maxio-advanced-billing:dotnet-configuration-resilience — steps 1–5.
- maxio-advanced-billing:dotnet-error-handling — steps 1–5.
- maxio-advanced-billing:dotnet-testing — step 5.

a body that does not match its declared type — a drifted or malformed **2xx** response (a missing required member) or a **non-2xx** body that does not match its operation's generated {Operation}Error shape — surfaces as ResponseDeserializationException, an ApiException that keeps the HTTP status and names the target type but is **not** an ApiException<TError>; a catch ladder that handles only ApiException<TError> lets it escape, so it must also catch ResponseDeserializationException (or ApiException).

## 5. PRODUCTION READINESS
| # | Concern | Decision |
|---|---|---|
| 1 | Fail-fast | PublicApi registration binds exactly Maxio:ApiKey, Subdomain, ProductFamilyHandle, BaseUrl; rejects blank required fields, invalid override. |
| 2 | Secrets | Load provided env values into PublicApi user-secrets outside repo; options captured singleton, restart on rotation. No credential values in repo. |
| 3 | Timeout | Whole endpoint budget 60s; independent reconciliation budget 15s after ambiguous writes. Per-attempt 10s, at most one GET retry; POST never retried. |
| 4 | Retry ownership | SDK GET retry only; writes disabled for both customer and subscription. |
| 5 | Idempotency | Neither write request record exposes real key; generated GUID header is not deduplication. Stable customer reference by Identity ID and stable subscription reference per user+plan; application claims gate writes; reconcile by references. |
| 6 | Observability | Safe operation/status at Warning, completed IDs at Information; never raw error bodies/PII. Error correlation ID: not declared on consumed response contracts, use application trace ID. |
| 7 | Sensitive data | Names/email personal: request bodies/headers off, explicit LoggerFactory; JSON only, no form-body keys used. |
| 8 | Environment | Us Production configured site sandbox; optional BaseUrl overrides it verbatim. Eu and Ebb not used. The supplied site is explicitly sandbox per user; MAXIO_ENVIRONMENT is not interpreted as an invented sandbox SDK enum. |
| 9 | Concurrency | EF primary-key claims persisted before provider calls. SQL production; explicit in-memory development limit accepted by user. |
| 10 | Partial reads | Plans page cap returns explicit incomplete/error, never silently offers partial set. |
| 11 | Unknown | Retain unknown claim; reconcile in failing catch, plus on subsequent reads/retries with no new write until resolved. |

### DUPLICATE CLAIMS
| Write | Where claim stored | What rejects second | Where rejection caught | Where in code |
|---|---|---|---|---|
| CreateCustomer | EF owned table; primary key scoped to site/user/customer | primary-key insertion refusal, including in-memory | claim acquisition catches DbUpdateException/duplicate ArgumentException, then reads prior claim | SubscriptionService.TryClaim → SubscriptionService.Customer / client.Customers.CreateCustomer |
| CreateSubscription | EF owned table; primary key scoped to site/user/plan | primary-key insertion refusal, including in-memory | claim acquisition catches DbUpdateException/duplicate ArgumentException, then reads prior claim | SubscriptionService.TryClaim → SubscriptionService.Enroll / client.Subscriptions.CreateSubscription |

Order: claim → SDK call → record result. Release on definite provider rejection, retain ambiguous claim. A prior pending claim must never authorize a new POST. An unknown state must have executable reconciliation on subsequent access. Handle process crash before POST conservatively; never auto-expire a claim into a retry.

### PAGED READS
| Read | Cap | Caller learns cutoff | Where in code |
|---|---|---|---|
| ListProductsForProductFamily | 100 pages ×200 | explicit catalog-incomplete error response (no partial offers) | SubscriptionService.Plans |

### UNKNOWN OUTCOMES
| Write | Re-read | Reference | Where in code | Test failing connection |
|---|---|---|---|---|
| CreateCustomer | ReadCustomerByReference | stable Identity customer reference | SubscriptionService.Write catch → ReadCustomer; Customer on subsequent requests | SubscriptionBillingTests.LostCustomerResponseReconcilesWithoutResend |
| CreateSubscription | FindSubscription | stable user+plan subscription reference | SubscriptionService.Write catch → ReadSubscription; Enroll/Mine on subsequent requests | SubscriptionBillingTests.LostSubscriptionResponseReconcilesWithoutResend |

## 6. Assumptions & Blockers
- Minor assumption: one enrollment per user per product handle; repeated POST returns existing result; caller must choose offered handle. No catalog IDs or handles hard-coded.
- User explicitly permits in-memory persistence loss during a run; production uses existing SQL store and migration. No blockers.
- UNVERIFIED: live wire may omit nullable subscription fields; validate essential returned IDs, ownership, product and envelope; fail safely, retaining unknown claim where write outcome cannot be proved.
- YOUR CALL — not in the map: application persistence, claims, routes, DTOs and budgets above.
- Repo patterns: MinimalApi.Endpoint IEndpoint + AddRoute (src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs); JWT scheme attribute same exemplar; EF CatalogContext (src/Infrastructure/Data/CatalogContext.cs); Identity lookup uses src/Infrastructure/Identity/IdentityTokenClaimService.cs. Existing JWT carries Name; resolve user via UserManager instead of trusting client customer IDs.

## 7. Source labels
SDK contracts: named map/source rows above resolve within plugin sdk/dotnet/ (Models/ and Requests/ short paths mean src/MaxioAdvancedBilling/). Application decisions: YOUR CALL — not in the map. Runtime nullability caveat: UNVERIFIED.
