namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing (Billing API) integration. Values are bound
/// from the "Maxio" configuration section; secrets must come from user-secrets or
/// environment variables, never from files inside the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Maxio API key, used as the Basic-auth username (password is a literal "X").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain, e.g. "cp-exp-4" for https://cp-exp-4.chargify.com.
    /// Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that contains the subscription plans offered to shoppers.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim override for the API base address (e.g. an API Gateway URL).
    /// When set, it is used instead of the URL derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
