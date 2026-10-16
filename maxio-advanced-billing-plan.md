# Maxio Advanced Billing integration plan

## 1. Scope & sequence

| Step | Work | Operations | Source |
| --- | --- | --- | --- |
| 1 | Reference installed plugin SDK project in place; bind exactly Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle, Maxio:BaseUrl; validate configuration; construct singleton client. No catalog identifiers or credentials in source. | none | sdk/dotnet/sdk-map.md |
| 2 | Add application billing service contract, transport-neutral results, EF claim records and migration in existing CatalogContext. Production SQL persistence; runtime InMemory is explicitly ephemeral. | none | YOUR CALL — not in the map |
| 3 | Browse configured family, using its handle directly; page catalog; omit archived and unusable records. | ProductFamilies.ListProductsForProductFamily | sdk/dotnet/map/operations/ProductFamilies.md |
| 4 | Resolve JWT caller through existing Identity user; claim customer identity before create; reconcile existing reference; create only when positively absent and no unresolved write. | Customers.ReadCustomerByReference, Customers.CreateCustomer | sdk/dotnet/map/operations/Customers.md |
| 5 | Validate requested handle against offered catalog; claim user+plan before create; reconcile deterministic reference; subscribe with existing customer and offered handle. | Subscriptions.FindSubscription, Subscriptions.CreateSubscription | sdk/dotnet/map/operations/Subscriptions.md |
| 6 | Read caller's provider subscriptions and project plan, price, state, next assessment date; settle unknown claim outcomes through bounded reconciliation worker and repeat requests. | Customers.ReadCustomerByReference, Customers.ListCustomerSubscriptions, Subscriptions.FindSubscription | sdk/dotnet/map/operations/Customers.md; sdk/dotnet/map/operations/Subscriptions.md |
| 7 | JWT endpoints GET /api/subscription-plans, POST /api/subscriptions, GET /api/my-subscriptions; isolated HTTP-stub and concurrency/error tests; authenticated live sandbox smoke; guide. Existing checkout remains parallel. | operations above | YOUR CALL — not in the map |

### Codebase conventions

| Pattern | Exemplar |
| --- | --- |
| Ardalis EndpointBaseAsync + attribute route + Swagger | src/PublicApi/AuthEndpoints/AuthenticateEndpoint.cs |
| Existing record store and model configuration assembly | src/Infrastructure/Data/CatalogContext.cs |

## 2. CONTRACT SHEET

Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.
Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

For compactness the exact method signatures below retain generated unqualified names; their fully-qualified request/body/return types are explicitly provided in adjacent cells. In every signature, RequestOptions means MaxioAdvancedBilling.Core.RequestOptions; CancellationToken means System.Threading.CancellationToken.

