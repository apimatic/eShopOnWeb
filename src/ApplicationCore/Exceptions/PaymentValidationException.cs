using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The request was well-formed but invalid for a payment action (e.g. an unknown catalog item, no
/// card supplied, or a reference to a saved card that isn't the caller's). Surfaces as HTTP 400.
/// </summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message)
    {
    }
}
