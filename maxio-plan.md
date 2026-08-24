# Maxio subscription billing plan

## 1. Scope & sequence

| Step | Implementation outcome | Maxio operations | Source |
|---|---|---|---|
| 0 | **Gate:** resolve Blocker B1 before implementation. The generated SDK exposes no documented subscription idempotency key and does not document `reference` as unique; the requested hard guarantee cannot be claimed from this contract. | `CreateSubscription`, `FindSubscription` | `operations/Subscriptions.md`; `Models/CreateSubscription.cs` (scoped source lookup) |
| 1 | Add NuGet `AsadAli.AdvancedBilling.Sdk` **1.0.2** to the project that owns the Maxio adapter. Bind only `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`; validate required values at startup and never log the key. Register one long-lived `HttpClient` pipeline and construct the SDK client through DI. | client construction only | `sdk-map.md` |
| 2 | Add an application-owned Maxio gateway plus DTOs and an error boundary. Keep SDK records/exceptions out of HTTP endpoint contracts. | all operations below | `sdk-map.md`; YOUR CALL — not in the map |
| 3 | Resolve the configured family by exact ordinal handle comparison over `ListProductFamilies`; fail configuration if zero or multiple matches. Enumerate all product pages using the resolved numeric family ID converted to invariant string. Exclude archived products and map plan name/handle/description/price/interval. | `ListProductFamilies`; `ListProductsForProductFamily` | `operations/ProductFamilies.md` |
| 4 | Derive one deterministic, opaque Maxio customer reference from authenticated caller identity. Look it up first. If absent, create with required first name, last name, and email; if concurrent create loses the unique-reference race, look it up again. Persist the user/customer mapping after a confirmed response. | `ReadCustomerByReference`; `CreateCustomer` | `operations/Customers.md` |
| 5 | Validate a requested plan handle belongs to the configured family using the family product enumeration; never trust a client-supplied numeric ID. Derive a deterministic subscription reference and coordinate the application write with a durable pending/confirmed/failed ledger having unique constraints on the caller+plan key and remote reference. Reconcile by `FindSubscription` before any create and after an ambiguous create result. **This minimizes duplicate double-click work but does not resolve B1.** | `FindSubscription`; `CreateSubscription` | `operations/Subscriptions.md`; YOUR CALL — not in the map |
| 6 | On a successful or reconciled create, require a non-null response envelope payload, persist Maxio IDs/reference, and return plan, price, state, and `NextAssessmentAt` as next billing time (fall back to `CurrentPeriodEndsAt` only when the assessment time is absent). | `CreateSubscription`; optionally `ReadSubscription` for an already-persisted Maxio ID | `operations/Subscriptions.md`; `records-3-Of-Su.md`; scoped `Models/Subscription.cs` source lookup |
| 7 | Treat Maxio as system of record for the account view: resolve the caller's Maxio customer and return live subscriptions from the customer operation, mapping the same summary fields. A missing Maxio customer produces an empty list; provider failures do not silently fall back to stale local state. | `ReadCustomerByReference`; `ListCustomerSubscriptions` | `operations/Customers.md` |
| 8 | Add JWT-authenticated PublicApi endpoints at the requested routes. Caller identity comes only from validated token claims; POST accepts a plan handle and never a user/customer/subscription ID. Return deterministic validation/conflict/provider-unavailable errors without leaking Maxio bodies or credentials. | gateway operations above | YOUR CALL — not in the map |
| 9 | Add unit, integration, concurrency, persistence, malformed-2xx, typed-error, raw-error, and cancellation tests. Then build/test and run a sandbox smoke flow using user-secrets and in-memory storage as required by the machine brief. | gateway operations above | YOUR CALL — not in the map |

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

### Operation contracts

