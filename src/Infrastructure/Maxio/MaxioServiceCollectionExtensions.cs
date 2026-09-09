using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration: settings binding, a typed HttpClient with Basic
/// auth and transient-fault retries, and the <see cref="IMaxioBillingService"/> orchestration service.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddTransient<MaxioRetryHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((serviceProvider, client) =>
            {
                var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;

                // Fail with a clear, actionable message if the deployment is missing Maxio configuration.
                settings.Validate();

                client.BaseAddress = settings.ResolveBaseUrl();

                // Maxio enforces a 120s cut-off; stay just under it.
                client.Timeout = TimeSpan.FromSeconds(100);

                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // HTTP Basic auth: API key as the username, "X" as the password.
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            })
            .AddHttpMessageHandler<MaxioRetryHandler>();

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();

        return services;
    }
}
