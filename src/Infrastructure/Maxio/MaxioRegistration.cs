using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioRegistration
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration: options bound from
    /// the "Maxio" configuration section, a typed HTTP client, and the
    /// billing provider. Configuration values come from user-secrets and/or
    /// the MAXIO_* environment variables — never from committed files.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MaxioOptions>>().Value);

        services.AddHttpClient<ISubscriptionBillingProvider, MaxioClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            client.BaseAddress = new Uri(options.ResolveBaseUrl() + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
