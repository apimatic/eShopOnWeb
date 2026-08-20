using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.PublicApi.Sms;

/// <summary>
/// Wires the Twilio-backed SMS notification feature: a long-lived <see cref="TwilioSdkClient"/> over a
/// dedicated named <c>HttpClient</c>, the gateway, and the application notification services. The host
/// binds and validates <see cref="TwilioSettings"/> (see the PublicApi startup) so a missing credential
/// refuses to boot.
/// </summary>
public static class SmsNotificationServiceExtensions
{
    private const string HttpClientName = "TwilioMessaging";

    public static IServiceCollection AddSmsNotifications(this IServiceCollection services)
    {
        // A dedicated, isolated pipeline: Timeout bounds one attempt (default 100s is an outage);
        // PooledConnectionLifetime keeps DNS fresh behind the long-lived singleton client. 60s leaves
        // headroom for a cold first call to the messaging host on a slow network.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(60))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                // Bound one attempt below HttpClient.Timeout; the whole-call budget is the request's token.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) }
            };

            // When set, the override is used verbatim for every messaging-API call (server node Default);
            // it deliberately does not touch the separate lookup host.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;

            // Twilio__LookupsBaseUrl overrides ONLY the Lookup host, verbatim.
            var lookupsBaseUrl = System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl");
            if (!string.IsNullOrEmpty(lookupsBaseUrl))
                options.Server.Default4.Production.BaseUrl = lookupsBaseUrl;

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddScoped<INotificationAdminService, NotificationAdminService>();

        return services;
    }
}
