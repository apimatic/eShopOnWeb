using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

/// <summary>
/// The outbound target server must be configuration-driven, so the identical build can be pointed
/// at production, a sandbox tenant or a local mock without a code change (plan.md §2.3). These
/// tests assert on the URL traffic actually goes to, not just on the settings object, because the
/// provider SDK routes from its own server options rather than from HttpClient.BaseAddress.
/// </summary>
public class TargetServerResolution
{
    [Fact]
    public void DerivesTheUsHostFromTheSubdomainWhenNoOverrideIsSet()
    {
        var settings = BillingClientBuilder.Settings(baseUrl: null, environment: "US");

        Assert.Equal("https://test-site.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesTheEuHostWhenTheRegionIsEu()
    {
        var settings = BillingClientBuilder.Settings(baseUrl: null, environment: "EU");

        Assert.Equal("https://test-site.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void FallsBackToTheUsHostForAnUnrecognisedRegion()
    {
        var settings = BillingClientBuilder.Settings(baseUrl: null, environment: "somewhere-else");

        Assert.Equal("https://test-site.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void UsesAnExplicitBaseUrlVerbatim()
    {
        var settings = BillingClientBuilder.Settings(baseUrl: "http://localhost:8080");

        Assert.True(settings.HasExplicitBaseUrl);
        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void TreatsAnEmptyBaseUrlAsNoOverride()
    {
        var settings = BillingClientBuilder.Settings(baseUrl: "   ");

        Assert.False(settings.HasExplicitBaseUrl);
        Assert.Equal("https://test-site.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public async Task SendsTrafficToTheDerivedHostWhenNoOverrideIsSet()
    {
        var handler = new StubHttpMessageHandler().RespondOk(HttpMethod.Get, "/products.json", MaxioJson.ProductList());
        var client = BillingClientBuilder.Build(handler, BillingClientBuilder.Settings(baseUrl: null));

        await client.ListPlansAsync();

        Assert.StartsWith("https://test-site.chargify.com/", handler.LastRequest.AbsoluteUri);
    }

    [Fact]
    public async Task SendsTrafficToAnExplicitBaseUrlInsteadOfTheDerivedHost()
    {
        var handler = new StubHttpMessageHandler().RespondOk(HttpMethod.Get, "/products.json", MaxioJson.ProductList());
        var client = BillingClientBuilder.Build(handler, BillingClientBuilder.Settings(baseUrl: "http://localhost:8080"));

        await client.ListPlansAsync();

        // The override wins outright: no request may reach the subdomain-derived host.
        Assert.StartsWith("http://localhost:8080/", handler.LastRequest.AbsoluteUri);
        Assert.DoesNotContain("chargify.com", handler.LastRequest.AbsoluteUri);
    }

    [Fact]
    public async Task HonoursAnExplicitBaseUrlEvenInTheEuRegion()
    {
        // The override must not be defeated by the region axis, which selects a different server.
        var handler = new StubHttpMessageHandler().RespondOk(HttpMethod.Get, "/products.json", MaxioJson.ProductList());
        var settings = BillingClientBuilder.Settings(baseUrl: "http://localhost:8080", environment: "EU");
        var client = BillingClientBuilder.Build(handler, settings);

        await client.ListPlansAsync();

        Assert.StartsWith("http://localhost:8080/", handler.LastRequest.AbsoluteUri);
        Assert.DoesNotContain("ebilling.maxio.com", handler.LastRequest.AbsoluteUri);
    }

    [Fact]
    public async Task SendsTrafficToTheEuHostWhenTheRegionIsEuAndNoOverrideIsSet()
    {
        var handler = new StubHttpMessageHandler().RespondOk(HttpMethod.Get, "/products.json", MaxioJson.ProductList());
        var client = BillingClientBuilder.Build(handler, BillingClientBuilder.Settings(baseUrl: null, environment: "EU"));

        await client.ListPlansAsync();

        Assert.StartsWith("https://test-site.ebilling.maxio.com/", handler.LastRequest.AbsoluteUri);
    }

    [Fact]
    public async Task AuthenticatesWithTheConfiguredApiKeyAsTheBasicUsername()
    {
        var handler = new StubHttpMessageHandler().RespondOk(HttpMethod.Get, "/products.json", MaxioJson.ProductList());
        var client = BillingClientBuilder.Build(handler);

        await client.ListPlansAsync();

        var request = handler.LastRequest;
        Assert.Equal("Basic", request.AuthorizationScheme);

        // Maxio's scheme is the API key as the username and the literal "x" as the password.
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(request.AuthorizationParameter!));
        Assert.Equal("test-api-key:x", decoded);
    }

    [Fact]
    public void RefusesToConstructWithoutAnApiKey()
    {
        var settings = BillingClientBuilder.Settings();
        settings.ApiKey = null;

        var exception = Assert.Throws<BillingConfigurationException>(() =>
            BillingClientBuilder.Build(new StubHttpMessageHandler(), settings));

        Assert.Contains("ApiKey", exception.Message);
    }

    [Fact]
    public void RefusesToConstructWithNeitherABaseUrlNorASubdomain()
    {
        var settings = BillingClientBuilder.Settings();
        settings.Subdomain = null;
        settings.BaseUrl = null;

        Assert.Throws<BillingConfigurationException>(() =>
            BillingClientBuilder.Build(new StubHttpMessageHandler(), settings));
    }

    [Fact]
    public void ResolveBaseUrlExplainsItselfWhenNothingIsConfigured()
    {
        var settings = new MaxioSettings { ApiKey = "k" };

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());

        Assert.Contains("Maxio:BaseUrl", exception.Message);
        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    [Fact]
    public void BindsFromTheMaxioConfigurationSectionName()
    {
        // The section name is part of the deployment contract documented in plan.md §5.
        Assert.Equal("Maxio", MaxioSettings.SectionName);
        Assert.NotNull(Options.Create(BillingClientBuilder.Settings()).Value);
    }
}
