using System.Globalization;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Wire-shaped payloads matching what Maxio actually returns, so the tests exercise real
/// deserialization and mapping rather than a convenient fiction.
/// </summary>
/// <remarks>
/// Note the deliberate unit mismatch baked into these fixtures, because it is exactly what the
/// integration has to get right: plan prices arrive as integer <c>*_in_cents</c>, while a
/// component's <c>unit_price</c> arrives as a decimal string in dollars.
/// </remarks>
public static class MaxioJson
{
    /// <summary>Pro Plan: $299.00/month, arriving as 29900 cents.</summary>
    public const long ProPlanCents = 29_900;

    /// <summary>Basic Plan: $29.00/month, arriving as 2900 cents.</summary>
    public const long BasicPlanCents = 2_900;

    public static string Product(
        int id = 7130997,
        string handle = BillingClientFixture.DefaultPlanHandle,
        string name = "Pro Plan",
        long priceInCents = ProPlanCents,
        bool archived = false,
        bool requireCreditCard = false,
        string familyHandle = BillingClientFixture.FamilyHandle) =>
        $$"""
        {
          "id": {{id}},
          "name": "{{name}}",
          "handle": "{{handle}}",
          "description": "{{name}} description",
          "price_in_cents": {{priceInCents}},
          "interval": 1,
          "interval_unit": "month",
          "require_credit_card": {{Bool(requireCreditCard)}},
          "request_credit_card": false,
          "archived_at": {{(archived ? "\"2024-01-01T00:00:00-05:00\"" : "null")}},
          "product_family": { "id": 3026730, "name": "eShopSubscribe", "handle": "{{familyHandle}}" }
        }
        """;

    public static string ProductEnvelope(string product) => $$"""{ "product": {{product}} }""";

    public static string ProductList(params string[] products)
        => "[" + string.Join(",", products.Select(p => $$"""{ "product": {{p}} }""")) + "]";

    public static string Customer(
        int id = 51234,
        string reference = "demouser@microsoft.com",
        string email = "demouser@microsoft.com") =>
        $$"""
        {
          "id": {{id}},
          "first_name": "Demo",
          "last_name": "User",
          "email": "{{email}}",
          "reference": "{{reference}}",
          "created_at": "2024-05-01T10:00:00-04:00"
        }
        """;

    public static string CustomerEnvelope(string customer) => $$"""{ "customer": {{customer}} }""";

    public static string Subscription(
        int id = 900001,
        string state = "active",
        string planHandle = BillingClientFixture.DefaultPlanHandle,
        string planName = "Pro Plan",
        long planPriceInCents = ProPlanCents,
        long balanceInCents = 0,
        bool cancelAtEndOfPeriod = false,
        string? delayedCancelAt = null,
        string? nextProductHandle = null,
        int customerId = 51234,
        string customerReference = "demouser@microsoft.com") =>
        $$"""
        {
          "id": {{id}},
          "state": "{{state}}",
          "balance_in_cents": {{balanceInCents}},
          "current_period_started_at": "2024-06-01T00:00:00-04:00",
          "current_period_ends_at": "2024-07-01T00:00:00-04:00",
          "next_assessment_at": "2024-07-01T00:00:00-04:00",
          "activated_at": "2024-06-01T00:00:00-04:00",
          "canceled_at": null,
          "delayed_cancel_at": {{(delayedCancelAt is null ? "null" : $"\"{delayedCancelAt}\"")}},
          "cancel_at_end_of_period": {{Bool(cancelAtEndOfPeriod)}},
          "next_product_handle": {{(nextProductHandle is null ? "null" : $"\"{nextProductHandle}\"")}},
          "product": {{Product(handle: planHandle, name: planName, priceInCents: planPriceInCents)}},
          "customer": {{Customer(id: customerId, reference: customerReference)}}
        }
        """;

    public static string SubscriptionEnvelope(string subscription) => $$"""{ "subscription": {{subscription}} }""";

