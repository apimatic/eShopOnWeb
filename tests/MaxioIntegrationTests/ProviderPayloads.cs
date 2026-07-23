namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Provider response bodies in the exact envelope shapes the Maxio API returns, so the tests
/// exercise real deserialisation rather than a convenient approximation of it.
/// </summary>
public static class ProviderPayloads
{
    /// <summary>Pro Plan at $299.00/month — 29900 cents.</summary>
    public const string ProPlan = """
        {"product": {
            "id": 7126957,
            "name": "Pro Plan",
            "handle": "eshop-pro",
            "description": "Full storefront access.",
            "price_in_cents": 29900,
            "interval": 1,
            "interval_unit": "month",
            "archived_at": null,
            "require_credit_card": false,
            "product_family": { "id": 3023074, "handle": "eshop-subscribe" }
        }}
        """;

    /// <summary>Basic Plan at $29.00/month — 2900 cents.</summary>
    public const string BasicPlan = """
        {"product": {
            "id": 7126958,
            "name": "Basic Plan",
            "handle": "basic-plan",
            "description": "Entry level access.",
            "price_in_cents": 2900,
            "interval": 1,
            "interval_unit": "month",
            "archived_at": null,
            "product_family": { "id": 3023074, "handle": "eshop-subscribe" }
        }}
        """;

    public const string ArchivedPlan = """
        {"product": {
            "id": 7126959,
            "name": "Retired Plan",
            "handle": "retired-plan",
            "price_in_cents": 9900,
            "interval": 1,
            "interval_unit": "month",
            "archived_at": "2025-01-01T00:00:00Z"
        }}
        """;

    public static string PlanList(params string[] planEnvelopes) => $"[{string.Join(",", planEnvelopes)}]";

    public const string EmptyList = "[]";

    public const string Customer = """
        {"customer": {
            "id": 5001,
            "first_name": "buyer",
            "last_name": "eShopOnWeb",
            "email": "buyer@example.com",
            "reference": "buyer@example.com"
        }}
        """;

    /// <summary>An active subscription on the Pro plan.</summary>
    public const string ActiveSubscription = """
        {"subscription": {
            "id": 90001,
            "state": "active",
            "current_period_ends_at": "2026-08-01T00:00:00Z",
            "next_assessment_at": "2026-08-01T00:00:00Z",
            "activated_at": "2026-07-01T00:00:00Z",
            "cancel_at_end_of_period": false,
            "product": {
                "id": 7126957,
                "name": "Pro Plan",
                "handle": "eshop-pro",
                "price_in_cents": 29900,
                "interval": 1,
                "interval_unit": "month"
            },
            "customer": { "id": 5001, "reference": "buyer@example.com" }
        }}
        """;

    public const string OnHoldSubscription = """
        {"subscription": {
            "id": 90001,
            "state": "on_hold",
            "on_hold_at": "2026-07-15T00:00:00Z",
            "product": { "id": 7126957, "handle": "eshop-pro", "price_in_cents": 29900 },
            "customer": { "id": 5001, "reference": "buyer@example.com" }
        }}
        """;

    public const string CanceledSubscription = """
        {"subscription": {
            "id": 90001,
            "state": "canceled",
            "canceled_at": "2026-07-20T00:00:00Z",
            "product": { "id": 7126957, "handle": "eshop-pro", "price_in_cents": 29900 },
            "customer": { "id": 5001, "reference": "buyer@example.com" }
        }}
        """;

    /// <summary>An end-of-period cancellation already scheduled.</summary>
    public const string PendingCancellationSubscription = """
        {"subscription": {
            "id": 90001,
            "state": "active",
            "cancel_at_end_of_period": true,
            "delayed_cancel_at": "2026-08-01T00:00:00Z",
            "current_period_ends_at": "2026-08-01T00:00:00Z",
            "product": { "id": 7126957, "handle": "eshop-pro", "price_in_cents": 29900 },
            "customer": { "id": 5001, "reference": "buyer@example.com" }
        }}
        """;

