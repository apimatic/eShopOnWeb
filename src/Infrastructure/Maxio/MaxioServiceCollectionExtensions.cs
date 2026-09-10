using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration: binds <see cref="MaxioSettings"/>
    /// from the "Maxio" configuration section and wires <see cref="IMaxioBillingService"/> as a
    /// typed <see cref="System.Net.Http.HttpClient"/> with HTTP Basic auth and a retry handler.
    /// Registration never throws on missing configuration (so the host still boots for tests and
    /// non-billing scenarios); calls fail with a clear error only when Maxio is actually invoked.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind manually (no configuration-binder package dependency in this project) and
        // register as IOptions so the settings can be injected wherever needed.
        var section = configuration.GetSection(MaxioSettings.CONFIG_NAME);
        var settings = new MaxioSettings
        {
            ApiKey = section["ApiKey"],
            Subdomain = section["Subdomain"],
            ProductFamilyHandle = section["ProductFamilyHandle"],
            BaseUrl = section["BaseUrl"]
        };
        services.AddSingleton(Options.Create(settings));

        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioBillingService, MaxioBillingService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(100); // under Maxio's 120s server cutoff
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (settings.IsConfigured)
            {
                client.BaseAddress = settings.ResolveBaseUri();

                // HTTP Basic auth: API key as the username, "X" as the password.
                var raw = Encoding.ASCII.GetBytes($"{settings.ApiKey}:X");
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
            }
        })
        .AddHttpMessageHandler<MaxioRetryHandler>();

        return services;
    }
}
