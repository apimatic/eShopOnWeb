using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Builds a real <see cref="MaxioBillingClient"/> wired through the production
/// <see cref="MaxioHttpClientConfigurator"/> (so base-URL resolution and Basic-auth wiring are the
/// same code paths that run in the hosts), but pointed at a <see cref="StubHttpMessageHandler"/>.
/// </summary>
public static class MaxioClientHarness
{
    public static MaxioSettings DefaultSettings() => new()
    {
        ApiKey = "test-key",
        Subdomain = "apimatic-hackathon",
        Environment = "US",
        BaseUrl = "https://billing.test.local",
        ProductFamilyHandle = "eshop-subscribe",
        ProductFamilyId = 3023074,
        DefaultProductHandle = "eshop-pro",
        DefaultProductId = 7126957,
        AlternateProductHandle = "basic-plan",
        AlternateProductId = 7126958,
        MeteredComponentHandle = "api-call",
        MeteredComponentId = 3057195
    };

    public static (MaxioBillingClient client, StubHttpMessageHandler handler) Create(
        StubHttpMessageHandler handler, MaxioSettings? settings = null)
    {
        settings ??= DefaultSettings();
        var http = new HttpClient(handler);
        MaxioHttpClientConfigurator.Configure(http, settings);
        var client = new MaxioBillingClient(http, Options.Create(settings));
        return (client, handler);
    }

    public static (MaxioBillingClient client, StubHttpMessageHandler handler) WithResponse(
        HttpStatusCode status, string body, MaxioSettings? settings = null)
        => Create(StubHttpMessageHandler.Always(status, body), settings);

    /// <summary>Routes responses by (uppercase HTTP method, path substring) so multi-call flows work.</summary>
    public static (MaxioBillingClient client, StubHttpMessageHandler handler) WithRoutes(
        IReadOnlyList<(string method, string pathContains, HttpStatusCode status, string body)> routes,
        MaxioSettings? settings = null)
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            foreach (var route in routes)
            {
                if (string.Equals(request.Method.Method, route.method, StringComparison.OrdinalIgnoreCase)
                    && request.RequestUri!.PathAndQuery.Contains(route.pathContains, StringComparison.OrdinalIgnoreCase))
                {
                    return (route.status, route.body);
                }
            }

            return (HttpStatusCode.NotImplemented, "{\"errors\":[\"no stubbed route matched\"]}");
        });

        return Create(handler, settings);
    }
}
