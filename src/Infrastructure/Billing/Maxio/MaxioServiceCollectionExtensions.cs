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

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Named <see cref="HttpClient"/> used only by the Maxio client, so its timeout/handlers never leak to other consumers.</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>Per-attempt bound enforced by the SDK's retry pipeline (GETs retry; POSTs get exactly one attempt).</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Backstop per-attempt bound on the transport; larger than <see cref="AttemptTimeout"/> so it only fires if that one is disabled.</summary>
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription-billing gateway. Settings are bound from the
    /// <c>Maxio</c> section and validated when the host starts, so a missing credential stops the app from booting.
    /// The client options are built once, when the singleton is first resolved: a rotated API key takes effect on restart.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        services.AddHttpClient(HttpClientName, c => c.Timeout = HttpClientTimeout)
            // The SDK client is a long-lived singleton holding one HttpClient: recycle pooled connections so DNS changes are seen.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var retryLogger = loggerFactory.CreateLogger("Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Retry");
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings, loggerFactory, retryLogger,
                sp.GetRequiredService<TimeProvider>()));
        });

        services.AddScoped<ISubscriptionBillingGateway, MaxioBillingGateway>();
        return services;
    }

    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions settings, ILoggerFactory loggerFactory,
        ILogger retryLogger, TimeProvider timeProvider)
    {
        var environment = string.Equals(settings.Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase)
            ? ServerEnvironment.Eu
            : ServerEnvironment.Us;

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            TimeProvider = timeProvider,
            // Maxio Basic auth: the API key is the username, the password is the literal "x".
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
            // Request bodies carry customer PII (name, email): never log them. Assigning LoggerFactory explicitly also
            // disarms the SDK's environment-variable logging switch.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
                LogRequestBody = false,
            },
            Retry = RetryOptions.Default() with
            {
                Timeout = AttemptTimeout,
                MaxRetries = 2,
                // Only reads are resent by the SDK; writes are settled by re-reading Maxio by reference instead.
                HttpMethodsToRetry = new[] { HttpMethod.Get },
                OnRetry = attempt => retryLogger.LogWarning("Maxio call retry #{AttemptNumber} after {Delay}: {Reason}",
                    attempt.AttemptNumber, attempt.Delay, DescribeRetryReason(attempt.Reason)),
            },
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Verbatim override of the API base address (both regions, so the environment cannot shadow it).
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain!.Trim();
            options.Server.Production.Eu.Site = settings.Subdomain!.Trim();
        }

        return options;
    }

    private static string DescribeRetryReason(RetryReason reason) => reason switch
    {
        RetryReason.Status status => $"HTTP {(int)status.StatusCode}",
        RetryReason.Failure failure => failure.Exception.GetType().Name,
        _ => "unknown"
    };
}
