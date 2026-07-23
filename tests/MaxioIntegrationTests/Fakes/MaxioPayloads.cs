namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Canned Maxio wire payloads mirroring the seeded sandbox (family <c>eshop-subscribe</c>, plans
/// <c>eshop-pro</c> $299/mo and <c>basic-plan</c> $29/mo, metered component <c>api-call</c> $0.01).
/// </summary>
/// <remarks>
/// The JSON is written out literally — snake_case keys, cents as integers, the price of the metered
/// unit as a decimal string — so a test fails if the client stops reading the real wire shape.
/// </remarks>
internal static class MaxioPayloads
{
    public const int FamilyId = 3_026_729;
    public const int ProProductId = 7_130_995;
    public const int BasicProductId = 7_130_996;
    public const int ComponentId = 3_062_732;
    public const int CustomerId = 42;
    public const int SubscriptionId = 100;
    public const string CustomerReference = "customer@microsoft.com";

    /// <summary>The period boundary in the canned subscription payloads, as a UTC instant.</summary>
    public static readonly DateTimeOffset PeriodEnd = new(2026, 8, 23, 4, 0, 0, TimeSpan.Zero);

    public const string EmptyList = "[]";

    public const string ProductFamilies = """
        [{"product_family":{"id":3026729,"name":"eShopSubscribe","handle":"eshop-subscribe","description":"eShopOnWeb Subscribe"}}]
        """;

    public const string Products = """
        [
          {"product":{"id":7130995,"name":"Pro Plan","handle":"eshop-pro","description":"Pro","price_in_cents":29900,
                      "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,
                      "product_family":{"id":3026729,"handle":"eshop-subscribe"}}},
          {"product":{"id":7130996,"name":"Basic Plan","handle":"basic-plan","description":"Basic","price_in_cents":2900,
                      "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,
                      "product_family":{"id":3026729,"handle":"eshop-subscribe"}}},
          {"product":{"id":9999,"name":"Retired Plan","handle":"retired-plan","price_in_cents":100,
                      "interval":1,"interval_unit":"month","archived_at":"2025-01-01T00:00:00-04:00",
                      "product_family":{"id":3026729,"handle":"eshop-subscribe"}}},
          {"product":{"id":8888,"name":"Handleless Plan","price_in_cents":100,"interval":1,"interval_unit":"month",
                      "product_family":{"id":3026729,"handle":"eshop-subscribe"}}}
        ]
        """;

    public const string MeteredComponents = """
        [{"component":{"id":3062732,"name":"API Calls","handle":"api-call","kind":"metered_component",
                       "unit_name":"api call","unit_price":"0.01","price_per_unit_in_cents":1,"pricing_scheme":"per_unit",
                       "product_family_id":3026729,"product_family_handle":"eshop-subscribe","archived":false}}]
        """;

    /// <summary>The same handle seeded with the wrong kind — the UC0 mistake UC2 must refuse to bill on.</summary>
    public const string QuantityBasedComponents = """
        [{"component":{"id":3062732,"name":"API Calls","handle":"api-call","kind":"quantity_based_component",
                       "unit_name":"api call","unit_price":"0.01","pricing_scheme":"per_unit",
                       "product_family_id":3026729,"product_family_handle":"eshop-subscribe","archived":false}}]
        """;

    public const string Customer = """
        {"customer":{"id":42,"first_name":"customer","last_name":"eShopOnWeb",
                     "email":"customer@microsoft.com","reference":"customer@microsoft.com"}}
        """;

    public const string Usage = """
        {"usage":{"id":9001,"quantity":5,"memo":"nightly batch","component_id":3062732,
                  "component_handle":"api-call","subscription_id":100,"created_at":"2026-07-23T10:00:00-04:00"}}
        """;

    /// <summary>Maxio echoes the quantity back as a decimal string on some sites.</summary>
    public const string UsageWithStringQuantity = """
        {"usage":{"id":9002,"quantity":"2.5","component_id":3062732,"component_handle":"api-call",
                  "subscription_id":100}}
        """;

    public const string SubscriptionComponent = """
        {"component":{"id":555,"component_id":3062732,"component_handle":"api-call","name":"API Calls",
                      "kind":"metered_component","unit_balance":42,"unit_name":"api call","subscription_id":100}}
        """;

    public const string MigrationPreview = """
        {"migration":{"prorated_adjustment_in_cents":23900,"charge_in_cents":24900,
                      "payment_due_in_cents":23900,"credit_applied_in_cents":1000}}
        """;

    public const string DelayedCancellation = """{"message":"Subscription will be canceled at the end of the period."}""";

    public const string NotFound = """{"error":"Not Found"}""";

    private const string SubscriptionTemplate = """
        {"subscription":{"id":<ID>,"state":"<STATE>","product_price_in_cents":<PRICE>,
          "current_period_ends_at":"2026-08-23T00:00:00-04:00","next_assessment_at":"2026-08-23T00:00:00-04:00",
          "cancel_at_end_of_period":<CANCEL_AT_END>,"delayed_cancel_at":<DELAYED_CANCEL_AT>,
          "customer":{"id":42,"reference":"customer@microsoft.com","email":"customer@microsoft.com"},
          "product":{"id":7130995,"handle":"<PRODUCT_HANDLE>","name":"Pro Plan","price_in_cents":<PRICE>}}}
        """;

    public static string Subscription(string state = "active",
        string productHandle = "eshop-pro",
        long productPriceInCents = 29_900,
        bool cancelAtEndOfPeriod = false,
        int id = SubscriptionId) =>
        SubscriptionTemplate
            .Replace("<ID>", id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("<STATE>", state)
            .Replace("<PRICE>", productPriceInCents.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("<CANCEL_AT_END>", cancelAtEndOfPeriod ? "true" : "false")
            .Replace("<DELAYED_CANCEL_AT>", cancelAtEndOfPeriod ? "\"2026-08-23T00:00:00-04:00\"" : "null")
            .Replace("<PRODUCT_HANDLE>", productHandle);

    public static string SubscriptionList(string state = "active") => $"[{Subscription(state)}]";
}
