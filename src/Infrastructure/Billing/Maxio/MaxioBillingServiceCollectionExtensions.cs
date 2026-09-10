using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Registration for the Maxio-backed subscription billing capability. This is additive and
/// self-contained: call it from a host that exposes the capability (the PublicApi).
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(settings =>
            {
                settings.Validate();
                return true;
            }, "Maxio settings are invalid.")
            .ValidateOnStart();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            client.BaseAddress = settings.ResolveBaseUrl();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Maxio auth: HTTP Basic with the API key as username and the literal "x" as password.
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
