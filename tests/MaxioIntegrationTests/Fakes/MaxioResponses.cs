namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Response bodies shaped exactly like the ones the live Maxio sandbox returns, so the parsing
/// these tests exercise is the parsing production performs.
/// </summary>
public static class MaxioResponses
{
    public const string ProductFamilies = """
        [{"product_family":{"id":3026730,"name":"eShopSubscribe","description":null,
        "handle":"eshop-subscribe","accounting_code":null,"archived_at":null}}]
        """;

    /// <summary>Pro Plan at $299.00 and Basic Plan at $29.00, both priced in integer cents.</summary>
    public const string ProductsInFamily = """
        [{"product":{"id":7130998,"name":"Basic Plan","handle":"basic-plan","description":"Entry plan",
        "price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,
        "archived_at":null,"product_family":{"id":3026730,"handle":"eshop-subscribe","name":"eShopSubscribe"}}},
        {"product":{"id":7130997,"name":"Pro Plan","handle":"eshop-pro","description":"Everything included",
        "price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,
        "archived_at":null,"product_family":{"id":3026730,"handle":"eshop-subscribe","name":"eShopSubscribe"}}}]
        """;

    public const string ProductsInFamilyWithArchived = """
        [{"product":{"id":7130997,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,
        "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,
        "product_family":{"id":3026730,"handle":"eshop-subscribe"}}},
        {"product":{"id":7130996,"name":"Retired Plan","handle":"retired-plan","price_in_cents":9900,
        "interval":1,"interval_unit":"month","require_credit_card":false,
        "archived_at":"2026-01-05T10:00:00-05:00","product_family":{"id":3026730,"handle":"eshop-subscribe"}}}]
        """;

    public const string ProProduct = """
        {"product":{"id":7130997,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,
        "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,
        "product_family":{"id":3026730,"handle":"eshop-subscribe","name":"eShopSubscribe"}}}
        """;

    public const string ArchivedProduct = """
        {"product":{"id":7130996,"name":"Retired Plan","handle":"retired-plan","price_in_cents":9900,
        "interval":1,"interval_unit":"month","archived_at":"2026-01-05T10:00:00-05:00",
        "product_family":{"id":3026730,"handle":"eshop-subscribe"}}}
        """;

    /// <summary>A plan that lives in a different product family than the one configured.</summary>
    public const string ForeignFamilyProduct = """
        {"product":{"id":9999999,"name":"Someone Else's Plan","handle":"eshop-pro","price_in_cents":100000,
        "interval":1,"interval_unit":"month","archived_at":null,
        "product_family":{"id":4444444,"handle":"other-family"}}}
        """;

    public const string Customer = """
        {"customer":{"id":97883340,"reference":"shopper@example.com","email":"shopper@example.com",
        "first_name":"shopper","last_name":"eShopOnWeb"}}
        """;

    public const string ActiveSubscription = """
        {"subscription":{"id":93491347,"state":"active","balance_in_cents":29900,
        "product_price_in_cents":29900,"current_period_started_at":"2026-07-23T20:21:57+05:00",
        "current_period_ends_at":"2026-08-23T20:21:57+05:00","next_assessment_at":"2026-08-23T20:21:57+05:00",
        "canceled_at":null,"cancel_at_end_of_period":false,"delayed_cancel_at":null,"next_product_handle":null,
        "product":{"id":7130997,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900},
        "customer":{"id":97883340,"reference":"shopper@example.com","email":"shopper@example.com"}}}
        """;

    public const string OnHoldSubscription = """
        {"subscription":{"id":93491347,"state":"on_hold","previous_state":"active",
        "balance_in_cents":29900,"product_price_in_cents":29900,
        "product":{"id":7130997,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900},
        "customer":{"id":97883340,"reference":"shopper@example.com"}}}
        """;

    public const string CanceledSubscription = """
        {"subscription":{"id":93491347,"state":"canceled","previous_state":"active",
        "balance_in_cents":0,"product_price_in_cents":2900,"canceled_at":"2026-07-23T20:23:10+05:00",
        "cancel_at_end_of_period":false,
        "product":{"id":7130998,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900},
        "customer":{"id":97883340,"reference":"shopper@example.com"}}}
        """;

