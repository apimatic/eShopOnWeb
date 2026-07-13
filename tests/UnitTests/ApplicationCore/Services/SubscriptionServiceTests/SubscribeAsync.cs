using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscribeAsync
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPlanChangePreviewCache _previewCache = Substitute.For<IPlanChangePreviewCache>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService CreateSut() => new(_billingClient, _previewCache, _publisher, _logger);

    [Fact]
    public async Task ReturnsExistingActiveSubscription_AndNeverCreatesASecondEnrollment()
    {
        var customer = new BillingCustomer(1, "buyer@example.com", "buyer@example.com");
        var existing = new BillingSubscription(42, 1, "buyer@example.com", 7111477, "eshop-pro", "Pro Plan", "active", 29900, null, null, null);

        _billingClient.FindCustomerByReferenceAsync("buyer@example.com").Returns(customer);
        _billingClient.ListCustomerSubscriptionsAsync(1).Returns(new[] { existing });

        var sut = CreateSut();
        var result = await sut.SubscribeAsync("buyer@example.com", "buyer@example.com", "Jane", "Buyer", "eshop-pro");

        Assert.Equal(42, result.SubscriptionId);
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task CreatesCustomerAndSubscription_WhenNoCustomerExistsYet()
    {
        var customer = new BillingCustomer(2, "newuser@example.com", "newuser@example.com");
        var created = new BillingSubscription(99, 2, "newuser@example.com", 7111477, "eshop-pro", "Pro Plan", "active", 29900, null, null, null);

        _billingClient.FindCustomerByReferenceAsync("newuser@example.com").Returns((BillingCustomer?)null);
        _billingClient.CreateCustomerAsync("newuser@example.com", "newuser@example.com", "New", "User").Returns(customer);
        _billingClient.ListCustomerSubscriptionsAsync(2).Returns(System.Array.Empty<BillingSubscription>());
        _billingClient.CreateSubscriptionAsync(2, "eshop-pro").Returns(created);

        var sut = CreateSut();
        var result = await sut.SubscribeAsync("newuser@example.com", "newuser@example.com", "New", "User", "eshop-pro");

        Assert.Equal(99, result.SubscriptionId);
        await _publisher.Received(1).Publish(
            Arg.Is<object>(n => n.GetType() == typeof(Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.SubscriptionActivated)
                && ((Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.SubscriptionActivated)n).SubscriptionId == 99),
            Arg.Any<System.Threading.CancellationToken>());
    }
}
