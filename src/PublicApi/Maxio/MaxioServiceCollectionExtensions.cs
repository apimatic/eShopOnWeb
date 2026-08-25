using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Named HttpClient for the Maxio SDK — keeps its timeout and handler
    /// pipeline off the shared default factory client.
    /// </summary>
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt (backstop against a hung provider); the
                // whole-call budget lives in MaxioSubscriptionService.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey ?? string.Empty,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10) // per attempt
                }
            };
            options.Server.Production.Us.Site = settings.Subdomain ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
