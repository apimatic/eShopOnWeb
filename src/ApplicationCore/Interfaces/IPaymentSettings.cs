namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Payment settings the application layer needs, without depending on the Infrastructure configuration type.</summary>
public interface IPaymentSettings
{
    /// <summary>The three-letter ISO-4217 currency used for all payments, from configuration.</summary>
    string Currency { get; }
}
