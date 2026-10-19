using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the dedicated HttpClient the Maxio SDK client runs over.</summary>
    public const string HTTP_CLIENT_NAME = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription billing service.
    /// The host refuses to start when a required <c>Maxio:</c> setting is missing or invalid.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.CONFIG_NAME))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(HTTP_CLIENT_NAME, (sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
                // Backstop for one attempt; the whole-operation budget is enforced by MaxioBillingService.
                client.Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton holding this HttpClient: recycle connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are captured once here; a rotated API key takes effect on the next process start.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings, sp.GetRequiredService<ILoggerFactory>()));
        });

        services.AddScoped<ISubscriptionEnrollmentStore, SubscriptionEnrollmentStore>();
        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    public static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            // Maxio Basic auth: the API key is the user name and the password is the literal "x".
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" },
            // Defaults keep POST out of HttpMethodsToRetry, so customer/subscription creation is never resent by the SDK.
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds)
            },
            Logging = new LoggingOptions
            {
                // Set explicitly: also disables the MAXIOADVANCEDBILLINGCLIENT_LOG environment switch.
                LoggerFactory = loggerFactory,
                // Customer create bodies carry e-mail and names — never log bodies or headers.
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl.Trim();
        if (settings.IsEu)
        {
            if (baseUrl != null) options.Server.Production.Eu.BaseUrl = baseUrl;
            else options.Server.Production.Eu.Site = settings.Subdomain!.Trim();
        }
        else
        {
            if (baseUrl != null) options.Server.Production.Us.BaseUrl = baseUrl;
            else options.Server.Production.Us.Site = settings.Subdomain!.Trim();
        }

        return options;
    }
}
