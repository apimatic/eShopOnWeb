using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A shopper-scoped resource (an order or a saved card) was not found for the caller. Thrown identically
/// whether the resource does not exist or belongs to another shopper, so ownership is never leaked. Surfaces
/// as HTTP 404.
/// </summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message)
    {
    }
}
