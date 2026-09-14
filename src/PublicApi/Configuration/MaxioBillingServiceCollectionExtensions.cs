using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Registers the Maxio Advanced Billing subscription integration: validated settings and a typed
/// <see cref="System.Net.Http.HttpClient"/> pre-configured with the API base address and HTTP Basic
/// authorization.
/// </summary>
public static class MaxioBillingServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind and validate settings up-front so a misconfigured deployment fails fast at startup
        // rather than on the first billing request.
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<ISubscriptionBillingService, MaxioSubscriptionBillingService>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            client.BaseAddress = settings.ResolveBaseUri();

            // Maxio Advanced Billing uses HTTP Basic auth: API key as the username, literal "x" as the password.
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
