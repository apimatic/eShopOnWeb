using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public sealed class SubscriptionBillingServiceTests
{
    private static readonly BillingUser User = new("user-123", "shopper@example.test", "Shopper", "Example");
    private static readonly SubscriptionPlan Plan = new("eshop-pro", "Pro", null, 29900, 1, "month", "USD");
    private static readonly SubscriptionConfirmation Confirmation = new(
        SubscriptionBillingService.SubscriptionReference(User.UserId, Plan.Handle),
        Plan.Handle,
        Plan.Name,
        Plan.PriceInCents,
        "active",
        DateTimeOffset.Parse("2026-09-21T00:00:00Z"));

    [Fact]
    public async Task ReplayedSubscribeReturnsExistingSubscriptionWithoutSecondProviderWrite()
    {
        var gateway = Substitute.For<IMaxioBillingGateway>();
        var store = Substitute.For<IBillingLinkStore>();
        var subscriptionLease = "subscription-lease";
        var customerLease = "customer-lease";
        gateway.GetPlansAsync(Arg.Any<CancellationToken>()).Returns(new[] { Plan });
        gateway.FindSubscriptionAsync(
                Confirmation.Reference,
                Arg.Any<string>(),
                Plan.Handle,
                Arg.Any<CancellationToken>())
            .Returns((SubscriptionConfirmation?)null);
        gateway.CreateSubscriptionAsync(
                Confirmation.Reference,
                Arg.Any<string>(),
                Plan.Handle,
                Arg.Any<CancellationToken>())
            .Returns(Confirmation);
        store.ClaimSubscriptionAsync(
                User.UserId,
                Plan.Handle,
                Confirmation.Reference,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(
                new SubscriptionClaim(BillingClaimStatus.Acquired, Confirmation.Reference, subscriptionLease, null),
                new SubscriptionClaim(BillingClaimStatus.Completed, Confirmation.Reference, null, Confirmation));
        store.ClaimCustomerAsync(
                User.UserId,
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new CustomerClaim(BillingClaimStatus.Acquired, call.ArgAt<string>(1), customerLease));

        var service = new SubscriptionBillingService(gateway, store);

        var first = await service.SubscribeAsync(User, Plan.Handle, default);
        var replay = await service.SubscribeAsync(User, Plan.Handle, default);

        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.Equal(first.Subscription, replay.Subscription);
        await gateway.Received(1).EnsureCustomerAsync(User, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await gateway.Received(1).CreateSubscriptionAsync(
            Confirmation.Reference,
            Arg.Any<string>(),
            Plan.Handle,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAProductOutsideConfiguredFamilyBeforeCreatingAnything()
    {
        var gateway = Substitute.For<IMaxioBillingGateway>();
        var store = Substitute.For<IBillingLinkStore>();
        gateway.GetPlansAsync(Arg.Any<CancellationToken>()).Returns(new[] { Plan });
        var service = new SubscriptionBillingService(gateway, store);

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(User, "another-family-plan", default));

        Assert.Equal(BillingErrorKind.NotFound, exception.Kind);
        await gateway.DidNotReceiveWithAnyArgs().EnsureCustomerAsync(default!, default!, default);
        await gateway.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default!, default!, default);
    }

    [Fact]
    public void DeterministicReferencesContainNoUserPii()
    {
        var customerReference = SubscriptionBillingService.CustomerReference(User.UserId);
        var subscriptionReference = SubscriptionBillingService.SubscriptionReference(User.UserId, Plan.Handle);

        Assert.Equal(customerReference, SubscriptionBillingService.CustomerReference(User.UserId));
        Assert.Equal(subscriptionReference, SubscriptionBillingService.SubscriptionReference(User.UserId, Plan.Handle));
        Assert.DoesNotContain(User.UserId, customerReference);
        Assert.DoesNotContain(User.UserId, subscriptionReference);
        Assert.StartsWith("eshop-customer-v1-", customerReference);
        Assert.StartsWith("eshop-sub-v1-", subscriptionReference);
    }
}

