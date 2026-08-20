using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class SubscriptionBillingServiceTests
{
    [Fact]
    public async Task RepeatedSubscribeCreatesOneCustomerAndOneSubscription()
    {
        var maxioClient = Substitute.For<IMaxioClient>();
        var plan = new SubscriptionPlan("pro", "Pro", "", 29900, 1, "month", false);
        maxioClient.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubscriptionPlan>>(new[] { plan }));

        MaxioCustomer? customer = null;
        maxioClient.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(customer));
        maxioClient.CreateCustomerAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                customer = new MaxioCustomer(42);
                return Task.FromResult(customer);
            });

        MaxioSubscription? subscription = null;
        maxioClient.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(subscription));
        maxioClient.CreateSubscriptionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                subscription = new MaxioSubscription(
                    84,
                    42,
                    "pro",
                    "Pro",
                    "family",
                    "Default",
                    29900,
                    1,
                    "month",
                    "active",
                    DateTimeOffset.Parse("2030-01-01T00:00:00Z"));
                return Task.FromResult(subscription);
            });

        var contextOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new CatalogContext(contextOptions);
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "not-a-secret",
            Subdomain = "test",
            ProductFamilyHandle = "family"
        });
        var service = new SubscriptionBillingService(
            maxioClient,
            context,
            options,
            TimeProvider.System,
            NullLogger<SubscriptionBillingService>.Instance);
        var user = new BillingUser("user-id", "shopper@example.test");

        var concurrent = await Task.WhenAll(
            service.SubscribeAsync(user, "pro"),
            service.SubscribeAsync(user, "pro"));
        var retry = await service.SubscribeAsync(user, "pro");

        Assert.Equal(concurrent[0], concurrent[1]);
        Assert.Equal(concurrent[0], retry);
        Assert.Single(await context.SubscriptionEnrollments.ToListAsync());
        await maxioClient.Received(1).CreateCustomerAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            user.Email,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await maxioClient.Received(1).CreateSubscriptionAsync(
            "pro",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptionsUsesMaxioAsSystemOfRecordAndFiltersFamily()
    {
        var maxioClient = Substitute.For<IMaxioClient>();
        maxioClient.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(42));
        maxioClient.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                Subscription(1, "configured-family"),
                Subscription(2, "other-family")
            });

        var contextOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new CatalogContext(contextOptions);
        var service = new SubscriptionBillingService(
            maxioClient,
            context,
            Options.Create(new MaxioOptions
            {
                ApiKey = "not-a-secret",
                Subdomain = "test",
                ProductFamilyHandle = "configured-family"
            }),
            TimeProvider.System,
            NullLogger<SubscriptionBillingService>.Instance);

        var result = await service.ListSubscriptionsAsync("user-id");

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
    }

    private static MaxioSubscription Subscription(long id, string family) => new(
        id,
        42,
        "pro",
        "Pro",
        family,
        "Default",
        29900,
        1,
        "month",
        "active",
        DateTimeOffset.Parse("2030-01-01T00:00:00Z"));
}
