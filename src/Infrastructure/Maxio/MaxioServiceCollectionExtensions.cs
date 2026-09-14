using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio-backed <see cref="ISubscriptionBillingService"/> together
    /// with a configured, resilient <see cref="System.Net.Http.HttpClient"/>.
    /// Settings are bound from the "Maxio" configuration section and validated at
    /// startup so misconfiguration fails fast.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);
        services.Configure<MaxioSettings>(section);

        // Settings are validated lazily (when the billing service is first used),
        // not at startup: this is an additive capability and the storefront must be
        // able to run even when Maxio is not configured.
        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<ISubscriptionBillingService, MaxioBillingService>(client =>
        {
            if (settings.IsConfigured)
            {
                client.BaseAddress = settings.ResolveBaseUrl();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X")));
            }

            // Stay under Maxio's 120s server-side cutoff.
            client.Timeout = TimeSpan.FromSeconds(100);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio-Integration/1.0");
        })
        .AddHttpMessageHandler<MaxioRetryHandler>();

        return services;
    }
}
