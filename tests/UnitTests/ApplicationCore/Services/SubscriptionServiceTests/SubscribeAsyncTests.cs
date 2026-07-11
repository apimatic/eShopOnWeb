using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscribeAsyncTests
{
    private readonly IBillingClient _mockBillingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _mockPublisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriptionBuilder _builder = new();

    private SubscriptionService CreateService() => new(_mockBillingClient, _mockPublisher, _mockLogger);

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExists()
    {
        _mockBillingClient.FindSubscriptionByCustomerReferenceAsync(SubscriptionBuilder.TestBuyerId, Arg.Any<CancellationToken>())
            .Returns((Subscription?)null);
        _mockBillingClient.EnsureCustomerAsync(SubscriptionBuilder.TestBuyerId, SubscriptionBuilder.TestBuyerId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(1, SubscriptionBuilder.TestBuyerId, SubscriptionBuilder.TestBuyerId));
        var created = _builder.Active();
        _mockBillingClient.CreateSubscriptionAsync(SubscriptionBuilder.TestBuyerId, SubscriptionBuilder.TestProductHandle, Arg.Any<CancellationToken>())
            .Returns(created);

        var service = CreateService();
        var result = await service.SubscribeAsync(SubscriptionBuilder.TestBuyerId, SubscriptionBuilder.TestProductHandle);

        Assert.Equal(created.Id, result.Id);
        await _mockBillingClient.Received(1).EnsureCustomerAsync(SubscriptionBuilder.TestBuyerId, SubscriptionBuilder.TestBuyerId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _mockBillingClient.Received(1).CreateSubscriptionAsync(SubscriptionBuilder.TestBuyerId, SubscriptionBuilder.TestProductHandle, Arg.Any<CancellationToken>());
        await _mockPublisher.Received(1).Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingSubscriptionWithoutCreatingANewOne()
    {
        var existing = _builder.Active();
        _mockBillingClient.FindSubscriptionByCustomerReferenceAsync(SubscriptionBuilder.TestBuyerId, Arg.Any<CancellationToken>())
            .Returns(existing);

        var service = CreateService();
        var result = await service.SubscribeAsync(SubscriptionBuilder.TestBuyerId, SubscriptionBuilder.TestProductHandle);

        Assert.Equal(existing.Id, result.Id);
        await _mockBillingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _mockPublisher.DidNotReceive().Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatesANewSubscriptionWhenExistingOneIsCanceled()
    {
        var canceled = _builder.WithState("canceled");
        _mockBillingClient.FindSubscriptionByCustomerReferenceAsync(SubscriptionBuilder.TestBuyerId, Arg.Any<CancellationToken>())
            .Returns(canceled);
        _mockBillingClient.EnsureCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(1, SubscriptionBuilder.TestBuyerId, SubscriptionBuilder.TestBuyerId));
        var recreated = _builder.Active();
        _mockBillingClient.CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(recreated);

        var service = CreateService();
        await service.SubscribeAsync(SubscriptionBuilder.TestBuyerId, SubscriptionBuilder.TestProductHandle);

        await _mockBillingClient.Received(1).CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
