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

public class SubscribeTests
{
    private const string User = "demouser@microsoft.com";
    private const string CustomerRef = "eshop-demouser@microsoft.com";
    private const string SubscriptionRef = "eshop-sub-demouser@microsoft.com";

    private readonly ISubscriptionBillingGateway _gateway = Substitute.For<ISubscriptionBillingGateway>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly DbContextOptions<CatalogContext> _dbOptions = new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase($"subscriptions-{Guid.NewGuid()}").Options;
    private SubscriptionServiceSettings _settings = new();

    private static readonly BillingPlan Pro = new(1, "eshop-pro", "Pro Plan", null, 29900, 1, "month");
    private static readonly BillingPlan Basic = new(2, "basic-plan", "Basic Plan", null, 2900, 1, "month");

    public SubscribeTests()
    {
        _gateway.GetPlansAsync(Arg.Any<CancellationToken>()).Returns(new BillingPlanCatalog(new[] { Pro, Basic }, false));
    }

    // A fresh store per call, like one DI scope per HTTP request.
    private SubscriptionService NewService() =>
        new(_gateway, new EfSubscriptionEnrollmentStore(new CatalogContext(_dbOptions)), _settings, TimeProvider.System, _logger);

    private Task<SubscriptionEnrollment?> StoredEnrollment() =>
        new EfSubscriptionEnrollmentStore(new CatalogContext(_dbOptions)).FindAsync(User, CancellationToken.None);

    private static BillingSubscription Sub(int id, string plan = "eshop-pro", int customerId = 10) =>
        new(id, SubscriptionRef, customerId, plan, "Pro Plan", 29900, "USD", 1, "month", "active",
            DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow);

