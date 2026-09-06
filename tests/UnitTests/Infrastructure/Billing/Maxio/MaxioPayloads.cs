namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// Response bodies shaped like the ones the Maxio sandbox actually returns, trimmed to the fields this
/// integration reads.
/// </summary>
public static class MaxioPayloads
{
    public const string TwoProductsOneArchived = """
    [
      {
        "product": {
          "id": 7126957,
          "name": "Pro Plan",
          "handle": "eshop-pro",
          "description": "Everything, monthly",
          "price_in_cents": 29900,
          "interval": 1,
          "interval_unit": "month",
          "require_credit_card": false,
          "archived_at": null,
          "product_family": { "id": 3023074, "handle": "eshop-subscribe", "name": "eShop Subscribe" }
        }
      },
      {
        "product": {
          "id": 7126958,
          "name": "Basic Plan",
          "handle": "basic-plan",
          "price_in_cents": 2900,
          "interval": 1,
          "interval_unit": "month",
          "require_credit_card": true,
          "archived_at": null,
          "product_family": { "id": 3023074, "handle": "eshop-subscribe", "name": "eShop Subscribe" }
        }
      },
      {
        "product": {
          "id": 7126959,
          "name": "Retired Plan",
          "handle": "retired-plan",
          "price_in_cents": 100,
          "interval": 1,
          "interval_unit": "month",
          "archived_at": "2025-01-01T00:00:00-05:00",
          "product_family": { "id": 3023074, "handle": "eshop-subscribe", "name": "eShop Subscribe" }
        }
      }
    ]
    """;

    public const string NoProducts = "[]";

    public const string Customer = """
    {
      "customer": {
        "id": 60251234,
        "first_name": "Demouser",
        "last_name": "Customer",
        "email": "demouser@microsoft.com",
        "reference": "eshoponweb-03563e80f5c66ca727b65f6d4b321923"
      }
    }
    """;

    public const string CustomerNotFound = """{ "errors": ["Customer not found"] }""";

    public const string NoSubscriptions = "[]";

    public const string ActiveProSubscriptionList = "[" + ActiveProSubscriptionBody + "]";

    public const string ActiveProSubscription = ActiveProSubscriptionBody;

    private const string ActiveProSubscriptionBody = """
    {
      "subscription": {
        "id": 94211648,
        "state": "active",
        "product_price_in_cents": 29900,
        "current_period_started_at": "2026-09-06T20:22:33-04:00",
        "current_period_ends_at": "2026-10-06T20:22:33-04:00",
        "next_assessment_at": "2026-10-06T20:22:33-04:00",
        "created_at": "2026-09-06T20:22:33-04:00",
        "payment_collection_method": "remittance",
        "product": {
          "id": 7126957,
          "name": "Pro Plan",
          "handle": "eshop-pro",
          "price_in_cents": 29900,
          "interval": 1,
          "interval_unit": "month"
        },
        "customer": {
          "id": 60251234,
          "email": "demouser@microsoft.com",
          "reference": "eshoponweb-03563e80f5c66ca727b65f6d4b321923"
        }
      }
    }
    """;

    public const string CanceledProSubscriptionList = """
    [
      {
        "subscription": {
          "id": 94200001,
          "state": "canceled",
          "created_at": "2026-01-06T20:22:33-04:00",
          "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" }
        }
      }
    ]
    """;

    public const string NoPaymentMethodError = """
    { "errors": ["No payment method was on file for the $299.00 balance"] }
    """;

    public const string UnauthorizedError = """{ "error": "Unauthorized" }""";
}
