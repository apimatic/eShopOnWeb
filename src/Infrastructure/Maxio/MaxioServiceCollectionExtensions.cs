using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds the "Maxio" configuration section and registers a typed HttpClient for the
    /// Maxio Advanced Billing API. Per the OpenAPI spec, authentication is HTTP Basic with
    /// the API key as username and "x" as password, and the server URL is templated from
    /// the site subdomain (unless Maxio:BaseUrl overrides it).
    /// </summary>
    public static IServiceCollection AddMaxio(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(settings =>
            {
                try
                {
                    settings.Validate();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }, "Maxio settings are missing or invalid. Set Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl) via user-secrets or environment variables.")
            .ValidateOnStart();

        services.AddHttpClient<IMaxioClient, MaxioClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            client.BaseAddress = settings.GetBaseAddress();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x")));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();

        return services;
    }
}
