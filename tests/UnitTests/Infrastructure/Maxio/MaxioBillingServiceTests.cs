using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingServiceTests
{
    private const string UserId = "user-123";
    private const string Email = "demouser@microsoft.com";

    private readonly IMaxioClient _client = Substitute.For<IMaxioClient>();
    private readonly IAppLogger<MaxioBillingService> _logger = Substitute.For<IAppLogger<MaxioBillingService>>();
    private readonly MaxioBillingService _service;

    public MaxioBillingServiceTests()
    {
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        });
        _service = new MaxioBillingService(_client, settings, _logger);

        _client.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            Plan(id: 1, handle: "basic-plan", name: "Basic Plan", priceInCents: 2900),
            Plan(id: 2, handle: "eshop-pro", name: "Pro Plan", priceInCents: 29900),
            Plan(id: 3, handle: "other-family-plan", name: "Other", priceInCents: 100, familyHandle: "other-family"),
            Plan(id: 4, handle: "archived-plan", name: "Archived", priceInCents: 500, archivedAt: DateTimeOffset.UtcNow)
        });
    }

    [Fact]
    public async Task ListPlans_ReturnsOnlyLivePlansInConfiguredFamily()
    {
        var plans = await _service.ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle));
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        _client.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Email, UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = UserId, Email = Email });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync("eshop-pro", UserId, "remittance", Arg.Any<CancellationToken>())
            .Returns(Subscription(id: 9001, handle: "eshop-pro", state: "active", priceInCents: 29900));

        var result = await _service.SubscribeAsync(UserId, Email, "Demo User", "eshop-pro");

        Assert.Equal(9001, result.SubscriptionId);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(29900, result.PriceInCents);
        await _client.Received(1).CreateSubscriptionAsync("eshop-pro", UserId, "remittance", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ReturnsExistingSubscription_WhenAlreadySubscribed()
    {
        _client.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = UserId });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>
        {
            Subscription(id: 9001, handle: "eshop-pro", state: "active")
        });

        var result = await _service.SubscribeAsync(UserId, Email, null, "eshop-pro");

        Assert.Equal(9001, result.SubscriptionId);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_AllowsResubscribe_WhenPreviousSubscriptionCanceled()
    {
        _client.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = UserId });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>
        {
            Subscription(id: 9001, handle: "eshop-pro", state: "canceled")
        });
        _client.CreateSubscriptionAsync("eshop-pro", UserId, "remittance", Arg.Any<CancellationToken>())
            .Returns(Subscription(id: 9002, handle: "eshop-pro", state: "active"));

        var result = await _service.SubscribeAsync(UserId, Email, null, "eshop-pro");

        Assert.Equal(9002, result.SubscriptionId);
    }

    [Fact]
    public async Task Subscribe_ThrowsNotFound_WhenPlanOutsideConfiguredFamily()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => _service.SubscribeAsync(UserId, Email, null, "other-family-plan"));
    }

    [Fact]
    public async Task Subscribe_RecoversFromCustomerCreateRace_On422()
    {
        _client.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 42, Reference = UserId });
        _client.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Email, UserId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException(HttpStatusCode.UnprocessableEntity, "Reference: has already been taken."));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync("basic-plan", UserId, "remittance", Arg.Any<CancellationToken>())
            .Returns(Subscription(id: 9003, handle: "basic-plan", state: "active"));

        var result = await _service.SubscribeAsync(UserId, Email, null, "basic-plan");

        Assert.Equal(9003, result.SubscriptionId);
    }

    [Fact]
    public async Task ListSubscriptions_ReturnsEmpty_WhenNoCustomerExists()
    {
        _client.FindCustomerByReferenceAsync(UserId, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var result = await _service.ListSubscriptionsAsync(UserId);

        Assert.Empty(result);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    private static MaxioProduct Plan(int id, string handle, string name, long priceInCents,
        string familyHandle = "eshop-subscribe", DateTimeOffset? archivedAt = null) => new()
    {
        Id = id,
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        ArchivedAt = archivedAt,
        ProductFamily = new MaxioProductFamily { Id = 100, Handle = familyHandle }
    };

    private static MaxioSubscription Subscription(long id, string handle, string state, long priceInCents = 2900) => new()
    {
        Id = id,
        State = state,
        PaymentCollectionMethod = "remittance",
        ActivatedAt = DateTimeOffset.UtcNow,
        CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
        NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
        Product = Plan(1, handle, handle, priceInCents)
    };
}
