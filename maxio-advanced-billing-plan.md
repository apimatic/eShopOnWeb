# Maxio subscription integration plan

## Scope & sequence

| Step | Implementation | Operations | Source |
| --- | --- | --- | --- |
| 1 | Bind and validate Maxio section, singleton SDK and safe logging; reference SDK in plugin without copying it | client construction | sdk/dotnet/sdk-map.md |
| 2 | List configured family's available plans, using family handle directly; reject unavailable/archived plans before subscribe | ProductFamilies.ListProductsForProductFamily | sdk/dotnet/map/operations/ProductFamilies.md |
| 3 | Resolve authenticated user through UserManager, atomically claim customer, lookup stable user/site reference, create only when absent, persist ID | Customers.ReadCustomerByReference, Customers.CreateCustomer | sdk/dotnet/map/operations/Customers.md |
| 4 | Atomically claim user/site/product, lookup deterministic subscription reference, create once, return provider state/price/date | Subscriptions.FindSubscription, Subscriptions.CreateSubscription | sdk/dotnet/map/operations/Subscriptions.md |
| 5 | Account list reads provider; reconcile persisted pending/unknown claims without repeating writes; add migration, tests, live verification and guide | Customers.ListCustomerSubscriptions, both reference lookups | YOUR CALL — not in the map |

## CONTRACT SHEET

Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.
Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

All source references below are relative to the plugin root. SDK root is `sdk/dotnet/`; records are `MaxioAdvancedBilling.Models`, request records are `MaxioAdvancedBilling.Requests.<Controller>`, enums `MaxioAdvancedBilling.Models.Enums`, errors `MaxioAdvancedBilling.Errors`; core request options `MaxioAdvancedBilling.Core.RequestOptions`. All methods return Task of the response shown. All optional trailing arguments have the exact signature `RequestOptions? requestOptions = null, CancellationToken cancellationToken = default`.

