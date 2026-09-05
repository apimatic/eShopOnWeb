# Maxio subscription integration plan

## 1. Scope & sequence

| Step | Work and SDK operations |
|---|---|
| 1 | Add `AsadAli.AdvancedBilling.Sdk` version `1.0.2` as the sole direct Maxio package reference and register one long-lived `MaxioAdvancedBilling.MaxioAdvancedBillingClient` over a named `HttpClient`. Bind and validate only `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`; configure Basic credentials, the US Production server's `Site`, and, when non-blank, that server's `BaseUrl` verbatim. |
| 2 | Build one integration boundary that applies a whole-call cancellation budget, translates provider failures without leaking bodies or exception text, and protects subscription POSTs from retry re-sends. |
| 3 | Discover subscription plans: `client.ProductFamilies.ListProductFamilies` and exact-match `ProductFamily.Handle` to `Maxio:ProductFamilyHandle`; then page `client.ProductFamilies.ListProductsForProductFamily`, discard archived products, and map each available product's handle/name/description/price/interval. Numeric IDs are response data only, never configuration. |
| 4 | Ensure customer: derive one stable Maxio customer reference from the authenticated application user; `client.Customers.ReadCustomerByReference`; on a definite 404, `client.Customers.CreateCustomer` with that reference and the application's required name/email fields; reconcile a concurrent create by reading the reference again. |
| 5 | Enrol idempotently: validate the requested product handle is in the configured family; derive a deterministic subscription reference from user identity plus plan handle; serialize/uniquely persist the application enrollment key; `client.Subscriptions.FindSubscription` before `CreateSubscription`, and reconcile the reference after an ambiguous POST outcome. Create with `ProductHandle`, `CustomerReference`, `Reference`, and `PaymentCollectionMethod = CollectionMethod.Invoice`; map the response to plan/price/state/next-assessment date. |
| 6 | List the caller's subscriptions: resolve its Maxio customer by the stable reference and call `client.Customers.ListCustomerSubscriptions`; unwrap each response and map only subscriptions belonging to that customer. |
| 7 | Test the integration at the `HttpMessageHandler` seam: outgoing URL/body/auth-safe shape, family/plan discovery and paging, customer-not-found/create/race reconciliation, duplicate enrolment, typed create rejection, raw lookup/list failure, malformed success/error bodies, and transport resend protection. Live verification must exercise all three capability endpoints against sandbox using a JWT. |

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

