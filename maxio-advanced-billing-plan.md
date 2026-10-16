# Maxio subscription billing plan

## Scope & sequence

| Step | Work and operations | Source |
| --- | --- | --- |
| 1 | Add a project reference to the installed plugin SDK, never copy its source. Bind exactly `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`; required values come from user-secrets populated from the task's environment variables. No values in repository files. SDK options are captured once; restart for rotation. | sdk/dotnet/sdk-map.md; YOUR CALL — not in the map |
| 2 | Add application billing contracts and EF customer/subscription claim records in existing CatalogContext, including relational migration and model snapshot. Preserve commerce behavior. Claims are primary-key inserts before provider calls. | YOUR CALL — not in the map |
| 3 | Implement catalog retrieval with `Products.ListProducts`, explicit Page/PerPage, filter every product to configured family handle and nonarchived status. Prices are provider cents and interval; never hard-code catalog IDs, handles, currency, or prices. | sdk/dotnet/map/operations/Products.md; YOUR CALL — not in the map |
| 4 | Implement customer ensure using `Customers.ReadCustomerByReference`, claim, `Customers.CreateCustomer`, record result. Implement subscribe using list-membership validation, claim, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription`, record result. Catch ambiguous outcomes and reconcile by reference. | sdk/dotnet/map/operations/Customers.md; sdk/dotnet/map/operations/Subscriptions.md |
| 5 | Implement account read with `Customers.ReadCustomerByReference` and `Customers.ListCustomerSubscriptions`; ownership derives from authenticated Identity user, never body customer IDs. Read current provider state and subscription price, not a stale local plan snapshot. | sdk/dotnet/map/operations/Customers.md; YOUR CALL — not in the map |
| 6 | Add JWT-protected `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions` using IEndpoint/AddRoute conventions. Resolve token Name with UserManager to obtain stable account identity and server-owned email. Request accepts ProductHandle plus required FirstName/LastName for new-customer enrollment; never accepts identity/customer/reference overrides. POST returns 201 for created, 200 for replay, 202 with pending status for retained ambiguous work. | YOUR CALL — not in the map |
| 7 | Add SDK-seam and endpoint tests; build and run tests; run detached PublicApi on assigned ports with in-memory databases, authenticate, list, subscribe, replay/concurrent subscribe, account read, alternate plan and cross-account checks. Inspect first-call SDK Information logs. Stop only processes started for this task. Write reproducible verification guide. | YOUR CALL — not in the map |

### Repository conventions

| Pattern | One exemplar |
| --- | --- |
| IEndpoint with AddRoute, JWT authentication attribute, Results, WithTags | src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs |
| Request/response correlation base classes | src/PublicApi/BaseResponse.cs |
| Identity token Name claim | src/Infrastructure/Identity/IdentityTokenClaimService.cs |
| Identity user has email/username but no first/last-name fields | src/Infrastructure/Identity/ApplicationUser.cs |
| Existing app-owned entity store | src/Infrastructure/Data/CatalogContext.cs |
| EF entity configuration | src/Infrastructure/Data/Config/OrderConfiguration.cs |
| Existing in-memory/SQL Server provider selection | src/Infrastructure/Dependencies.cs |
| Migration pattern | src/Infrastructure/Data/Migrations/20201202111507_InitialModel.cs |

## CONTRACT SHEET

Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.
Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

All signatures below return `System.Threading.Tasks.Task<T>` where T is the response column. Their common trailing parameters are `MaxioAdvancedBilling.Core.RequestOptions? requestOptions = null, System.Threading.CancellationToken cancellationToken = default`. Bodies are optional at the request-record level but MUST be supplied for the creates. Field shape tables below are part of each row's contract.

| Controller property | Method signature (first parameter; common tail above) | Request record + used members | Body model + used fields | Response envelope + fields read | Error case/accessors/payload | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.Products` | `ListProducts(MaxioAdvancedBilling.Requests.Products.ListProductsRequest request, …)` | `MaxioAdvancedBilling.Requests.Products.ListProductsRequest`: Page:int default 1; PerPage:int default 20 maximum 200; IncludeArchived:bool? optional, send false; all other fields omitted | none | `System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; `.Product` → product shape below | B: `MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` | Plain list, manual query paging, stop short page; map has no Pageable | sdk/dotnet/map/operations/Products.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Products/ListProductsRequest.cs |
| `client.Customers` | `ReadCustomerByReference(MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest request, …)` | Reference:string required | none | `MaxioAdvancedBilling.Models.CustomerResponse.Customer` required → customer shape | B: `MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; 404 alone means absent | none | sdk/dotnet/map/operations/Customers.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Customers/ReadCustomerByReferenceRequest.cs |
| `client.Customers` | `CreateCustomer(MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest request, …)` | Body:`MaxioAdvancedBilling.Models.CreateCustomerRequest?`, optional in record, supply | `MaxioAdvancedBilling.Models.CreateCustomerRequest.Customer` required; nested `MaxioAdvancedBilling.Models.CreateCustomer` below | `MaxioAdvancedBilling.Models.CustomerResponse.Customer` required → customer shape | A: `MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` 422; inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback | none | sdk/dotnet/map/operations/Customers.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Customers/CreateCustomerOperationRequest.cs; sdk/dotnet/src/MaxioAdvancedBilling/Api/Customers.cs |
| `client.Subscriptions` | `FindSubscription(MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest request, …)` | Reference:string? optional in record, supply deterministic reference | none | `MaxioAdvancedBilling.Models.SubscriptionResponse.Subscription` nullable → subscription shape | A: `MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` 404; inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback | none | sdk/dotnet/map/operations/Subscriptions.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Subscriptions/FindSubscriptionRequest.cs; sdk/dotnet/src/MaxioAdvancedBilling/Api/Subscriptions.cs |
| `client.Subscriptions` | `CreateSubscription(MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest request, …)` | Body:`MaxioAdvancedBilling.Models.CreateSubscriptionRequest?`, optional in record, supply | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest.Subscription` required; nested `MaxioAdvancedBilling.Models.CreateSubscription` below | `MaxioAdvancedBilling.Models.SubscriptionResponse.Subscription` nullable → subscription shape | A: `MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` 422; inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback | none | sdk/dotnet/map/operations/Subscriptions.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Subscriptions/CreateSubscriptionOperationRequest.cs; sdk/dotnet/src/MaxioAdvancedBilling/Api/Subscriptions.cs |
| `client.Customers` | `ListCustomerSubscriptions(MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest request, …)` | CustomerId:int required, from own ensured customer | none | `System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; read each `.Subscription` | B: `MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` | none, no page inputs | sdk/dotnet/map/operations/Customers.md; sdk/dotnet/src/MaxioAdvancedBilling/Requests/Customers/ListCustomerSubscriptionsRequest.cs |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| Every caller ProductHandle must be present in the complete active configured-family plan set offered by the application; existence lookup alone is insufficient. | `CreateSubscription` ← `ListProducts` | Billing subscribe service reads offered catalog and checks ordinal handle membership before any claim or provider write; reject if catalog incomplete. |
| Subscription CustomerId must be the provider ID recovered/created for the authenticated user's deterministic customer reference. | `CreateSubscription` ← `ReadCustomerByReference` / `CreateCustomer` | Billing service uses server-owned ensured customer result; caller cannot supply CustomerId. |

