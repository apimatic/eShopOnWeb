# Maxio subscription integration plan

## 1. Scope & sequence
| Step | Work / operations | Repository exemplar |
| --- | --- | --- |
| 1 | Bind Maxio section, fail fast, register plugin SDK singleton and named HTTP client | src/PublicApi/Program.cs |
| 2 | Add durable primary-key billing claims and SQL migration | src/Infrastructure/Data/CatalogContext.cs |
| 3 | ListProducts; filter configured family and archived/payment-required plans; validate offered handle before writes | src/PublicApi/CatalogTypeEndpoints/CatalogTypeListEndpoint.cs |
| 4 | Claim customer, ReadCustomerByReference, CreateCustomer; claim user/plan, FindSubscription, CreateSubscription | src/Infrastructure/Identity/ApplicationUser.cs |
| 5 | ListCustomerSubscriptions for token owner; reconcile unknown claims in background | src/PublicApi/Program.cs |
| 6 | HTTP-seam tests, build, sandbox HTTP verification, operator guide | tests/PublicApiIntegrationTests/ProgramTest.cs |

## 2. CONTRACT SHEET
Signatures are generated code, verbatim: each operation taking inputs takes ONE request record first, built with an object initializer using that record's property names; operations without inputs take none.
Every SDK type is fully qualified with the namespace implied by THAT type's map-named source path, never a neighbouring type's namespace.

All signatures below end in `(request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` and return Task of the listed result. SDK source root is plugin-relative `sdk/dotnet/`.

| Controller.method | Request record and used members | Body (wire names) | Response and read fields | Errors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| Sites.ReadSite | no request; RequestOptions? and CancellationToken | none | Models.SiteResponse.Site required; Models.Site Currency (currency) string?, Test (test) bool?, RelationshipInvoicingEnabled (relationship_invoicing_enabled) bool? | B RawError | none | map/operations/Sites.md; Models/SiteResponse.cs, Site.cs; Api/Sites.cs |
| Products.ListProducts | Requests.Products.ListProductsRequest: Page int default 1, PerPage int default 20 max 200, IncludeArchived bool? | none | IReadOnlyList<Models.ProductResponse>; required Product (product); Product: Id int?, Handle string?, Name string?, Description string?, PriceInCents long?, Interval int?, IntervalUnit enum?, ArchivedAt DateTimeOffset?, RequireCreditCard bool?, ProductFamily ProductFamily?; family Handle string?, ArchivedAt DateTimeOffset? | B RawError | explicit page/per_page in request; map has no pagination abstraction | map/operations/Products.md; src/MaxioAdvancedBilling/Requests/Products/ListProductsRequest.cs; Models/ProductResponse.cs, Product.cs, ProductFamily.cs |
| Customers.ReadCustomerByReference | Requests.Customers.ReadCustomerByReferenceRequest: required Reference string | none | Models.CustomerResponse: required Customer (customer); Customer Id int?, Reference string? | B RawError; 404 means absent | none | map/operations/Customers.md; Requests/Customers/ReadCustomerByReferenceRequest.cs; Models/CustomerResponse.cs, Customer.cs |
| Customers.CreateCustomer | Requests.Customers.CreateCustomerOperationRequest: Body Models.CreateCustomerRequest? | required Customer (customer): Models.CreateCustomer; required FirstName (first_name), LastName (last_name), Email (email) string; Reference (reference) string? | Models.CustomerResponse.Customer.Id | A Errors.CreateCustomerError: TryGetCustomerErrorResponse1(out Models.CustomerErrorResponse1) 422, TryGetRawError fallback; Errors nullable union, never log PII | none | map/operations/Customers.md; Requests/Customers/CreateCustomerOperationRequest.cs; Models/CreateCustomerRequest.cs, CreateCustomer.cs; Api/Customers.cs |
| Subscriptions.FindSubscription | Requests.Subscriptions.FindSubscriptionRequest: Reference string? (set) | none | Models.SubscriptionResponse.Subscription (subscription) nullable | A Errors.FindSubscriptionError: TryGetNoContent(out RawError) 404; TryGetRawError fallback | none | map/operations/Subscriptions.md; Requests/Subscriptions/FindSubscriptionRequest.cs; Models/SubscriptionResponse.cs |
| Subscriptions.CreateSubscription | Requests.Subscriptions.CreateSubscriptionOperationRequest: Body Models.CreateSubscriptionRequest? | required Subscription (subscription): Models.CreateSubscription; ProductHandle (product_handle) string?, CustomerId (customer_id) int?, Reference (reference) string? all set; product_id, customer_attributes/customer_reference alternatives omitted; payment, offers, coupons, import dates omitted | Models.SubscriptionResponse.Subscription: Id int?, Customer Customer?, Product Product?, ProductPriceInCents (product_price_in_cents) long?, State enum?, NextAssessmentAt (next_assessment_at) DateTimeOffset?, Currency string?, Reference string? | A Errors.CreateSubscriptionError: TryGetErrorListResponse1(out Models.ErrorListResponse1) 422 (required Errors string list), TryGetRawError fallback | none | map/operations/Subscriptions.md; Requests/Subscriptions/CreateSubscriptionOperationRequest.cs; Models/CreateSubscriptionRequest.cs, CreateSubscription.cs, Subscription.cs; Api/Subscriptions.cs |
| Customers.ListCustomerSubscriptions | Requests.Customers.ListCustomerSubscriptionsRequest: required CustomerId int | none | IReadOnlyList<Models.SubscriptionResponse> | B RawError | none per map | map/operations/Customers.md; Requests/Customers/ListCustomerSubscriptionsRequest.cs |

