namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// Response bodies shaped like the ones Maxio returns, trimmed to the fields the integration reads.
/// </summary>
public static class MaxioPayloads
{
    public static string Products(params string[] products) => $"[{string.Join(",", products)}]";

    public static string Product(string handle, string name, long priceInCents,
        bool requireCreditCard = false, string? archivedAt = null) => $$"""
        {
          "product": {
            "id": 7130999,
            "name": "{{name}}",
            "handle": "{{handle}}",
            "description": null,
            "price_in_cents": {{priceInCents}},
            "interval": 1,
            "interval_unit": "month",
            "require_credit_card": {{(requireCreditCard ? "true" : "false")}},
            "archived_at": {{(archivedAt is null ? "null" : $"\"{archivedAt}\"")}},
            "trial_interval": null,
            "trial_interval_unit": null,
            "product_price_point_handle": "uuid:1079cad8",
            "product_price_point_name": "Original",
            "product_family": { "id": 3026731, "name": "Demo Subscriptions", "handle": "demo-subscriptions" }
          }
        }
        """;

    public static string Customer(long id, string reference, string email = "shopper@example.com") => $$"""
        {
          "customer": {
            "id": {{id}},
            "first_name": "Shopper",
            "last_name": "eShopOnWeb",
            "email": "{{email}}",
            "reference": "{{reference}}"
          }
        }
        """;

    public static string Subscriptions(params string[] subscriptions) => $"[{string.Join(",", subscriptions)}]";

    public static string Subscription(long id, string state, string planHandle, long priceInCents = 29900,
        long customerId = 98840116, string createdAt = "2026-09-06T20:56:59+05:00") => $$"""
        {
          "subscription": {
            "id": {{id}},
            "state": "{{state}}",
            "reference": "eshop-shopper-{{planHandle}}",
            "balance_in_cents": 0,
            "product_price_in_cents": {{priceInCents}},
            "current_period_started_at": "{{createdAt}}",
            "current_period_ends_at": "2026-10-06T20:56:59+05:00",
            "next_assessment_at": "2026-10-06T20:56:59+05:00",
            "activated_at": "{{createdAt}}",
            "canceled_at": null,
            "created_at": "{{createdAt}}",
            "customer": { "id": {{customerId}}, "reference": "eshop-shopper", "email": "shopper@example.com" },
            "product": {
              "id": 7130999,
              "name": "Pro Plan",
              "handle": "{{planHandle}}",
              "price_in_cents": {{priceInCents}},
              "interval": 1,
              "interval_unit": "month",
              "product_price_point_handle": "uuid:1079cad8"
            }
          }
        }
        """;

    public static string Errors(params string[] messages) =>
        $"{{\"errors\":[{string.Join(",", messages.Select(m => $"\"{m}\""))}]}}";
}
