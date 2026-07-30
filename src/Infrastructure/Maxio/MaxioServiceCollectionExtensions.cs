using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing subscription integration: the typed HTTP client
    /// (with HTTP Basic auth, resilient retry, and a base address derived from configuration),
    /// and <see cref="ISubscriptionBillingService"/>. Settings are bound from the <c>Maxio:</c>
    /// section and validated at registration time so misconfiguration fails fast.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new MaxioSettings();
        configuration.GetSection(MaxioSettings.SectionName).Bind(settings);
        settings.Validate();

        services.AddSingleton(settings);
        services.AddSingleton<KeyedAsyncLock>();
        services.AddTransient<MaxioRetryHandler>();

        var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));

        services.AddHttpClient<MaxioClient>(client =>
            {
                client.BaseAddress = settings.ResolveBaseAddress();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                // Maxio enforces a 120s server-side cut-off; align the client timeout accordingly.
                client.Timeout = TimeSpan.FromSeconds(120);
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionService>();

        return services;
    }
}
