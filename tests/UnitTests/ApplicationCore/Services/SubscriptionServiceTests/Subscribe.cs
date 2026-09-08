using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    private const string USER_ID = "demouser@microsoft.com";
    private const string EXPECTED_REFERENCE = "eshoponweb:demouser@microsoft.com";

    private readonly FakeMaxioAdvancedBillingClient _client = new();
    private readonly List<MaxioSubscription> _store = new();
    private readonly IRepository<MaxioSubscription> _repository;
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    public Subscribe()
    {
        _repository = Substitute.For<IRepository<MaxioSubscription>>();
        _repository.ListAsync(Arg.Any<MaxioSubscriptionsByUserIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(_store.ToList()));
        _repository.AddAsync(Arg.Any<MaxioSubscription>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _store.Add(call.Arg<MaxioSubscription>());
                return Task.FromResult(call.Arg<MaxioSubscription>());
            });
        _repository.UpdateAsync(Arg.Any<MaxioSubscription>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<MaxioSubscription>()));
    }

    private SubscriptionService CreateService() =>
        new(_client, _repository, _logger, new MaxioSettings { ProductFamilyHandle = "eshop-subscribe", ApiKey = "key", Subdomain = "site" });

    [Fact]
    public async Task CreatesExactlyOneCustomerAndOneSubscriptionForNewUser()
    {
        var service = CreateService();

        var result = await service.SubscribeAsync(USER_ID, "eshop-pro");

        Assert.True(result.NewlyCreated);
        Assert.Equal("active", result.State);
        Assert.Equal(FakeMaxioAdvancedBillingClient.ProPlan.Price, result.Price);
        Assert.NotNull(result.NextBillingAt);
        Assert.Equal(1, _client.CustomersCreated);
        Assert.Equal(1, _client.SubscriptionsCreated);
        var tracked = Assert.Single(_store);
        Assert.Equal(USER_ID, tracked.UserId);
        Assert.Equal("eshop-pro", tracked.PlanHandle);
    }

    [Fact]
    public async Task IsIdempotentOnDoubleSubmit_AndDoesNotRecreateCustomer()
    {
        var service = CreateService();

        var first = await service.SubscribeAsync(USER_ID, "eshop-pro");
        var second = await service.SubscribeAsync(USER_ID, "eshop-pro");

        Assert.True(first.NewlyCreated);
        Assert.False(second.NewlyCreated);
        Assert.Equal(first.MaxioSubscriptionId, second.MaxioSubscriptionId);
        Assert.Equal(1, _client.CustomersCreated);
        Assert.Equal(1, _client.SubscriptionsCreated);
        Assert.Single(_store);
    }

    [Fact]
    public async Task ConcurrentDoubleClickedRequestsCreateASingleSubscription()
    {
        var service = CreateService();

        var results = await Task.WhenAll(
            Task.Run(() => service.SubscribeAsync(USER_ID, "eshop-pro")),
            Task.Run(() => service.SubscribeAsync(USER_ID, "eshop-pro")),
            Task.Run(() => service.SubscribeAsync(USER_ID, "eshop-pro")));

        Assert.Equal(1, _client.CustomersCreated);
        Assert.Equal(1, _client.SubscriptionsCreated);
        Assert.All(results, r => Assert.Equal(results[0].MaxioSubscriptionId, r.MaxioSubscriptionId));
        Assert.Single(_store);
    }

    [Fact]
    public async Task AllowsSecondSubscriptionForADifferentPlan()
    {
        var service = CreateService();

        await service.SubscribeAsync(USER_ID, "eshop-pro");
        var basic = await service.SubscribeAsync(USER_ID, "basic-plan");

        Assert.True(basic.NewlyCreated);
        Assert.Equal(2, _client.SubscriptionsCreated);
        Assert.Equal(1, _client.CustomersCreated);
    }

    [Fact]
    public async Task ReSubscribesAfterPreviousSubscriptionWasCanceled()
    {
        var service = CreateService();
        var first = await service.SubscribeAsync(USER_ID, "eshop-pro");
        _client.CancelSubscription(first.MaxioSubscriptionId);

        var second = await service.SubscribeAsync(USER_ID, "eshop-pro");

        Assert.True(second.NewlyCreated);
        Assert.Equal(2, _client.SubscriptionsCreated);
    }

    [Fact]
    public async Task ThrowsWhenPlanIsNotInTheConfiguredFamily()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => service.SubscribeAsync(USER_ID, "not-a-plan"));
        Assert.Equal(0, _client.CustomersCreated);
        Assert.Equal(0, _client.SubscriptionsCreated);
    }

    [Fact]
    public async Task RecoversWhenCustomerCreationLosesARaceOnTheUniqueReference()
    {
        var winner = new MaxioCustomer(999, EXPECTED_REFERENCE, USER_ID);
        var client = Substitute.For<IMaxioAdvancedBillingClient>();
        client.GetPlansForProductFamilyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MaxioPlan> { FakeMaxioAdvancedBillingClient.ProPlan });
        client.FindCustomerByReferenceAsync(EXPECTED_REFERENCE, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<MaxioCustomer?>(null), _ => Task.FromResult<MaxioCustomer?>(winner));
        client.CreateCustomerAsync(EXPECTED_REFERENCE, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<MaxioCustomer>(new MaxioBillingProviderException("Reference: must be unique - that value has been taken.", 422, new[] { "Reference: must be unique" }, duplicateCustomerReference: true)));
        client.GetSubscriptionsForCustomerAsync(winner.Id, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionInfo>());
        client.CreateSubscriptionAsync(winner.Id, FakeMaxioAdvancedBillingClient.ProPlan, Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscriptionInfo(1, winner.Id, FakeMaxioAdvancedBillingClient.ProPlan.ProductId, "eshop-pro", "Pro Plan", 29900, "active", DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow));

        var service = new SubscriptionService(client, _repository, _logger, new MaxioSettings { ProductFamilyHandle = "eshop-subscribe" });

        var result = await service.SubscribeAsync(USER_ID, "eshop-pro");

        Assert.True(result.NewlyCreated);
        Assert.Equal(winner.Id, result.MaxioCustomerId);
    }
}
