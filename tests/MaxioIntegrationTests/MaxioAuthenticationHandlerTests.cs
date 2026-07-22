using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class MaxioAuthenticationHandlerTests
{
    [Fact]
    public async Task EveryCallCarriesTheApiKeyAsBasicCredentials()
    {
        var transport = new StubHttpMessageHandler().AlwaysRespondWith(HttpStatusCode.OK, "{}");
        var client = Build(transport, "my-api-key");

        await client.GetAsync("http://localhost:8080/subscriptions.json");
        await client.PostAsync("http://localhost:8080/subscriptions.json", new StringContent("{}"));

        Assert.Equal(2, transport.Requests.Count);
        Assert.All(transport.Requests, request =>
        {
            Assert.Equal("Basic", request.AuthScheme);
            // The specification pairs the API key as username with the literal password "x".
            Assert.Equal("my-api-key:x", Encoding.UTF8.GetString(Convert.FromBase64String(request.AuthParameter!)));
        });
    }

    [Fact]
    public async Task TheApiKeyNeverAppearsInTheUrl()
    {
        var transport = new StubHttpMessageHandler().AlwaysRespondWith(HttpStatusCode.OK, "{}");
        var client = Build(transport, "my-api-key");

        await client.GetAsync("http://localhost:8080/customers/lookup.json?reference=someone@example.com");

        var request = Assert.Single(transport.Requests);
        Assert.DoesNotContain("my-api-key", request.Path, StringComparison.Ordinal);
        Assert.DoesNotContain("my-api-key", request.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAnApiKeyTheCallIsRefusedBeforeItLeaves()
    {
        var transport = new StubHttpMessageHandler().AlwaysRespondWith(HttpStatusCode.OK, "{}");
        var client = Build(transport, apiKey: "  ");

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.GetAsync("http://localhost:8080/subscriptions.json"));

        Assert.Empty(transport.Requests);
    }

    private static HttpClient Build(StubHttpMessageHandler transport, string apiKey)
    {
        var settings = new MaxioSettings { ApiKey = apiKey, BaseUrl = "http://localhost:8080" };
        var handler = new MaxioAuthenticationHandler(new StaticOptionsMonitor<MaxioSettings>(settings))
        {
            InnerHandler = transport
        };

        return new HttpClient(handler);
    }
}
