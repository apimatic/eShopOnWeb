using System;
using System.Net.Http;
using Maxio;
using Maxio.Core.Authentication.Basic;
using Maxio.Core.Configuration;
using Maxio.Core.Hooks;
using Maxio.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    public const string HttpClientName = "Maxio";
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName));

        var settings = configuration.GetSection(MaxioOptions.SectionName).Get<MaxioOptions>() ?? new MaxioOptions();
        EnsureConfigured(settings.ApiKey, "Maxio:ApiKey");
        EnsureConfigured(settings.Subdomain, "Maxio:Subdomain");
        EnsureConfigured(settings.ProductFamilyHandle, "Maxio:ProductFamilyHandle");

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = AttemptTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = ConnectionLifetime
            });

        services.AddSingleton(sp =>
        {
            var captured = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioClient(httpClient, BuildClientOptions(captured, loggerFactory));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    internal static MaxioClientOptions BuildClientOptions(MaxioOptions settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with { Timeout = AttemptTimeout },
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            },
            Hooks =
            [
                SdkHook.OnResponse((response, _) => MaxioCallContext.LastHttpStatus = response.StatusCode)
            ]
        };

        options.Server.Production.Us.Site = settings.Subdomain;
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }

        return options;
    }

    private static void EnsureConfigured(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it via environment variable, user-secrets, or your secret store before starting the app.");
        }
    }
}
