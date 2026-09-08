using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class MaxioSubscriptionServiceTests
{
    private const string FAMILY = "eshop-subscribe";
    private const string USER_ID = "b71a1f34-3f2a-4c7e-9e07-1f1c6d2f6f7e";

    private readonly IMaxioBillingClient _client = Substitute.For<IMaxioBillingClient>();
    private readonly MaxioSettings _settings = new() { ProductFamilyHandle = FAMILY };
    private readonly MaxioSubscriptionService _sut;

    public MaxioSubscriptionServiceTests()
    {
        _sut = new MaxioSubscriptionService(_client, _settings, Substitute.For<IAppLogger<MaxioSubscriptionService>>());
    }

    private static SubscriberProfile Profile() => new()
    {
        Reference = MaxioSubscriptionService.CustomerReferenceFor(USER_ID),
        FirstName = "Demo",
        LastName = "User",
        Email = "demouser@microsoft.com",
    };

    private static MaxioPlan ProPlan() => new()
    {
        Id = 1,
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
    };

    private void GivenPlans(params MaxioPlan[] plans) =>
        _client.ListPlansAsync(FAMILY, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MaxioPlan>>(plans));

    private void GivenNoCustomer() =>
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

    private void GivenCustomer(MaxioCustomer customer) =>
        _client.FindCustomerByReferenceAsync(customer.Reference!, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MaxioCustomer?>(customer));

    private void GivenSubscriptions(params MaxioSubscription[] subscriptions) =>
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MaxioSubscription>>(subscriptions.ToList()));

    [Fact]
    public async Task GetAvailablePlans_returns_plans_from_configured_family()
    {
        GivenPlans(ProPlan());

        var plans = await _sut.GetAvailablePlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
        await _client.Received(1).ListPlansAsync(FAMILY, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_creates_maxio_customer_when_none_exists()
    {
        GivenPlans(ProPlan());
        GivenNoCustomer();
        var created = new MaxioCustomer { Id = 77, Reference = Profile().Reference };
        _client.CreateCustomerAsync(Arg.Any<SubscriberProfile>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(created));
        GivenSubscriptions();
        GivenCreatedSubscription(created.Id);

        var result = await _sut.SubscribeAsync(Profile(), "eshop-pro", null);

        Assert.False(result.AlreadySubscribed);
        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<SubscriberProfile>(p => p.Reference == created.Reference), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_adopts_existing_customer_without_creating_a_new_one()
    {
        GivenPlans(ProPlan());
        GivenCustomer(new MaxioCustomer { Id = 77, Reference = Profile().Reference });
        GivenSubscriptions();
        GivenCreatedSubscription(77);

        var result = await _sut.SubscribeAsync(Profile(), "eshop-pro", null);

        Assert.False(result.AlreadySubscribed);
        await _client.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!, default);
    }

    [Fact]
    public async Task Subscribe_returns_existing_live_subscription_instead_of_creating_a_duplicate()
    {
        GivenPlans(ProPlan());
        GivenCustomer(new MaxioCustomer { Id = 77, Reference = Profile().Reference });
        var live = new MaxioSubscription { Id = 5, CustomerId = 77, State = "active", PlanHandle = "eshop-pro", PlanName = "Pro Plan", CreatedAt = DateTimeOffset.UtcNow };
        GivenSubscriptions(live);

        var result = await _sut.SubscribeAsync(Profile(), "eshop-pro", null);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(5, result.Subscription.Id);
        await _client.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default, default!, default);
    }

    [Fact]
    public async Task Subscribe_ignores_canceled_subscription_and_enrolls_again()
    {
        GivenPlans(ProPlan());
        GivenCustomer(new MaxioCustomer { Id = 77, Reference = Profile().Reference });
        var canceled = new MaxioSubscription { Id = 5, CustomerId = 77, State = "canceled", PlanHandle = "eshop-pro", PlanName = "Pro Plan", CreatedAt = DateTimeOffset.UtcNow.AddDays(-9) };
        GivenSubscriptions(canceled);
        GivenCreatedSubscription(77);

        var result = await _sut.SubscribeAsync(Profile(), "eshop-pro", null);

        Assert.False(result.AlreadySubscribed);
        await _client.Received(1).CreateSubscriptionAsync(77, "eshop-pro", Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_recovers_winners_subscription_when_maxio_flags_duplicate_submission()
    {
        GivenPlans(ProPlan());
        GivenCustomer(new MaxioCustomer { Id = 77, Reference = Profile().Reference });
        var raced = new MaxioSubscription { Id = 9, CustomerId = 77, State = "active", PlanHandle = "eshop-pro", PlanName = "Pro Plan", CreatedAt = DateTimeOffset.UtcNow };

        // First pre-check sees nothing; after the 409 the lookup finds the winning subscription.
        _client.ListCustomerSubscriptionsAsync(77, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<IReadOnlyList<MaxioSubscription>>(new List<MaxioSubscription>()),
                Task.FromResult<IReadOnlyList<MaxioSubscription>>(new List<MaxioSubscription> { raced }));
        _client.CreateSubscriptionAsync(77, "eshop-pro", Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MaxioSubscription>(
                new MaxioApiException(HttpStatusCode.Conflict, new[] { "DuplicatePrevention::DuplicateSubmissionError" })));

        var result = await _sut.SubscribeAsync(Profile(), "eshop-pro", null);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(9, result.Subscription.Id);
    }

    [Fact]
    public async Task Subscribe_sends_uniqueness_token_scoped_to_client_request_id()
    {
        GivenPlans(ProPlan());
        GivenCustomer(new MaxioCustomer { Id = 77, Reference = Profile().Reference });
        GivenSubscriptions();
        GivenCreatedSubscription(77);

        await _sut.SubscribeAsync(Profile(), "eshop-pro", "req-42");

        await _client.Received(1).CreateSubscriptionAsync(
            77, "eshop-pro", Arg.Any<string?>(),
            Arg.Is<string>(t => t.Contains("req-42")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_rejects_plan_that_is_not_offered()
    {
        GivenPlans(ProPlan());

        await Assert.ThrowsAsync<PlanNotOfferedException>(() => _sut.SubscribeAsync(Profile(), "no-such-plan", null));
        await _client.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default, default!, default, default!, default);
    }

    [Fact]
    public async Task EnsureCustomer_adopts_customer_created_by_racing_request()
    {
        var customer = new MaxioCustomer { Id = 88, Reference = Profile().Reference };
        GivenNoCustomer();
        _client.CreateCustomerAsync(Arg.Any<SubscriberProfile>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MaxioCustomer>(
                new MaxioApiException(HttpStatusCode.UnprocessableEntity, new[] { "Reference has already been taken" })));
        _client.FindCustomerByReferenceAsync(customer.Reference!, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<MaxioCustomer?>(null),
                Task.FromResult<MaxioCustomer?>(customer));

        var result = await _sut.EnsureCustomerAsync(Profile());

        Assert.Equal(88, result.Id);
    }

    [Fact]
    public async Task GetSubscriptions_returns_empty_when_shopper_has_no_maxio_customer()
    {
        GivenNoCustomer();

        var result = await _sut.GetSubscriptionsForSubscriberAsync(USER_ID);

        Assert.Empty(result);
        await _client.DidNotReceiveWithAnyArgs().ListCustomerSubscriptionsAsync(default, default);
    }

    [Fact]
    public async Task Throws_when_product_family_not_configured()
    {
        var sut = new MaxioSubscriptionService(_client, new MaxioSettings(), Substitute.For<IAppLogger<MaxioSubscriptionService>>());

        await Assert.ThrowsAsync<MaxioIntegrationException>(() => sut.GetAvailablePlansAsync());
    }

    private void GivenCreatedSubscription(long customerId) =>
        _client.CreateSubscriptionAsync(customerId, "eshop-pro", Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MaxioSubscription
            {
                Id = 1234,
                CustomerId = customerId,
                State = "active",
                PlanHandle = "eshop-pro",
                PlanName = "Pro Plan",
                PriceInCents = 29900,
                CreatedAt = DateTimeOffset.UtcNow,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            }));
}
