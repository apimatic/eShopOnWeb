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
    internal const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing. Settings come from the <c>Maxio:</c>
    /// configuration section and are validated when the host starts.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.AddHttpClient(HttpClientName, client =>
            {
                // Backstop for one attempt; the whole operation is bounded by the service's cancellation budget.
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a long-lived singleton: recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are built once here, so a rotated API key takes effect on the next process restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings, sp.GetRequiredService<ILoggerFactory>()));
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddSingleton<MaxioPlanCatalog>();
        services.AddScoped<EnrollmentStore>();
        services.AddScoped<MaxioSubscriptionBillingService>();
        services.AddScoped<ISubscriptionBillingService>(sp => sp.GetRequiredService<MaxioSubscriptionBillingService>());
        services.AddHostedService<BillingReconciliationService>();

        return services;
    }

    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEuEnvironment ? ServerEnvironment.Eu : ServerEnvironment.Us,
            // Maxio Basic auth: the API key is the username and the password is the literal "x".
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
            // Defaults keep POST out of the retried verbs, so creates are never resent; reads retry twice.
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = TimeSpan.FromSeconds(10)
            },
            // Customer payloads carry personal data: never log bodies, and assign the factory explicitly so the
            // MAXIOADVANCEDBILLINGCLIENT_LOG environment variable cannot turn body logging on.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        if (settings.IsEuEnvironment)
        {
            if (settings.HasBaseUrlOverride)
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
            else
                options.Server.Production.Eu.Site = settings.Subdomain!.Trim();
        }
        else
        {
            if (settings.HasBaseUrlOverride)
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            else
                options.Server.Production.Us.Site = settings.Subdomain!.Trim();
        }

        return options;
    }
}
