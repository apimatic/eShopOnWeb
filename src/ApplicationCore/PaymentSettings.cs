namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Payment-related settings bound from the "PayPal" configuration section.
/// Values are supplied via environment variables / user-secrets, never hard-coded.
/// </summary>
public class PaymentSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string Currency { get; set; } = string.Empty;
}
