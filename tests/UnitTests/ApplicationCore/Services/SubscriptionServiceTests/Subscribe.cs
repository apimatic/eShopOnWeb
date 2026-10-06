using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    private const string USER_ID = SubscriptionServiceHarness.USER_ID;
    private const string PLAN_HANDLE = SubscriptionServiceHarness.PLAN_HANDLE;

    [Fact]
    public async Task FirstSubscribeEnsuresCustomerAndEnrollsUserWithDeterministicReferences()
    {
        var harness = new SubscriptionServiceHarness();
        harness.Maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Microsoft.eShopWeb.ApplicationCore.Maxio.MaxioSubscription?)null);
        harness.Maxio.EnsureCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(harness.Customer);
        harness.Maxio.SubscribeAsync(harness.Customer.CustomerId, PLAN_HANDLE, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(harness.LiveSubscription());

        var result = await harness.CreateService().SubscribeAsync(USER_ID, PLAN_HANDLE);

        Assert.True(result.Created);
        Assert.Equal(94685018, result.Subscription.MaxioSubscriptionId);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(29900, result.Subscription.PriceInCents);

        await harness.Maxio.Received(1).EnsureCustomerAsync(
            $"eshoponweb-user:{USER_ID}", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await harness.Maxio.Received(1).SubscribeAsync(
            harness.Customer.CustomerId, PLAN_HANDLE, $"eshoponweb-sub:{USER_ID}:{PLAN_HANDLE}", Arg.Any<CancellationToken>());

        Assert.Single(harness.Store);
        var mirror = harness.Store.Single();
        Assert.Equal(USER_ID, mirror.UserId);
        Assert.Equal(harness.Customer.CustomerId, mirror.MaxioCustomerId);
        Assert.Equal(PLAN_HANDLE, mirror.PlanHandle);
        Assert.NotNull(mirror.NextBillingAtUtc);
    }

    [Fact]
    public async Task DoubleClickReplaysTheLiveSubscriptionAndNeverCreatesASecondOne()
    {
        var harness = new SubscriptionServiceHarness();
        var live = harness.LiveSubscription();
        harness.Maxio.FindSubscriptionByReferenceAsync($"eshoponweb-sub:{USER_ID}:{PLAN_HANDLE}", Arg.Any<CancellationToken>())
            .Returns(live);
        harness.Maxio.EnsureCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(harness.Customer);

        var first = await harness.CreateService().SubscribeAsync(USER_ID, PLAN_HANDLE);
        var second = await harness.CreateService().SubscribeAsync(USER_ID, PLAN_HANDLE);

        Assert.False(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Subscription.MaxioSubscriptionId, second.Subscription.MaxioSubscriptionId);

        // The enrollment endpoint was never called, so no second subscription can exist.
        await harness.Maxio.DidNotReceiveWithAnyArgs().SubscribeAsync(default, default!, default!, default);
        // ...and the customer was ensured (idempotently), never blindly created twice.
        await harness.Maxio.Received(2).EnsureCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Single(harness.Store);
    }

    [Fact]
    public async Task ConcurrentRaceOnUniqueReferenceResolvesToTheWinningSubscription()
    {
        var harness = new SubscriptionServiceHarness();
        harness.Maxio.FindSubscriptionByReferenceAsync($"eshoponweb-sub:{USER_ID}:{PLAN_HANDLE}", Arg.Any<CancellationToken>())
            .Returns((Microsoft.eShopWeb.ApplicationCore.Maxio.MaxioSubscription?)null);
        harness.Maxio.EnsureCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(harness.Customer);
        // Maxio answered 422 "Reference: must be unique" -> client returns null...
        harness.Maxio.SubscribeAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Microsoft.eShopWeb.ApplicationCore.Maxio.MaxioSubscription?)null);
        // ...and the race resolution lookup finds the subscription the other request created.
        harness.Maxio.FindSubscriptionByReferenceAsync($"eshoponweb-sub:{USER_ID}:{PLAN_HANDLE}", Arg.Any<CancellationToken>())
            .Returns(harness.LiveSubscription());

        var result = await harness.CreateService().SubscribeAsync(USER_ID, PLAN_HANDLE);

        Assert.Equal(94685018, result.Subscription.MaxioSubscriptionId);
        Assert.Equal("eshoponweb-sub:demouser@microsoft.com:eshop-pro", result.Subscription.SubscriptionReference);
    }

    [Fact]
    public async Task ResubscribingAfterCancellationCreatesANewEnrollmentWithANewReference()
    {
        var harness = new SubscriptionServiceHarness();
        var canceled = harness.LiveSubscription(state: "canceled");
        harness.Maxio.FindSubscriptionByReferenceAsync($"eshoponweb-sub:{USER_ID}:{PLAN_HANDLE}", Arg.Any<CancellationToken>())
            .Returns(canceled);
        harness.Maxio.EnsureCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(harness.Customer);
        harness.Maxio.SubscribeAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(harness.LiveSubscription(id: 99999));

        var result = await harness.CreateService().SubscribeAsync(USER_ID, PLAN_HANDLE);

        Assert.True(result.Created);
        Assert.Equal(99999, result.Subscription.MaxioSubscriptionId);
        await harness.Maxio.Received(1).SubscribeAsync(
            Arg.Any<long>(),
            PLAN_HANDLE,
            Arg.Is<string>(r => r.StartsWith($"eshoponweb-sub:{USER_ID}:{PLAN_HANDLE}:")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownPlanIsRejectedBeforeTouchingTheBillingProvider()
    {
        var harness = new SubscriptionServiceHarness();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => harness.CreateService().SubscribeAsync(USER_ID, "no-such-plan"));

        await harness.Maxio.DidNotReceiveWithAnyArgs().EnsureCustomerAsync(default!, default!, default!, default!, default);
        await harness.Maxio.DidNotReceiveWithAnyArgs().SubscribeAsync(default, default!, default!, default);
    }
}
