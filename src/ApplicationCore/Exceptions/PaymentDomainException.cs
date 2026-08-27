using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A deterministic, caller-actionable rejection from the order/payment domain
/// (wrong state, over-refund, ownership violation, ...). Carries the HTTP status
/// the API should answer with.
/// </summary>
public class PaymentDomainException : Exception
{
    public PaymentDomainException(string message, int statusCode = 409) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
