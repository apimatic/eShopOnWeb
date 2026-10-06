using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    private readonly IMaxioBillingGateway _gateway = Substitute.For<IMaxioBillingGateway>();
    private readonly SubscriptionService _service;

    public Subscribe()
    {
        _service = new SubscriptionService(
            _gateway,
            Options.Create(new MaxioSettings { ProductFamilyHandle = SubscriptionServiceTestData.FAMILY_HANDLE }));

        _gateway
            .GetFamilyPlansAsync(SubscriptionServiceTestData.FAMILY_HANDLE, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Plans());
    }

    [Fact]
    public async Task CreatesCustomerAndSubscription_WhenUserHasNoBillingRecord()
    {
        _gateway.FindCustomerByReferenceAsync(SubscriptionServiceTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _gateway.CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Customer());
        _gateway.GetCustomerSubscriptionsAsync(SubscriptionServiceTestData.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.NoSubscriptions());
        _gateway.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Subscription());

        var result = await _service.SubscribeAsync(SubscriptionServiceTestData.Subscriber(), SubscriptionServiceTestData.PLAN_HANDLE);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(SubscriptionServiceTestData.SUBSCRIPTION_ID, result.Subscription.Id);

        await _gateway.CreateCustomerAsync(
            Arg.Is<CreateMaxioCustomerRequest>(c =>
                c.Reference == SubscriptionServiceTestData.CustomerReference &&
                c.Email == SubscriptionServiceTestData.EMAIL),
            Arg.Any<CancellationToken>());

        await _gateway.CreateSubscriptionAsync(
            Arg.Is<CreateMaxioSubscriptionRequest>(s =>
                s.CustomerId == SubscriptionServiceTestData.CUSTOMER_ID &&
                s.ProductId == SubscriptionServiceTestData.PLAN_ID &&
                s.Reference == SubscriptionServiceTestData.SubscriptionReference &&
                !string.IsNullOrWhiteSpace(s.UniquenessToken)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrollsCardlessPlansWithoutForcingAutomaticBilling()
    {
        ArrangeFreshUser();

        await _service.SubscribeAsync(SubscriptionServiceTestData.Subscriber(), SubscriptionServiceTestData.PLAN_HANDLE);

        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateMaxioSubscriptionRequest>(s =>
                s.PaymentCollectionMethod == SubscriptionService.PAYMENT_COLLECTION_REMITTANCE),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DelegatesPaymentHandlingToBillingSystem_ForPlansRequiringCard()
    {
        var paidPlan = SubscriptionServiceTestData.ProPlan();
        paidPlan.RequiresPaymentMethod = true;
        _gateway
            .GetFamilyPlansAsync(SubscriptionServiceTestData.FAMILY_HANDLE, Arg.Any<CancellationToken>())
            .Returns(new System.Collections.Generic.List<SubscriptionPlan> { paidPlan });
        ArrangeFreshUser();

        await _service.SubscribeAsync(SubscriptionServiceTestData.Subscriber(), SubscriptionServiceTestData.PLAN_HANDLE);

        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateMaxioSubscriptionRequest>(s => s.PaymentCollectionMethod == null),
            Arg.Any<CancellationToken>());
    }

    private void ArrangeFreshUser()
    {
        _gateway.FindCustomerByReferenceAsync(SubscriptionServiceTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Customer());
        _gateway.GetCustomerSubscriptionsAsync(SubscriptionServiceTestData.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.NoSubscriptions());
        _gateway.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Subscription());
    }

    [Fact]
    public async Task ReusesExistingCustomer_WithoutCreatingAnother()
    {
        _gateway.FindCustomerByReferenceAsync(SubscriptionServiceTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Customer());
        _gateway.GetCustomerSubscriptionsAsync(SubscriptionServiceTestData.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.NoSubscriptions());
        _gateway.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Subscription());

        await _service.SubscribeAsync(SubscriptionServiceTestData.Subscriber(), SubscriptionServiceTestData.PLAN_HANDLE);

        await _gateway
            .DidNotReceive()
            .CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsActiveSubscription_WhenDoubleClicked()
    {
        _gateway.FindCustomerByReferenceAsync(SubscriptionServiceTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Customer());
        _gateway.GetCustomerSubscriptionsAsync(SubscriptionServiceTestData.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(new System.Collections.Generic.List<MaxioSubscription> { SubscriptionServiceTestData.Subscription() });

        var first = await _service.SubscribeAsync(SubscriptionServiceTestData.Subscriber(), SubscriptionServiceTestData.PLAN_HANDLE);
        var second = await _service.SubscribeAsync(SubscriptionServiceTestData.Subscriber(), SubscriptionServiceTestData.PLAN_HANDLE);

        Assert.True(first.AlreadySubscribed);
        Assert.True(second.AlreadySubscribed);
        Assert.Equal(SubscriptionServiceTestData.SUBSCRIPTION_ID, second.Subscription.Id);

        await _gateway
            .DidNotReceive()
            .CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsWinnersSubscription_WhenConcurrentCreateLosesRace()
    {
        _gateway.FindCustomerByReferenceAsync(SubscriptionServiceTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Customer());
        _gateway.GetCustomerSubscriptionsAsync(SubscriptionServiceTestData.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(
                SubscriptionServiceTestData.NoSubscriptions(),
                new System.Collections.Generic.List<MaxioSubscription> { SubscriptionServiceTestData.Subscription() });
        _gateway.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MaxioSubscription>(new MaxioApiException(409, "DuplicatePrevention::DuplicateSubmissionError")));

        var result = await _service.SubscribeAsync(SubscriptionServiceTestData.Subscriber(), SubscriptionServiceTestData.PLAN_HANDLE);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(SubscriptionServiceTestData.SUBSCRIPTION_ID, result.Subscription.Id);
    }

    [Fact]
    public async Task ReReadsCustomer_WhenCreateIsRejectedAsDuplicate()
    {
        // Two subscribe requests arrive simultaneously for a brand-new user:
        // both miss the lookup; one wins the create, ours gets a duplicate
        // rejection and must adopt the winner's customer instead of failing.
        _gateway.FindCustomerByReferenceAsync(SubscriptionServiceTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, SubscriptionServiceTestData.Customer());
        _gateway.CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MaxioCustomer>(new MaxioApiException(422, "Reference: has already been taken")));
        _gateway.GetCustomerSubscriptionsAsync(SubscriptionServiceTestData.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.NoSubscriptions());
        _gateway.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Subscription());

        var result = await _service.SubscribeAsync(SubscriptionServiceTestData.Subscriber(), SubscriptionServiceTestData.PLAN_HANDLE);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(SubscriptionServiceTestData.SUBSCRIPTION_ID, result.Subscription.Id);
        await _gateway
            .Received(2)
            .FindCustomerByReferenceAsync(SubscriptionServiceTestData.CustomerReference, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatesNewSubscription_WhenPreviousOneWasCanceled()
    {
        _gateway.FindCustomerByReferenceAsync(SubscriptionServiceTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Customer());
        _gateway.GetCustomerSubscriptionsAsync(SubscriptionServiceTestData.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(new System.Collections.Generic.List<MaxioSubscription>
            {
                SubscriptionServiceTestData.Subscription(state: "canceled"),
            });
        _gateway.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Subscription(id: SubscriptionServiceTestData.SUBSCRIPTION_ID + 1));

        var result = await _service.SubscribeAsync(SubscriptionServiceTestData.Subscriber(), SubscriptionServiceTestData.PLAN_HANDLE);

        Assert.False(result.AlreadySubscribed);
        await _gateway.Received(1).CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsAndTouchesNothingElse_WhenPlanHandleIsNotInTheFamily()
    {
        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => _service.SubscribeAsync(SubscriptionServiceTestData.Subscriber(), "plan-not-in-family"));

        Assert.Equal("plan-not-in-family", exception.PlanHandle);
        await _gateway
            .DidNotReceive()
            .FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway
            .DidNotReceive()
            .CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PropagatesRejections_WhenNoExistingSubscriptionExplainsThem()
    {
        _gateway.FindCustomerByReferenceAsync(SubscriptionServiceTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.Customer());
        _gateway.GetCustomerSubscriptionsAsync(SubscriptionServiceTestData.CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceTestData.NoSubscriptions());
        _gateway.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MaxioSubscription>(new MaxioApiException(422, "Payment method: required by plan")));

        await Assert.ThrowsAsync<MaxioApiException>(
            () => _service.SubscribeAsync(SubscriptionServiceTestData.Subscriber(), SubscriptionServiceTestData.PLAN_HANDLE));
    }
}
