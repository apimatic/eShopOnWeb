using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Settings bound from the <c>Maxio</c> configuration section. No value is ever defaulted to a
/// literal from any particular billing site - the same build has to run against a different site
/// and a different catalog purely by changing configuration.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>
    /// The Maxio API key. Supply it through user-secrets, environment variables or a secret store -
    /// never a file in the repository.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, used to derive the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim, instead of an
    /// address derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// The configuration keys that are required but absent. Returns key *names* only - a value from
    /// this section is never echoed into a log or an HTTP response.
    /// </summary>
    public IReadOnlyList<string> MissingSettings()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            missing.Add(CONFIG_NAME + ":" + nameof(ApiKey));
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            missing.Add(CONFIG_NAME + ":" + nameof(Subdomain));
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            missing.Add(CONFIG_NAME + ":" + nameof(ProductFamilyHandle));
        }

        return missing;
    }

    public bool IsConfigured => MissingSettings().Count == 0;
}
