# Maxio Subscription Billing Integration — eShopOnWeb

## Scope & Sequence

1. **SDK initialization & DI setup** — register `MaxioAdvancedBillingClient` with HTTP/auth
2. **Configuration binding** — load API key, subdomain, product family handle from `Maxio:` config section
3. **API endpoints** — implement three PublicApi controller actions with JWT auth
4. **Plans query** — list available products for the configured product family
5. **Customer idempotency** — query for existing customer by reference (eShopOnWeb user ID); create if missing
6. **Subscription creation** — create subscription with product/customer; handle duplicate references
7. **Subscription listing** — return customer's active subscriptions with state and billing info
8. **Error boundary** — catch SDK exceptions, map to application responses (422 validation, 404 not found, 500 outage)

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `MaxioAdvancedBilling.Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation | Signature & Required Params | Request Body + Fields | Response Envelope + Fields | Error Case | Notes | Source |
|---|---|---|---|---|---|---|
| **ListProductsForProductFamily** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — must pass: `productFamilyId` (string), all optional params explicitly (pass `null` to skip) | N/A (GET only) | `IReadOnlyList<ProductResponse>` — each item wraps `Product` with fields: `Id (int)`, `Name (string)`, `Handle (string)`, `PriceInCents (long)`, `Interval (int)`, `IntervalUnit (IntervalUnit)`, `TrialPriceInCents (long?)`, `TrialInterval (int?)`, `ProductFamily (ProductFamily?)` with `Handle (string)` | **Case A (typed)**: `SdkException<ListProductsForProductFamilyError>` with `TryGetString(out string)` [404], `TryGetRawError(out RawError)` [fallback] | 404 → product family not found; pass product family handle (e.g. `"eshop-subscribe"`) as `productFamilyId`; pagination: manual `page`+`perPage` (default 20 per page) | `operations/ProductFamilies.md` |
| **ReadCustomerByReference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — must pass: `reference` (string, wire param: `reference`) | N/A (GET only) | `CustomerResponse` wraps `Customer` with fields: `Id (int)`, `FirstName (string)`, `LastName (string)`, `Email (string)`, `Reference (string)`, `CreatedAt (DateTimeOffset)`, `UpdatedAt (DateTimeOffset)` | **Case B (raw)**: `SdkException<RawError>` with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | 404 → customer reference not found (use as "does not exist" check); no paging | `operations/Customers.md` |
| **CreateCustomer** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — must pass: `body` explicitly | `CreateCustomerRequest` wraps `Customer` record with: `FirstName (string) !req`, `LastName (string) !req`, `Email (string) !req`, `Reference (string)?` (wire: `reference`), `Organization (string)?`, `Address (string)?`, `City (string)?`, `State (string)?`, `Zip (string)?`, `Country (string)?`, `Phone (string)?`, `Locale (string)?` | `CustomerResponse` wraps `Customer` with: `Id (int)`, `FirstName (string)`, `LastName (string)`, `Email (string)`, `Reference (string)`, `CreatedAt (DateTimeOffset)`, `UpdatedAt (DateTimeOffset)` | **Case A (typed)**: `SdkException<CreateCustomerError>` with `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] — 422 payload has `Errors` (Errors union) with `PerPage` and `PricePoint` lists | 422 → validation error (reference already used, email invalid, etc.); use `Reference` field for idempotent link to eShopOnWeb user; **no payment profile required** on this endpoint | `operations/Customers.md` |
| **CreateSubscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — must pass: `body` explicitly | `CreateSubscriptionRequest` wraps `CreateSubscription` record with: `CustomerId (int)?` OR `CustomerReference (string)?` (wire: `customer_reference`), `ProductHandle (string)?` OR `ProductId (int)?`, `Reference (string)?` (wire: `reference`, for subscription reference), `PaymentCollectionMethod (CollectionMethod)?` (enum: `Automatic`, `Remittance`; optional — default per product), optional fields listed in Notes below | `SubscriptionResponse` wraps `Subscription` with fields: `Id (int)`, `State (SubscriptionState)` enum (values: `Active`, `Pending`, `Paused`, `Canceled`, `Expired`, `TrialEnded`, `Awaiting_Signup`, `Past_Due`, `Dunning`, `Trial`), `ProductId (int)`, `CustomerId (int)`, `CurrentPeriodStartedAt (DateTimeOffset?)`, `CurrentPeriodEndsAt (DateTimeOffset?)`, `NextAssessmentAt (DateTimeOffset?)` ← **next billing date**, `ActivatedAt (DateTimeOffset?)`, `CanceledAt (DateTimeOffset?)`, `CreatedAt (DateTimeOffset)`, `UpdatedAt (DateTimeOffset)` | **Case A (typed)**: `SdkException<CreateSubscriptionError>` with `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] — 422 payload has `Errors` (list of strings) | 422 → validation error (customer not found, product not found, reference already used); use `Reference` field (subscription reference) for idempotent re-subscribe check; **no payment method required** (per spec: "no payment method required"); `PaymentCollectionMethod` typically `Automatic` for recurring | `operations/Subscriptions.md` |
| **ListCustomerSubscriptions** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — must pass: `customerId` (int) | N/A (GET only) | `IReadOnlyList<SubscriptionResponse>` — each wraps `Subscription` with fields: `Id (int)`, `State (SubscriptionState)`, `ProductId (int)`, `ProductHandle (string?)`, `CustomerId (int)`, `CurrentPeriodEndsAt (DateTimeOffset?)`, `NextAssessmentAt (DateTimeOffset?)` ← next billing, `ActivatedAt (DateTimeOffset?)`, `CanceledAt (DateTimeOffset?)`, `CreatedAt (DateTimeOffset)`, `UpdatedAt (DateTimeOffset)`, `Reference (string?)` | **Case B (raw)**: `SdkException<RawError>` with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | 404 → customer not found; no paging built-in (returns all) | `operations/Customers.md` |
| **ListSubscriptions** (alt) | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — must pass all optional params explicitly; defaults: `page=1`, `perPage=20` | N/A (GET only) | `IReadOnlyList<SubscriptionResponse>` — same fields as ListCustomerSubscriptions above | **Case B (raw)**: `SdkException<RawError>` | alternative: filter by product ID + customer ID via metadata or search; manual paging; **use ListCustomerSubscriptions if customer ID is known** — simpler, no metadata filter overhead | `operations/Subscriptions.md` |

### Enum values (from `map/models/enums.md`)

**`SubscriptionState`** — C# members: `Active`, `Awaiting_Signup`, `Canceled`, `Dunning`, `Expired`, `Past_Due`, `Paused`, `Pending`, `Trial`, `TrialEnded`

**`CollectionMethod`** — C# members: `Automatic`, `Remittance` (wire: `automatic`, `remittance`)

**`IntervalUnit`** — C# members: `Day`, `Month`, `Year` (wire: `day`, `month`, `year`)

### Namespaces (using directives)

- **Client & auth**: `using MaxioAdvancedBilling;` `using MaxioAdvancedBilling.Core.Authentication.Basic;` `using MaxioAdvancedBilling.Servers;`
- **Operations (controller accessors)**: `using MaxioAdvancedBilling.Api;` — NOT typically needed in client code; the client exposes properties directly
- **Request/response models**: `using MaxioAdvancedBilling.Models;`
- **Enums**: `using MaxioAdvancedBilling.Models.Enums;`
- **Error types**: `using MaxioAdvancedBilling.Errors;`
- **Core (auth, retry, config)**: `using MaxioAdvancedBilling.Core.Authentication.Basic;` `using MaxioAdvancedBilling.Core.Configuration;`

### Operations fields & Notes

**ListProductsForProductFamily** — querying a product family by handle (e.g. `"eshop-subscribe"`); passing all 8 optional filter params explicitly as `null` is required (no defaults); returns `Product` records with `PriceInCents` (in cents, divide by 100 for USD display), `Interval` + `IntervalUnit` (billing period), no trial prices for this flow (plans have `TrialPriceInCents: null`), no setup fees.

**ReadCustomerByReference** → lookup by user ID (eShopOnWeb `userId`); 404 signals no customer exists yet (not an error—normal flow to "create if missing").

**CreateCustomer** → set `Reference` to eShopOnWeb user ID; `FirstName`, `LastName`, `Email` are required; `CreatedAt` in response is Maxio's timestamp (not eShopOnWeb signup time).

**CreateSubscription** → **pass either `CustomerId` (int) or `CustomerReference` (string, wire: `customer_reference`)**; **pass either `ProductHandle` (string) or `ProductId` (int)**; `Reference` (subscription reference, optional) can be used to idempotently link to eShopOnWeb's subscription record (detect duplicate by this field); `NextAssessmentAt` is the next billing date to show the user; **no `PaymentProfileId` or `CreditCardAttributes` required** (payment method not collected at signup). **Notes from map**: no trial, no setup fee, no payment method required — these are enforced on the Maxio product definition, not the SDK call.

**ListCustomerSubscriptions** → queries by Maxio customer ID (from earlier `CreateCustomer` response); returns only that customer's subs.

---

## Trap Notes

⚠ **Step 1 (client init & DI)** — The SDK client wraps a long-lived `HttpClient` which must be registered via `IHttpClientFactory` and reused, **not** rebuilt per request. The `MaxioAdvancedBillingClient` itself may be transient, but the underlying `HttpClient` is the expensive resource. **MUST load `dotnet-client-initialization`** before writing the client construction and DI code.

⚠ **Step 2 (auth)** — Maxio uses HTTP Basic authentication. The username is the API key; the password is the literal string `"x"`. Load credentials from configuration (e.g. `config["Maxio:ApiKey"]`), **never hardcode**. **MUST load `dotnet-authentication`** before setting `BasicAuthCredentials`.

⚠ **Step 3 (calling endpoints)** — **Do not use positional arguments for optional parameters.** Many SDK operations have 8+ optional query params with no C# defaults — a positional call will silently bind wrong parameters. Use named arguments (`productFamilyId: id, dateField: null, filter: null, …`) or build request objects. **MUST load `dotnet-calling-endpoints`** before the first `client.Customers.CreateCustomer()` call.

⚠ **Step 4 (models)** — Request/response fields are immutable records with `init`-only setters; `required` fields must be set in the object initializer. `CollectionMethod` and `SubscriptionState` are **not** C# enums — they are `StringEnum<T>` types; construct with `StringEnum<CollectionMethod>.FromValue("automatic")` or use static members (e.g. `CollectionMethod.Automatic`). Field names in wire names use snake_case (`customer_reference`); the C# property is PascalCase (`CustomerReference`). **MUST load `dotnet-models`** before building a request.

⚠ **Step 5 (error handling — drifted/malformed 2xx body)** — A missing `required` member in a 2xx response (e.g., `Subscription.Id` omitted) causes deserialization to throw `System.Text.Json.JsonException`, **not** an `SdkException`. If the error boundary catches only `SdkException<…>`, the `JsonException` escapes and the integration returns 500 instead of a deterministic error. This is **not** an operation-level error — it is a contract violation on the provider's side. Map it to a logged 5xx and alert ops.

⚠ **Step 5 (error handling — malformed non-2xx body)** — A 422 response whose body does **not** match the operation's generated `CreateSubscriptionError` shape throws `JsonException` **while constructing the error object** — the exception replaces the `SdkException` and the HTTP status (422) is lost. The boundary then can't distinguish between a provider outage (500) and a validation error (422). **If this happens, the call is retried forever.** Map every `JsonException` to a logged alert and use the status from the original wire (if available); if not, treat as unknown error. **MUST load `dotnet-error-handling`** and implement the boundary **before** wiring any error handler.

⚠ **Step 6 (config & resilience)** — `Timeout` in `RetryOptions` is **per-attempt**, not total wall-clock time. A `POST /subscriptions` that times out on attempt 1 **is retried** on attempt 2 (because HTTP POST is in `HttpMethodsToRetry` by default if the error is a **transport failure**; a 503 on POST is **not** retried because 503 is not in the default `StatusCodesToRetry`). **`MaxRetries = 0` is rejected at construction — the floor is 1.** The base URL is `https://{subdomain}.chargify.com` (US) by default; set `options.Server.Production.Us.Site = "cp-exp-1"` to override the subdomain. **MUST load `dotnet-configuration-resilience`** before tuning retry/timeout or overriding the base URL.