### Model shapes used by operation rows

All fields below are optional/nullable unless explicitly marked required. Wire names are literal source attributes. Only the listed fields are accessed or constructed.

| Fully qualified type | Fields used (C# / wire : declared type) | Source |
| --- | --- | --- |
| `MaxioAdvancedBilling.Models.CreateCustomerRequest` | Customer / customer : `MaxioAdvancedBilling.Models.CreateCustomer` required | sdk/dotnet/src/MaxioAdvancedBilling/Models/CreateCustomerRequest.cs |
| `MaxioAdvancedBilling.Models.CreateCustomer` | FirstName / first_name : string required; LastName / last_name : string required; Email / email : string required; Reference / reference : string? | sdk/dotnet/src/MaxioAdvancedBilling/Models/CreateCustomer.cs |
| `MaxioAdvancedBilling.Models.CustomerResponse` | Customer / customer : `MaxioAdvancedBilling.Models.Customer` required | sdk/dotnet/src/MaxioAdvancedBilling/Models/CustomerResponse.cs |
| `MaxioAdvancedBilling.Models.Customer` | Id / id : int?; Reference / reference : string? | sdk/dotnet/src/MaxioAdvancedBilling/Models/Customer.cs |
| `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` | Subscription / subscription : `MaxioAdvancedBilling.Models.CreateSubscription` required | sdk/dotnet/src/MaxioAdvancedBilling/Models/CreateSubscriptionRequest.cs |
| `MaxioAdvancedBilling.Models.CreateSubscription` | ProductHandle / product_handle : string?; CustomerId / customer_id : int?; Reference / reference : string?; PaymentCollectionMethod / payment_collection_method : `MaxioAdvancedBilling.Models.Enums.CollectionMethod?` | sdk/dotnet/src/MaxioAdvancedBilling/Models/CreateSubscription.cs |
| `MaxioAdvancedBilling.Models.SubscriptionResponse` | Subscription / subscription : `MaxioAdvancedBilling.Models.Subscription?` | sdk/dotnet/src/MaxioAdvancedBilling/Models/SubscriptionResponse.cs |
| `MaxioAdvancedBilling.Models.Subscription` | Id / id : int?; Reference / reference : string?; State / state : `MaxioAdvancedBilling.Models.Enums.SubscriptionState?`; ProductPriceInCents / product_price_in_cents : long?; CurrentPeriodEndsAt / current_period_ends_at : DateTimeOffset?; NextAssessmentAt / next_assessment_at : DateTimeOffset?; Product / product : `MaxioAdvancedBilling.Models.Product?`; Customer / customer : `MaxioAdvancedBilling.Models.Customer?`; Currency / currency : string?; PaymentCollectionMethod / payment_collection_method : `MaxioAdvancedBilling.Models.Enums.CollectionMethod?` | sdk/dotnet/src/MaxioAdvancedBilling/Models/Subscription.cs |
| `MaxioAdvancedBilling.Models.ProductResponse` | Product / product : `MaxioAdvancedBilling.Models.Product` required | sdk/dotnet/src/MaxioAdvancedBilling/Models/ProductResponse.cs |
| `MaxioAdvancedBilling.Models.Product` | Id / id : int?; Handle / handle : string?; Name / name : string?; Description / description : string?; PriceInCents / price_in_cents : long?; Interval / interval : int?; IntervalUnit / interval_unit : `MaxioAdvancedBilling.Models.Enums.IntervalUnit?`; ArchivedAt / archived_at : DateTimeOffset?; RequireCreditCard / require_credit_card : bool?; ProductFamily / product_family : `MaxioAdvancedBilling.Models.ProductFamily?` | sdk/dotnet/src/MaxioAdvancedBilling/Models/Product.cs |
| `MaxioAdvancedBilling.Models.ProductFamily` | Handle / handle : string?; ArchivedAt / archived_at : DateTimeOffset? | sdk/dotnet/src/MaxioAdvancedBilling/Models/ProductFamily.cs |
| `MaxioAdvancedBilling.Models.CustomerErrorResponse1` | Errors / errors : `MaxioAdvancedBilling.Models.AnyOf.Errors1?`; avoid reflecting provider PII into public responses | sdk/dotnet/src/MaxioAdvancedBilling/Models/CustomerErrorResponse1.cs |
| `MaxioAdvancedBilling.Models.ErrorListResponse1` | Errors / errors : `System.Collections.Generic.IReadOnlyList<string>` required; sanitize public messages | sdk/dotnet/src/MaxioAdvancedBilling/Models/ErrorListResponse1.cs |
| `MaxioAdvancedBilling.Models.AnyOf.Errors1` | `TryGetCustomerError(out MaxioAdvancedBilling.Models.CustomerError)` and `TryGetListOfString(out System.Collections.Generic.IReadOnlyList<string>)`; integration need not traverse CustomerError | sdk/dotnet/src/MaxioAdvancedBilling/Models/AnyOf/Errors1.cs |

CreateSubscription prose requires product handle or ID and existing customer ID/reference or customer attributes despite optional model members. Supply ProductHandle, CustomerId and PaymentCollectionMethod=CollectionMethod.Remittance for this no-card recurring invoice flow; deliberately omit ProductId, CustomerReference, CustomerAttributes, price-point overrides, CustomPrice, components, payment attributes, coupons, NextBillingAt, InitialBillingAt, DeferSignup overrides. Catalog default pricing and normal immediate enrollment apply. Payment profile/card attributes are omitted because the task requires no card capture. Live verification of the original automatic-collection request returned typed 422 Errors: "No payment method was on file for the $299.00 balance". CreateSubscription.PaymentCollectionMethod documents remittance for Relationship Invoicing. Explicit remittance produced active enrollment at the unchanged catalog price with a next assessment date; invoices remain payable, rather than pretending the balance was collected. Responses disclose the collection method. Legacy Statements invoice mode is outside this explicit policy. Do not manipulate future billing dates to bypass a rejection. Source: sdk/dotnet/src/MaxioAdvancedBilling/Api/Subscriptions.cs and sdk/dotnet/src/MaxioAdvancedBilling/Models/CreateSubscription.cs.

Customer creation Reference is provider-unique per CreateCustomer remarks; neither create request record carries a caller-supplied idempotency member. Subscription reference lookup is supported, but source does not promise subscription-reference uniqueness: application claim provides write exclusion. Source: sdk/dotnet/src/MaxioAdvancedBilling/Api/Customers.cs; sdk/dotnet/src/MaxioAdvancedBilling/Api/Subscriptions.cs; request records above.

### Enums actually needed

| Type | Static members = wire values | Source |
| --- | --- | --- |
| `MaxioAdvancedBilling.Models.Enums.CollectionMethod` | Automatic=automatic; Remittance=remittance; Prepaid=prepaid; Invoice=invoice. Use Remittance explicitly for current Relationship Invoicing no-card billing; preserve the provider response value. | sdk/dotnet/src/MaxioAdvancedBilling/Models/Enums/CollectionMethod.cs; sdk/dotnet/src/MaxioAdvancedBilling/Models/CreateSubscription.cs |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | Day=day; Month=month; preserve unknown wire values when presenting data | sdk/dotnet/src/MaxioAdvancedBilling/Models/Enums/IntervalUnit.cs |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | Pending=pending; FailedToCreate=failed_to_create; Trialing=trialing; Assessing=assessing; Active=active; SoftFailure=soft_failure; PastDue=past_due; Suspended=suspended; Canceled=canceled; Expired=expired; Paused=paused; Unpaid=unpaid; TrialEnded=trial_ended; OnHold=on_hold; AwaitingSignup=awaiting_signup; preserve unknown values, never silently label active | sdk/dotnet/src/MaxioAdvancedBilling/Models/Enums/SubscriptionState.cs |

### Client/auth/server facts

| Fact | Contract | Source |
| --- | --- | --- |
| Construction | `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`; client properties Customers, Products, Subscriptions | sdk/dotnet/sdk-map.md |
| Auth | options.BasicAuth: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials`; Username=Maxio:ApiKey; Password=literal protocol value `x`; reject blank ApiKey/Subdomain/ProductFamilyHandle at startup | sdk/dotnet/sdk-map.md; YOUR CALL — not in the map |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` default (US), `.Eu` (EU). No sandbox enum. `MAXIO_ENVIRONMENT` is a region marker, not a sandbox flag; current sandbox's site is supplied by configuration. | sdk/dotnet/sdk-map.md |
| Groups | Production: US `https://{site}.chargify.com`, EU `https://{site}.ebilling.maxio.com`; Ebb both regions `https://events.chargify.com/{site}`. Each Site template default is `subdomain`. Scope uses Production only. | sdk/dotnet/sdk-map.md |
| Overrides | Configure selected `options.Server.Production.Us.Site` from Maxio:Subdomain for this US sandbox; `options.Server.Production.Us.BaseUrl` is exactly Maxio:BaseUrl when supplied, preserving it without reconstructing a host. For EU selection configure corresponding Eu node. | sdk/dotnet/sdk-map.md; YOUR CALL — not in the map |
| Retry | `MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with { … }`; all members required. Default retry verbs GET/HEAD/PUT/OPTIONS, MaxRetries=3, Timeout=100s per attempt; integration sets explicit choices below. | sdk/dotnet/sdk-map.md; sdk/dotnet/src/MaxioAdvancedBilling/Core/Configuration/RetryOptions.cs |
| Raw errors | `MaxioAdvancedBilling.Core.ErrorResponse.RawError`: StatusCode:HttpStatusCode; ReadAsBytes():ReadOnlyMemory<byte>; ReadAsString():string; ReadAsJson<T>():T? | sdk/dotnet/sdk-map.md |

### Defensive mapping directives

| Concern | Directive | Source |
| --- | --- | --- |
| Missing optional success members | Validate envelopes, positive IDs, exact reference/customer ownership, plan family/handle, nonnegative price and positive interval. Treat malformed/unusable success as potentially committed for writes and reconcile; never create again because a response cannot be mapped. | UNVERIFIED |
| Next billing display | Return nullable next billing derived from provider NextAssessmentAt, falling back to CurrentPeriodEndsAt with explicit provenance if needed; never synthesize a calendar month. Validate live seeded flow populates it. | YOUR CALL — not in the map |
| Price/currency | Subscription confirmation uses Subscription.ProductPriceInCents, never catalog Product.PriceInCents as a substitute for a subscribed price point. Currency comes from Subscription.Currency when available; catalog model has no currency member, so list plans in cents without inventing USD. | sdk/dotnet/src/MaxioAdvancedBilling/Models/Subscription.cs; sdk/dotnet/src/MaxioAdvancedBilling/Models/Product.cs; YOUR CALL — not in the map |

## Trap notes

| Step | Hazard |
| --- | --- |
| 1 | Client lifetime and HttpClient ownership can leak connections or capture disposed clients. **MUST load maxio-advanced-billing:dotnet-client-initialization** |
| 1 | Credential placement and omitted credentials can turn a startup problem into a production 401. **MUST load maxio-advanced-billing:dotnet-authentication** |
| 1–5 | What Timeout actually bounds, verb retry eligibility, base URL server node, and logging defaults can invalidate the entire request budget or resend writes. **MUST load maxio-advanced-billing:dotnet-configuration-resilience** |
| 3–5 | Request-record ownership, cancellation arguments and response envelopes can compile incorrectly or map the wrong level. **MUST load maxio-advanced-billing:dotnet-calling-endpoints** |
| 3–5 | Nullable records and open enums can hide missing identifiers or mislabel new provider states. **MUST load maxio-advanced-billing:dotnet-models** |
| 4–6 | Error families and malformed success/error bodies can bypass a narrow catch and incorrectly permit a repeated write. **MUST load maxio-advanced-billing:dotnet-error-handling** |
| 7 | SDK exception construction and HTTP stubbing can test an impossible path instead of dropped-response behavior. **MUST load maxio-advanced-billing:dotnet-testing** |

## REQUIRED READING

| Skill | Step |
| --- | --- |
| maxio-advanced-billing:dotnet-getting-started | all contract facts; already loaded during planning |
| maxio-advanced-billing:dotnet-client-initialization | 1 |
| maxio-advanced-billing:dotnet-authentication | 1 |
| maxio-advanced-billing:dotnet-configuration-resilience | 1–5 |
| maxio-advanced-billing:dotnet-calling-endpoints | 3–5 |
| maxio-advanced-billing:dotnet-models | 3–5 |
| maxio-advanced-billing:dotnet-error-handling | 4–6 |
| maxio-advanced-billing:dotnet-testing | 7 |

a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## PRODUCTION READINESS

Application choices in this section are `YOUR CALL — not in the map`; SDK facts refer to the contract sheet sources.

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind Maxio section during PublicApi registration; refuse startup on missing/blank ApiKey, Subdomain or ProductFamilyHandle. Optional BaseUrl, when present, must be absolute http(s) and goes verbatim to selected server node; allow HTTP for local stub tests. Auth protocol password is SDK-documented x. |
| 2 | Secret sourcing & rotation | Main agent loads env secrets into PublicApi user-secrets outside repo. Build SDK options once during registration, capture singleton client; restart for rotation. Deployment supplies same section through normal .NET providers. Never persist values in repo. |
| 3 | Total timeout budget | Single 30s foreground budget per endpoint shared by every SDK read/page/write. SDK per-attempt 5s timeout; GET at most one retry with short delay. If cancellation/timeout follows an attempted write, persist Unknown and perform reference reconciliation with an independent bounded 5s token; endpoint maximum 35s including reconciliation. Background reconciliation gets separate 10s iteration budget. Propagate client cancellation to reads; do not let a disconnected client discard durable write state. |
| 4 | Write-retry ownership | SDK retry methods explicitly GET and HEAD only; MaxRetries=1. CreateCustomer/CreateSubscription POST never automatically retried. Local orchestration owns reconciliation; no repeated POST for Unknown claims. |
| 5 | Idempotency & ambiguous writes | Neither write has a true request-record key. Customer reference is deterministic stable normalized-username hash; subscription reference deterministic customer-reference+plan hash. Namespace references/claims by configured target site/base address and family to prevent config retargeting collisions. Provider customer uniqueness aids recovery, not local exclusion. Never cite injected random header as dedupe. |
| 6 | Observability | Own correlation ID, operation, claim state, elapsed time and safe numeric IDs at Information; reconciliation/retry at Warning; terminal infrastructure failures Error. No declared correlation-ID property exists in in-scope typed error payloads; do not invent one. Never log raw errors, user email/names, keys or full request bodies. |
| 7 | Sensitive data | Customer body contains personal data. Explicit ILoggerFactory; SDK LogRequestBody=false, header logging=false; no per-call Trace override. No forms used, but deny-list includes first_name,last_name,email,reference and existing credential keys defensively. |
| 8 | Environment selection | SDK US/EU regions and Production/Ebb groups listed above; scope only Production. Default US region appropriate to task's provided US marker; no sandbox enum. Development/test must use only provided sandbox credentials/site; stubs use BaseUrl. Production supplies site/region through deployment config and same four mandated Maxio bindings; never hard-code Maxio:Subdomain or catalog values. |
| 9 | Duplicate prevention under concurrency | Rows below insert deterministic primary-key claims in existing CatalogContext; primary key refusal works with SQL Server and EF InMemory. Catch DbUpdateException and InMemory duplicate-key ArgumentException around claim SaveChanges, detach rejected Added entity, reload winning row; return completed or pending result, never call provider as loser. Relational primary-key constraint catches cross-process concurrency; singleton keyed lock may optimize only, never substitutes for store refusal. |
| 10 | Partial results | Products page loop has 50-page, 200/page and shared deadline caps. Response includes IsPartial and NextPage when capped. Subscribe refuses catalog validation on partial set. ListCustomerSubscriptions is unpaged by SDK contract and cannot be represented as secretly complete from a locally truncated subset. |
| 11 | Unknown outcomes | Network loss, timeout, cancellation after call, mapping failures and ambiguous 5xx keep claim Unknown. Catch re-reads matching reference before response; if not found/unavailable return 202 pending and background reconciler repeatedly re-reads durable Unknown/InFlight records. No blind POST resend, no claim deletion after ambiguous failure. Definite provider refusal releases claim only after safe reference reconciliation when relevant (e.g. duplicate customer 422). |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| CreateCustomer | CatalogContext billing-customer claim table, deterministic target-site+customer-reference primary key; states Claimed/Completed/Unknown | Store primary-key insert refusal before SDK call, including EF InMemory duplicate-key refusal | Billing claim acquisition catches DbUpdateException/ArgumentException on SaveChanges; detach loser and load prior status; no SDK write by loser | `MaxioSubscriptionBilling.TryClaimAsync` then `MaxioSubscriptionBilling.EnsureCustomer` (`client.Customers.CreateCustomer`) |
| CreateSubscription | CatalogContext billing-subscription claim table, deterministic target-site+customer-reference+product-handle primary key; stores provider reference and result | Store primary-key insert refusal before SDK call, including EF InMemory duplicate-key refusal | Billing claim acquisition catches DbUpdateException/ArgumentException on SaveChanges; detach loser and load prior status; no SDK write by loser | `MaxioSubscriptionBilling.TryClaimAsync` then `MaxioSubscriptionBilling.SubscribeAsync` (`client.Subscriptions.CreateSubscription`) |

Each row is claim → SDK call → record result. Known provider rejection releases retryable failed claim; Unknown is retained. A durable relational migration is part of this work. EF InMemory retains claims only for one process lifetime as task explicitly allows; restart recovers by deterministic provider reference before any provider create. Claim keys must use stable normalized usernames rather than seeded random identity IDs for reference recovery. One enrollment per shopper+plan is the deliberate API semantics; cancel/resubscribe and plan changes are outside scope.

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| Products.ListProducts | 50 manual pages of PerPage=200, shared 30s deadline; stop on short/empty page; reject no-progress provider pages | Plan-list response IsPartial=true and NextPage; subscribe rejects incomplete offered set before write | `MaxioSubscriptionBilling.ListPlansCore` |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| CreateCustomer | Customers.ReadCustomerByReference | Same deterministic Reference sent in customer body, validate exact match and positive ID | `MaxioSubscriptionBilling.RecoverWrite`, `ReconcileClaimAsync`, `ReadCustomer`; `ReconcilePendingAsync` uses the same lookup | `SubscriptionBillingTests.AmbiguousWriteRecoversByReferenceAndNeverResends("customer-drop")`; `UnknownClaimRetainedAndSweepOnlyReadsUntilDelayedCommitAppears(true)` |
| CreateSubscription | Subscriptions.FindSubscription | Same deterministic Reference sent in subscription body; verify owning Customer.Id and Product.Handle before adopting | `MaxioSubscriptionBilling.RecoverWrite`, `ReconcileClaimAsync`, `FindSubscription`; `ReconcilePendingAsync` uses the same lookup | `SubscriptionBillingTests.AmbiguousWriteRecoversByReferenceAndNeverResends("subscription-drop")`; `UnknownClaimRetainedAndSweepOnlyReadsUntilDelayedCommitAppears(false)` |

Reconciliation is executed by the ambiguous-write catch and background worker/startup sweep, including Claimed records abandoned by a crash. A read returning 404 after an ambiguous POST does not prove that POST will never commit; keep Unknown and retry reads. A successful read completes claim and exposes current provider result. Worker reads only; it never creates another subscription to settle an unknown. Account reads can also reconcile pending rows safely.

## Assumptions & Blockers

- No plugin capability blocker: all required catalog/customer/subscription operations and reference reconciliation exist in the shipped SDK.
- Minor product decision: one enrollment per authenticated shopper and product handle. Repeated POST returns existing subscription even if later canceled; cancellation, resubscription, migration, usage reporting and card collection are outside this additive feature.
- Minor request decision: POST requires shopper first/last name because existing ApplicationUser has neither; email always comes from resolved Identity account. The verification guide supplies these request fields.
- In-memory demo claims disappear on restart, as the task permits. Stable provider reference derives from normalized username rather than random seeded user ID; production claims persist in existing relational CatalogContext with an included migration.
- Account renaming requires retaining the original stored billing reference; use persisted claim/reference mappings before deriving a new reference. Renaming accounts is outside this feature. Bind only the four mandated Maxio keys; region may use the existing environment marker outside that options section, otherwise SDK US default.
- The SDK source is available only through the installed plugin project reference. CI/teammates must install this plugin and re-point the project reference if installed at a different location; do not vendor a copy.
- Live sandbox acceptance verified October 16, 2026: catalog discovery, JWT enforcement, existing Pro recovery, fresh Basic concurrency (one 201 and five 202), replay of the same ID, and independent second-account Pro enrollment/isolation passed. Provider responses populated active state, actual 2900/29900 USD-cent subscription prices, November 16 next billing dates and remittance collection; provider logs showed one create per fresh shopper/plan. Invoices remain payable; no payment was simulated. Malformed/missing optional provider responses remain defensive test cases rather than claims about live population. Provider rejection diagnostics used plugin typed errors and sources only.
