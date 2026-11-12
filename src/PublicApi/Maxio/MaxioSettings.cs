using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Bound from the "Maxio" configuration section.</summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    /// <summary>Optional override; used verbatim as the API base address when set.</summary>
    public string? BaseUrl { get; set; }
    /// <summary>Optional: plan handle used when a subscribe request does not name one.</summary>
    public string? DefaultPlanHandle { get; set; }
    /// <summary>Optional: automatic, remittance (default) or prepaid. Remittance bills by invoice, so no card is needed at signup.</summary>
    public string? PaymentCollectionMethod { get; set; }
}

public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings s)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(s.ApiKey))
            errors.Add("Maxio:ApiKey is not configured.");
        if (string.IsNullOrWhiteSpace(s.ProductFamilyHandle))
            errors.Add("Maxio:ProductFamilyHandle is not configured.");
        if (!string.IsNullOrWhiteSpace(s.PaymentCollectionMethod) &&
            !new[] { "automatic", "remittance", "prepaid" }.Contains(s.PaymentCollectionMethod.Trim().ToLowerInvariant()))
            errors.Add("Maxio:PaymentCollectionMethod must be automatic, remittance or prepaid.");
        if (!string.IsNullOrWhiteSpace(s.BaseUrl))
        {
            if (!Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                errors.Add("Maxio:BaseUrl must be an absolute http(s) URL.");
        }
        else if (string.IsNullOrWhiteSpace(s.Subdomain))
        {
            errors.Add("Maxio:Subdomain is not configured (required when Maxio:BaseUrl is not set).");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
