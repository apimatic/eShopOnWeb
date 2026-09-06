using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    public const string HttpClientName = "Maxio";

    /// <summary>
    /// Binds the <c>Maxio:</c> configuration section and wires up the billing integration.
    /// <para>
    /// Configuration is validated lazily rather than at startup, on purpose: the host must still
    /// boot on a machine with no Maxio credentials — for the existing tests, and so the rest of
    /// the storefront keeps working — and the subscription endpoints answer 503 with the specific
    /// missing keys instead of the process refusing to start.
    /// </para>
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(HttpClientName, (provider, client) =>
            {
                var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

                // An unconfigured section must not blow up client construction; the service layer
                // reports the missing keys as a 503 when a request actually needs them.
                if (!string.IsNullOrWhiteSpace(settings.Subdomain) || !string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    client.BaseAddress = settings.ResolveBaseAddress();
                }

                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    // Advanced Billing authenticates with HTTP Basic: the API key is the user name
                    // and the password is the literal "x".
                    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                }

                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
