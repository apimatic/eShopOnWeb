using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing, bound from the
    /// <c>Maxio</c> configuration section (<c>Maxio:ApiKey</c>, <c>Maxio:Subdomain</c>,
    /// <c>Maxio:ProductFamilyHandle</c> and the optional <c>Maxio:BaseUrl</c> override).
    /// Configuration is validated at start-up, so a missing key fails the host rather than the
    /// first shopper.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioOptions.SectionName);

        // Billing runs alongside the one-time commerce flow rather than underneath it. When no
        // credentials are supplied (a fresh clone, a test host) the API still starts; the three
        // subscription endpoints answer 503 with instructions instead of the host refusing to boot.
        if (string.IsNullOrWhiteSpace(section[nameof(MaxioOptions.ApiKey)]))
        {
            services.AddScoped<ISubscriptionBillingService, UnconfiguredSubscriptionBillingService>();
            return services;
        }

        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();
        services.AddMemoryCache();
        services.AddSingleton<KeyedAsyncLock>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;

                client.BaseAddress = options.ResolveBaseAddress();
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

                // HTTP Basic over TLS with the API key as the user name and "X" as the password,
                // as documented in the Billing API authentication guide.
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ApiKey}:X"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
            })
            .AddHttpMessageHandler(provider =>
            {
                var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;
                var logger = provider.GetRequiredService<ILogger<MaxioResilienceHandler>>();
                return new MaxioResilienceHandler(logger, options.MaxRetries);
            });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
