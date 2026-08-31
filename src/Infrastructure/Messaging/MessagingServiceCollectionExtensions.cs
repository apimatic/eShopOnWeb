using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    private const string HttpClientName = "TwilioSdk";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt; the provider wrapper owns the whole-call budget.
                client.Timeout = TimeSpan.FromSeconds(10);
            })
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
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10)
                },
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Messaging-API calls resolve through server slot "Default"; overriding only
                // that slot's BaseUrl redirects every messaging call and nothing else
                // (e.g. Lookup keeps its own host).
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<IMessageProvider, TwilioMessageProvider>();

        return services;
    }
}
