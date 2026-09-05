using System.Net;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string Username = "shopper@example.com";
    private const string PlanHandle = "eshop-pro";

    private readonly IMaxioClient _client = Substitute.For<IMaxioClient>();
    private readonly MaxioSubscriptionService _sut;

    public MaxioSubscriptionServiceTests()
    {
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" });
        _sut = new MaxioSubscriptionService(_client, options);
    }

    private static MaxioCustomer Customer(int id = 1) => new() { Id = id, Reference = Username, Email = Username };

    private static MaxioSubscription Subscription(int id, string state, string planHandle = PlanHandle) => new()
    {
        Id = id,
        State = state,
        Product = new MaxioProduct { Handle = planHandle, Name = "Pro Plan" }
    };

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenNeitherExists()
    {
        _client.LookupCustomerByReferenceAsync(Username, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Username, Username, Arg.Any<CancellationToken>())
            .Returns(Customer(42));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(System.Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(42, PlanHandle, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(100, "active"));

        var result = await _sut.SubscribeAsync(Username, PlanHandle);

        Assert.True(result.IsNewSubscription);
        Assert.Equal(100, result.Subscription.SubscriptionId);
        await _client.Received(1).CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Username, Username, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_DoesNotCreateCustomer_WhenOneAlreadyExistsForReference()
    {
        _client.LookupCustomerByReferenceAsync(Username, Arg.Any<CancellationToken>()).Returns(Customer(7));
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>()).Returns(System.Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(7, PlanHandle, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(200, "active"));

        await _sut.SubscribeAsync(Username, PlanHandle);

        await _client.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingLiveSubscription_InsteadOfCreatingDuplicate()
    {
        _client.LookupCustomerByReferenceAsync(Username, Arg.Any<CancellationToken>()).Returns(Customer(7));
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(55, "active") });

        var result = await _sut.SubscribeAsync(Username, PlanHandle);

        Assert.False(result.IsNewSubscription);
        Assert.Equal(55, result.Subscription.SubscriptionId);
        await _client.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task SubscribeAsync_IgnoresCanceledSubscription_AndCreatesANewOne()
    {
        _client.LookupCustomerByReferenceAsync(Username, Arg.Any<CancellationToken>()).Returns(Customer(7));
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(55, "canceled") });
        _client.CreateSubscriptionAsync(7, PlanHandle, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(300, "active"));

        var result = await _sut.SubscribeAsync(Username, PlanHandle);

        Assert.True(result.IsNewSubscription);
        Assert.Equal(300, result.Subscription.SubscriptionId);
    }

    [Fact]
    public async Task SubscribeAsync_FallsBackToExistingSubscription_WhenCreateReportsDuplicateSubmission()
    {
        // First list (pre-check) finds nothing; Maxio's uniqueness_token then reports the concurrent
        // request as a duplicate (CreateSubscriptionAsync returns null); the second list (post-race)
        // finds the subscription the other request just created.
        _client.LookupCustomerByReferenceAsync(Username, Arg.Any<CancellationToken>()).Returns(Customer(7));
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(System.Array.Empty<MaxioSubscription>(), new[] { Subscription(99, "active") });
        _client.CreateSubscriptionAsync(7, PlanHandle, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null);

        var result = await _sut.SubscribeAsync(Username, PlanHandle);

        Assert.False(result.IsNewSubscription);
        Assert.Equal(99, result.Subscription.SubscriptionId);
    }

    [Fact]
    public async Task SubscribeAsync_RecoversFromCustomerCreateRace_ByReLookingUpTheReference()
    {
        // Two concurrent first-time subscribes: our create loses the reference-uniqueness race (422),
        // so we fall back to reading the customer the other request just created.
        _client.LookupCustomerByReferenceAsync(Username, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, Customer(9));
        _client.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Username, Username, Arg.Any<CancellationToken>())
            .Returns<MaxioCustomer>(_ => throw new MaxioApiException(HttpStatusCode.UnprocessableEntity, "Reference has already been taken."));
        _client.ListCustomerSubscriptionsAsync(9, Arg.Any<CancellationToken>()).Returns(System.Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(9, PlanHandle, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(11, "active"));

        var result = await _sut.SubscribeAsync(Username, PlanHandle);

        Assert.True(result.IsNewSubscription);
        Assert.Equal(11, result.Subscription.SubscriptionId);
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_ReturnsEmpty_WhenCustomerHasNeverSubscribed()
    {
        _client.LookupCustomerByReferenceAsync(Username, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var result = await _sut.GetSubscriptionsForUserAsync(Username);

        Assert.Empty(result);
        await _client.DidNotReceiveWithAnyArgs().ListCustomerSubscriptionsAsync(default, default);
    }

    [Fact]
    public async Task GetAvailablePlansAsync_MapsMaxioProductsToPlans()
    {
        _client.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new[] { new MaxioProduct { Handle = PlanHandle, Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" } });

        var plans = await _sut.GetAvailablePlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal(PlanHandle, plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
    }
}
