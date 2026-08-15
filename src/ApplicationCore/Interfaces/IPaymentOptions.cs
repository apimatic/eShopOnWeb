namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Payment settings the application layer needs. Bound from the <c>PayPal:</c> configuration section.</summary>
public interface IPaymentOptions
{
    /// <summary>ISO-4217 currency code that order amounts are charged in (e.g. <c>USD</c>).</summary>
    string Currency { get; }
}
