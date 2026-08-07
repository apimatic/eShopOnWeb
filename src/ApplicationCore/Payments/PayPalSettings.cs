namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// PayPal configuration, bound from the <c>PayPal:</c> configuration section.
/// Secret values are supplied via .NET user-secrets / environment, never the repo.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Always "sandbox" for this integration.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim; otherwise
    /// the base address is derived from <see cref="Environment"/> (sandbox).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The effective API base address to call.</summary>
    public string ResolvedBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? SandboxBaseUrl : BaseUrl!.TrimEnd('/');
}
