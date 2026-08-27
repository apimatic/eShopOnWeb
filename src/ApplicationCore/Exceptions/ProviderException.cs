using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An error returned by the messaging provider's API. The raw provider message may
/// contain PII (e.g. a destination number); use <see cref="ErrorCode"/> for logging.
/// </summary>
public class ProviderException : Exception
{
    public ProviderException(string message, int? errorCode = null, int? httpStatusCode = null)
        : base(message)
    {
        ErrorCode = errorCode;
        HttpStatusCode = httpStatusCode;
    }

    public int? ErrorCode { get; }
    public int? HttpStatusCode { get; }
}
