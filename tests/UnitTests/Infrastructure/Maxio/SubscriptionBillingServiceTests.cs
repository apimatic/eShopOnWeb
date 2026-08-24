using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class SubscriptionBillingServiceTests
{
    [Fact]
    public async Task ConcurrentRepeatedSubscribeCreatesOnlyOneRemoteSubscription()
    {
        await using var context = NewContext();
        var gateway = Substitute.For<IMaxioBillingGateway>();
        var plan = new SubscriptionPlan("pro", "Pro", null, 29900, 1, "month");
        var customer = new MaxioCustomer(42, "customer-reference");
        var subscription = new SubscriptionDetails(99, "pro", "Pro", 29900, "active", DateTimeOffset.UtcNow.AddMonths(1), "USD");

        gateway.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubscriptionPlan>>([plan]));
        gateway.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MaxioCustomer?>(null));
        gateway.CreateCustomerAsync(Arg.Any<BillingUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(customer));
        gateway.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<SubscriptionDetails?>(null),
                Task.FromResult<SubscriptionDetails?>(subscription));
        gateway.CreateSubscriptionAsync("pro", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(subscription));

        var service = new SubscriptionBillingService(context, gateway, new AsyncKeyedLocker());
        var user = new BillingUser("user-1", "shopper@example.com", "Shopper", "Customer");

        var results = await Task.WhenAll(
            service.SubscribeAsync(user, "pro", CancellationToken.None),
            service.SubscribeAsync(user, "pro", CancellationToken.None));

        Assert.All(results, result => Assert.Equal(99, result.Id));
        await gateway.Received(1).CreateCustomerAsync(user, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await gateway.Received(1).CreateSubscriptionAsync("pro", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Single(await context.SubscriptionEnrollments.ToListAsync());
    }

    [Fact]
    public async Task MissingCustomerReturnsNoSubscriptions()
    {
        await using var context = NewContext();
        var gateway = Substitute.For<IMaxioBillingGateway>();
        gateway.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MaxioCustomer?>(null));
        var service = new SubscriptionBillingService(context, gateway, new AsyncKeyedLocker());

        var result = await service.ListSubscriptionsAsync(
            new BillingUser("user-1", "shopper@example.com", "Shopper", "Customer"),
            CancellationToken.None);

        Assert.Empty(result);
        await gateway.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeterministicProviderRejectionReleasesTheEnrollmentClaim()
    {
        await using var context = NewContext();
        var gateway = Substitute.For<IMaxioBillingGateway>();
        gateway.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SubscriptionPlan>>(
                [new SubscriptionPlan("pro", "Pro", null, 29900, 1, "month")]));
        gateway.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MaxioCustomer?>(new MaxioCustomer(42, "customer-reference")));
        gateway.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SubscriptionDetails?>(null));
        gateway.CreateSubscriptionAsync("pro", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<SubscriptionDetails>>(_ => throw new SubscriptionBillingException(
                System.Net.HttpStatusCode.UnprocessableEntity,
                "Rejected."));
        var service = new SubscriptionBillingService(context, gateway, new AsyncKeyedLocker());

        await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.SubscribeAsync(
            new BillingUser("user-1", "shopper@example.com", "Shopper", "Customer"),
            "pro",
            CancellationToken.None));

        Assert.Empty(await context.SubscriptionEnrollments.ToListAsync());
    }

    private static CatalogContext NewContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CatalogContext(options);
    }
}
