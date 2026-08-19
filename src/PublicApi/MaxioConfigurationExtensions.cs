using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class MaxioConfigurationExtensions
{
    /// <summary>
    /// Maps the sandbox credential environment variables onto the <c>Maxio:</c> configuration section.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentBindings(this IConfigurationBuilder builder)
    {
        var data = new Dictionary<string, string?>();
        Copy("MAXIO_API_KEY", "Maxio:ApiKey");
        Copy("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Copy("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Copy("MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (data.Count > 0)
        {
            builder.AddInMemoryCollection(data);
        }

        return builder;

        void Copy(string environmentVariable, string configurationKey)
        {
            var value = System.Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                data[configurationKey] = value;
            }
        }
    }
}
