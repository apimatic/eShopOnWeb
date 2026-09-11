namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>The payment settings the domain needs: the currency all amounts are charged in.
/// Bound from configuration (<c>PayPal:Currency</c>) by the host.</summary>
public interface IPaymentSettings
{
    string CurrencyCode { get; }
}
