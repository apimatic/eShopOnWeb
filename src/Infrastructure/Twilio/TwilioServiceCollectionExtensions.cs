using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioSmsGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.SectionName));
        services.AddHttpClient<ISmsGateway, TwilioSmsGateway>();
        services.AddSingleton<ISmsSendingNumber, TwilioSendingNumber>();
        return services;
    }
}
