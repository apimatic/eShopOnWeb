using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.SectionName));

        services.AddHttpClient<IPhoneNumberLookup, TwilioPhoneNumberLookup>((sp, client) =>
        {
            client.BaseAddress = new Uri("https://lookups.twilio.com");
            client.Timeout = TimeSpan.FromSeconds(30);
            ApplyBasicAuth(client, sp);
        });

        services.AddHttpClient<ISmsGateway, TwilioSmsGateway>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            client.BaseAddress = new Uri(options.ResolveMessagingBaseUrl());
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            ApplyBasicAuth(client, sp);
        });

        return services;
    }

    private static void ApplyBasicAuth(HttpClient client, IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }
}
