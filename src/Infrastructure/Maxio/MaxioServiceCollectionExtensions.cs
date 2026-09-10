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
/// Registers the Maxio-backed subscription billing capability: options binding + validation, the
/// typed Maxio HTTP client (base address + Basic auth), and the orchestration service.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName));

        // Validation runs the first time IOptions<MaxioOptions>.Value is resolved; the host triggers
        // this explicitly at startup (see the PublicApi Program) so misconfiguration fails fast.
        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();
        services.AddSingleton<SubscriberLockRegistry>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;

            client.BaseAddress = options.ResolveBaseAddress();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Maxio uses HTTP Basic auth with the API key as the username and a fixed "x" password.
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
