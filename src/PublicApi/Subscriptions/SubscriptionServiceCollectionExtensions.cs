using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public static class SubscriptionServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioSubscriptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetRequiredSection(MaxioOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options =>
            {
                try
                {
                    _ = options.GetBaseUri();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }, "Maxio:BaseUrl must be an absolute HTTP or HTTPS URL.")
            .ValidateOnStart();

        services.AddHttpClient<IMaxioClient, MaxioClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            client.BaseAddress = options.GetBaseUri();
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        });

        services.AddSingleton<SubscriptionKeyedLock>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        return services;
    }
}
