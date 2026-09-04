# Maxio Advanced Billing integration contract sheet

## 1. Scope & sequence

Implement the additive subscription capability in `src/PublicApi`, keeping caller identity from the already-authenticated JWT and keeping the Maxio client behind the integration boundary.

1. Load every skill in §4 before implementation. Register the `AsadAli.AdvancedBilling.Sdk` client once, bind the four requested `Maxio:*` settings, configure Basic auth, and configure the Production US server with the configured subdomain plus the optional base-URL override.
2. Implement `GET /api/subscription-plans` with `ProductFamilies.ListProductsForProductFamily`, passing the configured family as the `handle:<value>` path form and manually consuming its page/per-page results. Map only the plan fields the API exposes.
3. Implement customer resolution for the authenticated JWT identity with `Customers.ReadCustomerByReference`; on a confirmed provider not-found result, create with `Customers.CreateCustomer` using the same deterministic reference. A losing concurrent create must reconcile by reading that reference again.
4. Implement `POST /api/subscriptions`: validate the selected product handle against the configured family’s handle-based plan catalog, derive a deterministic subscription reference from the authenticated identity and selected product handle, find it with `Subscriptions.FindSubscription`, and create it with `Subscriptions.CreateSubscription` only when absent. Send the product by `ProductHandle`; do not send numeric product IDs or payment-profile/card/bank fields.
5. Implement `GET /api/my-subscriptions` by resolving the customer and calling `Customers.ListCustomerSubscriptions`. The SDK operation returns all subscriptions for the customer; the application must make its own explicit “current” filtering/presentation decision.
6. Add the one error boundary around every SDK call, including reads. Preserve provider-vs-transport/parse distinctions without exposing SDK exception text. Add in-memory-DB verification and SDK HTTP-client-seam tests, then build and perform the requested sandbox end-to-end verification while inspecting the first outbound requests.

## 2. CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal
C# identifier. The cancellation-token parameter really is named `ct`: in named
arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take
each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation contracts

