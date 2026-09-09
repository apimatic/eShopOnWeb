using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing SDK client and the
/// <see cref="IMaxioBillingService"/> implementation. The client is built over
/// a named <see cref="System.Net.Http.HttpClient"/> so its timeout, handler
/// lifetime and any future delegating handlers stay scoped to this integration
/// instead of the shared default factory client.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "Maxio.AdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services)
    {
        services.AddHttpClient(HttpClientName, httpClient =>
            {
                // Bounds one attempt; a hung provider gives way in ~10s instead
                // of the 100s default. The whole call is bounded separately in
                // MaxioBillingService.
                httpClient.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                // The billing service is a singleton holding one HttpClient; keep
                // pooled connections rotating so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton<IMaxioBillingService, MaxioBillingService>();
        return services;
    }
}
