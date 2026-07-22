using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The configurable target server. The same build must be able to hit production, a dev/sandbox
/// tenant, or a local mock purely through configuration, and an explicit override must never be
/// silently ignored in favour of a subdomain-derived host.
/// </summary>
public class TargetServerTests
{
    [Fact]
    public void Derives_the_host_from_the_subdomain_when_no_override_is_configured()
    {
        var settings = BillingTestHarness.Settings(baseUrl: null);

        Assert.Equal("https://cp-exp-2.chargify.com", settings.ResolveBaseUrl());
        Assert.False(settings.HasExplicitBaseUrl);
    }

    [Fact]
    public void Derives_the_european_host_when_the_region_is_eu()
    {
        var settings = BillingTestHarness.Settings(baseUrl: null, region: MaxioRegion.Eu);

        Assert.Equal("https://cp-exp-2.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("https://another-tenant.example.com")]
    public void An_explicit_base_url_wins_verbatim_over_the_derived_host(string configured)
    {
        var settings = BillingTestHarness.Settings(baseUrl: configured);

        Assert.True(settings.HasExplicitBaseUrl);
        Assert.Equal(configured, settings.ResolveBaseUrl());
    }

    [Fact]
    public void Whitespace_around_an_explicit_base_url_is_trimmed()
    {
        var settings = BillingTestHarness.Settings(baseUrl: "  http://localhost:8080  ");

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void An_empty_base_url_falls_back_to_the_derived_host()
    {
        var settings = BillingTestHarness.Settings(baseUrl: "   ");

        Assert.False(settings.HasExplicitBaseUrl);
        Assert.Equal("https://cp-exp-2.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void Resolving_fails_when_neither_a_base_url_nor_a_subdomain_is_configured()
    {
        var settings = BillingTestHarness.Settings(baseUrl: null);
        settings.Subdomain = string.Empty;

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("BaseUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_rejects_a_missing_api_key()
    {
        var settings = BillingTestHarness.Settings();
        settings.ApiKey = string.Empty;

        var exception = Assert.Throws<InvalidOperationException>(() => settings.Validate());
        Assert.Contains("ApiKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_rejects_a_base_url_that_is_not_an_absolute_uri()
    {
        var settings = BillingTestHarness.Settings(baseUrl: "not-a-url");

        var exception = Assert.Throws<InvalidOperationException>(() => settings.Validate());
        Assert.Contains("absolute URI", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_rejects_a_missing_metered_component_handle()
    {
        var settings = BillingTestHarness.Settings();
        settings.MeteredComponentHandle = string.Empty;

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public async Task Outbound_traffic_actually_goes_to_the_configured_override_host()
    {
        var server = new StubBillingServer()
            .Get("products/handle", BillingJson.ProductEnvelope(
                BillingJson.Product(7130995, "eshop-pro", "Pro Plan", 29900)));

        var client = BillingTestHarness.Build(server, BillingTestHarness.Settings(baseUrl: "http://localhost:8080"));

        await client.GetPlanAsync("eshop-pro");

        var request = Assert.Single(server.Requests);
        Assert.Equal("localhost", request.Uri.Host);
        Assert.Equal(8080, request.Uri.Port);
        Assert.Equal("http", request.Uri.Scheme);
    }

    [Fact]
    public async Task Outbound_traffic_goes_to_the_derived_host_when_no_override_is_configured()
    {
        var server = new StubBillingServer()
            .Get("products/handle", BillingJson.ProductEnvelope(
                BillingJson.Product(7130995, "eshop-pro", "Pro Plan", 29900)));

        var client = BillingTestHarness.Build(server, BillingTestHarness.Settings(baseUrl: null));

        await client.GetPlanAsync("eshop-pro");

        var request = Assert.Single(server.Requests);
        Assert.Equal("cp-exp-2.chargify.com", request.Uri.Host);
        Assert.Equal("https", request.Uri.Scheme);
    }

    [Fact]
    public async Task Requests_carry_the_configured_api_key_as_basic_credentials()
    {
        var server = new StubBillingServer()
            .Get("products/handle", BillingJson.ProductEnvelope(
                BillingJson.Product(7130995, "eshop-pro", "Pro Plan", 29900)));

        var client = BillingTestHarness.Build(server);

        await client.GetPlanAsync("eshop-pro");

        var request = Assert.Single(server.Requests);
        Assert.NotNull(request.Authorization);
        Assert.StartsWith("Basic ", request.Authorization, StringComparison.Ordinal);

        var decoded = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(request.Authorization!["Basic ".Length..]));

        Assert.Equal("test-api-key:x", decoded);
    }
}
