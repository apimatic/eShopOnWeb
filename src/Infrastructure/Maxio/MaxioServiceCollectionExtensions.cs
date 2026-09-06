using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing, bound from the "Maxio"
    /// configuration section.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The section is missing or incomplete. Failing at startup is deliberate: a half-configured
    /// billing integration should not reach the point of taking a shopper's subscribe request.
    /// </exception>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = new MaxioSettings();
        configuration.GetSection(MaxioSettings.ConfigurationSection).Bind(settings);

        var problems = settings.Validate();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Maxio subscription billing is not configured correctly:" +
                Environment.NewLine + " - " + string.Join(Environment.NewLine + " - ", problems));
        }

        services.AddSingleton(settings);
        services.AddMemoryCache();

        services.AddHttpClient<IBillingGateway, MaxioApiClient>(client =>
            {
                client.BaseAddress = settings.ResolveBaseAddress();
                client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

                // Maxio authenticates with HTTP Basic over TLS: the API key is the username and
                // the password is a literal "X".
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
            })
            .AddHttpMessageHandler(provider => new MaxioRetryHandler(
                settings.MaxRetryAttempts,
                provider.GetRequiredService<ILogger<MaxioRetryHandler>>()));

        services.AddSingleton<ISubscriptionOptions>(settings);
        services.AddSingleton<ISubscriberLock, SubscriberLock>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
