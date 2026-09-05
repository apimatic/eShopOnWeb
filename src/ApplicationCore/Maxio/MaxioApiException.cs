using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Thrown when the Maxio API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors)
        : base(errors.Count > 0 ? string.Join("; ", errors) : $"Maxio API request failed with status {statusCode}")
    {
        StatusCode = statusCode;
        Errors = errors;
    }
}
