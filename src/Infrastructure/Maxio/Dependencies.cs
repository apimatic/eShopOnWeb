using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class Dependencies
{
    /// <summary>
    /// Registers the Maxio (Advanced Billing) integration: options binding, the HTTP
    /// client, and the subscription service.
    /// </summary>
    public static void ConfigureMaxioServices(IConfiguration configuration, IServiceCollection services)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddHttpClient<IMaxioClient, MaxioClient>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
    }
}
