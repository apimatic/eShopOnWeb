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
    private const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio client and the subscription service. Settings are read once here, so the host
    /// refuses to start when one is missing, and a rotated API key needs a restart.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.CONFIG_NAME).Get<MaxioSettings>() ?? new MaxioSettings();
        Validate(settings);
        services.AddSingleton(Options.Create(settings));

        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                // The username is the API key; Maxio expects the literal "x" as the password.
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10), MaxRetries = 2 },
                // Request bodies carry customer PII; they stay out of the logs (LogRequestBody defaults to off).
                Logging = new LoggingOptions { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() },
            };
            options.Server.Production.Us.Site = settings.Subdomain!;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionService, SubscriptionService>();
        return services;
    }

    private static void Validate(MaxioSettings s)
    {
        string? missing = null;
        if (string.IsNullOrWhiteSpace(s.ApiKey)) missing = "Maxio:ApiKey";
        else if (string.IsNullOrWhiteSpace(s.Subdomain)) missing = "Maxio:Subdomain";
        else if (string.IsNullOrWhiteSpace(s.ProductFamilyHandle)) missing = "Maxio:ProductFamilyHandle";
        if (missing != null)
        {
            throw new InvalidOperationException(
                $"{missing} is not configured. Set it with user-secrets or the environment (as Maxio__{missing.Substring(6)}) before starting.");
        }

        if (!string.IsNullOrWhiteSpace(s.BaseUrl) && !Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute URL when set.");
        }
    }
}
