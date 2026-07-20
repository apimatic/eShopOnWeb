using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>UC1 happy path (find-or-create customer, subscribe, list/read) against the real sandbox.</summary>
[Collection(MaxioCollection.Name)]
public class CustomerAndSubscriptionTests
{
    private readonly MaxioFixture _fixture;

    public CustomerAndSubscriptionTests(MaxioFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FindCustomerByReferenceAsync_UnknownReference_ReturnsNull()
    {
        var unknownReference = $"xunit-unknown-{Guid.NewGuid():N}@example.com";

        var customer = await _fixture.BillingClient.FindCustomerByReferenceAsync(unknownReference);

        Assert.Null(customer);
    }

    [Fact]
    public async Task EnsureCustomerAsync_IsIdempotent_ReturnsSameCustomerIdOnSecondCall()
    {
        var reference = $"xunit-idempotent-{Guid.NewGuid():N}@example.com";

        var first = await _fixture.BillingClient.EnsureCustomerAsync(reference, reference, "XUnit", "Tester");
        var second = await _fixture.BillingClient.EnsureCustomerAsync(reference, reference, "XUnit", "Tester");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(reference, first.Reference);
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerAsync_CustomerWithNoSubscriptions_ReturnsEmpty()
    {
        var reference = $"xunit-no-subs-{Guid.NewGuid():N}@example.com";
        var customer = await _fixture.BillingClient.EnsureCustomerAsync(reference, reference, "XUnit", "Tester");

        var subscriptions = await _fixture.BillingClient.ListSubscriptionsForCustomerAsync(customer.Id);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task FindLiveSubscriptionAsync_CustomerWithNoSubscriptions_ReturnsNull()
    {
        var reference = $"xunit-no-live-sub-{Guid.NewGuid():N}@example.com";
        var customer = await _fixture.BillingClient.EnsureCustomerAsync(reference, reference, "XUnit", "Tester");

        var active = await _fixture.BillingClient.FindLiveSubscriptionAsync(customer.Id);

        Assert.Null(active);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ThenGetSubscriptionAsync_ReturnsMatchingLiveState()
    {
        var reference = $"xunit-subscribe-{Guid.NewGuid():N}@example.com";
        var customer = await _fixture.BillingClient.EnsureCustomerAsync(reference, reference, "XUnit", "Tester");

        var created = await _fixture.BillingClient.CreateSubscriptionAsync(customer.Id, "eshop-pro");

        Assert.True(created.Id > 0);
        Assert.Equal("eshop-pro", created.ProductHandle);
        Assert.Equal(customer.Id, created.CustomerId);
        Assert.True(created.IsLive);
        Assert.Equal(29900, created.PriceInCents);

        var reRead = await _fixture.BillingClient.GetSubscriptionAsync(created.Id);
        Assert.Equal(created.Id, reRead.Id);
        Assert.Equal("eshop-pro", reRead.ProductHandle);
        Assert.Equal(reference, reRead.CustomerReference);

        var listed = await _fixture.BillingClient.ListSubscriptionsForCustomerAsync(customer.Id);
        Assert.Contains(listed, s => s.Id == created.Id);

        var foundLive = await _fixture.BillingClient.FindLiveSubscriptionAsync(customer.Id);
        Assert.NotNull(foundLive);
        Assert.Equal(created.Id, foundLive!.Id);
    }

    [Fact]
    public async Task GetSubscriptionAsync_UnknownId_ThrowsBillingProviderException()
    {
        await Assert.ThrowsAsync<BillingProviderException>(
            () => _fixture.BillingClient.GetSubscriptionAsync(int.MaxValue));
    }
}
