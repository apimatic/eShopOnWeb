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

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>Name of the <see cref="HttpClient"/> the Maxio SDK client sends through.</summary>
    public const string HttpClientName = "Maxio";

    // One attempt may wait this long for response headers; the per-call budgets in MaxioSubscriptionService
    // bound the whole call, retries included.
    internal static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing. The host refuses to start when a
    /// required <c>Maxio:</c> setting is missing.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();

        services.AddHttpClient(HttpClientName, c => c.Timeout = HttpClientTimeout)
            // The SDK client is a singleton holding this HttpClient; recycle pooled connections so DNS changes are seen.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

        // Built once: a rotated API key takes effect on the next process restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, CreateClientOptions(settings, sp.GetRequiredService<ILoggerFactory>()));
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<MaxioSiteProfileCache>();
        services.AddScoped<ISubscriptionEnrollmentStore, SubscriptionEnrollmentStore>();
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    internal static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioOptions settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEuHosted ? ServerEnvironment.Eu : ServerEnvironment.Us,
            // Maxio documents the API key as the user name and "x" as the password.
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" },
            // Default verbs only (GET/HEAD/PUT/OPTIONS): customer and subscription creates (POST) are never resent.
            Retry = RetryOptions.Default() with { Timeout = PerAttemptTimeout },
            // Assigned explicitly so the SDK's environment-variable logger can never switch on body logging:
            // customer create carries names and email addresses.
            Logging = new LoggingOptions { LoggerFactory = loggerFactory, LogRequestBody = false }
        };

        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl.Trim();
        var site = settings.Subdomain?.Trim() ?? string.Empty;
        if (settings.IsEuHosted)
        {
            options.Server.Production.Eu.Site = site;
            if (baseUrl is not null) options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = site;
            if (baseUrl is not null) options.Server.Production.Us.BaseUrl = baseUrl;
        }

        return options;
    }
}
