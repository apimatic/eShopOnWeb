using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when the Maxio API returns a non-success HTTP status. Carries the status code and raw response
/// body so callers can translate specific conditions (e.g. 422 validation, 409 duplicate) into an
/// appropriate API response for the eShopOnWeb client.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    /// <summary>The raw Maxio response body, if any. Useful for surfacing validation errors.</summary>
    public string? ResponseBody { get; }

    public MaxioApiException(HttpStatusCode statusCode, string? responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>True when Maxio rejected the request as a duplicate (uniqueness_token collision).</summary>
    public bool IsDuplicate => StatusCode == HttpStatusCode.Conflict;
}
