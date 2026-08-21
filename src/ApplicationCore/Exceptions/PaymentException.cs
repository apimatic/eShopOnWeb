using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An application-level payment error the caller can act on: an order that isn't there (or isn't
/// theirs) → 404, an operation invalid for the current state → 409, a validation problem such as an
/// over-refund → 422. Distinct from <see cref="PaymentGatewayException"/>, which comes from the
/// provider boundary.
/// </summary>
public class PaymentException : Exception, IApiStatusCodeException
{
    public PaymentException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
    public string? Issue => null;
}
