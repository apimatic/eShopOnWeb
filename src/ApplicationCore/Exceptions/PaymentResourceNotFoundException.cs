using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The order or saved card does not exist <em>for this caller</em> (HTTP 404). Another shopper's
/// resource is reported exactly like a missing one so its existence is not disclosed.
/// </summary>
public class PaymentResourceNotFoundException : Exception
{
    public PaymentResourceNotFoundException(string message) : base(message)
    {
    }
}
