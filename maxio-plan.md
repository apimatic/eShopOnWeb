# Maxio subscription integration plan

## 1. Scope & sequence

| Step | Work / operations | Source |
|---|---|---|
| 1 | Add `AsadAli.AdvancedBilling.Sdk` **1.0.0** (the package version in the SDK source tagged `v1.0.2`, map commit `15db14b`). Bind only `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`; load their values from user-secrets populated from the named environment variables. Reject missing/blank required settings at startup; never serialize a secret. | `sdk-map.md`; `MaxioAdvancedBilling.csproj` (SDK source) |
| 2 | Register one SDK client with the PublicApi HTTP-client factory. Configure Basic auth and Production server/site from the bound options. Use the configured subdomain for the server `Site`; when `BaseUrl` is nonempty override Production US/EU `BaseUrl` verbatim. Sandbox is a deployment setting: select the documented server environment appropriate to the configured sandbox host, never derive or hard-code a catalog/site hostname. | `sdk-map.md` |
| 3 | Create an application billing gateway plus a durable enrollment/idempotency store keyed by the authenticated application's stable user ID and requested product handle. The gateway alone owns Maxio calls; endpoints receive the token identity through the existing PublicApi convention and never accept a caller-supplied user/customer ID. | YOUR CALL — not in the map |
| 4 | Implement `GET /api/subscription-plans`: call `ProductFamilies.ListProductFamilies`, find the configured family by `ProductFamily.Handle`, then call `ProductFamilies.ListProductsForProductFamily` using the discovered ID (not any seeded numeric ID). Return non-archived plans with handle, name, description, interval, currency-independent cents price and display price. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| 5 | Implement `POST /api/subscriptions`: validate the requested handle against the Step-4 catalog; enter the user+plan idempotency critical section; look up the Maxio customer by deterministic application-owned reference with `Customers.ReadCustomerByReference`, create it if absent with the required profile attributes and that unique reference, then create the subscription with `ProductHandle`, `CustomerId`, and a deterministic application-owned subscription reference. Persist the returned Maxio IDs and final result atomically with the enrollment record. Return plan, provider billing price, state, and next billing date from the response. | `operations/Customers.md`; `operations/Subscriptions.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md` |
| 6 | Implement `GET /api/my-subscriptions`: use the authenticated user’s customer mapping (or lookup by its deterministic reference), call `Customers.ListCustomerSubscriptions`, and project only that customer’s returned subscriptions. Reconcile the durable enrollment metadata/catalog with provider fields to return plan, price, state and `NextAssessmentAt`; no caller-provided customer/subscription IDs participate. | `operations/Customers.md`; `records-3-Of-Su.md` |
| 7 | Add integration-boundary error translation, structured safe logging/correlation, concurrency/idempotency tests, SDK-handler tests, endpoint authentication/ownership tests, and endpoint/provider-failure tests. Do not change the existing basket/order checkout path. | YOUR CALL — not in the map |

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

All models below are `MaxioAdvancedBilling.Models`; all typed operation errors are
`MaxioAdvancedBilling.Errors`; raw errors are `MaxioAdvancedBilling.Core.ErrorResponse.RawError`;
the throwing generic is `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>`.

