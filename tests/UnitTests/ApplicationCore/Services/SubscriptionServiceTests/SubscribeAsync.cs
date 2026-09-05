using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscribeAsync
{
    private const string BuyerReference = "buyer@example.com";
    private const string PlanHandle = "eshop-pro";

    private readonly IMaxioService _mockMaxioService = Substitute.For<IMaxioService>();

    [Fact]
    public async Task CreatesCustomerThenSubscriptionWhenBuyerIsNew()
    {
        _mockMaxioService.FindCustomerByReferenceAsync(BuyerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        var newCustomer = new MaxioCustomer { Id = 42, Reference = BuyerReference };
        _mockMaxioService.CreateCustomerAsync(Arg.Any<NewMaxioCustomer>(), Arg.Any<CancellationToken>())
            .Returns(newCustomer);
        _mockMaxioService.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(System.Array.Empty<MaxioSubscription>());
        var createdSubscription = new MaxioSubscription { Id = 1, State = "active" };
        _mockMaxioService.CreateSubscriptionAsync(BuyerReference, PlanHandle, Arg.Any<CancellationToken>())
            .Returns(createdSubscription);

        var subscriptionService = new SubscriptionService(_mockMaxioService);
        var result = await subscriptionService.SubscribeAsync(BuyerReference, BuyerReference, PlanHandle);

        Assert.Equal(createdSubscription, result);
        await _mockMaxioService.Received(1).CreateCustomerAsync(Arg.Any<NewMaxioCustomer>(), Arg.Any<CancellationToken>());
        await _mockMaxioService.Received(1).CreateSubscriptionAsync(BuyerReference, PlanHandle, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotCreateCustomerWhenOneAlreadyExists()
    {
        var existingCustomer = new MaxioCustomer { Id = 7, Reference = BuyerReference };
        _mockMaxioService.FindCustomerByReferenceAsync(BuyerReference, Arg.Any<CancellationToken>())
            .Returns(existingCustomer);
        _mockMaxioService.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(System.Array.Empty<MaxioSubscription>());
        _mockMaxioService.CreateSubscriptionAsync(BuyerReference, PlanHandle, Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription { Id = 2, State = "active" });

        var subscriptionService = new SubscriptionService(_mockMaxioService);
        await subscriptionService.SubscribeAsync(BuyerReference, BuyerReference, PlanHandle);

        await _mockMaxioService.DidNotReceive().CreateCustomerAsync(Arg.Any<NewMaxioCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingSubscriptionInsteadOfCreatingADuplicate()
    {
        var existingCustomer = new MaxioCustomer { Id = 7, Reference = BuyerReference };
        _mockMaxioService.FindCustomerByReferenceAsync(BuyerReference, Arg.Any<CancellationToken>())
            .Returns(existingCustomer);

        var existingSubscription = new MaxioSubscription
        {
            Id = 99,
            State = "active",
            Product = new MaxioProduct { Handle = PlanHandle }
        };
        _mockMaxioService.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new[] { existingSubscription });

        var subscriptionService = new SubscriptionService(_mockMaxioService);
        var result = await subscriptionService.SubscribeAsync(BuyerReference, BuyerReference, PlanHandle);

        Assert.Equal(existingSubscription, result);
        await _mockMaxioService.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatesANewSubscriptionWhenExistingOnesAreForADifferentPlan()
    {
        var existingCustomer = new MaxioCustomer { Id = 7, Reference = BuyerReference };
        _mockMaxioService.FindCustomerByReferenceAsync(BuyerReference, Arg.Any<CancellationToken>())
            .Returns(existingCustomer);

        var otherPlanSubscription = new MaxioSubscription
        {
            Id = 55,
            State = "active",
            Product = new MaxioProduct { Handle = "basic-plan" }
        };
        _mockMaxioService.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new[] { otherPlanSubscription });

        var newSubscription = new MaxioSubscription { Id = 56, State = "active" };
        _mockMaxioService.CreateSubscriptionAsync(BuyerReference, PlanHandle, Arg.Any<CancellationToken>())
            .Returns(newSubscription);

        var subscriptionService = new SubscriptionService(_mockMaxioService);
        var result = await subscriptionService.SubscribeAsync(BuyerReference, BuyerReference, PlanHandle);

        Assert.Equal(newSubscription, result);
    }

    [Fact]
    public async Task FallsBackToLookupWhenCreateCustomerRacesAConcurrentRequest()
    {
        _mockMaxioService.FindCustomerByReferenceAsync(BuyerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 3, Reference = BuyerReference });
        _mockMaxioService.CreateCustomerAsync(Arg.Any<NewMaxioCustomer>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException(HttpStatusCode.UnprocessableEntity, "Reference has already been taken"));
        _mockMaxioService.ListCustomerSubscriptionsAsync(3, Arg.Any<CancellationToken>())
            .Returns(System.Array.Empty<MaxioSubscription>());
        var createdSubscription = new MaxioSubscription { Id = 4, State = "active" };
        _mockMaxioService.CreateSubscriptionAsync(BuyerReference, PlanHandle, Arg.Any<CancellationToken>())
            .Returns(createdSubscription);

        var subscriptionService = new SubscriptionService(_mockMaxioService);
        var result = await subscriptionService.SubscribeAsync(BuyerReference, BuyerReference, PlanHandle);

        Assert.Equal(createdSubscription, result);
        await _mockMaxioService.Received(2).FindCustomerByReferenceAsync(BuyerReference, Arg.Any<CancellationToken>());
    }
}
