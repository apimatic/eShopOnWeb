using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Constants;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the "Maxio" configuration section.
/// Secret values (ApiKey) must come from a secure store such as environment variables or user-secrets.
/// </summary>
public class MaxioSettings
{
    public const string SECTION_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The subdomain of the Maxio site, e.g. "mycompany". Used to derive the API base URL
    /// (https://{subdomain}.chargify.com) when <see cref="BaseUrl"/> is not specified.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the Maxio product family that contains the subscription plans offered by the shop.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, it is used verbatim as the Advanced Billing API base address
    /// instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException("Maxio settings are incomplete. Configure 'Maxio:BaseUrl' or 'Maxio:Subdomain' (and 'Maxio:ApiKey').");
        }

        return $"https://{Subdomain.Trim()}.chargify.com";
    }
}
