using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Internal;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string PlanHandle = "eshop-pro";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly SubscriberIdentity _subscriber = new("demouser@microsoft.com", "demouser@microsoft.com");

    public MaxioSubscriptionServiceTests()
    {
        _client.GetSiteAsync(Arg.Any<CancellationToken>()).Returns(new MaxioSite { Currency = "USD" });
        _client.ListProductsForFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Id = 1, Handle = PlanHandle, Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
                new() { Id = 2, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" }
            });
    }

    [Fact]
    public async Task PlansAreOrderedByPriceAndExcludeArchivedProducts()
    {
        _client.ListProductsForFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = PlanHandle, Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
                new() { Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" },
                new() { Handle = "retired-plan", Name = "Retired", PriceInCents = 100, ArchivedAt = DateTimeOffset.UtcNow }
            });

        var plans = await CreateService().GetPlansAsync();

        Assert.Equal(new[] { "basic-plan", PlanHandle }, Array.ConvertAll(plans.ToArray(), plan => plan.Handle));
    }

    [Fact]
    public async Task SubscribingCreatesTheCustomerAndTheSubscriptionWhenNeitherExists()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCustomerAttributes>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ActiveSubscription(id: 7));

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.True(result.Created);
        Assert.Equal(7, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        await _client.Received(1).CreateCustomerAsync(Arg.Any<MaxioCustomerAttributes>(), Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribingReusesAnExistingCustomerInsteadOfCreatingASecond()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ActiveSubscription(id: 7));

        await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCustomerAttributes>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepeatingASubscribeReturnsTheExistingSubscriptionWithoutCreatingAnother()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ActiveSubscription(id: 7));

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.False(result.Created);
        Assert.Equal(7, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LosingTheRaceToCreateTheCustomerAdoptsTheWinnersRecord()
    {
        var adopted = new MaxioCustomer { Id = 99 };
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, adopted);
        _client.CreateCustomerAsync(Arg.Any<MaxioCustomerAttributes>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ReferenceTaken());
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ActiveSubscription(id: 7));

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.True(result.Created);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioSubscriptionAttributes>(attributes => attributes.CustomerId == 99),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LosingTheRaceToCreateTheSubscriptionAdoptsTheWinnersSubscription()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null, ActiveSubscription(id: 7));
        _client.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ReferenceTaken());

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.False(result.Created);
        Assert.Equal(7, result.Subscription.Id);
    }

    [Fact]
    public async Task ADuplicateSubmissionThatLandedResolvesToTheSubscriptionItCreated()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null, ActiveSubscription(id: 7));
        _client.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(DuplicateSubmission());

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.False(result.Created);
        Assert.Equal(7, result.Subscription.Id);
    }

    [Fact]
    public async Task ADuplicateSubmissionStillInFlightIsReportedRatherThanGuessedAt()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(DuplicateSubmission());

        await Assert.ThrowsAsync<SubscriptionInProgressException>(
            () => CreateService().SubscribeAsync(_subscriber, PlanHandle));
    }

    [Fact]
    public async Task AnEndedSubscriptionDoesNotBlockSubscribingToThePlanAgain()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });

        // The canonical reference holds a canceled subscription; the next slot is free.
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ActiveSubscription(id: 7, state: "canceled"), (MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ActiveSubscription(id: 8));

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.True(result.Created);
        Assert.Equal(8, result.Subscription.Id);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioSubscriptionAttributes>(attributes => attributes.Reference!.EndsWith("--r1", StringComparison.Ordinal)),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribingToAnUnpublishedPlanIsRejectedBeforeAnythingIsCreated()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(_subscriber, "no-such-plan"));

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCustomerAttributes>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AShopperWithNoBillingCustomerHasNoSubscriptions()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var subscriptions = await CreateService().GetSubscriptionsAsync(_subscriber);

        Assert.Empty(subscriptions);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscriptionsAreListedWithLiveOnesFirst()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                ActiveSubscription(id: 1, state: "canceled"),
                ActiveSubscription(id: 2)
            });

        var subscriptions = await CreateService().GetSubscriptionsAsync(_subscriber);

        Assert.Equal(2, subscriptions[0].Id);
        Assert.True(subscriptions[0].IsLive);
        Assert.False(subscriptions[1].IsLive);
    }

    [Fact]
    public async Task NextBillingDateFallsBackToThePeriodEndWhenNoAssessmentIsScheduled()
    {
        var periodEnd = DateTimeOffset.UtcNow.AddDays(30);
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new()
                {
                    Id = 1,
                    State = "active",
                    CurrentPeriodEndsAt = periodEnd,
                    NextAssessmentAt = null,
                    Product = new MaxioProduct { Handle = PlanHandle }
                }
            });

        var subscriptions = await CreateService().GetSubscriptionsAsync(_subscriber);

        Assert.Equal(periodEnd, subscriptions[0].NextBillingAt);
    }

    [Fact]
    public async Task AnUnconfiguredIntegrationReportsItselfUnavailableRatherThanFailingObscurely()
    {
        var service = CreateService(new MaxioSettings { ProductFamilyHandle = FamilyHandle });

        await Assert.ThrowsAsync<SubscriptionBillingNotConfiguredException>(
            () => service.SubscribeAsync(_subscriber, PlanHandle));
        await Assert.ThrowsAsync<SubscriptionBillingNotConfiguredException>(() => service.GetPlansAsync());
    }

    [Fact]
    public async Task AMissingProductFamilyIsReportedAsAConfigurationProblem()
    {
        var service = CreateService(new MaxioSettings { ApiKey = "key", Subdomain = "acme" });

        await Assert.ThrowsAsync<SubscriptionBillingNotConfiguredException>(() => service.GetPlansAsync());
    }

    [Fact]
    public async Task UpstreamFailuresSurfaceAsBillingExceptionsCarryingTheStatusCode()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException(HttpStatusCode.ServiceUnavailable, "look up customer", new[] { "upstream is down" }));

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => CreateService().SubscribeAsync(_subscriber, PlanHandle));

        Assert.Equal(503, exception.UpstreamStatusCode);
    }

    private MaxioSubscriptionService CreateService(MaxioSettings? settings = null) =>
        new(
            _client,
            Options.Create(settings ?? new MaxioSettings
            {
                ApiKey = "test-key",
                Subdomain = "test-site",
                ProductFamilyHandle = FamilyHandle
            }),
            new MemoryCache(new MemoryCacheOptions()),
            new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionService>.Instance);

    private static MaxioSubscription ActiveSubscription(long id, string state = "active") => new()
    {
        Id = id,
        State = state,
        Currency = "USD",
        ProductPriceInCents = 29900,
        NextAssessmentAt = DateTimeOffset.UtcNow.AddDays(30),
        ActivatedAt = DateTimeOffset.UtcNow,
        Product = new MaxioProduct { Handle = PlanHandle, Name = "Pro Plan", Interval = 1, IntervalUnit = "month" },
        Customer = new MaxioCustomer { Id = 42 }
    };

    private static MaxioApiException ReferenceTaken() =>
        new(HttpStatusCode.UnprocessableEntity, "create", new[] { "Reference: must be unique - that value has been taken." });

    private static MaxioApiException DuplicateSubmission() =>
        new(HttpStatusCode.Conflict, "create", new[] { "DuplicatePrevention::DuplicateSubmissionError" });
}
