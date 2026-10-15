using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    private const string HTTP_CLIENT_NAME = "Maxio";
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registers the Maxio client, gateway and subscription service. The host refuses to start when the
    /// Maxio settings are missing. Settings are read once, when the client is built; rotating the secret needs a restart.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.CONFIG_NAME))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>());

        // Named client: keeps this pipeline off the shared default HttpClient. Timeout is per attempt.
        services.AddHttpClient(HTTP_CLIENT_NAME, c => c.Timeout = AttemptTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp => CreateClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME),
            sp.GetRequiredService<IOptions<MaxioSettings>>().Value,
            sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<IMaxioGateway, MaxioGateway>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        return services;
    }

    public static MaxioAdvancedBillingClient CreateClient(HttpClient httpClient, MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var useEu = string.Equals(settings.Environment, "EU", StringComparison.OrdinalIgnoreCase);
        var options = new MaxioAdvancedBillingClientOptions
        {
            // The API key is the Basic-auth username; the password is the constant "x".
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
            Environment = useEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            Retry = RetryOptions.Default() with { Timeout = AttemptTimeout },
            // Bodies carry customer name/email: never log them; assign the factory explicitly.
            Logging = new LoggingOptions { LoggerFactory = loggerFactory, LogRequestBody = false }
        };

        if (useEu)
        {
            options.Server.Production.Eu.Site = settings.Subdomain!;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain!;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
