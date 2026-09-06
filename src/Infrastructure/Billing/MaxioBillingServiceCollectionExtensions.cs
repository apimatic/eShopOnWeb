using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Authentication;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Registers subscription billing backed by Maxio Advanced Billing.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>Name of the <see cref="IHttpClientFactory"/> client used for Advanced Billing traffic.</summary>
    public const string HttpClientName = "Maxio.AdvancedBilling";

    /// <summary>
    /// Binds the <c>Maxio</c> configuration section and registers
    /// <see cref="ISubscriptionBillingService"/>.
    /// </summary>
    /// <remarks>
    /// Kept out of <see cref="Dependencies.ConfigureServices"/> on purpose: subscription billing is an
    /// additive capability exposed by PublicApi, and the storefront has no business opening a connection
    /// to the billing system just by starting up.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateDataAnnotations()
            // Fail at startup rather than on the first shopper who tries to subscribe.
            .ValidateOnStart();

        services.AddMemoryCache();

        services.AddSingleton<MaxioReferenceFactory>(sp =>
            new MaxioReferenceFactory(sp.GetRequiredService<IOptions<MaxioSettings>>().Value.ReferencePrefix));

        services.AddSingleton<SubscriberGate>();

        services.AddHttpClient(HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
                client.Timeout = settings.Timeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            // Order matters: the address is rewritten once, outside the retry loop. Rewriting inside it
            // would re-apply any path prefix on the base address to an already-rewritten URL on each retry.
            .AddHttpMessageHandler(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
                return new MaxioBaseAddressHandler(settings.ResolveBaseAddress());
            })
            .AddHttpMessageHandler(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
                return new MaxioResilienceHandler(
                    settings.MaxConcurrentRequests,
                    settings.MaxRetries,
                    settings.RetryBaseDelay,
                    sp.GetRequiredService<ILogger<MaxioResilienceHandler>>());
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            return new AdvancedBillingClient.Builder()
                // Advanced Billing takes the API key as the Basic user name with the literal password "x".
                .BasicAuthCredentials(new BasicAuthModel.Builder(settings.ApiKey, "x").Build())
                .Environment(settings.IsEuropeanEnvironment()
                    ? AdvancedBilling.Standard.Environment.EU
                    : AdvancedBilling.Standard.Environment.US)
                .Site(settings.Subdomain ?? string.Empty)

                // false: this pipeline already owns timeout, retry and concurrency, so the SDK should not
                // wrap a second, differently-tuned Polly policy around it.
                .HttpClientConfig(config => config.HttpClientInstance(httpClient, overrideHttpClientConfiguration: false))
                .Build();
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
