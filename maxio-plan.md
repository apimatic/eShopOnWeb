# Maxio recurring-subscription integration plan

## 1. Scope & sequence

| Step | Application work | SDK operations / contract |
|---|---|---|
| 1 | Add the pinned package, bind `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`; register one reusable HTTP pipeline and the SDK client. Do not log or persist the API key. | Client construction, Basic auth, Production server selection/override (`sdk-map.md`). |
| 2 | Implement JWT-protected `GET /api/subscription-plans`. Page through all site products, keep non-archived products whose `Product.ProductFamily.Handle` exactly equals the configured handle, and project stable handles only. For the seeded sandbox the expected family is `eshop-subscribe` and expected product handles are `eshop-pro` and `basic-plan`. | `client.Products.ListProducts` (`operations/Products.md`). |
| 3 | Resolve the requested plan handle again server-side before a write; reject a product outside the configured family or one with `RequireCreditCard == true`. Numeric product/family/price-point IDs are neither accepted nor sent. | `client.Products.ReadProductByHandle` (`operations/Products.md`). |
| 4 | Derive a stable customer reference from the authenticated JWT subject/application user ID (never email), load first name/last name/email from the trusted user profile, then lookup-or-create the customer. On a concurrent-create 422, lookup again by reference. | `client.Customers.ReadCustomerByReference`, `client.Customers.CreateCustomer` (`operations/Customers.md`). |
| 5 | Implement JWT-protected `POST /api/subscriptions` around a durable local idempotency row with a unique key owned by the authenticated user. The winning transaction uses stable `product_handle`, `customer_reference`, and deterministic subscription `reference`; it omits every payment-profile/card/bank field. Reconcile an ambiguous outcome with `FindSubscription`; do not blindly issue a second create. | `client.Subscriptions.FindSubscription`, `client.Subscriptions.CreateSubscription` (`operations/Subscriptions.md`). |
| 6 | Implement JWT-protected `GET /api/my-subscriptions` from the caller-owned local ledger of subscription references, refreshing each through `FindSubscription`. Return product handle/name, price in cents, currency, state, and `NextAssessmentAt` as the outward next-billing date. | `client.Subscriptions.FindSubscription` (`operations/Subscriptions.md`). |
| 7 | Add contract-boundary, concurrency, recovery, pagination, authorization, and configuration tests. | All operations above; no live credentials in tests. |

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

### Package, client, authentication, and server

| Contract | Exact fact | Source |
|---|---|---|
| Package/version | Pin `<PackageReference Include="AsadAli.AdvancedBilling.Sdk" Version="1.0.2" />`. The map is generated from source tag `v1.0.2`, commit `15db14b`; root namespace is `MaxioAdvancedBilling`; target is `netstandard2.0`. Do not reference SDK source projects. | `sdk-map.md` |
| Constructor | The only constructor is `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`. | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` has `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`, `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`, `Server: MaxioAdvancedBilling.ServerOptions`, and `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`. | `sdk-map.md` |
| Authentication | Basic only: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = configuredApiKey, Password = "x" }`. No credential literal belongs in source, checked-in configuration, logs, or this plan. | `sdk-map.md` |
| Sandbox host | Select `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` for the normal sandbox template `https://{site}.chargify.com`, and assign `options.Server.Production.Us.Site = configuredSubdomain`. (`Eu` targets `https://{site}.ebilling.maxio.com`.) | `sdk-map.md` |
| Optional base URL | When configured, assign the configuration value verbatim to `options.Server.Production.Us.BaseUrl`; do not append the subdomain or API paths. All operations in this sheet use the Production server group. | `sdk-map.md` |
| Client accessors | `client.Products`, `client.Customers`, and `client.Subscriptions` are the generated controller properties. | `operations/Products.md`; `operations/Customers.md`; `operations/Subscriptions.md` |

### Operation contracts

