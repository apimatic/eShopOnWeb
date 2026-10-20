using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static FakeMaxio _maxio = null!;
    private static WebApplicationFactory<Program> _application = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _maxio = new FakeMaxio();
        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(BillingServiceCollectionExtensions.MaxioHttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => _maxio)));
    }

    [ClassCleanup]
    public static void ClassCleanup() => _application.Dispose();

    [TestMethod]
    [DataRow("GET", "api/subscription-plans")]
    [DataRow("GET", "api/my-subscriptions")]
    [DataRow("POST", "api/subscriptions")]
    public async Task RequiresBearerToken(string method, string route)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = method == "POST" ? Json("{\"planHandle\":\"eshop-pro\"}") : null
        };

        var response = await _application.CreateClient().SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsPlansOfTheConfiguredFamily()
    {
        var response = await AuthenticatedClient(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>()!;
        Assert.IsTrue(model.Complete);
        var pro = model.Plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual(299m, pro.Price);
        Assert.AreEqual("month", pro.IntervalUnit);
    }

    [TestMethod]
    public async Task SubscribeThenSeeItInMySubscriptions()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetAdminUserToken());

        var created = await client.PostAsync("api/subscriptions", Json("{\"planHandle\":\"eshop-pro\"}"));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var createdModel = (await created.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.IsTrue(createdModel.Created);
        Assert.AreEqual("eshop-pro", createdModel.Subscription.PlanHandle);
        Assert.AreEqual(299m, createdModel.Subscription.Price);
        Assert.AreEqual("active", createdModel.Subscription.State);
        Assert.IsNotNull(createdModel.Subscription.NextBillingDate);

        var repeated = await client.PostAsync("api/subscriptions", Json("{\"planHandle\":\"eshop-pro\"}"));
        Assert.AreEqual(HttpStatusCode.OK, repeated.StatusCode);
        Assert.AreEqual(createdModel.Subscription.Id, (await repeated.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!.Subscription.Id);

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var mineModel = (await mine.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>()!;
        Assert.AreEqual(createdModel.Subscription.Id, mineModel.Subscriptions.Single(s => s.PlanHandle == "eshop-pro").Id);
    }

    [TestMethod]
    public async Task UnknownPlanIsABadRequest()
    {
        var response = await AuthenticatedClient(ApiTokenHelper.GetNormalUserToken())
            .PostAsync("api/subscriptions", Json("{\"planHandle\":\"no-such-plan\"}"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await response.Content.ReadAsStringAsync()).FromJson<ErrorDetails>()!;
        StringAssert.Contains(error.Message, "no-such-plan");
    }

    private static HttpClient AuthenticatedClient(string token)
    {
        var client = _application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
