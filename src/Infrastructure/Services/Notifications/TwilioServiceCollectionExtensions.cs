using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services.Notifications;

/// <summary>
/// DI wiring for the Twilio SMS gateway. The auth token is read straight from configuration into the SDK
/// client here and never placed on the injectable <see cref="TwilioSettings"/> object, written to a file,
/// or logged.
/// </summary>
public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioSms(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        services.AddTwilioSdkClient(o =>
        {
            // Basic auth: AccountSid as username, AuthToken as password. The token is read here only —
            // it is intentionally absent from TwilioSettings.
            o.AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = configuration["Twilio:AccountSid"]!,
                Password = configuration["Twilio:AuthToken"]!
            };

            o.Environment = ServerEnvironment.Production;

            // Disable the SDK's built-in HTTP request/response logging entirely. The SDK's HttpLogger
            // logs the request line (verb + URL) at Information whenever its resolved ILoggerFactory has
            // Information enabled — and the Lookup GET carries the shopper's phone number in the URL path,
            // so it would land in application logs. LoggingOptions has no on/off flag or log-level for the
            // request line (LogRequestHeaders/LogResponseHeaders/LogRequestBody only gate headers/body and
            // already default to false); the only full off-switch is a no-op logger factory. It must be
            // NON-NULL: leaving LoggerFactory null makes AddTwilioSdkClient backfill the app's
            // ILoggerFactory (via `?? sp.GetService<ILoggerFactory>()`), which re-enables the logging.
            o.Logging = o.Logging with { LoggerFactory = NullLoggerFactory.Instance };

            // Override the messaging (2010-04-01 "api") server base URL only when explicitly configured,
            // and use the configured value verbatim.
            var baseUrl = configuration["Twilio:BaseUrl"];
            if (!string.IsNullOrEmpty(baseUrl))
            {
                o.Server.Default.Production.BaseUrl = baseUrl;
            }
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();

        return services;
    }
}
