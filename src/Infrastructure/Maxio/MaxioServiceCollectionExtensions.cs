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
    /// Registers Maxio Advanced Billing: binds and validates <see cref="MaxioSettings"/> from the
    /// <c>Maxio</c> configuration section and wires <see cref="IMaxioBillingService"/> onto a typed,
    /// pre-authenticated <see cref="System.Net.Http.HttpClient"/>.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey), "Maxio:ApiKey is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Subdomain), "Maxio:Subdomain is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.")
            .Validate(s => TryResolveBaseAddress(s), "Maxio:BaseUrl must be a valid absolute URL when provided.")
            .ValidateOnStart();

        services.AddHttpClient<IMaxioBillingService, MaxioBillingService>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            client.BaseAddress = settings.ResolveBaseAddress();

            // Maxio uses HTTP Basic auth: API key as the username, the literal "X" as the password.
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Maxio enforces a 120s server-side cut-off; stay just under it.
            client.Timeout = TimeSpan.FromSeconds(110);
        });

        return services;
    }

    private static bool TryResolveBaseAddress(MaxioSettings settings)
    {
        try
        {
            _ = settings.ResolveBaseAddress();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