    public static string SubscriptionList(params string[] subscriptions)
        => "[" + string.Join(",", subscriptions.Select(s => $$"""{ "subscription": {{s}} }""")) + "]";

    /// <summary>
    /// A component. <paramref name="unitPrice"/> is a decimal STRING in dollars, which is how Maxio
    /// reports a per-unit price — unlike plan prices, it is not in cents.
    /// </summary>
    public static string Component(
        int id = 3062733,
        string handle = BillingClientFixture.ComponentHandle,
        string name = "API Calls",
        string kind = "metered_component",
        string unitPrice = "0.01",
        bool archived = false) =>
        $$"""
        {
          "id": {{id}},
          "name": "{{name}}",
          "handle": "{{handle}}",
          "kind": "{{kind}}",
          "unit_name": "call",
          "unit_price": "{{unitPrice}}",
          "pricing_scheme": "per_unit",
          "product_family_id": 3026730,
          "product_family_handle": "{{BillingClientFixture.FamilyHandle}}",
          "archived": {{Bool(archived)}},
          "archived_at": {{(archived ? "\"2024-01-01T00:00:00-05:00\"" : "null")}}
        }
        """;

    public static string ComponentEnvelope(string component) => $$"""{ "component": {{component}} }""";

    public static string Usage(
        long id = 555001,
        int quantity = 1,
        string? memo = "eShopOnWeb order 42",
        int componentId = 3062733,
        int subscriptionId = 900001) =>
        $$"""
        {
          "usage": {
            "id": {{id}},
            "memo": {{(memo is null ? "null" : $"\"{memo}\"")}},
            "created_at": "2024-06-10T12:00:00-04:00",
            "quantity": {{quantity}},
            "component_id": {{componentId}},
            "component_handle": "{{BillingClientFixture.ComponentHandle}}",
            "subscription_id": {{subscriptionId}}
          }
        }
        """;

    /// <summary>The subscription's line item for a component; usage accrues to <c>unit_balance</c>.</summary>
    public static string SubscriptionComponent(int unitBalance, int componentId = 3062733, int subscriptionId = 900001) =>
        $$"""
        {
          "component": {
            "id": 77001,
            "component_id": {{componentId}},
            "component_handle": "{{BillingClientFixture.ComponentHandle}}",
            "subscription_id": {{subscriptionId}},
            "kind": "metered_component",
            "unit_name": "call",
            "unit_balance": {{unitBalance}},
            "pricing_scheme": "per_unit",
            "enabled": true
          }
        }
        """;

    public static string MigrationPreview(
        long proratedAdjustmentInCents = 24_000,
        long chargeInCents = 27_000,
        long creditAppliedInCents = 3_000,
        long paymentDueInCents = 24_000) =>
        $$"""
        {
          "migration": {
            "prorated_adjustment_in_cents": {{proratedAdjustmentInCents}},
            "charge_in_cents": {{chargeInCents}},
            "credit_applied_in_cents": {{creditAppliedInCents}},
            "payment_due_in_cents": {{paymentDueInCents}}
          }
        }
        """;

    /// <summary>
    /// The error-list shape most operations return: <c>errors</c> is an array of strings.
    /// </summary>
    public static string Errors(params string[] messages)
        => $$"""{ "errors": [{{string.Join(",", messages.Select(m => $"\"{m}\""))}}] }""";

    /// <summary>
    /// The customer endpoints return a different 422 shape: <c>errors</c> is an OBJECT keyed by
    /// field name, not an array. Getting this wrong is what makes a catch block silently miss.
    /// </summary>
    public static string CustomerErrors(string field = "reference", params string[] messages)
        => $$"""{ "errors": { "{{field}}": [{{string.Join(",", messages.Select(m => $"\"{m}\""))}}] } }""";

    public static string DelayedCancellation(string message = "Subscription 900001 scheduled for cancellation")
        => $$"""{ "message": "{{message}}" }""";

    private static string Bool(bool value) => value ? "true" : "false";

    public static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
