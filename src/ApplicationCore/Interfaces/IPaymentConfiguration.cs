namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Payment settings the domain needs. Implemented in the composition root from <c>PayPal:</c> config.</summary>
public interface IPaymentConfiguration
{
    /// <summary>The ISO-4217 currency all order amounts are charged in (from <c>PayPal:Currency</c>).</summary>
    string Currency { get; }
}
