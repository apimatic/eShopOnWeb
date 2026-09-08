using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="MaxioOptions"/> from the <c>Maxio</c> configuration section and
    /// registers the Maxio HTTP client + <see cref="MaxioBillingService"/>.
    /// </summary>
    public static IServiceCollection AddMaxioServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddHttpClient(MaxioBillingService.HttpClientName);

        services.AddSingleton<IMaxioBillingService, MaxioBillingService>();

        return services;
    }
}
