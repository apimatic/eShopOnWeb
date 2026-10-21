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
    /// <summary>Name of the <see cref="HttpClient"/> the Maxio SDK client is built on. Tests replace its
    /// primary handler to stub the provider.</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers Maxio Advanced Billing as the <see cref="ISubscriptionBillingService"/>. Settings bind from
    /// the <c>Maxio:</c> section and are validated when the host starts; a missing credential stops startup.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services)
    {
        services.AddOptions<MaxioOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                var section = configuration.GetSection(MaxioOptions.SectionName);
                options.ApiKey = section["ApiKey"];
                options.Subdomain = section["Subdomain"];
                options.ProductFamilyHandle = section["ProductFamilyHandle"];
                options.BaseUrl = section["BaseUrl"];
                options.Environment = section["Environment"];
            })
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>());

        services.AddHttpClient(HttpClientName, client =>
            {
                // Backstop per attempt; the SDK's own per-attempt timeout (below) fires first.
                client.Timeout = MaxioBillingDefaults.HttpClientTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton holding one HttpClient; recycle pooled connections so DNS
                // changes at the provider are picked up without a restart.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Built once, on first use, from options validated at startup. A rotated API key therefore takes
        // effect on the next (rolling) restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings, sp.GetRequiredService<ILoggerFactory>()));
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddScoped<BillingClaimStore>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                // Maxio's Basic scheme: the API key is the username and the password is the literal "x".
                Username = settings.ApiKey!.Trim(),
                Password = "x"
            },
            // Default retry policy (GET/HEAD/PUT/OPTIONS only — creates are POSTs and are never resent),
            // with a per-attempt timeout that fits inside the per-call budgets.
            Retry = RetryOptions.Default() with { Timeout = MaxioBillingDefaults.AttemptTimeout },
            Logging = new LoggingOptions
            {
                // Explicit factory: keeps the SDK's environment-variable switch (which can force unredacted
                // body logging) disarmed. Customer requests carry PII, so bodies are never logged.
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            var site = settings.Subdomain.Trim();
            options.Server.Production.Us.Site = site;
            options.Server.Production.Eu.Site = site;
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Verbatim override of the derived https://{site}... address.
            var baseUrl = settings.BaseUrl.Trim();
            options.Server.Production.Us.BaseUrl = baseUrl;
            options.Server.Production.Eu.BaseUrl = baseUrl;
        }

        return options;
    }
}

internal static class MaxioBillingDefaults
{
    /// <summary>Whole-call budget for a read endpoint (all SDK calls it makes, retries included).</summary>
    public static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(15);

    /// <summary>Whole-call budget for the subscribe endpoint.</summary>
    public static readonly TimeSpan WriteBudget = TimeSpan.FromSeconds(30);

    /// <summary>Fresh budget for re-reading the provider after a write whose outcome is unknown.</summary>
    public static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(15);

    /// <summary>How long a pending claim protects an unsettled write before another request may take it over.
    /// Far longer than <see cref="WriteBudget"/> + <see cref="SettleBudget"/>, so the original request is gone.</summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan PlanCacheDuration = TimeSpan.FromSeconds(60);

    public const int ProductsPerPage = 200;
    public const int MaxProductPages = 10;
}