| Controller property | Method signature | Request record + members used | Body model + fields used | Response envelope + fields read | Error case + accessors + payload | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| ProductFamilies | ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest: ProductFamilyId: string required (numeric ID OR handle prefixed with handle:); Page: int optional default 1; PerPage: int optional default 20 maximum 200; IncludeArchived: bool? optional. Other filter/date/include members omitted. | none | IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>; Product (product): MaxioAdvancedBilling.Models.Product required; product fields in shape table below | A: MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>; TryGetString(out string) [404], TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError) fallback | Map defaults say no automatic pagination; record exposes manual Page/PerPage, empty page ends traversal | sdk/dotnet/map/operations/ProductFamilies.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs; sdk/dotnet/src/MaxioAdvancedBilling/Models/ProductResponse.cs |
| Customers | ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest: Reference: string required | none | MaxioAdvancedBilling.Models.CustomerResponse.Customer (customer): MaxioAdvancedBilling.Models.Customer required; Id (id): int?; Reference (reference): string? | B: MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>; raw StatusCode, ReadAsBytes(), ReadAsString(), ReadAsJson<T>() | none | sdk/dotnet/map/operations/Customers.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Customers/ReadCustomerByReferenceRequest.cs; sdk/dotnet/src/MaxioAdvancedBilling/Models/CustomerResponse.cs; sdk/dotnet/src/MaxioAdvancedBilling/Models/Customer.cs |
| Customers | CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest: Body: MaxioAdvancedBilling.Models.CreateCustomerRequest? optional; no caller-supplied idempotency key | MaxioAdvancedBilling.Models.CreateCustomerRequest.Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer required; FirstName (first_name): string required; LastName (last_name): string required; Email (email): string required; Reference (reference): string? optional but integration always sets stable user-derived reference. All address/tax/payment-related optional fields omitted. | MaxioAdvancedBilling.Models.CustomerResponse.Customer required; Id (id): int?; Reference (reference): string? | A: MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>; TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1) [422]; TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError) fallback | none | sdk/dotnet/map/operations/Customers.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Customers/CreateCustomerOperationRequest.cs; sdk/dotnet/src/MaxioAdvancedBilling/Models/CreateCustomerRequest.cs; sdk/dotnet/src/MaxioAdvancedBilling/Models/CreateCustomer.cs; sdk/dotnet/src/MaxioAdvancedBilling/Api/Customers.cs |
| Subscriptions | FindSubscription(FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest: Reference: string? optional but always supplied | none | MaxioAdvancedBilling.Models.SubscriptionResponse.Subscription (subscription): MaxioAdvancedBilling.Models.Subscription? optional; fields below | A: MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>; TryGetNoContent(out MaxioAdvancedBilling.Core.ErrorResponse.RawError) [404]; TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError) fallback | none | sdk/dotnet/map/operations/Subscriptions.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Subscriptions/FindSubscriptionRequest.cs; sdk/dotnet/src/MaxioAdvancedBilling/Models/SubscriptionResponse.cs |
| Subscriptions | CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest: Body: MaxioAdvancedBilling.Models.CreateSubscriptionRequest? optional; no caller-supplied idempotency key | MaxioAdvancedBilling.Models.CreateSubscriptionRequest.Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription required; ProductHandle (product_handle): string? semantically required unless ProductId; CustomerId (customer_id): int? semantically required unless CustomerReference/customer attributes; Reference (reference): string? always set; PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod? explicitly set to Remittance for card-free recurring invoice billing. No required C# fields on inner model. Omit ProductId, CustomerReference, CustomerAttributes, payment profile/card/bank attributes, Components, OfferId, custom pricing, price-point selection, coupons, next/initial billing overrides, agreement fields. Use product pricing/trial defaults and immediate signup. | MaxioAdvancedBilling.Models.SubscriptionResponse.Subscription nullable; fields below; validate nonnull envelope and usable ID before declaring success | A: MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>; TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1) [422]; TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError) fallback | none | sdk/dotnet/map/operations/Subscriptions.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Subscriptions/CreateSubscriptionOperationRequest.cs; sdk/dotnet/src/MaxioAdvancedBilling/Models/CreateSubscriptionRequest.cs; sdk/dotnet/src/MaxioAdvancedBilling/Models/CreateSubscription.cs; sdk/dotnet/src/MaxioAdvancedBilling/Api/Subscriptions.cs |
| Customers | ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) | MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest: CustomerId: int required | none | IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>; nullable Subscription member; fields below | B: MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>; raw StatusCode, ReadAsBytes(), ReadAsString(), ReadAsJson<T>() | none; record has no page members | sdk/dotnet/map/operations/Customers.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Customers/ListCustomerSubscriptionsRequest.cs; sdk/dotnet/src/MaxioAdvancedBilling/Models/SubscriptionResponse.cs |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| POST ProductHandle must be an unarchived, usable handle returned by the application's configured-family catalog traversal; existence elsewhere on the site is insufficient | CreateSubscription ← ListProductsForProductFamily | billing service SubscribeAsync validates offered set before claiming/subscribing |
| Subscription CustomerId must be the authenticated user's stored/reconciled customer result, never caller input | CreateSubscription ← CreateCustomer / ReadCustomerByReference | billing service customer resolution before CreateSubscription |
| Account reads and reconciliation must match authenticated customer ID and expected handle/reference; do not expose a mismatching lookup result | ListCustomerSubscriptions / FindSubscription ← CreateCustomer / ReadCustomerByReference and offered ListProductsForProductFamily result | billing service ownership checks before projection or recording successful claim |