### CROSS-OPERATION INVARIANTS
| Invariant | Operations | Enforced where |
| --- | --- | --- |
| Posted product handle belongs to complete offered set in configured family | CreateSubscription ← ListProducts | SubscriptionService.SubscribeAsync before claiming or writing |
| CustomerId is resolved from token owner's deterministic reference | CreateSubscription ← ReadCustomerByReference / CreateCustomer | SubscriptionService.EnsureCustomerAsync |

| Enum / construction | Facts | Source |
| --- | --- | --- |
| Models.Enums.IntervalUnit | Day=day, Month=month; preserve unknown Value | Models/Enums/IntervalUnit.cs |
| Models.Enums.CollectionMethod | Automatic=automatic, Remittance=remittance, Prepaid=prepaid, Invoice=invoice. CreateSubscription.PaymentCollectionMethod (payment_collection_method) CollectionMethod?: current Relationship Invoicing accepts remittance/automatic/prepaid; legacy accepts invoice/automatic. Use remittance or invoice according to ReadSite.RelationshipInvoicingEnabled to permit enrollment without payment capture | Models/Enums/CollectionMethod.cs; Models/CreateSubscription.cs |
| Models.Enums.SubscriptionState | pending, failed_to_create, trialing, assessing, active, soft_failure, past_due, suspended, canceled, expired, paused, unpaid, trial_ended, on_hold, awaiting_signup; expose Value | Models/Enums/SubscriptionState.cs |
| Client | MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions); BasicAuth Core.Authentication.Basic.BasicAuthCredentials Username=ApiKey, Password=x | sdk-map.md |
| Servers | Servers.ServerEnvironment.Us default and Eu; Production US https://{site}.chargify.com, EU https://{site}.ebilling.maxio.com; Ebb both https://events.chargify.com/{site}. Scope uses Production only; set Production.Us.Site or BaseUrl | sdk-map.md |
| Reference uniqueness | Do not depend on provider rejecting duplicate references; claims provide exclusion and lookups reconcile | UNVERIFIED |
| Product availability | Fail closed when required display data is absent; no stale numeric IDs; derive legal set from live catalog | YOUR CALL — not in the map |

## 3. Trap notes
- Step 1: credential application can silently skip missing credentials. MUST load maxio-advanced-billing:dotnet-authentication
- Step 1: singleton HTTP lifetime and DNS affect availability. MUST load maxio-advanced-billing:dotnet-client-initialization
- Steps 1–5: retry eligibility, timeout scope and ambiguous writes affect duplicate safety. MUST load maxio-advanced-billing:dotnet-configuration-resilience
- Steps 3–5: request records, envelopes and cancellation positions affect correctness. MUST load maxio-advanced-billing:dotnet-calling-endpoints
- Steps 3–5: nullable fields and open enum wire values affect mapping. MUST load maxio-advanced-billing:dotnet-models
- Steps 4–6: typed and malformed errors require complete boundaries. MUST load maxio-advanced-billing:dotnet-error-handling
- Step 6: capture disposed request bodies inside stub handler. MUST load maxio-advanced-billing:dotnet-testing

## 4. REQUIRED READING
- maxio-advanced-billing:dotnet-client-initialization · step 1
- maxio-advanced-billing:dotnet-authentication · step 1
- maxio-advanced-billing:dotnet-configuration-resilience · steps 1–5
- maxio-advanced-billing:dotnet-calling-endpoints · steps 3–5
- maxio-advanced-billing:dotnet-models · steps 3–5
- maxio-advanced-billing:dotnet-error-handling · steps 4–6
- maxio-advanced-billing:dotnet-testing · step 6

