using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddTransient<MaxioAuthenticationHandler>();
        services.AddHttpClient<ISubscriptionBillingService, MaxioBillingService>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
                if (!options.IsConfigured)
                {
                    return;
                }

                var region = configuration["MAXIO_ENVIRONMENT"];
                client.BaseAddress = MaxioBillingService.ToHttpClientBaseAddress(options.ResolveApiBaseUrl(region));
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            })
            .AddHttpMessageHandler<MaxioAuthenticationHandler>();

        return services;
    }
}
