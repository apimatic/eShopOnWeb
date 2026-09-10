using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment operation would violate a business rule (e.g. refunding more than was
/// captured, or acting on a payment in the wrong state). Surfaced to the caller as a 422.
/// </summary>
public class PaymentDomainException : Exception
{
    public PaymentDomainException(string message) : base(message)
    {
    }
}
