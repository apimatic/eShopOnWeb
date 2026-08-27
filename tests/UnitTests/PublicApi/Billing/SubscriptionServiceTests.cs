using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Billing;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Billing;

public class SubscriptionServiceTests
{
    [Fact]
    public async Task RepeatedSubscribeCreatesOnlyOnceAndReturnsExistingSubscription()
    {
        var gateway = Substitute.For<IMaxioBillingGateway>();
        var enrollments = Substitute.For<ISubscriptionEnrollmentStore>();
        var user = new ApplicationUser { Id = "user-42", UserName = "shopper@example.test", Email = "shopper@example.test" };
        var userManager = CreateUserManager(user);
        var product = new BillingProduct("eshop-pro", "Pro", 29900, 1, "month", false);
        var subscription = new BillingSubscription(123, "reference", "eshop-pro", "Pro", 29900, "USD", "active", DateTimeOffset.UtcNow.AddMonths(1), null);
        var enrollment = new SubscriptionEnrollment
        {
            Id = 1,
            UserId = user.Id,
            ProductHandle = product.Handle,
            CustomerReference = "customer-reference",
            SubscriptionReference = "subscription-reference",
            Status = "Pending"
        };

        gateway.FindProductAsync(product.Handle, Arg.Any<CancellationToken>()).Returns(product);
        gateway.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null, subscription);
        gateway.EnsureCustomerAsync(Arg.Any<BillingCustomerProfile>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(88, "customer-reference"));
        gateway.CreateSubscriptionAsync(product.Handle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(subscription);
        enrollments.GetOrCreateAsync(user.Id, product.Handle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(enrollment);
        enrollments.TryAcquireLeaseAsync(enrollment.Id, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var service = new SubscriptionService(gateway, enrollments, userManager);

        var first = await service.SubscribeAsync(user.UserName!, product.Handle, CancellationToken.None);
        var second = await service.SubscribeAsync(user.UserName!, product.Handle, CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        await gateway.Received(1).CreateSubscriptionAsync(product.Handle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await gateway.Received(1).EnsureCustomerAsync(Arg.Any<BillingCustomerProfile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsPaymentRequiredProductBeforeCustomerCreation()
    {
        var gateway = Substitute.For<IMaxioBillingGateway>();
        var enrollments = Substitute.For<ISubscriptionEnrollmentStore>();
        var user = new ApplicationUser { Id = "user-42", UserName = "shopper@example.test", Email = "shopper@example.test" };
        gateway.FindProductAsync("card-plan", Arg.Any<CancellationToken>())
            .Returns(new BillingProduct("card-plan", "Card plan", 1000, 1, "month", true));
        var service = new SubscriptionService(gateway, enrollments, CreateUserManager(user));

        var exception = await Assert.ThrowsAsync<BillingException>(() =>
            service.SubscribeAsync(user.UserName!, "card-plan", CancellationToken.None));

        Assert.Equal("payment_method_required", exception.Code);
        await gateway.DidNotReceive().EnsureCustomerAsync(Arg.Any<BillingCustomerProfile>(), Arg.Any<CancellationToken>());
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationUser user)
    {
        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var manager = Substitute.For<UserManager<ApplicationUser>>(
            store, null, null, null, null, null, null, null, null);
        manager.FindByNameAsync(user.UserName!).Returns(user);
        return manager;
    }
}
