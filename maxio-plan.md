# Maxio Advanced Billing Integration Plan — eShopOnWeb Recurring Subscriptions

## Scope & Sequence

This plan integrates recurring subscription billing into eShopOnWeb alongside existing one-time commerce. The flow sequence is:

1. **Enroll user endpoint** (on PublicApi) — `POST /api/subscriptions`
   - Idempotently create a Maxio customer (if not already created)
   - Create a subscription to a plan
   - Return confirmation (plan handle, price, next billing date, state)

2. **List subscription plans endpoint** (on PublicApi) — `GET /api/subscription-plans`
   - Retrieve all active plans from the `eshop-subscribe` product family
   - Return plan handles, names, prices

3. **List user subscriptions endpoint** (on PublicApi) — `GET /api/my-subscriptions`
   - Retrieve all subscriptions for the authenticated user
   - Return state, next billing date, plan details

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Step | Operation (Controller.Method) | Signature | Request Model | Response Model | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 1a: Idempotent customer lookup | `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | None; `reference` is a query param | `CustomerResponse` with `Customer` field (`Id`, `Email`, `FirstName`, `LastName`, `Reference`) | Case B: `SdkException<RawError>` — if 404 means customer not found, use `ex.Error.StatusCode` to detect (404 = not found) | None | `operations/Customers.md` |
| 1b: Create customer idempotently | `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — body must pass explicitly | `CreateCustomerRequest` wraps `CreateCustomer !req` (required). Inner `CreateCustomer` fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Organization (organization): string?` | `CustomerResponse` with `Customer` field | Case A: `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | None | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| 2: Create subscription | `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — body must pass explicitly | `CreateSubscriptionRequest` wraps `CreateSubscription !req`. Minimum required fields: `ProductHandle (product_handle): string?` OR `ProductId (product_id): int?`; `CustomerId (customer_id): int?` OR `CustomerReference (customer_reference): string?`. Retrieved from plan selection and customer lookup. Optional but commonly used: `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (wire values: `automatic`, `remittance`, `prepaid`, `invoice`; use `CollectionMethod.Automatic` as default) | `SubscriptionResponse` with `Subscription` field. Extract: `Id (id): int?`, `State (state): SubscriptionState?` (wire values: `active`, `trialing`, `canceled`, etc.), `ProductPriceInCents (product_price_in_cents): long?` (divide by 100 for USD), `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ProductHandle (product_handle): string?`, `ProductId (product_id): int?` | Case A: `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. 422 on validation errors (bad product, customer, plan mismatch). Inspect `ErrorListResponse1.Errors` (array of strings) for human message | None | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |
| 3: List plans by product family | `ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params (pass `null` to skip); `productFamilyId` is NOT nullable (required positional). For listing plans: pass `productFamilyId: "eshop-subscribe"` or the handle of the product family. | None; query params only | `IReadOnlyList<ProductResponse>` — each `ProductResponse` wraps `Product !req`. Extract: `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?` (divide by 100 for USD), `Id (id): int?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` (wire values: `day`, `month`) | Case A: `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] (product family not found) · `TryGetRawError(out RawError)` [fallback] | Manual `page` + `perPage` (defaults: `page` = 1, `perPage` = 20). For full list, start at page 1 and increment until response count < perPage | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| 4: List subscriptions for customer | `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `customerId` is the Maxio-generated ID from step 1a/1b | None; URL path param only | `IReadOnlyList<SubscriptionResponse>` — each `SubscriptionResponse` wraps `Subscription?`. Extract per-subscription: `Id (id): int?`, `State (state): SubscriptionState?`, `ProductHandle (product_handle): string?`, `ProductName (product_name): string?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` | Case B: `SdkException<RawError>` — no typed accessors. Use `ex.Error.StatusCode` (404 = customer not found) and `ex.Error.ReadAsString()` for error body | None | `operations/Customers.md`, `records-4-Su-We.md` |

### Request / Response Model Details