⚠ **Always include these two rows in every integration's first plan sheet:**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`**.

- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`**.

---

## REQUIRED READING

Load **before implementation starts**:

| Skill | Governs |
|-------|---------|
| `dotnet-client-initialization` | Step 1 — client setup, DI registration, long-lived HttpClient |
| `dotnet-authentication` | Step 2 — Basic auth (API key + "x"), credential loading from config |
| `dotnet-calling-endpoints` | Step 3 — calling operations, named arguments for optional params, async/await |
| `dotnet-models` | Step 4 — request/response immutable records, `StringEnum<T>`, field wire names, `required` setters |
| `dotnet-error-handling` | Step 5 — `SdkException<TError>` vs `RawError`, typed error `TryGet…(out …)` accessors, `JsonException` interception, retry logic |
| `dotnet-configuration-resilience` | Step 6 — `RetryOptions`, `Timeout` per-attempt, base URL override, server subdomain |

These are referenced above and must be loaded in full before writing code. This sheet deliberately omits their contents — the skillset carries the patterns and defaults that only a full load provides.

---

## Assumptions & Blockers

- **Assumption: eShopOnWeb user ID is stable** — used as `Reference` on both customer and subscription entities; the app never changes a user's ID (typical).
- **Assumption: subscriptions are created without payment method** — the task specifies "no payment method required"; Maxio product `eshop-pro` and `basic-plan` are configured on site `cp-exp-1` with this constraint.
- **Assumption: subscription state is transient** — the app reads `State` and `NextAssessmentAt` from each call; no persistent cached state in eShopOnWeb DB (API does not expose webhooks for state changes in the plan).
- **Assumption: customer lookup by reference is idempotent** — two simultaneous signup requests both call `ReadCustomerByReference` and get 404, then both call `CreateCustomer`; Maxio rejects the second with 422 (reference already used). App must catch 422 and retry the lookup. (Or use a DB transaction to prevent the race, but that's app-level.)

**No blockers identified.** The Maxio product family and plans are seeded on site `cp-exp-1` and stable (handles: `eshop-subscribe`, `eshop-pro`, `basic-plan`); the API key and subdomain are known.
