using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration: the SDK client (auth + server), the billing
/// service, and the current-shopper resolver. Additive — it does not touch the existing commerce flow.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);
        services.Configure<MaxioSettings>(section);
        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        // Register the SDK client. Credentials/site come from configuration (user-secrets / env), never
        // from source. Missing values do not fail startup — the billing service reports a clear error on
        // first use — so the rest of the app (and its tests) boot without Maxio secrets present.
        services.AddMaxioAdvancedBillingClient(options =>
        {
            // HTTP Basic: API key is the username, the literal "x" is the password (SDK auth convention).
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            };
            options.Environment = ServerEnvironment.Us;
            options.Server.Production.Us.Site = settings.Subdomain;

            // Optional explicit base-URL override; otherwise the URL is derived from the subdomain.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            // The SDK cannot disable retries entirely (floor is 1). Hold it at the floor so a transport
            // failure resends a non-idempotent write (CreateCustomer/CreateSubscription) at most once; the
            // billing service additionally reconciles after a failure and guards against double-submits.
            options.Retry = RetryOptions.Default() with { MaxRetries = 1 };
        });

        // The SDK's DI extension resolves the default IHttpClientFactory client. Give that client's primary
        // handler a bounded pooled-connection lifetime so DNS/handler rotation applies even if the SDK
        // client is registered as a singleton (a singleton caches one HttpClient for the process lifetime).
        services.AddHttpClient(Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentShopperService, CurrentShopperService>();
        services.AddScoped<IMaxioBillingService, MaxioBillingService>();

        return services;
    }
}
