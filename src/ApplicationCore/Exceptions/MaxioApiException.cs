using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a call to the Maxio Advanced Billing API fails in a way the caller cannot correct
/// (transport error, unexpected status, or an upstream 5xx). Represents an upstream/gateway failure.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(string message, int? statusCode = null, IReadOnlyList<string>? errors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>The HTTP status code returned by Maxio, when the failure was an HTTP response.</summary>
    public int? StatusCode { get; }

    /// <summary>Any structured error messages parsed from the Maxio error response body.</summary>
    public IReadOnlyList<string> Errors { get; }
}
