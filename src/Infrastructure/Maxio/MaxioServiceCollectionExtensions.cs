using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Maps MAXIO_* environment variables onto the Maxio: configuration section so the
    /// same keys work whether values arrive from user-secrets or the environment.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentOverrides(this IConfigurationBuilder builder)
    {
        var overlay = new Dictionary<string, string?>();
        Map("MAXIO_API_KEY", "Maxio:ApiKey");
        Map("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");
        Map("MAXIO_BASE_URL", "Maxio:BaseUrl");

        if (overlay.Count > 0)
        {
            builder.AddInMemoryCollection(overlay);
        }

        return builder;

        void Map(string environmentVariable, string configurationKey)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                overlay[configurationKey] = value;
            }
        }
    }

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddSingleton(sp =>
        {
            var maxio = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            return new SubscriptionBillingOptions
            {
                ProductFamilyHandle = maxio.ProductFamilyHandle
            };
        });

        services.AddTransient<MaxioTransientRetryHandler>();
        services.AddHttpClient<IMaxioAdvancedBillingClient, MaxioAdvancedBillingClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
                var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) && string.IsNullOrWhiteSpace(options.Subdomain)
                    ? "https://invalid.chargify.com/"
                    : options.ResolveBaseUrl();

                client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio/1.0");

                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
                }
            })
            .AddHttpMessageHandler<MaxioTransientRetryHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            });

        services.AddSingleton<SubscriptionIdempotencyGate>();
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }
}
