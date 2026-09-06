using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration that backs the recurring-subscription capability.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>Logical name of the typed <see cref="System.Net.Http.HttpClient"/> used for Maxio calls.</summary>
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName));

        // Deliberately not validated through the options pipeline: a deployment that does not enable
        // subscriptions must still be able to boot, and an OptionsValidationException at resolution time
        // would surface as an opaque 500. MaxioOptions.Validate() is checked at each use site instead, so
        // the endpoints can report missing configuration as a clear 503.

        services.AddMemoryCache();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddTransient<MaxioResilienceHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(HttpClientName, ConfigureClient)
            .AddHttpMessageHandler<MaxioResilienceHandler>()
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static void ConfigureClient(IServiceProvider serviceProvider, System.Net.Http.HttpClient client)
    {
        var options = serviceProvider.GetRequiredService<IOptionsMonitor<MaxioOptions>>().CurrentValue;

        var hasCredentials = !string.IsNullOrWhiteSpace(options.ApiKey);
        var hasAddress = !string.IsNullOrWhiteSpace(options.BaseUrl) || !string.IsNullOrWhiteSpace(options.Subdomain);

        if (hasCredentials && hasAddress)
        {
            client.BaseAddress = options.ResolveBaseAddress();

            // Specification security scheme "BasicAuth": the username is the API key, the password is "x".
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(options.ApiKey!.Trim() + ":x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");

        // Overall deadline for one logical call, retries included.
        client.Timeout = options.RequestTimeout > TimeSpan.Zero ? options.RequestTimeout : TimeSpan.FromSeconds(30);
    }
}
