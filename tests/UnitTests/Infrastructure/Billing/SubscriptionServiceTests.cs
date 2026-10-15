using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class SubscriptionServiceTests
{
    private static readonly Subscriber Shopper = new("shopper@example.test", "shopper@example.test");
    private static readonly SubscriptionPlan Pro = new("pro", "Pro", null, 29900, 1, "month");

    private static SubscriptionSummary Summary(int id = 10) =>
        new(id, "pro", "Pro", 29900, "active", null, null, null);

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly IMaxioGateway _gateway = Substitute.For<IMaxioGateway>();

    private SubscriptionService NewService(TimeProvider? time = null)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_dbName).Options;
        return new SubscriptionService(new CatalogContext(options), _gateway, NullLogger<SubscriptionService>.Instance, time);
    }

    private CatalogContext NewDb() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_dbName).Options);

    public SubscriptionServiceTests()
    {
        _gateway.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new MaxioPlanListing(new[] { Pro }, true));
        _gateway.FindCustomerIdByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((int?)null);
        _gateway.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(5);
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SubscriptionSummary?)null);
        _gateway.CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Summary());
    }

    [Fact]
    public async Task UnknownPlanIsRejectedBeforeAnyWrite()
    {
        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => NewService().SubscribeAsync(Shopper, "ghost", default));

        Assert.Equal(SubscriptionFailure.PlanNotFound, ex.Failure);
        await _gateway.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!, default!, default!, default!, default);
        await _gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerOnceThenSubscription()
    {
        var result = await NewService().SubscribeAsync(Shopper, "pro", default);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(10, result.Subscription.SubscriptionId);
        await _gateway.Received(1).CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), "shopper@example.test", Arg.Any<CancellationToken>());
        await _gateway.Received(1).CreateSubscriptionAsync(5, "pro", Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(EnrollmentStatus.Active, (await NewDb().SubscriptionEnrollments.SingleAsync()).Status);
    }

    [Fact]
    public async Task SecondSubscribeReturnsTheExistingSubscriptionWithoutAnotherWrite()
    {
        await NewService().SubscribeAsync(Shopper, "pro", default);
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Summary());

        var again = await NewService().SubscribeAsync(Shopper, "pro", default);

        Assert.True(again.AlreadySubscribed);
        await _gateway.Received(1).CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.Received(1).CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConcurrentSubscribeSendsExactlyOneSubscriptionWrite()
    {
        var gate = new TaskCompletionSource();
        _gateway.CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ => { await gate.Task; return Summary(); });
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SubscriptionSummary?)null);

        var first = NewService().SubscribeAsync(Shopper, "pro", default);
        await Task.Delay(300); // first holds the claim and is waiting inside the Maxio call
        var second = await Assert.ThrowsAsync<SubscriptionException>(() => NewService().SubscribeAsync(Shopper, "pro", default));
        gate.SetResult();
        await first;

        Assert.Equal(SubscriptionFailure.InProgress, second.Failure);
        await _gateway.Received(1).CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectionReleasesTheClaimSoTheUserCanRetry()
    {
        _gateway.CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<SubscriptionSummary>(_ => throw new MaxioGatewayException(MaxioFailureKind.Rejected, "no"));

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => NewService().SubscribeAsync(Shopper, "pro", default));

        Assert.Equal(SubscriptionFailure.Rejected, ex.Failure);
        Assert.Empty(await NewDb().SubscriptionEnrollments.ToListAsync());
    }

    [Fact]
    public async Task UnknownOutcomeIsSettledByReReadingMaxioWithTheSentReference()
    {
        string? sentRef = null;
        _gateway.CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Do<string>(r => sentRef = r), Arg.Any<CancellationToken>())
            .Returns<SubscriptionSummary>(_ => throw new MaxioGatewayException(MaxioFailureKind.UnknownOutcome, "reset"));
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Summary(77));

        var result = await NewService().SubscribeAsync(Shopper, "pro", default);

        Assert.Equal(77, result.Subscription.SubscriptionId);
        await _gateway.Received().FindSubscriptionByReferenceAsync(sentRef!, Arg.Any<CancellationToken>());
        var row = await NewDb().SubscriptionEnrollments.SingleAsync();
        Assert.Equal(EnrollmentStatus.Active, row.Status);
        Assert.Equal(77, row.MaxioSubscriptionId);
    }

    [Fact]
    public async Task UnknownOutcomeNotFoundKeepsTheClaimPendingAndLaterReadsSettleIt()
    {
        _gateway.CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<SubscriptionSummary>(_ => throw new MaxioGatewayException(MaxioFailureKind.UnknownOutcome, "reset"));

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => NewService().SubscribeAsync(Shopper, "pro", default));
        Assert.Equal(SubscriptionFailure.InProgress, ex.Failure);
        Assert.Equal(EnrollmentStatus.Pending, (await NewDb().SubscriptionEnrollments.SingleAsync()).Status);

        // Maxio did apply the write after all; my-subscriptions settles the pending row.
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Summary(88));
        _gateway.ListCustomerSubscriptionsAsync(5, Arg.Any<CancellationToken>()).Returns(new[] { Summary(88) });

        var mine = await NewService().ListMySubscriptionsAsync(Shopper.UserName, default);

        Assert.Single(mine);
        Assert.Equal(EnrollmentStatus.Active, (await NewDb().SubscriptionEnrollments.SingleAsync()).Status);
    }

    [Fact]
    public async Task CustomerIsReusedAcrossPlans()
    {
        _gateway.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(new MaxioPlanListing(new[] { Pro, new SubscriptionPlan("basic", "Basic", null, 2900, 1, "month") }, true));
        await NewService().SubscribeAsync(Shopper, "pro", default);
        await NewService().SubscribeAsync(Shopper, "basic", default);

        await _gateway.Received(1).CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(2, await NewDb().SubscriptionEnrollments.CountAsync());
    }

    [Fact]
    public async Task MySubscriptionsIsEmptyForAUserWithoutAMaxioCustomer()
    {
        Assert.Empty(await NewService().ListMySubscriptionsAsync("nobody@example.test", default));
        await _gateway.DidNotReceiveWithAnyArgs().ListCustomerSubscriptionsAsync(default, default);
    }
}
