# Maxio Advanced Billing Integration — eShopOnWeb Subscription Billing

## Scope & Sequence

**Hero flow implementation — three API endpoints:**

1. **`GET /api/subscription-plans`** → list available subscription plans by querying Maxio products
   - Call: `ListProductsForProductFamily("handle:eshop-subscribe", ...)` to fetch plans in product family
   - Call: For each product, read pricing (stored on `Product.PriceInCents`)
   
2. **`POST /api/subscriptions`** → create a user subscription (idempotent customer creation + subscription)
   - Call: `ReadCustomerByReference(eShopOnWeb user ID)` to check for existing Maxio customer
   - If not found (404): `CreateCustomer(CreateCustomerRequest)` with user ID as `reference`
   - Call: `CreateSubscription(CreateSubscriptionRequest)` with product handle and customer ID/reference
   - Persist: eShopOnWeb user ID → Maxio customer ID mapping
   
3. **`GET /api/my-subscriptions`** → list active subscriptions for logged-in user
   - Lookup: Maxio customer ID from user-mapping persistence
   - Call: `ListCustomerSubscriptions(customerId)` to fetch all subscriptions for that customer

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation | Signature | Request Model | Response Envelope | Error Case | Pagination | Source |
|-----------|-----------|---|---|---|---|---|
| **ListProductsForProductFamily** — retrieve all products in `eshop-subscribe` family | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` · Pass `productFamilyId: "handle:eshop-subscribe"`; for query params, pass `null` to skip unused filters | n/a (read-only) | `IReadOnlyList<ProductResponse>` — each element is `ProductResponse` with single field `Product (product): Product !req` (namespace: `MaxioAdvancedBilling.Models`) | **Case A** — `SdkException<ListProductsForProductFamilyError>` (namespace: `MaxioAdvancedBilling.Errors`) · `TryGetString(out string)` [404] — throws if product family not found · `TryGetRawError(out RawError)` [fallback] | manual via `page`/`perPage` (defaults: page=1, perPage=20) | `map/operations/ProductFamilies.md` |
| **ReadCustomerByReference** — idempotent lookup of existing customer by eShopOnWeb user ID | `ReadCustomerByReference(string reference, CancellationToken ct = default)` · Pass the **eShopOnWeb user ID** as `reference` | n/a (query param only) | `CustomerResponse` — single field `Customer (customer): Customer !req` (namespace: `MaxioAdvancedBilling.Models`) · Caller extracts `.Customer.Id` for mapping | **Case B** — `SdkException<RawError>` (status codes: 404 not found, 400+ errors) · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` accessors | none | `map/operations/Customers.md` |
| **CreateCustomer** — create Maxio customer record (called only if `ReadCustomerByReference` throws 404) | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` · `body` required (pass explicitly) | `CreateCustomerRequest` { `Customer (customer): CreateCustomer !req` } · `CreateCustomer` fields (all optional except noted): `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` [**set to eShopOnWeb user ID**], `Organization`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` (namespace: `MaxioAdvancedBilling.Models`) | `CustomerResponse` — field `Customer (customer): Customer !req` (namespace: `MaxioAdvancedBilling.Models`) | **Case A** — `SdkException<CreateCustomerError>` (namespace: `MaxioAdvancedBilling.Errors`) · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] — validation errors (namespace: `MaxioAdvancedBilling.Models`) · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Customers.md` |
| **CreateSubscription** — enroll customer in product | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` · `body` required | `CreateSubscriptionRequest` { `Subscription (subscription): CreateSubscription !req` } · `CreateSubscription` fields (optional unless noted): `ProductHandle (product_handle): string?` [**use `"eshop-pro"` or `"basic-plan"`**] or `ProductId (product_id): int?`, `CustomerId (customer_id): int?` [**from customer lookup/creation**] or `CustomerReference (customer_reference): string?` [eShopOnWeb user ID], `PaymentProfileId`, `Components`, `Coupons`, `Reference` [optional user ref], etc. (namespace: `MaxioAdvancedBilling.Models`) | `SubscriptionResponse` — field `Subscription (subscription): Subscription?` (namespace: `MaxioAdvancedBilling.Models`) · Caller extracts `.Subscription.Id`, `.Subscription.CurrentPeriodEndsAt`, `.Subscription.State`, `.Subscription.NextBillingAt` | **Case A** — `SdkException<CreateSubscriptionError>` (namespace: `MaxioAdvancedBilling.Errors`) · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] — validation/payment errors (namespace: `MaxioAdvancedBilling.Models`) · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md` |
| **ListCustomerSubscriptions** — fetch all active subscriptions for a customer | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` · Pass Maxio `customerId` from lookup | n/a (path param only) | `IReadOnlyList<SubscriptionResponse>` — each is `SubscriptionResponse.Subscription: Subscription?` (namespace: `MaxioAdvancedBilling.Models`) | **Case B** — `SdkException<RawError>` (status codes: 404, 400+) · `StatusCode`, `ReadAsString()`, etc. accessors | none — returns all subscriptions for customer | `map/operations/Customers.md` |

