using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success status. Carries the
/// decoded <c>errors</c> payload (when present) so callers can surface a meaningful message.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }
    public string? Body { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string? body, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
        Body = body;
    }

    public MaxioApiException(int statusCode, string? body, string message)
        : this(statusCode, Array.Empty<string>(), body, message)
    {
    }
}
