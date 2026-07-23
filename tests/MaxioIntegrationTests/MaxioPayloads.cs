namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Response bodies in the shapes the Maxio OpenAPI specification documents, matching what the live
/// sandbox returns for the seeded eShopSubscribe catalog. Prices deliberately keep the provider's
/// own units: products and migrations in integer minor units, components as a decimal string.
/// </summary>
public static class MaxioPayloads
{
    /// <summary>Pro Plan — $299.00/month, so 29900 minor units.</summary>
    public const string ProPlanProduct = """
        {
          "product": {
            "id": 7130997,
            "name": "Pro Plan",
            "handle": "eshop-pro",
            "description": "The full eShopOnWeb subscription.",
            "price_in_cents": 29900,
            "interval": 1,
            "interval_unit": "month",
            "archived_at": null
          }
        }
        """;

    /// <summary>Basic Plan — $29.00/month, so 2900 minor units.</summary>
    public const string BasicPlanProduct = """
        {
          "product": {
            "id": 7130998,
            "name": "Basic Plan",
            "handle": "basic-plan",
            "description": "The entry-level eShopOnWeb subscription.",
            "price_in_cents": 2900,
            "interval": 1,
            "interval_unit": "month",
            "archived_at": null
          }
        }
        """;

    public const string ArchivedProduct = """
        {
          "product": {
            "id": 7130999,
            "name": "Retired Plan",
            "handle": "retired-plan",
            "price_in_cents": 9900,
            "interval": 1,
            "interval_unit": "month",
            "archived_at": "2026-01-01T00:00:00-05:00"
          }
        }
        """;

    public static string PlanList => $"[{ProPlanProduct},{BasicPlanProduct}]";

    public static string PlanListWithArchived => $"[{ProPlanProduct},{BasicPlanProduct},{ArchivedProduct}]";

    public const string EmptyList = "[]";

    public const string ProductFamily = """
        {
          "product_family": {
            "id": 3026730,
            "name": "eShopSubscribe",
            "handle": "eshop-subscribe",
            "description": null
          }
        }
        """;

    public const string Customer = """
        {
          "customer": {
            "id": 97865317,
            "first_name": "Demo",
            "last_name": "User",
            "email": "demouser@microsoft.com",
            "reference": "demouser@microsoft.com",
            "created_at": "2026-07-23T11:44:53+05:00"
          }
        }
        """;

    /// <summary>An active subscription on Pro Plan. product_price_in_cents is 29900 = $299.00.</summary>
    public const string ActiveProSubscription = """
        {
          "subscription": {
            "id": 93482336,
            "state": "active",
            "balance_in_cents": 0,
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-08-23T11:44:53+05:00",
            "current_period_started_at": "2026-07-23T11:44:53+05:00",
            "activated_at": "2026-07-23T11:44:53+05:00",
            "cancel_at_end_of_period": false,
            "delayed_cancel_at": null,
            "customer": {
              "id": 97865317,
              "first_name": "Demo",
              "last_name": "User",
              "email": "demouser@microsoft.com",
              "reference": "demouser@microsoft.com"
            },
            "product": {
              "id": 7130997,
              "name": "Pro Plan",
              "handle": "eshop-pro",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month"
            }
          }
        }
        """;

    /// <summary>The same subscription after moving to Basic Plan.</summary>
    public const string ActiveBasicSubscription = """
        {
          "subscription": {
            "id": 93482336,
            "state": "active",
            "product_price_in_cents": 2900,
            "current_period_ends_at": "2026-08-23T11:44:53+05:00",
            "activated_at": "2026-07-23T11:44:53+05:00",
            "cancel_at_end_of_period": false,
            "customer": { "id": 97865317, "reference": "demouser@microsoft.com" },
            "product": {
              "id": 7130998,
              "name": "Basic Plan",
              "handle": "basic-plan",
              "price_in_cents": 2900,
              "interval": 1,
              "interval_unit": "month"
            }
          }
        }
        """;

    public const string OnHoldSubscription = """
        {
          "subscription": {
            "id": 93482336,
            "state": "on_hold",
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-08-23T11:44:53+05:00",
            "cancel_at_end_of_period": false,
            "customer": { "id": 97865317, "reference": "demouser@microsoft.com" },
            "product": { "id": 7130997, "name": "Pro Plan", "handle": "eshop-pro", "interval": 1, "interval_unit": "month" }
          }
        }
        """;

