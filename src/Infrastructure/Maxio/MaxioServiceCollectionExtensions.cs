using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetRequiredSection(MaxioOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey) && !string.IsNullOrWhiteSpace(options.Subdomain) &&
                                 !string.IsNullOrWhiteSpace(options.ProductFamilyHandle), "Maxio credentials and product family handle are required.")
            .ValidateOnStart();

        services.AddSingleton<MaxioWriteScope>();
        services.AddTransient<MaxioWriteRetryGuardHandler>();
        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            .AddHttpMessageHandler<MaxioWriteRetryGuardHandler>();

        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioOptions>>().Value;
            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials { Username = options.ApiKey, Password = "x" },
                Retry = RetryOptions.Default() with { MaxRetries = 1, Timeout = TimeSpan.FromSeconds(10) }
            };
            clientOptions.Server.Production.Us.Site = options.Subdomain;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName), clientOptions);
        });
        services.AddScoped<MaxioBillingGateway>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
