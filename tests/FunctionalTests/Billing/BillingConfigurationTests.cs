using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.FunctionalTests.Billing;

public class BillingConfigurationTests
{
    [Theory]
    [InlineData("ApiKey")]
    [InlineData("Subdomain")]
    [InlineData("ProductFamilyHandle")]
    public async Task BlankRequiredSettingRefusesHostStartup(string blank)
    {
        var values = new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "stub-key", ["Maxio:Subdomain"] = "stub-site", ["Maxio:ProductFamilyHandle"] = "family"
        };
        values["Maxio:" + blank] = " ";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        using var host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddSubscriptionBilling(configuration);
        }).Build();
        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Contains("Maxio:" + blank, error.Message);
        Assert.DoesNotContain("stub-key", error.Message);
    }
}
