using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioDependencies
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration.
    /// Configuration is bound from the "Maxio" section (user-secrets / environment variables).
    /// </summary>
    public static IServiceCollection AddMaxio(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName));

        services.AddHttpClient<IMaxioClient, MaxioClient>();
        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}