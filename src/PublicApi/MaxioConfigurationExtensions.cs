using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Registers the Maxio Advanced Billing integration. Settings come from the "Maxio"
/// configuration section (secrets belong in user-secrets or environment variables);
/// the well-known MAXIO_* environment variables are used as fallbacks when the section
/// does not carry a value.
/// </summary>
public static class MaxioConfigurationExtensions
{
    public static IServiceCollection AddMaxio(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .PostConfigure(o =>
            {
                o.ApiKey = Choose(o.ApiKey, configuration["MAXIO_API_KEY"]);
                o.Subdomain = Choose(o.Subdomain, configuration["MAXIO_SITE_SUBDOMAIN"]);
                o.ProductFamilyHandle = Choose(o.ProductFamilyHandle, configuration["MAXIO_DEFAULT_PRODUCT_FAMILY"]);
            })
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Maxio:ApiKey is required (set MAXIO_API_KEY or user-secrets).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required (set MAXIO_DEFAULT_PRODUCT_FAMILY or user-secrets).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Subdomain) || !string.IsNullOrWhiteSpace(o.BaseUrl),
                "Either Maxio:Subdomain (set MAXIO_SITE_SUBDOMAIN or user-secrets) or Maxio:BaseUrl must be provided.")
            .ValidateOnStart();

        services.AddHttpClient<MaxioClient>(MaxioClient.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            client.BaseAddress = new Uri(ResolveBaseUrl(options));
            client.Timeout = TimeSpan.FromSeconds(30);

            // Maxio Billing API auth: HTTP Basic with the API key as username and "X" as password.
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    private static string ResolveBaseUrl(MaxioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/') + "/";
        }

        return $"https://{options.Subdomain}.chargify.com/";
    }

    private static string Choose(string configuredValue, string? fallback) =>
        string.IsNullOrWhiteSpace(configuredValue) ? (fallback ?? string.Empty) : configuredValue;
}
