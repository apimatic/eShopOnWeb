namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Response bodies shaped exactly as the Maxio API returns them (maxio-spec/openapi.yaml), so the
/// tests exercise real parsing rather than a convenient approximation.
/// </summary>
public static class MaxioPayloads
{
    public const int ProId = 7130995;
    public const int BasicId = 7130996;
    public const int ComponentId = 3062732;

    /// <summary>Two live plans plus one archived plan, which must be filtered out.</summary>
    public const string ProductList = """
        [
          {"product":{"id":7130995,"name":"Pro Plan","handle":"eshop-pro","description":"Everything, monthly",
            "price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,
            "archived_at":null}},
          {"product":{"id":7130996,"name":"Basic Plan","handle":"basic-plan","description":null,
            "price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,
            "archived_at":null}},
          {"product":{"id":7130997,"name":"Retired Plan","handle":"retired-plan","description":null,
            "price_in_cents":9900,"interval":1,"interval_unit":"month","require_credit_card":true,
            "archived_at":"2026-01-01T00:00:00-05:00"}}
        ]
        """;

    /// <summary>The metered component's unit_price is a decimal string in dollars, not cents.</summary>
    public const string ComponentList = """
        [
          {"component":{"id":3062732,"name":"API Calls","handle":"api-call","kind":"metered_component",
            "unit_name":"api call","pricing_scheme":"per_unit","unit_price":"0.01"}},
          {"component":{"id":3062733,"name":"Seats","handle":"seats","kind":"quantity_based_component",
            "unit_name":"seat","pricing_scheme":"per_unit","unit_price":"12.50"}}
        ]
        """;

    public const string Customer = """
        {"customer":{"id":88001,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com",
          "first_name":"demouser","last_name":"microsoft.com"}}
        """;

    public static string Subscription(int id = 15236915, string state = "active",
        string planHandle = "eshop-pro", string planName = "Pro Plan", int planPriceInCents = 29900,
        bool cancelAtEndOfPeriod = false) => $$"""
        {"subscription":{"id":{{id}},"state":"{{state}}",
          "current_period_ends_at":"2026-08-22T14:48:10-05:00",
          "activated_at":"2026-07-22T14:48:12-05:00",
          "cancel_at_end_of_period":{{(cancelAtEndOfPeriod ? "true" : "false")}},
          "delayed_cancel_at":null,"automatically_resume_at":null,
          "customer":{"id":88001,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com",
            "first_name":"demouser","last_name":"microsoft.com"},
          "product":{"id":7130995,"name":"{{planName}}","handle":"{{planHandle}}",
            "price_in_cents":{{planPriceInCents}},"interval":1,"interval_unit":"month"
          }
        }
        }
        """;

    public static string SubscriptionList(params string[] subscriptions) =>
        "[" + string.Join(",", subscriptions) + "]";

    public const string Usage = """
        {"usage":{"id":138522957,"memo":"Order 42 placed","created_at":"2026-07-22T10:05:32-06:00",
          "price_point_id":149416,"quantity":3,"component_id":3062732,"component_handle":"api-call",
          "subscription_id":15236915}}
        """;

    public static string SubscriptionComponent(string unitBalance) => $$"""
        {"component":{"component_id":3062732,"subscription_id":15236915,"name":"API Calls",
          "kind":"metered_component","unit_name":"api call","component_handle":"api-call",
          "unit_balance":{{unitBalance}},"pricing_scheme":"per_unit","enabled":true
          }
        }
        """;

    public const string MigrationPreview = """
        {"migration":{"prorated_adjustment_in_cents":-1250,"charge_in_cents":29900,
          "payment_due_in_cents":28650,"credit_applied_in_cents":0}}
        """;

    public const string DelayedCancellation = """
        {"message":"This subscription will be canceled at the end of the period"}
        """;

    /// <summary>A 422 with the array error shape (Error-List-Response).</summary>
    public const string ErrorList = """
        {"errors":["Product: could not be found.","Subscription must be active"]}
        """;

    /// <summary>A 422 with the object-map error shape (Customer-Error-Response).</summary>
    public const string ErrorMap = """
        {"errors":{"customer":"can't be blank"}}
        """;

    /// <summary>A 422 with the single-error shape (Single-Error-Response).</summary>
    public const string SingleError = """
        {"error":"The subscription is already canceled"}
        """;
}
