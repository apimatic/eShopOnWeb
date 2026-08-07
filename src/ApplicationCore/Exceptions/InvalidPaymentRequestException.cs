using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>A payment/order request was malformed (e.g. no items, unknown catalog item, or an
/// instruction that names neither a card nor a saved card). Maps to HTTP 400.</summary>
public class InvalidPaymentRequestException : Exception
{
    public InvalidPaymentRequestException(string message) : base(message)
    {
    }
}
