using System.Globalization;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Response bodies shaped exactly like the ones in maxio-spec/openapi.yaml, so the tests exercise
/// the real wire format (integer cents, string unit prices, nested envelopes) rather than a
/// convenient approximation of it.
/// </summary>
internal static class MaxioPayloads
{
    public const string ProPlanProduct = """
    {"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","description":"Everything included",
      "price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,
      "archived_at":null,"product_family":{"id":3023074,"name":"eShopSubscribe","handle":"eshop-subscribe"}}}
    """;

    public const string BasicPlanProduct = """
    {"product":{"id":7126958,"name":"Basic Plan","handle":"basic-plan","description":"Starter",
      "price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,
      "archived_at":null,"product_family":{"id":3023074,"name":"eShopSubscribe","handle":"eshop-subscribe"}}}
    """;

    public const string ArchivedProduct = """
    {"product":{"id":7126000,"name":"Retired Plan","handle":"retired-plan",
      "price_in_cents":9900,"interval":1,"interval_unit":"month","require_credit_card":false,
      "archived_at":"2024-01-05T09:00:00-05:00","product_family":{"id":3023074,"handle":"eshop-subscribe"}}}
    """;

    public static readonly string ProductList = $"[{ProPlanProduct},{BasicPlanProduct},{ArchivedProduct}]";

    public const string MeteredComponent = """
    {"component":{"id":3057195,"name":"API Calls","handle":"api-call","pricing_scheme":"per_unit",
      "unit_name":"api call","unit_price":"0.01","product_family_id":3023074,
      "product_family_handle":"eshop-subscribe","kind":"metered_component","archived":false}}
    """;

    public const string QuantityBasedComponent = """
    {"component":{"id":3057196,"name":"Seats","handle":"api-call","pricing_scheme":"per_unit",
      "unit_name":"seat","unit_price":"5.0","product_family_id":3023074,
      "kind":"quantity_based_component","archived":false}}
    """;

    public const string TieredMeteredComponent = """
    {"component":{"id":3057197,"name":"API Calls","handle":"api-call","pricing_scheme":"tiered",
      "unit_name":"api call","unit_price":null,"product_family_id":3023074,
      "kind":"metered_component","archived":false}}
    """;

    public const string Customer = """
    {"customer":{"id":14714298,"first_name":"Demo","last_name":"User","email":"demo@microsoft.com",
      "reference":"demo@microsoft.com","created_at":"2024-01-01T10:00:00-05:00"}}
    """;

    public static string Subscription(string state = "active",
        string productHandle = "eshop-pro",
        int priceInCents = 29900,
        bool cancelAtEndOfPeriod = false,
        string? delayedCancelAt = null) => SubscriptionTemplate
            .Replace("<STATE>", state)
            .Replace("<HANDLE>", productHandle)
            .Replace("<PRICE>", priceInCents.ToString(CultureInfo.InvariantCulture))
            .Replace("<CANCEL_AT_END>", cancelAtEndOfPeriod ? "true" : "false")
            .Replace("<DELAYED_CANCEL_AT>", delayedCancelAt is null ? "null" : $"\"{delayedCancelAt}\"");

    private const string SubscriptionTemplate = """
    {"subscription":{"id":15236915,"state":"<STATE>","balance_in_cents":1250,
      "product_price_in_cents":<PRICE>,
      "current_period_ends_at":"2024-02-15T14:48:10-05:00",
      "next_assessment_at":"2024-02-15T14:48:10-05:00",
      "cancel_at_end_of_period":<CANCEL_AT_END>,
      "delayed_cancel_at":<DELAYED_CANCEL_AT>,
      "customer":{"id":14714298,"reference":"demo@microsoft.com","email":"demo@microsoft.com"},
      "product":{"id":7126957,"name":"Pro Plan","handle":"<HANDLE>","price_in_cents":<PRICE>,
        "interval":1,"interval_unit":"month","product_family":{"id":3023074,"handle":"eshop-subscribe"}}}}
    """;

    public const string Usage = """
    {"usage":{"id":138522957,"memo":"Order placed","created_at":"2024-01-20T10:05:32-06:00",
      "price_point_id":149416,"quantity":"25.0","component_id":3057195,
      "component_handle":"api-call","subscription_id":15236915}}
    """;

    public const string SubscriptionComponentWithBalance = """
    {"component":{"component_id":3057195,"component_handle":"api-call","subscription_id":15236915,
      "name":"API Calls","kind":"metered_component","unit_balance":175,"allocated_quantity":0}}
    """;

    public const string SubscriptionComponentWithoutBalance = """
    {"component":{"component_id":3057195,"component_handle":"api-call","subscription_id":15236915,
      "name":"API Calls","kind":"metered_component","unit_balance":null}}
    """;

    public const string UsageList = """
    [{"usage":{"id":1,"quantity":"20.0","component_id":3057195,"subscription_id":15236915}},
     {"usage":{"id":2,"quantity":5,"component_id":3057195,"subscription_id":15236915}}]
    """;

    public const string MigrationPreview = """
    {"migration":{"prorated_adjustment_in_cents":-1667,"charge_in_cents":29900,
      "payment_due_in_cents":28233,"credit_applied_in_cents":1667}}
    """;

    public const string DelayedCancellation = """{"message":"This subscription will be canceled"}""";

    public const string ValidationErrors = """{"errors":["Product: is invalid.","Quantity: must be positive."]}""";

    public const string CustomerReferenceTaken = """{"errors":{"customer":"reference: has already been taken"}}""";
}
