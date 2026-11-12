using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HTTP_CLIENT_NAME = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio SDK client (singleton, built once from the "Maxio:" section) and the billing service.
    /// The host refuses to start when a required setting is missing or blank.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateDataAnnotations()
            .Validate(s => s.IsValidEnvironment, "Maxio:Environment must be 'US' or 'EU'.")
            .Validate(s => string.IsNullOrWhiteSpace(s.BaseUrl) || Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out _),
                "Maxio:BaseUrl, when set, must be an absolute URL.")
            .ValidateOnStart();

        services.AddHttpClient(HTTP_CLIENT_NAME, c => c.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                // Chargify/Maxio basic auth: username = API key, password = "x".
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey.Trim(), Password = "x" },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) },
                Logging = new LoggingOptions { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() }
            };

            if (settings.IsEu)
            {
                options.Environment = ServerEnvironment.Eu;
                options.Server.Production.Eu.Site = settings.Subdomain.Trim();
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
                }
            }
            else
            {
                options.Environment = ServerEnvironment.Us;
                options.Server.Production.Us.Site = settings.Subdomain.Trim();
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    options.Server.Production.Us.BaseUrl = settings.BaseUrl;
                }
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();
        return services;
    }
}
