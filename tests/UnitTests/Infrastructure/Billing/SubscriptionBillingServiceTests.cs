using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class SubscriptionBillingServiceTests
{
    [Fact]
    public async Task RepeatedSubscribeCreatesOnlyOneMaxioSubscription()
    {
        await using var context = CreateContext();
        var gateway = Substitute.For<ISubscriptionBillingGateway>();
        var plan = new BillingPlan(1, "pro", "Pro", "Pro plan", 29900, 1, "month");
        var customer = new BillingCustomer(11, "customer-reference");
        var subscription = new BillingSubscription(22, 11, null, "active", "pro", "Pro", 29900,
            1, "month", DateTimeOffset.UtcNow.AddMonths(1), "family");

        gateway.GetPlansAsync(Arg.Any<CancellationToken>()).Returns(new[] { plan });
        gateway.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(customer);
        var listCalls = 0;
        string? createdReference = null;
        gateway.GetCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<BillingSubscription>>(listCalls++ == 0
                ? Array.Empty<BillingSubscription>()
                : new[] { subscription with { Reference = createdReference } }));
        gateway.CreateSubscriptionAsync(customer.Id, plan.Handle, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                createdReference = call.ArgAt<string>(2);
                return subscription with { Reference = createdReference };
            });

        var service = CreateService(context, gateway);
        var user = new BillingUser("user-id", "user@example.com", "User", "Customer");

        var first = await service.SubscribeAsync(user, plan.Handle);
        var second = await service.SubscribeAsync(user, plan.Handle);

        Assert.True(first.Created);
        Assert.False(second.Created);
        await gateway.Received(1).CreateSubscriptionAsync(customer.Id, plan.Handle, Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        Assert.Single(context.SubscriptionEnrollments);
    }

    [Fact]
    public async Task RejectsProductOutsideConfiguredFamily()
    {
        await using var context = CreateContext();
        var gateway = Substitute.For<ISubscriptionBillingGateway>();
        gateway.GetPlansAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<BillingPlan>());
        var service = CreateService(context, gateway);

        await Assert.ThrowsAsync<BillingPlanNotFoundException>(() => service.SubscribeAsync(
            new BillingUser("user-id", "user@example.com", "User", "Customer"), "unknown"));

        await gateway.DidNotReceive().CreateCustomerAsync(Arg.Any<BillingUser>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private static SubscriptionBillingService CreateService(CatalogContext context,
        ISubscriptionBillingGateway gateway)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test",
            Subdomain = "test",
            ProductFamilyHandle = "family"
        });
        return new SubscriptionBillingService(context, gateway, options, new SubscriptionOperationLock());
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CatalogContext(options);
    }
}
