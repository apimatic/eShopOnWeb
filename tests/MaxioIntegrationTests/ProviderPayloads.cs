namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Canned Maxio wire payloads, written in the provider's own snake_case shape. Money arrives the way
/// Maxio sends it — <c>*_in_cents</c> fields in cents, <c>unit_price</c> as a decimal currency
/// string — which is what lets these tests pin the client's unit conversions.
/// </summary>
public static class ProviderPayloads
{
    public const string ProPlanProduct = """
        {
          "id": 7130999,
          "name": "Pro Plan",
          "handle": "eshop-pro",
          "description": "The eShopOnWeb Pro subscription.",
          "price_in_cents": 29900,
          "interval": 1,
          "interval_unit": "month",
          "require_credit_card": false,
          "taxable": false
        }
        """;

    public const string BasicPlanProduct = """
        {
          "id": 7131000,
          "name": "Basic Plan",
          "handle": "basic-plan",
          "description": "The eShopOnWeb Basic subscription.",
          "price_in_cents": 2900,
          "interval": 1,
          "interval_unit": "month",
          "require_credit_card": false,
          "taxable": false
        }
        """;

    public static string ProductResponse(string product) => $$"""{"product": {{product}} }""";

    public static string ProductList(params string[] products) =>
        "[" + string.Join(",", products.Select(ProductResponse)) + "]";

    public const string MeteredComponent = """
        {
          "id": 3062734,
          "name": "API Calls",
          "handle": "api-call",
          "kind": "metered_component",
          "unit_name": "API call",
          "unit_price": "0.01",
          "price_per_unit_in_cents": 1,
          "pricing_scheme": "per_unit",
          "product_family_id": 3026731,
          "product_family_handle": "eshop-subscribe",
          "archived": false
        }
        """;

    public const string QuantityBasedComponent = """
        {
          "id": 3062799,
          "name": "Seats",
          "handle": "api-call",
          "kind": "quantity_based_component",
          "unit_name": "seat",
          "unit_price": "5.00",
          "price_per_unit_in_cents": 500,
          "pricing_scheme": "per_unit",
          "product_family_handle": "eshop-subscribe"
        }
        """;

    public static string ComponentResponse(string component) => $$"""{"component": {{component}} }""";

    public const string Customer = """
        {
          "id": 5551212,
          "first_name": "demouser",
          "last_name": "microsoft",
          "email": "demouser@microsoft.com",
          "reference": "demouser@microsoft.com",
          "organization": null
        }
        """;

    public static string CustomerResponse(string customer) => $$"""{"customer": {{customer}} }""";

    /// <summary>An active subscription on the Pro plan, with a period end and next assessment date.</summary>
    public static string Subscription(string state = "active",
        string product = ProPlanProduct,
        bool cancelAtEndOfPeriod = false,
        string? nextProductHandle = null) => $$"""
        {
          "id": 90210,
          "state": "{{state}}",
          "current_period_started_at": "2026-07-01T00:00:00-04:00",
          "current_period_ends_at": "2026-08-01T00:00:00-04:00",
          "next_assessment_at": "2026-08-01T00:00:00-04:00",
          "cancel_at_end_of_period": {{(cancelAtEndOfPeriod ? "true" : "false")}},
          "next_product_handle": {{(nextProductHandle is null ? "null" : $"\"{nextProductHandle}\"")}},
          "product_price_in_cents": 29900,
          "customer": {{Customer}},
          "product": {{product}}
        }
        """;

    public static string SubscriptionResponse(string subscription) =>
        $$"""{"subscription": {{subscription}} }""";

    public static string SubscriptionList(params string[] subscriptions) =>
        "[" + string.Join(",", subscriptions.Select(SubscriptionResponse)) + "]";

    public static string UsageResponse(int quantity) => $$"""
        {
          "usage": {
            "id": 778899,
            "memo": "eShopOnWeb order 42",
            "created_at": "2026-07-22T10:00:00-04:00",
            "quantity": {{quantity}},
            "component_id": 3062734,
            "component_handle": "api-call",
            "subscription_id": 90210
          }
        }
        """;

    public static string SubscriptionComponentResponse(int unitBalance) => $$"""
        {
          "component": {
            "id": 3062734,
            "name": "API Calls",
            "kind": "metered_component",
            "unit_name": "API call",
            "enabled": true,
            "unit_balance": {{unitBalance}},
            "component_id": 3062734,
            "component_handle": "api-call",
            "subscription_id": 90210
          }
        }
        """;

    /// <summary>A migration preview. Every amount is in cents on the wire.</summary>
    public const string MigrationPreview = """
        {
          "migration": {
            "prorated_adjustment_in_cents": 24750,
            "charge_in_cents": 27000,
            "payment_due_in_cents": 24750,
            "credit_applied_in_cents": 2250
          }
        }
        """;

    public const string ProductFamily = """
        {
          "id": 3026731,
          "name": "eShopSubscribe",
          "handle": "eshop-subscribe",
          "description": "Recurring plans and metered add-ons."
        }
        """;

    public static string ProductFamilyResponse(string family) => $$"""{"product_family": {{family}} }""";

    public static string ProductFamilyList(params string[] families) =>
        "[" + string.Join(",", families.Select(ProductFamilyResponse)) + "]";

    public const string DelayedCancellationAccepted = """{"message": "Subscription will be canceled at the end of the period."}""";

    public const string ValidationErrors = """{"errors": ["Product handle is invalid.", "Coupon not found."]}""";
}
