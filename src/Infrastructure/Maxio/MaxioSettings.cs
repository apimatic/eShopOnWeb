using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the <c>Maxio:</c> section.
/// Values are supplied via configuration (user-secrets / environment), never hard-coded in the repo.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Chargify API key (used as the Basic-auth username; password is the literal "x").</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain — fills <c>{site}</c> in <c>https://{site}.chargify.com</c>.</summary>
    [Required]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose plans are offered for subscription.</summary>
    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim as the base address instead of
    /// deriving one from <see cref="Subdomain"/> (e.g. a mock, proxy, EU host, or self-hosted gateway).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Payment collection method for new subscriptions (Maxio <c>payment_collection_method</c> wire value).
    /// Defaults to <c>remittance</c> so a subscription can be created without a card on file (invoice-based
    /// billing) — the plans in scope require no payment method. Configurable per site (a legacy Statements
    /// site uses <c>invoice</c>; set <c>automatic</c> only where a card is captured separately).
    /// </summary>
    public string CollectionMethod { get; set; } = "remittance";

    /// <summary>
    /// Fails fast on a missing or blank required credential — every part is checked separately, because a
    /// blank part is not a missing one. The message names the config key and never echoes a value.
    /// </summary>
    public void Validate()
    {
        RequireNonBlank(ApiKey, $"{SectionName}:{nameof(ApiKey)}");
        RequireNonBlank(Subdomain, $"{SectionName}:{nameof(Subdomain)}");
        RequireNonBlank(ProductFamilyHandle, $"{SectionName}:{nameof(ProductFamilyHandle)}");
    }

    private static void RequireNonBlank(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it via environment variable, user-secrets, or your secret " +
                "store before starting the app.");
        }
    }
}
