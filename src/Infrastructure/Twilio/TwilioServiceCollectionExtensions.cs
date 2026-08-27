using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    /// <summary>
    /// Binds the Twilio: configuration section and registers the Twilio-backed
    /// messaging provider and phone number validator.
    /// </summary>
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName));

        services.AddHttpClient<IMessageProvider, TwilioMessageProvider>();
        services.AddHttpClient<IPhoneNumberValidator, TwilioPhoneNumberValidator>();

        return services;
    }
}
