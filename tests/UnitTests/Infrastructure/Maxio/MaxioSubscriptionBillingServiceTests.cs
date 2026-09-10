using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "sample-family";
    private const string ProPlanHandle = "sample-pro";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly SubscriberIdentity _subscriber = SubscriberIdentity.FromEmail("shopper@example.com");

    private MaxioSubscriptionBillingService CreateService()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = FamilyHandle,
            PaymentCollectionMethod = "remittance",
        });

        return new MaxioSubscriptionBillingService(
            _client,
            new SubscriberLockRegistry(),
            options,
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private void SetupPlans(params MaxioProduct[] products) =>
        _client.ListProductsForFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(products.ToList());

    private static MaxioProduct ProProduct() => new()
    {
        Id = 1,
        Name = "Pro Plan",
        Handle = ProPlanHandle,
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        RequireCreditCard = false,
    };

    private static MaxioSubscription SubscriptionFor(string handle, string state, long id = 100) => new()
    {
        Id = id,
        State = state,
        ProductPriceInCents = 29900,
        Currency = "USD",
        Product = new MaxioProduct { Handle = handle, Name = "Pro Plan" },
    };

    [Fact]
    public async Task ListPlans_MapsProducts_AndSkipsThoseWithoutHandle()
    {
        SetupPlans(
            ProProduct(),
            new MaxioProduct { Id = 2, Name = "No Handle", Handle = null, PriceInCents = 100 });

        var service = CreateService();

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal(ProPlanHandle, plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.False(plan.RequiresPaymentMethod);
    }

    [Fact]
    public async Task Subscribe_WhenNoCustomerExists_CreatesCustomerThenSubscription()
    {
        SetupPlans(ProProduct());
        _client.FindCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 55, Reference = _subscriber.Reference });
        _client.ListCustomerSubscriptionsAsync(55, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionFor(ProPlanHandle, "active", id: 200));

        var service = CreateService();

        var result = await service.SubscribeAsync(_subscriber, ProPlanHandle);

        Assert.Equal(200, result.Id);
        Assert.Equal("active", result.State);
        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomer>(c => c.Reference == _subscriber.Reference && c.Email == _subscriber.Email),
            Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(s => s.CustomerId == 55 && s.ProductHandle == ProPlanHandle && s.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WhenCustomerExists_DoesNotCreateCustomer()
    {
        SetupPlans(ProProduct());
        _client.FindCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 55, Reference = _subscriber.Reference });
        _client.ListCustomerSubscriptionsAsync(55, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionFor(ProPlanHandle, "active", id: 201));

        var service = CreateService();

        await service.SubscribeAsync(_subscriber, ProPlanHandle);

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WhenLiveSubscriptionToSamePlanExists_ReturnsExisting_AndDoesNotCreate()
    {
        SetupPlans(ProProduct());
        _client.FindCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 55, Reference = _subscriber.Reference });
        _client.ListCustomerSubscriptionsAsync(55, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { SubscriptionFor(ProPlanHandle, "active", id: 999) });

        var service = CreateService();

        var result = await service.SubscribeAsync(_subscriber, ProPlanHandle);

        Assert.Equal(999, result.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WhenOnlyCanceledSubscriptionExists_CreatesNew()
    {
        SetupPlans(ProProduct());
        _client.FindCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 55, Reference = _subscriber.Reference });
        _client.ListCustomerSubscriptionsAsync(55, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { SubscriptionFor(ProPlanHandle, "canceled", id: 999) });
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionFor(ProPlanHandle, "active", id: 202));

        var service = CreateService();

        var result = await service.SubscribeAsync(_subscriber, ProPlanHandle);

        Assert.Equal(202, result.Id);
        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WhenPlanUnknown_Throws()
    {
        SetupPlans(ProProduct());

        var service = CreateService();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(_subscriber, "not-a-plan"));
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WhenCreateCustomerFailsButCustomerNowExists_RecoversViaLookup()
    {
        SetupPlans(ProProduct());
        // First lookup: none. After a failed create (reference race), second lookup finds it.
        _client.FindCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 77, Reference = _subscriber.Reference });
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SubscriptionBillingException("Reference has already been taken"));
        _client.ListCustomerSubscriptionsAsync(77, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionFor(ProPlanHandle, "active", id: 203));

        var service = CreateService();

        var result = await service.SubscribeAsync(_subscriber, ProPlanHandle);

        Assert.Equal(203, result.Id);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(s => s.CustomerId == 77), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptions_WhenNoCustomer_ReturnsEmpty()
    {
        _client.FindCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var service = CreateService();

        var result = await service.ListSubscriptionsAsync(_subscriber);

        Assert.Empty(result);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }
}