| Controller property | Exact SDK method signature | Request/query fields used | Response envelope and fields read | Error contract | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies` | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)`; the first five nullable arguments have no defaults and must be passed explicitly | pass all five filters as `null` | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; each `ProductFamily (product_family): ProductFamily?`; read `Id (id): int?`, `Handle (handle): string?` | Case B `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; read `StatusCode`, `ReadAsString()`/`ReadAsJson<T>()` only defensively | none | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.Enums.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)`; eight nullable arguments from `dateField` through `include` must be passed explicitly | `productFamilyId` = invariant decimal text of resolved ID; null date/filter/include values; `includeArchived: false`; explicit `page`/`perPage` | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; each required `Product (product): Product`; read `Id (id)`, `Name (name)`, `Handle (handle)`, `Description (description)`, `PriceInCents (price_in_cents)`, `Interval (interval)`, `IntervalUnit (interval_unit)`, `ArchivedAt (archived_at)`, nested `ProductFamily (product_family)` | Case A `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` for 404, inherited `TryGetRawError(out RawError)` fallback | manual `page` + `perPage`; response has no count/next-page envelope | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | query `reference` wire name `reference` | `MaxioAdvancedBilling.Models.CustomerResponse`; required `Customer (customer): Customer`; read `Id (id)`, `Reference (reference)`, `FirstName (first_name)`, `LastName (last_name)`, `Email (email)` | Case B `SdkException<RawError>`; inspect `RawError.StatusCode` for 404 and generic failures | none | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| `client.Customers` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)`; nullable `body` has no default and must be passed explicitly | request envelope required `Customer (customer): CreateCustomer`; inner required `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`; carry optional `Reference (reference): string` because operation Notes state it is the app's unique customer identifier. Omit address/country/state and every other optional field. | `CustomerResponse`; required `Customer`; read ID/reference/name/email | Case A `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` for 422, inherited `TryGetRawError(out RawError)` fallback. `CustomerErrorResponse1.Errors` is modeled as `MaxioAdvancedBilling.Models.Errors?`, whose generated fields are `PerPage` and `PricePoint`; this is visibly inconsistent with customer validation, so extract best-effort and fall back to a generic safe message (`UNVERIFIED` wire fit). | none | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| `client.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)`; nullable `reference` has no default and must be passed explicitly | query `reference` wire name `reference`; always pass the non-null deterministic app reference | `MaxioAdvancedBilling.Models.SubscriptionResponse`; `Subscription (subscription): Subscription?` | Case A `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` for 404, inherited `TryGetRawError(out RawError)` fallback | none | `operations/Subscriptions.md`; `records-4-Su-We.md` |
| `client.Subscriptions` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)`; nullable `body` has no default and must be passed explicitly | request envelope required `Subscription (subscription): CreateSubscription`. Inner model marks no fields C#-required, but operation Notes require product identification and customer identification: carry `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?`, and application `Reference (reference): string?`. Omit product/customer numeric alternatives, price-point selection, payment profile/card/bank fields, coupons, components, dates, import/group/tax fields, because this flow selects the product default price point and the seeded product does not require payment. | `SubscriptionResponse`; optional `Subscription`. Read `Id (id)`, `Reference (reference)`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents)`, `NextAssessmentAt (next_assessment_at)`, `CurrentPeriodEndsAt (current_period_ends_at)`, nested `Product` name/handle/price/interval, nested `Customer.Id` | Case A `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` for 422, whose required `Errors (errors)` is `IReadOnlyList<string>`; inherited `TryGetRawError(out RawError)` fallback | none | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |
| `client.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<MaxioAdvancedBilling.Models.Enums.SubscriptionInclude>? include, CancellationToken ct = default)`; nullable `include` has no default and must be passed explicitly | `include: null` | `SubscriptionResponse`; same inner fields as Create | Case B `SdkException<RawError>` | none | `operations/Subscriptions.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |
| `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | path customer ID only | `IReadOnlyList<SubscriptionResponse>`; map same summary fields from each optional `Subscription` | Case B `SdkException<RawError>` | none | `operations/Customers.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |

### Enums actually read

| Fully-qualified type | Members (C# member → wire value) | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` (`StringEnum`) | `Day` → `day`; `Month` → `month` | `enums.md` |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` (`StringEnum`) | `Pending` → `pending`; `FailedToCreate` → `failed_to_create`; `Trialing` → `trialing`; `Assessing` → `assessing`; `Active` → `active`; `SoftFailure` → `soft_failure`; `PastDue` → `past_due`; `Suspended` → `suspended`; `Canceled` → `canceled`; `Expired` → `expired`; `Paused` → `paused`; `Unpaid` → `unpaid`; `TrialEnded` → `trial_ended`; `OnHold` → `on_hold`; `AwaitingSignup` → `awaiting_signup` | `enums.md` |

### Client, authentication, and server contracts

| Fact | Exact contract | Source |
|---|---|---|
| Package/version | NuGet package `AsadAli.AdvancedBilling.Sdk`, map/source version/tag **1.0.2**, root namespace `MaxioAdvancedBilling`, target `netstandard2.0` | `sdk-map.md` |
| Constructor | `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`; only constructor | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`: `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`, `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`, `Server: MaxioAdvancedBilling.ServerOptions`, `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` |
| Authentication | Basic only: `BasicAuthCredentials { Username = Maxio:ApiKey, Password = "x" }`; both credential members are required strings | `sdk-map.md` |
| Environment | `ServerEnvironment.Us` value `US` is default and resolves Production to `https://{site}.chargify.com`; `ServerEnvironment.Eu` value `EU` resolves to `https://{site}.ebilling.maxio.com`. There is no SDK `Sandbox` environment value. | `sdk-map.md`; `enums.md` |
| Subdomain-derived host | For this US-site configuration, set `MaxioAdvancedBilling.ServerOptions.Production` (`MaxioAdvancedBilling.Servers.ProductionOptions`) `.Us.Site` to `Maxio:Subdomain`; default `.Us.BaseUrl` template is `https://{site}.chargify.com`. | `sdk-map.md`; `ServerOptions.cs`; `Servers/ProductionOptions.cs` |
| Base URL override | When `Maxio:BaseUrl` is non-empty, assign it verbatim to `options.Server.Production.Us.BaseUrl`; otherwise leave that default template and set `.Site`. This scope uses only Production endpoints, never Ebb. | `sdk-map.md` |
| Configuration boundary | The task supplies exactly `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`. The SDK has no sandbox selector; sandbox targeting is the chosen Maxio site/host and credentials. Do not invent `Maxio:Environment`. | `sdk-map.md`; YOUR CALL — not in the map |
| Customer idempotency capability | `CreateCustomer` Notes explicitly state only one customer may exist for a given `reference`, and `ReadCustomerByReference` returns the single match. | `operations/Customers.md` |
| Subscription lookup capability | `CreateSubscription.Reference` is only documented as an app-provided reference, and `FindSubscription(reference)` returns a subscription. Neither operation contract nor model source documents uniqueness or create-time deduplication. The generated create signature has only body + `ct`, with no idempotency-key argument. | `operations/Subscriptions.md`; `Models/CreateSubscription.cs` (scoped source lookup) |

