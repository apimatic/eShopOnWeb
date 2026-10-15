using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    /// <summary>A host whose Maxio transport is the fake; a unique family keeps each test's claims apart.</summary>
    private static (WebApplicationFactory<Program> Factory, FakeMaxioHandler Maxio) CreateHost()
    {
        var maxio = new FakeMaxioHandler("fam-" + Guid.NewGuid().ToString("N"));
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maxio:ProductFamilyHandle"] = maxio.FamilyHandle,
            }));
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => maxio));
        });
        return (factory, maxio);
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, string? token = null)
    {
        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task Endpoints_RequireAToken()
    {
        var (factory, _) = CreateHost();
        var client = Client(factory);

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync("api/subscriptions", Body("""{"planHandle":"eshop-pro"}"""))).StatusCode);
    }

    [TestMethod]
    public async Task Plans_ListsTheConfiguredFamily()
    {
        var (factory, maxio) = CreateHost();

        var response = await Client(factory, ApiTokenHelper.GetNormalUserToken()).GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>()!;
        Assert.AreEqual(maxio.FamilyHandle, model.ProductFamilyHandle);
        CollectionAssert.AreEquivalent(new[] { "eshop-pro", "basic-plan" }, model.Plans.Select(p => p.Handle).ToArray());
        Assert.AreEqual(299m, model.Plans.Single(p => p.Handle == "eshop-pro").Price);
        Assert.IsFalse(model.IsTruncated);
    }

    [TestMethod]
    public async Task Subscribe_ThenMySubscriptions_ConfirmsPlanPriceStateAndNextBilling()
    {
        var (factory, maxio) = CreateHost();
        var client = Client(factory, ApiTokenHelper.GetNormalUserToken());

        var created = await client.PostAsync("api/subscriptions", Body("""{"planHandle":"eshop-pro"}"""));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var createdModel = (await created.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.AreEqual("created", createdModel.Status);
        Assert.AreEqual("active", createdModel.Subscription!.State);

        var again = await client.PostAsync("api/subscriptions", Body("""{"planHandle":"eshop-pro"}"""));
        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);

        var mine = (await (await client.GetAsync("api/my-subscriptions")).Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>()!;
        var subscription = mine.Subscriptions.Single();
        Assert.AreEqual("eshop-pro", subscription.PlanHandle);
        Assert.AreEqual(299m, subscription.Price);
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingAt);
        Assert.AreEqual(1, maxio.SubscriptionCreates);
        Assert.AreEqual(1, maxio.CustomerCreates);
    }

    [TestMethod]
    public async Task Subscribe_ConcurrentDoubleSubmit_CreatesExactlyOneSubscription()
    {
        var (factory, maxio) = CreateHost();
        var token = ApiTokenHelper.GetAdminUserToken();

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            Client(factory, token).PostAsync("api/subscriptions", Body("""{"planHandle":"basic-plan"}"""))));

        var statuses = responses.Select(r => r.StatusCode).ToList();
        Assert.AreEqual(1, statuses.Count(s => s == HttpStatusCode.Created), string.Join(",", statuses));
        Assert.IsTrue(statuses.All(s => s is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict), string.Join(",", statuses));
        Assert.AreEqual(1, maxio.SubscriptionCreates);
        Assert.AreEqual(1, maxio.CustomerCreates);
    }

    [TestMethod]
    public async Task Subscribe_PlanNotOffered_IsBadRequest()
    {
        var (factory, maxio) = CreateHost();

        var response = await Client(factory, ApiTokenHelper.GetNormalUserToken())
            .PostAsync("api/subscriptions", Body("""{"planHandle":"not-a-plan"}"""));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, maxio.SubscriptionCreates);
    }

    [TestMethod]
    public async Task Subscribe_MissingPlan_IsValidationProblem()
    {
        var (factory, _) = CreateHost();

        var response = await Client(factory, ApiTokenHelper.GetNormalUserToken()).PostAsync("api/subscriptions", Body("{}"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
