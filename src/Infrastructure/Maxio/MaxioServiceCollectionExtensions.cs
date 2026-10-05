using System;
using System.Net.Http;
using System.Threading;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription billing service.
    /// Validates the configuration eagerly so a host with missing or blank credentials
    /// refuses to start instead of failing with a 401 on the first call.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(MaxioOptions.SectionName).Get<MaxioOptions>()
                      ?? throw new InvalidOperationException(
                          $"The '{MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)}', " +
                          $"'{MaxioOptions.SectionName}:{nameof(MaxioOptions.Subdomain)}' and " +
                          $"'{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}' configuration keys are not configured. " +
                          "Set them via user-secrets or environment variables before starting the host.");

        // Every part of the credential must be present and non-blank; a blank part is a
        // misconfiguration, not a partial one. The Basic password is the fixed value "x".
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)} is not configured. " +
                "Set it via user-secrets or environment variables before starting the host.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)} is not configured. " +
                "Set it via user-secrets or environment variables before starting the host.");
        }

        var baseUrlOverride = !string.IsNullOrWhiteSpace(options.BaseUrl);
        if (!baseUrlOverride && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new InvalidOperationException(
                $"{MaxioOptions.SectionName}:{nameof(MaxioOptions.Subdomain)} is not configured " +
                $"(and no {MaxioOptions.SectionName}:{nameof(MaxioOptions.BaseUrl)} override is set). " +
                "Set it via user-secrets or environment variables before starting the host.");
        }

        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddHttpClient(ClientName, httpClient =>
            {
                // Per-attempt backstop; the SDK's Retry.Timeout is also per attempt.
                httpClient.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ResolveEnvironment(configuration),
                BasicAuth = new BasicAuthCredentials
                {
                    Username = options.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    // Per attempt. A GET that hangs costs at most ~2 attempts + backoff; POSTs
                    // are never resent by the SDK (default HttpMethodsToRetry excludes POST).
                    Timeout = TimeSpan.FromSeconds(10),
                    MaxRetries = 2
                },
                Logging = new LoggingOptions
                {
                    // Assigned explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG environment
                    // variable cannot switch request/response body logging on from outside.
                    LoggerFactory = NullLoggerFactory.Instance,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                    LogRequestBody = false
                }
            };

            if (baseUrlOverride)
            {
                clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl!.TrimEnd('/');
            }
            else
            {
                clientOptions.Server.Production.Us.Site = options.Subdomain;
            }

            return new MaxioAdvancedBillingClient(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName),
                clientOptions);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }

    private const string ClientName = "MaxioAdvancedBilling";

    private static ServerEnvironment ResolveEnvironment(IConfiguration configuration)
    {
        // MAXIO_ENVIRONMENT selects the Maxio hosting region ("US"/"EU"); defaults to US.
        var value = configuration["MAXIO_ENVIRONMENT"];
        return string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase)
            ? ServerEnvironment.Eu
            : ServerEnvironment.Us;
    }
}