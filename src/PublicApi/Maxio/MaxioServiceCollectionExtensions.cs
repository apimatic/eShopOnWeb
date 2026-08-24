using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds the "Maxio" configuration section and registers the typed Maxio API client.
    /// Base address comes from Maxio:BaseUrl when set, otherwise https://{Maxio:Subdomain}.chargify.com
    /// (the US production server template from the OpenAPI spec).
    /// </summary>
    public static IServiceCollection AddMaxio(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName));

        services.AddHttpClient<IMaxioClient, MaxioClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            settings.Validate();

            client.BaseAddress = settings.GetBaseAddress();
            // Spec securitySchemes.BasicAuth: "The username is a Maxio Chargify API key. The password is x."
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