### Response/model shapes

All listed fields are optional unless marked required. Optional fields missing from live bodies must stay nullable or fail closed; never synthesize price/state/dates.

| Fully-qualified type | Fields read / wire names | Source |
| --- | --- | --- |
| MaxioAdvancedBilling.Models.Product | Id (id): int?; Name (name): string?; Handle (handle): string?; Description (description): string?; PriceInCents (price_in_cents): long?; Interval (interval): int?; IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?; ArchivedAt (archived_at): DateTimeOffset?; RequireCreditCard (require_credit_card): bool?; ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily? | sdk/dotnet/src/MaxioAdvancedBilling/Models/Product.cs |
| MaxioAdvancedBilling.Models.ProductFamily | Handle (handle): string?; Id (id): int? | sdk/dotnet/src/MaxioAdvancedBilling/Models/ProductFamily.cs |
| MaxioAdvancedBilling.Models.Subscription | Id (id): int?; Reference (reference): string?; ProductPriceInCents (product_price_in_cents): long?; State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?; NextAssessmentAt (next_assessment_at): DateTimeOffset? (next attempted collection); CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset? (regular period end); Currency (currency): string?; Customer (customer): MaxioAdvancedBilling.Models.Customer?; Product (product): MaxioAdvancedBilling.Models.Product? | sdk/dotnet/src/MaxioAdvancedBilling/Models/Subscription.cs |
| MaxioAdvancedBilling.Models.CustomerErrorResponse1 | Errors (errors): MaxioAdvancedBilling.Models.AnyOf.Errors1? optional; do not log body | sdk/dotnet/src/MaxioAdvancedBilling/Models/CustomerErrorResponse1.cs |
| MaxioAdvancedBilling.Models.AnyOf.Errors1 | TryGetCustomerError(out MaxioAdvancedBilling.Models.CustomerError); TryGetListOfString(out IReadOnlyList<string>); no payload details required for sanitized public error translation | sdk/dotnet/src/MaxioAdvancedBilling/Models/AnyOf/Errors1.cs |
| MaxioAdvancedBilling.Models.ErrorListResponse1 | Errors (errors): IReadOnlyList<string> required; sanitize public errors and avoid raw-body logging | sdk/dotnet/src/MaxioAdvancedBilling/Models/ErrorListResponse1.cs |

### Enum values used

| Fully-qualified enum | Declared values (member = wire) | Handling | Source |
| --- | --- | --- | --- |
| MaxioAdvancedBilling.Models.Enums.IntervalUnit | Day=day; Month=month | Return known/unknown wire value faithfully; nullable stays nullable | sdk/dotnet/src/MaxioAdvancedBilling/Models/Enums/IntervalUnit.cs |
| MaxioAdvancedBilling.Models.Enums.SubscriptionState | Pending=pending; FailedToCreate=failed_to_create; Trialing=trialing; Assessing=assessing; Active=active; SoftFailure=soft_failure; PastDue=past_due; Suspended=suspended; Canceled=canceled; Expired=expired; Paused=paused; Unpaid=unpaid; TrialEnded=trial_ended; OnHold=on_hold; AwaitingSignup=awaiting_signup | Display provider state, including unknown values; never promise active unless returned | sdk/dotnet/src/MaxioAdvancedBilling/Models/Enums/SubscriptionState.cs |

| MaxioAdvancedBilling.Models.Enums.CollectionMethod | Automatic=automatic; Remittance=remittance; Prepaid=prepaid; Invoice=invoice | Set Remittance explicitly; source documents remittance/automatic/prepaid for Relationship Invoicing and invoice/automatic for legacy Statements | sdk/dotnet/src/MaxioAdvancedBilling/Models/Enums/CollectionMethod.cs |

### Client construction, authentication, server nodes

