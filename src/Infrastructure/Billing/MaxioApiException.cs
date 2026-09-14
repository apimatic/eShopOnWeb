using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Represents a non-successful response from the Maxio Advanced Billing API.
/// </summary>
internal sealed class MaxioApiException : Exception
{
    public MaxioApiException(int? statusCode, IReadOnlyList<string> errors, string? rawBody = null)
        : base(errors is { Count: > 0 } ? string.Join(" ", errors) : "The billing provider returned an unexpected response.")
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
        RawBody = rawBody;
    }

    public int? StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public string? RawBody { get; }
}
