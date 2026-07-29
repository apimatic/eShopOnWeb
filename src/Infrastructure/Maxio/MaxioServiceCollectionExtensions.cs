using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio subscription-billing integration: binds <see cref="MaxioSettings"/>
    /// from the <c>Maxio:</c> configuration section and wires up a typed <see cref="System.Net.Http.HttpClient"/>
    /// pre-configured with the resolved base address, HTTP Basic authentication, and a
    /// transient-error retry policy.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));
        services.AddTransient<MaxioTransientErrorHandler>();

        // Configuration is validated and applied lazily, when the typed client is first
        // created (i.e. on the first subscription request), rather than at host startup.
        // This keeps hosts that never touch the billing endpoints — such as the PublicApi
        // integration-test host — bootable without Maxio settings, while still failing
        // fast and clearly the moment the integration is actually exercised.
        services.AddHttpClient<IMaxioSubscriptionService, MaxioSubscriptionService>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            settings.Validate();

            client.BaseAddress = settings.ResolveBaseAddress();

            // Maxio enforces a 120s server-side cut-off; keep the client aligned.
            client.Timeout = TimeSpan.FromSeconds(120);

            // HTTP Basic auth: API key as the username, "X" as the password.
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        })
        .AddHttpMessageHandler<MaxioTransientErrorHandler>();

        return services;
    }
}
