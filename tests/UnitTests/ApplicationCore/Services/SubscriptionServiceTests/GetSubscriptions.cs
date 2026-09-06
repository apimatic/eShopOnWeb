using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class GetSubscriptions
{
    private readonly ISubscriptionBillingGateway _gateway = Substitute.For<ISubscriptionBillingGateway>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriberIdentity _subscriber = SubscriptionBuilder.Subscriber();

    private SubscriptionService CreateService() => new(_gateway, _logger);

    [Fact]
    public async Task ReturnsNothingForAShopperWhoHasNeverSubscribed()
    {
        _gateway.FindCustomerByReferenceAsync(_subscriber.BillingReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        var subscriptions = await CreateService().GetSubscriptionsAsync(_subscriber);

        Assert.Empty(subscriptions);
        await _gateway.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheShoppersSubscriptionsNewestFirst()
    {
        var older = SubscriptionBuilder.Subscription(id: 1);
        var newer = SubscriptionBuilder.Subscription(id: 2);
        newer = new CustomerSubscription
        {
            Id = newer.Id,
            State = newer.State,
            CustomerId = newer.CustomerId,
            PlanHandle = newer.PlanHandle,
            CreatedAt = older.CreatedAt.AddDays(1)
        };

        _gateway.FindCustomerByReferenceAsync(_subscriber.BillingReference, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Customer());
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[] { older, newer });

        var subscriptions = await CreateService().GetSubscriptionsAsync(_subscriber);

        Assert.Collection(subscriptions,
            first => Assert.Equal(2, first.Id),
            second => Assert.Equal(1, second.Id));
    }

    [Fact]
    public async Task ScopesTheLookupToTheAuthenticatedShopper()
    {
        _gateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        await CreateService().GetSubscriptionsAsync(SubscriptionBuilder.Subscriber("someone.else@microsoft.com"));

        await _gateway.Received(1).FindCustomerByReferenceAsync("eshop:someone.else@microsoft.com",
            Arg.Any<CancellationToken>());
    }
}
