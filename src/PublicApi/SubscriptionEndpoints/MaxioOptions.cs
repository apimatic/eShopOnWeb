using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Settings for the Maxio Advanced Billing site used for subscriptions.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Subdomain { get; init; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>
    /// Optional full Maxio API base address. When absent, the US Advanced Billing
    /// address is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; init; }

    public Uri GetBaseUri()
    {
        var value = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new OptionsValidationException(SectionName, typeof(MaxioOptions),
                new[] { "Maxio:BaseUrl must be an absolute HTTPS URL when supplied." });
        }

        return new Uri(uri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
    }
}
