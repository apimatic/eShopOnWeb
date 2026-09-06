using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class BillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the recurring-subscription capability backed by Maxio Advanced Billing.
    /// </summary>
    /// <remarks>
    /// Settings are bound from the "Maxio" configuration section - <c>Maxio:ApiKey</c>,
    /// <c>Maxio:Subdomain</c>, <c>Maxio:ProductFamilyHandle</c> and the optional
    /// <c>Maxio:BaseUrl</c> - so the same build runs against any Maxio site and catalogue.
    /// Nothing here has a baked-in default that points at a particular site.
    /// Options are validated on first use rather than at startup, so an eShopOnWeb
    /// deployment without billing configured still serves the rest of the API.
    /// </remarks>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName));

        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();

        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IBillingGateway, MaxioBillingGateway>((serviceProvider, httpClient) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;

                httpClient.BaseAddress = options.ResolveBaseAddress();
                httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

                // Advanced Billing authenticates with HTTP basic auth: the API key as the
                // user name and the literal "x" as the password.
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ApiKey}:x"));
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("eShopOnWeb", "1.0"));
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddSingleton<KeyedAsyncLock>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
