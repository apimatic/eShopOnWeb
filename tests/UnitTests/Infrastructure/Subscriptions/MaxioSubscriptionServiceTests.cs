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
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Subscriptions;

public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string PlanHandle = "eshop-pro";

    private static readonly SubscriberIdentity Subscriber =
        new("demouser@microsoft.com", "demouser@microsoft.com");

    private static readonly string CustomerReference =
        MaxioReference.ForCustomer("eshoponweb", Subscriber.UserKey);

    private readonly IMaxioClient _client = Substitute.For<IMaxioClient>();

    private MaxioSubscriptionService CreateService()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = FamilyHandle
        });

        return new MaxioSubscriptionService(
            _client,
            options,
            new MaxioSiteMetadataCache(options, NullLogger<MaxioSiteMetadataCache>.Instance),
            new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static MaxioProduct Product(
        string handle = PlanHandle,
        string family = FamilyHandle,
        long priceInCents = 29900,
        DateTimeOffset? archivedAt = null) => new()
        {
            Id = 1,
            Handle = handle,
            Name = "Pro Plan",
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            ArchivedAt = archivedAt,
            ProductFamily = new MaxioProductFamily { Id = 9, Handle = family }
        };

    private static MaxioSubscription Subscription(
        int id = 7,
        string state = MaxioSubscriptionStates.Active,
        string handle = PlanHandle,
        string? reference = null) => new()
        {
            Id = id,
            State = state,
            Reference = reference,
            ProductPriceInCents = 29900,
            Currency = "USD",
            CurrentPeriodEndsAt = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero),
            NextAssessmentAt = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero),
            Customer = new MaxioCustomer { Id = 42, Reference = CustomerReference, Email = Subscriber.Email },
            Product = Product(handle)
        };

    private void ArrangeSite() =>
        _client.ReadSiteAsync(Arg.Any<CancellationToken>()).Returns(new MaxioSite { Currency = "USD" });

    // ---- ListPlansAsync ----

    [Fact]
    public async Task ListPlansSkipsArchivedProductsAndOrdersByPrice()
    {
        ArrangeSite();
        _client.ListProductsForProductFamilyAsync($"handle:{FamilyHandle}", 1, 200, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                Product("eshop-pro", priceInCents: 29900),
                Product("basic-plan", priceInCents: 2900),
                Product("retired-plan", priceInCents: 100, archivedAt: DateTimeOffset.UtcNow)
            });

        var plans = await CreateService().ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle));
        Assert.Equal(29m, plans[0].Price);
        Assert.Equal("USD", plans[0].Currency);
    }

    [Fact]
    public async Task ListPlansTranslatesAnUnknownProductFamilyIntoAConfigurationFault()
    {
        ArrangeSite();
        _client.ListProductsForProductFamilyAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException("listProductsForProductFamily", HttpStatusCode.NotFound, Array.Empty<string>()));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => CreateService().ListPlansAsync());

        Assert.Contains(FamilyHandle, exception.Message);
    }

    [Fact]
    public async Task ListPlansStillWorksWhenSiteMetadataIsUnavailable()
    {
        _client.ReadSiteAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioTransportException("readSite", "unreachable"));
        _client.ListProductsForProductFamilyAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { Product() });

        var plans = await CreateService().ListPlansAsync();

        Assert.Null(Assert.Single(plans).Currency);
    }

    // ---- SubscribeAsync ----

    [Fact]
    public async Task SubscribeCreatesTheCustomerWhenNoneExists()
    {
        ArrangeSite();
        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Product());
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription());

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        Assert.False(result.AlreadyExisted);
        Assert.Equal(7, result.Subscription.Id);
        Assert.True(result.Subscription.IsLive);

        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomerRequest>(r =>
                r.Customer.Reference == CustomerReference &&
                r.Customer.Email == Subscriber.Email &&
                !string.IsNullOrWhiteSpace(r.Customer.FirstName) &&
                !string.IsNullOrWhiteSpace(r.Customer.LastName)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReusesTheExistingCustomer()
    {
        ArrangeSite();
        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Product());
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription());

        await CreateService().SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeRecoversWhenAnotherWriterCreatesTheCustomerFirst()
    {
        ArrangeSite();
        var created = new MaxioCustomer { Id = 42, Reference = CustomerReference };

        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Product());
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(_ => null, _ => created);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException("createCustomer", HttpStatusCode.UnprocessableEntity,
                new[] { "Reference: must be unique - that value has been taken." }));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription());

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        Assert.Equal(7, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeReturnsTheExistingSubscriptionInsteadOfCreatingASecondOne()
    {
        ArrangeSite();
        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Product());
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(id: 99, reference: $"{CustomerReference}-{PlanHandle}") });

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        Assert.True(result.AlreadyExisted);
        Assert.Equal(99, result.Subscription.Id);

        await _client.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACanceledSubscriptionDoesNotBlockResubscribing()
    {
        ArrangeSite();
        var baseReference = MaxioReference.ForSubscription(CustomerReference, PlanHandle);
        var nextReference = MaxioReference.ForSubscription(CustomerReference, PlanHandle, attempt: 2);

        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Product());
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(id: 99, state: MaxioSubscriptionStates.Canceled, reference: baseReference) });
        _client.FindSubscriptionAsync(baseReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(id: 99, state: MaxioSubscriptionStates.Canceled, reference: baseReference));
        _client.FindSubscriptionAsync(nextReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(id: 100, reference: nextReference));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        Assert.False(result.AlreadyExisted);
        Assert.Equal(100, result.Subscription.Id);

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionRequest>(r => r.Subscription.Reference == nextReference),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeSendsTheConfiguredCollectionMethodAndCustomerId()
    {
        ArrangeSite();
        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Product());
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription());

        await CreateService().SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle));

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionRequest>(r =>
                r.Subscription.ProductHandle == PlanHandle &&
                r.Subscription.CustomerId == 42 &&
                r.Subscription.PaymentCollectionMethod == MaxioCollectionMethods.Remittance &&
                r.Subscription.Reference == MaxioReference.ForSubscription(CustomerReference, PlanHandle)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConcurrentSubscribesCreateExactlyOneSubscription()
    {
        ArrangeSite();
        var createdSubscriptions = new List<MaxioSubscription>();
        var customer = new MaxioCustomer { Id = 42, Reference = CustomerReference };

        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Product());
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(customer);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(_ => createdSubscriptions.ToArray());
        _client.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => createdSubscriptions.FirstOrDefault(s => s.Reference == (string)call[0]));
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var reference = call.Arg<MaxioCreateSubscriptionRequest>().Subscription.Reference;
                var subscription = Subscription(id: 200 + createdSubscriptions.Count, reference: reference);
                createdSubscriptions.Add(subscription);
                return subscription;
            });

        var service = CreateService();
        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle))));

        Assert.Single(createdSubscriptions);
        Assert.Equal(1, results.Count(r => !r.AlreadyExisted));
        Assert.Single(results.Select(r => r.Subscription.Id).Distinct());
    }

    [Fact]
    public async Task SubscribeRejectsAnUnknownPlan()
    {
        _client.ReadProductByHandleAsync("nope", Arg.Any<CancellationToken>()).Returns((MaxioProduct?)null);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            CreateService().SubscribeAsync(new SubscribeRequest(Subscriber, "nope")));
    }

    [Fact]
    public async Task SubscribeRejectsAPlanFromAnotherProductFamily()
    {
        _client.ReadProductByHandleAsync("other-plan", Arg.Any<CancellationToken>())
            .Returns(Product("other-plan", family: "some-other-family"));

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            CreateService().SubscribeAsync(new SubscribeRequest(Subscriber, "other-plan")));

        await _client.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeRejectsAnArchivedPlan()
    {
        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>())
            .Returns(Product(archivedAt: DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            CreateService().SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle)));
    }

    [Fact]
    public async Task SubscribeRejectsAnEmptyPlanHandle()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            CreateService().SubscribeAsync(new SubscribeRequest(Subscriber, "   ")));
    }

    // ---- ListSubscriptionsAsync ----

    [Fact]
    public async Task ListSubscriptionsReturnsEmptyWhenTheUserHasNoBillingCustomer()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        Assert.Empty(await CreateService().ListSubscriptionsAsync(Subscriber));

        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptionsReturnsNewestFirstWithMappedDetail()
    {
        ArrangeSite();
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });

        var older = Subscription(id: 1);
        older.CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = Subscription(id: 2, state: MaxioSubscriptionStates.Canceled);
        newer.CreatedAt = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new[] { older, newer });

        var subscriptions = await CreateService().ListSubscriptionsAsync(Subscriber);

        Assert.Equal(new[] { 2, 1 }, subscriptions.Select(s => s.Id));
        Assert.False(subscriptions[0].IsLive);
        Assert.True(subscriptions[1].IsLive);
        Assert.Equal(299m, subscriptions[1].Price);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), subscriptions[1].NextBillingAt);
        Assert.Equal(CustomerReference, subscriptions[1].Customer.Reference);
    }
}
