using System;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class GetSubscriptionsAsync
{
    private const string UserName = "demouser@microsoft.com";

    private readonly FakeBillingGateway _gateway = new();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService CreateService() => new(_gateway, new KeyedAsyncLock(), _logger);

    [Fact]
    public async Task ReturnsEmptyForAShopperWhoHasNeverSubscribed()
    {
        var subscriptions = await CreateService().GetSubscriptionsAsync(UserName);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ReturnsTheShopperSubscriptionsNewestFirst()
    {
        _gateway.SeedCustomer(new BillingCustomer
        {
            Id = 7,
            Reference = BillingCustomerReference.ForUser(UserName),
            Email = UserName
        });

        _gateway.SeedSubscription(new CustomerSubscription
        {
            Id = 1,
            CustomerId = 7,
            State = SubscriptionStates.Canceled,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        });
        _gateway.SeedSubscription(new CustomerSubscription
        {
            Id = 2,
            CustomerId = 7,
            State = SubscriptionStates.Active,
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Somebody else's subscription must not leak into the result.
        _gateway.SeedSubscription(new CustomerSubscription { Id = 3, CustomerId = 99, State = SubscriptionStates.Active });

        var subscriptions = await CreateService().GetSubscriptionsAsync(UserName);

        Assert.Equal(new[] { 2, 1 }, System.Linq.Enumerable.Select(subscriptions, s => s.Id));
    }

    [Fact]
    public async Task MatchesTheShopperCaseInsensitively()
    {
        _gateway.SeedCustomer(new BillingCustomer
        {
            Id = 7,
            Reference = BillingCustomerReference.ForUser(UserName),
            Email = UserName
        });
        _gateway.SeedSubscription(new CustomerSubscription { Id = 1, CustomerId = 7, State = SubscriptionStates.Active });

        var subscriptions = await CreateService().GetSubscriptionsAsync("DemoUser@Microsoft.com");

        Assert.Single(subscriptions);
    }
}
