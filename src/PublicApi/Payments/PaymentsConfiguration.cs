using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Wires up the PayPal gateway and the payment/saved-card application services.</summary>
public static class PaymentsConfiguration
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(PayPalOptions.SectionName).Get<PayPalOptions>() ?? new PayPalOptions();
        services.AddSingleton(options);

        services.AddHttpClient(PayPalClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        // Singleton: the client caches an OAuth2 token across requests.
        services.AddSingleton<IPayPalGateway, PayPalClient>();

        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();

        return services;
    }
}

/// <summary>Helpers for reading the authenticated shopper's identity from the JWT.</summary>
public static class CurrentUser
{
    /// <summary>The buyer id (username/email) from the token; this is what orders and cards are scoped by.</summary>
    public static string BuyerId(ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            throw new UnauthorizedAccessException("The request is not associated with an authenticated user.");
        }
        return buyerId;
    }
}
