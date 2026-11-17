using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Finix;

/// <summary>
/// A caller-facing failure from the payment integration. The exception middleware maps this to
/// the status code and message below; it never carries a raw SDK exception or a request body.
/// </summary>
public abstract class PaymentException : Exception
{
    protected PaymentException(string message, int statusCode, bool outcomeUnknown = false, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        OutcomeUnknown = outcomeUnknown;
    }

    public int StatusCode { get; }
    public bool OutcomeUnknown { get; }
}

public sealed class PaymentValidationException : PaymentException
{
    public PaymentValidationException(string message) : base(message, (int)HttpStatusCode.BadRequest) { }
}

public sealed class PaymentNotFoundException : PaymentException
{
    public PaymentNotFoundException(string message) : base(message, (int)HttpStatusCode.NotFound) { }
}

public sealed class PaymentConflictException : PaymentException
{
    public PaymentConflictException(string message) : base(message, (int)HttpStatusCode.Conflict) { }
}

public sealed class PaymentProviderException : PaymentException
{
    public PaymentProviderException(string message, HttpStatusCode? providerStatus = null, bool outcomeUnknown = false,
        Exception? inner = null)
        : base(message, MapStatus(providerStatus, outcomeUnknown), outcomeUnknown, inner)
    {
        ProviderStatus = providerStatus;
    }

    public HttpStatusCode? ProviderStatus { get; }

    private static int MapStatus(HttpStatusCode? providerStatus, bool outcomeUnknown)
    {
        if (outcomeUnknown || providerStatus is null)
        {
            return (int)HttpStatusCode.BadGateway;
        }

        var status = (int)providerStatus.Value;
        return status switch
        {
            400 or 402 or 404 or 409 or 422 => status,
            401 or 403 or 429 => (int)HttpStatusCode.BadGateway,
            >= 500 => (int)HttpStatusCode.BadGateway,
            _ => (int)HttpStatusCode.BadGateway
        };
    }
}
