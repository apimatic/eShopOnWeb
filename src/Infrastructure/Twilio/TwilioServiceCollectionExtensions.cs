using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    private const string ClientName = "eShopOnWeb.Twilio";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetRequiredSection(TwilioSettings.SectionName))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.AccountSid), "Twilio:AccountSid is not configured.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.AuthToken), "Twilio:AuthToken is not configured.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.FromNumber), "Twilio:FromNumber is not configured.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.MessagingServiceSid), "Twilio:MessagingServiceSid is not configured.")
            .ValidateOnStart();

        services.AddSingleton<SingleAttemptWriteGuard>();
        services.AddTransient<SingleAttemptWriteHandler>();
        services.AddLogging(logging => logging.AddFilter(
            "System.Net.Http.HttpClient.eShopOnWeb.Twilio", LogLevel.None));
        services.AddHttpClient(ClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .AddHttpMessageHandler<SingleAttemptWriteHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TwilioSettings>>().Value;
            var options = new TwilioSdk.TwilioSdkClientOptions
            {
                Environment = TwilioSdk.Servers.ServerEnvironment.Production,
                Retry = TwilioSdk.Core.Configuration.RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(8)
                },
                AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
            return new TwilioSdk.TwilioSdkClient(httpClient, options);
        });

        services.AddSingleton<ITwilioMessageProvider, TwilioMessageProvider>();
        return services;
    }
}
