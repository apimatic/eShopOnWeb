using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing SDK client and the billing service.
/// The client is a long-lived singleton over a named <see cref="System.Net.Http.HttpClient"/>
/// managed by <see cref="System.Net.Http.IHttpClientFactory"/>; its options object is built
/// once at registration, so a rotated Maxio:ApiKey takes effect on process restart.
/// </summary>
public static class MaxioBillingRegistration
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Total deadline for one billing flow (a handler makes several Maxio calls; the
    /// per-attempt timeout does not bound the whole flow).
    /// </summary>
    internal static readonly TimeSpan FlowBudget = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Per-attempt bound for one Maxio call (backstop; also enforced by HttpClient.Timeout).
    /// </summary>
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(15);

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Map the documented environment variable names onto the Maxio: configuration keys
        // (names only — values arrive from the deployment environment or user-secrets).
        MapEnvironmentVariable(configuration, MaxioOptions.ApiKeyEnvVar, $"{MaxioOptions.SectionName}:ApiKey");
        MapEnvironmentVariable(configuration, MaxioOptions.SubdomainEnvVar, $"{MaxioOptions.SectionName}:Subdomain");
        MapEnvironmentVariable(configuration, MaxioOptions.ProductFamilyHandleEnvVar, $"{MaxioOptions.SectionName}:ProductFamilyHandle");
        MapEnvironmentVariable(configuration, MaxioOptions.EnvironmentEnvVar, $"{MaxioOptions.SectionName}:Environment");

        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt; the SDK's own Retry.Timeout is per attempt too. The whole
                // flow is bounded separately (see MaxioBillingService).
                client.Timeout = PerAttemptTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                // The SDK client below is a singleton, so keep pooled connections rotating.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var maxio = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = string.Equals(maxio.Environment, "EU", StringComparison.OrdinalIgnoreCase)
                    ? ServerEnvironment.Eu
                    : ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxio.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 2,
                    Timeout = PerAttemptTimeout
                },
                Logging = new LoggingOptions
                {
                    // LoggerFactory assigned explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG
                    // environment variable cannot switch logging (incl. request bodies) on.
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                    LogRequestBody = false
                }
            };

            // Maxio:BaseUrl, when set, is used verbatim as the API base address; otherwise the
            // host is derived from the site subdomain. Set the site for both hosting regions so
            // the selected environment always resolves correctly.
            if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = maxio.BaseUrl;
                options.Server.Production.Eu.BaseUrl = maxio.BaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = maxio.Subdomain;
                options.Server.Production.Eu.Site = maxio.Subdomain;
            }

            return new MaxioAdvancedBillingClient(
                sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(HttpClientName),
                options);
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();
        services.AddHttpContextAccessor();

        return services;
    }

    private static void MapEnvironmentVariable(IConfiguration configuration, string environmentVariable, string configKey)
    {
        var value = System.Environment.GetEnvironmentVariable(environmentVariable);
        // Environment variables override file/secret/placeholder configuration — the
        // standard .NET precedence — so a deployment's MAXIO_* variables always win.
        if (!string.IsNullOrWhiteSpace(value))
        {
            configuration[configKey] = value;
        }
    }
}