### Request Model Details

**`CreateCustomerRequest` / `CreateCustomer`** (namespace: `MaxioAdvancedBilling.Models`)  
Fields on `CreateCustomer` (the inner `customer` object):
- `FirstName (first_name): string !req` — required
- `LastName (last_name): string !req` — required  
- `Email (email): string !req` — required
- `Reference (reference): string?` — optional, **set this to the eShopOnWeb user ID for idempotent lookup**
- `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?` — optional billing/shipping info
- `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?` — optional contact/tax info
- `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?` — optional tax handling
- `Organization (organization): string?`, `CcEmails (cc_emails): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?` — optional metadata

**`CreateSubscriptionRequest` / `CreateSubscription`** (namespace: `MaxioAdvancedBilling.Models`)  
Fields on `CreateSubscription` (the inner `subscription` object) — **all optional unless noted**:
- `ProductHandle (product_handle): string?` **OR** `ProductId (product_id): int?` — required: one of these two; use handle `"eshop-pro"` (ID 7126957) or `"basic-plan"` (ID 7126958)
- `CustomerId (customer_id): int?` **OR** `CustomerReference (customer_reference): string?` — required: one of these; pass Maxio customer ID or eShopOnWeb user ID as reference
- `Reference (reference): string?` — optional subscription reference from your app (e.g. order ID)
- `PaymentProfileId (payment_profile_id): int?` — optional; **NOT required per sandbox setup** (payment method NOT required for these plans)
- `Components (components): IReadOnlyList<CreateSubscriptionComponent>?` — optional; if metered usage (`api-call` component) is to be tracked, pass this
- `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?` — optional coupon codes
- `ExpiresAt (expires_at): DateTimeOffset?` — optional expiration date
- `CancellationMessage (cancellation_message): string?`, `CancellationMethod (cancellation_method): string?` — optional cancellation info
- Other fields: `ReceivesInvoiceEmails`, `NetTerms`, `DeferSignup`, `InitialBillingAt`, `NextBillingAt`, etc. — optional; see map for full list

### Response Model Details

