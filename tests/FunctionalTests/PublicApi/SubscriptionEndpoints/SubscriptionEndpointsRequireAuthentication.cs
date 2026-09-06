using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.eShopWeb.FunctionalTests.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Proves the subscription routes are mapped and that none of them is reachable without a bearer
/// token. The assertions stop at the authorization boundary on purpose: reaching the handler would
/// mean calling the real billing provider, which is not something a test run should do.
/// </summary>
[Collection("Sequential")]
public class SubscriptionEndpointsRequireAuthentication : IClassFixture<TestApiApplication>
{
    private readonly HttpClient _client;

    public SubscriptionEndpointsRequireAuthentication(TestApiApplication factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListingPlansWithoutATokenIsUnauthorized()
    {
        var response = await _client.GetAsync("api/subscription-plans");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListingMySubscriptionsWithoutATokenIsUnauthorized()
    {
        var response = await _client.GetAsync("api/my-subscriptions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubscribingWithoutATokenIsUnauthorized()
    {
        var content = new StringContent("""{"planHandle":"eshop-pro"}""", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("api/subscriptions", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
