using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Composition root for the Maxio Advanced Billing integration: binds settings from the
/// "Maxio" configuration section and wires a typed <see cref="System.Net.Http.HttpClient"/>
/// with the correct base address and Basic authentication.
/// </summary>
public static class MaxioSubscriptionBillingExtensions
{
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.ConfigSection).Get<MaxioSettings>()
                       ?? new MaxioSettings();
        settings.Validate();

        services.AddSingleton(settings);
        services.AddSingleton<MaxioSubscriptionCoordinator>();

        // Basic auth: API key as the username, the literal "x" as the password.
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        var baseUri = settings.ResolveBaseUri();

        services.AddHttpClient<ISubscriptionBillingService, MaxioBillingService>(client =>
        {
            client.BaseAddress = baseUri;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
