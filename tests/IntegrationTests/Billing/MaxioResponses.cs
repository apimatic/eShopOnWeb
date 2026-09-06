#nullable enable

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>
/// Maxio response bodies, written with the provider's own wire names (snake_case), so the tests
/// exercise the SDK's real deserialization rather than a convenient shape.
/// </summary>
public static class MaxioResponses
{
    public const string TwoProducts = """
        [
          { "product": { "id": 1, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900,
                         "interval": 1, "interval_unit": "month", "require_credit_card": false,
                         "initial_charge_in_cents": 0, "archived_at": null,
                         "product_family": { "id": 9, "handle": "test-family", "name": "Test Family" } } },
          { "product": { "id": 2, "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900,
                         "interval": 1, "interval_unit": "month", "require_credit_card": false,
                         "initial_charge_in_cents": 0, "archived_at": null,
                         "product_family": { "id": 9, "handle": "test-family", "name": "Test Family" } } }
        ]
        """;

    public const string ProductsIncludingArchived = """
        [
          { "product": { "id": 1, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900,
                         "interval": 1, "interval_unit": "month", "require_credit_card": false,
                         "initial_charge_in_cents": 0, "archived_at": null } },
          { "product": { "id": 3, "handle": "retired-plan", "name": "Retired", "price_in_cents": 100,
                         "archived_at": "2020-01-01T00:00:00Z" } }
        ]
        """;

    public const string Customer = """
        { "customer": { "id": 42, "reference": "eshoponweb-shopper", "email": "shopper@example.com" } }
        """;

    public const string NoSubscriptions = "[]";

    public const string ActiveProSubscription = """
        [
          { "subscription": { "id": 777, "state": "active",
                              "reference": "eshoponweb-shopper--eshop-pro",
                              "product_price_in_cents": 29900,
                              "current_period_started_at": "2026-09-06T00:00:00Z",
                              "current_period_ends_at": "2026-10-06T00:00:00Z",
                              "next_assessment_at": "2026-10-06T00:00:00Z",
                              "activated_at": "2026-09-06T00:00:00Z",
                              "product": { "handle": "eshop-pro", "name": "Pro Plan",
                                           "interval": 1, "interval_unit": "month" } } }
        ]
        """;

    public static string CanceledProSubscription(string reference) => """
        [
          { "subscription": { "id": 500, "state": "canceled",
                              "reference": "@REFERENCE@",
                              "product_price_in_cents": 29900,
                              "canceled_at": "2026-08-01T00:00:00Z",
                              "product": { "handle": "eshop-pro", "name": "Pro Plan" } } }
        ]
        """.Replace("@REFERENCE@", reference);

    public const string CreatedSubscription = """
        { "subscription": { "id": 900, "state": "active",
                            "reference": "eshoponweb-shopper--eshop-pro",
                            "product_price_in_cents": 29900,
                            "next_assessment_at": "2026-10-06T00:00:00Z",
                            "product": { "handle": "eshop-pro", "name": "Pro Plan",
                                         "interval": 1, "interval_unit": "month" } } }
        """;

    public const string FoundSubscription = """
        { "subscription": { "id": 901, "state": "active",
                            "reference": "eshoponweb-shopper--eshop-pro",
                            "product_price_in_cents": 29900,
                            "product": { "handle": "eshop-pro", "name": "Pro Plan" } } }
        """;

    /// <summary>The 422 the sandbox really returns when a card-less signup is billed automatically.</summary>
    public const string NoPaymentMethodError = """
        { "errors": ["No payment method was on file for the $299.00 balance"] }
        """;

    public const string DuplicateCustomerError = """
        { "errors": {} }
        """;
}
