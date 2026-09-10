using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public static class MaxioServiceRegistration
{
    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription integration service.
    /// Credentials are bound from the <c>Maxio:</c> configuration section and validated at
    /// startup (the host refuses to boot if a required value is missing or blank).
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.SectionName);

        // Fail-fast: bind + validate before the app serves any request.
        services.AddOptions<MaxioSettings>()
            .Bind(section)
            .ValidateDataAnnotations() // [Required] ApiKey, ProductFamilyHandle (blank/whitespace rejected)
            .Validate(s => !string.IsNullOrWhiteSpace(s.BaseUrl) || !string.IsNullOrWhiteSpace(s.Subdomain),
                "Maxio:Subdomain is required when Maxio:BaseUrl is not set.")
            .ValidateOnStart();

        // Read once at registration to configure the SDK client (options are captured in the
        // singleton, so a rotated secret takes effect only on process restart).
        var settings = section.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.Environment = ServerEnvironment.Us; // Basic auth ⇒ US/EU only; sandbox is a US chargify site
            options.BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" };

            // Per-attempt timeout (writes are POST ⇒ never resent by the SDK; the whole-call
            // deadline is enforced by the service via a linked CancellationToken).
            options.Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.PerAttemptTimeoutSeconds))
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Verbatim override (no {site} token ⇒ used as-is).
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
            else if (!string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                // Derive https://{site}.chargify.com from the subdomain.
                options.Server.Production.Us.Site = settings.Subdomain;
            }
            // Logging.LoggerFactory is filled from the container by AddMaxioAdvancedBillingClient
            // (non-null) ⇒ the MAXIOADVANCEDBILLINGCLIENT_LOG env var cannot force request-body
            // logging on; LogRequestBody stays off (default).
        });

        services.AddSingleton<IMaxioSubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
