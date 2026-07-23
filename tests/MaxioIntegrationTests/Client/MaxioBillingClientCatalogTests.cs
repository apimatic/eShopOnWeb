using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Client;

/// <summary>
/// Reading the plan catalogue and the metered component — including money magnitude, handle-to-id
/// resolution, and what happens when the seed is missing or wrong.
/// </summary>
public class MaxioBillingClientCatalogTests
{
    [Fact]
    public async Task ListsThePlansInTheConfiguredFamilyWithPricesInMajorUnits()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog();

        var plans = await harness.Client.ListPlansAsync();

        Assert.Collection(plans,
            pro =>
            {
                Assert.Equal("eshop-pro", pro.Handle);
                Assert.Equal(MaxioPayloads.ProProductId, pro.Id);
                Assert.Equal(29_900, pro.PriceInCents);

                // $299.00, not $29,900 and not $2.99.
                Assert.Equal(299.00m, pro.Price);
                Assert.Equal(1, pro.Interval);
                Assert.Equal("month", pro.IntervalUnit);
                Assert.False(pro.RequiresPaymentMethod);
                Assert.Equal("eshop-subscribe", pro.ProductFamilyHandle);
            },
            basic =>
            {
                Assert.Equal("basic-plan", basic.Handle);
                Assert.Equal(2_900, basic.PriceInCents);
                Assert.Equal(29.00m, basic.Price);
            });
    }

    [Fact]
    public async Task ExcludesArchivedAndHandlelessProductsFromTheCustomerFacingPlanList()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog();

        var plans = await harness.Client.ListPlansAsync();

        Assert.DoesNotContain(plans, p => p.Handle == "retired-plan");
        Assert.Equal(2, plans.Count);
    }

    [Fact]
    public async Task ResolvesTheFamilyIdFromItsHandleRatherThanAConfiguredId()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog();

        await harness.Client.ListPlansAsync();

        Assert.Single(harness.Handler.RequestsFor(HttpMethod.Get, "/product_families.json"));
        Assert.Single(harness.Handler.RequestsFor(HttpMethod.Get, $"/product_families/{MaxioPayloads.FamilyId}/products.json"));
    }

    [Fact]
    public async Task TargetsTheExplicitlyConfiguredBaseUrlAndAuthenticatesWithTheApiKey()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog();

        await harness.Client.ListPlansAsync();

        var request = harness.Handler.Requests[0];
        Assert.Equal("/product_families.json", request.Path);

        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("test-api-key:x"));
        Assert.Equal(expected, request.Authorization);
    }

    [Fact]
    public async Task ResolvesTheCatalogueOnceAndServesLaterCallsFromTheCache()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog();

        await harness.Client.ListPlansAsync();
        await harness.Client.ListPlansAsync();
        await harness.Client.FindPlanAsync("eshop-pro");

        Assert.Single(harness.Handler.RequestsFor(HttpMethod.Get, "/product_families.json"));
    }

    [Fact]
    public async Task FindsAPlanByItsDurableHandle()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog();

        var plan = await harness.Client.FindPlanAsync("basic-plan");

        Assert.NotNull(plan);
        Assert.Equal(29.00m, plan.Price);
    }

    [Fact]
    public async Task ReturnsNoPlanForAnUnknownHandleWithoutAmplifyingProviderLoad()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog();

        // A caller-supplied handle must never be able to force repeated catalogue round-trips.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Null(await harness.Client.FindPlanAsync($"ghost-plan-{attempt}"));
        }

        Assert.Single(harness.Handler.RequestsFor(HttpMethod.Get, "/product_families.json"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task ReturnsNoPlanForABlankHandleWithoutCallingTheProvider(string? handle)
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog();

        Assert.Null(await harness.Client.FindPlanAsync(handle!));
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task ReturnsAnEmptyPlanListWhenTheFamilyHoldsNoProducts()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler =>
            handler.Map(HttpMethod.Get, $"/product_families/{MaxioPayloads.FamilyId}/products.json", MaxioPayloads.EmptyList));

        Assert.Empty(await harness.Client.ListPlansAsync());
    }

    [Fact]
    public async Task FailsClearlyWhenTheConfiguredProductFamilyDoesNotExist()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler =>
            handler.Map(HttpMethod.Get, "/product_families.json", MaxioPayloads.EmptyList));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => harness.Client.ListPlansAsync());

        Assert.Contains("eshop-subscribe", exception.Message);
        Assert.Contains("UC0", exception.Message);
    }

    [Fact]
    public async Task ResolvesTheMeteredComponentAndItsPerUnitPrice()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog();

        var component = await harness.Client.GetMeteredComponentAsync();

        Assert.Equal(MaxioPayloads.ComponentId, component.Id);
        Assert.Equal("api-call", component.Handle);
        Assert.True(component.IsMetered);
        Assert.Equal("metered_component", component.Kind);

        // One cent per unit — read from the decimal string, not from the cents field.
        Assert.Equal(0.01m, component.UnitPrice);
        Assert.Equal("per_unit", component.PricingScheme);
    }

    [Fact]
    public async Task ReportsAComponentSeededWithTheWrongKindAsNotMetered()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler =>
            handler.Map(HttpMethod.Get, $"/product_families/{MaxioPayloads.FamilyId}/components.json", MaxioPayloads.QuantityBasedComponents));

        var component = await harness.Client.GetMeteredComponentAsync();

        Assert.False(component.IsMetered);
        Assert.Equal("quantity_based_component", component.Kind);
    }

    [Fact]
    public async Task FailsWhenTheConfiguredComponentHandleIsNotOnTheFamily()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler =>
            handler.Map(HttpMethod.Get, $"/product_families/{MaxioPayloads.FamilyId}/components.json", MaxioPayloads.EmptyList));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => harness.Client.GetMeteredComponentAsync());

        Assert.Contains("api-call", exception.Message);
        Assert.Contains("UC0", exception.Message);
    }

    [Fact]
    public async Task SurfacesBadCredentialsAsATypedExceptionCarryingTheStatus()
    {
        const string upstreamDiagnostic = "api key 1a2b3c revoked on shard 7";

        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler =>
            handler.Map(HttpMethod.Get, "/product_families.json",
                $$"""{"error":"{{upstreamDiagnostic}}"}""", HttpStatusCode.Unauthorized));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => harness.Client.ListPlansAsync());

        Assert.Equal(401, exception.StatusCode);

        // The status is actionable; the provider's raw body is a diagnostic and belongs in the log,
        // not in a message that reaches a storefront page or an unprivileged API client.
        Assert.DoesNotContain(upstreamDiagnostic, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SurfacesAnUnreachableProviderAsATypedException()
    {
        var handler = new ThrowingHandler(new HttpRequestException("No such host is known."));
        using var httpClient = new HttpClient(handler);
        var client = new MaxioBillingClient(
            httpClient,
            Options.Create(MaxioBillingClientHarness.Settings()),
            new MaxioCatalogCache(),
            Substitute.For<IAppLogger<MaxioBillingClient>>());

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Contains("Could not reach Maxio", exception.Message);
        Assert.Null(exception.StatusCode);
    }

    [Fact]
    public async Task StartsWithoutConfigurationAndReportsTheGapAsATypedBillingError()
    {
        // A host whose Maxio section is missing must still start and still serve every other page;
        // the gap surfaces only when a subscription operation is attempted.
        using var httpClient = new HttpClient(new StubMaxioHandler());

        var client = new MaxioBillingClient(
            httpClient,
            Options.Create(new MaxioSettings()),
            new MaxioCatalogCache(),
            Substitute.For<IAppLogger<MaxioBillingClient>>());

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Contains("Maxio:ApiKey", exception.Message);
    }

    [Fact]
    public async Task ReportsAMissingBaseUrlAndSubdomainAsATypedBillingError()
    {
        var settings = MaxioBillingClientHarness.Settings();
        settings.BaseUrl = null;
        settings.Subdomain = string.Empty;

        using var httpClient = new HttpClient(new StubMaxioHandler());

        var client = new MaxioBillingClient(
            httpClient,
            Options.Create(settings),
            new MaxioCatalogCache(),
            Substitute.For<IAppLogger<MaxioBillingClient>>());

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw _exception;
    }
}
