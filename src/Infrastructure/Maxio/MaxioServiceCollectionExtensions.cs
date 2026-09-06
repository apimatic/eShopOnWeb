using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing implementation of subscription billing.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Wires up <see cref="ISubscriptionBillingService"/> against Maxio using the <c>Maxio</c>
    /// configuration section.
    /// </summary>
    /// <remarks>
    /// Registration deliberately does not fail when the section is absent or incomplete. The host
    /// serves catalog, basket and order traffic that has nothing to do with billing, and taking the
    /// whole application down because one optional capability is unconfigured would be the wrong
    /// trade. Instead the first call to a subscription endpoint reports exactly which keys are
    /// missing, as a 503.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .Validate(
                s => string.IsNullOrWhiteSpace(s.PaymentCollectionMethod) is false,
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.PaymentCollectionMethod)}' cannot be blank.")
            .Validate(
                s => s.MaxAttempts >= 1,
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.MaxAttempts)}' must be at least 1.")
            .Validate(
                s => s.Timeout > TimeSpan.Zero,
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.Timeout)}' must be positive.");

        services.AddMemoryCache();
        services.AddSingleton<SubscriberGate>();
        services.AddTransient<MaxioTransientFaultHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((provider, client) =>
            {
                var settings = provider.GetRequiredService<IOptionsMonitor<MaxioSettings>>().CurrentValue;

                client.Timeout = settings.Timeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");

                if (!settings.IsConfigured)
                {
                    // Leave the client unusable rather than throwing here: resolving the service graph
                    // must not fail, and every entry point checks configuration before it makes a call.
                    return;
                }

                client.BaseAddress = settings.ResolveBaseAddress();

                // Maxio authenticates with HTTP Basic, API key as the user name and the literal "x"
                // as the password.
                var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey!.Trim()}:x"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
            })
            .AddHttpMessageHandler<MaxioTransientFaultHandler>();

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
