using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation is invalid for an order's current payment state — e.g. refunding
/// an order that was never paid, or paying an order that has already been refunded. Surfaced as HTTP
/// 409 Conflict by the API boundary.
/// </summary>
public class PaymentStateConflictException : Exception
{
    public PaymentStateConflictException(string message) : base(message)
    {
    }
}
