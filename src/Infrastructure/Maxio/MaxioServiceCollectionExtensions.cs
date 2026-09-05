using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing subscription integration. Reads its settings from the
    /// "Maxio" configuration section (ApiKey, Subdomain, ProductFamilyHandle, BaseUrl) — bind these
    /// via user-secrets/environment variables, never via a file committed to source control.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection("Maxio"));

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            client.BaseAddress = new Uri(options.ResolveBaseUrl());
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        });

        services.AddSingleton<IUserOperationLock, UserOperationLock>();
        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
