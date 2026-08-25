# Maxio subscription billing plan

## 1. Scope & sequence

| Step | Application work | Maxio operations | Source |
|---|---|---|---|
| 1. Package and configuration | Add NuGet `AsadAli.AdvancedBilling.Sdk` at the map version (`1.0.2`). Bind one validated options object from exactly `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`. Load secret values into .NET user-secrets from the process environment; never persist values in the repository. An absent/blank `BaseUrl` selects the US Production template plus `Subdomain`; a present value is assigned verbatim to the Production US `BaseUrl`. | Client construction only | `sdk-map.md` |
| 2. Client/auth/resilience registration | Register a reusable `HttpClient` pipeline and construct `MaxioAdvancedBilling.MaxioAdvancedBillingClient`. Set Basic auth, Production server site/base override, bounded timeout/retry behavior, and cancellation propagation. The sandbox is the configured site; the SDK has US/EU hosting environments, not a separate `Sandbox` enum. | Client construction only | `sdk-map.md` |
| 3. Plans query | Page through `ListProductsForProductFamily` using `productFamilyId = "handle:" + configured ProductFamilyHandle`, excluding archived products. Project each required `ProductResponse.Product` into the public plan DTO; never persist or configure a numeric family/product ID. | `ProductFamilies.ListProductsForProductFamily` | `operations/ProductFamilies.md`; handle selector form is documented in `operations/ProductFamilies.md` under `ReadProductFamily` |
| 4. Resolve caller/customer | Obtain an immutable eShop user identifier and customer name/email from the authenticated principal/application identity layer. Use the immutable identifier as Maxio `reference`. `ReadCustomerByReference`; on 404, `CreateCustomer` with required name/email/reference. If create returns 422 (including a concurrent creator), immediately repeat the lookup: an existing exact reference is success; otherwise translate the validation failure. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` | `operations/Customers.md` |
| 5. Validate subscribe target | Resolve the submitted stable product handle with `ReadProductByHandle`, then require `Product.ProductFamily.Handle` to equal configured `ProductFamilyHandle` and `ArchivedAt` to be null. This prevents subscribing to an arbitrary product or trusting a client-supplied numeric ID/price. | `Products.ReadProductByHandle` | `operations/Products.md`; `records-3-Of-Su.md` |
| 6. Idempotent enrollment | Derive a deterministic application idempotency key from immutable user identity plus normalized product handle. Use it as `CreateSubscription.Reference`. First call `FindSubscription`; a found subscription is the idempotent result. On its typed 404, create with `ProductHandle`, `CustomerReference`, `Reference`, and `PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance`. The explicit remittance collection path prevents an immediate-balance signup from requiring an on-file payment method on a Relationship Invoicing site. The SDK exposes no idempotency-header parameter and the map does not say subscription references are unique, so the application must reserve/serialize the key before the create and persist/reconcile the result; after any ambiguous write failure or 422, call `FindSubscription` before deciding to retry/fail. | `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `enums.md` |
| 7. Confirm subscribe response | Treat `SubscriptionResponse.Subscription == null` as an upstream contract failure. Return Maxio subscription ID/reference, product name/handle, actual `ProductPriceInCents`, currency, state, and `NextAssessmentAt` as next billing date. Maxio remains the system of record; any local row is an idempotency/index record, not billing state. | Result of `FindSubscription` or `CreateSubscription` | `records-3-Of-Su.md`; `records-4-Su-We.md` |
| 8. My subscriptions | Resolve the Maxio customer by authenticated user's reference. A customer-lookup 404 returns an empty list. Otherwise use its Maxio customer ID only as the immediate path parameter to `ListCustomerSubscriptions` and project every response using the same confirmation mapper. This operation is not paginated. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` | `operations/Customers.md` |
| 9. HTTP surface | Add JWT-authorized endpoints following PublicApi conventions: `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`. Accept only a product handle on POST; identity/customer reference is never client supplied. Map validation/not-found/conflict/upstream failures without leaking provider bodies or secrets. | Calls above through an application-owned billing gateway | `YOUR CALL — not in the map` |
| 10. Verification/tests | Unit-test the application gateway through an owned interface and SDK wire behavior through the SDK constructor's `HttpClient` seam. Cover pagination, response envelopes, identity isolation, customer create race, duplicate subscription double-click, ambiguous transport reconciliation, typed/raw errors, null 2xx envelopes, and cancellation. Add authenticated endpoint tests; use sandbox only for the final smoke flow. | All operations above | `sdk-map.md`; `YOUR CALL — not in the map` |

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

