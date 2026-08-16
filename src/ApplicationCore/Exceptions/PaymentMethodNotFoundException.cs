using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested saved card does not exist, or does not belong to the caller — deliberately
/// indistinguishable. Maps to 404 at the API boundary.
/// </summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"Saved card {paymentMethodId} was not found.")
    {
    }
}
