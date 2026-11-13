# Maxio Advanced Billing plan: eShopOnWeb subscriptions (PublicApi)

Skill: `dotnet-integrate-maxio-advanced-billing`. SDK source: this plugin's `sdk/dotnet/`. Every source path below is relative to that root, e.g. `map/operations/Customers.md` or `src/MaxioAdvancedBilling/Models/Product.cs`. Business rules are cited by id, from the plugin's `business-rules/`.

Repo facts I read (used read-only):
- **Endpoint exemplar:** `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs`. It shows the `IEndpoint<IResult, TRequest, TService>` shape, `AddRoute` with `app.MapPost("api/...")`, `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`, `.Produces<>()` and `.WithTags()`.
- **Duplicate handling:** `DuplicateException` is thrown in that endpoint and mapped in `src/PublicApi/Middleware/ExceptionMiddleware.cs`.
- **JWT identity:** the only identity claim the token carries is `ClaimTypes.Name` = the user name (`src/Infrastructure/Identity/IdentityTokenClaimService.cs:30`).
- **Records store:** EF Core `CatalogContext` (`src/Infrastructure/Data/CatalogContext.cs`), registered in `src/Infrastructure/Dependencies.cs`. With `UseOnlyInMemoryDatabase=true` it runs on the in-memory provider. Entity keys are set in `src/Infrastructure/Data/Config/*Configuration.cs` with `HasKey`.
- **User secrets:** `src/PublicApi/PublicApi.csproj` has a `UserSecretsId`.

## 1. Scope & sequence

| # | Step | Operations used |
| --- | --- | --- |
| 1 | Add a `ProjectReference` from the project that will hold the Maxio adapter (Infrastructure, or PublicApi) to `sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj`. | — |
| 2 | Bind the `Maxio:` section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional). Fail-fast validation at startup (§5 row 1). Register the client with `AddMaxioAdvancedBillingClient`, setting `BasicAuth` and the Production server node from config (§2 client facts). | — |
| 3 | **Plan catalog.** List the products of the family `handle:{Maxio:ProductFamilyHandle}`, walking pages up to a cap. The result carries a completeness flag. `GET /api/subscription-plans` returns handle, name, price, interval and interval unit. | `ProductFamilies.ListProductsForProductFamily` |
| 4 | **Find-or-create the customer.** Write a claim row in `CatalogContext` keyed by the customer reference. Then look the customer up by reference. Only on not-found, create it with that `reference`. Record the Maxio customer id on the claim row. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 5 | **Subscribe** (`POST /api/subscriptions`). Steps in order: check the cross-operation invariant (step 3 listing); resolve the site's collection method (`ReadSite` once per process, cached: `RelationshipInvoicingEnabled == true` → `CollectionMethod.Remittance`, `== false` → `CollectionMethod.Invoice`, `null` → 503, no write); run step 4; write the subscription claim row (PK = subscription reference); call `CreateSubscription` with `ProductHandle`, `CustomerId`, `Reference` and `PaymentCollectionMethod`; record the result. On an unknown outcome, settle it with `FindSubscription` by reference (fallback: `ListCustomerSubscriptions`). Return plan, price, state and next billing date. | `Sites.ReadSite`, `Subscriptions.CreateSubscription`, `Subscriptions.FindSubscription`, `Customers.ListCustomerSubscriptions` |
| 6 | **My subscriptions** (`GET /api/my-subscriptions`). Resolve the customer (local claim row, else `ReadCustomerByReference`). If not found, return an empty list and never create anything. Otherwise list that customer's subscriptions. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 7 | Tests: unit tests with a stubbed `HttpClient` (connection failure, 404, 422, truncated listing), plus PublicApi integration tests for the three routes. | — |

Explicitly out of scope:
- `customer_attributes` on `CreateSubscription`. Rule `maxio-c4:createSubscription.5`: it creates a duplicate customer when the customer already exists.
- Payment profiles and 3DS.
- Metered usage for `api-call`.
- Webhooks.
- `ListCustomers` with `q`. Rule `maxio-c4:readCustomerByReference.1`: use the lookup endpoint for an exact reference.

## 2. CONTRACT SHEET

> ⚠ **Signatures are generated code, verbatim.** Each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none). Build it with an object initializer whose property names are the record's own, never with flat arguments.
> ⚠ **Every SDK type is written fully-qualified with the namespace its source path implies.** Take it from the path the map gives for THAT type, never from where a neighbouring type sits.

All operations are on server group `Production` and are throw-only (no `…Result` variants). The auth bullet on every one of them is `options.BasicAuth` (`sdk-map.md` defaults table; each op page).

### Per-operation rows

**`client.ProductFamilies.ListProductsForProductFamily`**
- **Signature:** `ListProductsForProductFamily(MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>>`
- **Request record members:**
  - `ProductFamilyId: string, required`. Its doc: "Either the product family's id or its handle prefixed with `handle:`". Pass `"handle:" + Maxio:ProductFamilyHandle`.
  - `Page: int`, default `1`, `[Minimum(1)]`.
  - `PerPage: int`, default `20`, `[Maximum(200)]`.
  - `IncludeArchived: bool?`. Leave unset, so archived products are excluded (rule `maxio-c4:listComponents.6`).
  - Not used: `DateField`, `Filter`, `StartDate`, `EndDate`, `StartDatetime`, `EndDatetime`, `Include`.
- **Body:** none.
- **Response:**
  - Envelope: `ProductResponse.Product` (`product`, **required**) → `MaxioAdvancedBilling.Models.Product`.
  - Fields read, all nullable: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `RequireCreditCard (require_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?`.
- **Error:**
  - Case A: `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`.
  - Accessors: `TryGetString(out string?)` [404], `TryGetRawError(out RawError?)` [fallback].
  - Rule `maxio-c4:listProductsForProductFamily.1`: 404 means the family handle is not a valid family. Treat it as a configuration error.
- **Pagination:**
  - SDK: none. The map block has no Pagination bullet, so the call returns one page.
  - Server side: page/per_page. A page past the end comes back empty, so stop on an empty page or one shorter than `PerPage` (rules `maxio-c4:listComponents.8`, `.9`, `listInvoices.10`).
- **Source:** `map/operations/ProductFamilies.md`, `src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`, `src/MaxioAdvancedBilling/Models/ProductResponse.cs`, `src/MaxioAdvancedBilling/Models/Product.cs`.

