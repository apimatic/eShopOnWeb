# Maxio Subscription Billing Integration — eShopOnWeb

## Scope & Sequence

1. **Client initialization & configuration** — register `MaxioAdvancedBillingClient` with DI; load API key and Maxio host from configuration (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:BaseUrl`, `Maxio:ProductFamilyHandle`).
2. **List subscription plans** (GET /api/subscription-plans) — call `client.ProductFamilies.ListProductsForProductFamily(...)` to list plans in the eshop-subscribe product family.
3. **Idempotent customer lookup or creation** — before creating a subscription, call `client.Customers.ReadCustomerByReference(...)` to find or create a customer.
4. **Create subscription** (POST /api/subscriptions) — call `client.Subscriptions.CreateSubscription(...)` to attach a user to a plan.
5. **Retrieve user's subscriptions** (GET /api/my-subscriptions) — call `client.Customers.ListCustomerSubscriptions(...)` to fetch active subscriptions.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Step | Operation | Signature | Request Model & Fields | Response Envelope & Fields | Error Case | Pagination | Source |
|------|-----------|-----------|------------------------|----------------------------|------------|-----------|--------|
| **1** | Clients.ProductFamilies.ListProductsForProductFamily | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `productFamilyId` is the product family handle or ID string (e.g. `"eshop-subscribe"` or `"3023074"`). All optional params default to `null`. | Query string params: `page`, `per_page`, `date_field`, `filter`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `include_archived`, `include`. For this call, pass `null` for all optional date/filter params; use `page=1, perPage=20` (or omit for defaults). | Returns `IReadOnlyList<ProductResponse>` — each wraps one `Product (product): Product` field. Extract: `Product.Id`, `Product.Handle`, `Product.Name`, `Product.PriceInCents`, `Product.Interval`, `Product.IntervalUnit`. | **Case A — `SdkException<ListProductsForProductFamilyError>`** with `TryGetString(out string)` [404 — family not found] + `TryGetRawError(out RawError)` [fallback]. | Manual `page`+`perPage`. | `operations/ProductFamilies.md` |
| **2a** | Clients.Customers.ReadCustomerByReference | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` is the user's identity (e.g. user ID or email from JWT claims). | Query string param: `reference` ← `reference`. | Returns `CustomerResponse` — wraps `Customer (customer): Customer` field. Extract: `Customer.Id` (Maxio ID). On 404, customer does not exist (see step 2b). | **Case B — `SdkException<RawError>`** with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, etc. HTTP 404 means no match. | None | `operations/Customers.md` |
| **2b** | Clients.Customers.CreateCustomer | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` — nullable, no default → **must pass explicitly**. | Request wrapper: `CreateCustomerRequest` contains `Customer (customer): Customer!req` (required). Inner `Customer` type is the record `Customer` (not a request model; reuse core model) with fields: `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?` (app's user ID — required for idempotency; only field that must be set). Other optional fields: `Organization`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`. | Returns `CustomerResponse` — wraps `Customer (customer): Customer!req` field. Extract: `Customer.Id`. | **Case A — `SdkException<CreateCustomerError>`** with `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] + `TryGetRawError(out RawError)` [fallback]. | None | `operations/Customers.md` |
| **3** | Clients.Subscriptions.CreateSubscription | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` — nullable, no default → **must pass explicitly**. | Request wrapper: `CreateSubscriptionRequest` contains `Subscription (subscription): CreateSubscription!req` (required, nested record). Inner `CreateSubscription` type fields (all optional): `ProductHandle (product_handle): string?` or `ProductId (product_id): int?` — pass product handle (e.g. "eshop-pro") or product ID. `CustomerId (customer_id): int?` (from step 2a/2b). Additional optional: `Reference (reference): string?` (idempotency key — app's subscription reference), `PaymentProfileId (payment_profile_id): int?` (if payment already stored), `CustomerReference (customer_reference): string?` (alternative to `CustomerId`). Notes: payment collection method defaults on the product; if the product requires payment, `PaymentProfileId` or payment attributes must be supplied (out of scope for this plan — payment setup is deferred). | Returns `SubscriptionResponse` — wraps `Subscription (subscription): Subscription?` field. Extract: `Subscription.Id`, `Subscription.State`, `Subscription.CurrentPeriodEndsAt`, `Subscription.NextBillingAt`. | **Case A — `SdkException<CreateSubscriptionError>`** with `TryGetErrorListResponse1(out ErrorListResponse1)` [422 — validation failed, e.g. payment missing] + `TryGetRawError(out RawError)` [fallback]. | None | `operations/Subscriptions.md` |
| **4** | Clients.Customers.ListCustomerSubscriptions | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `customerId` is the Maxio customer ID (from step 2a or 2b). | No request body; `customerId` is path param. | Returns `IReadOnlyList<SubscriptionResponse>` — each wraps `Subscription (subscription): Subscription?` field. Extract per subscription: `Subscription.Id`, `Subscription.ProductId` / `Subscription.ProductHandle`, `Subscription.State`, `Subscription.CurrentPeriodEndsAt`. | **Case B — `SdkException<RawError>`** with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`. | None | `operations/Customers.md` |

### Enums (values used in this plan)

From `map/models/enums.md`:

| Enum | C# Members (wire values in parens) | Source | Usage |
|------|-------|--------|-------|
| `CollectionMethod` | `Invoice` (`"invoice"`), `Automatic` (`"automatic"`), `Remittance` (`"remittance"`) | `Models/Enums/CollectionMethod.cs` | Payment collection; set on product or subscription. Default typically inherited from product. |

### Client construction & configuration

From `sdk-map.md`:

- Root namespace: `MaxioAdvancedBilling`
- Client: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`
- Options namespace: `MaxioAdvancedBilling` (options is `MaxioAdvancedBillingClientOptions`)
- Auth namespace: `MaxioAdvancedBilling.Core.Authentication.Basic`
- Server namespace: `MaxioAdvancedBilling.Servers`

**Basic Auth:**
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" },
    Environment = ServerEnvironment.Us,
    Server = new ServerOptions
    {
        Production = new ProductionOptions
        {
            Us = new ServerOptions
            {
                Site = "<subdomain>",  // e.g. "cp-exp-3"
                // Optional: override BaseUrl for mock/dev:
                // BaseUrl = "http://localhost:8080"
            }
        }
    }
};

var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Configuration binding:**
- Bind from appsettings: `Maxio:ApiKey` → BasicAuth.Username
- Bind from appsettings: `Maxio:Subdomain` → ServerOptions.Production.Us.Site
- Bind from appsettings: `Maxio:BaseUrl` (optional) → ServerOptions.Production.Us.BaseUrl
- Bind from appsettings: `Maxio:ProductFamilyHandle` → use in step 1 call

**DI alternative** (reuse existing pattern):
```csharp
services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" };
    // Customize Server.Production.Us.Site, etc. if needed
});
```

---

## Trap Notes

⚠ **Step 1 (client registration)** — the SDK's `HttpClient` must be long-lived and reused; do not create a new one per request. Use `IHttpClientFactory` or register the SDK client via DI so the framework manages the HTTP pipeline. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Step 1 (authentication)** — Basic Auth requires `Username = API key` (not email), `Password = literal "x"`. API keys are issued in the Maxio UI and must be loaded from secure configuration (env vars, secrets vault), **never hardcoded**. **MUST load `dotnet-authentication`** before setting credentials.

⚠ **Step 1 (server & host)** — the sandbox subdomain is `cp-exp-3`. Ensure `ServerOptions.Production.Us.Site` (or the config binding) is set correctly. Default environment is `ServerEnvironment.Us`; only override to `.Eu` if your Maxio account is EU-hosted. If testing against a mock/dev host, override `ServerOptions.Production.Us.BaseUrl` (e.g., to `http://localhost:8080`). **MUST load `dotnet-configuration-resilience`** to understand retry/timeout boundaries and per-attempt vs. total.

⚠ **Step 2a (customer lookup)** — `ReadCustomerByReference` returns 404 if no match; do **not** treat 404 as an error in the integration boundary — it signals "customer does not exist, proceed to 2b." Catch the `SdkException<RawError>` and check `StatusCode == HttpStatusCode.NotFound` to branch to creation.

⚠ **Step 2b (idempotent customer creation)** — set `Customer.Reference` to the app's user ID (e.g., the JWT `sub` claim). On a retry, a duplicate `Reference` will be rejected with a 422 error ("reference must be unique"). The integration must handle this: either re-query by reference (idempotent), or track which customers are already created in the app. **MUST load `dotnet-error-handling`** to parse the 422 error shape (`CustomerErrorResponse1` and its nested `Errors` field).

⚠ **Step 3 (create subscription)** — the `CreateSubscription` model is a flat record with dozens of optional fields. Only set the required/relevant ones: `ProductHandle` or `ProductId`, `CustomerId` or `CustomerReference`, and (if needed) `Reference` for idempotency. **Do not set fields you don't understand — unmodeled or misspelled JSON names are dropped on deserialize.** The provider's API docs clarify which fields are accepted for each product. **MUST load `dotnet-models`** before building the request.

⚠ **Step 3 (payment on subscription creation)** — if the product requires payment and the customer has no default payment profile, the call will fail with 422 "payment profile required" (or similar). This plan assumes payment is optional (deferred to a separate flow, e.g., update subscription with payment later, or use a payment gateway pre-auth). If payment is **required on signup**, wire `PaymentProfileId` or payment attributes (`CreditCardAttributes`, `BankAccountAttributes`) — out of scope here; see product configuration on the Maxio UI. **MUST load `dotnet-error-handling`** to distinguish a payment error (422 with a `payment_profile` error key) from other validation errors.

⚠ **Step 3 & 4 (response envelope)** — `SubscriptionResponse` wraps the actual subscription data in a single field: `Subscription (subscription): Subscription?`. Always read one level down: `response.Subscription.Id`, not `response.Id`. Likewise, `ProductResponse.Product`, `CustomerResponse.Customer`. **MUST load `dotnet-calling-endpoints`** before first call to confirm which fields to read from the response.

⚠ **Error boundary — two sources of `JsonException`:**
  - A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
  - A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the integration boundary to handle both cases.

---

## REQUIRED READING

Load **before implementation starts**. The sheet deliberately does not carry their contents; these are the companion skills that govern usage, traps, and best practices:

| Skill | Step(s) | Governs |
|-------|---------|---------|
| `dotnet-client-initialization` | 1 | Client & DI registration; `HttpClient` lifecycle; service collection setup |
| `dotnet-authentication` | 1 | Basic Auth; credential loading; per-environment config |
| `dotnet-calling-endpoints` | 2–4 | Operation signatures; required vs. optional params; calling patterns; response unwrapping |
| `dotnet-models` | 2–4 | Request/response record construction; enum/union handling; optional field defaults |
| `dotnet-error-handling` | 2–4 | Exception boundaries; Case A typed errors vs. Case B raw errors; `TryGet…` accessors; `JsonException` handling |
| `dotnet-configuration-resilience` | 1 | Retry/timeout semantics; per-attempt vs. total; base-URL override; resilience tuning |

---

## Assumptions & Blockers

### Assumptions

1. **Payment profile optional on MVP.** The plan assumes subscription creation succeeds without payment on the initial call (i.e., the product's payment collection method is non-mandatory, or the site allows "pay later" workflows). If the product **requires** payment upfront, the integration must wire payment attributes on `CreateSubscription` or accept a 422 error and fall back to a separate payment flow (out of scope here).

2. **User identity is stable.** The app's user ID (or email) is set as `Customer.Reference` and is immutable. This ensures idempotent customer lookup: if `ReadCustomerByReference(userRef)` succeeds, that customer is always the same user; if it fails with 404, a new customer can be safely created with that reference.

3. **Product Family handle is known.** The integration uses the product family handle `"eshop-subscribe"` (from the sandbox: ID 3023074). If the handle changes or multiple families are in scope, the `Maxio:ProductFamilyHandle` config binding allows runtime override.

4. **Environment is US-hosted.** The plan hardcodes `ServerEnvironment.Us`. If the production Maxio account is EU-hosted, `ServerEnvironment.Eu` must be set in configuration or code before deployment.

5. **No webhook ingestion.** This plan covers synchronous REST calls only; async billing events (webhooks) are not included.

### Blockers

None. The SDK operations, error types, and response shapes are all well-defined in the map. All required fields for the hero flow are present in the generated models.