| Use | Controller property · exact asynchronous signature / return | Request and response shape used here | Error contract | Pagination | Source |
|---|---|---|---|---|---|
| List configured-family plans | `client.Products` · `System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>> ListProducts(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, System.DateTimeOffset? endDate, System.DateTimeOffset? endDatetime, System.DateTimeOffset? startDate, System.DateTimeOffset? startDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, System.Threading.CancellationToken ct = default)` | Pass the first eight nullable/no-default parameters explicitly (`null`, except `includeArchived: false`); read each `MaxioAdvancedBilling.Models.ProductResponse.Product`. Filter by `Product.ProductFamily?.Handle == configuredHandle`. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; read `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, or `ReadAsBytes()`. | Manual `page` + `perPage`; there is no total-count envelope. With `perPage: 20`, continue until a page has fewer than 20 items (an exact multiple requires one final empty page). | `operations/Products.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md` |
| Validate selected plan | `client.Products` · `System.Threading.Tasks.Task<MaxioAdvancedBilling.Models.ProductResponse> ReadProductByHandle(string apiHandle, System.Threading.CancellationToken ct = default)` | Send the client-supplied stable handle as `apiHandle`; reread the `Product` envelope and validate family handle, `ArchivedAt`, and `RequireCreditCard`. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; a not-found status is available only through `RawError.StatusCode`. | None. | `operations/Products.md`; `records-3-Of-Su.md` |
| Lookup customer by stable app identity | `client.Customers` · `System.Threading.Tasks.Task<MaxioAdvancedBilling.Models.CustomerResponse> ReadCustomerByReference(string reference, System.Threading.CancellationToken ct = default)` | `reference` is the stable caller-owned customer reference; response envelope is required `CustomerResponse.Customer`. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; classify not-found by `RawError.StatusCode`. | None. | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| Create customer | `client.Customers` · `System.Threading.Tasks.Task<MaxioAdvancedBilling.Models.CustomerResponse> CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, System.Threading.CancellationToken ct = default)` | Pass non-null `CreateCustomerRequest`; required envelope member `Customer`; required inner members `FirstName`, `LastName`, `Email`; set `Reference`. The map explicitly guarantees only one customer for a given reference. Read required `CustomerResponse.Customer`. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], then inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. | None. | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| Reconcile/find subscription by stable reference | `client.Subscriptions` · `System.Threading.Tasks.Task<MaxioAdvancedBilling.Models.SubscriptionResponse> FindSubscription(string? reference, System.Threading.CancellationToken ct = default)` | Pass the deterministic subscription reference explicitly; envelope field `SubscriptionResponse.Subscription` is nullable and must be checked before mapping. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [404], then inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. | None. | `operations/Subscriptions.md`; `records-3-Of-Su.md` |
| Create subscription | `client.Subscriptions` · `System.Threading.Tasks.Task<MaxioAdvancedBilling.Models.SubscriptionResponse> CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, System.Threading.CancellationToken ct = default)` | Pass non-null `CreateSubscriptionRequest`; required envelope member `Subscription`. Set only `CreateSubscription.ProductHandle`, `CustomerReference`, and deterministic `Reference`. Leave `ProductId`, `ProductPricePointId`, `CustomerId`, `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, and `BankAccountAttributes` null. Read nullable `SubscriptionResponse.Subscription`. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], then inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. | None. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md` |
| Numeric-only operation deliberately excluded | `client.Customers` · `System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>> ListCustomerSubscriptions(int customerId, System.Threading.CancellationToken ct = default)` | The SDK requires an Advanced Billing numeric customer ID and has no stable-reference overload, so this integration MUST NOT call it. Use the local reference ledger plus `FindSubscription`. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`. | None. | `operations/Customers.md` |

### Request/response records and outward mappings

Only fields that this integration sets, validates, or returns are included; all other generated optional request fields stay null/default.

