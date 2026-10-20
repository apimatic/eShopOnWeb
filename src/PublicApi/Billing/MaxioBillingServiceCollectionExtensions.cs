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

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>Name of the HttpClient the Maxio SDK client is built on (kept off the shared default client).</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers Maxio subscription billing: validated <c>Maxio:</c> settings (startup fails on a missing or blank
    /// value), a singleton SDK client over a named HttpClient, the gateway, the claim store and the subscription service.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>());

        services.TryAddSingleton(new MaxioCallPolicy());
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName, (sp, http) =>
            {
                // Backstop above the SDK's per-attempt timeout; the whole-call bound is the gateway's CallBudget token.
                http.Timeout = sp.GetRequiredService<MaxioCallPolicy>().CallBudget + TimeSpan.FromSeconds(2);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton holding this HttpClient: recycle pooled connections so DNS changes are seen.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.TryAddSingleton(sp =>
        {
            // Built once: a rotated secret takes effect on process restart.
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var policy = sp.GetRequiredService<MaxioCallPolicy>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return CreateClient(httpClient, settings, policy, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>());
        });

        services.AddScoped<MaxioBillingGateway>();
        services.AddScoped<IBillingClaimStore, BillingClaimStore>();
        services.AddScoped<SubscriptionService>();

        return services;
    }

    /// <summary>Builds the SDK client from validated settings. Also used by tests with a stubbed HttpClient.</summary>
    public static MaxioAdvancedBillingClient CreateClient(
        HttpClient httpClient, MaxioSettings settings, MaxioCallPolicy policy, ILoggerFactory loggerFactory, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            // HTTP Basic: username = API key, password = the literal "x".
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
            Environment = ServerEnvironment.Us,
            // Default retry verbs (GET/HEAD/PUT/OPTIONS) are kept: POST (CreateCustomer/CreateSubscription) is never resent.
            Retry = RetryOptions.Default() with
            {
                MaxRetries = policy.MaxRetries,
                Timeout = policy.AttemptTimeout,
            },
            // Explicit factory: disables the MAXIOADVANCEDBILLINGCLIENT_LOG override; bodies (customer PII) never logged.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
            },
        };
        if (timeProvider is not null)
        {
            options.TimeProvider = timeProvider;
        }

        if (settings.HasBaseUrlOverride)
        {
            // Maxio:BaseUrl is used verbatim instead of the subdomain-derived address.
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                throw new InvalidOperationException("Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).");
            }
            options.Server.Production.Us.Site = settings.Subdomain!;
            options.Server.Production.Eu.Site = settings.Subdomain!;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
