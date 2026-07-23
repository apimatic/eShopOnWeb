namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Maxio wire payloads used by the tests. The property names here are the provider's own JSON
/// field names, so a mapping that stops honouring them fails the tests rather than passing on a
/// convenient shape of our own invention.
/// </summary>
/// <remarks>
/// The literals use triple-brace interpolation so that JSON's own braces stay literal and only
/// <c>{{{value}}}</c> is substituted.
/// </remarks>
public static class MaxioResponses
{
    public static string ProductFamilyList(int id, string handle) =>
        $$$"""
        [{"product_family":{"id":{{{id}}},"name":"eShopSubscribe","handle":"{{{handle}}}","description":"Demo family"}}]
        """;

    public static string Product(
        int id = 7126957,
        string handle = "eshop-pro",
        string name = "Pro Plan",
        long priceInCents = 29900,
        int interval = 1,
        string intervalUnit = "month",
        bool requireCreditCard = false,
        string? archivedAt = null) =>
        $$$"""
        {"product":{{{ProductBody(id, handle, name, priceInCents, interval, intervalUnit, requireCreditCard, archivedAt)}}}}
        """;

    public static string ProductList(params string[] productBodies) =>
        "[" + string.Join(",", productBodies.Select(body => $$$"""{"product":{{{body}}}}""")) + "]";

    public static string ProductBody(
        int id = 7126957,
        string handle = "eshop-pro",
        string name = "Pro Plan",
        long priceInCents = 29900,
        int interval = 1,
        string intervalUnit = "month",
        bool requireCreditCard = false,
        string? archivedAt = null) =>
        $$$"""
        {"id":{{{id}}},"name":"{{{name}}}","handle":"{{{handle}}}","description":"A demo plan",
         "price_in_cents":{{{priceInCents}}},"interval":{{{interval}}},"interval_unit":"{{{intervalUnit}}}",
         "require_credit_card":{{{Bool(requireCreditCard)}}},"request_credit_card":false,
         "archived_at":{{{Nullable(archivedAt)}}}}
        """;

    public static string ComponentList(params string[] componentBodies) =>
        "[" + string.Join(",", componentBodies.Select(body => $$$"""{"component":{{{body}}}}""")) + "]";

    public static string ComponentBody(
        int id = 3057195,
        string handle = "api-call",
        string name = "API Calls",
        string kind = "metered_component",
        long pricePerUnitInCents = 1,
        string unitPrice = "0.01",
        bool archived = false) =>
        $$$"""
        {"id":{{{id}}},"name":"{{{name}}}","handle":"{{{handle}}}","kind":"{{{kind}}}","unit_name":"api call",
         "price_per_unit_in_cents":{{{pricePerUnitInCents}}},"unit_price":"{{{unitPrice}}}",
         "archived":{{{Bool(archived)}}},"product_family_id":3023074}
        """;

    /// <summary>A component whose cents field is absent, leaving only the dollar string.</summary>
    public static string ComponentBodyWithoutCents(string unitPrice = "0.25") =>
        $$$"""
        {"id":3057195,"name":"API Calls","handle":"api-call","kind":"metered_component",
         "unit_price":"{{{unitPrice}}}","archived":false,"product_family_id":3023074}
        """;

    public static string Customer(
        int id = 55001,
        string reference = "demouser@microsoft.com",
        string email = "demouser@microsoft.com",
        string firstName = "demouser",
        string lastName = "eShopOnWeb") =>
        $$$"""
        {"customer":{"id":{{{id}}},"first_name":"{{{firstName}}}","last_name":"{{{lastName}}}",
         "email":"{{{email}}}","reference":"{{{reference}}}"}}
        """;

    public static string Subscription(
        int id = 90001,
        string state = "active",
        int customerId = 55001,
        string customerReference = "demouser@microsoft.com",
        int productId = 7126957,
        string productHandle = "eshop-pro",
        long productPriceInCents = 29900,
        bool cancelAtEndOfPeriod = false,
        string? nextProductHandle = null) =>
        $$$"""
        {"subscription":{{{SubscriptionBody(id, state, customerId, customerReference, productId, productHandle, productPriceInCents, cancelAtEndOfPeriod, nextProductHandle)}}}}
        """;

