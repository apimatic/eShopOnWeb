using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registration of the Maxio Advanced Billing integration: binds <see cref="MaxioSettings"/>
/// from the <c>Maxio:</c> configuration section, configures a typed <see cref="System.Net.Http.HttpClient"/>
/// with HTTP Basic auth (per the spec's BasicAuth security scheme), and registers the billing service.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddSingleton(Options.Create(settings));

        // The client factory invokes this configuration lazily, the first time the typed client
        // is resolved (i.e. when a subscription endpoint is actually called). Validating here
        // rather than at startup keeps the app (and unrelated tests) bootable without Maxio
        // configuration, while still failing fast with a clear message when billing is used.
        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(client =>
        {
            settings.Validate();

            client.BaseAddress = settings.ResolveBaseUri();

            // BasicAuth: username = API key, password = "x" (Maxio OpenAPI securityScheme).
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }
}