| Fully-qualified record | Exact relevant members (`C# name (wire_name): type`; `!req` means C# `required`) | Integration rule | Source |
|---|---|---|---|
| `MaxioAdvancedBilling.Models.ProductResponse` | `Product (product): MaxioAdvancedBilling.Models.Product !req` | Always unwrap one level. | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.Product` | `Name (name): string?`; `Handle (handle): string?`; `Description (description): string?`; `PriceInCents (price_in_cents): long?`; `Interval (interval): int?`; `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`; `ArchivedAt (archived_at): DateTimeOffset?`; `RequireCreditCard (require_credit_card): bool?`; `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?`; `ProductPricePointHandle (product_price_point_handle): string?` | Public plan DTO: handle/name/description/price-in-cents/interval/unit. Never expose or accept numeric IDs. Exclude archived products and products requiring a card. | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.ProductFamily` | `Handle (handle): string?` | Exact ordinal comparison with configured `ProductFamilyHandle`; no numeric ID. | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.CreateCustomerRequest` | `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer !req` | Non-null envelope. | `records-1-Ac-Cr.md` |
| `MaxioAdvancedBilling.Models.CreateCustomer` | `FirstName (first_name): string !req`; `LastName (last_name): string !req`; `Email (email): string !req`; `Reference (reference): string?` | Required profile data comes from the authenticated app user, not request-body identity fields. Always set reference. | `records-1-Ac-Cr.md` |
| `MaxioAdvancedBilling.Models.CustomerResponse` | `Customer (customer): MaxioAdvancedBilling.Models.Customer !req` | Always unwrap one level. | `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Models.Customer` | `FirstName (first_name): string?`; `LastName (last_name): string?`; `Email (email): string?`; `Reference (reference): string?` | Verify/reuse by reference; ignore numeric `Id`. | `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` | `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription !req` | Non-null envelope. | `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Models.CreateSubscription` | `ProductHandle (product_handle): string?`; `CustomerReference (customer_reference): string?`; `Reference (reference): string?`; prohibited numeric/payment alternatives present on the model: `ProductId`, `ProductPricePointId`, `CustomerId`, `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` (all nullable) | Set the three stable-reference fields only. The operation note says payment information may still be required depending on product configuration; prevalidation of `Product.RequireCreditCard` is mandatory. | `records-2-Cr-Ne.md`; `operations/Subscriptions.md` |
| `MaxioAdvancedBilling.Models.SubscriptionResponse` | `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?` | Nullable envelope member: treat a successful response with null subscription as an integration/provider-contract failure. | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.Subscription` | `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`; `ProductPriceInCents (product_price_in_cents): long?`; `NextAssessmentAt (next_assessment_at): DateTimeOffset?`; `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`; `Reference (reference): string?`; `Currency (currency): string?`; `Product (product): MaxioAdvancedBilling.Models.Product?` | Outward DTO: subscription reference, `Product.Handle`, `Product.Name`, price in cents, currency, state wire value, and `NextAssessmentAt` as `nextBillingAt`. Ignore numeric IDs. | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.ErrorListResponse1` | `Errors (errors): System.Collections.Generic.IReadOnlyList<string> !req` | Best-effort 422 message list; otherwise emit the boundary's generic provider-rejection message. | `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Models.CustomerErrorResponse1` | `Errors (errors): MaxioAdvancedBilling.Models.Errors?` | The generated shared payload is suspiciously narrow for customer validation; see uncertainty below. | `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Models.Errors` | `PerPage (per_page): System.Collections.Generic.IReadOnlyList<string>?`; `PricePoint (price_point): System.Collections.Generic.IReadOnlyList<string>?` | `UNVERIFIED`: these generated members do not resemble general customer validation fields. Extract best-effort only and fall back to the generic provider-rejection message; never claim live wire compatibility. | `records-2-Cr-Ne.md` |

### Enum values actually read

| Fully-qualified type | Generated kind | Exact C# member (`wire value`) | Source |
|---|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `StringEnum` | `Day (day)`, `Month (month)` | `enums.md` |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `StringEnum` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `enums.md` |

### Identity, idempotency, and capability limits

| Concern | Grounded directive / limitation | Source |
|---|---|---|
| Authenticated ownership | All three PublicApi routes require JWT authentication. Derive customer/subscription references and local-ledger ownership from the trusted JWT subject/application user record; ignore any caller-supplied user/customer identity. | Application boundary requirement; SDK requests in `records-1-Ac-Cr.md` and `records-2-Cr-Ne.md` |
| Stable identifiers | Public inputs and Maxio calls use only product handle, customer reference, and subscription reference. The integration does not persist, accept, return as identifiers, or pass Maxio numeric IDs. | `operations/Products.md`; `operations/Customers.md`; `operations/Subscriptions.md` |
| Customer idempotency | The map explicitly documents customer `reference` as unique. Lookup first; create once; if a competing request wins and this create returns 422, lookup by the same reference and continue. | `operations/Customers.md` |
| Subscription idempotency | `CreateSubscription` exposes no idempotency-key argument/header contract. `CreateSubscription.Reference` exists and `FindSubscription` can look it up, but the map does **not** state that subscription references are unique. Therefore reference lookup alone is not a double-click barrier. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md` |
| Double-click concurrency | Before external I/O, atomically insert/claim a caller-owned local row under a database unique constraint (for example normalized user ID + product handle, or an explicit client idempotency key scoped to the user). Only the winner may call Maxio. Persist the deterministic subscription reference and state (`Pending`, `Succeeded`, `Unknown`, `Failed`) in that transaction; duplicates return/reconcile the same row. | Required compensating design for the SDK limitation above. |
| Ambiguous create result | On a lost/ambiguous response, mark the durable row `Unknown` and reconcile with `FindSubscription(reference)`. Do not issue a fresh create merely because the first result is unknown. Absolute exactly-once creation cannot be proven from this SDK contract because there is no server idempotency contract and reference uniqueness is undocumented. | `operations/Subscriptions.md` |
| List-my-subscriptions gap | The only customer-specific list operation is `ListCustomerSubscriptions(int customerId)`. `ListSubscriptions` has no customer/customer-reference filter. To obey the no-numeric-ID requirement, the local ledger is mandatory and `GET /api/my-subscriptions` refreshes its references with `FindSubscription`. | `operations/Customers.md`; `operations/Subscriptions.md` |
| Payment-method-free creation | Omit all payment profile/card/bank fields. The operation contract warns payment may be required depending on product options; reject products with `RequireCreditCard == true`. If the seeded product is configured incompatibly, that is a sandbox configuration error, not a request-shape workaround. | `operations/Subscriptions.md`; `records-3-Of-Su.md` |
| Plan currency gap | `Product` exposes `PriceInCents` but no currency field. `Subscription` exposes `Currency`. Thus the plan-list contract can return price in cents and interval, but a trustworthy plan currency requires separate site configuration/contract not present in this scope. | `records-3-Of-Su.md` |

## 3. Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership/lifetime and the generated client wrapper's DI lifetime affect connection reuse and testability. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 1 (authentication) — credential timing, configuration sourcing, and rotation determine whether every request is authenticated without leaking secrets. **MUST load `dotnet-authentication`** before wiring Basic auth.

⚠ Steps 1 and 5 (server/retry/timeout) — the option names do not reveal the total call bound or whether a failed subscription write can be sent more than once, which directly affects the `Unknown` idempotency state. **MUST load `dotnet-configuration-resilience`** before configuring the client or write recovery.

⚠ Steps 2–6 (calls) — nullable parameters without C# defaults must still be passed, list calls need exact named arguments, envelopes must be unwrapped, and cancellation must bind to `ct`. **MUST load `dotnet-calling-endpoints`** before the first SDK call.

⚠ Steps 2–6 (models) — required initializers, nullable response members, wire names, and `StringEnum` projection can otherwise produce compile errors or incorrect DTOs. **MUST load `dotnet-models`** before constructing or mapping models.

⚠ Steps 4–6 (error boundary) — Case A typed accessors and Case B `RawError` have different extraction paths, and the generated customer 422 model may not carry useful live validation fields. **MUST load `dotnet-error-handling`** before writing the boundary.

⚠ Steps 4–6 (2xx deserialization) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Steps 4–6 (non-2xx deserialization) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 7 (tests) — faking generated controllers couples tests to SDK internals and misses real serialization, URL, cancellation, and error behavior. **MUST load `dotnet-testing`** before writing integration-layer tests.

## 4. REQUIRED READING

Load these **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1: client construction, `HttpClient` ownership, and DI registration. |
| `dotnet-authentication` | Step 1: Basic credentials and secret/rotation boundary. |
| `dotnet-configuration-resilience` | Steps 1, 2, and 5: environment/base URL, pagination, cancellation/timeouts, retries, and ambiguous POST outcomes. |
| `dotnet-calling-endpoints` | Steps 2–6: controller access, named arguments, async calls, `ct`, and envelopes. |
| `dotnet-models` | Steps 2–6: request initializers, nullability, wire names, and `StringEnum` values. |
| `dotnet-error-handling` | Steps 2–6: Case A/Case B exceptions, typed accessors, raw bodies, and both `JsonException` paths. |
| `dotnet-testing` | Step 7: HTTP seam and behavioral/error/concurrency tests. |

## 5. Assumptions & Blockers

- Assumption: “seeded sandbox plans `eshop-pro/basic-plan`” means two product handles, `eshop-pro` and `basic-plan`, under product-family handle `eshop-subscribe`.
- Assumption: the authenticated application user has a stable immutable ID/subject and trusted first name, last name, and email. Maxio's `CreateCustomer` model requires all three name/email fields; if the project lacks either name, a product decision is required (collect it or define an approved deterministic placeholder).
- Assumption: each selected plan uses its default product price point, because no product-price-point handle was requested.
- Blocker: the SDK has no stable-reference overload for listing a customer's subscriptions. A durable local subscription-reference ledger is required to satisfy `GET /api/my-subscriptions` without numeric IDs.
- Blocker: the SDK exposes no subscription idempotency-key contract, and the map does not guarantee subscription-reference uniqueness. The local unique claim prevents application double-click concurrency, but an ambiguous provider call cannot be proven exactly-once; it must enter `Unknown` and be reconciled rather than blindly recreated.
- Blocker: `Product` does not expose currency, so `GET /api/subscription-plans` cannot report a map-grounded currency without a separate configured/site-currency contract. Subscription responses can report `Subscription.Currency`.
