using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscriptionServiceTests
{
    private const string UserName = "demouser@microsoft.com";
    private const string CatalogKey = "test-family";
    private const int CustomerId = 501;

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly ISubscriptionBillingGateway _gateway = Substitute.For<ISubscriptionBillingGateway>();
    private readonly ISubscriberDirectory _directory = Substitute.For<ISubscriberDirectory>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly BillingReferences _references = BillingReferences.For(UserName, CatalogKey);

    public SubscriptionServiceTests()
    {
        _gateway.CatalogKey.Returns(CatalogKey);
        _gateway.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new PlanCatalog(
            new[]
            {
                new SubscriptionPlan("pro", "Pro Plan", null, 29900, 1, "month"),
                new SubscriptionPlan("basic", "Basic Plan", null, 2900, 1, "month")
            }, IsComplete: true, DefaultPlanHandle: "pro"));
        _directory.FindByUserNameAsync(UserName).Returns(new Subscriber(UserName, UserName));
        _gateway.EnsureCustomerAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>()).Returns(CustomerId);
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BillingSubscription?)null);
    }

    private CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_databaseName).Options);

    /// <summary>Each service gets its own DbContext, like two separate HTTP requests.</summary>
    private SubscriptionService NewService() =>
        new(_gateway, new EfSubscriptionEnrollmentStore(NewContext()), _directory, _clock, _logger);

    private static BillingSubscription Subscription(string reference, string plan = "pro") =>
        new(9001, reference, plan, "Pro Plan", "active", 29900, 1, "month",
            DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private SubscriptionEnrollment? StoredClaim()
    {
        using var context = NewContext();
        return context.SubscriptionEnrollments.AsNoTracking().SingleOrDefault();
    }

    [Fact]
    public async Task Subscribe_EnsuresCustomerAndCreatesSubscription()
    {
        _gateway.CreateSubscriptionAsync(CustomerId, "pro", _references.SubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(_references.SubscriptionReference));

        var result = await NewService().SubscribeAsync(UserName, "pro", default);

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(9001, result.Subscription!.Id);
        await _gateway.Received(1).EnsureCustomerAsync(
            Arg.Is<BillingCustomer>(c => c.Reference == _references.CustomerReference && c.Email == UserName), Arg.Any<CancellationToken>());
        var claim = StoredClaim();
        Assert.Equal(SubscriptionEnrollmentStatus.Completed, claim!.Status);
        Assert.Equal(9001, claim.BillingSubscriptionId);
    }

    [Fact]
    public async Task Subscribe_UsesConfiguredDefaultPlan_WhenNoneRequested()
    {
        _gateway.CreateSubscriptionAsync(CustomerId, "pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(_references.SubscriptionReference));

        var result = await NewService().SubscribeAsync(UserName, null, default);

        Assert.Equal("pro", result.PlanHandle);
    }

    [Fact]
    public async Task Subscribe_RejectsPlanNotInCatalog_WithoutCallingProviderWrites()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotOfferedException>(() => NewService().SubscribeAsync(UserName, "gold", default));

        await _gateway.DidNotReceiveWithAnyArgs().EnsureCustomerAsync(default!, default);
        await _gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default!, default);
        Assert.Null(StoredClaim());
    }

    [Fact]
    public async Task Subscribe_PlanMissingFromIncompleteCatalog_IsUnavailableNotRejected()
    {
        _gateway.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new PlanCatalog(
            new[] { new SubscriptionPlan("pro", "Pro Plan", null, 29900, 1, "month") }, IsComplete: false, DefaultPlanHandle: null));

        await Assert.ThrowsAsync<BillingUnavailableException>(() => NewService().SubscribeAsync(UserName, "gold", default));
    }

    [Fact]
    public async Task Subscribe_WhileAnotherRequestHoldsTheClaim_IsRefusedBeforeReachingProvider()
    {
        // First request is mid-flight: its create has not returned yet.
        var release = new TaskCompletionSource<BillingSubscription>();
        _gateway.CreateSubscriptionAsync(CustomerId, "pro", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(release.Task);
        var first = NewService().SubscribeAsync(UserName, "pro", default);

        await Assert.ThrowsAsync<DuplicateException>(() => NewService().SubscribeAsync(UserName, "pro", default));

        release.SetResult(Subscription(_references.SubscriptionReference));
        Assert.Equal(SubscribeOutcome.Created, (await first).Outcome);
        await _gateway.Received(1).CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.Received(1).EnsureCustomerAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_Again_ReturnsExistingSubscription_WithoutCreating()
    {
        var existing = Subscription(_references.SubscriptionReference);
        _gateway.CreateSubscriptionAsync(CustomerId, "pro", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(existing);
        await NewService().SubscribeAsync(UserName, "pro", default);
        _gateway.FindSubscriptionByReferenceAsync(_references.SubscriptionReference, Arg.Any<CancellationToken>()).Returns(existing);

        var again = await NewService().SubscribeAsync(UserName, "pro", default);

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, again.Outcome);
        Assert.Equal(existing.Id, again.Subscription!.Id);
        await _gateway.Received(1).CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ToDifferentPlanWhenAlreadySubscribed_IsConflict()
    {
        _gateway.CreateSubscriptionAsync(CustomerId, "pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(_references.SubscriptionReference));
        await NewService().SubscribeAsync(UserName, "pro", default);

        await Assert.ThrowsAsync<DuplicateException>(() => NewService().SubscribeAsync(UserName, "basic", default));
    }

    [Fact]
    public async Task Subscribe_WhenProviderAlreadyHoldsReference_DoesNotCreateAgain()
    {
        // e.g. the local claim was lost on restart, but the provider kept the subscription.
        _gateway.FindSubscriptionByReferenceAsync(_references.SubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(_references.SubscriptionReference));

        var result = await NewService().SubscribeAsync(UserName, "pro", default);

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        await _gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Subscribe_ProviderRejects_ReleasesClaimSoTheShopperCanRetry()
    {
        _gateway.CreateSubscriptionAsync(CustomerId, "pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingRejectedException("The billing provider rejected the subscription: nope"));

        await Assert.ThrowsAsync<BillingRejectedException>(() => NewService().SubscribeAsync(UserName, "pro", default));

        Assert.Null(StoredClaim());
    }

    [Fact]
    public async Task Subscribe_OutcomeUnknown_ReturnsPending_AndKeepsClaimForSettlement()
    {
        _gateway.CreateSubscriptionAsync(CustomerId, "pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingOutcomeUnknownException("unknown"));

        var result = await NewService().SubscribeAsync(UserName, "pro", default);

        Assert.Equal(SubscribeOutcome.Pending, result.Outcome);
        Assert.Equal(SubscriptionEnrollmentStatus.OutcomeUnknown, StoredClaim()!.Status);
    }

    [Fact]
    public async Task UnknownClaim_IsSettledOnNextRead()
    {
        _gateway.CreateSubscriptionAsync(CustomerId, "pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingOutcomeUnknownException("unknown"));
        await NewService().SubscribeAsync(UserName, "pro", default);

        // The create had landed after all.
        var landed = Subscription(_references.SubscriptionReference);
        _gateway.FindSubscriptionByReferenceAsync(_references.SubscriptionReference, Arg.Any<CancellationToken>()).Returns(landed);
        _gateway.ListSubscriptionsForCustomerAsync(CustomerId, Arg.Any<CancellationToken>()).Returns(new[] { landed });

        var mine = await NewService().GetSubscriptionsAsync(UserName, default);

        Assert.Null(mine.PendingPlanHandle);
        Assert.Single(mine.Subscriptions);
        var claim = StoredClaim()!;
        Assert.Equal(SubscriptionEnrollmentStatus.Completed, claim.Status);
        Assert.Equal(landed.Id, claim.BillingSubscriptionId);
    }

    [Fact]
    public async Task UnknownClaim_NotFoundAndRecent_StaysPending_WithoutSecondCreate()
    {
        _gateway.CreateSubscriptionAsync(CustomerId, "pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingOutcomeUnknownException("unknown"));
        await NewService().SubscribeAsync(UserName, "pro", default);

        var retry = await NewService().SubscribeAsync(UserName, "pro", default);

        Assert.Equal(SubscribeOutcome.Pending, retry.Outcome);
        await _gateway.Received(1).CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownClaim_NotFoundAfterLease_IsTakenOverAndRetried()
    {
        _gateway.CreateSubscriptionAsync(CustomerId, "pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new BillingOutcomeUnknownException("unknown"), _ => Subscription(_references.SubscriptionReference));
        await NewService().SubscribeAsync(UserName, "pro", default);

        _clock.Advance(SubscriptionService.DefaultClaimLease + TimeSpan.FromSeconds(1));
        var retry = await NewService().SubscribeAsync(UserName, "pro", default);

        Assert.Equal(SubscribeOutcome.Created, retry.Outcome);
        await _gateway.Received(2).CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptions_ForShopperWithoutCustomer_IsEmpty()
    {
        _gateway.FindCustomerIdAsync(_references.CustomerReference, Arg.Any<CancellationToken>()).Returns((int?)null);

        var mine = await NewService().GetSubscriptionsAsync(UserName, default);

        Assert.Empty(mine.Subscriptions);
        await _gateway.DidNotReceiveWithAnyArgs().ListSubscriptionsForCustomerAsync(default, default);
    }

    [Fact]
    public void References_AreDeterministic_CaseInsensitive_AndCarryNoEmail()
    {
        var a = BillingReferences.For("DemoUser@Microsoft.com", CatalogKey);
        var b = BillingReferences.For("demouser@microsoft.com", CatalogKey);

        Assert.Equal(a, b);
        Assert.DoesNotContain("microsoft", a.CustomerReference);
        Assert.StartsWith("eshop-", a.CustomerReference);
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
