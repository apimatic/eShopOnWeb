using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a call to the Maxio Billing API fails. <see cref="StatusCode"/> carries the
/// HTTP status the PublicApi should surface to its own caller.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }
}