### Application-owned contracts

| Concern | Directive | Source |
|---|---|---|
| Caller identity | Use a stable authenticated JWT subject/issuer-derived key; never accept user ID in request input. Hash/encode if necessary before using it as a Maxio reference. | YOUR CALL — not in the map |
| POST request | Require `planHandle`; reject blank/unknown/out-of-family handles. Do not accept numeric Maxio IDs. | YOUR CALL — not in the map |
| Durable mapping | Persist caller key, deterministic customer reference, Maxio customer ID, plan handle, deterministic subscription reference, Maxio subscription ID, lifecycle state, timestamps, and concurrency token. Enforce unique caller/customer reference, caller+plan, and subscription reference indexes. | YOUR CALL — not in the map |
| Ledger states | Insert `Pending` before remote creation, confirm only after a non-null Maxio subscription payload, and reconcile pending/ambiguous records through `FindSubscription`. Do not mark local state active independently of Maxio. | YOUR CALL — not in the map |
| HTTP semantics | GET plans/account require JWT. POST requires JWT; return success for an already-confirmed deterministic subscription, validation error for bad plan, conflict for a still-in-progress request, and sanitized provider error otherwise. | YOUR CALL — not in the map |

## 3. Trap notes

- ⚠ Step 1 (client registration) — `HttpClient` ownership and SDK wrapper lifetime can cause connection churn or handler misuse. **MUST load `dotnet-client-initialization`** before registering the client.
- ⚠ Step 1 (authentication) — credential timing, rotation, and configuration binding determine whether requests carry the intended Basic identity. **MUST load `dotnet-authentication`** before wiring credentials.
- ⚠ Steps 3–7 (operation calls) — generated optional parameters without C# defaults can silently mis-bind positional calls, and envelopes add a payload level. **MUST load `dotnet-calling-endpoints`** before writing calls.
- ⚠ Steps 3–7 (models) — required members, nullability, wire names, and `StringEnum` value extraction can make plausible object initializers or mappings wrong. **MUST load `dotnet-models`** before constructing or mapping models.
- ⚠ Steps 2 and 8 (error boundary) — typed and raw SDK errors require different access paths and unsafe error-body assumptions can leak data or erase status meaning. **MUST load `dotnet-error-handling`** before writing catches.
- ⚠ Steps 1 and 5 (resilience/idempotency) — retry trigger behavior can re-send a write and timeout settings can change the ambiguity window around creation. **MUST load `dotnet-configuration-resilience`** before configuring the client or claiming idempotency.
- ⚠ Step 3 (pagination) — list operations expose manual page/per-page controls without a next-page token, so incomplete traversal can hide plans. **MUST load `dotnet-configuration-resilience`** before implementing enumeration.
- ⚠ Step 9 (tests) — faking SDK implementation details rather than the supported HTTP seam makes tests brittle and can miss serialization/error behavior. **MUST load `dotnet-testing`** before writing tests.
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 4. REQUIRED READING

