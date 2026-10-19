using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// A billing failure with the HTTP status the caller should see. Messages are caller-safe:
/// never an SDK exception message, URL or request body.
/// </summary>
public class BillingException : Exception
{
    public BillingException(HttpStatusCode statusCode, string message, IReadOnlyList<string>? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Details = details ?? Array.Empty<string>();
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Validation messages returned by Maxio (e.g. on a 422), safe to show to the caller.</summary>
    public IReadOnlyList<string> Details { get; }
}

/// <summary>Maxio answered with a definitive refusal (4xx). <see cref="ProviderStatus"/> is Maxio's status.</summary>
public class MaxioRejectedException : BillingException
{
    public MaxioRejectedException(HttpStatusCode providerStatus, string message, IReadOnlyList<string>? details, Exception innerException)
        : base(MapStatus(providerStatus), message, details, innerException)
    {
        ProviderStatus = providerStatus;
    }

    public HttpStatusCode ProviderStatus { get; }

    // 401/403 mean OUR credentials are wrong, 404 means OUR configuration points at something missing,
    // 429 means OUR quota is spent: none is the caller's fault. Validation (422) and other 4xx are passed through.
    private static HttpStatusCode MapStatus(HttpStatusCode providerStatus) => (int)providerStatus switch
    {
        401 or 403 or 404 => HttpStatusCode.BadGateway,
        429 => HttpStatusCode.ServiceUnavailable,
        _ => providerStatus
    };
}

/// <summary>Maxio could not be reached, failed (5xx) or answered with an unreadable body.</summary>
public class MaxioUnavailableException : BillingException
{
    public MaxioUnavailableException(string message, Exception innerException)
        : base(HttpStatusCode.BadGateway, message, null, innerException)
    {
    }
}

/// <summary>
/// A write may or may not have been applied by Maxio (connection dropped, timeout, 5xx, unreadable 2xx).
/// Thrown only by gateway write methods; the caller must settle it by re-reading Maxio.
/// </summary>
public class MaxioOutcomeUnknownException : BillingException
{
    public MaxioOutcomeUnknownException(string message, Exception innerException)
        : base(HttpStatusCode.ServiceUnavailable, message, null, innerException)
    {
    }
}
