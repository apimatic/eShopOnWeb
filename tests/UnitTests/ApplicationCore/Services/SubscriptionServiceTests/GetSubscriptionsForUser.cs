using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class GetSubscriptionsForUser
{
    private const string USER_ID = "demouser@microsoft.com";

    private readonly FakeMaxioAdvancedBillingClient _client = new();
    private readonly List<MaxioSubscription> _store = new();
    private readonly IRepository<MaxioSubscription> _repository;
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    public GetSubscriptionsForUser()
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
    public async Task ReturnsEmptyListWhenUserHasNoMaxioCustomer()
    {
        var service = CreateService();

        var results = await service.GetSubscriptionsForUserAsync(USER_ID);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ReturnsLiveAndCanceledSubscriptionsFromTheBillingProvider()
    {
        var service = CreateService();
        var pro = await service.SubscribeAsync(USER_ID, "eshop-pro");
        await service.SubscribeAsync(USER_ID, "basic-plan");
        _client.CancelSubscription(pro.MaxioSubscriptionId);

        var results = await service.GetSubscriptionsForUserAsync(USER_ID);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, s => s.PlanHandle == "eshop-pro" && s.State == "canceled");
        Assert.Contains(results, s => s.PlanHandle == "basic-plan" && s.State == "active");
    }
}
