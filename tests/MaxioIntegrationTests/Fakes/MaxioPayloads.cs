namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Maxio response bodies in the exact shape the API returns, taken from the operation examples in
/// maxio-spec/openapi.yaml and from the seeded eShopSubscribe product family. Money is deliberately
/// left in the provider's own units — integer minor units for products and subscriptions, a decimal
/// major-unit string for components — so the client's conversions are genuinely under test.
/// </summary>
internal static class MaxioPayloads
{
    public const int ProPlanId = 7130995;
    public const int BasicPlanId = 7130996;
    public const int ApiCallComponentId = 3062732;
    public const int CustomerId = 97865600;
    public const int SubscriptionId = 93482504;
    public const string CustomerReference = "demouser@microsoft.com";

    public const string ProPlanJson = """
        {
          "product": {
            "id": 7130995,
            "name": "Pro Plan",
            "handle": "eshop-pro",
            "description": "Everything in Basic, plus priority support.",
            "price_in_cents": 29900,
            "interval": 1,
            "interval_unit": "month",
            "archived_at": null,
            "product_family": { "id": 3026729, "handle": "eshop-subscribe", "name": "eShopSubscribe" }
          }
        }
        """;

    public const string BasicPlanJson = """
        {
          "product": {
            "id": 7130996,
            "name": "Basic Plan",
            "handle": "basic-plan",
            "description": null,
            "price_in_cents": 2900,
            "interval": 1,
            "interval_unit": "month",
            "archived_at": null,
            "product_family": { "id": 3026729, "handle": "eshop-subscribe", "name": "eShopSubscribe" }
          }
        }
        """;

    /// <summary>The family's product list, including one archived product that must not be offered.</summary>
    private const string ArchivedPlanJson = """
        {
          "product": {
            "id": 7130997,
            "name": "Retired Plan",
            "handle": "retired-plan",
            "price_in_cents": 9900,
            "interval": 1,
            "interval_unit": "month",
            "archived_at": "2026-01-05T09:00:00-05:00"
          }
        }
        """;

    public const string PlanListJson = $"[{BasicPlanJson},{ProPlanJson},{ArchivedPlanJson}]";

    public const string CustomerJson = """
        {
          "customer": {
            "id": 97865600,
            "first_name": "demouser",
            "last_name": "eShopOnWeb",
            "email": "demouser@microsoft.com",
            "reference": "demouser@microsoft.com"
          }
        }
        """;

    /// <summary>An active subscription on the Pro Plan, with the plan nested as Maxio returns it.</summary>
    public const string ActiveSubscriptionJson = """
        {
          "subscription": {
            "id": 93482504,
            "state": "active",
            "balance_in_cents": 29900,
            "current_period_ends_at": "2026-08-23T11:55:15+05:00",
            "next_assessment_at": "2026-08-23T11:55:15+05:00",
            "cancel_at_end_of_period": false,
            "delayed_cancel_at": null,
            "next_product_handle": null,
            "customer": { "id": 97865600, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" },
            "product": {
              "id": 7130995,
              "name": "Pro Plan",
              "handle": "eshop-pro",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month"
            }
          }
        }
        """;

    public const string CancelledSubscriptionJson = """
        {
          "subscription": {
            "id": 93482504,
            "state": "canceled",
            "balance_in_cents": 0,
            "current_period_ends_at": "2026-08-23T11:55:15+05:00",
            "next_assessment_at": null,
            "cancel_at_end_of_period": false,
            "customer": { "id": 97865600, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" },
            "product": {
              "id": 7130995, "name": "Pro Plan", "handle": "eshop-pro",
              "price_in_cents": 29900, "interval": 1, "interval_unit": "month"
            }
          }
        }
        """;

