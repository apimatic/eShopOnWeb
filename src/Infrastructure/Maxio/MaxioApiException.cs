using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio Billing API, with the status and error messages it returned.
/// </summary>
public class MaxioApiException : BillingGatewayException
{
    public MaxioApiException(HttpStatusCode statusCode, IEnumerable<string> errors, string requestDescription)
        : base(BuildMessage(statusCode, errors, requestDescription), (int)statusCode, errors)
    {
        StatusCode = statusCode;
        RequestDescription = requestDescription;
    }

    public HttpStatusCode StatusCode { get; }

    public string RequestDescription { get; }

    /// <summary>
    /// True when Maxio's duplicate prevention rejected this request because an identical one
    /// (same uniqueness token) was already received. The original request may or may not have
    /// succeeded, so the caller must re-read to find out.
    /// </summary>
    public bool IsDuplicateSubmission => StatusCode == HttpStatusCode.Conflict;

    /// <summary>
    /// True when the request was rejected because the customer reference is already taken,
    /// which means the customer we were about to create already exists.
    /// </summary>
    public bool IndicatesReferenceTaken =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e => e.Contains("reference", System.StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(HttpStatusCode statusCode, IEnumerable<string> errors, string requestDescription)
    {
        var detail = string.Join("; ", errors);
        return string.IsNullOrWhiteSpace(detail)
            ? $"Maxio request '{requestDescription}' failed with HTTP {(int)statusCode} {statusCode}."
            : $"Maxio request '{requestDescription}' failed with HTTP {(int)statusCode} {statusCode}: {detail}";
    }
}