**`client.Customers.ReadCustomerByReference`**
- **Signature:** `ReadCustomerByReference(MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<MaxioAdvancedBilling.Models.CustomerResponse>`
- **Request record:** `Reference: string, required`. Wire: query `reference`. Route `GET /customers/lookup.json`.
- **Body:** none.
- **Response:**
  - Envelope: `CustomerResponse.Customer` (`customer`, **required**) → `MaxioAdvancedBilling.Models.Customer`.
  - Fields read: `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`.
- **Error:** Case B, `ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`.
- **Pagination:** none.
- **Source:** `map/operations/Customers.md`, `src/MaxioAdvancedBilling/Api/Customers.cs` (remarks: "returns a customer by their unique reference ID… a single match"), `src/MaxioAdvancedBilling/Models/Customer.cs`.
- **Not-found status: `UNVERIFIED`.** The map gives no status for not-found. Directive: treat **only** `ex.Error.StatusCode == HttpStatusCode.NotFound` as "absent". Treat every other `ApiException<RawError>` as a failure. Never treat a failure as "absent" and go on to create.

**`client.Customers.CreateCustomer`**
- **Signature:** `CreateCustomer(MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<MaxioAdvancedBilling.Models.CustomerResponse>`
- **Request record:** `Body: MaxioAdvancedBilling.Models.CreateCustomerRequest?`. No member is marked required; set `Body` anyway.
- **Body model:** `CreateCustomerRequest.Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer, required`. Fields of `CreateCustomer`:
  - `FirstName (first_name): string, required`
  - `LastName (last_name): string, required`
  - `Email (email): string, required`
  - `Reference (reference): string?`. **Carry it.** It is the idempotent-lookup key. Remarks: "you can only create one customer for a given reference value… must be unique"; rule `maxio-c4:createCustomer.1`.
  - `Organization (organization): string?`. Omitted: Maxio fills in first and last name (rule `maxio-c4:createCustomer.12`).
  - Left out: `cc_emails`, address fields, `phone`, `locale`, VAT and entity-identifier fields (rules `createCustomer.2`–`.9` apply only if sent), `tax_exempt`, `surcharging`, `parent_id`, `salesforce_id`, `branding_theme_id`.
- **Response:** `CustomerResponse.Customer` → read `Id (id): int?` and `Reference (reference): string?`.
- **Error:**
  - Case A: `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`.
  - Accessors: `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1?)` [422], `TryGetRawError(out RawError?)` [fallback].
  - Payload: `CustomerErrorResponse1.Errors (errors): MaxioAdvancedBilling.Models.AnyOf.Errors1?`. Its accessors are `TryGetCustomerError(out MaxioAdvancedBilling.Models.CustomerError?)`, `TryGetListOfString(out IReadOnlyList<string>?)` and `TryGetUnknown(out JsonElement)`.
- **Pagination:** none.
- **Source:** `map/operations/Customers.md`, `src/MaxioAdvancedBilling/Requests/Customers/CreateCustomerOperationRequest.cs`, `src/MaxioAdvancedBilling/Models/CreateCustomerRequest.cs`, `src/MaxioAdvancedBilling/Models/CreateCustomer.cs`, `src/MaxioAdvancedBilling/Errors/CreateCustomerError.cs`, `src/MaxioAdvancedBilling/Models/AnyOf/Errors1.cs`.
- **`UNVERIFIED`: the outcome of a duplicate reference.** Rule `createCustomer.1` says "outcome not stated". Directive: on any 422, re-read with `ReadCustomerByReference` before reporting failure. If it finds the customer, adopt it.
- **`UNVERIFIED`: blank names.** Whether blank `first_name`/`last_name` are accepted is not stated. Directive: always send non-blank values.

**`client.Subscriptions.CreateSubscription`**
- **Signature:** `CreateSubscription(MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<MaxioAdvancedBilling.Models.SubscriptionResponse>`
- **Request record:** `Body: MaxioAdvancedBilling.Models.CreateSubscriptionRequest?`. No member is marked required; set `Body` anyway.
- **Body model:** `CreateSubscriptionRequest.Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription, required`. `CreateSubscription` marks nothing required, so carry these fields, which the remarks and rules tie to acceptance:
  - `ProductHandle (product_handle): string?`. The doc says "Required, unless a `product_id` is given" (rule `createSubscription.1`).
  - `CustomerId (customer_id): int?`. "Required, unless a `customer_reference` or a set of `customer_attributes` is given" (rule `createSubscription.4`). `CustomerReference (customer_reference): string?` is the documented alternative.
  - `Reference (reference): string?`. "The reference value (provided by your app) for the subscription itself." Carry it: it is the reconciliation key for `FindSubscription`.
  - `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?` (declared in `src/MaxioAdvancedBilling/Models/CreateSubscription.cs`; enum in `src/MaxioAdvancedBilling/Models/Enums/CollectionMethod.cs`). **Carry it** (revision R1). Doc: "For legacy Statements Architecture valid options are - `invoice`, `automatic`. For current Relationship Invoicing Architecture valid options are - `remittance`, `automatic`, `prepaid`." Send `CollectionMethod.Remittance` when `Sites.ReadSite` → `Site.RelationshipInvoicingEnabled == true`, `CollectionMethod.Invoice` when `== false`. Never hard-code, never a config key. Rules `maxio-c4:createSubscription.41`, `signupWithSubscriptionGroup.18`, `every-request.42`: a value outside the site's architecture is refused. Live evidence (cp-exp-2): omitting it (site default `automatic`) with no payment method → 422 `ErrorListResponse1.Errors = ["No payment method was on file for the $299.00 balance"]`.
  - Left out:
    - `product_id`, `product_price_point_*`, `custom_price`, `coupon_code(s)`, `net_terms` (site default; rule `createSubscription.48` bounds it 0–180 if ever sent).
    - `next_billing_at`, `initial_billing_at`, `defer_signup` (default `false`). Rules `.10` and `.12`: omitting `next_billing_at` assesses charges at creation; a future `initial_billing_at` gives `awaiting_signup`.
    - `payment_profile_id`, `payment_profile_attributes`, `credit_card_attributes`, `bank_account_attributes`. The plans need no payment method.
    - `customer_attributes`. Forbidden (rule `.5`).
    - `components`, `calendar_billing`, `metafields`, `group`, `ref`, `currency`, `expires_at`.
