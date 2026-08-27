using System;
using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class MessagingDependencies
{
    private const string HttpClientName = "TwilioMessaging";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetRequiredSection(TwilioSettings.SectionName))
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.AccountSid) &&
                    !string.IsNullOrWhiteSpace(settings.AuthToken) &&
                    !string.IsNullOrWhiteSpace(settings.FromNumber) &&
                    !string.IsNullOrWhiteSpace(settings.MessagingServiceSid),
                "Twilio:AccountSid, Twilio:AuthToken, Twilio:FromNumber, and Twilio:MessagingServiceSid are required.")
            .Validate(
                settings => string.IsNullOrWhiteSpace(settings.BaseUrl) ||
                    Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp),
                "Twilio:BaseUrl must be an absolute HTTP or HTTPS URL when configured.")
            .ValidateOnStart();

        services.AddSingleton<ProviderWriteGuard>();
        services.AddTransient<ProviderWriteGuardHandler>();
        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.All
            })
            .AddHttpMessageHandler<ProviderWriteGuardHandler>();

        services.AddSingleton(serviceProvider =>
        {
            TwilioSettings settings = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TwilioSettings>>().Value;
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
                    MaxRetries = 2,
                    Timeout = TimeSpan.FromSeconds(5)
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            HttpClient httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new TwilioSdkClient(httpClient, options);
        });

        services.AddSingleton<IMessageProvider, TwilioMessageProvider>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddHostedService<ScheduledMessageCancellationWorker>();
        return services;
    }
}