**CreateCustomer (inner record in `CreateCustomerRequest`)**
- Namespace: `MaxioAdvancedBilling.Models`
- Required: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`
- Optional but recommended for idempotency: `Reference (reference): string?` — use a stable app user ID or email hash as the reference to enable `ReadCustomerByReference` lookup
- Source: `records-1-Ac-Cr.md` (line 124–125)

**CreateSubscription (inner record in `CreateSubscriptionRequest`)**
- Namespace: `MaxioAdvancedBilling.Models`
- Identify customer: `CustomerId (customer_id): int?` OR `CustomerReference (customer_reference): string?` (use the reference field to match the customer created above)
- Identify plan: `ProductHandle (product_handle): string?` OR `ProductId (product_id): int?` (use handle: `"eshop-pro"` or `"basic-plan"`)
- Optional billing: `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — wire values from `MaxioAdvancedBilling.Models.Enums.CollectionMethod`: `Automatic`, `Remittance`, `Prepaid`, `Invoice`
- Source: `records-2-Cr-Ne.md` (line 17, line 21)

**SubscriptionResponse (wrapper)**
- Namespace: `MaxioAdvancedBilling.Models`
- Structure: `public record SubscriptionResponse { public Subscription? Subscription { get; init; } }`
- Extract the subscription: `response.Subscription` (not `response.Id` — that does not exist)
- Source: `Models/SubscriptionResponse.cs`

**Subscription (inner record in `SubscriptionResponse`)**
- Namespace: `MaxioAdvancedBilling.Models`
- Access plan details via nested `Product`: `subscription.Product?.Handle`, `subscription.Product?.Name` (NOT via `subscription.ProductHandle` or `subscription.ProductName` — those do not exist)
- State enum: `SubscriptionState` in namespace `MaxioAdvancedBilling.Models.Enums` — values: `Active (active)`, `Trialing (trialing)`, `Canceled (canceled)`, `PastDue (past_due)`, `OnHold (on_hold)`, `Paused (paused)`, `Expired (expired)`, `Suspended (suspended)`, `AwaitingSignup (awaiting_signup)`, `TrialEnded (trial_ended)`, `Assessing (assessing)`, `Pending (pending)`, `FailedToCreate (failed_to_create)`, `SoftFailure (soft_failure)`, `Unpaid (unpaid)`
- Billing date: `NextAssessmentAt (next_assessment_at): DateTimeOffset?` — next renewal date
- Contains nested `Product (product): Product?` for plan name and handle
- Source: `records-4-Su-We.md` (line 156), `enums.md` (line 96), `Models/Subscription.cs`

**Product (inner record in `ProductResponse`)**
- Namespace: `MaxioAdvancedBilling.Models`
- Price is in cents: divide `PriceInCents` by 100 to get USD
- Interval: `Interval (interval): int?` + `IntervalUnit (interval_unit): IntervalUnit?` (from `MaxioAdvancedBilling.Models.Enums`, values: `Day`, `Month`)
- Source: `records-3-Of-Su.md` (line 62), `enums.md` (line 47)

### Enum Value Lists

**CollectionMethod** (`MaxioAdvancedBilling.Models.Enums`)
- `Automatic (automatic)` — billing automatically collected
- `Remittance (remittance)` — Relationship Invoicing
- `Prepaid (prepaid)` — prepaid balance
- `Invoice (invoice)` — send invoice for manual payment
- Source: `enums.md` (line 21)

**IntervalUnit** (`MaxioAdvancedBilling.Models.Enums`)
- `Day (day)`
- `Month (month)`
- Source: `enums.md` (line 47)

**SubscriptionState** (`MaxioAdvancedBilling.Models.Enums`)
- `Active (active)` — subscription is active
- `Trialing (trialing)` — in trial period
- `Canceled (canceled)` — subscription canceled
- `PastDue (past_due)` — payment past due
- `OnHold (on_hold)` — on hold
- `Paused (paused)` — paused
- `Expired (expired)` — expired
- `Suspended (suspended)` — suspended (dunning)
- `AwaitingSignup (awaiting_signup)` — awaiting signup activation
- `TrialEnded (trial_ended)` — trial ended
- Source: `enums.md` (line 96)

### Client Construction & Auth

**Client Registration** (per `sdk-map.md`):
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers; // ServerEnvironment

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = "<MAXIO_API_KEY>", 
        Password = "x" 
    },
    Environment = ServerEnvironment.Us, // or ServerEnvironment.Eu
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Configuration Keys** (to load from `IConfiguration` / appsettings):
- `Maxio:ApiKey` — value is the API key (username for HTTP Basic)
- `Maxio:Subdomain` — site subdomain (e.g., `cp-exp-1`); **not** needed in client constructor if base URL is correctly set
- `Maxio:ProductFamilyHandle` — handle for the product family, e.g., `eshop-subscribe`
- `Maxio:BaseUrl` (optional) — override the base URL (default: `https://{subdomain}.chargify.com` for US, `https://{subdomain}.ebilling.maxio.com` for EU)

