using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.CONFIG_NAME);
        services.Configure<MaxioSettings>(section);
        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddMaxioAdvancedBillingClient(options =>
        {
            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                // Maxio basic auth: API key as username, "x" as password.
                options.BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" };
            }

            if (!string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                options.Server.Production.Us.Site = settings.Subdomain;
                options.Server.Production.Eu.Site = settings.Subdomain;
            }

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }
        });
        services.AddSingleton<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
