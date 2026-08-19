using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.PostConfigure<MaxioOptions>(options => ApplyEnvironmentFallbacks(options));

        services.AddTransient<HttpStatusCaptureHandler>();
        services.AddTransient<WriteOnceDelegatingHandler>();

        services.AddHttpClient(MaxioOptions.HttpClientName, client =>
            {
                // Bounds one attempt; default 100s would pin a request thread on a hang.
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<HttpStatusCaptureHandler>()
            .AddHttpMessageHandler<WriteOnceDelegatingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioOptions.HttpClientName);
            var maxio = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(maxio));
        });

        services.AddScoped<ApplicationCore.Interfaces.ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    internal static void ApplyEnvironmentFallbacks(MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            options.ApiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY") ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            options.Subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN") ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            options.ProductFamilyHandle = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY") ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            options.BaseUrl = Environment.GetEnvironmentVariable("MAXIO_BASE_URL");
        }

        if (string.IsNullOrWhiteSpace(options.Environment))
        {
            options.Environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT") ?? "US";
        }
    }

    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions maxio)
    {
        var environment = MapEnvironment(maxio.Environment);
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxRetries = 1
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = maxio.ApiKey,
                Password = "x"
            }
        };

        if (environment == ServerEnvironment.Eu)
        {
            options.Server.Production.Eu.Site = maxio.Subdomain;
            if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
            {
                options.Server.Production.Eu.BaseUrl = maxio.BaseUrl;
            }
        }
        else
        {
            options.Server.Production.Us.Site = maxio.Subdomain;
            if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = maxio.BaseUrl;
            }
        }

        return options;
    }

    private static ServerEnvironment MapEnvironment(string? value)
    {
        if (string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        return ServerEnvironment.Us;
    }
}