**Server Node Override** (if needed to redirect to sandbox or mock):
```csharp
options.Server.Production.Us.BaseUrl = "https://cp-exp-1.chargify.com"; // or your mock server
```

**HttpClient Lifetime** — MUST load `dotnet-client-initialization` before construction. The `HttpClient` passed to the SDK constructor must be **long-lived** and reused (via `IHttpClientFactory` in DI). Do NOT create a new `HttpClient` per request.

### Trap Notes

- ⚠ **Step 1a (ReadCustomerByReference)** — Maxio returns 404 (not found) when no customer matches the reference. This is NOT an exception; check `ex.Error.StatusCode == HttpStatusCode.NotFound` (Case B error). **MUST load `dotnet-error-handling`** for the difference between Case A typed errors (with structured `TryGet…` accessors) and Case B raw errors (only `StatusCode` and body readers).

- ⚠ **Step 1b (CreateCustomer) — error type namespace (CRITICAL)** — The exception type `CreateCustomerError` is in namespace `MaxioAdvancedBilling.Errors`. **You must add `using MaxioAdvancedBilling.Errors;`** to make it visible (it is NOT in `MaxioAdvancedBilling.Models`). The operation throws on 422 (validation failure) with a typed error object; inspect `ex.Error.TryGetCustomerErrorResponse1(out var payload)` and `payload.Errors` for field-level validation messages. **MUST load `dotnet-error-handling`** to understand Case A error accessors.

- ⚠ **Step 1b (CreateCustomer) — idempotent customer creation** — The `Reference` field is **unique per site**; set it to a stable app user ID or normalized email so you can look it up later. If you omit the reference, you must track the Maxio customer ID locally.

- ⚠ **Step 2 (CreateSubscription) — error type namespace (CRITICAL)** — The exception type `CreateSubscriptionError` is in namespace `MaxioAdvancedBilling.Errors`. **You must add `using MaxioAdvancedBilling.Errors;`** to make it visible. **MUST load `dotnet-error-handling`** for Case A error accessor patterns.

- ⚠ **Step 2 (CreateSubscription) — accessing plan handle and name from subscription (CRITICAL)** — The `Subscription` model does NOT have `ProductHandle` or `ProductName` properties. Access the plan details via the nested `Product` object: `subscription.Product?.Handle` and `subscription.Product?.Name`. Similarly, access the subscription ID as `response.Subscription?.Id` (the response wraps `Subscription` in a `SubscriptionResponse` object). **MUST load `dotnet-models`** to understand nested record field access patterns.

- ⚠ **Step 2 (CreateSubscription) — plan identification** — Must specify either `ProductHandle` (string, recommended: `"eshop-pro"` or `"basic-plan"`) or `ProductId` (int). Must identify the customer by either `CustomerId` (int) or `CustomerReference` (string, matches the Reference you set in step 1b). Omitting both customer identifiers throws a 422 validation error. **MUST load `dotnet-calling-endpoints`** — many optional params have no C# default and mis-bind in positional calls; always use **named arguments** for optional fields.

- ⚠ **Step 3 (ListProductsForProductFamily) — pagination parameter types** — `page` and `perPage` are both `int?` with defaults `1` and `20`. If you are computing these from user input (e.g., as doubles from a decimal), **cast to `int`**: `page: (int?)pageValue, perPage: (int?)perPageValue`. The signature shown in the contract sheet is correct.

- ⚠ **Step 3 (ListProductsForProductFamily) — product family handle vs. ID** — The operation takes `productFamilyId: string`, which accepts **both** a numeric ID and a handle string (e.g., `"eshop-subscribe"`). For configuration-driven access, store the **handle** (`Maxio:ProductFamilyHandle = "eshop-subscribe"`) and pass it directly; the SDK and Maxio accept both formats. **MUST load `dotnet-calling-endpoints`** to handle the nullable query params (pass `null` to skip unused filters).

