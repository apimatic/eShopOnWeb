using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;

namespace PublicApiIntegrationTests.Maxio;

public static class MaxioWireFixtures
{
    public const string CustomerReference = "eshop:demouser@microsoft.com";
    public const string SubscriptionReference = "eshop:demouser@microsoft.com:eshop-pro";
    public const int CustomerId = 999001;
    public const int SubscriptionId = 94248374;

    public const string ProductsJson =
        """
        [
          { "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "product_family": { "id": 3023074, "handle": "eshop-subscribe", "name": "eShop Subscribe" } } },
          { "product": { "id": 7126958, "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "product_family": { "id": 3023074, "handle": "eshop-subscribe", "name": "eShop Subscribe" } } }
        ]
        """;

    public const string SiteJson =
        """
        { "site": { "id": 43991, "subdomain": "cp-exp-5", "currency": "USD" } }
        """;

    public const string CustomerJson =
        """
        { "customer": { "id": 999001, "reference": "eshop:demouser@microsoft.com", "first_name": "demouser", "last_name": "microsoft.com", "email": "demouser@microsoft.com" } }
        """;

    public const string SubscriptionJson =
        """
        { "subscription": { "id": 94248374, "reference": "eshop:demouser@microsoft.com:eshop-pro", "state": "active", "product_price_in_cents": 29900, "current_period_ends_at": "2026-10-09T00:13:04+00:00", "created_at": "2026-09-09T00:13:04+00:00", "currency": "USD", "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" }, "customer": { "id": 999001, "reference": "eshop:demouser@microsoft.com", "email": "demouser@microsoft.com" } } }
        """;

    public const string SubscriptionsListJson = "[ " + SubscriptionJson + " ]";

    public const string ProductsListRequestKey = "GET /product_families/handle%3Aeshop-subscribe/products.json";

    public const string CustomerSubscriptionsRequestKey = "GET /customers/999001/subscriptions.json";
}

public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<string, HttpResponseMessage> _responder;

    public List<string> Requests { get; } = new List<string>();

    public StubHandler(Func<string, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public int Count(string methodAndPath) => Requests.Count(r => r == methodAndPath);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
        Requests.Add(key);
        return Task.FromResult(_responder(key));
    }
}

public static class StubResponses
{
    public static HttpResponseMessage Ok(string json) => Json(HttpStatusCode.OK, json);

    public static HttpResponseMessage Created(string json) => Json(HttpStatusCode.Created, json);

    public static HttpResponseMessage Unprocessable(string json) => Json(HttpStatusCode.UnprocessableEntity, json);

    public static HttpResponseMessage NotFound() => new HttpResponseMessage(HttpStatusCode.NotFound);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new HttpResponseMessage(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

public static class MaxioTestClientFactory
{
    public static MaxioAdvancedBillingClient CreateClient(StubHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(5)
            },
            Server = new ServerOptions
            {
                Production = new ProductionOptions
                {
                    Us = new ProductionOptions.UsOptions
                    {
                        BaseUrl = "https://maxio.test"
                    }
                }
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = "test-api-key",
                Password = "x"
            }
        };

        return new MaxioAdvancedBillingClient(new HttpClient(handler), options);
    }
}