**`ProductResponse`** (namespace: `MaxioAdvancedBilling.Models`)
- Single required field: `Product (product): Product !req`
- Extract from `Product`: `Id`, `Name`, `Handle`, `PriceInCents` (in cents, e.g. 29900 = $299.00), `Interval`, `IntervalUnit`, `Description`, `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `ExpirationInterval`, `ExpirationIntervalUnit`, etc.

**`CustomerResponse`** (namespace: `MaxioAdvancedBilling.Models`)
- Single required field: `Customer (customer): Customer !req`
- Extract: `Customer.Id` (Maxio-assigned customer ID), `Customer.FirstName`, `Customer.LastName`, `Customer.Email`, `Customer.Reference` (the eShopOnWeb user ID you provided), `Customer.CreatedAt`, `Customer.UpdatedAt`, etc.

**`SubscriptionResponse`** (namespace: `MaxioAdvancedBilling.Models`)
- Single optional field: `Subscription (subscription): Subscription?`
- Extract: `Subscription.Id`, `Subscription.CustomerId`, `Subscription.ProductId`, `Subscription.ProductHandle`, `Subscription.State` (active/pending/canceled/etc.), `Subscription.CurrentPeriodEndsAt`, `Subscription.NextBillingAt`, `Subscription.Balance`, `Subscription.TotalRevenueInCents`, `Subscription.CreatedAt`, `Subscription.UpdatedAt`, `Subscription.Reference`, etc.

---

## Enums

**CollectionMethod** (wire values used in request/response, namespace: `MaxioAdvancedBilling.Models.Enums`)  
Wire values: `"automatic"`, `"remittance"`, `"invoice"`. Default for subscriptions: automatic. Not passed by default (optional).

**SubscriptionState** (subscription status, namespace: `MaxioAdvancedBilling.Models.Enums`)  
Wire values: `"pending"`, `"trialing"`, `"assessing"`, `"active"`, `"soft_fail"`, `"past_due"`, `"suspended"`, `"cancellation_pending"`, `"canceled"`, `"expired"`, etc. Returned by API; use in filtering/display.

**IntervalUnit** (billing period, namespace: `MaxioAdvancedBilling.Models.Enums`)  
Wire values: `"day"`, `"month"`, `"year"`. All sandbox plans use `"month"`.

---

## Error Handling & Accessors

**ListProductsForProductFamily** (Case A)
- Throws: `SdkException<ListProductsForProductFamilyError>`
- 404 (product family not found): `TryGetString(out var msg)` returns `true`; `msg` is error string
- Other: `TryGetRawError(out var raw)`; check `raw.StatusCode`

**ReadCustomerByReference** (Case B)
- Throws: `SdkException<RawError>`
- 404 (customer not found): `ex.Error.StatusCode == HttpStatusCode.NotFound`
- Extract error message: `ex.Error.ReadAsString()` or `ex.Error.ReadAsJson<T>()`

**CreateCustomer** (Case A)
- Throws: `SdkException<CreateCustomerError>`
- 422 (validation error, e.g. duplicate reference): `TryGetCustomerErrorResponse1(out var e422)` returns `true`; examine `e422.Errors` for field-level errors
- Other: `TryGetRawError(out var raw)`

**CreateSubscription** (Case A)
- Throws: `SdkException<CreateSubscriptionError>`
- 422 (validation/payment failure): `TryGetErrorListResponse1(out var e422)` returns `true`; `e422.Errors` is `IReadOnlyList<string>` of error messages
- Other: `TryGetRawError(out var raw)`

**ListCustomerSubscriptions** (Case B)
- Throws: `SdkException<RawError>`
- Extract: `ex.Error.StatusCode`, `ex.Error.ReadAsString()`

---

## Client & Configuration

**Client construction** (namespace: `MaxioAdvancedBilling`)  
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = "<MAXIO_API_KEY>",  // binding key: Maxio:ApiKey
        Password = "x"                  // literal "x"
    },
    Environment = ServerEnvironment.Us, // or .Eu; binding key: Maxio:Environment (default: Us)
    Server = new ServerOptions
    {
        Production = new ProductionOptions
        {
            Us = new UrlServerOptions
            {
                Site = "<MAXIO_SITE_SUBDOMAIN>"  // binding key: Maxio:Subdomain; e.g. "cp-exp-2"
            }
        }
    }
};
// If Maxio:BaseUrl is set in config, override:
// options.Server.Production.Us.BaseUrl = "<MAXIO_BASE_URL>";

var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Configuration bindings** (from env → user-secrets):
- `MAXIO_API_KEY` → `Maxio:ApiKey` (required, API key)
- `MAXIO_SITE_SUBDOMAIN` → `Maxio:Subdomain` (required, e.g. "cp-exp-2")
- `MAXIO_ENVIRONMENT` → `Maxio:Environment` (optional, default: "Us"; values: "Us" or "Eu")
- `MAXIO_DEFAULT_PRODUCT_FAMILY` → `Maxio:ProductFamilyHandle` (optional, default: "eshop-subscribe"; override for testing)
- `MAXIO_BASE_URL` → `Maxio:BaseUrl` (optional; when set, use verbatim as API base URL instead of deriving from subdomain)

**Namespace imports** (add to consuming classes):
```csharp
using MaxioAdvancedBilling;                           // Client, ClientOptions
using MaxioAdvancedBilling.Api;                       // Operation controllers
using MaxioAdvancedBilling.Models;                    // Request/response records, Customer, Subscription, Product, etc.
using MaxioAdvancedBilling.Models.Enums;              // SubscriptionState, IntervalUnit, CollectionMethod, etc.
using MaxioAdvancedBilling.Errors;                    // CreateCustomerError, CreateSubscriptionError, etc.
using MaxioAdvancedBilling.Core.Authentication.Basic; // BasicAuthCredentials
using MaxioAdvancedBilling.Servers;                   // ServerEnvironment, ServerOptions
using MaxioAdvancedBilling.Core.ErrorResponse;        // RawError
```

---

## Trap Notes

⚠ **Step 1 (Client initialization)** — The SDK's retry/timeout options do **not** bound a whole request and are **not** the timeout on the `HttpClient` you register. Retries are statuses (e.g. 5xx, transport errors) or specific methods; timeouts apply per-attempt. **MUST load `dotnet-configuration-resilience`** before wiring the client and tuning retry/timeout behavior.

⚠ **Step 2 (Authentication)** — Credentials (`Username` = API key, `Password` = literal `"x"`) must be set in `BasicAuthCredentials` **before** constructing the client. If credentials change, a new client must be created (the SDK does not support mid-lifetime credential refresh). **MUST load `dotnet-authentication`** before wiring auth.

⚠ **Step 3 (Customer idempotency via reference)** — `ReadCustomerByReference` throws a `SdkException<RawError>` on 404; the result is **not** `null` but an exception. Wrap in try/catch and check `.StatusCode == HttpStatusCode.NotFound` to distinguish "customer doesn't exist" (404) from other errors (4xx/5xx). **Do not** pass a nullable return; 404 must be caught and converted to a "not found" signal.

⚠ **Step 4 (Subscription payment method NOT required)** — The sandbox plans (`eshop-pro`, `basic-plan`) are configured with **no payment method required** at signup. Do **not** pass `PaymentProfileId` or credit-card attributes unless the integration explicitly needs to collect payment at subscription time. If payment is later required (e.g. via dunning), the API will reject or defer the subscription state change.

⚠ **Step 5 (Error boundary — JsonException handling)** — The SDK can throw `System.Text.Json.JsonException` in two scenarios:
1. **Malformed 2xx body** (e.g. missing required field in response model): throws `JsonException` from deserialization, **not** an `SdkException`. An exception-only catch ladder that only catches `SdkException` will let this escape the integration boundary.
2. **Malformed error response** (non-2xx body that doesn't match the generated `{Operation}Error` shape): throws `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces** the `SdkException` and the HTTP status code is destroyed. A boundary that maps every `JsonException` to a 5xx will report a deterministic rejection (422, 400) as an outage; a caller that retries 5xx retries something that will never succeed.

