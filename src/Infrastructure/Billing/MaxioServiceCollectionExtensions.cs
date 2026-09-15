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
    public const string ApiKeyEnvironmentVariable = "MAXIO_API_KEY";
    public const string SubdomainEnvironmentVariable = "MAXIO_SITE_SUBDOMAIN";
    public const string ProductFamilyEnvironmentVariable = "MAXIO_DEFAULT_PRODUCT_FAMILY";

    public static IConfiguration AddMaxioEnvironmentVariables(this IConfiguration configuration)
    {
        Map(configuration, ApiKeyEnvironmentVariable, $"{MaxioOptions.SectionName}:ApiKey");
        Map(configuration, SubdomainEnvironmentVariable, $"{MaxioOptions.SectionName}:Subdomain");
        Map(configuration, ProductFamilyEnvironmentVariable, $"{MaxioOptions.SectionName}:ProductFamilyHandle");
        return configuration;
    }

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName));

        services.AddHttpClient<IMaxioAdvancedBillingClient, MaxioAdvancedBillingClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            client.BaseAddress = options.ResolveApiBaseAddress();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    private static void Map(IConfiguration configuration, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[configurationKey] = value;
        }
    }
}
