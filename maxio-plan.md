# Maxio Subscription Integration Plan — eShopOnWeb Reference App

## Scope & Sequence

1. **Client setup & DI registration** — Initialize `MaxioAdvancedBillingClient` with Basic auth (API key, literal `"x"`); configure subdomain/base URL from `Maxio:*` config keys; register in DI container for PublicApi
2. **Product Family & Plan Enumeration** (`GET /api/subscription-plans`) — Fetch the `eshop-subscribe` family, then list active plans (Pro $299/mo, Basic $29/mo) from that family
3. **Customer Idempotency** — On subscription request, check if user already has a Maxio customer (lookup by `reference` = eShopWeb userId); create one if absent
4. **Subscription Creation** (`POST /api/subscriptions`) — Create subscription by linking authenticated user to selected plan; return state, price, next-billing-date
5. **Subscription Retrieval** (`GET /api/my-subscriptions`) — List user's subscriptions by customer lookup; return active subscription states
6. **Error Boundary & Logging** — Centralized exception handling for API errors (422 validation, 404 not found, timeout/network); log and transform to HTTP problem details

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation / Item | Method Signature / Type / Value | Source | Notes |
|---|---|---|---|
| **Fetch Product Family** | `client.ProductFamilies.ListProductFamilies(null, null, null, null, null, ct)` returns `IReadOnlyList<ProductFamilyResponse>` | operations/ProductFamilies.md | Case B error (`SdkException<RawError>`); call with all `null` params to skip filters; search result for `.ProductFamily?.Handle == "eshop-subscribe"` in client code (no filtering on SDK side) |
| **List Products in Family** | `client.ProductFamilies.ListProductsForProductFamily("eshop-subscribe", null, null, null, null, null, null, null, null, page: 1, perPage: 20, ct)` returns `IReadOnlyList<ProductResponse>` | operations/ProductFamilies.md | Case A error: `SdkException<ListProductsForProductFamilyError>` with `TryGetString(out string)` [404] accessor; `productFamilyId` is the family **handle** (not ID); pagination manual with `page`/`perPage` defaults; each response wraps a `Product` at `response.Product` |
| **Lookup Customer by Reference** | `client.Customers.ReadCustomerByReference(userIdString, ct)` returns `CustomerResponse` | operations/Customers.md | Case B error (`SdkException<RawError>`); `reference` query param maps C# parameter name `reference`; 404 means no customer yet; response wraps `Customer` at `.Customer` |
| **Create Customer (Idempotent)** | `client.Customers.CreateCustomer(new CreateCustomerRequest { Customer = new CreateCustomer { FirstName = "John", LastName = "Doe", Email = "john@example.com", Reference = userIdString, ... } }, ct)` returns `CustomerResponse` | operations/Customers.md | Case A error: `SdkException<CreateCustomerError>` with `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] accessor; `Reference` field is **optional but strongly recommended** — it must be unique and maps userId for idempotency; `FirstName`, `LastName`, `Email` are **required**; response wraps customer at `.Customer` |
| **Create Subscription** | `client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest { Subscription = new CreateSubscription { ProductHandle = "eshop-pro" /* or ProductId: productId */, CustomerId = customerId /* or CustomerReference: userIdString */, ... } }, ct)` returns `SubscriptionResponse` | operations/Subscriptions.md | Case A error: `SdkException<CreateSubscriptionError>` with `TryGetErrorListResponse1(out ErrorListResponse1)` [422] accessor; either `ProductHandle` OR `ProductId` required; either `CustomerId` OR `CustomerReference` required; **Payment method is NOT required** (per site config); response wraps subscription at `.Subscription` |
| **List Customer Subscriptions** | `client.Customers.ListCustomerSubscriptions(customerId, ct)` returns `IReadOnlyList<SubscriptionResponse>` | operations/Customers.md | Case B error (`SdkException<RawError>`); each item wraps a `Subscription` at `.Subscription` |
| `MaxioAdvancedBillingClient` (constructor) | `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | sdk-map.md | Required: `httpClient` must be long-lived (reused via `IHttpClientFactory`, not per-request); `options` must include `BasicAuth` and `Environment`; **see companion skill `dotnet-client-initialization`** |
| `MaxioAdvancedBillingClientOptions` | Properties: `BasicAuth` (`BasicAuthCredentials?`), `Environment` (`ServerEnvironment`), `Retry` (`RetryOptions`), `Server` (`ServerOptions`) | sdk-map.md | Must set `BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" }`; `Environment` defaults to `ServerEnvironment.Us`; `Server.Production.Us.BaseUrl` and `.Site` override subdomain derivation |
| `BasicAuthCredentials` | Fields: `Username` (your API key), `Password` (literal `"x"`) | sdk-map.md, namespace `MaxioAdvancedBilling.Core.Authentication.Basic` | Always use exactly `"x"` as password; username is the API key from Maxio dashboard |
| `ServerEnvironment` | `ServerEnvironment.Us` (default), `ServerEnvironment.Eu` | sdk-map.md, namespace `MaxioAdvancedBilling.Servers` | US = `https://{site}.chargify.com`, EU = `https://{site}.ebilling.maxio.com`; site defaults to subdomain from config |
| **Error: Case A (Typed)** | `SdkException<CreateCustomerError>` or `SdkException<CreateSubscriptionError>`, etc. | sdk-map.md | Throw-only (no Result variants); call `TryGet…(out …)` accessors to extract status-specific payload; always check fallback `TryGetRawError(out RawError)` for unmapped statuses |
| **Error: Case B (Raw)** | `SdkException<RawError>` | sdk-map.md | Throw-only; access `.StatusCode: HttpStatusCode`, `.ReadAsString(): string`, `.ReadAsJson<T>(): T?`; use when operation has no typed error class |
| **SubscriptionState enum** | Members: `Pending`, `Trialing`, `Active`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `OnHold`, `AwaitingSignup`, `Unpaid`, `TrialEnded`, `Assessing`, `SoftFailure`, `FailedToCreate` (wire: `pending`, `trialing`, `active`, etc.) | enums.md, namespace `MaxioAdvancedBilling.Models.Enums` | Use `SubscriptionState.Active`, not C# enum syntax; read from `Subscription.State: SubscriptionState?` |
| **CollectionMethod enum** | Members: `Automatic`, `Remittance`, `Prepaid`, `Invoice` (wire: `automatic`, `remittance`, `prepaid`, `invoice`) | enums.md, namespace `MaxioAdvancedBilling.Models.Enums` | Optional on subscription create; controls payment collection mode |
| `CreateCustomer` record (request model) | **Required fields**: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`; **Optional but recommended**: `Reference (reference): string` (unique identifier for idempotency); Optional: `Organization`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `ParentId`, etc. | records-2-Cr-Ne.md, namespace `MaxioAdvancedBilling.Models` | Wire names in parentheses; immutable record with `init`-only setters; `Reference` must be unique per site; no default value for required fields |
| `CreateSubscription` record (request model) | **Either one required**: `ProductHandle (product_handle): string?` **OR** `ProductId (product_id): int?`; **Either one required**: `CustomerId (customer_id): int?` **OR** `CustomerReference (customer_reference): string?`; Optional: `CouponCode`, `PaymentProfileId`, `PaymentCollectionMethod`, `NextBillingAt`, `Reference`, `CustomerAttributes` (inline customer creation), `Components` (add-ons), `MetaFields`, etc. | records-2-Cr-Ne.md, namespace `MaxioAdvancedBilling.Models` | Immutable; **payment method not required** on the plan's site; `CustomerAttributes` allows create-customer-and-subscribe in one call (alternative to prior `CreateCustomer` call); `Reference` on subscription for your own tracking |
| `Customer` record (response payload) | `Id (id): int?`, `FirstName`, `LastName`, `Email`, `Reference`, `CreatedAt`, `UpdatedAt`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `Verified`, `TaxExempt`, `Locale`, plus internal fields (`PortalCustomerCreatedAt`, `DefaultSubscriptionGroupUid`, `Maxioid`, etc.) | records-2-Cr-Ne.md, namespace `MaxioAdvancedBilling.Models` | Returned nested in `CustomerResponse.Customer`; **`Id` is Maxio's generated customer ID** (save for later API calls); `Reference` echoes what you set (userId in this design) |
| `Subscription` record (response payload) | `Id (id): int?`, `State (state): SubscriptionState?`, `ProductId`, `ProductPricePointId`, `Customer` (nested Customer), `Product` (nested Product), `CurrentPeriodStartedAt`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `TrialStartedAt`, `TrialEndedAt`, `ActivatedAt`, `CanceledAt`, `CreatedAt`, `UpdatedAt`, `BalanceInCents`, `PaymentCollectionMethod`, `Reference`, `CouponCodes`, `Coupons` (array), plus many derived fields | records-3-Of-Su.md, namespace `MaxioAdvancedBilling.Models` | All optional (no `required` fields); returned nested in `SubscriptionResponse.Subscription`; **`State` is the subscription status** (watch for `Active`, `PastDue`, `Suspended`, `Canceled`); nesting: `.Customer.Email`, `.Product.Name`, `.Product.Handle` |
| `Product` record (response payload) | `Id`, `Name`, `Handle`, `Description`, `PriceInCents` (long, in cents), `Interval` (int, billing cycle qty), `IntervalUnit` (enum: `Day`, `Month`), `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `ProductFamily` (nested ProductFamily), `TaxCode`, `RequireCreditCard`, `CreatedAt`, `UpdatedAt`, etc. | records-3-Of-Su.md, namespace `MaxioAdvancedBilling.Models` | All optional; nested in `Subscription.Product` and `ProductResponse.Product`; `PriceInCents` is the per-interval price in cents (299.00/mo = 29900 cents) |
| `ProductFamily` record (response payload) | `Id`, `Name`, `Handle`, `Description`, `AccountingCode`, `CreatedAt`, `UpdatedAt`, `ArchivedAt` | records-3-Of-Su.md, namespace `MaxioAdvancedBilling.Models` | All optional; nested in `Product.ProductFamily` and returned in `ProductFamilyResponse.ProductFamily`; **`Handle` is the lookup key** ("eshop-subscribe") |
| `CustomerResponse` envelope | `Customer (customer): Customer !req` (required field) | records-2-Cr-Ne.md, namespace `MaxioAdvancedBilling.Models` | Wraps single `Customer` object; immutable record |
| `SubscriptionResponse` envelope | `Subscription (subscription): Subscription?` (optional field) | records-4-Su-We.md, namespace `MaxioAdvancedBilling.Models` | Wraps single `Subscription` object (may be null); immutable record |
| `ProductResponse` envelope | `Product (product): Product !req` (required field) | records-3-Of-Su.md, namespace `MaxioAdvancedBilling.Models` | Wraps single `Product` object; immutable record |
| `ProductFamilyResponse` envelope | `ProductFamily (product_family): ProductFamily?` (optional field) | records-3-Of-Su.md, namespace `MaxioAdvancedBilling.Models` | Wraps single `ProductFamily` object (may be null); immutable record |
| Configuration keys (appsettings.json binding) | `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional override) | YOUR CALL — not in the map | All read from environment vars / user-secrets; default `ProductFamilyHandle` = "eshop-subscribe"; `BaseUrl` only if redirecting to mock/dev host |
| **Idempotency Implementation** | Call `ReadCustomerByReference(userId)` before `CreateCustomer`; if 404 (not found), call `CreateCustomer` with `Reference = userId`; if found, reuse `customer.Id` for subscription creation | YOUR CALL — not in the map | SDK does **not** prevent duplicate customers with same reference; **your code must enforce idempotency** via lookup-first pattern |
| **Double-click Prevention on Subscription** | Store subscription state in eShopWeb session/cache after successful creation; check state before allowing second `CreateSubscription` call in same session; or use Maxio subscription lookup by customer + product combination | YOUR CALL — not in the map | SDK has no built-in deduplication; **your code prevents double-submit** by tracking subscription existence locally or verifying via `ListCustomerSubscriptions` before create |

---

## Enum Value Tables

### SubscriptionState (wire `subscription_state` on list filters)
| C# Member | Wire Value | Meaning |
|---|---|---|
| `Pending` | `pending` | Awaiting first payment or setup |
| `Trialing` | `trialing` | In trial period |
| `Active` | `active` | Subscribed and in-period |
| `SoftFailure` | `soft_failure` | Payment retry in progress |
| `PastDue` | `past_due` | Invoice unpaid, dunning in progress |
| `Suspended` | `suspended` | Suspended (temporary hold) |
| `Canceled` | `canceled` | Subscription canceled |
| `Expired` | `expired` | Subscription reached expiration |
| `AwaitingSignup` | `awaiting_signup` | Awaiting customer confirmation |
| `Unpaid` | `unpaid` | Unpaid state |
| `TrialEnded` | `trial_ended` | Trial ended, no obligation |
| `Assessing` | `assessing` | (Internal) Assessment in progress |
| `FailedToCreate` | `failed_to_create` | Creation failed |
| `OnHold` | `on_hold` | Subscription on hold |

### CollectionMethod (optional on subscription)
| C# Member | Wire Value | Meaning |
|---|---|---|
| `Automatic` | `automatic` | Automatic payment collection (default) |
| `Remittance` | `remittance` | Manual remittance (invoice-based) |
| `Prepaid` | `prepaid` | Prepaid balance (no charge at billing) |
| `Invoice` | `invoice` | Invoice-based collection (legacy) |

---

## Client Construction & Configuration Facts

| Item | Value / Pattern | Source |
|---|---|---|
| **DI Registration** | `.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" }; o.Environment = ServerEnvironment.Us; o.Server.Production.Us.Site = subdomain; })` or manual `new MaxioAdvancedBillingClient(httpClient, options)` | `dotnet-client-initialization` skill + sdk-map.md |
| **HttpClient Requirement** | Must be long-lived (singleton `IHttpClientFactory.CreateClient()`, NOT per-request new); SDK wraps it and reuses across all calls | `dotnet-client-initialization` skill |
| **Basic Auth Setup** | `BasicAuthCredentials { Username = apiKey (from config), Password = "x" (literal) }` — set before client construction or in DI callback | `dotnet-authentication` skill + sdk-map.md |
| **Subdomain / Base URL** | `options.Server.Production.Us.Site = "cp-exp-1"` (sandbox) or read from `Maxio:Subdomain` config; OR override `options.Server.Production.Us.BaseUrl = "http://localhost:3000"` for mocking | sdk-map.md |
| **Namespaces to Add** | `using MaxioAdvancedBilling;` (client, root types) · `using MaxioAdvancedBilling.Api;` (controllers) · `using MaxioAdvancedBilling.Models;` (records) · `using MaxioAdvancedBilling.Models.Enums;` (enum types) · `using MaxioAdvancedBilling.Errors;` (error types) · `using MaxioAdvancedBilling.Core.Authentication.Basic;` (auth) · `using MaxioAdvancedBilling.Servers;` (ServerEnvironment) | sdk-map.md |

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The sheet deliberately does not carry their contents; each skill carries patterns, gotchas, and worked examples the plan cannot include:

