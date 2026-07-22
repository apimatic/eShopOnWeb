using System.Globalization;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure;

/// <summary>
/// Provider-shaped JSON payloads. These use the provider's own wire names and magnitudes — prices in
/// cents on products and subscriptions, a decimal dollar string on a component's unit price — so a
/// regression in the integration's conversion or envelope handling shows up as a failing assertion.
/// </summary>
public static class BillingJson
{
    public static string ProductFamilyList(params (int Id, string Handle)[] families)
    {
        var items = families.Select(f =>
            $$$"""{"product_family":{"id":{{{f.Id}}},"name":"{{{f.Handle}}}","handle":"{{{f.Handle}}}","description":"seeded"}}""");

        return "[" + string.Join(",", items) + "]";
    }

    public static string Product(int id,
        string handle,
        string name,
        long priceInCents,
        string familyHandle = BillingTestHarness.ProductFamilyHandle,
        bool requireCreditCard = false,
        string? archivedAt = null)
    {
        var archived = archivedAt is null ? "null" : $"\"{archivedAt}\"";

        return $$"""
        {
          "id": {{id}},
          "name": "{{name}}",
          "handle": "{{handle}}",
          "description": "{{name}} description",
          "price_in_cents": {{priceInCents}},
          "interval": 1,
          "interval_unit": "month",
          "require_credit_card": {{(requireCreditCard ? "true" : "false")}},
          "taxable": false,
          "archived_at": {{archived}},
          "product_family": { "id": 3026729, "handle": "{{familyHandle}}", "name": "{{familyHandle}}" }
        }
        """;
    }

    public static string ProductEnvelope(string product) => $$"""{"product":{{product}}}""";

    public static string ProductList(params string[] products)
        => "[" + string.Join(",", products.Select(p => $$"""{"product":{{p}}}""")) + "]";

    public static string Component(int id,
        string handle,
        string kind = "metered_component",
        string unitPrice = "0.01",
        string familyHandle = BillingTestHarness.ProductFamilyHandle,
        bool archived = false)
        => $$"""
        {
          "component": {
            "id": {{id}},
            "name": "API Calls",
            "handle": "{{handle}}",
            "kind": "{{kind}}",
            "pricing_scheme": "per_unit",
            "unit_name": "call",
            "unit_price": "{{unitPrice}}",
            "price_per_unit_in_cents": 1,
            "archived": {{(archived ? "true" : "false")}},
            "product_family_id": 3026729,
            "product_family_handle": "{{familyHandle}}",
            "product_family_name": "eShopSubscribe"
          }
        }
        """;

    public static string Customer(int id, string reference, string email = "demouser@microsoft.com")
        => $$"""
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

    public static string Subscription(int id,
        string state = "active",
        string planHandle = "eshop-pro",
        string planName = "Pro Plan",
        long productPriceInCents = 29900,
        string? currentPeriodEndsAt = "2026-08-22T00:00:00-04:00",
        bool cancelAtEndOfPeriod = false,
        string? delayedCancelAt = null,
        string? nextProductHandle = null,
        int customerId = 501,
        string customerReference = "demouser@microsoft.com")
    {
        var periodEnd = currentPeriodEndsAt is null ? "null" : $"\"{currentPeriodEndsAt}\"";
        var delayed = delayedCancelAt is null ? "null" : $"\"{delayedCancelAt}\"";
        var nextHandle = nextProductHandle is null ? "null" : $"\"{nextProductHandle}\"";

        return $$"""
        {
          "id": {{id}},
          "state": "{{state}}",
          "product_price_in_cents": {{productPriceInCents}},
          "total_revenue_in_cents": {{productPriceInCents}},
          "current_period_ends_at": {{periodEnd}},
          "next_assessment_at": {{periodEnd}},
          "cancel_at_end_of_period": {{(cancelAtEndOfPeriod ? "true" : "false")}},
          "delayed_cancel_at": {{delayed}},
          "next_product_handle": {{nextHandle}},
          "product": {
            "id": 7130995,
            "handle": "{{planHandle}}",
            "name": "{{planName}}",
            "price_in_cents": {{productPriceInCents}},
            "interval": 1,
            "interval_unit": "month"
          },
          "customer": { "id": {{customerId}}, "reference": "{{customerReference}}", "email": "{{customerReference}}" }
        }
        """;
    }

    public static string SubscriptionEnvelope(string subscription) => $$"""{"subscription":{{subscription}}}""";

    public static string SubscriptionList(params string[] subscriptions)
        => "[" + string.Join(",", subscriptions.Select(s => $$"""{"subscription":{{s}}}""")) + "]";

    public static string Usage(long id, int quantity, string? memo = null, string componentHandle = "api-call")
    {
        var memoJson = memo is null ? "null" : $"\"{memo}\"";

        return $$"""
        {
          "usage": {
            "id": {{id}},
            "quantity": {{quantity}},
            "memo": {{memoJson}},
            "created_at": "2026-07-22T10:00:00-04:00",
            "component_id": 3062732,
            "component_handle": "{{componentHandle}}",
            "subscription_id": 1001
          }
        }
        """;
    }

    public static string SubscriptionComponent(int componentId, int unitBalance)
        => $$"""
        {
          "component": {
            "component_id": {{componentId}},
            "component_handle": "api-call",
            "name": "API Calls",
            "kind": "metered_component",
            "unit_balance": {{unitBalance}},
            "pricing_scheme": "per_unit",
            "enabled": true
          }
        }
        """;

    public static string MigrationPreview(long proratedAdjustmentInCents,
        long chargeInCents,
        long paymentDueInCents,
        long creditAppliedInCents)
        => $$"""
        {
          "migration": {
            "prorated_adjustment_in_cents": {{proratedAdjustmentInCents}},
            "charge_in_cents": {{chargeInCents}},
            "payment_due_in_cents": {{paymentDueInCents}},
            "credit_applied_in_cents": {{creditAppliedInCents}}
          }
        }
        """;

    public static string Errors(params string[] messages)
        => $$"""{"errors":[{{string.Join(",", messages.Select(m => $"\"{m}\""))}}]}""";

    public static string DelayedCancellation(string message)
        => $$"""{"message":"{{message}}"}""";

    public static string NotFound() => """{"error":"Not Found"}""";

    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
