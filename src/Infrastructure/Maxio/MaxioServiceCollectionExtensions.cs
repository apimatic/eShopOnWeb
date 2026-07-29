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
    /// Registers the Maxio Advanced Billing integration: binds the "Maxio" configuration section,
    /// validates it, and registers <see cref="IBillingService"/> as a typed HttpClient configured
    /// with the API base address and HTTP Basic authentication.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.ConfigurationSection);
        services.Configure<MaxioSettings>(section);

        // Settings are validated lazily on first use (see MaxioBillingService), so the host still
        // boots when the billing feature is not exercised (e.g. under the test harness). The typed
        // client uses absolute request URLs, so no BaseAddress is required here.
        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        // Maxio authenticates with HTTP Basic: API key as the username, literal "X" as the password.
        var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));

        services.AddHttpClient<IBillingService, MaxioBillingService>(client =>
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio-Integration/1.0");
            // Maxio enforces a 120s server-side cut-off; stay just under it.
            client.Timeout = TimeSpan.FromSeconds(110);
        });

        return services;
    }
}