    /// <summary>What Maxio reports once a cancellation has been deferred to the period end.</summary>
    public const string PendingCancellationSubscription = """
        {"subscription":{"id":93491347,"state":"active","balance_in_cents":2900,
        "product_price_in_cents":2900,"cancel_at_end_of_period":true,
        "delayed_cancel_at":"2026-08-23T20:23:00+05:00",
        "product":{"id":7130998,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900},
        "customer":{"id":97883340,"reference":"shopper@example.com"}}}
        """;

    /// <summary>A subscription with a plan change scheduled for the next renewal.</summary>
    public const string DelayedPlanChangeSubscription = """
        {"subscription":{"id":93491347,"state":"active","balance_in_cents":29900,
        "product_price_in_cents":29900,"next_product_handle":"basic-plan",
        "product":{"id":7130997,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900},
        "customer":{"id":97883340,"reference":"shopper@example.com"}}}
        """;

    public const string SubscriptionList = """
        [{"subscription":{"id":93491347,"state":"active","balance_in_cents":29900,
        "product_price_in_cents":29900,"current_period_ends_at":"2026-08-23T20:21:57+05:00",
        "product":{"id":7130997,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900},
        "customer":{"id":97883340,"reference":"shopper@example.com"}}}]
        """;

    public const string EmptyList = "[]";

    public const string MeteredComponent = """
        {"component":{"id":3062733,"name":"API Calls","handle":"api-call","kind":"metered_component",
        "pricing_scheme":"per_unit","unit_price":"0.01","unit_name":"api call","archived":false}}
        """;

    /// <summary>A quantity-based component — the mis-seed UC0 warns about.</summary>
    public const string QuantityBasedComponent = """
        {"component":{"id":3062733,"name":"API Calls","handle":"api-call",
        "kind":"quantity_based_component","pricing_scheme":"per_unit","unit_price":"0.01",
        "unit_name":"api call","archived":false}}
        """;

    public const string UsageRecorded = """
        {"usage":{"id":3633945529,"memo":"probe usage","quantity":150,"component_id":3062733,
        "component_handle":"api-call","subscription_id":93491347}}
        """;

    /// <summary>unit_balance is the period-to-date total; allocated_quantity is null for metered.</summary>
    public const string SubscriptionComponentWithBalance = """
        {"component":{"component_id":3062733,"component_handle":"api-call","kind":"metered_component",
        "unit_balance":200,"allocated_quantity":null}}
        """;

    /// <summary>Downgrading from $299 to $29 mid-period: a credit, and nothing to pay today.</summary>
    public const string MigrationPreview = """
        {"migration":{"prorated_adjustment_in_cents":-29900,"charge_in_cents":3100,
        "payment_due_in_cents":0,"credit_applied_in_cents":-26800}}
        """;

    public const string MigratedSubscription = """
        {"subscription":{"id":93491347,"state":"active","balance_in_cents":3100,
        "product_price_in_cents":2900,"current_period_ends_at":"2026-08-23T20:21:57+05:00",
        "product":{"id":7130998,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900},
        "customer":{"id":97883340,"reference":"shopper@example.com"}}}
        """;

    public const string DelayedCancelAccepted = """
        {"message":"This subscription is scheduled to be canceled at the end of the current period"}
        """;

    // --- Error shapes, all of which Maxio really uses ---

    public const string ErrorArray = """
        {"errors":["Subscription must be active"]}
        """;

    public const string ErrorArrayMultiple = """
        {"errors":["Bank routing number: cannot be blank.","Bank account number: cannot be blank."]}
        """;

    /// <summary>Cancelling an already-cancelled subscription answers with a singular "error".</summary>
    public const string ErrorSingular = """
        {"error":"The subscription is already canceled"}
        """;

    /// <summary>Creating a customer answers with a field map.</summary>
    public const string ErrorFieldMap = """
        {"errors":{"customer":"can't be blank"}}
        """;

    public const string UnauthorizedText = "HTTP Basic: Access denied.";
}
