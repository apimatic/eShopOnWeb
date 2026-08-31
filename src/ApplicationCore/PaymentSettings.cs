namespace Microsoft.eShopWeb;

/// <summary>
/// Payment-related settings bound from the "PayPal" configuration section.
/// Only the currency is needed inside the domain services; the credentials
/// are consumed by the Infrastructure gateway wiring.
/// </summary>
public class PaymentSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string Currency { get; set; } = "USD";
}
