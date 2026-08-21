using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MaxioSettings>>().Value);

        services.AddTransient<MaxioRetryHandler>();
        services.AddHttpClient<IMaxioBillingGateway, MaxioBillingGateway>((sp, client) =>
            {
                var settings = sp.GetRequiredService<MaxioSettings>();
                ConfigureClient(client, settings);
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }

    internal static void ConfigureClient(HttpClient client, MaxioSettings settings)
    {
        var baseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl) || !string.IsNullOrWhiteSpace(settings.Subdomain)
            ? settings.GetApiBaseUrl()
            : "https://invalid.chargify.com";

        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio/1.0");

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }
}
