# Maxio Advanced Billing plan

## Scope & sequence

| Step | Work / operations |
| --- | --- |
| 1 | Register validated Maxio client from `Maxio:` configuration. |
| 2 | `ListProductFamilies`, select configured handle, then `ListProductsForProductFamily` for `GET /api/subscription-plans`. |
| 3 | Resolve/create customer by deterministic application-user reference: `ReadCustomerByReference`, `CreateCustomer`. |
| 4 | Claim `(user, plan)` before `CreateSubscription`; re-read by subscription reference after ambiguous outcomes. |
| 5 | `ListCustomerSubscriptions` for `GET /api/my-subscriptions`. |

## CONTRACT SHEET

WARNING: signatures are generated code; every operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names, never flat arguments.

WARNING: every SDK type is fully-qualified with the namespace implied by its map-named source path for THAT type, never inferred from a neighbouring type.

| Operation | Controller · method signature | Request record / body fields used | Response fields read | Error / paging | Source |
| --- | --- | --- | --- | --- | --- |
| ListProductFamilies | `ProductFamilies.ListProductFamilies(ListProductFamiliesRequest, RequestOptions?, CancellationToken)` | none | `ProductFamilyResponse.ProductFamily.Handle`, `Id` | Case B `ApiException<RawError>`; no pagination | `map/operations/ProductFamilies.md`, `Models/ProductFamily*.cs` |
| ListProductsForProductFamily | `ProductFamilies.ListProductsForProductFamily(ListProductsForProductFamilyRequest, RequestOptions?, CancellationToken)` | `ProductFamilyId: string required`, `Page: int`, `PerPage: int` | `ProductResponse.Product.Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit` | Case A `ApiException<ListProductsForProductFamilyError>`; page/per-page request pagination | `map/operations/ProductFamilies.md`, named request/model files |
| ReadCustomerByReference | `Customers.ReadCustomerByReference(ReadCustomerByReferenceRequest, RequestOptions?, CancellationToken)` | `Reference: string required` | `CustomerResponse.Customer.Id` | Case B; no pagination | `map/operations/Customers.md`, named request/model files |
| CreateCustomer | `Customers.CreateCustomer(CreateCustomerOperationRequest, RequestOptions?, CancellationToken)` | `Body: CreateCustomerRequest?`; `Customer (customer): CreateCustomer required`; `FirstName`, `LastName`, `Email` required; `Reference` optional | `CustomerResponse.Customer.Id` | Case A `ApiException<CreateCustomerError>`; `TryGetCustomerErrorResponse1` 422; no pagination | `map/operations/Customers.md`, named request/model files |
| CreateSubscription | `Subscriptions.CreateSubscription(CreateSubscriptionOperationRequest, RequestOptions?, CancellationToken)` | `Body: CreateSubscriptionRequest?`; `Subscription (subscription): CreateSubscription required`; `ProductHandle`, `CustomerId`, `Reference`, `PaymentCollectionMethod (payment_collection_method)` optional; application selects `CollectionMethod.Invoice` | `SubscriptionResponse.Subscription.Id`, `State`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `Product`, `Reference` | Case A `ApiException<CreateSubscriptionError>`; `TryGetErrorListResponse1` 422; no pagination | `map/operations/Subscriptions.md`, `Models/CreateSubscription.cs`, `Models/Enums/CollectionMethod.cs` |
| FindSubscription | `Subscriptions.FindSubscription(FindSubscriptionRequest, RequestOptions?, CancellationToken)` | `Reference: string` | same subscription fields | Case A `ApiException<FindSubscriptionError>`; `TryGetNoContent` 404; no pagination | `map/operations/Subscriptions.md` |
| ListCustomerSubscriptions | `Customers.ListCustomerSubscriptions(ListCustomerSubscriptionsRequest, RequestOptions?, CancellationToken)` | `CustomerId: int required` | `SubscriptionResponse.Subscription` fields above | Case B; no pagination | `map/operations/Customers.md` |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| Requested plan handle must be a plan returned under the configured product family | `CreateSubscription` ← `ListProductsForProductFamily` | subscription service validates selected handle from the family list before the write |

## Trap notes