- **Response:**
  - Envelope: `SubscriptionResponse.Subscription` (`subscription`) → `MaxioAdvancedBilling.Models.Subscription?`. **It is nullable.**
  - Fields read: `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `Reference (reference): string?`, `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?` (`src/MaxioAdvancedBilling/Models/Subscription.cs`), `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `Product (product): MaxioAdvancedBilling.Models.Product?` (→ `Handle`, `Name`, `Interval`, `IntervalUnit`), `Customer (customer): MaxioAdvancedBilling.Models.Customer?`.
- **Error:**
  - Case A: `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`.
  - Accessors: `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1?)` [422], `TryGetRawError(out RawError?)` [fallback].
  - Payload: `ErrorListResponse1.Errors (errors): IReadOnlyList<string>, required`.
- **Pagination:** none.
- **Source:** `map/operations/Subscriptions.md`, `src/MaxioAdvancedBilling/Api/Subscriptions.cs` (remarks), `src/MaxioAdvancedBilling/Models/CreateSubscription.cs`, `src/MaxioAdvancedBilling/Models/SubscriptionResponse.cs`, `src/MaxioAdvancedBilling/Models/Subscription.cs`, `src/MaxioAdvancedBilling/Errors/CreateSubscriptionError.cs`, `src/MaxioAdvancedBilling/Models/ErrorListResponse1.cs`.
- **"Next billing date":**
  - `current_period_ends_at` is documented as "when the next regularly scheduled attempted charge will occur".
  - `next_assessment_at` is documented as "when capture of payment will be tried or retried". It diverges after a failed renewal.
  - Which field to show the user: **decided**: `nextBillingAt` = `current_period_ends_at` (the scheduled renewal); `next_assessment_at` is also returned as `nextAssessmentAt`. Implemented in `MaxioSubscriptionBillingService.ToDetails` → `SubscriptionDto.From`.
  - Remittance/invoice: rule `maxio-c4:background.60` says that when a remittance subscription renews, the customer is not charged. An invoice is generated and payment is recorded manually. No rule states the **state** after a remittance or invoice create with no payment method: `UNVERIFIED`. Directive: show whatever `State` comes back, and don't assume `active`. `current_period_ends_at` stays the renewal date. Under remittance, `next_assessment_at` is the next invoice-issue time, not a charge.
- **`UNVERIFIED`: rule `maxio-c4:createSubscription.7`** ("product's options require payment information (a paid product …) and the request carries no payment method → outcome not stated"). Whether it still refuses a paid product under `remittance`/`invoice` is not stated. Directive: on a 422 whose `Errors` still names a missing payment method after sending the invoice-style method, report it as a site/product configuration error (log at Error, release the claim, return 422/503). Don't retry with another collection method.
- **Collection-method 422 fallback: not used.** `ReadSite` gives the architecture directly, so the plan does not try remittance first and then fall back to invoice. If a 422 names `payment_collection_method` anyway, invalidate the cached site read, log at Error and fail without retrying in the same request.

**`client.Sites.ReadSite`** (new, revision R1)
- **Signature:** `ReadSite(RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<MaxioAdvancedBilling.Models.SiteResponse>`. No request record; the operation takes no inputs. Route `GET /site.json`. Auth `options.BasicAuth`.
- **Body:** none.
- **Response:**
  - Envelope: `SiteResponse.Site` (`site`, **required**) → `MaxioAdvancedBilling.Models.Site`.
  - Fields read: `RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?`, `Subdomain (subdomain): string?` (log only), `Test (test): bool?` (log only).
  - Not used: `DefaultPaymentCollectionMethod (default_payment_collection_method): string?`. It is the site default (`automatic` on cp-exp-2 per the live 422), not the architecture.
- **Error:** Case B, `ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`. Any failure → subscribe returns 503 with no Maxio write.
- **Pagination:** none.
- **Caching:** read once per process on first subscribe, lazily (not at startup, so the host can start while Maxio is down), then cached in a singleton. Concurrent first calls share one `Lazy`/`Task`. A failed read is not cached.
- **`UNVERIFIED`: a null flag.** Rule `maxio-c4:readSite.1` lists the RI-only extra properties (`customer_hierarchy_enabled`, `whopays_*`, `multi_frequency_enabled`); `relationship_invoicing_enabled` is not one of them. Whether it can be absent is not stated. Directive: `null` → 503 and log at Error. Don't guess an architecture.
- **Source:** `map/operations/Sites.md`, `src/MaxioAdvancedBilling/Api/Sites.cs` (l.114), `src/MaxioAdvancedBilling/Models/SiteResponse.cs`, `src/MaxioAdvancedBilling/Models/Site.cs` (l.34–35), `business-rules/operations/sites.md`.

**`client.Subscriptions.FindSubscription`**
- **Signature:** `FindSubscription(MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<MaxioAdvancedBilling.Models.SubscriptionResponse>`
- **Request record:** `Reference: string?`. Not required in C#, but the lookup is meaningless without it, so always set it. Wire: query `reference`. Route `GET /subscriptions/lookup.json`.
- **Body:** none.
- **Response:** `SubscriptionResponse.Subscription?`, as for `CreateSubscription`.
- **Error:**
  - Case A: `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`.
  - Accessors: `TryGetNoContent(out RawError?)` [404] means not found. `TryGetRawError(out RawError?)` [fallback].
- **Pagination:** none.
- **Source:** `map/operations/Subscriptions.md`, `src/MaxioAdvancedBilling/Requests/Subscriptions/FindSubscriptionRequest.cs`, `src/MaxioAdvancedBilling/Errors/FindSubscriptionError.cs`.
- **`UNVERIFIED`: subscription reference uniqueness.** Maxio does not state that subscription references are unique. Directive: make our references unique ourselves (they are the claim PK). Also check that the found subscription's `Customer.Id` equals our customer id before adopting it.