| Controller | Method signature (trailing arguments as above) | Request record members | Body fields | Response envelope/read fields | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| ProductFamilies | ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | ProductFamilyId: required string, supports `handle:` prefix; Page: int=1; PerPage: int=20; IncludeArchived: bool?; other unused optional DateField, Filter, dates, Include | none | IReadOnlyList<ProductResponse>; required Product (product): Product; fields below | A ApiException<ListProductsForProductFamilyError>; TryGetString(out string) 404, TryGetRawError(out RawError) fallback | Plain list, manually drive Page/PerPage | sdk/dotnet/map/operations/ProductFamilies.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs; Models/ProductResponse.cs; Models/Product.cs |
| Customers | ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Reference: required string | none | CustomerResponse.Customer (customer): required Customer; Id (id): int?, Reference (reference): string? | B ApiException<RawError> | none | sdk/dotnet/map/operations/Customers.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Customers/ReadCustomerByReferenceRequest.cs; Models/CustomerResponse.cs; Models/Customer.cs |
| Customers | CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Body: CreateCustomerRequest?; no caller idempotency key | CreateCustomerRequest.Customer (customer): required CreateCustomer; FirstName (first_name), LastName (last_name), Email (email): required string; Reference (reference): string? | CustomerResponse.Customer required; Id int?, Reference string? | A ApiException<CreateCustomerError>; TryGetCustomerErrorResponse1(out CustomerErrorResponse1) 422, TryGetRawError(out RawError) fallback; CustomerErrorResponse1.Errors (errors): Models.AnyOf.Errors1? supports TryGetCustomerError and TryGetListOfString | none | sdk/dotnet/map/operations/Customers.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Customers/CreateCustomerOperationRequest.cs; Models/CreateCustomerRequest.cs; Models/CreateCustomer.cs; Models/CustomerErrorResponse1.cs; Models/AnyOf/Errors1.cs; Api/Customers.cs |
| Subscriptions | FindSubscription(FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Reference: string? (application always supplies it) | none | SubscriptionResponse.Subscription (subscription): Subscription?; fields below | A ApiException<FindSubscriptionError>; TryGetNoContent(out RawError) 404, TryGetRawError(out RawError) fallback | none | sdk/dotnet/map/operations/Subscriptions.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Subscriptions/FindSubscriptionRequest.cs; Models/SubscriptionResponse.cs; Models/Subscription.cs |
| Subscriptions | CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | Body: CreateSubscriptionRequest?; no caller idempotency key | CreateSubscriptionRequest.Subscription (subscription): required CreateSubscription; ProductHandle (product_handle): string?, CustomerId (customer_id): int?, Reference (reference): string?; must supply product handle or product ID and customer ID/reference/attributes despite nullable C# fields | SubscriptionResponse.Subscription nullable; fields below | A ApiException<CreateSubscriptionError>; TryGetErrorListResponse1(out ErrorListResponse1) 422, TryGetRawError(out RawError) fallback; ErrorListResponse1.Errors (errors): required IReadOnlyList<string> | none | sdk/dotnet/map/operations/Subscriptions.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Subscriptions/CreateSubscriptionOperationRequest.cs; Models/CreateSubscriptionRequest.cs; Models/CreateSubscription.cs; Models/ErrorListResponse1.cs; Api/Subscriptions.cs |
| Customers | ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | CustomerId: required int | none | IReadOnlyList<SubscriptionResponse>; each Subscription nullable | B ApiException<RawError> | none; source says all subscriptions belonging to customer | sdk/dotnet/map/operations/Customers.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Customers/ListCustomerSubscriptionsRequest.cs; Api/Customers.cs |
| Sites | ReadSite(RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | none | none | SiteResponse.Site (site): required Site; Currency (currency): string?; RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?; Test (test): bool? | B ApiException<RawError> | none | sdk/dotnet/map/operations/Sites.md; sdk/dotnet/src/MaxioAdvancedBilling/Models/SiteResponse.cs; Models/Site.cs |

### Read model fields

| Type | Fields read (wire names), all optional | Source |
| --- | --- | --- |
| MaxioAdvancedBilling.Models.Product | Id (id): int?; Handle (handle), Name (name), Description (description): string?; PriceInCents (price_in_cents): long?; Interval (interval): int?; IntervalUnit (interval_unit): IntervalUnit?; ArchivedAt (archived_at): DateTimeOffset?; RequireCreditCard (require_credit_card): bool?; ProductFamily (product_family): ProductFamily? | sdk/dotnet/src/MaxioAdvancedBilling/Models/Product.cs |
| MaxioAdvancedBilling.Models.ProductFamily | Id (id): int?; Handle (handle), Name (name): string? | sdk/dotnet/src/MaxioAdvancedBilling/Models/ProductFamily.cs |
| MaxioAdvancedBilling.Models.Subscription | Id (id): int?; Reference (reference): string?; State (state): SubscriptionState?; Product (product): Product?; Customer (customer): Customer?; ProductPriceInCents (product_price_in_cents), CurrentBillingAmountInCents (current_billing_amount_in_cents): long?; CurrentPeriodEndsAt (current_period_ends_at), NextAssessmentAt (next_assessment_at): DateTimeOffset?; Currency (currency): string? | sdk/dotnet/src/MaxioAdvancedBilling/Models/Subscription.cs |

Subscription model has no NextBillingAt field; expose next billing from NextAssessmentAt, falling back to CurrentPeriodEndsAt and preserving null when neither exists. Never invent a date. Subscription product may be null per SDK remarks; validate before presenting plan details. Product has no currency field: plan currency cannot be inferred as USD from prices alone; omit/mark unknown unless obtained from additional supported source. CreateSubscription optional fields deliberately omitted: alternative ProductId, CustomerReference/CustomerAttributes, price points, payment profile/card/bank fields, custom prices, dates, components, discounts, tax and migration controls; use seeded product defaults. No request model has a stable header key; generated Idempotency-Key is not an application deduplication mechanism.

Live verification correction: the sandbox rejected automatic collection with `No payment method was on file for the $299.00 balance`. `CreateSubscription.PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?` is supported by Models/CreateSubscription.cs. Its documented values are Automatic=`automatic`, Remittance=`remittance`, Prepaid=`prepaid`, Invoice=`invoice` (Models/Enums/CollectionMethod.cs). Use ReadSite.RelationshipInvoicingEnabled to choose Remittance for current invoicing and Invoice for legacy invoicing. This is explicit invoice collection for the authorized no-card flow; it does not mark an invoice paid or alter plan pricing. ReadSite supplies plan currency and verifies Test=true when running in Development. Unknown site architecture/currency fails before writes. Sources: sdk/dotnet/src/MaxioAdvancedBilling/Models/CreateSubscription.cs; Models/Enums/CollectionMethod.cs; Models/Site.cs. Application collection choice: YOUR CALL — not in the map.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| Submitted product handle must be present in configured family's currently offered nonarchived plan set | CreateSubscription ← ListProductsForProductFamily | Subscription application service validates against the same complete catalog read used by GET plans before customer/subscription writes |
| CustomerId used to create subscription must belong to the JWT-resolved immutable user reference | CreateSubscription ← ReadCustomerByReference or CreateCustomer | Customer ownership/reference validation in application service before CreateSubscription |

### Enums

| Type | Known values | Source |
| --- | --- | --- |
| MaxioAdvancedBilling.Models.Enums.IntervalUnit | Day=`day`, Month=`month`; open enum, preserve unknown wire values | sdk/dotnet/src/MaxioAdvancedBilling/Models/Enums/IntervalUnit.cs |
| MaxioAdvancedBilling.Models.Enums.SubscriptionState | Pending, FailedToCreate, Trialing, Assessing, Active, SoftFailure, PastDue, Suspended, Canceled, Expired, Paused, Unpaid, TrialEnded, OnHold, AwaitingSignup; wire values snake_case; open enum, preserve unknown values | sdk/dotnet/src/MaxioAdvancedBilling/Models/Enums/SubscriptionState.cs |

### Client/auth/server facts

| Fact | Decision/contract | Source |
| --- | --- | --- |
| Client | MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options); BasicAuth uses Core.Authentication.Basic.BasicAuthCredentials Username API key, Password `x` | sdk/dotnet/sdk-map.md |
| Server groups | Production US `https://{site}.chargify.com`, EU `https://{site}.ebilling.maxio.com`; Ebb both `https://events.chargify.com/{site}`. Only Production used; ServerEnvironment.Us default and Eu available, no sandbox enum | sdk/dotnet/sdk-map.md |
| Configuration | Bind Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle, Maxio:BaseUrl. Optional override assigned verbatim to selected Production environment BaseUrl; otherwise Site from Subdomain. Sandbox isolation comes from configured sandbox site credentials; no invented sandbox SDK enum | YOUR CALL — not in the map |
| Error defaults | RawError in Core.ErrorResponse exposes StatusCode, ReadAsBytes, ReadAsString, ReadAsJson<T>; ApiException<TError> in Core.Exceptions exposes StatusCode and Error. ResponseDeserializationException is ApiException, not typed ApiException<T>. Required CustomerResponse.Customer and ProductResponse.Product can reject drifted 2xx bodies; nullable SubscriptionResponse.Subscription requires explicit validation | sdk/dotnet/sdk-map.md |

## Trap notes

| Step | Hazard |
| --- | --- |
| 1 | HttpClient ownership and singleton options capture can leak sockets or stale credentials — MUST load maxio-advanced-billing:dotnet-client-initialization |
| 1 | Basic authentication credentials and fail-fast behavior can send anonymous traffic — MUST load maxio-advanced-billing:dotnet-authentication |
| 1–5 | Timeout budgets, write retry eligibility, safe logging and URL overrides can silently change side effects — MUST load maxio-advanced-billing:dotnet-configuration-resilience |
| 2–5 | Per-operation request records and response envelopes are easy to flatten incorrectly — MUST load maxio-advanced-billing:dotnet-calling-endpoints |
| 2–5 | Nullable response fields, open enums and union error bodies can lose state or throw — MUST load maxio-advanced-billing:dotnet-models |
| 3–5 | Transport errors and malformed responses are not all typed provider errors — MUST load maxio-advanced-billing:dotnet-error-handling |
| 5 | Mocking the wrong seam misses lost-response and retry behavior — MUST load maxio-advanced-billing:dotnet-testing |

## REQUIRED READING

- maxio-advanced-billing:dotnet-client-initialization · step 1
- maxio-advanced-billing:dotnet-authentication · step 1
- maxio-advanced-billing:dotnet-configuration-resilience · steps 1–5
- maxio-advanced-billing:dotnet-calling-endpoints · steps 2–5
- maxio-advanced-billing:dotnet-models · steps 2–5
- maxio-advanced-billing:dotnet-error-handling · steps 3–5
- maxio-advanced-billing:dotnet-testing · step 5

a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## PRODUCTION READINESS

| # | Concern | Decision | Source |
| --- | --- | --- | --- |
| 1 | Credential fail-fast | PublicApi startup binds Maxio section and rejects blank ApiKey, Subdomain, ProductFamilyHandle; validates optional BaseUrl | YOUR CALL — not in the map |
| 2 | Secret sourcing & rotation | Environment credentials loaded into PublicApi user-secrets outside repo. Options captured once at registration; rotate via process restart | YOUR CALL — not in the map |
| 3 | Total timeout budget | 30-second shared interactive deadline, 10-second attempts; ambiguous write recovery gets another 15 seconds even after disconnect, for at most 45 seconds of provider work plus persistence/HTTP overhead | YOUR CALL — not in the map |
| 4 | Write-retry ownership | SDK read retries limited; POST customer/subscription writes never retried, including transport/timeouts; explicit HttpMethodsToRetry only GET/HEAD | sdk/dotnet/sdk-map.md; maxio-advanced-billing:dotnet-configuration-resilience |
| 5 | Idempotency & ambiguous writes | Neither create request record has key. Customer unique reference documented; subscription uniqueness not documented. Application claims precede writes, deterministic references reconcile outcomes; generated header not relied upon | sdk/dotnet/src/MaxioAdvancedBilling/Requests/Customers/CreateCustomerOperationRequest.cs; Requests/Subscriptions/CreateSubscriptionOperationRequest.cs; Api/Customers.cs |
| 6 | Observability | Info: operation/outcome and opaque local correlation. Warning: pending/unknown and SDK status. Error: terminal infrastructure errors. No provider correlation field declared in used error payloads, so no fabricated ID; avoid logging raw bodies or exception text containing PII | YOUR CALL — not in the map |
| 7 | Sensitive data | Customer first/last/email present. LogRequestBody false, explicit LoggerFactory; no per-call Trace; no raw body diagnostics; deny-list includes customer/first_name/last_name/email/reference if form logging ever added | sdk/dotnet/src/MaxioAdvancedBilling/Models/CreateCustomer.cs |
| 8 | Environment | Only Production group used; configure US or EU host using supplied environment; sandbox means supplied sandbox site, never infer from an SDK sandbox enum; BaseUrl override wins verbatim | sdk/dotnet/sdk-map.md |
| 9 | Duplicate prevention | CatalogContext stores PK claims before SDK calls; SQL migration makes durable deployment; in-memory PK also rejects duplicate claim, with restart limitation documented | YOUR CALL — not in the map |
| 10 | Partial results | Plan catalog manual paging at 100/page up to 100 pages; at cap return explicit incomplete response or fail catalog read and refuse subscribe, never treat partial plan set as complete | YOUR CALL — not in the map |
| 11 | Unknown outcomes | Catch write transport/timeouts/cancellation/malformed success; bounded immediate read by reference, persist unknown if unresolved; background worker re-reads pending/unknown without recreating | YOUR CALL — not in the map |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| CreateCustomer | CatalogContext claim entity, deterministic string PK including site/user/customer | SQL PK or EF InMemory duplicate key rejects SaveChanges before provider call | Claim acquisition catches DbUpdateException and InMemory ArgumentException, detaches failed insert then reads prior claim | SubscriptionBillingService.TryClaim → EnsureCustomer (CreateCustomer call inside CreateAndReconcile) |
| CreateSubscription | CatalogContext claim entity, deterministic string PK including site/user/product handle | SQL PK or EF InMemory duplicate key rejects SaveChanges before provider call | Claim acquisition catches DbUpdateException and InMemory ArgumentException, detaches failed insert then reads prior claim | SubscriptionBillingService.TryClaim → SubscribeAsync (CreateSubscription call inside CreateAndReconcile) |

Order is claim → lookup/reconcile → SDK write if new claim and definitive absence → persist result. Delete/release claims only on definitive provider refusal; an ambiguous claim never permits another create. Claim uses app store, not provider. Existing completed claims replay current provider state; pending claims return pending and reconcile. Concurrent request must never steal or expire a claim into a repeat write.

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| ListProductsForProductFamily | 100 pages, 100 items/page, 30 second shared deadline; stop short page | Explicit incomplete/error response at cap; subscribe refuses to validate against incomplete set | SubscriptionBillingService.ReadPlans throws BillingException(503) at cap |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| CreateCustomer | Customers.ReadCustomerByReference | deterministic site/user reference stored before write | SubscriptionBillingService.CreateAndReconcile catch → LookupCustomer; retained claims → ReconcileAsync via BillingReconciliationWorker.ExecuteAsync | SubscriptionBillingTests.LostCustomerResponseIsReconciledWithoutResending |
| CreateSubscription | Subscriptions.FindSubscription | deterministic site/user/product reference stored before write | SubscriptionBillingService.CreateAndReconcile catch → LookupSubscription; retained claims → ReconcileAsync via BillingReconciliationWorker.ExecuteAsync | SubscriptionBillingTests.LostSubscriptionResponseIsReconciledWithoutResending; UnresolvedWriteRemainsClaimedAndWorkerSettlesIt |

## Existing application conventions

| Pattern | Exemplar |
| --- | --- |
| MinimalApi.Endpoint IEndpoint and JWT auth route convention | src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs |
| Catalog EF record store and configuration discovery | src/Infrastructure/Data/CatalogContext.cs |
| InMemory versus SQL Server provider selection | src/Infrastructure/Dependencies.cs |
| Existing JWT Name claim and user lookup | src/Infrastructure/Identity/IdentityTokenClaimService.cs |
| Identity user has only IdentityUser built-in fields; no first/last names | src/Infrastructure/Identity/ApplicationUser.cs |

## Assumptions & Blockers

- One subscription per authenticated immutable user/site/product handle; repeated requests replay it. Cancellation, resubscription and metering are outside this additive hero flow.
- No customer first/last fields exist on ApplicationUser. Implementation must choose and document deterministic nonempty display-name fallback or collect names in subscribe request, while email and ownership come from authenticated user.
- The configured seeded plans permit subscription without payment capture as stated by the user; live traffic must verify this. If provider refuses without a capability outside the plugin, stop and report the gap.
- UNVERIFIED: live error-body formatting may differ from generated typed shapes. Handle ApiException/ResponseDeserializationException by status where safe; never infer missing required success fields. Null/malformed success is an unknown write outcome requiring reconciliation.
- Production durable claims require CatalogContext SQL storage and migration. The authorized in-memory development mode loses both identity IDs and claims on restart. Deterministic references recover completed writes only while the immutable identity ID remains stable. In-memory mode cannot preserve unresolved claims across restart; never claim production durability for it.
- SDK is plugin-local source, not NuGet; deployment/CI must install the plugin and point the project reference to that installation. Never copy or modify SDK source.
- No unsupported Maxio capability is required by this plan; no current blocker.