### Operations

| Purpose / controller property | Exact method signature and return | Request model / fields used | Response envelope / fields read | Error contract | Pagination | Source |
|---|---|---|---|---|---|---|
| Family plans · `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` → `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` | No body. Pass the eight required-but-nullable parameters explicitly: `dateField:null`, `filter:null`, all four dates `null`, `includeArchived:false`, `include:null`; pass named `page`, `perPage`, `ct`. `productFamilyId` is `handle:{configured handle}`. | Each `MaxioAdvancedBilling.Models.ProductResponse` has required `Product (product): MaxioAdvancedBilling.Models.Product`; read `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, `RequireCreditCard (require_credit_card): bool?`, and nested `ProductFamily (product_family).Handle (handle): string?`. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` for 404; inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. | Manual `page` + `perPage`, defaults 1/20. Continue until a page has fewer than `perPage`; cancellation applies to every page. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| Product validation · `client.Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` → `MaxioAdvancedBilling.Models.ProductResponse` | No body; `apiHandle` is the caller-selected stable handle. | Required `Product (product): MaxioAdvancedBilling.Models.Product`; read the same handle/name/price/archive/family fields above. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; inspect `StatusCode`, with `ReadAsString()` only for sanitized logging/diagnostics. | None | `operations/Products.md`; `records-3-Of-Su.md` |
| Customer lookup · `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` → `MaxioAdvancedBilling.Models.CustomerResponse` | No body. `reference` is the app's immutable authenticated-user identifier, never request input. | Required `Customer (customer): MaxioAdvancedBilling.Models.Customer`; read `Id (id): int?`, `Reference (reference): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`. Reject a successful envelope whose required inner object or needed ID/reference is unusable. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; classify 404 via `StatusCode`; `ReadAsString()`/`ReadAsJson<T>()` are available but provider content must not leak. | None | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| Customer create · `client.Customers` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` → `MaxioAdvancedBilling.Models.CustomerResponse`; nullable `body` has no default and must be passed | `MaxioAdvancedBilling.Models.CreateCustomerRequest`: required `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer`. Inner required fields: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`; also set optional `Reference (reference): string?`. Notes require reference uniqueness and define it as the app's identifier. Address/country/state/locale and other optional fields are deliberately omitted because this flow does not source them. | Same required customer envelope/fields as lookup. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` at 422; inherited raw fallback. The mapped 422 payload has `Errors (errors): MaxioAdvancedBilling.Models.Errors?`, whose generated members are only `PerPage` and `PricePoint`; it cannot reliably expose a reference collision. On every 422, re-lookup the exact reference first; if absent, extract best-effort and fall back to a generic validation message. **UNVERIFIED:** live 422 bodies may contain unmodeled fields that deserialization drops. | None | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| Subscription idempotency lookup · `client.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)` → `MaxioAdvancedBilling.Models.SubscriptionResponse`; nullable `reference` has no default and must be passed | No body; pass the non-null deterministic subscription reference. | `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`; fields are listed in the common subscription projection row below. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` at 404; inherited raw fallback. | None | `operations/Subscriptions.md`; `records-4-Su-We.md` |
| Subscription create · `client.Subscriptions` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` → `MaxioAdvancedBilling.Models.SubscriptionResponse`; nullable `body` has no default and must be passed | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`: required `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription`. Set optional `ProductHandle (product_handle): string?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?`, and `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod? = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance` (`remittance`). Notes explicitly allow product handle and customer reference; the enum documents `remittance` as valid for current Relationship Invoicing. Deliberately omit `product_id`, price-point selectors (use product default), `customer_id`, `customer_attributes`, payment-profile/card/bank fields, and payment-profile ID. | Nullable subscription envelope; fields are listed below. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` at 422, whose required `Errors (errors)` is `IReadOnlyList<string>`; inherited raw fallback. No signature/model field exposes an idempotency header. | None | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-4-Su-We.md`; `enums.md` |
| Customer subscriptions · `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` → `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` | No body. `customerId` must come from the just-resolved customer envelope, not configuration/client input. | Each nullable subscription envelope uses the common projection below; fail/sanitize individual malformed entries according to the chosen endpoint policy. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; use `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, or `ReadAsBytes()` defensively. | None | `operations/Customers.md`; `records-4-Su-We.md` |

### Common response projection

| SDK record | Members read | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.SubscriptionResponse` | `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?` | `records-4-Su-We.md` |
| `MaxioAdvancedBilling.Models.Subscription` | `Id (id): int?`; `Reference (reference): string?`; `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`; `ProductPriceInCents (product_price_in_cents): long?`; `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (public next-billing date); `Currency (currency): string?`; `Product (product): MaxioAdvancedBilling.Models.Product?`; `Customer (customer): MaxioAdvancedBilling.Models.Customer?` | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.Product` inside subscription | `Name (name): string?`; `Handle (handle): string?`; `PriceInCents (price_in_cents): long?`; `Interval (interval): int?`; `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`; `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?` | `records-3-Of-Su.md` |

