using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscribeAsync
{
    private const string BuyerId = "buyer@example.com";
    private const string ProductHandle = "eshop-pro";

    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private static BillingPlan Plan(string handle = ProductHandle) => new(handle, "Pro Plan", 29900, 1, BillingIntervalUnit.Month, false);

    private static Subscription MakeSubscription(int id, string buyerId, SubscriptionStatus status, string productHandle = ProductHandle) => new(
        id, buyerId, buyerId, productHandle, "Pro Plan", 29900, status, null, null, false, null, null);

    private SubscriptionService CreateSut() => new(_billingClient, _publisher, _logger);

    [Fact]
    public async Task CreatesNewSubscription_WhenNoneExists()
    {
        _billingClient.ListPlansAsync().Returns(new List<BillingPlan> { Plan() });
        _billingClient.ListCustomerSubscriptionsAsync(BuyerId).Returns(new List<Subscription>());
        var created = MakeSubscription(1, BuyerId, SubscriptionStatus.Active);
        _billingClient.CreateSubscriptionAsync(BuyerId, ProductHandle).Returns(created);

        var sut = CreateSut();
        var result = await sut.SubscribeAsync(BuyerId, BuyerId, ProductHandle);

        Assert.Equal(created.Id, result.Id);
        await _billingClient.Received(1).EnsureCustomerAsync(BuyerId, BuyerId);
        await _billingClient.Received(1).CreateSubscriptionAsync(BuyerId, ProductHandle);
        await _publisher.Received(1).Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingActiveSubscription_WhenAlreadySubscribed_AndDoesNotCreateAnother()
    {
        _billingClient.ListPlansAsync().Returns(new List<BillingPlan> { Plan() });
        var existing = MakeSubscription(1, BuyerId, SubscriptionStatus.Active);
        _billingClient.ListCustomerSubscriptionsAsync(BuyerId).Returns(new List<Subscription> { existing });

        var sut = CreateSut();
        var result = await sut.SubscribeAsync(BuyerId, BuyerId, ProductHandle);

        Assert.Equal(existing.Id, result.Id);
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Throws_WhenProductHandleDoesNotResolve()
    {
        _billingClient.ListPlansAsync().Returns(new List<BillingPlan> { Plan("basic-plan") });

        var sut = CreateSut();

        await Assert.ThrowsAsync<BillingProviderException>(() => sut.SubscribeAsync(BuyerId, BuyerId, "unknown-handle"));
        await _billingClient.DidNotReceive().EnsureCustomerAsync(Arg.Any<string>(), Arg.Any<string>());
    }
}
