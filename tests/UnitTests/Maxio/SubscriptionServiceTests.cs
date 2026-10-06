using System.Net;
using Maxio;
using Maxio.Configuration;
using Maxio.Exceptions;
using Maxio.Models;
using Maxio.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class SubscriptionServiceTests
{
    private const string UserEmail = "demouser@microsoft.com";
    private const string PlanHandle = "eshop-pro";
    private const string CustomerReference = "eshop:demouser@microsoft.com";

    private readonly IMaxioClient _maxioClient = Substitute.For<IMaxioClient>();
    private readonly SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        });
        _service = new SubscriptionService(_maxioClient, options, NullLogger<SubscriptionService>.Instance);
    }

    [Fact]
    public async Task GetPlansAsync_ReturnsOnlyNonArchivedProductsSortedByPrice()
    {
        _maxioClient.ListProductsForProductFamilyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<Product>
            {
                new() { Id = 1, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, ArchivedAt = null },
                new() { Id = 2, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, ArchivedAt = null },
                new() { Id = 3, Handle = "old-plan", Name = "Old Plan", PriceInCents = 100, ArchivedAt = DateTimeOffset.UtcNow }
            });

        var plans = await _service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle);
        Assert.Equal("eshop-pro", plans[1].Handle);
        await _maxioClient.Received(1).ListProductsForProductFamilyAsync("handle:eshop-subscribe", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerWhenMissing()
    {
        _maxioClient.GetCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((Customer?)null);
        _maxioClient.CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new Customer { Id = 42, Reference = CustomerReference, Email = UserEmail });
        _maxioClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<Subscription>());
        _maxioClient.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new Subscription { Id = 100, State = "active", Product = new Product { Handle = PlanHandle } });

        var subscription = await _service.SubscribeAsync(UserEmail, PlanHandle);

        Assert.Equal(100, subscription.Id);
        await _maxioClient.Received(1).CreateCustomerAsync(Arg.Is<CreateCustomer>(c =>
            c.Email == UserEmail && c.Reference == CustomerReference), Arg.Any<CancellationToken>());
        await _maxioClient.Received(1).CreateSubscriptionAsync(Arg.Is<CreateSubscription>(s =>
            s.ProductHandle == PlanHandle && s.CustomerId == 42 && s.PaymentCollectionMethod == "remittance"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ReusesExistingCustomer()
    {
        _maxioClient.GetCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new Customer { Id = 42, Reference = CustomerReference });
        _maxioClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<Subscription>());
        _maxioClient.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new Subscription { Id = 100, State = "active" });

        await _service.SubscribeAsync(UserEmail, PlanHandle);

        await _maxioClient.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscriptionForSamePlan()
    {
        var existing = new Subscription
        {
            Id = 55,
            State = "active",
            Product = new Product { Handle = PlanHandle }
        };
        _maxioClient.GetCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new Customer { Id = 42, Reference = CustomerReference });
        _maxioClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<Subscription> { existing });

        var result = await _service.SubscribeAsync(UserEmail, PlanHandle);

        Assert.Equal(55, result.Id);
        await _maxioClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesNewSubscriptionWhenExistingIsCanceled()
    {
        var canceled = new Subscription
        {
            Id = 55,
            State = "canceled",
            Product = new Product { Handle = PlanHandle }
        };
        _maxioClient.GetCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new Customer { Id = 42, Reference = CustomerReference });
        _maxioClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<Subscription> { canceled });
        _maxioClient.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new Subscription { Id = 100, State = "active" });

        var result = await _service.SubscribeAsync(UserEmail, PlanHandle);

        Assert.Equal(100, result.Id);
        await _maxioClient.Received(1).CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_HandlesCustomerCreateRace()
    {
        _maxioClient.GetCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((Customer?)null, new Customer { Id = 42, Reference = CustomerReference });
        _maxioClient.CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Customer>(new MaxioApiException(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"Reference: already taken\"]}", new[] { "Reference: already taken" }, "duplicate")));
        _maxioClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<Subscription>());
        _maxioClient.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new Subscription { Id = 100, State = "active" });

        var result = await _service.SubscribeAsync(UserEmail, PlanHandle);

        Assert.Equal(100, result.Id);
        await _maxioClient.Received(2).GetCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>());
        await _maxioClient.Received(1).CreateSubscriptionAsync(Arg.Is<CreateSubscription>(s => s.CustomerId == 42), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMySubscriptionsAsync_ReturnsEmptyWhenNoCustomer()
    {
        _maxioClient.GetCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        var result = await _service.GetMySubscriptionsAsync(UserEmail);

        Assert.Empty(result);
        await _maxioClient.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMySubscriptionsAsync_ReturnsCustomerSubscriptions()
    {
        _maxioClient.GetCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new Customer { Id = 42, Reference = CustomerReference });
        _maxioClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<Subscription>
            {
                new() { Id = 100, State = "active", Product = new Product { Handle = PlanHandle } }
            });

        var result = await _service.GetMySubscriptionsAsync(UserEmail);

        var subscription = Assert.Single(result);
        Assert.Equal(100, subscription.Id);
    }
}
