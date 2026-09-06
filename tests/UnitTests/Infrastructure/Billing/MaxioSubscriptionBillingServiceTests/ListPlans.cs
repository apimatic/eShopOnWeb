using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.MaxioSubscriptionBillingServiceTests;

public class ListPlans
{
    [Fact]
    public async Task ResolvesTheProductFamilyByHandleAndProjectsItsProducts()
    {
        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router());

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);

        var pro = Assert.Single(plans, plan => plan.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal("USD", pro.Currency);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentProfileAtSignup);

        // Numeric ids are reassigned when the catalog is re-seeded, so the family must be reached by handle.
        Assert.Contains(handler.Requests, request => request.Path == "/product_families.json");
        Assert.Contains(handler.Requests, request => request.Path == "/product_families/3026729/products.json");
    }

    [Fact]
    public async Task OrdersPlansByPriceAndAsksForOnePageOnly()
    {
        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router());

        var plans = await service.ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(plan => plan.Handle).ToArray());

        // The family returned fewer products than a page holds, so there is nothing to page for.
        Assert.Equal(1, handler.CountOf(HttpMethod.Get, "/product_families/3026729/products.json"));
    }

    [Fact]
    public async Task ReportsMisconfigurationWhenTheProductFamilyHandleIsUnknown()
    {
        var settings = MaxioTestHost.DefaultSettings();
        settings.ProductFamilyHandle = "does-not-exist";

        var (service, _) = MaxioTestHost.Create(MaxioTestHost.Router(), settings);

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.ListPlansAsync());

        Assert.Equal(BillingFailureKind.NotConfigured, exception.Kind);
    }

    [Fact]
    public async Task ReportsMisconfigurationWhenCredentialsAreMissing()
    {
        var settings = MaxioTestHost.DefaultSettings();
        settings.ApiKey = null;

        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router(), settings);

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.ListPlansAsync());

        Assert.Equal(BillingFailureKind.NotConfigured, exception.Kind);
        Assert.Contains("Maxio:ApiKey", exception.Details);
        // It must fail before reaching the provider, not after an unauthenticated round trip.
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task StillListsPlansWhenTheSiteCurrencyCannotBeRead()
    {
        var (service, _) = MaxioTestHost.Create(request =>
            request.RequestUri!.AbsolutePath == "/site.json"
                ? MaxioStubHandler.Json(HttpStatusCode.InternalServerError, "{}")
                : MaxioTestHost.Router()(request));

        var plans = await service.ListPlansAsync();

        // Currency is display metadata; losing it must not take the catalog down with it.
        Assert.Equal(2, plans.Count);
        Assert.All(plans, plan => Assert.Null(plan.Currency));
    }
}
