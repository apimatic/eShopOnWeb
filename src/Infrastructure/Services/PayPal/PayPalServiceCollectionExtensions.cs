using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Registers the PayPal payment gateway: binds <see cref="PayPalOptions"/> from the
/// <c>PayPal:</c> configuration section, constructs a long-lived <see cref="PayPalServerSdkClient"/>
/// with OAuth2 client-credentials, and wires <see cref="IPaymentGateway"/>.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalPaymentGateway(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalOptions.SectionName);
        var options = new PayPalOptions
        {
            ClientId = section["ClientId"] ?? string.Empty,
            ClientSecret = section["ClientSecret"] ?? string.Empty,
            Environment = section["Environment"] ?? string.Empty,
            Currency = section["Currency"] ?? string.Empty,
            BaseUrl = section["BaseUrl"]
        };

        // The options double as the app-level payment settings (currency).
        services.AddSingleton(options);
        services.AddSingleton<IPaymentSettings>(options);

        // The SDK client is a singleton, so the HttpClient it wraps is constructed once and reused
        // for the whole app lifetime (dotnet-client-initialization). PooledConnectionLifetime keeps
        // DNS fresh behind the long-lived client; Timeout bounds a single attempt.
        services.AddSingleton(_ => BuildClient(options));
        services.AddSingleton<IPaymentGateway, PayPalPaymentGateway>();

        return services;
    }

    private static PayPalServerSdkClient BuildClient(PayPalOptions options)
    {
        var httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var sdkOptions = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret
            }
        };

        // When BaseUrl is configured, override it verbatim. The SDK resolves both the API paths and
        // the /v1/oauth2/token request through this same base URL, so this one override drives EVERY
        // call including the token request.
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            sdkOptions.Server = new ServerOptions
            {
                Default = new DefaultOptions
                {
                    Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = options.BaseUrl! }
                }
            };
        }

        return new PayPalServerSdkClient(httpClient, sdkOptions);
    }
}
