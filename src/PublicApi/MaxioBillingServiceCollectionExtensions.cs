using System;
using System.Collections.Generic;
using System.Net.Http;
using Maxio;
using Maxio.Core.Authentication.Basic;
using Maxio.Core.Configuration;
using Maxio.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

public static class MaxioBillingServiceCollectionExtensions
{
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var options = new MaxioClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(15),
                    MaxRetries = 2
                },
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false
                }
            };

            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            return new MaxioClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    public static IConfigurationBuilder AddMaxioEnvironmentOverrides(this IConfigurationBuilder builder)
    {
        var overlay = new Dictionary<string, string?>();
        Copy("MAXIO_API_KEY", "Maxio:ApiKey", overlay);
        Copy("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain", overlay);
        Copy("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle", overlay);
        Copy("MAXIO_BASE_URL", "Maxio:BaseUrl", overlay);

        if (overlay.Count > 0)
        {
            builder.AddInMemoryCollection(overlay);
        }

        return builder;
    }

    private static void Copy(string envName, string configKey, IDictionary<string, string?> overlay)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overlay[configKey] = value;
        }
    }
}