### Enums actually read

| Fully-qualified type | Generated static members and wire values | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day` (`day`), `Month` (`month`) | `enums.md` |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending` (`pending`), `FailedToCreate` (`failed_to_create`), `Trialing` (`trialing`), `Assessing` (`assessing`), `Active` (`active`), `SoftFailure` (`soft_failure`), `PastDue` (`past_due`), `Suspended` (`suspended`), `Canceled` (`canceled`), `Expired` (`expired`), `Paused` (`paused`), `Unpaid` (`unpaid`), `TrialEnded` (`trial_ended`), `OnHold` (`on_hold`), `AwaitingSignup` (`awaiting_signup`) | `enums.md` |

### Client construction, auth, and server nodes

| Fact | Exact SDK contract | Source |
|---|---|---|
| Package/client | NuGet `AsadAli.AdvancedBilling.Sdk` (map source tag `v1.0.2`); root type `MaxioAdvancedBilling.MaxioAdvancedBillingClient`; only constructor `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` properties: `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`, `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`, `Server: MaxioAdvancedBilling.ServerOptions`, `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` |
| Authentication | `BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = configured ApiKey, Password = "x" }`; Basic auth is the sole SDK auth scheme | `sdk-map.md` |
| Default sandbox-site host | `Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us`; set `options.Server.Production.Us.Site = configured Subdomain`; Production US template is `https://{site}.chargify.com` | `sdk-map.md` |
| Verbatim override | If and only if configured `Maxio:BaseUrl` is nonblank, assign its exact value to `options.Server.Production.Us.BaseUrl`; do not concatenate the subdomain, append paths, normalize, or infer it from another setting | `sdk-map.md`; verbatim requirement is task input |
| Retry members | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` has required `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`; construct a complete value or start from `RetryOptions.Default()` | `sdk-map.md` |
| Error core | Throw-only SDK. Typed operations throw `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` and expose `.Error`; typed errors derive from `MaxioAdvancedBilling.Core.ErrorResponse.ApiError` with `TryGetRawError`. Case B uses `MaxioAdvancedBilling.Core.ErrorResponse.RawError` with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | `sdk-map.md` |

### Application-owned contracts

| Concern | Required consequence | Source |
|---|---|---|
| Caller identity | Resolve immutable user ID plus create-required first name, last name, and email from the JWT/application identity path. Reject before Maxio if required identity data cannot be produced. Never accept customer reference from the request. | `YOUR CALL — not in the map`; required customer fields are in `records-1-Ac-Cr.md` |
| Subscription request DTO | Accept one nonblank stable product handle. Normalize only for validation/idempotency; send the catalog's actual handle returned by Maxio. | `YOUR CALL — not in the map` |
| Idempotency reservation | The SDK exposes lookup/create calls but no idempotency-header field, and its Notes do not promise subscription-reference uniqueness. A deterministic reference alone does not close concurrent double-clicks: reserve/serialize `(user, product)` in application persistence and reconcile every uncertain create through `FindSubscription`. | `YOUR CALL — not in the map`; SDK absence established by `operations/Subscriptions.md` and `records-2-Cr-Ne.md` |
| Public price shape | Preserve integer minor units (`priceInCents`) to avoid decimal ambiguity. Plans expose default product price; subscriptions expose actual `ProductPriceInCents` and `Currency`. | `YOUR CALL — not in the map`; fields from `records-3-Of-Su.md` |

## 3. Trap notes

⚠ Step 2 (client registration) — client/`HttpClient` ownership and dependency-injection lifetime determine whether connections are reused safely. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 2 (authentication) — credential timing, rotation, and per-environment configuration can make otherwise correct calls fail with 401/403. **MUST load `dotnet-authentication`** before setting credentials.

⚠ Steps 3–8 (calls) — optional parameters without C# defaults, literal named argument `ct`, controller ownership, and response envelopes can silently mis-bind or read the wrong level. **MUST load `dotnet-calling-endpoints`** before the first operation call.

⚠ Steps 3–8 (models) — required/init-only members, nullability, string-enum extraction, and envelope nesting affect both construction and projection. **MUST load `dotnet-models`** before constructing or mapping SDK models.

⚠ Steps 2 and 6 (resilience) — retry eligibility for writes, what timeout bounds, base-server overrides, and manual pagination determine whether a failed enrollment can be re-sent or a plan list truncated. **MUST load `dotnet-configuration-resilience`** before configuring or calling the client.

⚠ Steps 4–8 (errors) — typed and raw operations require different catch shapes and accessors; leaking raw provider content can disclose operational detail. **MUST load `dotnet-error-handling`** before writing the boundary.

⚠ Steps 6 and 10 (tests) — the correct fake seam and behavior-level assertions determine whether tests survive SDK regeneration and cover uncertain writes. **MUST load `dotnet-testing`** before writing integration tests.

⚠ A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.

⚠ A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `System.Text.Json.JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 4. REQUIRED READING

