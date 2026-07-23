using System.Globalization;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Response bodies copied from the shape the live Maxio Advanced Billing API actually returns, so
/// the tests exercise real parsing rather than a convenient invention.
/// </summary>
/// <remarks>
/// The bodies are plain raw strings — JSON is dense in braces, so values are substituted through
/// named placeholders rather than string interpolation.
/// </remarks>
public static class MaxioResponses
{
    public const long FamilyId = 3026731;
    public const long ProPlanId = 7130999;
    public const long BasicPlanId = 7131000;
    public const long ComponentId = 3062734;
    public const long CustomerId = 97882982;
    public const long SubscriptionId = 93491148;
    public const long UsageId = 3633939705;

    public const string FamilyPath = "/product_families.json";

    public static string ProductsPath => $"/product_families/{FamilyId}/products.json";

    public static string ComponentsPath => $"/product_families/{FamilyId}/components.json";

    public const string EmptyArray = "[]";

    public const string ProductFamilies = """
    [{"product_family":{"id":3026731,"name":"eShopSubscribe","handle":"eshop-subscribe","archived_at":null}}]
    """;

    /// <summary>The two demo plans: $299.00 and $29.00 per month, reported in cents.</summary>
    public const string Products = """
    [
      {"product":{"id":7131000,"name":"Basic Plan","handle":"basic-plan","description":null,
        "price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,
        "archived_at":null,"product_family":{"id":3026731,"handle":"eshop-subscribe"}}},
      {"product":{"id":7130999,"name":"Pro Plan","handle":"eshop-pro","description":null,
        "price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,
        "archived_at":null,"product_family":{"id":3026731,"handle":"eshop-subscribe"}}}
    ]
    """;

    /// <summary>A plan list where one plan has been archived and must not be offered.</summary>
    public const string ProductsWithArchived = """
    [
      {"product":{"id":7130999,"name":"Pro Plan","handle":"eshop-pro",
        "price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,
        "archived_at":null,"product_family":{"id":3026731,"handle":"eshop-subscribe"}}},
      {"product":{"id":7131274,"name":"Retired Plan","handle":"retired-plan",
        "price_in_cents":1500,"interval":1,"interval_unit":"month","require_credit_card":false,
        "archived_at":"2026-07-23T20:37:55+05:00","product_family":{"id":3026731,"handle":"eshop-subscribe"}}}
    ]
    """;

    /// <summary>The metered component: per-unit at $0.01, price reported as a decimal string.</summary>
    public const string MeteredComponents = """
    [{"component":{"id":3062734,"name":"API Calls","handle":"api-call","pricing_scheme":"per_unit",
      "unit_name":"api call","unit_price":"0.01","kind":"metered_component","archived":false}}]
    """;

    /// <summary>The same handle occupied by a quantity-based component — UC0 got the kind wrong.</summary>
    public const string QuantityBasedComponents = """
    [{"component":{"id":3062734,"name":"API Calls","handle":"api-call","pricing_scheme":"per_unit",
      "unit_name":"api call","unit_price":"0.01","kind":"quantity_based_component","archived":false}}]
    """;

    public const string Customer = """
    {"customer":{"id":97882982,"first_name":"Demo","last_name":"User",
      "email":"demouser@microsoft.com","reference":"demouser@microsoft.com"}}
    """;

    public const string Usage = """
    {"usage":{"id":3633939705,"memo":"probe usage","quantity":5,
      "component_id":3062734,"component_handle":"api-call","subscription_id":93491148}}
    """;

    /// <summary>
    /// A downgrade preview: the unused Pro remainder is credited (negative) and the Basic remainder
    /// is charged, leaving nothing due now.
    /// </summary>
    public const string MigrationPreview = """
    {"migration":{"prorated_adjustment_in_cents":-29900,"charge_in_cents":2905,
      "payment_due_in_cents":0,"credit_applied_in_cents":-26995}}
    """;

    /// <summary>
    /// What scheduling an end-of-period cancellation actually answers with — a bare confirmation,
    /// not the subscription.
    /// </summary>
    public const string DelayedCancelAcknowledgement = """
    {"message":"This subscription is scheduled to be canceled at the end of the current period"}
    """;

    public const string ErrorsArray = """
    {"errors":["Only subscriptions that are on hold can be resumed."]}
    """;

    public const string ErrorsObject = """
    {"errors":{"product_handle":["must be specified"]}}
    """;

    private const string SubscriptionTemplate = """
    {"subscription":{"id":93491148,"state":"@STATE@","balance_in_cents":29900,
      "product_price_in_cents":@PRICE@,
      "current_period_started_at":"2026-07-23T20:12:08+05:00",
      "current_period_ends_at":"2026-08-23T20:12:08+05:00",
      "next_assessment_at":"2026-08-23T20:12:08+05:00",
      "cancel_at_end_of_period":@CANCEL_EOP@,
      "delayed_cancel_at":@DELAYED_CANCEL@,
      "next_product_handle":@NEXT_PRODUCT@,
      "currency":"USD",
      "product":{"id":7130999,"name":"@PRODUCT_NAME@","handle":"@PRODUCT_HANDLE@",
        "price_in_cents":@PRICE@,"interval":1,"interval_unit":"month",
        "require_credit_card":false,"archived_at":null},
      "customer":{"id":97882982,"email":"demouser@microsoft.com","reference":"demouser@microsoft.com"}}}
    """;

    private const string SubscriptionComponentTemplate = """
    {"component":{"component_id":3062734,"component_handle":"api-call",
      "kind":"metered_component","unit_balance":@BALANCE@}}
    """;

    public static string Subscription(string state = "active",
        string productHandle = "eshop-pro",
        string productName = "Pro Plan",
        int productPriceInCents = 29900,
        bool cancelAtEndOfPeriod = false,
        string? delayedCancelAt = null,
        string? nextProductHandle = null) =>
        SubscriptionTemplate
            .Replace("@STATE@", state)
            .Replace("@PRICE@", productPriceInCents.ToString(CultureInfo.InvariantCulture))
            .Replace("@CANCEL_EOP@", cancelAtEndOfPeriod ? "true" : "false")
            .Replace("@DELAYED_CANCEL@", JsonStringOrNull(delayedCancelAt))
            .Replace("@NEXT_PRODUCT@", JsonStringOrNull(nextProductHandle))
            .Replace("@PRODUCT_NAME@", productName)
            .Replace("@PRODUCT_HANDLE@", productHandle);

    public static string SubscriptionList(string state = "active", string productHandle = "eshop-pro") =>
        $"[{Subscription(state, productHandle)}]";

    public static string SubscriptionComponent(int unitBalance) =>
        SubscriptionComponentTemplate.Replace("@BALANCE@", unitBalance.ToString(CultureInfo.InvariantCulture));

    private static string JsonStringOrNull(string? value) => value is null ? "null" : $"\"{value}\"";
}
