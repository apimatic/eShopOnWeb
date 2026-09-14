using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="MaxioOptions"/> from the "Maxio" configuration section and registers
    /// <see cref="IMaxioClient"/> as a typed HttpClient. Configuration values are expected to
    /// originate from user-secrets/environment variables, never hard-coded appsettings values.
    /// </summary>
    public static IServiceCollection AddMaxioClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.ConfigSectionName));
        services.AddHttpClient<IMaxioClient, MaxioClient>();
        return services;
    }
}