**`client.Customers.ListCustomerSubscriptions`**
- **Signature:** `ListCustomerSubscriptions(MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>>`
- **Request record:** `CustomerId: int, required`. Its doc: "The Chargify id of the customer". The record has no page or per_page members.
- **Body:** none.
- **Response:** each `SubscriptionResponse.Subscription?`, with the fields listed under `CreateSubscription`. Rule `maxio-c4:listCustomerSubscriptions.1`: `Product` can be null.
- **Error:** Case B, `ApiException<RawError>`.
- **Pagination:** none (SDK and record).
- **Source:** `map/operations/Customers.md`, `src/MaxioAdvancedBilling/Requests/Customers/ListCustomerSubscriptionsRequest.cs`, `src/MaxioAdvancedBilling/Api/Customers.cs` (remarks: "Lists all subscriptions that belong to a customer").
- **`UNVERIFIED`: server-side truncation.** Whether the server truncates long lists is unknown. Directive: none possible from the SDK, which exposes no page input. Recorded in §6.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The `productHandle` in `POST /api/subscriptions` must be one whose `Product.Handle` the family listing returned for `handle:{Maxio:ProductFamilyHandle}`. Non-archived only. Ordinal, case-sensitive match (rule `every-request.4`). | `Subscriptions.CreateSubscription` ← `ProductFamilies.ListProductsForProductFamily` | Before `CreateSubscription`, in the subscribe service's plan check (`MaxioSubscriptionBillingService.GetOfferedPlanAsync`, which reads the step-3 listing via `GetPlanCatalogAsync`). A handle not in the listing gets 400 and no Maxio write. A listing whose completeness flag is false (cap hit) and does not contain the handle gets 503, never accept-by-default. |
| `CreateSubscription.CustomerId` must be the id that find-or-create resolved for **this** caller's reference. | `Subscriptions.CreateSubscription` ← `Customers.ReadCustomerByReference` / `Customers.CreateCustomer` (stored on the customer claim row) | In the subscribe service: read from the caller's own claim row (keyed by the reference derived from the JWT `ClaimTypes.Name`), never from the request body. |
| `CreateSubscription.PaymentCollectionMethod` must be valid for the site's invoicing architecture: `Remittance` iff `Site.RelationshipInvoicingEnabled == true`, `Invoice` iff `== false` (rules `createSubscription.41`, `every-request.42`). | `Subscriptions.CreateSubscription` ← `Sites.ReadSite` | Before `CreateSubscription`, in the subscribe service, from the cached site read (singleton `MaxioSiteArchitecture.GetInvoiceCollectionMethodAsync`, called in `MaxioSubscriptionBillingService.SubscribeAsync` before any claim). A `null` flag or a failed read gives 503 and no claim row or Maxio write. |
| `ListCustomerSubscriptions.CustomerId` must be the caller's own customer id. | `Customers.ListCustomerSubscriptions` ← `Customers.ReadCustomerByReference` / stored claim row | In the my-subscriptions handler, resolved from the JWT identity only. No route or query parameter supplies it. |

### Enum values needed

| Enum (namespace `MaxioAdvancedBilling.Models.Enums`) | Members → wire | Source |
| --- | --- | --- |
| `SubscriptionState` (open string enum) | `Pending`→`pending`, `FailedToCreate`→`failed_to_create`, `Trialing`→`trialing`, `Assessing`→`assessing`, `Active`→`active`, `SoftFailure`→`soft_failure`, `PastDue`→`past_due`, `Suspended`→`suspended`, `Canceled`→`canceled`, `Expired`→`expired`, `Paused`→`paused`, `Unpaid`→`unpaid`, `TrialEnded`→`trial_ended`, `OnHold`→`on_hold`, `AwaitingSignup`→`awaiting_signup` | `src/MaxioAdvancedBilling/Models/Enums/SubscriptionState.cs` |
| `IntervalUnit` (open string enum) | `Day`→`day`, `Month`→`month` | `src/MaxioAdvancedBilling/Models/Enums/IntervalUnit.cs` |
| `CollectionMethod` (open string enum, `sealed record : OpenStringEnum<CollectionMethod>`) | `Automatic`→`automatic`, `Remittance`→`remittance`, `Prepaid`→`prepaid`, `Invoice`→`invoice`. Send only `Remittance` (RI) or `Invoice` (Statements). | `src/MaxioAdvancedBilling/Models/Enums/CollectionMethod.cs` (l.17–23). Not the same type as `CollectionMethod1` (the `ListSubscriptions` query enum). |

Neither is a C# enum. An undeclared wire value reaches `Match`'s `otherwise` (`sdk-map.md` Models conventions).

### Client construction, auth, server node

