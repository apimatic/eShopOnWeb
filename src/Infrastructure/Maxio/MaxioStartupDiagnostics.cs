using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Reports at startup whether subscription billing is usable.
/// </summary>
/// <remarks>
/// Startup is not failed on missing billing configuration, so that a deployment which does not use
/// subscriptions still runs. This turns the alternative failure mode — endpoints answering 503 for
/// reasons nobody sees — into one line in the log, without ever printing the API key.
/// </remarks>
public static class MaxioStartupDiagnostics
{
    public static void Log(IServiceProvider services, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        var settings = services.GetService<IOptions<MaxioSettings>>()?.Value;

        if (settings is null || !settings.IsConfigured)
        {
            logger.LogWarning(
                "Subscription billing is NOT configured; /api/subscription-plans, /api/subscriptions and " +
                "/api/my-subscriptions will answer 503. Set {SectionName}:ApiKey, {SectionName}:Subdomain " +
                "and {SectionName}:ProductFamilyHandle (or the MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN and " +
                "MAXIO_DEFAULT_PRODUCT_FAMILY environment variables).",
                MaxioSettings.SectionName,
                MaxioSettings.SectionName,
                MaxioSettings.SectionName);
            return;
        }

        logger.LogInformation(
            "Subscription billing configured against Maxio {BaseAddress} (environment {Environment}, product family {ProductFamilyHandle}, collection method {PaymentCollectionMethod}).",
            settings.ResolveBaseAddress(),
            settings.Environment,
            settings.ProductFamilyHandle,
            settings.PaymentCollectionMethod);
    }
}
