using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioSms(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));
        services.AddHttpClient<ISmsProvider, TwilioSmsProvider>();
        return services;
    }
}