| Controller property · operation | Method signature (verbatim) | Request model + fields used | Response envelope + fields read | Error case and accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all five nullable non-defaulted parameters must be passed, normally `null`. | No body. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; unwrap nullable `ProductFamily (product_family): ProductFamily?`, then exact-match `Handle (handle): string?`; `Id (id): int?` is later rendered as the string path argument. | Case B: `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; direct `StatusCode`, `ReadAsString()` (or bytes/JSON) on `ex.Error`. | None. | `operations/ProductFamilies.md`; `models/records-3-Of-Su.md` |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.Enums.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — eight nullable non-defaulted parameters must be passed, normally `null`; use named `page`, `perPage`, and `ct`. | No body. Do not rely on server filtering semantics not stated by the map; remove an item whose `Product.ArchivedAt` is non-null after reading it. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; unwrap required `Product (product): Product`; map `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, and `ArchivedAt (archived_at): DateTimeOffset?`. | Case A: `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404], then `TryGetRawError(out RawError)` last. | Manual `page` + `perPage`; fetch pages until the returned count is less than the requested page size. | `operations/ProductFamilies.md`; `models/records-3-Of-Su.md`; `models/enums.md` |
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | No body; `reference` is the deterministic application-user reference. | `MaxioAdvancedBilling.Models.CustomerResponse`; unwrap required `Customer (customer): Customer`; read `Id (id): int?`, `Reference (reference): string?`, and identity fields only as needed for validation. | Case B: `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; direct `StatusCode`, `ReadAsString()` on `ex.Error`. Treat only a definite 404 as absence; parsing/transport failures are not absence. | None. | `operations/Customers.md`; `models/records-2-Cr-Ne.md` |
| `client.Customers.CreateCustomer` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` — `body` is nullable but non-defaulted and must be passed. | `CreateCustomerRequest`: `Customer (customer): CreateCustomer !req`; `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` (set it to the stable user reference). The map's Notes state this reference may have only one customer. Other optional fields are omitted. | `CustomerResponse` → required `Customer`; read `Id`, `Reference`. | Case A: `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], then `TryGetRawError(out RawError)` last. `CustomerErrorResponse1.Errors` is `Errors?`. | None. | `operations/Customers.md`; `models/records-1-Ac-Cr.md`; `models/records-2-Cr-Ne.md` |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` is nullable but non-defaulted and must be passed. | No body; pass the deterministic enrollment reference. | `MaxioAdvancedBilling.Models.SubscriptionResponse`; unwrap nullable `Subscription (subscription): Subscription?`; a successful but null wrapper payload is not an enrolment and must not be treated as one. Read fields below. | Case A: `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404], then `TryGetRawError(out RawError)` last. Only the mapped 404 can mean no existing subscription. | None. | `operations/Subscriptions.md`; `models/records-4-Su-We.md` |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` is nullable but non-defaulted and must be passed. | `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription !req`; set `ProductHandle (product_handle): string?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?`, and `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod? = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice`. The operation Notes permit product selection by `product_handle` and customer selection by `customer_reference`. The map lists `Invoice (invoice)` as a `CollectionMethod` value for legacy Statements Architecture; it avoids automatic payment collection for this sandbox no-card signup. Do not send `ProductId`, `CustomerId`, a price-point field, customer attributes, or card/bank payment fields. | `SubscriptionResponse` → nullable `Subscription`; read `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `Product (product): Product?` (`Handle`, `Name`), `Reference (reference): string?`. Expose `NextAssessmentAt` as the capability's next-billing-date; that response mapping is an application decision. | Case A: `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (its required `Errors` is `IReadOnlyList<string>`), then `TryGetRawError(out RawError)` last. | None. | `operations/Subscriptions.md`; `models/records-2-Cr-Ne.md`; `models/records-3-Of-Su.md`; `models/records-4-Su-We.md`; `models/enums.md` |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | No body; pass the Maxio `Customer.Id` found by stable reference, not an application ID. | `IReadOnlyList<SubscriptionResponse>`; unwrap every nullable `Subscription` and map the same `Id`, `Reference`, `Product.Handle`/`Name`, `ProductPriceInCents`, `State`, and `NextAssessmentAt` fields as the create response. | Case B: `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; direct `StatusCode`, `ReadAsString()` (or bytes/JSON) on `ex.Error`. | None. | `operations/Customers.md`; `models/records-3-Of-Su.md`; `models/records-4-Su-We.md`; `models/enums.md` |

### Enums used