| Skill | Step | Notes |
|---|---|---|
| `dotnet-client-initialization` | Client setup & DI | HttpClient long-lifetime, transient vs. singleton client wrapper, DI builder options |
| `dotnet-authentication` | Auth wiring | BasicAuthCredentials construction, config key loading, rotating/refreshing (N/A for API key, but read for completeness) |
| `dotnet-calling-endpoints` | Operation calls | Named arguments (parameter names are literal, `ct:` not `cancellationToken:`), required-but-nullable params, response envelope nesting (`.Subscription`, `.Customer`, etc.) |
| `dotnet-models` | Request/response fields | `StringEnum<T>` construction (use static members like `SubscriptionState.Active`, not `SubscriptionState.FromValue`), unions via `TryGet…` (N/A for this plan), required vs. optional field handling |
| `dotnet-error-handling` | Exception boundary | Case A vs. Case B distinction, `TryGet…` accessor usage, `SdkException<T>` structure, distinction between `JsonException` (malformed 2xx/non-2xx body) and `SdkException` (well-formed error) |
| `dotnet-configuration-resilience` | Retry/timeout config | `Timeout` is per-attempt not total, `HttpMethodsToRetry` gates status-based retry only (transport failures retry on all verbs including POST), `MaxRetries` floor is 1, non-idempotent writes can execute more than once |
| `dotnet-testing` | Unit test stubs | HttpClient mock/test seam, matching test framework to project (xUnit, MSTest, NUnit) |

