# Maxio subscription integration plan

## 1. Scope & sequence

| Step | Work and Maxio operations |
|---|---|
| 1 | Add/retain NuGet `AsadAli.AdvancedBilling.Sdk` at the SDK-map source tag `v1.0.2`; configure a single `MaxioAdvancedBilling.MaxioAdvancedBillingClient` over a named, long-lived `HttpClient`. Bind only `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`; use `Maxio:BaseUrl` verbatim when supplied, otherwise set the Production-US site from `Maxio:Subdomain`. |
| 2 | Add a subscription integration boundary and durable application mapping/idempotency state keyed by the authenticated application user and requested product handle. The caller never supplies a customer ID or user ID. Resolve the Maxio customer with `Customers.ReadCustomerByReference`; on absence create with `Customers.CreateCustomer`, using a deterministic application-user reference. |
| 3 | Implement `GET /api/subscription-plans`: call `Products.ListProducts`, drive its manual page parameters, retain only `ProductResponse.Product.ProductFamily.Handle == Maxio:ProductFamilyHandle`, and return only unarchived selectable products with handle/name/price/interval data. Never depend on seeded numeric IDs. |
| 4 | Implement `POST /api/subscriptions`: validate the submitted product handle against Step 3’s configured-family catalog; serialize same-user/same-plan work; look up the deterministic subscription reference via `Subscriptions.FindSubscription`; create only if absent via `Subscriptions.CreateSubscription` with `CustomerId`, `ProductHandle`, and the deterministic reference. Persist/converge the application mapping and return data from the returned `SubscriptionResponse.Subscription`. |
| 5 | Implement `GET /api/my-subscriptions`: resolve the caller’s deterministic Maxio customer reference, then call `Customers.ListCustomerSubscriptions`; project only each `SubscriptionResponse.Subscription` into plan/price/state/next-billing output. |
| 6 | Protect all three endpoints with the PublicApi project’s JWT convention; translate SDK failures at the one integration boundary; test SDK traffic through a fake `HttpMessageHandler`, including no duplicate Maxio writes for concurrent/retried same-user/same-plan requests. |

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

| Operation / controller property | Method signature | Request model + fields used | Response envelope + fields read | Error case | Pagination | Source |
|---|---|---|---|---|---|---|
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Products` / catalog | `ListProducts(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.Enums.ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default): Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>>` | no body; pass each leading nullable argument explicitly (normally `null`), `includeArchived: false` | `ProductResponse.Product (product): MaxioAdvancedBilling.Models.Product !req`; read `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?`; then `ProductFamily.Handle (handle): string?` | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | manual `page` + `perPage` | `operations/Products.md`; `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers` / customer lookup | `ReadCustomerByReference(string reference, CancellationToken ct = default): Task<MaxioAdvancedBilling.Models.CustomerResponse>` | no body; deterministic application-user reference | `CustomerResponse.Customer (customer): MaxioAdvancedBilling.Models.Customer !req`; read `Id (id): int?`, `Reference (reference): string?` | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | none | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers` / create customer | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default): Task<MaxioAdvancedBilling.Models.CustomerResponse>` | `CreateCustomerRequest.Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer !req`; `CreateCustomer.FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`. The operation notes make `reference` unique when supplied; omit no required `CreateCustomer` field. | `CustomerResponse.Customer (customer): MaxioAdvancedBilling.Models.Customer !req`; read `Id (id): int?`, `Reference (reference): string?` | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback | none | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Subscriptions` / subscription lookup | `FindSubscription(string? reference, CancellationToken ct = default): Task<MaxioAdvancedBilling.Models.SubscriptionResponse>` | no body; `reference` has no C# default and must be passed explicitly | `SubscriptionResponse.Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`; read `Id (id): int?`, `Reference (reference): string?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `Product (product): MaxioAdvancedBilling.Models.Product?`; then `Product.Handle (handle): string?`, `Product.Name (name): string?` | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [404], `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback | none | `operations/Subscriptions.md`; `records-3-Of-Su.md`; `records-4-Su-We.md`; `enums.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Subscriptions` / enroll | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default): Task<MaxioAdvancedBilling.Models.SubscriptionResponse>` | `CreateSubscriptionRequest.Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription !req`; fields used: `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?`, `Reference (reference): string?`. The operation notes require a product by `product_id` or `product_handle`, and a customer by `customer_id` or `customer_reference`; omit all payment fields for this seeded no-payment-required catalog. | `SubscriptionResponse.Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`; read the same projection fields as `FindSubscription` plus `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` if reporting next billing fallback. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback | none | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md`; `records-4-Su-We.md`; `enums.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers` / my subscriptions | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default): Task<IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>>` | no body | each `SubscriptionResponse.Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`; project `Product.Handle`, `Product.Name`, `ProductPriceInCents`, `CurrentBillingAmountInCents`, `State`, `NextAssessmentAt` (or `CurrentPeriodEndsAt` as a documented response field if the application selects a fallback) | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | none | `operations/Customers.md`; `records-3-Of-Su.md`; `records-4-Su-We.md`; `enums.md` |

