using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Verifies the HTTP mapping in the subscription endpoints: success shapes and the translation of a
/// <see cref="MaxioBillingException"/> into a problem response that carries the boundary's HTTP status.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTests
{
    [TestMethod]
    public async Task Subscribe_ReturnsCreated_WhenNewSubscription()
    {
        var billing = new FakeBilling
        {
            SubscribeResult = new SubscribeResult(new SubscriptionDto { Id = 1, State = "active" }, AlreadySubscribed: false)
        };
        var endpoint = new SubscribeEndpoint(billing, new FakeShopper());

        var result = await endpoint.HandleAsync(new SubscribeRequest { PlanHandle = "eshop-pro" }, CancellationToken.None);

        var created = result as Created<SubscribeResponse>;
        Assert.IsNotNull(created);
        Assert.AreEqual(201, created!.StatusCode);
        Assert.IsFalse(created.Value!.AlreadySubscribed);
        Assert.AreEqual(1, created.Value.Subscription!.Id);
    }

    [TestMethod]
    public async Task Subscribe_ReturnsOk_WhenAlreadySubscribed()
    {
        var billing = new FakeBilling
        {
            SubscribeResult = new SubscribeResult(new SubscriptionDto { Id = 1 }, AlreadySubscribed: true)
        };
        var endpoint = new SubscribeEndpoint(billing, new FakeShopper());

        var result = await endpoint.HandleAsync(new SubscribeRequest { PlanHandle = "eshop-pro" }, CancellationToken.None);

        var ok = result as Ok<SubscribeResponse>;
        Assert.IsNotNull(ok);
        Assert.AreEqual(200, ok!.StatusCode);
        Assert.IsTrue(ok.Value!.AlreadySubscribed);
    }

    [TestMethod]
    public async Task Subscribe_ReturnsProblemWithCarriedStatus_OnBillingException()
    {
        var billing = new FakeBilling
        {
            Exception = new MaxioBillingException("nope", HttpStatusCode.UnprocessableEntity)
        };
        var endpoint = new SubscribeEndpoint(billing, new FakeShopper());

        var result = await endpoint.HandleAsync(new SubscribeRequest { PlanHandle = "bad" }, CancellationToken.None);

        var problem = result as ProblemHttpResult;
        Assert.IsNotNull(problem);
        Assert.AreEqual(422, problem!.StatusCode);
    }

    [TestMethod]
    public async Task ListPlans_ReturnsOkWithPlans()
    {
        var billing = new FakeBilling
        {
            Plans = new List<SubscriptionPlanDto> { new() { Handle = "eshop-pro", Name = "Pro Plan" } }
        };
        var endpoint = new ListSubscriptionPlansEndpoint(billing);

        var result = await endpoint.HandleAsync(CancellationToken.None);

        var ok = result as Ok<ListSubscriptionPlansResponse>;
        Assert.IsNotNull(ok);
        Assert.AreEqual(1, ok!.Value!.Plans.Count);
        Assert.AreEqual("eshop-pro", ok.Value.Plans[0].Handle);
    }

    [TestMethod]
    public async Task ListMySubscriptions_ReturnsProblem_OnBillingException()
    {
        var billing = new FakeBilling
        {
            Exception = new MaxioBillingException("provider down", HttpStatusCode.BadGateway)
        };
        var endpoint = new ListMySubscriptionsEndpoint(billing, new FakeShopper());

        var result = await endpoint.HandleAsync(CancellationToken.None);

        var problem = result as ProblemHttpResult;
        Assert.IsNotNull(problem);
        Assert.AreEqual(502, problem!.StatusCode);
    }

    // ---- fakes ----

    private sealed class FakeBilling : IMaxioBillingService
    {
        public SubscribeResult? SubscribeResult { get; set; }
        public Exception? Exception { get; set; }
        public IReadOnlyList<SubscriptionPlanDto> Plans { get; set; } = Array.Empty<SubscriptionPlanDto>();
        public IReadOnlyList<SubscriptionDto> Subscriptions { get; set; } = Array.Empty<SubscriptionDto>();

        public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct) =>
            Exception is not null ? throw Exception : Task.FromResult(Plans);

        public Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken ct) =>
            Exception is not null ? throw Exception : Task.FromResult(SubscribeResult!);

        public Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ShopperIdentity shopper, CancellationToken ct) =>
            Exception is not null ? throw Exception : Task.FromResult(Subscriptions);
    }

    private sealed class FakeShopper : ICurrentShopperService
    {
        public Task<ShopperIdentity> GetCurrentShopperAsync(CancellationToken ct) =>
            Task.FromResult(new ShopperIdentity("demouser@microsoft.com", "demouser@microsoft.com", "demouser", "Shopper"));
    }
}
