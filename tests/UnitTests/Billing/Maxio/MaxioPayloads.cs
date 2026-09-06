using System.Globalization;

namespace Microsoft.eShopWeb.UnitTests.Billing.Maxio;

/// <summary>
/// Response bodies shaped exactly like the ones the Maxio sandbox returns, trimmed to the fields the
/// integration reads plus a few it deliberately ignores.
/// </summary>
/// <remarks>
/// The templates carry <c>$name$</c> placeholders rather than string interpolation so the JSON stays
/// readable: interpolated raw strings would need brace escaping on every object literal.
/// </remarks>
public static class MaxioPayloads
{
    public const string ProductsPath = "/product_families/handle:demo-plans/products.json";
    public const string CustomerLookupPath = "/customers/lookup.json";
    public const string CustomersPath = "/customers.json";
    public const string SubscriptionLookupPath = "/subscriptions/lookup.json";
    public const string SubscriptionsPath = "/subscriptions.json";

    public const string ReferenceTakenError = """{"errors":["Reference: must be unique - that value has been taken."]}""";

    public const string NoPaymentMethodError = """{"errors":["No payment method was on file for the $299.00 balance"]}""";

    public static string CustomerSubscriptionsPath(long customerId) =>
        $"/customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";

    public static string Products() => """
        [
          {"product":{"id":7130998,"handle":"basic-plan","name":"Basic Plan","description":null,
            "price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,
            "request_credit_card":true,"taxable":false,"trial_price_in_cents":null,"trial_interval":null,
            "trial_interval_unit":null,"archived_at":null,"version_number":1,
            "product_family":{"id":3026730,"name":"Demo Plans","handle":"demo-plans"}}},
          {"product":{"id":7130997,"handle":"eshop-pro","name":"Pro Plan","description":null,
            "price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,
            "request_credit_card":true,"taxable":false,"trial_price_in_cents":null,"trial_interval":null,
            "trial_interval_unit":null,"archived_at":null,"version_number":1,
            "product_family":{"id":3026730,"name":"Demo Plans","handle":"demo-plans"}}},
          {"product":{"id":7130996,"handle":"retired-plan","name":"Retired Plan","description":null,
            "price_in_cents":100,"interval":1,"interval_unit":"month","require_credit_card":false,
            "taxable":false,"archived_at":"2026-01-01T00:00:00+05:00",
            "product_family":{"id":3026730,"name":"Demo Plans","handle":"demo-plans"}}}
        ]
        """;

    private const string CustomerTemplate = """
        {"customer":{"id":$id$,"first_name":"demouser","last_name":"eShopOnWeb",
          "email":"demouser@microsoft.com","reference":"$reference$","verified":false,
          "created_at":"2026-09-06T19:11:52+05:00"}}
        """;

    private const string SubscriptionTemplate = """
        {"subscription":{"id":$id$,"state":"$state$","reference":"$reference$","currency":"USD",
          "balance_in_cents":$price$,"product_price_in_cents":$price$,
          "payment_collection_method":"remittance",
          "current_period_started_at":"$createdAt$","current_period_ends_at":"2026-10-06T19:11:52+05:00",
          "next_assessment_at":"2026-10-06T19:11:52+05:00","activated_at":"$createdAt$",
          "trial_ended_at":null,"canceled_at":null,"expires_at":null,"created_at":"$createdAt$",
          "product":{"id":7130997,"handle":"$productHandle$","name":"Pro Plan","price_in_cents":$price$,
            "interval":1,"interval_unit":"month",
            "product_family":{"id":3026730,"name":"Demo Plans","handle":"demo-plans"}},
          "customer":{"id":98839435,"reference":"eshoponweb:demouser@microsoft.com",
            "email":"demouser@microsoft.com"}}}
        """;

    public static string Customer(long id = 98839435, string reference = "eshoponweb:demouser@microsoft.com") =>
        CustomerTemplate
            .Replace("$id$", id.ToString(CultureInfo.InvariantCulture))
            .Replace("$reference$", reference);

    public static string Subscription(
        long id = 94211097,
        string state = "active",
        string reference = "eshoponweb:demouser@microsoft.com:eshop-pro",
        string productHandle = "eshop-pro",
        long priceInCents = 29900,
        string createdAt = "2026-09-06T19:11:52+05:00") =>
        SubscriptionTemplate
            .Replace("$id$", id.ToString(CultureInfo.InvariantCulture))
            .Replace("$state$", state)
            .Replace("$reference$", reference)
            .Replace("$productHandle$", productHandle)
            .Replace("$price$", priceInCents.ToString(CultureInfo.InvariantCulture))
            .Replace("$createdAt$", createdAt);

    public static string SubscriptionList(params string[] subscriptionEnvelopes) =>
        "[" + string.Join(",", subscriptionEnvelopes) + "]";
}