| Controller property · exact method signature | Request model / fields used | Response envelope / fields read | Error case / accessors | Pagination | Source |
|---|---|---|---|---|---|
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.ProductFamilies` · `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | No body. Pass all five filter arguments explicitly as `null` to list the configured family. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; each response has required `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily`; read `Id (id): int?`, `Handle (handle): string?`, `ArchivedAt (archived_at): DateTimeOffset?`. | Case B: `SdkException<RawError>`; read `StatusCode`, `ReadAsString()`, or typed `ReadAsJson<T>()` only at the boundary. | None. | `operations/ProductFamilies.md`; `records-3-Of-Su.md`; `sdk-map.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.ProductFamilies` · `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.Enums.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | No body. `productFamilyId` is the discovered family numeric ID formatted as a string; explicitly pass `null` for `dateField` through `include`; use named `page`/`perPage` and walk pages. Do not use seed IDs. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; required `Product (product): MaxioAdvancedBilling.Models.Product`; read `Id`, `Handle`, `Name`, `Description`, `ArchivedAt`, `PriceInCents`, `Interval`, `IntervalUnit`, `ProductPricePointId`, `ProductPricePointHandle`. | Case A: `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404], then `TryGetRawError(out RawError)` fallback. | Manual `page` + `perPage`; defaults 1 / 20. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers` · `ReadCustomerByReference(string reference, CancellationToken ct = default)` | No body; `reference` is the deterministic application-owned customer reference, never a request field. | `MaxioAdvancedBilling.Models.CustomerResponse` → required `Customer (customer): MaxioAdvancedBilling.Models.Customer`; read `Id (id): int?`, `Reference`, `Email`, `FirstName`, `LastName`. | Case B: `SdkException<RawError>`; an absent customer is the provider 404 status, otherwise translate by status/body through the boundary. | None. | `operations/Customers.md`; `records-2-Cr-Ne.md`; `sdk-map.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers` · `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` | Required outer `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer`; set all required inner fields: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`; set `Reference (reference): string?` to the deterministic user reference. The provider Notes say only one customer may use a given reference. Omit all other optional address/profile fields unless supplied by the app. | `CustomerResponse` → required `Customer`; read `Id`, `Reference`. | Case A: `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], then `TryGetRawError(out RawError)`. `CustomerErrorResponse1.Errors (errors): MaxioAdvancedBilling.Models.Errors?`; its known fields are `PerPage` and `PricePoint` string lists, so preserve a generic safe provider message if no useful field is present. On a create conflict, re-read by reference while still in the critical section. | None. | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Subscriptions` · `FindSubscription(string? reference, CancellationToken ct = default)` | No body; explicitly pass the deterministic application-owned subscription reference. | `MaxioAdvancedBilling.Models.SubscriptionResponse` → nullable `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`; guard null before projection. Read `Id`, `State`, `Product`, `ProductPriceInCents`, `CurrentBillingAmountInCents`, `NextAssessmentAt`, `Reference`, `Currency`. | Case A: `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404], then `TryGetRawError(out RawError)`. Use before a write and for defensive reconciliation after an ambiguous outcome. | None. | `operations/Subscriptions.md`; `records-3-Of-Su.md`; `sdk-map.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Subscriptions` · `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` | Required outer `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription`; set `ProductHandle (product_handle): string?` only from the Step-4 catalog, `CustomerId (customer_id): int?` from the lookup/create response, and `Reference (reference): string?` to the deterministic enrollment reference. The operation Notes require a product (`product_id` **or** `product_handle`) and an existing customer (`customer_id` **or** `customer_reference`); do not send numeric product IDs, custom pricing, payment profile data, or a collection method. | `SubscriptionResponse` → nullable `Subscription`; guard it. Read `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `Product (product): Product?`, `ProductPriceInCents`, `CurrentBillingAmountInCents`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `Currency`, `Reference`. | Case A: `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], then `TryGetRawError(out RawError)`. `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` required. Return a safe validation response for deterministic rejection; preserve no raw secrets/provider body in the API response. | None. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers` · `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | No body; `customerId` only from durable mapping or `ReadCustomerByReference`, never from HTTP input. | `IReadOnlyList<SubscriptionResponse>`; each nullable inner `Subscription` must be guarded. Read `Id`, `Product` (`Handle`, `Name`, `PriceInCents`), `ProductPriceInCents`, `CurrentBillingAmountInCents`, `State`, `NextAssessmentAt`, `Currency`, `Reference`. | Case B: `SdkException<RawError>`; read `StatusCode` and safe error content via the documented `RawError` members. | None. | `operations/Customers.md`; `records-3-Of-Su.md`; `sdk-map.md` |

### Enum values used

| Fully-qualified enum | Literal members (wire values) | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day (day)`, `Month (month)` | `models/enums.md` |

### Client construction, auth, and server facts

