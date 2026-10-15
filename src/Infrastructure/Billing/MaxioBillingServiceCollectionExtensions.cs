using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    // Maxio's Basic-auth scheme takes the API key as the username and the literal "x" as the password.
    private const string BasicAuthPassword = "x";

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing. The host refuses to start when the
    /// <c>Maxio:</c> settings are incomplete. The SDK client is built once from the settings at first resolution,
    /// so a rotated API key takes effect on the next process restart.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);
        services.AddOptions<MaxioSettings>()
            .Configure(settings => MaxioSettings.Bind(section, settings))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.AddHttpClient(HttpClientName, (sp, httpClient) =>
                httpClient.Timeout = sp.GetRequiredService<IOptions<MaxioSettings>>().Value.HttpClientTimeout)
            // The SDK client is a singleton holding this HttpClient; recycle pooled connections so DNS changes are seen.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

        services.AddSingleton(CreateClient);
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        services.AddScoped<ISubscriptionBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionClaimStore, EfSubscriptionClaimStore>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        return services;
    }

    private static MaxioAdvancedBillingClient CreateClient(IServiceProvider serviceProvider)
    {
        var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var retryLogger = loggerFactory.CreateLogger(typeof(MaxioBillingGateway).FullName!);

        if (!ServerEnvironment.TryGetKnownValue(settings.Environment, out var environment))
        {
            throw new InvalidOperationException($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.Environment)} must be 'US' or 'EU'.");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = BasicAuthPassword },
            // Default verbs only: GETs are retried, the POSTs this integration makes are never resent.
            Retry = RetryOptions.Default() with
            {
                Timeout = settings.AttemptTimeout,
                OnRetry = attempt => retryLogger.LogWarning("Maxio call retry #{Attempt} after {Delay} ({Reason}).",
                    attempt.AttemptNumber, attempt.Delay, DescribeRetryReason(attempt.Reason))
            },
            // Explicit factory (also disables the SDK's environment-variable logger); never log bodies or headers —
            // customer requests carry names and email addresses.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            },
            TimeProvider = serviceProvider.GetRequiredService<TimeProvider>()
        };

        var subdomain = settings.Subdomain!.Trim();
        var baseUrlOverride = string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl;
        if (environment == ServerEnvironment.Eu)
        {
            options.Server.Production.Eu.Site = subdomain;
            if (baseUrlOverride is not null)
            {
                options.Server.Production.Eu.BaseUrl = baseUrlOverride;
            }
        }
        else
        {
            options.Server.Production.Us.Site = subdomain;
            if (baseUrlOverride is not null)
            {
                options.Server.Production.Us.BaseUrl = baseUrlOverride;
            }
        }

        var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
        return new MaxioAdvancedBillingClient(httpClient, options);
    }

    private static string DescribeRetryReason(RetryReason reason) => reason switch
    {
        RetryReason.Status status => $"HTTP {(int)status.StatusCode}",
        RetryReason.Failure failure => failure.Exception.GetType().Name,
        _ => "unknown"
    };
}
