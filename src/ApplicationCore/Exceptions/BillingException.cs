using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A billing failure with a caller-safe message and the HTTP status the API should answer with.
/// The message never carries provider exception text, URLs or secrets.
/// </summary>
public class BillingException : Exception
{
    public BillingException(int httpStatusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatusCode = httpStatusCode;
    }

    /// <summary>The HTTP status code to return to the API caller.</summary>
    public int HttpStatusCode { get; }

    /// <summary>The HTTP status the billing provider answered with, when it answered at all.</summary>
    public int? ProviderStatusCode { get; init; }
}
