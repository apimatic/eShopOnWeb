using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscriptionServiceTestsBase
{
    protected const string FamilyHandle = "test-family";

    protected readonly IMaxioGateway _mockGateway = Substitute.For<IMaxioGateway>();
    protected readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();

    protected SubscriptionService CreateService() =>
        new SubscriptionService(
            _mockGateway,
            Microsoft.Extensions.Options.Options.Create(new MaxioOptions
            {
                ApiKey = "test-key",
                Subdomain = "test-site",
                ProductFamilyHandle = FamilyHandle
            }),
            new PerUserOperationLocks(),
            _mockLogger);

    protected static MaxioProduct Plan(string handle, int priceInCents = 29900, DateTimeOffset? archivedAt = null) =>
        new MaxioProduct(1, handle, $"{handle} name", null, priceInCents, 1, "month", RequireCreditCard: false, archivedAt);

    protected static MaxioSubscription Subscription(int id, string handle, string state = "active") =>
        new MaxioSubscription(
            Id: id,
            State: state,
            ProductPriceInCents: 29900,
            CreatedAt: DateTimeOffset.UtcNow,
            ActivatedAt: DateTimeOffset.UtcNow,
            CurrentPeriodStartedAt: DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt: DateTimeOffset.UtcNow.AddDays(30),
            NextAssessmentAt: DateTimeOffset.UtcNow.AddDays(30),
            CanceledAt: null,
            PaymentCollectionMethod: "remittance",
            Product: new MaxioProductSummary(handle, $"{handle} name"));

    protected static SubscribeCommand Command(string planHandle = "eshop-pro") =>
        new SubscribeCommand("user-1", "user1@example.com", "demouser", "example.com", planHandle);
}

public class GetPlans : SubscriptionServiceTestsBase
{
    [Fact]
    public async Task ReturnsOnlyActivePlansSortedByPrice()
    {
        _mockGateway.ListFamilyProductsAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Plan("pro", 29900), Plan("basic", 2900), Plan("old", 100, DateTimeOffset.UtcNow) });

        var service = CreateService();
        var plans = await service.GetPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic", plans[0].Handle);
        Assert.Equal("pro", plans[1].Handle);
    }
}

public class Subscribe : SubscriptionServiceTestsBase
{
    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNew()
    {
        _mockGateway.ListFamilyProductsAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Plan("eshop-pro") });
        _mockGateway.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _mockGateway.CreateCustomerAsync("user1@example.com", "demouser", "example.com", "user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(11, "user1@example.com", "demouser", "example.com", "user-1"));
        _mockGateway.ListCustomerSubscriptionsAsync(11, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _mockGateway.CreateSubscriptionAsync(11, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(Subscription(42, "eshop-pro"));

        var result = await CreateService().SubscribeAsync(Command(), CancellationToken.None);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(42, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal("active", result.Subscription.State);
    }

    [Fact]
    public async Task IsIdempotentWhenAlreadySubscribed()
    {
        _mockGateway.ListFamilyProductsAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Plan("eshop-pro") });
        _mockGateway.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(11, "user1@example.com", "demouser", "example.com", "user-1"));
        _mockGateway.ListCustomerSubscriptionsAsync(11, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(42, "eshop-pro") });

        var result = await CreateService().SubscribeAsync(Command(), CancellationToken.None);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(42, result.Subscription.Id);
        await _mockGateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IgnoresCanceledSubscriptionsOfSamePlan()
    {
        _mockGateway.ListFamilyProductsAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Plan("eshop-pro") });
        _mockGateway.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(11, "user1@example.com", "demouser", "example.com", "user-1"));
        _mockGateway.ListCustomerSubscriptionsAsync(11, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(42, "eshop-pro", state: "canceled") });
        _mockGateway.CreateSubscriptionAsync(11, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(Subscription(43, "eshop-pro"));

        var result = await CreateService().SubscribeAsync(Command(), CancellationToken.None);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(43, result.Subscription.Id);
    }

    [Fact]
    public async Task ThrowsWhenPlanNotFound()
    {
        _mockGateway.ListFamilyProductsAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Plan("basic") });

        var service = CreateService();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(Command("does-not-exist"), CancellationToken.None));
        await _mockGateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversWhenCustomerReferenceWasTakenConcurrently()
    {
        _mockGateway.ListFamilyProductsAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Plan("eshop-pro") });
        _mockGateway.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null,
                     new MaxioCustomer(11, "user1@example.com", "demouser", "example.com", "user-1"));
        _mockGateway.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException("reference taken", 422, "{\"errors\":[\"Reference: must be unique\"]}"));
        _mockGateway.ListCustomerSubscriptionsAsync(11, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _mockGateway.CreateSubscriptionAsync(11, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(Subscription(42, "eshop-pro"));

        var result = await CreateService().SubscribeAsync(Command(), CancellationToken.None);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(42, result.Subscription.Id);
    }

    [Fact]
    public async Task WrapsGatewayFailuresInBillingProviderException()
    {
        _mockGateway.ListFamilyProductsAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Plan("eshop-pro") });
        _mockGateway.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException("boom", 500, "boom"));

        var service = CreateService();

        await Assert.ThrowsAsync<BillingProviderException>(
            () => service.SubscribeAsync(Command(), CancellationToken.None));
    }
}

public class GetMySubscriptions : SubscriptionServiceTestsBase
{
    [Fact]
    public async Task ReturnsEmptyListWhenUserHasNoBillingCustomer()
    {
        _mockGateway.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var subscriptions = await CreateService().GetMySubscriptionsAsync("user-1", CancellationToken.None);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ReturnsAllSubscriptionsOfTheUser()
    {
        _mockGateway.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(11, "user1@example.com", "demouser", "example.com", "user-1"));
        _mockGateway.ListCustomerSubscriptionsAsync(11, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                Subscription(42, "eshop-pro"),
                Subscription(43, "basic-plan", state: "canceled")
            });

        var subscriptions = await CreateService().GetMySubscriptionsAsync("user-1", CancellationToken.None);

        Assert.Equal(2, subscriptions.Count());
        Assert.Contains(subscriptions, s => s.Id == 42 && s.State == "active");
        Assert.Contains(subscriptions, s => s.Id == 43 && s.State == "canceled");
    }
}
