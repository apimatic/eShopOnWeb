using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Bound from the "Maxio" configuration section.</summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Optional. Plan used when a subscribe request names none. Must be a listed plan.</summary>
    public string? DefaultPlanHandle { get; set; }

    public int PerAttemptTimeoutSeconds { get; set; } = 10;
    public int RequestBudgetSeconds { get; set; } = 25;
    public int MaxPlanPages { get; set; } = 5;
    public int StaleClaimSeconds { get; set; } = 120;

    /// <summary>Collection method for plans that do not require a payment method (remittance or invoice, by site architecture).</summary>
    public string NoPaymentMethodCollection { get; set; } = "remittance";

    public void Validate()
    {
        var missing = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add("Maxio:ApiKey");
        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl)) missing.Add("Maxio:Subdomain");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) missing.Add("Maxio:ProductFamilyHandle");
        if (missing.Count > 0)
            throw new InvalidOperationException("Missing required configuration: " + string.Join(", ", missing));
        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute URL when set.");
    }
}
