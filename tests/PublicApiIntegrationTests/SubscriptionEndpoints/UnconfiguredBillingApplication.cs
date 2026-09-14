using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The API with no billing credentials, which is how it runs for anyone who has not opted into the
/// subscription capability.
/// </summary>
internal sealed class UnconfiguredBillingApplication : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseOnlyInMemoryDatabase"] = "true",
                ["Maxio:ApiKey"] = string.Empty,
                ["Maxio:Subdomain"] = string.Empty,
                ["Maxio:ProductFamilyHandle"] = string.Empty,
                ["Maxio:BaseUrl"] = string.Empty
            }));
    }
}
