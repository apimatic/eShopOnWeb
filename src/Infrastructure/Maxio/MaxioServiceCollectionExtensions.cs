using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the dedicated HttpClient this integration owns.</summary>
    public const string HttpClientName = "Maxio";

    /// <summary>
    /// Registers Maxio Advanced Billing as the subscription billing provider, bound from the
    /// <c>Maxio</c> configuration section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The SDK ships its own <c>AddMaxioAdvancedBillingClient</c> helper, which builds a singleton client
    /// over the <em>default, unnamed</em> factory client. This registers the same shape over a
    /// <em>named</em> one instead, for two reasons: the per-attempt timeout and the message handlers below
    /// would otherwise apply to every other unnamed HttpClient consumer in the process, and the send guard
    /// must not sit in anyone else's pipeline.
    /// </para>
    /// <para>
    /// Missing configuration does not stop the host — subscriptions are an additive capability and should
    /// not take the rest of the API down. It surfaces the first time the billing client is resolved, as a
    /// <see cref="BillingException"/> naming the settings that are missing.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMaxioBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioSettings>(
            configuration.GetSection(MaxioSettings.ConfigurationSectionName));

        services.AddTransient<MaxioSingleSendGuardHandler>();
        services.AddTransient<MaxioWireLoggingHandler>();

        services.AddHttpClient(HttpClientName, (serviceProvider, httpClient) =>
            {
                var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

                // Bounds a single attempt, not the whole call. Its expiry is not retried, so this is what
                // stops a hung provider from pinning a request thread; the call budget lives in the service.
                httpClient.Timeout = settings.RequestTimeout;
            })
            .AddHttpMessageHandler<MaxioSingleSendGuardHandler>()
            .AddHttpMessageHandler<MaxioWireLoggingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The client below is a singleton, so it never picks up the factory's handler rotation.
                // Without this, a DNS change would be cached for the life of the process.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            var problems = settings.Validate();
            if (problems.Count > 0)
            {
                throw new BillingException(
                    BillingFailureKind.NotConfigured,
                    "Subscription billing is not configured for this deployment.",
                    problems,
                    null);
            }

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    // Maxio authenticates the API key as the Basic user name; the password is a fixed
                    // placeholder rather than a secret.
                    Username = settings.ApiKey!.Trim(),
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    // The provider's floor is 1 attempt beyond the first; writes are kept safe by the
                    // send guard rather than by trying to switch retries off.
                    MaxRetries = Math.Max(1, settings.MaxRetries),
                    Timeout = settings.RequestTimeout
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Used verbatim: a value with no {site} placeholder is left exactly as configured.
                options.Server.Production.Us.BaseUrl = settings.BaseUrl.Trim();
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!.Trim();
            }

            var httpClient = serviceProvider
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient(HttpClientName);

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<MaxioSubscribeGate>();
        services.AddSingleton<MaxioSiteProvider>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
