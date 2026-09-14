using Microsoft.eShopWeb.MaxioBilling.Configuration;
using Microsoft.eShopWeb.MaxioBilling.Interfaces;
using Microsoft.eShopWeb.MaxioBilling.Internal;
using Microsoft.eShopWeb.MaxioBilling.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.MaxioBilling;

/// <summary>Registers Maxio-backed subscription billing.</summary>
public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Binds the <c>Maxio</c> configuration section and registers
    /// <see cref="ISubscriptionBillingService"/>.
    /// <para>
    /// Registration never throws on missing credentials: the host must still start without them, and
    /// the subscription endpoints report the misconfiguration instead.
    /// </para>
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MaxioBillingOptions>(configuration.GetSection(MaxioBillingOptions.SectionName));

        var settings = configuration.GetSection(MaxioBillingOptions.SectionName).Get<MaxioBillingOptions>()
                       ?? new MaxioBillingOptions();

        services.AddSingleton<SingleSendHandler>();

        // A named client, not the shared unnamed one: the timeout, the primary handler and the
        // single-send guard below belong to Maxio only and must not change every other consumer.
        services.AddHttpClient(MaxioClientAccessor.HttpClientName, client =>
            {
                // Bounds one attempt. The SDK's per-attempt timeout is set alongside it, and the
                // whole-call budget is a linked cancellation token in the service.
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds));
            })
            .AddHttpMessageHandler<SingleSendHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton and holds this HttpClient for the process lifetime,
                // so connection (and therefore DNS) refresh has to be forced here.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddMemoryCache();
        services.AddSingleton<SubscriberLocks>();
        services.AddSingleton<MaxioClientAccessor>();
        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
