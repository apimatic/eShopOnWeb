# Maxio Advanced Billing (.NET SDK) integration plan — eShopOnWeb subscriptions

SDK: NuGet `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · target `netstandard2.0` (runs on .NET 8) · map generated from source tag `v1.0.2` (commit `15db14b`).

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|---|---|
| 0 | Register SDK client in DI; bind config keys `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional verbatim base-address override) | `AddMaxioAdvancedBillingClient` (§ Client facts) |
| 1 | One-time site-architecture probe: RI vs legacy decides the cardless `CollectionMethod` | `Sites.ReadSite` |
| 2 | Ensure a Maxio customer exists for the logged-in user (idempotent by `reference`) | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomers` (email fallback) |
| 3 | List available plans (products in the configured product family) | `ProductFamilies.ListProductsForProductFamily`, `Products.ReadProductByHandle` (single-plan fallback for `eshop-pro` / `basic-plan`) |
| 4 | Subscribe the user to a plan without card capture (idempotent by subscription `reference` + pre-check) | `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription`, `Subscriptions.ActivateSubscription` (only if state is `awaiting_signup`) |
| 5 | Display the user's subscriptions: plan / price / state / next-billing date | `Customers.ListCustomerSubscriptions`, `Subscriptions.ReadSubscription` |

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

Namespaces: records ⇒ `MaxioAdvancedBilling.Models` · enums ⇒ `MaxioAdvancedBilling.Models.Enums` · typed error classes ⇒ `MaxioAdvancedBilling.Errors` · `SdkException<T>` ⇒ `MaxioAdvancedBilling.Core.Exceptions` · `RawError` ⇒ `MaxioAdvancedBilling.Core.ErrorResponse` · `BasicAuthCredentials` ⇒ `MaxioAdvancedBilling.Core.Authentication.Basic` · `ServerEnvironment` ⇒ `MaxioAdvancedBilling.Servers` · `RetryOptions` ⇒ `MaxioAdvancedBilling.Core.Configuration`. Every operation is throw-only — there are no `…Result` no-throw variants in this SDK.

### 2.1 Operations

| Operation (`client.X.Y`) | Signature (params in order) | Request model + fields set | Response envelope + fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.Sites.ReadSite` | `ReadSite(CancellationToken ct = default)` | — | `SiteResponse` → `Site (site): Site !req`; read `RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?`, `Test (test): bool?` | **Case B** `SdkException<RawError>` → `StatusCode`, `ReadAsString()`, `ReadAsBytes()`, `ReadAsJson<T>()` | none | `operations/Sites.md`, `records-3-Of-Su.md` |
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | query `reference` ← `reference` | `CustomerResponse` → `Customer (customer): Customer !req`; read `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?` | **Case B** `SdkException<RawError>`; a missing customer surfaces as `StatusCode == HttpStatusCode.NotFound` (404) | none | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }`; `CreateCustomer` fields set: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` | `CustomerResponse` (same as above) | **Case A** `SdkException<CreateCustomerError>` → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| `client.Customers.ListCustomers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — the 7 params `direction`…`q` have no default → **pass all explicitly** (pass `null` to skip) | query `q` ← `q` (search: email, reference, name, org) | `IReadOnlyList<CustomerResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` | `operations/Customers.md` |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` have no default → **pass all explicitly** (`null` to skip) | path `product_family_id` ← `productFamilyId` — **accepts the handle form `"handle:eshop-subscribe"`** (SDK XML doc: "Either the product family's id or its handle prefixed with `handle:`") | `IReadOnlyList<ProductResponse>` → `Product (product): Product !req`; read `Id`, `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `RequestCreditCard (request_credit_card): bool?`, `RequireCreditCard (require_credit_card): bool?`, `ProductFamily (product_family): ProductFamily?` | **Case A** `SdkException<ListProductsForProductFamilyError>` → `TryGetString(out string)` [404], `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (default 20, server max 200) | `operations/ProductFamilies.md` + source `Api/ProductFamilies.cs`, `records-3-Of-Su.md` |
| `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | path `api_handle` ← `apiHandle` | `ProductResponse` (same as above) | **Case B** `SdkException<RawError>`; missing product ⇒ 404 via `StatusCode` | none | `operations/Products.md`, `records-3-Of-Su.md` |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, no default → **must pass explicitly** | query `reference` ← `reference` | `SubscriptionResponse` (same as below) | **Case A** `SdkException<FindSubscriptionError>` → `TryGetNoContent(out RawError)` [404 — this is the "no subscription with that reference" signal], `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`; `CreateSubscription` fields set: `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `NetTerms (net_terms): string?`, `Reference (reference): string?`. **No payment-profile / card fields are set** — the model carries `PaymentProfileId`, `CreditCardAttributes`, `BankAccountAttributes` etc. and they stay `null` | `SubscriptionResponse` → `Subscription (subscription): Subscription?` (**nullable inner — null-check before every read**); read `Id (id): int?`, `State (state): SubscriptionState?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` (= next billing date), `ProductPriceInCents (product_price_in_cents): long?`, `Product (product): Product?` | **Case A** `SdkException<CreateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422 → `Errors (errors): IReadOnlyList<string> !req`], `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `records-4-Su-We.md` |
| `client.Subscriptions.ActivateSubscription` | `ActivateSubscription(int subscriptionId, ActivateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** (pass `null`) | none set | `SubscriptionResponse` | **Case A** `SdkException<ActivateSubscriptionError>` → `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [400], `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | path `customer_id` ← `customerId` | `IReadOnlyList<SubscriptionResponse>` — read per subscription: `Id`, `State`, `CurrentPeriodEndsAt` (next billing), `Product` (`Name`, `Handle`, `PriceInCents`), `ProductPriceInCents`, `PaymentCollectionMethod`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `CanceledAt (canceled_at): DateTimeOffset?` | **Case B** `SdkException<RawError>` | **none** — returns all subscriptions for the customer in one call | `operations/Customers.md`, `records-3-Of-Su.md`, `records-4-Su-We.md` |
| `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, no default → **must pass explicitly** (pass `null`) | query `include` ← `include` | `SubscriptionResponse` | **Case B** `SdkException<RawError>` | none | `operations/Subscriptions.md` |

