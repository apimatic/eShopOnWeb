using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioEnvironmentOverrideTests
{
    [Fact]
    public void ApplyMaxioEnvironmentOverrides_CopiesMaxioEnvVarsOntoSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        configuration["MAXIO_API_KEY"] = "from-env";
        configuration["MAXIO_SITE_SUBDOMAIN"] = "my-site";
        configuration["MAXIO_DEFAULT_PRODUCT_FAMILY"] = "family-handle";

        configuration.ApplyMaxioEnvironmentOverrides();

        Assert.Equal("from-env", configuration["Maxio:ApiKey"]);
        Assert.Equal("my-site", configuration["Maxio:Subdomain"]);
        Assert.Equal("family-handle", configuration["Maxio:ProductFamilyHandle"]);
        Assert.True(string.IsNullOrEmpty(configuration["Maxio:BaseUrl"]));
    }

    [Fact]
    public void ApplyMaxioEnvironmentOverrides_SetsEuBaseUrlWhenEnvironmentIsEu()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        configuration["MAXIO_SITE_SUBDOMAIN"] = "eu-site";
        configuration["MAXIO_ENVIRONMENT"] = "EU";

        configuration.ApplyMaxioEnvironmentOverrides();

        Assert.Equal("https://eu-site.ebilling.maxio.com", configuration["Maxio:BaseUrl"]);
    }

    [Fact]
    public void ApplyMaxioEnvironmentOverrides_DoesNotOverwriteExistingSectionValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        configuration["Maxio:ApiKey"] = "already-set";
        configuration["MAXIO_API_KEY"] = "from-env";

        configuration.ApplyMaxioEnvironmentOverrides();

        Assert.Equal("already-set", configuration["Maxio:ApiKey"]);
    }
}
