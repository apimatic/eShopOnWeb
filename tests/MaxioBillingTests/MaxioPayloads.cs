using System.Globalization;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// The Maxio wire payloads these tests replay. Kept in one place so the shapes — and, critically, the
/// units Maxio uses (money in cents on products and migrations, decimal-dollar strings on components) —
/// are stated once and asserted against everywhere.
/// </summary>
/// <remarks>
/// Written as plain (non-interpolated) raw strings with <c>%TOKEN%</c> placeholders: JSON is dense in
/// braces, and interpolated raw strings make these payloads unreadable.
/// </remarks>
public static class MaxioPayloads
{
    public const int FamilyId = 3023074;
    public const int ProProductId = 7126957;
    public const int BasicProductId = 7126958;
    public const int ComponentId = 3057195;
    public const int CustomerId = 55001;
    public const int SubscriptionId = 99001;

    public const string CustomerReference = "demouser@microsoft.com";

    public const string EmptyList = "[]";

    public const string ProductFamilies = """
        [{"product_family":{"id":3023074,"name":"eShopSubscribe","handle":"eshop-subscribe"}}]
        """;

    /// <summary>$299.00/month and $29.00/month expressed the way Maxio expresses them: in cents.</summary>
    public const string Products = """
        [{"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","description":"Pro tier",
          "price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,
          "archived_at":null}},
         {"product":{"id":7126958,"name":"Basic Plan","handle":"basic-plan","description":"Basic tier",
          "price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,
          "archived_at":null}}]
        """;

    /// <summary>Includes an archived plan, which the integration must not offer.</summary>
    public const string ProductsWithArchived = """
        [{"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,
          "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null}},
         {"product":{"id":900001,"name":"Retired Plan","handle":"retired-plan","price_in_cents":9900,
          "interval":1,"interval_unit":"month","require_credit_card":false,
          "archived_at":"2024-01-31T10:00:00-04:00"}}]
        """;

    /// <summary>A plan that demands card capture, which the demo subscribe flow cannot satisfy.</summary>
    public const string ProductsRequiringPaymentMethod = """
        [{"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,
          "interval":1,"interval_unit":"month","require_credit_card":true,"archived_at":null}}]
        """;

    /// <summary>$0.01 per unit — Maxio publishes a component unit price as a decimal-dollars string.</summary>
    public const string MeteredComponents = """
        [{"component":{"id":3057195,"name":"API Calls","handle":"api-call","kind":"metered_component",
          "pricing_scheme":"per_unit","unit_price":"0.01","unit_name":"call","archived":false}}]
        """;

    /// <summary>The UC0 mis-seed: right handle, wrong kind.</summary>
    public const string QuantityBasedComponents = """
        [{"component":{"id":3057195,"name":"API Calls","handle":"api-call",
          "kind":"quantity_based_component","pricing_scheme":"per_unit","unit_price":"0.01",
          "unit_name":"call","archived":false}}]
        """;

    /// <summary>No decimal-dollars string; only the cents field, which is the documented fallback.</summary>
    public const string MeteredComponentsPricedInCentsOnly = """
        [{"component":{"id":3057195,"name":"API Calls","handle":"api-call","kind":"metered_component",
          "pricing_scheme":"per_unit","price_per_unit_in_cents":250,"unit_name":"call","archived":false}}]
        """;

    public const string Customer = """
        {"customer":{"id":55001,"first_name":"Demo","last_name":"User",
         "email":"demouser@microsoft.com","reference":"demouser@microsoft.com"}}
        """;

    public const string DelayedCancellationAccepted = """
        {"message":"Your subscription will be canceled at the end of the current billing period."}
        """;

    public const string ErrorList = """
        {"errors":["Payment method is required for this product."]}
        """;

