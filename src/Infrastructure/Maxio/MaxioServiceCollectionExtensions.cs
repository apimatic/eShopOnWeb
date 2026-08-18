using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure;

public static class MaxioServiceCollectionExtensions
{
    public static IConfigurationBuilder AddMaxioEnvironmentBinding(this IConfigurationBuilder builder)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Bind(data, "MAXIO_API_KEY", "Maxio:ApiKey");
        Bind(data, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Bind(data, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        if (data.Count > 0)
        {
            builder.AddInMemoryCollection(data);
        }

        return builder;
    }

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddSingleton<ISubscriptionBillingSettings, MaxioSubscriptionBillingSettings>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddHttpClient<IMaxioAdvancedBillingClient, MaxioAdvancedBillingClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var baseUrl = options.TryResolveBaseUrl() ?? "https://localhost";
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
        });

        return services;
    }

    private static void Bind(IDictionary<string, string?> data, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            data[configurationKey] = value;
        }
    }
}
