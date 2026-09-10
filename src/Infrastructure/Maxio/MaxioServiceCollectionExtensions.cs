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
    /// Registers the Maxio subscription integration: binds <see cref="MaxioSettings"/> from the
    /// <c>Maxio:</c> configuration section, configures a typed <see cref="MaxioApiClient"/> with HTTP
    /// Basic auth and the resolved base address, and exposes <see cref="IMaxioSubscriptionService"/>.
    /// </summary>
    /// <remarks>
    /// Configuration is bound eagerly but only *validated* when the client is first used, so the host
    /// still boots (and unrelated endpoints keep working) when Maxio settings are absent — e.g. in test
    /// hosts that never exercise the subscription endpoints.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<MaxioApiClient>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            settings.Validate();

            client.BaseAddress = settings.ResolveBaseUri();
            client.Timeout = TimeSpan.FromSeconds(30);

            // HTTP Basic per the spec: username = API key, password = literal "x".
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio-Integration/1.0");
        })
        .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