| Fact | Value | Source |
| --- | --- | --- |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient`. Only constructor: `(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`. | `sdk-map.md` Getting a client |
| DI | `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure)` registers a **singleton**. The `configure` delegate runs inside the singleton factory, on first resolution. The `HttpClient` comes from `IHttpClientFactory.CreateClient()`. The container's `ILoggerFactory` is assigned when none is set. | `src/MaxioAdvancedBilling/ServiceCollectionExtensions.cs` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`: `Environment`, `Retry`, `Logging`, `TimeProvider`, `Server`, `StreamReadTimeout`, `Hooks`, `BasicAuth`, `BasicAuthProvider`. | `src/MaxioAdvancedBilling/MaxioAdvancedBillingClientOptions.cs` |
| Auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`. "The `username` is a Maxio Chargify API key. The `password` is `x`." Both members are `required string`. Rule `maxio-c4:every-request.1` says the same. | `sdk-map.md` Servers & auth; `src/MaxioAdvancedBilling/Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Credential env fallback | A `BasicAuth` left `null` is read at construction from `MAXIO_ADVANCED_BILLING_BASIC_AUTH_USERNAME` / `MAXIO_ADVANCED_BILLING_BASIC_AUTH_PASSWORD`. A value set in code wins. **A credential that is set but blank is treated as configured and sent as-is.** | `README.md` "Credentials from environment variables" and line 120 |
| Environments | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default, wire `US`) and `.Eu` (`EU`). The SDK declares **no sandbox environment**: the sandbox is a separate *site* (subdomain). | `sdk-map.md` Servers & auth |
| Server groups | `Production`: Us `https://{site}.chargify.com`, Eu `https://{site}.ebilling.maxio.com`. `Ebb`: `https://events.chargify.com/{site}` (both envs). **This scope uses only `Production`.** | `sdk-map.md` Servers & auth |
| Subdomain → node | `options.Server.Production.Us.Site = <Maxio:Subdomain>`. `Site` defaults to the literal `"subdomain"`. Types: `MaxioAdvancedBilling.ServerOptions.Production` → `MaxioAdvancedBilling.Servers.ProductionOptions.Us` (`UsOptions { string BaseUrl; string Site }`). Use `.Eu` only if `Environment = Eu`. | `src/MaxioAdvancedBilling/Servers/ProductionOptions.cs`, `src/MaxioAdvancedBilling/ServerOptions.cs` |
| Verbatim BaseUrl override | When `Maxio:BaseUrl` is set: `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>`, verbatim, on the same environment that `options.Environment` selects (Us). Per the companion skill, a literal URL with no `{placeholders}` is used as-is and `Site` is then irrelevant. **How a trailing `/` on the override joins the operation path (e.g. `/customers.json`) is `UNVERIFIED`.** Directive: assert the outgoing request URI in a stub-`HttpClient` test, for an override with and without a trailing slash. | `src/MaxioAdvancedBilling/Servers/ProductionOptions.cs`; `dotnet-configuration-resilience` |
| `Maxio:ProductFamilyHandle` | Passed as `ProductFamilyId = "handle:" + value`. Rule `maxio-c4:updateComponent.1`: path handles need the `handle:` prefix. | `ListProductsForProductFamilyRequest.cs` doc |
| Exceptions (all `MaxioAdvancedBilling.Core.Exceptions`) | `SdkException` (abstract base), `ApiException` (abstract), `ApiException<TError>`, `ResponseDeserializationException`, `SdkConnectionException`, `SdkTimeoutException` (derives from `SdkConnectionException`), `AuthSchemeException`. Your own cancellation surfaces as `OperationCanceledException`. | `src/MaxioAdvancedBilling/Core/Exceptions/*.cs`; `README.md` Error Handling |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse.RawError`: `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. | `sdk-map.md` |
| Default retry policy | Retried statuses: 408, 429, 500, 502, 503, 504. Retried methods: GET, HEAD, PUT, OPTIONS (**not POST**). `MaxRetries = 3`, `Delay = 1s`, `Timeout = 100s`. | `src/MaxioAdvancedBilling/Core/Configuration/RetryOptions.cs` |
| Idempotency | `CreateCustomerOperationRequest`, `CreateSubscriptionOperationRequest` and their body models declare **no** caller-supplied key member. No `uniqueness_token`, no `Idempotency-Key` member. The `Idempotency-Key` header the SDK sends is a fresh `Guid.NewGuid()` per call (seen in `Api/Subscriptions.cs` `CreateSubscription`) and **is not a key**. | the request/model files above |

## 3. Trap notes

| Step | Hazard (what it costs) | Pointer |
| --- | --- | --- |
| 2 | When the `configure` delegate runs, who owns the `HttpClient`, and what lifetime the registered client has: getting it wrong leaks sockets or freezes a stale config. | **MUST load `maxio:dotnet-client-initialization`** |
| 2 | How `BasicAuth` interacts with the SDK's own credential env variables, and what the SDK does with a present-but-blank key: a blank `Maxio:ApiKey` can reach Maxio as a real request, and a missing one can be silently replaced from another variable. | **MUST load `maxio:dotnet-authentication`** |
| 2 | Which per-environment server options are read, and when they are captured: a `Site`/`BaseUrl` set on the wrong environment, or after construction in the wrong way, is silently ignored and traffic goes to the default host. | **MUST load `maxio:dotnet-configuration-resilience`** |
| 3–6 | The operation's request record wraps a separate body model (`Body` → envelope → payload). Flattening it, or skipping a level, sends an empty body. | **MUST load `maxio:dotnet-calling-endpoints`** |
| 3–6 | Nullable envelopes (`SubscriptionResponse.Subscription`), open string enums (`SubscriptionState`, `IntervalUnit`) and `DateTimeOffset` values in the site's time zone (rule `every-request.12`): naive reads throw or show the wrong value or time. | **MUST load `maxio:dotnet-models`** |
| 3–6 | Case A ops (`CreateCustomer`, `CreateSubscription`, `FindSubscription`, `ListProductsForProductFamily`) and Case B ops (`ReadCustomerByReference`, `ListCustomerSubscriptions`) need different catch shapes. A not-found is one of those errors, so mistaking it for a failure, or a failure for a not-found, creates a duplicate customer. | **MUST load `maxio:dotnet-error-handling`** |
| 4–5 | Which exceptions mean "Maxio may already have acted" versus "nothing was sent". Treating them alike either duplicates a subscription or reports a false failure. | **MUST load `maxio:dotnet-error-handling`** |
| 3–6 | What `RetryOptions.Timeout` actually bounds, and what bounds a whole call including retries: the shopper's wait can exceed the budget you think you set. | **MUST load `maxio:dotnet-configuration-resilience`** |
| 4–5 | Which HTTP methods and statuses the SDK resends on its own: widening the retry policy can resend a non-idempotent create. | **MUST load `maxio:dotnet-configuration-resilience`** |
| 3 | The SDK hands back one page per call. How to walk pages and detect the end is your code's job, and a silent single page drops plans. | **MUST load `maxio:dotnet-configuration-resilience`** |
| 4 | Request-body logging and the log env variable: customer name and email (PII) can land in logs with no code change. | **MUST load `maxio:dotnet-configuration-resilience`** |
| 7 | How to fail the connection, return a 404/422 body, and assert the outgoing URI through the `HttpClient` seam. Without it the unknown-outcome and BaseUrl tests can't be written. | **MUST load `maxio:dotnet-testing`** |

## 4. REQUIRED READING (load all before implementation starts)

| Skill | Governs |
| --- | --- |
| `maxio:dotnet-getting-started` | Map and source lookups for any fact not on this sheet. |
| `maxio:dotnet-client-initialization` | Step 2: DI registration, `HttpClient` ownership. |
| `maxio:dotnet-authentication` | Step 2: `BasicAuth`, env fallback, fail-fast. |
| `maxio:dotnet-calling-endpoints` | Steps 3–6: request records, `requestOptions`, `cancellationToken`. |
| `maxio:dotnet-models` | Steps 3–6: nullable envelopes, open enums, dates. |
| `maxio:dotnet-error-handling` | Steps 3–6: Case A/B catch ladders, not-found detection, unknown outcomes. |
| `maxio:dotnet-configuration-resilience` | Steps 2–6: server node and BaseUrl, timeouts, retries, paging, logging. |
| `maxio:dotnet-testing` | Step 7: stubbed `HttpClient`, failing the connection, asserting URIs. |

