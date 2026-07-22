namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Provider payloads in the shape Maxio Advanced Billing actually returns them: money in integer
/// cents on <c>*_in_cents</c> fields, decimal strings elsewhere, and snake-cased wire names.
/// </summary>
public static class BillingPayloads
{
    public const string ProductFamilies = """
        [{"product_family":{"id":3023074,"name":"eShopSubscribe","handle":"eshop-subscribe"}}]
        """;

    /// <summary>Pro Plan at $299.00/month and Basic Plan at $29.00/month, plus one archived plan.</summary>
    public const string ProductsForFamily = """
        [
          {"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,
            "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null}},
          {"product":{"id":7126958,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,
            "interval":1,"interval_unit":"month","require_credit_card":true,"archived_at":null}},
          {"product":{"id":7126959,"handle":"retired-plan","name":"Retired Plan","price_in_cents":1000,
            "interval":1,"interval_unit":"month","archived_at":"2024-01-01T00:00:00-05:00"}}
        ]
        """;

    public const string ProProduct = """
        {"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,
          "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null}}
        """;

    public const string BasicProduct = """
        {"product":{"id":7126958,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,
          "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null}}
        """;

    public const string Customer = """
        {"customer":{"id":88001,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com",
          "first_name":"demouser","last_name":"eShopOnWeb"}}
        """;

    /// <summary>An active subscription on Pro Plan with a $12.34 balance.</summary>
    public const string ActiveSubscription = """
        {"subscription":{"id":15236915,"state":"active","balance_in_cents":1234,"currency":"USD",
          "current_period_ends_at":"2026-08-01T00:00:00-04:00","next_assessment_at":"2026-08-01T00:00:00-04:00",
          "cancel_at_end_of_period":false,
          "customer":{"id":88001,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com"},
          "product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,
            "interval":1,"interval_unit":"month"}}}
        """;

    public const string CustomerSubscriptions = $"[{ActiveSubscription}]";

    public const string PausedSubscription = """
        {"subscription":{"id":15236915,"state":"on_hold","balance_in_cents":0,
          "on_hold_at":"2026-07-20T00:00:00-04:00",
          "customer":{"id":88001,"reference":"demouser@microsoft.com"},
          "product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900}}}
        """;

    public const string CanceledSubscription = """
        {"subscription":{"id":15236915,"state":"canceled","balance_in_cents":0,
          "canceled_at":"2026-07-22T00:00:00-04:00",
          "customer":{"id":88001,"reference":"demouser@microsoft.com"},
          "product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900}}}
        """;

    public const string PendingCancellationSubscription = """
        {"subscription":{"id":15236915,"state":"active","balance_in_cents":0,
          "cancel_at_end_of_period":true,"current_period_ends_at":"2026-08-01T00:00:00-04:00",
          "customer":{"id":88001,"reference":"demouser@microsoft.com"},
          "product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900}}}
        """;

    /// <summary>The metered add-on: $0.01 a unit, reported as a decimal string, not as cents.</summary>
    public const string MeteredComponent = """
        {"component":{"id":3057195,"name":"API Calls","handle":"api-call","kind":"metered_component",
          "pricing_scheme":"per_unit","unit_price":"0.01","unit_name":"api call",
          "price_per_unit_in_cents":null,"archived":false}}
        """;

    public const string QuantityBasedComponent = """
        {"component":{"id":3057196,"name":"Seats","handle":"api-call","kind":"quantity_based_component",
          "pricing_scheme":"per_unit","unit_price":"5.00","archived":false}}
        """;

    public const string UsageRecorded = """
        {"usage":{"id":138522957,"memo":"eShopOnWeb order 42","created_at":"2026-07-22T10:05:32-06:00",
          "quantity":7,"component_id":3057195,"component_handle":"api-call","subscription_id":15236915}}
        """;

    public const string SubscriptionComponent = """
        {"component":{"component_id":3057195,"subscription_id":15236915,"unit_balance":250,
          "kind":"metered_component","unit_name":"api call","enabled":true}}
        """;

    /// <summary>$100.00 charge, $40.00 credit applied, $60.00 due.</summary>
    public const string MigrationPreview = """
        {"migration":{"prorated_adjustment_in_cents":-4000,"charge_in_cents":10000,
          "payment_due_in_cents":6000,"credit_applied_in_cents":4000}}
        """;

    /// <summary>The provider omits the payment due, leaving it to be derived from charge less credit.</summary>
    public const string MigrationPreviewWithoutPaymentDue = """
        {"migration":{"charge_in_cents":10000,"credit_applied_in_cents":4000}}
        """;

    public const string MigratedSubscription = """
        {"subscription":{"id":15236915,"state":"active","balance_in_cents":6000,
          "customer":{"id":88001,"reference":"demouser@microsoft.com"},
          "product":{"id":7126958,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900}}}
        """;

    public const string DelayedCancellationAccepted = """
        {"message":"Successfully scheduled the subscription to be canceled at the end of the period"}
        """;

    public const string ValidationErrors = """
        {"errors":["Product must be provided","Subscription is not active"]}
        """;
}
