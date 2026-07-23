using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Handlers;

/// <summary>The in-process reactions to subscription lifecycle facts (§2.5).</summary>
public class NotificationHandlerTests
{
    [Fact]
    public async Task ActivationHandlerConfirmsTheSubscriptionToTheCustomerByEmail()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var handler = new SubscriptionActivatedHandler(emailSender, Substitute.For<IAppLogger<SubscriptionActivatedHandler>>());

        await handler.Handle(new SubscriptionActivated(SubscriptionBuilder.Subscription()), CancellationToken.None);

        await emailSender.Received(1).SendEmailAsync(
            SubscriptionBuilder.UserReference,
            Arg.Is<string>(subject => subject.Contains("subscription")),
            Arg.Is<string>(body => body.Contains("Pro Plan") && body.Contains("299.00")));
    }

    [Fact]
    public async Task PlanChangeHandlerAuditsTheAgreedAmount()
    {
        var logger = Substitute.For<IAppLogger<SubscriptionPlanChangedHandler>>();
        var handler = new SubscriptionPlanChangedHandler(logger);
        var preview = SubscriptionBuilder.Preview();

        await handler.Handle(
            new SubscriptionPlanChanged(SubscriptionBuilder.Subscription(), SubscriptionBuilder.BasicPlanHandle, preview),
            CancellationToken.None);

        logger.Received(1).LogInformation(
            Arg.Any<string>(),
            Arg.Is<object[]>(args => args.Contains(preview.PaymentDueInCents)));
    }

    [Fact]
    public async Task StateChangeHandlerAuditsTheOldAndNewState()
    {
        var logger = Substitute.For<IAppLogger<SubscriptionStateChangedHandler>>();
        var handler = new SubscriptionStateChangedHandler(logger);

        await handler.Handle(
            new SubscriptionStateChanged(
                SubscriptionBuilder.Subscription(state: SubscriptionState.Canceled),
                SubscriptionState.Active,
                SubscriptionLifecycleAction.Cancel),
            CancellationToken.None);

        logger.Received(1).LogInformation(
            Arg.Any<string>(),
            Arg.Is<object[]>(args =>
                args.Contains(SubscriptionState.Active) &&
                args.Contains(SubscriptionState.Canceled) &&
                args.Contains(SubscriptionLifecycleAction.Cancel)));
    }

    [Fact]
    public async Task OrderPlacedHandlerRecordsExactlyOneBillableUnitPerOrder()
    {
        var subscriptionService = Substitute.For<ISubscriptionService>();
        subscriptionService
            .RecordUsageForUserAsync(SubscriptionBuilder.UserReference, 1m, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new UsageReport(SubscriptionBuilder.UsageRecord(), 4, 0.01m));

        var handler = new OrderPlacedUsageHandler(subscriptionService, Substitute.For<IAppLogger<OrderPlacedUsageHandler>>());

        await handler.Handle(new OrderPlaced(77, SubscriptionBuilder.UserReference), CancellationToken.None);

        await subscriptionService.Received(1).RecordUsageForUserAsync(
            SubscriptionBuilder.UserReference,
            1m,
            Arg.Is<string>(memo => memo.Contains("77")),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(BillingFailures))]
    public async Task OrderPlacedHandlerNeverLetsABillingFailureEscapeIntoTheOrderLifecycle(Exception failure)
    {
        var subscriptionService = Substitute.For<ISubscriptionService>();
        subscriptionService
            .RecordUsageForUserAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(failure);

        var logger = Substitute.For<IAppLogger<OrderPlacedUsageHandler>>();
        var handler = new OrderPlacedUsageHandler(subscriptionService, logger);

        // The order is already persisted by the time this runs, so nothing may propagate.
        await handler.Handle(new OrderPlaced(77, SubscriptionBuilder.UserReference), CancellationToken.None);

        logger.Received(1).LogWarning(Arg.Any<string>(), Arg.Any<object[]>());
    }

    public static TheoryData<Exception> BillingFailures() => new()
    {
        new InvalidSubscriptionOperationException("no active subscription"),
        new BillingProviderException("Maxio is down", 503),
        new InvalidOperationException("Maxio:ApiKey is not configured"),
        new HttpRequestException("No such host is known."),
        new TimeoutException("timed out")
    };
}