**Hazard (verbatim):** a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | The credential comes from `IConfiguration` (`Maxio:ApiKey`), never from the SDK's own env variables. The SDK sends a blank-but-set credential as-is (`README.md` l.120), so the host validates at startup and refuses to start when any of these is null or whitespace: `Maxio:ApiKey`; `Maxio:ProductFamilyHandle`; and `Maxio:Subdomain` **unless** `Maxio:BaseUrl` is set. Use `AddOptions<MaxioSettings>().Bind(...).Validate(...).ValidateOnStart()` or an equivalent. The password part is the constant `"x"` (map), so it can never be blank. `BasicAuth` is always assigned in code, so the SDK env fallback never fills it. |
| 2 | Secret sourcing & rotation | Dev: .NET user-secrets on `src/PublicApi` (existing `UserSecretsId`), keys `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`. Other deployments: environment config or provider under the same keys. No value is committed to `appsettings*.json`. `AddMaxioAdvancedBillingClient` builds the options once, in the singleton factory, and captures them, so **a rotated key takes effect only after a process restart**. That is accepted. If restart-free rotation is ever needed, switch to `options.BasicAuthProvider` (asked on every request) reading the current config. |
| 3 | Total timeout budget | The caller gets at most **30 s** of Maxio work per inbound request (below Maxio's 120 s cut-off, rule `every-request.31`). It is enforced by a `CancellationTokenSource(TimeSpan.FromSeconds(30))`, linked to `HttpContext.RequestAborted` and passed as `cancellationToken` to every SDK call in that request. `RetryOptions.Timeout` is not relied on as the budget (see the trap note). The exact number is tunable. |
| 4 | Write-retry ownership | `CreateCustomer` and `CreateSubscription` are POSTs. The default policy does not retry POST (`RetryOptions.cs`), so **the SDK never resends either**. The code must not add POST to `HttpMethodsToRetry`. Reads (`ListProductsForProductFamily`, `ReadCustomerByReference`, `FindSubscription`, `ListCustomerSubscriptions`, `ReadSite`) are GETs and may be retried by the SDK. The app does no retry of its own on writes: it reconciles instead (row 11). |
| 5 | Idempotency & ambiguous writes | **`CreateCustomer`:** no key member. Uniqueness comes from the customer `reference` (remarks: one customer per reference). Reconcile with `ReadCustomerByReference(reference)`. **`CreateSubscription`:** no key member. Reconcile with `FindSubscription(Reference = our subscription reference)`, falling back to `ListCustomerSubscriptions(customerId)` and matching `Reference`. The per-call `Idempotency-Key` header is a fresh GUID and is not a key. Rule `every-request.36`'s `uniqueness_token` is not a member of either request model, so it is **not used** (§6). |
| 6 | Observability | `ILogger` from DI (the SDK gets the container's factory). Levels: Information for subscribe success (eShop user name hash, Maxio customer id, subscription id, product handle, state). Warning for 4xx from Maxio (operation, status, the `ErrorListResponse1.Errors` / `CustomerErrorResponse1` messages). Error for 5xx, connection failures, timeouts and unknown outcomes (operation, our claim reference). The map's error models (`ErrorListResponse1`, `CustomerErrorResponse1`, `RawError`) declare **no provider correlation id**. The correlation id logged is our own reference (customer and subscription reference, used as claim PKs) plus the PublicApi request's correlation id (`request.CorrelationId()` pattern from the exemplar). A 429 body's "track this request with support" text (rule `every-request.28`) is logged verbatim at Warning. |
| 7 | Sensitive data | Yes: `CreateCustomer` carries `first_name`, `last_name` and `email`, and the customer reference is derived from the user name. Decisions: `LogRequestBody` off; `options.Logging.LoggerFactory` assigned explicitly (DI does this, but set it in the `configure` delegate anyway so the log env variable is never armed); no request-body echo in our own logs; log a hash of the user name, not the raw value. The scope sends JSON bodies only, so the form-body deny-list is N/A. |
| 8 | Environment selection | Server groups: `Production` and `Ebb`. **Scope touches `Production` only.** `options.Environment` stays the default `ServerEnvironment.Us`: no binding key selects it, and the task's keys are exactly ApiKey/Subdomain/ProductFamilyHandle/BaseUrl. Every deployment sets `Maxio:Subdomain` → `Server.Production.Us.Site`, or `Maxio:BaseUrl` → `Server.Production.Us.BaseUrl` verbatim. The SDK declares no sandbox environment. Test traffic is kept off live by the **site**: dev/test config points `Maxio:Subdomain` at the sandbox site (`cp-exp-2`) with a sandbox key, and automated tests use a stubbed `HttpClient` and never reach Maxio. The sandbox and live sites may differ in invoicing architecture, so the collection method is read from whichever site is configured (`Sites.ReadSite`) and is never a config key or constant. Tests stub `GET /site.json` with both `relationship_invoicing_enabled` values, and with the flag absent. |
| 9 | Duplicate prevention under concurrency | See **DUPLICATE CLAIMS**. Two writes: customer create and subscription create. |
| 10 | Partial results | `ListProductsForProductFamily` is walked page by page with a page cap. The returned plan-catalog type carries `bool IsComplete`, and the `GET /api/subscription-plans` response exposes it (e.g. `"complete": false`). `ListCustomerSubscriptions` has no paging input, so the result can't be flagged (§6). |
| 11 | Unknown outcomes | See **UNKNOWN OUTCOMES**. In the create call's own `catch`, a connection failure, timeout or 5xx is settled by a re-read by reference before returning. If the re-read also fails, the claim row is left in a `Pending` (unknown) state, the caller gets 202/503 with a "pending, retry GET /api/my-subscriptions" body, and the next `POST` or `GET` for that user re-runs the settle step on `Pending` rows. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` (find-or-create for one eShop user) | New entity `MaxioCustomerLink` in `CatalogContext`, **PK = customer reference**. The reference is derived from the JWT `ClaimTypes.Name`; **decided** format: `eshop-cus-` + first 16 bytes (hex) of SHA-256 of the trimmed, upper-cased user name, so no e-mail reaches Maxio (`MaxioReferences.ForCustomer`). Implemented as entity `BillingCustomerLink` (base `BillingClaim`: `Status` Pending/Completed, `ProviderId`, `LeaseExpiresUtc`, `Version` concurrency token, timestamps). Written with `SaveChangesAsync` **before** `ReadCustomerByReference`/`CreateCustomer`. | The store refusing an insert with a duplicate primary key on `SaveChangesAsync`. The in-memory provider enforces PKs. | A `catch` around the claim's `SaveChangesAsync`, for the exception the EF in-memory provider raises on a duplicate key. Pin the exact type with a two-context concurrency test before writing the catch. On catch: load the existing row. If `Created`, use `MaxioCustomerId`. If `Pending`, run the settle step (row 11), or reply 409 "in progress". Release (delete) the claim when Maxio refuses the create with a definite 4xx. | Claim: `BillingClaimStore.TryClaimAsync` (INSERT; duplicate caught by its `catch (… when IsPossibleDuplicateKey)`, then the existing row is read), called from `MaxioSubscriptionBillingService.EnsureCustomerAsync`. SDK call: `MaxioSubscriptionBillingService.CreateCustomerAsync` → `client.Customers.CreateCustomer` (preceded by `FindCustomerAsync` → `ReadCustomerByReference`). |
| `Subscriptions.CreateSubscription` (double-click on Subscribe) | New entity `MaxioSubscriptionClaim` in `CatalogContext`, **PK = subscription reference**, which is deterministic from (customer reference, product handle) so that a second click computes the same key. **Decided** format: `eshop-sub-` + first 16 bytes (hex) of SHA-256 of (customer reference + newline + plan handle) (`MaxioReferences.ForSubscription`). Re-subscribing to the same plan after cancellation is **not** supported by this flow: the same (user, plan) resolves to the existing subscription and `POST` answers `already_exists` with its current state. Implemented as entity `SubscriptionEnrollment` (base `BillingClaim`, plus `CustomerReference`, `PlanHandle`). Written with `SaveChangesAsync` before `CreateSubscription`; the same value is sent as `CreateSubscription.Reference`. | The store refusing an insert with a duplicate primary key on `SaveChangesAsync`. | A `catch` around the claim's `SaveChangesAsync` for the duplicate-key exception (same pinned type). On catch: load the row. If `Created`, return the existing subscription (`ReadSubscription` is not needed: `FindSubscription` by reference, or the stored projection). If `Pending`, settle or reply 409. The claim is released (deleted) when `CreateSubscription` fails with a definite `ApiException<CreateSubscriptionError>` 422. | Claim: `BillingClaimStore.TryClaimAsync`, called from `MaxioSubscriptionBillingService.EnsureSubscriptionAsync`. SDK call: `MaxioSubscriptionBillingService.CreateSubscriptionAsync` → `client.Subscriptions.CreateSubscription` (preceded by `FindSubscriptionAsync` → `FindSubscription`). Verified live: two simultaneous POSTs → one 201, one 409, one Maxio subscription. |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ProductFamilies.ListProductsForProductFamily` (`handle:{Maxio:ProductFamilyHandle}`) | `PerPage = 200` (the max, rule `listComponents.8`) and a page cap (e.g. 5 pages). The walk stops on an empty page or one shorter than `PerPage`; reaching the cap while still full means truncated. | The plan-catalog return type's `IsComplete` is false. `GET /api/subscription-plans` returns it in the response body (`complete: false`). The POST invariant check returns 503 when the handle is absent from an incomplete listing. | `MaxioSubscriptionBillingService.GetPlanCatalogAsync` sets `PlanCatalog.IsComplete` (cap: `ProductPageSize` 200 × `MaxProductPages` 5); `SubscriptionPlanListEndpoint.HandleAsync` exposes it as `complete`; `GetOfferedPlanAsync` returns 503 for an absent handle on an incomplete listing. Test: `GetPlans_FlagsTheListingIncomplete_WhenThePageCapIsReached`. |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` | `Customers.ReadCustomerByReference` | `CreateCustomer.Reference`, which is also the claim PK. | `MaxioSubscriptionBillingService.CreateCustomerAsync` `catch (Exception ex) when (IsUnknownOutcome(…))` → `SettleCustomerAfterAmbiguousCreateAsync` (found → complete claim; definite 404 → release claim + 503 retry, since Maxio allows one customer per reference; re-read fails → `MarkUnknownAsync` keeps the claim pending with a 30 s cool-down, after which `BillingClaimStore.TryClaimAsync` lets the next request take it over and re-read before writing). A 4xx rejection also re-reads (`TryFindCustomerForSettleAsync`) to adopt a duplicate-reference customer. | `MaxioSubscriptionBillingServiceTests.Subscribe_CustomerCreateConnectionDrop_AdoptsTheCustomerMaxioCreated` (FakeMaxio applies the create, then throws `HttpRequestException`). |
| `Subscriptions.CreateSubscription` | `Subscriptions.FindSubscription` (404 via `TryGetNoContent` = not created). Fallback when the lookup itself is ambiguous: `Customers.ListCustomerSubscriptions(CustomerId)`, matching `Subscription.Reference`. | `CreateSubscription.Reference`, which is also the subscription claim PK. Adopt the match only if `Subscription.Customer.Id` equals our customer id. | `MaxioSubscriptionBillingService.CreateSubscriptionAsync` `catch (Exception ex) when (IsUnknownOutcome(…))` → `SettleSubscriptionAfterAmbiguousCreateAsync` (FindSubscription, fallback ListCustomerSubscriptions; found → complete + 201; not found/unreadable → `MarkUnknownAsync` + outcome `Pending` → HTTP 202). Pending rows are settled later by `SettlePendingEnrollmentsAsync` (on `GET /api/my-subscriptions`) or by lease take-over in `BillingClaimStore.TryClaimAsync` + the look-before-write `FindSubscriptionAsync` in `EnsureSubscriptionAsync`. | `Subscribe_ConnectionDropsAfterMaxioCreatedIt_IsSettledByReference`, `Subscribe_ConnectionFailsAndOutcomeCannotBeConfirmed_StaysPendingThenRetryCreatesOnce`, `Subscribe_PendingOutcomeThatLanded_IsSettledByMySubscriptions` (all in `MaxioSubscriptionBillingServiceTests`). |

## 6. Assumptions & Blockers

**Blockers**
- **B1. Claim durability under the mandated store.** The duplicate-claim rows live in `CatalogContext`. With `UseOnlyInMemoryDatabase=true` (mandated: no SQL Server) they **do not outlive the process**. After a restart, a request can no longer be stopped by a claim from before the restart. Mitigation in the plan: Maxio-side checks run after every successful claim (`ReadCustomerByReference` before `CreateCustomer`; `FindSubscription` by the deterministic reference before `CreateSubscription`). These cover the restart case for sequential requests, but not two concurrent requests that straddle a restart. On the SQL Server configuration the same `CatalogContext` tables are durable. The user must accept this, or provide a durable store.
- **B2. No first-class idempotency key in the SDK.** Rule `maxio-c4:every-request.36` describes a top-level `uniqueness_token` for data-changing requests (409 on reuse). `CreateCustomerRequest` and `CreateSubscriptionRequest` declare no such member (`src/MaxioAdvancedBilling/Models/CreateCustomerRequest.cs`, `CreateSubscriptionRequest.cs`); only the generic `AdditionalProperties` bag exists. Per the task's "do not work around" mandate, the plan does **not** inject `uniqueness_token` through `AdditionalProperties`. Duplicate safety rests on the local PK claim plus reconciliation by reference. This is a reported capability gap, not a stop: the flow is fully implementable without it.

**Assumptions**
- **A1. Payment method not required.** Rule `maxio-c4:createSubscription.7`: a product whose options require payment information is refused without a payment method. The plan relies on the task's statement that both plans have "payment method not required", and sends no payment data. The listing exposes `Product.RequireCreditCard`. If it is `true` for a handle, Maxio is expected to refuse the subscribe. **Decided:** no local pre-check; `RequireCreditCard` is surfaced as `requiresPaymentMethod` in `GET /api/subscription-plans`, and a Maxio refusal is returned as 422 with Maxio's message (`MaxioSubscriptionBillingService.CreateSubscriptionAsync`, which also logs it at Error as a product-configuration problem).
- **A2. Charge at creation (revised R1: the original assumption was wrong).** `next_billing_at` is omitted, so per rule `createSubscription.10` charges are assessed at creation. Live cp-exp-2 evidence: with `payment_collection_method` omitted (site default `automatic`) and no payment method, `CreateSubscription` → 422 `["No payment method was on file for the $299.00 balance"]`, even though `require_credit_card` is false. The plan now sends an invoice-style `PaymentCollectionMethod`: `Remittance` on Relationship Invoicing sites, `Invoice` on Statements sites, chosen from `Sites.ReadSite` → `Site.RelationshipInvoicingEnabled`. Under remittance, renewals generate an invoice instead of a charge (rule `background.60`). Not established: the post-create `state`, and whether rule `createSubscription.7` still refuses a paid product under remittance/invoice. Both are `UNVERIFIED`, so show whatever `State` comes back. Re-run the live sandbox subscribe to settle them.
- **A3. Customer reference and names.** The customer reference is derived from the JWT `ClaimTypes.Name` (the user name), which is the token's only identity claim. `CreateCustomer` needs non-blank `FirstName`, `LastName` and `Email`. The token carries none of those as separate claims. **Decided:** `Email` from the Identity user via `UserManager.FindByNameAsync` (falls back to the user name); `FirstName` = e-mail local part; `LastName` = `"eShopOnWeb customer"` (`CreateSubscriptionEndpoint.HandleAsync` / `CreateSubscriptionEndpoint.ToProfile`).
- **A4. Customer id width.** `Customer.Id` and `Subscription.Id` are `int?` in the SDK, and `ListCustomerSubscriptionsRequest.CustomerId` is `int`. Rule `every-request.21` warns that id widths may change. Store them locally in a wider type and convert at the call site; **decided:** local column `long?` (`BillingClaim.ProviderId`, `bigint`), converted with a checked cast at the call site (`MaxioSubscriptionBillingService.ToMaxioId`).
- **A5. `ListCustomerSubscriptions` completeness.** The record has no `page`/`per_page` and the map shows no pagination, so the plan treats the result as complete. Whether the server truncates long lists is `UNVERIFIED`. A demo user has at most a handful of subscriptions.
- **A6. `MAXIO_ENVIRONMENT` is not bound.** The task lists it but gives no `Maxio:` key for it, and the SDK's environments are `US`/`EU` hosting regions, not sandbox/production. `options.Environment` stays `Us`. If the sandbox site were EU-hosted, `Environment` would need to be `Eu` (rule `every-request.6`). That is assumed not to be the case.
- **A7. User secrets load in Development only.** User-secrets are loaded only when the environment is Development (standard host behaviour). Other environments supply the same keys through env config, e.g. `Maxio__ApiKey`.
- **A8. Concurrency limit.** Maxio allows at most 4 concurrent calls per subdomain (rule `every-request.26`). The plan walks product pages sequentially and makes no fan-out calls.
- **A9. "Next billing date" field.** **Decided:** `current_period_ends_at` → `nextBillingAt`; `next_assessment_at` also returned as `nextAssessmentAt` (`MaxioSubscriptionBillingService.ToDetails`).
- **A10. Site architecture flag (R1).** `Site.RelationshipInvoicingEnabled` (`bool?`) is assumed present on every site. If it is null, subscribe returns 503 and does not guess. The site read is cached for the process lifetime, so a change of architecture on the Maxio site takes effect after a restart, or after the cache is invalidated by a 422 naming `payment_collection_method`.

## 7. Source labels

Each `Source` cell above names the map page or map-named file it came from. `UNVERIFIED` marks facts only live traffic can settle: not-found status of `ReadCustomerByReference`, duplicate-reference outcome on `CreateCustomer`, subscription-reference uniqueness, BaseUrl trailing-slash join, `ListCustomerSubscriptions` truncation, post-create state. Application decisions are recorded where they were marked.

**Settled live (cp-exp-2, 2026-11-13) and by tests:** `ReadCustomerByReference` not-found is HTTP 404 (the new-customer path ran: 404 → `CreateCustomer` 201); `FindSubscription` not-found is HTTP 404; the site is Relationship Invoicing, and a `remittance` subscription with no payment method is created in state `active` with `current_period_ends_at` one month out; a `Maxio:BaseUrl` override joins operation paths correctly with or without a trailing slash (`BaseUrlOverride_IsUsedVerbatimInsteadOfTheSubdomain`); a `ListProductsForProductFamily` 404 with a non-JSON body surfaces as `ResponseDeserializationException`, so the 404 check catches the `ApiException` base. Still unverified: duplicate-reference outcome of `CreateCustomer`, subscription-reference uniqueness, `ListCustomerSubscriptions` truncation.