**MUST load `dotnet-error-handling`** to understand the full error-handling contract and the distinction between application errors (4xx, 422 from validation) and infrastructure failures (transport, timeout, parse errors).

⚠ **Step 6 (User ↔ Maxio customer mapping persistence)** — The integration must persistently store the mapping from eShopOnWeb user ID to Maxio customer ID (or use the `Reference` field on ReadCustomerByReference for lookups). If persistence is lost, a retry of subscription creation will create a duplicate customer with the same reference. The **Maxio API rejects duplicate references** (422 on CreateCustomer if reference already exists). Plan for: (a) storing mapping in a local table/cache, or (b) always falling back to ReadCustomerByReference before CreateCustomer (idempotent if the lookup succeeds).

⚠ **Step 7 (Subscription state transitions)** — A newly created subscription may enter `pending`, `trialing`, `assessing`, or `active` state depending on product configuration (trials, initial charges, dunning). Do **not** assume `State == "active"` immediately after CreateSubscription. Check `Subscription.State` and `Subscription.NextBillingAt` to understand when the next charge is scheduled. If payment is required and fails, the state may be `soft_fail`, `past_due`, or `suspended`.

---

## REQUIRED READING

Before implementation, load these companion skills (in order) — they carry best practices, worked examples, and gotchas the contract sheet does not:

1. **`dotnet-client-initialization`** — Step 1: how to register `HttpClient` and the SDK client in DI, avoid recreating clients, set up the `MaxioAdvancedBillingClientOptions`.

