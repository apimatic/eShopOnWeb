using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Boots the PublicApi with Maxio settings taken from the ambient environment, so the live
/// subscription flow can be exercised on a machine that has sandbox credentials without any of
/// those values living in the repository. With no credentials present the host still starts and the
/// subscription endpoints report the capability as unavailable.
/// </summary>
public class SubscriptionApiFactory : WebApplicationFactory<Program>
{
    public static bool MaxioIsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAXIO_API_KEY")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration =>
        {
            if (!MaxioIsConfigured)
            {
                return;
            }

            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseOnlyInMemoryDatabase"] = "true",
                ["Maxio:ApiKey"] = Environment.GetEnvironmentVariable("MAXIO_API_KEY"),
                ["Maxio:Subdomain"] = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"),
                ["Maxio:ProductFamilyHandle"] = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY"),

                // Optional verbatim override; absent on the sandbox, where the address is derived
                // from the subdomain.
                ["Maxio:BaseUrl"] = Environment.GetEnvironmentVariable("MAXIO_BASE_URL")
            });
        });
    }
}
