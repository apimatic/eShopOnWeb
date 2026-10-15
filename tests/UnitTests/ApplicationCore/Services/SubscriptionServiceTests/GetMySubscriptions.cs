using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class GetMySubscriptions
{
    private readonly SubscriptionServiceTestContext _ctx = new();

    [Fact]
    public async Task NoCustomerAtProvider_ReturnsEmpty()
    {
        _ctx.Gateway.FindCustomerIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((int?)null);

        var mine = await _ctx.CreateService().GetMySubscriptionsAsync(SubscriptionServiceTestContext.Shopper);

        Assert.Empty(mine.Subscriptions);
        Assert.Null(mine.Pending);
    }

    [Fact]
    public async Task ReadsFromProvider_FilteredToConfiguredFamily()
    {
        _ctx.Gateway.FindCustomerIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(77);
        var other = SubscriptionServiceTestContext.Subscription(2, "x") with { ProductFamilyHandle = "other-family" };
        _ctx.Gateway.ListCustomerSubscriptionsAsync(77, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionServiceTestContext.Subscription(1, "r"), other });

        var mine = await _ctx.CreateService().GetMySubscriptionsAsync(SubscriptionServiceTestContext.Shopper);

        Assert.Equal(new[] { 1 }, mine.Subscriptions.Select(s => s.Id));
    }

    [Fact]
    public async Task UnsettledPendingEnrollment_IsReportedAsPending()
    {
        var pending = new SubscriptionEnrollment(SubscriptionService.CustomerReferenceFor("demouser@microsoft.com"),
            SubscriptionServiceTestContext.Family, "demouser@microsoft.com", "eshop-pro", "ref-p", _ctx.Clock.GetUtcNow());
        _ctx.Store.Rows[pending.ClaimKey] = pending;
        _ctx.Gateway.FindSubscriptionAsync("ref-p", Arg.Any<CancellationToken>()).Returns((BillingSubscription?)null);
        _ctx.Gateway.FindCustomerIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((int?)null);

        var mine = await _ctx.CreateService().GetMySubscriptionsAsync(SubscriptionServiceTestContext.Shopper);

        Assert.NotNull(mine.Pending);
        Assert.Equal("ref-p", mine.Pending!.SubscriptionReference);
    }
}
