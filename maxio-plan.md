# Maxio Subscription Billing Integration — eShopOnWeb

## Scope & Sequence

1. **SDK Client Setup** — register the SDK client via DI with credentials from configuration
2. **Three PublicApi endpoints** — GET `/api/subscription-plans`, POST `/api/subscriptions`, GET `/api/my-subscriptions`
3. **Customer idempotency** — create or find customer by eShop user reference
4. **Subscription lifecycle** — create, read, and list subscriptions; expose state/price/dates to caller

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation 1: List available subscription plans

| | |
|---|---|
| **Endpoint** | `GET /api/subscription-plans` (PublicApi, JWT-authed) |
| **SDK method** | `client.Products.ListProducts(…)` |
| **Signature** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — all 8 filter params (`dateField` … `include`) are nullable, no default → **must pass explicitly** (pass `null` to skip); pagination defaults: `page` = 1, `perPage` = 20 |
| **Query params** | `page`, `per_page` (C# ← wire) |
| **Returns** | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` — each response is an **envelope** containing `ProductResponse.Product: MaxioAdvancedBilling.Models.Product` (required, non-null) |
| **Product fields (extract)** | `Product.Id: int`, `Product.Name: string?`, `Product.Handle: string?`, `Product.Description: string?`, `Product.PriceInCents: long?`, `Product.Interval: int?`, `Product.IntervalUnit: MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `Product.TrialPriceInCents: long?`, `Product.TrialInterval: int?`, `Product.TrialIntervalUnit: MaxioAdvancedBilling.Models.Enums.IntervalUnit?` |
| **Error case** | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — **Case B (raw)** — no typed accessors; use `ex.Error.StatusCode: HttpStatusCode`, `ex.Error.ReadAsString(): string` |
| **Pagination** | Manual `page` + `perPage`; call repeatedly with `page++` until results exhaust |
| **Source** | `map/operations/Products.md` — `ListProducts` row |

**Trap:** When calling with all 8 filter params as `null`, you are still **passing them explicitly**. The wire name for `dateField` is `date_field`, etc. (query param mapper). Read `dotnet-calling-endpoints` before writing named or positional calls.

---

### Operation 2: Create or retrieve a customer (idempotent)

| | |
|---|---|
| **Step 2a: Try to find customer by reference** | |
| **SDK method** | `client.Customers.ReadCustomerByReference(reference)` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Query param** | `reference` (wire name same as C# param name) |
| **Returns** | `MaxioAdvancedBilling.Models.CustomerResponse` — **envelope** containing `CustomerResponse.Customer: MaxioAdvancedBilling.Models.Customer` |
| **Customer fields (extract)** | `Customer.Id: int`, `Customer.FirstName: string?`, `Customer.LastName: string?`, `Customer.Email: string?`, `Customer.Reference: string?`, `Customer.Organization: string?`, `Customer.Address: string?`, `Customer.City: string?`, `Customer.State: string?`, `Customer.Zip: string?`, `Customer.Country: string?`, `Customer.CreatedAt: DateTimeOffset?`, `Customer.UpdatedAt: DateTimeOffset?` |
| **Error case** | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — **Case B** — on 404 (not found), `StatusCode == HttpStatusCode.NotFound`; OK to use as signal to create new customer (not an exceptional condition in your domain) |
| **Source** | `map/operations/Customers.md` — `ReadCustomerByReference` row |
| | |
| **Step 2b: Create customer (if not found)** | |
| **SDK method** | `client.Customers.CreateCustomer(body)` |
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — body is nullable, no default → **must pass explicitly** |
| **Request body model** | `MaxioAdvancedBilling.Models.CreateCustomerRequest` — **envelope** with field `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer !req` |
| **CreateCustomer record fields (required)** | `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req` |
| **CreateCustomer record fields (recommended for idempotency)** | `Reference (reference): string?` — set to eShopOnWeb user ID/UUID to allow later lookup without storing Maxio customer ID |
| **CreateCustomer record fields (optional)** | `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?` |
| **Returns** | `MaxioAdvancedBilling.Models.CustomerResponse` — **envelope** containing `CustomerResponse.Customer: MaxioAdvancedBilling.Models.Customer` |
| **Error case** | `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — **Case A (typed)** — check `ex.Error.TryGetCustomerErrorResponse1(out var e422)` for validation errors [422], fallback `ex.Error.TryGetRawError(out var raw)` [other statuses]; also `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` [4xx/5xx outside typed range] |
| **Error payload (422)** | `MaxioAdvancedBilling.Models.CustomerErrorResponse1` with field `Errors (errors): MaxioAdvancedBilling.Models.Errors?` — Errors record contains `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` (unlikely here; check for `Reference` field collision if reference already in use) |
| **Source** | `map/operations/Customers.md` — `CreateCustomer` row |

**Trap:** On 422 from `CreateCustomer`, the error may indicate duplicate `reference` (if you are setting it). Check `ex.Error.TryGetCustomerErrorResponse1(out var e)` then inspect `e.Errors` for the fields that collided. If reference was the issue, fall back to lookup by the same reference; if not, propagate the error. **MUST load `dotnet-error-handling`** before writing the try/catch boundary.

---

### Operation 3: Create a subscription (single user → single plan)

| | |
|---|---|
| **SDK method** | `client.Subscriptions.CreateSubscription(body)` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — body is nullable, no default → **must pass explicitly** |
| **Request body model** | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` — **envelope** with field `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription !req` |
| **CreateSubscription record fields (required for this scope)** | `ProductHandle (product_handle): string?` — set to the plan handle (e.g. `"eshop-pro"`); alternative: `ProductId (product_id): int?` but handle is preferred |
| **CreateSubscription record fields (identifying customer — one of the two)** | **Option A:** `CustomerId (customer_id): int?` — set to the Maxio customer ID from step 2 / `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — **Case A (typed)** — check `ex.Error.TryGetErrorListResponse1(out var e422)` for validation errors [422], fallback `ex.Error.TryGetRawError(out var raw)` [other statuses]; also `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` [4xx/5xx outside typed range] |
| **CreateSubscription record fields (identifying customer — one of the two)** | **Option B:** `CustomerReference (customer_reference): string?` — set to the same eShopOnWeb user reference you set on the customer; Maxio will look it up server-side |
| **CreateSubscription record fields (optional but common)** | `Reference (reference): string?` — unique subscription reference for later lookup; e.g. user ID + plan handle; `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?` — for the sandbox, not required (the spec notes "payment not required"); `CouponCode (coupon_code): string?` or `CouponCodes (coupon_codes): IReadOnlyList<string>?` if applicable; `NextBillingAt (next_billing_at): DateTimeOffset?` to set first billing date explicitly |
| **CreateSubscription record fields (other optional)** | `Components (components): IReadOnlyList<MaxioAdvancedBilling.Models.CreateSubscriptionComponent>?` (for metered usage, optional); `DeferSignup (defer_signup): bool? = false` (default false — create immediately) |
| **Returns** | `MaxioAdvancedBilling.Models.SubscriptionResponse` — **envelope** containing `SubscriptionResponse.Subscription: MaxioAdvancedBilling.Models.Subscription?` (nullable) |
| **Subscription fields (extract for response)** | `Subscription.Id: int`, `Subscription.State: MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `Subscription.ProductPriceInCents: long?`, `Subscription.CurrentPeriodEndsAt: DateTimeOffset?`, `Subscription.NextAssessmentAt: DateTimeOffset?`, `Subscription.ActivatedAt: DateTimeOffset?`, `Subscription.CreatedAt: DateTimeOffset?`, `Subscription.UpdatedAt: DateTimeOffset?`, `Subscription.Reference: string?`, `Subscription.CouponCode: string?`, `Subscription.CouponCodes: IReadOnlyList<string>?` |
| **Error case** | `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — **Case A (typed)** — check `ex.Error.TryGetErrorListResponse1(out var e422)` for validation errors [422], fallback `ex.Error.TryGetRawError(out var raw)` [other statuses]; also `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` [4xx/5xx outside typed range] |
| **Error payload (422)** | `MaxioAdvancedBilling.Models.ErrorListResponse1` with field `Errors (errors): IReadOnlyList<string> !req` — list of validation messages (e.g. `["Customer not found"]`, `["Product not found"]`, `["Invalid coupon"]`, etc.); consume and return to caller |
| **Source** | `map/operations/Subscriptions.md` — `CreateSubscription` row |

**Trap:** To idempotently upsert (i.e., return existing subscription if already created for this customer/plan pair), set a consistent `Reference` on creation (e.g. `{customerId}:{planHandle}`), then on next request call `FindSubscription(reference)` before creating. If 404, create; otherwise return existing. `FindSubscription` is the lookup endpoint for subscription by reference. **MUST load `dotnet-calling-endpoints`** before writing the request body (the nested `CreateSubscription` record structure and required field set).

---

### Operation 4: Get a subscription by ID

| | |
|---|---|
| **SDK method** | `client.Subscriptions.ReadSubscription(subscriptionId, include)` |
| **Signature** | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` is nullable, no default → **must pass explicitly** (pass `null` for no includes) |
| **Path param** | `subscriptionId` (wire name: `subscription_id`) |
| **Query param** | `include` — optional list of include flags (e.g. `SubscriptionInclude.SelfServicePageToken`) |
| **Returns** | `MaxioAdvancedBilling.Models.SubscriptionResponse` — **envelope** containing `SubscriptionResponse.Subscription: MaxioAdvancedBilling.Models.Subscription?` |
| **Subscription fields (same as create response above)** | — |
| **Error case** | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — **Case B** — no typed accessors; use `ex.Error.StatusCode`, `ex.Error.ReadAsString()` |
| **Source** | `map/operations/Subscriptions.md` — `ReadSubscription` row |

---

### Operation 5: List subscriptions (for a specific customer, optionally filtered by state)

| | |
|---|---|
| **SDK method** | `client.Subscriptions.ListSubscriptions(state, product, productPricePointId, coupon, couponCode, dateField, startDate, endDate, startDatetime, endDatetime, metadata, direction, sort, include, page, perPage, ct)` |
| **Signature** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 filter params (`state` … `include`) are nullable, no default → **must pass explicitly**; pagination defaults: `page` = 1, `perPage` = 20 |
| **Query params** | `state`, `product`, `product_price_point_id`, `coupon`, `coupon_code`, `date_field`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `metadata` (dict), `direction`, `sort`, `include` (list), `page`, `per_page` |
| **Returns** | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` — each response is an **envelope** containing `SubscriptionResponse.Subscription: MaxioAdvancedBilling.Models.Subscription?` |
| **Subscription fields (same as above)** | — |
| **Filtering (for your use case — optional)** | To list only active subscriptions: `state = MaxioAdvancedBilling.Models.Enums.SubscriptionStateFilter.Active` (or other state enum value); pass all other filter params as `null` |
| **Error case** | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — **Case B** |
| **Pagination** | Manual `page` + `perPage`; increment `page` to fetch next batch |
| **Source** | `map/operations/Subscriptions.md` — `ListSubscriptions` row |

**Trap:** The endpoint does not filter by customer ID directly; it returns all subscriptions on the site (paginated). To filter for a single customer's subscriptions, the Customers controller has an operation `ListCustomerSubscriptions(customerId)` — consider using that instead if you have the Maxio customer ID. **MUST load `dotnet-calling-endpoints`** before calling (the enum values for `state`, `direction`, `sort` must be constructed correctly from `StringEnum<T>`).

---

## Enum values referenced above

These enums live in `MaxioAdvancedBilling.Models.Enums`. Construct them as `EnumType.FromValue("wire_value")` or use the static members listed in `map/models/enums.md`:

| Enum | Where used | Wire value examples | Source |
|---|---|---|---|
| `SubscriptionState` | Subscription response `.State` field | `"active"`, `"paused"`, `"canceled"`, etc. | `map/models/enums.md` — search `SubscriptionState` |
| `SubscriptionStateFilter` | `ListSubscriptions` param `state` | `"active"`, `"paused"`, `"canceled"`, etc. | `map/models/enums.md` — search `SubscriptionStateFilter` |
| `IntervalUnit` | Product `.IntervalUnit`, `.TrialIntervalUnit` | `"month"`, `"year"`, `"week"`, `"day"` | `map/models/enums.md` — search `IntervalUnit` |
| `CollectionMethod` | `CreateSubscription` param `PaymentCollectionMethod` | `"automatic"`, `"remittance"`, `"invoice"` | `map/models/enums.md` — search `CollectionMethod` |
| `SortingDirection` | `ListSubscriptions` param `direction` | `"asc"`, `"desc"` | `map/models/enums.md` — search `SortingDirection` |
| `SubscriptionSort` | `ListSubscriptions` param `sort` | `"number"`, `"state"`, `"signup_date"`, `"created_at"`, `"updated_at"` | `map/models/enums.md` — search `SubscriptionSort` |

---

## Client setup & configuration

| | |
|---|---|
| **SDK version** | `AsadAli.AdvancedBilling.Sdk` (install via NuGet) |
| **Root namespace** | `MaxioAdvancedBilling` (note: differs from package ID) |
| **Client class** | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` |
| **Options class** | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` |
| **Auth** | HTTP **Basic** — `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` with `Username = API key`, `Password = "x"` (literal string) |
| **Environment** | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default; use `.Eu` only if your Maxio account is EU-hosted) |
| **Server override (optional)** | `options.Server.Production.Us.BaseUrl` can be overridden (e.g., for mocking/dev) |
| **Configuration binding** | Read from config keys (see below) — never hardcode secrets in code |
| **DI registration** | Use `services.AddMaxioAdvancedBillingClient(opts => { … })` from `MaxioAdvancedBilling` namespace, or register `HttpClient` + construct `MaxioAdvancedBillingClient` manually |

### Configuration keys (from scope)

| Config key | Value | Example | Purpose |
|---|---|---|---|
| `Maxio:ApiKey` | Your Maxio/Chargify API key | `"sk_test_…"` | HTTP Basic auth username |
| `Maxio:Subdomain` | Your Maxio site subdomain | `"cp-exp-4"` | Populates `{site}` in base URL `https://{site}.chargify.com` |
| `Maxio:ProductFamilyHandle` | Product family handle (read-only for listing) | `"eshop-subscribe"` | Not used by SDK (part of your app's business logic) |
| `Maxio:BaseUrl` | (Optional override) | `"https://cp-exp-4.chargify.com"` or `"http://localhost:8080"` | Override the full base URL (dev/test environments) |

**Binding:** Use `IOptions<MaxioSettings>` or equivalent configuration section in `appsettings.json` / environment variables per your project's config provider. Example:

```json
{
  "Maxio": {
    "ApiKey": "YOUR_API_KEY",
    "Subdomain": "cp-exp-4",
    "ProductFamilyHandle": "eshop-subscribe"
  }
}
```

---

## Trap notes

⚠ **Step 1 (SDK client init)** — the SDK's `HttpClient` must be long-lived and reused via `IHttpClientFactory`, not created once per request. The client wrapper itself may be transient, but the underlying HTTP infrastructure is not. **MUST load `dotnet-client-initialization`** before wiring DI.

⚠ **Step 1 (auth)** — credentials must be set *before* client construction, or the client will have no auth and all calls will 401. The config binding must load the key from configuration, not hardcode it. **MUST load `dotnet-authentication`** before setting `BasicAuth`.

⚠ **Step 2 (customer idempotency)** — if the customer reference already exists on Maxio, `CreateCustomer` will return 422 with an error message about the duplicate reference. Handle this by catching the 422, extracting the error message, and falling back to `ReadCustomerByReference` to fetch the existing customer. Store the Maxio customer ID so you don't have to look it up repeatedly. **MUST load `dotnet-error-handling`** before writing the boundary.

⚠ **Step 3 (subscription creation)** — payment information is not required for the sandbox (per the spec: "payment not required"). If you do supply `PaymentProfileId` or `CreditCardAttributes`, the subscription will attempt to charge immediately (or at `NextBillingAt` if set). Omitting payment info means the subscription is created in a "trialing" or "awaiting signup" state (depending on product config). **MUST load `dotnet-calling-endpoints`** for the nested request body shape.

⚠ **Response envelope unwrapping** — `ProductResponse`, `CustomerResponse`, `SubscriptionResponse` are wrappers. Extract the inner object via `.Product`, `.Customer`, `.Subscription` fields respectively. If `.Subscription` is null, the API returned an error (or the body was malformed). **MUST load `dotnet-models`** before unpacking unions or complex nested structures.

⚠ **Deserialization errors (2xx with bad body)** — if the API returns a 2xx status but the JSON body does not match the generated `ProductResponse` / `CustomerResponse` / `SubscriptionResponse` shape (e.g., a missing `required` field), the SDK's JSON deserializer throws `System.Text.Json.JsonException`, **NOT** an `SdkException`. This bubbles up past the SDK boundary and becomes an uncaught exception in your integration. A boundary that only catches `SdkException` will let a `JsonException` escape. Map **every** `JsonException` from deserialization to a 5xx error indicator in your boundary. **MUST load `dotnet-error-handling`** — it covers both the typed 422 exceptions and the deserialization trap.

⚠ **Non-2xx body deserialization errors** — if the API returns a non-2xx status (e.g., 422, 500) and the error body does not match the expected `{Operation}Error` shape, the SDK's error handler throws `JsonException` **while constructing the exception object**, so the exception replaces the `SdkException` and the HTTP status is lost. Do not assume the presence of an `SdkException`; catch `JsonException` separately and log/report it as a potential API contract mismatch. **MUST load `dotnet-error-handling`** — it shows how to layer both catches.

⚠ **Retry semantics** — the SDK retries on certain HTTP statuses and **all transport errors** (e.g., network timeout). A `POST` (subscription creation) can be retried if a transport error occurs, which means `CreateSubscription` **could execute twice and create two subscriptions** if the first attempt times out but actually succeeded server-side. Mitigate by setting `Reference` on the subscription and checking for a duplicate on retry. **MUST load `dotnet-configuration-resilience`** to understand which operations retry, which statuses trigger retries, and whether you can disable them (spoiler: `MaxRetries = 0` is rejected; floor is 1).

---

## REQUIRED READING

Load **before implementation starts**. The sheet deliberately does not carry their contents — these are the definitive references for each step:

| Skill | Step(s) it governs |
|---|---|
| `dotnet-client-initialization` | Client & DI setup (step 1) |
| `dotnet-authentication` | Credentials & auth scheme (step 1) |
| `dotnet-calling-endpoints` | Operation calls & request bodies (steps 2–5) |
| `dotnet-models` | Nested model unpacking, enums, union construction (steps 2–5) |
| `dotnet-error-handling` | Exception boundary (`try/catch`), both `SdkException` cases and `JsonException` traps (all steps) |
| `dotnet-configuration-resilience` | Retry tuning, timeout bounds, logging, base-URL override (step 1) |

Also mandatory (not in trap notes, but applies to all SDK integrations):

- `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the FIRST sheet, not a later revision: the boundary is written early, and a caveat that arrives afterwards arrives too late to shape it.

---

## Assumptions & Blockers

| Assumption | Rationale | Risk |
|---|---|---|
| eShopOnWeb user ID can be serialized to `string` and used as `Reference` | Needed to map 1:1 eShop user → Maxio customer without storing Maxio ID in eShop DB | If user ID is mutable or private (e.g., internal sequence), this breaks; use stable UUID instead |
| Subscription `Reference` can also be constructed deterministically (e.g., `{customerId}:{planHandle}`) | Allows idempotent re-creation (lookup by reference, create if missing) | If reference cannot be computed again (e.g., timestamp-based), you'll create duplicates on retry |
| Maxio site `cp-exp-4` has three products: `eshop-pro`, `eshop-basic`, `api-call` (metered, optional for now) | Scope limits to these handles; no endpoint creates products | If handles differ or products are absent, `CreateSubscription` fails with "product not found" |
| Payment collection method defaults to `automatic` (or omit, let site default apply) | Sandbox spec says "payment not required"; omitting payment method avoids early charge | If the sandbox requires explicit payment method, you must set `CollectionMethod.Automatic` or similar |
| No other service in eShop writes to Maxio (no concurrent subscriptions manager, etc.) | Idempotency via `Reference` assumes your code is the only writer | If another process also creates subscriptions for the same customer/plan pair, duplicate-detection logic must be more robust |
| JWT token from PublicApi controller correctly identifies the authenticated user | Assumption: caller's identity is available via claims/principal | If identity extraction fails or is misconfigured, you cannot map subscription to the right user |

**Blockers:** None at spec-design time. All required operations are present in the SDK, error cases are typed/documented, and configuration is standard. Verify sandbox credentials and product handles before coding.

---

## Notes on scope decisions

- **Three endpoints only.** The plan does not include cancel, update, or payment-method operations. If those are added later, apply the same map-first contract approach (each operation has its own rows in the SDK map).
- **Idempotency via `Reference`.** The customer and subscription both support a reference field. Leverage it to avoid duplicates on transient failures (which trigger SDK retries).
- **No payment profiles in create.** The sandbox spec says payment is not required. The full `CreateSubscription` request model supports nested `CreditCardAttributes` and `PaymentProfileAttributes`, but for this scope, omit them.
- **Stateless API design.** The three PublicApi endpoints are stateless (read/create/list). Caller must provide sufficient context (plan handle, customer identity via JWT) to make decisions. No session state in Maxio.
- **Metered components optional.** The `api-call` component is seeded but not wired into the subscription creation. To enable usage tracking, add `Components` array to `CreateSubscription` and call usage endpoints later (not in scope).
