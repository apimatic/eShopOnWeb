using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Registers subscription billing backed by Maxio Advanced Billing.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "Maxio";

    /// <summary>
    /// Binds the <c>Maxio</c> configuration section and registers
    /// <see cref="ISubscriptionBillingService"/>.
    /// </summary>
    /// <remarks>
    /// Settings come from configuration only — <c>Maxio:ApiKey</c>, <c>Maxio:Subdomain</c>,
    /// <c>Maxio:ProductFamilyHandle</c> and the optional <c>Maxio:BaseUrl</c>. Supply the API key
    /// through user-secrets, environment variables or a vault; never through a file in the repository.
    /// Configuration is validated at startup so a misconfigured host fails immediately instead of on
    /// the first shopper request.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(MaxioOptions.SectionName);

        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();

        var optionsBuilder = services.AddOptions<MaxioOptions>().Bind(section);

        // Validate eagerly when the host is configured for Maxio, so a bad subdomain or product
        // family is caught at startup rather than by the first shopper. When no API key is present
        // the host still starts — the rest of eShopOnWeb does not depend on billing — and the
        // subscription endpoints report the misconfiguration when they are called.
        if (!string.IsNullOrWhiteSpace(section[nameof(MaxioOptions.ApiKey)]))
        {
            optionsBuilder.ValidateOnStart();
        }

        services.AddMemoryCache();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>(HttpClientName, (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;

                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio-Integration/1.0");

                // Advanced Billing authenticates with HTTP basic auth: the API key is the user name
                // and the password is the literal "x".
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
