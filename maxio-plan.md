# Maxio Subscription Integration Plan — eShopOnWeb

## Scope & sequence

1. **Fetch available plans** — `ListProducts` → enumerate product family, filter by active status, build list of plans with handle + price + metadata
2. **Ensure customer exists** (idempotent) — `ReadCustomerByReference` to check by eShopOnWeb user ID; if exists, use it; otherwise `CreateCustomer` with user email and reference
3. **Create subscription** — `CreateSubscription` with customer ID + product handle + payment-collection-method (prepaid, no card required)
4. **List user subscriptions** — `ListCustomerSubscriptions` to return active/past subscriptions with state + next billing date

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

| Operation | Signature | Request | Response | Error | Source |
|-----------|-----------|---------|----------|-------|--------|
| **1. List Plans** | `client.Products.ListProducts(dateField: null, filter: null, endDate: null, endDatetime: null, startDate: null, startDatetime: null, includeArchived: null, include: null, page: 1, perPage: 20, ct: default)` → `IReadOnlyList<ProductResponse>` | None (all params optional; pass `null` to skip) | `ProductResponse { product: Product !req }` — extract: `product.Id`, `product.Handle`, `product.PriceInCents`, `product.Interval`, `product.IntervalUnit`, `product.Name`, `product.Description` | **Case B** (`SdkException<RawError>`): `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | operations/Products.md |
| **2. Get/Create Customer (idempotent)** | **Read:** `client.Customers.ReadCustomerByReference(reference: "user-id", ct: default)` → `CustomerResponse` **Create:** `client.Customers.CreateCustomer(body: CreateCustomerRequest !req, ct: default)` → `CustomerResponse` | **Create body:** `CreateCustomerRequest { customer: CreateCustomer !req }` where `CreateCustomer` has: `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?` — at minimum pass `Email` + `Reference` for idempotency | `CustomerResponse { customer: Customer !req }` — extract: `customer.Id`, `customer.Email`, `customer.Reference` | **Read — Case B** (`RawError`): 404 if not found **Create — Case A** (`CreateCustomerError`): `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] with errors map; fallback `TryGetRawError(out RawError)` | operations/Customers.md |
| **3. Create Subscription** | `client.Subscriptions.CreateSubscription(body: CreateSubscriptionRequest !req, ct: default)` → `SubscriptionResponse` | `CreateSubscriptionRequest { subscription: CreateSubscription !req }` where `CreateSubscription` has: **required:** `ProductHandle (product_handle): string?` OR `ProductId (product_id): int?` (wire name: choose one); **customer identity:** `CustomerId (customer_id): int?` OR `CustomerReference (customer_reference): string?` (one required); **payment:** `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (wire: enum value; set to `automatic` or `prepaid` per spec); **optional:** `Reference (reference): string?` (subscription reference for idempotency) | `SubscriptionResponse { subscription: Subscription !req }` — extract: `subscription.Id`, `subscription.State` (wire: enum; cast to `SubscriptionState`), `subscription.NextAssessmentAt`, `subscription.CurrentPeriodEndsAt`, `subscription.Product { id, name, handle }` | **Case A** (`CreateSubscriptionError`): `TryGetErrorListResponse1(out ErrorListResponse1)` [422] with `errors: IReadOnlyList<string>` (field-level errors); fallback `TryGetRawError(out RawError)` | operations/Subscriptions.md |
| **4. List User Subscriptions** | `client.Customers.ListCustomerSubscriptions(customerId: int, ct: default)` → `IReadOnlyList<SubscriptionResponse>` | None (path param only) | `IReadOnlyList<SubscriptionResponse>` — each element has: `subscription: Subscription { Id (id): int?, State (state): SubscriptionState?, NextAssessmentAt (next_assessment_at): DateTimeOffset?, Product (product): Product?, Customer (customer): Customer? }` | **Case B** (`RawError`): `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | operations/Customers.md |

### Enums

| Enum | Values (wire) | C# Static | Source |
|------|---------------|-----------|--------|
| `SubscriptionState` | pending, failed_to_create, trialing, assessing, active, soft_failure, past_due, suspended, canceled, expired, paused, unpaid, trial_ended, on_hold, awaiting_signup | `SubscriptionState.Pending`, `SubscriptionState.Active`, `SubscriptionState.Canceled`, etc. | models/enums.md |
| `IntervalUnit` | day, month | `IntervalUnit.Day`, `IntervalUnit.Month` | models/enums.md |
| `CollectionMethod` | automatic, remittance, prepaid, invoice | `CollectionMethod.Automatic`, `CollectionMethod.Prepaid`, etc. | models/enums.md |

