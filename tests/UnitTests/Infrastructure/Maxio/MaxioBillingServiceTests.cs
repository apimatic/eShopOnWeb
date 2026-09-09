using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Behavioural tests for the subscribe orchestration and its idempotency guarantees, exercising the
/// service against a substituted <see cref="IMaxioApiClient"/> (no network).
/// </summary>
public class MaxioBillingServiceTests
{
    private const string UserName = "demouser@microsoft.com";
    private const string PlanHandle = "eshop-pro";
    private const string FamilyHandle = "eshop-subscribe";

    private readonly IMaxioApiClient _apiClient = Substitute.For<IMaxioApiClient>();

    private MaxioBillingService CreateService()
    {
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle
        });
        return new MaxioBillingService(_apiClient, settings, NullLogger<MaxioBillingService>.Instance);
    }

    private void ArrangePlanExists()
    {
        _apiClient.ListProductsForFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Product(PlanHandle, 29900) });
    }

    private static MaxioProduct Product(string handle, long priceInCents, DateTimeOffset? archivedAt = null) => new()
    {
        Id = 1,
        Handle = handle,
        Name = handle,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        Currency = "USD",
        ArchivedAt = archivedAt
    };

    private static MaxioCustomer Customer(long id) => new() { Id = id, Reference = UserName, Email = UserName };

    private static MaxioSubscription Subscription(long id, string state, string planHandle) => new()
    {
        Id = id,
        State = state,
        ProductPriceInCents = 29900,
        CreatedAt = DateTimeOffset.UtcNow,
        CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
        Product = Product(planHandle, 29900),
        Customer = Customer(1)
    };

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionWhenNoneExist()
    {
        ArrangePlanExists();
        _apiClient.FindCustomerByReferenceAsync(UserName, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _apiClient.CreateCustomerAsync(Arg.Any<MaxioCustomerAttributes>(), Arg.Any<CancellationToken>()).Returns(Customer(1));
        _apiClient.ListCustomerSubscriptionsAsync(1, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _apiClient.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(500, "active", PlanHandle));

        var result = await CreateService().SubscribeAsync(UserName, PlanHandle);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(500, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);

        await _apiClient.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCustomerAttributes>(a => a.Reference == UserName && a.Email == UserName), Arg.Any<CancellationToken>());
        await _apiClient.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioSubscriptionAttributes>(a =>
                a.ProductHandle == PlanHandle &&
                a.CustomerId == 1 &&
                a.PaymentCollectionMethod == "remittance" &&
                a.UniquenessToken == $"eshopweb-subscribe:{UserName}:{PlanHandle}"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeIsIdempotentWhenLiveSubscriptionAlreadyExists()
    {
        ArrangePlanExists();
        _apiClient.FindCustomerByReferenceAsync(UserName, Arg.Any<CancellationToken>()).Returns(Customer(1));
        _apiClient.ListCustomerSubscriptionsAsync(1, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(777, "active", PlanHandle) });

        var result = await CreateService().SubscribeAsync(UserName, PlanHandle);

        Assert.True(result.AlreadyExisted);
        Assert.Equal(777, result.Subscription.Id);
        await _apiClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReSubscribesWhenOnlyDeadSubscriptionExists()
    {
        ArrangePlanExists();
        _apiClient.FindCustomerByReferenceAsync(UserName, Arg.Any<CancellationToken>()).Returns(Customer(1));
        _apiClient.ListCustomerSubscriptionsAsync(1, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(1, "canceled", PlanHandle) });
        _apiClient.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(900, "active", PlanHandle));

        var result = await CreateService().SubscribeAsync(UserName, PlanHandle);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(900, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeThrowsWhenPlanIsUnknown()
    {
        ArrangePlanExists(); // family only contains eshop-pro

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(UserName, "does-not-exist"));

        await _apiClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeRecoversFromConcurrentCreateConflict()
    {
        ArrangePlanExists();
        _apiClient.FindCustomerByReferenceAsync(UserName, Arg.Any<CancellationToken>()).Returns(Customer(1));
        // Pre-check finds nothing; after the conflict, the racing request's subscription is visible.
        _apiClient.ListCustomerSubscriptionsAsync(1, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>(), new List<MaxioSubscription> { Subscription(999, "active", PlanHandle) });
        _apiClient.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException(HttpStatusCode.Conflict,
                new[] { "DuplicatePrevention::DuplicateSubmissionError" }, "POST subscriptions.json"));

        var result = await CreateService().SubscribeAsync(UserName, PlanHandle);

        Assert.True(result.AlreadyExisted);
        Assert.Equal(999, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeRecoversWhenCustomerReferenceRaceReturns422()
    {
        ArrangePlanExists();
        _apiClient.FindCustomerByReferenceAsync(UserName, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, Customer(1));
        _apiClient.CreateCustomerAsync(Arg.Any<MaxioCustomerAttributes>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException(HttpStatusCode.UnprocessableEntity,
                new[] { "Reference: must be unique - that value has been taken." }, "POST customers.json"));
        _apiClient.ListCustomerSubscriptionsAsync(1, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _apiClient.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionAttributes>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(501, "active", PlanHandle));

        var result = await CreateService().SubscribeAsync(UserName, PlanHandle);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(501, result.Subscription.Id);
    }

    [Fact]
    public async Task GetSubscriptionsReturnsEmptyWhenNoCustomer()
    {
        _apiClient.FindCustomerByReferenceAsync(UserName, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var subscriptions = await CreateService().GetSubscriptionsForUserAsync(UserName);

        Assert.Empty(subscriptions);
        await _apiClient.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAvailablePlansFiltersArchivedAndOrdersByPrice()
    {
        _apiClient.ListProductsForFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            Product("eshop-pro", 29900),
            Product("basic-plan", 2900),
            Product("legacy", 100, archivedAt: DateTimeOffset.UtcNow)
        });

        var plans = await CreateService().GetAvailablePlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle); // cheapest first
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal("299.00", plans[1].FormattedPrice);
    }
}
