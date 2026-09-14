using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registration for the Maxio Advanced Billing integration: binds <see cref="MaxioSettings"/> from
/// the <c>Maxio</c> configuration section, wires the typed <see cref="MaxioApiClient"/> HttpClient
/// (base address + HTTP Basic auth), and exposes it as <see cref="IMaxioBillingService"/>.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);
        services.Configure<MaxioSettings>(section);

        // The HttpClient is configured lazily from bound options so the app still boots when Maxio is
        // not configured (e.g. the test host, which never calls these endpoints). When unconfigured,
        // the base address/auth are simply not set; MaxioBillingService fails fast with a clear
        // BillingException before any request is attempted.
        services.AddHttpClient<MaxioApiClient>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!settings.IsConfigured)
            {
                return;
            }

            client.BaseAddress = settings.ResolveBaseUri();

            // API key is the Basic-auth username; the password is a literal "X" per Maxio's API.
            var basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();

        return services;
    }
}
