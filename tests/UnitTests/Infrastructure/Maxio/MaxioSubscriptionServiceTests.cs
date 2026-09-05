using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly IUserOperationLock _userLock = Substitute.For<IUserOperationLock>();
    private readonly MaxioSubscriptionService _sut;

    public MaxioSubscriptionServiceTests()
    {
        _userLock.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<System.IDisposable>());
        _sut = new MaxioSubscriptionService(_client, _userLock);
    }

    private static MaxioProductWire ProProduct() => new()
    {
        Id = 1,
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        RequireCreditCard = false
    };

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenUserHasNeither()
    {
        _client.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(ProProduct());
        _client.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns((MaxioCustomerWire?)null);
        _client.CreateCustomerAsync("user-1", "user1@test.com", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerWire { Id = 123, Reference = "user-1" });
        _client.ListSubscriptionsForCustomerAsync(123, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionWire>());
        _client.CreateSubscriptionAsync("user-1", "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscriptionWire { Id = 555, State = "active", Product = ProProduct(), ProductPriceInCents = 29900 });

        var result = await _sut.SubscribeAsync("user-1", "user1@test.com", "eshop-pro");

        Assert.Equal(555, result.SubscriptionId);
        Assert.Equal("active", result.State);
        await _client.Received(1).CreateCustomerAsync("user-1", "user1@test.com", Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync("user-1", "eshop-pro", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingLiveSubscription_InsteadOfCreatingDuplicate()
    {
        _client.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(ProProduct());
        _client.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerWire { Id = 123, Reference = "user-1" });
        _client.ListSubscriptionsForCustomerAsync(123, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionWire>
            {
                new() { Id = 999, State = "active", Product = ProProduct(), ProductPriceInCents = 29900 }
            });

        var result = await _sut.SubscribeAsync("user-1", "user1@test.com", "eshop-pro");

        Assert.Equal(999, result.SubscriptionId);
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesNewSubscription_WhenExistingOneForThatPlanIsCanceled()
    {
        _client.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(ProProduct());
        _client.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerWire { Id = 123, Reference = "user-1" });
        _client.ListSubscriptionsForCustomerAsync(123, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionWire>
            {
                new() { Id = 111, State = "canceled", Product = ProProduct(), ProductPriceInCents = 29900 }
            });
        _client.CreateSubscriptionAsync("user-1", "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscriptionWire { Id = 222, State = "active", Product = ProProduct(), ProductPriceInCents = 29900 });

        var result = await _sut.SubscribeAsync("user-1", "user1@test.com", "eshop-pro");

        Assert.Equal(222, result.SubscriptionId);
    }

    [Fact]
    public async Task SubscribeAsync_Throws_WhenPlanHandleIsUnknown()
    {
        _client.GetProductByHandleAsync("does-not-exist", Arg.Any<CancellationToken>()).Returns((MaxioProductWire?)null);

        await Assert.ThrowsAsync<MaxioPlanNotFoundException>(
            () => _sut.SubscribeAsync("user-1", "user1@test.com", "does-not-exist"));
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_ReturnsEmpty_WhenUserHasNoMaxioCustomerYet()
    {
        _client.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns((MaxioCustomerWire?)null);

        var result = await _sut.GetSubscriptionsForUserAsync("user-1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindOrCreateCustomer_RecoversViaLookup_WhenCreateRacesAnotherRequest()
    {
        _client.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(ProProduct());
        _client.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomerWire?)null, new MaxioCustomerWire { Id = 123, Reference = "user-1" });
        _client.CreateCustomerAsync("user-1", "user1@test.com", Arg.Any<CancellationToken>())
            .Returns<MaxioCustomerWire>(_ => throw new MaxioApiException(System.Net.HttpStatusCode.UnprocessableEntity, "duplicate reference"));
        _client.ListSubscriptionsForCustomerAsync(123, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionWire>());
        _client.CreateSubscriptionAsync("user-1", "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscriptionWire { Id = 555, State = "active", Product = ProProduct(), ProductPriceInCents = 29900 });

        var result = await _sut.SubscribeAsync("user-1", "user1@test.com", "eshop-pro");

        Assert.Equal(555, result.SubscriptionId);
    }
}
