using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The caller's request was rejected before it reached the payment processor — an empty basket,
/// an unknown catalog item, a refund larger than what was captured. Surfaces as HTTP 400.
/// </summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message)
    {
    }
}