a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS
| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind exact Maxio keys; reject blank ApiKey, Subdomain, ProductFamilyHandle at startup; validate optional BaseUrl absolute HTTPS (loopback HTTP for tests) |
| 2 | Secrets and rotation | Load supplied environment values into PublicApi user-secrets outside repository; section binding; options captured once in singleton, restart for rotation |
| 3 | Total timeout | 45-second total API service budget; 10-second per SDK attempt; independent 15-second reconciliation budget following ambiguous writes; total write path at most 60 seconds plus local DB operations |
| 4 | Retry ownership | GET only, one retry; POST never retried |
| 5 | Idempotency | Neither create record has real idempotency member. Deterministic customer and user/plan references; EF primary-key claim precedes writes; reconcile ambiguous result |
| 6 | Observability | Information operation/status/trace ID; warning provider status and response request-id header if present, no raw error/body/identity logging; no documented correlation member in selected typed error bodies |
| 7 | Sensitive data | Name/email requests: LogRequestBody false, explicit LoggerFactory, header logging false; add first_name,last_name,email,reference to RedactedKeys |
| 8 | Environment | SDK declares US/EU regions, no sandbox enum. Sandbox is selected through supplied site configuration; use US Production group for sandbox, BaseUrl override verbatim; Ebb unused. ReadSite.Test must be true in Development, also supplies catalog currency; no assumption that supplied MAXIO_ENVIRONMENT labels a sandbox rather than a region |
| 9 | Concurrency | See DUPLICATE CLAIMS; SQL primary key durable, in-memory primary key for local demo only, no process lock relied upon |
| 10 | Partial results | Catalog cap 50 pages × 200; return 503 rather than partial offered catalog |
| 11 | Unknown outcomes | Re-read in create catch; persist unsettled claim, periodic background re-read; never automatically resend an ambiguous write. Claims with no provider match require operator settlement before release, preventing delayed provider commits from causing duplicates |

### DUPLICATE CLAIMS
| Write | Where claim stored | What rejects second | Where caught | Where in code |
| --- | --- | --- | --- | --- |
| CreateCustomer | CatalogContext BillingClaims, hashed site/user primary key | SQL PK / in-memory duplicate key | BillingClaimStore.TryClaimAsync catches DbUpdateException / ArgumentException | BillingClaimStore.TryClaimAsync → SubscriptionService.EnsureCustomerAsync |
| CreateSubscription | CatalogContext BillingClaims, hashed site/user/plan primary key | SQL PK / in-memory duplicate key | BillingClaimStore.TryClaimAsync catches DbUpdateException / ArgumentException | BillingClaimStore.TryClaimAsync → SubscriptionService.SubscribeAsync |

### PAGED READS
| Read | Cap | Caller notification | Where in code |
| --- | --- | --- | --- |
| ListProducts | 50 pages of 200 | 503 ProblemDetails, never partial list | SubscriptionService.PlansAsync throws BillingUnavailableException; SubscriptionEndpoints.ExecuteAsync sets 503 |

### UNKNOWN OUTCOMES
| Write | Re-read operation | Reference | Where in code | Connection-failure test |
| --- | --- | --- | --- | --- |
| CreateCustomer | ReadCustomerByReference | deterministic site/user hash | EnsureCustomerAsync catch → RecoverCustomerAsync; ReconcilePendingAsync sweeps unsettled rows | LostCustomerResponseIsReconciledWithoutResending |
| CreateSubscription | FindSubscription | deterministic site/user/plan hash | SubscribeAsync catch → RecoverSubscriptionAsync; ReconcilePendingAsync sweeps unsettled rows | LostSubscriptionResponseIsReconciledWithoutResending; UnknownOutcomeStaysPendingAndBackgroundReconciliationSettlesIt |

## 6. Assumptions & Blockers
No plugin capability gap identified. API-only scope per task. One subscription per user per plan; repeat returns existing subscription including canceled states. Identity has no personal-name fields: use 'eShop' / 'Shopper' as required customer names. Production uses existing SQL Server and the added migration; local in-memory durability is intentionally limited to the process as requested. No automatic retry of unresolved ambiguous writes. Live verification found automatic collection rejects a nonzero opening balance without a payment profile despite the product's optional-card setting. Select the SDK-documented remittance/invoice collection mode from site architecture, preserving full price and normal renewal dates; payment settlement remains in Maxio.

## 7. Source labels
SDK paths above are relative to plugin SDK root, with Models/Requests/Api shorthand under src/MaxioAdvancedBilling. Application architecture and readiness decisions are YOUR CALL — not in the map. Provider reference uniqueness remains UNVERIFIED and is not a correctness dependency.

## Verification
- PublicApi built with the plugin SDK project in place.
- Solution regressions: 90 passed, including 16 subscription cases; existing catalog, auth, order/basket, functional and unit tests passed.
- Sandbox HTTP: plans returned expected monthly cents/currency; Pro subscribe 201 active, repeat 200 same provider ID, account read current provider record; anonymous read 401.
- scripts/Verify-Subscriptions.ps1 passed the same sandbox flow and stopped its detached host; no verification port listener remained.
- SQL Server runtime unavailable locally; migration SQL generation verifies the new table/primary key without a connection. SQL production runtime remains an operator deployment check.
- No credentials in repository source; local user-secrets initialized directly through a pipe. The SDK source stays external and read-only.
