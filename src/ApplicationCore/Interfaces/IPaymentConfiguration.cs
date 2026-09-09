namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Payment settings the application core needs, kept as an abstraction so the core
/// does not depend on the Infrastructure settings type. Values come from the
/// <c>PayPal:</c> configuration section.
/// </summary>
public interface IPaymentConfiguration
{
    /// <summary>The three-letter ISO-4217 currency for all payments (e.g. USD).</summary>
    string Currency { get; }
}
