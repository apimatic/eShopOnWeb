using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioBilling";

    /// <summary>
    /// Registers the Maxio Advanced Billing client, the billing service and the
    /// current-user resolver. Configuration is validated when the billing service
    /// is first resolved, so hosts without Maxio configuration (e.g. the existing
    /// test suite) are unaffected until a billing endpoint is actually used.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton<IMaxioBillingService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            options.Validate();

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var client = MaxioClientFactory.Create(httpClient, options);
            return new MaxioBillingService(
                client,
                options,
                sp.GetRequiredService<ILogger<MaxioBillingService>>());
        });

        return services;
    }
}
