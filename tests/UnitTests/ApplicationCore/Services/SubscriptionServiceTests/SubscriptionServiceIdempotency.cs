using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscriptionServiceTests
{
    private const string UserId = "user-1";
    private const string Email = "user1@example.com";
    private const string Plan = "eshop-pro";

    private readonly ISubscriptionBillingProvider _billingProvider = Substitute.For<ISubscriptionBillingProvider>();
    private readonly IReadRepository<UserSubscription> _readRepo = Substitute.For<IReadRepository<UserSubscription>>();
    private readonly IRepository<UserSubscription> _writeRepo = Substitute.For<IRepository<UserSubscription>>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService CreateService() =>
        new(_billingProvider, _readRepo, _writeRepo, _logger);

    private static BillingSubscription FakeBillingSubscription(long id = 42) =>
        new(id, 7, "active", Plan, "Pro Plan", 29900, new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc));

    private void SetupPlans(params string[] handles)
    {
        var plans = handles.Select(h => new BillingPlan(h, h + " name", 2900, 1, "month")).ToList();
        _billingProvider.ListPlansAsync(default).Returns(plans);
    }

    [Fact]
    public async Task SubscribeCreatesNewRecordWhenNoneExists()
    {
        SetupPlans(Plan);
        _readRepo.FirstOrDefaultAsync(Arg.Any<UserSubscriptionsForUserSpec>(), default).Returns((UserSubscription?)null);
        _writeRepo.FirstOrDefaultAsync(Arg.Any<UserSubscriptionForUserAndPlanSpec>(), default).Returns((UserSubscription?)null);
        _billingProvider.SubscribeAsync(UserId, Email, Arg.Any<string>(), Arg.Any<string>(), Plan, default)
            .Returns(FakeBillingSubscription());

        var service = CreateService();
        var result = await service.SubscribeAsync(UserId, Email, "Demo User", Plan, default);

        Assert.Equal(42, result.BillingSubscriptionId);
        Assert.Equal("active", result.State);
        await _billingProvider.Received(1).SubscribeAsync(UserId, Email, Arg.Any<string>(), Arg.Any<string>(), Plan, default);
        await _writeRepo.Received(1).AddAsync(Arg.Any<UserSubscription>(), default);
    }

    [Fact]
    public async Task SubscribeIsIdempotentWhenSubscriptionAlreadyActive()
    {
        SetupPlans(Plan);
        var existing = new UserSubscription(UserId, Email, 7, 99, Plan, "Pro Plan", 29900, "active", null);
        _writeRepo.FirstOrDefaultAsync(Arg.Any<UserSubscriptionForUserAndPlanSpec>(), default).Returns(existing);
        _billingProvider.GetSubscriptionAsync(99, default).Returns(FakeBillingSubscription(99));

        var service = CreateService();
        var first = await service.SubscribeAsync(UserId, Email, "Demo User", Plan, default);
        var second = await service.SubscribeAsync(UserId, Email, "Demo User", Plan, default);

        Assert.Same(existing, first);
        Assert.Same(existing, second);
        await _billingProvider.DidNotReceive().SubscribeAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), default);
        await _writeRepo.DidNotReceive().AddAsync(Arg.Any<UserSubscription>(), default);
    }

    [Fact]
    public async Task SubscribeCreatesNewBillingSubscriptionWhenPreviousOneWasCanceled()
    {
        SetupPlans(Plan);
        var existing = new UserSubscription(UserId, Email, 7, 99, Plan, "Pro Plan", 29900, "canceled", null);
        _writeRepo.FirstOrDefaultAsync(Arg.Any<UserSubscriptionForUserAndPlanSpec>(), default).Returns(existing);
        _billingProvider.GetSubscriptionAsync(99, default)
            .Returns(new BillingSubscription(99, 7, "canceled", Plan, "Pro Plan", 29900, null));
        _billingProvider.SubscribeAsync(UserId, Email, Arg.Any<string>(), Arg.Any<string>(), Plan, default)
            .Returns(FakeBillingSubscription(123));

        var service = CreateService();
        var result = await service.SubscribeAsync(UserId, Email, "Demo User", Plan, default);

        Assert.Equal(123, result.BillingSubscriptionId);
        Assert.Equal("active", result.State);
        await _billingProvider.Received(1).SubscribeAsync(UserId, Email, Arg.Any<string>(), Arg.Any<string>(), Plan, default);
        await _writeRepo.DidNotReceive().AddAsync(Arg.Any<UserSubscription>(), default);
    }

    [Fact]
    public async Task SubscribeThrowsWhenPlanIsUnknown()
    {
        SetupPlans("basic-plan");
        var service = CreateService();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(UserId, Email, "Demo User", "nope", default));

        await _billingProvider.DidNotReceive().SubscribeAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), default);
    }
}
