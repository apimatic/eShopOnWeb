# Maxio Subscription Integration Plan — eShopOnWeb

## Scope & Sequence

1. **Client initialization & DI registration** — set up `MaxioAdvancedBillingClient` with HTTP Basic auth from configuration; register singleton `HttpClient` + transient client wrapper.
2. **Idempotent customer resolution** — before creating a subscription, resolve or create a Maxio customer by the eShopOnWeb user's ID (reference).
3. **List available products** — fetch products by handle (`eshop-pro`, `basic-plan`) from Maxio sandbox environment to return to UI.
4. **Create subscription** — POST a subscription to Maxio, linking the Maxio customer to a product, with idempotent customer creation inline if needed.
5. **List user subscriptions** — return the user's active subscription list from Maxio.
6. **Error boundary** — trap SDK exceptions, map typed vs raw errors, and respond with user-facing messages.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation | Signature | Request model + fields | Response envelope + inner fields | Error case + accessors + payload | Pagination | Source |
|---|---|---|---|---|---|---|
| **CreateCustomer** (idempotent by `reference`) | `client.Customers.CreateCustomer(body, ct)` · `body`: `CreateCustomerRequest?` (nullable, **must pass explicitly**) | `CreateCustomerRequest` wrapper · `Customer (customer): CreateCustomer !req` · **Fields to send:** `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Organization (organization): string?`, `Reference (reference): string?` (use eShopOnWeb user ID here for idempotency), `Address (address): string?`, `City (city): string?`, `State (state): string?` (ISO-3166-2 code), `Zip (zip): string?`, `Country (country): string?` (ISO-3166-1 alpha-2), `Phone (phone): string?`, `Verified (verified): bool?`, `TaxExempt (tax_exempt): bool?`, `VatNumber (vat_number): string?`, `Metafields (metafields): IReadOnlyDictionary<string, string>?` | `CustomerResponse` · `Customer (customer): Customer !req` · **Read:** `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` | **Case A (typed)** · `SdkException<CreateCustomerError>` · `ex.Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `ex.Error.TryGetRawError(out RawError)` [fallback] · Payload: `CustomerErrorResponse1` · `Errors (errors): Errors?` | None | `operations/Customers.md` |
| **ReadCustomerByReference** (lookup user's existing Maxio customer) | `client.Customers.ReadCustomerByReference(reference, ct)` · `reference`: string (literal customer reference/eShopOnWeb user ID) | Query param: `reference` ← `reference` | `CustomerResponse` · `Customer (customer): Customer !req` · Same fields as CreateCustomer response | **Case B (raw)** · `SdkException<RawError>` · `ex.Error.StatusCode: HttpStatusCode` · `ex.Error.ReadAsString(): string` · `ex.Error.ReadAsJson<T>(): T?` | None | `operations/Customers.md` |
| **ListProducts** (GET available plans by product family handle) | `client.Products.ListProducts(dateField, filter, endDate, endDatetime, startDate, startDatetime, includeArchived, include, page, perPage, ct)` · **Must pass explicitly (nullable):** `dateField`, `filter`, `endDate`, `endDatetime`, `startDate`, `startDatetime`, `includeArchived`, `include` (pass `null` to skip) · `page` default 1, `perPage` default 20 | Query params: `date_field` ← `dateField`, `filter` ← `filter`, `end_date` ← `endDate`, `end_datetime` ← `endDatetime`, `start_date` ← `startDate`, `start_datetime` ← `startDatetime`, `page` ← `page`, `per_page` ← `perPage`, `include_archived` ← `includeArchived`, `include` ← `include` | `IReadOnlyList<ProductResponse>` · Each element: `ProductResponse` · `Product (product): Product !req` · **Read:** `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, `TrialIntervalUnit (trial_interval_unit): IntervalUnit?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `ArchivedAt (archived_at): DateTimeOffset?` | **Case B (raw)** · `SdkException<RawError>` · `ex.Error.StatusCode` · `ex.Error.ReadAsString()` · `ex.Error.ReadAsJson<T>()` | Manual: `page` (default 1) + `perPage` (default 20) | `operations/Products.md` |
| **ReadProductByHandle** (single product lookup by handle) | `client.Products.ReadProductByHandle(apiHandle, ct)` · `apiHandle`: string (product handle e.g. "eshop-pro") | Query param: N/A (path param) | `ProductResponse` · `Product (product): Product !req` · Same fields as ListProducts | **Case B (raw)** · `SdkException<RawError>` · `ex.Error.StatusCode` · `ex.Error.ReadAsString()` · `ex.Error.ReadAsJson<T>()` | None | `operations/Products.md` |
| **CreateSubscription** (attach user to plan, idempotent by customer reference) | `client.Subscriptions.CreateSubscription(body, ct)` · `body`: `CreateSubscriptionRequest?` (nullable, **must pass explicitly**) | `CreateSubscriptionRequest` wrapper · `Subscription (subscription): CreateSubscription !req` · **Fields to send (all optional):** `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `ReceivesInvoiceEmails (receives_invoice_emails): string?`, `NetTerms (net_terms): string?`, `Reference (reference): string?` (subscription reference from eShopOnWeb), `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `CustomerAttributes (customer_attributes): CustomerAttributes?` (nested, for inline customer creation), `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?`, `CreditCardAttributes (credit_card_attributes): PaymentProfileAttributes?` | `SubscriptionResponse` · `Subscription (subscription): Subscription?` · **Read:** `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `TrialStartedAt (trial_started_at): DateTimeOffset?`, `TrialEndedAt (trial_ended_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `BalanceInCents (balance_in_cents): long?`, `Customer (customer): Customer?`, `Product (product): Product?`, `CouponCode (coupon_code): string?` | **Case A (typed)** · `SdkException<CreateSubscriptionError>` · `ex.Error.TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `ex.Error.TryGetRawError(out RawError)` [fallback] · Payload: `ErrorListResponse1` · `Errors (errors): IReadOnlyList<string> !req` | None | `operations/Subscriptions.md` |
| **ListSubscriptions** (get user's subscriptions, filtered by customer or state) | `client.Subscriptions.ListSubscriptions(state, product, productPricePointId, coupon, couponCode, dateField, startDate, endDate, startDatetime, endDatetime, metadata, direction, sort, include, page, perPage, ct)` · **Must pass explicitly (nullable):** `state`, `product`, `productPricePointId`, `coupon`, `couponCode`, `dateField`, `startDate`, `endDate`, `startDatetime`, `endDatetime`, `metadata`, `direction`, `sort`, `include` · `page` default 1, `perPage` default 20 | Query params: `page` ← `page`, `per_page` ← `perPage`, `state` ← `state`, `product` ← `product`, `product_price_point_id` ← `productPricePointId`, `coupon` ← `coupon`, `coupon_code` ← `couponCode`, `date_field` ← `dateField`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`, `metadata` ← `metadata`, `direction` ← `direction`, `sort` ← `sort`, `include` ← `include` · Note: filtering by customer_id is **not** a query param on this operation; use customer_id via `ListCustomerSubscriptions` instead, or query all then filter client-side | `IReadOnlyList<SubscriptionResponse>` · Each element: `SubscriptionResponse` · `Subscription (subscription): Subscription?` · Same fields as CreateSubscription response | **Case B (raw)** · `SdkException<RawError>` · `ex.Error.StatusCode` · `ex.Error.ReadAsString()` · `ex.Error.ReadAsJson<T>()` | Manual: `page` (default 1) + `perPage` (default 20) | `operations/Subscriptions.md` |
| **ReadSubscription** (fetch single subscription by ID) | `client.Subscriptions.ReadSubscription(subscriptionId, include, ct)` · `subscriptionId`: int · `include`: `IReadOnlyList<SubscriptionInclude>?` (nullable, **must pass explicitly**; pass `null` if no includes needed) | Query param: `include` ← `include` | `SubscriptionResponse` · `Subscription (subscription): Subscription?` · Same fields as CreateSubscription response | **Case B (raw)** · `SdkException<RawError>` · `ex.Error.StatusCode` · `ex.Error.ReadAsString()` · `ex.Error.ReadAsJson<T>()` | None | `operations/Subscriptions.md` |

---

## Enums required

| Enum | Namespace | Values used | Source |
|---|---|---|---|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Active (active)`, `Trialing (trialing)`, `Canceled (canceled)`, `Paused (paused)` — use `SubscriptionState.Active` in code | `models/enums.md` |
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | `Automatic (automatic)`, `Invoice (invoice)`, `Remittance (remittance)`, `Prepaid (prepaid)` — use `CollectionMethod.Automatic` in code | `models/enums.md` |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Month (month)`, `Day (day)` — use `IntervalUnit.Month` in code | `models/enums.md` |

---

## Client initialization & configuration

| Aspect | Details | Source |
|---|---|---|
| **NuGet package** | `AsadAli.AdvancedBilling.Sdk` | `maxio-getting-started` SDK identity |
| **Root namespace** | `MaxioAdvancedBilling` (import with `using`) | `maxio-getting-started` |
| **Client class** | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `maxio-getting-started` |
| **Auth** | HTTP Basic: `options.BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" }` · Username = API key (from `Maxio:ApiKey` config), Password = literal `"x"` | `maxio-getting-started` SDK identity |
| **Auth namespace** | `using MaxioAdvancedBilling.Core.Authentication.Basic;` | `maxio-getting-started` |
| **Environments** | `options.Environment = ServerEnvironment.Us` (US-hosted, default); EU option: `ServerEnvironment.Eu` · Import: `using MaxioAdvancedBilling.Servers;` | `maxio-getting-started` |
| **Base URL override** | `options.Server.Production.Us.BaseUrl = "http://localhost:8080"` (for dev/mock) · Default: `https://{site}.chargify.com` with `{site}` from `Maxio:Subdomain` config | `maxio-getting-started` |
| **Retry/resilience** | Configurable via `options.Retry` (`RetryOptions`, backed by Polly); DO NOT rely on SDK defaults for production | `sdk-map.md` error-handling section |
| **HttpClient binding** | Client must be registered as DI singleton; inject `IHttpClientFactory` or register `HttpClient` as scoped/transient via `AddMaxioAdvancedBillingClient(o => { o.BasicAuth = ...; })` · **MUST load `dotnet-client-initialization`** before DI setup | `maxio-getting-started` |

---

## Trap notes

- ⚠ **Step 1 (client initialization)** — the `MaxioAdvancedBillingClient` takes an `HttpClient` as a constructor parameter; this client must be **long-lived and registered as a singleton** in DI (not recreated per request), or via `IHttpClientFactory`. The SDK options (e.g. `Retry`) do **not** configure the HttpClient itself — they configure the SDK's internal request pipeline (Polly). **MUST load `dotnet-client-initialization`** before wiring the client.

- ⚠ **Step 2 (authentication)** — credentials must be set in `BasicAuthCredentials` **before** client construction, or via the DI callback. HTTP Basic auth is username (API key) + password (literal `"x"`). Load credentials from `Maxio:ApiKey` binding, never hardcode. **MUST load `dotnet-authentication`** before setting credentials.

- ⚠ **Step 3 (idempotent customer resolution)** — `CreateCustomer` will reject a duplicate `reference` with a 422 error. Before creating a subscription, always call `ReadCustomerByReference` to check if the Maxio customer already exists (by the eShopOnWeb user ID). If found, use that customer ID in the subscription call. On first signup, create the customer inline using `CustomerAttributes` in the `CreateSubscription` body, **or** create it separately and link by `CustomerId`. Both patterns are supported; choose one and stick with it. **MUST load `dotnet-calling-endpoints`** for named-argument handling on multi-param operations.

- ⚠ **Step 4 (operation signatures & optional params)** — many operations have numerous nullable parameters with **no C# default** (e.g. `ListSubscriptions`). A positional call will misalign them. Use named arguments only: `client.Products.ListProducts(dateField: null, filter: null, ..., page: 1, perPage: 20, ct: ct)`. Cancellation token is `ct`, not `cancellationToken`. **MUST load `dotnet-calling-endpoints`** before the first call.

- ⚠ **Step 5 (response envelopes — read one level down)** — all read operations return an **envelope** record (e.g. `ProductResponse`, `SubscriptionResponse`, `CustomerResponse`). Each envelope has exactly one field (`Product`, `Subscription`, `Customer`, respectively). To access the actual data, read that field: `var product = response.Product;` (the field value may be nullable). **MUST load `dotnet-models`** before deserializing responses.

- ⚠ **Step 6 (error handling — two cases, opposite strategies)** — **Case A** operations throw `SdkException<{Operation}Error>` with typed accessors (`TryGet*`). **Case B** operations throw `SdkException<RawError>` only. Some operations are Case B (e.g. `ListSubscriptions`, `ReadSubscription`, `ListProducts`); others are Case A (e.g. `CreateCustomer`, `CreateSubscription`). Confirm each operation's case in the map row above. A `JsonException` can escape deserialization if a 2xx body is malformed (missing required field) — this is **NOT** an `SdkException` and must be handled separately. A non-2xx body that doesn't match the error shape throws `JsonException` *while constructing* the error object, **replacing** the `SdkException` and destroying the HTTP status — a boundary that maps every `JsonException` to 5xx then retries 5xx will retry forever. **MUST load `dotnet-error-handling`** before writing the error boundary.

- ⚠ **System.Text.Json.JsonException reaches the boundary from two directions and they need opposite handling** (drifted or malformed **2xx** body surfaces as `JsonException` from deserialization, **not** as `SdkException`, so SDK-exception-only catch lets it escape; non-2xx body mismatched to its error shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and HTTP status is lost) **MUST load `dotnet-error-handling`** before writing the boundary.

- ⚠ **Step 7 (pagination)** — `ListProducts`, `ListSubscriptions`, `ListCustomers` support manual pagination via `page` and `perPage` query parameters. These are **not** automatic cursors; you must loop and increment `page` yourself to fetch all results. Defaults are `page=1, perPage=20`. **MUST load `dotnet-calling-endpoints`** to confirm parameter order and named-argument usage.

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The sheet deliberately does not carry their contents; each skill supplies the details and best practices its step requires:

| Skill | Governs step |
|---|---|
| `dotnet-client-initialization` | Step 1 — client & DI setup; `HttpClient` singleton binding; `MaxioAdvancedBillingClientOptions` construction |
| `dotnet-authentication` | Step 2 — setting HTTP Basic credentials; loading from configuration; rotation/refresh patterns |
| `dotnet-calling-endpoints` | Step 3–7 — operation signatures; named vs positional arguments; cancellation tokens; calling `client.Customers`, `client.Products`, `client.Subscriptions` |
| `dotnet-models` | Step 5 — request/response record shapes; envelope one-level-down reads; required vs optional fields; union construction (`TryGet…`); enums as `StringEnum<T>` (not C# enum) |
| `dotnet-error-handling` | Step 6 — Case A vs Case B; `TryGet*` accessors; `SdkException<T>` unwrapping; `JsonException` duality; boundary design that avoids retrying non-retryable errors |
| `dotnet-configuration-resilience` | Tuning — retries (`HttpMethodsToRetry` gates **status** only, not transport failures), timeouts (per-attempt not total), base-URL override, logging hooks |
| `dotnet-testing` | Tests — `HttpClient` mock construction; framework alignment |

---

## Assumptions & Blockers

| Category | Item |
|---|---|
| **Assumption** | eShopOnWeb user IDs (or a derived customer reference) will be passed as the Maxio `reference` field to enable idempotent customer creation and lookup. The `reference` must be **unique per customer** in Maxio. |
| **Assumption** | The Maxio sandbox environment is already provisioned with the product family `eshop-subscribe` and products `eshop-pro` ($299/mo) and `basic-plan` ($29/mo), each with a `handle` matching the name. If not, these products must be created in the sandbox before integration can proceed. |
| **Assumption** | Payment collection method will default to `Automatic` for subscriptions; if `Remittance`, `Prepaid`, or `Invoice` is required, it must be specified in `PaymentCollectionMethod` on the subscription request. |
| **Assumption** | Subscription state transitions (Trialing → Active → Canceled, etc.) are handled by Maxio; the app reads the `State` field from `SubscriptionResponse` but does not drive state changes via API (activation, cancellation, reactivation are separate operations not in this scope). |
| **Blocker** | None identified at planning time. Live integration will confirm whether the stable product handles and product family handle exist in the sandbox. |
