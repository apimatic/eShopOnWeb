using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static (HttpClient Client, FakeBillingGateway Gateway) NewClient(string? token = null)
    {
        var gateway = new FakeBillingGateway();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ISubscriptionBillingGateway>(gateway)));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token ?? ApiTokenHelper.GetNormalUserToken());
        return (client, gateway);
    }

    private static StringContent Subscribe(string planHandle) =>
        new($$"""{ "planHandle": "{{planHandle}}" }""", Encoding.UTF8, "application/json");

    [TestMethod]
    [DataRow("GET", "api/subscription-plans")]
    [DataRow("GET", "api/my-subscriptions")]
    [DataRow("POST", "api/subscriptions")]
    public async Task RequiresJwt(string method, string route)
    {
        var client = ProgramTest.NewClient;
        var request = new HttpRequestMessage(new HttpMethod(method), route) { Content = method == "POST" ? Subscribe("plan-created") : null };

        var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsPlansFromProvider()
    {
        var (client, _) = NewClient();

        var response = await client.GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>()!;
        Assert.AreEqual(4, model.Plans.Count);
        var plan = model.Plans.Single(p => p.Handle == "plan-created");
        Assert.AreEqual(299.00m, plan.Price);
        Assert.AreEqual("month", plan.IntervalUnit);
        Assert.IsFalse(model.Truncated);
    }

    [TestMethod]
    public async Task SubscribeCreatesThenRepeatIsIdempotent()
    {
        var (client, gateway) = NewClient(ApiTokenHelper.GetAdminUserToken());

        var first = await client.PostAsync("api/subscriptions", Subscribe("plan-created"));
        var second = await client.PostAsync("api/subscriptions", Subscribe("plan-created"));

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        var created = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.AreEqual(CreateSubscriptionResponse.StatusCreated, created.Status);
        Assert.AreEqual("plan-created", created.Subscription!.PlanHandle);
        Assert.AreEqual("active", created.Subscription.State);
        Assert.IsNotNull(created.Subscription.NextBillingAt);

        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var repeated = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.AreEqual(CreateSubscriptionResponse.StatusAlreadySubscribed, repeated.Status);
        Assert.AreEqual(created.Subscription.Id, repeated.Subscription!.Id);
        Assert.AreEqual(1, gateway.CreateSubscriptionCalls);

        var mine = (await (await client.GetAsync("api/my-subscriptions")).Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>()!;
        Assert.IsTrue(mine.Subscriptions.Any(s => s.Id == created.Subscription.Id));
    }

    [TestMethod]
    public async Task ConcurrentDoubleClickReachesProviderOnce()
    {
        var (client, gateway) = NewClient();
        gateway.CreateDelay = TimeSpan.FromMilliseconds(500);

        var responses = await Task.WhenAll(
            client.PostAsync("api/subscriptions", Subscribe("plan-race")),
            client.PostAsync("api/subscriptions", Subscribe("plan-race")));

        var statuses = responses.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray();
        CollectionAssert.AreEqual(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, statuses);
        Assert.AreEqual(1, gateway.CreateSubscriptionCalls);
        Assert.IsTrue(gateway.CreateCustomerCalls <= 1);
    }

    [TestMethod]
    public async Task RejectsPlanNotOffered()
    {
        var (client, gateway) = NewClient();

        var response = await client.PostAsync("api/subscriptions", Subscribe("not-a-plan"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, gateway.CreateSubscriptionCalls);
    }

    [TestMethod]
    public async Task UnknownOutcomeIsAcceptedThenSettledWithoutASecondWrite()
    {
        var (client, gateway) = NewClient(ApiTokenHelper.GetAdminUserToken());
        gateway.FailedWriteLands = true;
        gateway.CreateSubscriptionFailure = reference => new BillingOutcomeUnknownException(reference);

        var first = await client.PostAsync("api/subscriptions", Subscribe("plan-unknown"));
        Assert.AreEqual(HttpStatusCode.Accepted, first.StatusCode);
        Assert.AreEqual(CreateSubscriptionResponse.StatusPending,
            (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!.Status);

        gateway.CreateSubscriptionFailure = null;
        var retry = await client.PostAsync("api/subscriptions", Subscribe("plan-unknown"));

        Assert.AreEqual(HttpStatusCode.Created, retry.StatusCode);
        var settled = (await retry.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.AreEqual("plan-unknown", settled.Subscription!.PlanHandle);
        Assert.AreEqual(1, gateway.CreateSubscriptionCalls, "the landed write must be found by reference, not repeated");
    }

    [TestMethod]
    public async Task ProviderRejectionIsReturnedAndReleasesTheClaim()
    {
        var (client, gateway) = NewClient();
        gateway.CreateSubscriptionFailure = _ => new BillingProviderException(BillingFailureKind.Rejected,
            "No payment method was on file", 422, new[] { "No payment method was on file" });

        var rejected = await client.PostAsync("api/subscriptions", Subscribe("plan-rejected"));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        StringAssert.Contains(await rejected.Content.ReadAsStringAsync(), "No payment method was on file");

        gateway.CreateSubscriptionFailure = null;
        var retry = await client.PostAsync("api/subscriptions", Subscribe("plan-rejected"));

        Assert.AreEqual(HttpStatusCode.Created, retry.StatusCode);
        Assert.AreEqual(2, gateway.CreateSubscriptionCalls);
    }

    [TestMethod]
    public async Task ProviderOutageIsServiceUnavailableWithSafeMessage()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ISubscriptionBillingGateway>(new UnreachableGateway())));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "unreachable");
    }

    private sealed class UnreachableGateway : ISubscriptionBillingGateway
    {
        private static Exception Down() => new BillingProviderException(BillingFailureKind.Unavailable, "The billing provider is unreachable. Please retry.");
        public Task<SubscriptionPlanCatalog> GetPlanCatalogAsync(CancellationToken cancellationToken) => throw Down();
        public Task<int?> FindCustomerIdAsync(string customerReference, CancellationToken cancellationToken) => throw Down();
        public Task<int> CreateCustomerAsync(string customerReference, BillingSubscriber subscriber, CancellationToken cancellationToken) => throw Down();
        public Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken cancellationToken) => throw Down();
        public Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string subscriptionReference, CancellationToken cancellationToken) => throw Down();
        public Task<BillingSubscription?> FindSubscriptionAsync(string subscriptionReference, CancellationToken cancellationToken) => throw Down();
    }
}
