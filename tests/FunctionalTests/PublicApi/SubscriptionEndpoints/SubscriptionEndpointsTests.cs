using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.FunctionalTests.Web.Api;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.FunctionalTests.PublicApi.SubscriptionEndpoints;

[Collection("Sequential")]
public class SubscriptionEndpointsTests : IClassFixture<TestApiApplication>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly StubSubscriptionBillingService _stub = new();
    private readonly HttpClient _client;

    public SubscriptionEndpointsTests(TestApiApplication factory)
    {
        var customized = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddScoped<ISubscriptionBillingService>(_ => _stub);
            });
        });

        _client = customized.CreateClient();
    }

    private void Authenticate() =>
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

    [Fact]
    public async Task ListPlans_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("api/subscription-plans");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListPlans_WithToken_ReturnsPlans()
    {
        Authenticate();

        var response = await _client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var plans = doc.RootElement.GetProperty("plans");
        Assert.Equal(2, plans.GetArrayLength());
        Assert.Equal("eshop-pro", plans[0].GetProperty("handle").GetString());
        Assert.Equal("299.00 per month", plans[0].GetProperty("formattedPrice").GetString());
    }

    [Fact]
    public async Task Subscribe_WithToken_ReturnsActiveSubscription_UsingTokenIdentity()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var subscription = doc.RootElement.GetProperty("subscription");
        Assert.Equal("active", subscription.GetProperty("state").GetString());
        Assert.Equal("eshop-pro", subscription.GetProperty("planHandle").GetString());

        // Identity must come from the JWT, not the request body.
        Assert.Equal("demouser@microsoft.com", _stub.LastSubscriberReference);
        Assert.Equal("eshop-pro", _stub.LastPlanHandle);
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_Returns404()
    {
        Authenticate();

        var response = await _client.PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Subscribe_WithoutToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MySubscriptions_WithToken_ReturnsSubscriptions()
    {
        Authenticate();

        var response = await _client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var subscriptions = doc.RootElement.GetProperty("subscriptions");
        Assert.Equal(1, subscriptions.GetArrayLength());
        Assert.Equal("eshop-pro", subscriptions[0].GetProperty("planHandle").GetString());
        Assert.Equal("demouser@microsoft.com", _stub.LastSubscriberReference);
    }
}
