using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a Maxio API call fails or returns a response the integration cannot honor. Carries the
/// upstream HTTP status and any validation messages Maxio returned so they can be surfaced to callers.
/// </summary>
public class MaxioException : Exception
{
    public MaxioException(string message, int? statusCode = null, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>The HTTP status code Maxio returned, when the failure originated from an API response.</summary>
    public int? StatusCode { get; }

    /// <summary>Validation messages extracted from Maxio's error body, if any.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when the failure is a client/validation error (4xx) rather than an upstream or transport
    /// fault, so the API can map it to a 4xx rather than a 502.
    /// </summary>
    public bool IsClientError => StatusCode is >= 400 and < 500;
}
