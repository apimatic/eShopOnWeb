using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Bound from the "Maxio" configuration section.</summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (secret; user-secrets or environment only).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose plans are offered.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional. When set, used verbatim as the API base address instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Optional. Plan used by POST /api/subscriptions when the request names none.</summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>Collection method for new subscriptions: remittance (invoice, no card capture; default), automatic, prepaid or invoice (legacy sites).</summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Total budget for one inbound request's Maxio calls.</summary>
    public TimeSpan RequestBudget { get; set; } = TimeSpan.FromSeconds(30);
}
