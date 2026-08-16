using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised for an invalid step in the payment flow (unknown order, wrong state, refund exceeding capture,
/// a saved card that isn't the caller's). Carries the HTTP status the API should return.
/// </summary>
public class PaymentFlowException : Exception, IApiException
{
    public int StatusCode { get; }
    public string? DebugId => null;

    public PaymentFlowException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }
}
