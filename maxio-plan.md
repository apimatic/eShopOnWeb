# Maxio Subscription Integration Plan — eShopOnWeb

## SDK Installation

**NuGet package:** `AsadAli.AdvancedBilling.Sdk` **version `1.0.2`** (the only version published on nuget.org; maps to SDK source tag `v1.0.2`, the ref the bundled SDK map was generated from).

```bash
dotnet add package AsadAli.AdvancedBilling.Sdk --version 1.0.2
```

**Important:** The NuGet package ID differs from the root namespace — install by package ID, but import with `using MaxioAdvancedBilling;` in code.

---

## Scope & sequence

| # | Step | Operations |
|---|------|-----------|
| 1 | Client & DI setup | *SDK client initialization with HTTP factory & credentials* |
| 2 | Configuration binding | Map Maxio config keys to `MaxioAdvancedBillingClientOptions` |
| 3 | Idempotent customer lookup/creation | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 4 | Product family + product listing | `Products.ListProductsForProductFamily` |
| 5 | Idempotent subscription creation | `Subscriptions.CreateSubscription` |
| 6 | Subscription read for display | `Subscriptions.ReadSubscription` |
| 7 | API endpoint wiring (PublicApi, JWT-auth) | GET /api/subscription-plans, POST /api/subscriptions, GET /api/my-subscriptions |
| 8 | End-to-end tests | Sandbox hero-flow validation (login → plan list → subscribe → view subscription) |

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

| Step | Controller·Method | Signature | Request Model | Response Envelope | Error Case | Pagination | Source |
|------|---|---|---|---|---|---|---|
| 3 | `Customers.ReadCustomerByReference` | `ReadCustomerByReference(reference: string, ct: CancellationToken = default)` → `CustomerResponse` | Query param: `reference` (wire name) | `CustomerResponse { Customer (customer): Customer !req }` — read `.Customer.Reference` to validate lookup | **Case B** (`SdkException<RawError>`) — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | none | `operations/Customers.md` |
| 3 | `Customers.CreateCustomer` | `CreateCustomer(body: CreateCustomerRequest?, ct: CancellationToken = default)` → `CustomerResponse` | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }` · `CreateCustomer { FirstName (first_name): string !req, LastName (last_name): string !req, Email (email): string !req, Reference (reference): string?, … }` | `CustomerResponse { Customer (customer): Customer !req }` — read `.Customer.Id`, `.Reference` | **Case A** (`SdkException<CreateCustomerError>`) — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |
| 4 | `Products.ListProductsForProductFamily` | `ListProductsForProductFamily(productFamilyId: string, dateField: BasicDateField?, filter: ListProductsFilter?, startDate: DateTimeOffset?, endDate: DateTimeOffset?, startDatetime: DateTimeOffset?, endDatetime: DateTimeOffset?, includeArchived: bool?, include: ListProductsInclude?, page: int? = 1, perPage: int? = 20, ct: CancellationToken = default)` → `IReadOnlyList<ProductResponse>` | Path: `productFamilyId` (wire: `product_family_id`) · Query: 8 optional params (pass `null` to skip); defaults `page=1`, `perPage=20` | `IReadOnlyList<ProductResponse>` — each element `{ Product (product): Product !req }` — read `.Product.Handle` (wire: `handle`), `.Product.PriceInCents`, `.Product.Interval`, `.Product.IntervalUnit` | **Case A** (`SdkException<ListProductsForProductFamilyError>`) — `TryGetString(out string)` [404], `TryGetRawError(out RawError)` [fallback] | manual `page` + `perPage` | `operations/ProductFamilies.md` |
| 5 | `Subscriptions.CreateSubscription` | `CreateSubscription(body: CreateSubscriptionRequest?, ct: CancellationToken = default)` → `SubscriptionResponse` | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }` · `CreateSubscription` fields: `CustomerId (customer_id): int?`, `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `Reference (reference): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — no payment profile required per spec (plan has no trial/setup fee, payment NOT required) | `SubscriptionResponse { Subscription (subscription): Subscription !req }` — read `.Subscription.Id`, `.Subscription.State` (wire: `state`: enum `SubscriptionState`), `.Subscription.CurrentPeriodEndsAt` | **Case A** (`SdkException<CreateSubscriptionError>`) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| 6 | `Subscriptions.ReadSubscription` | `ReadSubscription(subscriptionId: int, include: IReadOnlyList<SubscriptionInclude>?, ct: CancellationToken = default)` → `SubscriptionResponse` | Path: `subscriptionId` · Query: `include` (wire: `include`) — optional list | `SubscriptionResponse { Subscription (subscription): Subscription !req }` | **Case B** (`SdkException<RawError>`) | none | `operations/Subscriptions.md` |

