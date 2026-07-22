namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Response bodies shaped exactly as the Maxio OpenAPI specification documents them, using the
/// values seeded for this integration (plan.md §1.3): Pro Plan at $299.00/month, Basic Plan at
/// $29.00/month, and the metered <c>api-call</c> component at $0.01 per unit.
/// </summary>
public static class MaxioPayloads
{
    public const string ProPlanJson = """
        {"product":{"id":7130993,"name":"Pro Plan","handle":"eshop-pro","description":"Everything, monthly",
        "price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,
        "archived_at":null,"product_family":{"id":3026728,"handle":"eshop-subscribe","name":"eShopSubscribe"}}}
        """;

    public const string BasicPlanJson = """
        {"product":{"id":7130994,"name":"Basic Plan","handle":"basic-plan","description":"The essentials",
        "price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,
        "archived_at":null,"product_family":{"id":3026728,"handle":"eshop-subscribe","name":"eShopSubscribe"}}}
        """;

    /// <summary>An archived plan must never be offered to customers.</summary>
    public const string RetiredPlanJson = """
        {"product":{"id":7130995,"name":"Legacy Plan","handle":"legacy-plan","price_in_cents":9900,
        "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":"2025-01-01T00:00:00-05:00",
        "product_family":{"id":3026728,"handle":"eshop-subscribe","name":"eShopSubscribe"}}}
        """;

    public static string PlanListJson => $"[{ProPlanJson},{BasicPlanJson},{RetiredPlanJson}]";

    /// <summary>Note <c>unit_price</c> is a string in the specification, not a number.</summary>
    public const string MeteredComponentJson = """
        {"component":{"id":3062731,"name":"API Calls","handle":"api-call","kind":"metered_component",
        "pricing_scheme":"per_unit","unit_price":"0.01","unit_name":"call","product_family_id":3026728,
        "product_family_handle":"eshop-subscribe","archived":false}}
        """;

    public const string QuantityBasedComponentJson = """
        {"component":{"id":3062732,"name":"Seats","handle":"api-call","kind":"quantity_based_component",
        "pricing_scheme":"per_unit","unit_price":"5.00","unit_name":"seat","product_family_id":3026728,
        "product_family_handle":"eshop-subscribe","archived":false}}
        """;

    public const string CustomerJson = """
        {"customer":{"id":14543792,"first_name":"demouser","last_name":"eShopOnWeb",
        "email":"demouser@microsoft.com","reference":"demouser@microsoft.com",
        "created_at":"2026-07-01T10:20:55-04:00","updated_at":"2026-07-01T10:20:58-04:00"}}
        """;

    public static string SubscriptionJson(int id = 93462813,
        string state = "active",
        string planHandle = "eshop-pro",
        string planName = "Pro Plan",
        long priceInCents = 29900,
        bool cancelAtEndOfPeriod = false,
        string? nextProductHandle = null)
    {
        var cancelFlag = cancelAtEndOfPeriod ? "true" : "false";
        var nextPlan = nextProductHandle is null ? "null" : $"\"{nextProductHandle}\"";

        // Laid out one closing brace per line so no brace run collides with the raw-string
        // interpolation delimiters.
        return $$"""
            {
              "subscription": {
                "id": {{id}},
                "state": "{{state}}",
                "current_period_started_at": "2026-07-22T19:07:29+05:00",
                "current_period_ends_at": "2026-08-22T19:07:29+05:00",
                "cancel_at_end_of_period": {{cancelFlag}},
                "canceled_at": null,
                "product_price_in_cents": {{priceInCents}},
                "next_product_handle": {{nextPlan}},
                "customer": {
                  "id": 14543792,
                  "email": "demouser@microsoft.com",
                  "reference": "demouser@microsoft.com",
                  "first_name": "demouser",
                  "last_name": "eShopOnWeb"
                },
                "product": {
                  "id": 7130993,
                  "handle": "{{planHandle}}",
                  "name": "{{planName}}",
                  "price_in_cents": {{priceInCents}},
                  "interval": 1,
                  "interval_unit": "month",
                  "require_credit_card": false,
                  "product_family": {
                    "id": 3026728,
                    "handle": "eshop-subscribe",
                    "name": "eShopSubscribe"
                  }
                }
              }
            }
            """;
    }

    /// <summary>The specification types usage <c>quantity</c> as integer OR string.</summary>
    public const string UsageJson = """
        {"usage":{"id":138522957,"memo":"eShop API calls","created_at":"2026-07-25T10:05:32+05:00",
        "price_point_id":149416,"quantity":250,"component_id":3062731,"component_handle":"api-call",
        "subscription_id":93462813}}
        """;

    public const string UsageListJson = """
        [{"usage":{"id":178534642,"memo":"20","created_at":"2026-07-25T11:58:42+05:00","price_point_id":242632,
        "quantity":"20.5","component_id":3062731,"component_handle":"api-call","subscription_id":93462813}},
        {"usage":{"id":178534591,"memo":"10","created_at":"2026-07-26T11:58:29+05:00","price_point_id":242632,
        "quantity":10,"component_id":3062731,"component_handle":"api-call","subscription_id":93462813}}]
        """;

    /// <summary>A usage record dated before the current period must not count toward the period total.</summary>
    public const string UsageListSpanningPeriodsJson = """
        [{"usage":{"id":178534000,"memo":"last period","created_at":"2026-07-22T08:00:00+05:00",
        "price_point_id":242632,"quantity":900,"component_id":3062731,"component_handle":"api-call",
        "subscription_id":93462813}},
        {"usage":{"id":178534642,"memo":"this period","created_at":"2026-07-25T11:58:42+05:00",
        "price_point_id":242632,"quantity":"20.5","component_id":3062731,"component_handle":"api-call",
        "subscription_id":93462813}}]
        """;

    public const string MigrationPreviewJson = """
        {"migration":{"prorated_adjustment_in_cents":-29900,"charge_in_cents":3149,
        "payment_due_in_cents":0,"credit_applied_in_cents":-26751}}
        """;

    public const string DelayedCancelJson = """{"message":"Successfully initiated delayed cancellation."}""";

    public const string ErrorListJson = """{"errors":["This subscription is not eligible to be put on hold."]}""";

    public const string SingleErrorJson = """{"error":"Subscription must be active"}""";

    public const string ErrorMapJson = """{"errors":{"customer":"Email is invalid"}}""";
}
