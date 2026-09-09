namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration. Bound from the
/// "Maxio" configuration section; values are supplied via user secrets or
/// environment variables and are never stored in the repository.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The site API key, used as the Basic authentication username ("x" is the password).
    /// </summary>
    public string ApiKey { get; set; } = default!;

    /// <summary>
    /// The Maxio site subdomain, used to derive the API base address
    /// (https://&lt;subdomain&gt;.chargify.com) when <see cref="BaseUrl"/> is not set.
    /// </summary>
    public string Subdomain { get; set; } = default!;

    /// <summary>
    /// Handle of the product family that contains the subscribable plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = default!;

    /// <summary>
    /// Optional. When set, used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional. The payment collection method used when creating subscriptions
    /// ("automatic", "remittance", "prepaid", or "invoice"). Defaults to
    /// "remittance" so that shoppers can subscribe without capturing a payment
    /// method — sites with automatic collection attempt an immediate charge at
    /// signup, which requires a payment profile on file.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