| Step | Hazard |
| --- | --- |
| 1 | HTTP client ownership, DI lifetime, and options construction affect socket use and credential rotation. **MUST load maxio-advanced-billing:dotnet-client-initialization** |
| 1 | Basic-auth fields are scheme-specific; a missing segment silently changes request authentication. **MUST load maxio-advanced-billing:dotnet-authentication** |
| 2-5 | Operation calls use request records and may have response envelopes/cancellation traps. **MUST load maxio-advanced-billing:dotnet-calling-endpoints** |
| 2-5 | Request and response model nullability, enum conversion, and JSON shape differ from familiar DTOs. **MUST load maxio-advanced-billing:dotnet-models** |
| 2-5 | Typed, raw, connection, and malformed-response failures require a complete error boundary. **MUST load maxio-advanced-billing:dotnet-error-handling** |
| 1-5 | Timeout/retry semantics, server overrides, and logging hooks have non-obvious scope. **MUST load maxio-advanced-billing:dotnet-configuration-resilience** |
| tests | SDK client seams and failure simulations are not ordinary HTTP mocks. **MUST load maxio-advanced-billing:dotnet-testing** |

## REQUIRED READING

- maxio-advanced-billing:dotnet-client-initialization · DI registration
- maxio-advanced-billing:dotnet-authentication · credential binding
- maxio-advanced-billing:dotnet-calling-endpoints · operation invocation
- maxio-advanced-billing:dotnet-models · SDK models
- maxio-advanced-billing:dotnet-error-handling · translation boundary
- maxio-advanced-billing:dotnet-configuration-resilience · transport setup
- maxio-advanced-billing:dotnet-testing · tests
- A body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Validate nonblank `Maxio:ApiKey`, `Maxio:Subdomain`, and `Maxio:ProductFamilyHandle` at host start. |
| 2 | Secret sourcing & rotation | User secrets hold values populated from environment; client options are built once in singleton registration, so rotation requires restart. |
| 3 | Total timeout budget | A 30-second linked cancellation budget is enforced by the service; SDK retry options are configured after resilience reading. |
| 4 | Write-retry ownership | Customer/subscription writes are not automatically retried; reconciliation owns ambiguous results. |
| 5 | Idempotency & ambiguous writes | SDK request records expose no real write key used here; local primary-key claim plus deterministic Maxio `reference` supports reconciliation. |
| 6 | Observability | Log operation, application user id, plan handle, Maxio status and provider correlation/error text at warning/error; no request body or secret. |
| 7 | Sensitive data | Customer email/name is personal data; request-body logging stays disabled and SDK logger factory is explicit. |
| 8 | Environment selection | Scope uses `Production` group only; sandbox traffic uses configured sandbox subdomain/base override, US unless `MAXIO_ENVIRONMENT` selects EU. Ebb is untouched. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | Product-family products are fetched in bounded pages and expose `isTruncated` if cap reached. Customer subscriptions SDK call is non-paged. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where caught | Where in the code |
| --- | --- | --- | --- | --- |
| CreateCustomer | PublicApi subscription claim table, deterministic customer-reference key | primary-key conflict | `DbUpdateException` (relational) / `ArgumentException` (in-memory) | `MaxioSubscriptionService.EnsureCustomerAsync` claim write, then `Customers.CreateCustomer` |
| CreateSubscription | PublicApi subscription claim table, deterministic user/plan key | primary-key conflict | `DbUpdateException` (relational) / `ArgumentException` (in-memory) | `MaxioSubscriptionService.SubscribeAsync` claim write, then `Subscriptions.CreateSubscription` |

### PAGED READS

| Read | What caps it | How caller learns it was cut short | Where in code |
| --- | --- | --- | --- |
| ListProductsForProductFamily | 200 items/page, 1,000 total | `SubscriptionPlansResponse.IsTruncated` | `MaxioSubscriptionService.GetPlansAsync` |

### UNKNOWN OUTCOMES

| Write | Operation re-read | Reference searched | Where in code | Test that fails connection |
| --- | --- | --- | --- | --- |
| CreateCustomer | `ReadCustomerByReference` | deterministic user reference | `MaxioSubscriptionService.EnsureCustomerAsync` connection/deserialization catches | SDK `HttpMessageHandler` connection-failure test (to be added) |
| CreateSubscription | `FindSubscription` | deterministic user/plan reference | `MaxioSubscriptionService.SubscribeAsync` connection/deserialization catches | SDK `HttpMessageHandler` connection-failure test (to be added) |

## Assumptions & Blockers

Minor assumption: PublicApi's existing EF store is the durable application-owned claim store in deployed environments; in-memory development persistence is intentionally process-scoped as stated in the task. No blocker.

## Source labels

SDK facts: plugin-relative `sdk/dotnet/sdk-map.md`, `map/operations/Customers.md`, `ProductFamilies.md`, and `Subscriptions.md`, plus each map-named source file. Application persistence/HTTP shape: YOUR CALL — not in the map.
