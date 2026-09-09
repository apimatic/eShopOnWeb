using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio-backed subscription-billing capability: binds <see cref="MaxioSettings"/> from the
    /// "Maxio" configuration section, configures a typed <see cref="MaxioClient"/> (base URL + HTTP Basic auth),
    /// and exposes it through <see cref="ISubscriptionBillingService"/>.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));
        services.AddSingleton<KeyedAsyncLock>();

        services.AddHttpClient<IMaxioClient, MaxioClient>((provider, http) =>
        {
            var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            http.BaseAddress = settings.ResolveBaseAddress();
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Maxio uses HTTP Basic auth: the API key is the username, the password is the literal "x".
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }
}
