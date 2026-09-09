using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio:</c> section.
/// The credential (<see cref="ApiKey"/>) and the two identifiers required to address the right
/// site and catalog are all <see cref="RequiredAttribute"/>; combined with
/// <c>ValidateDataAnnotations().ValidateOnStart()</c> the host refuses to boot when any is missing
/// or blank, rather than discovering it as a 401/404 on the first call in production.
/// Values are never hard-coded — they come from configuration (user-secrets in this app).
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Site API key. Sent as the HTTP Basic username (password is the fixed literal "x").</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain; substituted into the default base URL <c>https://{site}.chargify.com</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are exposed as subscription plans.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/> — for an EU/self-hosted/gateway deployment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