    private static SubscribeCommand Command(string plan = "eshop-pro") => new(User, User, plan);

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        _gateway.FindCustomerByReferenceAsync(CustomerRef, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _gateway.CreateCustomerAsync(Arg.Any<BillingCustomerProfile>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(10, CustomerRef, User));
        _gateway.CreateSubscriptionAsync(10, "eshop-pro", SubscriptionRef, Arg.Any<CancellationToken>()).Returns(Sub(500));

        var result = await NewService().SubscribeAsync(Command(), CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(500, result.Subscription.Id);
        await _gateway.Received(1).CreateCustomerAsync(
            Arg.Is<BillingCustomerProfile>(p => p.Reference == CustomerRef && p.Email == User && p.FirstName == "demouser"),
            Arg.Any<CancellationToken>());
        var stored = await StoredEnrollment();
        Assert.Equal(EnrollmentStatus.Enrolled, stored!.Status);
        Assert.Equal(500, stored.BillingSubscriptionId);
        Assert.Equal(10, stored.BillingCustomerId);
    }

    [Fact]
    public async Task ReusesExistingCustomer()
    {
        _gateway.FindCustomerByReferenceAsync(CustomerRef, Arg.Any<CancellationToken>()).Returns(new BillingCustomer(10, CustomerRef, User));
        _gateway.CreateSubscriptionAsync(10, "eshop-pro", SubscriptionRef, Arg.Any<CancellationToken>()).Returns(Sub(500));

        await NewService().SubscribeAsync(Command(), CancellationToken.None);

        await _gateway.DidNotReceive().CreateCustomerAsync(Arg.Any<BillingCustomerProfile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsPlanTheFamilyDoesNotOffer()
    {
        await Assert.ThrowsAsync<UnknownSubscriptionPlanException>(
            () => NewService().SubscribeAsync(Command("gold"), CancellationToken.None));

        await _gateway.DidNotReceive().CreateCustomerAsync(Arg.Any<BillingCustomerProfile>(), Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Null(await StoredEnrollment());
    }

    [Fact]
    public async Task SecondRequestWhileFirstIsInFlightIsRefusedWithoutReachingProvider()
    {
        var release = new TaskCompletionSource<BillingSubscription>();
        _gateway.FindCustomerByReferenceAsync(CustomerRef, Arg.Any<CancellationToken>()).Returns(new BillingCustomer(10, CustomerRef, User));
        _gateway.CreateSubscriptionAsync(10, "eshop-pro", SubscriptionRef, Arg.Any<CancellationToken>()).Returns(release.Task);

        var first = NewService().SubscribeAsync(Command(), CancellationToken.None);
        await Assert.ThrowsAsync<SubscriptionConflictException>(() => NewService().SubscribeAsync(Command(), CancellationToken.None));

        release.SetResult(Sub(500));
        Assert.True((await first).Created);
        await _gateway.Received(1).CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepeatedSubscribeForSamePlanReturnsExistingSubscription()
    {
        _gateway.FindCustomerByReferenceAsync(CustomerRef, Arg.Any<CancellationToken>()).Returns(new BillingCustomer(10, CustomerRef, User));
        _gateway.CreateSubscriptionAsync(10, "eshop-pro", SubscriptionRef, Arg.Any<CancellationToken>()).Returns(Sub(500));
        await NewService().SubscribeAsync(Command(), CancellationToken.None);
        _gateway.FindSubscriptionByReferenceAsync(SubscriptionRef, Arg.Any<CancellationToken>()).Returns(Sub(500));

        var again = await NewService().SubscribeAsync(Command(), CancellationToken.None);

        Assert.False(again.Created);
        Assert.Equal(500, again.Subscription.Id);
        await _gateway.Received(1).CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribingToAnotherPlanWhileSubscribedIsAConflict()
    {
        _gateway.FindCustomerByReferenceAsync(CustomerRef, Arg.Any<CancellationToken>()).Returns(new BillingCustomer(10, CustomerRef, User));
        _gateway.CreateSubscriptionAsync(10, "eshop-pro", SubscriptionRef, Arg.Any<CancellationToken>()).Returns(Sub(500));
        await NewService().SubscribeAsync(Command(), CancellationToken.None);
        _gateway.FindSubscriptionByReferenceAsync(SubscriptionRef, Arg.Any<CancellationToken>()).Returns(Sub(500));

        await Assert.ThrowsAsync<SubscriptionConflictException>(
            () => NewService().SubscribeAsync(Command("basic-plan"), CancellationToken.None));
        await _gateway.Received(1).CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdoptsSubscriptionAlreadyAtProviderInsteadOfCreatingAnother()
    {
        // e.g. the local store was wiped (in-memory database restarted) but Maxio still has the subscription.
        _gateway.FindCustomerByReferenceAsync(CustomerRef, Arg.Any<CancellationToken>()).Returns(new BillingCustomer(10, CustomerRef, User));
        _gateway.FindSubscriptionByReferenceAsync(SubscriptionRef, Arg.Any<CancellationToken>()).Returns(Sub(500));

        var result = await NewService().SubscribeAsync(Command(), CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(500, result.Subscription.Id);
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(EnrollmentStatus.Enrolled, (await StoredEnrollment())!.Status);
    }

    [Fact]
    public async Task ProviderRejectionReleasesTheClaim()
    {
        _gateway.FindCustomerByReferenceAsync(CustomerRef, Arg.Any<CancellationToken>()).Returns(new BillingCustomer(10, CustomerRef, User));
        _gateway.CreateSubscriptionAsync(10, "eshop-pro", SubscriptionRef, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException(BillingFailureKind.Rejected, "rejected", 422, new[] { "No payment method" }));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => NewService().SubscribeAsync(Command(), CancellationToken.None));

        Assert.Equal(BillingFailureKind.Rejected, ex.Kind);
        Assert.Null(await StoredEnrollment());
    }

    [Fact]
    public async Task UnansweredCreateIsSettledByReferenceInTheSameRequest()
    {
        _gateway.FindCustomerByReferenceAsync(CustomerRef, Arg.Any<CancellationToken>()).Returns(new BillingCustomer(10, CustomerRef, User));
        _gateway.FindSubscriptionByReferenceAsync(SubscriptionRef, Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null, Sub(500));
        _gateway.CreateSubscriptionAsync(10, "eshop-pro", SubscriptionRef, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingOutcomeUnknownException(BillingFailureKind.Timeout, "no answer"));

        var result = await NewService().SubscribeAsync(Command(), CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(500, result.Subscription.Id);
        Assert.Equal(EnrollmentStatus.Enrolled, (await StoredEnrollment())!.Status);
    }

    [Fact]
    public async Task UnsettledCreateIsRecordedAsUnknownAndSettledByTheNextRequest()
    {
        _gateway.FindCustomerByReferenceAsync(CustomerRef, Arg.Any<CancellationToken>()).Returns(new BillingCustomer(10, CustomerRef, User));
        _gateway.FindSubscriptionByReferenceAsync(SubscriptionRef, Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult<BillingSubscription?>(null),
                _ => throw new BillingProviderException(BillingFailureKind.Timeout, "still no answer"),
                _ => Task.FromResult<BillingSubscription?>(Sub(500)));
        _gateway.CreateSubscriptionAsync(10, "eshop-pro", SubscriptionRef, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingOutcomeUnknownException(BillingFailureKind.Timeout, "no answer"));

        await Assert.ThrowsAsync<SubscriptionOutcomeUnknownException>(() => NewService().SubscribeAsync(Command(), CancellationToken.None));
        Assert.Equal(EnrollmentStatus.OutcomeUnknown, (await StoredEnrollment())!.Status);

        var settled = await NewService().SubscribeAsync(Command(), CancellationToken.None);

        Assert.Equal(500, settled.Subscription.Id);
        Assert.Equal(EnrollmentStatus.Enrolled, (await StoredEnrollment())!.Status);
        await _gateway.Received(1).CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownOutcomeThatNeverLandedIsRetriedByTheNextRequest()
    {
        _gateway.FindCustomerByReferenceAsync(CustomerRef, Arg.Any<CancellationToken>()).Returns(new BillingCustomer(10, CustomerRef, User));
        _gateway.FindSubscriptionByReferenceAsync(SubscriptionRef, Arg.Any<CancellationToken>()).Returns((BillingSubscription?)null);
        _gateway.CreateSubscriptionAsync(10, "eshop-pro", SubscriptionRef, Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new BillingOutcomeUnknownException(BillingFailureKind.Unreachable, "dropped"),
                _ => Task.FromResult(Sub(501)));

        await Assert.ThrowsAsync<SubscriptionOutcomeUnknownException>(() => NewService().SubscribeAsync(Command(), CancellationToken.None));
        var retried = await NewService().SubscribeAsync(Command(), CancellationToken.None);

        Assert.True(retried.Created);
        Assert.Equal(501, retried.Subscription.Id);
    }

    [Fact]
    public async Task UnsettledCustomerCreateReleasesTheClaimAndTheRetryFindsTheCustomer()
    {
        // Customer references are unique at Maxio, so the retry reads the customer instead of creating another.
        _gateway.FindCustomerByReferenceAsync(CustomerRef, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null, new BillingCustomer(10, CustomerRef, User));
        _gateway.CreateCustomerAsync(Arg.Any<BillingCustomerProfile>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingOutcomeUnknownException(BillingFailureKind.Timeout, "no answer"));
        _gateway.CreateSubscriptionAsync(10, "eshop-pro", SubscriptionRef, Arg.Any<CancellationToken>()).Returns(Sub(500));

        await Assert.ThrowsAsync<BillingOutcomeUnknownException>(() => NewService().SubscribeAsync(Command(), CancellationToken.None));
        Assert.Null(await StoredEnrollment());

        var retried = await NewService().SubscribeAsync(Command(), CancellationToken.None);

        Assert.Equal(500, retried.Subscription.Id);
        await _gateway.Received(1).CreateCustomerAsync(Arg.Any<BillingCustomerProfile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AbandonedPendingClaimIsSettledInsteadOfBlockingForever()
    {
        _settings = new SubscriptionServiceSettings { StaleClaimAfter = TimeSpan.Zero };
        var abandoned = SubscriptionEnrollment.Claim(User, "eshop-pro", SubscriptionRef, DateTimeOffset.UtcNow.AddMinutes(-5));
        await new EfSubscriptionEnrollmentStore(new CatalogContext(_dbOptions)).TryClaimAsync(abandoned, CancellationToken.None);
        _gateway.FindSubscriptionByReferenceAsync(SubscriptionRef, Arg.Any<CancellationToken>()).Returns(Sub(500));

        var result = await NewService().SubscribeAsync(Command(), CancellationToken.None);

        Assert.Equal(500, result.Subscription.Id);
        Assert.Equal(EnrollmentStatus.Enrolled, (await StoredEnrollment())!.Status);
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnresponsiveProviderFailsWithinTheBudgetAndReleasesTheClaim()
    {
        _settings = new SubscriptionServiceSettings { RequestBudget = TimeSpan.FromMilliseconds(300) };
        _gateway.FindCustomerByReferenceAsync(CustomerRef, Arg.Any<CancellationToken>())
            .Returns(call => HangUntilCancelled<BillingCustomer?>(call.Arg<CancellationToken>()));

        var started = DateTimeOffset.UtcNow;
        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => NewService().SubscribeAsync(Command(), CancellationToken.None));

        Assert.Equal(BillingFailureKind.Timeout, ex.Kind);
        Assert.Contains("Maxio did not respond", ex.Message);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.Null(await StoredEnrollment());
    }

    [Fact]
    public async Task MySubscriptionsIsEmptyWhenNoCustomerExists()
    {
        _gateway.FindCustomerByReferenceAsync(CustomerRef, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);

        var subscriptions = await NewService().GetMySubscriptionsAsync(User, CancellationToken.None);

        Assert.Empty(subscriptions);
        await _gateway.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MySubscriptionsListsTheCallersCustomerOnly()
    {
        _gateway.FindCustomerByReferenceAsync(CustomerRef, Arg.Any<CancellationToken>()).Returns(new BillingCustomer(10, CustomerRef, User));
        _gateway.ListCustomerSubscriptionsAsync(10, Arg.Any<CancellationToken>()).Returns(new[] { Sub(500) });

        var subscriptions = await NewService().GetMySubscriptionsAsync(User, CancellationToken.None);

        Assert.Equal(500, Assert.Single(subscriptions).Id);
    }

    private static async Task<T> HangUntilCancelled<T>(CancellationToken token)
    {
        await Task.Delay(Timeout.Infinite, token);
        throw new InvalidOperationException("unreachable");
    }
}
