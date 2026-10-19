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

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>Named HttpClient used only by the Maxio SDK client, so its timeout/handlers never leak to other consumers.</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>Per-attempt bound applied by the SDK's retry pipeline (reads may be retried; POSTs never are).</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Backstop per-attempt bound on the transport; the whole-call budget lives in <see cref="MaxioSubscriptionService"/>.</summary>
    public static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription billing service.
    /// The host refuses to start when a required <c>Maxio:</c> setting is missing.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();

        services.AddHttpClient(HttpClientName, client => client.Timeout = HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton holding one HttpClient; recycle connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.TryAddSingleton(TimeProvider.System);

        // Options are read once here: rotating Maxio:ApiKey takes effect on the next process restart.
        services.AddSingleton(sp => CreateClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetRequiredService<IOptions<MaxioOptions>>().Value,
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<TimeProvider>()));

        services.AddMemoryCache();
        services.AddScoped<BillingClaimStore>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionService>();

        return services;
    }

    public static MaxioAdvancedBillingClient CreateClient(HttpClient httpClient, MaxioOptions settings, ILoggerFactory loggerFactory, TimeProvider timeProvider)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            // Maxio Basic auth: the API key is the username, the password is the literal "x".
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" },
            Environment = string.Equals(settings.Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase)
                ? ServerEnvironment.Eu
                : ServerEnvironment.Us,
            // Default retryable verbs (GET/HEAD/PUT/OPTIONS) are kept, so customer/subscription POSTs are never resent.
            Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = AttemptTimeout },
            // Explicit factory: disables the MAXIOADVANCEDBILLINGCLIENT_LOG override; bodies carry customer PII, so never log them.
            Logging = new LoggingOptions { LoggerFactory = loggerFactory, LogRequestBody = false },
            TimeProvider = timeProvider,
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Maxio:BaseUrl is used verbatim instead of the subdomain-derived address.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl.Trim();
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl.Trim();
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain!.Trim();
            options.Server.Production.Eu.Site = settings.Subdomain!.Trim();
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
