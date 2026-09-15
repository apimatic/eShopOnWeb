# Maxio recurring subscriptions — plan and contract sheet

## 1. Scope & sequence

1. Register one reused `MaxioAdvancedBilling.MaxioAdvancedBillingClient` for the Maxio
   Production server group; bind `Maxio:ApiKey`, `Maxio:Subdomain`, and optional
   `Maxio:BaseUrl`, using Basic authentication. **MUST load** the client, authentication,
   and resilience companion skills named below before implementation.
2. Implement `GET /api/subscription-plans` with `client.Products.ListProducts(...)`, manually
   walking its `page`/`perPage` results and retaining products whose nested
   `ProductFamily.Handle` equals `Maxio:ProductFamilyHandle`; expose handle/name/price and
   interval from each product. No catalog ID is configured or hard-coded.
3. Implement `POST /api/subscriptions`: derive a stable application-owned customer reference
   from the authenticated token identity; read it with `ReadCustomerByReference`; on mapped
   404 create the customer with `CreateCustomer`; then derive the subscription reference chosen
   by the application, read it with `FindSubscription`, and only when absent call
   `CreateSubscription` with `CustomerReference`, `ProductHandle`, optional
   `ProductPricePointHandle`, and `Reference`. On a create race or unknown transport outcome,
   reconcile with `FindSubscription` before reporting failure.
4. Implement `GET /api/my-subscriptions`: resolve the same customer reference, obtain its
   Maxio customer ID, call `ListCustomerSubscriptions`, and map every returned subscription to
   plan, price, state, and next billing date.
5. Add the application’s JWT authorization and response/error translation around these calls;
   pass the request-abort token through every SDK call as `ct: ct`. Test through the SDK’s
   supplied `System.Net.Http.HttpClient` seam, including 404/422, malformed bodies, race
   reconciliation, and transport-failure behavior.

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

All SDK operations are async and throw on non-2xx; there are no generated no-throw result
variants. `ct` is always the final parameter. A response envelope must be unwrapped before
reading its inner record; nullable inner response members must be checked defensively.

