using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class SubscriptionBillingServiceTests
{
    private readonly IMaxioClient _maxioClient = Substitute.For<IMaxioClient>();
    private readonly MaxioOptions _options = new() { ProductFamilyHandle = "eshop-subscribe" };

    private SubscriptionBillingService CreateSut() => new(_maxioClient, _options);

    private static MaxioProduct Plan(string handle, string family, long priceInCents = 1000, DateTimeOffset? archivedAt = null) =>
        new(1, handle, handle, "description", priceInCents, 1, "month", family, archivedAt);

    [Fact]
    public async Task GetAvailablePlansAsync_OnlyReturnsNonArchivedPlansFromConfiguredFamily()
    {
        _maxioClient.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            Plan("eshop-pro", "eshop-subscribe"),
            Plan("other-family-plan", "some-other-family"),
            Plan("archived-plan", "eshop-subscribe", archivedAt: DateTimeOffset.UtcNow)
        });

        var plans = await CreateSut().GetAvailablePlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsWhenPlanHandleIsUnknown()
    {
        _maxioClient.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct> { Plan("eshop-pro", "eshop-subscribe") });

        var sut = CreateSut();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            sut.SubscribeAsync(new SubscribeToPlanRequest("user-1", "user@example.com", "First", "Last", "no-such-plan")));
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerWhenNoneExistsForReference()
    {
        _maxioClient.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct> { Plan("eshop-pro", "eshop-subscribe") });
        _maxioClient.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _maxioClient.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(42, "user-1", "user@example.com", "First", "Last"));
        _maxioClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _maxioClient.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription(99, "active", 42, "eshop-pro", "Pro Plan", 29900, DateTimeOffset.UtcNow, null, null));

        var enrollment = await CreateSut().SubscribeAsync(
            new SubscribeToPlanRequest("user-1", "user@example.com", "First", "Last", "eshop-pro"));

        await _maxioClient.Received(1).CreateCustomerAsync(Arg.Is<MaxioCreateCustomer>(c => c.Reference == "user-1"), Arg.Any<CancellationToken>());
        await _maxioClient.Received(1).CreateSubscriptionAsync(Arg.Is<MaxioCreateSubscription>(s => s.CustomerId == 42 && s.ProductHandle == "eshop-pro"), Arg.Any<CancellationToken>());
        Assert.False(enrollment.AlreadyExisted);
        Assert.Equal(99, enrollment.SubscriptionId);
    }

    [Fact]
    public async Task SubscribeAsync_ReusesExistingCustomerWithoutCreatingANewOne()
    {
        _maxioClient.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct> { Plan("eshop-pro", "eshop-subscribe") });
        _maxioClient.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(42, "user-1", "user@example.com", "First", "Last"));
        _maxioClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _maxioClient.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription(99, "active", 42, "eshop-pro", "Pro Plan", 29900, DateTimeOffset.UtcNow, null, null));

        await CreateSut().SubscribeAsync(new SubscribeToPlanRequest("user-1", "user@example.com", "First", "Last", "eshop-pro"));

        await _maxioClient.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingLiveSubscriptionInsteadOfCreatingADuplicate()
    {
        var existing = new MaxioSubscription(77, "active", 42, "eshop-pro", "Pro Plan", 29900, DateTimeOffset.UtcNow, null, null);

        _maxioClient.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct> { Plan("eshop-pro", "eshop-subscribe") });
        _maxioClient.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(42, "user-1", "user@example.com", "First", "Last"));
        _maxioClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription> { existing });

        var enrollment = await CreateSut().SubscribeAsync(new SubscribeToPlanRequest("user-1", "user@example.com", "First", "Last", "eshop-pro"));

        await _maxioClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
        Assert.True(enrollment.AlreadyExisted);
        Assert.Equal(77, enrollment.SubscriptionId);
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    public async Task SubscribeAsync_CreatesANewSubscriptionWhenThePriorOneForThatPlanIsTerminal(string terminalState)
    {
        var terminated = new MaxioSubscription(77, terminalState, 42, "eshop-pro", "Pro Plan", 29900, DateTimeOffset.UtcNow, null, null);

        _maxioClient.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct> { Plan("eshop-pro", "eshop-subscribe") });
        _maxioClient.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(42, "user-1", "user@example.com", "First", "Last"));
        _maxioClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription> { terminated });
        _maxioClient.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription(99, "active", 42, "eshop-pro", "Pro Plan", 29900, DateTimeOffset.UtcNow, null, null));

        var enrollment = await CreateSut().SubscribeAsync(new SubscribeToPlanRequest("user-1", "user@example.com", "First", "Last", "eshop-pro"));

        await _maxioClient.Received(1).CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
        Assert.False(enrollment.AlreadyExisted);
    }

    [Fact]
    public async Task GetSubscriptionsForCustomerAsync_ReturnsEmptyWhenNoMaxioCustomerExistsYet()
    {
        _maxioClient.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var subscriptions = await CreateSut().GetSubscriptionsForCustomerAsync("user-1");

        Assert.Empty(subscriptions);
        await _maxioClient.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