### Client Construction & Auth

| Step | Detail | Source |
|------|--------|--------|
| **Namespace** | `MaxioAdvancedBilling` (root); `MaxioAdvancedBilling.Api` (controller groups); `MaxioAdvancedBilling.Models` (records); `MaxioAdvancedBilling.Models.Enums` (enums); `MaxioAdvancedBilling.Errors` (typed error classes); `MaxioAdvancedBilling.Core.Authentication.Basic` (auth); `MaxioAdvancedBilling.Servers` (ServerEnvironment) | sdk-map.md |
| **Auth scheme** | HTTP Basic — `Username` = Maxio API key (from `Maxio:ApiKey` config), `Password` = literal `"x"` | sdk-map.md |
| **Environment** | `ServerEnvironment.Us` (sandbox + production default); infer from `MAXIO_ENVIRONMENT` config or default to US | sdk-map.md |
| **Base URL override** | Optional: if `Maxio:BaseUrl` set in config, pass to `options.Server.Production.Us.BaseUrl` verbatim; otherwise omit (SDK uses environment default) | sdk-map.md |
| **Client class** | `MaxioAdvancedBillingClient(httpClient: System.Net.Http.HttpClient, options: MaxioAdvancedBillingClientOptions)` | sdk-map.md |
| **HttpClient** | Must be long-lived, reused across calls (use `IHttpClientFactory` or DI singleton); **not** created per-request | sdk-map.md |
| **Property access** | `client.Customers`, `client.Products`, `client.Subscriptions` | sdk-map.md |

---

## Trap notes

