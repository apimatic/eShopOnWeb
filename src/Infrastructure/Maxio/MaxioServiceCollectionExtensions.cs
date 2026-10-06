using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration. Settings are bound from the
    /// "Maxio" configuration section (user-secrets / environment in practice);
    /// no values are hard-coded here.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.CONFIG_NAME));

        services.AddHttpClient(MaxioBillingService.HttpClientName)
            .ConfigureHttpClient((serviceProvider, httpClient) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
                ConfigureClient(options, httpClient);
            });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }

    private static void ConfigureClient(MaxioOptions options, HttpClient httpClient)
    {
        httpClient.BaseAddress = options.ResolveBaseUrl();
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Maxio Advanced Billing Basic auth: API key as username, "X" as password.
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ApiKey}:X"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
}