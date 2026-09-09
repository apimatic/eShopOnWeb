using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success response.
/// Carries the upstream status code and parsed error list from the spec's error
/// models (<c>errors</c> as a list, a string map, or a single string).
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string responseBody, IEnumerable<string> errors)
        : this(statusCode, responseBody, errors.ToArray())
    {
    }

    private MaxioApiException(int statusCode, string responseBody, IReadOnlyList<string> errors)
        : base(BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Errors = errors;
    }

    public int StatusCode { get; }
    public string ResponseBody { get; }
    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(int statusCode, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error details returned";
        return $"Maxio API request failed with status {statusCode}: {detail}";
    }
}
