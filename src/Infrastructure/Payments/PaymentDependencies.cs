using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Registers the PayPal integration: strongly-typed settings, the SDK client (over a dedicated, bounded
/// HttpClient), the gateway, and the payment orchestration service.
/// </summary>
public static class PaymentDependencies
{
    private const string HttpClientName = "PayPal";

    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        var section = configuration.GetSection(PayPalSettings.CONFIG_NAME);
        services.Configure<PayPalSettings>(section);
        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();

        var wireLog = string.Equals(section["WireLog"], "true", StringComparison.OrdinalIgnoreCase);

        // A dedicated, bounded HttpClient keeps this pipeline (timeout, handler rotation) off the shared
        // default client. Timeout bounds one attempt; PooledConnectionLifetime keeps DNS fresh behind the
        // long-lived (singleton) SDK client.
        var httpClientBuilder = services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        if (wireLog)
        {
            services.AddTransient<PayPalResponseLoggingHandler>();
            httpClientBuilder.AddHttpMessageHandler<PayPalResponseLoggingHandler>();
        }

        services.AddSingleton(serviceProvider =>
        {
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new PayPalServerSdkClient(httpClient, BuildClientOptions(settings));
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IPaymentService, PaymentService>();
    }

    private static PayPalServerSdkClientOptions BuildClientOptions(PayPalSettings settings)
    {
        var options = new PayPalServerSdkClientOptions
        {
            // The SDK exposes only the Sandbox environment; a non-sandbox target is reached via BaseUrl.
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret
            }
        };

        // When PayPal:BaseUrl is set, use it verbatim as the base address for every call — including the
        // OAuth token request, which resolves through this same Sandbox.BaseUrl.
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server = new ServerOptions
            {
                Default = new DefaultOptions
                {
                    Sandbox = new DefaultOptions.SandboxOptions
                    {
                        BaseUrl = settings.BaseUrl
                    }
                }
            };
        }

        return options;
    }
}
