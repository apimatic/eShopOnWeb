using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.SubscriptionBilling;

public class MaxioSubscriptionBillingServiceTests
{
    [Fact]
    public async Task ConcurrentEnrollmentCreatesOnlyOneMaxioSubscription()
    {
        await using var dbContext = CreateDbContext();
        var identityUser = new ApplicationUser
        {
            Id = "user-1",
            UserName = "shopper@example.com",
            Email = "shopper@example.com"
        };
        dbContext.Users.Add(identityUser);
        await dbContext.SaveChangesAsync();

        var maxio = Substitute.For<IMaxioBillingClient>();
        var family = new MaxioProductFamily(10, "Plans", "family-handle");
        var product = new MaxioProduct(20, "Pro", "pro-plan", null, 29900, 1, "month", false, null, family);
        MaxioCustomer? customer = null;
        MaxioSubscription? subscription = null;
        var createCount = 0;

        maxio.GetSiteAsync(Arg.Any<CancellationToken>()).Returns(new MaxioSite("USD", true));
        maxio.GetProductsAsync("family-handle", Arg.Any<CancellationToken>())
            .Returns(new[] { product });
        maxio.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => customer);
        maxio.CreateCustomerAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                customer = new MaxioCustomer(30, "shopper@example.com", call.ArgAt<string>(3));
                return customer;
            });
        maxio.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => subscription);
        maxio.CreateSubscriptionAsync(
                "pro-plan",
                30,
                Arg.Any<string>(),
                Arg.Any<string>(),
                "remittance",
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                Interlocked.Increment(ref createCount);
                await Task.Delay(50);
                subscription = new MaxioSubscription(
                    40,
                    "active",
                    29900,
                    DateTimeOffset.UtcNow.AddMonths(1),
                    DateTimeOffset.UtcNow.AddMonths(1),
                    customer!,
                    product);
                return subscription;
            });

        var service = new MaxioSubscriptionBillingService(
            maxio,
            dbContext,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new MaxioOptions { ProductFamilyHandle = "family-handle" }),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
        var user = new SubscriptionUser(identityUser.Id, identityUser.Email!, identityUser.UserName!);

        var enrollments = await Task.WhenAll(
            service.SubscribeAsync(user, "pro-plan"),
            service.SubscribeAsync(user, "pro-plan"));

        Assert.Equal(1, createCount);
        Assert.Single(enrollments.Where(result => result.Created));
        Assert.Single(enrollments.Where(result => !result.Created));
        Assert.Single(await dbContext.MaxioCustomerMappings.ToListAsync());
        var mapping = Assert.Single(await dbContext.MaxioSubscriptionMappings.ToListAsync());
        Assert.Equal(SubscriptionCreationStatus.Completed, mapping.CreationStatus);
        Assert.Equal(40, mapping.MaxioSubscriptionId);
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppIdentityDbContext(options);
    }
}
