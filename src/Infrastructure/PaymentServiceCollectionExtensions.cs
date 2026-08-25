using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure;

public static class PaymentServiceCollectionExtensions
{
    public static void AddPayPalPaymentGateway(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalOptions.CONFIG_NAME);
        services.Configure<PayPalOptions>(section);
        var payPalOptions = section.Get<PayPalOptions>() ?? new PayPalOptions();

        services.AddPayPalServerSdkClient(options =>
        {
            options.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = payPalOptions.ClientId,
                ClientSecret = payPalOptions.ClientSecret
            };
            options.Environment = ServerEnvironment.Sandbox;

            if (!string.IsNullOrWhiteSpace(payPalOptions.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = payPalOptions.BaseUrl;
            }
        });

        services.AddScoped<IPaymentGatewayService, PayPalPaymentGatewayService>();
    }
}
