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
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var maxioOptions = ReadOptions(configuration);
        services.AddSingleton(Options.Create(maxioOptions));

        services.AddTransient<MaxioWriteOnceHandler>();
        services.AddTransient<MaxioStatusCaptureHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<MaxioWriteOnceHandler>()
            .AddHttpMessageHandler<MaxioStatusCaptureHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            return new MaxioAdvancedBillingClient(httpClient, CreateSdkOptions(options));
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    internal static MaxioOptions ReadOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioOptions.SectionName);
        var defaultHandle = section[nameof(MaxioOptions.DefaultProductHandle)];
        return new MaxioOptions
        {
            ApiKey = section[nameof(MaxioOptions.ApiKey)] ?? string.Empty,
            Subdomain = section[nameof(MaxioOptions.Subdomain)] ?? string.Empty,
            ProductFamilyHandle = section[nameof(MaxioOptions.ProductFamilyHandle)] ?? string.Empty,
            BaseUrl = section[nameof(MaxioOptions.BaseUrl)],
            Environment = section[nameof(MaxioOptions.Environment)],
            DefaultProductHandle = string.IsNullOrWhiteSpace(defaultHandle) ? "eshop-pro" : defaultHandle
        };
    }

    internal static MaxioAdvancedBillingClientOptions CreateSdkOptions(MaxioOptions options)
    {
        var environment = ParseEnvironment(options.Environment);
        var sdkOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            Retry = RetryOptions.Default() with
            {
                Timeout = options.AttemptTimeout,
                MaxRetries = 1
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = string.IsNullOrWhiteSpace(options.ApiKey) ? "missing" : options.ApiKey,
                Password = "x"
            }
        };

        var site = string.IsNullOrWhiteSpace(options.Subdomain) ? "subdomain" : options.Subdomain;
        sdkOptions.Server.Production.Us.Site = site;
        sdkOptions.Server.Production.Eu.Site = site;

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (environment == ServerEnvironment.Eu)
            {
                sdkOptions.Server.Production.Eu.BaseUrl = options.BaseUrl;
            }
            else
            {
                sdkOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
            }
        }

        return sdkOptions;
    }

    internal static ServerEnvironment ParseEnvironment(string? value)
    {
        if (string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        return ServerEnvironment.Us;
    }
}