| Fully-qualified type | Values needed/read |
|---|---|
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day (day)`, `Month (month)` |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |

Source: `models/enums.md`.

### Client construction, authentication, and server facts

| Fact | Contract |
|---|---|
| Package / client | Add exactly one direct project reference: `<PackageReference Include="AsadAli.AdvancedBilling.Sdk" Version="1.0.2" />`. `1.0.2` is the current published version verified by NuGet package search. The SDK map declares `netstandard2.0`, so it is compatible with a `net8.0` consumer. Its package dependencies are transitively restored: `Microsoft.Extensions.Http` `10.0.8`, `Polly` `8.6.5`, `PolySharp` `1.15.0` (private assets in the SDK), `System.Net.Http.Json` `10.0.8`, and `System.Net.ServerSentEvents` `10.0.8`; do not add duplicate direct references merely for the SDK. Construct `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`. |
| Authentication | Set `options.BasicAuth` to `new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = Maxio:ApiKey, Password = "x" }` before construction. |
| Default sandbox server derivation | Select `MaxioAdvancedBilling.Servers.ServerEnvironment.Us`; set `options.Server.Production.Us.Site` from `Maxio:Subdomain`. The documented US Production template is `https://{site}.chargify.com`. |
| Optional base override | If `Maxio:BaseUrl` is set, assign it verbatim to `options.Server.Production.Us.BaseUrl`, before construction. It overrides the Production US template; do not derive, append, or normalize it. |
| Configuration boundary | Read exactly `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`; reject missing/blank required values at startup and keep all values out of repository files. `MAXIO_ENVIRONMENT` is not an SDK configuration key in this sheet. |
| Retry surface | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` is on `options.Retry`; its documented `Default()` exists. Retry policy and timeouts must be deliberately configured in the integration boundary. |
| Idempotency consequence | `CreateSubscription` supplies nullable `Reference`, and `FindSubscription` looks up a reference. The map does **not** state that subscription references are provider-unique. The application must therefore serialize and uniquely persist each user+plan enrollment, use the deterministic reference for reconciliation, and use a re-send-blocking handler around POST; provider-level uniqueness is **UNVERIFIED**. |

Sources: `sdk-map.md`; `operations/Customers.md`; `operations/Subscriptions.md`; `models/records-2-Cr-Ne.md`; `models/records-3-Of-Su.md`; `models/records-4-Su-We.md`.

## 3. Trap notes

⚠ Step 1 (client registration) — handler ownership/lifetime and the generated DI extension's use of the unnamed client can accidentally share timeout or handler policy with unrelated callers. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (authentication) — the SDK's credential property must be set before construction, and credential rotation/configuration binding must not put secrets in code. **MUST load `dotnet-authentication`** before setting credentials.

⚠ Step 3 (catalog discovery) — generated list signatures have nullable parameters with no C# defaults, plus manual pagination; positional calls can bind the wrong request. **MUST load `dotnet-calling-endpoints`** before calls.

⚠ Step 3–6 (model construction and mapping) — response envelopes, nullable resources, string-enums, and date/money fields can be read or initialized with the wrong shape. **MUST load `dotnet-models`** before payload/mapping code.

⚠ Step 2 (write safety) — transport failures can re-send a POST even when status-based retries exclude POST, so a normal retry setting cannot prove a single upstream enrollment. **MUST load `dotnet-configuration-resilience`** before registering the client and the write guard.

⚠ Step 2 (timeout boundary) — SDK retry/timeout options do not establish a single caller-visible timeout budget. **MUST load `dotnet-configuration-resilience`** before setting resilience and cancellation policy.

⚠ Step 2 (error boundary) — Case A typed accessors and Case B raw errors require different catch ladders; losing a provider 4xx turns a deterministic rejection into a retryable server failure. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 2 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary; a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 7 (tests) — mocking the generated client instead of its HTTP seam skips the generated serialization/error/retry behavior that must be verified. **MUST load `dotnet-testing`** before tests.

## 4. REQUIRED READING

Load these before implementation starts; this sheet deliberately does not carry their contents.

| Skill | Governing step |
|---|---|
| `dotnet-client-initialization` | Step 1: named `HttpClient`, client lifetime, DI registration |
| `dotnet-authentication` | Step 1: Basic credentials and configuration-safe loading |
| `dotnet-calling-endpoints` | Steps 3–6: generated calls, named arguments, envelopes |
| `dotnet-models` | Steps 3–6: records, nullability, enums and response mapping |
| `dotnet-configuration-resilience` | Steps 1–2: server override, retries, timeout budget, POST resend guard |
| `dotnet-error-handling` | Step 2: typed/raw provider errors, transport and JSON failure boundary |
| `dotnet-testing` | Step 7: `HttpMessageHandler` seam and behavioral tests |

## 5. Assumptions & Blockers

- Assumption — the supplied sandbox is US-hosted: the required configuration keys contain no hosting-region selector, while the SDK documents `ServerEnvironment.Us` as its default. If a target is not reachable through the US template, supply its complete host through `Maxio:BaseUrl`.
- Assumption — a stable authenticated application user identifier and the required first name, last name, and email are available to construct the unique customer reference and required `CreateCustomer` body. Their application source is **YOUR CALL — not in the map**.
- Assumption — the application persists the user+plan enrollment identity/lease in its own store. The user requirement to survive a double-click cannot be established from an SDK nullable `Reference` field alone; provider enforcement of duplicate subscription references is **UNVERIFIED**.
- Blockers — none.
