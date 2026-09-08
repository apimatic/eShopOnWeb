using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string? message = null)
        : base(message ?? $"Maxio API call failed with status {statusCode}: {(errors.Count > 0 ? string.Join("; ", errors) : "no error details returned")}.")
    {
        StatusCode = statusCode;
        Errors = errors;
    }
}
