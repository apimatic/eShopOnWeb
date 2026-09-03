using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Core.Authentication.Basic;
using Twilio.Core.Configuration;
using Twilio.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Twilio SMS integration: strongly-typed settings (validated at startup), the Twilio
    /// client (Basic auth, a bounded per-attempt timeout, and the messaging base-URL override), the SMS
    /// gateway, and the order-notification orchestration service.
    /// </summary>
    public static IServiceCollection AddTwilioSms(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(TwilioSettings.SectionName);

        // Fail-fast: every required part is checked (a blank part is not a missing one) and ValidateOnStart
        // makes the host refuse to start when any is missing or blank, rather than discovering it as a 401
        // on the first message. The message names the config key but never echoes a value.
        services.AddOptions<TwilioSettings>()
            .Bind(section)
            .Validate(s => !string.IsNullOrWhiteSpace(s.AccountSid), "Twilio:AccountSid is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.AuthToken), "Twilio:AuthToken is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.FromNumber), "Twilio:FromNumber is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.MessagingServiceSid), "Twilio:MessagingServiceSid is not configured.")
            .ValidateOnStart();

        // The gateway takes the validated settings object directly.
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<TwilioSettings>>().Value);

        // Options are captured once here at registration (the SDK builds the client once and holds it in the
        // singleton) — a rotated secret takes effect on process restart. Values come from configuration/
        // user-secrets; none are hard-coded.
        var settings = section.Get<TwilioSettings>() ?? new TwilioSettings();
        services.AddTwilioClient(options =>
        {
            options.Environment = ServerEnvironment.Production;
            options.AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = settings.AccountSid,
                Password = settings.AuthToken
            };

            // Per-attempt timeout (default is 100s). A whole-request deadline is enforced separately via a
            // linked CancellationToken at the API boundary.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) };

            // Disable the SDK's built-in request/response logger: it logs the request URL, and the URL PATH
            // is not redacted — a Lookups request carries the shopper's number in the path, so leaving the
            // logger on would write that number to the logs. Assigning LoggerFactory explicitly also disarms
            // the TWILIOCLIENT_LOG environment variable (which could otherwise force unredacted body logging
            // on from outside the code). Observability comes from the application's own structured logs,
            // which never carry a number or message body. LogRequestBody stays off regardless.
            options.Logging = new LoggingOptions
            {
                LoggerFactory = NullLoggerFactory.Instance,
                LogRequestBody = false
            };

            // When Twilio:BaseUrl is set, use it verbatim as the base address for every messaging-API call
            // (the messaging operations resolve through the Default server group). Other Twilio hosts
            // (e.g. Lookups, on a different group) are deliberately left on their defaults.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }
        });

        services.AddSingleton<ISmsGateway, TwilioSmsGateway>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
