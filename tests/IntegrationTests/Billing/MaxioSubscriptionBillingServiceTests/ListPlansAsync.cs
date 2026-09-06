using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing.MaxioSubscriptionBillingServiceTests;

public class ListPlansAsync
{
    [Fact]
    public async Task ReturnsPlansOfTheConfiguredFamilyOrderedByPrice()
    {
        var transport = new StubTransport(_ => StubTransport.Ok(MaxioTestHarness.ProductsJson(
            MaxioTestHarness.Product("pro-plan", "Pro Plan", 29900),
            MaxioTestHarness.Product("starter-plan", "Starter Plan", 2900))));

        var plans = await MaxioTestHarness.CreateService(transport).ListPlansAsync();

        Assert.Equal(new[] { "starter-plan", "pro-plan" }, plans.Select(plan => plan.Handle));
        Assert.Equal(29.00m, plans[0].Price);
        Assert.Equal(2900L, plans[0].PriceInCents);
        Assert.Equal("month", plans[0].IntervalUnit);
        Assert.Equal(1, plans[0].Interval);
        Assert.Equal(MaxioTestHarness.FamilyHandle, plans[0].ProductFamilyHandle);
    }

    [Fact]
    public async Task DropsArchivedPlans()
    {
        var transport = new StubTransport(_ => StubTransport.Ok(MaxioTestHarness.ProductsJson(
            MaxioTestHarness.Product("live-plan", "Live Plan", 2900),
            MaxioTestHarness.Product("retired-plan", "Retired Plan", 1900, archivedAt: "2026-01-01T00:00:00Z"))));

        var plans = await MaxioTestHarness.CreateService(transport).ListPlansAsync();

        Assert.Equal("live-plan", Assert.Single(plans).Handle);
    }

    [Fact]
    public async Task AsksTheProviderForTheFamilyByHandleAndExcludesArchivedRows()
    {
        var transport = new StubTransport(_ => StubTransport.Ok(MaxioTestHarness.ProductsJson(
            MaxioTestHarness.Product("live-plan", "Live Plan", 2900))));

        await MaxioTestHarness.CreateService(transport).ListPlansAsync();

        var request = Assert.Single(transport.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("/product_families/handle%3A" + MaxioTestHarness.FamilyHandle + "/products.json", request.Uri.AbsoluteUri);
        Assert.Contains("include_archived=false", request.Uri.Query);
        Assert.Contains("per_page=100", request.Uri.Query);
        Assert.Contains("page=1", request.Uri.Query);
    }

    [Fact]
    public async Task FallsBackToTheNumericFamilyIdWhenTheHandleFormIsRejected()
    {
        // A site that will not accept the percent-encoded "handle:" segment must still work, via
        // the documented id form - and must not be reported as an outage.
        var transport = new StubTransport(request =>
        {
            if (request.Uri.AbsoluteUri.Contains("handle%3A", StringComparison.Ordinal))
            {
                return StubTransport.Json(HttpStatusCode.NotFound, "\"Not Found\"");
            }

            if (request.Uri.AbsolutePath.EndsWith("/product_families.json", StringComparison.Ordinal))
            {
                return StubTransport.Ok($@"[{{""product_family"":{{ ""id"": 4242, ""handle"": ""{MaxioTestHarness.FamilyHandle}"" }}}}]");
            }

            return StubTransport.Ok(MaxioTestHarness.ProductsJson(
                MaxioTestHarness.Product("live-plan", "Live Plan", 2900)));
        });

        var plans = await MaxioTestHarness.CreateService(transport).ListPlansAsync();

        Assert.Equal("live-plan", Assert.Single(plans).Handle);
        Assert.Equal(1, transport.CountOf(HttpMethod.Get, "/product_families/4242/products.json"));
    }

    [Fact]
    public async Task ReportsMissingConfigurationAsServiceUnavailableWithoutCallingTheProvider()
    {
        var transport = new StubTransport(_ => StubTransport.Ok("[]"));
        var settings = new MaxioSettings { Subdomain = "test-site", ProductFamilyHandle = "test-family" };

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => MaxioTestHarness.CreateService(transport, settings).ListPlansAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Contains("Maxio:ApiKey", string.Join(" ", exception.Details));
        Assert.Empty(transport.Requests);
        Assert.DoesNotContain("test-api-key", exception.Message);
    }

    [Fact]
    public async Task ReportsAnUnauthorizedProviderAsAServerSideFailureNotACallerError()
    {
        // A rejected API key is our misconfiguration, not something the caller can act on.
        var transport = new StubTransport(_ => StubTransport.Json(HttpStatusCode.Unauthorized, "{}"));

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => MaxioTestHarness.CreateService(transport).ListPlansAsync());

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, exception.ProviderStatusCode);
    }

    [Fact]
    public async Task ReportsAnUnreadableSuccessBodyAsABadGatewayRatherThanLettingItEscape()
    {
        var transport = new StubTransport(_ => StubTransport.Ok("{ this is not json"));

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => MaxioTestHarness.CreateService(transport).ListPlansAsync());

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.DoesNotContain("System.Text.Json", exception.Message, StringComparison.Ordinal);
    }
}
