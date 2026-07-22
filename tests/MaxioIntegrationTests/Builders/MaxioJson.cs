namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Response payloads shaped like the ones in the Maxio Advanced Billing OpenAPI specification.
/// </summary>
public static class MaxioJson
{
    public const string ProPlanProduct = """
        {
          "product": {
            "id": 7126957,
            "name": "Pro Plan",
            "handle": "eshop-pro",
            "description": "The hero plan",
            "price_in_cents": 29900,
            "interval": 1,
            "interval_unit": "month",
            "require_credit_card": false,
            "archived_at": null,
            "product_family": { "id": 3023074, "name": "eShopSubscribe", "handle": "eshop-subscribe" }
          }
        }
        """;

    public const string BasicPlanProduct = """
        {
          "product": {
            "id": 7126958,
            "name": "Basic Plan",
            "handle": "basic-plan",
            "price_in_cents": 2900,
            "interval": 1,
            "interval_unit": "month",
            "require_credit_card": false,
            "archived_at": null,
            "product_family": { "id": 3023074, "handle": "eshop-subscribe" }
          }
        }
        """;

    public const string ArchivedProduct = """
        {
          "product": {
            "id": 999,
            "name": "Retired Plan",
            "handle": "retired",
            "price_in_cents": 100,
            "interval": 1,
            "interval_unit": "month",
            "archived_at": "2024-01-01T00:00:00-05:00",
            "product_family": { "id": 3023074, "handle": "eshop-subscribe" }
          }
        }
        """;

    public static string ProductList(params string[] products) => "[" + string.Join(",", products) + "]";

    public const string Customer = """
        {
          "customer": {
            "id": 88833369,
            "first_name": "demo",
            "last_name": "user",
            "email": "demouser@microsoft.com",
            "reference": "demouser@microsoft.com"
          }
        }
        """;

    public const string ActiveSubscription = """
        {
          "subscription": {
            "id": 15236915,
            "state": "active",
            "balance_in_cents": 0,
            "product_price_in_cents": 29900,
            "current_period_started_at": "2026-07-01T00:00:00-05:00",
            "current_period_ends_at": "2026-08-01T00:00:00-05:00",
            "next_assessment_at": "2026-08-01T00:00:00-05:00",
            "activated_at": "2026-07-01T00:00:00-05:00",
            "cancel_at_end_of_period": false,
            "customer": { "id": 88833369, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" },
            "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 }
          }
        }
        """;

    public const string OnHoldSubscription = """
        {
          "subscription": {
            "id": 15236915,
            "state": "on_hold",
            "balance_in_cents": 1000,
            "product_price_in_cents": 29900,
            "cancel_at_end_of_period": false,
            "customer": { "id": 88833369, "reference": "demouser@microsoft.com" },
            "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan" }
          }
        }
        """;

    public const string PendingCancellationSubscription = """
        {
          "subscription": {
            "id": 15236915,
            "state": "active",
            "balance_in_cents": 0,
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-08-01T00:00:00-05:00",
            "cancel_at_end_of_period": true,
            "delayed_cancel_at": "2026-08-01T00:00:00-05:00",
            "customer": { "id": 88833369, "reference": "demouser@microsoft.com" },
            "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan" }
          }
        }
        """;

    public static string SubscriptionList(params string[] subscriptions) => "[" + string.Join(",", subscriptions) + "]";

    public const string MeteredComponent = """
        {
          "component": {
            "id": 3057195,
            "name": "API Calls",
            "handle": "api-call",
            "kind": "metered_component",
            "pricing_scheme": "per_unit",
            "unit_price": "0.01",
            "unit_name": "call",
            "product_family_id": 3023074,
            "product_family_handle": "eshop-subscribe",
            "archived": false
          }
        }
        """;

    public const string QuantityBasedComponent = """
        {
          "component": {
            "id": 3057196,
            "name": "Seats",
            "handle": "api-call",
            "kind": "quantity_based_component",
            "pricing_scheme": "per_unit",
            "unit_price": "5.0",
            "product_family_id": 3023074,
            "archived": false
          }
        }
        """;

    public const string Usage = """
        {
          "usage": {
            "id": 138522957,
            "memo": "order placed",
            "created_at": "2026-07-22T10:05:32-06:00",
            "price_point_id": 149416,
            "quantity": "250.0",
            "component_id": 3057195,
            "component_handle": "api-call",
            "subscription_id": 15236915
          }
        }
        """;

    public const string SubscriptionComponent = """
        {
          "component": {
            "component_id": 3057195,
            "subscription_id": 15236915,
            "component_handle": "api-call",
            "name": "API Calls",
            "kind": "metered_component",
            "unit_balance": 250,
            "enabled": true
          }
        }
        """;

    public const string MigrationPreview = """
        {
          "migration": {
            "prorated_adjustment_in_cents": -1450,
            "charge_in_cents": 14950,
            "payment_due_in_cents": 13500,
            "credit_applied_in_cents": 0
          }
        }
        """;

    public const string DelayedCancellationAck = """
        { "message": "This subscription will be canceled at the end of the period" }
        """;

    public const string ErrorArray = """
        { "errors": ["Quantity: must be greater than 0."] }
        """;

    public const string ErrorObject = """
        { "errors": { "customer": "can't be blank" } }
        """;
}
