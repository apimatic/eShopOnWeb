using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class ListPlansAndSubscriptionsTests
{
    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioSubscriptionBillingService _service;

    public ListPlansAndSubscriptionsTests()
    {
        _client.GetSiteAsync(Arg.Any<CancellationToken>()).Returns(MaxioTestData.Site());

        _service = new MaxioSubscriptionBillingService(
            _client,
            MaxioTestData.Settings(),
            new MemoryCache(new MemoryCacheOptions()),
            new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task ListsPlansCheapestFirstAndOmitsArchivedOnes()
    {
        _client.ListProductsForFamilyAsync(MaxioTestData.FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MaxioTestData.Product(MaxioTestData.ProPlanHandle, "Pro Plan", 29900),
                MaxioTestData.Product("retired-plan", "Retired Plan", 100, archivedAt: DateTimeOffset.UtcNow),
                MaxioTestData.Product(MaxioTestData.BasicPlanHandle, "Basic Plan", 2900)
            });

        var plans = await _service.ListPlansAsync();

        Assert.Equal(new[] { MaxioTestData.BasicPlanHandle, MaxioTestData.ProPlanHandle }, plans.Select(p => p.Handle));
        Assert.Equal(29.00m, plans[0].Price);
        Assert.Equal(299.00m, plans[1].Price);
        Assert.All(plans, p => Assert.Equal("USD", p.Currency));
        Assert.All(plans, p => Assert.Equal("month", p.IntervalUnit));
    }

    [Fact]
    public async Task ReadsTheSiteCurrencyOnceAndThenServesItFromCache()
    {
        _client.ListProductsForFamilyAsync(MaxioTestData.FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new[] { MaxioTestData.Product() });

        await _service.ListPlansAsync();
        await _service.ListPlansAsync();

        await _client.Received(1).GetSiteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsNoSubscriptionsForAShopperWhoHasNeverSubscribed()
    {
        _client.FindCustomerByReferenceAsync(MaxioTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var subscriptions = await _service.ListSubscriptionsForUserAsync(MaxioTestData.UserName);

        Assert.Empty(subscriptions);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListsSubscriptionsMostRecentFirstIncludingEndedOnes()
    {
        _client.FindCustomerByReferenceAsync(MaxioTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns(MaxioTestData.Customer());

        var older = MaxioTestData.Subscription(id: 1, state: "canceled", planHandle: MaxioTestData.BasicPlanHandle);
        older.ActivatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        _client.ListCustomerSubscriptionsAsync(MaxioTestData.Customer().Id, Arg.Any<CancellationToken>())
            .Returns(new[] { older, MaxioTestData.Subscription(id: 2) });

        var subscriptions = await _service.ListSubscriptionsForUserAsync(MaxioTestData.UserName);

        Assert.Equal(new long[] { 2, 1 }, subscriptions.Select(s => s.Id));
        Assert.True(subscriptions[0].IsLive);
        Assert.False(subscriptions[1].IsLive);
    }

    [Fact]
    public async Task LooksTheShopperUpUnderTheSameReferenceRegardlessOfUserNameCasing()
    {
        _client.FindCustomerByReferenceAsync(MaxioTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns(MaxioTestData.Customer());
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MaxioTestData.Subscription() });

        var subscriptions = await _service.ListSubscriptionsForUserAsync(" DemoUser@Microsoft.com ");

        Assert.Single(subscriptions);
    }
}