    public static string SubscriptionList(params string[] subscriptionBodies) =>
        "[" + string.Join(",", subscriptionBodies.Select(body => $$$"""{"subscription":{{{body}}}}""")) + "]";

    public static string SubscriptionBody(
        int id = 90001,
        string state = "active",
        int customerId = 55001,
        string customerReference = "demouser@microsoft.com",
        int productId = 7126957,
        string productHandle = "eshop-pro",
        long productPriceInCents = 29900,
        bool cancelAtEndOfPeriod = false,
        string? nextProductHandle = null) =>
        $$$"""
        {"id":{{{id}}},"state":"{{{state}}}",
         "current_period_started_at":"2026-07-01T00:00:00Z",
         "current_period_ends_at":"2026-08-01T00:00:00Z",
         "next_assessment_at":"2026-08-01T00:00:00Z",
         "cancel_at_end_of_period":{{{Bool(cancelAtEndOfPeriod)}}},
         "next_product_handle":{{{Nullable(nextProductHandle)}}},
         "product_price_in_cents":{{{productPriceInCents}}},
         "customer":{"id":{{{customerId}}},"reference":"{{{customerReference}}}","email":"{{{customerReference}}}"},
         "product":{"id":{{{productId}}},"handle":"{{{productHandle}}}","name":"Pro Plan","price_in_cents":{{{productPriceInCents}}}}}
        """;

    public static string Usage(
        long id = 4400001,
        int subscriptionId = 90001,
        int componentId = 3057195,
        int quantity = 5,
        string memo = "api calls") =>
        $$$"""
        {"usage":{"id":{{{id}}},"memo":"{{{memo}}}","created_at":"2026-07-20T10:00:00Z",
         "quantity":{{{quantity}}},"component_id":{{{componentId}}},"component_handle":"api-call",
         "subscription_id":{{{subscriptionId}}}}}
        """;

    /// <summary>A usage record whose quantity arrives as a decimal string rather than a number.</summary>
    public static string UsageWithStringQuantity(string quantity = "2.5") =>
        $$$"""
        {"usage":{"id":4400002,"memo":"fractional","created_at":"2026-07-20T11:00:00Z",
         "quantity":"{{{quantity}}}","component_id":3057195,"subscription_id":90001}}
        """;

    public static string UsageList(params string[] usageEnvelopes) =>
        "[" + string.Join(",", usageEnvelopes) + "]";

    public static string MigrationPreview(
        long proratedAdjustmentInCents = -13500,
        long chargeInCents = 29900,
        long creditAppliedInCents = 13500,
        long paymentDueInCents = 16400) =>
        $$$"""
        {"migration":{"prorated_adjustment_in_cents":{{{proratedAdjustmentInCents}}},
         "charge_in_cents":{{{chargeInCents}}},
         "payment_due_in_cents":{{{paymentDueInCents}}},
         "credit_applied_in_cents":{{{creditAppliedInCents}}}}}
        """;

    public static string DelayedCancellation(string message = "Subscription scheduled for cancellation") =>
        $$$"""{"message":"{{{message}}}"}""";

    /// <summary>Maxio's standard validation-failure body.</summary>
    public static string ErrorList(params string[] errors) =>
        $$$"""{"errors":[{{{string.Join(",", errors.Select(error => $"\"{error}\""))}}}]}""";

    /// <summary>The site record, which decides how new subscriptions collect payment.</summary>
    public static string Site(bool relationshipInvoicingEnabled = true) =>
        $$$"""
        {"site":{"id":1,"name":"eShop Sandbox","subdomain":"test-site",
         "relationship_invoicing_enabled":{{{Bool(relationshipInvoicingEnabled)}}},
         "default_payment_collection_method":"automatic"}}
        """;

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Nullable(string? value) => value is null ? "null" : $"\"{value}\"";
}
