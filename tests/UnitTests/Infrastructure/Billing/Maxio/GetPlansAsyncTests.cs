using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class GetPlansAsyncTests
{
    [Fact]
    public async Task ProjectsPlansAndDropsArchivedOnes()
    {
        using var host = MaxioTestHost.Create(new MaxioStubHandler()
            .Route(HttpMethod.Get, "products.json", HttpStatusCode.OK, MaxioPayloads.TwoProductsOneArchived));

        var plans = (await host.Service.GetPlansAsync()).ToList();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle));

        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal("Everything, monthly", pro.Description);
        // The provider reports cents; shoppers are quoted currency.
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal("1 month", pro.BillingPeriod);
        Assert.False(pro.RequiresPaymentMethod);
        Assert.Equal("eshop-subscribe", pro.ProductFamilyHandle);

        Assert.True(plans.Single(p => p.Handle == "basic-plan").RequiresPaymentMethod);
    }

    [Fact]
    public async Task AsksForTheFamilyByHandleSoNumericIdsAreNeverConfigured()
    {
        using var host = MaxioTestHost.Create(new MaxioStubHandler()
            .Route(HttpMethod.Get, "products.json", HttpStatusCode.OK, MaxioPayloads.NoProducts));

        await host.Service.GetPlansAsync();

        var uri = host.Handler.LastRequestUri!;
        Assert.Contains("eshop-subscribe", Uri.UnescapeDataString(uri.AbsolutePath));
        Assert.Contains("include_archived=false", uri.Query);
    }

    [Fact]
    public async Task DerivesTheBaseAddressFromTheConfiguredSubdomain()
    {
        using var host = MaxioTestHost.Create(new MaxioStubHandler()
            .Route(HttpMethod.Get, "products.json", HttpStatusCode.OK, MaxioPayloads.NoProducts));

        await host.Service.GetPlansAsync();

        Assert.Equal("https://test-site.chargify.com", host.Handler.LastRequestUri!.GetLeftPart(UriPartial.Authority));
    }

    [Fact]
    public async Task UsesAConfiguredBaseUrlVerbatimInsteadOfTheSubdomain()
    {
        // The override exists so the same build can be pointed at a mock server or a proxy; it must not be
        // recomposed from the subdomain.
        using var host = MaxioTestHost.Create(
            new MaxioStubHandler().Route(HttpMethod.Get, "products.json", HttpStatusCode.OK, MaxioPayloads.NoProducts),
            new Dictionary<string, string?> { ["Maxio:BaseUrl"] = "https://billing.internal.example.com/maxio" });

        await host.Service.GetPlansAsync();

        var uri = host.Handler.LastRequestUri!;
        Assert.Equal("https://billing.internal.example.com", uri.GetLeftPart(UriPartial.Authority));
        Assert.StartsWith("/maxio/", uri.AbsolutePath);
    }

    [Fact]
    public async Task ReportsMissingConfigurationAsOurOwnProblem()
    {
        using var host = MaxioTestHost.Create(
            new MaxioStubHandler(),
            new Dictionary<string, string?> { ["Maxio:ApiKey"] = null });

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => host.Service.GetPlansAsync());

        Assert.Equal(BillingFailure.Configuration, exception.Failure);
        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task DoesNotReportAProviderCredentialFailureAsTheCallersFault()
    {
        using var host = MaxioTestHost.Create(new MaxioStubHandler()
            .Route(HttpMethod.Get, "products.json", HttpStatusCode.Unauthorized, MaxioPayloads.UnauthorizedError));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => host.Service.GetPlansAsync());

        // A 401 from Maxio means our API key is wrong, which is a deployment fault - never the caller's.
        Assert.Equal(BillingFailure.Configuration, exception.Failure);
        Assert.Equal(401, exception.ProviderStatusCode);
        Assert.DoesNotContain("Unauthorized", exception.ToCallerMessage());
    }
}
