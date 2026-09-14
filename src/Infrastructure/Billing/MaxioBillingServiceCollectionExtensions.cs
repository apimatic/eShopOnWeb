using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Binds the Maxio options, configures the authenticated Maxio API client, and registers the
    /// subscription application service. Configuration is read from the "Maxio" section using exactly
    /// the keys Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle and the optional Maxio:BaseUrl.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var section = configuration.GetSection(MaxioOptions.SectionName);
        services.AddOptions<MaxioOptions>()
            .Bind(section);

        services.AddMemoryCache();

        services.AddHttpClient<MaxioApiClient>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioOptions>>().Value;
            httpClient.BaseAddress = new Uri(options.GetBaseAddress());
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x")));
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}

public static class MaxioConfigurationBuilderExtensions
{
    /// <summary>
    /// Bridges the documented Maxio environment variables onto the "Maxio" configuration section so the
    /// same build runs against whichever site/catalog the environment points at. Referenced names only —
    /// no credential values are compiled or written anywhere.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>();
        AddIfPresent(values, "Maxio:ApiKey", "MAXIO_API_KEY");
        AddIfPresent(values, "Maxio:Subdomain", "MAXIO_SITE_SUBDOMAIN");
        AddIfPresent(values, "Maxio:ProductFamilyHandle", "MAXIO_DEFAULT_PRODUCT_FAMILY");
        if (values.Count > 0)
        {
            builder.AddInMemoryCollection(values);
        }

        return builder;
    }

    private static void AddIfPresent(IDictionary<string, string?> values, string configKey, string environmentVariable)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[configKey] = value;
        }
    }
}
