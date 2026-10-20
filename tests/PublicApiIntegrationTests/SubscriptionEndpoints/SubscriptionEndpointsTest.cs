using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static (HttpClient Client, FakeMaxio Maxio) NewClient(string? token)
    {
        var maxio = new FakeMaxio();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(MaxioServiceCollectionExtensions.HTTP_CLIENT_NAME)
                    .ConfigurePrimaryHttpMessageHandler(() => maxio)));
        var client = factory.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, maxio);
    }

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task RequiresAToken()
    {
        var (client, _) = NewClient(null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync("api/subscriptions", Body("{\"productHandle\":\"pro\"}"))).StatusCode);
    }

    [TestMethod]
    public async Task ListsPlans()
    {
        var (client, _) = NewClient(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.AreEqual(2, model!.Plans.Count);
        Assert.AreEqual("basic", model.Plans[0].Handle);
        Assert.AreEqual(29m, model.Plans[0].Price);
        Assert.IsFalse(model.Truncated);
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentAndShowsInMySubscriptions()
    {
        var (client, maxio) = NewClient(ApiTokenHelper.GetNormalUserToken());

        var first = await client.PostAsync("api/subscriptions", Body("{\"productHandle\":\"pro\"}"));
        var second = await client.PostAsync("api/subscriptions", Body("{\"productHandle\":\"pro\"}"));

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var created = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        var repeated = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.AreEqual("created", created!.Status);
        Assert.AreEqual("already_subscribed", repeated!.Status);
        Assert.AreEqual(created.Subscription!.Id, repeated.Subscription!.Id);
        Assert.AreEqual(299m, created.Subscription.Price);
        Assert.AreEqual("active", created.Subscription.State);
        Assert.IsNotNull(created.Subscription.NextBillingDate);
        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, "/subscriptions.json"));

        var mine = (await (await client.GetAsync("api/my-subscriptions")).Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
        Assert.AreEqual(1, mine!.Subscriptions.Count);
        Assert.AreEqual("pro", mine.Subscriptions[0].PlanHandle);
    }

    [TestMethod]
    public async Task UnknownPlanIsABadRequest()
    {
        var (client, maxio) = NewClient(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", Body("{\"productHandle\":\"not-a-plan\"}"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, maxio.Count(HttpMethod.Post, "/customers.json"));
    }

    [TestMethod]
    public async Task ProviderOutageIsABadGatewayWithoutInternals()
    {
        var (client, maxio) = NewClient(ApiTokenHelper.GetNormalUserToken());
        maxio.FailEverythingWith = HttpStatusCode.Unauthorized;

        var response = await client.GetAsync("api/subscription-plans");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.IsFalse(body.Contains("MaxioAdvancedBilling"), body);
        Assert.IsFalse(body.Contains("127.0.0.1"), body);
    }
}
