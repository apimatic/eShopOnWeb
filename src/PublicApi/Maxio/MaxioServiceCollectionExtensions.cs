using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription billing
    /// boundary. Credentials and site settings are read from the <c>Maxio</c>
    /// configuration section; no value is hard-coded. When <c>Maxio:BaseUrl</c>
    /// is set it is used verbatim as the API base address; otherwise the base
    /// address is derived from <c>Maxio:Subdomain</c>.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            Validate(options);

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(options));
        });

        services.AddSingleton<IMaxioBillingService, MaxioBillingService>();

        return services;
    }

    private static void Validate(MaxioOptions options)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey)) missing.Add(MaxioOptions.SectionName + ":" + nameof(options.ApiKey));
        if (string.IsNullOrWhiteSpace(options.Subdomain)) missing.Add(MaxioOptions.SectionName + ":" + nameof(options.Subdomain));
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle)) missing.Add(MaxioOptions.SectionName + ":" + nameof(options.ProductFamilyHandle));
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "The Maxio billing integration is not configured. Missing required settings: " +
                string.Join(", ", missing) +
                $". In development, load them with dotnet user-secrets on project {typeof(MaxioOptions).Assembly.GetName().Name}.");
        }
    }

    private static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions options)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(10)
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = options.ApiKey,
                Password = "x"
            }
        };

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
        }
        else
        {
            clientOptions.Server.Production.Us.Site = options.Subdomain;
        }

        return clientOptions;
    }
}
