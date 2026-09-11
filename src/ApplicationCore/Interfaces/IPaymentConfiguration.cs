namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Supplies payment configuration the application core needs (kept as an interface so the core
/// does not depend on the options/binding machinery). Currency comes from PayPal:Currency.
/// </summary>
public interface IPaymentConfiguration
{
    string CurrencyCode { get; }
}
