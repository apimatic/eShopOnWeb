namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Binds the <c>PayPal:</c> configuration section. Values are supplied at runtime from
/// configuration (env vars / user-secrets) and are never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Target PayPal environment. Only <c>sandbox</c> is supported by this integration.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim, overriding the
    /// address derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
