using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services;

public class MaxioSubscriptionServiceTests
{
    private static readonly IOptions<MaxioOptions> Options =
        Microsoft.Extensions.Options.Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" });

    private const string UserId = "user-1";
    private const string CustomerReference = "eshop-user-user-1";
    private const string ProSubscriptionReference = "eshop-sub-user-1-eshop-pro";

    private static MaxioSubscription Subscription(string state = "active", string handle = "eshop-pro") =>
        new()
        {
            Id = 42,
            State = state,
            Reference = ProSubscriptionReference,
            Product = new MaxioProduct { Id = 7, Handle = handle, Name = "Pro Plan", PriceInCents = 29900 },
            ProductPriceInCents = 29900,
            NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1)
        };

    [Fact]
    public async Task SubscribeAsync_WhenNoCustomerOrSubscriptionExists_CreatesBoth()
    {
        var maxio = Substitute.For<IMaxioApiClient>();
        maxio.FindSubscriptionByReferenceAsync(ProSubscriptionReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        maxio.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>()).Returns(new MaxioCustomer { Id = 5, Reference = CustomerReference });
        maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new[] { new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 } });
        maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>()).Returns(Subscription());

        var service = new MaxioSubscriptionService(maxio, Options);

        var result = await service.SubscribeAsync("eshop-pro", UserId, "demouser@microsoft.com", "demouser@microsoft.com");

        Assert.True(result.Created);
        Assert.Equal(42, result.Subscription.SubscriptionId);
        Assert.Equal(299m, result.Subscription.Price);
        await maxio.Received(1).CreateCustomerAsync(Arg.Is<MaxioCreateCustomer>(c =>
            c.Reference == CustomerReference && c.Email == "demouser@microsoft.com"));
        await maxio.Received(1).CreateSubscriptionAsync(Arg.Is<MaxioCreateSubscription>(s =>
            s.ProductHandle == "eshop-pro" && s.CustomerId == 5 && s.Reference == ProSubscriptionReference));
    }

    [Fact]
    public async Task SubscribeAsync_WhenLiveSubscriptionAlreadyExists_DoesNotCreateAnything()
    {
        var maxio = Substitute.For<IMaxioApiClient>();
        maxio.FindSubscriptionByReferenceAsync(ProSubscriptionReference, Arg.Any<CancellationToken>()).Returns(Subscription("active"));

        var service = new MaxioSubscriptionService(maxio, Options);

        var result = await service.SubscribeAsync("eshop-pro", UserId, "demouser@microsoft.com", "demouser@microsoft.com");

        Assert.False(result.Created);
        Assert.Equal(42, result.Subscription.SubscriptionId);
        await maxio.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!);
        await maxio.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!);
    }

    [Fact]
    public async Task SubscribeAsync_WhenCanceledSubscriptionExists_CreatesFreshSubscription()
    {
        var maxio = Substitute.For<IMaxioApiClient>();
        maxio.FindSubscriptionByReferenceAsync(ProSubscriptionReference, Arg.Any<CancellationToken>()).Returns(Subscription("canceled"));
        maxio.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(new MaxioCustomer { Id = 5 });
        maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new[] { new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 } });
        maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>()).Returns(Subscription());

        var service = new MaxioSubscriptionService(maxio, Options);

        var result = await service.SubscribeAsync("eshop-pro", UserId, "demouser@microsoft.com", "demouser@microsoft.com");

        Assert.True(result.Created);
        await maxio.Received(1).CreateSubscriptionAsync(Arg.Is<MaxioCreateSubscription>(s =>
            s.Reference != ProSubscriptionReference && s.Reference.StartsWith(ProSubscriptionReference)));
    }

    [Fact]
    public async Task SubscribeAsync_WhenCustomerCreateLosesRace_RecoversExistingCustomer()
    {
        var maxio = Substitute.For<IMaxioApiClient>();
        maxio.FindSubscriptionByReferenceAsync(ProSubscriptionReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        maxio.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null,
                     new MaxioCustomer { Id = 9, Reference = CustomerReference });
        maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>())
            .Throws(new MaxioApiException(System.Net.HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"API Reference: must be unique\"]}"));
        maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new[] { new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 } });
        maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>()).Returns(Subscription());

        var service = new MaxioSubscriptionService(maxio, Options);

        var result = await service.SubscribeAsync("eshop-pro", UserId, "demouser@microsoft.com", "demouser@microsoft.com");

        Assert.True(result.Created);
        await maxio.Received(1).CreateSubscriptionAsync(Arg.Is<MaxioCreateSubscription>(s => s.CustomerId == 9));
    }

    [Fact]
    public async Task SubscribeAsync_WhenPlanNotInFamily_ThrowsPlanNotFound()
    {
        var maxio = Substitute.For<IMaxioApiClient>();
        maxio.FindSubscriptionByReferenceAsync(ProSubscriptionReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        maxio.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(new MaxioCustomer { Id = 5 });
        maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new[] { new MaxioProduct { Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900 } });

        var service = new MaxioSubscriptionService(maxio, Options);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            service.SubscribeAsync("eshop-pro", UserId, "demouser@microsoft.com", "demouser@microsoft.com"));
    }

    [Fact]
    public async Task GetPlansAsync_FiltersArchivedProducts_AndMapsPrices()
    {
        var maxio = Substitute.For<IMaxioApiClient>();
        maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 },
                new MaxioProduct { Handle = "old", Name = "Old Plan", PriceInCents = 1000, ArchivedAt = DateTimeOffset.UtcNow }
            });

        var service = new MaxioSubscriptionService(maxio, Options);

        var plans = await service.GetPlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans.Single().Handle);
        Assert.Equal(299m, plans.Single().Price);
    }

    [Fact]
    public async Task GetMySubscriptionsAsync_WhenNoCustomer_ReturnsEmptyList()
    {
        var maxio = Substitute.For<IMaxioApiClient>();
        maxio.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var service = new MaxioSubscriptionService(maxio, Options);

        var subscriptions = await service.GetMySubscriptionsAsync(UserId);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task GetMySubscriptionsAsync_ListsCustomerSubscriptions()
    {
        var maxio = Substitute.For<IMaxioApiClient>();
        maxio.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(new MaxioCustomer { Id = 5 });
        maxio.ListCustomerSubscriptionsAsync(5, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription("active"), Subscription("canceled", "basic-plan") });

        var service = new MaxioSubscriptionService(maxio, Options);

        var subscriptions = await service.GetMySubscriptionsAsync(UserId);

        Assert.Equal(2, subscriptions.Count);
        Assert.Contains(subscriptions, s => s.PlanHandle == "basic-plan" && s.State == "canceled");
    }
}