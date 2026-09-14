using System.Globalization;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Provider response bodies, in the wire shapes Maxio actually returns.
/// </summary>
internal static class MaxioPayloads
{
    public const string ProductFamiliesPath = "/product_families.json";
    public const string SitePath = "/site.json";
    public const string CustomerLookupPath = "/customers/lookup.json";
    public const string CreateCustomerPath = "/customers.json";
    public const string CreateSubscriptionPath = "/subscriptions.json";

    public static string ProductsPath(int familyId = MaxioTestHarness.ProductFamilyId) =>
        $"/product_families/{familyId.ToString(CultureInfo.InvariantCulture)}/products.json";

    public static string CustomerSubscriptionsPath(int customerId = MaxioTestHarness.CustomerId) =>
        $"/customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";

    /// <summary>A family list whose second entry is the one the configured handle names.</summary>
    public static string ProductFamilies(
        int matchingId = MaxioTestHarness.ProductFamilyId,
        string matchingHandle = MaxioTestHarness.ProductFamilyHandle) =>
        $$"""
        [
          { "product_family": { "id": 1, "handle": "some-other-family", "name": "Other" } },
          { "product_family": { "id": {{matchingId}}, "handle": "{{matchingHandle}}", "name": "eShop Subscribe" } }
        ]
        """;

    public static string Site(string currency = "USD", bool relationshipInvoicing = true) =>
        $$"""
        { "site": { "id": 1, "subdomain": "test-site", "currency": "{{currency}}",
                    "test": true, "relationship_invoicing_enabled": {{(relationshipInvoicing ? "true" : "false")}} } }
        """;

    public static string Products(bool requireCreditCard = false) =>
        $$"""
        [
          { "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan",
                         "description": "Everything, monthly", "price_in_cents": 29900,
                         "interval": 1, "interval_unit": "month", "initial_charge_in_cents": 0,
                         "require_credit_card": {{(requireCreditCard ? "true" : "false")}},
                         "request_credit_card": false, "archived_at": null } },
          { "product": { "id": 7126958, "handle": "basic-plan", "name": "Basic Plan",
                         "price_in_cents": 2900, "interval": 1, "interval_unit": "month",
                         "require_credit_card": false, "archived_at": null } },
          { "product": { "id": 7126959, "handle": "retired-plan", "name": "Retired Plan",
                         "price_in_cents": 100, "interval": 1, "interval_unit": "month",
                         "archived_at": "2020-01-01T00:00:00-05:00" } }
        ]
        """;

    public static string Customer(
        int id = MaxioTestHarness.CustomerId,
        string reference = MaxioTestHarness.CustomerReference) =>
        $$"""
        { "customer": { "id": {{id}}, "reference": "{{reference}}",
                        "email": "demouser@microsoft.com", "first_name": "Demouser", "last_name": "Customer" } }
        """;

    public static string NotFound() => """{ "error": "Customer not found" }""";

    public static string Subscription(
        int id = 94211243,
        string state = "active",
        string productHandle = "eshop-pro") =>
        $$"""
        { "subscription": { "id": {{id}}, "state": "{{state}}",
            "current_period_started_at": "2026-09-06T19:31:07-05:00",
            "current_period_ends_at": "2026-10-06T19:31:07-05:00",
            "next_assessment_at": "2026-10-06T19:31:07-05:00",
            "product_price_in_cents": 29900, "currency": "USD",
            "payment_collection_method": "remittance",
            "product": { "id": 7126957, "handle": "{{productHandle}}", "name": "Pro Plan", "price_in_cents": 29900 },
            "customer": { "id": {{MaxioTestHarness.CustomerId}}, "reference": "{{MaxioTestHarness.CustomerReference}}" } } }
        """;

    public static string SubscriptionList(
        int id = 94211243,
        string state = "active",
        string productHandle = "eshop-pro") =>
        $"[ {Subscription(id, state, productHandle)} ]";

    public const string EmptySubscriptionList = "[]";

    /// <summary>The 422 the provider returns when a signup balance cannot be collected.</summary>
    public const string NoPaymentMethodError =
        """{ "errors": ["No payment method was on file for the $299.00 balance"] }""";
}
