using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string UserKey = "demouser@microsoft.com";
    private const string ProductHandle = "eshop-pro";

    private readonly IMaxioApiClient _maxio = Substitute.For<IMaxioApiClient>();

    private MaxioSubscriptionService CreateService() =>
        new(_maxio, Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" }), NullLogger<MaxioSubscriptionService>.Instance);

    private static MaxioCustomer TestCustomer(int id = 1001) =>
        new() { Id = id, FirstName = "Demo", LastName = "User", Email = UserKey, Reference = $"eshopweb-user:{UserKey}" };

    private static MaxioSubscription TestSubscription(int id = 5001, string? reference = null) =>
        new()
        {
            Id = id,
            State = "active",
            Reference = reference,
            ProductPriceInCents = 29900,
            CurrentPeriodEndsAt = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc),
            ActivatedAt = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc),
            Customer = TestCustomer(),
            Product = new MaxioProduct { Id = 42, Handle = ProductHandle, Name = "Pro Plan", Interval = 1, IntervalUnit = "month" }
        };

    [Fact]
    public async Task SubscribeAsync_WhenNew_CreatesCustomerAndSubscriptionWithStableReferences()
    {
        _maxio.GetSubscriptionByReferenceAsync(Arg.Any<string>()).Returns((MaxioSubscription?)null);
        _maxio.GetCustomerByReferenceAsync(Arg.Any<string>()).Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>()).Returns(TestCustomer());
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>()).Returns(TestSubscription());

        var service = CreateService();
        var result = await service.SubscribeAsync(UserKey, UserKey, ProductHandle);

        Assert.True(result.Created);
        Assert.Equal(5001, result.Subscription.SubscriptionId);
        Assert.Equal("active", result.Subscription.State);

        await _maxio.Received(1).CreateCustomerAsync(Arg.Is<MaxioCreateCustomer>(c =>
            c.Reference == $"eshopweb-user:{UserKey}" &&
            c.Email == UserKey));
        await _maxio.Received(1).CreateSubscriptionAsync(Arg.Is<MaxioCreateSubscription>(s =>
            s.ProductHandle == ProductHandle &&
            s.CustomerId == 1001 &&
            s.Reference == $"eshopweb-sub:{UserKey}:{ProductHandle}"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenAlreadySubscribed_ReturnsExistingWithoutCreatingAnything()
    {
        var reference = $"eshopweb-sub:{UserKey}:{ProductHandle}";
        _maxio.GetSubscriptionByReferenceAsync(reference).Returns(TestSubscription(reference: reference));

        var service = CreateService();
        var result = await service.SubscribeAsync(UserKey, UserKey, ProductHandle);

        Assert.False(result.Created);
        Assert.Equal(reference, result.Subscription.SubscriptionReference);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>());
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>());
    }

    [Fact]
    public async Task SubscribeAsync_WhenCustomerExists_DoesNotCreateAnotherCustomer()
    {
        _maxio.GetSubscriptionByReferenceAsync(Arg.Any<string>()).Returns((MaxioSubscription?)null);
        _maxio.GetCustomerByReferenceAsync($"eshopweb-user:{UserKey}").Returns(TestCustomer());
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>()).Returns(TestSubscription());

        var service = CreateService();
        var result = await service.SubscribeAsync(UserKey, UserKey, ProductHandle);

        Assert.True(result.Created);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>());
        await _maxio.Received(1).CreateSubscriptionAsync(Arg.Is<MaxioCreateSubscription>(s => s.CustomerId == 1001));
    }

    [Fact]
    public async Task SubscribeAsync_WhenCustomerCreateLosesReferenceRace_RecoversExistingCustomer()
    {
        _maxio.GetSubscriptionByReferenceAsync(Arg.Any<string>()).Returns((MaxioSubscription?)null);
        _maxio.GetCustomerByReferenceAsync(Arg.Any<string>()).Returns(
            (MaxioCustomer?)null,
            TestCustomer());
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>())
            .Returns(Task.FromException<MaxioCustomer>(new BillingException(
                "Customer Reference: must be unique - that value has been taken.",
                422,
                new[] { "Customer Reference: must be unique - that value has been taken." })));
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>()).Returns(TestSubscription());

        var service = CreateService();
        var result = await service.SubscribeAsync(UserKey, UserKey, ProductHandle);

        Assert.True(result.Created);
        await _maxio.Received(1).CreateSubscriptionAsync(Arg.Is<MaxioCreateSubscription>(s => s.CustomerId == 1001));
    }

    [Fact]
    public async Task ListUserSubscriptionsAsync_WhenCustomerUnknown_ReturnsEmpty()
    {
        _maxio.GetCustomerByReferenceAsync(Arg.Any<string>()).Returns((MaxioCustomer?)null);

        var service = CreateService();
        var subscriptions = await service.ListUserSubscriptionsAsync(UserKey, UserKey);

        Assert.Empty(subscriptions);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task ListUserSubscriptionsAsync_MapsMaxioSubscriptions()
    {
        _maxio.GetCustomerByReferenceAsync($"eshopweb-user:{UserKey}").Returns(TestCustomer());
        _maxio.ListCustomerSubscriptionsAsync(1001).Returns(
            new List<MaxioSubscription> { TestSubscription(reference: "eshopweb-sub:demo:eshop-pro") });

        var service = CreateService();
        var subscriptions = await service.ListUserSubscriptionsAsync(UserKey, UserKey);

        var single = Assert.Single(subscriptions);
        Assert.Equal(5001, single.SubscriptionId);
        Assert.Equal(ProductHandle, single.ProductHandle);
        Assert.Equal("Pro Plan", single.ProductName);
        Assert.Equal(29900, single.PriceInCents);
        Assert.Equal("active", single.State);
        Assert.Equal(new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc), single.NextBillingDate);
    }
}