2. **`dotnet-authentication`** — Step 2: how to load credentials from configuration, wire `BasicAuthCredentials`, and handle credential rotation (if needed).

3. **`dotnet-calling-endpoints`** — Step 3: how to call each operation on the client; named arguments for optional params; pagination.

4. **`dotnet-models`** — Step 4: request/response record immutability, `init`-only setters, required fields, nullable handling, and how to construct request objects.

5. **`dotnet-error-handling`** — Step 5 (CRITICAL): the full error-handling model; when to catch `SdkException<T>` vs. `JsonException` vs. `RawError`; how to use `TryGet…` accessors; why 422 responses are typed but 500s are not.

6. **`dotnet-configuration-resilience`** — Step 6: retry options, timeout semantics (per-attempt, not per-call), backoff strategies, and how they interact with HTTP methods and status codes.

7. **`dotnet-testing`** — Step 7: if writing tests, how to mock the `HttpClient` and SDK.

---

## Special Handling: JsonException at the Boundary

**Your error boundary MUST handle `System.Text.Json.JsonException` correctly.** These rows are mandatory in your first boundary implementation and belong in the first sheet, not a later revision:

| Scenario | Exception Type | HTTP Status | Root Cause | Correct Handling |
|----------|---|---|---|---|
| Response body deserializes but a required field is missing (e.g. `Subscription` in `SubscriptionResponse` when it should not be null) | `JsonException` | 200–299 (2xx) | SDK response model validation failure; not an API error | Catch `JsonException` separately; **do not** map to 5xx; caller cannot retry; log as a data contract defect (API returned incomplete data). |
| Response body for a 422/400 does not match the generated `{Operation}Error` shape | `JsonException` | 422, 400, etc. (4xx/5xx) | SDK error deserialization failure; the HTTP status is destroyed before the exception reaches handler | Catch `JsonException` separately; **do not** retry; log the status, raw body, and exception; the original error is lost and must be recovered from the raw body or logs. |

**Load `dotnet-error-handling` BEFORE you write the catch ladder.** These rows are not optional; they appear in the first sheet so the boundary is shaped correctly from day one.

---

## Assumptions & Blockers

### Assumptions

1. **eShopOnWeb user ID is stable and globally unique within the Maxio site.** The integration uses this as the `reference` on Maxio customers for idempotent lookups. If user IDs can change or are not unique per site, the mapping strategy must be revised (e.g., store Maxio customer ID in app state).

2. **Subscription billing is a feature opt-in by the app; cart/checkout flow is unaffected.** The integration exposes three new endpoints and does not modify existing checkout behavior. Subscription data is managed separately and does not interfere with one-time purchases.

3. **Payment method collection is deferred or handled outside the Maxio API.** Per sandbox setup, all three plans are configured with **no payment method required** at subscription creation. If payment is to be collected later (via Maxio's billing portal, webhooks, or a separate payment step), that is out of scope here.

4. **Maxio site/subdomain (`cp-exp-2` for sandbox) is stable and pre-configured.** The integration assumes the site already exists and contains the product family handle `eshop-subscribe`, product handles `eshop-pro` and `basic-plan`, and metered component handle `api-call`. No product/plan creation is in scope.

5. **Persistent user ↔ Maxio customer mapping is the app's responsibility.** The integration does **not** implement a mapping store; it is the caller's job to persist and retrieve the Maxio customer ID for a given eShopOnWeb user (or always call `ReadCustomerByReference` as the lookup).

6. **Logging and observability are outside scope.** The plan does not include retry logging, metric collection, or structured logging; those are left to the implementer.

### Blockers

**None identified.** All required operations exist in the SDK; the sandbox site and product configuration are confirmed; the API contract is clear. Implementation can proceed.

---

## Summary

This plan covers the full contract for three endpoints (**`GET /api/subscription-plans`**, **`POST /api/subscriptions`**, **`GET /api/my-subscriptions`**) across five SDK operations (list products in family, read/create customers, create subscriptions, list subscriptions). All signatures, request/response models with wire names and required flags, error accessors, and configuration are specified. Seven companion skills must be loaded before implementation begins; a JsonException handling rule is mandatory in the error boundary. Assumptions are explicit, and no blockers remain.