### 2.2 Enums needed (all `StringEnum<T>` — NOT C# enums; build via static members or `Type.FromValue("wire")`; namespace `MaxioAdvancedBilling.Models.Enums`)

| Enum | Members (`CSharpMember (wire)`) |
|---|---|
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` — per map: legacy Statements Architecture accepts `invoice`/`automatic`; Relationship Invoicing accepts `remittance`/`automatic`/`prepaid` |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` (pass `null` — only needed if you date-filter `ListCustomers`) |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` (pass `null`) |

### 2.3 Client construction, auth, server node

| Fact | Value | Source |
|---|---|---|
| Package / namespace | `AsadAli.AdvancedBilling.Sdk` / `using MaxioAdvancedBilling;` | `sdk-map.md` |
| Only constructor | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`) | `sdk-map.md` |
| Options properties | `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?` | `sdk-map.md` |
| Auth (Basic) | `o.BasicAuth = new BasicAuthCredentials { Username = <`Maxio:ApiKey`>, Password = "x" }` — username = API key, password = the literal string `"x"` | `sdk-map.md` |
| Site/subdomain | `o.Server.Production.Us.Site = <`Maxio:Subdomain`>` — `{site}` defaults to `subdomain` in `https://{site}.chargify.com` | `sdk-map.md` |
| Environment | `ServerEnvironment.Us` (default, `https://{site}.chargify.com`) / `ServerEnvironment.Eu` (`https://{site}.ebilling.maxio.com`) | `sdk-map.md` |
| Verbatim base-URL override | `o.Server.Production.Us.BaseUrl = <`Maxio:BaseUrl`>` — overrides the whole derived base address for the Production group (dev/mock host or full sandbox URL) | `sdk-map.md` |
| Retry config | `o.Retry` — `RetryOptions` (all members `required`; start from `RetryOptions.Default()`); namespace `MaxioAdvancedBilling.Core.Configuration` | `sdk-map.md` |

### 2.4 Idempotency — what the SDK contract actually gives you

- **Customer create IS provider-protected.** `CreateCustomer` notes: "you may only create one customer for a given reference value. If provided, the `reference` value must be unique." So: `ReadCustomerByReference(reference: "eshop-user-{userId}")` → 404 ⇒ create with the same `Reference`; a concurrent duplicate create is rejected with **422** (`TryGetCustomerErrorResponse1`) ⇒ recover by re-running the lookup. Two double-clicks can never yield two customers with the same reference. (`operations/Customers.md`)
- **Subscription create is NOT provider-protected.** `CreateSubscription.Reference` is free-form and the map documents **no uniqueness enforcement** for subscription references. `Subscriptions.FindSubscription(reference)` gives you the pre-check (`TryGetNoContent` ⇒ 404 ⇒ safe to create), but a true double-click race can still pass two pre-checks before either create lands. A serialization guard on the app side (per-user/per-plan lock or unique constraint) is **YOUR CALL — not in the map**; the SDK supplies the lookup tool, not the mutex.
- There is **no idempotency-key header** anywhere in the operations in scope — dedupe must go through `reference` + lookup.

## 3. Trap notes

