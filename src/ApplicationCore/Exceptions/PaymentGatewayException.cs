using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the payment provider rejects or fails a call. Carries the provider's
/// error name and debug id so operators can act on it; never carries card details.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(int httpStatusCode, string? errorName, string message, string? debugId = null)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        ErrorName = errorName;
        DebugId = debugId;
    }

    public int HttpStatusCode { get; }
    public string? ErrorName { get; }
    public string? DebugId { get; }

    /// <summary>True when the provider answered 4xx — the request itself was rejected.</summary>
    public bool IsClientError => HttpStatusCode >= 400 && HttpStatusCode < 500;

    /// <summary>True when the provider says the request is syntactically fine but cannot be processed (HTTP 422).</summary>
    public bool IsUnprocessable => HttpStatusCode == 422;
}
