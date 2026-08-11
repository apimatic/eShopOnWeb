namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Payment settings the core needs. Bound from configuration in Infrastructure.</summary>
public interface IPaymentSettings
{
    /// <summary>ISO-4217 currency all payments are denominated in (from <c>PayPal:Currency</c>).</summary>
    string Currency { get; }
}
