using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing, bound from the
    /// <c>Maxio</c> configuration section.
    /// <para>
    /// Registration deliberately never throws: an application without Maxio settings still starts and
    /// serves everything else, and the subscription endpoints report the missing keys by name when
    /// they are called. Supply the API key through user-secrets or the environment.
    /// </para>
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        // Shared across requests: site settings are cached, and per-shopper locks only serialise
        // enrollment if every request sees the same lock table.
        services.AddSingleton<MaxioSiteCache>();
        services.AddSingleton<SubscriberLocks>();

        services
            .AddHttpClient<IMaxioApiClient, MaxioApiClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;

                client.Timeout = options.Timeout > TimeSpan.Zero ? options.Timeout : TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // Leave the address and credentials unset when configuration is incomplete; the
                // billing service reports that as a clear "not configured" failure instead.
                if (options.Validate().Count > 0)
                {
                    return;
                }

                client.BaseAddress = options.ResolveBaseAddress();

                // Specification security scheme "BasicAuth": the user name is the API key and the
                // password is the literal "x".
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            })
            .AddHttpMessageHandler(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
                var logger = serviceProvider.GetRequiredService<ILogger<MaxioTransientFaultHandler>>();

                return new MaxioTransientFaultHandler(options.MaxRetries, logger);
            });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