    public const string CanceledSubscription = """
        {
          "subscription": {
            "id": 93482336,
            "state": "canceled",
            "product_price_in_cents": 29900,
            "cancel_at_end_of_period": false,
            "canceled_at": "2026-07-23T12:00:00+05:00",
            "customer": { "id": 97865317, "reference": "demouser@microsoft.com" },
            "product": { "id": 7130997, "name": "Pro Plan", "handle": "eshop-pro", "interval": 1, "interval_unit": "month" }
          }
        }
        """;

    /// <summary>Active but scheduled to cancel at the period boundary.</summary>
    public const string PendingCancellationSubscription = """
        {
          "subscription": {
            "id": 93482336,
            "state": "active",
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-08-23T11:44:53+05:00",
            "cancel_at_end_of_period": true,
            "delayed_cancel_at": "2026-08-23T11:44:53+05:00",
            "customer": { "id": 97865317, "reference": "demouser@microsoft.com" },
            "product": { "id": 7130997, "name": "Pro Plan", "handle": "eshop-pro", "interval": 1, "interval_unit": "month" }
          }
        }
        """;

    public static string SubscriptionList => $"[{ActiveProSubscription}]";

    /// <summary>Metered component, per-unit, $0.01 each — a decimal string, not minor units.</summary>
    public const string MeteredComponent = """
        {
          "component": {
            "id": 3062733,
            "name": "API Calls",
            "handle": "api-call",
            "kind": "metered_component",
            "pricing_scheme": "per_unit",
            "unit_price": "0.01",
            "unit_name": "API call",
            "product_family_id": 3026730,
            "archived": false
          }
        }
        """;

    /// <summary>The same handle seeded with the wrong kind — the UC0 mistake UC2 must refuse.</summary>
    public const string QuantityBasedComponent = """
        {
          "component": {
            "id": 3062733,
            "name": "API Calls",
            "handle": "api-call",
            "kind": "quantity_based_component",
            "pricing_scheme": "per_unit",
            "unit_price": "0.01",
            "product_family_id": 3026730,
            "archived": false
          }
        }
        """;

    public const string UsageRecorded = """
        {
          "usage": {
            "id": 3633653747,
            "memo": "Reported from the storefront",
            "created_at": "2026-07-23T11:45:05+05:00",
            "price_point_id": 20405689,
            "quantity": 150,
            "component_id": 3062733,
            "component_handle": "api-call",
            "subscription_id": 93482336
          }
        }
        """;

    /// <summary>Maxio may return quantity as a string; the client must read it either way.</summary>
    public const string UsageRecordedWithStringQuantity = """
        {
          "usage": {
            "id": 3633653748,
            "memo": "batch",
            "created_at": "2026-07-23T11:46:05+05:00",
            "quantity": "20.5",
            "component_id": 3062733,
            "component_handle": "api-call",
            "subscription_id": 93482336
          }
        }
        """;

    /// <summary>The subscription's component line item, carrying the period-to-date unit_balance.</summary>
    public const string SubscriptionComponentWithBalance = """
        {
          "component": {
            "component_id": 3062733,
            "subscription_id": 93482336,
            "component_handle": "api-call",
            "name": "API Calls",
            "kind": "metered_component",
            "unit_balance": 150,
            "unit_name": "API call",
            "enabled": true
          }
        }
        """;

    /// <summary>Downgrading Pro to Basic mid-period: a $299.00 credit against a $30.49 charge.</summary>
    public const string MigrationPreview = """
        {
          "migration": {
            "prorated_adjustment_in_cents": -29900,
            "charge_in_cents": 3049,
            "payment_due_in_cents": 0,
            "credit_applied_in_cents": -26851
          }
        }
        """;

    public const string DelayedCancellationMessage = """
        { "message": "This subscription is scheduled to be canceled at the end of the current period" }
        """;

    /// <summary>The array error shape: {"errors": ["..."]}.</summary>
    public const string ErrorArray = """
        { "errors": ["No payment method was on file for the $299.00 balance"] }
        """;

    /// <summary>The object error shape: {"errors": {"field": "..."}}.</summary>
    public const string ErrorObject = """
        { "errors": { "customer": "can't be blank" } }
        """;
}
