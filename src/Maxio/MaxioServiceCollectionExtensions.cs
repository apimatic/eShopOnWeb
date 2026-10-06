using System.Net.Http.Headers;
using System.Text;
using Maxio.Configuration;
using Maxio.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration services.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Binds the "Maxio" configuration section and registers the Maxio API client and subscription service.
    /// </summary>
    public static IServiceCollection AddMaxio(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioOptions.SectionName);
        services.Configure<MaxioOptions>(section);

        services.AddHttpClient<IMaxioClient, MaxioClient>((sp, httpClient) =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;

            var apiKey = options.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Maxio:ApiKey is not configured. Set MAXIO_API_KEY.");
            }

            httpClient.BaseAddress = new Uri(MaxioClient.ResolveBaseUrl(options));
            httpClient.Timeout = TimeSpan.FromSeconds(60);

            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