| Caller capability / controller property | Method signature (exact parameter names/order) | Request model and fields used (`Name (wire_name): type, required?`) | Response envelope and integration fields | Error case, accessors, payload | Pagination | Source |
|---|---|---|---|---|---|---|
| `GET /api/subscription-plans` → `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | No body. Pass all leading nullable parameters explicitly (normally `null`), then `page`/`perPage` by name. `productFamilyId` is the string `handle:` + the configured `Maxio:ProductFamilyHandle` value; do not convert it to a number. | `System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` directly. Each `ProductResponse.Product` (`product`): `MaxioAdvancedBilling.Models.Product` is required. Read `Product.Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `ProductPricePointName (product_price_point_name): string?`, and `ProductPricePointHandle (product_price_point_handle): string?`. | **Case A:** `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`. Handle `TryGetString(out string)` for 404, then `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` last. | Manual `page` + `perPage`; defaults are 1 and 20. Continue until the provider returns fewer than the requested page size (and handle an exact-full final page by requesting the next page). | `map/operations/ProductFamilies.md`; `map/models/records-3-Of-Su.md`; `map/models/enums.md`; scoped source docs confirm `productFamilyId` accepts an ID or `handle:`-prefixed handle (`Api/ProductFamilies.cs`). |
| Customer lookup for `POST /api/subscriptions` and `GET /api/my-subscriptions` → `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | No body. `reference` is the deterministic reference derived from the stable JWT identity chosen by the application. | `MaxioAdvancedBilling.Models.CustomerResponse` envelope: `Customer (customer): MaxioAdvancedBilling.Models.Customer` is required. Read `Id (id): int?`, `Reference (reference): string?`, and `Email (email): string?`; fail safely if the nullable `Id` is absent before an operation that requires it. | **Case B:** `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`. Read `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsBytes()`, or `ReadAsJson<T>()` (the latter can throw `JsonException`). The map gives no typed not-found accessor: treat only a confirmed provider not-found status as absence; every other status is a provider failure. **UNVERIFIED:** the exact no-match status for this raw lookup requires live/provider verification; never turn an unparseable body into absence. | None. | `map/operations/Customers.md`; `map/models/records-2-Cr-Ne.md`. |
| Customer creation for `POST /api/subscriptions` → `client.Customers` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.CreateCustomerRequest`: `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer !req`. Used inner fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` (set to the same deterministic JWT-derived reference). All other inner fields are optional and omitted unless the application has validated values. The operation Notes tie supplied `Country (country): string?` to a two-character ISO country code and supplied `State (state): string?` to the applicable ISO state code; do not invent either value. | `MaxioAdvancedBilling.Models.CustomerResponse`; read `Customer.Id`, `Customer.Reference`, and `Customer.Email` as above. | **Case A:** `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`. Handle `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` for 422, then `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` last. The typed payload has `Errors (errors): MaxioAdvancedBilling.Models.Errors?`; do not expose its raw SDK representation directly. | None. | `map/operations/Customers.md`; `map/models/records-1-Ac-Cr.md`; `map/models/records-2-Cr-Ne.md`. |
| Subscription lookup for `POST /api/subscriptions` → `client.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)` | No body. Pass the deterministic subscription `reference` explicitly. | `MaxioAdvancedBilling.Models.SubscriptionResponse` envelope: `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`. Null is a distinct malformed/empty success result and must not be treated as “not found”. | **Case A:** `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`. Handle `TryGetNoContent(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` for 404 as the absence branch; then `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` last for other untyped statuses. | None. | `map/operations/Subscriptions.md`; `map/models/records-4-Su-We.md`. |
| Subscription creation for `POST /api/subscriptions` → `client.Subscriptions` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`: `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription !req`. Used fields: `ProductHandle (product_handle): string?` (selected handle), `CustomerId (customer_id): int?` (the Maxio customer found/created by reference), and `Reference (reference): string?` (deterministic idempotency/reconciliation reference). Leave `ProductId`, `ProductPricePointId`, and all payment fields (`PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`) unset; the request has no required payment field in the generated model. `CustomerReference (customer_reference): string?` is an alternative customer selector and is not needed when `CustomerId` is used. `NextBillingAt (next_billing_at): DateTimeOffset?` is an optional request field, not a response field, and is not part of this signup flow. | `MaxioAdvancedBilling.Models.SubscriptionResponse`; unwrap nullable `Subscription`. Read `Id (id): int?`, `Reference (reference): string?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `Product (product): MaxioAdvancedBilling.Models.Product?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `Currency (currency): string?`, and nested `Product.Name`, `Product.Handle`, `Product.PriceInCents`, `Product.ProductPricePointName`, and `Product.ProductPricePointHandle`. The generated response has no `next_billing_at` member; do not fabricate one. | **Case A:** `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`. Handle `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` for 422, then `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` last. `ErrorListResponse1.Errors (errors): System.Collections.Generic.IReadOnlyList<string> !req`. | None. | `map/operations/Subscriptions.md`; `map/models/records-2-Cr-Ne.md`; `map/models/records-3-Of-Su.md`; `map/models/records-4-Su-We.md`. |
| `GET /api/my-subscriptions` → `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | No body. `customerId` is the `Customer.Id` returned by the customer envelope; this numeric identifier is required by this customer-scoped SDK operation even though product selection uses handles. | `System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` directly. Unwrap each nullable `Subscription`; map the same plan, price, state, currency, `NextAssessmentAt`, and `CurrentPeriodEndsAt` fields as the create response. The operation Notes say it lists **all** subscriptions belonging to the customer; “current” filtering is application policy, not an SDK filter here. | **Case B:** `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`. Read `StatusCode` and `ReadAsString()` directly; there are no typed accessors. | None. | `map/operations/Customers.md`; `map/models/records-4-Su-We.md`; `map/models/records-3-Of-Su.md`. |

### Required response mapping

| Application concept | SDK source field(s) and exact type | Contract note |
|---|---|---|
| Plan on the plans endpoint | `MaxioAdvancedBilling.Models.Product.Name: string?`, `Handle: string?`, `PriceInCents: long?`, `Interval: int?`, `IntervalUnit: MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, plus optional price-point name/handle | Product-list items are `ProductResponse.Product`; unwrap that required envelope member. Use the handle as the stable plan selector. |
| Plan on a subscription | `MaxioAdvancedBilling.Models.Subscription.Product: MaxioAdvancedBilling.Models.Product?` → `Name`, `Handle`, `ProductPricePointName`, `ProductPricePointHandle` | The nested product is nullable. Numeric product IDs are not the selection contract for this feature. |
| Price | Subscription `ProductPriceInCents: long?` and `CurrentBillingAmountInCents: long?`; nested product `PriceInCents: long?` | Keep these distinct in the application DTO: the SDK exposes both product price and current billing amount. Do not assume one is interchangeable with the other. |
| State | `MaxioAdvancedBilling.Models.Subscription.State: MaxioAdvancedBilling.Models.Enums.SubscriptionState?` | Read the enum’s wire value through the model API and preserve unknown/future values safely. |
| Next billing display | `NextAssessmentAt: DateTimeOffset?` and `CurrentPeriodEndsAt: DateTimeOffset?` | The model exposes no response `NextBillingAt`. `NextAssessmentAt` is the provider’s next-assessment field; `CurrentPeriodEndsAt` is the period-end field. If the product requires an exact “billing date” semantic, return both or label the chosen field honestly rather than inventing a wire property. **UNVERIFIED:** live/provider semantics of “next assessment” versus the application’s desired billing-date label. |

### Enum values needed for subscription-state mapping

`MaxioAdvancedBilling.Models.Enums.SubscriptionState` is a `StringEnum`, not a C# enum. Its map-listed values are:

| C# member | Wire value |
|---|---|
| `Pending` | `pending` |
| `FailedToCreate` | `failed_to_create` |
| `Trialing` | `trialing` |
| `Assessing` | `assessing` |
| `Active` | `active` |
| `SoftFailure` | `soft_failure` |
| `PastDue` | `past_due` |
| `Suspended` | `suspended` |
| `Canceled` | `canceled` |
| `Expired` | `expired` |
| `Paused` | `paused` |
| `Unpaid` | `unpaid` |
| `TrialEnded` | `trial_ended` |
| `OnHold` | `on_hold` |
| `AwaitingSignup` | `awaiting_signup` |

Source: `map/models/enums.md`.

### Client, configuration, auth, and server facts

| Concern | Contract |
|---|---|
| Package / namespace | Install NuGet package `AsadAli.AdvancedBilling.Sdk`; import root namespace `MaxioAdvancedBilling`. The client is `MaxioAdvancedBilling.MaxioAdvancedBillingClient`. |
| Construction | The only constructor is `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`. Configure options before construction and reuse the client/HTTP pipeline. |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions.Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`, `.Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`, `.Server: MaxioAdvancedBilling.ServerOptions`, `.BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`. |
| Auth | Set `BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`. Basic username is the Maxio API key; the literal password `"x"` is the SDK’s required protocol value. Never hard-code the API key. |
| Environment / server | Use `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (the map’s default unless the account is EU-hosted). Production server configuration is `MaxioAdvancedBilling.ServerOptions.Production`, whose US options are `MaxioAdvancedBilling.Servers.ProductionOptions.UsOptions`; default `BaseUrl` is `https://{site}.chargify.com` and default `Site` is `subdomain`. |
| Requested settings binding | `Maxio:ApiKey` → Basic username; `Maxio:Subdomain` → `options.Server.Production.Us.Site`; `Maxio:ProductFamilyHandle` → the plans path value `handle:<value>`; optional `Maxio:BaseUrl` → `options.Server.Production.Us.BaseUrl`. Leave the generated default URL template in place when `Maxio:BaseUrl` is absent; a literal override is used as-is. The four binding keys are from the user brief, not invented environment-variable names. |
| Secrets | Populate .NET user-secrets from the deployment-provided environment values for the four requested binding keys. No API key, subdomain, family handle, product handle, or base URL belongs in source. |
| DI / HTTP ownership | Use the SDK’s `AddMaxioAdvancedBillingClient` registration or an equivalent factory with the SDK’s `HttpClient` constructor seam; the supplied `HttpClient` must be long-lived/reused. Attach any first-run wire logging to the supplied pipeline and remove/gate it after verification. |

Sources: `sdk-map.md`; scoped source declarations `MaxioAdvancedBillingClientOptions.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`, and `Servers/ServerEnvironment.cs`.

### Error and idempotency boundary

- Every listed operation is async and throw-only; there are no generated `…Result` variants. Catch the exact `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` for each row. Case B’s `RawError` is read directly and has no `TryGetRawError`; Case A’s typed accessors must be checked before the inherited raw fallback.
- `Customers.CreateCustomer` Notes explicitly make `Reference` the unique application/customer identifier: only one customer may be created for a given reference. Derive the reference deterministically from the stable JWT identity, use it for lookup and create, and after a concurrent 422 create race read the same reference again before reporting failure.
- `Subscriptions.FindSubscription` gives a provider lookup by subscription reference and has a typed 404 `TryGetNoContent` accessor. `CreateSubscription` accepts a nullable `Reference`, but the operation Notes do not state that subscription references are uniqueness-enforced. The SDK has no separate idempotency-key parameter on either create signature. Therefore “find then create” is not atomic: production-grade duplicate prevention requires an application-owned uniqueness/coordination boundary keyed by the deterministic identity/product reference, plus reconciliation after any ambiguous create failure. The in-memory database can verify this within the test process; it cannot establish cross-instance production guarantees. This is an application decision, not an SDK guarantee.
- The SDK resilience pipeline can make a non-idempotent POST’s outcome ambiguous on transport failure. Do not blindly resend a create; reconcile with the deterministic reference. The exact retry/timeout and one-send guard mechanics belong to `dotnet-configuration-resilience`.
- Maxio’s `CreateSubscription` Notes say payment information **may be required depending on the subscribed Product’s options**. The requested flow intentionally omits payment fields, but its acceptance depends on the configured sandbox/product setup; see §5 Blockers.

## 3. Trap notes

⚠ Step 1 (client registration) — the SDK’s client/HTTP-pipeline lifetime and DI registration shape affect handler reuse, DNS rotation, and the blast radius of default-client configuration. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (authentication and secrets) — the generated options expose a nullable Basic-credentials property, and setting credentials at the wrong construction/configuration point or sourcing values outside the requested binding keys can produce an unauthenticated or secret-leaking client. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Step 2 (plans and all SDK calls) — list/search signatures contain leading nullable parameters without defaults, and `ct` is the literal cancellation-token parameter; positional calls can silently mis-bind filters. **MUST load `dotnet-calling-endpoints`** before writing calls.

⚠ Step 2 (request/response mapping) — generated records are immutable, envelopes must be unwrapped, enum values are generated string-enums, nullable nested `Subscription`/`Product` members need guards, and unknown JSON fields may disappear. **MUST load `dotnet-models`** before constructing or mapping models.

⚠ Step 2 (error boundary) — Case A typed errors, Case B raw errors, transport failures, malformed bodies, and provider status preservation require different branches; a shared `ApiError` helper cannot see operation-specific accessors. **MUST load `dotnet-error-handling`** before writing catches or HTTP error mapping.

⚠ Step 1/2 (resilience and pagination) — retry/status eligibility, transport retries on writes, per-attempt versus whole-call bounds, base-URL selection, manual page traversal, and first-wire logging all affect duplicate creation and outage behavior. **MUST load `dotnet-configuration-resilience`** before tuning or relying on the client.

⚠ Step 6 (tests) — the test seam is the supplied `HttpClient`/fake handler, and status-fault versus transport-fault behavior must be asserted separately, including request method/path/query/body. **MUST load `dotnet-testing`** before writing verification.

`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 4. REQUIRED READING

These skills are to be loaded **before implementation starts**; this sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1 client construction, DI, `HttpClient` ownership and lifetime.
- `dotnet-authentication` — Step 1 Basic credentials and configuration-backed secret wiring.
- `dotnet-calling-endpoints` — Steps 2–5 operation calls, exact argument use, cancellation, envelopes.
- `dotnet-models` — Steps 2–5 request records, wire names, nullable fields, enums, and response mapping.
- `dotnet-error-handling` — Steps 2–6 typed/raw exceptions, parse failures, transport failures, and boundary translation.
- `dotnet-configuration-resilience` — Steps 1–6 server/base URL, retries, timeouts, pagination, logging, and write ambiguity.
- `dotnet-testing` — Step 6 in-memory verification, fake `HttpClient` seam, and error/transport coverage.

## 5. Assumptions & Blockers

### Assumptions

- The JWT contains a stable caller identifier and the authenticated eShopOnWeb user has values from which the required Maxio customer `FirstName`, `LastName`, and `Email` can be mapped. The actual claim names and profile lookup are application-owned and are not specified here.
- “Current subscriptions” means the customer-scoped list is the source; the application will explicitly define whether states such as `active`, `trialing`, `past_due`, or other returned states are shown or filtered. The SDK operation itself returns all customer subscriptions.
- The application will choose and enforce a deterministic, collision-resistant subscription reference and a uniqueness/coordination strategy appropriate to its deployment. The SDK does not make find-and-create atomic.
- Sandbox configuration will use the requested `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl` bindings, with deployment values loaded into .NET user-secrets rather than source.

### Blockers

- **Paymentless signup must be enabled by the selected Maxio Product configuration.** The `CreateSubscription` operation Notes state that payment information may be required depending on Product options, while this requested body omits payment fields. Until the configured sandbox products are verified to permit signup without a payment method, the provider may reject the required POST; this is a provider-configuration blocker, not something the generated request model can settle.
- **End-to-end sandbox verification requires valid deployment values** for the requested configuration bindings and at least one configured product family/product handle. No secret values or provider handles are included in this sheet.
