using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The order/payment is in a state that does not allow the requested operation
/// (e.g. capturing an uncapturable authorization, cancelling a fulfilled order).
/// Maps to HTTP 409 at the API boundary.
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }
}
