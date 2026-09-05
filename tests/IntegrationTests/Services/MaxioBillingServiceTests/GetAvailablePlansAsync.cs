using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services.MaxioBillingServiceTests;

public class GetAvailablePlansAsync
{
    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioBillingService _sut;

    public GetAvailablePlansAsync()
    {
        _sut = new MaxioBillingService(_client, new MaxioBuyerLock(), Options.Create(new MaxioSettings
        {
            ProductFamilyHandle = "eshop-subscribe"
        }));
    }

    [Fact]
    public async Task ExcludesArchivedPlansAndConvertsCentsToDecimal()
    {
        _client.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
                new() { Handle = "old-plan", Name = "Old Plan", PriceInCents = 999, ArchivedAt = DateTimeOffset.UtcNow }
            });

        var plans = await _sut.GetAvailablePlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal(1, plan.IntervalCount);
        Assert.Equal("month", plan.IntervalUnit);
    }
}
