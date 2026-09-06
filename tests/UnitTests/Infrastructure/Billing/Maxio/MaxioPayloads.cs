namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// Response bodies shaped like the ones the Maxio sandbox returns, trimmed to the fields the
/// integration reads.
/// </summary>
internal static class MaxioPayloads
{
    public const string Site = """
        {"site":{"id":93063,"name":"Test","subdomain":"test-site","currency":"USD","relationship_invoicing_enabled":true,"test":true}}
        """;

    /// <summary>Two live plans plus one archived plan that must not be offered.</summary>
    public const string ProductFamilyProducts = """
        [
          {"product":{"id":7130999,"name":"Pro Plan","handle":"eshop-pro","description":"Everything in Basic, plus priority support","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"taxable":false,"archived_at":null,"product_family":{"id":3026731,"name":"eShopSubscribe","handle":"demo-plans"}}},
          {"product":{"id":7131000,"name":"Basic Plan","handle":"basic-plan","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,"taxable":false,"archived_at":null,"product_family":{"id":3026731,"name":"eShopSubscribe","handle":"demo-plans"}}},
          {"product":{"id":7131001,"name":"Retired Plan","handle":"retired-plan","price_in_cents":100,"interval":1,"interval_unit":"month","require_credit_card":false,"taxable":false,"archived_at":"2026-01-01T00:00:00+00:00","product_family":{"id":3026731,"name":"eShopSubscribe","handle":"demo-plans"}}}
        ]
        """;

    public const string Customer = """
        {"customer":{"id":98837189,"first_name":"Demouser","last_name":"Shopper","email":"demouser@microsoft.com","reference":"eshoponweb-demouser@microsoft.com","created_at":"2026-09-06T10:00:00+05:00"}}
        """;

    public const string DuplicateReferenceError = """
        {"errors":["Reference: must be unique - that value has been taken."]}
        """;

    public const string DuplicateSubmissionError = """
        {"errors":["DuplicatePrevention::DuplicateSubmissionError"]}
        """;

    public const string NoPaymentMethodError = """
        {"errors":["No payment method was on file for the $299.00 balance"]}
        """;

    public const string NoSubscriptions = "[]";

    /// <summary>Body of POST /subscriptions.json.</summary>
    public static string CreatedSubscription(long id = 94208329, string state = "active", string planHandle = "eshop-pro", long priceInCents = 29900) =>
        "{\"subscription\":{" + SubscriptionBody(id, state, planHandle, priceInCents) + "}}";

    /// <summary>Body of GET /customers/{id}/subscriptions.json.</summary>
    public static string SubscriptionList(params string[] subscriptions) =>
        "[" + string.Join(",", subscriptions.Select(s => "{\"subscription\":{" + s + "}}")) + "]";

    /// <summary>The members of a subscription object, without the enclosing braces.</summary>
    public static string SubscriptionBody(long id, string state, string planHandle, long priceInCents, string createdAt = "2026-09-06T10:16:05+05:00") =>
        $"\"id\":{id},\"state\":\"{state}\",\"balance_in_cents\":{priceInCents},\"product_price_in_cents\":{priceInCents},\"currency\":\"USD\"," +
        $"\"current_period_started_at\":\"{createdAt}\",\"current_period_ends_at\":\"2026-10-06T10:16:05+05:00\",\"next_assessment_at\":\"2026-10-06T10:16:05+05:00\"," +
        $"\"activated_at\":\"{createdAt}\",\"canceled_at\":null,\"created_at\":\"{createdAt}\",\"payment_collection_method\":\"remittance\"," +
        $"\"product\":{{\"id\":7130999,\"name\":\"Pro Plan\",\"handle\":\"{planHandle}\",\"price_in_cents\":{priceInCents},\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false,\"taxable\":false}}," +
        "\"customer\":{\"id\":98837189,\"first_name\":\"Demouser\",\"last_name\":\"Shopper\",\"email\":\"demouser@microsoft.com\",\"reference\":\"eshoponweb-demouser@microsoft.com\",\"created_at\":\"2026-09-06T10:00:00+05:00\"}";
}