### Enums (wire values used in API contracts)

| Enum | C# Member (wire value) | Purpose | Source |
|------|---|---|---|
| `SubscriptionState` | `Active` (`"active"`), `Pending` (`"pending"`), `Trialing` (`"trialing"`), `Canceled` (`"canceled"`), `Expired` (`"expired"`), … | Subscription lifecycle state (read from response) | `map/models/enums.md` |
| `CollectionMethod` | `Automatic` (`"automatic"`), `Remittance` (`"remittance"`) | How subscription payment is collected | `map/models/enums.md` |

### Client construction & auth

| Item | Type | Binding Key / Config | Default | Source |
|---|---|---|---|---|
| **API Key** | `string` | `Maxio:ApiKey` (from `MAXIO_API_KEY` env var) | *none* | `dotnet-authentication` |
| **Auth scheme** | `BasicAuthCredentials` | `Username` = API key, `Password` = literal `"x"` | — | `sdk-map.md` § Getting a client |
| **Site subdomain** | `string` | `Maxio:Subdomain` (from `MAXIO_SITE_SUBDOMAIN` env var) | *none* | `dotnet-configuration-resilience` |
| **Product family handle** | `string` | `Maxio:ProductFamilyHandle` (from `MAXIO_DEFAULT_PRODUCT_FAMILY` env var) | *none* | YOUR CALL — not in map |
| **Base URL override** | `string?` | `Maxio:BaseUrl` (optional override) | `https://{Subdomain}.chargify.com` (US, default) | `sdk-map.md` § Servers & auth |
| **Environment** | `ServerEnvironment` | `MAXIO_ENVIRONMENT` env var → `ServerEnvironment.Us` or `.Eu` | `ServerEnvironment.Us` | `sdk-map.md` |
| **HttpClient** | `System.Net.Http.HttpClient` | DI-registered, long-lived, reused via `IHttpClientFactory` | *required* | `dotnet-client-initialization` |

