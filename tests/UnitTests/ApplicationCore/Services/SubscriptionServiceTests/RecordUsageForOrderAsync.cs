using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class RecordUsageForOrderAsync
{
    private const string BuyerId = "buyer@example.com";

    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private static Subscription MakeSubscription(SubscriptionStatus status) => new(
        1, BuyerId, BuyerId, "eshop-pro", "Pro Plan", 29900, status, null, null, false, null, null);

    private SubscriptionService CreateSut() => new(_billingClient, _publisher, _logger);

    [Fact]
    public async Task RecordsOneUnit_WhenBuyerHasActiveSubscription()
    {
        _billingClient.ListCustomerSubscriptionsAsync(BuyerId).Returns(new List<Subscription> { MakeSubscription(SubscriptionStatus.Active) });

        var sut = CreateSut();
        await sut.RecordUsageForOrderAsync(BuyerId);

        await _billingClient.Received(1).RecordUsageAsync(1, 1, "Order placed");
    }

    [Fact]
    public async Task DoesNothing_WhenBuyerHasNoActiveSubscription()
    {
        _billingClient.ListCustomerSubscriptionsAsync(BuyerId).Returns(new List<Subscription>());

        var sut = CreateSut();
        await sut.RecordUsageForOrderAsync(BuyerId);

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task DoesNotThrow_WhenProviderCallFails()
    {
        _billingClient.ListCustomerSubscriptionsAsync(BuyerId).Returns(new List<Subscription> { MakeSubscription(SubscriptionStatus.Active) });
        _billingClient.EnsureMeteredComponentConfiguredAsync().Returns(Task.FromException(new BillingProviderException("boom")));

        var sut = CreateSut();

        var exception = await Record.ExceptionAsync(() => sut.RecordUsageForOrderAsync(BuyerId));

        Assert.Null(exception);
    }
}
