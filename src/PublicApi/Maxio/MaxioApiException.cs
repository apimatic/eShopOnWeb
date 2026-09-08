using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when the Billing API responds with a non-success status code.
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int? statusCode, IReadOnlyList<string> errors, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public int? StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }
}
