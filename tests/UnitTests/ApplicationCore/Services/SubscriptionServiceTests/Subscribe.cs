using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    private const string USER = "demouser@microsoft.com";

    private readonly IMaxioBillingGateway _gateway = Substitute.For<IMaxioBillingGateway>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService CreateService() => new(_gateway, _logger);

    private void WithPlans(params SubscriptionPlan[] plans)
        => _gateway.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(plans.ToList());

    private static readonly SubscriptionPlan BasicPlan =
        new() { Id = 7126958, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" };

    private static BillingCustomer Customer(long id = 42) => new()
    {
        Id = id,
        Reference = USER,
        Email = USER,
        FirstName = "demouser",
        LastName = "eShopOnWeb",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static BillingSubscription Active(long id = 99) => new()
    {
        Id = id,
        Reference = $"eshoponweb:{USER}:basic-plan",
        State = "active",
        CustomerId = 42,
        PlanHandle = "basic-plan",
        PlanName = "Basic Plan",
        PriceInCents = 2900,
        NextBillingDate = DateTimeOffset.UtcNow.AddDays(30),
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNeitherExists()
    {
        WithPlans(BasicPlan);
        _gateway.FindCustomerByReferenceAsync(USER, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>()).Returns(Customer());
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BillingSubscription?)null);
        _gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>()).Returns(Active());

        var result = await CreateService().SubscribeAsync(USER, "basic-plan");

        Assert.True(result.Created);
        Assert.Equal("active", result.Subscription.State);
        await _gateway.Received(1).CreateCustomerAsync(
            Arg.Is<NewBillingCustomer>(c => c.Reference == USER && c.Email == USER),
            Arg.Any<CancellationToken>());
        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewBillingSubscription>(s =>
                s.PlanHandle == "basic-plan" &&
                s.CustomerReference == USER &&
                s.Reference == $"eshoponweb:{USER}:basic-plan" &&
                s.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsesDefaultCollectionForPlansThatRequirePaymentMethod()
    {
        WithPlans(new SubscriptionPlan { Handle = "pro", Name = "Pro", PriceInCents = 29900, RequiresPaymentMethod = true });
        _gateway.FindCustomerByReferenceAsync(USER, Arg.Any<CancellationToken>()).Returns(Customer());
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BillingSubscription?)null);
        _gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>()).Returns(Active());

        await CreateService().SubscribeAsync(USER, "pro");

        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewBillingSubscription>(s => s.PaymentCollectionMethod == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesExistingCustomer()
    {
        WithPlans(BasicPlan);
        _gateway.FindCustomerByReferenceAsync(USER, Arg.Any<CancellationToken>()).Returns(Customer());
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BillingSubscription?)null);
        _gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>()).Returns(Active());

        var result = await CreateService().SubscribeAsync(USER, "basic-plan");

        Assert.True(result.Created);
        await _gateway.DidNotReceive().CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplaysExistingSubscriptionWithoutCreatingAnotherOne()
    {
        WithPlans(BasicPlan);
        _gateway.FindCustomerByReferenceAsync(USER, Arg.Any<CancellationToken>()).Returns(Customer());
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Active());

        var result = await CreateService().SubscribeAsync(USER, "basic-plan");

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsPlanNotFoundForUnknownHandle()
    {
        WithPlans(BasicPlan);

        await Assert.ThrowsAsync<PlanNotFoundException>(async () => await CreateService().SubscribeAsync(USER, "no-such-plan"));

        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsPlanNotFoundForArchivedPlan()
    {
        WithPlans(new SubscriptionPlan { Handle = "basic-plan", IsArchived = true, ArchivedAt = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<PlanNotFoundException>(async () => await CreateService().SubscribeAsync(USER, "basic-plan"));
    }

    [Fact]
    public async Task ResolvesCustomerCreateConflictByReadingBack()
    {
        WithPlans(BasicPlan);
        _gateway.FindCustomerByReferenceAsync(USER, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null, Customer());
        _gateway.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioReferenceConflictException("Reference: has already been taken."));
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BillingSubscription?)null);
        _gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>()).Returns(Active());

        var result = await CreateService().SubscribeAsync(USER, "basic-plan");

        Assert.True(result.Created);
        await _gateway.Received(2).FindCustomerByReferenceAsync(USER, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolvesSubscriptionCreateConflictAsReplay()
    {
        WithPlans(BasicPlan);
        _gateway.FindCustomerByReferenceAsync(USER, Arg.Any<CancellationToken>()).Returns(Customer());
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null, Active());
        _gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioReferenceConflictException("Reference: has already been taken."));

        var result = await CreateService().SubscribeAsync(USER, "basic-plan");

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
    }

    [Fact]
    public async Task ConcurrentDoubleSubscribeCreatesExactlyOneSubscription()
    {
        WithPlans(BasicPlan);
        _gateway.FindCustomerByReferenceAsync(USER, Arg.Any<CancellationToken>()).Returns(Customer());

        var subscriptionsCreated = 0;
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => subscriptionsCreated > 0 ? Active() : (BillingSubscription?)null);
        _gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                subscriptionsCreated++;
                return Active();
            });

        var service = CreateService();
        var first = service.SubscribeAsync(USER, "basic-plan");
        var second = service.SubscribeAsync(USER, "basic-plan");
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, subscriptionsCreated);
        Assert.Single(results.Where(r => r.Created));
    }

    [Fact]
    public async Task RejectsBlankUserReference()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () => await CreateService().SubscribeAsync(" ", "basic-plan"));
    }

    [Fact]
    public async Task UsesPlanHandleFromCatalogForSubscriptionRequest()
    {
        WithPlans(BasicPlan);
        _gateway.FindCustomerByReferenceAsync(USER, Arg.Any<CancellationToken>()).Returns(Customer());
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BillingSubscription?)null);
        _gateway.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>()).Returns(Active());

        await CreateService().SubscribeAsync(USER, "BASIC-PLAN"); // case-insensitive match on catalog handle

        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewBillingSubscription>(s => s.PlanHandle == "basic-plan"),
            Arg.Any<CancellationToken>());
    }
}
