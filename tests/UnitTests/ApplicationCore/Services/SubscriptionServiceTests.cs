using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class SubscriptionServiceTests
{
    private static readonly SubscriptionPlan Pro = new("eshop-pro", "Pro", null, 299m, 1, "month");

    private static SubscriptionInfo Sub(string state = "active") =>
        new("1", state, "eshop-pro", "Pro", 299m, 1, "month", null);

    private readonly IBillingGateway _gateway = Substitute.For<IBillingGateway>();
    private readonly SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        _service = new SubscriptionService(_gateway);
        _gateway.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new[] { Pro });
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNew()
    {
        _gateway.FindCustomerIdAsync("u@x.com", Arg.Any<CancellationToken>()).Returns((int?)null);
        _gateway.CreateCustomerAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>()).Returns(7);
        _gateway.ListSubscriptionsAsync(7, Arg.Any<CancellationToken>()).Returns(new SubscriptionInfo[0]);
        _gateway.CreateSubscriptionAsync(7, "eshop-pro", Arg.Any<CancellationToken>()).Returns(Sub());

        var result = await _service.SubscribeAsync("u@x.com", null, "eshop-pro");

        Assert.True(result.Created);
        await _gateway.Received(1).CreateCustomerAsync(
            Arg.Is<BillingCustomer>(c => c.Reference == "u@x.com" && c.Email == "u@x.com"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_IsIdempotent_WhenLiveSubscriptionExists()
    {
        _gateway.FindCustomerIdAsync("u@x.com", Arg.Any<CancellationToken>()).Returns(7);
        _gateway.ListSubscriptionsAsync(7, Arg.Any<CancellationToken>()).Returns(new[] { Sub() });

        var result = await _service.SubscribeAsync("u@x.com", null, "eshop-pro");

        Assert.False(result.Created);
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_CreatesNew_WhenPreviousSubscriptionCanceled()
    {
        _gateway.FindCustomerIdAsync("u@x.com", Arg.Any<CancellationToken>()).Returns(7);
        _gateway.ListSubscriptionsAsync(7, Arg.Any<CancellationToken>()).Returns(new[] { Sub("canceled") });
        _gateway.CreateSubscriptionAsync(7, "eshop-pro", Arg.Any<CancellationToken>()).Returns(Sub());

        var result = await _service.SubscribeAsync("u@x.com", null, "eshop-pro");

        Assert.True(result.Created);
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_IsNotFound()
    {
        var ex = await Assert.ThrowsAsync<BillingException>(() => _service.SubscribeAsync("u@x.com", null, "nope"));

        Assert.Equal(BillingFailureKind.NotFound, ex.Kind);
        await _gateway.DidNotReceive().CreateCustomerAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ReconcilesAfterTransportFailure()
    {
        _gateway.FindCustomerIdAsync("u@x.com", Arg.Any<CancellationToken>()).Returns(7);
        _gateway.ListSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionInfo[0], new[] { Sub() });
        _gateway.CreateSubscriptionAsync(7, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns<Task<SubscriptionInfo>>(_ => throw new BillingException(BillingFailureKind.Unavailable, "down"));

        var result = await _service.SubscribeAsync("u@x.com", null, "eshop-pro");

        Assert.Equal("1", result.Subscription.SubscriptionId);
    }

    [Fact]
    public async Task Subscribe_TransportFailureWithNothingCreated_Propagates()
    {
        _gateway.FindCustomerIdAsync("u@x.com", Arg.Any<CancellationToken>()).Returns(7);
        _gateway.ListSubscriptionsAsync(7, Arg.Any<CancellationToken>()).Returns(new SubscriptionInfo[0]);
        _gateway.CreateSubscriptionAsync(7, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns<Task<SubscriptionInfo>>(_ => throw new BillingException(BillingFailureKind.Unavailable, "down"));

        var ex = await Assert.ThrowsAsync<BillingException>(() => _service.SubscribeAsync("u@x.com", null, "eshop-pro"));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
    }

    [Fact]
    public async Task MySubscriptions_Empty_WhenNoCustomer()
    {
        _gateway.FindCustomerIdAsync("u@x.com", Arg.Any<CancellationToken>()).Returns((int?)null);

        Assert.Empty(await _service.GetMySubscriptionsAsync("u@x.com"));
    }
}
