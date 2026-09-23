using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio:</c> section.
/// The values themselves are supplied out-of-band (environment variables loaded into .NET
/// user-secrets); this type only names the keys and enforces that the required ones are present.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Chargify API key. Used as the Basic-auth username (password is the literal "x").</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain; substituted into <c>https://{site}.chargify.com</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The product-family handle whose products are the subscribable plans.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim, overriding the
    /// subdomain-derived <c>https://{Subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
