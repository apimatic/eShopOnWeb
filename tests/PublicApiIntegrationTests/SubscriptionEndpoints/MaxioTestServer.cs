using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Builds a test host whose Maxio SDK client talks to a stubbed HTTP handler, plus the
/// canned Maxio wire payloads for the seeded demo catalog (fake values — no secrets).
/// </summary>
public static class MaxioTestServer
{
    public const int ProductFamilyId = 3023074;
    public const int CustomerId = 501;

    public const string ProductFamiliesJson = """
        [{"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]
        """;

    public const string ProductsJson = """
        [
          {"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","description":"For pros","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}},
          {"product":{"id":7126958,"handle":"basic-plan","name":"Basic Plan","description":"For starters","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false}}
        ]
        """;

    public const string CustomerJson = """
        {"customer":{"id":501,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com","first_name":"demouser","last_name":"Customer"}}
        """;

    public const string SubscriptionObjectJson = """
        {"id":9001,"state":"active","reference":"demouser@microsoft.com:eshop-pro","product_price_in_cents":29900,"next_assessment_at":"2026-09-26T00:00:00Z","current_period_ends_at":"2026-09-26T00:00:00Z","activated_at":"2026-08-26T00:00:00Z","product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"},"customer":{"id":501,"reference":"demouser@microsoft.com"}}
        """;

    public static readonly string SubscriptionJson = "{\"subscription\":" + SubscriptionObjectJson + "}";
    public static readonly string SubscriptionListJson = "[{\"subscription\":" + SubscriptionObjectJson + "}]";

    public static WebApplicationFactory<Program> CreateFactory(MaxioStubHandler handler)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient(MaxioBillingExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        });

    /// <summary>Stub for the plans flow: family lookup, then products for that family id.</summary>
    public static MaxioStubHandler ForPlans()
    {
        return new MaxioStubHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (request.Method == HttpMethod.Get && path == "/product_families.json")
                return MaxioStubHandler.Json(HttpStatusCode.OK, ProductFamiliesJson);
            if (request.Method == HttpMethod.Get && path == $"/product_families/{ProductFamilyId}/products.json")
                return MaxioStubHandler.Json(HttpStatusCode.OK, ProductsJson);

            return MaxioStubHandler.Json(HttpStatusCode.NotFound, "{\"errors\":[\"unexpected request\"]}");
        });
    }

    /// <summary>
    /// Stateful stub for the subscribe flow: once created, the customer and subscription
    /// "persist", so repeat subscribes exercise the idempotent path.
    /// </summary>
    public static MaxioStubHandler ForSubscribeFlow(bool failSubscriptionCreateWith422 = false)
    {
        var customerExists = false;
        var subscriptionExists = false;

        return new MaxioStubHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
                return customerExists
                    ? MaxioStubHandler.Json(HttpStatusCode.OK, CustomerJson)
                    : MaxioStubHandler.Json(HttpStatusCode.NotFound, "{\"errors\":[\"Customer not found\"]}");

            if (request.Method == HttpMethod.Post && path == "/customers.json")
            {
                customerExists = true;
                return MaxioStubHandler.Json(HttpStatusCode.Created, CustomerJson);
            }

            if (request.Method == HttpMethod.Get && path == $"/customers/{CustomerId}/subscriptions.json")
                return MaxioStubHandler.Json(HttpStatusCode.OK, subscriptionExists ? SubscriptionListJson : "[]");

            if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                if (failSubscriptionCreateWith422)
                    return MaxioStubHandler.Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"Product requires a payment method\"]}");
                subscriptionExists = true;
                return MaxioStubHandler.Json(HttpStatusCode.Created, SubscriptionJson);
            }

            return MaxioStubHandler.Json(HttpStatusCode.NotFound, "{\"errors\":[\"unexpected request\"]}");
        });
    }
}
