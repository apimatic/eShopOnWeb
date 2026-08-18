using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure while talking to PayPal (a provider error response, a transport failure, or an unreadable body).
/// <see cref="StatusCode"/> carries PayPal's HTTP status when there was one, so the boundary can map a provider
/// 4xx back to a client 4xx and treat transport/unknown failures as 5xx. The message is always caller-safe —
/// raw provider/JSON detail is never propagated through it.
/// </summary>
public class PaymentGatewayException : Exception
{
    /// <summary>PayPal's HTTP status code, when the failure was an HTTP error response; otherwise null.</summary>
    public int? StatusCode { get; }

    public PaymentGatewayException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
