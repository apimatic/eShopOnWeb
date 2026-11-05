using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Bound from the "Maxio:" configuration section. No value is ever defaulted in code.</summary>
public class MaxioSettings : IValidatableObject
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (user-secrets / environment; never in appsettings).</summary>
    public string? ApiKey { get; set; }
    /// <summary>Maxio site subdomain.</summary>
    public string? Subdomain { get; set; }
    /// <summary>Handle of the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }
    /// <summary>Optional. When set, used verbatim as the API base address instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    public System.Collections.Generic.IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            yield return new ValidationResult($"{SectionName}:ApiKey is not configured.", new[] { nameof(ApiKey) });
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            yield return new ValidationResult($"{SectionName}:ProductFamilyHandle is not configured.", new[] { nameof(ProductFamilyHandle) });
        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
            yield return new ValidationResult($"{SectionName}:Subdomain is not configured (required unless {SectionName}:BaseUrl is set).", new[] { nameof(Subdomain) });
        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
            yield return new ValidationResult($"{SectionName}:BaseUrl is not an absolute URL.", new[] { nameof(BaseUrl) });
    }
}
