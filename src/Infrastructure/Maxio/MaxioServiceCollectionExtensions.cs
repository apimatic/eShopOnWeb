using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.PostConfigure<MaxioOptions>(options =>
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
        });

        services.AddTransient<OnceOnlySendHandler>();
        services.AddTransient<MaxioLoggingHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<MaxioLoggingHandler>()
            .AddHttpMessageHandler<OnceOnlySendHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var config = sp.GetRequiredService<IConfiguration>();
            var envRaw = config["MAXIO_ENVIRONMENT"] ?? "US";
            return CreateClient(httpClient, settings, envRaw);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    internal static MaxioAdvancedBillingClient CreateClient(
        HttpClient httpClient,
        MaxioOptions settings,
        string environmentRaw)
    {
        if (!ServerEnvironment.TryGetKnownValue(environmentRaw, out var environment) || environment is null)
        {
            throw new InvalidOperationException("MAXIO_ENVIRONMENT must be US or EU.");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) },
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            }
        };

        if (environment == ServerEnvironment.Us)
        {
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
        }
        else
        {
            options.Server.Production.Eu.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
