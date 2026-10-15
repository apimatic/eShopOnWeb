using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioConfigurationExtensions
{
    private static readonly (string Variable, string Key)[] Aliases =
    {
        ("MAXIO_API_KEY", "Maxio:ApiKey"),
        ("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain"),
        ("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle"),
        ("MAXIO_ENVIRONMENT", "Maxio:Environment"),
        ("MAXIO_BASE_URL", "Maxio:BaseUrl"),
    };

    /// <summary>
    /// Maps the <c>MAXIO_*</c> environment variables onto their <c>Maxio:</c> keys as the
    /// lowest-precedence configuration source, so appsettings, user-secrets, <c>Maxio__*</c>
    /// environment variables and the command line all override them.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentAliases(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (variable, key) in Aliases)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value;
            }
        }

        if (values.Count > 0)
        {
            builder.Sources.Insert(0, new MemoryConfigurationSource { InitialData = values });
        }

        return builder;
    }
}
