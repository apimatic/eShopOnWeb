# Maxio subscription plan — PublicApi

## 1. Scope & sequence

| Step | Work | Maxio operation(s) |
|---|---|---|
| 1 | Add the published `AsadAli.AdvancedBilling.Sdk` package and a single DI-owned Maxio client. Bind only `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`; no catalog IDs or credential values are code/config-file constants. Initialize `MaxioAdvancedBillingClientOptions.BasicAuth` with `BasicAuthCredentials.Username` from `Maxio:ApiKey` and `Password = "x"`; use `ServerEnvironment.Us`, `options.Server.Production.Us.Site` from `Maxio:Subdomain`, and when non-empty `options.Server.Production.Us.BaseUrl` verbatim. Register a write-only `DelegatingHandler`/per-call send scope which permits exactly one outbound Maxio `POST`; the SDK cannot disable transport-failure retries with `RetryOptions`, and its retry pipeline would otherwise re-send a create. | client construction only |
| 2 | `GET /api/subscription-plans`: list product families, match `ProductFamily.Handle` exactly to `Maxio:ProductFamilyHandle`, use its returned numeric `Id` (as the required string path argument), then page through that family's non-archived products. Return plan handle, name, description, `PriceInCents`, interval, and interval unit. Do not use supplied/stale numeric IDs. | `ListProductFamilies`, `ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions`: authenticate from the PublicApi JWT identity; load the application user to supply Maxio's required first name, last name, and email. Use a stable application-owned customer reference derived from the authenticated user ID; look it up, create only when absent, and on a concurrent 422 create race re-read by reference. | `ReadCustomerByReference`, `CreateCustomer` |
| 4 | Validate the requested plan handle against the live family result from Step 2. Use an application idempotency record with a database unique key for `(userId, planHandle)` and a deterministic, application-owned subscription `Reference`. The request that owns the claim first calls `FindSubscription(reference)`; only its 404 path may issue `CreateSubscription`. Persist the returned Maxio subscription ID/reference before reporting success. On an uncertain write outcome or a duplicate-key race, read by the same reference and return the existing subscription if present; otherwise retain/return a retryable processing failure rather than sending a second create. This is the application concurrency rule that makes double-clicks converge without assuming an undocumented Maxio uniqueness guarantee for subscription references. | `FindSubscription`, `CreateSubscription` |
| 5 | `GET /api/my-subscriptions`: resolve the authenticated caller's Maxio customer by its stable reference, then list that customer's subscriptions from Maxio. Map each response's inner subscription and product to ID/reference, plan name/handle, actual `ProductPriceInCents`, `State`, and `NextAssessmentAt` (the response field representing the next billing assessment date). | `ReadCustomerByReference`, `ListCustomerSubscriptions` |
| 6 | Put one integration boundary around all SDK calls. Translate known rejection/not-found conditions from the documented typed/raw payload path; never expose a provider exception, credential, or raw unbounded error body. Build unit/integration tests for catalog discovery, customer race recovery, subscription idempotency, Maxio rejection, and response mapping. | all above |

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

