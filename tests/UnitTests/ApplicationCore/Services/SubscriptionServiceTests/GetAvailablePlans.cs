using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class GetAvailablePlans
{
    private readonly IMaxioBillingGateway _gateway = Substitute.For<IMaxioBillingGateway>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService CreateService() => new(_gateway, _logger);

    [Fact]
    public async Task ReturnsActivePlansOrderedByPrice()
    {
        _gateway.ListPlansAsync(default).ReturnsForAnyArgs(new List<SubscriptionPlan>
        {
            new() { Handle = "pro", Name = "Pro", PriceInCents = 29900 },
            new() { Handle = "basic", Name = "Basic", PriceInCents = 2900 }
        });

        var plans = await CreateService().GetAvailablePlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic", plans[0].Handle);
        Assert.Equal("pro", plans[1].Handle);
    }

    [Fact]
    public async Task FiltersOutArchivedPlans()
    {
        _gateway.ListPlansAsync(default).ReturnsForAnyArgs(new List<SubscriptionPlan>
        {
            new() { Handle = "old", Name = "Old plan", PriceInCents = 100, IsArchived = true, ArchivedAt = DateTimeOffset.UtcNow },
            new() { Handle = "current", Name = "Current plan", PriceInCents = 200 }
        });

        var plans = await CreateService().GetAvailablePlansAsync();

        Assert.Single(plans);
        Assert.Equal("current", plans[0].Handle);
    }
}
