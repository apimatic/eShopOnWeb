using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscribeAsync : SubscriptionServiceTestBase
{
    private static SubscribeRequest Request(string planHandle = ProPlanHandle, string? idempotencyKey = null) => new()
    {
        UserName = UserName,
        PlanHandle = planHandle,
        IdempotencyKey = idempotencyKey
    };

    [Fact]
    public async Task CreatesCustomerAndSubscriptionForANewSubscriber()
    {
        BillingGateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        BillingGateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(Customer());
        BillingGateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        BillingGateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, ProPlanHandle, "active"));

        var result = await CreateService().SubscribeAsync(Request());

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(1, result.Subscription.Id);
        Assert.Equal(ProPlanHandle, result.Plan.Handle);

        await BillingGateway.Received(1).CreateCustomerAsync(
            Arg.Is<NewBillingCustomer>(customer => customer.Reference == CustomerReference && customer.Email == UserName),
            Arg.Any<CancellationToken>());
        await BillingGateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscription>(subscription => subscription.CustomerId == 42 && subscription.PlanHandle == ProPlanHandle),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesAnExistingCustomerInsteadOfCreatingASecond()
    {
        BillingGateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        BillingGateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        BillingGateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, ProPlanHandle, "active"));

        await CreateService().SubscribeAsync(Request());

        await BillingGateway.DidNotReceive().CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionWhenAlreadySubscribedToThePlan()
    {
        BillingGateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        BillingGateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(7, ProPlanHandle, "active") });

        var result = await CreateService().SubscribeAsync(Request());

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(7, result.Subscription.Id);
        await BillingGateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("trialing")]
    [InlineData("past_due")]
    [InlineData("soft_failure")]
    public async Task TreatsRecoverableStatesAsStillSubscribed(string state)
    {
        BillingGateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        BillingGateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(7, ProPlanHandle, state) });

        var result = await CreateService().SubscribeAsync(Request());

        Assert.True(result.AlreadySubscribed);
        await BillingGateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    public async Task SubscribesAgainWhenThePreviousSubscriptionHasEnded(string endedState)
    {
        BillingGateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        BillingGateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(7, ProPlanHandle, endedState) });
        BillingGateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(8, ProPlanHandle, "active"));

        var result = await CreateService().SubscribeAsync(Request());

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(8, result.Subscription.Id);
    }

    [Fact]
    public async Task DoesNotConfuseALiveSubscriptionOnAnotherPlanWithThisOne()
    {
        BillingGateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        BillingGateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(7, BasicPlanHandle, "active") });
        BillingGateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(8, ProPlanHandle, "active"));

        var result = await CreateService().SubscribeAsync(Request());

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(8, result.Subscription.Id);
    }

    [Fact]
    public async Task UsesTheWinningCustomerWhenTheCreateLosesARace()
    {
        BillingGateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null, Customer(99));
        BillingGateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingConflictException("Reference: must be unique."));
        BillingGateway.ListCustomerSubscriptionsAsync(99, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        BillingGateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, ProPlanHandle, "active"));

        var result = await CreateService().SubscribeAsync(Request());

        Assert.Equal(1, result.Subscription.Id);
        await BillingGateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscription>(subscription => subscription.CustomerId == 99), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheWinningSubscriptionWhenTheCreateIsRejectedAsADuplicate()
    {
        BillingGateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        BillingGateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>(), new[] { Subscription(5, ProPlanHandle, "active") });
        BillingGateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingConflictException("DuplicatePrevention::DuplicateSubmissionError"));

        var result = await CreateService().SubscribeAsync(Request(idempotencyKey: "replayed"));

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(5, result.Subscription.Id);
    }

    [Fact]
    public async Task SurfacesTheConflictWhenADuplicateRejectionLeftNoSubscriptionBehind()
    {
        BillingGateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        BillingGateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        BillingGateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingConflictException("DuplicatePrevention::DuplicateSubmissionError"));

        await Assert.ThrowsAsync<BillingConflictException>(() => CreateService().SubscribeAsync(Request()));
    }

    [Fact]
    public async Task ReportsTheAvailablePlansWhenTheHandleIsUnknown()
    {
        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(Request("no-such-plan")));

        Assert.Equal("no-such-plan", exception.PlanHandle);
        Assert.Contains(ProPlanHandle, exception.AvailableHandles);
        await BillingGateway.DidNotReceive().CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MatchesThePlanHandleWithoutRegardToCase()
    {
        BillingGateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        BillingGateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        BillingGateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, ProPlanHandle, "active"));

        await CreateService().SubscribeAsync(Request("ESHOP-PRO"));

        // The canonical handle from the catalog is what reaches the billing system.
        await BillingGateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewSubscription>(subscription => subscription.PlanHandle == ProPlanHandle), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DerivesACustomerNameFromTheEmailWhenNoneIsSupplied()
    {
        NewBillingCustomer? captured = null;
        BillingGateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        BillingGateway.CreateCustomerAsync(Arg.Do<NewBillingCustomer>(customer => captured = customer), Arg.Any<CancellationToken>())
            .Returns(Customer());
        BillingGateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        BillingGateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, ProPlanHandle, "active"));

        await CreateService().SubscribeAsync(new SubscribeRequest
        {
            UserName = "ada.lovelace@example.com",
            PlanHandle = ProPlanHandle
        });

        Assert.NotNull(captured);
        Assert.Equal("Ada", captured!.FirstName);
        Assert.Equal("Lovelace", captured.LastName);
    }

    [Fact]
    public async Task PrefersTheSuppliedCustomerNameOverTheDerivedOne()
    {
        NewBillingCustomer? captured = null;
        BillingGateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        BillingGateway.CreateCustomerAsync(Arg.Do<NewBillingCustomer>(customer => captured = customer), Arg.Any<CancellationToken>())
            .Returns(Customer());
        BillingGateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        BillingGateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, ProPlanHandle, "active"));

        await CreateService().SubscribeAsync(new SubscribeRequest
        {
            UserName = UserName,
            PlanHandle = ProPlanHandle,
            FirstName = "Grace",
            LastName = "Hopper",
            Organization = "eShopOnWeb"
        });

        Assert.Equal("Grace", captured!.FirstName);
        Assert.Equal("Hopper", captured.LastName);
        Assert.Equal("eShopOnWeb", captured.Organization);
    }

    [Fact]
    public async Task SendsTheSameUniquenessTokenForTheSameIdempotencyKey()
    {
        var tokens = await CaptureTokensAsync("stable-key", "stable-key");

        Assert.Equal(tokens[0], tokens[1]);
    }

    [Fact]
    public async Task SendsAFreshUniquenessTokenWhenNoIdempotencyKeyIsSupplied()
    {
        // A failed create burns its token for an hour, so two independent attempts must not share one.
        var tokens = await CaptureTokensAsync(null, null);

        Assert.NotEqual(tokens[0], tokens[1]);
        Assert.All(tokens, token => Assert.False(string.IsNullOrWhiteSpace(token)));
    }

    [Fact]
    public async Task OnlyCreatesOnceWhenTheSameShopperSubscribesConcurrently()
    {
        var createCount = 0;
        BillingGateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        BillingGateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(_ => Volatile.Read(ref createCount) == 0
                ? Array.Empty<BillingSubscription>()
                : new[] { Subscription(1, ProPlanHandle, "active") });
        BillingGateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref createCount);
                return Task.FromResult(Subscription(1, ProPlanHandle, "active"));
            });

        var service = CreateService();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.SubscribeAsync(Request())));

        Assert.Equal(1, createCount);
        Assert.Single(results.Where(result => !result.AlreadySubscribed));
        Assert.All(results, result => Assert.Equal(1, result.Subscription.Id));
    }

    private async Task<List<string?>> CaptureTokensAsync(string? firstKey, string? secondKey)
    {
        var tokens = new List<string?>();
        BillingGateway.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        BillingGateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        BillingGateway.CreateSubscriptionAsync(
                Arg.Do<NewSubscription>(subscription => tokens.Add(subscription.UniquenessToken)),
                Arg.Any<CancellationToken>())
            .Returns(Subscription(1, ProPlanHandle, "active"));

        var service = CreateService();
        await service.SubscribeAsync(Request(idempotencyKey: firstKey));
        await service.SubscribeAsync(Request(idempotencyKey: secondKey));

        return tokens;
    }
}
