using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration: binds <see cref="MaxioSettings"/>
    /// from the "Maxio" configuration section, configures a typed HttpClient (Basic auth,
    /// base address, retry/backoff) and the <see cref="ISubscriptionBillingService"/>.
    /// Fails fast at startup if required settings are missing.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.CONFIG_SECTION);
        services.Configure<MaxioSettings>(section);

        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();
        settings.Validate();

        var baseAddress = settings.ResolveBaseAddress();
        var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));

        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioClient, MaxioClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(30);
            // Maxio authenticates with HTTP Basic: API key as username, literal "x" as password.
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-MaxioIntegration/1.0");
        })
        .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
