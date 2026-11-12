using System.Collections.Generic;
using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Nothing here has a default: the same build runs against any
/// Maxio site and catalog purely through configuration (user-secrets in development, environment/secret store elsewhere).
/// </summary>
public class MaxioSettings : IValidatableObject
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (the Basic-auth username).</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ApiKey is not configured.")]
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain; fills the <c>{site}</c> placeholder of the SDK's default host.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:Subdomain is not configured.")]
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are the offered subscription plans.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ProductFamilyHandle is not configured.")]
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl)
            && !(Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
                 && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
        {
            yield return new ValidationResult("Maxio:BaseUrl must be an absolute http(s) URL when set.", [nameof(BaseUrl)]);
        }
    }
}
