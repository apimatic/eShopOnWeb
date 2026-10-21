using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Recipes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Per attempt, for reads and writes alike: above Maxio's own 120 s cut-off, so a slow write is
    /// answered by Maxio before the client gives up on it. Reads get a shorter whole-call deadline in
    /// <see cref="MaxioGateway"/>.
    /// </summary>
    public static readonly TimeSpan AttemptTimeout = SafeSignup.MinimumClientTimeout;

    // The Maxio sandbox credentials arrive as these environment variables. They are mapped onto the
    // Maxio: keys as the lowest-priority source, so user-secrets, Maxio__* variables and the command line
    // all override them.
    private static readonly IReadOnlyDictionary<string, string> EnvironmentVariableMap = new Dictionary<string, string>
    {
        ["MAXIO_API_KEY"] = "Maxio:ApiKey",
        ["MAXIO_SITE_SUBDOMAIN"] = "Maxio:Subdomain",
        ["MAXIO_DEFAULT_PRODUCT_FAMILY"] = "Maxio:ProductFamilyHandle",
        ["MAXIO_ENVIRONMENT"] = "Maxio:Environment",
    };

    /// <summary>
    /// Adds the <c>MAXIO_*</c> environment variables, when present, as the lowest-priority values of
    /// the <c>Maxio:</c> section.
    /// </summary>
    public static void AddMaxioEnvironmentVariableFallback(this IConfigurationBuilder configuration)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (variable, key) in EnvironmentVariableMap)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value)) continue;
            // MAXIO_ENVIRONMENT may name something other than a region; only a region is a value for Maxio:Environment.
            if (key == "Maxio:Environment" && !IsRegion(value)) continue;
            values[key] = value;
        }

        if (values.Count > 0)
            configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = values });
    }

    /// <summary>
    /// Registers the Maxio client and the subscription service. Validates the <c>Maxio:</c> section here,
    /// at registration, so a missing credential stops the host from starting.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var maxio = configuration.GetSection(MaxioOptions.SectionName).Get<MaxioOptions>() ?? new MaxioOptions();
        maxio.Validate();
        services.AddSingleton(maxio);

        services.AddHttpClient(HttpClientName, client => client.Timeout = AttemptTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton holding one HttpClient: recycle connections so DNS stays fresh.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = new MaxioAdvancedBillingClientOptions();
            ConfigureClient(options, maxio, sp.GetRequiredService<ILoggerFactory>());
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<MaxioGateway>();
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        services.AddHostedService<SubscriptionSignupReconciler>();
        return services;
    }

    /// <summary>Applies credentials, site, region, base URL, retry and logging settings to the SDK options.</summary>
    public static void ConfigureClient(MaxioAdvancedBillingClientOptions options, MaxioOptions maxio, ILoggerFactory loggerFactory)
    {
        maxio.Validate();

        options.BasicAuth = new BasicAuthCredentials { Username = maxio.ApiKey!.Trim(), Password = "x" };
        options.Environment = maxio.ResolveEnvironment();

        if (!string.IsNullOrWhiteSpace(maxio.Subdomain))
        {
            // Only the selected environment's Site is read, and each server group has its own: set all four.
            var site = maxio.Subdomain.Trim();
            options.Server.Production.Us.Site = options.Server.Production.Eu.Site = site;
            options.Server.Ebb.Us.Site = options.Server.Ebb.Eu.Site = site;
        }

        if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
        {
            // Maxio:BaseUrl is used verbatim, whatever the region.
            var baseUrl = maxio.BaseUrl.Trim();
            options.Server.Production.Us.BaseUrl = options.Server.Production.Eu.BaseUrl = baseUrl;
        }

        // No automatic resend of a PUT or of a 429 (Maxio asks for a pause of minutes); POST is never
        // resent. Both per-attempt timeouts at 130 s.
        options.Retry = RequestLimit.WithoutUnsafeRetries(RetryOptions.Default() with { Timeout = AttemptTimeout });

        // Assigned explicitly so the SDK's log environment variable cannot switch body logging on.
        // Customer create bodies carry names and e-mail addresses.
        options.Logging = new LoggingOptions
        {
            LoggerFactory = loggerFactory,
            LogRequestBody = false,
            LogRequestHeaders = false,
            LogResponseHeaders = false,
        };
    }

    private static bool IsRegion(string value) =>
        new[] { "US", "EU" }.Contains(value.Trim().ToUpperInvariant());
}
