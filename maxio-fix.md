# Maxio SDK 1.0.2 corrections (map-grounded)
Source: .opencode/skills/maxio-getting-started/sdk-map.md + sub-pages (read this session).
Namespace: MaxioAdvancedBilling. Package: AsadAli.AdvancedBilling.Sdk 1.0.2 (commit 15db14b).

## 1) Server override — exact property path
Map: sdk-map.md §Servers & auth (lines 213-223).
- Options type: MaxioAdvancedBillingClientOptions (root namespace MaxioAdvancedBilling).
- Property: Server (type ServerOptions, namespace MaxioAdvancedBilling.Servers / Server.cs).
- ServerOptions has Production (ProductionOptions) and Ebb (EbbOptions); each has Us / Eu.
- To override US production base URL: options.Server.Production.Us.BaseUrl = "http://localhost:8080".
- To override site/subdomain: options.Server.Production.Us.Site = "your-subdomain".
There is NO Chargify property inside ServerOptions; your attempt `options.Server.Chargify.Us.BaseUrl` is wrong.

## 2) Subscription model properties (records-3-Of-Su.md line with `Subscription`)
Subscription (namespace MaxioAdvancedBilling.Models; source Models/Subscription.cs) field list (wire names in parens):
- Id (id): int?
- State (state): SubscriptionState?
- Product (product): Product?  — NOT ProductHandle/ProductId at top level
- BalanceInCents (balance_in_cents): long?
- CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?
- NextAssessmentAt (next_assessment_at): DateTimeOffset?  (next billing assessment; NOT `NextBillingAt` on model)
- CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?
- PreviousState (previous_state): SubscriptionState?
- Reference (reference): string?
- Customer (customer): Customer?
- ProductPricePointId (product_price_point_id): int?
- etc.
Your guesses did NOT match: Subscription has NO direct PropertyHandle, ProductId, AmountPaid, NextBillingAt, PeriodEnd. Use nested Product (with Handle/Id) and the fields above.

## 3) ListProductsForProductFamily required params (operations/ProductFamilies.md)
Signature: ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)
- Required (no default, must pass explicitly): productFamilyId (string), plus all nullable params before defaults must be passed explicitly if you call with named args: dateField, filter, startDate, endDate, startDatetime, endDatetime, includeArchived, include.
- Defaults only on page (1) and perPage (20).
Correct named-argument call (pass null for all optional):
  client.ProductFamilies.ListProductsForProductFamily(
    productFamilyId: "family-id",
    dateField: null,
    filter: null,
    startDate: null,
    endDate: null,
    startDatetime: null,
    endDatetime: null,
    includeArchived: null,
    include: null,
    ct: default);
NOTE: `dateField` was the missed required-explicit param; it must be passed (null to skip). Source: map row notes "5 params ... must pass explicitly".

## 4) ListCustomers required params (operations/Customers.md)
Signature: ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)
- `direction` is nullable with NO default and must be passed explicitly (pass null to skip). It is NOT a validation-required field, but the SDK signature requires an explicit argument.
Correct named call:
  client.Customers.ListCustomers(
    direction: null,
    dateField: null,
    startDate: null,
    endDate: null,
    startDatetime: null,
    endDatetime: null,
    q: null,
    ct: default);
Defaults: page = 1, perPage = 50.

## 5) CreateSubscriptionRequest / CreateSubscription properties (records-2-Cr-Ne.md; operations/Subscriptions.md)
Request model: CreateSubscription (namespace MaxioAdvancedBilling.Models; Models/CreateSubscription.cs).
Correct property names (C# identifier → wire name):
- ProductHandle (product_handle): string?
- ProductId (product_id): int?
- ProductPricePointHandle (product_price_point_handle): string?
- ProductPricePointId (product_price_point_id): int?
- CustomerReference (customer_reference): string?
- CustomerId (customer_id): int?
- NextBillingAt (next_billing_at): DateTimeOffset?
- InitialBillingAt (initial_billing_at): DateTimeOffset?
- Reference (reference): string?
- CouponCode (coupon_code): string?
- CouponCodes (coupon_codes): IReadOnlyList<string>?
- CustomerAttributes (customer_attributes): CustomerAttributes?
- PaymentProfileId (payment_profile_id): int?
- PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?
- Components (components): IReadOnlyList<CreateSubscriptionComponent>?
Response envelope: SubscriptionResponse (source operations/Subscriptions.md) — read .Subscription (inner Subscription), NOT direct properties.
Note: `CreateSubscription` method takes `CreateSubscriptionRequest? body` (nullable, must pass explicitly). Source: Subscriptions.md CreateSubscription row.

---
Trap notes (must load companion skills — contents not reproduced here per rules):
- Client init / auth / server nodes: MUST load dotnet-client-initialization + dotnet-authentication + dotnet-configuration-resilience before wiring Server override.
- Request building / enums / response envelope (SubscriptionResponse.Subscription): MUST load dotnet-models.
- Call signatures / named args / cancellation token named `ct`: MUST load dotnet-calling-endpoints.
- Error handling (SdkException<CreateSubscriptionError> with TryGetErrorListResponse1, Case A/B mechanics, JsonException boundary): MUST load dotnet-error-handling. Include the two mandatory JsonException rows (drifted 2xx body vs non-2xx construction failure) in the boundary design.
- Tests: MUST load dotnet-testing.
