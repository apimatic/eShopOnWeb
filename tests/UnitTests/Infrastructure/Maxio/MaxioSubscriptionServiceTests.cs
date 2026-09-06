using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string PlanHandle = "eshop-pro";
    private const string CustomerReference = "eshop:demouser@microsoft.com";
    private const string SubscriptionReference = "eshop:demouser@microsoft.com:eshop-pro";
    private const string SecondSlotReference = "eshop:demouser@microsoft.com:eshop-pro#2";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly IMaxioCatalogCache _catalog = Substitute.For<IMaxioCatalogCache>();
    private readonly Subscriber _subscriber = new("demouser@microsoft.com", "demouser@microsoft.com");

    public MaxioSubscriptionServiceTests()
    {
        _catalog.GetCurrencyAsync(Arg.Any<CancellationToken>()).Returns("USD");
        _catalog.GetPlansAsync(Arg.Any<CancellationToken>()).Returns(new[] { Plan(PlanHandle) });
    }

    private MaxioSubscriptionService CreateService() => new(
        _client,
        _catalog,
        Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "acme",
            ProductFamilyHandle = "eshop-subscribe"
        }),
        NullLogger<MaxioSubscriptionService>.Instance);

    private static SubscriptionPlan Plan(string handle) => new()
    {
        Handle = handle,
        Name = "Pro Plan",
        PriceInCents = 29900,
        Currency = "USD",
        Interval = new BillingInterval(1, "month"),
        RequiresPaymentMethod = false,
        Taxable = false,
        ProductFamilyHandle = "eshop-subscribe",
        ProductId = 1
    };

    private static MaxioCustomer Customer(int id = 42) => new() { Id = id, Reference = CustomerReference };

    private static MaxioSubscription Subscription(int id, string state, string reference, int customerId = 42) => new()
    {
        Id = id,
        State = state,
        Reference = reference,
        ProductPriceInCents = 29900,
        Currency = "USD",
        CreatedAt = DateTimeOffset.UtcNow,
        Product = new MaxioProduct { Handle = PlanHandle, Name = "Pro Plan", Interval = 1, IntervalUnit = "month" },
        Customer = new MaxioCustomer { Id = customerId, Reference = CustomerReference }
    };

    private static MaxioApiException ReferenceTaken(string operation) => new(
        operation,
        HttpStatusCode.UnprocessableEntity,
        new[] { "Reference: must be unique - that value has been taken." });

    [Fact]
    public async Task CreatesTheCustomerAndSubscriptionOnAFirstSubscribe()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>()).Returns(Customer());
        _client.FindSubscriptionByReferenceAsync(SubscriptionReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, "active", SubscriptionReference));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, PlanHandle));

        Assert.False(result.AlreadySubscribed);
        Assert.True(result.CustomerCreated);
        Assert.Equal(SubscriptionState.Active, result.Subscription.State);
        Assert.Equal(SubscriptionReference, result.Subscription.Reference);
    }

    [Fact]
    public async Task SendsTheConfiguredCollectionMethodAndTheResolvedCustomerId()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer(77));
        _client.FindSubscriptionByReferenceAsync(SubscriptionReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, "active", SubscriptionReference, customerId: 77));

        await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, PlanHandle));

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionRequest>(r =>
                r.Subscription.CustomerId == 77 &&
                r.Subscription.ProductHandle == PlanHandle &&
                r.Subscription.Reference == SubscriptionReference &&
                r.Subscription.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotCreateASecondCustomerForAShopperWhoAlreadyHasOne()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer());
        _client.FindSubscriptionByReferenceAsync(SubscriptionReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, "active", SubscriptionReference));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, PlanHandle));

        Assert.False(result.CustomerCreated);
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolvesAConcurrentCustomerCreateByReadingTheWinnerBack()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, Customer(99));
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns<MaxioCustomer>(_ => throw ReferenceTaken("createCustomer"));
        _client.FindSubscriptionByReferenceAsync(SubscriptionReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, "active", SubscriptionReference, customerId: 99));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, PlanHandle));

        Assert.False(result.CustomerCreated);
        Assert.Equal(99, result.Subscription.CustomerId);
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer());
        _client.FindSubscriptionByReferenceAsync(SubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(5, "active", SubscriptionReference));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, PlanHandle));

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(5, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolvesALostCreateRaceByReadingBackTheSubscriptionThatWon()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer());
        _client.FindSubscriptionByReferenceAsync(SubscriptionReference, Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null, Subscription(8, "active", SubscriptionReference));
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns<MaxioSubscription>(_ => throw ReferenceTaken("createSubscription"));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, PlanHandle));

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(8, result.Subscription.Id);
    }

    [Fact]
    public async Task ReSubscribingAfterCancellationTakesTheNextReferenceSlot()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer());
        _client.FindSubscriptionByReferenceAsync(SubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(5, "canceled", SubscriptionReference));
        _client.FindSubscriptionByReferenceAsync(SecondSlotReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(6, "active", SecondSlotReference));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, PlanHandle));

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(6, result.Subscription.Id);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionRequest>(r => r.Subscription.Reference == SecondSlotReference),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnIdempotencyKeyPinsTheRequestToASingleReferenceEvenAfterCancellation()
    {
        const string keyedReference = "eshop:demouser@microsoft.com:key:cart-7f3a";

        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer());
        _client.FindSubscriptionByReferenceAsync(keyedReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(9, "canceled", keyedReference));

        var service = CreateService();

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => service.SubscribeAsync(new SubscribeRequest(_subscriber, PlanHandle, "cart-7f3a")));

        Assert.True(exception.IsClientError);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAPlanHandleThatIsNotOnOffer()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "not-a-plan")));

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportsMisconfigurationRatherThanCallingTheProvider()
    {
        var service = new MaxioSubscriptionService(
            _client,
            _catalog,
            Options.Create(new MaxioSettings()),
            NullLogger<MaxioSubscriptionService>.Instance);

        var exception = await Assert.ThrowsAsync<BillingNotConfiguredException>(() => service.GetPlansAsync());

        Assert.Contains(exception.Problems, p => p.Contains("Maxio:ApiKey"));
    }

    [Fact]
    public async Task ListsNothingForAShopperWhoHasNeverSubscribed()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        Assert.Empty(await CreateService().GetSubscriptionsAsync(_subscriber));

        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListsTheShoppersSubscriptionsNewestFirst()
    {
        var older = Subscription(1, "canceled", SubscriptionReference);
        older.CreatedAt = DateTimeOffset.UtcNow.AddDays(-10);
        var newer = Subscription(2, "active", SecondSlotReference);

        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { older, newer });

        var subscriptions = await CreateService().GetSubscriptionsAsync(_subscriber);

        Assert.Equal(new[] { 2, 1 }, subscriptions.Select(s => s.Id).ToArray());
        Assert.Single(subscriptions.Where(s => s.IsLive));
    }
}
