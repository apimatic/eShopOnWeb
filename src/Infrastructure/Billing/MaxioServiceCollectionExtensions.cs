using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Maps MAXIO_* environment variables onto the <c>Maxio:</c> configuration section
    /// without writing secret values into the repository.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentBindings(this IConfigurationBuilder builder)
    {
        var data = new Dictionary<string, string?>();
        Bind(data, "MAXIO_API_KEY", "Maxio:ApiKey");
        Bind(data, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Bind(data, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Bind(data, "MAXIO_BASE_URL", "Maxio:BaseUrl");
        return builder.AddInMemoryCollection(data);
    }

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddHttpClient<MaxioApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
            client.BaseAddress = new Uri(options.ResolveBaseUrl(environment));
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();
        return services;
    }

    private static void Bind(Dictionary<string, string?> data, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            data[configurationKey] = value;
        }
    }
}