| Fact / decision | Contract | Source |
| --- | --- | --- |
| Constructor | MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options); caller owns HttpClient lifetime | sdk/dotnet/sdk-map.md |
| Basic auth | options.BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials; Username=Maxio:ApiKey; Password=x. Validate key before construction. | sdk/dotnet/sdk-map.md |
| Environments | MaxioAdvancedBilling.Servers.ServerEnvironment.Us (default US), .Eu (EU); no SDK sandbox member | sdk/dotnet/sdk-map.md |
| Production group | Us https://{site}.chargify.com; Eu https://{site}.ebilling.maxio.com. options.Server.Production.Us.Site / .Eu.Site default subdomain; BaseUrl override on corresponding node | sdk/dotnet/sdk-map.md |
| Ebb group | Both environments https://events.chargify.com/{site}; options.Server.Ebb.Us.Site / .Eu.Site and BaseUrl. Out of scope. | sdk/dotnet/sdk-map.md |
| Configuration selection | US region for supplied sandbox. Maxio:Subdomain selects site. Set Production.Us.BaseUrl to Maxio:BaseUrl verbatim when provided; otherwise set Production.Us.Site. Different regional sites can supply literal BaseUrl. Do not use env var region as proof of sandbox. | YOUR CALL — not in the map |
| SDK reference | Build against plugin sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj in place; never vendor source. CI needs plugin installed and path re-pointed or configured. | sdk/dotnet/sdk-map.md |
| Error correlation | In-scope typed payloads expose no declared correlation member. Log local request trace ID, SDK operation/status and safe provider request-id header only if present; do not invent correlation fields or log RawError body. | sdk/dotnet/src/MaxioAdvancedBilling/Models/CustomerErrorResponse1.cs; sdk/dotnet/src/MaxioAdvancedBilling/Models/ErrorListResponse1.cs |

## 3. Trap notes

- Step 1: HTTP lifetime and options capture can break hosted DI ownership; MUST load maxio-advanced-billing:dotnet-client-initialization.
- Step 1: partial/missing Basic credentials can appear only as remote authorization failure; MUST load maxio-advanced-billing:dotnet-authentication.
- Steps 1, 3–6: what timeout bounds, write resends, cancellation, unknown writes and manual paging can make calls hang or duplicate effects; MUST load maxio-advanced-billing:dotnet-configuration-resilience.
- Steps 3–6: request-record argument and response-envelope mistakes cause compile/runtime errors; MUST load maxio-advanced-billing:dotnet-calling-endpoints.
- Steps 3–6: nullable fields, open enum values and required envelope members affect safe projection; MUST load maxio-advanced-billing:dotnet-models.
- Steps 4–6: error deserialization and transport ambiguity can bypass typed catches or trigger duplicate creates; MUST load maxio-advanced-billing:dotnet-error-handling.
- Step 7: transport stub, malformed responses and retry tests require the actual SDK seam; MUST load maxio-advanced-billing:dotnet-testing.

## 4. REQUIRED READING

| Skill | Governs |
| --- | --- |
| maxio-advanced-billing:dotnet-getting-started | all SDK contracts |
| maxio-advanced-billing:dotnet-client-initialization | step 1 |
| maxio-advanced-billing:dotnet-authentication | step 1 |
| maxio-advanced-billing:dotnet-configuration-resilience | steps 1, 3–7 |
| maxio-advanced-billing:dotnet-calling-endpoints | steps 3–6 |
| maxio-advanced-billing:dotnet-models | steps 3–6 |
| maxio-advanced-billing:dotnet-error-handling | steps 4–7 |
| maxio-advanced-billing:dotnet-testing | step 7 |