    /// <summary>A subscription moved to Basic, used to assert the plan-change outcome.</summary>
    public const string BasicSubscription = """
        {"subscription": {
            "id": 90001,
            "state": "active",
            "product": { "id": 7126958, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900 },
            "customer": { "id": 5001, "reference": "buyer@example.com" }
        }}
        """;

    /// <summary>A delayed plan change scheduled for the next renewal.</summary>
    public const string DelayedChangeSubscription = """
        {"subscription": {
            "id": 90001,
            "state": "active",
            "next_product_handle": "basic-plan",
            "current_period_ends_at": "2026-08-01T00:00:00Z",
            "product": { "id": 7126957, "handle": "eshop-pro", "price_in_cents": 29900 },
            "customer": { "id": 5001, "reference": "buyer@example.com" }
        }}
        """;

    public static string SubscriptionList(params string[] envelopes) => $"[{string.Join(",", envelopes)}]";

    /// <summary>The api-call component, metered, at $0.01/unit — 1 cent.</summary>
    public const string MeteredComponent = """
        {"component": {
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
        }}
        """;

    /// <summary>The same component reported without the explicit cents field, price only as a string.</summary>
    public const string MeteredComponentPriceAsString = """
        {"component": {
            "id": 3057195,
            "handle": "api-call",
            "kind": "metered_component",
            "pricing_scheme": "per_unit",
            "unit_price": "0.01",
            "product_family_handle": "eshop-subscribe"
        }}
        """;

    /// <summary>A component of the wrong kind, which must never be used to record usage.</summary>
    public const string QuantityBasedComponent = """
        {"component": {
            "id": 3057195,
            "handle": "api-call",
            "kind": "quantity_based_component",
            "pricing_scheme": "per_unit",
            "price_per_unit_in_cents": 1,
            "product_family_handle": "eshop-subscribe"
        }}
        """;

    /// <summary>A component belonging to a different product family.</summary>
    public const string ForeignFamilyComponent = """
        {"component": {
            "id": 3057195,
            "handle": "api-call",
            "kind": "metered_component",
            "price_per_unit_in_cents": 1,
            "product_family_handle": "some-other-family"
        }}
        """;

    public const string AcceptedUsage = """
        {"usage": {
            "id": 900123,
            "quantity": 25,
            "memo": "eShopOnWeb order 42",
            "component_id": 3057195,
            "component_handle": "api-call",
            "subscription_id": 90001,
            "created_at": "2026-07-23T10:00:00Z"
        }}
        """;

    /// <summary>The provider may report the accepted quantity as a string.</summary>
    public const string AcceptedUsageStringQuantity = """
        {"usage": {
            "id": 900124,
            "quantity": "7",
            "component_id": 3057195,
            "component_handle": "api-call",
            "subscription_id": 90001
        }}
        """;

    /// <summary>25 units accrued this period. The wire key is `component`, not `subscription_component`.</summary>
    public const string SubscriptionComponentUsage = """
        {"component": {
            "id": 88,
            "component_id": 3057195,
            "component_handle": "api-call",
            "name": "API Calls",
            "kind": "metered_component",
            "unit_balance": 25,
            "pricing_scheme": "per_unit",
            "subscription_id": 90001
        }}
        """;

    /// <summary>An upgrade preview: $299.00 charged, $15.00 credited, $284.00 due.</summary>
    public const string MigrationPreview = """
        {"migration": {
            "prorated_adjustment_in_cents": -1500,
            "charge_in_cents": 29900,
            "payment_due_in_cents": 28400,
            "credit_applied_in_cents": 1500
        }}
        """;

    public const string DelayedCancellationAccepted = """{"message": "Delayed cancellation scheduled."}""";

    /// <summary>The 422 validation shape most write operations return — a list of messages.</summary>
    public const string ValidationError = """{"errors": ["Product handle: is invalid."]}""";

    /// <summary>
    /// The 422 shape the customer operations declare, which is an object rather than the list every
    /// other operation uses.
    /// </summary>
    public const string CustomerValidationError = """{"errors": {"per_page": ["is invalid"]}}""";

    public const string NotFoundError = """{"error": "Not Found"}""";
}
