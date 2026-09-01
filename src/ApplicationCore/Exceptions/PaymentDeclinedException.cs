using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The processor declined the payment (e.g. card declined). Safe to show to the shopper.
/// </summary>
public class PaymentDeclinedException : Exception
{
    public PaymentDeclinedException(string message) : base(message)
    {
    }
}
