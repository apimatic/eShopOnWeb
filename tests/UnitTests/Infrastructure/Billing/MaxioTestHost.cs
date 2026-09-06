using System.Net;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>
/// Builds the real service over a stubbed transport, including the real message-handler pipeline — the
/// write-once guarantee lives in that pipeline, so a test that skipped it would prove nothing about it.
/// </summary>
public static class MaxioTestHost
{
    public const string ProductFamilyHandle = "eshop-subscribe";
    public const string PlanHandle = "eshop-pro";
    public const int CustomerId = 42;

    public static MaxioSettings DefaultSettings() => new()
    {
        ApiKey = "not-a-real-key",
        Subdomain = "test-site",
        ProductFamilyHandle = ProductFamilyHandle,
        // Keep the retry pipeline at its floor so a deliberately failing test does not sit in backoff.
        MaxRetries = 1,
        AttemptTimeoutSeconds = 5,
        CallBudgetSeconds = 10
    };

    public static (MaxioSubscriptionBillingService Service, MaxioStubHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        MaxioSettings? settings = null)
    {
        settings ??= DefaultSettings();

        var stub = new MaxioStubHandler(responder);
        var httpClient = new HttpClient(new MaxioCallScopeHandler { InnerHandler = stub });
        var client = new MaxioAdvancedBillingClient(httpClient, MaxioClientOptionsFactory.Create(settings));

        var service = new MaxioSubscriptionBillingService(
            client,
            Options.Create(settings),
            new InProcessBillingOperationLock(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioSubscriptionBillingService>.Instance);

        return (service, stub);
    }

    /// <summary>
    /// Routes by path, mirroring the shapes observed on the wire against a real Maxio site. Any path the test
    /// did not opt into answers 500, so an unexpected call is a failure rather than a silent pass.
    /// </summary>
    public static Func<HttpRequestMessage, HttpResponseMessage> Router(
        bool customerExists = false,
        bool relationshipInvoicing = true,
        string existingSubscriptionsJson = "[]",
        Func<HttpRequestMessage, HttpResponseMessage>? onCreateSubscription = null,
        Func<HttpRequestMessage, HttpResponseMessage>? onCustomerLookup = null)
    {
        return request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/product_families.json")
                return MaxioStubHandler.Json(HttpStatusCode.OK, ProductFamiliesJson);

            if (path == "/site.json")
                return MaxioStubHandler.Json(HttpStatusCode.OK, SiteJson(relationshipInvoicing));

            if (path.EndsWith("/products.json", StringComparison.Ordinal))
                return MaxioStubHandler.Json(HttpStatusCode.OK, ProductsJson);

            if (path == "/customers/lookup.json")
                return onCustomerLookup?.Invoke(request)
                       ?? (customerExists
                           ? MaxioStubHandler.Json(HttpStatusCode.OK, CustomerJson)
                           : MaxioStubHandler.Json(HttpStatusCode.NotFound, """{"error":"Not Found"}"""));

            if (path == "/customers.json" && request.Method == HttpMethod.Post)
                return MaxioStubHandler.Json(HttpStatusCode.Created, CustomerJson);

            if (path.EndsWith("/subscriptions.json", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
                return MaxioStubHandler.Json(HttpStatusCode.OK, existingSubscriptionsJson);

            if (path == "/subscriptions.json" && request.Method == HttpMethod.Post)
                return onCreateSubscription?.Invoke(request)
                       ?? MaxioStubHandler.Json(HttpStatusCode.Created, CreatedSubscriptionJson);

            return MaxioStubHandler.Json(HttpStatusCode.InternalServerError, $$"""{"error":"unexpected call to {{path}}"}""");
        };
    }

    public const string ProductFamiliesJson = """
        [{"product_family":{"id":3026729,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]
        """;

    public static string SiteJson(bool relationshipInvoicing) =>
        """{"site":{"id":1,"subdomain":"test-site","currency":"USD","test":true,"relationship_invoicing_enabled":"""
        + (relationshipInvoicing ? "true" : "false")
        + ""","default_payment_collection_method":"automatic"}}""";

    public const string ProductsJson = """
        [{"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,
                     "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,
                     "product_family":{"id":3026729,"handle":"eshop-subscribe"}}},
         {"product":{"id":7126958,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,
                     "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,
                     "product_family":{"id":3026729,"handle":"eshop-subscribe"}}}]
        """;

    public const string CustomerJson = """
        {"customer":{"id":42,"reference":"eshoponweb-demouser@microsoft.com","email":"demouser@microsoft.com",
                     "first_name":"Demouser","last_name":"Customer"}}
        """;

    public const string CreatedSubscriptionJson = """
        {"subscription":{"id":94208636,"state":"active","currency":"USD","product_price_in_cents":29900,
                         "next_assessment_at":"2026-10-06T11:37:45+05:00",
                         "current_period_started_at":"2026-09-06T11:37:45+05:00",
                         "current_period_ends_at":"2026-10-06T11:37:45+05:00",
                         "activated_at":"2026-09-06T11:37:46+05:00",
                         "customer":{"id":42,"reference":"eshoponweb-demouser@microsoft.com"},
                         "product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900}}}
        """;

    public const string LiveSubscriptionListJson = $"[{CreatedSubscriptionJson}]";

    public const string CanceledSubscriptionListJson = """
        [{"subscription":{"id":94208600,"state":"canceled","currency":"USD","product_price_in_cents":29900,
                          "customer":{"id":42},
                          "product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900}}}]
        """;
}
