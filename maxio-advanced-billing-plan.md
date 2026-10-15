# Maxio Advanced Billing plan

## 1. Scope & sequence

| Step | Work / Maxio operations |
| --- | --- |
| 1 | Add `Maxio` validated settings and a singleton SDK client. |
| 2 | Add an Identity-db backed subscription request claim and mapping. |
| 3 | List plans with `ProductFamilies.ListProductsForProductFamily` using the configured handle in the documented `handle:` form. |
| 4 | Subscribe: `Customers.ReadCustomerByReference` / `Customers.CreateCustomer`, `Products.ReadProductByHandle`, `Subscriptions.CreateSubscription`; reconcile with `Subscriptions.FindSubscription`. |
| 5 | List account subscriptions with `Customers.ListCustomerSubscriptions`. |

## 2. CONTRACT SHEET

**Generated-code warning:** every operation with input takes one request record as its first parameter; construct it with an object initializer using its own property names, never flat arguments.

**Namespace warning:** every SDK type is fully qualified by the namespace implied by its map-named source path, never inferred from a neighbouring type.

| Operation | Controller / signature | Request + body | Response fields read | Error / paging | Source |
| --- | --- | --- | --- | --- | --- |
| Read customer by reference | `Customers.ReadCustomerByReference(ReadCustomerByReferenceRequest, RequestOptions?, CancellationToken)` | `Reference: string, required` | `CustomerResponse.Customer.Id` | Case B `ApiException<RawError>`; none | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs`; `Models/CustomerResponse.cs`; `Models/Customer.cs` |
| Create customer | `Customers.CreateCustomer(CreateCustomerOperationRequest, RequestOptions?, CancellationToken)` | `Body: CreateCustomerRequest?`; `Customer (customer): CreateCustomer, required`; `FirstName`, `LastName`, `Email` required; `Reference` optional | `CustomerResponse.Customer.Id` | Case A `ApiException<CreateCustomerError>`: `TryGetCustomerErrorResponse1` 422 / raw fallback; none | `map/operations/Customers.md`; `Requests/Customers/CreateCustomerOperationRequest.cs`; `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs` |
| List plans | `ProductFamilies.ListProductsForProductFamily(ListProductsForProductFamilyRequest, RequestOptions?, CancellationToken)` | `ProductFamilyId: string, required`; `Page: int`, `PerPage: int` | `IReadOnlyList<ProductResponse>` and `Product.{Handle,Name,Description,PriceInCents,Interval,IntervalUnit}` | Case A `ApiException<ListProductsForProductFamilyError>`; page through until a short page | `map/operations/ProductFamilies.md`; `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/ProductResponse.cs`; `Models/Product.cs` |
| Create subscription | `Subscriptions.CreateSubscription(CreateSubscriptionOperationRequest, RequestOptions?, CancellationToken)` | `Body: CreateSubscriptionRequest?`; `Subscription (subscription): CreateSubscription, required`; `ProductHandle`, `CustomerId`, `Reference` optional | `SubscriptionResponse.Subscription.{Id,ProductHandle,State,CurrentPeriodEndsAt,Reference}` | Case A `ApiException<CreateSubscriptionError>`: `TryGetErrorListResponse1` 422 / raw fallback; none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/CreateSubscriptionOperationRequest.cs`; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs`; `Models/SubscriptionResponse.cs`; `Models/Subscription.cs` |
| Find subscription | `Subscriptions.FindSubscription(FindSubscriptionRequest, RequestOptions?, CancellationToken)` | `Reference: string?` | `SubscriptionResponse.Subscription` fields above | Case A `ApiException<FindSubscriptionError>`: `TryGetNoContent` 404 / raw fallback; none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/FindSubscriptionRequest.cs`; `Models/SubscriptionResponse.cs`; `Models/Subscription.cs` |
| List customer subscriptions | `Customers.ListCustomerSubscriptions(ListCustomerSubscriptionsRequest, RequestOptions?, CancellationToken)` | `CustomerId: int, required` | each `SubscriptionResponse.Subscription` fields above | Case B `ApiException<RawError>`; none | `map/operations/Customers.md`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs`; `Models/SubscriptionResponse.cs`; `Models/Subscription.cs` |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| A supplied plan handle must be one returned from the configured product family. | `CreateSubscription` ← `ListProductsForProductFamily` | `SubscriptionService.GetPlansAsync` before `CreateSubscription` |
| A subscription’s customer id must be the customer returned by the user’s stable customer-reference lookup/create. | `CreateSubscription` ← `ReadCustomerByReference` / `CreateCustomer` | `SubscriptionService.EnsureCustomerAsync` |

## 3. Trap notes

| Step | Hazard |
| --- | --- |
| 1 | SDK clients retain their HTTP client/options and server overrides live below the server group/environment hierarchy. **MUST load `maxio-advanced-billing:dotnet-client-initialization` and `maxio-advanced-billing:dotnet-configuration-resilience`.** |
| 1 | HTTP Basic requires both API key and `x`, and an unset scheme silently sends no credential. **MUST load `maxio-advanced-billing:dotnet-authentication`.** |
| 3–5 | Calls use request records and response envelopes rather than flat arguments. **MUST load `maxio-advanced-billing:dotnet-calling-endpoints` and `maxio-advanced-billing:dotnet-models`.** |
| 4–5 | Typed provider failures, raw failures, and response deserialization failures need one deliberate boundary. **MUST load `maxio-advanced-billing:dotnet-error-handling`.** |
| 6 | SDK tests must stub at its supported HTTP seam, including retry/error paths. **MUST load `maxio-advanced-billing:dotnet-testing`.** |

## 4. REQUIRED READING

- `maxio-advanced-billing:dotnet-client-initialization` — client/DI setup.
- `maxio-advanced-billing:dotnet-authentication` — Basic credentials and fail-fast startup.
- `maxio-advanced-billing:dotnet-calling-endpoints` — operation calls.
- `maxio-advanced-billing:dotnet-models` — request and response models.
- `maxio-advanced-billing:dotnet-error-handling` — Maxio exception boundary.
- `maxio-advanced-billing:dotnet-configuration-resilience` — timeout, retry, base URL and logging.
- `maxio-advanced-billing:dotnet-testing` — SDK test seam.
- A body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind `Maxio` and validate nonblank `ApiKey`, `Subdomain`, and `ProductFamilyHandle` on host start. |
| 2 | Secret sourcing & rotation | User-secrets receive environment values; singleton captures options at registration, so rotation requires restart. |
| 3 | Total timeout budget | 30 seconds is enforced by the named Maxio `HttpClient`; SDK retries are disabled for writes and bounded for reads. |
| 4 | Write-retry ownership | No SDK retries for POST customer/subscription writes; provider reconciliation owns ambiguous outcomes. |
| 5 | Idempotency & ambiguous writes | Customer creation uses unique stable provider reference; subscription uses a stable local claim plus stable provider reference and `FindSubscription` reconciliation. |
| 6 | Observability | Log operation, local correlation/reference and provider HTTP status at warning/error; never log credential or request body. |
| 7 | Sensitive data | Scope sends name/email only; SDK body logging is disabled and an explicit logger factory is configured. |
| 8 | Environment selection | All scope operations use Production server group; sandbox is selected by configured sandbox subdomain / optional `BaseUrl`. `Us` is default and `Eu` is selected from `Maxio:Environment` when supplied. Ebb is untouched. |
| 9 | Duplicate prevention under concurrency | Identity DB primary-key claim is written before provider subscription creation. |
| 10 | Partial results | Product-family plans are exhausted through pages until the returned page is shorter than 200; customer subscriptions are unpaged. |
| 11 | Unknown outcomes | Customer create re-reads customer reference; subscription create finds stable subscription reference before returning. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Create subscription | `AppIdentityDbContext.SubscriptionEnrollments`, PK `(UserId, ProductHandle)` | EF duplicate key save failure | `SubscriptionService.SubscribeAsync` | `SubscriptionService.SubscribeAsync` claim save → `Subscriptions.CreateSubscription` |

### PAGED READS

| List products for product family | loops until a page contains fewer than 200 results | full `IReadOnlyList<SubscriptionPlanDto>` only after exhaustion | `SubscriptionService.GetPlansAsync` |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| Create customer | `ReadCustomerByReference` | stable `eshop:{userId}` customer reference | `SubscriptionService.EnsureCustomerAsync` `SdkConnectionException` catch | integration verification exercises normal response; SDK handler seam is documented for disconnect injection |
| Create subscription | `FindSubscription` | stable local request reference | `SubscriptionService.SubscribeAsync` `SdkConnectionException` catch | integration verification exercises normal response; SDK handler seam is documented for disconnect injection |

## 6. Assumptions & Blockers

- The configured product-family handle is present on each returned plan as `Product.ProductFamily.Handle`; plans are rejected if it differs.
- User profile supplies an email; when display name is unavailable, the email prefix is used as first name and `Customer` as last name.
- The local Identity database is the durable claim store. In-memory development state intentionally resets on host restart as stated in the task.

## 7. Source labels

SDK facts above are sourced from `sdk-map.md`, the listed operation pages and listed SDK source files under the plugin-relative `sdk/dotnet/`; local concurrency/persistence/API decisions are **YOUR CALL — not in the map**.
