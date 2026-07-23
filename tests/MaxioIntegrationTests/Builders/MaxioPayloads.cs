namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Response bodies shaped exactly as the Maxio OpenAPI specification defines them, including the
/// envelope keys and the integer-cents money representation.
/// </summary>
public static class MaxioPayloads
{
    public const string PRO_PLAN_CENTS = "29900";
    public const string BASIC_PLAN_CENTS = "2900";

    public static string Product(int id, string handle, string name, string priceInCents,
        string? archivedAt = null, bool requireCreditCard = false)
    {
        var archived = archivedAt is null ? "null" : $"\"{archivedAt}\"";

        return $$"""
        {
          "id": {{id}},
          "name": "{{name}}",
          "handle": "{{handle}}",
          "price_in_cents": {{priceInCents}},
          "interval": 1,
          "interval_unit": "month",
          "require_credit_card": {{(requireCreditCard ? "true" : "false")}},
          "archived_at": {{archived}},
          "product_family": { "id": 3023074, "name": "eShopSubscribe", "handle": "eshop-subscribe" }
        }
        """;
    }

    public static string ProductList(params string[] products)
    {
        return "[" + string.Join(",", products.Select(product => $"{{\"product\":{product}}}")) + "]";
    }

    public static string ProductEnvelope(string product) => $"{{\"product\":{product}}}";

    public static string Component(int id, string handle, string kind, string unitPrice)
    {
        return $$"""
        {
          "component": {
            "id": {{id}},
            "name": "API Calls",
            "handle": "{{handle}}",
            "pricing_scheme": "per_unit",
            "unit_price": "{{unitPrice}}",
            "product_family_id": 3023074,
            "kind": "{{kind}}",
            "archived": false
          }
        }
        """;
    }

    public static string Customer(int id, string reference, string email)
    {
        return $$"""
        {
          "customer": {
            "id": {{id}},
            "first_name": "Demo",
            "last_name": "User",
            "email": "{{email}}",
            "reference": "{{reference}}"
          }
        }
        """;
    }

    public static string Subscription(int id, string state, string productHandle, string productName,
        string productPriceInCents, string currentPeriodEndsAt = "2026-08-23T12:00:00-05:00",
        bool cancelAtEndOfPeriod = false, string? delayedCancelAt = null, string? nextProductHandle = null,
        int customerId = 55501, string customerReference = "demouser@microsoft.com")
    {
        var delayed = delayedCancelAt is null ? "null" : $"\"{delayedCancelAt}\"";
        var next = nextProductHandle is null ? "null" : $"\"{nextProductHandle}\"";

        return $$"""
        {
          "id": {{id}},
          "state": "{{state}}",
          "balance_in_cents": 0,
          "product_price_in_cents": {{productPriceInCents}},
          "current_period_ends_at": "{{currentPeriodEndsAt}}",
          "cancel_at_end_of_period": {{(cancelAtEndOfPeriod ? "true" : "false")}},
          "delayed_cancel_at": {{delayed}},
          "next_product_handle": {{next}},
          "customer": { "id": {{customerId}}, "reference": "{{customerReference}}", "email": "{{customerReference}}" },
          "product": {
            "id": 7126957,
            "name": "{{productName}}",
            "handle": "{{productHandle}}",
            "price_in_cents": {{productPriceInCents}},
            "interval": 1,
            "interval_unit": "month",
            "require_credit_card": false,
            "archived_at": null
          }
        }
        """;
    }

    public static string SubscriptionEnvelope(string subscription) => $"{{\"subscription\":{subscription}}}";

    public static string SubscriptionList(params string[] subscriptions)
    {
        return "[" + string.Join(",", subscriptions.Select(item => $"{{\"subscription\":{item}}}")) + "]";
    }

    public static string Usage(long id, int subscriptionId, int componentId, string componentHandle,
        string quantityJson, string memo)
    {
        return $$"""
        {
          "usage": {
            "id": {{id}},
            "memo": "{{memo}}",
            "created_at": "2026-07-23T10:05:32-06:00",
            "price_point_id": 149416,
            "quantity": {{quantityJson}},
            "component_id": {{componentId}},
            "component_handle": "{{componentHandle}}",
            "subscription_id": {{subscriptionId}}
          }
        }
        """;
    }

    public static string SubscriptionComponent(int componentId, string componentHandle, string unitBalance)
    {
        return $$"""
        {
          "component": {
            "component_id": {{componentId}},
            "subscription_id": 15236915,
            "component_handle": "{{componentHandle}}",
            "kind": "metered_component",
            "name": "API Calls",
            "unit_balance": {{unitBalance}},
            "unit_name": "api call"
          }
        }
        """;
    }

    public static string MigrationPreview(string proratedAdjustmentInCents, string chargeInCents,
        string paymentDueInCents, string creditAppliedInCents)
    {
        return $$"""
        {
          "migration": {
            "prorated_adjustment_in_cents": {{proratedAdjustmentInCents}},
            "charge_in_cents": {{chargeInCents}},
            "payment_due_in_cents": {{paymentDueInCents}},
            "credit_applied_in_cents": {{creditAppliedInCents}}
          }
        }
        """;
    }

    /// <summary>The specification's list-of-messages error shape.</summary>
    public static string ErrorList(params string[] messages)
    {
        return "{\"errors\":[" + string.Join(",", messages.Select(message => $"\"{message}\"")) + "]}";
    }

    /// <summary>The specification's field-to-message map error shape.</summary>
    public static string ErrorMap(string field, string message) => $"{{\"errors\":{{\"{field}\":\"{message}\"}}}}";
}
