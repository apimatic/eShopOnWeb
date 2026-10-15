# Maxio Advanced Billing integration plan

## Scope & sequence
| Step | Work | Source |
| --- | --- | --- |
| 1 | Bind settings, validate startup, reference plugin SDK in place, configure singleton SDK client and HTTP transport. | sdk/dotnet/sdk-map.md |
| 2 | Add EF claim records to CatalogContext, migration, atomic primary-key claim store, unknown-state reconciliation worker. | YOUR CALL — not in the map; src/Infrastructure/Data/CatalogContext.cs |
| 3 | JWT-only GET /api/subscription-plans: ListProductsForProductFamily using `handle:` + Maxio:ProductFamilyHandle; expose eligible nonarchived recurring plans and provider price. | map/operations/ProductFamilies.md |
| 4 | JWT-only POST /api/subscriptions accepts only product handle. Resolve authenticated user, validate offered handle, ensure customer using ReadCustomerByReference/CreateCustomer, then FindSubscription/CreateSubscription under claims. | map/operations/Customers.md; map/operations/Subscriptions.md |
| 5 | JWT-only GET /api/my-subscriptions resolves same customer reference then ListSubscriptions filtered by provider customer ID; returns current provider plan, price, state and next assessment. | map/operations/Subscriptions.md |
| 6 | Mock transport tests including concurrent callers, primary key enforcement with InMemory, lost response reconciliation, malformed responses, paging, unauthorized access and configuration. Document setup and verify against supplied sandbox if credentials available. | YOUR CALL — not in the map |

## CONTRACT SHEET
Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.
Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

All SDK paths below are relative to plugin sdk/dotnet/. Records: MaxioAdvancedBilling.Models; request records: MaxioAdvancedBilling.Requests.<Controller>; errors: MaxioAdvancedBilling.Errors; RequestOptions: MaxioAdvancedBilling.Core; CancellationToken: System.Threading. All rows use `RequestOptions? requestOptions = null, CancellationToken cancellationToken = default` as their final parameters, and return Task<T> of the stated envelope.

