using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";

    private readonly IMaxioApiClient _apiClient = Substitute.For<IMaxioApiClient>();
    private readonly BillingSubscriber _subscriber = new("demouser@microsoft.com", "demouser@microsoft.com");

    private MaxioBillingService CreateService()
    {
        var options = Options.Create(new MaxioSettings
        {
            ApiKey = "unused-in-tests",
            Subdomain = "cp-exp-8",
            ProductFamilyHandle = FamilyHandle
        });

        return new MaxioBillingService(_apiClient, options, NullLogger<MaxioBillingService>.Instance);
    }

    private void GivenPlans()
    {
        _apiClient.ListProductsForFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                Product(7126957, "eshop-pro", "Pro Plan", 29900),
                Product(7126958, "basic-plan", "Basic Plan", 2900)
            });
    }

    [Fact]
    public async Task GetAvailablePlans_ExcludesArchived_AndOrdersByPrice()
    {
        _apiClient.ListProductsForFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                Product(1, "eshop-pro", "Pro Plan", 29900),
                Product(2, "basic-plan", "Basic Plan", 2900),
                Product(3, "old-plan", "Archived", 100) with { ArchivedAt = DateTimeOffset.UtcNow }
            });

        var plans = await CreateService().GetAvailablePlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle); // cheapest first
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal(299.00m, plans[1].Price);
    }

    [Fact]
    public async Task Subscribe_WhenNoCustomer_CreatesCustomerThenSubscription()
    {
        GivenPlans();
        _apiClient.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _apiClient.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(Customer(555, _subscriber.UserId));
        _apiClient.ListCustomerSubscriptionsAsync(555, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _apiClient.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(900, "active", "eshop-pro", 555));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(900, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        await _apiClient.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomer>(c => c.Reference == _subscriber.UserId && c.Email == _subscriber.Email),
            Arg.Any<CancellationToken>());
        await _apiClient.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(s => s.ProductHandle == "eshop-pro" && s.CustomerId == 555),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WhenCustomerExists_DoesNotCreateCustomer()
    {
        GivenPlans();
        _apiClient.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns(Customer(42, _subscriber.UserId));
        _apiClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _apiClient.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(901, "active", "eshop-pro", 42));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        await _apiClient.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WhenLiveSubscriptionExists_ReturnsExisting_WithoutCreating()
    {
        GivenPlans();
        _apiClient.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns(Customer(42, _subscriber.UserId));
        _apiClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(777, "active", "eshop-pro", 42) });

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(777, result.Id);
        await _apiClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_IgnoresCanceledSubscription_AndCreatesNew()
    {
        GivenPlans();
        _apiClient.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns(Customer(42, _subscriber.UserId));
        _apiClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(500, "canceled", "eshop-pro", 42) });
        _apiClient.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(902, "active", "eshop-pro", 42));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(902, result.Id);
    }

    [Fact]
    public async Task Subscribe_OnDuplicateSubmission_ReturnsConcurrentWinner()
    {
        GivenPlans();
        _apiClient.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns(Customer(42, _subscriber.UserId));
        // First list (pre-check): none. Second list (recovery after 409): the winner exists.
        _apiClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(
                _ => new List<MaxioSubscription>(),
                _ => new List<MaxioSubscription> { Subscription(808, "active", "eshop-pro", 42) });
        _apiClient.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioDuplicateSubmissionException());

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(808, result.Id);
    }

    [Fact]
    public async Task Subscribe_OnDuplicateSubmission_WithNoWinner_RetriesWithFreshToken()
    {
        GivenPlans();
        _apiClient.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns(Customer(42, _subscriber.UserId));
        _apiClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        // First create: duplicate submission; retry with a fresh token succeeds.
        _apiClient.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new MaxioDuplicateSubmissionException(),
                _ => Subscription(903, "active", "eshop-pro", 42));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(903, result.Id);
        await _apiClient.Received(2).CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WhenCustomerReferenceRaced_ReReadsExistingCustomer()
    {
        GivenPlans();
        // Pre-check lookup: no customer. Post-race lookup: customer now exists.
        _apiClient.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns(
                _ => (MaxioCustomer?)null,
                _ => Customer(77, _subscriber.UserId));
        _apiClient.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioDuplicateReferenceException(_subscriber.UserId));
        _apiClient.ListCustomerSubscriptionsAsync(77, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _apiClient.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(904, "active", "eshop-pro", 77));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.Equal(904, result.Id);
        await _apiClient.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(s => s.CustomerId == 77), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WithNullPlanHandle_UsesCheapestPlan()
    {
        GivenPlans();
        _apiClient.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns(Customer(42, _subscriber.UserId));
        _apiClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _apiClient.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(905, "active", "basic-plan", 42));

        var result = await CreateService().SubscribeAsync(_subscriber, planHandle: null);

        Assert.Equal("basic-plan", result.PlanHandle);
        await _apiClient.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(s => s.ProductHandle == "basic-plan"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WithUnknownPlan_ThrowsBillingException()
    {
        GivenPlans();

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => CreateService().SubscribeAsync(_subscriber, "does-not-exist"));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task GetSubscriptionsFor_WhenNoCustomer_ReturnsEmpty()
    {
        _apiClient.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var result = await CreateService().GetSubscriptionsForAsync(_subscriber);

        Assert.Empty(result);
        await _apiClient.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptionsFor_ReturnsNewestFirst()
    {
        _apiClient.LookupCustomerByReferenceAsync(_subscriber.UserId, Arg.Any<CancellationToken>())
            .Returns(Customer(42, _subscriber.UserId));
        _apiClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                Subscription(1, "canceled", "basic-plan", 42) with { CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) },
                Subscription(2, "active", "eshop-pro", 42) with { CreatedAt = DateTimeOffset.UtcNow }
            });

        var result = await CreateService().GetSubscriptionsForAsync(_subscriber);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Id); // newest first
    }

    private static MaxioProduct Product(int id, string handle, string name, long priceInCents) => new()
    {
        Id = id,
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month"
    };

    private static MaxioCustomer Customer(int id, string reference) => new()
    {
        Id = id,
        Reference = reference,
        Email = reference
    };

    private static MaxioSubscription Subscription(int id, string state, string productHandle, int customerId) => new()
    {
        Id = id,
        State = state,
        ProductPriceInCents = 29900,
        CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
        CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
        NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
        CreatedAt = DateTimeOffset.UtcNow,
        Customer = Customer(customerId, "demouser@microsoft.com"),
        Product = Product(1, productHandle, productHandle, 29900)
    };
}
