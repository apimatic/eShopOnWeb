using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a shopper-scoped payment resource (order, payment, or saved card) does not exist or does not
/// belong to the caller. The same exception is used for "not found" and "not yours" so the existence of another
/// shopper's data is never revealed. Maps to HTTP 404 at the API boundary.
/// </summary>
public class PaymentResourceNotFoundException : Exception
{
    public PaymentResourceNotFoundException(string message) : base(message)
    {
    }
}
