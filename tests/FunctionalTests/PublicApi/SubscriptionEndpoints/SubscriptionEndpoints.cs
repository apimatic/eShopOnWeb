using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.FunctionalTests.PublicApi;
using Microsoft.eShopWeb.FunctionalTests.Web.Api;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Xunit;

namespace Microsoft.eShopWeb.FunctionalTests.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Exercises the subscription endpoints through the real host: routing, JWT authorization, model
/// binding and the exception-to-status-code mapping.
/// <para>
/// The test host carries no Maxio credentials, which is itself part of the contract: subscription
/// billing is additive, so the API still starts and the rest of it still works - these three routes
/// simply answer 503 with an actionable message. Everything that depends on talking to Maxio is
/// covered against a stubbed provider in the IntegrationTests project.
/// </para>
/// </summary>
[Collection("Sequential")]
public class SubscriptionEndpoints : IClassFixture<TestApiApplication>
{
    private readonly HttpClient _client;

    public SubscriptionEndpoints(TestApiApplication factory)
    {
        _client = factory.CreateClient();
    }

    private HttpClient Authenticated()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return _client;
    }

    [Theory]
    [InlineData("api/subscription-plans")]
    [InlineData("api/my-subscriptions")]
    public async Task ReadEndpointsRequireABearerToken(string route)
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubscribingRequiresABearerToken()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsJsonAsync("api/subscriptions",
            new CreateSubscriptionRequest { PlanHandle = "eshop-pro" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubscribingWithoutAPlanHandleIsRejectedBeforeTheBillingProviderIsCalled()
    {
        var response = await Authenticated().PostAsJsonAsync("api/subscriptions", new CreateSubscriptionRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("plan handle is required", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("api/subscription-plans")]
    [InlineData("api/my-subscriptions")]
    public async Task ReadEndpointsReportUnconfiguredBillingAsUnavailableRatherThanAServerError(string route)
    {
        var response = await Authenticated().GetAsync(route);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Maxio:ApiKey", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SubscribingReportsUnconfiguredBillingAsUnavailableRatherThanAServerError()
    {
        var response = await Authenticated().PostAsJsonAsync("api/subscriptions",
            new CreateSubscriptionRequest { PlanHandle = "eshop-pro" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Maxio:ApiKey", await response.Content.ReadAsStringAsync());
    }
}
