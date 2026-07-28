using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingServiceTests
{
    // Neutral test values; the real family handle comes from configuration at runtime.
    private const string FamilyHandle = "test-family";
    private const string PlanHandle = "test-pro";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly IAppLogger<MaxioBillingService> _logger = Substitute.For<IAppLogger<MaxioBillingService>>();
    private readonly SubscriberIdentity _subscriber = new("demouser@microsoft.com");

    private MaxioBillingService CreateService()
    {
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = FamilyHandle,
        });
        return new MaxioBillingService(_client, settings, _logger);
    }

    private void SeedPlans()
    {
        _client.ListProductsForFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<ProductDto>
            {
                new() { Id = 1, Handle = PlanHandle, Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
                new() { Id = 2, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" },
            });
    }

    [Fact]
    public async Task ListPlans_ExcludesArchivedProducts()
    {
        _client.ListProductsForFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<ProductDto>
            {
                new() { Id = 1, Handle = PlanHandle, Name = "Pro Plan", PriceInCents = 29900 },
                new() { Id = 3, Handle = "old", Name = "Old", ArchivedAt = System.DateTimeOffset.UtcNow },
            });

        var plans = await CreateService().ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal(PlanHandle, plans.First().Handle);
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_ThrowsPlanNotFound()
    {
        SeedPlans();
        var service = CreateService();

        await Assert.ThrowsAsync<PlanNotFoundException>(
            () => service.SubscribeAsync(_subscriber, "does-not-exist"));

        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_NoCustomer_CreatesCustomerThenSubscription_Cardless()
    {
        SeedPlans();
        _client.FindCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>()).Returns((CustomerDto?)null);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerDto>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerDto { Id = 42, Reference = _subscriber.Reference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<SubscriptionDto>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionDto>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionDto { Id = 100, State = "active", ProductPriceInCents = 29900, Product = new ProductDto { Handle = PlanHandle, Name = "Pro Plan" } });

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.False(result.AlreadyEnrolled);
        Assert.Equal(100, result.Subscription.Id);
        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomerDto>(c => c.Reference == _subscriber.Reference && c.Email == _subscriber.Email),
            Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionDto>(s => s.ProductHandle == PlanHandle && s.CustomerId == 42 && s.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ExistingLiveSubscription_IsIdempotent_NoDuplicate()
    {
        SeedPlans();
        _client.FindCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(new CustomerDto { Id = 42, Reference = _subscriber.Reference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionDto>
            {
                new() { Id = 100, State = "active", ProductPriceInCents = 29900, Product = new ProductDto { Handle = PlanHandle, Name = "Pro Plan" } },
            });

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.True(result.AlreadyEnrolled);
        Assert.Equal(100, result.Subscription.Id);
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomerDto>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_CanceledSubscriptionToSamePlan_CreatesNew()
    {
        SeedPlans();
        _client.FindCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(new CustomerDto { Id = 42 });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionDto>
            {
                new() { Id = 99, State = "canceled", Product = new ProductDto { Handle = PlanHandle } },
            });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionDto>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionDto { Id = 101, State = "active", Product = new ProductDto { Handle = PlanHandle } });

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.False(result.AlreadyEnrolled);
        Assert.Equal(101, result.Subscription.Id);
        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<CreateSubscriptionDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ConcurrentCustomerCreate_RecoversViaReadByReference()
    {
        SeedPlans();
        // First lookup misses; create loses the race (422); re-lookup finds the winner's customer.
        _client.FindCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns((CustomerDto?)null, new CustomerDto { Id = 77, Reference = _subscriber.Reference });
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerDto>(), Arg.Any<CancellationToken>())
            .Returns<CustomerDto>(_ => throw MaxioApiException.FromResponse(
                HttpStatusCode.UnprocessableEntity, "createCustomer",
                "{\"errors\":[\"Reference: must be unique - that value has been taken.\"]}"));
        _client.ListCustomerSubscriptionsAsync(77, Arg.Any<CancellationToken>()).Returns(new List<SubscriptionDto>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionDto>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionDto { Id = 102, State = "active", Product = new ProductDto { Handle = PlanHandle } });

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.False(result.AlreadyEnrolled);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionDto>(s => s.CustomerId == 77), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptions_NoCustomer_ReturnsEmpty()
    {
        _client.FindCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>()).Returns((CustomerDto?)null);

        var subs = await CreateService().ListSubscriptionsAsync(_subscriber);

        Assert.Empty(subs);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }
}
