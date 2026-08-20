using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

internal static class MaxioConfiguration
{
    public static void AddMaxioBilling(this WebApplicationBuilder builder)
    {
        ApplyEnvironmentOverrides(builder.Configuration);

        builder.Services.Configure<MaxioOptions>(builder.Configuration.GetSection(MaxioOptions.SectionName));
        builder.Services.AddHttpClient<ISubscriptionBillingService, MaxioBillingService>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            client.BaseAddress = MaxioBaseUrl.Resolve(options, builder.Configuration["MAXIO_ENVIRONMENT"]);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
        });
    }

    private static void ApplyEnvironmentOverrides(ConfigurationManager configuration)
    {
        var overrides = new Dictionary<string, string?>();
        CopyIfPresent(configuration, overrides, "MAXIO_API_KEY", "Maxio:ApiKey");
        CopyIfPresent(configuration, overrides, "MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        CopyIfPresent(configuration, overrides, "MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        if (overrides.Count > 0)
        {
            configuration.AddInMemoryCollection(overrides);
        }
    }

    private static void CopyIfPresent(
        IConfiguration configuration,
        IDictionary<string, string?> overrides,
        string sourceKey,
        string destinationKey)
    {
        var value = configuration[sourceKey];
        if (!string.IsNullOrWhiteSpace(value))
        {
            overrides[destinationKey] = value;
        }
    }
}
