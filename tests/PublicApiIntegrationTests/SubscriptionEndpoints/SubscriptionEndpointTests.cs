using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
[DoNotParallelize]
public sealed class SubscriptionEndpointTests
{
    private FakeMaxioClient _maxio = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _maxio = ProgramTest.Application.Services.GetRequiredService<FakeMaxioClient>();
        _maxio.Reset();
        using var scope = ProgramTest.Application.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        db.SubscriptionRecords.RemoveRange(db.SubscriptionRecords);
        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task AllSubscriptionEndpointsRequireBearerToken()
    {
        using var client = ProgramTest.NewClient;

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "eshop-pro" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/my-subscriptions")).StatusCode);
    }

    [TestMethod]
    public async Task HeroFlowReturnsPlansCreatesOnceAndListsSubscription()
    {
        using var client = AuthenticatedClient();

        var plansResponse = await client.GetFromJsonAsync<SubscriptionPlansResponse>("/api/subscription-plans");
        Assert.AreEqual(2, plansResponse!.Plans.Count);
        Assert.AreEqual(2900, plansResponse.Plans.First().PriceInCents);

        var firstHttpResponse = await client.PostAsJsonAsync(
            "/api/subscriptions",
            new CreateSubscriptionRequest { ProductHandle = "eshop-pro" });
        Assert.AreEqual(HttpStatusCode.Created, firstHttpResponse.StatusCode);
        var first = await firstHttpResponse.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.IsTrue(first!.Created);
        Assert.AreEqual("eshop-pro", first.Subscription.PlanHandle);
        Assert.AreEqual(29900, first.Subscription.PriceInCents);
        Assert.AreEqual("active", first.Subscription.State);
        Assert.IsNotNull(first.Subscription.NextBillingAt);

        var secondHttpResponse = await client.PostAsJsonAsync(
            "/api/subscriptions",
            new CreateSubscriptionRequest { ProductHandle = "eshop-pro" });
        Assert.AreEqual(HttpStatusCode.OK, secondHttpResponse.StatusCode);
        var second = await secondHttpResponse.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.IsFalse(second!.Created);
        Assert.AreEqual(first.Subscription.Id, second.Subscription.Id);
        Assert.AreEqual(1, _maxio.CustomerCreateCount);
        Assert.AreEqual(1, _maxio.SubscriptionCreateCount);

        var mine = await client.GetFromJsonAsync<MySubscriptionsResponse>("/api/my-subscriptions");
        Assert.AreEqual(1, mine!.Subscriptions.Count);
        Assert.AreEqual(first.Subscription.Id, mine.Subscriptions[0].Id);
    }

    [TestMethod]
    public async Task UnknownPlanDoesNotCreateCustomerOrSubscription()
    {
        using var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/api/subscriptions",
            new CreateSubscriptionRequest { ProductHandle = "not-a-plan" });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(0, _maxio.CustomerCreateCount);
        Assert.AreEqual(0, _maxio.SubscriptionCreateCount);
    }

    [TestMethod]
    public async Task ConcurrentDoubleClickCreatesExactlyOneSubscription()
    {
        using var firstClient = AuthenticatedClient();
        using var secondClient = AuthenticatedClient();
        var request = new CreateSubscriptionRequest { ProductHandle = "eshop-pro" };

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/api/subscriptions", request),
            secondClient.PostAsJsonAsync("/api/subscriptions", request));
        var bodies = await Task.WhenAll(
            responses[0].Content.ReadFromJsonAsync<CreateSubscriptionResponse>(),
            responses[1].Content.ReadFromJsonAsync<CreateSubscriptionResponse>());

        CollectionAssert.AreEquivalent(
            new[] { HttpStatusCode.Created, HttpStatusCode.OK },
            responses.Select(x => x.StatusCode).ToArray());
        Assert.AreEqual(bodies[0]!.Subscription.Id, bodies[1]!.Subscription.Id);
        Assert.AreEqual(1, _maxio.CustomerCreateCount);
        Assert.AreEqual(1, _maxio.SubscriptionCreateCount);
    }

    private static HttpClient AuthenticatedClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());
        return client;
    }
}
