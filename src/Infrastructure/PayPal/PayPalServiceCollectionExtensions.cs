using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));
        services.AddMemoryCache();
        services.AddTransient<PayPalAuthenticationHandler>();

        services.AddHttpClient<PayPalAccessTokenService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            client.BaseAddress = new Uri(AppendSlash(options.ResolveBaseUrl()));
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<IPayPalGateway, PayPalGateway>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            client.BaseAddress = new Uri(AppendSlash(options.ResolveBaseUrl()));
            client.Timeout = TimeSpan.FromSeconds(45);
        }).AddHttpMessageHandler<PayPalAuthenticationHandler>();

        return services;
    }

    private static string AppendSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
