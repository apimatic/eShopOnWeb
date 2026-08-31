using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class TwilioMessagingRegistration
{
    /// <summary>
    /// Registers the Twilio SDK client (singleton over a named, factory-managed HttpClient),
    /// the messaging gateway, and the order-notification orchestration service. Credentials
    /// are validated at startup — a missing secret refuses the boot rather than failing the
    /// first unlucky request.
    /// </summary>
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddTransient<SendOnceGuardHandler>();

        services.AddHttpClient(TwilioTextMessagingService.HttpClientName, client =>
            {
                // Bounds one attempt (the SDK applies it per attempt); the whole-call budget
                // lives in TwilioTextMessagingService.
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<SendOnceGuardHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Scoped to the messaging API node (api.twilio.com); Lookup keeps its own host.
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient(TwilioTextMessagingService.HttpClientName);
            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ITextMessagingService, TwilioTextMessagingService>();

        services.AddScoped<IOrderNotificationService>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var followUpDelay = TimeSpan.FromDays(settings.FollowUpDelayDays > 0 ? settings.FollowUpDelayDays : 3);
            return new OrderNotificationService(
                sp.GetRequiredService<IRepository<OrderNotification>>(),
                sp.GetRequiredService<IRepository<ContactNumber>>(),
                sp.GetRequiredService<ITextMessagingService>(),
                sp.GetRequiredService<IAppLogger<OrderNotificationService>>(),
                followUpDelay);
        });

        return services;
    }
}
