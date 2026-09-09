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
    /// Registers the Maxio-backed subscription billing services. Settings are bound from the
    /// "Maxio" configuration section (Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle,
    /// and optional Maxio:BaseUrl), which are supplied via user-secrets / environment variables.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddHttpClient<IMaxioClient, MaxioClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            settings.Validate();

            httpClient.BaseAddress = settings.ResolveBaseUri();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            // HTTP Basic auth: API key as username, literal "x" as the (ignored) password.
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    /// <summary>
    /// Validates the bound Maxio settings, throwing a descriptive error at startup if any
    /// required value is missing. Call after <see cref="AddMaxioSubscriptions"/> has run.
    /// </summary>
    public static void ValidateMaxioSettings(this IServiceProvider serviceProvider)
    {
        var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
        settings.Validate();
    }
}
