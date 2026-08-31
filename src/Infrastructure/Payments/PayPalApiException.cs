using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// An error response returned by the PayPal API, per the error model in the OpenAPI specs
/// (name / message / debug_id / details).
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string? errorName, string message, string? debugId,
        IEnumerable<string>? issues = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        DebugId = debugId;
        Issues = issues;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>PayPal's machine-readable error name, e.g. UNPROCESSABLE_ENTITY.</summary>
    public string? ErrorName { get; }

    /// <summary>PayPal's correlation id; quote it when contacting PayPal support.</summary>
    public string? DebugId { get; }

    /// <summary>The issue descriptions from the error's details array.</summary>
    public IEnumerable<string>? Issues { get; }
}
