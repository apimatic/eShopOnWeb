namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Payment configuration the application layer needs (kept out of Infrastructure settings types).</summary>
public interface IPaymentConfiguration
{
    /// <summary>The 3-letter ISO-4217 currency code payments are taken in.</summary>
    string CurrencyCode { get; }
}