**Client options construction (pseudocode):**
```csharp
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = config["Maxio:ApiKey"],  // API key
        Password = "x"                       // literal
    },
    Environment = (config["Maxio:Environment"] == "EU") 
        ? ServerEnvironment.Eu 
        : ServerEnvironment.Us,
    Server = new ServerOptions
    {
        Production = new ProductionOptions
        {
            Us = new ServerConfig 
            { 
                Site = config["Maxio:Subdomain"],
                BaseUrl = config["Maxio:BaseUrl"]  // if present, override default
            }
        }
    }
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

---

## Trap notes

1. **Step 1 (Client & DI)** — `IHttpClientFactory` is required; the SDK's `HttpClient` constructor parameter is the test seam and must be a long-lived instance reused across requests, not recreated per call. **MUST load `dotnet-client-initialization`** before wiring the client into DI or creating it directly.

2. **Step 2 (Config)** — Binding keys are literal strings (`"Maxio:ApiKey"`, etc.); do not invent alternate key names. The auth credentials follow HTTP Basic: Username = API key (from config), Password = literal `"x"` (not a placeholder). **MUST load `dotnet-authentication`** to confirm credential handling.

3. **Step 3 (Customer lookup)** — `ReadCustomerByReference` returns 404 as a typed Case-A error *and* as Case-B `RawError`; distinguish by attempting the `TryGetString()` accessor first. **Response wrapping:** `CustomerResponse.Customer` is the payload — do not read fields directly from `CustomerResponse`. Create idempotent by: (a) attempt `ReadCustomerByReference(reference: userId.ToString())`, (b) on 404, call `CreateCustomer` with the user's email + first/last name from the app identity, setting `Reference` to the userId. **MUST load `dotnet-error-handling`** to write the catch ladder.

4. **Step 4 (Product listing)** — `ListProductsForProductFamily` requires `productFamilyId` as a path parameter (wire: `product_family_id`). If only the product family handle is known ("eshop-subscribe"), **a second lookup is needed first** — the map does not carry a `ReadProductFamilyByHandle` operation; use `ListProductFamilies()` and filter in-memory for `Handle == "eshop-subscribe"`, then pass the ID. **Response wrapping:** each element is `ProductResponse { Product }` — read `.Product.Handle`, `.Product.PriceInCents` (wire: `price_in_cents`, in cents not dollars), `.Product.Interval`, `.Product.IntervalUnit`. On 404 (family not found), the error surface is `ListProductsForProductFamilyError.TryGetString()` returning the message, or a raw 404.

5. **Step 5 (Subscription creation)** — `CreateSubscription` request model is nested: `CreateSubscriptionRequest { Subscription: CreateSubscription { CustomerId, ProductHandle, … } }`. No payment profile is required per spec (the Pro/Basic plans have no trial/setup fee and payment is not required for signup). To set the product by handle (not ID), pass `ProductHandle: "eshop-pro"` or `"basic-plan"` in the `CreateSubscription` object; **either `ProductHandle` or `ProductId` is acceptable**, but only one should be set. The `Reference` field (wire: `reference`) is optional but recommended for idempotency — set it to a unique key (e.g., `${userId}-${productHandle}-${timestamp}`) or an idempotency UUID. `PaymentCollectionMethod` (wire: `payment_collection_method`) defaults to the site's default; if needed, pass `CollectionMethod.Automatic` or `CollectionMethod.Remittance`. On 422 (validation error), `CreateSubscriptionError.TryGetErrorListResponse1()` carries the error list (e.g., missing email if customer is new). **Response wrapping:** `SubscriptionResponse.Subscription` is the payload; read `.Subscription.Id` (the Maxio subscription ID), `.Subscription.State` (enum, wire value `"active"`), `.Subscription.CurrentPeriodEndsAt` (next billing date). **MUST load `dotnet-calling-endpoints`** (parameter order, named arguments, nullable handling).

6. **Step 6 (Subscription read)** — `ReadSubscription` takes `subscriptionId` (the Maxio ID returned from create) and optional `include` list (e.g., `SubscriptionInclude.Customer` to embed customer data). **Response wrapping:** `SubscriptionResponse.Subscription` is the payload.

7. **Step 7 (API endpoints)** — All three PublicApi endpoints use JWT auth (not SDK-managed; your app's auth boundary). Inside each endpoint, the SDK client is a dependency (DI or singleton). For `POST /api/subscriptions`, extract the logged-in user's ID/email/name from the JWT claims, pass them to the Maxio flow (customer lookup/creation, then subscription creation). For `GET /api/my-subscriptions`, use the user ID to query the local in-memory mapping (userId → Maxio subscription ID), then call `ReadSubscription(subscriptionId)` for each; cache or filter in-memory to avoid per-user list calls if possible. **Do not** call `ListSubscriptions` with a customer filter unless the spec requires listing all site subscriptions.

8. **Error boundary — TWO critical JsonException cases (read BEFORE writing catch logic):**
   - **Drifted 2xx body** (missing `required` field): `JsonException` thrown during deserialization of a 2xx response, *not* caught as `SdkException` — let it escape to the boundary as an outage signal.
   - **Non-2xx body mismatch**: a 422+ response body that does not match the operation's `{Operation}Error` shape throws `JsonException` *during construction of the error object*, replacing the `SdkException` and destroying the HTTP status — map every `JsonException` to a 5xx, then **never** retry 5xx, or else a caller's retry loop retries something that can never succeed. **MUST load `dotnet-error-handling`** BEFORE writing the boundary.

---

## REQUIRED READING

These companion skills carry binding details the contract sheet deliberately omits. Load **before implementation starts**:

| Skill | Governs |
|---|---|
| **`dotnet-client-initialization`** | Step 1: HttpClient factory lifetime, options object construction, DI registration via `AddMaxioAdvancedBillingClient`. |
| **`dotnet-authentication`** | Step 2: BasicAuthCredentials wiring, credential rotation, per-environment config. |
| **`dotnet-calling-endpoints`** | Step 5: Parameter order, required-but-nullable flags, named arguments, async/`await`, cancellation tokens. |
| **`dotnet-models`** | Step 3–5: Request/response envelope unwrapping, union construction (`TryGet…`), `StringEnum<T>` membership vs wire values. |
| **`dotnet-error-handling`** | Steps 3–6: SdkException catch ladder (Case A vs B), `TryGet…` accessors, JsonException handling at the boundary. |
| **`dotnet-configuration-resilience`** | Step 2: Retry/timeout options, per-attempt vs total timeout semantics, logging hooks, idempotency caveats (POST is retried on transport failure; check if subscription creation is safe to retry). |

---

## Assumptions & Blockers

### Assumptions

- **Idempotency via `Reference` field:** both `CreateCustomer` and `CreateSubscription` accept a `Reference` field (wire name). For customers, set it to the app's user ID (as string). For subscriptions, either rely on Maxio's duplicate-detection on (customer, product, period start) or set `Reference` to a unique key. The plan assumes the app will manage the user↔subscription mapping in-memory (as stated in spec: "in-memory DB, no persistence across restart; userId ↔ subscription mapping local to run").
- **No payment profile required:** The spec states the Pro and Basic plans have "no trial/setup fee, payment method NOT required"; thus `CreateSubscription` will not include payment profile attributes (`PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`). The plan assumes Maxio's sandbox configuration allows this.
- **Product family handle is known:** The spec names the product family handle as "eshop-subscribe"; the plan assumes this handle exists in the sandbox and is retrievable via `ListProductFamilies()` or directly via a 1-based ID if that operation were available.
- **JWT auth is in-app:** The PublicApi endpoints (GET /api/subscription-plans, POST /api/subscriptions, GET /api/my-subscriptions) are protected by the app's JWT auth (not SDK-managed). The app extracts user identity from the JWT and passes it to the Maxio flow.
- **In-memory subscription tracking:** The spec says "in-memory DB, no persistence across restart; userId ↔ subscription mapping local to run". The plan assumes the app maintains a `Dictionary<int, int>` or similar (userId → Maxio subscriptionId) at runtime, or queries Maxio via customer lookup on each request.

### Blockers

- **None identified.** All required operations are available on the SDK map. The sandbox catalog is assumed to exist as specified (product family "eshop-subscribe", products "eshop-pro" and "basic-plan").

---

**File:** `C:\claude-runs\t1h45ali-maxio-sdk-haiku45high-080\repo\maxio-plan.md`

**Summary:** Maxio subscription integration plan for eShopOnWeb hero flow (login → plan list → subscribe → view). Seven implementation steps grounded in SDK operations: customer lookup/creation (idempotent via Reference), product family + product listing, subscription creation, and three PublicApi endpoints backed by JWT auth. All signatures, error cases, and response envelopes are taken verbatim from the bundled SDK map; the five companion dotnet-* skills must be loaded before coding to resolve traps around client lifetime, auth wiring, error boundaries, and retry semantics.

**Assumptions & Blockers:**
- Assumptions: idempotency via Reference field, no payment profile required (spec-compliant), product family handle known, JWT auth in-app, in-memory subscription tracking.
- Blockers: none.
