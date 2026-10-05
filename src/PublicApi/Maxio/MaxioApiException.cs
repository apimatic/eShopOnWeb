using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns a non-success response or
/// the integration is misconfigured.
/// </summary>
public class MaxioApiException : Exception
{
    public int? StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(string message, int? statusCode = null, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }
}