a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision | Source |
| --- | --- | --- | --- |
| 1 | Credential fail-fast | PublicApi startup binds Maxio section and rejects missing/blank ApiKey, Subdomain, ProductFamilyHandle; optional BaseUrl must be absolute HTTP(S), nonblank when set. No remote discovery needed at startup. | YOUR CALL — not in the map |
| 2 | Secret sourcing & rotation | Load env credentials into PublicApi .NET user-secrets outside repo; use config keys above; validated IOptions and singleton SDK options are frozen once at host startup. Rotate by restarting process. Deployment uses secret config provider, never committed values. | YOUR CALL — not in the map |
| 3 | Total timeout budget | One billing boundary owns 30-second interactive budget linked to request cancellation; per-attempt SDK timeout 10 seconds, HttpClient 10 seconds. Ambiguous write reconciliation has independent bounded 10-second token after cancellation. Total live response bound at most 40 seconds; unresolved state persists for worker. Worker uses separate bounded 30-second cycles. | YOUR CALL — not in the map |
| 4 | Write-retry ownership | Explicitly configure retryable methods GET/HEAD only, maximum 2 retries; POST creates never resent by SDK. No application retries of an unresolved write. | YOUR CALL — not in the map |
| 5 | Idempotency & ambiguous writes | Both create request records have no real idempotency member. Deterministic customer reference and deterministic user+plan subscription reference; customer docs promise unique reference, subscription uniqueness UNVERIFIED. Local claims prevent races. Lookup settles outcomes; never assume missing lookup proves an ambiguous write cannot still complete. | sdk/dotnet/src/MaxioAdvancedBilling/Requests/Customers/CreateCustomerOperationRequest.cs; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Subscriptions/CreateSubscriptionOperationRequest.cs; sdk/dotnet/src/MaxioAdvancedBilling/Api/Customers.cs |
| 6 | Observability | Information: operation, trace ID, outcome, provider entity IDs; Warning: retries, pending reconciliation, provider rejection; Error: unexpected integration faults. No email/name/key/token/raw bodies. Provider payload correlation member absent; use local trace ID and safe response request-id header only if available. | YOUR CALL — not in the map |
| 7 | Sensitive data | Customer names/email/reference are sensitive. Explicit LoggerFactory, LogRequestBody=false; do not log response bodies in application; disable SDK category body output. Extend RedactedKeys with first_name,last_name,email,reference,customer,subscription as defense for future form paths; current requests JSON so deny-list alone is insufficient. | sdk/dotnet/src/MaxioAdvancedBilling/Models/CreateCustomer.cs; sdk/dotnet/src/MaxioAdvancedBilling/Core/Configuration/LoggingOptions.cs |
| 8 | Environment selection | Only Production group touched; both groups and Us/Eu nodes enumerated above. Development/testing supplied sandbox site/key only; SDK has no sandbox flag. Production operators supply intended site through same four config keys. BaseUrl override honored verbatim. | sdk/dotnet/sdk-map.md |
| 9 | Duplicate prevention | EF primary-key insert claims before provider calls; PK enforcement works on SQL and InMemory. Catch SQL DbUpdateException / InMemory duplicate ArgumentException only when reloading proves same claim exists; otherwise rethrow. Different plan calls share same customer claim. | YOUR CALL — not in the map |
| 10 | Partial results | Manual catalog pages limited to 50 x 200 records and overall budget; return IsTruncated=true when cap reached; POST refuses to validate against an incomplete catalog. Timeout is explicit error, never successful empty result. Account read has no page API. | YOUR CALL — not in the map |
| 11 | Unknown outcomes | Transport/timeout/cancellation after send, 5xx, unreadable success, or success missing usable ID become Unknown. Own catch first looks up reference. If not found/unavailable, persist Unknown and return 202 pending; hosted reconciliation worker and repeat requests read it until settled. Never delete unresolved claim or automatically re-create on 404 after unknown send. Definite 4xx validation rejection releases claim after reconciliation rules. | YOUR CALL — not in the map |

### DUPLICATE CLAIMS

