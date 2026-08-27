using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioDependencies
{
    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetRequiredSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations()
            .Validate(settings => string.IsNullOrWhiteSpace(settings.BaseUrl) ||
                                  Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _),
                "Twilio:BaseUrl must be an absolute URL when configured.")
            .ValidateOnStart();

        services.AddTransient<TwilioWriteOnceHandler>();
        services.AddHttpClient("TwilioMessaging", client => client.Timeout = TimeSpan.FromSeconds(10))
            .AddHttpMessageHandler<TwilioWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var options = new TwilioSdk.TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(8)
                },
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                }
            };
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("TwilioMessaging");
            return new TwilioSdk.TwilioSdkClient(httpClient, options);
        });

        services.AddSingleton<ISmsProvider, TwilioSmsProvider>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddHostedService<NotificationCancellationWorker>();
    }
}