| Public endpoint / purpose | Controller property · exact SDK method signature | Request model and fields used (`C# (wire)`; required?) | Response envelope and fields used | Error case, accessors, payload | Pagination | Source |
|---|---|---|---|---|---|---|
| `GET /api/subscription-plans` — catalog | `client.Products.ListProducts(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, System.DateTimeOffset? endDate, System.DateTimeOffset? endDatetime, System.DateTimeOffset? startDate, System.DateTimeOffset? startDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, System.Threading.CancellationToken ct = default)`; call with named arguments and the eight leading nullable arguments explicitly (normally `null`), `includeArchived: false`, and `ct: ct`. | None. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; each `ProductResponse.Product` is required. Read `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?` then its `Handle (handle): string?`, `ArchivedAt (archived_at): System.DateTimeOffset?`, `ProductPricePointHandle (product_price_point_handle): string?`, and `ProductPricePointName (product_price_point_name): string?`. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; read `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsBytes()`, or `ReadAsJson<T>()` directly. | Manual `page` + `perPage`; repeat until a page contains fewer than the requested page size. | `operations/Products.md`; `records-3-Of-Su.md`; `enums.md` |
| `POST /api/subscriptions` — locate customer by token identity reference | `client.Customers.ReadCustomerByReference(string reference, System.Threading.CancellationToken ct = default)` | None; `reference` is the stable customer reference selected by the application. | `MaxioAdvancedBilling.Models.CustomerResponse`; read required `Customer (customer): MaxioAdvancedBilling.Models.Customer`, especially `Id (id): int?`, `Reference (reference): string?`, and identity fields needed by the application. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; inspect `ex.Error.StatusCode` for the not-found branch, otherwise use the raw accessors directly. | None. | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| `POST /api/subscriptions` — create missing customer | `client.Customers.CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, System.Threading.CancellationToken ct = default)`; `body` must be passed explicitly. | `MaxioAdvancedBilling.Models.CreateCustomerRequest.Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer`, required. Inner `MaxioAdvancedBilling.Models.CreateCustomer`: `FirstName (first_name): string`, required; `LastName (last_name): string`, required; `Email (email): string`, required; `Reference (reference): string?`, optional. Set the three required identity values and the stable reference. Leave the remaining optional fields unset unless the application has them; if `Country (country)` or `State (state)` is supplied, the operation notes require ISO country/state codes. | `MaxioAdvancedBilling.Models.CustomerResponse`; read `Customer`, then `Id`, `Reference`, and identity fields. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` for 422, then `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. `CustomerErrorResponse1.Errors (errors): MaxioAdvancedBilling.Models.Errors?`. | None. | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| `POST /api/subscriptions` — locate an existing enrollment | `client.Subscriptions.FindSubscription(string? reference, System.Threading.CancellationToken ct = default)`; nullable `reference` must be passed explicitly. | None; use the stable subscription reference selected by the application. | `MaxioAdvancedBilling.Models.SubscriptionResponse`; read optional `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` for 404, then `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. | None. | `operations/Subscriptions.md`; `records-4-Su-We.md` |
| `POST /api/subscriptions` — enroll with handles | `client.Subscriptions.CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, System.Threading.CancellationToken ct = default)`; `body` must be passed explicitly. | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest.Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription`, required. Set `ProductHandle (product_handle): string?`, `CustomerReference (customer_reference): string?`, and `Reference (reference): string?`; set `ProductPricePointHandle (product_price_point_handle): string?` when the selected plan supplies one. Leave `ProductId`, `ProductPricePointId`, `CustomerId`, and `CustomerAttributes` unset: handles and the pre-existing customer reference are the intended selectors. Leave payment-profile/card fields unset only if the selected product/site does not require payment; otherwise a tokenized payment-profile input must be added by the application. | `MaxioAdvancedBilling.Models.SubscriptionResponse`; read optional `Subscription`, then `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): System.DateTimeOffset?`, `Reference (reference): string?`, `Product (product): MaxioAdvancedBilling.Models.Product?` and its `Handle`, `Name`, `ProductPricePointHandle`, and `ProductPricePointName`. Treat `CurrentPeriodEndsAt` as the next-billing date exposed by this integration: the mapped subscription notes identify it as the field to verify the next billing date, and `Subscription` has no `NextBillingAt` field. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` for 422, then `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` required. | None. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md`; `records-4-Su-We.md`; `enums.md` |
| `GET /api/my-subscriptions` — list customer enrollments | `client.Customers.ListCustomerSubscriptions(int customerId, System.Threading.CancellationToken ct = default)` | None; `customerId` is the non-null Maxio `Customer.Id` obtained from the customer envelope. | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; for each item read optional `Subscription`, then `Product.Handle`/`Product.Name`/`ProductPricePointHandle`/`ProductPricePointName`, `ProductPriceInCents (product_price_in_cents): long?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, and `CurrentPeriodEndsAt (current_period_ends_at): System.DateTimeOffset?` as next billing date. Reject or translate a response whose required envelope or customer ID is unexpectedly absent. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; read `StatusCode`, `ReadAsString()`, `ReadAsBytes()`, or `ReadAsJson<T>()` directly. | None. | `operations/Customers.md`; `records-3-Of-Su.md`; `records-4-Su-We.md`; `enums.md` |

⚠ A request model may mark nothing required, and then `required?` selects nothing for you. The
operation Notes still govern acceptance: `CreateCustomer` requires the three inner customer
fields shown above; `CreateSubscription` Notes require a product selector and an existing
customer selector, and may require payment information according to the product configuration.

### Required enum values

| Fully-qualified type | Literal member (`wire value`) | Use |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day (day)`, `Month (month)` | Plan interval returned from `Product`. |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | Preserve the provider state in the application response; it is a `StringEnum`, not a C# enum. |

### Client construction, configuration, authentication, and server

