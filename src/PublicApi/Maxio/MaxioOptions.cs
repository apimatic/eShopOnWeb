namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings that describe how to reach the Maxio Advanced Billing (Chargify) site and which
/// product family holds the subscription plans surfaced to shoppers.
/// Bound from the "Maxio" configuration section; values are supplied through the
/// MAXIO_* environment variables (see Program.cs) or user secrets - never from repository files.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_SECTION_NAME = "Maxio";

    /// <summary>
    /// The Maxio site API key. HTTP Basic auth uses it as the username and the literal "x" as
    /// the password (see https://ahshaikh-mintlify-deploy.mintlify.site/introduction/authentication).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain (e.g. "cp-exp-8").</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that contains the subscribable plans (e.g. "eshop-subscribe").
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional absolute API base URL. When set it is used verbatim instead of deriving
    /// https://{Subdomain}.chargify.com.
    /// </summary>
    public string? BaseUrl { get; set; }
}
