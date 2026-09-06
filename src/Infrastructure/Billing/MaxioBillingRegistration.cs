using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Wires Maxio Advanced Billing into the application's service container.
/// </summary>
public static class MaxioBillingRegistration
{
    /// <summary>Name of the dedicated <see cref="HttpClient"/> registration used by the SDK client.</summary>
    public const string HTTP_CLIENT_NAME = "Maxio";

    /// <summary>
    /// Maxio's Basic scheme takes the API key as the username and a literal placeholder as the password.
    /// </summary>
    private const string ApiKeyPasswordPlaceholder = "x";

    /// <summary>Bounds a single HTTP attempt. Left at the 100s default, a hung provider is an outage.</summary>
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(8);

    /// <summary>Backstop on one attempt inside the socket layer, including requests the SDK does not retry.</summary>
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Keeps DNS fresh behind the long-lived (singleton) SDK client, which takes its HttpClient from the
    /// factory once and would otherwise never see a rotated handler.
    /// </summary>
    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Two attempts, not four. Every extra attempt multiplies the worst case, and the whole point of the
    /// budget in <see cref="MaxioSubscriptionBillingService.OperationBudget"/> is that
    /// <c>attempts x AttemptTimeout + backoff</c> stays under it.
    /// </summary>
    private const int MaxRetries = 2;

    /// <summary>
    /// Registers <see cref="ISubscriptionBillingService"/>.
    /// <para>
    /// When the <c>Maxio:</c> section is incomplete this registers
    /// <see cref="UnconfiguredSubscriptionBillingService"/> instead of an SDK client, so a host without
    /// billing credentials still starts and the rest of the API keeps working.
    /// </para>
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new MaxioSettings();
        configuration.GetSection(MaxioSettings.CONFIG_SECTION).Bind(settings);
        services.AddSingleton(settings);

        if (!settings.IsConfigured)
        {
            services.AddSingleton<ISubscriptionBillingService, UnconfiguredSubscriptionBillingService>();
            return services;
        }

        // A named client, not the shared default one the SDK's own extension resolves: the timeout, the
        // primary handler and the write-once handler below are all specific to Maxio and must not change
        // behaviour for every other unnamed HttpClient consumer in the app.
        services.AddTransient<MaxioWriteOnceHandler>();
        services.AddHttpClient(HTTP_CLIENT_NAME, client => client.Timeout = HttpClientTimeout)
            .AddHttpMessageHandler<MaxioWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = PooledConnectionLifetime
            });

        services.AddSingleton(serviceProvider =>
        {
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings));
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    /// <summary>
    /// Builds the SDK options. The client snapshots the environment and the auth pipeline when it is
    /// constructed, so everything has to be right here — in particular, leaving <c>BasicAuth</c> null
    /// sends no <c>Authorization</c> header at all and every call fails 401 with no client-side error.
    /// </summary>
    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey ?? string.Empty,
                Password = ApiKeyPasswordPlaceholder
            },
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = MaxRetries,
                Timeout = AttemptTimeout
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain;
        }

        // An explicit base address wins outright. A URL with no {site} placeholder is used verbatim,
        // which is also how a non-US Maxio site is targeted without a second configuration key.
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }

        return options;
    }
}