- ⚠ **Step 4 (ListCustomerSubscriptions) — subscription state interpretation** — Extract `Subscription.State` (enum `SubscriptionState`) to determine active vs. canceled subscriptions. Values like `Active`, `Trialing`, `PastDue`, `Suspended` indicate active billing; `Canceled` and `Expired` indicate the subscription is no longer billing. **MUST load `dotnet-models`** to work with enums (they are `StringEnum<T>` records, not C# enums; construct via static members or `FromValue(wireValue)`).

- ⚠ **Price precision** — `PriceInCents` and `ProductPriceInCents` are `long?` (cents). Divide by 100.0 to get USD. Store and display with `decimal` or `double` for currency; never use `float`.

- ⚠ **Namespace management (CRITICAL FOR ERROR HANDLERS)** — **Error types like `CreateCustomerError` and `CreateSubscriptionError` live ONLY in `MaxioAdvancedBilling.Errors`**, NOT in Models or root namespace. **You MUST add `using MaxioAdvancedBilling.Errors;`** to your error handlers, or the compiler will fail with `CS0246` ("type does not exist"). Other types: client/auth in `MaxioAdvancedBilling`; models in `MaxioAdvancedBilling.Models`; enums in `MaxioAdvancedBilling.Models.Enums`; core exceptions in `MaxioAdvancedBilling.Core.Exceptions`. Each namespace must be declared separately via `using`. **MUST load `dotnet-models`** before referencing enums, unions, or model fields to understand namespace requirements.

- ⚠ **Wire names and JSON serialization** — All request/response models are immutable records with `init`-only setters. Required fields must be set in the object initializer (not null-coalesced). Wire names (JSON keys) differ from C# property names (e.g., `first_name` vs. `FirstName`); the SDK handles this automatically. Never construct JSON by hand; use the generated models. **MUST load `dotnet-models`** for union handling (some fields are `OneOf` / `AnyOf` and require factory methods + `TryGet…` accessors).

- ⚠ **Request bodies must pass explicitly** — All operation signatures that take a request body (e.g., `CreateCustomer(CreateCustomerRequest? body, …)`) mark `body` as nullable but require it to be passed explicitly (no implicit default). Build the request object and pass it; do not rely on null coalescing. **MUST load `dotnet-calling-endpoints`** before the first call.

## REQUIRED READING

The following companion skills must be loaded **before implementation starts**. The sheet deliberately does not carry their contents; each addresses a contract trap the signature hides:

| Skill | Step(s) | Purpose |
|---|---|---|
| `dotnet-client-initialization` | Client & DI setup (pre-step) | The `HttpClient` and SDK client lifetime, threading safety, DI registration |
| `dotnet-authentication` | Auth wiring (pre-step) | HTTP Basic credentials (username = API key, password = `"x"`), env config loading |
| `dotnet-calling-endpoints` | Steps 1–4 | Calling operations with named args, required vs. optional params, async/await, cancellation |
| `dotnet-models` | Steps 1–4 | Immutable records, required fields, enums (not C# enums), unions + `TryGet…`, wire names |
| `dotnet-error-handling` | Steps 1b, 2, 3, 4 | Case A typed errors (`SdkException<{Op}Error>` with `TryGet…` accessors) vs. Case B raw errors (`SdkException<RawError>`), JSON deserialization errors, retry boundaries |
| `dotnet-configuration-resilience` | Post-setup | Retry/timeout semantics, `HttpClient` long-lived registration, Polly integration |

**Both JSON deserialization error rows (mandatory for all integrations):**
- A **drifted or malformed 2xx body** (e.g., missing `required` member) surfaces as `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. This is a data contract failure, not a Maxio error.
- A **non-2xx body that does not match** the operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed. A boundary that maps every `JsonException` to a 5xx then reports it as an outage; a caller that retries 5xx retries something that can never succeed.
- **MUST load `dotnet-error-handling`** before writing the error boundary to understand these cases and how to separate them (test the exception type, not just the message).

## Assumptions & Blockers

**Assumptions:**
- Sandbox Maxio site `cp-exp-1` is already provisioned with product family `eshop-subscribe`, plans `eshop-pro` ($299/mo) and `basic-plan` ($29/mo), all with zero trial and no payment required to create the subscription (signup is deferred or payment is optional).
- eShopOnWeb has an `IConfiguration` service and can load `Maxio:*` settings from appsettings or environment variables.
- The `IHttpClientFactory` or a long-lived `HttpClient` is available for DI.
- JWT authentication on PublicApi is already implemented; the plan does not cover JWT token validation (that is the caller's concern).
- No metered components are used in phase 1 (the `api-call` component exists but is not enrolled).

**Blockers:**
- None identified. The Maxio API surface is complete for this scope. All operations are documented in the map; no live-traffic-only behaviors are suspected.
