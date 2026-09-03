using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for failures raised by the subscription-billing integration. Carries the HTTP status
/// the API boundary should surface to the caller, so a provider client-error (4xx) is not collapsed
/// into a generic 5xx and a transport/unknown failure is not reported as a caller mistake.
/// The message is always caller-safe — provider/framework exception detail is never propagated.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
