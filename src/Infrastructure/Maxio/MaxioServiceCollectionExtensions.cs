using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .PostConfigure<IConfiguration>((options, config) => ApplyEnvironmentFallbacks(options, config));

        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();

        services.AddHttpClient<IMaxioBillingGateway, MaxioBillingClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
                var baseUrl = options.IsConfigured
                    ? options.ResolveBaseUrl()
                    : "https://localhost";
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(60);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio/1.0");

                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
                }
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 4
            });

        return services;
    }

    /// <summary>
    /// Credentials arrive as MAXIO_* environment variables. Prefer the bound <c>Maxio:</c>
    /// section (user-secrets / Maxio__* env vars) and fill any remaining blanks from MAXIO_*.
    /// </summary>
    internal static void ApplyEnvironmentFallbacks(MaxioOptions options, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            options.ApiKey = configuration["MAXIO_API_KEY"] ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            options.Subdomain = configuration["MAXIO_SITE_SUBDOMAIN"] ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            options.ProductFamilyHandle = configuration["MAXIO_DEFAULT_PRODUCT_FAMILY"] ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            options.BaseUrl = configuration["MAXIO_BASE_URL"];
        }
    }
}
