namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Response payloads shaped exactly as the Maxio OpenAPI specification documents them —
/// list endpoints return arrays of single-key wrapper objects, product prices are integer
/// cents, and component unit prices are strings.
/// </summary>
public static class MaxioJson
{
    public const string ProPlanId = "7130993";
    public const string BasicPlanId = "7130994";

    public static string Product(string id, string handle, string name, int priceInCents,
        string? archivedAt = null, bool requireCreditCard = false) =>
        $$"""
        {
          "id": {{id}},
          "name": "{{name}}",
          "handle": "{{handle}}",
          "description": "{{name}} description",
          "price_in_cents": {{priceInCents}},
          "interval": 1,
          "interval_unit": "month",
          "require_credit_card": {{(requireCreditCard ? "true" : "false")}},
          "archived_at": {{(archivedAt is null ? "null" : $"\"{archivedAt}\"")}},
          "product_family": { "id": 3026728, "handle": "eshop-subscribe", "name": "eShopSubscribe" }
        }
        """;

    public static string ProductResponse(string id, string handle, string name, int priceInCents) =>
        $$"""{ "product": {{Product(id, handle, name, priceInCents)}} }""";

    public static string ProductList(params string[] products) =>
        "[" + string.Join(",", products.Select(p => $$"""{ "product": {{p}} }""")) + "]";

    public static string Customer(int id, string reference, string email) =>
        $$"""
        {
          "id": {{id}},
          "reference": "{{reference}}",
          "email": "{{email}}",
          "first_name": "Demo",
          "last_name": "User"
        }
        """;

    public static string CustomerResponse(int id, string reference, string email) =>
        $$"""{ "customer": {{Customer(id, reference, email)}} }""";

    public static string Subscription(int id, string state, string planHandle = "eshop-pro",
        int planPriceInCents = 29900, int customerId = 55, string customerReference = "demo@microsoft.com",
        string? currentPeriodEndsAt = "2026-08-22T10:00:00-05:00", bool cancelAtEndOfPeriod = false,
        string? canceledAt = null, string? automaticallyResumeAt = null) =>
        $$"""
        {
          "id": {{id}},
          "state": "{{state}}",
          "current_period_ends_at": {{(currentPeriodEndsAt is null ? "null" : $"\"{currentPeriodEndsAt}\"")}},
          "cancel_at_end_of_period": {{(cancelAtEndOfPeriod ? "true" : "false")}},
          "canceled_at": {{(canceledAt is null ? "null" : $"\"{canceledAt}\"")}},
          "automatically_resume_at": {{(automaticallyResumeAt is null ? "null" : $"\"{automaticallyResumeAt}\"")}},
          "customer": {{Customer(customerId, customerReference, customerReference)}},
          "product": {{Product(ProPlanId, planHandle, "Pro Plan", planPriceInCents)}}
        }
        """;

    public static string SubscriptionResponse(int id, string state, string planHandle = "eshop-pro",
        int planPriceInCents = 29900) =>
        $$"""{ "subscription": {{Subscription(id, state, planHandle, planPriceInCents)}} }""";

    public static string SubscriptionList(params string[] subscriptions) =>
        "[" + string.Join(",", subscriptions.Select(s => $$"""{ "subscription": {{s}} }""")) + "]";

    /// <summary>Note: Maxio returns component unit prices as strings, e.g. "0.01".</summary>
    public static string ComponentResponse(int id, string handle, string kind, string? unitPrice = "0.01",
        string familyHandle = "eshop-subscribe") =>
        $$"""
        {
          "component": {
            "id": {{id}},
            "name": "API Calls",
            "handle": "{{handle}}",
            "kind": "{{kind}}",
            "pricing_scheme": "per_unit",
            "unit_price": {{(unitPrice is null ? "null" : $"\"{unitPrice}\"")}},
            "product_family_id": 3026728,
            "product_family_handle": "{{familyHandle}}",
            "archived": false
          }
        }
        """;

    public static string SubscriptionComponentResponse(int componentId, string handle, decimal unitBalance) =>
        $$"""
        {
          "component": {
            "id": 1,
            "component_id": {{componentId}},
            "component_handle": "{{handle}}",
            "kind": "metered_component",
            "unit_balance": {{unitBalance}},
            "subscription_id": 101
          }
        }
        """;

    /// <summary>Maxio may express a usage quantity as a number or as a string such as "20.0".</summary>
    public static string UsageResponse(long id, string quantityLiteral, string? memo = "recorded by test",
        int componentId = 3062731, string componentHandle = "api-call", int subscriptionId = 101) =>
        $$"""
        {
          "usage": {
            "id": {{id}},
            "memo": {{(memo is null ? "null" : $"\"{memo}\"")}},
            "created_at": "2026-07-22T11:00:00-05:00",
            "quantity": {{quantityLiteral}},
            "component_id": {{componentId}},
            "component_handle": "{{componentHandle}}",
            "subscription_id": {{subscriptionId}}
          }
        }
        """;

    public static string MigrationPreviewResponse(int proratedAdjustment, int charge, int paymentDue,
        int creditApplied) =>
        $$"""
        {
          "migration": {
            "prorated_adjustment_in_cents": {{proratedAdjustment}},
            "charge_in_cents": {{charge}},
            "payment_due_in_cents": {{paymentDue}},
            "credit_applied_in_cents": {{creditApplied}}
          }
        }
        """;

    public const string DelayedCancelMessage = """{ "message": "This subscription will be canceled" }""";
}
