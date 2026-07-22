using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class UsageSummaryAndAuditing
{
    private readonly SubscriptionServiceFixture _fixture = new();

    [Fact]
    public async Task UsageSummaryReportsTheRunningTotalAndWhatItWillCost()
    {
        _fixture.BillingClient.FindCustomerByReferenceAsync(SubscriptionServiceFixture.UserReference,
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.Customer());
        _fixture.BillingClient.ListSubscriptionsAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active) });
        _fixture.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.MeteredComponent());
        _fixture.BillingClient.GetPeriodToDateUnitsAsync(90210, Arg.Any<BillingComponent>(),
                Arg.Any<CancellationToken>())
            .Returns(750);

        var report = await _fixture.CreateService()
            .GetUsageSummaryAsync(SubscriptionServiceFixture.UserReference);

        Assert.NotNull(report);
        Assert.Equal(750, report.PeriodToDateUnits);
        // 750 units at $0.01 is $7.50.
        Assert.Equal(7.50m, report.PeriodToDateCharge);
    }

    [Fact]
    public async Task UsageSummaryIsAbsentForAUserWithNoSubscription()
    {
        _fixture.BillingClient.FindCustomerByReferenceAsync(SubscriptionServiceFixture.UserReference,
                Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        var report = await _fixture.CreateService()
            .GetUsageSummaryAsync(SubscriptionServiceFixture.UserReference);

        Assert.Null(report);
    }

    [Fact]
    public async Task AdministrativeUsageTargetsTheGivenSubscriptionDirectly()
    {
        _fixture.BillingClient.GetSubscriptionAsync(777, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active));
        _fixture.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.MeteredComponent());
        _fixture.BillingClient.RecordUsageAsync(777, Arg.Any<BillingComponent>(), 9m, "admin",
                Arg.Any<CancellationToken>())
            .Returns(new UsageRecord(1, 777, 3062734, "api-call", 9m, "admin", DateTimeOffset.UtcNow));

        var report = await _fixture.CreateService().RecordUsageForSubscriptionAsync(777, 9m, "admin");

        Assert.Equal(9m, report.Record.Quantity);
        // The customer lookup is bypassed entirely for the administrative path.
        await _fixture.BillingClient.DidNotReceive()
            .FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdministrativeUsageIsStillRefusedOnASubscriptionThatIsNotLive()
    {
        _fixture.BillingClient.GetSubscriptionAsync(777, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Canceled));

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _fixture.CreateService().RecordUsageForSubscriptionAsync(777, 1m, null));
    }

    [Fact]
    public async Task AdministrativeLifecycleTargetsTheGivenSubscriptionDirectly()
    {
        _fixture.BillingClient.GetSubscriptionAsync(777, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active));
        _fixture.BillingClient.PauseSubscriptionAsync(90210, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Paused));

        var updated = await _fixture.CreateService().ExecuteLifecycleActionForSubscriptionAsync(777,
            SubscriptionLifecycleAction.Pause, CancellationTiming.Immediate, null);

        Assert.Equal(SubscriptionState.Paused, updated.State);
        await _fixture.Publisher.Received(1)
            .Publish(Arg.Any<SubscriptionStateChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuditHandlerRecordsEveryLifecycleFact()
    {
        var logger = Substitute.For<IAppLogger<SubscriptionAuditLogHandler>>();
        var handler = new SubscriptionAuditLogHandler(logger);
        var subscription = SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active);
        var preview = new PlanChangePreview("basic-plan", "eshop-pro", PlanChangeTiming.Immediately,
            247.50m, 270m, 247.50m, 22.50m);

        await handler.Handle(new SubscriptionActivated(subscription), CancellationToken.None);
        await handler.Handle(new SubscriptionPlanChanged(subscription, "basic-plan",
            PlanChangeTiming.Immediately, preview), CancellationToken.None);
        await handler.Handle(new SubscriptionStateChanged(subscription, SubscriptionState.Paused,
            SubscriptionLifecycleAction.Resume), CancellationToken.None);

        logger.Received(1).LogInformation(Arg.Is<string>(m => m.Contains("activated")));
        logger.Received(1).LogInformation(Arg.Is<string>(m => m.Contains("basic-plan") && m.Contains("eshop-pro")));
        logger.Received(1).LogInformation(Arg.Is<string>(m => m.Contains("Paused") && m.Contains("Active")));
    }
}
