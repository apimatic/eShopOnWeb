using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a call to the Maxio Advanced Billing API fails.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when Maxio rejected the request itself (4xx, e.g. an unknown plan handle),
    /// meaning the caller supplied something invalid. False for transport failures or
    /// 5xx responses from Maxio, which are treated as upstream outages.
    /// </summary>
    public bool IsClientError => StatusCode is >= 400 and < 500;

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors)
        : base($"Maxio API request failed with status {statusCode}: {string.Join("; ", errors)}")
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public MaxioApiException(string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = 0;
        Errors = new[] { message };
    }
}