| Fact | Contract | Source |
|---|---|---|
| Package and client | `AsadAli.AdvancedBilling.Sdk` package version **1.0.0**; `MaxioAdvancedBilling.MaxioAdvancedBillingClient` constructor is `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`. | `sdk-map.md`; `MaxioAdvancedBilling.csproj` (SDK source) |
| Options/auth | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions.BasicAuth` is `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`; set `Username` from `Maxio:ApiKey` and `Password` to literal `"x"`. | `sdk-map.md` |
| Environment/server | `Environment` is `MaxioAdvancedBilling.Servers.ServerEnvironment`: `Us` (default, `https://{site}.chargify.com`) or `Eu` (`https://{site}.ebilling.maxio.com`). Set Production `Site` from `Maxio:Subdomain`; production base-address override is `options.Server.Production.Us.BaseUrl` or `.Eu.BaseUrl`, chosen for the configured environment. `Maxio:BaseUrl`, when nonempty, is used verbatim. | `sdk-map.md` |
| Catalog configuration | Only `Maxio:ProductFamilyHandle` selects the family. It is resolved at runtime by `ProductFamily.Handle`, then its returned `Id` feeds the product-list operation; seed numeric IDs are never configuration. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| Caller identity | Resolve stable user ID and required customer profile values from the existing JWT-authenticated application identity path. | YOUR CALL — not in the map |

## 3. Trap notes

⚠ Step 1 (client registration) — client/`HttpClient` lifetime and ownership determine whether handlers, sockets, and DNS refresh behave safely. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1/2 (configuration and auth) — server-environment/site versus verbatim base-URL override can route credentials to the wrong host, and Basic credential construction must not expose or retain the API key. **MUST load `dotnet-authentication`** before credential wiring and **MUST load `dotnet-configuration-resilience`** before server setup.

⚠ Step 4–6 (calls) — required-but-nullable optional parameters have no C# default and a positional list call can bind a filter to the wrong argument. **MUST load `dotnet-calling-endpoints`** before writing calls.

⚠ Step 4–6 (models) — response envelopes add a level, `SubscriptionResponse.Subscription` is nullable, and SDK string enums/unions are not ordinary C# enums/records. **MUST load `dotnet-models`** before mapping request or response models.

⚠ Step 5 (idempotency) — a double-click, process race, or ambiguous write result must converge through the durable enrollment uniqueness boundary and reference lookups; the effect of a failed write being re-sent must be assessed before enabling retries. **MUST load `dotnet-configuration-resilience`** before configuring write behavior.

⚠ Step 5–7 (error boundary) — operations mix typed Case-A errors with raw Case-B errors; a catch ladder that assumes one form loses validation/status behavior. **MUST load `dotnet-error-handling`** before writing the boundary.

⚠ Step 5–7 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary; **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 5–7 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 7 (tests) — faking generated controllers rather than the supported HTTP seam makes tests couple to SDK internals and miss serialization/error cases. **MUST load `dotnet-testing`** before writing integration tests.

## 4. REQUIRED READING

Load these **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` · Step 1 client/DI registration.
- `dotnet-authentication` · Step 2 Basic-auth credentials and configuration.
- `dotnet-configuration-resilience` · Steps 1–2 server override and Step 5 write retry/timeout behavior.
- `dotnet-calling-endpoints` · Steps 4–6 exact SDK invocation.
- `dotnet-models` · Steps 4–6 SDK request/response/enums mapping.
- `dotnet-error-handling` · Steps 5–7 provider and JSON failure boundary.
- `dotnet-testing` · Step 7 HTTP-seam and endpoint tests.

## 5. Assumptions & Blockers

### Assumptions

| Item | Decision | Source |
|---|---|---|
| API request shape | `POST /api/subscriptions` carries only a requested plan handle; all user/customer identity derives from JWT and all plan facts are revalidated against the configured Maxio family. | YOUR CALL — not in the map |
| Customer attributes | The JWT/application user profile supplies nonempty first name, last name, and email, which `CreateCustomer` requires. If unavailable, reject locally before the Maxio write. | `records-1-Ac-Cr.md`; YOUR CALL — not in the map |
| User/subscription persistence | A real durable DB is the production idempotency store; the requested in-memory development mode intentionally only preserves that mapping for one process lifetime. | YOUR CALL — not in the map |

### Blockers

None. The map documents customer-reference uniqueness and subscription lookup by reference; it does **not** document subscription-reference uniqueness or a provider idempotency key. Treat recovery from an ambiguous subscription create as **UNVERIFIED**: retain the serialized durable enrollment record, look up its deterministic reference before any application-level reattempt, and extract a found response best-effort; otherwise return a retriable generic outcome rather than issue another application-level create.
