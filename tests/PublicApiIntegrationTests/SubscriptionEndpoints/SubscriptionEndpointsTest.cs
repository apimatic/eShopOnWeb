using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private SubscriptionApiFactory _factory = null!;

    [TestInitialize]
    public void Initialize() => _factory = new SubscriptionApiFactory();

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task ListPlansReturnsUnauthorizedWithoutAToken()
    {
        var response = await _factory.CreateClient().GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeReturnsUnauthorizedWithoutAToken()
    {
        var response = await _factory.CreateClient().PostAsync("api/subscriptions", Json(new { }));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsReturnsUnauthorizedWithoutAToken()
    {
        var response = await _factory.CreateClient().GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPlansReturnsTheOfferedPlans()
    {
        var response = await AuthenticatedClient().GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var model = await response.Content.ReadFromJsonAsync<ListSubscriptionPlansResponse>(JsonOptions);

        Assert.IsNotNull(model);
        Assert.AreEqual(2, model!.SubscriptionPlans.Count);
        var pro = model.SubscriptionPlans.Single(p => p.Handle == FakeBillingGateway.ProPlanHandle);
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(299m, pro.Price);
        Assert.AreEqual("month", pro.IntervalUnit);
    }

    [TestMethod]
    public async Task MySubscriptionsIsEmptyForAShopperWhoHasNeverSubscribed()
    {
        var response = await AuthenticatedClient().GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        var model = await response.Content.ReadFromJsonAsync<ListMySubscriptionsResponse>(JsonOptions);

        Assert.IsNotNull(model);
        Assert.AreEqual(0, model!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task SubscribeCreatesTheCustomerAndTheSubscriptionAndReportsTheNextBillingDate()
    {
        var response = await AuthenticatedClient()
            .PostAsync("api/subscriptions", Json(new { planHandle = FakeBillingGateway.ProPlanHandle }));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var model = await response.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(JsonOptions);

        Assert.IsNotNull(model);
        Assert.IsTrue(model!.Created);
        Assert.IsTrue(model.CustomerCreated);
        Assert.IsNotNull(model.Subscription);
        Assert.AreEqual(FakeBillingGateway.ProPlanHandle, model.Subscription!.PlanHandle);
        Assert.AreEqual(299m, model.Subscription.Price);
        Assert.AreEqual(SubscriptionStates.Active, model.Subscription.State);
        Assert.IsTrue(model.Subscription.IsLive);
        Assert.IsNotNull(model.Subscription.NextBillingAt);
        Assert.IsTrue(model.Subscription.NextBillingAt > DateTimeOffset.UtcNow);
    }

    [TestMethod]
    public async Task SubscribingTwiceReturnsTheExistingSubscriptionInsteadOfASecondOne()
    {
        var client = AuthenticatedClient();
        var payload = new { planHandle = FakeBillingGateway.ProPlanHandle };

        var first = await client.PostAsync("api/subscriptions", Json(payload));
        var second = await client.PostAsync("api/subscriptions", Json(payload));

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

        var firstModel = await first.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(JsonOptions);
        var secondModel = await second.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(JsonOptions);

        Assert.IsFalse(secondModel!.Created);
        Assert.AreEqual(firstModel!.Subscription!.Id, secondModel.Subscription!.Id);
        Assert.AreEqual(1, _factory.Gateway.CustomersCreated);
        Assert.AreEqual(1, _factory.Gateway.SubscriptionsCreated);
    }

    [TestMethod]
    public async Task ADoubleClickCreatesOneCustomerAndOneSubscription()
    {
        var client = AuthenticatedClient();
        var payload = new { planHandle = FakeBillingGateway.ProPlanHandle };

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => client.PostAsync("api/subscriptions", Json(payload))));

        Assert.AreEqual(1, _factory.Gateway.CustomersCreated);
        Assert.AreEqual(1, _factory.Gateway.SubscriptionsCreated);
        Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.AreEqual(7, responses.Count(r => r.StatusCode == HttpStatusCode.OK));

        var ids = new List<int>();
        foreach (var response in responses)
        {
            var model = await response.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(JsonOptions);
            ids.Add(model!.Subscription!.Id);
        }

        Assert.AreEqual(1, ids.Distinct().Count());
    }

    [TestMethod]
    public async Task SubscribingToASecondPlanIsAllowed()
    {
        var client = AuthenticatedClient();

        await client.PostAsync("api/subscriptions", Json(new { planHandle = FakeBillingGateway.ProPlanHandle }));
        var second = await client.PostAsync("api/subscriptions", Json(new { planHandle = FakeBillingGateway.BasicPlanHandle }));

        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode);
        Assert.AreEqual(2, _factory.Gateway.SubscriptionsCreated);
        Assert.AreEqual(1, _factory.Gateway.CustomersCreated);
    }

    [TestMethod]
    public async Task SubscribeFallsBackToTheConfiguredDefaultPlan()
    {
        // appsettings.test.json configures a default plan handle the fake does not offer, which is
        // what proves the default is actually consulted when the caller names no plan.
        var response = await AuthenticatedClient().PostAsync("api/subscriptions", Json(new { }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "test-plan");
    }

    [TestMethod]
    public async Task SubscribeRejectsAPlanThatIsNotOnOffer()
    {
        var response = await AuthenticatedClient()
            .PostAsync("api/subscriptions", Json(new { planHandle = "no-such-plan" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, _factory.Gateway.SubscriptionsCreated);
    }

    [TestMethod]
    public async Task SubscribeReportsAConflictWhenADuplicateSubmissionLeftNothingBehind()
    {
        _factory.Gateway.RejectNextSubmissionAsDuplicate = true;

        var response = await AuthenticatedClient()
            .PostAsync("api/subscriptions", Json(new { planHandle = FakeBillingGateway.ProPlanHandle }));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsReadsBackWhatSubscribeCreated()
    {
        var client = AuthenticatedClient();
        await client.PostAsync("api/subscriptions", Json(new { planHandle = FakeBillingGateway.ProPlanHandle }));
        await client.PostAsync("api/subscriptions", Json(new { planHandle = FakeBillingGateway.BasicPlanHandle }));

        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        var model = await response.Content.ReadFromJsonAsync<ListMySubscriptionsResponse>(JsonOptions);

        Assert.AreEqual(2, model!.Subscriptions.Count);
        CollectionAssert.AreEquivalent(
            new[] { FakeBillingGateway.ProPlanHandle, FakeBillingGateway.BasicPlanHandle },
            model.Subscriptions.Select(s => s.PlanHandle).ToArray());
    }

    [TestMethod]
    public async Task ShoppersOnlyEverSeeTheirOwnSubscriptions()
    {
        var demoUser = AuthenticatedClient();
        await demoUser.PostAsync("api/subscriptions", Json(new { planHandle = FakeBillingGateway.ProPlanHandle }));

        var adminUser = _factory.CreateClient();
        adminUser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await adminUser.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        var model = await response.Content.ReadFromJsonAsync<ListMySubscriptionsResponse>(JsonOptions);

        Assert.AreEqual(0, model!.Subscriptions.Count);
    }
}
