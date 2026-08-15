namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Payment settings the application layer needs, kept behind an interface so ApplicationCore does
/// not depend on the concrete PayPal configuration type in Infrastructure.
/// </summary>
public interface IPaymentConfiguration
{
    /// <summary>ISO-4217 currency code used for all order amounts.</summary>
    string Currency { get; }
}
