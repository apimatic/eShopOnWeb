using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi;

public class SubscriptionServiceTests
{
    [Fact]
    public async Task ConcurrentSubscribeCreatesOneAtomicCustomerSignupAndOneSubscription()
    {
        var maxio = Substitute.For<IMaxioClient>();
        var plan = Product("pro", "family");
        maxio.ListProductsAsync("family", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MaxioProduct>>(new[] { plan }));
        maxio.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MaxioCustomer?>(null));
        maxio.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MaxioSubscription?>(null));

        maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(50);
                var request = call.Arg<MaxioCreateSubscription>();
                return new MaxioSubscription
                {
                    Id = 42,
                    State = "active",
                    Reference = request.Reference,
                    ProductPriceInCents = plan.PriceInCents,
                    Customer = new MaxioCustomer
                    {
                        Id = 10,
                        Reference = request.CustomerAttributes!.Reference
                    },
                    Product = plan
                };
            });
        var service = CreateService(maxio);
        var user = new SubscriptionUser("user-id", "shopper@example.com", "shopper@example.com");

        var results = await Task.WhenAll(
            service.SubscribeAsync(user, "pro", default),
            service.SubscribeAsync(user, "pro", default));

        Assert.All(results, result => Assert.Equal(42, result!.Subscription.Id));
        Assert.Single(results.Where(result => result!.Created));
        await maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(request =>
                request.CustomerId == null && request.CustomerAttributes != null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MySubscriptionsOnlyReturnsConfiguredProductFamily()
    {
        var maxio = Substitute.For<IMaxioClient>();
        maxio.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 10 });
        maxio.ListCustomerSubscriptionsAsync(10, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MaxioSubscription>>(new[]
            {
                Subscription(42, Product("pro", "family")),
                Subscription(43, Product("other", "another-family"))
            }));
        var service = CreateService(maxio);

        var results = await service.GetSubscriptionsAsync(
            new SubscriptionUser("user-id", "shopper@example.com", "shopper@example.com"),
            default);

        Assert.Equal(42, Assert.Single(results).Id);
    }

    private static SubscriptionService CreateService(IMaxioClient maxio) => new(
        maxio,
        Options.Create(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = "family"
        }),
        new AsyncKeyedLock(),
        new MemoryCache(new MemoryCacheOptions()));

    private static MaxioProduct Product(string handle, string family) => new()
    {
        Id = 7,
        Handle = handle,
        Name = handle,
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamily = new MaxioProductFamily { Handle = family }
    };

    private static MaxioSubscription Subscription(int id, MaxioProduct product) => new()
    {
        Id = id,
        State = "active",
        ProductPriceInCents = product.PriceInCents,
        Product = product
    };
}
