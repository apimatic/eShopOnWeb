using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Subscriptions;

public class SubscriptionServiceTests
{
    [Fact]
    public async Task ConcurrentSubscribeCreatesOneMaxioSubscription()
    {
        var maxio = Substitute.For<IMaxioClient>();
        var repository = Substitute.For<IRepository<UserSubscription>>();
        var service = CreateService(maxio, repository);
        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "demo@example.com",
            Email = "demo@example.com"
        };
        var customer = Customer();
        var subscription = Subscription(customer);
        maxio.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new[] { Plan() });
        maxio.FindCustomerAsync(user.Id, Arg.Any<CancellationToken>()).Returns(customer);
        maxio.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null, subscription);
        maxio.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionDraft>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(subscription);

        var results = await Task.WhenAll(
            service.SubscribeAsync(user, "test-plan", CancellationToken.None),
            service.SubscribeAsync(user, "test-plan", CancellationToken.None));

        Assert.All(results, result => Assert.Equal(9001, result.Id));
        await maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioSubscriptionDraft>(draft => draft.CustomerId == customer.Id &&
                draft.ProductHandle == "test-plan"),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListPlansOnlyReturnsConfiguredActiveFamily()
    {
        var maxio = Substitute.For<IMaxioClient>();
        var service = CreateService(maxio, Substitute.For<IRepository<UserSubscription>>());
        var archived = Plan() with { Handle = "archived", ArchivedAt = DateTimeOffset.UtcNow };
        var otherFamily = Plan() with
        {
            Handle = "other",
            ProductFamily = new MaxioProductFamily(2, "Other", "other-family")
        };
        maxio.ListProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { otherFamily, archived, Plan() });

        var plans = await service.ListPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal("test-plan", plan.Handle);
        Assert.Equal(299m, plan.Price);
    }

    private static SubscriptionService CreateService(IMaxioClient maxio,
        IRepository<UserSubscription> repository) => new(maxio, repository,
        new SubscriptionOperationLock(), Options.Create(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = "test-family"
        }));

    private static MaxioProduct Plan() => new(7126957, "Pro Plan", "test-plan", "Pro", 29900,
        1, "month", null, new MaxioProductFamily(3023074, "Test Family", "test-family"));

    private static MaxioCustomer Customer() =>
        new(42, "Demo", "Customer", "demo@example.com", "user-1");

    private static MaxioSubscription Subscription(MaxioCustomer customer) =>
        new(9001, "active", "eshop-reference", 29900, DateTimeOffset.UtcNow.AddMonths(1),
            customer, Plan());
}