| Controller | Method signature | Request record and members used | Body fields | Response fields read | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| ProductFamilies | ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | ProductFamilyId:string required, `handle:` prefix explicitly supported; Page:int default 1; PerPage:int default 20 max 200; IncludeArchived:bool? optional false. Omit date/filter/include inputs. | none | IReadOnlyList<ProductResponse>; Product required (product); Product: Id(id):int?, Handle(handle):string?, Name(name):string?, Description(description):string?, PriceInCents(price_in_cents):long?, Interval(interval):int?, IntervalUnit(interval_unit):Enums.IntervalUnit?, ArchivedAt(archived_at):DateTimeOffset?, RequireCreditCard(require_credit_card):bool?, RequireBillingAddress(require_billing_address):bool?, RequireShippingAddress(require_shipping_address):bool?, ProductFamily(product_family):ProductFamily? with Handle(handle):string?, Id(id):int?. | A ListProductsForProductFamilyError.TryGetString(out string) 404; TryGetRawError(out Core.ErrorResponse.RawError) fallback | Map defaults say no Pageable; explicit Page/PerPage member docs support manual pages until empty. | map/operations/ProductFamilies.md; src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs; Models/ProductResponse.cs; Models/Product.cs; Models/ProductFamily.cs |
| Customers | ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Reference:string required | none | CustomerResponse.Customer(customer):Customer required; Id(id):int?, Reference(reference):string? | B ApiException<Core.ErrorResponse.RawError>; StatusCode, ReadAsBytes(), ReadAsString(), ReadAsJson<T>() | none | map/operations/Customers.md; Requests/Customers/ReadCustomerByReferenceRequest.cs; Models/CustomerResponse.cs; Models/Customer.cs |
| Customers | CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Body:CreateCustomerRequest? optional in record, supplied by application; no caller idempotency member | CreateCustomerRequest.Customer(customer):CreateCustomer required; CreateCustomer.FirstName(first_name):string required; LastName(last_name):string required; Email(email):string required; Reference(reference):string? supplied. Omit address, tax, locale, organization and remaining optional fields. Provider prose guarantees customer reference unique. | CustomerResponse.Customer required, Id:int?, Reference:string? | A CreateCustomerError.TryGetCustomerErrorResponse1(out CustomerErrorResponse1) 422; TryGetRawError fallback. CustomerErrorResponse1.Errors(errors):Models.AnyOf.Errors1?; TryGetCustomerError and TryGetListOfString. Do not echo provider bodies. | none | map/operations/Customers.md; Requests/Customers/CreateCustomerOperationRequest.cs; Models/CreateCustomerRequest.cs; Models/CreateCustomer.cs; Models/CustomerErrorResponse1.cs; Models/AnyOf/Errors1.cs; Api/Customers.cs |
| Subscriptions | FindSubscription(FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Reference:string? supplied | none | SubscriptionResponse.Subscription(subscription):Subscription?; fields below | A FindSubscriptionError.TryGetNoContent(out Core.ErrorResponse.RawError) 404; TryGetRawError fallback | none | map/operations/Subscriptions.md; Requests/Subscriptions/FindSubscriptionRequest.cs; Models/SubscriptionResponse.cs; Models/Subscription.cs; Api/Subscriptions.cs |
| Subscriptions | CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Body:CreateSubscriptionRequest? optional in record, supplied; no caller idempotency member | CreateSubscriptionRequest.Subscription(subscription):CreateSubscription required; ProductHandle(product_handle):string? semantically required unless ProductId given; CustomerId(customer_id):int? semantically required unless customer_reference/customer_attributes given; Reference(reference):string? supplied; PaymentCollectionMethod(payment_collection_method):MaxioAdvancedBilling.Models.Enums.CollectionMethod? supplied as Remittance for paymentless enrollment. Omit ProductId, customer_reference/customer_attributes, price overrides, coupon fields, payment profiles, components and dates. DeferSignup defaults false. | SubscriptionResponse.Subscription nullable; Subscription.Id(id):int?, Reference(reference):string?, Customer(customer):Customer?, Product(product):Product?, ProductPriceInCents(product_price_in_cents):long?, State(state):Enums.SubscriptionState?, NextAssessmentAt(next_assessment_at):DateTimeOffset?, CurrentPeriodEndsAt(current_period_ends_at):DateTimeOffset?, Currency(currency):string?. All fields optional; validate identity/missing essential fields defensively. | A CreateSubscriptionError.TryGetErrorListResponse1(out ErrorListResponse1) 422; TryGetRawError fallback; ErrorListResponse1.Errors(errors):IReadOnlyList<string> required | none | map/operations/Subscriptions.md; Requests/Subscriptions/CreateSubscriptionOperationRequest.cs; Models/CreateSubscriptionRequest.cs; Models/CreateSubscription.cs; Models/SubscriptionResponse.cs; Models/Subscription.cs; Models/ErrorListResponse1.cs; Api/Subscriptions.cs |
| Subscriptions | ListSubscriptions(ListSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | CustomerId:int? supplied; Page:int default 1; PerPage:int default 20 max 200; Sort:Enums.SubscriptionSort default SignupDate. Other query inputs omitted. Product filter does NOT accept handle. | none | IReadOnlyList<SubscriptionResponse>; fields above; filter/check customer ownership and configured family before response | B ApiException<Core.ErrorResponse.RawError> | Map defaults no Pageable; request docs support manual pages until empty. | map/operations/Subscriptions.md; Requests/Subscriptions/ListSubscriptionsRequest.cs; Models/Subscription.cs |

### CROSS-OPERATION INVARIANTS
| Invariant | Operations | Enforced where |
| --- | --- | --- |
| Submitted product handle is in current complete offered recurring nonarchived plan set for configured family, with payment/profile requirements compatible with this endpoint. | CreateSubscription ← ListProductsForProductFamily | Subscription service enrollment before claim/create; same eligibility predicate as GET plans; reject incomplete catalog rather than validate against partial set. |
| Provider customer ID comes exclusively from authenticated user's deterministic reference lookup/create result. | CreateSubscription ← ReadCustomerByReference / CreateCustomer | Subscription service enrollment validates returned customer reference and ID before create. |

### Enum values needed
| Fully qualified type | Values | Source |
| --- | --- | --- |
| MaxioAdvancedBilling.Models.Enums.CollectionMethod | Automatic="automatic", Remittance="remittance", Prepaid="prepaid", Invoice="invoice". Request member docs list remittance/automatic/prepaid for current Relationship Invoicing and invoice/automatic for legacy Statements. Application chooses explicit Remittance; live sandbox verification succeeded. | Models/Enums/CollectionMethod.cs; Models/CreateSubscription.cs |
| MaxioAdvancedBilling.Models.Enums.IntervalUnit | Day="day", Month="month"; unknown values preserved, do not infer monthly on unknown. | Models/Enums/IntervalUnit.cs |
| MaxioAdvancedBilling.Models.Enums.SubscriptionState | Pending=pending, FailedToCreate=failed_to_create, Trialing=trialing, Assessing=assessing, Active=active, SoftFailure=soft_failure, PastDue=past_due, Suspended=suspended, Canceled=canceled, Expired=expired, Paused=paused, Unpaid=unpaid, TrialEnded=trial_ended, OnHold=on_hold, AwaitingSignup=awaiting_signup; expose Value including unknown values. | Models/Enums/SubscriptionState.cs |

### Client construction/auth/server nodes
| Fact/decision | Source |
| --- | --- |
| MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options); BasicAuth = Core.Authentication.Basic.BasicAuthCredentials { Username=API key, Password="x" }. | sdk-map.md |
| ServerEnvironment.Us default US and Eu EU; Production US https://{site}.chargify.com, EU https://{site}.ebilling.maxio.com; Ebb both https://events.chargify.com/{site}. Production.<Us/Eu>.Site selects subdomain, BaseUrl overrides literal configured string. This scope uses Production only. No sandbox enum exists. | sdk-map.md |
| Settings exactly Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle and optional Maxio:BaseUrl. Map supplied environment aliases into these keys; MAXIO_ENVIRONMENT selects Us/Eu, default Us, reject unknown values. Preserve optional BaseUrl verbatim, validate absolute URL without rewriting. | YOUR CALL — not in the map |

## Trap notes
- Step 1: Auth scheme skipping and credential lifetime can defer startup errors or retain rotated secrets. **MUST load maxio-advanced-billing:dotnet-authentication**
- Step 1: HttpClient ownership and client registration can exhaust sockets or retain stale configuration. **MUST load maxio-advanced-billing:dotnet-client-initialization**
- Steps 1–5: What Timeout actually bounds, retry verb gating, server-node selection and explicit paging can cause duplicate writes or incomplete reads. **MUST load maxio-advanced-billing:dotnet-configuration-resilience**
- Steps 3–5: Request records, return envelopes and cancellation arguments can be misapplied. **MUST load maxio-advanced-billing:dotnet-calling-endpoints**
- Steps 3–5: Optional response members, open enums and union error shapes can hide malformed provider data. **MUST load maxio-advanced-billing:dotnet-models**
- Steps 4–6: Exception types and malformed success/error bodies can escape narrow catch ladders. **MUST load maxio-advanced-billing:dotnet-error-handling**
- Step 6: Incorrect SDK stubs bypass real serialization, retry and error behavior. **MUST load maxio-advanced-billing:dotnet-testing**

## REQUIRED READING
- maxio-advanced-billing:dotnet-getting-started — all SDK contracts
- maxio-advanced-billing:dotnet-client-initialization — step 1
- maxio-advanced-billing:dotnet-authentication — step 1
- maxio-advanced-billing:dotnet-configuration-resilience — steps 1–6
- maxio-advanced-billing:dotnet-calling-endpoints — steps 3–5
- maxio-advanced-billing:dotnet-models — steps 3–5
- maxio-advanced-billing:dotnet-error-handling — steps 4–6
- maxio-advanced-billing:dotnet-testing — step 6

a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## PRODUCTION READINESS
All application decisions in this section are YOUR CALL — not in the map.
| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | PublicApi registration validates nonblank ApiKey, Subdomain, ProductFamilyHandle; key username and fixed password both present; fail host startup before first call. |
| 2 | Secret sourcing & rotation | User-secrets/environment supply key, never tracked. Construct singleton options once at registration; restart process after rotation. |
| 3 | Total timeout budget | Main workflow has a shared 30-second budget; up to two independent 10-second reconciliation windows yield at most 50 seconds for entire enrollment. Worker gets 30 seconds. GET retries remain within these budgets; POST never retried. Cancellation after dispatch persists unknown state. |
| 4 | Write-retry ownership | CreateCustomer and CreateSubscription are POST; keep them outside retried verbs, never blindly replay after unknown result. GET may retry once within total budget. |
| 5 | Idempotency & ambiguous writes | Neither create request record has a real caller key. Deterministic provider references plus pre-call EF PK claims; read by reference in failing catch, persist unknown and worker reconciliation if unavailable. Random injected header is not a key. |
| 6 | Observability | Information: operation/outcome/duration/internal correlation; Warning: ambiguity/rate limiting; Error: unexpected integration fault. Named error bodies have no declared provider correlation field; do not invent one or log bodies. May log safe provider request-id header only when actually present through hook. |
| 7 | Sensitive data | Customer names/email/reference are sensitive. Explicit ILoggerFactory, LogRequestBody=false; header logging disabled; redact authorization and supplied sensitive form keys first_name,last_name,email,reference,customer,subscription. Never return raw error payloads. |
| 8 | Environment selection | Scope Production only; Us/Eu configured before constructing client; Ebb unused. Supplied sandbox is isolated by configured subdomain; production deployment must supply its own site and secrets. Never bake the configured sandbox site or family/product handles into code. Optional BaseUrl is verbatim override on selected Production node. |
| 9 | Duplicate prevention | See DUPLICATE CLAIMS. DB primary keys enforce exclusion before provider calls, including EF InMemory. SQL deployment persists claims; InMemory dev only with restart-loss caveat. Reserved claims orphaned before dispatch may be released after two minutes; Dispatching and Unknown claims are never released merely because of age/lease expiry. |
| 10 | Partial results | Max 100 pages × 200 records, deadline may end earlier. Envelope IsPartial=true and continuation page when capped; enroll fails closed if complete offered set unavailable. All subscribers remain provider-owned. |
| 11 | Unknown outcomes | Own catch attempts read by reference with independent bounded token; success records result. Unresolved claims become Unknown and return accepted/pending, worker reconciles later without resending create. No automatic resend after repeated 404 because original operation may still complete. |

### DUPLICATE CLAIMS
| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| CreateCustomer | CatalogContext claim entity PK derived from billing target + region + stable ApplicationUser.Id; store reference and resulting customer ID | Insert/save before call; relational PK refusal or InMemory duplicate-key exception; do not rely on unique index or read-before-write | Claim store catches DbUpdateException with confirmed PK conflict, and InMemory duplicate ArgumentException; detach failed entity then read existing claim; unrelated errors rethrow | BillingClaimStore.TryClaimAsync (called by SubscriptionBilling.EnsureCustomerAsync) → MaxioGateway.CreateCustomerAsync |
| CreateSubscription | CatalogContext claim entity PK derived from billing target + region + family + stable user ID + product handle; provider reference deterministic; claim persisted before dispatch | Same DB primary-key refusal, before SDK POST | Claim store same provider-specific duplicate handling; pending/unknown returns accepted, completed reads provider result | BillingClaimStore.TryClaimAsync (called by SubscriptionBilling.SubscribeAsync) → MaxioGateway.CreateSubscriptionAsync |

### PAGED READS
| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| ListProductsForProductFamily | 100 pages, PerPage 200, 30-second budget; walk until empty | IsPartial=true plus next page when capped; enrollment refuses partial catalog | SubscriptionBilling.GetPlansCoreAsync |
| ListSubscriptions | 100 pages, PerPage 200, 30-second budget; walk until empty | IsPartial=true plus next page when capped; provider error returns error rather than disguised empty list | SubscriptionBilling.GetMineAsync |

### UNKNOWN OUTCOMES
| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| CreateCustomer | ReadCustomerByReference | deterministic billing-target-and-region-scoped stable user reference persisted in claim before POST | SubscriptionBilling.EnsureCustomerAsync catch | SubscriptionBillingTests.LostCustomerResponseReconcilesWithoutAnotherPost |
| CreateSubscription | FindSubscription | deterministic billing-target/region/family/user/product reference persisted in claim before POST; validate returned customer and product | SubscriptionBilling.SubscribeAsync catch → SettleSubscriptionAsync / ReconcileAsync | SubscriptionBillingTests.LostSubscriptionResponseReconcilesWithoutAnotherPost; UnknownWriteRemainsPendingAndWorkerSettlesItWithoutResending |

## Repository conventions
| Pattern | Exemplar |
| --- | --- |
| Endpoint route class and JWT auth scheme | src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs |
| Service registration | src/PublicApi/Program.cs |
| Existing record storage | src/Infrastructure/Data/CatalogContext.cs |
| SQL/InMemory selection | src/Infrastructure/Dependencies.cs |
| Trusted token Name claim source | src/Infrastructure/Identity/IdentityTokenClaimService.cs |
| Identity model has no first/last names | src/Infrastructure/Identity/ApplicationUser.cs |

## Assumptions & Blockers
- No SDK capability blocker found for requested product subscriptions. Seeded component is context only; usage reporting and component enrollment are outside these three endpoints.
- JWT currently issues ClaimTypes.Name. Resolve ApplicationUser by authenticated name and derive provider references from immutable ApplicationUser.Id; never accept email/customer ID/user ID from caller. Required provider first/last names use documented application fallback derived from trusted username because ApplicationUser has no such fields; require trusted nonblank Email rather than inventing an email.
- Treat one subscription per user and offered product as the double-click key for this additive feature. Repeated enrollment returns the existing provider subscription; cancellation/resubscription policy is outside scope.
- Live sandbox verification: omitting collection method returned typed ErrorListResponse1.Errors containing "No payment method was on file for the $299.00 balance" despite RequireCreditCard=false. Application chooses explicit PaymentCollectionMethod=Remittance, grounded in Models/CreateSubscription.cs and Models/Enums/CollectionMethod.cs; live SDK and authenticated PublicApi enrollment both succeeded with active state, configured price and next billing date.
- UNVERIFIED: provider visibility after ambiguous create is immediate. Preserve unknown claims and retry reads in worker; never infer a safe resend from one absent lookup.
- Build references the plugin SDK in place through the overridable MaxioSdkProject MSBuild property; teammates/CI need the plugin installed and the property pointed at their copy. SDK source must not be copied into repository.
- InMemory claims survive concurrent callers but not process restart and cannot coordinate separate processes; use persistent SQL storage in production.


