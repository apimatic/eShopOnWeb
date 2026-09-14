using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    [Fact]
    public async Task RepeatedEnrollmentCreatesOnlyOneMaxioSubscriptionAndOneMapping()
    {
        var maxio = Substitute.For<IMaxioClient>();
        var product = Product();
        var customer = new MaxioCustomer { Id = 42 };
        var subscription = Subscription(product, customer);
        maxio.ListProductsForFamilyAsync("family", Arg.Any<CancellationToken>())
            .Returns(new[] { product });
        maxio.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(customer);
        maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null, subscription);
        maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(subscription);

        await using var context = NewContext();
        var service = new MaxioSubscriptionBillingService(
            maxio,
            context,
            Options.Create(new MaxioOptions
            {
                ApiKey = "not-a-secret",
                Subdomain = "test",
                ProductFamilyHandle = "family"
            }),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
        var shopper = new SubscriptionShopper("user-id", "shopper@example.com");

        var first = await service.SubscribeAsync(shopper, "eshop-pro");
        var second = await service.SubscribeAsync(shopper, "eshop-pro");

        Assert.True(first.Created);
        Assert.False(second.Created);
        await maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(x =>
                x.ProductHandle == "eshop-pro" &&
                x.CustomerId == 42 &&
                x.PaymentCollectionMethod == "remittance" &&
                !string.IsNullOrWhiteSpace(x.Reference)),
            Arg.Any<CancellationToken>());
        Assert.Equal(1, await context.UserSubscriptions.CountAsync());
    }

    [Fact]
    public async Task MySubscriptionsOnlyReturnsConfiguredProductFamily()
    {
        var maxio = Substitute.For<IMaxioClient>();
        var customer = new MaxioCustomer { Id = 42 };
        maxio.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(customer);
        maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                Subscription(Product("family"), customer),
                Subscription(Product("another-family"), customer, 1002)
            });

        await using var context = NewContext();
        var service = new MaxioSubscriptionBillingService(
            maxio,
            context,
            Options.Create(new MaxioOptions
            {
                ApiKey = "not-a-secret",
                Subdomain = "test",
                ProductFamilyHandle = "family"
            }),
            NullLogger<MaxioSubscriptionBillingService>.Instance);

        var result = await service.ListSubscriptionsAsync(
            new SubscriptionShopper("user-id", "shopper@example.com"));

        Assert.Single(result);
        Assert.Equal(1001, result[0].MaxioSubscriptionId);
    }

    private static CatalogContext NewContext() => new(
        new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static MaxioProduct Product(string familyHandle = "family") => new()
    {
        Id = 1,
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamily = new MaxioProductFamily { Handle = familyHandle }
    };

    private static MaxioSubscription Subscription(
        MaxioProduct product,
        MaxioCustomer customer,
        long id = 1001) => new()
    {
        Id = id,
        State = "active",
        ProductPriceInCents = 29900,
        Currency = "USD",
        CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
        Product = product,
        Customer = customer
    };
}
