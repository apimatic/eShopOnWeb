namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Provider wire payloads, in the envelope shapes the Maxio API uses. Money is always in cents here, which
/// is exactly what the mapping under test has to convert.
/// </summary>
public static class ProviderPayloads
{
    /// <summary>Pro Plan at $299.00/month, expressed on the wire as 29900 cents.</summary>
    public const string ProPlanProductList = """
        [
          {
            "product": {
              "id": 7126957,
              "name": "Pro Plan",
              "handle": "eshop-pro",
              "description": "Everything, monthly",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month",
              "require_credit_card": false,
              "request_credit_card": false,
              "archived_at": null,
              "product_family": { "id": 3023074, "handle": "eshop-subscribe", "name": "eShopSubscribe" },
              "an_unmodelled_future_field": "must be ignored, not rejected"
            }
          },
          {
            "product": {
              "id": 7126958,
              "name": "Basic Plan",
              "handle": "basic-plan",
              "price_in_cents": 2900,
              "interval": 1,
              "interval_unit": "month",
              "require_credit_card": true,
              "archived_at": "2026-01-05T10:00:00-05:00",
              "product_family": { "id": 3023074, "handle": "eshop-subscribe" }
            }
          }
        ]
        """;

    public const string EmptyList = "[]";

    public const string ProductFamilyList = """
        [
          { "product_family": { "id": 3023074, "name": "eShopSubscribe", "handle": "eshop-subscribe" } }
        ]
        """;

    public const string Customer = """
        {
          "customer": {
            "id": 555001,
            "first_name": "shopper",
            "last_name": "Customer",
            "email": "shopper@example.com",
            "reference": "shopper@example.com"
          }
        }
        """;

    /// <summary>An active subscription on the Pro Plan with a $12.34 balance.</summary>
    public const string ActiveSubscription = """
        {
          "subscription": {
            "id": 15236915,
            "state": "active",
            "balance_in_cents": 1234,
            "current_period_started_at": "2026-07-01T00:00:00-04:00",
            "current_period_ends_at": "2026-08-01T00:00:00-04:00",
            "next_assessment_at": "2026-08-01T00:00:00-04:00",
            "cancel_at_end_of_period": false,
            "customer": { "id": 555001, "reference": "shopper@example.com", "email": "shopper@example.com" },
            "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 }
          }
        }
        """;

    public const string PausedSubscription = """
        {
          "subscription": {
            "id": 15236915,
            "state": "on_hold",
            "balance_in_cents": 0,
            "customer": { "id": 555001, "reference": "shopper@example.com" },
            "product": { "id": 7126957, "handle": "eshop-pro", "price_in_cents": 29900 }
          }
        }
        """;

    public const string SubscriptionPendingEndOfPeriodCancellation = """
        {
          "subscription": {
            "id": 15236915,
            "state": "active",
            "balance_in_cents": 0,
            "cancel_at_end_of_period": true,
            "delayed_cancel_at": "2026-08-01T00:00:00-04:00",
            "customer": { "id": 555001, "reference": "shopper@example.com" },
            "product": { "id": 7126957, "handle": "eshop-pro", "price_in_cents": 29900 }
          }
        }
        """;

    public const string SubscriptionList = $"[{ActiveSubscription}]";

    public const string DelayedCancellationAccepted = """{ "message": "Subscription will be cancelled at end of period" }""";

    /// <summary>The API Calls component: metered, $0.01 per unit — one cent.</summary>
    public const string MeteredComponent = """
        {
          "component": {
            "id": 3057195,
            "name": "API Calls",
            "handle": "api-call",
            "kind": "metered_component",
            "pricing_scheme": "per_unit",
            "unit_name": "call",
            "unit_price": "0.01",
            "price_per_unit_in_cents": 1,
            "product_family_id": 3023074,
            "product_family_handle": "eshop-subscribe"
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
            "price_per_unit_in_cents": 500
          }
        }
        """;

    public const string UsageRecord = """
        {
          "usage": {
            "id": 900001,
            "quantity": 5,
            "memo": "order placed",
            "created_at": "2026-07-23T09:15:00-04:00",
            "component_id": 3057195,
            "component_handle": "api-call",
            "subscription_id": 15236915
          }
        }
        """;

    /// <summary>Usage read back as a string quantity — the provider models it as an int-or-string union.</summary>
    public const string UsageRecordWithStringQuantity = """
        {
          "usage": {
            "id": 900002,
            "quantity": "7.5",
            "component_id": 3057195,
            "component_handle": "api-call",
            "subscription_id": 15236915
          }
        }
        """;

    public const string SubscriptionComponentBalance = """
        {
          "component": {
            "id": 3057195,
            "component_id": 3057195,
            "component_handle": "api-call",
            "kind": "metered_component",
            "unit_balance": 42,
            "subscription_id": 15236915
          }
        }
        """;

    /// <summary>An upgrade preview: $250.00 prorated charge, $10.00 credit applied.</summary>
    public const string MigrationPreview = """
        {
          "migration": {
            "prorated_adjustment_in_cents": 25000,
            "charge_in_cents": 27000,
            "payment_due_in_cents": 26000,
            "credit_applied_in_cents": 1000
          }
        }
        """;

    /// <summary>A downgrade preview, where the prorated adjustment is a credit rather than a charge.</summary>
    public const string MigrationPreviewWithCredit = """
        {
          "migration": {
            "prorated_adjustment_in_cents": -13500,
            "charge_in_cents": 0,
            "payment_due_in_cents": 0,
            "credit_applied_in_cents": 13500
          }
        }
        """;

    public const string ValidationErrors = """{ "errors": ["Product handle is invalid", "Customer is required"] }""";
}
