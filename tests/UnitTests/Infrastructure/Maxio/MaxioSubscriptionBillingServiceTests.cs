using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string UserName = "demouser@microsoft.com";
    private const string PlanHandle = "eshop-pro";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();

    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "key",
        Subdomain = "acme-billing",
        ProductFamilyHandle = "demo-subscriptions",
        PlanCacheSeconds = 0
    };

    private MaxioSubscriptionBillingService CreateService() => new(
        _client,
        Options.Create(_settings),
        new MemoryCache(new MemoryCacheOptions()),
        new SubscriberLockProvider(),
        NullLogger<MaxioSubscriptionBillingService>.Instance);

    private void GivenCatalog(params MaxioProduct[] products)
    {
        _client.ReadSiteAsync(Arg.Any<CancellationToken>()).Returns(new MaxioSite { Currency = "USD" });
        _client.ListProductsForProductFamilyAsync("handle:demo-subscriptions", Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(products.ToList());
    }

    private static MaxioProduct Product(string handle, string name, long priceInCents, DateTimeOffset? archivedAt = null) => new()
    {
        Id = 1,
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        ArchivedAt = archivedAt,
        ProductFamily = new MaxioProductFamily { Handle = "demo-subscriptions" }
    };

    private static MaxioSubscription Subscription(int id, string state, string planHandle, int customerId = 42) => new()
    {
        Id = id,
        State = state,
        ProductPriceInCents = 29900,
        Currency = "USD",
        NextAssessmentAt = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero),
        Product = Product(planHandle, "Pro Plan", 29900),
        Customer = new MaxioCustomer { Id = customerId, Reference = $"eshoponweb:{UserName}" }
    };

    private static MaxioApiException Unprocessable(params string[] errors) =>
        new(HttpStatusCode.UnprocessableEntity, "POST", "/subscriptions.json", errors, null);

    [Fact]
    public async Task ListPlansSkipsArchivedProductsAndOrdersByPrice()
    {
        GivenCatalog(
            Product("eshop-pro", "Pro Plan", 29900),
            Product("basic-plan", "Basic Plan", 2900),
            Product("legacy", "Legacy Plan", 100, archivedAt: DateTimeOffset.UtcNow));

        var plans = await CreateService().ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(plan => plan.Handle));
        Assert.Equal("USD", plans[0].Currency);
        Assert.Equal(29m, plans[0].Price);
    }

    [Fact]
    public async Task ListPlansStillReturnsPlansWhenTheSiteCurrencyCannotBeRead()
    {
        GivenCatalog(Product(PlanHandle, "Pro Plan", 29900));
        _client.ReadSiteAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioTransportException("unreachable", null));

        var plans = await CreateService().ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal(string.Empty, plans[0].Currency);
    }

    [Fact]
    public async Task SubscribeCreatesTheCustomerOnFirstUseAndEnrollsTheShopper()
    {
        GivenCatalog(Product(PlanHandle, "Pro Plan", 29900));
        _client.ReadCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = $"eshoponweb:{UserName}" });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(99, "active", PlanHandle));

        var result = await CreateService().SubscribeAsync(new SubscribeCommand(UserName, PlanHandle));

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.True(result.Subscription.IsLive);

        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomerRequest>(request =>
                request.Customer.Reference == $"eshoponweb:{UserName}" &&
                request.Customer.Email == UserName &&
                request.Customer.FirstName == "Demouser"),
            Arg.Any<CancellationToken>());

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionRequest>(request =>
                request.Subscription.ProductHandle == PlanHandle &&
                request.Subscription.CustomerId == 42 &&
                request.Subscription.Reference == $"eshoponweb:{UserName}:{PlanHandle}" &&
                request.Subscription.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        GivenCatalog(Product(PlanHandle, "Pro Plan", 29900));
        _client.ReadCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(99, "active", PlanHandle) });

        var result = await CreateService().SubscribeAsync(new SubscribeCommand(UserName, PlanHandle));

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeIgnoresEndedSubscriptionsWhenDecidingWhetherToEnroll()
    {
        GivenCatalog(Product(PlanHandle, "Pro Plan", 29900));
        _client.ReadCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(90, "canceled", PlanHandle) });
        // The natural reference is already taken by the canceled subscription.
        _client.FindSubscriptionByReferenceAsync($"eshoponweb:{UserName}:{PlanHandle}", Arg.Any<CancellationToken>())
            .Returns(Subscription(90, "canceled", PlanHandle));
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(91, "active", PlanHandle));

        var result = await CreateService().SubscribeAsync(new SubscribeCommand(UserName, PlanHandle));

        Assert.True(result.Created);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionRequest>(request =>
                request.Subscription.Reference!.StartsWith($"eshoponweb:{UserName}:{PlanHandle}:", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReconcilesARejectedCreateThatAnotherInstanceHadAlreadySatisfied()
    {
        GivenCatalog(Product(PlanHandle, "Pro Plan", 29900));
        _client.ReadCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(
                _ => new List<MaxioSubscription>(),
                _ => new List<MaxioSubscription> { Subscription(99, "active", PlanHandle) });
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Unprocessable("Reference: must be unique - that value has been taken."));

        var result = await CreateService().SubscribeAsync(new SubscribeCommand(UserName, PlanHandle));

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeSurfacesBillingRejectionsThatAreNotRaces()
    {
        GivenCatalog(Product(PlanHandle, "Pro Plan", 29900));
        _client.ReadCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Unprocessable("No payment method was on file for the $299.00 balance"));

        var exception = await Assert.ThrowsAsync<BillingValidationException>(
            () => CreateService().SubscribeAsync(new SubscribeCommand(UserName, PlanHandle)));

        Assert.Contains("No payment method", exception.Errors.Single());
    }

    [Fact]
    public async Task SubscribeReusesACustomerThatAConcurrentRequestCreatedFirst()
    {
        GivenCatalog(Product(PlanHandle, "Pro Plan", 29900));
        _client.ReadCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => null,
                _ => new MaxioCustomer { Id = 42 });
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException(
                HttpStatusCode.UnprocessableEntity, "POST", "/customers.json",
                new[] { "Reference: must be unique - that value has been taken." }, null));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(99, "active", PlanHandle) });

        var result = await CreateService().SubscribeAsync(new SubscribeCommand(UserName, PlanHandle));

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeRejectsAPlanThatIsNotInTheCatalog()
    {
        GivenCatalog(Product(PlanHandle, "Pro Plan", 29900));

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(new SubscribeCommand(UserName, "not-a-plan")));

        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeFallsBackToTheConfiguredDefaultPlanWhenNoneIsRequested()
    {
        _settings.DefaultPlanHandle = PlanHandle;
        GivenCatalog(Product(PlanHandle, "Pro Plan", 29900));
        _client.ReadCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(99, "active", PlanHandle));

        var result = await CreateService().SubscribeAsync(new SubscribeCommand(UserName, string.Empty));

        Assert.True(result.Created);
    }

    [Fact]
    public async Task SubscribeTranslatesCredentialFailuresIntoAGatewayError()
    {
        GivenCatalog(Product(PlanHandle, "Pro Plan", 29900));
        _client.ReadCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException(HttpStatusCode.Unauthorized, "GET", "/customers/lookup.json", Array.Empty<string>(), null));

        var exception = await Assert.ThrowsAsync<BillingGatewayException>(
            () => CreateService().SubscribeAsync(new SubscribeCommand(UserName, PlanHandle)));

        Assert.Equal((int)HttpStatusCode.Unauthorized, exception.UpstreamStatusCode);
    }

    [Fact]
    public async Task ListSubscriptionsReturnsNothingForAShopperWithNoBillingCustomer()
    {
        _client.ReadCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var subscriptions = await CreateService().ListSubscriptionsAsync(UserName);

        Assert.Empty(subscriptions);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptionsReportsNewestFirstWithTheNextBillingDate()
    {
        _client.ReadCustomerByReferenceAsync($"eshoponweb:{UserName}", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });

        var older = Subscription(1, "canceled", "basic-plan");
        older.CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = Subscription(2, "active", PlanHandle);
        newer.CreatedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { older, newer });

        var subscriptions = await CreateService().ListSubscriptionsAsync(UserName);

        Assert.Equal(new[] { 2, 1 }, subscriptions.Select(subscription => subscription.Id));
        Assert.True(subscriptions[0].IsLive);
        Assert.False(subscriptions[1].IsLive);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), subscriptions[0].NextBillingAt);
        Assert.Equal(299m, subscriptions[0].Price);
    }

    [Fact]
    public async Task RefusesToServeWhenTheIntegrationIsNotConfigured()
    {
        _settings.ApiKey = null;

        await Assert.ThrowsAsync<BillingConfigurationException>(() => CreateService().ListPlansAsync());
    }
}