Load every item below **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 client construction, `HttpClient` lifetime, and DI |
| `dotnet-authentication` | Step 1 Basic credentials and rotation |
| `dotnet-calling-endpoints` | Steps 3–7 generated operation invocation and envelopes |
| `dotnet-models` | Steps 3–7 requests, nullable responses, and `StringEnum` mapping |
| `dotnet-error-handling` | Steps 2 and 8 typed/raw/JSON exception boundary |
| `dotnet-configuration-resilience` | Steps 1, 3, and 5 retries, timeouts, server override, pagination, and logging |
| `dotnet-testing` | Step 9 supported test seam and behavioral coverage |

## 5. Assumptions & Blockers

- **Assumption A1 — application policy:** one subscription per authenticated caller and plan handle is the intended meaning of double-click idempotency; the HTTP body supplies the desired plan handle because only the product-family handle is configurable.
- **Assumption A2 — sandbox catalog:** the configured family contains products whose default price points are the prices to display/enroll; the integration does not choose a non-default price point.
- **Assumption A3 — US host shape:** absent `Maxio:BaseUrl`, the supplied site uses the SDK's US Production template. The SDK has only US/EU hosting values and no Sandbox environment value.
- **Blocker B1 — no grounded exactly-once subscription create capability:** `CreateSubscription` exposes no idempotency-key parameter, and neither its operation Notes nor the map-named `CreateSubscription.Reference` source documentation says that `reference` is unique or deduplicates creates. `FindSubscription` permits reconciliation but cannot prevent two already-accepted creates, and retry behavior creates an additional ambiguity that must be evaluated through `dotnet-configuration-resilience`. The plugin therefore cannot substantiate the mandate that a double-click can **never** create two Maxio subscriptions. Per the task's non-negotiable tooling rule, implementation must stop until Maxio/plugin supplies a documented create idempotency mechanism or the requirement is explicitly narrowed to application-coordinated best effort.
