using System;
using System.Net;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio client (a singleton: options are captured once, so a rotated key needs a restart)
    /// and the billing service. Throws at startup when a setting is missing.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = MaxioSettings.Load(configuration);

        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" };
            options.Environment = settings.IsEu ? ServerEnvironment.Eu : ServerEnvironment.Us;
            var site = settings.Subdomain!.Trim();
            if (settings.IsEu)
            {
                options.Server.Production.Eu.Site = site;
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = site;
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            // Only reads are resent by the SDK; writes (POST and PUT) are never retried.
            options.Retry = RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = TimeSpan.FromSeconds(10),
                HttpMethodsToRetry = [HttpMethod.Get, HttpMethod.Head],
            };
            options.Logging = options.Logging with { LogRequestBody = false };
        });

        services.AddScoped<MaxioBillingService>();
        return services;
    }
}
