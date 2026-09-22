using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

public static class SmsNotificationServiceExtensions
{
    /// <summary>
    /// Registers the Twilio-backed SMS order-notification feature: bound + fail-fast-validated
    /// settings, the Twilio SDK client (singleton), the provider, and the application services.
    /// Call this on the PublicApi host only.
    /// </summary>
    public static IServiceCollection AddSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind and validate on start: the host refuses to boot if any credential is missing/blank,
        // rather than surfacing it as a 401 on the first call. See TwilioSettingsValidator.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TwilioSettings>, TwilioSettingsValidator>();

        // Read once, at registration, and capture in the client singleton (a rotated secret needs a
        // process restart). Secrets come from configuration (env → user-secrets), never from code.
        var settings = configuration.GetSection(TwilioSettings.SectionName).Get<TwilioSettings>() ?? new TwilioSettings();

        services.AddTwilioSdkClient(options =>
        {
            options.Environment = ServerEnvironment.Production;
            options.AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = settings.AccountSid,
                Password = settings.AuthToken
            };

            // Twilio:BaseUrl overrides ONLY the messaging API (the Default server group: create,
            // read, list). Lookups (Default4) keeps its own host, as required.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl!;
            }

            // Per-attempt timeout (the whole-call budget is enforced with a CancellationToken in the
            // provider). POST writes are not retried by the SDK, so a rejected/failed send is not resent.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) };

            // Request bodies carry the recipient number and message text (PII) — never log them.
            // LoggerFactory is filled from the container by AddTwilioSdkClient, which disables the
            // TWILIOSDKCLIENT_LOG environment variable from switching body logging on.
            options.Logging = new LoggingOptions
            {
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            };
        });

        services.AddSingleton<ISmsProvider, TwilioSmsProvider>();
        services.AddScoped<IResendIdempotencyStore, Data.ResendIdempotencyStore>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