| Controller property · operation | Verbatim method signature | Request model / fields used (C# name (wire): type, required?) | Response envelope / fields read | Error case | Pagination | Source |
|---|---|---|---|---|---|---|
| `MaxioAdvancedBilling.Api.ProductFamilies` · `ListProductFamilies` | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all five nullable args have no C# default and must be passed (normally `null`). | none | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; each `ProductFamily (product_family): ProductFamily?`; match `ProductFamily.Handle`, read `ProductFamily.Id`. | B — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. | none | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Api.ProductFamilies` · `ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.Enums.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — nullable parameters through `include` have no C# default and must be passed. | no body; send the dynamically resolved family ID as `productFamilyId`; set `includeArchived` to `false`. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; each `Product (product): Product !req`; read `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `ArchivedAt`, `RequireCreditCard`. | A — `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404], then `TryGetRawError(out RawError)`. | manual `page` + `perPage`; exhaust pages before presenting plans. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Api.Customers` · `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | no body; query `reference` is the stable application customer reference. | `MaxioAdvancedBilling.Models.CustomerResponse` → `Customer (customer): Customer !req`; read `Customer.Id`, `Reference`. | B — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; inspect `StatusCode` for 404 and use documented raw accessors only as needed. | none | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Api.Customers` · `CreateCustomer` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable but no C# default; must be passed. | `CreateCustomerRequest.Customer (customer): CreateCustomer !req`; `CreateCustomer.FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`. Leave other fields absent unless the app has a deliberate value. The operation notes say a supplied `reference` may have only one customer, so use it for the caller's stable ID. | `CustomerResponse.Customer` → `Customer.Id`, `Reference`. | A — `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], then `TryGetRawError(out RawError)`. | none | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Api.Subscriptions` · `FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — nullable argument has no C# default; must be passed. | no body; query `reference` is the deterministic application subscription reference. | `MaxioAdvancedBilling.Models.SubscriptionResponse` → `Subscription (subscription): Subscription?`; read `Id`, `Reference`, `State`, `ProductPriceInCents`, `NextAssessmentAt`, `Product`. | A — `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [404], then `TryGetRawError(out RawError)`. | none | `operations/Subscriptions.md`; `records-4-Su-We.md`; `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Api.Subscriptions` · `CreateSubscription` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable but no C# default; must be passed. | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription !req`; set only `CreateSubscription.ProductHandle (product_handle): string?`, `CustomerReference (customer_reference): string?`, and application deterministic `Reference (reference): string?`. Do not set `Ref (ref)` — its source comment identifies it as a referral code. Do not send payment fields, components, pricing, or billing-date overrides for this seeded no-payment-required plan flow. The operation notes tie acceptance to one of `product_id`/`product_handle` and one of `customer_id`/`customer_reference`/`customer_attributes`; this plan selects the handle/reference path. | `SubscriptionResponse.Subscription` → `Id`, `Reference`, `State`, `ProductPriceInCents`, `NextAssessmentAt`, and `Product (product): Product?` → `Name`, `Handle`. | A — `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], then `TryGetRawError(out RawError)`. | none | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |
| `MaxioAdvancedBilling.Api.Customers` · `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | no body. | `IReadOnlyList<SubscriptionResponse>`; for each non-null `Subscription`, map `Id`, `Reference`, `State`, `ProductPriceInCents`, `NextAssessmentAt`, and nested `Product.Name`/`Product.Handle`. | B — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. | none | `operations/Customers.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |

### Enum values used

| Fully-qualified type | Literal members (wire values) | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day (day)`, `Month (month)` | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `models/enums.md` |

### Client construction, auth, and server facts

| Fact | Contract | Source |
|---|---|---|
| Package / client | Package `AsadAli.AdvancedBilling.Sdk`; exact construction: `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(httpClient, options)`, where `httpClient` is `System.Net.Http.HttpClient` and `options` is `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`. Controllers are properties on the client. | `sdk-map.md` |
| Auth | Exact initializer property chain: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = /* Maxio:ApiKey */, Password = "x" };`. `BasicAuth` is `BasicAuthCredentials?`; `Username` and `Password` are the credential properties. | `sdk-map.md` |
| Server | Exact initializer property chain: `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us; options.Server.Production.Us.Site = /* Maxio:Subdomain */;` and, only for the optional override, `options.Server.Production.Us.BaseUrl = /* Maxio:BaseUrl verbatim */;`. Production US template is `https://{site}.chargify.com`. | `sdk-map.md` |
| Retry / POST writes | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions.Retry` is `MaxioAdvancedBilling.Core.Configuration.RetryOptions`, with `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, and `OnRetry`. There is no no-retry setting: `MaxRetries = 0` is rejected and `HttpMethodsToRetry` gates only status retries. Even when `POST` is absent from `HttpMethodsToRetry`, transport `HttpRequestException` retries can re-send it. Use the Step-1 one-send handler plus provider-state reconciliation; do not claim `RetryOptions` can disable it. | `sdk-map.md`; `dotnet-configuration-resilience` |
| Configuration boundary | Read only `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`; validation must reject missing/blank required values at startup without logging their values. | YOUR CALL — not in the map |
| Caller identity / customer reference | Resolve caller/user fields from PublicApi's existing JWT identity path. Generate an opaque stable reference from the application user ID; do not use an email as the identifier. | YOUR CALL — not in the map |

## 3. Trap notes

⚠ Step 1 (client registration) — client lifetime, `HttpClient` ownership, and the DI registration seam can otherwise produce socket/handler churn. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (authentication) — credential construction and rotation must not disclose the API key or use the wrong Basic-auth manager shape. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Steps 2–5 (endpoint calls) — these list methods have nullable parameters with no C# defaults, so positional calls can bind a filter into the wrong slot. **MUST load `dotnet-calling-endpoints`** before writing calls.

⚠ Steps 3–5 (models) — required envelope members, string-enum representation, nullable response envelopes, and exact JSON wire names make direct DTO mapping error-prone. **MUST load `dotnet-models`** before building or reading SDK models.

⚠ Step 4 (idempotent write) — transport failure retry behavior and retry/timeout configuration can re-send a write or leave its outcome uncertain; the application claim/reconciliation path must remain authoritative. **MUST load `dotnet-configuration-resilience`** before registering or tuning the client.

⚠ Step 6 (error boundary) — Case-A typed exceptions and Case-B raw exceptions expose different safe error-body paths; a catch ladder using the wrong accessor loses the rejection classification. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary; **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 (tests) — SDK tests need the HTTP seam rather than a fake generated controller so tests do not couple to SDK internals. **MUST load `dotnet-testing`** before writing tests.

## 4. REQUIRED READING

Load all of these **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governing step |
|---|---|
| `dotnet-client-initialization` | Step 1 — client / DI registration |
| `dotnet-authentication` | Step 1 — Basic credentials |
| `dotnet-calling-endpoints` | Steps 2–5 — method invocation and pagination |
| `dotnet-models` | Steps 3–5 — request/response construction and mapping |
| `dotnet-configuration-resilience` | Steps 1 and 4 — server override, timeout, retry, write reconciliation |
| `dotnet-error-handling` | Step 6 — typed/raw/JSON exception boundary |
| `dotnet-testing` | Step 6 — HTTP test seam |

## 5. Assumptions & Blockers

- Assumption: the application has (or will add) a durable database table and unique constraint for the subscription idempotency claim. This is required for multi-request/process double-click safety; its schema and lifecycle are application design, not an SDK fact.
- Assumption: the PublicApi authenticated-user lookup can supply non-empty first name, last name, and email, because `CreateCustomer` requires all three. If any is unavailable, reject the subscribe request before calling Maxio rather than fabricate identity data.
- Assumption: `Maxio:BaseUrl` is blank for normal sandbox use; when supplied it is a complete Production API base URL and is used verbatim as mandated. No numeric product or product-family IDs are assumed.
- No Maxio SDK contract blocker.
