namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The payment configuration the domain services need. Currency is bound from <c>PayPal:Currency</c>;
/// it is never hard-coded so the same build can run against a differently-configured account.
/// </summary>
public interface IPaymentSettings
{
    string Currency { get; }
}
