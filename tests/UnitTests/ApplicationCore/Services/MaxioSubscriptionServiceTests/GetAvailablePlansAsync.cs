using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.MaxioSubscriptionServiceTests;

public class GetAvailablePlansAsync
{
    private readonly MaxioOptions _options = new() { ProductFamilyHandle = "eshop-subscribe" };
    private readonly IMaxioClient _client = Substitute.For<IMaxioClient>();

    [Fact]
    public async Task ExcludesArchivedProducts()
    {
        var live = new MaxioProduct(1, "eshop-pro", "Pro Plan", null, 29900, 1, "month", null);
        var archived = new MaxioProduct(2, "old-plan", "Old Plan", null, 1000, 1, "month", DateTimeOffset.UtcNow);
        _client.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { live, archived });

        var service = new MaxioSubscriptionService(_client, _options);
        var plans = await service.GetAvailablePlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
    }
}