⚠ **Step 1 (list plans)** — `ListProducts` returns `IReadOnlyList<ProductResponse>`, NOT `IReadOnlyList<Product>` directly; you **must** extract the `Product` from `response.Product` for each element. The response envelope is a required field; it **always** returns a typed list. **MUST load `dotnet-calling-endpoints`** to confirm named argument handling (many optional params have no C# default and mis-bind in positional form).

⚠ **Step 2 (ensure customer, idempotent)** — Two operations, two different error types: `ReadCustomerByReference` is Case B (raw `RawError` on any failure, including 404); `CreateCustomer` is Case A (typed `CreateCustomerError` with a `TryGet` accessor for 422 shape validation). The reference field must be provided on both reads and creates to ensure idempotency against the same eShopOnWeb user ID. **MUST load `dotnet-error-handling`** before writing the try/catch boundary: Case A and Case B require different catch blocks, and 404 is *not* thrown — it surfaces as a successful call returning no match.

⚠ **Step 3 (create subscription, idempotent)** — `CreateSubscription` throws Case A (`CreateSubscriptionError` with `TryGetErrorListResponse1(out ErrorListResponse1)` for 422). The request takes **either** `ProductHandle` **or** `ProductId` (not both); supply the product family handle from the sandbox context (`eshop-subscribe`) if using handles, or query product ID first if using IDs. **Payment method:** set `PaymentCollectionMethod` to one of the enum values (`Automatic` or `Prepaid`); the spec says "no payment method required," but the API may default to `Automatic`; be explicit. **Customer identity:** pass **either** `CustomerId` (if fetched/created above) **or** `CustomerReference` (eShopOnWeb user ID); choose one path and stick to it across reads and creates. **Optional idempotency:** the request model accepts `Reference (reference): string?` — pass the eShopOnWeb subscription ID here if you have one, to prevent duplicate subscriptions on retry. **MUST load `dotnet-error-handling`** to handle the 422 error shape correctly.

⚠ **Boundary trap — `JsonException` from two directions** — Read `dotnet-error-handling` **before** writing error handling:
  - A drifted or malformed **2xx** body (missing required `Subscription` field in `SubscriptionResponse`) surfaces as a `JsonException` from deserialization, **not** `SdkException` — a boundary that only catches `SdkException` lets it escape and crashes the app.
  - A **non-2xx** body that does not match the operation's `CreateSubscriptionError` shape throws `JsonException` *while constructing* the error object, **replacing** the `SdkException` and destroying the HTTP status — a boundary that maps every `JsonException` to 5xx then retries it will retry something that can never succeed.

⚠ **Resilience and per-operation retry semantics** — `CreateSubscription` and `CreateCustomer` are non-idempotent writes. The SDK's default retry configuration retries on transport failure (not just status codes) and on **every** HTTP verb, including POST. If a write succeeds but the response is lost (e.g., timeout after 202), a retry will create a duplicate. Use the `Reference` field (subscription reference, customer reference) to allow Maxio to detect and reject duplicates on re-send, or disable retries for writes via `options.Retry`. **MUST load `dotnet-configuration-resilience`** before tuning retry/timeout behavior for this integration.

⚠ **Enum wire values vs. C# member names** — `SubscriptionState`, `IntervalUnit`, `CollectionMethod` are `StringEnum<T>` (NOT C# enums). Construct via `SubscriptionState.FromValue("active")` or the static member `SubscriptionState.Active`. The wire value is `active` (lowercase, underscore); the C# member is `Active` (PascalCase). Reading back from JSON: deserialization is automatic; you read the C# type and can use `.ToString()` to get the wire value, or call the accessor directly. **MUST load `dotnet-models`** for union and enum construction patterns.

---

## REQUIRED READING

These companion skills **must** be loaded **before implementation starts**. The sheet deliberately omits their resolved details — each skill carries defaults, worked examples, and edge cases this summary cannot fit.

| Skill | Step(s) | Purpose |
|-------|---------|---------|
| `dotnet-client-initialization` | Client & DI setup | Long-lived `HttpClient`, `IHttpClientFactory`, transient vs. singleton client wrapper, constructor arguments |
| `dotnet-authentication` | Auth setup | HTTP Basic credentials (username/password), loading from config, per-request auth, managed rotation |
| `dotnet-calling-endpoints` | Steps 1–4 | Operation signatures, required vs. optional params, request/response envelope shapes, async/await, cancellation |
| `dotnet-models` | Steps 2–4 | Record immutability, `required` fields, nullable, optional fields, union types, enum (StringEnum<T>) construction and reading |
| `dotnet-error-handling` | Steps 2–4 | Case A (typed `{Op}Error` with `TryGet…` accessors) vs. Case B (raw `RawError`), `JsonException` from deserialization and from error-shape parsing, boundary patterns, exception hierarchy |
| `dotnet-configuration-resilience` | Step 3 | Retry options, timeout bounds (per-attempt vs. total), `HttpMethodsToRetry`, exponential backoff, logging hooks; why transport failures retry on every verb, why POST can execute twice |
| `dotnet-testing` | End-to-end tests | Stubbing with `HttpClient` constructor, matcher patterns, assertion helpers |

**Both of these hazard rows must appear in the FIRST sheet and are non-negotiable:**

- **`System.Text.Json.JsonException` from malformed 2xx body** — deserialization failure (missing required field in `SubscriptionResponse.Subscription`, e.g.) surfaces as `JsonException`, **not** `SdkException`. An exception boundary that catches only `SdkException` lets it escape. Map `JsonException` independently. **MUST load `dotnet-error-handling`** before writing the boundary.
- **`System.Text.Json.JsonException` from non-2xx body mismatch** — if the server returns a non-2xx status with a body shape that does not match the operation's typed `{Operation}Error` (e.g., `CreateSubscriptionError`), `JsonException` is thrown *while constructing the error object*, **replacing** the `SdkException` and destroying the HTTP status with it. A boundary that maps every `JsonException` to 5xx then retries will retry a failure that can never recover. Validate error-shape expectations, and do **not** blindly retry 5xx when `JsonException` is the root cause. **MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

### Assumptions

- eShopOnWeb user ID (PK from the eShopOnWeb user table) will be passed as the `Reference` field to Maxio `Customer` and `Subscription` create operations, enabling idempotent re-sends and duplicate detection.
- Maxio sandbox site subdomain is configured in `Maxio:Subdomain` (from environment `MAXIO_SITE_SUBDOMAIN`).
- Product family handle `eshop-subscribe` and plan handles (`eshop-pro`, `basic-plan`) are pre-created in Maxio sandbox and stable (not renamed/deleted during integration).
- Payment collection method defaults to or is set to `Prepaid` or `Automatic` per spec ("no payment method required"); no payment profile/credit card is collected upfront.
- Subscription state `Active` indicates enrollment success; `FailedToCreate` or other error states indicate creation failure (caller should handle and log).

### Blockers

None identified. All required operations (`ListProducts`, `ReadCustomerByReference`, `CreateCustomer`, `CreateSubscription`, `ListCustomerSubscriptions`) are exposed by the SDK and map cleanly to the endpoints. No SDK capability gap blocks the plan.
