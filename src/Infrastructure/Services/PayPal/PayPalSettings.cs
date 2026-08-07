namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal" section. Values come from
/// configuration/user-secrets/environment — never hard-coded, so the same build runs against a
/// different PayPal app just by changing the settings.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Always "sandbox" for this integration.</summary>
    public string? Environment { get; set; }

    /// <summary>Optional explicit API base address. When set, it is used verbatim instead of the
    /// environment-derived host.</summary>
    public string? BaseUrl { get; set; }
}
