using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration: the <c>Maxio</c> settings binding, the
/// typed <see cref="IMaxioClient"/> HTTP client (base address + Basic auth), and the
/// <see cref="ISubscriptionService"/> orchestration.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    // Maxio enforces a 120-second request cut-off; allow a little headroom above that.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(130);

    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddHttpClient<IMaxioClient, MaxioClient>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                throw new InvalidOperationException(
                    "Maxio:ApiKey is not configured. Provide it via configuration or user-secrets.");
            }

            if (string.IsNullOrWhiteSpace(settings.Subdomain) && string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                throw new InvalidOperationException(
                    "Maxio requires either Maxio:Subdomain or Maxio:BaseUrl to be configured.");
            }

            var baseUrl = settings.ResolveBaseUrl();
            client.BaseAddress = new Uri(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/");

            // HTTP Basic Authentication: API key as the user name, "X" as the password.
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = RequestTimeout;
        });

        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
