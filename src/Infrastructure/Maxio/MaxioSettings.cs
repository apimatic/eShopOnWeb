using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Bound from the "Maxio:" configuration section. No value is hard-coded anywhere.</summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Secret: user-secrets / environment only. Never in appsettings*.json.</summary>
    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    /// <summary>Optional. When set it is used verbatim as the API base address instead of deriving one from the subdomain.</summary>
    public string? BaseUrl { get; set; }
    /// <summary>Optional. Plan used by POST /api/subscriptions when the request names none.</summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// How Maxio collects payment for new subscriptions. This flow captures no card, so the default is "remittance"
    /// (invoiced); "automatic" would need a payment profile on file. Optional key: Maxio:PaymentCollectionMethod.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Whole-request budget for all provider calls of one operation.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Per-attempt budget for a single HTTP call.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public static string? Validate(MaxioSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.ApiKey))
            return "Maxio:ApiKey is not configured (set it via user-secrets or the MAXIO_API_KEY environment variable).";
        if (string.IsNullOrWhiteSpace(s.ProductFamilyHandle))
            return "Maxio:ProductFamilyHandle is not configured.";
        if (string.IsNullOrWhiteSpace(s.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(s.Subdomain))
                return "Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).";
        }
        else if (!Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "Maxio:BaseUrl must be an absolute http(s) URL.";
        }
        if (!MaxioAdvancedBilling.Models.Enums.CollectionMethod.TryGetKnownValue(s.PaymentCollectionMethod ?? string.Empty, out _))
            return "Maxio:PaymentCollectionMethod is not a known collection method (e.g. remittance, automatic, prepaid, invoice).";
        return null;
    }
}
