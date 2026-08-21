using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class SubscriptionBillingServiceTests
{
    [Fact]
    public async Task ParallelIdenticalRequestsCreateOneCustomerAndOneSubscription()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppIdentityDbContext>(options =>
            options.UseInMemoryDatabase($"identity-{Guid.NewGuid()}"));
        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<AppIdentityDbContext>();
        await using var provider = services.BuildServiceProvider();
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = "subscriber@example.com",
            Email = "subscriber@example.com",
            FirstName = "Sub",
            LastName = "Scriber"
        };
        Assert.True((await userManager.CreateAsync(user)).Succeeded);

        var catalogOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"catalog-{Guid.NewGuid()}")
            .Options;
        await using var catalogContext = new CatalogContext(catalogOptions);
        var gateway = Substitute.For<IMaxioBillingGateway>();
        var plan = new SubscriptionPlan("portable-plan", "default", "Portable", 2900, 1, "month");
        gateway.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { plan });
        gateway.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        gateway.CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(call => new BillingCustomer(
                123,
                call.Arg<CreateBillingCustomer>().Reference,
                "Sub",
                "Scriber",
                "subscriber@example.com"));
        gateway.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SubscriptionDetails?)null);
        gateway.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<CreateBillingSubscription>();
                return new SubscriptionDetails(
                    456,
                    request.ProductHandle,
                    "Portable",
                    plan.PricePointHandle,
                    2900,
                    "USD",
                    "active",
                    DateTimeOffset.UtcNow.AddMonths(1),
                    123,
                    request.CustomerReference,
                    request.Reference);
            });
        gateway.ReadSubscriptionAsync(456, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var create = gateway.ReceivedCalls()
                    .Single(received => received.GetMethodInfo().Name == nameof(IMaxioBillingGateway.CreateSubscriptionAsync))
                    .GetArguments()[0] as CreateBillingSubscription;
                return new SubscriptionDetails(
                    456,
                    "portable-plan",
                    "Portable",
                    "default",
                    2900,
                    "USD",
                    "active",
                    DateTimeOffset.UtcNow.AddMonths(1),
                    123,
                    create!.CustomerReference,
                    create.Reference);
            });
        var service = new SubscriptionBillingService(
            gateway,
            new EfSubscriptionLinkStore(catalogContext),
            userManager,
            new AsyncKeyedLocker(),
            TimeProvider.System);

        var results = await Task.WhenAll(
            service.SubscribeAsync(user.UserName!, "portable-plan", null, default),
            service.SubscribeAsync(user.UserName!, "portable-plan", null, default));

        Assert.Single(results.Where(result => result.Created));
        Assert.All(results, result => Assert.Equal(456, result.Subscription.Id));
        await gateway.Received(1).CreateCustomerAsync(
            Arg.Any<CreateBillingCustomer>(),
            Arg.Any<CancellationToken>());
        await gateway.Received(1).CreateSubscriptionAsync(
            Arg.Any<CreateBillingSubscription>(),
            Arg.Any<CancellationToken>());
        var createRequest = gateway.ReceivedCalls()
            .Single(received => received.GetMethodInfo().Name == nameof(IMaxioBillingGateway.CreateSubscriptionAsync))
            .GetArguments()[0] as CreateBillingSubscription;
        Assert.NotNull(createRequest);
        Assert.Null(createRequest.PricePointHandle);
        Assert.Single(await catalogContext.MaxioSubscriptionLinks.ToListAsync());
    }
}