    private const string SubscriptionTemplate = """
        {"subscription":{"id":99001,"state":"%STATE%",
         "current_period_started_at":"2026-07-01T00:00:00-04:00",
         "current_period_ends_at":"2026-08-01T00:00:00-04:00",
         "next_assessment_at":"2026-08-01T00:00:00-04:00",
         "product_price_in_cents":%PRICE%,
         "customer":{"id":55001,"first_name":"Demo","last_name":"User",
           "email":"demouser@microsoft.com","reference":"demouser@microsoft.com"},
         "product":{"id":7126957,"name":"Pro Plan","handle":"%HANDLE%","price_in_cents":%PRICE%,
           "interval":1,"interval_unit":"month","require_credit_card":false}}}
        """;

    /// <summary>A subscription in an arbitrary provider state, on an arbitrary plan.</summary>
    public static string Subscription(string state = "active", string productHandle = "eshop-pro",
        long productPriceInCents = 29900) =>
        SubscriptionTemplate
            .Replace("%STATE%", state)
            .Replace("%HANDLE%", productHandle)
            .Replace("%PRICE%", productPriceInCents.ToString(CultureInfo.InvariantCulture));

    public static string SubscriptionList(string state = "active", string productHandle = "eshop-pro") =>
        "[" + Subscription(state, productHandle) + "]";

    /// <summary>A subscription with a cancellation already scheduled for the end of the period.</summary>
    public const string SubscriptionPendingCancel = """
        {"subscription":{"id":99001,"state":"active",
         "current_period_started_at":"2026-07-01T00:00:00-04:00",
         "current_period_ends_at":"2026-08-01T00:00:00-04:00",
         "delayed_cancel_at":"2026-08-01T00:00:00-04:00","cancel_at_end_of_period":true,
         "customer":{"id":55001,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com"},
         "product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,
           "interval":1,"interval_unit":"month"}}}
        """;

    private const string UsageTemplate = """
        {"usage":{"id":%ID%,"quantity":%QUANTITY%,"memo":"%MEMO%",
         "created_at":"2026-07-23T09:30:00-04:00","component_id":3057195,
         "component_handle":"api-call","subscription_id":99001}}
        """;

    /// <summary>A usage record whose quantity came back as a JSON number.</summary>
    public static string Usage(long id, int quantity, string memo = "") =>
        UsageTemplate
            .Replace("%ID%", id.ToString(CultureInfo.InvariantCulture))
            .Replace("%QUANTITY%", quantity.ToString(CultureInfo.InvariantCulture))
            .Replace("%MEMO%", memo);

    /// <summary>A usage record whose quantity came back as a JSON string — the other half of the union.</summary>
    public static string UsageWithStringQuantity(long id, string quantity, string memo = "") =>
        UsageTemplate
            .Replace("%ID%", id.ToString(CultureInfo.InvariantCulture))
            .Replace("%QUANTITY%", "\"" + quantity + "\"")
            .Replace("%MEMO%", memo);

    private const string SubscriptionComponentTemplate = """
        {"component":{"id":3057195,"component_id":3057195,"component_handle":"api-call",
         "name":"API Calls","kind":"metered_component","unit_balance":%BALANCE%,
         "subscription_id":99001}}
        """;

    /// <summary>The subscription's metered accumulation for the current period.</summary>
    public static string SubscriptionComponent(int unitBalance) =>
        SubscriptionComponentTemplate.Replace("%BALANCE%", unitBalance.ToString(CultureInfo.InvariantCulture));

    private const string MigrationPreviewTemplate = """
        {"migration":{"prorated_adjustment_in_cents":%NET%,"charge_in_cents":%CHARGE%,
         "payment_due_in_cents":%NET%,"credit_applied_in_cents":%CREDIT%}}
        """;

    /// <summary>A migration preview. Maxio reports every one of these amounts in cents.</summary>
    public static string MigrationPreview(long chargeInCents, long creditAppliedInCents) =>
        MigrationPreviewTemplate
            .Replace("%CHARGE%", chargeInCents.ToString(CultureInfo.InvariantCulture))
            .Replace("%CREDIT%", creditAppliedInCents.ToString(CultureInfo.InvariantCulture))
            .Replace("%NET%", (chargeInCents - creditAppliedInCents).ToString(CultureInfo.InvariantCulture));
}
