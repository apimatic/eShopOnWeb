using System.Net.Http.Headers;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests.Client;

/// <summary>
/// Talks to a real Maxio sandbox, when one is configured in the environment.
/// </summary>
/// <remarks>
/// These are read-only checks: they confirm that the credential works, that the configured product
/// family resolves by handle, and that the shapes coming back still deserialize into the
/// transcribed models. Nothing is created, so the suite is safe to run repeatedly. Without
/// <c>MAXIO_API_KEY</c> and <c>MAXIO_SITE_SUBDOMAIN</c> in the environment they do nothing, so the
/// suite still runs offline.
/// </remarks>
public class MaxioSandboxSmokeTests
{
    [Fact]
    public async Task The_configured_credential_can_read_the_site()
    {
        var client = BuildClientOrNull();
        if (client is null)
        {
            return;
        }

        var site = await client.ReadSiteAsync();

        Assert.False(string.IsNullOrWhiteSpace(site.Currency));
        Assert.Equal(
            Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"),
            site.Subdomain);
    }

    [Fact]
    public async Task The_configured_product_family_resolves_by_handle_and_offers_plans()
    {
        var client = BuildClientOrNull();
        var familyHandle = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY");

        if (client is null || string.IsNullOrWhiteSpace(familyHandle))
        {
            return;
        }

        var products = await client.ListProductsForProductFamilyAsync($"handle:{familyHandle}");

        Assert.NotEmpty(products);
        Assert.All(products, product =>
        {
            Assert.False(string.IsNullOrWhiteSpace(product.Handle));
            Assert.Equal(familyHandle, product.ProductFamily?.Handle);
        });
    }

    [Fact]
    public async Task A_reference_that_belongs_to_nobody_reads_back_as_no_customer()
    {
        var client = BuildClientOrNull();
        if (client is null)
        {
            return;
        }

        var reference = MaxioReferences.ForCustomer("eshoponweb-smoketest", $"{Guid.NewGuid():N}@example.invalid");

        Assert.Null(await client.ReadCustomerByReferenceAsync(reference));
    }

    private static MaxioApiClient? BuildClientOrNull()
    {
        var options = new MaxioOptions
        {
            ApiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY"),
            Subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"),
            ProductFamilyHandle = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY") ?? "unused",
            BaseUrl = Environment.GetEnvironmentVariable("MAXIO_BASE_URL")
        };

        if (!options.IsConfigured)
        {
            return null;
        }

        var authentication = new MaxioAuthenticationHandler(new StaticOptionsMonitor<MaxioOptions>(options))
        {
            InnerHandler = new HttpClientHandler()
        };

        var httpClient = new HttpClient(authentication)
        {
            BaseAddress = options.ResolveBaseAddress(),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };

        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);
    }
}
