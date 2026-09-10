using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested order/payment does not exist, or does not belong to the caller. Both collapse to the
/// same "not found" so ownership of another shopper's order is never revealed. Maps to 404.
/// </summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}
