using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration: options binding and validation, the typed HTTP
/// client with authentication and retry, and the subscription capability built on top of them.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// The password Maxio expects alongside the API key, per the <c>BasicAuth</c> security scheme in
    /// the OpenAPI specification: "The `username` is a Maxio Chargify API key. The `password` is `x`."
    /// </summary>
    private const string ApiKeyPassword = "x";

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Validation runs the first time the options are resolved rather than at start-up, so a host
        // that never touches the subscription endpoints still boots without Maxio credentials.
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.ConfigurationSectionName));

        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();
        services.AddMemoryCache();
        services.AddTransient<MaxioTransientFaultHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;

                client.BaseAddress = options.ResolveBaseAddress();
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");

                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{options.ApiKey}:{ApiKeyPassword}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            })
            .AddHttpMessageHandler<MaxioTransientFaultHandler>();

        services.AddScoped<ISubscriptionBillingGateway, MaxioSubscriptionBillingGateway>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
