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

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>Name of the dedicated <see cref="HttpClient"/> the Maxio SDK client is built over.</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers Maxio Advanced Billing as the subscription billing provider.
    /// Throws at registration — so the host refuses to start — when a required <c>Maxio:</c> setting is missing or invalid.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = MaxioSettings.FromConfiguration(configuration);
        var problems = settings.Validate();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Maxio billing is not configured: " + string.Join(" ", problems) +
                " Set the values with 'dotnet user-secrets' (development) or environment variables such as Maxio__ApiKey (deployment).");
        }

        services.AddSingleton(settings);
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName, client =>
            {
                // Per-attempt backstop above the SDK's own per-attempt timeout; the whole-call budget is a
                // CancellationToken applied by the billing service (MaxioBillingLimits).
                client.Timeout = MaxioBillingLimits.HttpClientTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton; recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are built once here: rotating Maxio:ApiKey takes effect on the next process restart.
        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings,
                sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>()));
        });

        services.AddScoped<MaxioPlanCatalog>();
        services.AddScoped<MaxioCustomerProvisioner>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    public static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings, ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!.Trim(),
                Password = "x"
            },
            Environment = settings.IsEuEnvironment ? ServerEnvironment.Eu : ServerEnvironment.Us,
            // POST stays out of HttpMethodsToRetry (the default list): neither create call may be resent by the SDK.
            Retry = RetryOptions.Default() with
            {
                MaxRetries = MaxioBillingLimits.MaxRetries,
                Timeout = MaxioBillingLimits.AttemptTimeout
            },
            // Customer create bodies carry personal data: never log bodies or headers, and assign the logger
            // factory explicitly so the SDK's environment-variable log switch stays disarmed.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            },
            TimeProvider = timeProvider
        };

        var subdomain = settings.Subdomain?.Trim();
        var baseUrl = settings.BaseUrl?.Trim();
        if (settings.IsEuEnvironment)
        {
            if (!string.IsNullOrEmpty(subdomain)) options.Server.Production.Eu.Site = subdomain;
            if (!string.IsNullOrEmpty(baseUrl)) options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            if (!string.IsNullOrEmpty(subdomain)) options.Server.Production.Us.Site = subdomain;
            if (!string.IsNullOrEmpty(baseUrl)) options.Server.Production.Us.BaseUrl = baseUrl;
        }

        return options;
    }
}
