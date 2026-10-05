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

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the <see cref="HttpClient"/> dedicated to Maxio (kept off the shared default client).</summary>
    public const string HttpClientName = "Maxio";

    /// <summary>Per-attempt bound on one HTTP exchange; the request-wide bound is the subscription service's budget.</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(8);

    /// <summary>Transport backstop just above <see cref="AttemptTimeout"/>.</summary>
    public static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Maxio's Basic-auth password is always the literal "x" (the API key is the username).</summary>
    private const string BasicAuthPassword = "x";

    /// <summary>
    /// Registers the Maxio client and the billing gateway. Settings are validated when the host starts, so a
    /// missing credential stops the app instead of surfacing as a 401 on the first request.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, client => client.Timeout = HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton holding one HttpClient: recycle pooled connections so DNS changes are seen.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are built once, here: a rotated API key takes effect when the process restarts.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient,
                CreateClientOptions(settings, sp.GetRequiredService<ILoggerFactory>()));
        });

        services.AddScoped<IBillingGateway, MaxioBillingGateway>();
        return services;
    }

    public static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = BasicAuthPassword },
            // GET/HEAD/PUT/OPTIONS only (the default verb list): the POSTs that create customers and
            // subscriptions are never resent by the SDK.
            Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = AttemptTimeout },
            // An explicit factory also disables the SDK's log environment variable, which could otherwise switch
            // on unredacted request bodies (customer e-mail and names) from outside the code.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain!.Trim();
        }

        return options;
    }
}
