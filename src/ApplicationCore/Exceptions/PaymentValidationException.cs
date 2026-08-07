using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment-related request is invalid (e.g. unknown catalog item, unknown saved card,
/// or an illegal payment-state transition). Maps to a 400-class HTTP response.
/// </summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message)
    {
    }
}