### Enum values needed

| Type | Values / use | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`; return the provider value as state, not an app-invented reduced enum. | `enums.md` |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | Read its returned value for plan interval display; no request field needs it. | `records-3-Of-Su.md` |

### Client construction, authentication, and servers

| Fact | Contract | Source |
|---|---|---|
| Package / client | NuGet `AsadAli.AdvancedBilling.Sdk`; map source tag `v1.0.2`; `MaxioAdvancedBilling.MaxioAdvancedBillingClient` constructor is `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`. | `sdk-map.md` |
| Auth | Set `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions.BasicAuth` to `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` with `Username` from `Maxio:ApiKey` and literal `Password = "x"`. | `sdk-map.md` |
| Production server | Set `Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us`. Set `options.Server.Production.Us.Site` from `Maxio:Subdomain`; when `Maxio:BaseUrl` is set, use it verbatim in `options.Server.Production.Us.BaseUrl` instead. Production-US default template is `https://{site}.chargify.com`. | `sdk-map.md` |
| Configuration validation | `Maxio:ApiKey`, `Maxio:Subdomain`, and `Maxio:ProductFamilyHandle` are required; `Maxio:BaseUrl` is optional. Configuration source/user-secrets mechanics and PublicApi configuration layout are application work. | YOUR CALL — not in the map |

## 3. Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership/lifetime and the generated registration’s unnamed-client scope determine whether this integration leaks handlers or changes unrelated callers. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (authentication) — Basic credentials must be configured before client construction and must not leak through configuration/logging. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Steps 2–5 (endpoint calls) — leading nullable parameters without C# defaults, nested envelopes, and exact `ct` names can mis-bind a call or read the wrapper as the resource. **MUST load `dotnet-calling-endpoints`** before writing calls.

⚠ Steps 2–5 (models) — optional provider model fields do not select a request for you, and returned string-enums must be read safely. **MUST load `dotnet-models`** before constructing or mapping request/response models.

⚠ Step 4 (idempotent enrollment) — transport-failure retries and status retries have different resend behavior, so a provider write can be observed more than once unless the application-level idempotency state owns the outcome. **MUST load `dotnet-configuration-resilience`** before tuning/relying on retries.

⚠ Step 6 (error boundary) — Case A typed exceptions and Case B raw exceptions expose different error-reading paths; treating them as interchangeable loses provider rejection detail. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary; **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 (tests) — the SDK’s concrete client is tested at its supplied `HttpClient` seam, and retry assertions must distinguish a response status from a transport failure. **MUST load `dotnet-testing`** before writing integration tests.

## 4. REQUIRED READING

Load these before implementation starts; this sheet deliberately does not carry their contents.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 client/DI registration |
| `dotnet-authentication` | Step 1 credentials |
| `dotnet-configuration-resilience` | Steps 1 and 4 timeout, retry, base URL, and provider-write behavior |
| `dotnet-calling-endpoints` | Steps 2–5 SDK operations |
| `dotnet-models` | Steps 2–5 SDK request and response mappings |
| `dotnet-error-handling` | Step 6 provider exception boundary |
| `dotnet-testing` | Step 6 integration tests |

## 5. Assumptions & Blockers

| Type | Item |
|---|---|
| Assumption | Application JWT claims expose a stable, non-empty eShopOnWeb user identifier and enough user profile data to satisfy Maxio `CreateCustomer` required `FirstName`, `LastName`, and `Email`; selecting/extracting those claims is application work. |
| Assumption | A repeat request for the same authenticated user and same selected product handle represents the same enrollment; a durable unique mapping plus concurrency control is the application mechanism that makes double-clicks converge. |
| Assumption | The caller-facing POST body contains only a product handle (and, if the application elects, an app idempotency key); its shape and HTTP status mapping are application decisions. |
| Assumption | Catalog enumeration can use `ListProducts` and filter the returned `Product.ProductFamily.Handle` locally; this avoids any numeric product-family dependency. |
| Blocker | None in the SDK map. |
