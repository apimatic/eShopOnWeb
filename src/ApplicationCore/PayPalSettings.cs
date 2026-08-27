namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the "PayPal" configuration section. Values are supplied via
/// user-secrets / environment, never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Environment { get; set; }
    public string? Currency { get; set; }

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address
    /// for every PayPal call, including the token request.
    /// </summary>
    public string? BaseUrl { get; set; }
}
