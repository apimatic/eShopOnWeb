using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The three subscription routes end to end through the real PublicApi host (JWT auth, DI, EF in-memory store,
/// Maxio SDK), with Maxio itself replaced by <see cref="FakeMaxio"/> under the SDK's HttpClient.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static FakeMaxio _maxio = null!;
    private static WebApplicationFactory<Program> _factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // "test-family" and the base URL come from appsettings.test.json; nothing here can reach a real Maxio site.
        _maxio = new FakeMaxio("test-family")
            .WithProduct("pro", "Pro", 29900)
            .WithProduct("basic", "Basic", 2900);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => _maxio)));
    }

    [ClassCleanup]
    public static void ClassCleanup() => _factory.Dispose();

    private static HttpClient Client(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static StringContent Body(object value)
        => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    [TestMethod]
    [DataRow("GET", "api/subscription-plans")]
    [DataRow("GET", "api/my-subscriptions")]
    [DataRow("POST", "api/subscriptions")]
    public async Task RequiresAJwt(string method, string route)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
        {
            request.Content = Body(new { productHandle = "pro" });
        }

        var response = await Client(null).SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsThePlansOfTheConfiguredFamily()
    {
        var response = await Client(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.IsTrue(model!.Complete);
        CollectionAssert.AreEquivalent(new[] { "pro", "basic" }, model.Plans.Select(p => p.Handle).ToArray());
        Assert.AreEqual(299.00m, model.Plans.Single(p => p.Handle == "pro").Price);
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentAndShowsUpInMySubscriptions()
    {
        var client = Client(ApiTokenHelper.GetNormalUserToken());

        var created = await client.PostAsync("api/subscriptions", Body(new { productHandle = "pro" }));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var createdModel = (await created.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.AreEqual("created", createdModel!.Outcome);
        Assert.AreEqual("pro", createdModel.Subscription!.PlanHandle);
        Assert.AreEqual(299.00m, createdModel.Subscription.Price);
        Assert.AreEqual("active", createdModel.Subscription.State);
        Assert.IsNotNull(createdModel.Subscription.NextBillingAt);

        var again = await client.PostAsync("api/subscriptions", Body(new { productHandle = "pro" }));
        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);
        var againModel = (await again.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.AreEqual("already_exists", againModel!.Outcome);
        Assert.AreEqual(createdModel.Subscription.Id, againModel.Subscription!.Id);
        Assert.AreEqual(1, _maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.AreEqual(1, _maxio.Count(HttpMethod.Post, "/customers.json"));

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var mineModel = (await mine.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();
        Assert.AreEqual(createdModel.Subscription.Id, mineModel!.Subscriptions.Single().Id);
    }

    [TestMethod]
    [DataRow("not-a-plan", HttpStatusCode.BadRequest)]
    [DataRow("", HttpStatusCode.BadRequest)]
    public async Task RejectsAPlanTheShopDoesNotOffer(string handle, HttpStatusCode expected)
    {
        var posts = _maxio.Count(HttpMethod.Post, "/subscriptions.json");

        var response = await Client(ApiTokenHelper.GetAdminUserToken()).PostAsync("api/subscriptions", Body(new { productHandle = handle }));

        Assert.AreEqual(expected, response.StatusCode);
        Assert.AreEqual(posts, _maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }
}
