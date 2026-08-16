using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Registers the PayPal-backed <see cref="IPaymentGateway"/> and the underlying SDK client.
/// </summary>
public static class PaymentGatewayServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalPaymentGateway(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Bind the options POCO so PayPalPaymentGateway can read Currency (and the rest) at runtime.
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        // Read the credentials/base-URL once at registration time (the SDK DI callback captures
        // configuration once and may not resolve scoped services). Never hard-code any credential.
        var clientId = configuration["PayPal:ClientId"] ?? "";
        var clientSecret = configuration["PayPal:ClientSecret"] ?? "";
        var baseUrl = configuration["PayPal:BaseUrl"];

        // Use the SDK's own DI helper so the HttpClient lifetime is managed by IHttpClientFactory.
        services.AddPayPalServerSdkClient(o =>
        {
            // This SDK exposes exactly one environment (Sandbox); there is no Production constant.
            o.Environment = ServerEnvironment.Sandbox;

            o.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
            };

            // When a base-URL override is supplied it must govern ALL calls (including the OAuth2
            // token request), so set it on the selected environment's server node.
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                o.Server.Default.Sandbox.BaseUrl = baseUrl;
            }
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        return services;
    }
}
