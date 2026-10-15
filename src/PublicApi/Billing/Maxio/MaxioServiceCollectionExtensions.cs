using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>Per attempt: the SDK's own timeout (its expiry is retried on GETs).</summary>
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Per attempt backstop on the transport; ends the call the first time it fires.</summary>
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(15);

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        // A named client keeps this pipeline (timeout, handler) off the app's shared default HttpClient;
        // the pooled-connection lifetime keeps DNS fresh behind the long-lived singleton below.
        services.AddHttpClient(HttpClientName, client => client.Timeout = HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are built once here: a rotated API key takes effect after a process restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey!,
                    Password = "x" // Maxio's Basic scheme: the API key is the username, the password is the literal "x".
                },
                // Default retried verbs (GET/HEAD/PUT/OPTIONS) only: the POST writes are never resent.
                Retry = RetryOptions.Default() with { Timeout = AttemptTimeout },
                // Explicit factory: disables the MAXIOADVANCEDBILLINGCLIENT_LOG override, and bodies
                // (customer e-mail and name) and headers (the API key) are never logged.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            if (settings.HasBaseUrlOverride)
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingGateway, MaxioSubscriptionBillingGateway>();
        return services;
    }
}