| Concern | Authoritative contract |
|---|---|
| Package / root | Install NuGet package `AsadAli.AdvancedBilling.Sdk`; use root namespace `MaxioAdvancedBilling`. |
| Construction | `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`. Reuse the supplied `HttpClient` and client instance through DI. |
| Auth | Set `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions.BasicAuth` to `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` with `Username = Maxio:ApiKey` and `Password = "x"`; set it before construction or in the DI options callback. |
| Server / sandbox | Use `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (the map’s default US production template is `https://{site}.chargify.com`) and set `options.Server.Production.Us.Site = Maxio:Subdomain`. If `Maxio:BaseUrl` is supplied, assign it verbatim to `options.Server.Production.Us.BaseUrl`; do not hard-code a catalog ID or host. The map exposes US/EU server environments and a base-URL override, not a separate sandbox enum; the deployment-supplied sandbox URL is `UNVERIFIED` by this map. |
| Configuration keys | `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl` are the requested application binding keys (`YOUR CALL — not in the map`). The first three must be present for the configured flow; `Maxio:BaseUrl` controls the optional verbatim server override. |
| Retry / total bound | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions.Retry` is `MaxioAdvancedBilling.Core.Configuration.RetryOptions`; choose its values with the resilience skill, and put the whole-call deadline in the cancellation token passed as `ct:`. Do not assume SDK timeout settings define the whole endpoint budget. |

### Idempotency and lookup recommendations

| Decision | Recommendation | Source / trust |
|---|---|---|
| Customer identity | Use the token identity’s stable application-owned reference as `CreateCustomer.Reference`; `ReadCustomerByReference` is the exact Maxio lookup and returns one customer by unique reference. If lookup is 404, create; if concurrent creation returns the typed 422 path, look up again and return the existing customer when found. | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| Subscription identity | Use a deterministic application-owned `CreateSubscription.Reference` for the intended enrollment, pre-read with `FindSubscription`, and reconcile after 422/transport uncertainty by reading the same reference. Whether Maxio enforces uniqueness of subscription `reference` is not stated in the map and cannot be proven without live traffic: `UNVERIFIED`. Do not claim strict at-most-one upstream write until verified; surface an unknown outcome or use an application-side send guard if that guarantee is required. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `UNVERIFIED` for provider uniqueness |
| Plan selection | The catalog endpoint returns handles; accept the selected `ProductHandle` and optional `ProductPricePointHandle` from that catalog/application contract and send those fields to Maxio. Keep the configured `Maxio:ProductFamilyHandle` as the family selector; never substitute a numeric catalog ID. | `operations/Products.md`; `operations/Subscriptions.md`; `records-3-Of-Su.md` |
| Next billing date | Return `Subscription.CurrentPeriodEndsAt`; the mapped subscription Notes say to use this field to verify a changed next billing date, and no `NextBillingAt` property exists on the mapped `Subscription` record. | `operations/Subscriptions.md`; `records-3-Of-Su.md` |

## 3. Trap notes

⚠ Step 1 (client registration) — the SDK client and `HttpClient` lifetimes, nested server override,
and DI registration shape can affect handler reuse and configuration scope. **MUST load
`dotnet-client-initialization` and `dotnet-configuration-resilience`** before wiring the client.

⚠ Step 1 (authentication) — the credential property, construction order, and configuration-backed
secret handling are easy to wire incorrectly. **MUST load `dotnet-authentication`** before setting
credentials.

⚠ Step 2–4 (calls) — list/search methods have leading nullable parameters without C# defaults, and
the cancellation token is a named `ct` parameter. **MUST load `dotnet-calling-endpoints`** before
writing calls.

⚠ Step 2–4 (models) — nested envelopes, nullable response records, `StringEnum` values, and
wire-name/C#-name differences can silently produce the wrong mapping. **MUST load
`dotnet-models`** before constructing or mapping models.

⚠ Step 2–4 (error boundary) — typed and raw errors use different accessors, and every call is
throw-only. **MUST load `dotnet-error-handling`** before writing catches or translation middleware.

⚠ Step 3 (idempotent enrollment) — a failed POST can leave an unknown provider outcome, and a
retry or reconciliation choice affects whether a duplicate enrollment is possible. **MUST load
`dotnet-configuration-resilience`** before configuring retries, timeouts, or the write boundary.

⚠ Step 5 (tests) — stubbing the wrong seam or asserting only status retries does not cover transport
replays and deserialization/error paths. **MUST load `dotnet-testing`** before writing tests.

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary.

## 4. REQUIRED READING

Load these before implementation starts; this sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1 client construction, DI, and `HttpClient` lifetime.
- `dotnet-authentication` — Step 1 Basic credentials and configuration-backed secrets.
- `dotnet-calling-endpoints` — Steps 2–4 async calls, exact parameters, envelopes, and `ct`.
- `dotnet-models` — Steps 2–4 request construction, wire names, nullable fields, and enum mapping.
- `dotnet-error-handling` — Steps 2–5 exception boundary, typed/raw accessors, and `JsonException` paths.
- `dotnet-configuration-resilience` — Steps 1 and 3 retry, timeout, base URL, and write reconciliation hazards.
- `dotnet-testing` — Step 5 `HttpClient` handler seam and success/error/retry coverage.

## 5. Assumptions & Blockers

- **Blocker:** the SDK’s `CreateCustomer` model requires `FirstName`, `LastName`, and `Email`, but
  the JWT claims or another application identity/profile source supplying those values were not
  specified. The implementer must resolve that source before customer creation can be guaranteed.
- **Blocker:** the subscription Notes state that payment information may be required by the selected
  product. No payment-profile ID or tokenized payment input is specified for `POST /api/subscriptions`.
  Resolve this by product/site configuration or add an approved tokenized payment-profile path; do
  not send raw card data by assumption.
- **UNVERIFIED:** the map exposes subscription lookup by `Reference` but does not establish provider
  uniqueness enforcement for that field. The plan therefore requires pre-read plus post-failure
  reconciliation and does not claim strict at-most-one upstream write until sandbox/live verification.
- **Assumption:** the application will choose and persist/derive stable customer and subscription
  references from token identity and its enrollment policy; the SDK only supplies the reference
  fields and lookup operations, not the application’s identity or concurrency policy.
- **UNVERIFIED:** the exact sandbox host is deployment configuration. The map documents
  `ServerEnvironment.Us` and the nested Production `BaseUrl` override, not a dedicated sandbox
  selector; supply the sandbox URL through optional `Maxio:BaseUrl`.
