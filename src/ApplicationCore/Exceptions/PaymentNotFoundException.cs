using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested order/payment does not exist, or does not belong to the caller. Surfaces as HTTP 404
/// so one shopper can never even learn of another's order.
/// </summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message)
    {
    }
}
