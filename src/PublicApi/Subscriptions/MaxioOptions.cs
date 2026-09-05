using System.ComponentModel.DataAnnotations;
using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Subdomain { get; init; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; init; } = string.Empty;

    public string? BaseUrl { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(Subdomain) || string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio configuration requires ApiKey, Subdomain, and ProductFamilyHandle.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Maxio BaseUrl must be an absolute URI when provided.");
        }
    }
}
