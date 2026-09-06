using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers recurring subscription billing backed by Maxio Advanced Billing.
    ///
    /// Settings are bound from the "Maxio" configuration section:
    ///   Maxio:ApiKey              - API key (HTTP Basic user name; keep it in user-secrets or the environment)
    ///   Maxio:Subdomain           - Advanced Billing site subdomain
    ///   Maxio:BaseUrl             - optional verbatim base URL override
    ///   Maxio:ProductFamilyHandle - product family whose products are offered as plans
    ///
    /// Missing configuration does not prevent the host from starting: subscription endpoints then
    /// fail fast with a clear "billing not configured" error while the rest of the app keeps working.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.ConfigurationSection));

        services.AddMemoryCache();
        services.AddTransient<MaxioTransientFaultHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            if (settings.IsConfigured)
            {
                client.BaseAddress = settings.ResolveBaseAddress();

                // maxio-spec: components.securitySchemes.BasicAuth - the user name is the API key,
                // the password is the literal "x".
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:x"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }
            else
            {
                // Placeholder so HttpClient construction never throws; requests fail fast in the
                // service layer with BillingConfigurationException before reaching the transport.
                client.BaseAddress = new Uri("https://billing.invalid/");
            }

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
            client.Timeout = settings.ResolveTimeout();
        })
        .AddHttpMessageHandler<MaxioTransientFaultHandler>();

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    /// <summary>
    /// Writes a single startup line describing how billing is configured - without ever emitting the
    /// API key - so a misconfigured deployment is obvious in the logs.
    /// </summary>
    public static void LogMaxioConfiguration(this IServiceProvider services, ILogger logger)
    {
        var settings = services.GetRequiredService<IOptions<MaxioSettings>>().Value;

        if (!settings.IsConfigured)
        {
            logger.LogWarning(
                "Maxio subscription billing is NOT configured (Maxio:ApiKey and Maxio:Subdomain/Maxio:BaseUrl are required). " +
                "Subscription endpoints will return 503 until it is.");
            return;
        }

        logger.LogInformation(
            "Maxio subscription billing configured: base address {BaseAddress}, product family {ProductFamily}",
            settings.ResolveBaseAddress(),
            string.IsNullOrWhiteSpace(settings.ProductFamilyHandle) ? "(all products on the site)" : settings.ProductFamilyHandle);
    }
}
