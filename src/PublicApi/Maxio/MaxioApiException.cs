using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Represents a failed call to the Maxio Advanced Billing API.
/// Carries the HTTP status code and the error messages from the spec's Error-List-Response model.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(HttpStatusCode statusCode, IEnumerable<string> errors)
        : base($"Maxio API request failed with status {(int)statusCode} ({statusCode}): {string.Join("; ", errors)}")
    {
        StatusCode = statusCode;
        Errors = new List<string>(errors);
    }
}