**Always include, verbatim, both of these hazard rows** — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary; the error handler must catch `JsonException` separately to avoid silent failures or misattribution to the wrong status code.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the error boundary.

---

## Assumptions & Blockers

**Assumptions:**
- The Maxio SDK (`AsadAli.AdvancedBilling.Sdk`) is already in the project or will be added via NuGet before implementation; if not, add via `dotnet add package AsadAli.AdvancedBilling.Sdk` to PublicApi.csproj.
- Configuration keys (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`) are wired to environment variables or user-secrets in Startup.cs; implementer confirms this is set up.
- The `.NET 10 + .NET 8 runtime` constraint and `UseOnlyInMemoryDatabase=true` affect only the test/dev environment; Maxio integration is database-agnostic (only reads userId from auth context, no persistence of subscription state in local DB needed for this plan).
- The in-memory DB means userId ↔ subscription mapping is transient within a single app instance; on restart, the app has no record of what subscriptions the user created. Maxio is the source of truth; UI can re-fetch subscriptions from the API if needed.
- JWT authentication on PublicApi is already in place; the three endpoints inherit the `[Authorize]` attribute and `User.FindFirst(ClaimTypes.NameIdentifier)` returns the userId.

**Blockers:**
- None identified. All required operations are present in the SDK map; the SDK has no built-in idempotency guards, but the plan accounts for this with explicit lookup-first patterns in the integration code.

