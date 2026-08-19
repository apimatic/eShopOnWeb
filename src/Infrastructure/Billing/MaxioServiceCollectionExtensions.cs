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

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.PostConfigure<MaxioOptions>(ApplyEnvironmentOverrides);

        services.AddTransient<MaxioStatusCaptureHandler>();
        services.AddTransient<MaxioSingleSendHandler>();
        services.AddTransient<MaxioRequestLoggingHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(100);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<MaxioStatusCaptureHandler>()
            .AddHttpMessageHandler<MaxioSingleSendHandler>()
            .AddHttpMessageHandler<MaxioRequestLoggingHandler>();

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            return new MaxioAdvancedBillingClient(httpClient, CreateClientOptions(options));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    public static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioOptions maxio)
    {
        var environment = ServerEnvironment.Us;
        var envName = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        if (!string.IsNullOrWhiteSpace(envName)
            && ServerEnvironment.TryGetKnownValue(envName, out var parsed)
            && parsed is not null)
        {
            environment = parsed;
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(30)
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = maxio.ApiKey ?? string.Empty,
                Password = "x"
            }
        };

        if (environment == ServerEnvironment.Eu)
        {
            if (!string.IsNullOrWhiteSpace(maxio.Subdomain))
            {
                options.Server.Production.Eu.Site = maxio.Subdomain;
            }

            if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
            {
                options.Server.Production.Eu.BaseUrl = maxio.BaseUrl;
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(maxio.Subdomain))
            {
                options.Server.Production.Us.Site = maxio.Subdomain;
            }

            if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = maxio.BaseUrl;
            }
        }

        return options;
    }

    private static void ApplyEnvironmentOverrides(MaxioOptions options)
    {
        Overlay(Environment.GetEnvironmentVariable("MAXIO_API_KEY"), value => options.ApiKey = value);
        Overlay(Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"), value => options.Subdomain = value);
        Overlay(Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY"), value => options.ProductFamilyHandle = value);
        Overlay(Environment.GetEnvironmentVariable("MAXIO_BASE_URL"), value => options.BaseUrl = value);
    }

    private static void Overlay(string? value, Action<string> assign)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            assign(value);
        }
    }
}
