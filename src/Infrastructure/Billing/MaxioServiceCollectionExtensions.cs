using System;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing client. All settings are bound from the
    /// "Maxio" configuration section (Maxio:ApiKey, Maxio:Subdomain, Maxio:BaseUrl,
    /// Maxio:ProductFamilyHandle); secret values are never read from source control -
    /// supply them via environment variables or dotnet user-secrets.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SECTION_NAME);
        services.Configure<MaxioSettings>(section);

        services.AddHttpClient<IMaxioBillingGateway, MaxioBillingGateway>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            // Fail fast with an actionable message when configuration is missing; the
            // values themselves are never logged.
            settings.ValidateRequiredSettings();

            client.BaseAddress = new Uri(settings.ResolveBaseUrl().TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);

            // Spec security scheme: HTTP Basic, username = API key, password = "x".
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-MaxioIntegration/1.0");
        });

        return services;
    }
}
