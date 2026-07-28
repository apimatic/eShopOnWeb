using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registration for the Maxio billing integration: binds <see cref="MaxioSettings"/> from the
/// "Maxio" configuration section, configures the typed HTTP client (base address + HTTP Basic
/// auth from the spec), and wires up <see cref="IMaxioBillingService"/>.
/// </summary>
public static class MaxioServiceExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);
        services.Configure<MaxioSettings>(section);

        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();
        services.AddSingleton(settings);

        services.AddHttpClient<MaxioApiClient>(client =>
        {
            client.BaseAddress = new Uri(settings.ResolveBaseUrl());
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // HTTP Basic auth per the spec: api_key as the username, the literal "x" as the password.
            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();

        return services;
    }
}
