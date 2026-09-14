using System;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class BillingServiceCollectionExtensions
{
    private const string UserAgent = "eShopOnWeb-Subscriptions/1.0";

    /// <summary>
    /// Registers recurring-subscription billing backed by Maxio Advanced Billing.
    /// </summary>
    /// <remarks>
    /// Registration never fails on missing credentials: the rest of the application must still
    /// start and serve the one-time commerce flow. The subscription endpoints report the capability
    /// as unavailable instead, with a message naming the configuration keys that are missing.
    /// </remarks>
    public static IServiceCollection AddSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.ConfigurationSectionName));

        services.AddMemoryCache();
        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<MaxioApiClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

            // Retries are budgeted per attempt inside MaxioRetryHandler.
            httpClient.Timeout = Timeout.InfiniteTimeSpan;

            if (!settings.IsConfigured)
            {
                return;
            }

            httpClient.BaseAddress = settings.ResolveBaseAddress();

            // HTTP Basic over TLS, with the API key as the user name and a literal "X" as the password.
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey!.Trim()}:X"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        })
        .AddHttpMessageHandler<MaxioRetryHandler>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<KeyedAsyncLock>();
        services.AddScoped<ISubscriptionBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
