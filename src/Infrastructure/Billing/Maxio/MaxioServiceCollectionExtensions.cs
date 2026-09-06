using System;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Named client used for every call to the Maxio Advanced Billing API.</summary>
    public const string HttpClientName = "maxio";

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing, bound from the
    /// <c>Maxio:</c> configuration section.
    /// </summary>
    /// <remarks>
    /// Missing configuration does not stop the host from starting - the storefront's one-time
    /// commerce flow is independent of billing and should not be taken down by it. Instead the
    /// subscription endpoints fail fast with <c>503</c> and a message naming the missing keys, and
    /// a warning is logged at startup.
    /// </remarks>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<MaxioConcurrencyGate>();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddTransient<MaxioResilienceHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(HttpClientName, (serviceProvider, httpClient) =>
            {
                // The per-attempt timeout lives in MaxioResilienceHandler so that a retry gets a
                // full budget rather than whatever is left of the first attempt's.
                httpClient.Timeout = Timeout.InfiniteTimeSpan;
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio-Integration/1.0");

                var options = serviceProvider.GetRequiredService<IOptionsMonitor<MaxioOptions>>().CurrentValue;
                if (!options.IsConfigured)
                {
                    // Leave the client unusable rather than throwing here: resolving the client
                    // must not be what surfaces a configuration problem. MaxioSubscriptionService
                    // checks configuration first and raises a message that names the missing keys.
                    return;
                }

                httpClient.BaseAddress = options.ResolveBaseAddress();

                // Maxio authenticates with HTTP Basic: the API key as the username, a literal "X"
                // as the password.
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            })
            .AddHttpMessageHandler<MaxioResilienceHandler>();

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    /// <summary>
    /// Logs, once at startup, whether subscription billing is usable. Call this after the host is
    /// built so an operator finds out from the log rather than from the first shopper.
    /// </summary>
    public static void LogMaxioBillingStatus(this IServiceProvider services, ILogger logger)
    {
        var options = services.GetRequiredService<IOptionsMonitor<MaxioOptions>>().CurrentValue;

        if (!options.IsConfigured)
        {
            logger.LogWarning(
                "Maxio billing is NOT configured - subscription endpoints will answer 503. Provide {Keys}.",
                $"{MaxioOptions.SectionName}:ApiKey, {MaxioOptions.SectionName}:Subdomain, {MaxioOptions.SectionName}:ProductFamilyHandle");
            return;
        }

        // Deliberately logs the base address and family handle but never the API key.
        logger.LogInformation(
            "Maxio billing configured: base address {BaseAddress}, product family '{ProductFamilyHandle}'.",
            options.ResolveBaseAddress(), options.ProductFamilyHandle);
    }
}