    /// <summary>A subscription cancelling at the period boundary rather than immediately.</summary>
    public const string PendingCancellationSubscriptionJson = """
        {
          "subscription": {
            "id": 93482504,
            "state": "active",
            "balance_in_cents": 29900,
            "current_period_ends_at": "2026-08-23T11:55:15+05:00",
            "next_assessment_at": "2026-08-23T11:55:15+05:00",
            "cancel_at_end_of_period": true,
            "delayed_cancel_at": "2026-08-23T11:55:15+05:00",
            "customer": { "id": 97865600, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" },
            "product": {
              "id": 7130995, "name": "Pro Plan", "handle": "eshop-pro",
              "price_in_cents": 29900, "interval": 1, "interval_unit": "month"
            }
          }
        }
        """;

    public const string PausedSubscriptionJson = """
        {
          "subscription": {
            "id": 93482504,
            "state": "on_hold",
            "balance_in_cents": 29900,
            "current_period_ends_at": "2026-08-23T11:55:15+05:00",
            "next_assessment_at": "2026-08-23T11:55:15+05:00",
            "cancel_at_end_of_period": false,
            "customer": { "id": 97865600, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" },
            "product": {
              "id": 7130995, "name": "Pro Plan", "handle": "eshop-pro",
              "price_in_cents": 29900, "interval": 1, "interval_unit": "month"
            }
          }
        }
        """;

    /// <summary>A subscription with a delayed product change scheduled for the next renewal.</summary>
    public const string SubscriptionWithPendingPlanChangeJson = """
        {
          "subscription": {
            "id": 93482504,
            "state": "active",
            "balance_in_cents": 2900,
            "current_period_ends_at": "2026-08-23T11:55:15+05:00",
            "next_assessment_at": "2026-08-23T11:55:15+05:00",
            "cancel_at_end_of_period": false,
            "next_product_handle": "eshop-pro",
            "customer": { "id": 97865600, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" },
            "product": {
              "id": 7130996, "name": "Basic Plan", "handle": "basic-plan",
              "price_in_cents": 2900, "interval": 1, "interval_unit": "month"
            }
          }
        }
        """;

    public const string SubscriptionListJson = $"[{ActiveSubscriptionJson}]";

    /// <summary>The metered component. Maxio prices components in decimal dollars, as a string.</summary>
    public const string MeteredComponentJson = """
        {
          "component": {
            "id": 3062732,
            "name": "API Calls",
            "handle": "api-call",
            "kind": "metered_component",
            "pricing_scheme": "per_unit",
            "unit_price": "0.01",
            "product_family_id": 3026729
          }
        }
        """;

    /// <summary>The same handle pointing at a component of the wrong kind — UC0's mis-seed failure.</summary>
    public const string QuantityBasedComponentJson = """
        {
          "component": {
            "id": 3062732,
            "name": "API Calls",
            "handle": "api-call",
            "kind": "quantity_based_component",
            "pricing_scheme": "per_unit",
            "unit_price": "0.01",
            "product_family_id": 3026729
          }
        }
        """;

    public const string UsageJson = """
        {
          "usage": {
            "id": 3633658896,
            "memo": "API calls",
            "created_at": "2026-07-23T11:55:27+05:00",
            "price_point_id": 149416,
            "quantity": 25,
            "component_id": 3062732,
            "component_handle": "api-call",
            "subscription_id": 93482504
          }
        }
        """;

    /// <summary>The accrued balance line item Maxio returns for a subscription's component.</summary>
    public const string SubscriptionComponentJson = """
        {
          "component": {
            "component_id": 3062732,
            "subscription_id": 93482504,
            "kind": "metered_component",
            "unit_balance": 35
          }
        }
        """;

    /// <summary>A downgrade preview: credit for the unused Pro period against the Basic charge.</summary>
    public const string MigrationPreviewJson = """
        {
          "migration": {
            "prorated_adjustment_in_cents": -29900,
            "charge_in_cents": 2934,
            "payment_due_in_cents": 0,
            "credit_applied_in_cents": -26966
          }
        }
        """;

    public const string DelayedCancellationJson = """
        { "message": "This subscription will be canceled at the end of the period" }
        """;

    public const string UnprocessableEntityJson = """
        { "errors": ["No payment method was on file for the $299.00 balance"] }
        """;

    public const string NotFoundJson = """
        { "errors": ["Subscription not found"] }
        """;
}
