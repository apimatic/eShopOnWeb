namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>
/// Response bodies shaped like the ones the sandbox actually returns.
/// </summary>
internal static class MaxioFixtures
{
    public const string ProductFamilies = """
        [
          { "product_family": { "id": 3026729, "handle": "demo-subscriptions", "name": "Demo Subscriptions" } },
          { "product_family": { "id": 9999999, "handle": "some-other-family", "name": "Unrelated" } }
        ]
        """;

    public const string Products = """
        [
          { "product": { "id": 7126958, "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900,
                         "interval": 1, "interval_unit": "month", "require_credit_card": false } },
          { "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900,
                         "interval": 1, "interval_unit": "month", "require_credit_card": false } },
          { "product": { "id": 7126000, "handle": "retired-plan", "name": "Retired", "price_in_cents": 100,
                         "archived_at": "2026-01-01T00:00:00-05:00" } }
        ]
        """;

    public const string ProProduct = """
        { "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900,
                       "interval": 1, "interval_unit": "month", "require_credit_card": false } }
        """;

    public const string CardRequiredProduct = """
        { "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900,
                       "interval": 1, "interval_unit": "month", "require_credit_card": true } }
        """;

    /// <summary>A Relationship Invoicing site whose default collection method would fail a no-card signup.</summary>
    public const string Site = """
        { "site": { "id": 1, "subdomain": "test-site", "currency": "USD", "test": true,
                    "relationship_invoicing_enabled": true, "default_payment_collection_method": "automatic" } }
        """;

    public const string Customer = """
        { "customer": { "id": 98838161, "email": "demouser@microsoft.com", "first_name": "Demouser",
                        "last_name": "Customer", "reference": "eshoponweb-demouser-microsoft-com-03563e80" } }
        """;

    public const string NoSubscriptions = "[]";

    public const string ActiveProSubscription = """
        [
          { "subscription": { "id": 94209629, "state": "active",
                              "reference": "eshoponweb-demouser-microsoft-com-03563e80-eshop-pro",
                              "product_price_in_cents": 29900, "currency": "USD",
                              "current_period_started_at": "2026-09-06T15:06:27-05:00",
                              "current_period_ends_at": "2026-10-06T15:06:27-05:00",
                              "next_assessment_at": "2026-10-06T15:06:27-05:00",
                              "product": { "handle": "eshop-pro", "name": "Pro Plan" } } }
        ]
        """;

    public const string CanceledProSubscription = """
        [
          { "subscription": { "id": 94209600, "state": "canceled",
                              "reference": "eshoponweb-demouser-microsoft-com-03563e80-eshop-pro",
                              "product_price_in_cents": 29900, "currency": "USD",
                              "canceled_at": "2026-08-01T10:00:00-05:00",
                              "product": { "handle": "eshop-pro", "name": "Pro Plan" } } }
        ]
        """;

    public const string CreatedProSubscription = """
        { "subscription": { "id": 94209629, "state": "active",
                            "reference": "eshoponweb-demouser-microsoft-com-03563e80-eshop-pro",
                            "product_price_in_cents": 29900, "currency": "USD",
                            "current_period_started_at": "2026-09-06T15:06:27-05:00",
                            "current_period_ends_at": "2026-10-06T15:06:27-05:00",
                            "next_assessment_at": "2026-10-06T15:06:27-05:00",
                            "product": { "handle": "eshop-pro", "name": "Pro Plan" } } }
        """;

    public const string NotFound = """{ "errors": ["Not found"] }""";

    public const string NoPaymentMethodOnFile = """{ "errors": ["No payment method was on file for the $299.00 balance"] }""";
}
