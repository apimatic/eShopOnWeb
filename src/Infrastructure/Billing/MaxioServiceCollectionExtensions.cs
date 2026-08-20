using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddHttpClient<MaxioApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl) || !string.IsNullOrWhiteSpace(options.Subdomain))
            {
                client.BaseAddress = options.GetApiBaseAddress();
            }

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }

            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    /// <summary>
    /// Maps <c>MAXIO_*</c> environment variables onto the <c>Maxio:</c> configuration section when
    /// those keys are not already populated (for example from user-secrets).
    /// </summary>
    public static void ApplyMaxioEnvironmentOverrides(this IConfiguration configuration)
    {
        CopyIfEmpty(configuration, "Maxio:ApiKey", "MAXIO_API_KEY");
        CopyIfEmpty(configuration, "Maxio:Subdomain", "MAXIO_SITE_SUBDOMAIN");
        CopyIfEmpty(configuration, "Maxio:ProductFamilyHandle", "MAXIO_DEFAULT_PRODUCT_FAMILY");

        if (!string.IsNullOrWhiteSpace(configuration["Maxio:BaseUrl"]))
        {
            return;
        }

        var environment = configuration["MAXIO_ENVIRONMENT"];
        var subdomain = configuration["Maxio:Subdomain"];
        if (!string.IsNullOrWhiteSpace(subdomain)
            && string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase))
        {
            configuration["Maxio:BaseUrl"] = $"https://{subdomain}.ebilling.maxio.com";
        }
    }

    private static void CopyIfEmpty(IConfiguration configuration, string destinationKey, string sourceKey)
    {
        if (string.IsNullOrWhiteSpace(configuration[destinationKey])
            && !string.IsNullOrWhiteSpace(configuration[sourceKey]))
        {
            configuration[destinationKey] = configuration[sourceKey];
        }
    }
}
