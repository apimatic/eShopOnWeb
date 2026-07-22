using System.Globalization;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Maxio-shaped response payloads. Money is written the way the provider writes it — plan and
/// proration amounts in cents, component unit prices as decimal text — so the tests exercise the
/// integration's real unit conversion rather than a convenient fiction.
/// </summary>
public static class MaxioJson
{
    public static string ProductFamilyList(int id = 3026730, string handle = MaxioTestHarness.ProductFamilyHandle) =>
        $$"""
        [
          { "product_family": { "id": {{id}}, "name": "eShopSubscribe", "handle": "{{handle}}",
                                "description": "Recurring plans", "created_at": "2026-01-05T10:00:00-05:00" } },
          { "product_family": { "id": 9999999, "name": "Unrelated", "handle": "something-else" } }
        ]
        """;

    /// <param name="priceInCents">Written to <c>price_in_cents</c> exactly as Maxio would.</param>
    public static string Product(
        int id = 7130997,
        string handle = MaxioTestHarness.DefaultPlanHandle,
        string name = "Pro Plan",
        long priceInCents = 29_900L,
        bool requireCreditCard = false,
        bool requestCreditCard = true,
        string? archivedAt = null,
        int interval = 1,
        string intervalUnit = "month") =>
        $$"""
        {
          "id": {{id}},
          "name": "{{name}}",
          "handle": "{{handle}}",
          "description": "{{name}} for eShopOnWeb Subscribe.",
          "price_in_cents": {{priceInCents}},
          "interval": {{interval}},
          "interval_unit": "{{intervalUnit}}",
          "require_credit_card": {{Bool(requireCreditCard)}},
          "request_credit_card": {{Bool(requestCreditCard)}},
          "archived_at": {{Nullable(archivedAt)}},
          "product_family": { "id": 3026730, "handle": "{{MaxioTestHarness.ProductFamilyHandle}}", "name": "eShopSubscribe" }
        }
        """;

    public static string ProductResponse(string product) => $$"""{ "product": {{product}} }""";

    public static string ProductList(params string[] products) =>
        "[" + string.Join(",", products.Select(ProductResponse)) + "]";

    public static string Customer(int id = 55001, string reference = "demouser@microsoft.com") =>
        $$"""
        {
          "id": {{id}},
          "reference": "{{reference}}",
          "email": "{{reference}}",
          "first_name": "demouser",
          "last_name": "Customer"
        }
        """;

    public static string CustomerResponse(string customer) => $$"""{ "customer": {{customer}} }""";

    public static string Subscription(
        int id = 88001,
        string state = "active",
        int customerId = 55001,
        string customerReference = "demouser@microsoft.com",
        string planHandle = MaxioTestHarness.DefaultPlanHandle,
        long planPriceInCents = 29_900L,
        string? canceledAt = null,
        string? delayedCancelAt = null,
        bool cancelAtEndOfPeriod = false,
        string? nextProductHandle = null) =>
        $$"""
        {
          "id": {{id}},
          "state": "{{state}}",
          "current_period_started_at": "2026-07-01T00:00:00-04:00",
          "current_period_ends_at": "2026-08-01T00:00:00-04:00",
          "next_assessment_at": "2026-08-01T00:00:00-04:00",
          "canceled_at": {{Nullable(canceledAt)}},
          "delayed_cancel_at": {{Nullable(delayedCancelAt)}},
          "cancel_at_end_of_period": {{Bool(cancelAtEndOfPeriod)}},
          "next_product_handle": {{Nullable(nextProductHandle)}},
          "customer": { "id": {{customerId}}, "reference": "{{customerReference}}", "email": "{{customerReference}}" },
          "product": { "id": 7130997, "handle": "{{planHandle}}", "name": "Pro Plan",
                       "price_in_cents": {{planPriceInCents}}, "interval": 1, "interval_unit": "month" }
        }
        """;

    public static string SubscriptionResponse(string subscription) => $$"""{ "subscription": {{subscription}} }""";

    public static string SubscriptionList(params string[] subscriptions) =>
        "[" + string.Join(",", subscriptions.Select(SubscriptionResponse)) + "]";

    /// <param name="unitPrice">Decimal text, the way Maxio sends component unit prices.</param>
    public static string Component(
        int id = 3062733,
        string handle = MaxioTestHarness.MeteredComponentHandle,
        string kind = "metered_component",
        string unitPrice = "0.01",
        string productFamilyHandle = MaxioTestHarness.ProductFamilyHandle,
        string? archivedAt = null) =>
        $$"""
        {
          "component": {
            "id": {{id}},
            "name": "API Calls",
            "handle": "{{handle}}",
            "kind": "{{kind}}",
            "unit_name": "call",
            "unit_price": "{{unitPrice}}",
            "pricing_scheme": "per_unit",
            "product_family_handle": "{{productFamilyHandle}}",
            "archived_at": {{Nullable(archivedAt)}}
          }
        }
        """;

    public static string UsageResponse(
        long id = 991001,
        int quantity = 5,
        string memo = "eShopOnWeb order 42",
        int componentId = 3062733,
        int subscriptionId = 88001) =>
        $$"""
        {
          "usage": {
            "id": {{id}},
            "memo": "{{memo}}",
            "created_at": "2026-07-22T12:00:00-04:00",
            "quantity": {{quantity}},
            "component_id": {{componentId}},
            "component_handle": "{{MaxioTestHarness.MeteredComponentHandle}}",
            "subscription_id": {{subscriptionId}}
          }
        }
        """;

    public static string SubscriptionComponentResponse(int unitBalance = 17, int componentId = 3062733) =>
        $$"""
        {
          "component": {
            "component_id": {{componentId}},
            "component_handle": "{{MaxioTestHarness.MeteredComponentHandle}}",
            "subscription_id": 88001,
            "name": "API Calls",
            "kind": "metered_component",
            "unit_name": "call",
            "unit_balance": {{unitBalance}},
            "enabled": true
          }
        }
        """;

    /// <param name="proratedAdjustmentInCents">All migration-preview money is in cents.</param>
    public static string MigrationPreviewResponse(
        long proratedAdjustmentInCents = -24_900L,
        long chargeInCents = 29_900L,
        long paymentDueInCents = 5_000L,
        long creditAppliedInCents = 24_900L) =>
        $$"""
        {
          "migration": {
            "prorated_adjustment_in_cents": {{proratedAdjustmentInCents}},
            "charge_in_cents": {{chargeInCents}},
            "payment_due_in_cents": {{paymentDueInCents}},
            "credit_applied_in_cents": {{creditAppliedInCents}}
          }
        }
        """;

    public static string DelayedCancellationResponse(string message = "Subscription scheduled for cancellation.") =>
        $$"""{ "message": "{{message}}" }""";

    /// <summary>The 422 shape Maxio uses for validation failures.</summary>
    public static string ErrorList(params string[] errors) =>
        $$"""{ "errors": [{{string.Join(",", errors.Select(e => $"\"{e}\""))}}] }""";

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Nullable(string? value) =>
        value is null ? "null" : $"\"{value}\"";

    public static string Cents(long cents) => cents.ToString(CultureInfo.InvariantCulture);
}
