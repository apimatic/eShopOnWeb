using System;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration. Settings are read from the "Maxio"
/// configuration section; the <c>ApiKey</c>, <c>Subdomain</c> and <c>ProductFamilyHandle</c> values
/// fall back to their <c>MAXIO_*</c> environment variables when not present in configuration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new MaxioOptions();
        configuration.GetSection(MaxioOptions.CONFIG_SECTION_NAME).Bind(options);

        // The credentials arrive as environment variables; never assume they were placed in an
        // appsettings file. Configuration (user secrets, Maxio__* env vars, ...) always wins.
        options.ApiKey = FirstNonEmpty(options.ApiKey, Environment.GetEnvironmentVariable(MaxioOptions.API_KEY_ENV_VAR));
        options.Subdomain = FirstNonEmpty(options.Subdomain, Environment.GetEnvironmentVariable(MaxioOptions.SUBDOMAIN_ENV_VAR));
        options.ProductFamilyHandle = FirstNonEmpty(options.ProductFamilyHandle, Environment.GetEnvironmentVariable(MaxioOptions.PRODUCT_FAMILY_ENV_VAR));

        services.AddSingleton(options);
        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((sp, client) =>
        {
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });
        services.AddScoped<SubscriptionService>();

        return services;
    }

    private static string? FirstNonEmpty(string? configured, string? environment)
    {
        return !string.IsNullOrWhiteSpace(configured) ? configured : environment;
    }
}
