using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

public static class ConfigurationExtensions
{
    public static IConfigurationBuilder AddConfigurationFile(this IConfigurationBuilder configuration, string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(path))
            configuration.AddJsonFile(path, optional: true, reloadOnChange: false);
        return configuration;
    }
}
