using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section. Values are supplied through user-secrets or environment variables; none of them are
/// checked into the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Server templates taken from the OpenAPI specification (<c>servers</c> and
    /// <c>info.x-server-configuration</c>): the site subdomain is substituted for <c>{site}</c>.
    /// </summary>
    private const string UsServerTemplate = "https://{site}.chargify.com";
    private const string EuServerTemplate = "https://{site}.ebilling.maxio.com";

    /// <summary>
    /// Maxio API key. Sent as the basic-auth username, with the password <c>x</c>, per the
    /// <c>BasicAuth</c> security scheme in the specification.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The subdomain of the Maxio site, substituted into the server template.</summary>
    public string? Subdomain { get; set; }

    /// <summary>The product family whose products are published as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override of the API base address. When set it is used verbatim, instead of being
    /// derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional Maxio hosting environment, <c>US</c> (default) or <c>EU</c>, matching the
    /// environments declared in the specification. Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>True when enough is configured to talk to Maxio.</summary>
    public bool IsConfigured => !Validate().Any();

    /// <summary>Returns a message per configuration problem; empty when the settings are usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            errors.Add($"'{SectionName}:{nameof(ApiKey)}' is not set.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            errors.Add($"Either '{SectionName}:{nameof(Subdomain)}' or '{SectionName}:{nameof(BaseUrl)}' must be set.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri))
            {
                errors.Add($"'{SectionName}:{nameof(BaseUrl)}' is not an absolute URL.");
            }
            else if (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)
            {
                errors.Add($"'{SectionName}:{nameof(BaseUrl)}' must use http or https.");
            }
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            errors.Add($"'{SectionName}:{nameof(ProductFamilyHandle)}' is not set.");
        }

        return errors;
    }

    /// <summary>
    /// The API base address, without a trailing slash. <see cref="BaseUrl"/> wins when supplied;
    /// otherwise the specification's server template is filled in with the site subdomain.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        var template = string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase)
            ? EuServerTemplate
            : UsServerTemplate;

        return template.Replace("{site}", Subdomain!.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The configured product family formatted for the <c>product_family_id</c> path parameter,
    /// which accepts "either the product family's id or its handle prefixed with <c>handle:</c>".
    /// </summary>
    public string ResolveProductFamilyPathValue()
    {
        var value = ProductFamilyHandle!.Trim();

        if (value.StartsWith("handle:", StringComparison.OrdinalIgnoreCase) || value.All(char.IsDigit))
        {
            return value;
        }

        return $"handle:{value}";
    }
}