Load every item below **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Step governed |
|---|---|
| `dotnet-client-initialization` | Client construction, `HttpClient` ownership, DI registration |
| `dotnet-authentication` | Basic credentials, configuration, rotation, 401/403 diagnosis |
| `dotnet-calling-endpoints` | Controller selection, exact arguments, async/cancellation, envelopes |
| `dotnet-models` | Request initializers, required/null members, enums, response mapping |
| `dotnet-error-handling` | Typed/raw exception boundary and both `JsonException` directions |
| `dotnet-configuration-resilience` | Retry/timeout policy, Production server selection/base override, pagination |
| `dotnet-testing` | `HttpClient` test seam and integration behavior coverage |

## 5. Assumptions & Blockers

### Assumptions

- The application identity layer can produce an immutable per-user identifier plus the first name, last name, and email required by `CreateCustomer`; the exact claims/profile lookup and reference encoding are application-owned (`YOUR CALL — not in the map`).
- The seeded target products do not require payment information, as stated in the task; therefore the create payload intentionally omits payment-profile/card/bank attributes (`YOUR CALL — task input, not in the map`).
- `handle:{configured handle}` is the stable product-family selector: the family operation accepts a string selector, and the same controller's provider Notes document `handle:my-family` as the handle form (`operations/ProductFamilies.md`).
- Subscription reference uniqueness is not assumed. The application supplies the concurrency/idempotency reservation because the SDK map exposes no idempotency header and does not promise reference uniqueness (`YOUR CALL — not in the map`).

### Blockers

- None.