- ⚠ Step 0 (DI registration) — the SDK client wraps an `HttpClient` whose handler pipeline must be long-lived; the wrong DI lifetime silently rebuilds the handler pool or pins a stale client. **MUST load `dotnet-client-initialization`** before registering the client.
- ⚠ Step 0 (credentials) — Basic auth here is inverted-looking (username = API key, password = `"x"`), credentials must be in place before the first call, and the key must come from configuration (`Maxio:ApiKey`), never hard-coded. **MUST load `dotnet-authentication`** before wiring credentials.
- ⚠ Steps 2–5 (every list/search call) — `ListCustomers`, `ListProductsForProductFamily` etc. have several nullable parameters with **no C# default**: a positional call mis-binds silently. Call with named arguments (`q:`, `productFamilyId:`, `ct:`). **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 3–5 (models) — `CollectionMethod`/`SubscriptionState` are `StringEnum<T>` records, not C# enums; request records are immutable with `init`-only setters and `required` members; response wire names differ from C# names. **MUST load `dotnet-models`** before constructing payloads.
- ⚠ Steps 2–5 (error boundary) — operations split across Case A (typed `{Operation}Error` with `TryGet…`) and Case B (`SdkException<RawError>`), per the table above; a single catch shape loses error payloads. **MUST load `dotnet-error-handling`** before writing the boundary. Both of these hazards apply verbatim:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
- ⚠ Step 4 (resilience around the non-idempotent POST) — the SDK's retry/timeout options do **not** bound a whole call, and a **transport failure** is retried on every verb including `POST`, so `CreateSubscription` can execute more than server-side-once no matter what the app's pre-check does; whether a failed write can be re-sent safely must be decided with the retry semantics in view. **MUST load `dotnet-configuration-resilience`** before tuning the client.
- ⚠ Tests (steps 2–5) — the `HttpClient` constructor argument is the test seam; the SDK client itself is not the seam. **MUST load `dotnet-testing`** before stubbing.

## 4. REQUIRED READING

Load **before implementation starts** — the sheet deliberately does not carry these skills' contents:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — DI lifetime, `HttpClient` ownership/handler reuse, factory wiring for `AddMaxioAdvancedBillingClient`. |
| `dotnet-authentication` | Step 0 — Basic credential shape (username = key, password = `"x"`), config-sourced keys, per-environment credentials. |
| `dotnet-calling-endpoints` | Steps 2–5 — named-argument calls for no-default nullable params, envelope unwrapping, `ct` usage. |
| `dotnet-models` | Steps 2–5 — `StringEnum<T>` construction, `required`/`init` record semantics, wire names, nullable envelope inners. |
| `dotnet-error-handling` | Steps 2–5 — Case A/B catch ladder, `TryGet…` accessors, and the two `JsonException` directions in §3. |
| `dotnet-configuration-resilience` | Step 0/4 — retry/timeout semantics, what actually re-sends a `POST`, base-URL override behavior, pagination. |
| `dotnet-testing` | Steps 2–5 — faking at the `HttpClient` seam, covering error and edge paths. |

## 5. Assumptions & Blockers

- **Assumption (YOUR CALL — not in the map):** the sandbox site already contains product family `eshop-subscribe` with products `eshop-pro` and `basic-plan`. The SDK can create product families/products (`ProductFamilies.CreateProductFamily`, `Products.CreateProduct`) but seeding the catalog is outside this feature's scope; the plan only reads the catalog. If the handles are absent, `ListProductsForProductFamily`/`ReadProductByHandle` return 404 and step 3/4 cannot proceed until the catalog is seeded.
- **Assumption (YOUR CALL):** reference formats `"eshop-user-{userId}"` (customer) and `"eshop-sub-{userId}-{planHandle}"` (subscription) — the map only requires customer `reference` uniqueness; the exact format is an application decision.
- **Assumption (YOUR CALL):** the brief dictates user-secrets keys `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` but names no binding key for `MAXIO_ENVIRONMENT`; the plan suggests `Maxio:Environment` mapping to `ServerEnvironment.Us`/`.Eu` (the map's only two values). The sandbox is assumed US-hosted ⇒ `ServerEnvironment.Us` with the sandbox subdomain.
- **UNVERIFIED (resolved at runtime, not by the map):** whether the sandbox site runs Relationship Invoicing or legacy Statements Architecture. Step 1 reads `Site.RelationshipInvoicingEnabled` and picks `CollectionMethod.Remittance` (RI) vs `CollectionMethod.Invoice` (legacy); if the chosen value is rejected the 422 arrives as `ErrorListResponse1` with readable messages — handle it by surfacing that list, not by guessing the other value silently.
- **UNVERIFIED:** the subscription `State` immediately after a cardless signup (map docs describe both `active` and `awaiting_signup` flows for products without trial). Display whatever `SubscriptionResponse` returns; only if it is `AwaitingSignup` call `ActivateSubscription`.
- **UNVERIFIED:** whether the live 422 payload for `CreateCustomer` truly matches `CustomerErrorResponse1` — the map itself carries two disagreeing generated shapes (`CustomerErrorResponse` and `CustomerErrorResponse1`, both wrapping an `Errors` record whose fields are `PerPage`/`PricePoint`, which looks implausible for customer-validation messages). Defensive directive: extract best-effort from `TryGetCustomerErrorResponse1`, and when it yields nothing readable fall back to `TryGetRawError(out RawError)` → `RawError.ReadAsString()` for the message.
- **Blockers: none** — every operation, field, enum value, error type, and accessor above is grounded in the map (plus source `Api/ProductFamilies.cs` for the `handle:` path-parameter form).