Order for both rows: claim → SDK call → record result. Claim key includes billing site/base address scope so changing configuration cannot reuse another site's mapping. Successful claims persist; definite provider refusal releases; unknown claims retain reference and recovery state.

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Customers.CreateCustomer | Existing CatalogContext, new billing claim DbSet; primary key deterministic site+user+customer operation; persisted before outbound create | Store primary-key collision; SQL DbUpdateException, InMemory ArgumentException | Claim repository TryClaimAsync catches collision, detaches losing entity, reloads winner; never grants duplicate permission | SubscriptionBillingService.TryClaimAsync → MaxioGateway.CreateCustomerAsync (called by EnsureCustomerAsync) |
| Subscriptions.CreateSubscription | Same CatalogContext DbSet; primary key deterministic site+user+plan; persisted before outbound create | Store primary-key collision; SQL DbUpdateException, InMemory ArgumentException | Same claim repository TryClaimAsync catch; settled winner returns provider result, pending winner returns 202 | SubscriptionBillingService.TryClaimAsync → MaxioGateway.CreateSubscriptionAsync (called by SubscribeAsync) |

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| ProductFamilies.ListProductsForProductFamily | Manual Page starting 1; PerPage=200; stop on short/empty page; max 50 pages and 30-second boundary budget | Catalog result IsTruncated=true at page cap; POST fails closed on truncated offered set; timeout explicit failure | SubscriptionBillingService.LoadPlansAsync |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| Customers.CreateCustomer | Customers.ReadCustomerByReference | Same deterministic site+user customer reference as sent; assert returned reference/ID match | EnsureCustomerAsync catch → ReconcileCustomerAsync; ReconcilePendingAsync sweeps unresolved claims | LostCustomerResponseIsReconciledWithoutASecondWrite; UnknownCustomerWriteNeverReplaysAndSweepCanRecoverIt |
| Subscriptions.CreateSubscription | Subscriptions.FindSubscription | Same deterministic site+user+plan subscription reference as sent; assert returned reference/customer/handle match | SubscribeAsync catch → RecoverSubscriptionAsync; ReconcilePendingAsync sweeps unresolved claims | LostOrMalformedSubscriptionResponseIsReconciledWithoutASecondWrite; UnresolvedWriteRemainsPendingAndSweepSettlesItWithoutReplay |

### Additional defensive directives

| Case | Required behavior | Source |
| --- | --- | --- |
| Live subscription nullable fields | Reject missing subscription/ID as unknown after write; read-only projection preserves nulls and reports malformed required ownership data as provider error. Actual state/date/price presence must be verified live. | UNVERIFIED |
| Catalog uses manual page members despite map's no-pagination default | Walk request Page/PerPage explicitly; SDK returns plain IReadOnlyList and offers no automatic traversal. | sdk/dotnet/src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs |
| Pending claim owner crash | Reconciliation worker must handle every non-completed state that may have reached the provider. Do not reclaim and create merely because elapsed time passed; lookup first, preserve unresolved outcome. | YOUR CALL — not in the map |
| Identical caller request | POST accepts plan handle only, no mandatory caller idempotency header. Return same subscription for user+plan, including subsequent requests. No upgrade/cancel/resubscribe in this task. | YOUR CALL — not in the map |

## 6. Assumptions & Blockers

- Minor assumption: one subscription per authenticated eShop user per offered plan; repeat POST returns that existing subscription. A user may hold Basic and Pro concurrently. Cancellation, upgrades, card capture and component usage are outside this additive task.
- Minor assumption: POST requires a plan handle selected from GET plans. No seeded handle is hard-coded as a default; the documented hero verification selects the offered Pro plan.
- Minor assumption: the existing identity has no personal-name fields needed by the SDK create model; use an application-defined non-sensitive display name fallback while sending the authenticated user's Identity email. Never accept customer/user IDs from POST.
- Existing CatalogContext is the production durable claim store. UseOnlyInMemoryDatabase=true deliberately loses identities, claims and mappings on restart, as acknowledged in the task; multi-instance production requires the existing persistent SQL configuration and migration.
- Subscription reference uniqueness is not promised by the inspected SDK source. Therefore durable application claims and reconciliation provide duplicate prevention; the implementation must never rely on provider subscription-reference uniqueness or automatically retry unresolved creates.
- No blocking plugin capability gap found: configured-family listing by handle, customer reference lookup/create, subscription reference lookup/create and account subscription reads are all exposed.
- Application decision: this card-free feature uses explicit remittance collection for immediate recurring invoice billing; it leaves product price, trial and billing-date defaults intact. A different site must support Relationship Invoicing remittance for the same card-free flow.
- Live validation reported by implementation agent: omitting collection method produced typed CreateSubscriptionError 422 via ErrorListResponse1, “No payment method was on file for the $299.00 balance”, despite catalog RequireCreditCard=false. This settles that catalog flag alone cannot establish card-free signup acceptance. Live remittance verification succeeded: Pro and Basic returned active subscriptions, actual prices, and next assessment dates; repeated enrollment returned the same subscription.
