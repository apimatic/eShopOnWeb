using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Registers the PayPal integration: binds <see cref="PayPalSettings"/>, builds a long-lived
/// <see cref="PayPalServerSdkClient"/> over a named, factory-managed <see cref="HttpClient"/>, and wires the
/// gateway. The <c>PayPal:BaseUrl</c> override (when set) is applied to
/// <c>Server.Default.Sandbox.BaseUrl</c>, which the SDK uses for every call — including the OAuth token
/// request (the token endpoint is resolved from the same base).
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.SectionName);
        services.Configure<PayPalSettings>(section);
        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();

        // Named HttpClient keeps this pipeline off the shared default client. Timeout bounds a single
        // attempt (a hang ends there); PooledConnectionLifetime keeps DNS fresh behind the singleton below.
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(serviceProvider =>
        {
            var httpClient = serviceProvider
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret,
                },
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Used verbatim as the base for every call, including the OAuth token request.
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();

        return services;
    }
}
